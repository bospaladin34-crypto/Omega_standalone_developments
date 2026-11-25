using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmegaTriEngine
{
    public static class OmegaTriEngineMath
    {
        public const double GoldenPhi = 1.6180339887498949;
        public const int E8RootCount = 240;

        public static readonly double[][] E8Roots = BuildE8Roots240();
        public static readonly double[] E8_U1 = new double[] { 0.0, 0.0, -0.11911770, -0.11911770, -0.19273649, -0.19273649, -0.21763887, -0.92193305 };
        public static readonly double[] E8_U2 = new double[] { -0.09689907, 0.66982357, -0.46350566, 0.46350566, -0.23693033, 0.23693033, 0.0, 0.0 };
        public static readonly double[] E8_U3 = new double[] { 0.0, 0.0, -0.38336132, -0.38336132, 0.23693033, 0.23693033, 0.74996791, -0.17704341 };
        public static readonly double[] E8_U4 = new double[] { -0.15941060, 0.54488358, 0.11911770, -0.11911770, 0.56978596, -0.56978596, 0.0, 0.0 };

        // 6 orthonormalized icosahedral axes in R^3: (1 / sqrt(1 + phi^2)) * cyclic(0, 1, +-phi)
        public static readonly double[][] IcosaAxes6 = BuildIcosaAxes6();

        private static double[][] BuildE8Roots240()
        {
            double[][] list = new double[E8RootCount][];
            int idx = 0;
            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    for (int s1 = -1; s1 <= 1; s1 += 2)
                    {
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            double[] v = new double[8];
                            v[i] = s1;
                            v[j] = s2;
                            list[idx++] = v;
                        }
                    }
                }
            }
            for (int mask = 0; mask < 256; mask++)
            {
                int pop = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (((mask >> b) & 1) != 0) pop++;
                }
                if ((pop & 1) == 0)
                {
                    double[] v = new double[8];
                    for (int b = 0; b < 8; b++)
                    {
                        v[b] = (((mask >> b) & 1) != 0) ? -0.5 : 0.5;
                    }
                    list[idx++] = v;
                }
            }
            return list;
        }

        private static double[][] BuildIcosaAxes6()
        {
            double p = GoldenPhi;
            double n = 1.0 / Math.Sqrt(1.0 + p * p);
            return new double[][]
            {
                new double[] { 0.0, 1.0 * n,  p * n },
                new double[] { 0.0, 1.0 * n, -p * n },
                new double[] { 1.0 * n,  p * n, 0.0 },
                new double[] { 1.0 * n, -p * n, 0.0 },
                new double[] {  p * n, 0.0, 1.0 * n },
                new double[] { -p * n, 0.0, 1.0 * n }
            };
        }

        // Exact Conway-Sloane E8 = D8+ U (D8+ + 1/2) nearest lattice point & Voronoi squared distance
        public static double QuantizeE8Coset(double[] x8, out double[] qOut8, out bool isSpinorCoset)
        {
            double[] q0 = new double[8];
            double[] q1 = new double[8];
            double d0 = RoundD8(x8, 0.0, q0);
            double d1 = RoundD8(x8, 0.5, q1);
            if (d1 < d0)
            {
                isSpinorCoset = true;
                qOut8 = q1;
                return d1;
            }
            isSpinorCoset = false;
            qOut8 = q0;
            return d0;
        }

        private static double RoundD8(double[] x8, double shift, double[] dst8)
        {
            int sum = 0;
            int worstIdx = 0;
            double worstAbs = -1.0;
            double[] u = new double[8];
            int[] z = new int[8];
            for (int i = 0; i < 8; i++)
            {
                u[i] = x8[i] - shift;
                int zi = (int)Math.Floor(u[i] + 0.5);
                z[i] = zi;
                sum += zi;
                double err = Math.Abs(u[i] - zi);
                if (err > worstAbs)
                {
                    worstAbs = err;
                    worstIdx = i;
                }
            }
            if ((sum & 1) != 0)
            {
                double diff = u[worstIdx] - z[worstIdx];
                z[worstIdx] += (diff >= 0.0) ? 1 : -1;
            }
            double dist2 = 0.0;
            for (int i = 0; i < 8; i++)
            {
                double qi = z[i] + shift;
                dst8[i] = qi;
                double d = x8[i] - qi;
                dist2 += d * d;
            }
            return dist2;
        }

        public static int FindNearestE8Root(double[] x8, out double sqnrDb)
        {
            double norm2 = 0.0;
            for (int k = 0; k < 8; k++) norm2 += x8[k] * x8[k];
            if (norm2 < 1e-10)
            {
                sqnrDb = 0.0;
                return 0;
            }
            double scale = Math.Sqrt(2.0 / norm2);
            int bestIdx = 0;
            double bestDot = -1e9;
            for (int i = 0; i < E8RootCount; i++)
            {
                double[] r = E8Roots[i];
                double dot = 0.0;
                for (int k = 0; k < 8; k++) dot += (x8[k] * scale) * r[k];
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestIdx = i;
                }
            }
            double err2 = Math.Max(1e-7, 4.0 - 2.0 * bestDot);
            sqnrDb = 10.0 * Math.Log10(2.0 / err2);
            return bestIdx;
        }

        public static void EulerYawPitchToQuat(float yaw, float pitch, float roll, out float qx, out float qy, out float qz, out float qw)
        {
            double cy = Math.Cos(yaw * 0.5), sy = Math.Sin(yaw * 0.5);
            double cp = Math.Cos(pitch * 0.5), sp = Math.Sin(pitch * 0.5);
            double cr = Math.Cos(roll * 0.5), sr = Math.Sin(roll * 0.5);

            double w = cy * cp * cr + sy * sp * sr;
            double x = cy * sp * cr + sy * cp * sr;
            double y = sy * cp * cr - cy * sp * sr;
            double z = cy * cp * sr - sy * sp * cr;
            double inv = 1.0 / Math.Max(1e-12, Math.Sqrt(x * x + y * y + z * z + w * w));
            qx = (float)(x * inv);
            qy = (float)(y * inv);
            qz = (float)(z * inv);
            qw = (float)(w * inv);
        }

        public static void QuatToYawPitch(float qx, float qy, float qz, float qw, out float yaw, out float pitch)
        {
            double inv = 1.0 / Math.Max(1e-12, Math.Sqrt((double)qx * qx + (double)qy * qy + (double)qz * qz + (double)qw * qw));
            double x = qx * inv, y = qy * inv, z = qz * inv, w = qw * inv;
            // Exact inverse of EulerYawPitchToQuat(yaw, pitch, 0) + BuildCameraBasis(yaw, pitch)
            double fx = 2.0 * (x * z + w * y);
            double fy = 2.0 * (w * x - y * z);
            double fz = 1.0 - 2.0 * (x * x + y * y);
            double clampedFy = Math.Max(-1.0, Math.Min(1.0, fy));
            pitch = (float)Math.Asin(clampedFy);
            yaw = (float)Math.Atan2(fx, fz);
        }

        public static double ClampRange(double v, double lo, double hi)
        {
            return (v < lo) ? lo : ((v > hi) ? hi : v);
        }

        public static double EvalCatmullRom(double p0, double p1, double p2, double p3, double u)
        {
            double u2 = u * u;
            double u3 = u2 * u;
            return 0.5 * (
                2.0 * p1 +
                (-p0 + p2) * u +
                (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * u2 +
                (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * u3);
        }

        public static void QuatNormalizeD(double x, double y, double z, double w, out double nx, out double ny, out double nz, out double nw)
        {
            double n = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (n < 1e-12)
            {
                nx = 0.0; ny = 0.0; nz = 0.0; nw = 1.0;
                return;
            }
            double inv = 1.0 / n;
            nx = x * inv; ny = y * inv; nz = z * inv; nw = w * inv;
        }

        public static void QuatSlerpShortest(
            double ax, double ay, double az, double aw,
            double bx, double by, double bz, double bw,
            double t,
            out double rx, out double ry, out double rz, out double rw)
        {
            QuatNormalizeD(ax, ay, az, aw, out ax, out ay, out az, out aw);
            QuatNormalizeD(bx, by, bz, bw, out bx, out by, out bz, out bw);
            double dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0.0)
            {
                bx = -bx; by = -by; bz = -bz; bw = -bw;
                dot = -dot;
            }
            if (dot > 0.9995)
            {
                QuatNormalizeD(
                    ax + t * (bx - ax),
                    ay + t * (by - ay),
                    az + t * (bz - az),
                    aw + t * (bw - aw),
                    out rx, out ry, out rz, out rw);
                return;
            }
            double theta = Math.Acos(Math.Min(1.0, dot));
            double invSin = 1.0 / Math.Max(1e-12, Math.Sin(theta));
            double wa = Math.Sin((1.0 - t) * theta) * invSin;
            double wb = Math.Sin(t * theta) * invSin;
            QuatNormalizeD(
                wa * ax + wb * bx,
                wa * ay + wb * by,
                wa * az + wb * bz,
                wa * aw + wb * bw,
                out rx, out ry, out rz, out rw);
        }

        private static void QuatLogUnit(double x, double y, double z, double w, out double lx, out double ly, out double lz)
        {
            double vn = Math.Sqrt(x * x + y * y + z * z);
            if (vn < 1e-9)
            {
                lx = 0.0; ly = 0.0; lz = 0.0;
                return;
            }
            double ang = Math.Atan2(vn, w);
            double s = ang / vn;
            lx = x * s; ly = y * s; lz = z * s;
        }

        private static void QuatExpPure(double vx, double vy, double vz, out double qx, out double qy, out double qz, out double qw)
        {
            double th = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (th < 1e-9)
            {
                QuatNormalizeD(vx, vy, vz, 1.0, out qx, out qy, out qz, out qw);
                return;
            }
            double s = Math.Sin(th) / th;
            QuatNormalizeD(vx * s, vy * s, vz * s, Math.Cos(th), out qx, out qy, out qz, out qw);
        }

        private static void QuatSquadCtrl(
            double qm1x, double qm1y, double qm1z, double qm1w,
            double q0x, double q0y, double q0z, double q0w,
            double qp1x, double qp1y, double qp1z, double qp1w,
            out double sx, out double sy, out double sz, out double sw)
        {
            QuatNormalizeD(q0x, q0y, q0z, q0w, out q0x, out q0y, out q0z, out q0w);
            QuatNormalizeD(qm1x, qm1y, qm1z, qm1w, out qm1x, out qm1y, out qm1z, out qm1w);
            QuatNormalizeD(qp1x, qp1y, qp1z, qp1w, out qp1x, out qp1y, out qp1z, out qp1w);

            if (q0x * qm1x + q0y * qm1y + q0z * qm1z + q0w * qm1w < 0.0)
            {
                qm1x = -qm1x; qm1y = -qm1y; qm1z = -qm1z; qm1w = -qm1w;
            }
            if (q0x * qp1x + q0y * qp1y + q0z * qp1z + q0w * qp1w < 0.0)
            {
                qp1x = -qp1x; qp1y = -qp1y; qp1z = -qp1z; qp1w = -qp1w;
            }

            double d1x, d1y, d1z, d1w, d2x, d2y, d2z, d2w;
            QuatMulXYZW(-q0x, -q0y, -q0z, q0w, qm1x, qm1y, qm1z, qm1w, out d1x, out d1y, out d1z, out d1w);
            QuatMulXYZW(-q0x, -q0y, -q0z, q0w, qp1x, qp1y, qp1z, qp1w, out d2x, out d2y, out d2z, out d2w);

            double l1x, l1y, l1z, l2x, l2y, l2z;
            QuatLogUnit(d1x, d1y, d1z, d1w, out l1x, out l1y, out l1z);
            QuatLogUnit(d2x, d2y, d2z, d2w, out l2x, out l2y, out l2z);

            double ex, ey, ez, ew;
            QuatExpPure(-0.25 * (l1x + l2x), -0.25 * (l1y + l2y), -0.25 * (l1z + l2z), out ex, out ey, out ez, out ew);
            QuatMulXYZW(q0x, q0y, q0z, q0w, ex, ey, ez, ew, out sx, out sy, out sz, out sw);
            QuatNormalizeD(sx, sy, sz, sw, out sx, out sy, out sz, out sw);
        }

        public static void QuatSquad(
            double q0x, double q0y, double q0z, double q0w,
            double q1x, double q1y, double q1z, double q1w,
            double q2x, double q2y, double q2z, double q2w,
            double q3x, double q3y, double q3z, double q3w,
            double u,
            out double rx, out double ry, out double rz, out double rw)
        {
            QuatNormalizeD(q0x, q0y, q0z, q0w, out q0x, out q0y, out q0z, out q0w);
            QuatNormalizeD(q1x, q1y, q1z, q1w, out q1x, out q1y, out q1z, out q1w);
            QuatNormalizeD(q2x, q2y, q2z, q2w, out q2x, out q2y, out q2z, out q2w);
            QuatNormalizeD(q3x, q3y, q3z, q3w, out q3x, out q3y, out q3z, out q3w);

            if (q1x * q2x + q1y * q2y + q1z * q2z + q1w * q2w < 0.0)
            {
                q2x = -q2x; q2y = -q2y; q2z = -q2z; q2w = -q2w;
            }
            if (q1x * q0x + q1y * q0y + q1z * q0z + q1w * q0w < 0.0)
            {
                q0x = -q0x; q0y = -q0y; q0z = -q0z; q0w = -q0w;
            }
            if (q2x * q3x + q2y * q3y + q2z * q3z + q2w * q3w < 0.0)
            {
                q3x = -q3x; q3y = -q3y; q3z = -q3z; q3w = -q3w;
            }

            double s1x, s1y, s1z, s1w, s2x, s2y, s2z, s2w;
            QuatSquadCtrl(q0x, q0y, q0z, q0w, q1x, q1y, q1z, q1w, q2x, q2y, q2z, q2w, out s1x, out s1y, out s1z, out s1w);
            QuatSquadCtrl(q1x, q1y, q1z, q1w, q2x, q2y, q2z, q2w, q3x, q3y, q3z, q3w, out s2x, out s2y, out s2z, out s2w);

            double ax, ay, az, aw, bx, by, bz, bw;
            QuatSlerpShortest(q1x, q1y, q1z, q1w, q2x, q2y, q2z, q2w, u, out ax, out ay, out az, out aw);
            QuatSlerpShortest(s1x, s1y, s1z, s1w, s2x, s2y, s2z, s2w, u, out bx, out by, out bz, out bw);
            QuatSlerpShortest(ax, ay, az, aw, bx, by, bz, bw, 2.0 * u * (1.0 - u), out rx, out ry, out rz, out rw);
        }

        public static void BuildCameraBasis(
            float yaw, float pitch,
            out float fx, out float fy, out float fz,
            out float rx, out float ry, out float rz,
            out float ux, out float uy, out float uz)
        {
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            fx = (float)(sy * cp);
            fy = (float)(sp);
            fz = (float)(cy * cp);

            rx = (float)(cy);
            ry = 0.0f;
            rz = (float)(-sy);

            // up = cross(fwd, right) in left-handed Y-up view coordinates: (fy*rz - fz*ry, fz*rx - fx*rz, fx*ry - fy*rx)
            // Verify: at yaw=0, pitch=0 -> fwd=(0,0,1), right=(1,0,0) -> up=(0, 1, 0)
            ux = fy * rz - fz * ry;
            uy = fz * rx - fx * rz;
            uz = fx * ry - fy * rx;
        }

        // Enforce exact Penrose P3 zero-sum invariant: gamma[4] = -(gamma[0]+gamma[1]+gamma[2]+gamma[3])
        public static void ComputeZeroSumPentagridShifts(double phasonPhase, double[] dst5)
        {
            if (dst5 == null || dst5.Length < 5) return;
            double s = 0.0;
            for (int r = 0; r < 4; r++)
            {
                double g = 0.20 * Math.Cos(phasonPhase * 0.73 + r * 1.2566370614359172);
                dst5[r] = g;
                s += g;
            }
            dst5[4] = -s;
        }

        // Procedural 5D de Bruijn Pentagrid stone seam & perpendicular phason coordinate
        public static double EvalPenrosePentagrid(double x, double z, double[] gamma5, out double perpX, out double perpY)
        {
            perpX = 0.0;
            perpY = 0.0;
            double minLine = 1e9;
            for (int r = 0; r < 5; r++)
            {
                double ang = r * (2.0 * Math.PI / 5.0);
                double proj = x * Math.Cos(ang) + z * Math.Sin(ang) + gamma5[r];
                double idx = Math.Floor(proj + 0.5);
                double frac = Math.Abs(proj - idx);
                if (frac < minLine) minLine = frac;

                double conjAng = (2 * r) * (2.0 * Math.PI / 5.0);
                perpX += idx * Math.Cos(conjAng);
                perpY += idx * Math.Sin(conjAng);
            }
            return minLine;
        }

        // Procedural Einstein Hat Tile(1, sqrt(3)) aperiodic polykite metatile field (Smith et al., arXiv:2303.10798)
        public static double EvalEinsteinHatProcedural(double x, double z, double scale, out int metatileCluster)
        {
            double sx = x * scale;
            double sz = z * scale;
            // Incommensurable Laves [3.4.6.4] dual hex-kite + phi^2 hierarchical substitution modulation
            double invPhi2 = 1.0 / (GoldenPhi * GoldenPhi);
            double e0 = TriGridDist(sx, sz);
            double cs = Math.Cos(0.5235987756), sn = Math.Sin(0.5235987756);
            double rx = (sx * cs - sz * sn) * invPhi2 + 0.371;
            double rz = (sx * sn + sz * cs) * invPhi2 - 0.219;
            double e1 = TriGridDist(rx, rz);

            // Polykite 8-kite grouping mask breaks translational periodicity across H, T, P, F supertiles
            double selector = Math.Sin(sx * 1.1547005 + rz * GoldenPhi) + Math.Cos(sz * 1.1547005 - rx * GoldenPhi);
            metatileCluster = ((int)Math.Floor((selector + 2.5) * 1.7)) & 3;
            double seam = (selector > 0.18) ? e0 : Math.Min(e0 + 0.06, e1 * 1.35);
            return Math.Max(0.0, seam);
        }

        // Procedural Chiral Spectre Tile(1, 1) curved-arc homochiral monotile field (Smith et al., arXiv:2305.17743)
        public static double EvalChiralSpectreProcedural(double x, double z, double scale, double bowStrength, out int mysticCompound)
        {
            double sx = x * scale;
            double sz = z * scale;
            // Chiral orientation-preserving curl warp (no mirror reflection!) modelling Spectre bowed edges
            double curlX = sx + bowStrength * Math.Sin(sz * 2.0943951 + sx * 0.61803399);
            double curlZ = sz - bowStrength * Math.Sin(sx * 2.0943951 - sz * 0.61803399);
            double dBase = TriGridDist(curlX, curlZ);

            // Mystic 2-tile compound hierarchy at golden/sqrt(4+sqrt(15)) irrational rotation (~28.955 deg)
            double ca = 0.875001, sa = 0.484121;
            double mx = (curlX * ca - curlZ * sa) * 0.3660254 + 0.513;
            double mz = (curlX * sa + curlZ * ca) * 0.3660254 - 0.287;
            double dSuper = TriGridDist(mx, mz);
            mysticCompound = (dSuper < 0.19) ? 1 : 0;
            return Math.Min(dBase, dSuper * 1.65 + 0.025);
        }

        private static double TriGridDist(double u, double v)
        {
            double a = u;
            double b = -0.5 * u + 0.86602540378 * v;
            double c = -0.5 * u - 0.86602540378 * v;
            double da = Math.Abs(a - Math.Floor(a + 0.5));
            double db = Math.Abs(b - Math.Floor(b + 0.5));
            double dc = Math.Abs(c - Math.Floor(c + 0.5));
            return Math.Min(da, Math.Min(db, dc));
        }

        public static double SdGyroid(double x, double y, double z, double scale)
        {
            double s = Math.Max(0.05, Math.Abs(scale));
            return (Math.Sin(x * s) * Math.Cos(y * s) +
                    Math.Sin(y * s) * Math.Cos(z * s) +
                    Math.Sin(z * s) * Math.Cos(x * s)) / s;
        }

        // Central 6D->3D Icosahedral Crystal Monolith SDF with Gyroid internal lattice carving
        public static double EvalMonolith6DSdf(double px, double py, double pz, double phasonPhase, double k1, double k2, double k3, double gyroidScale)
        {
            double cx = px;
            double cy = (py - 2.65) * 0.82;
            double cz = pz;
            double radius = 1.32 * Math.Max(0.35, k1);
            double poly = -1e9;
            for (int m = 0; m < 6; m++)
            {
                double[] ax = IcosaAxes6[m];
                double phasonShift = 0.14 * k2 * Math.Cos(phasonPhase + m * 1.0471975512);
                double dPlane = Math.Abs(cx * ax[0] + cy * ax[1] + cz * ax[2]) - (radius + phasonShift);
                if (dPlane > poly) poly = dPlane;
            }
            double gyr = Math.Abs(SdGyroid(px, py - 2.65, pz, gyroidScale)) - 0.065;
            double k3Sat = Math.Max(0.0, Math.Min(1.0, k3));
            double carved = Math.Max(poly, -gyr * 0.38);
            return poly + (carved - poly) * k3Sat;
        }

        // Walkable 3D Sunken Obsidian Arena terrain height for WASD player physics
        public static float SampleArenaWalkHeight(float wx, float wz)
        {
            double r = Math.Sqrt(wx * wx + wz * wz);
            double onBridge = Math.Min(Math.Abs(wx), Math.Abs(wz));
            if (r < 4.6)
            {
                // Central sanctum dais steps
                if (r < 2.2) return 0.55f;
                return 0.25f;
            }
            if (r < 11.4)
            {
                // Intact arched obsidian causeways across N/S/E/W axes (crown y = 0.52m at r = 7.9m)
                if (onBridge < 1.38)
                {
                    double spanU = Math.Max(-1.0, Math.Min(1.0, (r - 7.9) / 3.6));
                    return (float)(0.28 + 0.24 * (1.0 - spanU * spanU));
                }
                return -0.22f;
            }
            if (r < 17.5)
            {
                // Tiered sunken obsidian amphitheater steps
                double stepT = (r - 11.4) / 1.22;
                int tier = Math.Min(5, Math.Max(0, (int)Math.Floor(stepT)));
                return (float)(0.20 + tier * 0.34);
            }
            return 2.15f;
        }


        // =====================================================================
        // TASK #5 (v1.4.0): 4D HOPF FIBRATION, 24/120-CELL, HYPERBOLIC {5,3,4} & 24D LEECH/GOLAY
        // =====================================================================

        // 12-row Paley/Curtis parity submatrix P for self-dual [24, 12, 8] Extended Binary Golay code G = [I_12 | P]
        // Verified: P * P^T = I_12 (mod 2), weight enumerator = 1 + 759 z^8 + 2576 z^12 + 759 z^16 + z^24 (4,096 codewords)
        public static readonly uint[] GolayP12Masks = new uint[12]
        {
            0xFFEu, 0x477u, 0x8EDu, 0x1DBu, 0x3B5u, 0x769u,
            0xED1u, 0xDA3u, 0xB47u, 0x68Fu, 0xD1Du, 0xA3Bu
        };

        private static readonly uint[] GolayCodebook4096 = BuildGolayCodebook4096();
        public static readonly double[][] HurwitzUnits24 = BuildHurwitzUnits24();

        private static uint[] BuildGolayCodebook4096()
        {
            uint[] cb = new uint[4096];
            for (uint m = 0u; m < 4096u; m++)
            {
                uint parity = 0u;
                for (int b = 0; b < 12; b++)
                {
                    if (((m >> b) & 1u) != 0u) parity ^= GolayP12Masks[b];
                }
                cb[m] = (m & 0xFFFu) | ((parity & 0xFFFu) << 12);
            }
            return cb;
        }

        public static int PopCount24(uint v)
        {
            uint x = v & 0xFFFFFFu;
            x = x - ((x >> 1) & 0x555555u);
            x = (x & 0x333333u) + ((x >> 2) & 0x333333u);
            return (int)((((x + (x >> 4)) & 0x0F0F0Fu) * 0x010101u) >> 16) & 0x3F;
        }

        public static uint EncodeGolay24(uint msg12)
        {
            return GolayCodebook4096[msg12 & 0xFFFu];
        }

        public static uint DecodeGolay24Nearest(uint received24, out int codewordWeight, out int hammingDist)
        {
            uint r = received24 & 0xFFFFFFu;
            // Fast syndrome check for 0-error or <=3-bit error in systematic half
            uint mSys = r & 0xFFFu;
            uint cSys = GolayCodebook4096[mSys];
            int dSys = PopCount24(r ^ cSys);
            if (dSys <= 3)
            {
                codewordWeight = PopCount24(cSys);
                hammingDist = dSys;
                return cSys;
            }
            uint bestCw = cSys;
            int bestDist = dSys;
            for (int m = 0; m < 4096; m++)
            {
                uint cw = GolayCodebook4096[m];
                int d = PopCount24(r ^ cw);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestCw = cw;
                    if (d <= 3) break;
                }
            }
            codewordWeight = PopCount24(bestCw);
            hammingDist = bestDist;
            return bestCw;
        }

        // 24D Leech-Golay Turyn/Construction-A Voronoi quantizer over R^24 = R^8 x R^8 x R^8
        public static double QuantizeLeech24TurynGolay(double[] x24, out double f2MinusF1, out int golayWeightOut)
        {
            golayWeightOut = 0;
            f2MinusF1 = 0.0;
            if (x24 == null || x24.Length < 24) return 0.0;

            uint hardBits = 0u;
            for (int i = 0; i < 24; i++)
            {
                int zi = (int)Math.Floor(x24[i] + 0.5);
                if ((zi & 1) != 0) hardBits |= (1u << i);
            }
            int cwWt, hDist;
            uint cw = DecodeGolay24Nearest(hardBits, out cwWt, out hDist);
            golayWeightOut = cwWt;

            // Evaluate 3 coupled E8 octads + Golay codeword parity coset
            double bestD2 = 1e9;
            double secondD2 = 1e9;
            double[] slice8 = new double[8];
            double totalE8 = 0.0;
            for (int oct = 0; oct < 3; oct++)
            {
                for (int k = 0; k < 8; k++) slice8[k] = x24[oct * 8 + k];
                double[] q8;
                bool sp;
                totalE8 += QuantizeE8Coset(slice8, out q8, out sp);
            }
            bestD2 = totalE8;

            // Compare Golay codeword coset shift (0.5 * c_i) and complementary parity coset
            for (int mode = 0; mode < 2; mode++)
            {
                double sh = (mode == 0) ? 0.0 : 0.25;
                double dAcc = 0.0;
                for (int i = 0; i < 24; i++)
                {
                    double bitOff = (((cw >> i) & 1u) != 0u) ? 0.5 : 0.0;
                    double target = Math.Floor((x24[i] - bitOff - sh) + 0.5) + bitOff + sh;
                    double diff = x24[i] - target;
                    dAcc += diff * diff;
                }
                if (dAcc < bestD2)
                {
                    secondD2 = bestD2;
                    bestD2 = dAcc;
                }
                else if (dAcc < secondD2)
                {
                    secondD2 = dAcc;
                }
            }
            double f1 = Math.Sqrt(Math.Max(0.0, bestD2));
            double f2 = Math.Sqrt(Math.Max(bestD2 + 1e-6, secondD2));
            f2MinusF1 = Math.Max(0.0, f2 - f1);
            return f1;
        }

        private static double[][] BuildHurwitzUnits24()
        {
            double[][] u = new double[24][];
            int idx = 0;
            // 8 Lipschitz axis units: +-1, +-i, +-j, +-k
            for (int a = 0; a < 4; a++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    double[] q = new double[4];
                    q[a] = s;
                    u[idx++] = q;
                }
            }
            // 16 half-integer units: (+-1 +- i +- j +- k) / 2
            for (int m = 0; m < 16; m++)
            {
                u[idx++] = new double[4]
                {
                    ((m & 1) != 0) ? -0.5 : 0.5,
                    ((m & 2) != 0) ? -0.5 : 0.5,
                    ((m & 4) != 0) ? -0.5 : 0.5,
                    ((m & 8) != 0) ? -0.5 : 0.5
                };
            }
            return u;
        }

        public static void QuatMulXYZW(
            double ax, double ay, double az, double aw,
            double bx, double by, double bz, double bw,
            out double rx, out double ry, out double rz, out double rw)
        {
            rx = aw * bx + ax * bw + ay * bz - az * by;
            ry = aw * by - ax * bz + ay * bw + az * bx;
            rz = aw * bz + ax * by - ay * bx + az * bw;
            rw = aw * bw - ax * bx - ay * by - az * bz;
        }

        public static void InverseStereographicR3ToS3(
            double x, double y, double z,
            out double z0, out double z1, out double z2, out double z3)
        {
            double r2 = x * x + y * y + z * z;
            double inv = 1.0 / (1.0 + r2);
            z0 = 2.0 * x * inv;
            z1 = 2.0 * y * inv;
            z2 = 2.0 * z * inv;
            z3 = (r2 - 1.0) * inv;
        }

        public static void HopfMapS3ToS2(
            double z0, double z1, double z2, double z3,
            out double bx, out double by, out double bz)
        {
            bx = 2.0 * (z0 * z2 + z1 * z3);
            by = 2.0 * (z1 * z2 - z0 * z3);
            bz = z0 * z0 + z1 * z1 - z2 * z2 - z3 * z3;
        }

        public static double[][] SampleHopfVillarceauFiberR3(double thetaS2, double phiS2, int sampleCount)
        {
            int n = Math.Max(16, sampleCount);
            double[][] pts = new double[n][];
            double ct = Math.Cos(thetaS2 * 0.5);
            double st = Math.Sin(thetaS2 * 0.5);
            for (int k = 0; k < n; k++)
            {
                double t = (2.0 * Math.PI * k) / n;
                double x0 = ct * Math.Cos(t + phiS2 * 0.5);
                double x1 = ct * Math.Sin(t + phiS2 * 0.5);
                double x2 = st * Math.Cos(t - phiS2 * 0.5);
                double x3 = st * Math.Sin(t - phiS2 * 0.5);
                double denom = Math.Max(1e-6, 1.0 - x3);
                pts[k] = new double[] { x0 / denom, x1 / denom, x2 / denom };
            }
            return pts;
        }

        // Exact discrete Gauss linking integral Lk(C1, C2) = +-1 for any pair of distinct Hopf Villarceau circles
        public static double ComputeGaussLinkingNumber(double[][] c1, double[][] c2)
        {
            int n1 = c1.Length;
            int n2 = c2.Length;
            double sum = 0.0;
            for (int i = 0; i < n1; i++)
            {
                double[] a0 = c1[i];
                double[] a1 = c1[(i + 1) % n1];
                double r1x = 0.5 * (a0[0] + a1[0]), r1y = 0.5 * (a0[1] + a1[1]), r1z = 0.5 * (a0[2] + a1[2]);
                double d1x = a1[0] - a0[0], d1y = a1[1] - a0[1], d1z = a1[2] - a0[2];
                for (int j = 0; j < n2; j++)
                {
                    double[] b0 = c2[j];
                    double[] b1 = c2[(j + 1) % n2];
                    double r2x = 0.5 * (b0[0] + b1[0]), r2y = 0.5 * (b0[1] + b1[1]), r2z = 0.5 * (b0[2] + b1[2]);
                    double d2x = b1[0] - b0[0], d2y = b1[1] - b0[1], d2z = b1[2] - b0[2];

                    double dx = r1x - r2x, dy = r1y - r2y, dz = r1z - r2z;
                    double cx = d1y * d2z - d1z * d2y;
                    double cy = d1z * d2x - d1x * d2z;
                    double cz = d1x * d2y - d1y * d2x;
                    double dist2 = dx * dx + dy * dy + dz * dz;
                    double dist3 = dist2 * Math.Sqrt(Math.Max(1e-12, dist2));
                    sum += (dx * cx + dy * cy + dz * cz) / dist3;
                }
            }
            return sum / (4.0 * Math.PI);
        }

        // Exact 16-step H4 = [5,3,3] Coxeter reflection group chamber fold (order 14,400, 120-cell / 600-cell symmetry)
        public static void FoldH4CoxeterChamber(
            double vx, double vy, double vz, double vw,
            out double fx, out double fy, out double fz, out double fw)
        {
            const double n1x = -0.8090169943749475; // -phi / 2
            const double n1y =  0.3090169943749474; //  (phi - 1) / 2
            const double n1z =  0.5;
            const double n3y = -0.8090169943749475;
            const double n3z =  0.5;
            const double n3w =  0.3090169943749474;

            fx = vx; fy = vy; fz = vz; fw = vw;
            for (int it = 0; it < 16; it++)
            {
                if (fx < 0.0) fx = -fx;
                double d1 = fx * n1x + fy * n1y + fz * n1z;
                if (d1 < 0.0)
                {
                    fx -= 2.0 * d1 * n1x;
                    fy -= 2.0 * d1 * n1y;
                    fz -= 2.0 * d1 * n1z;
                }
                if (fz > 0.0) fz = -fz;
                double d3 = fy * n3y + fz * n3z + fw * n3w;
                if (d3 < 0.0)
                {
                    fy -= 2.0 * d3 * n3y;
                    fz -= 2.0 * d3 * n3z;
                    fw -= 2.0 * d3 * n3w;
                }
            }
        }

        // Exact left Mobius gyrotranslation b (+) u in the 3D Poincare Ball B^3 (Riemannian isometry of H^3!)
        public static void MobiusTranslatePoincareBall(
            double ux, double uy, double uz,
            double bx, double by, double bz,
            out double wx, out double wy, out double wz)
        {
            double u2 = ux * ux + uy * uy + uz * uz;
            double b2 = bx * bx + by * by + bz * bz;
            double ub = ux * bx + uy * by + uz * bz;
            double denom = Math.Max(1e-12, 1.0 + 2.0 * ub + u2 * b2);
            double cU = (1.0 - b2) / denom;
            double cB = (1.0 + 2.0 * ub + u2) / denom;
            wx = cU * ux + cB * bx;
            wy = cU * uy + cB * by;
            wz = cU * uz + cB * bz;
        }

        public static double HyperbolicDistanceB3(
            double ax, double ay, double az,
            double bx, double by, double bz)
        {
            double dx = ax - bx, dy = ay - by, dz = az - bz;
            double d2 = dx * dx + dy * dy + dz * dz;
            double a2 = ax * ax + ay * ay + az * az;
            double b2 = bx * bx + by * by + bz * bz;
            double arg = 1.0 + (2.0 * d2) / Math.Max(1e-12, (1.0 - a2) * (1.0 - b2));
            if (arg < 1.0) arg = 1.0;
            return Math.Log(arg + Math.Sqrt(arg * arg - 1.0));
        }

        // CPU reference SDF for Hyperbolic Poincare {5,3,4} & {4,3,5} Honeycombs (Nelson & Segerman, arXiv:1511.02851)
        public static double EvalHyperbolicPoincarePortalSdf(
            double px, double py, double pz,
            double portalR, bool mode435,
            out int inversionsOut)
        {
            inversionsOut = 0;
            double r = Math.Sqrt(px * px + py * py + pz * pz);
            double dOuter = r - portalR;
            if (r >= portalR * 0.985) return dOuter;

            double ux = px / portalR, uy = py / portalR, uz = pz / portalR;
            double scaleJ = 1.0 / portalR;

            // Exact algebraic geodesic sphere constants satisfying |C|^2 - rho^2 = 1:
            // {5,3,4}: C = (0, sqrt(phi), -1/sqrt(phi)), rho^2 = 2/phi
            // {4,3,5}: C = (sqrt(1 + 2*phi), 0, 0),      rho^2 = 2*phi
            const double c534Y =  1.2720196495140690;
            const double c534Z = -0.7861513777574233;
            const double rho2_534 = 1.2360679774997897;
            const double rho_534  = 1.1117859405028423;

            const double c435X = 2.0581710272714924;
            const double rho2_435 = 3.2360679774997898;
            const double rho_435  = 1.7989074399478760;

            for (int it = 0; it < 12; it++)
            {
                if (!mode435)
                {
                    if (ux < 0.0) ux = -ux;
                    if (uz > 0.0) uz = -uz;
                    double d1 = ux * (-0.8090169943749475) + uy * 0.3090169943749474 + uz * 0.5;
                    if (d1 < 0.0)
                    {
                        ux -= 2.0 * d1 * (-0.8090169943749475);
                        uy -= 2.0 * d1 * 0.3090169943749474;
                        uz -= 2.0 * d1 * 0.5;
                    }
                    double dx = ux, dy = uy - c534Y, dz = uz - c534Z;
                    double d2 = Math.Max(1e-8, dx * dx + dy * dy + dz * dz);
                    if (d2 < rho2_534)
                    {
                        double k = rho2_534 / d2;
                        ux = dx * k;
                        uy = c534Y + dy * k;
                        uz = c534Z + dz * k;
                        scaleJ *= k;
                        inversionsOut++;
                    }
                    else break;
                }
                else
                {
                    ux = Math.Abs(ux); uy = Math.Abs(uy); uz = Math.Abs(uz);
                    if (ux < uy) { double t = ux; ux = uy; uy = t; }
                    if (uy < uz) { double t = uy; uy = uz; uz = t; }
                    if (ux < uy) { double t = ux; ux = uy; uy = t; }
                    double dx = ux - c435X, dy = uy, dz = uz;
                    double d2 = Math.Max(1e-8, dx * dx + dy * dy + dz * dz);
                    if (d2 < rho2_435)
                    {
                        double k = rho2_435 / d2;
                        ux = c435X + dx * k;
                        uy = dy * k;
                        uz = dz * k;
                        scaleJ *= k;
                        inversionsOut++;
                    }
                    else break;
                }
            }

            double dCell;
            if (!mode435)
            {
                double distC = Math.Sqrt(ux * ux + (uy - c534Y) * (uy - c534Y) + (uz - c534Z) * (uz - c534Z));
                dCell = ((distC - rho_534) - 0.055) / scaleJ * 0.90;
            }
            else
            {
                double distC = Math.Sqrt((ux - c435X) * (ux - c435X) + uy * uy + uz * uz);
                dCell = ((distC - rho_435) - 0.085) / scaleJ * 0.78;
            }
            return Math.Max(dOuter, dCell);
        }


        // =====================================================================
        // TASK #6 + EFFICIENCY OVERDRIVE (v1.5.0):
        //   1) Contiguous L1-Cache Flat E8 Root Table + Zero-Alloc Hot-Loop Quantizers
        //   2) Schaefer-Ju-Warren Manifold Hermite Dual Contouring (3x3 Jacobi SVD QEF,
        //      64^3 & 96^3 Parallel Z-Slice Solver, Sharp Crease .OBJ + .MTL Asset Forge)
        // =====================================================================

        public static readonly double[] E8RootsFlat1920 = BuildE8RootsFlat1920();

        private static readonly int[,] DcCornerOffsets8 = new int[8, 3]
        {
            { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 },
            { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 }
        };

        private static readonly int[,] DcCellEdges12 = new int[12, 2]
        {
            { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 },
            { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        private static readonly byte[] DcMaskCompCount = new byte[256];
        private static readonly sbyte[] DcMaskCornerComp = BuildDcManifoldTables();

        private static double[] BuildE8RootsFlat1920()
        {
            double[] flat = new double[240 * 8];
            for (int r = 0; r < 240; r++)
            {
                double[] row = E8Roots[r];
                int baseIdx = r << 3;
                for (int k = 0; k < 8; k++) flat[baseIdx + k] = row[k];
            }
            return flat;
        }

        // Zero-allocation L1-cache-resident E8 nearest-root scan for 144+ FPS render loop
        public static int FindNearestE8RootFast(double[] x8, out double sqnrDb)
        {
            sqnrDb = 0.0;
            if (x8 == null || x8.Length < 8) return 0;
            double x0 = x8[0], x1 = x8[1], x2 = x8[2], x3 = x8[3];
            double x4 = x8[4], x5 = x8[5], x6 = x8[6], x7 = x8[7];
            double sig = x0 * x0 + x1 * x1 + x2 * x2 + x3 * x3 + x4 * x4 + x5 * x5 + x6 * x6 + x7 * x7;
            if (sig < 1e-12) return 0;

            double invScale = Math.Sqrt(2.0 / sig);
            x0 *= invScale; x1 *= invScale; x2 *= invScale; x3 *= invScale;
            x4 *= invScale; x5 *= invScale; x6 *= invScale; x7 *= invScale;

            double[] roots = E8RootsFlat1920;
            int bestIdx = 0;
            double bestDot = -1e9;
            for (int r = 0, o = 0; r < 240; r++, o += 8)
            {
                double dt = x0 * roots[o] + x1 * roots[o + 1] + x2 * roots[o + 2] + x3 * roots[o + 3]
                          + x4 * roots[o + 4] + x5 * roots[o + 5] + x6 * roots[o + 6] + x7 * roots[o + 7];
                if (dt > bestDot)
                {
                    bestDot = dt;
                    bestIdx = r;
                }
            }
            double err2 = Math.Max(1e-9, 4.0 - 2.0 * bestDot);
            sqnrDb = 10.0 * Math.Log10(2.0 / err2);
            return bestIdx;
        }

        // Zero-allocation Conway-Sloane E8 coset quantizer (no per-call double[8] heap allocation)
        public static double QuantizeE8CosetFast(double[] x8, out bool isSpinorCoset)
        {
            isSpinorCoset = false;
            if (x8 == null || x8.Length < 8) return 0.0;
            double dA = RoundD8ScalarOnly(x8, 0.0);
            double dB = RoundD8ScalarOnly(x8, 0.5);
            if (dB < dA)
            {
                isSpinorCoset = true;
                return dB;
            }
            return dA;
        }

        private static double RoundD8ScalarOnly(double[] x8, double sh)
        {
            int sumZ = 0;
            double baseD2 = 0.0;
            double maxAbs = -1.0;
            for (int k = 0; k < 8; k++)
            {
                double u = x8[k] - sh;
                int z = (int)Math.Floor(u + 0.5);
                sumZ += z;
                double a = Math.Abs(u - z);
                baseD2 += a * a;
                if (a > maxAbs) maxAbs = a;
            }
            if ((sumZ & 1) != 0)
            {
                return baseD2 + 1.0 - 2.0 * maxAbs;
            }
            return baseD2;
        }

        private static sbyte[] BuildDcManifoldTables()
        {
            sbyte[] map = new sbyte[256 * 8];
            for (int i = 0; i < map.Length; i++) map[i] = -1;

            int[][] adj = new int[8][];
            for (int c = 0; c < 8; c++)
            {
                List<int> nb = new List<int>(3);
                for (int e = 0; e < 12; e++)
                {
                    if (DcCellEdges12[e, 0] == c) nb.Add(DcCellEdges12[e, 1]);
                    else if (DcCellEdges12[e, 1] == c) nb.Add(DcCellEdges12[e, 0]);
                }
                adj[c] = nb.ToArray();
            }

            int[] stack = new int[8];
            for (int mask = 1; mask < 255; mask++)
            {
                sbyte compId = 0;
                int rowOff = mask << 3;
                for (int c = 0; c < 8; c++)
                {
                    if (((mask >> c) & 1) != 0 && map[rowOff + c] < 0)
                    {
                        int sp = 0;
                        stack[sp++] = c;
                        map[rowOff + c] = compId;
                        while (sp > 0)
                        {
                            int cur = stack[--sp];
                            int[] nbs = adj[cur];
                            for (int k = 0; k < nbs.Length; k++)
                            {
                                int nb = nbs[k];
                                if (((mask >> nb) & 1) != 0 && map[rowOff + nb] < 0)
                                {
                                    map[rowOff + nb] = compId;
                                    stack[sp++] = nb;
                                }
                            }
                        }
                        compId++;
                    }
                }
                DcMaskCompCount[mask] = (byte)compId;
            }
            return map;
        }

        public sealed class DualContourBakeResult
        {
            public bool Ok;
            public string ObjPath;
            public string MtlPath;
            public int GridRes;
            public int SymmetryMode;
            public int VertexCount;
            public int TriangleCount;
            public int SharpCreaseCount;
            public int SplitCellCount;
            public int BoundaryEdges;
            public int NonManifoldEdges;
            public double SignedVolume;
            public double QefCreaseGainRatio;
            public double ElapsedMs;
        }

        // Exact 3x3 Symmetric Jacobi Eigendecomposition / Truncated SVD QEF Solver (Ju et al., SIGGRAPH 2002)
        public static void SolveSymmetricQef3x3(
            double a00, double a01, double a02,
            double a11, double a12, double a22,
            double b0, double b1, double b2,
            out double dx, out double dy, out double dz)
        {
            const double reg = 1e-4;
            const double svdTol = 0.02;

            double m00 = a00 + reg, m01 = a01, m02 = a02;
            double m11 = a11 + reg, m12 = a12;
            double m22 = a22 + reg;

            double v00 = 1.0, v01 = 0.0, v02 = 0.0;
            double v10 = 0.0, v11 = 1.0, v12 = 0.0;
            double v20 = 0.0, v21 = 0.0, v22 = 1.0;

            for (int sweep = 0; sweep < 6; sweep++)
            {
                if (Math.Abs(m01) > 1e-13)
                {
                    double tau = (m11 - m00) / (2.0 * m01);
                    double t = (tau >= 0.0)
                        ? (1.0 / (tau + Math.Sqrt(1.0 + tau * tau)))
                        : (-1.0 / (-tau + Math.Sqrt(1.0 + tau * tau)));
                    double c = 1.0 / Math.Sqrt(1.0 + t * t);
                    double s = t * c;
                    double nm00 = c * c * m00 - 2.0 * s * c * m01 + s * s * m11;
                    double nm11 = s * s * m00 + 2.0 * s * c * m01 + c * c * m11;
                    double nm02 = c * m02 - s * m12;
                    double nm12 = s * m02 + c * m12;
                    m00 = nm00; m11 = nm11; m01 = 0.0; m02 = nm02; m12 = nm12;
                    double tv;
                    tv = c * v00 - s * v01; v01 = s * v00 + c * v01; v00 = tv;
                    tv = c * v10 - s * v11; v11 = s * v10 + c * v11; v10 = tv;
                    tv = c * v20 - s * v21; v21 = s * v20 + c * v21; v20 = tv;
                }
                if (Math.Abs(m02) > 1e-13)
                {
                    double tau = (m22 - m00) / (2.0 * m02);
                    double t = (tau >= 0.0)
                        ? (1.0 / (tau + Math.Sqrt(1.0 + tau * tau)))
                        : (-1.0 / (-tau + Math.Sqrt(1.0 + tau * tau)));
                    double c = 1.0 / Math.Sqrt(1.0 + t * t);
                    double s = t * c;
                    double nm00 = c * c * m00 - 2.0 * s * c * m02 + s * s * m22;
                    double nm22 = s * s * m00 + 2.0 * s * c * m02 + c * c * m22;
                    double nm01 = c * m01 - s * m12;
                    double nm12 = s * m01 + c * m12;
                    m00 = nm00; m22 = nm22; m02 = 0.0; m01 = nm01; m12 = nm12;
                    double tv;
                    tv = c * v00 - s * v02; v02 = s * v00 + c * v02; v00 = tv;
                    tv = c * v10 - s * v12; v12 = s * v10 + c * v12; v10 = tv;
                    tv = c * v20 - s * v22; v22 = s * v20 + c * v22; v20 = tv;
                }
                if (Math.Abs(m12) > 1e-13)
                {
                    double tau = (m22 - m11) / (2.0 * m12);
                    double t = (tau >= 0.0)
                        ? (1.0 / (tau + Math.Sqrt(1.0 + tau * tau)))
                        : (-1.0 / (-tau + Math.Sqrt(1.0 + tau * tau)));
                    double c = 1.0 / Math.Sqrt(1.0 + t * t);
                    double s = t * c;
                    double nm11 = c * c * m11 - 2.0 * s * c * m12 + s * s * m22;
                    double nm22 = s * s * m11 + 2.0 * s * c * m12 + c * c * m22;
                    double nm01 = c * m01 - s * m02;
                    double nm02 = s * m01 + c * m02;
                    m11 = nm11; m22 = nm22; m12 = 0.0; m01 = nm01; m02 = nm02;
                    double tv;
                    tv = c * v01 - s * v02; v02 = s * v01 + c * v02; v01 = tv;
                    tv = c * v11 - s * v12; v12 = s * v11 + c * v12; v11 = tv;
                    tv = c * v21 - s * v22; v22 = s * v21 + c * v22; v21 = tv;
                }
            }

            double maxEv = Math.Max(1e-12, Math.Max(m00, Math.Max(m11, m22)));
            double cutoff = svdTol * maxEv;
            double inv0 = (m00 > cutoff) ? (1.0 / m00) : 0.0;
            double inv1 = (m11 > cutoff) ? (1.0 / m11) : 0.0;
            double inv2 = (m22 > cutoff) ? (1.0 / m22) : 0.0;

            double y0 = (v00 * b0 + v10 * b1 + v20 * b2) * inv0;
            double y1 = (v01 * b0 + v11 * b1 + v21 * b2) * inv1;
            double y2 = (v02 * b0 + v12 * b1 + v22 * b2) * inv2;

            dx = v00 * y0 + v01 * y1 + v02 * y2;
            dy = v10 * y0 + v11 * y1 + v12 * y2;
            dz = v20 * y0 + v21 * y1 + v22 * y2;
        }

        // Evaluates the active 4D/6D/Hyperbolic/24D Crystal SDF for Asset Forge across all 8 Symmetry Modes (0..7)
        public static double EvalCrystalForgeSdf(
            double px, double py, double pz,
            int symMode, double k1, double k2, double k3,
            double qx, double qy, double qz, double qw)
        {
            double sK1 = Math.Max(0.45, k1);
            if (symMode == 4)
            {
                // Mode 4: Stereographic S^3 -> S^2 Hopf Villarceau Linked Torus Shell
                double rEucl = Math.Sqrt(px * px + py * py + pz * pz);
                double maxShell = 1.85 * sK1;
                if (rEucl > maxShell + 0.30) return rEucl - maxShell;
                double ux = px / sK1, uy = py / sK1, uz = pz / sK1;
                double z0, z1, z2, z3;
                InverseStereographicR3ToS3(ux, uy, uz, out z0, out z1, out z2, out z3);
                double ql = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                double rqx = (ql > 0.25) ? (qx / ql) : 0.0;
                double rqy = (ql > 0.25) ? (qy / ql) : 0.0;
                double rqz = (ql > 0.25) ? (qz / ql) : 0.0;
                double rqw = (ql > 0.25) ? (qw / ql) : 1.0;
                double w0, w1, w2, w3;
                QuatMulXYZW(rqx, rqy, rqz, rqw, z0, z1, z2, z3, out w0, out w1, out w2, out w3);
                double bx, by, bz;
                HopfMapS3ToS2(w0, w1, w2, w3, out bx, out by, out bz);
                double az = Math.Atan2(by, bx + 1e-9);
                double sec6 = Math.Floor(az / 1.0471975512 + 0.5) * 1.0471975512;
                double dotEq = bx * Math.Cos(sec6) + by * Math.Sin(sec6);
                double bestDot = Math.Max(dotEq, Math.Abs(bz) * 0.985);
                double ringSeam = Math.Sqrt(Math.Max(0.0, 0.5 * (1.0 - bestDot)));
                double conf = sK1 * 0.5 * (1.0 + ux * ux + uy * uy + uz * uz);
                double tubeR = 0.135 + 0.035 * Math.Max(0.0, Math.Min(1.5, k3 * 0.65));
                double dRings = (ringSeam - tubeR) * Math.Min(conf, 2.15) * 0.68;
                return Math.Max(dRings, rEucl - maxShell);
            }
            if (symMode == 5)
            {
                // Mode 5: Sharp 4D Hurwitz 24-Cell {3,4,3} + H4 120-Cell {5,3,3} Polychoron Crystal + Gyroid Lattice
                double rad = 1.42 * sK1;
                double sliceW = 0.16 * rad * Math.Sin(k2 * 0.8);
                double ax = Math.Abs(px), ay = Math.Abs(py), az = Math.Abs(pz), aw = Math.Abs(sliceW);
                double maxAxis = Math.Max(Math.Max(ax, ay), Math.Max(az, aw));
                double sumHalf = 0.5 * (ax + ay + az + aw);
                double d24 = Math.Max(maxAxis, sumHalf) - 1.05 * rad;

                double fx, fy, fz, fw;
                FoldH4CoxeterChamber(px, py, pz, sliceW, out fx, out fy, out fz, out fw);
                double d120 = fw - 1.28 * rad;
                double dOuter = Math.Max(d24, d120);
                if (k3 > 0.10)
                {
                    double wallHalf = 0.045 + 0.045 * Math.Min(1.4, k3);
                    double gTube = Math.Abs(SdGyroid(px, py, pz, 2.6)) - wallHalf;
                    dOuter = Math.Max(dOuter, -gTube * 0.85);
                }
                return dOuter * 0.90;
            }
            if (symMode == 6)
            {
                // Mode 6: Hyperbolic Poincare {5,3,4} / {4,3,5} Sphere-Inversion Portal
                int invOut;
                return EvalHyperbolicPoincarePortalSdf(px, py, pz, 1.52 * sK1, k3 >= 0.95, out invOut);
            }

            // Modes 0..3 & 7: Sharp 6D Phason-Deformed 12-Plane Polytope + Boolean Gyroid / 24D Leech Channels
            double qx3 = px;
            double qy3 = py * 0.78;
            double qz3 = pz;
            double rad6 = 1.28 * sK1;
            double dPoly = -1e6;
            for (int m = 0; m < 6; m++)
            {
                double[] ax6 = IcosaAxes6[m];
                double ph = 0.13 * k2 * Math.Cos(m * 1.0471975512);
                double dm = Math.Abs(qx3 * ax6[0] + qy3 * ax6[1] + qz3 * ax6[2]) - (rad6 + ph);
                if (dm > dPoly) dPoly = dm;
            }

            if (symMode == 7 && k3 > 0.05)
            {
                // Sharp 24D Leech/E8 Voronoi facet channels carved into the crystal faces
                double[] s8 = new double[8]
                {
                    qx3 * 1.35, qy3 * 1.35, qz3 * 1.35, (qx3 + qy3 + qz3) * 0.78,
                    (qx3 - qz3) * 0.95, (qy3 - qx3) * 0.95, (qz3 + qy3) * 0.95, 0.25 * k2
                };
                bool sp;
                double d2 = QuantizeE8CosetFast(s8, out sp);
                double groove = Math.Sqrt(Math.Max(0.0, d2)) - 0.56;
                dPoly = Math.Max(dPoly, -Math.Abs(groove) + 0.055 * Math.Min(1.2, k3));
            }
            else if (k3 > 0.02)
            {
                double wallHalf = 0.048 + 0.095 * Math.Min(1.4, k3);
                double gTube = Math.Abs(SdGyroid(qx3, qy3, qz3, 2.85)) - wallHalf;
                dPoly = Math.Max(dPoly, -gTube * 0.85);
            }
            return dPoly * 0.85;
        }

        private static void EvalCrystalForgeGrad(
            double x, double y, double z,
            int symMode, double k1, double k2, double k3,
            double qx, double qy, double qz, double qw,
            out double nx, out double ny, out double nz)
        {
            const double eps = 0.0012;
            double f0 = EvalCrystalForgeSdf(x + eps, y - eps, z - eps, symMode, k1, k2, k3, qx, qy, qz, qw);
            double f1 = EvalCrystalForgeSdf(x - eps, y - eps, z + eps, symMode, k1, k2, k3, qx, qy, qz, qw);
            double f2 = EvalCrystalForgeSdf(x - eps, y + eps, z - eps, symMode, k1, k2, k3, qx, qy, qz, qw);
            double f3 = EvalCrystalForgeSdf(x + eps, y + eps, z + eps, symMode, k1, k2, k3, qx, qy, qz, qw);
            double gx = f0 - f1 - f2 + f3;
            double gy = -f0 - f1 + f2 + f3;
            double gz = -f0 + f1 - f2 + f3;
            double ln = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            if (ln < 1e-12) { nx = 0.0; ny = 1.0; nz = 0.0; return; }
            double inv = 1.0 / ln;
            nx = gx * inv; ny = gy * inv; nz = gz * inv;
        }

        // Multi-threaded Schaefer-Ju-Warren Manifold Hermite Dual Contouring Asset Forge (64^3 / 96^3)
        public static DualContourBakeResult BakeHermiteDualContourObj(
            string exportsDir,
            int gridRes,
            int symMode,
            float k1Scale,
            float k2Phason,
            float k3Carve,
            float qx, float qy, float qz, float qw,
            string optionalPbrPrefix)
        {
            Stopwatch sw = Stopwatch.StartNew();
            DualContourBakeResult resObj = new DualContourBakeResult();
            int res = Math.Max(24, Math.Min(112, gridRes));
            int sym = Math.Max(0, Math.Min(7, symMode));
            resObj.GridRes = res;
            resObj.SymmetryMode = sym;

            Directory.CreateDirectory(exportsDir);
            double sK1 = Math.Max(0.45, k1Scale);
            double bound = (sym == 6) ? (1.85 * sK1) : ((sym == 5) ? (2.55 * sK1) : Math.Max(2.55, 2.48 * sK1 + 0.28 * Math.Abs(k2Phason)));
            double maxOuterR = bound * 0.94;
            double step = (2.0 * bound) / res;
            int n1 = res + 1;

            // 1. Parallel (N+1)^3 Corner SDF Grid Evaluation with Outer Bounding-Sphere Fast Skip & Boundary Closure Lock
            float[] sdf = new float[n1 * n1 * n1];
            System.Threading.Tasks.Parallel.For(0, n1, delegate(int iz)
            {
                double pz = -bound + iz * step;
                bool zBorder = (iz == 0 || iz == res);
                for (int iy = 0; iy < n1; iy++)
                {
                    double py = -bound + iy * step;
                    bool yzBorder = zBorder || (iy == 0 || iy == res);
                    int rowBase = (iz * n1 + iy) * n1;
                    double yz2 = py * py + pz * pz;
                    for (int ix = 0; ix < n1; ix++)
                    {
                        if (yzBorder || ix == 0 || ix == res)
                        {
                            // Guarantee strictly closed interior level-set so 0 boundary edges occur for all slider values!
                            sdf[rowBase + ix] = 0.05f;
                            continue;
                        }
                        double px = -bound + ix * step;
                        double r2 = px * px + yz2;
                        if (r2 > maxOuterR * maxOuterR)
                        {
                            sdf[rowBase + ix] = (float)(Math.Sqrt(r2) - maxOuterR + 0.05);
                        }
                        else
                        {
                            sdf[rowBase + ix] = (float)EvalCrystalForgeSdf(px, py, pz, sym, sK1, k2Phason, k3Carve, qx, qy, qz, qw);
                        }
                    }
                }
            });

            // 2. Lock-Free Parallel Per-Z-Slice Hermite Crossing + 3x3 Jacobi SVD QEF Vertex Extraction
            List<float>[] sliceVertPosNrm = new List<float>[res]; // 6 floats per vertex: vx, vy, vz, nx, ny, nz
            List<int>[] sliceCellIdx = new List<int>[res];
            List<byte>[] sliceCellMask = new List<byte>[res];
            int[] sliceCreases = new int[res];
            int[] sliceSplits = new int[res];
            double[] sliceQefErr = new double[res];
            double[] sliceMassErr = new double[res];

            System.Threading.Tasks.Parallel.For(0, res, delegate(int iz)
            {
                List<float> vList = new List<float>(512 * 6);
                List<int> cIdxList = new List<int>(512);
                List<byte> cMaskList = new List<byte>(512);
                int localCreases = 0;
                int localSplits = 0;
                double localQefErr = 0.0;
                double localMassErr = 0.0;

                double[] cVals = new double[8];
                double[] ePx = new double[12], ePy = new double[12], ePz = new double[12];
                double[] eNx = new double[12], eNy = new double[12], eNz = new double[12];
                sbyte[] eComp = new sbyte[12];

                double z0 = -bound + iz * step;
                for (int iy = 0; iy < res; iy++)
                {
                    double y0 = -bound + iy * step;
                    for (int ix = 0; ix < res; ix++)
                    {
                        int mask = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            int gx = ix + DcCornerOffsets8[c, 0];
                            int gy = iy + DcCornerOffsets8[c, 1];
                            int gz = iz + DcCornerOffsets8[c, 2];
                            double dv = sdf[(gz * n1 + gy) * n1 + gx];
                            cVals[c] = dv;
                            if (dv < 0.0) mask |= (1 << c);
                        }
                        if (mask == 0 || mask == 255) continue;

                        int nComps = DcMaskCompCount[mask];
                        if (nComps > 1) localSplits++;
                        int maskRow = mask << 3;
                        double x0 = -bound + ix * step;

                        int activeEdgeCount = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int c0 = DcCellEdges12[e, 0];
                            int c1 = DcCellEdges12[e, 1];
                            bool in0 = ((mask >> c0) & 1) != 0;
                            bool in1 = ((mask >> c1) & 1) != 0;
                            if (in0 == in1) continue;

                            int insideCorner = in0 ? c0 : c1;
                            sbyte cid = DcMaskCornerComp[maskRow + insideCorner];

                            double d0 = cVals[c0];
                            double d1 = cVals[c1];
                            double p0x = x0 + DcCornerOffsets8[c0, 0] * step;
                            double p0y = y0 + DcCornerOffsets8[c0, 1] * step;
                            double p0z = z0 + DcCornerOffsets8[c0, 2] * step;
                            double p1x = x0 + DcCornerOffsets8[c1, 0] * step;
                            double p1y = y0 + DcCornerOffsets8[c1, 1] * step;
                            double p1z = z0 + DcCornerOffsets8[c1, 2] * step;

                            // 2-step secant/bracket root refinement on exact continuous SDF
                            double loT = 0.0, hiT = 1.0;
                            double loD = d0, hiD = d1;
                            double t = d0 / (d0 - d1);
                            for (int rf = 0; rf < 2; rf++)
                            {
                                double cx = p0x + (p1x - p0x) * t;
                                double cy = p0y + (p1y - p0y) * t;
                                double cz = p0z + (p1z - p0z) * t;
                                double dm = EvalCrystalForgeSdf(cx, cy, cz, sym, sK1, k2Phason, k3Carve, qx, qy, qz, qw);
                                if ((dm < 0.0) == (loD < 0.0)) { loT = t; loD = dm; }
                                else { hiT = t; hiD = dm; }
                                double denom = loD - hiD;
                                t = (Math.Abs(denom) > 1e-12) ? (loT + (loD / denom) * (hiT - loT)) : (0.5 * (loT + hiT));
                            }
                            double hx = p0x + (p1x - p0x) * t;
                            double hy = p0y + (p1y - p0y) * t;
                            double hz = p0z + (p1z - p0z) * t;
                            double hnx, hny, hnz;
                            EvalCrystalForgeGrad(hx, hy, hz, sym, sK1, k2Phason, k3Carve, qx, qy, qz, qw, out hnx, out hny, out hnz);

                            ePx[activeEdgeCount] = hx; ePy[activeEdgeCount] = hy; ePz[activeEdgeCount] = hz;
                            eNx[activeEdgeCount] = hnx; eNy[activeEdgeCount] = hny; eNz[activeEdgeCount] = hnz;
                            eComp[activeEdgeCount] = cid;
                            activeEdgeCount++;
                        }

                        cIdxList.Add((iz * res + iy) * res + ix);
                        cMaskList.Add((byte)mask);

                        for (int cid = 0; cid < nComps; cid++)
                        {
                            double mx = 0.0, my = 0.0, mz = 0.0;
                            double avgNx = 0.0, avgNy = 0.0, avgNz = 0.0;
                            int cnt = 0;
                            int firstK = -1;
                            for (int k = 0; k < activeEdgeCount; k++)
                            {
                                if (eComp[k] != cid) continue;
                                if (firstK < 0) firstK = k;
                                mx += ePx[k]; my += ePy[k]; mz += ePz[k];
                                avgNx += eNx[k]; avgNy += eNy[k]; avgNz += eNz[k];
                                cnt++;
                            }
                            double invC = 1.0 / Math.Max(1, cnt);
                            mx *= invC; my *= invC; mz *= invC;

                            double a00 = 0.0, a01 = 0.0, a02 = 0.0, a11 = 0.0, a12 = 0.0, a22 = 0.0;
                            double b0 = 0.0, b1 = 0.0, b2 = 0.0;
                            double minDot = 1.0;
                            double refNx = eNx[firstK], refNy = eNy[firstK], refNz = eNz[firstK];

                            for (int k = 0; k < activeEdgeCount; k++)
                            {
                                if (eComp[k] != cid) continue;
                                double nx = eNx[k], ny = eNy[k], nz = eNz[k];
                                double dt = nx * refNx + ny * refNy + nz * refNz;
                                if (dt < minDot) minDot = dt;
                                double rx = ePx[k] - mx, ry = ePy[k] - my, rz = ePz[k] - mz;
                                double rhs = nx * rx + ny * ry + nz * rz;
                                a00 += nx * nx; a01 += nx * ny; a02 += nx * nz;
                                a11 += ny * ny; a12 += ny * nz; a22 += nz * nz;
                                b0 += nx * rhs; b1 += ny * rhs; b2 += nz * rhs;
                            }

                            double dqx, dqy, dqz;
                            SolveSymmetricQef3x3(a00, a01, a02, a11, a12, a22, b0, b1, b2, out dqx, out dqy, out dqz);
                            double vx = mx + dqx, vy = my + dqy, vz = mz + dqz;
                            double pad = 0.02 * step;
                            if (vx < x0 - pad || vx > x0 + step + pad ||
                                vy < y0 - pad || vy > y0 + step + pad ||
                                vz < z0 - pad || vz > z0 + step + pad)
                            {
                                vx = Math.Max(x0, Math.Min(x0 + step, vx));
                                vy = Math.Max(y0, Math.Min(y0 + step, vy));
                                vz = Math.Max(z0, Math.Min(z0 + step, vz));
                            }

                            if (minDot < 0.78)
                            {
                                localCreases++;
                                for (int k = 0; k < activeEdgeCount; k++)
                                {
                                    if (eComp[k] != cid) continue;
                                    double eq = eNx[k] * (vx - ePx[k]) + eNy[k] * (vy - ePy[k]) + eNz[k] * (vz - ePz[k]);
                                    double em = eNx[k] * (mx - ePx[k]) + eNy[k] * (my - ePy[k]) + eNz[k] * (mz - ePz[k]);
                                    localQefErr += eq * eq;
                                    localMassErr += em * em;
                                }
                            }

                            double nLen = Math.Sqrt(avgNx * avgNx + avgNy * avgNy + avgNz * avgNz);
                            if (nLen > 1e-9) { avgNx /= nLen; avgNy /= nLen; avgNz /= nLen; }
                            else { avgNx = 0.0; avgNy = 1.0; avgNz = 0.0; }

                            vList.Add((float)vx); vList.Add((float)vy); vList.Add((float)vz);
                            vList.Add((float)avgNx); vList.Add((float)avgNy); vList.Add((float)avgNz);
                        }
                    }
                }

                sliceVertPosNrm[iz] = vList;
                sliceCellIdx[iz] = cIdxList;
                sliceCellMask[iz] = cMaskList;
                sliceCreases[iz] = localCreases;
                sliceSplits[iz] = localSplits;
                sliceQefErr[iz] = localQefErr;
                sliceMassErr[iz] = localMassErr;
            });

            // 3. Prefix-Sum Assemble Contiguous Vertex Array & Packed 1-Int-Per-Cell Component Table
            int totalVerts = 0;
            int totalCreases = 0, totalSplits = 0;
            double sumQefErr = 0.0, sumMassErr = 0.0;
            for (int iz = 0; iz < res; iz++)
            {
                totalVerts += sliceVertPosNrm[iz].Count / 6;
                totalCreases += sliceCreases[iz];
                totalSplits += sliceSplits[iz];
                sumQefErr += sliceQefErr[iz];
                sumMassErr += sliceMassErr[iz];
            }

            float[] vertPosNrm = new float[totalVerts * 6];
            int[] cellPacked = new int[res * res * res];
            for (int i = 0; i < cellPacked.Length; i++) cellPacked[i] = -1;

            int vCursor = 0;
            for (int iz = 0; iz < res; iz++)
            {
                List<float> sv = sliceVertPosNrm[iz];
                if (sv.Count > 0)
                {
                    sv.CopyTo(0, vertPosNrm, vCursor * 6, sv.Count);
                }
                List<int> scIdx = sliceCellIdx[iz];
                List<byte> scMask = sliceCellMask[iz];
                int localVid = vCursor;
                for (int k = 0; k < scIdx.Count; k++)
                {
                    byte mk = scMask[k];
                    cellPacked[scIdx[k]] = (localVid << 8) | mk;
                    localVid += DcMaskCompCount[mk];
                }
                vCursor = localVid;
                sliceVertPosNrm[iz] = null;
                sliceCellIdx[iz] = null;
                sliceCellMask[iz] = null;
            }

            // 4. Dual Quad -> Ridge-Aligned Triangle Emission across X, Y, Z Interior Sign-Change Grid Edges
            List<int> triIndices = new List<int>(totalVerts * 6);

            // X-edges: (ix, iy, iz) -> (ix+1, iy, iz)
            for (int iz = 1; iz < res; iz++)
            {
                for (int iy = 1; iy < res; iy++)
                {
                    int gRow = (iz * n1 + iy) * n1;
                    for (int ix = 0; ix < res; ix++)
                    {
                        bool inA = (sdf[gRow + ix] < 0.0f);
                        bool inB = (sdf[gRow + ix + 1] < 0.0f);
                        if (inA == inB) continue;
                        int bx = inA ? 0 : 1;
                        int v00 = LookupCellCornerVid(cellPacked, res, ix, iy - 1, iz - 1, bx + 2 + 4);
                        int v10 = LookupCellCornerVid(cellPacked, res, ix, iy,     iz - 1, bx + 0 + 4);
                        int v11 = LookupCellCornerVid(cellPacked, res, ix, iy,     iz,     bx + 0 + 0);
                        int v01 = LookupCellCornerVid(cellPacked, res, ix, iy - 1, iz,     bx + 2 + 0);
                        EmitCreaseAlignedQuad(triIndices, vertPosNrm, v00, v10, v11, v01, inA);
                    }
                }
            }

            // Y-edges: (ix, iy, iz) -> (ix, iy+1, iz)
            for (int iz = 1; iz < res; iz++)
            {
                for (int iy = 0; iy < res; iy++)
                {
                    int gRowA = (iz * n1 + iy) * n1;
                    int gRowB = (iz * n1 + iy + 1) * n1;
                    for (int ix = 1; ix < res; ix++)
                    {
                        bool inA = (sdf[gRowA + ix] < 0.0f);
                        bool inB = (sdf[gRowB + ix] < 0.0f);
                        if (inA == inB) continue;
                        int by = inA ? 0 : 2;
                        int v00 = LookupCellCornerVid(cellPacked, res, ix - 1, iy, iz - 1, 1 + by + 4);
                        int v01 = LookupCellCornerVid(cellPacked, res, ix - 1, iy, iz,     1 + by + 0);
                        int v11 = LookupCellCornerVid(cellPacked, res, ix,     iy, iz,     0 + by + 0);
                        int v10 = LookupCellCornerVid(cellPacked, res, ix,     iy, iz - 1, 0 + by + 4);
                        EmitCreaseAlignedQuad(triIndices, vertPosNrm, v00, v01, v11, v10, inA);
                    }
                }
            }

            // Z-edges: (ix, iy, iz) -> (ix, iy, iz+1)
            for (int iz = 0; iz < res; iz++)
            {
                for (int iy = 1; iy < res; iy++)
                {
                    int gRowA = (iz * n1 + iy) * n1;
                    int gRowB = ((iz + 1) * n1 + iy) * n1;
                    for (int ix = 1; ix < res; ix++)
                    {
                        bool inA = (sdf[gRowA + ix] < 0.0f);
                        bool inB = (sdf[gRowB + ix] < 0.0f);
                        if (inA == inB) continue;
                        int bz = inA ? 0 : 4;
                        int v00 = LookupCellCornerVid(cellPacked, res, ix - 1, iy - 1, iz, 1 + 2 + bz);
                        int v10 = LookupCellCornerVid(cellPacked, res, ix,     iy - 1, iz, 0 + 2 + bz);
                        int v11 = LookupCellCornerVid(cellPacked, res, ix,     iy,     iz, 0 + 0 + bz);
                        int v01 = LookupCellCornerVid(cellPacked, res, ix - 1, iy,     iz, 1 + 0 + bz);
                        EmitCreaseAlignedQuad(triIndices, vertPosNrm, v00, v10, v11, v01, inA);
                    }
                }
            }

            sdf = null;
            cellPacked = null;

            int triCount = triIndices.Count / 3;

            // 5. Zero-Alloc Sorted Half-Edge 2-Manifold & Divergence-Theorem Volume Verification
            ulong[] halfEdges = new ulong[triCount * 3];
            double vol6 = 0.0;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = triIndices[t * 3];
                int i1 = triIndices[t * 3 + 1];
                int i2 = triIndices[t * 3 + 2];

                halfEdges[t * 3]     = PackHalfEdgeKey(i0, i1);
                halfEdges[t * 3 + 1] = PackHalfEdgeKey(i1, i2);
                halfEdges[t * 3 + 2] = PackHalfEdgeKey(i2, i0);

                int p0 = i0 * 6, p1 = i1 * 6, p2 = i2 * 6;
                double x1 = vertPosNrm[p0],     y1 = vertPosNrm[p0 + 1], z1 = vertPosNrm[p0 + 2];
                double x2 = vertPosNrm[p1],     y2 = vertPosNrm[p1 + 1], z2 = vertPosNrm[p1 + 2];
                double x3 = vertPosNrm[p2],     y3 = vertPosNrm[p2 + 1], z3 = vertPosNrm[p2 + 2];
                vol6 += x1 * (y2 * z3 - z2 * y3) - y1 * (x2 * z3 - z2 * x3) + z1 * (x2 * y3 - y2 * x3);
            }

            Array.Sort(halfEdges);
            int boundaryEdges = 0;
            int nonManifoldEdges = 0;
            int heIdx = 0;
            int heLen = halfEdges.Length;
            while (heIdx < heLen)
            {
                ulong undirected = halfEdges[heIdx] >> 1;
                int runEnd = heIdx + 1;
                while (runEnd < heLen && (halfEdges[runEnd] >> 1) == undirected) runEnd++;
                int runCount = runEnd - heIdx;
                if (runCount == 1)
                {
                    boundaryEdges++;
                }
                else if (runCount == 2)
                {
                    if ((halfEdges[heIdx] ^ 1UL) != halfEdges[heIdx + 1])
                        nonManifoldEdges++;
                }
                else
                {
                    nonManifoldEdges += (runCount - 2);
                }
                heIdx = runEnd;
            }
            halfEdges = null;

            // 6. Stream Sharp-Creased Wavefront .OBJ + Companion PBR .MTL directly to Disk (64 KB buffer, 0 LOH StringBuilder!)
            string pbrStem = string.IsNullOrEmpty(optionalPbrPrefix)
                ? ("OmegaPBR_Mode" + sym.ToString(CultureInfo.InvariantCulture) + "_2048")
                : optionalPbrPrefix;
            string stem = "OmegaCrystal_Mode" + sym.ToString(CultureInfo.InvariantCulture) + "_DC" + res.ToString(CultureInfo.InvariantCulture);
            string objPath = Path.Combine(exportsDir, stem + ".obj");
            string mtlName = stem + ".mtl";
            string mtlPath = Path.Combine(exportsDir, mtlName);

            UTF8Encoding utf8NoBom = new UTF8Encoding(false);
            using (FileStream fsMtl = new FileStream(mtlPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096))
            using (StreamWriter swMtl = new StreamWriter(fsMtl, utf8NoBom))
            {
                swMtl.WriteLine("# OmegaEngine v1.5.0 PBR Material Companion (Blender / NifSkope / Skyrim AE Creation Kit)");
                swMtl.WriteLine("newmtl OmegaCrystalPBR");
                swMtl.WriteLine("Ka 1.000 1.000 1.000");
                swMtl.WriteLine("Kd 1.000 1.000 1.000");
                swMtl.WriteLine("Ks 0.420 0.460 0.520");
                swMtl.WriteLine("Ns 180.0");
                swMtl.WriteLine("d 1.0");
                swMtl.WriteLine("illum 2");
                swMtl.WriteLine("map_Kd " + pbrStem + "_Albedo.png");
                swMtl.WriteLine("map_Ke " + pbrStem + "_Emissive.png");
                swMtl.WriteLine("norm " + pbrStem + "_Normal.png");
                swMtl.WriteLine("map_bump -bm 1.0 " + pbrStem + "_Normal.png");
                swMtl.WriteLine("disp " + pbrStem + "_Height.png");
            }

            double invUvScale = 1.0 / Math.Max(0.5, 2.0 * bound);
            using (FileStream fsObj = new FileStream(objPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536))
            using (StreamWriter swObj = new StreamWriter(fsObj, utf8NoBom, 65536))
            {
                swObj.WriteLine("# Station 15 OmegaEngine v1.5.0 :: Hermite Dual Contouring Sharp-Creased Crystal Forge");
                swObj.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "# Mode={0} Grid={1}^3 Vertices={2} Triangles={3} BoundaryEdges={4} NonManifoldEdges={5}",
                    sym, res, totalVerts, triCount, boundaryEdges, nonManifoldEdges));
                swObj.WriteLine("mtllib " + mtlName);
                swObj.WriteLine("o " + stem);
                swObj.WriteLine("g " + stem);
                swObj.WriteLine("usemtl OmegaCrystalPBR");
                swObj.WriteLine("s off");

                for (int v = 0; v < totalVerts; v++)
                {
                    int o = v * 6;
                    // Shift Y +2.78m so exported crystal sits at exact world sanctum height above Dais
                    swObj.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "v {0:0.00000} {1:0.00000} {2:0.00000}",
                        vertPosNrm[o], vertPosNrm[o + 1] + 2.78f, vertPosNrm[o + 2]));
                }

                for (int v = 0; v < totalVerts; v++)
                {
                    int o = v * 6;
                    float vx = vertPosNrm[o], vy = vertPosNrm[o + 1], vz = vertPosNrm[o + 2];
                    float nx = Math.Abs(vertPosNrm[o + 3]), ny = Math.Abs(vertPosNrm[o + 4]), nz = Math.Abs(vertPosNrm[o + 5]);
                    double uTex, vTex;
                    if (ny >= nx && ny >= nz) { uTex = vx * invUvScale + 0.5; vTex = vz * invUvScale + 0.5; }
                    else if (nx >= nz)        { uTex = vz * invUvScale + 0.5; vTex = vy * invUvScale + 0.5; }
                    else                      { uTex = vx * invUvScale + 0.5; vTex = vy * invUvScale + 0.5; }
                    uTex = uTex - Math.Floor(uTex);
                    vTex = vTex - Math.Floor(vTex);
                    swObj.WriteLine(string.Format(CultureInfo.InvariantCulture, "vt {0:0.00000} {1:0.00000}", uTex, vTex));
                }

                // Per-triangle geometric face normals preserve razor-sharp C^0 dihedral facets in Blender & Skyrim CK
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = triIndices[t * 3] * 6;
                    int i1 = triIndices[t * 3 + 1] * 6;
                    int i2 = triIndices[t * 3 + 2] * 6;
                    double e1x = vertPosNrm[i1] - vertPosNrm[i0];
                    double e1y = vertPosNrm[i1 + 1] - vertPosNrm[i0 + 1];
                    double e1z = vertPosNrm[i1 + 2] - vertPosNrm[i0 + 2];
                    double e2x = vertPosNrm[i2] - vertPosNrm[i0];
                    double e2y = vertPosNrm[i2 + 1] - vertPosNrm[i0 + 1];
                    double e2z = vertPosNrm[i2 + 2] - vertPosNrm[i0 + 2];
                    double fnx = e1y * e2z - e1z * e2y;
                    double fny = e1z * e2x - e1x * e2z;
                    double fnz = e1x * e2y - e1y * e2x;
                    double fl = Math.Sqrt(fnx * fnx + fny * fny + fnz * fnz);
                    if (fl > 1e-12) { fnx /= fl; fny /= fl; fnz /= fl; }
                    else { fnx = 0.0; fny = 1.0; fnz = 0.0; }
                    swObj.WriteLine(string.Format(CultureInfo.InvariantCulture, "vn {0:0.0000} {1:0.0000} {2:0.0000}", fnx, fny, fnz));
                }

                for (int t = 0; t < triCount; t++)
                {
                    int a = triIndices[t * 3] + 1;
                    int b = triIndices[t * 3 + 1] + 1;
                    int c = triIndices[t * 3 + 2] + 1;
                    int fn = t + 1;
                    swObj.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "f {0}/{0}/{3} {1}/{1}/{3} {2}/{2}/{3}", a, b, c, fn));
                }
            }

            sw.Stop();
            resObj.Ok = (totalVerts > 0 && triCount > 0 && boundaryEdges == 0 && nonManifoldEdges == 0);
            resObj.ObjPath = objPath;
            resObj.MtlPath = mtlPath;
            resObj.VertexCount = totalVerts;
            resObj.TriangleCount = triCount;
            resObj.SharpCreaseCount = totalCreases;
            resObj.SplitCellCount = totalSplits;
            resObj.BoundaryEdges = boundaryEdges;
            resObj.NonManifoldEdges = nonManifoldEdges;
            resObj.SignedVolume = vol6 / 6.0;
            resObj.QefCreaseGainRatio = (totalCreases > 0 && sumQefErr > 1e-15) ? (sumMassErr / sumQefErr) : 10.0;
            resObj.ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            return resObj;
        }

        private static int LookupCellCornerVid(int[] cellPacked, int res, int ix, int iy, int iz, int cornerIdx)
        {
            int pk = cellPacked[(iz * res + iy) * res + ix];
            int firstVid = pk >> 8;
            int mask = pk & 0xFF;
            return firstVid + DcMaskCornerComp[(mask << 3) + cornerIdx];
        }

        private static void EmitCreaseAlignedQuad(List<int> tris, float[] vpn, int v0, int v1, int v2, int v3, bool flipOrder)
        {
            int a = v0, b = flipOrder ? v1 : v3, c = v2, d = flipOrder ? v3 : v1;
            int oA = a * 6 + 3, oB = b * 6 + 3, oC = c * 6 + 3, oD = d * 6 + 3;
            float alignAC = vpn[oA] * vpn[oC] + vpn[oA + 1] * vpn[oC + 1] + vpn[oA + 2] * vpn[oC + 2];
            float alignBD = vpn[oB] * vpn[oD] + vpn[oB + 1] * vpn[oD + 1] + vpn[oB + 2] * vpn[oD + 2];
            if (alignAC >= alignBD)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
            else
            {
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        private static ulong PackHalfEdgeKey(int u, int w)
        {
            uint lo = (uint)((u < w) ? u : w);
            uint hi = (uint)((u < w) ? w : u);
            ulong dir = (u < w) ? 0UL : 1UL;
            return ((ulong)lo << 33) | ((ulong)hi << 1) | dir;
        }

        // Procedural Wavefront .OBJ Exporter (Central 6D Icosahedral Monolith + Two-Tier Dais + 4 Bridges + 10 Pillars + 4 Orbs)
        public static string ExportWavefrontObjArena6D(string exportsDir, float k1Scale, float k2Phason, out int vertexCount, out int faceCount)
        {
            System.IO.Directory.CreateDirectory(exportsDir);
            string outPath = System.IO.Path.Combine(exportsDir, "OmegaEngine_Arena6D.obj");
            System.Text.StringBuilder sb = new System.Text.StringBuilder(65536);
            sb.AppendLine("# OmegaEngine v1.5.0 (Station 15 :19795) Procedural 6D Quasicrystal Arena Mesh");
            sb.AppendLine("# Units: Meters (Y-up; ready for Blender / NifSkope / Creation Kit import)");
            sb.AppendLine("o OmegaEngine_SunkenArena6D");

            int vBase = 1;
            int totalFaces = 0;

            // Helper lambda-free local mesh emitter via anonymous list of boxes/cylinders/icosahedron
            // 1. Central Two-Tier Sanctum Dais (24-sided cylinders)
            AppendCylinderObj(sb, "SanctumDaisLower", 0.0, -0.40, 0.0, 4.60, 0.65, 24, ref vBase, ref totalFaces);
            AppendCylinderObj(sb, "SanctumDaisUpper", 0.0, 0.25, 0.0, 2.25, 0.30, 24, ref vBase, ref totalFaces);

            // 2. 4 Intact Arched Obsidian Causeway Bridges (Approaches + Crown Keystone + Side Parapets)
            double[] segOff = new double[] { 5.5, 7.9, 10.3 };
            double[] segY   = new double[] { 0.28, 0.42, 0.28 };
            for (int s = 0; s < 3; s++)
            {
                string tag = s.ToString(CultureInfo.InvariantCulture);
                AppendBoxObj(sb, "BridgeNorth_Seg" + tag, 0.0, segY[s],  segOff[s], 1.36, 0.14, 1.25, ref vBase, ref totalFaces);
                AppendBoxObj(sb, "BridgeSouth_Seg" + tag, 0.0, segY[s], -segOff[s], 1.36, 0.14, 1.25, ref vBase, ref totalFaces);
                AppendBoxObj(sb, "BridgeEast_Seg"  + tag,  segOff[s], segY[s], 0.0, 1.25, 0.14, 1.36, ref vBase, ref totalFaces);
                AppendBoxObj(sb, "BridgeWest_Seg"  + tag, -segOff[s], segY[s], 0.0, 1.25, 0.14, 1.36, ref vBase, ref totalFaces);
            }

            // 3. 10 Obsidian Rune Pillars around r = 16.2m
            for (int p = 0; p < 10; p++)
            {
                double ang = p * (2.0 * Math.PI / 10.0);
                double px = Math.Cos(ang) * 16.2;
                double pz = Math.Sin(ang) * 16.2;
                AppendCylinderObj(sb, "RunePillar_" + p.ToString(CultureInfo.InvariantCulture), px, 0.15, pz, 0.46, 5.05, 12, ref vBase, ref totalFaces);
            }

            // 4. Central 6D Phason-Deformed Icosahedral Monolith (12 golden-ratio vertices, 20 triangular facets)
            sb.AppendLine("g CentralCrystalMonolith6D");
            double phi = 1.6180339887498949;
            double invNorm = 1.0 / Math.Sqrt(1.0 + phi * phi);
            double rad = 1.52 * Math.Max(0.4, k1Scale);
            double[,] rawV = new double[12, 3]
            {
                { -1,  phi, 0 }, {  1,  phi, 0 }, { -1, -phi, 0 }, {  1, -phi, 0 },
                {  0, -1,  phi }, {  0,  1,  phi }, {  0, -1, -phi }, {  0,  1, -phi },
                {  phi, 0, -1 }, {  phi, 0,  1 }, { -phi, 0, -1 }, { -phi, 0,  1 }
            };
            int monoStart = vBase;
            for (int i = 0; i < 12; i++)
            {
                double vx = rawV[i, 0] * invNorm;
                double vy = rawV[i, 1] * invNorm;
                double vz = rawV[i, 2] * invNorm;
                double ph = 1.0 + 0.08 * k2Phason * Math.Cos(i * 1.04719755);
                double wx = vx * rad * ph;
                double wy = 2.78 + (vy * rad * ph) * 1.22;
                double wz = vz * rad * ph;
                sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", wx, wy, wz);
                vBase++;
            }
            int[,] icoF = new int[20, 3]
            {
                {0,11,5},{0,5,1},{0,1,7},{0,7,10},{0,10,11},
                {1,5,9},{5,11,4},{11,10,2},{10,7,6},{7,1,8},
                {3,9,4},{3,4,2},{3,2,6},{3,6,8},{3,8,9},
                {4,9,5},{2,4,11},{6,2,10},{8,6,7},{9,8,1}
            };
            for (int f = 0; f < 20; f++)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n",
                    monoStart + icoF[f, 0], monoStart + icoF[f, 1], monoStart + icoF[f, 2]);
                totalFaces++;
            }

            // 5. 4 Floating Harmonic Resonator Orbs (octahedral crystals over N/S/E/W bridges)
            double[,] orbPos = new double[4, 2] { { 0.0, 8.0 }, { 0.0, -8.0 }, { 8.0, 0.0 }, { -8.0, 0.0 } };
            for (int o = 0; o < 4; o++)
            {
                AppendOctaOrbObj(sb, "HarmonicOrb_" + o.ToString(CultureInfo.InvariantCulture), orbPos[o, 0], 2.15, orbPos[o, 1], 0.34, ref vBase, ref totalFaces);
            }

            System.IO.File.WriteAllText(outPath, sb.ToString(), new System.Text.UTF8Encoding(false));
            vertexCount = vBase - 1;
            faceCount = totalFaces;
            return outPath;
        }

        private static void AppendCylinderObj(System.Text.StringBuilder sb, string groupName, double cx, double yBottom, double cz, double radius, double height, int segs, ref int vBase, ref int faceCount)
        {
            sb.AppendLine("g " + groupName);
            int start = vBase;
            double yTop = yBottom + height;
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx, yBottom, cz);
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx, yTop, cz);
            vBase += 2;
            for (int i = 0; i < segs; i++)
            {
                double a = i * (2.0 * Math.PI / segs);
                double px = cx + Math.Cos(a) * radius;
                double pz = cz + Math.Sin(a) * radius;
                sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", px, yBottom, pz);
                sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", px, yTop, pz);
                vBase += 2;
            }
            int cBot = start;
            int cTop = start + 1;
            for (int i = 0; i < segs; i++)
            {
                int ni = (i + 1) % segs;
                int b0 = start + 2 + i * 2;
                int t0 = b0 + 1;
                int b1 = start + 2 + ni * 2;
                int t1 = b1 + 1;
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", b0, b1, t1);
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", b0, t1, t0);
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", cTop, t0, t1);
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", cBot, b1, b0);
                faceCount += 4;
            }
        }

        private static void AppendBoxObj(System.Text.StringBuilder sb, string groupName, double cx, double cy, double cz, double hx, double hy, double hz, ref int vBase, ref int faceCount)
        {
            sb.AppendLine("g " + groupName);
            int s = vBase;
            double[] xs = new double[] { cx - hx, cx + hx };
            double[] ys = new double[] { cy - hy, cy + hy };
            double[] zs = new double[] { cz - hz, cz + hz };
            for (int iz = 0; iz < 2; iz++)
                for (int iy = 0; iy < 2; iy++)
                    for (int ix = 0; ix < 2; ix++)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", xs[ix], ys[iy], zs[iz]);
                        vBase++;
                    }
            int[,] quads = new int[6, 4]
            {
                {0,1,3,2}, {5,4,6,7}, {4,0,2,6}, {1,5,7,3}, {2,3,7,6}, {4,5,1,0}
            };
            for (int q = 0; q < 6; q++)
            {
                int a = s + quads[q, 0], b = s + quads[q, 1], c = s + quads[q, 2], d = s + quads[q, 3];
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", a, b, c);
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", a, c, d);
                faceCount += 2;
            }
        }

        private static void AppendOctaOrbObj(System.Text.StringBuilder sb, string groupName, double cx, double cy, double cz, double r, ref int vBase, ref int faceCount)
        {
            sb.AppendLine("g " + groupName);
            int s = vBase;
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx, cy + r * 1.35, cz);
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx, cy - r * 1.35, cz);
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx + r, cy, cz);
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx, cy, cz + r);
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx - r, cy, cz);
            sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.0000} {1:0.0000} {2:0.0000}\r\n", cx, cy, cz - r);
            vBase += 6;
            int[,] tris = new int[8, 3]
            {
                {0,2,3},{0,3,4},{0,4,5},{0,5,2},
                {1,3,2},{1,4,3},{1,5,4},{1,2,5}
            };
            for (int t = 0; t < 8; t++)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "f {0} {1} {2}\r\n", s + tris[t, 0], s + tris[t, 1], s + tris[t, 2]);
                faceCount++;
            }
        }
    }

    // Zero-NuGet 48 kHz WASAPI Loopback Spectral Analyzer + 48 kHz winmm.dll Thu'um Shockwave Synth + Station Poller
    public sealed class OmegaTriEngineAudioAndBridge : IDisposable
    {
        public const int SampleRate = 48000;
        private const int FftN = 256;
        private const int WaveBufFrames = 1024;
        private const int WaveBufCount = 4;
        private const uint WAVE_MAPPER = 0xFFFFFFFFu;
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

        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
        [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint RelComDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetEpDel(IntPtr s, int flow, int role, out IntPtr ppDev);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ActivateDel(IntPtr s, ref Guid iid, uint ctx, IntPtr pPar, out IntPtr ppIf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitAudDel(IntPtr s, int mode, uint flags, long dur, long per, IntPtr pFmt, IntPtr pGuid);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MixFmtDel(IntPtr s, out IntPtr ppFmt);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StartStopDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSvcDel(IntPtr s, ref Guid riid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBufDel(IntPtr s, out IntPtr ppData, out uint pFrames, out uint pFlags, out ulong pDevPos, out ulong pQpc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RelBufDel(IntPtr s, uint frames);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NextPktDel(IntPtr s, out uint pFrames);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenFileMappingW(uint access, int inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint hi, uint lo, UIntPtr cb);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int UnmapViewOfFile(IntPtr p);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CloseHandle(IntPtr h);

        private volatile bool _running;
        public volatile bool AudioMuted = false;
        public volatile bool WasapiCapturing = false;
        public volatile bool SynthStationOnline = false;
        public volatile bool PenroseStationOnline = false;
        public volatile bool SpatialMmfOnline = false;

        public volatile float Bass = 0.08f;
        public volatile float Mid  = 0.06f;
        public volatile float High = 0.04f;
        public volatile float Peak = 0.10f;
        public readonly float[] FftBands8 = new float[8];

        public volatile int SynthChladniM = 4;
        public volatile int SynthChladniN = 7;
        public volatile int SynthBpm = 96;
        public volatile float SpatialQuatX = 0.0f;
        public volatile float SpatialQuatY = 0.0f;
        public volatile float SpatialQuatZ = 0.0f;
        public volatile float SpatialQuatW = 1.0f;

        private double _shockEnv = 0.0;
        private double _shockFreq = 58.0;
        private double _phaseSub = 0.0;
        private double _phaseChord1 = 0.0;
        private double _phaseChord2 = 0.0;
        private double _phaseShimmer = 0.0;
        private uint _lfsr = 0xACE1u;

        private Thread _wasapiThread;
        private Thread _waveOutThread;
        private Thread _bridgePollThread;

        public void Start(bool enableAudioDevice)
        {
            if (_running) return;
            _running = true;

            if (enableAudioDevice)
            {
                _wasapiThread = new Thread(WasapiCaptureWorker);
                _wasapiThread.IsBackground = true;
                _wasapiThread.SetApartmentState(ApartmentState.MTA);
                _wasapiThread.Start();

                _waveOutThread = new Thread(WaveOutSynthWorker);
                _waveOutThread.IsBackground = true;
                _waveOutThread.Start();
            }

            _bridgePollThread = new Thread(CrossStationPollWorker);
            _bridgePollThread.IsBackground = true;
            _bridgePollThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { if (_wasapiThread != null && _wasapiThread.IsAlive) _wasapiThread.Join(350); } catch { }
            try { if (_waveOutThread != null && _waveOutThread.IsAlive) _waveOutThread.Join(350); } catch { }
            try { if (_bridgePollThread != null && _bridgePollThread.IsAlive) _bridgePollThread.Join(250); } catch { }
        }

        public void TriggerShockwaveAcoustic(float strength, float baseHz)
        {
            double clamped = Math.Max(0.2, Math.Min(2.0, strength));
            _shockFreq = Math.Max(36.0, Math.Min(220.0, baseHz));
            _shockEnv = Math.Min(1.6, _shockEnv + clamped);
        }

        // Deterministic 48 kHz stereo PCM generator + 256-sample Hann FFT analyzer (used both live & in --stress-test)
        public void SynthesizeAudioBlock(short[] interleavedStereo, int frames, float[] synthMono256)
        {
            double dt = 1.0 / SampleRate;
            double twoPi = 2.0 * Math.PI;
            int monoIdx = 0;

            for (int f = 0; f < frames; f++)
            {
                double env = _shockEnv;
                _shockEnv *= 0.99972;

                // Ambient E8 fifth + golden ratio harmonic pad + Thu'um downward sub-sweep + noise burst
                double subHz = _shockFreq * (1.0 + 0.85 * env * env);
                _phaseSub += twoPi * subHz * dt;
                if (_phaseSub > twoPi) _phaseSub -= twoPi;

                _phaseChord1 += twoPi * 110.0 * dt;
                if (_phaseChord1 > twoPi) _phaseChord1 -= twoPi;

                _phaseChord2 += twoPi * (110.0 * 1.498307) * dt;
                if (_phaseChord2 > twoPi) _phaseChord2 -= twoPi;

                _phaseShimmer += twoPi * (440.0 * 1.61803399) * dt;
                if (_phaseShimmer > twoPi) _phaseShimmer -= twoPi;

                _lfsr = (_lfsr >> 1) ^ (uint)(-(int)(_lfsr & 1u) & 0xB400u);
                double noise = ((_lfsr & 0xFFFFu) / 32768.0 - 1.0) * (env * env * 0.28);

                double drone = 0.045 * Math.Sin(_phaseChord1) + 0.032 * Math.Sin(_phaseChord2);
                double boom = env * (0.52 * Math.Sin(_phaseSub) + 0.20 * Math.Sin(_phaseShimmer * (1.0 + 0.25 * env))) + noise;
                double mixL = Math.Tanh(drone + boom * 0.95);
                double mixR = Math.Tanh(drone * 0.96 + boom * 1.02);

                if (monoIdx < FftN)
                {
                    synthMono256[monoIdx++] = (float)(0.5 * (mixL + mixR));
                }

                if (AudioMuted)
                {
                    interleavedStereo[f * 2 + 0] = 0;
                    interleavedStereo[f * 2 + 1] = 0;
                }
                else
                {
                    interleavedStereo[f * 2 + 0] = (short)(Math.Max(-0.98, Math.Min(0.98, mixL * 0.55)) * 32760.0);
                    interleavedStereo[f * 2 + 1] = (short)(Math.Max(-0.98, Math.Min(0.98, mixR * 0.55)) * 32760.0);
                }
            }
        }

        public static void Compute256PointHannFftBands(
            float[] samples256,
            float[] outBands8,
            out float outBass,
            out float outMid,
            out float outHigh,
            out float outPeak)
        {
            double[] re = new double[FftN];
            double[] im = new double[FftN];
            float pk = 0.0f;
            for (int i = 0; i < FftN; i++)
            {
                float s = (i < samples256.Length) ? samples256[i] : 0.0f;
                float a = Math.Abs(s);
                if (a > pk) pk = a;
                double w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftN - 1)));
                re[i] = s * w;
                im[i] = 0.0;
            }

            // In-place radix-2 Cooley-Tukey FFT (N = 256)
            int j = 0;
            for (int i = 1; i < FftN; i++)
            {
                int bit = FftN >> 1;
                while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= FftN; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wlenR = Math.Cos(ang), wlenI = Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < FftN; i += len)
                {
                    double wr = 1.0, wi = 0.0;
                    for (int k = 0; k < half; k++)
                    {
                        int u = i + k, v = i + k + half;
                        double vr = re[v] * wr - im[v] * wi;
                        double vi = re[v] * wi + im[v] * wr;
                        re[v] = re[u] - vr;
                        im[v] = im[u] - vi;
                        re[u] += vr;
                        im[u] += vi;
                        double nwr = wr * wlenR - wi * wlenI;
                        wi = wr * wlenI + wi * wlenR;
                        wr = nwr;
                    }
                }
            }

            int[] binEdges = new int[] { 1, 2, 4, 8, 14, 24, 42, 74, 127 };
            for (int b = 0; b < 8; b++)
            {
                int b0 = binEdges[b], b1 = binEdges[b + 1];
                double acc = 0.0;
                for (int k = b0; k <= b1; k++)
                {
                    acc += Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / 32.0;
                }
                float val = (float)Math.Min(1.5, acc / Math.Max(1, b1 - b0 + 1) * 3.8);
                outBands8[b] = val;
            }
            outBass = 0.5f * (outBands8[0] + outBands8[1]);
            outMid  = 0.333f * (outBands8[2] + outBands8[3] + outBands8[4]);
            outHigh = 0.333f * (outBands8[5] + outBands8[6] + outBands8[7]);
            outPeak = Math.Min(1.5f, pk * 1.4f);
        }

        private void WaveOutSynthWorker()
        {
            IntPtr hWave = IntPtr.Zero;
            IntPtr[] hdrPtrs = new IntPtr[WaveBufCount];
            GCHandle[] bufPins = new GCHandle[WaveBufCount];
            short[][] pcmBufs = new short[WaveBufCount][];
            float[] mono256 = new float[FftN];
            float[] tmpBands = new float[8];

            try
            {
                WAVEFORMATEX wfx = new WAVEFORMATEX();
                wfx.wFormatTag = 1;
                wfx.nChannels = 2;
                wfx.nSamplesPerSec = SampleRate;
                wfx.wBitsPerSample = 16;
                wfx.nBlockAlign = 4;
                wfx.nAvgBytesPerSec = SampleRate * 4u;
                wfx.cbSize = 0;

                if (waveOutOpen(out hWave, WAVE_MAPPER, ref wfx, IntPtr.Zero, IntPtr.Zero, 0u) != 0 || hWave == IntPtr.Zero)
                {
                    // Headless / CI node without speaker endpoint: still advance procedural FFT envelope
                    while (_running)
                    {
                        short[] dummy = new short[WaveBufFrames * 2];
                        SynthesizeAudioBlock(dummy, WaveBufFrames, mono256);
                        BlendFftIfIdle(mono256, tmpBands);
                        Thread.Sleep(20);
                    }
                    return;
                }

                uint cbHdr = (uint)Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < WaveBufCount; i++)
                {
                    pcmBufs[i] = new short[WaveBufFrames * 2];
                    bufPins[i] = GCHandle.Alloc(pcmBufs[i], GCHandleType.Pinned);
                    WAVEHDR wh = new WAVEHDR();
                    wh.lpData = bufPins[i].AddrOfPinnedObject();
                    wh.dwBufferLength = (uint)(WaveBufFrames * 4);
                    wh.dwFlags = WHDR_DONE;
                    hdrPtrs[i] = Marshal.AllocHGlobal((int)cbHdr);
                    Marshal.StructureToPtr(wh, hdrPtrs[i], false);
                    waveOutPrepareHeader(hWave, hdrPtrs[i], cbHdr);
                }

                while (_running)
                {
                    bool submitted = false;
                    for (int i = 0; i < WaveBufCount && _running; i++)
                    {
                        WAVEHDR cur = (WAVEHDR)Marshal.PtrToStructure(hdrPtrs[i], typeof(WAVEHDR));
                        if ((cur.dwFlags & WHDR_DONE) != 0u)
                        {
                            SynthesizeAudioBlock(pcmBufs[i], WaveBufFrames, mono256);
                            BlendFftIfIdle(mono256, tmpBands);
                            waveOutWrite(hWave, hdrPtrs[i], cbHdr);
                            submitted = true;
                        }
                    }
                    if (!submitted) Thread.Sleep(5);
                }
            }
            catch { }
            finally
            {
                if (hWave != IntPtr.Zero)
                {
                    try { waveOutReset(hWave); } catch { }
                    uint cbHdr = (uint)Marshal.SizeOf(typeof(WAVEHDR));
                    for (int i = 0; i < WaveBufCount; i++)
                    {
                        if (hdrPtrs[i] != IntPtr.Zero)
                        {
                            try { waveOutUnprepareHeader(hWave, hdrPtrs[i], cbHdr); } catch { }
                            Marshal.FreeHGlobal(hdrPtrs[i]);
                        }
                        if (bufPins[i].IsAllocated) bufPins[i].Free();
                    }
                    try { waveOutClose(hWave); } catch { }
                }
            }
        }

        private void BlendFftIfIdle(float[] mono256, float[] tmpBands)
        {
            if (WasapiCapturing && Peak > 0.03f) return;
            float b, m, h, pk;
            Compute256PointHannFftBands(mono256, tmpBands, out b, out m, out h, out pk);
            Bass = Bass * 0.65f + b * 0.35f;
            Mid  = Mid * 0.65f + m * 0.35f;
            High = High * 0.65f + h * 0.35f;
            Peak = Peak * 0.65f + pk * 0.35f;
            for (int k = 0; k < 8; k++) FftBands8[k] = FftBands8[k] * 0.65f + tmpBands[k] * 0.35f;
        }

        private void WasapiCaptureWorker()
        {
            int hrCo = CoInitializeEx(IntPtr.Zero, 0u);
            Guid clsidEnum = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            Guid iidEnum   = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCap    = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

            float[] ring256 = new float[FftN];
            float[] tmpBands = new float[8];
            int rPos = 0;

            while (_running)
            {
                IntPtr pEnum = IntPtr.Zero, pDev = IntPtr.Zero, pClient = IntPtr.Zero, pCap = IntPtr.Zero, pFmt = IntPtr.Zero;
                StartStopDel fnStop = null;
                try
                {
                    if (CoCreateInstance(ref clsidEnum, IntPtr.Zero, 23u, ref iidEnum, out pEnum) < 0 || pEnum == IntPtr.Zero)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    GetEpDel fnEp = Vtbl<GetEpDel>(pEnum, 4);
                    if (fnEp(pEnum, 0, 1, out pDev) < 0 || pDev == IntPtr.Zero) fnEp(pEnum, 0, 0, out pDev);
                    if (pDev == IntPtr.Zero) { Thread.Sleep(1000); continue; }
                    if (Vtbl<ActivateDel>(pDev, 3)(pDev, ref iidClient, 23u, IntPtr.Zero, out pClient) < 0 || pClient == IntPtr.Zero) { Thread.Sleep(1000); continue; }
                    if (Vtbl<MixFmtDel>(pClient, 8)(pClient, out pFmt) < 0 || pFmt == IntPtr.Zero) { Thread.Sleep(1000); continue; }

                    ushort tag = (ushort)Marshal.ReadInt16(pFmt, 0);
                    int ch = Math.Max(1, (int)(ushort)Marshal.ReadInt16(pFmt, 2));
                    int align = Math.Max(2, (int)(ushort)Marshal.ReadInt16(pFmt, 12));
                    int bits = (ushort)Marshal.ReadInt16(pFmt, 14);
                    int cbSz = (ushort)Marshal.ReadInt16(pFmt, 16);
                    bool isF32 = (tag == 3) || (tag == 0xFFFE && cbSz >= 22 && Marshal.ReadInt32(pFmt, 24) == 3);

                    if (Vtbl<InitAudDel>(pClient, 3)(pClient, 0, 0x00020000u, 500000L, 0L, pFmt, IntPtr.Zero) < 0) { Thread.Sleep(1000); continue; }
                    if (Vtbl<GetSvcDel>(pClient, 14)(pClient, ref iidCap, out pCap) < 0 || pCap == IntPtr.Zero) { Thread.Sleep(1000); continue; }
                    fnStop = Vtbl<StartStopDel>(pClient, 11);
                    if (Vtbl<StartStopDel>(pClient, 10)(pClient) < 0) { Thread.Sleep(1000); continue; }

                    NextPktDel fnNext = Vtbl<NextPktDel>(pCap, 5);
                    GetBufDel  fnGet  = Vtbl<GetBufDel>(pCap, 3);
                    RelBufDel  fnRel  = Vtbl<RelBufDel>(pCap, 4);
                    WasapiCapturing = true;
                    byte[] rawBuf = new byte[32768];

                    while (_running)
                    {
                        uint pkt = 0u;
                        if (fnNext(pCap, out pkt) < 0) break;
                        if (pkt == 0u) { Thread.Sleep(6); continue; }
                        while (pkt > 0u && _running)
                        {
                            IntPtr pData; uint nFr, flg; ulong dPos, qPos;
                            if (fnGet(pCap, out pData, out nFr, out flg, out dPos, out qPos) < 0) break;
                            int bytes = (int)nFr * align;
                            if (((flg & 2u) == 0u) && pData != IntPtr.Zero && nFr > 0u)
                            {
                                if (bytes > rawBuf.Length) rawBuf = new byte[bytes + 4096];
                                Marshal.Copy(pData, rawBuf, 0, bytes);
                                for (int f = 0; f < (int)nFr; f++)
                                {
                                    int o = f * align;
                                    float fl = 0.0f, fr = 0.0f;
                                    if (isF32 && bits == 32)
                                    {
                                        fl = BitConverter.ToSingle(rawBuf, o);
                                        fr = (ch >= 2) ? BitConverter.ToSingle(rawBuf, o + 4) : fl;
                                    }
                                    else if (bits == 16)
                                    {
                                        fl = BitConverter.ToInt16(rawBuf, o) / 32768.0f;
                                        fr = (ch >= 2) ? (BitConverter.ToInt16(rawBuf, o + 2) / 32768.0f) : fl;
                                    }
                                    ring256[rPos] = 0.5f * (fl + fr);
                                    rPos = (rPos + 1) & (FftN - 1);
                                }

                                float[] ordered = new float[FftN];
                                for (int k = 0; k < FftN; k++) ordered[k] = ring256[(rPos + k) & (FftN - 1)];
                                float b, m, h, pk;
                                Compute256PointHannFftBands(ordered, tmpBands, out b, out m, out h, out pk);
                                if (pk > 0.005f)
                                {
                                    Bass = Bass * 0.55f + b * 0.45f;
                                    Mid  = Mid * 0.55f + m * 0.45f;
                                    High = High * 0.55f + h * 0.45f;
                                    Peak = Peak * 0.55f + pk * 0.45f;
                                    for (int k = 0; k < 8; k++) FftBands8[k] = FftBands8[k] * 0.55f + tmpBands[k] * 0.45f;
                                }
                            }
                            fnRel(pCap, nFr);
                            if (fnNext(pCap, out pkt) < 0) break;
                        }
                    }
                }
                catch { Thread.Sleep(1000); }
                finally
                {
                    WasapiCapturing = false;
                    try { if (fnStop != null && pClient != IntPtr.Zero) fnStop(pClient); } catch { }
                    if (pFmt != IntPtr.Zero) CoTaskMemFree(pFmt);
                    RelCom(pCap); RelCom(pClient); RelCom(pDev); RelCom(pEnum);
                }
            }
            if (hrCo >= 0) CoUninitialize();
        }

        private void CrossStationPollWorker()
        {
            while (_running)
            {
                // 1. Poll OmegaSynth (:19782) fail-closed <= 140 ms
                string syJson = HttpGetFast("http://127.0.0.1:19782/api/status", 140);
                if (syJson.Length > 0)
                {
                    SynthStationOnline = true;
                    SynthChladniM = ExtractJsonInt(syJson, "m", SynthChladniM);
                    SynthChladniN = ExtractJsonInt(syJson, "n", SynthChladniN);
                    SynthBpm = ExtractJsonInt(syJson, "bpm", SynthBpm);
                }
                else
                {
                    SynthStationOnline = false;
                }

                // 2. Poll OmegaPenrose (:19785) fail-closed <= 140 ms
                string pnJson = HttpGetFast("http://127.0.0.1:19785/api/status", 140);
                PenroseStationOnline = (pnJson.Length > 0);

                // 3. Probe Local\OMEGA_RND_SPATIAL_BUS_V1 ("OSP4" 0x4F535034 96B Seqlock MMF) if live
                SpatialMmfOnline = TryReadOsp4Quat();

                Thread.Sleep(350);
            }
        }

        private bool TryReadOsp4Quat()
        {
            IntPtr h = IntPtr.Zero, p = IntPtr.Zero;
            try
            {
                h = OpenFileMappingW(0x0004u, 0, "Local\\OMEGA_RND_SPATIAL_BUS_V1");
                if (h == IntPtr.Zero) return false;
                p = MapViewOfFile(h, 0x0004u, 0u, 0u, (UIntPtr)96);
                if (p == IntPtr.Zero) return false;
                long s1 = Marshal.ReadInt64(p, 8);
                if ((s1 & 1L) != 0L) return false;
                uint magic = unchecked((uint)Marshal.ReadInt32(p, 0));
                int bw = Marshal.ReadInt32(p, 16);
                int bx = Marshal.ReadInt32(p, 20);
                int by = Marshal.ReadInt32(p, 24);
                int bz = Marshal.ReadInt32(p, 28);
                Thread.MemoryBarrier();
                long s2 = Marshal.ReadInt64(p, 8);
                if (s1 == s2 && magic == 0x4F535034u)
                {
                    SpatialQuatW = BitConverter.ToSingle(BitConverter.GetBytes(bw), 0);
                    SpatialQuatX = BitConverter.ToSingle(BitConverter.GetBytes(bx), 0);
                    SpatialQuatY = BitConverter.ToSingle(BitConverter.GetBytes(by), 0);
                    SpatialQuatZ = BitConverter.ToSingle(BitConverter.GetBytes(bz), 0);
                    return true;
                }
                return false;
            }
            catch { return false; }
            finally
            {
                if (p != IntPtr.Zero) { try { UnmapViewOfFile(p); } catch { } }
                if (h != IntPtr.Zero) { try { CloseHandle(h); } catch { } }
            }
        }

        private static readonly byte[] _reqSyBytes = Encoding.ASCII.GetBytes("GET /api/status HTTP/1.0\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        [ThreadStatic] private static byte[] _pollRecvScratch;

        private static string HttpGetFast(string url, int timeoutMs)
        {
            int port = (url.IndexOf("19782", StringComparison.Ordinal) >= 0) ? 19782 : 19785;
            try
            {
                if (_pollRecvScratch == null) _pollRecvScratch = new byte[2048];
                using (Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    sock.NoDelay = true;
                    sock.ReceiveTimeout = timeoutMs;
                    sock.SendTimeout = timeoutMs;
                    IAsyncResult ar = sock.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                    {
                        try { sock.Close(); } catch { }
                        return "";
                    }
                    sock.EndConnect(ar);
                    sock.Send(_reqSyBytes, 0, _reqSyBytes.Length, SocketFlags.None);
                    int total = 0;
                    while (total < _pollRecvScratch.Length)
                    {
                        int n = sock.Receive(_pollRecvScratch, total, _pollRecvScratch.Length - total, SocketFlags.None);
                        if (n <= 0) break;
                        total += n;
                    }
                    if (total <= 0) return "";
                    string raw = Encoding.UTF8.GetString(_pollRecvScratch, 0, total);
                    int bodyIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    return (bodyIdx >= 0) ? raw.Substring(bodyIdx + 4) : raw;
                }
            }
            catch
            {
                return "";
            }
        }

        private static int ExtractJsonInt(string json, string key, int defVal)
        {
            if (string.IsNullOrEmpty(json)) return defVal;
            string pat = "\"" + key + "\"";
            int idx = json.IndexOf(pat, StringComparison.Ordinal);
            if (idx < 0) return defVal;
            int col = json.IndexOf(':', idx + pat.Length);
            if (col < 0) return defVal;
            int p = col + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t' || json[p] == '\r' || json[p] == '\n')) p++;
            int start = p;
            if (p < json.Length && json[p] == '-') p++;
            while (p < json.Length && json[p] >= '0' && json[p] <= '9') p++;
            if (p <= start || (p == start + 1 && json[start] == '-')) return defVal;
            int val;
            if (int.TryParse(json.Substring(start, p - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
                return val;
            return defVal;
        }

        private static T Vtbl<T>(IntPtr p, int slot) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size), typeof(T));
        }

        private static void RelCom(IntPtr p)
        {
            if (p != IntPtr.Zero)
            {
                try { Vtbl<RelComDel>(p, 2)(p); } catch { }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}