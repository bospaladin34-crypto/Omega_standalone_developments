using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmegaStegStudio
{
    public sealed class OmegaStegAcousticModem : IDisposable
    {
        public const int SampleRate = 48000;
        public const int FftN = 2048;        // 42.67 ms orthogonal FFT window (df = 23.4375 Hz)
        public const int GuardSamples = 384; // 8.00 ms raised-cosine guard -> 50.67 ms per 24-bit Golay frame!
        public const int SymbolStride = FftN + GuardSamples; // 2432 samples

        // 24 Orthogonal Harmonic Bins (703.1 Hz .. 2320.3 Hz, spaced by 3 bins = 70.3125 Hz)
        // + 2 Continuous Reference Pilot Bins (Bin 26 = 609.38 Hz, Bin 104 = 2437.50 Hz)
        public static readonly int[] CarrierBins24 = new int[24]
        {
            30, 33, 36, 39, 42, 45, 48, 51, 54, 57, 60, 63,
            66, 69, 72, 75, 78, 81, 84, 87, 90, 93, 96, 99
        };
        public const int PilotBinLow = 26;
        public const int PilotBinHigh = 104;
        public const int SyncBinMarker = 108; // 2531.25 Hz preamble marker

        private const uint WAVE_MAPPER = 0xFFFFFFFFu;
        private const ushort WAVE_FORMAT_PCM = 1;
        private const uint CALLBACK_EVENT = 0x00050000u;
        private const uint WHDR_DONE = 0x00000001u;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutClose(IntPtr hwo);

        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveInOpen(out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
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

        // Rolling 64x64 Waterfall Spectrogram Buffer for 60 FPS GPU Display!
        public const int SpecCols = 64;
        public const int SpecRows = 64;
        public readonly float[] WaterfallSpec = new float[SpecCols * SpecRows];
        public readonly float[] LiveCarrierLevels24 = new float[24];

        public volatile bool IsTransmitting = false;
        public volatile bool IsMicListening = false;
        public volatile float MicPeakLevel = 0.0f;
        public volatile string ModemStatus = "Idle (48kHz 24-Carrier Harmonic Chord Ready)";
        public Action<GolayDecodeReceipt> OnAirGapDecoded;

        private Thread _micThread;
        private volatile bool _stopMicFlag = false;

        // Synthesize 48 kHz 16-bit Mono PCM of the Harmonic Chord Packet:
        // Structure:
        //   Symbol 0, 1: Sync Preamble A (PilotLow + PilotHigh + SyncBinMarker strong)
        //   Symbol 2   : Sync Frame-Count Header (Golay24 of nFrames!)
        //   Symbol 3.. : Golay24 Payload Frames (each 24-bit codeword modulates the 24 harmonic tones!)
        public short[] SynthesizeChordPcm(int[] golayFrames, out double durationSec)
        {
            int nData = golayFrames.Length;
            int totalSyms = 3 + nData;
            int totalSamples = totalSyms * SymbolStride + 2400;
            short[] pcm = new short[totalSamples];
            durationSec = totalSamples / (double)SampleRate;

            // Deterministic crest-factor-minimizing Schroeder phase offsets across 24 carriers!
            double[] phaseOffset = new double[24];
            for (int k = 0; k < 24; k++)
                phaseOffset[k] = Math.PI * k * k / 24.0;

            int lenFrameWord = GolayLeechCodec.EncodeGolay24(Math.Min(4095, nData));

            for (int sym = 0; sym < totalSyms; sym++)
            {
                int baseSample = 1200 + sym * SymbolStride;
                int word24 = (sym == 2) ? lenFrameWord : ((sym >= 3) ? golayFrames[sym - 3] : 0);
                bool isPreamble = (sym < 2);

                for (int n = 0; n < SymbolStride; n++)
                {
                    // Smooth raised-cosine window on guard edges -> zero clicks, warm harmonic bell chord!
                    double env = 1.0;
                    if (n < GuardSamples)
                        env = 0.5 * (1.0 - Math.Cos(Math.PI * n / GuardSamples));
                    else if (n >= SymbolStride - GuardSamples)
                        env = 0.5 * (1.0 - Math.Cos(Math.PI * (SymbolStride - 1 - n) / GuardSamples));

                    double tNorm = (2.0 * Math.PI * n) / FftN;
                    double acc = 0.0;

                    // Continuous Pilot Low & Pilot High
                    acc += 0.95 * Math.Cos(PilotBinLow * tNorm);
                    acc += 0.95 * Math.Cos(PilotBinHigh * tNorm + 1.1);

                    if (isPreamble)
                    {
                        acc += 1.65 * Math.Cos(SyncBinMarker * tNorm + (sym == 1 ? 0.7 : 0.0));
                        // Gentle fifth shimmer during preamble
                        acc += 0.65 * Math.Cos(CarrierBins24[0] * tNorm) + 0.65 * Math.Cos(CarrierBins24[11] * tNorm);
                    }
                    else
                    {
                        for (int k = 0; k < 24; k++)
                        {
                            bool bitOne = (((word24 >> k) & 1) != 0);
                            double amp = bitOne ? 1.00 : 0.16; // High-contrast harmonic envelope keying
                            acc += amp * Math.Cos(CarrierBins24[k] * tNorm + phaseOffset[k]);
                        }
                    }

                    double normSample = (acc / 16.5) * env;
                    if (normSample > 0.96) normSample = 0.96;
                    else if (normSample < -0.96) normSample = -0.96;
                    pcm[baseSample + n] = (short)(normSample * 31000.0);
                }
            }
            return pcm;
        }

        // Demodulate a 48 kHz Mono PCM recording (from .WAV or Live Laptop Mic) back into a GolayDecodeReceipt!
        public GolayDecodeReceipt DemodulateChordPcm(short[] pcm)
        {
            GolayDecodeReceipt fail = new GolayDecodeReceipt();
            if (pcm == null || pcm.Length < SymbolStride * 6)
            {
                fail.StatusSummary = "Audio clip too short for Golay sync preamble.";
                return fail;
            }

            // 1. Coarse-to-fine preamble search for peak SyncBinMarker + Pilot energy across Symbol 0 & 1
            int maxSearch = Math.Min(pcm.Length - SymbolStride * 4, SampleRate * 6);
            int bestStart = 0;
            double bestScore = -1.0;

            for (int pos = 0; pos < maxSearch; pos += 192)
            {
                double s0 = GoertzelMag(pcm, pos + GuardSamples / 2, FftN, SyncBinMarker)
                          + 0.5 * GoertzelMag(pcm, pos + GuardSamples / 2, FftN, PilotBinLow);
                double s1 = GoertzelMag(pcm, pos + SymbolStride + GuardSamples / 2, FftN, SyncBinMarker);
                double s2 = GoertzelMag(pcm, pos + SymbolStride * 2 + GuardSamples / 2, FftN, SyncBinMarker);
                double score = (s0 + s1) - 1.2 * s2; // Preamble ON in sym 0,1 and OFF in sym 2!
                if (score > bestScore) { bestScore = score; bestStart = pos; }
            }

            // Fine-align +-192 samples in steps of 24 samples
            int fineBest = bestStart;
            for (int pos = Math.Max(0, bestStart - 192); pos <= Math.Min(maxSearch, bestStart + 192); pos += 24)
            {
                double s0 = GoertzelMag(pcm, pos + GuardSamples / 2, FftN, SyncBinMarker);
                double s1 = GoertzelMag(pcm, pos + SymbolStride + GuardSamples / 2, FftN, SyncBinMarker);
                double s2 = GoertzelMag(pcm, pos + SymbolStride * 2 + GuardSamples / 2, FftN, SyncBinMarker);
                double score = (s0 + s1) - 1.2 * s2;
                if (score > bestScore) { bestScore = score; fineBest = pos; }
            }

            // 2. Decode Symbol 2 = Golay24 Frame Count!
            int sym2Offset = fineBest + SymbolStride * 2 + GuardSamples / 2;
            if (sym2Offset + FftN >= pcm.Length)
            {
                fail.StatusSummary = "Preamble found near end of buffer; incomplete payload.";
                return fail;
            }

            float[] llrLen = ExtractSymbolSoftLlr24(pcm, sym2Offset);
            int hardLen, healedLen, synLen, corrLen;
            int nDataFrames = GolayLeechCodec.DecodeLeech24Soft(llrLen, out hardLen, out healedLen, out synLen, out corrLen);
            if (nDataFrames < 8 || nDataFrames > 2048)
            {
                fail.StatusSummary = "Invalid Golay frame-count header (" + nDataFrames + ").";
                return fail;
            }

            int availFrames = (pcm.Length - (fineBest + SymbolStride * 3)) / SymbolStride;
            int count = Math.Min(nDataFrames, availFrames);
            if (count < 8)
            {
                fail.StatusSummary = "Clip truncated (" + count + "/" + nDataFrames + " Golay frames present).";
                return fail;
            }

            int[] rxHard = new int[count];
            float[][] rxSoft = new float[count][];
            for (int f = 0; f < count; f++)
            {
                int off = fineBest + SymbolStride * (3 + f) + GuardSamples / 2;
                float[] llr = ExtractSymbolSoftLlr24(pcm, off);
                rxSoft[f] = llr;
                int w = 0;
                for (int k = 0; k < 24; k++) if (llr[k] >= 0.0f) w |= (1 << k);
                rxHard[f] = w;
            }

            PushPcmToWaterfall(pcm, fineBest + SymbolStride * 3);
            return GolayLeechCodec.DecodeGolayFramesToPayload(rxHard, rxSoft, null);
        }

        private float[] ExtractSymbolSoftLlr24(short[] pcm, int offset)
        {
            double pLow = GoertzelMag(pcm, offset, FftN, PilotBinLow);
            double pHigh = GoertzelMag(pcm, offset, FftN, PilotBinHigh);
            double pilotRef = Math.Max(1e-4, 0.5 * (pLow + pHigh));
            // Decision threshold sits cleanly halfway between 0.16 and 1.00 * (pilotRef / 0.95) => ~0.55 * pilotRef
            double thresh = pilotRef * 0.56;

            float[] llr = new float[24];
            for (int k = 0; k < 24; k++)
            {
                double mag = GoertzelMag(pcm, offset, FftN, CarrierBins24[k]);
                double norm = (mag - thresh) / Math.Max(1e-4, pilotRef * 0.44);
                llr[k] = (float)Math.Max(-2.5, Math.Min(2.5, norm));
                LiveCarrierLevels24[k] = (float)Math.Max(0.0, Math.Min(1.2, mag / Math.Max(1e-4, pilotRef)));
            }
            return llr;
        }

        private static double GoertzelMag(short[] pcm, int offset, int count, int bin)
        {
            if (offset < 0 || offset + count > pcm.Length) return 0.0;
            double omega = (2.0 * Math.PI * bin) / FftN;
            double coeff = 2.0 * Math.Cos(omega);
            double s0 = 0.0, s1 = 0.0, s2 = 0.0;
            for (int i = 0; i < count; i++)
            {
                s0 = (pcm[offset + i] / 32768.0) + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            double mag2 = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return Math.Sqrt(Math.Max(0.0, mag2)) / (count * 0.5);
        }

        public void PushPcmToWaterfall(short[] pcm, int startSample)
        {
            if (pcm == null || pcm.Length < FftN) return;
            lock (WaterfallSpec)
            {
                // Shift rows down by 1
                Array.Copy(WaterfallSpec, 0, WaterfallSpec, SpecCols, (SpecRows - 1) * SpecCols);
                int off = Math.Max(0, Math.Min(pcm.Length - FftN, startSample));
                for (int c = 0; c < SpecCols; c++)
                {
                    int bin = 22 + c * 2; // spans bins 22..148 (~515 Hz .. 3468 Hz)
                    double m = GoertzelMag(pcm, off, FftN, bin);
                    WaterfallSpec[c] = (float)Math.Min(1.0, m * 14.0);
                }
            }
        }

        public void PlayChordOverSpeakersAsync(short[] pcm, Action onFinished)
        {
            if (IsTransmitting || pcm == null || pcm.Length == 0) return;
            IsTransmitting = true;
            ModemStatus = "TX LIVE :: Broadcasting 48kHz Leech-24 Harmonic Chord over Speakers...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                IntPtr hwo = IntPtr.Zero;
                AutoResetEvent ev = new AutoResetEvent(false);
                try
                {
                    WAVEFORMATEX fmt = new WAVEFORMATEX();
                    fmt.wFormatTag = WAVE_FORMAT_PCM;
                    fmt.nChannels = 1;
                    fmt.nSamplesPerSec = SampleRate;
                    fmt.wBitsPerSample = 16;
                    fmt.nBlockAlign = 2;
                    fmt.nAvgBytesPerSec = SampleRate * 2u;

                    if (waveOutOpen(out hwo, WAVE_MAPPER, ref fmt, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT) == 0 && hwo != IntPtr.Zero)
                    {
                        int chunkSamples = 4096;
                        byte[] rawBytes = new byte[chunkSamples * 2];
                        GCHandle hBuf = GCHandle.Alloc(rawBytes, GCHandleType.Pinned);
                        WAVEHDR hdr = new WAVEHDR();
                        GCHandle hHdr = GCHandle.Alloc(hdr, GCHandleType.Pinned);
                        try
                        {
                            int pos = 0;
                            while (pos < pcm.Length && IsTransmitting)
                            {
                                int take = Math.Min(chunkSamples, pcm.Length - pos);
                                Array.Clear(rawBytes, 0, rawBytes.Length);
                                Buffer.BlockCopy(pcm, pos * 2, rawBytes, 0, take * 2);

                                PushPcmToWaterfall(pcm, pos);
                                for (int k = 0; k < 24; k++)
                                {
                                    LiveCarrierLevels24[k] = (float)Math.Min(1.0, GoertzelMag(pcm, Math.Min(pcm.Length - FftN, pos), Math.Min(FftN, pcm.Length - pos), CarrierBins24[k]) * 16.0);
                                }

                                WAVEHDR wh = new WAVEHDR();
                                wh.lpData = hBuf.AddrOfPinnedObject();
                                wh.dwBufferLength = (uint)(take * 2);
                                Marshal.StructureToPtr(wh, hHdr.AddrOfPinnedObject(), false);

                                waveOutPrepareHeader(hwo, hHdr.AddrOfPinnedObject(), (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                                ev.Reset();
                                waveOutWrite(hwo, hHdr.AddrOfPinnedObject(), (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                                ev.WaitOne(400);
                                waveOutUnprepareHeader(hwo, hHdr.AddrOfPinnedObject(), (uint)Marshal.SizeOf(typeof(WAVEHDR)));

                                pos += take;
                            }
                        }
                        finally
                        {
                            hHdr.Free();
                            hBuf.Free();
                        }
                    }
                }
                catch { }
                finally
                {
                    if (hwo != IntPtr.Zero) { try { waveOutReset(hwo); waveOutClose(hwo); } catch { } }
                    ev.Close();
                    IsTransmitting = false;
                    ModemStatus = "TX Complete :: Harmonic Air-Gap Chord Broadcast Finished.";
                    if (onFinished != null) { try { onFinished(); } catch { } }
                }
            });
        }

        public void StartMicReceiver()
        {
            if (IsMicListening) return;
            _stopMicFlag = false;
            IsMicListening = true;
            ModemStatus = "MIC RX LIVE :: Listening for 48kHz Golay Harmonic Chord Preamble...";

            _micThread = new Thread(MicWorkerLoop);
            _micThread.IsBackground = true;
            _micThread.Start();
        }

        public void StopMicReceiver()
        {
            _stopMicFlag = true;
            IsMicListening = false;
            ModemStatus = "Mic Air-Gap Receiver Standby.";
        }

        private void MicWorkerLoop()
        {
            IntPtr hwi = IntPtr.Zero;
            AutoResetEvent ev = new AutoResetEvent(false);
            const int BufSamples = 2048;
            const int NumBufs = 4;
            GCHandle[] dataHandles = new GCHandle[NumBufs];
            GCHandle[] hdrHandles = new GCHandle[NumBufs];
            short[][] chunks = new short[NumBufs][];

            // Rolling 8-second capture ring (384,000 samples)
            short[] captureRing = new short[SampleRate * 8];
            int writePos = 0;
            int preambleArmedAt = -1;

            try
            {
                WAVEFORMATEX fmt = new WAVEFORMATEX();
                fmt.wFormatTag = WAVE_FORMAT_PCM;
                fmt.nChannels = 1;
                fmt.nSamplesPerSec = SampleRate;
                fmt.wBitsPerSample = 16;
                fmt.nBlockAlign = 2;
                fmt.nAvgBytesPerSec = SampleRate * 2u;

                if (waveInOpen(out hwi, WAVE_MAPPER, ref fmt, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT) != 0 || hwi == IntPtr.Zero)
                {
                    ModemStatus = "Mic Unavailable (Check Windows Microphone Privacy Settings).";
                    IsMicListening = false;
                    return;
                }

                uint cbHdr = (uint)Marshal.SizeOf(typeof(WAVEHDR));
                for (int b = 0; b < NumBufs; b++)
                {
                    chunks[b] = new short[BufSamples];
                    dataHandles[b] = GCHandle.Alloc(chunks[b], GCHandleType.Pinned);
                    WAVEHDR wh = new WAVEHDR();
                    wh.lpData = dataHandles[b].AddrOfPinnedObject();
                    wh.dwBufferLength = BufSamples * 2u;
                    hdrHandles[b] = GCHandle.Alloc(wh, GCHandleType.Pinned);
                    waveInPrepareHeader(hwi, hdrHandles[b].AddrOfPinnedObject(), cbHdr);
                    waveInAddBuffer(hwi, hdrHandles[b].AddrOfPinnedObject(), cbHdr);
                }

                waveInStart(hwi);
                int nextBuf = 0;

                while (!_stopMicFlag)
                {
                    ev.WaitOne(120);
                    for (int probe = 0; probe < NumBufs; probe++)
                    {
                        IntPtr pHdr = hdrHandles[nextBuf].AddrOfPinnedObject();
                        WAVEHDR cur = (WAVEHDR)Marshal.PtrToStructure(pHdr, typeof(WAVEHDR));
                        if ((cur.dwFlags & WHDR_DONE) == 0u) break;

                        short[] ch = chunks[nextBuf];
                        float pk = 0.0f;
                        for (int i = 0; i < BufSamples; i++)
                        {
                            float v = Math.Abs(ch[i] / 32768.0f);
                            if (v > pk) pk = v;
                            captureRing[writePos % captureRing.Length] = ch[i];
                            writePos++;
                        }
                        MicPeakLevel = MicPeakLevel * 0.65f + pk * 0.35f;
                        PushPcmToWaterfall(ch, 0);

                        double syncMag = GoertzelMag(ch, 0, BufSamples, SyncBinMarker);
                        if (preambleArmedAt < 0 && syncMag > 0.015)
                        {
                            preambleArmedAt = Math.Max(0, writePos - BufSamples * 2);
                            ModemStatus = "MIC RX :: Sync Preamble Detected! Capturing Golay Chord...";
                        }
                        else if (preambleArmedAt >= 0 && (writePos - preambleArmedAt) >= SampleRate * 4)
                        {
                            int len = Math.Min(captureRing.Length, writePos - preambleArmedAt);
                            short[] slice = new short[len];
                            for (int i = 0; i < len; i++)
                                slice[i] = captureRing[(preambleArmedAt + i) % captureRing.Length];
                            preambleArmedAt = -1;

                            GolayDecodeReceipt rec = DemodulateChordPcm(slice);
                            if (rec.Success && OnAirGapDecoded != null)
                            {
                                ModemStatus = "MIC RX DECODED :: " + rec.StatusSummary;
                                OnAirGapDecoded(rec);
                            }
                            else
                            {
                                ModemStatus = "MIC RX Listening (Last burst: " + rec.StatusSummary + ")";
                            }
                        }

                        cur.dwFlags &= ~WHDR_DONE;
                        cur.dwBytesRecorded = 0u;
                        Marshal.StructureToPtr(cur, pHdr, false);
                        waveInAddBuffer(hwi, pHdr, cbHdr);
                        nextBuf = (nextBuf + 1) % NumBufs;
                    }
                }
            }
            catch { }
            finally
            {
                if (hwi != IntPtr.Zero)
                {
                    try { waveInReset(hwi); } catch { }
                    uint cbHdr = (uint)Marshal.SizeOf(typeof(WAVEHDR));
                    for (int b = 0; b < NumBufs; b++)
                    {
                        if (hdrHandles[b].IsAllocated)
                        {
                            try { waveInUnprepareHeader(hwi, hdrHandles[b].AddrOfPinnedObject(), cbHdr); } catch { }
                            hdrHandles[b].Free();
                        }
                        if (dataHandles[b].IsAllocated) dataHandles[b].Free();
                    }
                    try { waveInClose(hwi); } catch { }
                }
                ev.Close();
                IsMicListening = false;
            }
        }

        public static void SaveWavFile(string path, short[] pcm)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int dataBytes = pcm.Length * 2;
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataBytes);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16);
                bw.Write((ushort)1); // PCM
                bw.Write((ushort)1); // Mono
                bw.Write(SampleRate);
                bw.Write(SampleRate * 2);
                bw.Write((ushort)2);
                bw.Write((ushort)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataBytes);
                byte[] buf = new byte[dataBytes];
                Buffer.BlockCopy(pcm, 0, buf, 0, dataBytes);
                bw.Write(buf);
            }
        }

        public static short[] LoadWavFileMono48k(string path)
        {
            byte[] all = File.ReadAllBytes(path);
            if (all.Length < 44) return new short[0];
            int ch = BitConverter.ToUInt16(all, 22);
            int sr = BitConverter.ToInt32(all, 24);
            int bits = BitConverter.ToUInt16(all, 34);
            int idx = 12;
            while (idx + 8 <= all.Length)
            {
                string id = Encoding.ASCII.GetString(all, idx, 4);
                int sz = BitConverter.ToInt32(all, idx + 4);
                if (id == "data")
                {
                    int start = idx + 8;
                    int avail = Math.Min(sz, all.Length - start);
                    int bytesPerFrame = Math.Max(2, ch * (bits / 8));
                    int frames = avail / bytesPerFrame;
                    short[] mono = new short[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        int o = start + f * bytesPerFrame;
                        mono[f] = BitConverter.ToInt16(all, o);
                    }
                    if (sr == SampleRate || sr <= 0) return mono;
                    // Linear resample to 48 kHz if external WAV was 44.1 kHz
                    int outLen = (int)((long)frames * SampleRate / sr);
                    short[] res = new short[outLen];
                    for (int i = 0; i < outLen; i++)
                    {
                        double srcPos = (double)i * sr / SampleRate;
                        int i0 = Math.Min(frames - 1, (int)srcPos);
                        int i1 = Math.Min(frames - 1, i0 + 1);
                        double fr = srcPos - i0;
                        res[i] = (short)(mono[i0] * (1.0 - fr) + mono[i1] * fr);
                    }
                    return res;
                }
                idx += 8 + Math.Max(0, sz);
            }
            return new short[0];
        }

        public void Dispose()
        {
            IsTransmitting = false;
            StopMicReceiver();
        }
    }
}
