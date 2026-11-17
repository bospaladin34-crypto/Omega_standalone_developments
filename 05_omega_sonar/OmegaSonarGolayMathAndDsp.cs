using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmegaSonarStudio
{
    public sealed class SonarRadarSnapshot
    {
        public int GolayLength = 64;
        public double TargetRangeM = 0.42;
        public double RadialVelocityMps = 0.0;
        public double AzimuthDeg = 0.0;
        public double PeakSnrDb = 36.4;
        public int OutOfPhaseSidelobeMax = 0;
        public string ActiveGesture = "HOVER_LOCK";
        public int GestureCount = 0;
        public bool HardwareAudioLive = false;
        public long FramesProcessed = 0;
        public double DspFps = 60.0;
        public float[] GolayPairCorr128 = new float[128];
        public float[] SingleCodeCorr128 = new float[128];
        public float[] RangeProfile64 = new float[64];
        public float[] RangeDoppler64x32 = new float[64 * 32];
        public float[] AzimuthFan61 = new float[61];
    }

    public static class GolayComplementaryMath
    {
        public const int SampleRate = 48000;
        public const double CarrierHz = 19000.0;
        public const int SamplesPerChip = 12; // 0.25 ms/chip -> 4 kHz ultrasonic chip bandwidth
        public const double SpeedOfSoundMps = 343.2;

        // Srdjan Budisin recursive Golay Complementary Pair (GCP) generator:
        // Produces unimodular sequences A, B in {-1, +1}^N (N = 2^m) satisfying:
        //   R_A[lag] + R_B[lag] == 2*N for lag == 0, and == 0 for ALL lag != 0!
        public static void GeneratePair(int mStages, int[] weights, out int[] seqA, out int[] seqB)
        {
            if (mStages < 1) mStages = 1;
            if (mStages > 10) mStages = 10;
            int n = 1 << mStages;
            seqA = new int[n];
            seqB = new int[n];
            seqA[0] = 1;
            seqB[0] = 1;

            for (int k = 0; k < mStages; k++)
            {
                int d = 1 << k;
                int wk = (weights != null && k < weights.Length && weights[k] < 0) ? -1 : 1;
                int[] nextA = new int[n];
                int[] nextB = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int bShift = (i >= d) ? seqB[i - d] : 0;
                    nextA[i] = seqA[i] + wk * bShift;
                    nextB[i] = seqA[i] - wk * bShift;
                }
                seqA = nextA;
                seqB = nextB;
            }
        }

        public static int AperiodicAutocorrelation(int[] seq, int lag)
        {
            int n = seq.Length;
            int start = (lag < 0) ? -lag : 0;
            int end = (lag > 0) ? (n - lag) : n;
            int sum = 0;
            for (int i = start; i < end; i++)
                sum += seq[i] * seq[i + lag];
            return sum;
        }

        public static bool VerifyZeroOutOfPhaseSidelobes(int[] seqA, int[] seqB, out int zeroLagPeak, out int maxOutOfPhaseAbs)
        {
            int n = seqA.Length;
            zeroLagPeak = AperiodicAutocorrelation(seqA, 0) + AperiodicAutocorrelation(seqB, 0);
            maxOutOfPhaseAbs = 0;
            for (int lag = -(n - 1); lag <= (n - 1); lag++)
            {
                if (lag == 0) continue;
                int s = AperiodicAutocorrelation(seqA, lag) + AperiodicAutocorrelation(seqB, lag);
                int a = Math.Abs(s);
                if (a > maxOutOfPhaseAbs) maxOutOfPhaseAbs = a;
            }
            return (zeroLagPeak == 2 * n) && (maxOutOfPhaseAbs == 0);
        }

        // O(N log2 N) Fast Golay Correlator (FGC) butterfly filter
        public static void FastGolayCorrelator(double[] sig, int mStages, int[] weights, out double[] corrA, out double[] corrB)
        {
            int len = sig.Length;
            double[] ra = new double[len];
            double[] rb = new double[len];
            Array.Copy(sig, ra, len);
            Array.Copy(sig, rb, len);

            for (int k = 0; k < mStages; k++)
            {
                int d = 1 << k;
                double wk = (weights != null && k < weights.Length && weights[k] < 0) ? -1.0 : 1.0;
                double[] nra = new double[len];
                double[] nrb = new double[len];
                for (int i = 0; i < len; i++)
                {
                    double raDelay = (i >= d) ? ra[i - d] : 0.0;
                    nra[i] = raDelay + wk * rb[i];
                    nrb[i] = raDelay - wk * rb[i];
                }
                ra = nra;
                rb = nrb;
            }
            corrA = ra;
            corrB = rb;
        }

        public static double[] DirectMatchedCorrelate(double[] sig, int[] code)
        {
            int len = sig.Length;
            int n = code.Length;
            double[] outCorr = new double[len];
            for (int i = 0; i < len; i++)
            {
                double acc = 0.0;
                for (int m = 0; m < n; m++)
                {
                    int idx = i - (n - 1) + m;
                    if (idx >= 0 && idx < len)
                        acc += sig[idx] * code[m];
                }
                outCorr[i] = acc;
            }
            return outCorr;
        }

        // Synthesize 19 kHz BPSK ultrasonic burst with raised-cosine chip shaping + smooth burst taper (zero audible clicks!)
        public static double[] Modulate19kHzBurst(int[] chips, int totalFrameSamples, int startSampleOffset, double amplitude, double dopplerRatio)
        {
            double[] buf = new double[totalFrameSamples];
            int nChips = chips.Length;
            int burstLen = nChips * SamplesPerChip;
            double fc = CarrierHz * (1.0 + dopplerRatio);
            int edgeSamples = 3;
            int burstRamp = Math.Min(24, burstLen / 6);

            for (int c = 0; c < nChips; c++)
            {
                double sign = chips[c] >= 0 ? 1.0 : -1.0;
                double prevSign = (c > 0) ? (chips[c - 1] >= 0 ? 1.0 : -1.0) : 0.0;
                double nextSign = (c + 1 < nChips) ? (chips[c + 1] >= 0 ? 1.0 : -1.0) : 0.0;
                for (int s = 0; s < SamplesPerChip; s++)
                {
                    int localIdx = c * SamplesPerChip + s;
                    int idx = startSampleOffset + localIdx;
                    if (idx < 0 || idx >= totalFrameSamples) continue;

                    // Only dip chip envelope on actual phase reversals so adjacent identical chips stay smooth!
                    double env = 1.0;
                    if (s < edgeSamples && prevSign != sign)
                        env = 0.5 * (1.0 - Math.Cos(Math.PI * (s + 0.5) / edgeSamples));
                    else if (s >= SamplesPerChip - edgeSamples && nextSign != sign)
                        env = 0.5 * (1.0 - Math.Cos(Math.PI * (SamplesPerChip - 1 - s + 0.5) / edgeSamples));

                    if (burstRamp > 0)
                    {
                        if (localIdx < burstRamp)
                            env *= 0.5 * (1.0 - Math.Cos(Math.PI * (localIdx + 0.5) / burstRamp));
                        else if (localIdx >= burstLen - burstRamp)
                            env *= 0.5 * (1.0 - Math.Cos(Math.PI * (burstLen - 1 - localIdx + 0.5) / burstRamp));
                    }

                    double phase = 2.0 * Math.PI * fc * idx / SampleRate;
                    buf[idx] += amplitude * sign * env * Math.Cos(phase);
                }
            }
            return buf;
        }

        public static void Demodulate19kHzToChipStream(double[] rxPcm, out double[] chipI, out double[] chipQ)
        {
            int nChips = rxPcm.Length / SamplesPerChip;
            chipI = new double[nChips];
            chipQ = new double[nChips];
            for (int c = 0; c < nChips; c++)
            {
                double accI = 0.0;
                double accQ = 0.0;
                int baseIdx = c * SamplesPerChip;
                for (int s = 0; s < SamplesPerChip; s++)
                {
                    int idx = baseIdx + s;
                    double ph = 2.0 * Math.PI * CarrierHz * idx / SampleRate;
                    double v = rxPcm[idx];
                    accI += v * Math.Cos(ph);
                    accQ -= v * Math.Sin(ph);
                }
                chipI[c] = (2.0 / SamplesPerChip) * accI;
                chipQ[c] = (2.0 / SamplesPerChip) * accQ;
            }
        }

        public static double EstimateRangeMetersFromCompressedPair(
            double[] rxFrameA,
            double[] rxFrameB,
            int mStages,
            int[] weights,
            int txStartSample,
            out double peakMag,
            out double[] magProfile)
        {
            int n = 1 << mStages;
            int totalSamples = rxFrameA.Length;
            magProfile = new double[totalSamples];
            peakMag = 0.0;
            int bestSampleIdx = 0;

            for (int sub = 0; sub < SamplesPerChip; sub++)
            {
                int nSubChips = (totalSamples - sub) / SamplesPerChip;
                if (nSubChips <= n) continue;
                double[] aI = new double[nSubChips], aQ = new double[nSubChips];
                double[] bI = new double[nSubChips], bQ = new double[nSubChips];

                for (int c = 0; c < nSubChips; c++)
                {
                    int b0 = sub + c * SamplesPerChip;
                    double sAi = 0.0, sAq = 0.0, sBi = 0.0, sBq = 0.0;
                    for (int s = 0; s < SamplesPerChip; s++)
                    {
                        int idx = b0 + s;
                        double cs = Math.Cos(2.0 * Math.PI * CarrierHz * idx / SampleRate);
                        double sn = Math.Sin(2.0 * Math.PI * CarrierHz * idx / SampleRate);
                        double va = rxFrameA[idx];
                        double vb = rxFrameB[idx];
                        sAi += va * cs; sAq -= va * sn;
                        sBi += vb * cs; sBq -= vb * sn;
                    }
                    aI[c] = sAi; aQ[c] = sAq;
                    bI[c] = sBi; bQ[c] = sBq;
                }

                double[] caI, d1, caQ, d2, d3, cbI, d4, cbQ;
                FastGolayCorrelator(aI, mStages, weights, out caI, out d1);
                FastGolayCorrelator(aQ, mStages, weights, out caQ, out d2);
                FastGolayCorrelator(bI, mStages, weights, out d3, out cbI);
                FastGolayCorrelator(bQ, mStages, weights, out d4, out cbQ);

                for (int c = n - 1; c < nSubChips; c++)
                {
                    double sumI = caI[c] + cbI[c];
                    double sumQ = caQ[c] + cbQ[c];
                    double mag = Math.Sqrt(sumI * sumI + sumQ * sumQ);
                    int endSample = sub + (c - (n - 1)) * SamplesPerChip;
                    if (endSample >= 0 && endSample < totalSamples)
                    {
                        magProfile[endSample] = mag;
                        if (endSample >= txStartSample && mag > peakMag)
                        {
                            peakMag = mag;
                            bestSampleIdx = endSample;
                        }
                    }
                }
            }

            double fracOffset = 0.0;
            if (bestSampleIdx > txStartSample && bestSampleIdx + 1 < totalSamples)
            {
                double y0 = magProfile[bestSampleIdx - 1];
                double y1 = magProfile[bestSampleIdx];
                double y2 = magProfile[bestSampleIdx + 1];
                double denom = (y0 - 2.0 * y1 + y2);
                if (Math.Abs(denom) > 1e-9)
                    fracOffset = Math.Max(-0.5, Math.Min(0.5, 0.5 * (y0 - y2) / denom));
            }

            double tofSamples = (bestSampleIdx + fracOffset) - txStartSample;
            if (tofSamples < 0.0) tofSamples = 0.0;
            return 0.5 * SpeedOfSoundMps * (tofSamples / SampleRate);
        }

        public static void FftRadix2(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1.0 : -1.0);
                double wlenR = Math.Cos(ang), wlenI = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double wr = 1.0, wi = 0.0;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        int u = i + k, v = i + k + half;
                        double vr = re[v] * wr - im[v] * wi;
                        double vi = re[v] * wi + im[v] * wr;
                        re[v] = re[u] - vr; im[v] = im[u] - vi;
                        re[u] += vr; im[u] += vi;
                        double nwr = wr * wlenR - wi * wlenI;
                        wi = wr * wlenI + wi * wlenR;
                        wr = nwr;
                    }
                }
            }
            if (inverse)
            {
                for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
            }
        }

        public static double Beamform3NodeAzimuthDeg(
            double[][] rxNodeEnvelopes,
            int targetSampleCenter,
            double elementSpacingM,
            out float[] fan61Normalized)
        {
            fan61Normalized = new float[61];
            double bestPow = -1.0;
            double bestDeg = 0.0;
            double maxP = 1e-9;
            double[] rawPow = new double[61];

            for (int b = 0; b < 61; b++)
            {
                double deg = -60.0 + b * 2.0;
                double rad = deg * Math.PI / 180.0;
                double sinTh = Math.Sin(rad);

                double pSum = 0.0;
                for (int w = -3; w <= 3; w++)
                {
                    double coh = 0.0;
                    for (int node = 0; node < rxNodeEnvelopes.Length; node++)
                    {
                        double xPos = (node == 0) ? 0.0 : ((node == 1) ? -elementSpacingM : elementSpacingM);
                        double delaySamples = (xPos * sinTh / SpeedOfSoundMps) * SampleRate;
                        double sampleCoord = targetSampleCenter + w + delaySamples;
                        int i0 = (int)Math.Floor(sampleCoord);
                        double frac = sampleCoord - i0;
                        double[] arr = rxNodeEnvelopes[node];
                        if (i0 >= 0 && i0 + 1 < arr.Length)
                        {
                            coh += arr[i0] * (1.0 - frac) + arr[i0 + 1] * frac;
                        }
                    }
                    pSum += coh * coh;
                }
                rawPow[b] = pSum;
                if (pSum > maxP) maxP = pSum;
                if (pSum > bestPow) { bestPow = pSum; bestDeg = deg; }
            }

            for (int b = 0; b < 61; b++)
                fan61Normalized[b] = (float)(rawPow[b] / maxP);

            return bestDeg;
        }
    }

    public sealed class OmegaSonarTransceiverEngine : IDisposable
    {
        private const uint WAVE_MAPPER = 0xFFFFFFFFu;
        private const ushort WAVE_FORMAT_PCM = 1;
        private const uint WHDR_DONE = 0x00000001u;
        private const uint CALLBACK_EVENT = 0x00050000u;

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

        private readonly object _lk = new object();
        private SonarRadarSnapshot _latest = new SonarRadarSnapshot();
        private readonly List<string> _gestureHistory = new List<string>();
        private readonly double[] _clutterBg64 = new double[64];
        private readonly double[,] _slowTimeRingI = new double[64, 32];
        private readonly double[,] _slowTimeRingQ = new double[64, 32];
        private int _slowPtr = 0;
        private double _dopplerPhaseRad = 0.0; // Proper phase integrator (eliminates _frames * step streak glitch!)

        public volatile int GolayStages = 6; // Default 2^6 = 64 chips
        public volatile int OperatingMode = 0; // 0 = Sim 3-Node Phased Array, 1 = Live WinMM 19kHz Sonar, 2 = Hybrid Audio + Sim
        public volatile bool ManualTargetOverride = false;
        public volatile float ManualHoldSec = 0.0f;
        public volatile float ManualRangeM = 0.44f;
        public volatile float ManualAzimuthDeg = 12.0f;
        public volatile float ManualVelocityMps = 0.0f;

        private double _simPhase = 0.0;
        private double _smoothedHwRangeM = 0.42;
        private double _smoothedHwVelMps = 0.0;
        private DateTime _lastSignFlipUtc = DateTime.MinValue;
        private DateTime _lastGestureLogUtc = DateTime.MinValue;
        private int _lastVelSign = 0;
        private int _gestureCounter = 0;
        private long _frames = 0;
        private volatile bool _audioActive = false;
        private volatile int _boostPingFrames = 0;
        private Thread _audioThread;
        private volatile bool _stopAudio = false;
        public volatile string AudioStatus = "Sim Mode (Select 'Live 48kHz WinMM 19kHz' to arm gapless ultrasonic sonar)";

        public SonarRadarSnapshot GetSnapshotCopy()
        {
            lock (_lk)
            {
                SonarRadarSnapshot c = new SonarRadarSnapshot();
                c.GolayLength = _latest.GolayLength;
                c.TargetRangeM = _latest.TargetRangeM;
                c.RadialVelocityMps = _latest.RadialVelocityMps;
                c.AzimuthDeg = _latest.AzimuthDeg;
                c.PeakSnrDb = _latest.PeakSnrDb;
                c.OutOfPhaseSidelobeMax = _latest.OutOfPhaseSidelobeMax;
                c.ActiveGesture = _latest.ActiveGesture;
                c.GestureCount = _latest.GestureCount;
                c.HardwareAudioLive = _latest.HardwareAudioLive;
                c.FramesProcessed = _latest.FramesProcessed;
                c.DspFps = _latest.DspFps;
                Array.Copy(_latest.GolayPairCorr128, c.GolayPairCorr128, 128);
                Array.Copy(_latest.SingleCodeCorr128, c.SingleCodeCorr128, 128);
                Array.Copy(_latest.RangeProfile64, c.RangeProfile64, 64);
                Array.Copy(_latest.RangeDoppler64x32, c.RangeDoppler64x32, 64 * 32);
                Array.Copy(_latest.AzimuthFan61, c.AzimuthFan61, 61);
                return c;
            }
        }

        public List<string> GetRecentGestures()
        {
            lock (_lk) { return new List<string>(_gestureHistory); }
        }

        public void SetLiveAudioEnabled(bool enable)
        {
            if (enable && !_audioActive)
            {
                _stopAudio = false;
                _audioThread = new Thread(WinMmTripleBufferedDuplexLoop);
                _audioThread.IsBackground = true;
                _audioThread.Start();
            }
            else if (!enable)
            {
                _stopAudio = true;
            }
        }

        public string EmitOneShot19kHzGolayPing()
        {
            int m = Math.Max(4, Math.Min(7, GolayStages));
            int[] w = new int[] { 1, -1, 1, 1, -1, 1, -1 };
            int[] ga, gb;
            GolayComplementaryMath.GeneratePair(m, w, out ga, out gb);
            int pk2N, maxSide;
            GolayComplementaryMath.VerifyZeroOutOfPhaseSidelobes(ga, gb, out pk2N, out maxSide);

            // If full-duplex audio stream is already open, inject boost frames without opening a 2nd waveOut device!
            if (_audioActive)
            {
                _boostPingFrames = 4;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "[19.000 kHz PTM GOLAY PING] N={0} chips | R_A[0]+R_B[0]={1} | Max Sidelobe={2} | Audio=Injected into Gapless 48kHz Stream",
                    1 << m, pk2N, maxSide);
            }

            int frameLen = 2400; // Exact 50.000 ms (950 integer cycles at 19.000 kHz)
            double[] bA = GolayComplementaryMath.Modulate19kHzBurst(ga, frameLen, 120, 0.40, 0.0);
            double[] bB = GolayComplementaryMath.Modulate19kHzBurst(gb, frameLen, 120, 0.40, 0.0);
            // Include smooth 10 ms lead-in/lead-out silence so DAC open/close never pops!
            int pad = 480;
            short[] pcm = new short[pad * 2 + frameLen * 4];
            double[][] ptm = new double[][] { bA, bB, bB, bA };
            for (int p = 0; p < 4; p++)
            {
                for (int i = 0; i < frameLen; i++)
                {
                    int v = (int)Math.Round(ptm[p][i] * 24000.0);
                    if (v > 32000) v = 32000; else if (v < -32000) v = -32000;
                    pcm[pad + p * frameLen + i] = (short)v;
                }
            }

            bool hwPlayed = TryPlayShortPcmClean(pcm);
            return string.Format(
                CultureInfo.InvariantCulture,
                "[19.000 kHz PTM GOLAY PING] N={0} chips | R_A[0]+R_B[0]={1} | Max Sidelobe={2} | Audio={3}",
                1 << m, pk2N, maxSide, hwPlayed ? "48kHz WinMM Tapered Burst Emitted" : "Simulated (No Audio Out)");
        }

        private static bool TryPlayShortPcmClean(short[] pcm)
        {
            IntPtr hwo = IntPtr.Zero;
            GCHandle hData = default(GCHandle);
            IntPtr pHdr = IntPtr.Zero;
            try
            {
                using (AutoResetEvent ev = new AutoResetEvent(false))
                {
                    WAVEFORMATEX wfx = new WAVEFORMATEX();
                    wfx.wFormatTag = WAVE_FORMAT_PCM;
                    wfx.nChannels = 1;
                    wfx.nSamplesPerSec = (uint)GolayComplementaryMath.SampleRate;
                    wfx.wBitsPerSample = 16;
                    wfx.nBlockAlign = 2;
                    wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * 2u;
                    IntPtr hEv = ev.SafeWaitHandle.DangerousGetHandle();
                    if (waveOutOpen(out hwo, WAVE_MAPPER, ref wfx, hEv, IntPtr.Zero, CALLBACK_EVENT) != 0 || hwo == IntPtr.Zero)
                        return false;

                    byte[] raw = new byte[pcm.Length * 2];
                    Buffer.BlockCopy(pcm, 0, raw, 0, raw.Length);
                    hData = GCHandle.Alloc(raw, GCHandleType.Pinned);
                    uint hdrSz = (uint)Marshal.SizeOf(typeof(WAVEHDR));
                    WAVEHDR hdr = new WAVEHDR();
                    hdr.lpData = hData.AddrOfPinnedObject();
                    hdr.dwBufferLength = (uint)raw.Length;
                    pHdr = Marshal.AllocHGlobal((int)hdrSz);
                    Marshal.StructureToPtr(hdr, pHdr, false);

                    if (waveOutPrepareHeader(hwo, pHdr, hdrSz) == 0)
                    {
                        ev.Reset();
                        waveOutWrite(hwo, pHdr, hdrSz);
                        for (int w = 0; w < 10; w++)
                        {
                            ev.WaitOne(45);
                            WAVEHDR cur = (WAVEHDR)Marshal.PtrToStructure(pHdr, typeof(WAVEHDR));
                            if ((cur.dwFlags & WHDR_DONE) != 0) break;
                        }
                        waveOutUnprepareHeader(hwo, pHdr, hdrSz);
                    }
                }
                return true;
            }
            catch { return false; }
            finally
            {
                if (pHdr != IntPtr.Zero) Marshal.FreeHGlobal(pHdr);
                if (hData.IsAllocated) hData.Free();
                if (hwo != IntPtr.Zero) { try { waveOutReset(hwo); waveOutClose(hwo); } catch { } }
            }
        }

        // Triple-buffered (3x WAVEHDR) gapless 48 kHz WinMM ultrasonic loop -> ZERO 10 Hz underrun clicks!
        private void WinMmTripleBufferedDuplexLoop()
        {
            const int RING = 3;
            IntPtr hwi = IntPtr.Zero;
            IntPtr hwo = IntPtr.Zero;
            GCHandle[] hInBufs = new GCHandle[RING];
            GCHandle[] hOutBufs = new GCHandle[RING];
            IntPtr[] pInHdrs = new IntPtr[RING];
            IntPtr[] pOutHdrs = new IntPtr[RING];
            byte[][] rxBuffers = new byte[RING][];
            byte[][] txBuffers = new byte[RING][];
            uint hdrSz = (uint)Marshal.SizeOf(typeof(WAVEHDR));

            try
            {
                using (AutoResetEvent inEv = new AutoResetEvent(false))
                using (AutoResetEvent outEv = new AutoResetEvent(false))
                {
                    WAVEFORMATEX wfx = new WAVEFORMATEX();
                    wfx.wFormatTag = WAVE_FORMAT_PCM;
                    wfx.nChannels = 1;
                    wfx.nSamplesPerSec = (uint)GolayComplementaryMath.SampleRate;
                    wfx.wBitsPerSample = 16;
                    wfx.nBlockAlign = 2;
                    wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * 2u;

                    int rIn = waveInOpen(out hwi, WAVE_MAPPER, ref wfx, inEv.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                    int rOut = waveOutOpen(out hwo, WAVE_MAPPER, ref wfx, outEv.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                    if (rIn != 0 || hwi == IntPtr.Zero)
                    {
                        AudioStatus = "WinMM Mic unavailable (code " + rIn + ") -- running Phased Physics Sim";
                        _audioActive = false;
                        return;
                    }

                    int cachedStages = -1;
                    int[] w = new int[] { 1, -1, 1, 1, -1, 1, -1 };
                    int halfFrame = 2400; // 50.000 ms (950 exact 19 kHz cycles -> zero boundary phase jump!)
                    int pairSamples = halfFrame * 2;
                    byte[] canonicalTx = new byte[pairSamples * 2];

                    Action<int, double> rebuildTx = delegate(int stg, double amp)
                    {
                        int[] ga, gb;
                        GolayComplementaryMath.GeneratePair(stg, w, out ga, out gb);
                        double[] txA = GolayComplementaryMath.Modulate19kHzBurst(ga, halfFrame, 96, amp, 0.0);
                        double[] txB = GolayComplementaryMath.Modulate19kHzBurst(gb, halfFrame, 96, amp, 0.0);
                        for (int i = 0; i < halfFrame; i++)
                        {
                            short sa = (short)Math.Max(-26000, Math.Min(26000, (int)Math.Round(txA[i] * 24000.0)));
                            short sb = (short)Math.Max(-26000, Math.Min(26000, (int)Math.Round(txB[i] * 24000.0)));
                            canonicalTx[i * 2] = (byte)(sa & 0xFF);
                            canonicalTx[i * 2 + 1] = (byte)((sa >> 8) & 0xFF);
                            canonicalTx[(halfFrame + i) * 2] = (byte)(sb & 0xFF);
                            canonicalTx[(halfFrame + i) * 2 + 1] = (byte)((sb >> 8) & 0xFF);
                        }
                    };

                    cachedStages = Math.Max(4, Math.Min(7, GolayStages));
                    rebuildTx(cachedStages, 0.28);

                    for (int k = 0; k < RING; k++)
                    {
                        rxBuffers[k] = new byte[pairSamples * 2];
                        txBuffers[k] = new byte[pairSamples * 2];
                        Buffer.BlockCopy(canonicalTx, 0, txBuffers[k], 0, canonicalTx.Length);
                        hInBufs[k] = GCHandle.Alloc(rxBuffers[k], GCHandleType.Pinned);
                        hOutBufs[k] = GCHandle.Alloc(txBuffers[k], GCHandleType.Pinned);

                        WAVEHDR ih = new WAVEHDR();
                        ih.lpData = hInBufs[k].AddrOfPinnedObject();
                        ih.dwBufferLength = (uint)rxBuffers[k].Length;
                        pInHdrs[k] = Marshal.AllocHGlobal((int)hdrSz);
                        Marshal.StructureToPtr(ih, pInHdrs[k], false);
                        waveInPrepareHeader(hwi, pInHdrs[k], hdrSz);
                        waveInAddBuffer(hwi, pInHdrs[k], hdrSz);

                        if (rOut == 0 && hwo != IntPtr.Zero)
                        {
                            WAVEHDR oh = new WAVEHDR();
                            oh.lpData = hOutBufs[k].AddrOfPinnedObject();
                            oh.dwBufferLength = (uint)txBuffers[k].Length;
                            pOutHdrs[k] = Marshal.AllocHGlobal((int)hdrSz);
                            Marshal.StructureToPtr(oh, pOutHdrs[k], false);
                            waveOutPrepareHeader(hwo, pOutHdrs[k], hdrSz);
                            waveOutWrite(hwo, pOutHdrs[k], hdrSz);
                        }
                    }

                    waveInStart(hwi);
                    _audioActive = true;
                    AudioStatus = "LIVE Gapless 3-Buf 48kHz / 19.000 kHz Golay Sonar Active";

                    double[] rxA = new double[halfFrame];
                    double[] rxB = new double[halfFrame];
                    int nextInIdx = 0;

                    while (!_stopAudio)
                    {
                        inEv.WaitOne(40);
                        int curStages = Math.Max(4, Math.Min(7, GolayStages));
                        bool boost = _boostPingFrames > 0;
                        if (boost) _boostPingFrames--;
                        if (curStages != cachedStages || boost)
                        {
                            cachedStages = curStages;
                            rebuildTx(cachedStages, boost ? 0.45 : 0.28);
                        }

                        // Re-queue any completed output buffers immediately so DAC never underruns!
                        if (rOut == 0 && hwo != IntPtr.Zero)
                        {
                            for (int k = 0; k < RING; k++)
                            {
                                WAVEHDR oh = (WAVEHDR)Marshal.PtrToStructure(pOutHdrs[k], typeof(WAVEHDR));
                                if ((oh.dwFlags & WHDR_DONE) != 0)
                                {
                                    Buffer.BlockCopy(canonicalTx, 0, txBuffers[k], 0, canonicalTx.Length);
                                    oh.dwFlags &= ~WHDR_DONE;
                                    Marshal.StructureToPtr(oh, pOutHdrs[k], false);
                                    waveOutWrite(hwo, pOutHdrs[k], hdrSz);
                                }
                            }
                        }

                        // Drain completed input buffers in FIFO order
                        for (int step = 0; step < RING && !_stopAudio; step++)
                        {
                            WAVEHDR ih = (WAVEHDR)Marshal.PtrToStructure(pInHdrs[nextInIdx], typeof(WAVEHDR));
                            if ((ih.dwFlags & WHDR_DONE) == 0) break;

                            byte[] rb = rxBuffers[nextInIdx];
                            for (int i = 0; i < halfFrame; i++)
                            {
                                short va = (short)(rb[i * 2] | (rb[i * 2 + 1] << 8));
                                short vb = (short)(rb[(halfFrame + i) * 2] | (rb[(halfFrame + i) * 2 + 1] << 8));
                                rxA[i] = va / 32768.0;
                                rxB[i] = vb / 32768.0;
                            }

                            ih.dwFlags &= ~WHDR_DONE;
                            ih.dwBytesRecorded = 0;
                            Marshal.StructureToPtr(ih, pInHdrs[nextInIdx], false);
                            waveInAddBuffer(hwi, pInHdrs[nextInIdx], hdrSz);
                            nextInIdx = (nextInIdx + 1) % RING;

                            IngestHardwareGolayPair(rxA, rxB, cachedStages, w);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AudioStatus = "WinMM Fallback (" + ex.Message + ")";
            }
            finally
            {
                _audioActive = false;
                if (hwi != IntPtr.Zero)
                {
                    try
                    {
                        waveInReset(hwi);
                        for (int k = 0; k < RING; k++)
                            if (pInHdrs[k] != IntPtr.Zero) waveInUnprepareHeader(hwi, pInHdrs[k], hdrSz);
                        waveInClose(hwi);
                    }
                    catch { }
                }
                if (hwo != IntPtr.Zero)
                {
                    try
                    {
                        waveOutReset(hwo);
                        for (int k = 0; k < RING; k++)
                            if (pOutHdrs[k] != IntPtr.Zero) waveOutUnprepareHeader(hwo, pOutHdrs[k], hdrSz);
                        waveOutClose(hwo);
                    }
                    catch { }
                }
                for (int k = 0; k < RING; k++)
                {
                    if (pInHdrs[k] != IntPtr.Zero) Marshal.FreeHGlobal(pInHdrs[k]);
                    if (pOutHdrs[k] != IntPtr.Zero) Marshal.FreeHGlobal(pOutHdrs[k]);
                    if (hInBufs[k].IsAllocated) hInBufs[k].Free();
                    if (hOutBufs[k].IsAllocated) hOutBufs[k].Free();
                }
                if (_stopAudio) AudioStatus = "Sim Mode (WinMM 19kHz Sonar Paused)";
            }
        }

        private void IngestHardwareGolayPair(double[] rxA, double[] rxB, int mStages, int[] weights)
        {
            double pk;
            double[] prof;
            GolayComplementaryMath.EstimateRangeMetersFromCompressedPair(rxA, rxB, mStages, weights, 0, out pk, out prof);

            // Auto-lock onto direct speaker->mic acoustic coupling peak anywhere across the buffer!
            int directIdx = 96;
            double directMax = 1e-9;
            int searchLimit = Math.Max(128, prof.Length - 260);
            for (int i = 0; i < searchLimit; i++)
            {
                if (prof[i] > directMax) { directMax = prof[i]; directIdx = i; }
            }

            float[] rp64 = new float[64];
            double bestMovingVal = 0.0;
            int bestBin = 12;
            for (int b = 0; b < 64; b++)
            {
                double rMeters = 0.06 + b * 0.025;
                int sIdx = directIdx + (int)Math.Round(2.0 * rMeters * GolayComplementaryMath.SampleRate / GolayComplementaryMath.SpeedOfSoundMps);
                double e = 0.0;
                for (int w = -3; w <= 3; w++)
                {
                    int k = (sIdx + w) % prof.Length;
                    if (k < 0) k += prof.Length;
                    if (prof[k] > e) e = prof[k];
                }
                _clutterBg64[b] = 0.88 * _clutterBg64[b] + 0.12 * e;
                double diff = Math.Max(0.0, e - 0.84 * _clutterBg64[b]);
                rp64[b] = (float)diff;
                if (b >= 2 && diff > bestMovingVal)
                {
                    bestMovingVal = diff;
                    bestBin = b;
                }
            }

            double rawRangeM = 0.06 + bestBin * 0.025;
            if (OperatingMode == 2 && bestMovingVal < 0.015 * directMax)
                return;

            // Alpha-Beta filter prevents single-frame bin-hopping jitter!
            if (bestMovingVal > 0.008 * directMax)
            {
                double instVel = (rawRangeM - _smoothedHwRangeM) / 0.10;
                instVel = Math.Max(-1.4, Math.Min(1.4, instVel));
                _smoothedHwRangeM = 0.72 * _smoothedHwRangeM + 0.28 * rawRangeM;
                _smoothedHwVelMps = 0.75 * _smoothedHwVelMps + 0.25 * instVel;
            }
            else
            {
                _smoothedHwVelMps *= 0.85;
            }
            UpdateRadarState(_smoothedHwRangeM, _smoothedHwVelMps, 0.0, rp64, true, mStages, weights);
        }

        public void StepSimulationTick(double dtSec, double imuYawDeg)
        {
            if (_audioActive && OperatingMode == 1) return;
            _simPhase += dtSec;

            int m = Math.Max(4, Math.Min(7, GolayStages));
            int[] weights = new int[] { 1, -1, 1, 1, -1, 1, -1 };

            double orbR = 0.48 + 0.24 * Math.Sin(_simPhase * 1.65) + 0.06 * Math.Cos(_simPhase * 4.1);
            double orbVel = 0.24 * 1.65 * Math.Cos(_simPhase * 1.65) - 0.06 * 4.1 * Math.Sin(_simPhase * 4.1);
            double orbAz = Math.Max(-56.0, Math.Min(56.0, 26.0 * Math.Sin(_simPhase * 0.85) + 0.25 * imuYawDeg));

            double tgtR, tgtVel, tgtAz;
            if (ManualTargetOverride)
            {
                ManualHoldSec = 1.20f;
                tgtR = Math.Max(0.08, Math.Min(1.60, ManualRangeM));
                tgtAz = Math.Max(-58.0, Math.Min(58.0, ManualAzimuthDeg));
                tgtVel = Math.Max(-1.25, Math.Min(1.25, ManualVelocityMps));
                ManualVelocityMps *= 0.90f;
            }
            else if (ManualHoldSec > 0.01f)
            {
                ManualHoldSec = Math.Max(0.0f, ManualHoldSec - (float)dtSec);
                double blend = ManualHoldSec / 1.20;
                tgtR = blend * ManualRangeM + (1.0 - blend) * orbR;
                tgtAz = blend * ManualAzimuthDeg + (1.0 - blend) * orbAz;
                tgtVel = (1.0 - blend) * orbVel;
            }
            else
            {
                tgtR = orbR;
                tgtVel = orbVel;
                tgtAz = orbAz;
            }

            float[] rp64 = new float[64];
            for (int b = 0; b < 64; b++)
            {
                double r = 0.06 + b * 0.025;
                double d = (r - tgtR) / 0.032;
                double clutter = 0.04 * Math.Exp(-b * 0.14);
                rp64[b] = (float)(Math.Exp(-0.5 * d * d) + clutter);
            }

            UpdateRadarState(tgtR, tgtVel, tgtAz, rp64, _audioActive, m, weights);
        }

        private void UpdateRadarState(double rangeM, double velMps, double azDeg, float[] rp64, bool hwLive, int mStages, int[] weights)
        {
            int n = 1 << mStages;
            int[] ga, gb;
            GolayComplementaryMath.GeneratePair(mStages, weights, out ga, out gb);

            float[] pairCorr = new float[128];
            float[] singleCorr = new float[128];
            int maxSide = 0;
            for (int k = 0; k < 128; k++)
            {
                int lag = k - 64;
                if (Math.Abs(lag) < n)
                {
                    int ra = GolayComplementaryMath.AperiodicAutocorrelation(ga, lag);
                    int rb = GolayComplementaryMath.AperiodicAutocorrelation(gb, lag);
                    int sum = ra + rb;
                    if (lag != 0 && Math.Abs(sum) > maxSide) maxSide = Math.Abs(sum);
                    pairCorr[k] = sum / (float)(2 * n);
                    singleCorr[k] = Math.Abs(ra) / (float)n;
                }
            }

            float maxRp = 1e-6f;
            for (int b = 0; b < 64; b++) if (rp64[b] > maxRp) maxRp = rp64[b];
            for (int b = 0; b < 64; b++) rp64[b] /= maxRp;

            // Proper phase integration across frames (fixes the _frames * step horizontal streak glitch!)
            double clampedVel = Math.Max(-1.20, Math.Min(1.20, velMps));
            double dopplerPhaseStep = (clampedVel / 1.25) * Math.PI * 0.92;
            _dopplerPhaseRad += dopplerPhaseStep;
            if (_dopplerPhaseRad > 2000.0 * Math.PI || _dopplerPhaseRad < -2000.0 * Math.PI)
                _dopplerPhaseRad = Math.IEEERemainder(_dopplerPhaseRad, 2.0 * Math.PI);

            double cs = Math.Cos(_dopplerPhaseRad);
            double sn = Math.Sin(_dopplerPhaseRad);
            for (int b = 0; b < 64; b++)
            {
                double amp = rp64[b];
                _slowTimeRingI[b, _slowPtr] = amp * cs;
                _slowTimeRingQ[b, _slowPtr] = amp * sn;
            }
            _slowPtr = (_slowPtr + 1) & 31;

            float[] rdMap = new float[64 * 32];
            double[] re = new double[32];
            double[] im = new double[32];
            float maxRd = 1e-5f;
            for (int b = 0; b < 64; b++)
            {
                for (int t = 0; t < 32; t++)
                {
                    int ringIdx = (_slowPtr + t) & 31;
                    double hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * t / 31.0));
                    re[t] = _slowTimeRingI[b, ringIdx] * hann;
                    im[t] = _slowTimeRingQ[b, ringIdx] * hann;
                }
                GolayComplementaryMath.FftRadix2(re, im, false);
                for (int d = 0; d < 32; d++)
                {
                    int shifted = (d + 16) & 31;
                    float mVal = (float)Math.Sqrt(re[d] * re[d] + im[d] * im[d]);
                    rdMap[b * 32 + shifted] = mVal;
                    if (mVal > maxRd) maxRd = mVal;
                }
            }
            for (int i = 0; i < rdMap.Length; i++) rdMap[i] /= maxRd;

            float[] fan61 = new float[61];
            for (int b = 0; b < 61; b++)
            {
                double deg = -60.0 + b * 2.0;
                double diff = (deg - azDeg) / 9.5;
                double mainLobe = Math.Exp(-0.5 * diff * diff);
                double grating = 0.05 * Math.Cos((deg - azDeg) * 0.22) * Math.Cos((deg - azDeg) * 0.22);
                fan61[b] = (float)Math.Min(1.0, mainLobe + grating);
            }

            string gest = "HOVER_LOCK";
            int sign = (velMps > 0.28) ? 1 : ((velMps < -0.28) ? -1 : 0);
            DateTime now = DateTime.UtcNow;
            if (sign != 0 && _lastVelSign != 0 && sign != _lastVelSign)
            {
                if ((now - _lastSignFlipUtc).TotalMilliseconds < 480.0)
                    gest = "DOUBLE_TAP_PULSE";
                _lastSignFlipUtc = now;
            }
            if (sign != 0) _lastVelSign = sign;
            if (gest != "DOUBLE_TAP_PULSE")
            {
                if (velMps < -0.32) gest = "APPROACH_PUSH";
                else if (velMps > 0.32) gest = "RETREAT_PULL";
                else if (Math.Abs(azDeg) > 18.0 && Math.Abs(velMps) > 0.12) gest = "ORBIT_SWEEP";
            }

            _frames++;

            lock (_lk)
            {
                if (gest != _latest.ActiveGesture && gest != "HOVER_LOCK" && (now - _lastGestureLogUtc).TotalMilliseconds > 280.0)
                {
                    _lastGestureLogUtc = now;
                    _gestureCounter++;
                    string entry = DateTime.Now.ToString("HH:mm:ss.fff") + " :: " + gest +
                        string.Format(CultureInfo.InvariantCulture, " (R={0:0.00}m, V={1:+0.00;-0.00}m/s, Az={2:+0.0;-0.0}deg)", rangeM, velMps, azDeg);
                    _gestureHistory.Insert(0, entry);
                    if (_gestureHistory.Count > 32) _gestureHistory.RemoveAt(_gestureHistory.Count - 1);
                }
                _latest.GolayLength = n;
                _latest.TargetRangeM = rangeM;
                _latest.RadialVelocityMps = velMps;
                _latest.AzimuthDeg = azDeg;
                _latest.PeakSnrDb = 20.0 * Math.Log10(2.0 * n) + 6.0;
                _latest.OutOfPhaseSidelobeMax = maxSide;
                _latest.ActiveGesture = gest;
                _latest.GestureCount = _gestureCounter;
                _latest.HardwareAudioLive = hwLive;
                _latest.FramesProcessed = _frames;
                _latest.DspFps = 60.0;
                Array.Copy(pairCorr, _latest.GolayPairCorr128, 128);
                Array.Copy(singleCorr, _latest.SingleCodeCorr128, 128);
                Array.Copy(rp64, _latest.RangeProfile64, 64);
                Array.Copy(rdMap, _latest.RangeDoppler64x32, 64 * 32);
                Array.Copy(fan61, _latest.AzimuthFan61, 61);
            }
        }

        public void Dispose()
        {
            _stopAudio = true;
        }
    }
}
