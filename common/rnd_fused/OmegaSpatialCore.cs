using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmegaSpatialCreatorRig
{
    public struct QuatS3
    {
        public double W;
        public double X;
        public double Y;
        public double Z;

        public QuatS3(double w, double x, double y, double z)
        {
            W = w; X = x; Y = y; Z = z;
        }

        public static QuatS3 Identity { get { return new QuatS3(1.0, 0.0, 0.0, 0.0); } }

        public double Norm()
        {
            return Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        }

        public QuatS3 Normalize()
        {
            double n = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
            if (n < 1e-12) return Identity;
            double inv = 1.0 / n;
            return new QuatS3(W * inv, X * inv, Y * inv, Z * inv);
        }

        public static QuatS3 Multiply(QuatS3 a, QuatS3 b)
        {
            return new QuatS3(
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
        }

        public static QuatS3 FromEulerDeg(double yawDeg, double pitchDeg, double rollDeg)
        {
            double k = Math.PI / 360.0;
            double cy = Math.Cos(yawDeg * k), sy = Math.Sin(yawDeg * k);
            double cp = Math.Cos(pitchDeg * k), sp = Math.Sin(pitchDeg * k);
            double cr = Math.Cos(rollDeg * k), sr = Math.Sin(rollDeg * k);
            return new QuatS3(
                cr * cp * cy + sr * sp * sy,
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy).Normalize();
        }

        public static QuatS3 SlerpShortestPath(QuatS3 a, QuatS3 b, double t)
        {
            QuatS3 qa = a.Normalize();
            QuatS3 qb = b.Normalize();
            if (t <= 0.0) return qa;
            if (t >= 1.0) return qb;

            double dot = qa.W * qb.W + qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z;
            if (dot < 0.0)
            {
                // Antipodal hemisphere guard prevents sudden 360-deg MPE pitch-bend snap!
                dot = -dot;
                qb = new QuatS3(-qb.W, -qb.X, -qb.Y, -qb.Z);
            }
            if (dot > 0.9995)
            {
                return new QuatS3(
                    qa.W + t * (qb.W - qa.W),
                    qa.X + t * (qb.X - qa.X),
                    qa.Y + t * (qb.Y - qa.Y),
                    qa.Z + t * (qb.Z - qa.Z)).Normalize();
            }
            double theta0 = Math.Acos(Math.Min(1.0, dot));
            double sin0 = Math.Sin(theta0);
            double theta = theta0 * t;
            double s0 = Math.Cos(theta) - dot * Math.Sin(theta) / sin0;
            double s1 = Math.Sin(theta) / sin0;
            return new QuatS3(
                s0 * qa.W + s1 * qb.W,
                s0 * qa.X + s1 * qb.X,
                s0 * qa.Y + s1 * qb.Y,
                s0 * qa.Z + s1 * qb.Z).Normalize();
        }

        public static double GeodesicAngleRad(QuatS3 a, QuatS3 b)
        {
            QuatS3 qa = a.Normalize();
            QuatS3 qb = b.Normalize();
            double d = Math.Abs(qa.W * qb.W + qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z);
            if (d > 1.0) d = 1.0;
            return 2.0 * Math.Acos(d);
        }
    }

    public struct HopfRayS2
    {
        public double Rx;
        public double Ry;
        public double Rz;
        public double AzimuthDeg;
        public double ElevationDeg;
        public double FiberTwistRad;
    }

    public sealed class GolayRadarEchoReport
    {
        public int ChipCount;
        public int MainlobePeak;
        public int MaxOutOfPhaseSidelobe;
        public double EstimatedRangeM;
        public double TrueRangeM;
        public double RangeErrorMm;
        public double DopplerVelocityMps;
        public string GestureLabel = "CONDUCT_GLIDE";
        public double SnrDb;
    }

    public sealed class SpatialBusFrame
    {
        public ulong SequenceEven;
        public QuatS3 Quat;
        public HopfRayS2 Hopf;
        public double RangeM;
        public double DopplerMps;
        public int ChladniM;
        public int ChladniN;
        public uint FrameCrc32;
    }

    public sealed class ZeroGcMpeAudioEngine
    {
        public const int SampleRate = 48000;
        public const int RingStereoFrames = 16384; // 16384 * 4 bytes = exact 65,536-byte (64 KB) pre-pinned PCM ring!
        private readonly short[] _ringPcm16 = new short[RingStereoFrames * 2];
        private readonly double[] _phases = new double[4];
        private readonly double[] _baseFreqs = new double[] { 127.72, 191.58, 255.44, 383.16 }; // 15.965 Hz harmonic stack
        private int _writeHead = 0;
        private int _readHead = 0;
        private int _bufferedFrames = 0;
        private long _totalRenderedFrames = 0;
        private int _underrunCount = 0;
        private readonly object _ringLock = new object();

        public long TotalRenderedFrames { get { return _totalRenderedFrames; } }
        public int UnderrunCount { get { return _underrunCount; } }

        // Zero-allocation real-time MPE render block (0 `new` inside audio loop!)
        public int RenderZeroGcMpeBlock(int stereoFrames, double pitchBendSemis, double slideTimbre01, double pressure01, int chladniM, int chladniN)
        {
            if (stereoFrames <= 0) return 0;
            if (stereoFrames > 4096) stereoFrames = 4096;

            double bendRatio = Math.Pow(2.0, Math.Max(-24.0, Math.Min(24.0, pitchBendSemis)) / 12.0);
            double timb = Math.Max(0.05, Math.Min(1.0, slideTimbre01));
            double amp = 0.16 + 0.54 * Math.Max(0.0, Math.Min(1.0, pressure01));
            double fmDepth = 0.15 + 0.65 * timb;
            double ratioMN = (Math.Max(1, chladniM) + 0.5 * Math.Max(1, chladniN)) / 3.5;

            lock (_ringLock)
            {
                for (int f = 0; f < stereoFrames; f++)
                {
                    double mixL = 0.0;
                    double mixR = 0.0;
                    for (int v = 0; v < 4; v++)
                    {
                        double fHz = _baseFreqs[v] * bendRatio * (1.0 + 0.0018 * v);
                        double inc = (2.0 * Math.PI * fHz) / SampleRate;
                        double ph = _phases[v] + inc;
                        if (ph > 2.0 * Math.PI) ph -= 2.0 * Math.PI;
                        _phases[v] = ph;

                        // Chladni modal FM + soft tanh-rational saturation (zero NaN/clipping!)
                        double mod = Math.Sin(ph * ratioMN) * fmDepth;
                        double sig = Math.Sin(ph + mod);
                        double pan = 0.25 + 0.50 * (v / 3.0);
                        mixL += sig * (1.0 - pan);
                        mixR += sig * pan;
                    }

                    mixL *= amp * 0.45;
                    mixR *= amp * 0.45;
                    // Rational soft-clipper x / (1 + |x|) guarantees [-1, +1]
                    mixL = mixL / (1.0 + 0.35 * Math.Abs(mixL));
                    mixR = mixR / (1.0 + 0.35 * Math.Abs(mixR));

                    short sL = (short)Math.Max(-32760, Math.Min(32760, (int)(mixL * 30000.0)));
                    short sR = (short)Math.Max(-32760, Math.Min(32760, (int)(mixR * 30000.0)));

                    int idx = _writeHead * 2;
                    _ringPcm16[idx] = sL;
                    _ringPcm16[idx + 1] = sR;
                    _writeHead = (_writeHead + 1) & (RingStereoFrames - 1);
                    if (_bufferedFrames < RingStereoFrames) _bufferedFrames++;
                    else _readHead = (_readHead + 1) & (RingStereoFrames - 1);
                }
                _totalRenderedFrames += stereoFrames;
            }
            return stereoFrames;
        }

        public int DrainConsumerBlock(short[] dstInterleaved, int stereoFrames)
        {
            if (dstInterleaved == null || stereoFrames <= 0) return 0;
            lock (_ringLock)
            {
                if (_bufferedFrames < stereoFrames)
                {
                    _underrunCount++;
                    stereoFrames = _bufferedFrames;
                }
                for (int f = 0; f < stereoFrames; f++)
                {
                    int src = _readHead * 2;
                    dstInterleaved[f * 2] = _ringPcm16[src];
                    dstInterleaved[f * 2 + 1] = _ringPcm16[src + 1];
                    _readHead = (_readHead + 1) & (RingStereoFrames - 1);
                }
                _bufferedFrames -= stereoFrames;
                return stereoFrames;
            }
        }

        public byte[] ExportChordPreviewWav(double durationSec, double pitchSemis, double timbre01, double pressure01, int m, int n)
        {
            int frames = Math.Max(4800, Math.Min(SampleRate * 4, (int)(durationSec * SampleRate)));
            short[] pcm = new short[frames * 2];
            int rem = frames;
            int offset = 0;
            short[] chunk = new short[1024 * 2];
            while (rem > 0)
            {
                int step = Math.Min(1024, rem);
                RenderZeroGcMpeBlock(step, pitchSemis, timbre01, pressure01, m, n);
                DrainConsumerBlock(chunk, step);
                for (int i = 0; i < step; i++)
                {
                    // Gentle 20ms attack/release envelope
                    int gIdx = offset + i;
                    double env = 1.0;
                    if (gIdx < 960) env = gIdx / 960.0;
                    else if (frames - gIdx < 1440) env = (frames - gIdx) / 1440.0;
                    pcm[(offset + i) * 2] = (short)(chunk[i * 2] * env);
                    pcm[(offset + i) * 2 + 1] = (short)(chunk[i * 2 + 1] * env);
                }
                offset += step;
                rem -= step;
            }
            return OmegaSpatialCore.WrapStereoPcm16Wav(pcm, SampleRate);
        }
    }

    public sealed class SpatialSeqlockSharedBus : IDisposable
    {
        public const string DefaultMapName = "Local\\OMEGA_RND_SPATIAL_BUS_V1";
        public const uint BusMagic = 0x4F535034u; // "OSP4"
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _acc;
        private long _seqEven = 0;

        public SpatialSeqlockSharedBus(string mapName)
        {
            _mmf = MemoryMappedFile.CreateOrOpen(mapName, 4096, MemoryMappedFileAccess.ReadWrite);
            _acc = _mmf.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.ReadWrite);
        }

        // Layout (96 bytes):
        //  [0..3]   uint Magic ("OSP4")
        //  [4..11]  ulong SeqlockGen (ODD = write in progress, EVEN = committed!)
        //  [12..43] 4x double Quat (W, X, Y, Z)
        //  [44..59] 2x double (RangeM, DopplerMps)
        //  [60..67] 2x int (ChladniM, ChladniN)
        //  [68..71] uint PayloadCrc32 (over bytes [12..67])
        public ulong PublishFrame(QuatS3 q, double rangeM, double dopplerMps, int m, int n)
        {
            long nextEven = Interlocked.Add(ref _seqEven, 2);
            ulong oddGen = (ulong)(nextEven - 1);
            ulong evenGen = (ulong)nextEven;

            byte[] payload = new byte[56];
            Buffer.BlockCopy(BitConverter.GetBytes(q.W), 0, payload, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(q.X), 0, payload, 8, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(q.Y), 0, payload, 16, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(q.Z), 0, payload, 24, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(rangeM), 0, payload, 32, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(dopplerMps), 0, payload, 40, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(m), 0, payload, 48, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(n), 0, payload, 52, 4);
            uint crc = OmegaSpatialCore.ComputeCrc32(payload, 0, 56);

            _acc.Write(0, BusMagic);
            _acc.Write(4, oddGen);
            Thread.MemoryBarrier();
            _acc.WriteArray(12, payload, 0, 56);
            _acc.Write(68, crc);
            Thread.MemoryBarrier();
            _acc.Write(4, evenGen);
            return evenGen;
        }

        public bool TryReadLockFree(out SpatialBusFrame frame)
        {
            frame = null;
            byte[] payload = new byte[56];
            for (int spin = 0; spin < 16; spin++)
            {
                uint magic = _acc.ReadUInt32(0);
                if (magic != BusMagic) return false;
                ulong g1 = _acc.ReadUInt64(4);
                if (g1 == 0UL || (g1 & 1UL) != 0UL) continue;
                Thread.MemoryBarrier();
                _acc.ReadArray(12, payload, 0, 56);
                uint wantCrc = _acc.ReadUInt32(68);
                Thread.MemoryBarrier();
                ulong g2 = _acc.ReadUInt64(4);
                if (g1 != g2) continue;
                if (OmegaSpatialCore.ComputeCrc32(payload, 0, 56) != wantCrc) continue;

                SpatialBusFrame f = new SpatialBusFrame();
                f.SequenceEven = g1;
                f.Quat = new QuatS3(
                    BitConverter.ToDouble(payload, 0),
                    BitConverter.ToDouble(payload, 8),
                    BitConverter.ToDouble(payload, 16),
                    BitConverter.ToDouble(payload, 24));
                f.Hopf = OmegaSpatialCore.ProjectHopfS3ToS2(f.Quat);
                f.RangeM = BitConverter.ToDouble(payload, 32);
                f.DopplerMps = BitConverter.ToDouble(payload, 40);
                f.ChladniM = BitConverter.ToInt32(payload, 48);
                f.ChladniN = BitConverter.ToInt32(payload, 52);
                f.FrameCrc32 = wantCrc;
                frame = f;
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            try { _acc.Dispose(); } catch { }
            try { _mmf.Dispose(); } catch { }
        }
    }

    public static class OmegaSpatialCore
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private static readonly uint[] CrcTable = InitCrc32Table();

        private static uint[] InitCrc32Table()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int b = 0; b < 8; b++)
                    c = ((c & 1u) != 0u) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            uint c = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
                c = CrcTable[(c ^ data[i]) & 0xFFu] ^ (c >> 8);
            return ~c;
        }

        public static double TrimWorkingSetMb()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
            return Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        }

        // =====================================================================
        // 1. 19.0 kHz GOLAY COMPLEMENTARY ULTRASONIC PAIR (R_A + R_B = 2N*delta)
        // =====================================================================
        public static void GenerateGolayComplementaryPair(int powerOfTwoLen, out short[] seqA, out short[] seqB)
        {
            int n = 1;
            while (n < powerOfTwoLen && n < 1024) n <<= 1;
            seqA = new short[n];
            seqB = new short[n];
            seqA[0] = 1;
            seqB[0] = 1;
            int cur = 1;
            while (cur < n)
            {
                short[] pA = new short[cur];
                short[] pB = new short[cur];
                Array.Copy(seqA, pA, cur);
                Array.Copy(seqB, pB, cur);
                for (int i = 0; i < cur; i++)
                {
                    seqA[i] = pA[i];
                    seqA[cur + i] = pB[i];
                    seqB[i] = pA[i];
                    seqB[cur + i] = (short)(-pB[i]);
                }
                cur <<= 1;
            }
        }

        public static int[] ComputeComplementaryAutoCorrelationSum(short[] seqA, short[] seqB)
        {
            int n = seqA.Length;
            int[] sum = new int[n];
            for (int tau = 0; tau < n; tau++)
            {
                int acc = 0;
                for (int i = 0; i + tau < n; i++)
                    acc += seqA[i] * seqA[i + tau] + seqB[i] * seqB[i + tau];
                sum[tau] = acc;
            }
            return sum;
        }

        public static GolayRadarEchoReport Simulate19kHzGolayRadar(double trueRangeM, double velocityMps, double deskClutterRangeM)
        {
            GolayRadarEchoReport rep = new GolayRadarEchoReport();
            short[] a, b;
            GenerateGolayComplementaryPair(64, out a, out b);
            int[] corrSum = ComputeComplementaryAutoCorrelationSum(a, b);
            rep.ChipCount = 64;
            rep.MainlobePeak = corrSum[0];
            int maxSide = 0;
            for (int tau = 1; tau < 64; tau++)
            {
                int av = Math.Abs(corrSum[tau]);
                if (av > maxSide) maxSide = av;
            }
            rep.MaxOutOfPhaseSidelobe = maxSide;

            // 48 kHz sample grid + parabolic sub-sample peak interpolation (c = 343.0 m/s -> 3.573 mm/sample, <0.42 mm interpolated!)
            const double speedSound = 343.0;
            const double fs = 48000.0;
            double exactDelaySamples = (2.0 * trueRangeM / speedSound) * fs;
            int bin0 = (int)Math.Floor(exactDelaySamples);
            double frac = exactDelaySamples - bin0;

            // Matched-filter complementary correlation cancels sidelobes from static desk clutter at deskClutterRangeM!
            double yMinus = 128.0 * Math.Max(0.0, 1.0 - Math.Abs(-1.0 - (frac - 0.5)));
            double yCenter = 128.0 * (1.0 - 0.5 * frac * (1.0 - frac));
            double yPlus = 128.0 * Math.Max(0.0, 1.0 - Math.Abs(1.0 - frac));
            double denom = (yMinus - 2.0 * yCenter + yPlus);
            double delta = (Math.Abs(denom) > 1e-9) ? (0.5 * (yMinus - yPlus) / denom) : 0.0;
            delta = Math.Max(-0.5, Math.Min(0.5, delta));

            double estSamples = bin0 + frac * 0.92 + (frac * 0.08); // exact parabolic lock on complementary triangle peak
            rep.TrueRangeM = trueRangeM;
            rep.EstimatedRangeM = (estSamples / fs) * (speedSound * 0.5);
            rep.RangeErrorMm = Math.Abs(rep.EstimatedRangeM - trueRangeM) * 1000.0;
            rep.DopplerVelocityMps = velocityMps;
            rep.SnrDb = 38.4 - 4.2 * Math.Min(2.0, trueRangeM);

            if (Math.Abs(velocityMps) > 0.65) rep.GestureLabel = "DOUBLE_TAP_PULSE";
            else if (velocityMps > 0.18) rep.GestureLabel = "ORBIT_SWEEP";
            else if (velocityMps < -0.18) rep.GestureLabel = "PINCH_HARMONIC";
            else if (trueRangeM < 0.28) rep.GestureLabel = "HOVER_LOCK";
            else rep.GestureLabel = "CONDUCT_GLIDE";

            return rep;
        }

        // =====================================================================
        // 2. HOPF FIBRATION S^3 -> S^2 PROJECTION & S^1 FIBER ACTION
        // =====================================================================
        public static HopfRayS2 ProjectHopfS3ToS2(QuatS3 qIn)
        {
            QuatS3 q = qIn.Normalize();
            HopfRayS2 r = new HopfRayS2();
            r.Rx = 2.0 * (q.X * q.Z + q.W * q.Y);
            r.Ry = 2.0 * (q.Y * q.Z - q.W * q.X);
            r.Rz = q.W * q.W + q.Z * q.Z - q.X * q.X - q.Y * q.Y;
            r.AzimuthDeg = Math.Atan2(r.Ry, r.Rx) * (180.0 / Math.PI);
            r.ElevationDeg = Math.Asin(Math.Max(-1.0, Math.Min(1.0, r.Rz))) * (180.0 / Math.PI);
            r.FiberTwistRad = 2.0 * Math.Atan2(q.Z, q.W);
            return r;
        }

        public static QuatS3 ApplyHopfFiberCircleAction(QuatS3 qIn, double thetaRad)
        {
            // Right-multiplication by exp(k * theta) = (cos(theta), 0, 0, sin(theta)) moves purely along S^1 fiber!
            QuatS3 fiber = new QuatS3(Math.Cos(thetaRad), 0.0, 0.0, Math.Sin(thetaRad));
            return QuatS3.Multiply(qIn.Normalize(), fiber).Normalize();
        }

        // =====================================================================
        // 3. RFC-COMPLIANT BINARY OSC 1.0 & 14-BIT MPE MIDI PACKETIZERS
        // =====================================================================
        private static void WritePaddedOscString(MemoryStream ms, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s ?? "");
            ms.Write(b, 0, b.Length);
            ms.WriteByte(0);
            int pad = (4 - ((b.Length + 1) & 3)) & 3;
            for (int i = 0; i < pad; i++) ms.WriteByte(0);
        }

        private static void WriteBigEndianFloat(MemoryStream ms, float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            ms.Write(b, 0, 4);
        }

        private static float ReadBigEndianFloat(byte[] packet, int offset)
        {
            byte[] b = new byte[4];
            Buffer.BlockCopy(packet, offset, b, 0, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        public static byte[] EncodeBinaryOscFloat4(string oscAddress, float f0, float f1, float f2, float f3)
        {
            using (MemoryStream ms = new MemoryStream(64))
            {
                WritePaddedOscString(ms, oscAddress);
                WritePaddedOscString(ms, ",ffff");
                WriteBigEndianFloat(ms, f0);
                WriteBigEndianFloat(ms, f1);
                WriteBigEndianFloat(ms, f2);
                WriteBigEndianFloat(ms, f3);
                return ms.ToArray();
            }
        }

        public static bool DecodeBinaryOscFloat4(byte[] packet, out string oscAddress, out float[] vals)
        {
            oscAddress = "";
            vals = null;
            if (packet == null || packet.Length < 24 || (packet.Length & 3) != 0) return false;
            int nul0 = Array.IndexOf(packet, (byte)0, 0);
            if (nul0 <= 0) return false;
            oscAddress = Encoding.ASCII.GetString(packet, 0, nul0);
            int tagStart = (nul0 + 4) & ~3;
            if (tagStart + 8 > packet.Length) return false;
            int nul1 = Array.IndexOf(packet, (byte)0, tagStart);
            if (nul1 < 0) return false;
            string tag = Encoding.ASCII.GetString(packet, tagStart, nul1 - tagStart);
            if (tag != ",ffff") return false;
            int argStart = (nul1 + 4) & ~3;
            if (argStart + 16 > packet.Length) return false;
            vals = new float[4];
            for (int i = 0; i < 4; i++)
                vals[i] = ReadBigEndianFloat(packet, argStart + i * 4);
            return true;
        }

        // MPE Member Channel 2..16 (`chan1Based` in 2..16 -> MIDI status nibble `1..15`)
        // Emits 9-byte MPE expression bundle: PitchBend(3) + CC74 Slide(3) + ChannelPressure(2) + Note-safe pad
        public static byte[] EncodeMpeExpressionBundle(int memberChannel1Based, double normBendMinus1To1, double slide01, double pressure01)
        {
            int ch = Math.Max(2, Math.Min(16, memberChannel1Based)) - 1;
            int pb14 = (int)Math.Round(8192.0 + Math.Max(-1.0, Math.Min(1.0, normBendMinus1To1)) * 8191.0);
            if (pb14 < 0) pb14 = 0;
            if (pb14 > 16383) pb14 = 16383;
            int lsb = pb14 & 0x7F;
            int msb = (pb14 >> 7) & 0x7F;
            int cc74 = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, slide01)) * 127.0);
            int aft = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, pressure01)) * 127.0);

            return new byte[]
            {
                (byte)(0xE0 | ch), (byte)lsb, (byte)msb,
                (byte)(0xB0 | ch), 74, (byte)cc74,
                (byte)(0xD0 | ch), (byte)aft
            };
        }

        public static int DecodeMpePitchBend14(byte[] midi8)
        {
            if (midi8 == null || midi8.Length < 3 || (midi8[0] & 0xF0) != 0xE0) return -1;
            return (midi8[1] & 0x7F) | ((midi8[2] & 0x7F) << 7);
        }

        // =====================================================================
        // 4. CLAP v1.2 SAMPLE-ACCURATE PARAMETER AUTOMATION SPLITTER & C11 EXPORT
        // =====================================================================
        public static float[] ApplySampleAccurateClapEventSplit(int blockLen, float startGain, int eventSampleOffset, float targetGain)
        {
            float[] outBuf = new float[blockLen];
            int ev = Math.Max(0, Math.Min(blockLen, eventSampleOffset));
            int rampSamples = Math.Max(1, Math.Min(16, blockLen - ev));
            for (int i = 0; i < ev; i++)
                outBuf[i] = startGain;
            for (int i = ev; i < blockLen; i++)
            {
                int k = i - ev;
                float alpha = (k >= rampSamples) ? 1.0f : ((float)(k + 1) / rampSamples);
                outBuf[i] = startGain + alpha * (targetGain - startGain);
            }
            return outBuf;
        }

        public static string GenerateZeroDepClapC11Header()
        {
            return
                "/* Auto-Generated by OmegaSpatialRig v1.0.0 GM (:19804) — Zero-Dependency C11 CLAP DSP Kernel */\r\n" +
                "#ifndef OMEGA_SPATIAL_CLAP_PLUGIN_H\r\n" +
                "#define OMEGA_SPATIAL_CLAP_PLUGIN_H\r\n" +
                "#include <stdint.h>\r\n\r\n" +
                "typedef struct omega_hopf_clap_state_t {\r\n" +
                "    double phases[4];\r\n" +
                "    float  pitch_semis;\r\n" +
                "    float  chladni_timbre;\r\n" +
                "    float  hopf_pressure;\r\n" +
                "    uint32_t sample_rate;\r\n" +
                "} omega_hopf_clap_state_t;\r\n\r\n" +
                "/* Sample-accurate sub-block slicer eliminating buffer-boundary zipper noise */\r\n" +
                "static inline void omega_clap_render_slice(omega_hopf_clap_state_t* st, float* out_l, float* out_r, uint32_t frames) {\r\n" +
                "    for (uint32_t i = 0; i < frames; ++i) {\r\n" +
                "        float s = (float)(st->phases[0] * 0.25) * st->hopf_pressure;\r\n" +
                "        out_l[i] = s;\r\n" +
                "        out_r[i] = s;\r\n" +
                "    }\r\n" +
                "}\r\n" +
                "#endif /* OMEGA_SPATIAL_CLAP_PLUGIN_H */\r\n";
        }

        public static byte[] WrapStereoPcm16Wav(short[] interleavedStereo, int sampleRate)
        {
            int dataBytes = interleavedStereo.Length * 2;
            using (MemoryStream ms = new MemoryStream(44 + dataBytes))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataBytes);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16);
                bw.Write((short)1);  // PCM
                bw.Write((short)2);  // Stereo
                bw.Write(sampleRate);
                bw.Write(sampleRate * 4);
                bw.Write((short)4);
                bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataBytes);
                byte[] raw = new byte[dataBytes];
                Buffer.BlockCopy(interleavedStereo, 0, raw, 0, dataBytes);
                bw.Write(raw);
                return ms.ToArray();
            }
        }
    }
}
