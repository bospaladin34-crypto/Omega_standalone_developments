using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmegaPenroseStudio
{
    public sealed class OmegaPenroseAudioPhasonPipe : IDisposable
    {
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
        [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetEpDel(IntPtr e, int dataFlow, int role, out IntPtr ppDev);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ActivateDel(IntPtr dev, ref Guid iid, uint clsCtx, IntPtr pProp, out IntPtr ppIf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MixFmtDel(IntPtr cli, out IntPtr ppFmt);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitAudDel(IntPtr cli, int share, uint flags, long dur, long per, IntPtr pFmt, IntPtr sess);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSvcDel(IntPtr cli, ref Guid iid, out IntPtr ppSvc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StartStopDel(IntPtr cli);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NextPktDel(IntPtr cap, out uint pFrames);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBufDel(IntPtr cap, out IntPtr ppData, out uint pFrames, out uint pFlags, out ulong pDevPos, out ulong pQpcPos);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RelBufDel(IntPtr cap, uint frames);

        private static T Vtbl<T>(IntPtr comObj, int slot) where T : class
        {
            IntPtr vtbl = Marshal.ReadIntPtr(comObj);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
        }

        private static void SafeRel(IntPtr p)
        {
            if (p != IntPtr.Zero) { try { Marshal.Release(p); } catch { } }
        }

        private volatile bool _running;
        private volatile bool _enabled;
        private Thread _wasapiThread;
        private Thread _synthPollThread;

        public volatile float SubBassLevel;   // 20..140 Hz (0..1)
        public volatile float MidHarmLevel;   // 140..1200 Hz (0..1)
        public volatile float HighAirLevel;   // 1.2..8 kHz (0..1)
        public volatile float BeatPulse;      // Decaying transient kick (0..1)
        public volatile bool SynthLinkLive;
        public volatile int SynthChladniM = 3;
        public volatile int SynthChladniN = 5;
        public volatile int SynthBpm = 112;
        public volatile int SynthVoices = 0;

        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _wasapiThread = new Thread(WasapiLoopbackWorker);
            _wasapiThread.IsBackground = true;
            _wasapiThread.Start();

            _synthPollThread = new Thread(OmegaSynthTelemetryWorker);
            _synthPollThread.IsBackground = true;
            _synthPollThread.Start();
        }

        private void OmegaSynthTelemetryWorker()
        {
            while (_running)
            {
                try
                {
                    if (!_enabled) { Thread.Sleep(350); continue; }
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:19782/api/status");
                    req.Method = "GET";
                    req.Timeout = 280;
                    req.ReadWriteTimeout = 280;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string json = sr.ReadToEnd();
                        SynthLinkLive = true;
                        SynthChladniM = ExtractInt(json, "m", SynthChladniM);
                        SynthChladniN = ExtractInt(json, "n", SynthChladniN);
                        SynthBpm = ExtractInt(json, "bpm", SynthBpm);
                        SynthVoices = ExtractInt(json, "voices", 0);
                    }
                }
                catch
                {
                    SynthLinkLive = false;
                }
                Thread.Sleep(320);
            }
        }

        private static int ExtractInt(string json, string key, int defVal)
        {
            Match m = Regex.Match(json ?? "", "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            int v;
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return v;
            return defVal;
        }

        private void WasapiLoopbackWorker()
        {
            CoInitializeEx(IntPtr.Zero, 0u);
            Guid clsidEnum = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            Guid iidEnum   = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCap    = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

            float lpLow = 0.0f, lpMid = 0.0f;
            float envSub = 0.0f, envMid = 0.0f, envHi = 0.0f, prevSub = 0.0f;

            while (_running)
            {
                if (!_enabled)
                {
                    SubBassLevel *= 0.85f; MidHarmLevel *= 0.85f; HighAirLevel *= 0.85f; BeatPulse *= 0.85f;
                    Thread.Sleep(80);
                    continue;
                }

                IntPtr pEnum = IntPtr.Zero, pDev = IntPtr.Zero, pClient = IntPtr.Zero, pCap = IntPtr.Zero, pFmt = IntPtr.Zero;
                StartStopDel fnStop = null;
                try
                {
                    if (CoCreateInstance(ref clsidEnum, IntPtr.Zero, 23u, ref iidEnum, out pEnum) < 0 || pEnum == IntPtr.Zero) { Thread.Sleep(600); continue; }
                    GetEpDel fnEp = Vtbl<GetEpDel>(pEnum, 4);
                    if (fnEp(pEnum, 0, 1, out pDev) < 0 || pDev == IntPtr.Zero) fnEp(pEnum, 0, 0, out pDev);
                    if (pDev == IntPtr.Zero) { Thread.Sleep(600); continue; }
                    if (Vtbl<ActivateDel>(pDev, 3)(pDev, ref iidClient, 23u, IntPtr.Zero, out pClient) < 0 || pClient == IntPtr.Zero) { Thread.Sleep(600); continue; }
                    if (Vtbl<MixFmtDel>(pClient, 8)(pClient, out pFmt) < 0 || pFmt == IntPtr.Zero) { Thread.Sleep(600); continue; }

                    ushort tag = (ushort)Marshal.ReadInt16(pFmt, 0);
                    int ch = Math.Max(1, (int)(ushort)Marshal.ReadInt16(pFmt, 2));
                    int rate = Math.Max(8000, Marshal.ReadInt32(pFmt, 4));
                    int align = Math.Max(2, (int)(ushort)Marshal.ReadInt16(pFmt, 12));
                    int bits = (ushort)Marshal.ReadInt16(pFmt, 14);
                    int cbSz = (ushort)Marshal.ReadInt16(pFmt, 16);
                    bool isF32 = (tag == 3) || (tag == 0xFFFE && cbSz >= 22 && Marshal.ReadInt32(pFmt, 24) == 3);

                    if (Vtbl<InitAudDel>(pClient, 3)(pClient, 0, 0x00020000u, 400000L, 0L, pFmt, IntPtr.Zero) < 0) { Thread.Sleep(600); continue; }
                    if (Vtbl<GetSvcDel>(pClient, 14)(pClient, ref iidCap, out pCap) < 0 || pCap == IntPtr.Zero) { Thread.Sleep(600); continue; }
                    fnStop = Vtbl<StartStopDel>(pClient, 11);
                    if (Vtbl<StartStopDel>(pClient, 10)(pClient) < 0) { Thread.Sleep(600); continue; }

                    NextPktDel fnNext = Vtbl<NextPktDel>(pCap, 5);
                    GetBufDel fnGet = Vtbl<GetBufDel>(pCap, 3);
                    RelBufDel fnRel = Vtbl<RelBufDel>(pCap, 4);
                    byte[] buf = new byte[65536];

                    float aLow = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 135.0 / rate));
                    float aMid = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 1250.0 / rate));

                    while (_running && _enabled)
                    {
                        uint pkt = 0u;
                        if (fnNext(pCap, out pkt) < 0) break;
                        if (pkt == 0u)
                        {
                            SubBassLevel *= 0.96f; MidHarmLevel *= 0.96f; HighAirLevel *= 0.96f; BeatPulse *= 0.92f;
                            Thread.Sleep(4);
                            continue;
                        }
                        while (pkt > 0u && _running && _enabled)
                        {
                            IntPtr pData; uint nFr, flg; ulong dPos, qPos;
                            if (fnGet(pCap, out pData, out nFr, out flg, out dPos, out qPos) < 0) break;
                            float sumSub = 0, sumMid = 0, sumHi = 0;
                            if (((flg & 2u) == 0u) && pData != IntPtr.Zero && nFr > 0u)
                            {
                                int bytes = (int)nFr * align;
                                if (bytes > buf.Length) buf = new byte[bytes + 4096];
                                Marshal.Copy(pData, buf, 0, bytes);
                                for (int f = 0; f < (int)nFr; f++)
                                {
                                    int o = f * align;
                                    float s = 0.0f;
                                    if (isF32 && bits == 32)
                                    {
                                        float fl = BitConverter.ToSingle(buf, o);
                                        float fr = (ch >= 2) ? BitConverter.ToSingle(buf, o + 4) : fl;
                                        s = 0.5f * (fl + fr);
                                    }
                                    else if (bits == 16)
                                    {
                                        float fl = BitConverter.ToInt16(buf, o) / 32768.0f;
                                        float fr = (ch >= 2) ? (BitConverter.ToInt16(buf, o + 2) / 32768.0f) : fl;
                                        s = 0.5f * (fl + fr);
                                    }
                                    lpLow += aLow * (s - lpLow);
                                    lpMid += aMid * (s - lpMid);
                                    float bandSub = lpLow;
                                    float bandMid = lpMid - lpLow;
                                    float bandHi  = s - lpMid;
                                    sumSub += bandSub * bandSub;
                                    sumMid += bandMid * bandMid;
                                    sumHi  += bandHi * bandHi;
                                }
                                float invN = 1.0f / Math.Max(1u, nFr);
                                float rmsSub = Math.Min(1.0f, (float)Math.Sqrt(sumSub * invN) * 6.5f);
                                float rmsMid = Math.Min(1.0f, (float)Math.Sqrt(sumMid * invN) * 7.5f);
                                float rmsHi  = Math.Min(1.0f, (float)Math.Sqrt(sumHi  * invN) * 9.5f);

                                envSub = envSub * 0.72f + rmsSub * 0.28f;
                                envMid = envMid * 0.72f + rmsMid * 0.28f;
                                envHi  = envHi  * 0.72f + rmsHi  * 0.28f;
                                if (rmsSub - prevSub > 0.14f)
                                    BeatPulse = Math.Min(1.0f, BeatPulse + (rmsSub - prevSub) * 2.2f);
                                else
                                    BeatPulse *= 0.90f;
                                prevSub = envSub;

                                SubBassLevel = envSub;
                                MidHarmLevel = envMid;
                                HighAirLevel = envHi;
                            }
                            fnRel(pCap, nFr);
                            if (fnNext(pCap, out pkt) < 0) break;
                        }
                    }
                }
                catch { Thread.Sleep(600); }
                finally
                {
                    try { if (fnStop != null && pClient != IntPtr.Zero) fnStop(pClient); } catch { }
                    if (pFmt != IntPtr.Zero) CoTaskMemFree(pFmt);
                    SafeRel(pCap); SafeRel(pClient); SafeRel(pDev); SafeRel(pEnum);
                }
            }
            try { CoUninitialize(); } catch { }
        }

        public void Dispose()
        {
            _running = false;
            _enabled = false;
        }
    }
}
