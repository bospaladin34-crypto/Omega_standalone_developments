using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OmegaSynthStudio
{
    public sealed class OmegaDecodedAudioClip
    {
        public string FilePath = "";
        public string DisplayName = "";
        public short[] PcmL = new short[0];
        public short[] PcmR = new short[0];
        public float DurationSec = 0.0f;

        // Extracted Acoustic Geometry & Spectral Resynthesis DNA
        public double DominantHz = 220.0;
        public string DominantNote = "A3";
        public double CentroidHz = 880.0;
        public int SuggestedM = 3;
        public int SuggestedN = 5;
        public int RosePetals = 7;
        public double StereoPhaseWidth = 0.35;
        public double[] ClonedRatios = new double[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        public double[] ClonedWeights = new double[] { 1.0, 0.55, 0.35, 0.25, 0.18, 0.12, 0.09, 0.06 };
        public double ClonedDecaySec = 3.2;
        public double ClonedBowMix = 0.45;
    }

    // Zero-NuGet Universal Audio File Decoder (.WAV 8/16/24/32/F32 + Win32 Media Foundation MP3/FLAC/M4A/AAC/OGG/MP4/WEBM)
    public static class OmegaAudioFileDecoder
    {
        private const uint MF_VERSION = 0x00020070u;
        private const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFEu;
        private const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFDu;
        private const uint MF_SOURCE_READERF_ERROR = 0x1u;
        private const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2u;

        private static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static Guid MF_MT_SUBTYPE    = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        private static Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00aa00389b71");

        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(uint version, uint dwFlags);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out IntPtr ppMFType);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateWaveFormatExFromMFMediaType(IntPtr pMFType, out IntPtr ppWF, out uint pcbSize, uint flags);
        [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int MFCreateSourceReaderFromURL(string pwszURL, IntPtr pAttributes, out IntPtr ppSourceReader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint RelComDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidDel(IntPtr s, ref Guid key, ref Guid val);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetStreamSelDel(IntPtr s, uint idx, int sel);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurTypeDel(IntPtr s, uint idx, IntPtr res, IntPtr pType);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCurTypeDel(IntPtr s, uint idx, out IntPtr ppType);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadSampleDel(IntPtr s, uint idx, uint ctrlFlags, out uint actualIdx, out uint streamFlags, out long timestamp, out IntPtr ppSample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ToContigBufDel(IntPtr s, out IntPtr ppBuf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockBufDel(IntPtr s, out IntPtr ppByte, out uint maxLen, out uint curLen);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UnlockBufDel(IntPtr s);

        public static OmegaDecodedAudioClip DecodeAndAnalyzeTo48kHz(string filePath, out string errorMsg)
        {
            errorMsg = "";
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                errorMsg = "Audio file not found.";
                return null;
            }

            float[] rawL = null, rawR = null;
            int srcRate = 48000;
            string ext = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();

            if (ext == ".wav")
            {
                TryDecodeRiffWav(filePath, out rawL, out rawR, out srcRate, out errorMsg);
            }

            if (rawL == null || rawL.Length < 64)
            {
                string mfErr;
                if (TryDecodeMediaFoundationMta(filePath, out rawL, out rawR, out srcRate, out mfErr))
                {
                    errorMsg = "";
                }
                else if (string.IsNullOrEmpty(errorMsg))
                {
                    errorMsg = mfErr;
                }
            }

            if (rawL == null || rawL.Length < 64)
            {
                if (string.IsNullOrEmpty(errorMsg)) errorMsg = "Unsupported or empty audio stream.";
                return null;
            }

            short[] pcm48L, pcm48R;
            ResampleTo48kHzInt16(rawL, rawR, srcRate, out pcm48L, out pcm48R);

            OmegaDecodedAudioClip clip = new OmegaDecodedAudioClip();
            clip.FilePath = filePath;
            clip.DisplayName = Path.GetFileName(filePath);
            clip.PcmL = pcm48L;
            clip.PcmR = pcm48R;
            clip.DurationSec = (float)pcm48L.Length / OmegaWavefieldAudioEngine.SampleRate;
            OmegaSpectralResynthesizer.AnalyzeClipGeometryAndDna(clip);
            return clip;
        }

        private static bool TryDecodeRiffWav(string path, out float[] outL, out float[] outR, out int sampleRate, out string err)
        {
            outL = null; outR = null; sampleRate = 48000; err = "";
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F')
                {
                    err = "Not a RIFF WAV container.";
                    return false;
                }
                int pos = 12;
                ushort fmtTag = 1, ch = 1, align = 2, bits = 16;
                int rate = 48000, dataOff = -1, dataLen = 0;

                while (pos + 8 <= b.Length)
                {
                    string cid = Encoding.ASCII.GetString(b, pos, 4);
                    int csz = BitConverter.ToInt32(b, pos + 4);
                    if (csz < 0) break;
                    int body = pos + 8;
                    if (cid == "fmt " && body + 16 <= b.Length)
                    {
                        fmtTag = BitConverter.ToUInt16(b, body);
                        ch = Math.Max((ushort)1, BitConverter.ToUInt16(b, body + 2));
                        rate = BitConverter.ToInt32(b, body + 4);
                        align = Math.Max((ushort)1, BitConverter.ToUInt16(b, body + 12));
                        bits = BitConverter.ToUInt16(b, body + 14);
                        if (fmtTag == 0xFFFE && csz >= 40 && body + 26 <= b.Length)
                            fmtTag = BitConverter.ToUInt16(b, body + 24);
                    }
                    else if (cid == "data")
                    {
                        dataOff = body;
                        dataLen = Math.Min(csz, b.Length - body);
                        break;
                    }
                    pos = body + csz + (csz & 1);
                }

                if (dataOff < 0 || dataLen < align) { err = "No PCM data chunk in WAV."; return false; }
                int maxFrames = Math.Min(dataLen / align, rate * 240); // Cap at 4 minutes for instant load
                outL = new float[maxFrames];
                outR = new float[maxFrames];
                sampleRate = Math.Max(8000, Math.Min(384000, rate));
                bool isFloat = (fmtTag == 3);

                for (int f = 0; f < maxFrames; f++)
                {
                    int o = dataOff + f * align;
                    float l = ReadSampleNorm(b, o, bits, isFloat);
                    float r = (ch >= 2) ? ReadSampleNorm(b, o + (bits / 8), bits, isFloat) : l;
                    outL[f] = l;
                    outR[f] = r;
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        private static float ReadSampleNorm(byte[] b, int o, int bits, bool isFloat)
        {
            if (o < 0 || o >= b.Length) return 0.0f;
            if (isFloat && bits == 32 && o + 4 <= b.Length) return Clamp1(BitConverter.ToSingle(b, o));
            if (bits == 16 && o + 2 <= b.Length) return BitConverter.ToInt16(b, o) / 32768.0f;
            if (bits == 24 && o + 3 <= b.Length)
            {
                int v = (b[o] << 8) | (b[o + 1] << 16) | (b[o + 2] << 24);
                return v / 2147483648.0f;
            }
            if (bits == 32 && o + 4 <= b.Length) return BitConverter.ToInt32(b, o) / 2147483648.0f;
            if (bits == 8) return (b[o] - 128) / 128.0f;
            return 0.0f;
        }

        private sealed class MfJob
        {
            public string Path;
            public float[] L, R;
            public int Rate = 48000;
            public bool Ok = false;
            public string Err = "";
        }

        private static bool TryDecodeMediaFoundationMta(string filePath, out float[] outL, out float[] outR, out int sampleRate, out string err)
        {
            MfJob job = new MfJob();
            job.Path = Path.GetFullPath(filePath);
            Thread t = new Thread(delegate() { RunMfDecodeCore(job); });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            t.Join(25000);
            outL = job.L; outR = job.R; sampleRate = job.Rate; err = job.Err;
            return job.Ok && outL != null && outL.Length >= 64;
        }

        private static void RunMfDecodeCore(MfJob job)
        {
            int hrCo = CoInitializeEx(IntPtr.Zero, 0u);
            bool mfStarted = false;
            IntPtr pReader = IntPtr.Zero, pPartial = IntPtr.Zero, pActual = IntPtr.Zero, pWaveFmt = IntPtr.Zero;
            try
            {
                int hr = MFStartup(MF_VERSION, 0u);
                if (hr < 0) { job.Err = "MFStartup 0x" + hr.ToString("X8"); return; }
                mfStarted = true;

                hr = MFCreateSourceReaderFromURL(job.Path, IntPtr.Zero, out pReader);
                if (hr < 0 || pReader == IntPtr.Zero) { job.Err = "MFCreateSourceReaderFromURL 0x" + hr.ToString("X8"); return; }

                SetStreamSelDel fnSel = Vtbl<SetStreamSelDel>(pReader, 4);
                fnSel(pReader, MF_SOURCE_READER_ALL_STREAMS, 0);
                if (fnSel(pReader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, 1) < 0)
                {
                    job.Err = "No decodable audio stream in file.";
                    return;
                }

                if (MFCreateMediaType(out pPartial) < 0 || pPartial == IntPtr.Zero) { job.Err = "MFCreateMediaType failed."; return; }
                SetGuidDel fnSetGuid = Vtbl<SetGuidDel>(pPartial, 24); // IMFAttributes::SetGUID = slot 24
                fnSetGuid(pPartial, ref MF_MT_MAJOR_TYPE, ref MFMediaType_Audio);
                fnSetGuid(pPartial, ref MF_MT_SUBTYPE, ref MFAudioFormat_PCM);

                hr = Vtbl<SetCurTypeDel>(pReader, 7)(pReader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, pPartial);
                if (hr < 0) { job.Err = "SetCurrentMediaType(PCM) 0x" + hr.ToString("X8"); return; }

                hr = Vtbl<GetCurTypeDel>(pReader, 6)(pReader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, out pActual);
                if (hr < 0 || pActual == IntPtr.Zero) { job.Err = "GetCurrentMediaType failed."; return; }

                uint cbWf = 0u;
                hr = MFCreateWaveFormatExFromMFMediaType(pActual, out pWaveFmt, out cbWf, 0u);
                if (hr < 0 || pWaveFmt == IntPtr.Zero) { job.Err = "MFCreateWaveFormatEx 0x" + hr.ToString("X8"); return; }

                ushort tag = (ushort)Marshal.ReadInt16(pWaveFmt, 0);
                int ch = Math.Max(1, (int)(ushort)Marshal.ReadInt16(pWaveFmt, 2));
                int rate = Marshal.ReadInt32(pWaveFmt, 4);
                int align = Math.Max(2, (int)(ushort)Marshal.ReadInt16(pWaveFmt, 12));
                int bits = (ushort)Marshal.ReadInt16(pWaveFmt, 14);
                bool isFloat = (tag == 3);
                job.Rate = Math.Max(8000, Math.Min(384000, rate));

                ReadSampleDel fnRead = Vtbl<ReadSampleDel>(pReader, 9);
                int maxSamples = job.Rate * 240;
                List<float> accL = new List<float>(Math.Min(maxSamples, job.Rate * 30));
                List<float> accR = new List<float>(Math.Min(maxSamples, job.Rate * 30));
                byte[] scratch = new byte[65536];

                while (accL.Count < maxSamples)
                {
                    uint actStream, flags; long ts; IntPtr pSample;
                    hr = fnRead(pReader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0u, out actStream, out flags, out ts, out pSample);
                    if (hr < 0 || (flags & (MF_SOURCE_READERF_ERROR | MF_SOURCE_READERF_ENDOFSTREAM)) != 0u)
                    {
                        RelCom(pSample);
                        break;
                    }
                    if (pSample == IntPtr.Zero) continue;

                    IntPtr pBuf = IntPtr.Zero;
                    try
                    {
                        // IMFSample::ConvertToContiguousBuffer = slot 41 (3 IUnknown + 30 IMFAttributes + 8 IMFSample)
                        if (Vtbl<ToContigBufDel>(pSample, 41)(pSample, out pBuf) >= 0 && pBuf != IntPtr.Zero)
                        {
                            IntPtr pBytes; uint maxL, curL;
                            if (Vtbl<LockBufDel>(pBuf, 3)(pBuf, out pBytes, out maxL, out curL) >= 0 && pBytes != IntPtr.Zero && curL >= (uint)align)
                            {
                                try
                                {
                                    int nBytes = (int)curL;
                                    if (nBytes > scratch.Length) scratch = new byte[nBytes + 4096];
                                    Marshal.Copy(pBytes, scratch, 0, nBytes);
                                    int nFrames = nBytes / align;
                                    int stepBytes = Math.Max(1, bits / 8);
                                    for (int f = 0; f < nFrames && accL.Count < maxSamples; f++)
                                    {
                                        int o = f * align;
                                        float fl = ReadSampleNorm(scratch, o, bits, isFloat);
                                        float fr = (ch >= 2) ? ReadSampleNorm(scratch, o + stepBytes, bits, isFloat) : fl;
                                        accL.Add(fl);
                                        accR.Add(fr);
                                    }
                                }
                                finally { Vtbl<UnlockBufDel>(pBuf, 4)(pBuf); }
                            }
                        }
                    }
                    finally
                    {
                        RelCom(pBuf);
                        RelCom(pSample);
                    }
                }

                job.L = accL.ToArray();
                job.R = accR.ToArray();
                job.Ok = (job.L.Length >= 64);
            }
            catch (Exception ex) { job.Err = ex.Message; }
            finally
            {
                if (pWaveFmt != IntPtr.Zero) CoTaskMemFree(pWaveFmt);
                RelCom(pActual);
                RelCom(pPartial);
                RelCom(pReader);
                if (mfStarted) { try { MFShutdown(); } catch { } }
                if (hrCo >= 0) CoUninitialize();
            }
        }

        private static void ResampleTo48kHzInt16(float[] srcL, float[] srcR, int srcRate, out short[] dstL, out short[] dstR)
        {
            const int dstRate = OmegaWavefieldAudioEngine.SampleRate;
            int nSrc = srcL.Length;
            float peak = 0.01f;
            for (int i = 0; i < nSrc; i += 4)
            {
                float m = Math.Max(Math.Abs(srcL[i]), Math.Abs(srcR[i]));
                if (m > peak) peak = m;
            }
            float normGain = (peak > 0.96f) ? (0.94f / peak) : ((peak < 0.25f) ? Math.Min(3.0f, 0.75f / peak) : 1.0f);

            if (srcRate == dstRate)
            {
                dstL = new short[nSrc]; dstR = new short[nSrc];
                for (int i = 0; i < nSrc; i++)
                {
                    dstL[i] = (short)(Clamp1(srcL[i] * normGain) * 32000.0f);
                    dstR[i] = (short)(Clamp1(srcR[i] * normGain) * 32000.0f);
                }
                return;
            }

            double ratio = (double)srcRate / dstRate;
            int nDst = Math.Max(64, (int)(nSrc / ratio));
            dstL = new short[nDst];
            dstR = new short[nDst];
            for (int i = 0; i < nDst; i++)
            {
                double sp = i * ratio;
                int i0 = (int)sp;
                if (i0 >= nSrc - 1) i0 = nSrc - 2;
                float frac = (float)(sp - i0);
                float fl = srcL[i0] * (1.0f - frac) + srcL[i0 + 1] * frac;
                float fr = srcR[i0] * (1.0f - frac) + srcR[i0 + 1] * frac;
                dstL[i] = (short)(Clamp1(fl * normGain) * 32000.0f);
                dstR[i] = (short)(Clamp1(fr * normGain) * 32000.0f);
            }
        }

        private static float Clamp1(float v) { return v > 0.99f ? 0.99f : (v < -0.99f ? -0.99f : v); }
        private static T Vtbl<T>(IntPtr p, int slot) where T : class { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size), typeof(T)); }
        private static void RelCom(IntPtr p) { if (p != IntPtr.Zero) { try { Vtbl<RelComDel>(p, 2)(p); } catch { } } }
    }

    // Spectral-to-Lattice Resynthesizer & Acoustic Geometry Analyzer
    public static class OmegaSpectralResynthesizer
    {
        public static void AnalyzeClipGeometryAndDna(OmegaDecodedAudioClip clip)
        {
            if (clip == null || clip.PcmL == null || clip.PcmL.Length < 512) return;
            int total = clip.PcmL.Length;

            // Find loudest transient/sustained window (4096 samples @ 48 kHz = 85.3 ms)
            int winLen = Math.Min(4096, total);
            int bestStart = 0;
            double bestRms = -1.0;
            int stride = Math.Max(512, total / 32);
            for (int off = 0; off + winLen <= total; off += stride)
            {
                double e = 0.0;
                for (int k = 0; k < winLen; k += 4)
                {
                    double m = (clip.PcmL[off + k] + clip.PcmR[off + k]) * 0.5 / 32768.0;
                    e += m * m;
                }
                if (e > bestRms) { bestRms = e; bestStart = off; }
            }

            // Measure stereo phase width & amplitude decay envelope
            double midE = 1e-9, sideE = 0.0;
            for (int k = 0; k < winLen; k++)
            {
                double l = clip.PcmL[bestStart + k] / 32768.0;
                double r = clip.PcmR[bestStart + k] / 32768.0;
                double mid = (l + r) * 0.5, side = (l - r) * 0.5;
                midE += mid * mid; sideE += side * side;
            }
            clip.StereoPhaseWidth = Math.Min(1.0, Math.Sqrt(sideE / midE));

            // High-resolution 180-bin logarithmic Goertzel scan (48 Hz -> 9,200 Hz) with Hann window
            const int Bins = 180;
            double[] freqs = new double[Bins];
            double[] mags = new double[Bins];
            double[] monoWin = new double[winLen];
            for (int n = 0; n < winLen; n++)
            {
                double hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * n / (winLen - 1)));
                monoWin[n] = ((clip.PcmL[bestStart + n] + clip.PcmR[bestStart + n]) * 0.5 / 32768.0) * hann;
            }

            double centroidNum = 0.0, centroidDen = 1e-9;
            for (int b = 0; b < Bins; b++)
            {
                double hz = 48.0 * Math.Pow(9200.0 / 48.0, (double)b / (Bins - 1));
                freqs[b] = hz;
                double w = 2.0 * Math.PI * hz / OmegaWavefieldAudioEngine.SampleRate;
                double cf = 2.0 * Math.Cos(w), s1 = 0.0, s2 = 0.0;
                for (int n = 0; n < winLen; n++)
                {
                    double s0 = monoWin[n] + cf * s1 - s2;
                    s2 = s1; s1 = s0;
                }
                double mag = Math.Sqrt(Math.Max(0.0, s1 * s1 + s2 * s2 - cf * s1 * s2)) / (winLen * 0.25);
                mags[b] = mag;
                centroidNum += hz * mag;
                centroidDen += mag;
            }
            clip.CentroidHz = centroidNum / centroidDen;

            // Pick up to 12 local spectral peaks separated by >= 3 bins
            List<int> peakBins = new List<int>();
            for (int b = 2; b < Bins - 2; b++)
            {
                if (mags[b] > mags[b - 1] && mags[b] >= mags[b + 1] && mags[b] > mags[b - 2] && mags[b] > mags[b + 2])
                    peakBins.Add(b);
            }
            peakBins.Sort(delegate(int a, int b) { return mags[b].CompareTo(mags[a]); });

            List<int> chosen = new List<int>();
            for (int i = 0; i < peakBins.Count && chosen.Count < 8; i++)
            {
                int cand = peakBins[i];
                bool tooClose = false;
                for (int j = 0; j < chosen.Count; j++) if (Math.Abs(chosen[j] - cand) < 4) { tooClose = true; break; }
                if (!tooClose) chosen.Add(cand);
            }
            if (chosen.Count == 0) chosen.Add(40);

            // Fundamental f0 is the lowest-frequency strong peak (>= 22% of strongest peak)
            double topMag = mags[chosen[0]];
            int f0Bin = chosen[0];
            for (int i = 0; i < chosen.Count; i++)
            {
                if (mags[chosen[i]] >= topMag * 0.22 && freqs[chosen[i]] < freqs[f0Bin])
                    f0Bin = chosen[i];
            }
            double f0 = Math.Max(45.0, Math.Min(2400.0, freqs[f0Bin]));
            clip.DominantHz = f0;
            int centsDummy;
            E8WaveLatticeMath.FormatNoteAndCents(f0, out clip.DominantNote, out centsDummy);

            chosen.Sort(delegate(int a, int b) { return freqs[a].CompareTo(freqs[b]); });
            double[] ratios = new double[8];
            double[] weights = new double[8];
            for (int i = 0; i < 8; i++)
            {
                if (i < chosen.Count)
                {
                    ratios[i] = Math.Max(0.5, Math.Min(15.0, freqs[chosen[i]] / f0));
                    weights[i] = Math.Max(0.05, Math.Min(1.0, mags[chosen[i]] / Math.Max(1e-6, topMag)));
                }
                else
                {
                    ratios[i] = (i == 0) ? 1.0 : (ratios[i - 1] + 1.0);
                    weights[i] = 0.08 / (i + 1.0);
                }
            }
            ratios[0] = 1.0;
            weights[0] = Math.Max(0.65, weights[0]);
            clip.ClonedRatios = ratios;
            clip.ClonedWeights = weights;

            // Estimate decay vs sustain profile across second half of clip
            double tailRms = 0.0;
            int tailPos = Math.Min(total - winLen, bestStart + Math.Min(total / 2, OmegaWavefieldAudioEngine.SampleRate));
            if (tailPos > bestStart)
            {
                for (int k = 0; k < winLen; k += 4)
                {
                    double m = (clip.PcmL[tailPos + k] + clip.PcmR[tailPos + k]) * 0.5 / 32768.0;
                    tailRms += m * m;
                }
            }
            double sustainRatio = Math.Sqrt(tailRms / Math.Max(1e-9, bestRms));
            clip.ClonedBowMix = Math.Max(0.08, Math.Min(0.92, sustainRatio * 0.95));
            clip.ClonedDecaySec = Math.Max(1.4, Math.Min(6.5, 1.6 + clip.DurationSec * 0.45 + sustainRatio * 2.5));

            // Derive signature Chladni (m, n) & Cymatic Rose petal count from harmonic ratios!
            int mEst = Math.Max(1, Math.Min(10, (int)Math.Round(ratios[1] * 1.8)));
            int nEst = Math.Max(2, Math.Min(12, (int)Math.Round((clip.CentroidHz / Math.Max(80.0, f0)) * 2.1)));
            if (mEst == nEst) nEst = Math.Min(12, mEst + 1);
            clip.SuggestedM = mEst;
            clip.SuggestedN = nEst;
            clip.RosePetals = Math.Max(3, Math.Min(24, mEst + nEst));
        }
    }

    // Zero-Feedback Win32 Core Audio WASAPI Loopback Pipe for OmegaBrowser / YouTube / Web Audio
    public sealed class OmegaWasapiLoopbackPipe : IDisposable
    {
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
        [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint RelDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetEpDel(IntPtr s, int flow, int role, out IntPtr ppDev);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ActivateDel(IntPtr s, ref Guid iid, uint ctx, IntPtr pPar, out IntPtr ppIf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitAudDel(IntPtr s, int mode, uint flags, long dur, long per, IntPtr pFmt, IntPtr pGuid);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MixFmtDel(IntPtr s, out IntPtr ppFmt);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StartStopDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSvcDel(IntPtr s, ref Guid riid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBufDel(IntPtr s, out IntPtr ppData, out uint pFrames, out uint pFlags, out ulong pDevPos, out ulong pQpc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RelBufDel(IntPtr s, uint frames);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NextPktDel(IntPtr s, out uint pFrames);

        private const int RingCap = 131072; // ~2.73 seconds at 48 kHz for 1-click Freeze & Resynthesize!
        private readonly short[] _ringL = new short[RingCap];
        private readonly short[] _ringR = new short[RingCap];
        private volatile int _writePos = 0;
        private volatile bool _running = false;
        private long _lastSignalTicks = 0;
        private Thread _worker;

        public volatile bool IsCapturing = false;
        public volatile int MixSampleRate = 48000;
        public volatile float PeakL = 0.0f;
        public volatile float PeakR = 0.0f;
        public string PipeTitle = "Desktop / OmegaBrowser Web Stream";
        public string PipeUrl = "";

        public void Start()
        {
            if (_running) return;
            _running = true;
            _worker = new Thread(CaptureWorker);
            _worker.IsBackground = true;
            _worker.SetApartmentState(ApartmentState.MTA);
            _worker.Name = "OmegaSynthWasapiPipe";
            _worker.Start();
        }

        public void Stop()
        {
            _running = false;
            IsCapturing = false;
            try { if (_worker != null && _worker.IsAlive) _worker.Join(350); } catch { }
        }

        public bool CopyLatestWindow(short[] dstL, short[] dstR)
        {
            if (!IsCapturing || dstL == null || dstR == null) return false;
            int n = Math.Min(dstL.Length, dstR.Length);
            long ageMs = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastSignalTicks)) / 10000L;
            if (ageMs > 220)
            {
                Array.Clear(dstL, 0, n);
                Array.Clear(dstR, 0, n);
                PeakL *= 0.85f; PeakR *= 0.85f;
                return true;
            }
            int wp = _writePos;
            int start = (wp - n + RingCap) & (RingCap - 1);
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) & (RingCap - 1);
                dstL[i] = _ringL[idx];
                dstR[i] = _ringR[idx];
            }
            return true;
        }

        public OmegaDecodedAudioClip CaptureFreezeClip(int durationMs)
        {
            int sr = Math.Max(8000, MixSampleRate);
            int count = Math.Min(RingCap - 1024, (sr * Math.Max(400, durationMs)) / 1000);
            short[] l = new short[count];
            short[] r = new short[count];
            int wp = _writePos;
            int start = (wp - count + RingCap) & (RingCap - 1);
            for (int i = 0; i < count; i++)
            {
                int idx = (start + i) & (RingCap - 1);
                l[i] = _ringL[idx];
                r[i] = _ringR[idx];
            }
            OmegaDecodedAudioClip clip = new OmegaDecodedAudioClip();
            clip.DisplayName = string.IsNullOrEmpty(PipeTitle) ? "Web Pipe Freeze" : PipeTitle;
            clip.PcmL = l;
            clip.PcmR = r;
            clip.DurationSec = (float)count / sr;
            OmegaSpectralResynthesizer.AnalyzeClipGeometryAndDna(clip);
            return clip;
        }

        private void CaptureWorker()
        {
            int hrCo = CoInitializeEx(IntPtr.Zero, 0u);
            Guid clsidEnum = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            Guid iidEnum   = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCap    = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

            while (_running)
            {
                IntPtr pEnum = IntPtr.Zero, pDev = IntPtr.Zero, pClient = IntPtr.Zero, pCap = IntPtr.Zero, pFmt = IntPtr.Zero;
                StartStopDel fnStop = null;
                try
                {
                    if (CoCreateInstance(ref clsidEnum, IntPtr.Zero, 23u, ref iidEnum, out pEnum) < 0 || pEnum == IntPtr.Zero) { Thread.Sleep(700); continue; }
                    GetEpDel fnEp = Vtbl<GetEpDel>(pEnum, 4);
                    if (fnEp(pEnum, 0, 1, out pDev) < 0 || pDev == IntPtr.Zero) fnEp(pEnum, 0, 0, out pDev);
                    if (pDev == IntPtr.Zero) { Thread.Sleep(700); continue; }
                    if (Vtbl<ActivateDel>(pDev, 3)(pDev, ref iidClient, 23u, IntPtr.Zero, out pClient) < 0 || pClient == IntPtr.Zero) { Thread.Sleep(700); continue; }
                    if (Vtbl<MixFmtDel>(pClient, 8)(pClient, out pFmt) < 0 || pFmt == IntPtr.Zero) { Thread.Sleep(700); continue; }

                    ushort tag = (ushort)Marshal.ReadInt16(pFmt, 0);
                    int ch = Math.Max(1, (int)(ushort)Marshal.ReadInt16(pFmt, 2));
                    int rate = Marshal.ReadInt32(pFmt, 4);
                    int align = Math.Max(2, (int)(ushort)Marshal.ReadInt16(pFmt, 12));
                    int bits = (ushort)Marshal.ReadInt16(pFmt, 14);
                    int cbSz = (ushort)Marshal.ReadInt16(pFmt, 16);
                    bool isF32 = (tag == 3) || (tag == 0xFFFE && cbSz >= 22 && Marshal.ReadInt32(pFmt, 24) == 3);
                    MixSampleRate = (rate >= 8000 && rate <= 384000) ? rate : 48000;

                    // AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000u
                    if (Vtbl<InitAudDel>(pClient, 3)(pClient, 0, 0x00020000u, 400000L, 0L, pFmt, IntPtr.Zero) < 0) { Thread.Sleep(700); continue; }
                    if (Vtbl<GetSvcDel>(pClient, 14)(pClient, ref iidCap, out pCap) < 0 || pCap == IntPtr.Zero) { Thread.Sleep(700); continue; }
                    fnStop = Vtbl<StartStopDel>(pClient, 11);
                    if (Vtbl<StartStopDel>(pClient, 10)(pClient) < 0) { Thread.Sleep(700); continue; }

                    NextPktDel fnNext = Vtbl<NextPktDel>(pCap, 5);
                    GetBufDel fnGet = Vtbl<GetBufDel>(pCap, 3);
                    RelBufDel fnRel = Vtbl<RelBufDel>(pCap, 4);
                    IsCapturing = true;
                    byte[] buf = new byte[65536];

                    while (_running)
                    {
                        uint pkt = 0u;
                        if (fnNext(pCap, out pkt) < 0) break;
                        if (pkt == 0u) { Thread.Sleep(3); continue; }
                        while (pkt > 0u && _running)
                        {
                            IntPtr pData; uint nFr, flg; ulong dPos, qPos;
                            if (fnGet(pCap, out pData, out nFr, out flg, out dPos, out qPos) < 0) break;
                            int wp = _writePos;
                            float pl = 0.0f, pr = 0.0f;
                            if (((flg & 2u) != 0u) || pData == IntPtr.Zero || nFr == 0u)
                            {
                                for (uint f = 0; f < nFr; f++) { _ringL[wp] = 0; _ringR[wp] = 0; wp = (wp + 1) & (RingCap - 1); }
                            }
                            else
                            {
                                int bytes = (int)nFr * align;
                                if (bytes > buf.Length) buf = new byte[bytes + 4096];
                                Marshal.Copy(pData, buf, 0, bytes);
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
                                    if (Math.Abs(fl) > pl) pl = Math.Abs(fl);
                                    if (Math.Abs(fr) > pr) pr = Math.Abs(fr);
                                    if (fl > 0.99f) fl = 0.99f; else if (fl < -0.99f) fl = -0.99f;
                                    if (fr > 0.99f) fr = 0.99f; else if (fr < -0.99f) fr = -0.99f;
                                    _ringL[wp] = (short)(fl * 32760.0f);
                                    _ringR[wp] = (short)(fr * 32760.0f);
                                    wp = (wp + 1) & (RingCap - 1);
                                }
                                Interlocked.Exchange(ref _lastSignalTicks, DateTime.UtcNow.Ticks);
                            }
                            _writePos = wp;
                            PeakL = PeakL * 0.70f + pl * 0.30f;
                            PeakR = PeakR * 0.70f + pr * 0.30f;
                            fnRel(pCap, nFr);
                            if (fnNext(pCap, out pkt) < 0) break;
                        }
                    }
                }
                catch { Thread.Sleep(700); }
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

        public void Dispose() { Stop(); }
        private static T Vtbl<T>(IntPtr p, int slot) where T : class { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size), typeof(T)); }
        private static void RelCom(IntPtr p) { if (p != IntPtr.Zero) { try { Vtbl<RelDel>(p, 2)(p); } catch { } } }
    }


    // Optional Laptop Microphone Ambient & Voice Input Engine (Win32 waveInOpen + Autocorrelation Pitch + Clap Detector)
    // Strictly Opt-In & Zero-Feedback: Never echoes raw mic PCM out of speakers!
    public sealed class OmegaAmbientMicCapture : IDisposable
    {
        private const uint WAVE_MAPPER = 0xFFFFFFFFu;
        private const ushort WAVE_FORMAT_PCM = 1;
        private const uint CALLBACK_EVENT = 0x00050000u;
        private const uint WHDR_DONE = 0x00000001u;
        private const int BufFrames = 1024;
        private const int BufCount = 6;
        private const int RingCap = 131072; // 2.73 seconds at 48 kHz for 1-click Sample Mic -> MPE #09!

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX_MIC
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR_MIC
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WAVEINCAPSW
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern int waveInGetDevCapsW(IntPtr uDeviceID, out WAVEINCAPSW pwic, uint cbwic);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInOpen(out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX_MIC pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInClose(IntPtr hwi);

        private readonly short[] _ringL = new short[RingCap];
        private readonly short[] _ringR = new short[RingCap];
        private readonly float[] _acfBuf = new float[BufFrames];
        private volatile int _writePos = 0;
        private volatile bool _running = false;
        private Thread _worker;
        private AutoResetEvent _inEvent;
        private int _transientPending = 0;
        private volatile float _transientStrength = 0.0f;
        private float _slowEnv = 0.01f;
        private long _lastOnsetTicks = 0;

        public volatile bool IsCapturing = false;
        public volatile int SelectedDeviceIndex = -1; // -1 = Default Laptop Mic (WAVE_MAPPER)
        public volatile float GainBoost = 2.5f;       // 0.5x .. 8.0x mic preamp
        public volatile float NoiseGate = 0.02f;      // 0.00 .. 0.25 ambient floor gate
        public volatile float PeakLevel = 0.0f;
        public volatile float RmsLevel = 0.0f;
        public volatile float DetectedHz = 0.0f;
        public volatile float PitchConfidence = 0.0f;
        public string ActiveDeviceName = "Default Laptop Mic";
        public string StatusMessage = "Mic OFF (Opt-In)";

        public static List<string> ListInputDevices()
        {
            List<string> list = new List<string>();
            list.Add("Default Laptop Mic (WAVE_MAPPER)");
            try
            {
                uint n = waveInGetNumDevs();
                uint cb = (uint)Marshal.SizeOf(typeof(WAVEINCAPSW));
                for (uint i = 0; i < n; i++)
                {
                    WAVEINCAPSW caps;
                    if (waveInGetDevCapsW((IntPtr)i, out caps, cb) == 0 && !string.IsNullOrEmpty(caps.szPname))
                        list.Add("HW #" + i + ": " + caps.szPname.Trim());
                    else
                        list.Add("HW #" + i + ": Audio Input Device");
                }
            }
            catch { }
            return list;
        }

        public bool Start(int deviceChoiceIndex)
        {
            Stop();
            SelectedDeviceIndex = deviceChoiceIndex;
            _running = true;
            _inEvent = new AutoResetEvent(false);
            _worker = new Thread(MicCaptureWorker);
            _worker.IsBackground = true;
            _worker.Priority = ThreadPriority.AboveNormal;
            _worker.Name = "OmegaSynthLaptopMicPump";
            _worker.Start();
            return true;
        }

        public void Stop()
        {
            _running = false;
            IsCapturing = false;
            try { if (_inEvent != null) _inEvent.Set(); } catch { }
            try { if (_worker != null && _worker.IsAlive) _worker.Join(400); } catch { }
            try { if (_inEvent != null) { _inEvent.Close(); _inEvent = null; } } catch { }
            PeakLevel = 0.0f;
            RmsLevel = 0.0f;
            DetectedHz = 0.0f;
            PitchConfidence = 0.0f;
            StatusMessage = "Mic OFF (Opt-In)";
        }

        public bool ConsumeTransientTrigger(out float strength)
        {
            if (Interlocked.Exchange(ref _transientPending, 0) == 1)
            {
                strength = _transientStrength;
                return true;
            }
            strength = 0.0f;
            return false;
        }

        public bool CopyLatestWindow(short[] dstL, short[] dstR)
        {
            if (!IsCapturing || dstL == null || dstR == null) return false;
            int n = Math.Min(dstL.Length, dstR.Length);
            int wp = _writePos;
            int start = (wp - n + RingCap) & (RingCap - 1);
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) & (RingCap - 1);
                dstL[i] = _ringL[idx];
                dstR[i] = _ringR[idx];
            }
            return true;
        }

        public OmegaDecodedAudioClip CaptureMicFreezeClip(int durationMs)
        {
            int sr = OmegaWavefieldAudioEngine.SampleRate;
            int count = Math.Min(RingCap - 1024, (sr * Math.Max(400, durationMs)) / 1000);
            short[] l = new short[count];
            short[] r = new short[count];
            int wp = _writePos;
            int start = (wp - count + RingCap) & (RingCap - 1);
            for (int i = 0; i < count; i++)
            {
                int idx = (start + i) & (RingCap - 1);
                l[i] = _ringL[idx];
                r[i] = _ringR[idx];
            }
            OmegaDecodedAudioClip clip = new OmegaDecodedAudioClip();
            clip.DisplayName = "Laptop Mic Sample (" + DateTime.Now.ToString("HHmmss") + ")";
            clip.PcmL = l;
            clip.PcmR = r;
            clip.DurationSec = (float)count / sr;
            OmegaSpectralResynthesizer.AnalyzeClipGeometryAndDna(clip);
            return clip;
        }

        private void MicCaptureWorker()
        {
            IntPtr hwi = IntPtr.Zero;
            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR_MIC));
            short[][] pcmBufs = new short[BufCount][];
            byte[][] hdrBytes = new byte[BufCount][];
            GCHandle[] pcmPins = new GCHandle[BufCount];
            GCHandle[] hdrPins = new GCHandle[BufCount];
            int channels = 2;

            try
            {
                uint devId = (SelectedDeviceIndex < 0) ? WAVE_MAPPER : (uint)SelectedDeviceIndex;
                WAVEFORMATEX_MIC fmt = new WAVEFORMATEX_MIC();
                fmt.wFormatTag = WAVE_FORMAT_PCM;
                fmt.nChannels = 2;
                fmt.nSamplesPerSec = (uint)OmegaWavefieldAudioEngine.SampleRate;
                fmt.wBitsPerSample = 16;
                fmt.nBlockAlign = 4;
                fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * 4u;
                fmt.cbSize = 0;

                int mmr = waveInOpen(out hwi, devId, ref fmt, _inEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                if (mmr != 0 || hwi == IntPtr.Zero)
                {
                    channels = 1;
                    fmt.nChannels = 1;
                    fmt.nBlockAlign = 2;
                    fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * 2u;
                    mmr = waveInOpen(out hwi, devId, ref fmt, _inEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                }
                if (mmr != 0 || hwi == IntPtr.Zero)
                {
                    StatusMessage = "Mic unavailable (winmm 0x" + mmr.ToString("X") + " - check Windows Mic privacy)";
                    IsCapturing = false;
                    return;
                }

                for (int b = 0; b < BufCount; b++)
                {
                    pcmBufs[b] = new short[BufFrames * channels];
                    pcmPins[b] = GCHandle.Alloc(pcmBufs[b], GCHandleType.Pinned);
                    hdrBytes[b] = new byte[hdrSize];
                    hdrPins[b] = GCHandle.Alloc(hdrBytes[b], GCHandleType.Pinned);

                    WAVEHDR_MIC h = new WAVEHDR_MIC();
                    h.lpData = pcmPins[b].AddrOfPinnedObject();
                    h.dwBufferLength = (uint)(pcmBufs[b].Length * 2);
                    Marshal.StructureToPtr(h, hdrPins[b].AddrOfPinnedObject(), false);

                    waveInPrepareHeader(hwi, hdrPins[b].AddrOfPinnedObject(), (uint)hdrSize);
                    waveInAddBuffer(hwi, hdrPins[b].AddrOfPinnedObject(), (uint)hdrSize);
                }

                waveInStart(hwi);
                IsCapturing = true;
                StatusMessage = "Laptop Mic LIVE (48kHz Zero-Feedback)";

                while (_running)
                {
                    _inEvent.WaitOne(35);
                    if (!_running) break;

                    for (int b = 0; b < BufCount; b++)
                    {
                        IntPtr pHdr = hdrPins[b].AddrOfPinnedObject();
                        WAVEHDR_MIC hdr = (WAVEHDR_MIC)Marshal.PtrToStructure(pHdr, typeof(WAVEHDR_MIC));
                        if ((hdr.dwFlags & WHDR_DONE) != 0u)
                        {
                            int framesRead = (int)(hdr.dwBytesRecorded / (uint)(channels * 2));
                            if (framesRead > BufFrames) framesRead = BufFrames;
                            if (framesRead > 0) ProcessCapturedBlock(pcmBufs[b], framesRead, channels);

                            hdr.dwBytesRecorded = 0u;
                            hdr.dwFlags &= ~WHDR_DONE;
                            Marshal.StructureToPtr(hdr, pHdr, false);
                            if (_running) waveInAddBuffer(hwi, pHdr, (uint)hdrSize);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Mic error: " + ex.Message;
            }
            finally
            {
                IsCapturing = false;
                if (hwi != IntPtr.Zero)
                {
                    try { waveInReset(hwi); } catch { }
                    for (int b = 0; b < BufCount; b++)
                    {
                        if (hdrPins[b].IsAllocated)
                        {
                            try { waveInUnprepareHeader(hwi, hdrPins[b].AddrOfPinnedObject(), (uint)hdrSize); } catch { }
                        }
                    }
                    try { waveInClose(hwi); } catch { }
                }
                for (int b = 0; b < BufCount; b++)
                {
                    if (hdrPins[b].IsAllocated) hdrPins[b].Free();
                    if (pcmPins[b].IsAllocated) pcmPins[b].Free();
                }
            }
        }

        private void ProcessCapturedBlock(short[] raw, int frames, int channels)
        {
            float gain = GainBoost;
            float gate = NoiseGate;
            int wp = _writePos;
            float pk = 0.0f;
            double sumSq = 0.0;

            for (int f = 0; f < frames; f++)
            {
                float sl, sr;
                if (channels >= 2)
                {
                    sl = (raw[f * 2] / 32768.0f) * gain;
                    sr = (raw[f * 2 + 1] / 32768.0f) * gain;
                }
                else
                {
                    sl = (raw[f] / 32768.0f) * gain;
                    sr = sl;
                }
                float mono = 0.5f * (sl + sr);
                float absM = Math.Abs(mono);
                if (absM > pk) pk = absM;
                sumSq += mono * mono;

                if (absM < gate)
                {
                    float atten = absM / Math.Max(0.002f, gate);
                    sl *= atten * atten;
                    sr *= atten * atten;
                    mono *= atten * atten;
                }
                if (sl > 0.98f) sl = 0.98f; else if (sl < -0.98f) sl = -0.98f;
                if (sr > 0.98f) sr = 0.98f; else if (sr < -0.98f) sr = -0.98f;

                _ringL[wp] = (short)(sl * 32600.0f);
                _ringR[wp] = (short)(sr * 32600.0f);
                wp = (wp + 1) & (RingCap - 1);

                if (f < BufFrames) _acfBuf[f] = mono;
            }
            _writePos = wp;

            float rms = (float)Math.Sqrt(sumSq / Math.Max(1, frames));
            PeakLevel = PeakLevel * 0.65f + Math.Min(1.0f, pk) * 0.35f;
            RmsLevel = RmsLevel * 0.60f + Math.Min(1.0f, rms) * 0.40f;

            float ratio = rms / Math.Max(0.012f, _slowEnv);
            _slowEnv = _slowEnv * 0.90f + rms * 0.10f;
            long nowTicks = DateTime.UtcNow.Ticks;
            if (rms > Math.Max(0.045f, gate * 2.2f) && ratio > 2.35f && ((nowTicks - _lastOnsetTicks) / 10000L) > 95L)
            {
                _lastOnsetTicks = nowTicks;
                _transientStrength = Math.Min(1.0f, rms * 2.2f);
                Interlocked.Exchange(ref _transientPending, 1);
            }

            if (frames >= 768 && rms > Math.Max(0.015f, gate * 1.15f))
            {
                int n = Math.Min(frames, 768);
                double e0 = 0.0;
                for (int i = 0; i < n; i++) e0 += _acfBuf[i] * _acfBuf[i];
                if (e0 > 1e-5)
                {
                    int minLag = 45;
                    int maxLag = 640;
                    int bestLag = 0;
                    double bestCorr = 0.0;
                    bool passedDip = false;
                    for (int lag = minLag; lag <= maxLag && lag < n - 32; lag += 2)
                    {
                        double sum = 0.0, e1 = 0.0, e2 = 0.0;
                        int span = n - lag;
                        for (int i = 0; i < span; i += 2)
                        {
                            float a = _acfBuf[i], b = _acfBuf[i + lag];
                            sum += a * b; e1 += a * a; e2 += b * b;
                        }
                        double norm = sum / Math.Sqrt(Math.Max(1e-9, e1 * e2));
                        if (norm < 0.25) passedDip = true;
                        if (passedDip && norm > bestCorr)
                        {
                            bestCorr = norm;
                            bestLag = lag;
                        }
                    }
                    if (bestLag > minLag && bestCorr >= 0.56)
                    {
                        double hz = (double)OmegaWavefieldAudioEngine.SampleRate / bestLag;
                        if (DetectedHz > 40.0f && Math.Abs(hz - DetectedHz) < DetectedHz * 0.08)
                            DetectedHz = (float)(DetectedHz * 0.65 + hz * 0.35);
                        else
                            DetectedHz = (float)hz;
                        PitchConfidence = (float)bestCorr;
                    }
                    else
                    {
                        PitchConfidence *= 0.75f;
                        if (PitchConfidence < 0.25f) DetectedHz = 0.0f;
                    }
                }
            }
            else
            {
                PitchConfidence *= 0.70f;
                if (PitchConfidence < 0.20f) DetectedHz = 0.0f;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    // Mathematical Vector .SVG + High-Res .PNG Acoustic Geometry Blueprint Exporter
    public static class OmegaCymaticGeometryExporter
    {
        public static string ExportVectorSvgBlueprint(
            string rendersDir, string title, int mMode, int nMode, float[] spec32, short[] waveL, short[] waveR)
        {
            Directory.CreateDirectory(rendersDir);
            string path = Path.Combine(rendersDir, "cymatic_shape_" + mMode + "_" + nMode + "_" + DateTime.Now.ToString("HHmmss") + ".svg");
            StringBuilder sb = new StringBuilder(32768);
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1200 620\" width=\"1200\" height=\"620\">");
            sb.AppendLine("  <rect width=\"1200\" height=\"620\" fill=\"#060913\"/>");
            sb.AppendLine("  <text x=\"32\" y=\"36\" fill=\"#f472b6\" font-family=\"Consolas,monospace\" font-size=\"16\" font-weight=\"bold\">OMEGASYNTH v1.1.1 FINAL GM :: ACOUSTIC GEOMETRY BLUEPRINT — " + XmlEsc(title) + " [Chladni(" + mMode + "," + nMode + ")]</text>");

            // Left Panel (520x520): Exact Chladni Nodal Zero-Crossing Contours
            sb.AppendLine("  <rect x=\"32\" y=\"56\" width=\"520\" height=\"520\" fill=\"#0a1122\" stroke=\"#38bdf8\" stroke-width=\"1.5\"/>");
            double sgn = (((mMode + nMode) & 1) == 0) ? 1.0 : -1.0;
            const int G = 96;
            sb.Append("  <path fill=\"none\" stroke=\"#facc15\" stroke-width=\"1.6\" d=\"");
            for (int gy = 0; gy < G; gy++)
            {
                double uy0 = (double)gy / G, uy1 = (double)(gy + 1) / G;
                for (int gx = 0; gx < G; gx++)
                {
                    double ux0 = (double)gx / G, ux1 = (double)(gx + 1) / G;
                    double v00 = EvalEig(mMode, nMode, sgn, ux0, uy0);
                    double v10 = EvalEig(mMode, nMode, sgn, ux1, uy0);
                    double v01 = EvalEig(mMode, nMode, sgn, ux0, uy1);
                    if (v00 * v10 <= 0.0)
                    {
                        double t = Math.Abs(v00) / Math.Max(1e-6, Math.Abs(v00 - v10));
                        double px = 32.0 + (ux0 + (ux1 - ux0) * t) * 520.0;
                        double py = 56.0 + uy0 * 520.0;
                        sb.AppendFormat(CultureInfo.InvariantCulture, "M{0:0.0},{1:0.0}h2.2", px, py);
                    }
                    if (v00 * v01 <= 0.0)
                    {
                        double t = Math.Abs(v00) / Math.Max(1e-6, Math.Abs(v00 - v01));
                        double px = 32.0 + ux0 * 520.0;
                        double py = 56.0 + (uy0 + (uy1 - uy0) * t) * 520.0;
                        sb.AppendFormat(CultureInfo.InvariantCulture, "M{0:0.0},{1:0.0}v2.2", px, py);
                    }
                }
            }
            sb.AppendLine("\"/>");

            // Right Panel (588,56 -> 580x520): Polar Cymatic Rose Mandala + Stereo Lissajous Core!
            double cx = 878.0, cy = 316.0;
            sb.AppendLine("  <circle cx=\"878\" cy=\"316\" r=\"248\" fill=\"#091020\" stroke=\"#c084fc\" stroke-width=\"1.2\"/>");
            int petals = Math.Max(3, mMode + nMode);
            for (int ring = 0; ring < 3; ring++)
            {
                string col = (ring == 0) ? "#38bdf8" : (ring == 1 ? "#f472b6" : "#34d399");
                double baseR = 95.0 + ring * 52.0;
                sb.Append("  <polygon fill=\"none\" stroke=\"" + col + "\" stroke-width=\"1.8\" points=\"");
                for (int k = 0; k < 360; k++)
                {
                    double th = k * Math.PI * 2.0 / 360.0;
                    double mod = 0.0;
                    for (int b = 0; b < 12; b++)
                    {
                        float sbVal = (spec32 != null && b * 2 < spec32.Length) ? spec32[b * 2] : 0.25f;
                        mod += sbVal * Math.Cos((petals + b + ring) * th + ring * 0.7) * 14.0;
                    }
                    double r = baseR + 28.0 * Math.Cos(petals * th) + mod;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0} ", cx + Math.Cos(th) * r, cy + Math.Sin(th) * r);
                }
                sb.AppendLine("\"/>");
            }

            if (waveL != null && waveR != null && waveL.Length >= 256)
            {
                sb.Append("  <polyline fill=\"none\" stroke=\"#fde047\" stroke-width=\"1.4\" points=\"");
                for (int i = 0; i < 256; i++)
                {
                    double lx = cx + (waveL[i] / 32768.0) * 115.0;
                    double ly = cy - (waveR[i] / 32768.0) * 115.0;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0} ", lx, ly);
                }
                sb.AppendLine("\"/>");
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string ExportHighResPngPrint(
            string rendersDir, Control stageCtrl, string title, int mMode, int nMode, float[] spec32, short[] waveL, short[] waveR)
        {
            Directory.CreateDirectory(rendersDir);
            string path = Path.Combine(rendersDir, "cymatic_print_" + mMode + "_" + nMode + "_" + DateTime.Now.ToString("HHmmss") + ".png");

            if (stageCtrl != null && stageCtrl.Visible && stageCtrl.Width >= 120 && stageCtrl.Height >= 120)
            {
                try
                {
                    Point sc = stageCtrl.PointToScreen(Point.Empty);
                    using (Bitmap snap = new Bitmap(stageCtrl.Width, stageCtrl.Height, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(snap))
                    {
                        g.CopyFromScreen(sc.X, sc.Y, 0, 0, snap.Size, CopyPixelOperation.SourceCopy);
                        using (SolidBrush br = new SolidBrush(Color.FromArgb(190, 8, 12, 24)))
                            g.FillRectangle(br, 12, 12, 540, 28);
                        using (Font fn = new Font("Consolas", 9.5f, FontStyle.Bold))
                        using (SolidBrush fg = new SolidBrush(Color.FromArgb(244, 114, 182)))
                            g.DrawString("OMEGASYNTH v1.1.1 FINAL GM :: " + title + " | Chladni(" + mMode + "," + nMode + ")", fn, fg, 18, 17);
                        snap.Save(path, ImageFormat.Png);
                        return path;
                    }
                }
                catch { }
            }

            using (Bitmap bmp = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(6, 9, 19));
                int petals = Math.Max(3, mMode + nMode);
                PointF[] pts = new PointF[720];
                for (int k = 0; k < 720; k++)
                {
                    double th = k * Math.PI * 2.0 / 720.0;
                    double r = 320.0 + 95.0 * Math.Cos(petals * th) + 45.0 * Math.Sin(mMode * th) * Math.Cos(nMode * th);
                    pts[k] = new PointF((float)(512.0 + Math.Cos(th) * r), (float)(512.0 + Math.Sin(th) * r));
                }
                using (Pen p = new Pen(Color.FromArgb(56, 189, 248), 2.4f)) g.DrawPolygon(p, pts);
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        public static string ExportSoundShapeCapsule(
            string printsDir, Control stageCtrl, string title, OmegaWavefieldAudioEngine audio,
            float[] spec32, short[] waveL, short[] waveR)
        {
            Directory.CreateDirectory(printsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safe = Regex.Replace(title ?? "capsule", @"[^A-Za-z0-9_\-]+", "_").Trim('_');
            if (safe.Length > 28) safe = safe.Substring(0, 28);
            if (safe.Length == 0) safe = "capsule";
            string baseDir = Path.Combine(printsDir, safe + "_" + stamp);
            Directory.CreateDirectory(baseDir);

            ExportHighResPngPrint(baseDir, stageCtrl, title, audio.ChladniM, audio.ChladniN, spec32, waveL, waveR);
            ExportVectorSvgBlueprint(baseDir, title, audio.ChladniM, audio.ChladniN, spec32, waveL, waveR);
            OmegaSynthPatchBank.SaveEnginePatchToFile(Path.Combine(baseDir, safe + ".osynth"), title, audio);
            return baseDir;
        }

        private static double EvalEig(int m, int n, double sgn, double x, double y)
        {
            return Math.Cos(m * Math.PI * x) * Math.Cos(n * Math.PI * y) + sgn * Math.Cos(n * Math.PI * x) * Math.Cos(m * Math.PI * y);
        }

        private static string XmlEsc(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
