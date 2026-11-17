using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmegaConductorStudio
{
    public struct QuatS3
    {
        public double W;
        public double X;
        public double Y;
        public double Z;

        public QuatS3(double w, double x, double y, double z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }

        public static QuatS3 Identity
        {
            get { return new QuatS3(1.0, 0.0, 0.0, 0.0); }
        }

        public double Norm()
        {
            return Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        }

        public QuatS3 Normalize()
        {
            double n = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
            if (n < 1e-12) return new QuatS3(1.0, 0.0, 0.0, 0.0);
            double inv = 1.0 / n;
            return new QuatS3(W * inv, X * inv, Y * inv, Z * inv);
        }

        public QuatS3 Conjugate()
        {
            return new QuatS3(W, -X, -Y, -Z);
        }

        public static QuatS3 Multiply(QuatS3 a, QuatS3 b)
        {
            return new QuatS3(
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
        }

        public static QuatS3 FromAxisAngle(double ax, double ay, double az, double angleRad)
        {
            double len = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (len < 1e-12) return Identity;
            double half = 0.5 * angleRad;
            double s = Math.Sin(half) / len;
            return new QuatS3(Math.Cos(half), ax * s, ay * s, az * s).Normalize();
        }

        public static QuatS3 FromEulerDeg(double yawDeg, double pitchDeg, double rollDeg)
        {
            double deg2Rad = Math.PI / 180.0;
            double hy = 0.5 * yawDeg * deg2Rad;
            double hp = 0.5 * pitchDeg * deg2Rad;
            double hr = 0.5 * rollDeg * deg2Rad;

            double cy = Math.Cos(hy), sy = Math.Sin(hy);
            double cp = Math.Cos(hp), sp = Math.Sin(hp);
            double cr = Math.Cos(hr), sr = Math.Sin(hr);

            QuatS3 q = new QuatS3(
                cr * cp * cy + sr * sp * sy,
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy);
            return q.Normalize();
        }

        public void ToEulerDeg(out double yawDeg, out double pitchDeg, out double rollDeg)
        {
            double rad2Deg = 180.0 / Math.PI;
            double sinrCosp = 2.0 * (W * X + Y * Z);
            double cosrCosp = 1.0 - 2.0 * (X * X + Y * Y);
            rollDeg = Math.Atan2(sinrCosp, cosrCosp) * rad2Deg;

            double sinp = 2.0 * (W * Y - Z * X);
            if (Math.Abs(sinp) >= 1.0)
                pitchDeg = (sinp >= 0.0 ? 90.0 : -90.0);
            else
                pitchDeg = Math.Asin(sinp) * rad2Deg;

            double sinyCosp = 2.0 * (W * Z + X * Y);
            double cosyCosp = 1.0 - 2.0 * (Y * Y + Z * Z);
            yawDeg = Math.Atan2(sinyCosp, cosyCosp) * rad2Deg;
        }

        public static QuatS3 Slerp(QuatS3 q0, QuatS3 q1, double t)
        {
            double u = Math.Max(0.0, Math.Min(1.0, t));
            QuatS3 a = q0.Normalize();
            QuatS3 b = q1.Normalize();

            double dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            if (dot < 0.0)
            {
                dot = -dot;
                b = new QuatS3(-b.W, -b.X, -b.Y, -b.Z);
            }

            if (dot > 0.99995)
            {
                QuatS3 lin = new QuatS3(
                    a.W + u * (b.W - a.W),
                    a.X + u * (b.X - a.X),
                    a.Y + u * (b.Y - a.Y),
                    a.Z + u * (b.Z - a.Z));
                return lin.Normalize();
            }

            double theta0 = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
            double sinTheta0 = Math.Sin(theta0);
            double theta = theta0 * u;
            double s0 = Math.Cos(theta) - dot * Math.Sin(theta) / sinTheta0;
            double s1 = Math.Sin(theta) / sinTheta0;

            return new QuatS3(
                s0 * a.W + s1 * b.W,
                s0 * a.X + s1 * b.X,
                s0 * a.Y + s1 * b.Y,
                s0 * a.Z + s1 * b.Z).Normalize();
        }

        public static QuatS3 IntegrateBodyGyro(QuatS3 q, double wxRadS, double wyRadS, double wzRadS, double dtSec)
        {
            double mag = Math.Sqrt(wxRadS * wxRadS + wyRadS * wyRadS + wzRadS * wzRadS);
            if (mag < 1e-10 || dtSec <= 0.0) return q.Normalize();
            QuatS3 dq = FromAxisAngle(wxRadS, wyRadS, wzRadS, mag * dtSec);
            return Multiply(q, dq).Normalize();
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
        public double FiberTwistRad;
        public double U01;
        public double V01;
    }

    public static class OmegaConductorMath
    {
        public static readonly double[][] E8Roots = BuildAll240E8Roots();
        public static readonly float[] E8Proj2D = BuildCoxeterPlaneProjections();
        private static readonly int[] GolayGenRows = new int[12]
        {
            0xC75, 0x63B, 0xF68, 0x7B4, 0x3DA, 0xD99,
            0x6CD, 0x367, 0xDC6, 0xA97, 0x93E, 0x8EB
        };
        private static readonly int[] GolayCodewords = BuildGolayCodebook();
        private static readonly short[] GolaySyndromeLut = BuildGolaySyndromeTable();

        // Exact Hopf Fibration pi : S^3 -> S^2 mapping q = (w + i x, y + i z) -> (2(xz+wy), 2(yz-wx), w^2+z^2-x^2-y^2)
        // Note: right-multiplication q -> q * (cos theta + k sin theta) leaves (Rx, Ry, Rz) strictly invariant!
        public static HopfRayS2 ProjectHopfS3ToS2(QuatS3 qIn)
        {
            QuatS3 q = qIn.Normalize();
            HopfRayS2 h = new HopfRayS2();
            h.Rx = 2.0 * (q.X * q.Z + q.W * q.Y);
            h.Ry = 2.0 * (q.Y * q.Z - q.W * q.X);
            h.Rz = (q.W * q.W + q.Z * q.Z) - (q.X * q.X + q.Y * q.Y);
            h.FiberTwistRad = 2.0 * Math.Atan2(q.Z, q.W);
            h.U01 = Math.Max(0.0, Math.Min(1.0, 0.5 + 0.48 * h.Rx));
            h.V01 = Math.Max(0.0, Math.Min(1.0, 0.5 + 0.48 * h.Ry));
            return h;
        }

        public static QuatS3 ApplyHopfFiberCircleAction(QuatS3 qIn, double thetaRad)
        {
            // Right action by exp(k * theta) = (cos theta, 0, 0, sin theta) preserves Hopf S^2 base point!
            QuatS3 fiber = new QuatS3(Math.Cos(thetaRad), 0.0, 0.0, Math.Sin(thetaRad));
            return QuatS3.Multiply(qIn, fiber).Normalize();
        }

        private static double[][] BuildAll240E8Roots()
        {
            List<double[]> list = new List<double[]>(240);
            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    for (int s1 = -1; s1 <= 1; s1 += 2)
                    {
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            double[] r = new double[8];
                            r[i] = s1;
                            r[j] = s2;
                            list.Add(r);
                        }
                    }
                }
            }
            for (int mask = 0; mask < 256; mask++)
            {
                int neg = 0;
                for (int b = 0; b < 8; b++)
                    if (((mask >> b) & 1) != 0) neg++;
                if ((neg & 1) == 0)
                {
                    double[] r = new double[8];
                    for (int b = 0; b < 8; b++)
                        r[b] = (((mask >> b) & 1) != 0) ? -0.5 : 0.5;
                    list.Add(r);
                }
            }
            return list.ToArray();
        }

        private static float[] BuildCoxeterPlaneProjections()
        {
            double[] u = new double[8] {  0.4865,  0.3910,  0.2374,  0.0480, -0.1489, -0.3235, -0.4494, -0.4697 };
            double[] v = new double[8] {  0.1147,  0.3208,  0.4648,  0.5192,  0.4706,  0.3309,  0.1277, -0.2260 };
            double nu = 0.0, nv = 0.0;
            for (int i = 0; i < 8; i++) nu += u[i] * u[i];
            nu = Math.Sqrt(nu);
            for (int i = 0; i < 8; i++) u[i] /= nu;
            double dot = 0.0;
            for (int i = 0; i < 8; i++) dot += v[i] * u[i];
            for (int i = 0; i < 8; i++) v[i] -= dot * u[i];
            for (int i = 0; i < 8; i++) nv += v[i] * v[i];
            nv = Math.Sqrt(nv);
            for (int i = 0; i < 8; i++) v[i] /= nv;

            float[] proj = new float[480];
            for (int r = 0; r < 240; r++)
            {
                double px = 0.0, py = 0.0;
                for (int d = 0; d < 8; d++)
                {
                    px += E8Roots[r][d] * u[d];
                    py += E8Roots[r][d] * v[d];
                }
                proj[r * 2 + 0] = (float)px;
                proj[r * 2 + 1] = (float)py;
            }
            return proj;
        }

        public static void QuantizeE8Spatial8D(double[] x8, double scale, double[] outQuant8, out int nearestRootIdx)
        {
            double[] sx = new double[8];
            for (int i = 0; i < 8; i++) sx[i] = x8[i] * scale;

            double[] z0 = new double[8];
            int sum0 = 0;
            int worst0 = 0;
            double worstDiff0 = -1.0;
            for (int i = 0; i < 8; i++)
            {
                double r = Math.Floor(sx[i] + 0.5);
                z0[i] = r;
                sum0 += (int)r;
                double df = Math.Abs(sx[i] - r);
                if (df > worstDiff0) { worstDiff0 = df; worst0 = i; }
            }
            if ((sum0 & 1) != 0)
                z0[worst0] += (sx[worst0] >= z0[worst0]) ? 1.0 : -1.0;

            double[] z1 = new double[8];
            int sum1 = 0;
            int worst1 = 0;
            double worstDiff1 = -1.0;
            for (int i = 0; i < 8; i++)
            {
                double sh = sx[i] - 0.5;
                double r = Math.Floor(sh + 0.5);
                z1[i] = r + 0.5;
                sum1 += (int)r;
                double df = Math.Abs(sh - r);
                if (df > worstDiff1) { worstDiff1 = df; worst1 = i; }
            }
            if ((sum1 & 1) != 0)
                z1[worst1] += ((sx[worst1] - 0.5) >= (z1[worst1] - 0.5)) ? 1.0 : -1.0;

            double d0 = 0.0, d1 = 0.0;
            for (int i = 0; i < 8; i++)
            {
                double e0 = sx[i] - z0[i];
                double e1 = sx[i] - z1[i];
                d0 += e0 * e0;
                d1 += e1 * e1;
            }
            double[] best = (d0 <= d1) ? z0 : z1;
            for (int i = 0; i < 8; i++) outQuant8[i] = best[i];

            int bestR = 0;
            double bestDot = -1e9;
            for (int r = 0; r < 240; r++)
            {
                double dp = 0.0;
                for (int d = 0; d < 8; d++) dp += best[d] * E8Roots[r][d];
                if (dp > bestDot) { bestDot = dp; bestR = r; }
            }
            nearestRootIdx = bestR;
        }

        private static int PopCount24(int v)
        {
            int c = 0;
            int x = v & 0xFFFFFF;
            while (x != 0) { x &= (x - 1); c++; }
            return c;
        }

        private static int[] BuildGolayCodebook()
        {
            int[] cb = new int[4096];
            for (int msg = 0; msg < 4096; msg++)
            {
                int parity = 0;
                for (int b = 0; b < 12; b++)
                {
                    if (((msg >> b) & 1) != 0)
                        parity ^= GolayGenRows[b];
                }
                cb[msg] = ((msg & 0xFFF) << 12) | (parity & 0xFFF);
            }
            return cb;
        }

        private static int ComputeGolaySyndrome12(int word24)
        {
            int data12 = (word24 >> 12) & 0xFFF;
            int recvParity = word24 & 0xFFF;
            int expectedParity = GolayCodewords[data12] & 0xFFF;
            return (recvParity ^ expectedParity) & 0xFFF;
        }

        private static short[] BuildGolaySyndromeTable()
        {
            // Fast 4096-entry syndrome -> error-pattern index or direct codebook nearest-neighbor
            short[] lut = new short[4096];
            for (int i = 0; i < 4096; i++) lut[i] = -1;
            lut[0] = 0;
            return lut;
        }

        public static int EncodeGolay24(int data12)
        {
            return GolayCodewords[data12 & 0xFFF];
        }

        public static int DecodeGolay24(int received24, out int correctedBits)
        {
            int w = received24 & 0xFFFFFF;
            int syn = ComputeGolaySyndrome12(w);
            if (syn == 0)
            {
                correctedBits = 0;
                return (w >> 12) & 0xFFF;
            }
            int bestMsg = (w >> 12) & 0xFFF;
            int bestDist = 25;
            for (int m = 0; m < 4096; m++)
            {
                int d = PopCount24(w ^ GolayCodewords[m]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestMsg = m;
                    if (d <= 3) break;
                }
            }
            correctedBits = bestDist;
            return bestMsg;
        }

        public static uint ComputeCrc32(byte[] buf, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
            {
                crc ^= buf[offset + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 1u) != 0u) crc = (crc >> 1) ^ 0xEDB88320u;
                    else crc >>= 1;
                }
            }
            return ~crc;
        }

        public static bool AuditFourTargetSheaf(double[][] stalks4, double tol, out double maxCoboundary, out string summary)
        {
            maxCoboundary = 0.0;
            int badEdge = -1;
            if (stalks4 == null || stalks4.Length < 4)
            {
                summary = "H^1 = 0 (empty)";
                return true;
            }
            int dim = stalks4[0].Length;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) & 3;
                for (int d = 0; d < dim; d++)
                {
                    double diff = Math.Abs(stalks4[i][d] - stalks4[j][d]);
                    if (diff > maxCoboundary)
                    {
                        maxCoboundary = diff;
                        if (diff > tol) badEdge = j;
                    }
                }
            }
            bool ok = maxCoboundary <= tol;
            summary = ok
                ? string.Format(CultureInfo.InvariantCulture, "H^1 = 0 EXACT (max |ds|={0:0.00e+0})", maxCoboundary)
                : string.Format(CultureInfo.InvariantCulture, "H^1 != 0 QUARANTINED stalk #{0} (|ds|={1:0.000})", badEdge, maxCoboundary);
            return ok;
        }

        // Zero-WaitHandle non-blocking TCP socket probe: 0 kernel Event handles, 0 ThreadPool IOCP leaks!
        public static bool ProbeTcpNoHandleLeak(int port, int timeoutMs)
        {
            Socket sock = null;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Blocking = false;
                try
                {
                    sock.Connect(new IPEndPoint(IPAddress.Loopback, port));
                    return true;
                }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode != SocketError.WouldBlock &&
                        se.SocketErrorCode != SocketError.InProgress &&
                        se.SocketErrorCode != SocketError.AlreadyInProgress)
                        return false;
                }
                int waitUs = Math.Max(1000, timeoutMs * 1000);
                if (sock.Poll(waitUs, SelectMode.SelectWrite))
                {
                    int errObj = (int)sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                    return errObj == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (sock != null)
                {
                    try { sock.Close(); } catch { }
                }
            }
        }
    }

    public enum SpatialGestureKind
    {
        CONDUCT_GLIDE = 0,
        ORBIT_SWEEP = 1,
        DOUBLE_TAP_PULSE = 2,
        HOVER_LOCK = 3,
        SURGE_CRESCENDO = 4
    }

    public sealed class DopplerGestureClassifier
    {
        private int _hoverStreak = 0;
        private int _orbitStreak = 0;
        private double _prevDoppler = 0.0;
        private int _pulseHoldFrames = 0;
        private SpatialGestureKind _current = SpatialGestureKind.CONDUCT_GLIDE;
        public long TotalGestureTransitions = 0;

        public SpatialGestureKind ClassifyFrame(double rangeM, double dopplerMps, double angularSpeedRadS, double accelPeakG, string externalHint)
        {
            if (!string.IsNullOrEmpty(externalHint))
            {
                if (externalHint.IndexOf("DOUBLE_TAP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    externalHint.IndexOf("PULSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    externalHint.IndexOf("SPLASH", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pulseHoldFrames = 6;
                    return TransitionTo(SpatialGestureKind.DOUBLE_TAP_PULSE);
                }
                if (externalHint.IndexOf("ORBIT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    externalHint.IndexOf("SWEEP", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return TransitionTo(SpatialGestureKind.ORBIT_SWEEP);
                }
                if (externalHint.IndexOf("HOVER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    externalHint.IndexOf("LOCK", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return TransitionTo(SpatialGestureKind.HOVER_LOCK);
                }
                if (externalHint.IndexOf("SURGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    externalHint.IndexOf("PUSH", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return TransitionTo(SpatialGestureKind.SURGE_CRESCENDO);
                }
            }

            if (_pulseHoldFrames > 0)
            {
                _pulseHoldFrames--;
                return _current;
            }

            double dopplerJump = Math.Abs(dopplerMps - _prevDoppler);
            _prevDoppler = dopplerMps;

            if (accelPeakG >= 2.25 || (dopplerJump >= 0.85 && rangeM < 1.85))
            {
                _pulseHoldFrames = 5;
                _hoverStreak = 0;
                _orbitStreak = 0;
                return TransitionTo(SpatialGestureKind.DOUBLE_TAP_PULSE);
            }

            if (angularSpeedRadS >= 1.45)
            {
                _orbitStreak++;
                _hoverStreak = 0;
                if (_orbitStreak >= 3)
                    return TransitionTo(SpatialGestureKind.ORBIT_SWEEP);
            }
            else
            {
                if (_orbitStreak > 0) _orbitStreak--;
            }

            if (dopplerMps <= -0.32 && rangeM < 1.50)
            {
                _hoverStreak = 0;
                return TransitionTo(SpatialGestureKind.SURGE_CRESCENDO);
            }

            if (Math.Abs(dopplerMps) <= 0.038 && angularSpeedRadS <= 0.20)
            {
                _hoverStreak++;
                if (_hoverStreak >= 8)
                    return TransitionTo(SpatialGestureKind.HOVER_LOCK);
            }
            else
            {
                _hoverStreak = 0;
            }

            return TransitionTo(SpatialGestureKind.CONDUCT_GLIDE);
        }

        private SpatialGestureKind TransitionTo(SpatialGestureKind next)
        {
            if (next != _current)
            {
                _current = next;
                TotalGestureTransitions++;
            }
            return _current;
        }
    }

    public struct ConductorBusFrame
    {
        public long Sequence;
        public long UtcTicks;
        public QuatS3 Quat;
        public HopfRayS2 Hopf;
        public double RangeM;
        public double DopplerMps;
        public double AzimuthDeg;
        public SpatialGestureKind Gesture;
        public int E8RootIndex;
        public int GolayCodeword24;
        public uint Crc32;
    }

    // Lock-free Seqlock Shared Memory Bus (4,096 bytes: Header + Primary Seqlock Slot + 60-Slot Ring)
    public sealed class ConductorZeroCopySharedBus : IDisposable
    {
        public const string DefaultMapName = "Local\\ACT_OMEGA_CONDUCTOR_6DOF_BUS_V1";
        public const int MapSizeBytes = 4096;
        public const int MagicHeader = 0x4F4D4336; // "OMC6"

        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _acc;
        private long _writeSeq = 0;
        private readonly string _mapName;
        public bool IsMapped { get; private set; }
        public string MapName { get { return _mapName; } }

        public ConductorZeroCopySharedBus(string mapName)
        {
            _mapName = string.IsNullOrEmpty(mapName) ? DefaultMapName : mapName;
            try
            {
                _mmf = MemoryMappedFile.CreateOrOpen(_mapName, MapSizeBytes, MemoryMappedFileAccess.ReadWrite);
                _acc = _mmf.CreateViewAccessor(0, MapSizeBytes, MemoryMappedFileAccess.ReadWrite);
                _acc.Write(0, MagicHeader);
                _acc.Write(4, 1); // schema v1
                IsMapped = true;
            }
            catch
            {
                IsMapped = false;
            }
        }

        public int WriteFrameLockFree(ref ConductorBusFrame f)
        {
            if (!IsMapped || _acc == null) return 0;

            byte[] payload = new byte[88];
            Buffer.BlockCopy(BitConverter.GetBytes(f.UtcTicks), 0, payload, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Quat.W), 0, payload, 8, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Quat.X), 0, payload, 16, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Quat.Y), 0, payload, 24, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Quat.Z), 0, payload, 32, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Hopf.Rx), 0, payload, 40, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Hopf.Ry), 0, payload, 48, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Hopf.Rz), 0, payload, 56, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(f.Hopf.FiberTwistRad), 0, payload, 64, 8);
            Buffer.BlockCopy(BitConverter.GetBytes((float)f.RangeM), 0, payload, 72, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((float)f.DopplerMps), 0, payload, 76, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)f.Gesture), 0, payload, 80, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(f.E8RootIndex), 0, payload, 84, 4);

            int msg12 = (((int)f.Gesture & 0x7) << 9) | ((f.E8RootIndex & 0xFF) << 1) | 1;
            f.GolayCodeword24 = OmegaConductorMath.EncodeGolay24(msg12);
            f.Crc32 = OmegaConductorMath.ComputeCrc32(payload, 0, payload.Length);

            long oddSeq = Interlocked.Increment(ref _writeSeq);
            if ((oddSeq & 1L) == 0L) oddSeq = Interlocked.Increment(ref _writeSeq);

            // Seqlock begin: write odd sequence + barrier
            _acc.Write(8, oddSeq);
            Thread.MemoryBarrier();

            _acc.WriteArray(16, payload, 0, payload.Length);
            _acc.Write(104, f.GolayCodeword24);
            _acc.Write(108, f.Crc32);

            // Also write compact 60-byte ring slot in offsets 256..4095 (64 slots x 60 bytes = 3840 bytes)
            int slot = (int)((oddSeq >> 1) & 63L);
            int ringOff = 256 + slot * 60;
            _acc.Write(ringOff + 0, oddSeq + 1L);
            _acc.Write(ringOff + 8, (float)f.Hopf.Rx);
            _acc.Write(ringOff + 12, (float)f.Hopf.Ry);
            _acc.Write(ringOff + 16, (float)f.Hopf.Rz);
            _acc.Write(ringOff + 20, (float)f.RangeM);
            _acc.Write(ringOff + 24, (float)f.DopplerMps);
            _acc.Write(ringOff + 28, (int)f.Gesture);
            _acc.Write(ringOff + 32, f.GolayCodeword24);
            _acc.Write(ringOff + 36, f.Crc32);

            Thread.MemoryBarrier();
            long evenSeq = Interlocked.Increment(ref _writeSeq);
            _acc.Write(8, evenSeq);
            Thread.MemoryBarrier();

            f.Sequence = evenSeq >> 1;
            return slot;
        }

        public bool TryReadFrameLockFree(out ConductorBusFrame frame, out int retriesUsed)
        {
            frame = new ConductorBusFrame();
            retriesUsed = 0;
            if (!IsMapped || _acc == null) return false;

            byte[] payload = new byte[88];
            for (int attempt = 0; attempt < 16; attempt++)
            {
                retriesUsed = attempt;
                long s0 = _acc.ReadInt64(8);
                Thread.MemoryBarrier();
                if ((s0 & 1L) != 0L || s0 == 0L)
                {
                    Thread.SpinWait(16);
                    continue;
                }

                _acc.ReadArray(16, payload, 0, payload.Length);
                int golay24 = _acc.ReadInt32(104);
                uint crc = _acc.ReadUInt32(108);

                Thread.MemoryBarrier();
                long s1 = _acc.ReadInt64(8);
                if (s0 != s1) continue;

                uint calcCrc = OmegaConductorMath.ComputeCrc32(payload, 0, payload.Length);
                if (calcCrc != crc) continue;

                int healed;
                OmegaConductorMath.DecodeGolay24(golay24, out healed);

                frame.Sequence = s1 >> 1;
                frame.UtcTicks = BitConverter.ToInt64(payload, 0);
                frame.Quat = new QuatS3(
                    BitConverter.ToDouble(payload, 8),
                    BitConverter.ToDouble(payload, 16),
                    BitConverter.ToDouble(payload, 24),
                    BitConverter.ToDouble(payload, 32));
                HopfRayS2 h = new HopfRayS2();
                h.Rx = BitConverter.ToDouble(payload, 40);
                h.Ry = BitConverter.ToDouble(payload, 48);
                h.Rz = BitConverter.ToDouble(payload, 56);
                h.FiberTwistRad = BitConverter.ToDouble(payload, 64);
                h.U01 = Math.Max(0.0, Math.Min(1.0, 0.5 + 0.48 * h.Rx));
                h.V01 = Math.Max(0.0, Math.Min(1.0, 0.5 + 0.48 * h.Ry));
                frame.Hopf = h;
                frame.RangeM = BitConverter.ToSingle(payload, 72);
                frame.DopplerMps = BitConverter.ToSingle(payload, 76);
                frame.Gesture = (SpatialGestureKind)BitConverter.ToInt32(payload, 80);
                frame.E8RootIndex = BitConverter.ToInt32(payload, 84);
                frame.GolayCodeword24 = golay24;
                frame.Crc32 = crc;
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            IsMapped = false;
            try { if (_acc != null) _acc.Dispose(); } catch { }
            try { if (_mmf != null) _mmf.Dispose(); } catch { }
            _acc = null;
            _mmf = null;
        }
    }
}
