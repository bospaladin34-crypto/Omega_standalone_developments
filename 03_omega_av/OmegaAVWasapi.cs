using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmegaAVStudio
{
 // Zero-NuGet Windows Core Audio WASAPI System-Audio Loopback + Live Stereo WAV Recorder Tap
 public sealed class WasapiLoopbackEngine
 {
 [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
 [DllImport("ole32.dll")] private static extern void CoUninitialize();
 [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
 [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);

 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseDel(IntPtr s);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetEpDel(IntPtr s, int flow, int role, out IntPtr ppDev);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ActivateDel(IntPtr s, ref Guid iid, uint ctx, IntPtr pPar, out IntPtr ppIf);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitAudDel(IntPtr s, int mode, uint flags, long dur, long per, IntPtr pFmt, IntPtr pGuid);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MixFmtDel(IntPtr s, out IntPtr ppFmt);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StartStopDel(IntPtr s);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSvcDel(IntPtr s, ref Guid riid, out IntPtr ppv);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBufDel(IntPtr s, out IntPtr ppData, out uint pFrames, out uint pFlags, out ulong pDevPos, out ulong pQpc);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RelBufDel(IntPtr s, uint frames);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NextPktDel(IntPtr s, out uint pFrames);

 private const int RingCap = 8192;
 private readonly short[] _ringL = new short[RingCap], _ringR = new short[RingCap];
 private volatile int _writePos = 0;
 private volatile bool _running = false;
 private long _lastActiveTicks = 0;
 private Thread _worker;

 private readonly object _recLock = new object();
 private List<short> _recL = null, _recR = null;
 private volatile bool _recording = false;

 public volatile bool IsCapturing = false;
 public volatile int MixSampleRate = 48000;
 public volatile float PeakLevel = 0.0f;
 public string StatusText = "WASAPI Idle";

 public void Start()
 {
 if (_running) return;
 _running = true;
 _worker = new Thread(CaptureLoop);
 _worker.IsBackground = true;
 _worker.SetApartmentState(ApartmentState.MTA);
 _worker.Start();
 }

 public void Stop()
 {
 _running = false; IsCapturing = false; _recording = false;
 try { if (_worker != null && _worker.IsAlive) _worker.Join(500); } catch { }
 }

 public void BeginLiveAudioRecord()
 {
 lock (_recLock)
 {
 _recL = new List<short>(MixSampleRate * 16);
 _recR = new List<short>(MixSampleRate * 16);
 _recording = true;
 }
 }

 public void EndLiveAudioRecord(out short[] pcmL, out short[] pcmR, out int sampleRate)
 {
 lock (_recLock)
 {
 _recording = false;
 sampleRate = MixSampleRate;
 pcmL = (_recL != null) ? _recL.ToArray() : new short[0];
 pcmR = (_recR != null) ? _recR.ToArray() : new short[0];
 _recL = null; _recR = null;
 }
 }

 public bool ReadLatestWindow(short[] dstL, short[] dstR, out int sampleRate)
 {
 sampleRate = MixSampleRate;
 if (!IsCapturing) return false;
 long ageMs = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastActiveTicks)) / 10000L;
 int n = dstL.Length;
 if (ageMs > 160)
 {
 for (int i = 0; i < n; i++) { dstL[i] = 0; dstR[i] = 0; }
 PeakLevel *= 0.85f;
 return true;
 }
 int start = (_writePos - n + RingCap) % RingCap;
 for (int i = 0; i < n; i++) { int idx = (start + i) % RingCap; dstL[i] = _ringL[idx]; dstR[i] = _ringR[idx]; }
 return true;
 }

 private void CaptureLoop()
 {
 int hrCo = CoInitializeEx(IntPtr.Zero, 0);
 Guid clsidEnum = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
 Guid iidEnum = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
 Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
 Guid iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

 while (_running)
 {
 IntPtr pEnum = IntPtr.Zero, pDev = IntPtr.Zero, pClient = IntPtr.Zero, pCap = IntPtr.Zero, pFmt = IntPtr.Zero;
 StartStopDel fnStop = null;
 try
 {
 if (CoCreateInstance(ref clsidEnum, IntPtr.Zero, 23u, ref iidEnum, out pEnum) < 0 || pEnum == IntPtr.Zero) { StatusText = "WASAPI Enum Wait"; Thread.Sleep(900); continue; }
 GetEpDel fnEp = Vtbl<GetEpDel>(pEnum, 4);
 if (fnEp(pEnum, 0, 1, out pDev) < 0 || pDev == IntPtr.Zero) fnEp(pEnum, 0, 0, out pDev);
 if (pDev == IntPtr.Zero) { StatusText = "No Speaker Endpoint"; Thread.Sleep(900); continue; }
 if (Vtbl<ActivateDel>(pDev, 3)(pDev, ref iidClient, 23u, IntPtr.Zero, out pClient) < 0 || pClient == IntPtr.Zero) { Thread.Sleep(900); continue; }
 if (Vtbl<MixFmtDel>(pClient, 8)(pClient, out pFmt) < 0 || pFmt == IntPtr.Zero) { Thread.Sleep(900); continue; }

 ushort tag = (ushort)Marshal.ReadInt16(pFmt, 0);
 int ch = Math.Max(1, (int)(ushort)Marshal.ReadInt16(pFmt, 2));
 int rate = Marshal.ReadInt32(pFmt, 4);
 int align = Math.Max(2, (int)(ushort)Marshal.ReadInt16(pFmt, 12));
 int bits = (ushort)Marshal.ReadInt16(pFmt, 14);
 int cbSz = (ushort)Marshal.ReadInt16(pFmt, 16);
 bool isF32 = (tag == 3) || (tag == 0xFFFE && cbSz >= 22 && Marshal.ReadInt32(pFmt, 24) == 3);
 MixSampleRate = (rate >= 8000 && rate <= 384000) ? rate : 48000;

 int hr = Vtbl<InitAudDel>(pClient, 3)(pClient, 0, 0x00020000u, 500000L, 0L, pFmt, IntPtr.Zero);
 if (hr < 0) { StatusText = "WASAPI Init 0x" + hr.ToString("X8"); Thread.Sleep(900); continue; }
 if (Vtbl<GetSvcDel>(pClient, 14)(pClient, ref iidCap, out pCap) < 0 || pCap == IntPtr.Zero) { Thread.Sleep(900); continue; }
 fnStop = Vtbl<StartStopDel>(pClient, 11);
 if (Vtbl<StartStopDel>(pClient, 10)(pClient) < 0) { Thread.Sleep(900); continue; }

 NextPktDel fnNext = Vtbl<NextPktDel>(pCap, 5);
 GetBufDel fnGet = Vtbl<GetBufDel>(pCap, 3);
 RelBufDel fnRel = Vtbl<RelBufDel>(pCap, 4);
 IsCapturing = true;
 StatusText = "WASAPI " + (MixSampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "kHz " + (isF32 ? "F32" : ("PCM" + bits));
 byte[] buf = new byte[65536];

 while (_running)
 {
 uint pkt = 0;
 if (fnNext(pCap, out pkt) < 0) break;
 if (pkt == 0) { Thread.Sleep(4); continue; }
 while (pkt > 0 && _running)
 {
  IntPtr pData; uint nFr, flg; ulong dPos, qPos;
  if (fnGet(pCap, out pData, out nFr, out flg, out dPos, out qPos) < 0) break;
  int bytes = (int)nFr * align, wp = _writePos;
  float pk = 0.0f;
  bool recNow = _recording;
  if (((flg & 2u) != 0) || pData == IntPtr.Zero || nFr == 0)
  {
  for (uint f = 0; f < nFr; f++) { _ringL[wp] = 0; _ringR[wp] = 0; if (++wp >= RingCap) wp = 0; }
  if (recNow && nFr > 0)
  {
  lock (_recLock) { if (_recording && _recL != null && _recL.Count < MixSampleRate * 180) { for (uint f = 0; f < nFr; f++) { _recL.Add(0); _recR.Add(0); } } }
  }
  }
  else
  {
  if (bytes > buf.Length) buf = new byte[bytes + 4096];
  Marshal.Copy(pData, buf, 0, bytes);
  short[] tmpL = recNow ? new short[nFr] : null;
  short[] tmpR = recNow ? new short[nFr] : null;
  for (int f = 0; f < (int)nFr; f++)
  {
  int o = f * align; float fl = 0.0f, fr = 0.0f;
  if (isF32 && bits == 32) { fl = BitConverter.ToSingle(buf, o); fr = (ch >= 2) ? BitConverter.ToSingle(buf, o + 4) : fl; }
  else if (bits == 16) { fl = BitConverter.ToInt16(buf, o) / 32768.0f; fr = (ch >= 2) ? (BitConverter.ToInt16(buf, o + 2) / 32768.0f) : fl; }
  else if (bits == 32) { fl = BitConverter.ToInt32(buf, o) / 2147483648.0f; fr = (ch >= 2) ? (BitConverter.ToInt32(buf, o + 4) / 2147483648.0f) : fl; }
  else if (bits == 24)
  {
  fl = ((buf[o] << 8) | (buf[o + 1] << 16) | (buf[o + 2] << 24)) / 2147483648.0f;
  fr = (ch >= 2) ? (((buf[o + 3] << 8) | (buf[o + 4] << 16) | (buf[o + 5] << 24)) / 2147483648.0f) : fl;
  }
  float m = Math.Max(Math.Abs(fl), Math.Abs(fr)); if (m > pk) pk = m;
  if (fl > 0.99f) fl = 0.99f; else if (fl < -0.99f) fl = -0.99f;
  if (fr > 0.99f) fr = 0.99f; else if (fr < -0.99f) fr = -0.99f;
  short sl = (short)(fl * 32760.0f), sr = (short)(fr * 32760.0f);
  _ringL[wp] = sl; _ringR[wp] = sr;
  if (recNow) { tmpL[f] = sl; tmpR[f] = sr; }
  if (++wp >= RingCap) wp = 0;
  }
  if (recNow && tmpL != null)
  {
  lock (_recLock)
  {
  if (_recording && _recL != null && _recL.Count < MixSampleRate * 180) { _recL.AddRange(tmpL); _recR.AddRange(tmpR); }
  }
  }
  Interlocked.Exchange(ref _lastActiveTicks, DateTime.UtcNow.Ticks);
  }
  _writePos = wp; PeakLevel = PeakLevel * 0.72f + pk * 0.28f;
  fnRel(pCap, nFr);
  if (fnNext(pCap, out pkt) < 0) break;
 }
 }
 }
 catch { Thread.Sleep(800); }
 finally
 {
 IsCapturing = false;
 try { if (fnStop != null && pClient != IntPtr.Zero) fnStop(pClient); } catch { }
 if (pFmt != IntPtr.Zero) CoTaskMemFree(pFmt);
 RelCom(pCap); RelCom(pClient); RelCom(pDev); RelCom(pEnum);
 }
 }
 if (hrCo >= 0) CoUninitialize();
 }

 private static T Vtbl<T>(IntPtr p, int slot) where T : class { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size), typeof(T)); }
 private static void RelCom(IntPtr p) { if (p != IntPtr.Zero) { try { Vtbl<ReleaseDel>(p, 2)(p); } catch { } } }
 }
}
