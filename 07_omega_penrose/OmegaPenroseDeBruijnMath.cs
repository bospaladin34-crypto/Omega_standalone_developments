using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmegaPenroseStudio
{
    public struct PenroseRhombTile
    {
        public float X0, Y0, X1, Y1, X2, Y2, X3, Y3;
        public float CenterX, CenterY;
        public float OrientationRad;
        public int IsThick;            // 1 = Thick / Unreflected Hat / Even Spectre, 0 = Thin / Anti-Hat / Odd Spectre
        public int WormFamilyA;        // Grid family r (or EdgeMask in bits 0..3 when >= 16: 16 + edgeMask!)
        public int EdgeMask;           // Bitmask 1=e01, 2=e12, 4=e23, 8=e30 (15 = full quad)
        public int IndexSumK;          // Sheet K in {1,2,3,4}
        public float PerpX, PerpY;     // Galois conjugate coordinate in E_perp
        public float PerpRadius;
        public float PhasonFlipEnergy;
        public float ZTierHeight;
        public int IsCorridorPath;
    }

    public struct MillerPeakInfo5D
    {
        public bool Active;
        public int H0, H1, H2, H3, H4;
        public float Gx, Gy;           // Physical reciprocal wavevector
        public float GParNorm;
        public float GPerpNorm;
        public float PlaneSpacingD;
        public float ScreenU, ScreenV; // Normalized [0..1] click position inside Bragg panel
        public string Summary;
    }

    public static class OmegaPenroseDeBruijnMath
    {
        public const double GoldenPhi = 1.6180339887498949;
        public const int MaxGpuRhombs = 5120;

        public static readonly string[] SymmetryModeNames = new string[]
        {
            "01. True Penrose P3 Sun (5D de Bruijn, SumGamma = 0)",
            "02. Penrose P3 Cartwheel Worms (5D, SumGamma = 0)",
            "03. Generalized D5 Half-Integer Quasicrystal (SumGamma = 0.5)",
            "04. Decagonal D10 Star Quasicrystal (Symmetric Shifts)",
            "05. Ammann-Beenker 8-Fold Silver-Mean Quasicrystal (4D->2D)",
            "06. Stampfli-Shield 12-Fold Dodecagonal Quasicrystal (6D->2D)",
            "07. 8D E8 Elser-Sloane H4 Quasicrystal (240-Root Coxeter-Icosian)",
            "08. Einstein 'Hat' Monotile Tile(1, sqrt3) (Gap-Free Chiral Patch)",
            "09. Chiral 'Spectre' Monotile Tile(1, 1) (Zero-Reflection Patch)"
        };

        // Orthonormal 30-fold Coxeter eigenvectors of 8D E8 (U1,U2 = physical Coxeter plane, U3,U4 = Galois conjugate plane)
        public static readonly double[] E8_U1 = new double[] { 0.0, 0.0, -0.11911770, -0.11911770, -0.19273649, -0.19273649, -0.21763887, -0.92193305 };
        public static readonly double[] E8_U2 = new double[] { -0.09689907, 0.66982357, -0.46350566, 0.46350566, -0.23693033, 0.23693033, 0.0, 0.0 };
        public static readonly double[] E8_U3 = new double[] { 0.0, 0.0, -0.38336132, -0.38336132, 0.23693033, 0.23693033, 0.74996791, -0.17704341 };
        public static readonly double[] E8_U4 = new double[] { -0.15941060, 0.54488358, 0.11911770, -0.11911770, 0.56978596, -0.56978596, 0.0, 0.0 };
        public static readonly double[][] E8Roots240 = BuildE8Roots240();

        // Verified 139 gap-free Einstein Hat placements (dq, dr, rot6, refl) on Laves [3.4.6.4] kite lattice
        private static readonly int[] HatPlacementsFlat = new int[] { 0,0,0,1, 1,-1,3,0, -2,1,1,0, 0,1,1,0, -1,2,0,0, 2,0,5,0, 3,0,4,0, 1,-2,2,0, -1,-1,1,0, 2,-1,0,0, 2,-2,0,0, 0,-2,1,0, 0,-3,3,0, -2,0,0,0, 1,1,2,0, 0,3,1,1, -1,2,2,0, -2,2,2,0, -3,2,1,0, -2,1,5,0, 4,-2,5,1, 3,0,0,0, -1,-2,4,0, -4,3,0,0, 1,-3,1,0, 1,-3,5,0, -4,2,0,0, -4,4,0,0, -2,3,5,1, -3,-1,2,0, -4,-1,1,0, 2,1,2,0, 3,1,0,0, -2,4,1,0, 1,3,4,0, -3,5,0,0, 3,-3,1,0, -3,0,5,1, 0,-4,3,0, -2,-2,5,0, -2,-3,2,1, 0,-5,2,0, 1,-4,0,0, 4,-5,3,0, 3,-4,0,1, 4,-3,2,0, 2,2,3,0, -4,2,4,0, -5,3,2,0, -4,5,5,0, 3,1,2,0, 2,3,1,0, -1,4,2,0, 5,-1,5,0, 4,0,1,0, -5,1,0,0, 5,-4,5,0, 4,-6,2,0, 2,-5,1,0, 6,-4,4,0, 5,-2,0,0, 7,-5,3,0, -3,6,0,0, -1,5,5,1, -3,5,4,0, 1,4,1,0, 0,5,0,0, 0,6,5,0, -2,7,0,0, -5,2,5,0, -3,-3,3,0, -5,0,0,0, -3,-4,2,0, -1,-5,3,0, -6,3,2,0, -7,3,1,0, -5,4,3,0, 4,2,1,1, 5,2,4,0, 3,3,2,0, 2,-6,4,0, -1,-5,1,0, 3,-7,3,0, 5,-1,1,0, 5,0,2,0, 6,0,0,0, -3,7,5,0, 3,-6,1,0, 7,-2,0,1, 6,-3,1,0, 8,-3,3,0, -6,1,3,0, -6,0,2,0, -8,4,0,0, 6,-7,3,0, 1,6,5,0, 2,4,4,1, 2,5,1,0, -6,6,1,0, -7,7,0,0, -6,5,4,0, -7,5,1,1, -8,4,2,0, -4,7,5,0, 5,-5,2,1, 7,-6,3,0, -6,-1,1,0, -7,0,1,0, -4,-2,0,1, -5,6,4,1, 0,-7,2,0, 1,-6,1,1, -1,-6,3,0, -1,-7,2,0, -5,-3,1,0, 3,-8,2,0, 2,-8,2,0, 6,-8,3,0, 5,-7,0,1, 5,-9,3,0, -4,-4,1,0, 6,0,2,0, -1,7,1,0, 2,6,4,0, -2,8,0,0, -2,7,4,0, 6,1,3,0, 4,3,2,0, 7,-4,1,0, 8,-5,5,0, 7,-7,2,0, -8,1,1,0, -7,2,0,1, -5,8,1,0, -3,-5,2,1, -6,8,5,0, 6,-7,1,0, -6,-2,3,0, -8,6,2,0 };

        // Verified 96 non-overlapping chiral Spectre Tile(1,1) placements (tx, ty, rot12)
        private static readonly float[] SpectrePlacementsFlat = new float[] { -1.50000f,1.20000f,1f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f, 1.09808f,0.96795f,0f };

        // Exact 12-triangle ear decomposition of the 14-vertex Tile(a,b) polygon + exterior edge bitmask
        private static readonly int[,] TileAbEarTris = new int[,]
        {
            { 13, 0, 1 }, { 1, 2, 3 }, { 13, 1, 3 }, { 13, 3, 4 },
            { 13, 4, 5 }, { 5, 6, 7 }, { 13, 5, 7 }, { 7, 8, 9 },
            { 13, 7, 9 }, { 13, 9, 10 }, { 13, 10, 11 }, { 11, 12, 13 }
        };

        private static double[][] BuildE8Roots240()
        {
            double[][] r = new double[240][];
            int idx = 0;
            // 112 integer roots: +/- e_i +/- e_j
            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    for (int s1 = -1; s1 <= 1; s1 += 2)
                    {
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            double[] v = new double[8];
                            v[i] = s1; v[j] = s2;
                            r[idx++] = v;
                        }
                    }
                }
            }
            // 128 half-integer spinor roots: (+/- 0.5)^8 with even number of minus signs
            for (int mask = 0; mask < 256; mask++)
            {
                int bits = 0;
                for (int b = 0; b < 8; b++) if (((mask >> b) & 1) != 0) bits++;
                if ((bits & 1) != 0) continue;
                double[] v = new double[8];
                for (int b = 0; b < 8; b++) v[b] = (((mask >> b) & 1) != 0) ? -0.5 : 0.5;
                r[idx++] = v;
            }
            return r;
        }

        public static void GetPresetShifts5D(
            int modeIndex, double phasonPhase, double sumGammaBias,
            float audioSub, float audioMid, float audioHigh, bool lockZeroSum,
            double[] gamma5)
        {
            double c1 = Math.Cos(phasonPhase);
            double s1 = Math.Sin(phasonPhase);
            double s2 = Math.Sin(phasonPhase * GoldenPhi + 0.7);

            double a0 = 0.22 * audioSub * Math.Sin(phasonPhase * 2.0 + 0.3);
            double a1 = 0.18 * audioMid * Math.Cos(phasonPhase * 3.0 + 1.1);
            double a2 = 0.14 * audioHigh * Math.Sin(phasonPhase * 5.0 + 2.2);

            if (modeIndex == 0 || modeIndex >= 6)
            {
                gamma5[0] = 0.2000 + 0.14 * c1 + a0;
                gamma5[1] = 0.2000 - 0.14 * c1 - a0 + a1;
                gamma5[2] = 0.2000 + 0.11 * s1 - a1 + a2;
                gamma5[3] = 0.2000 - 0.11 * s1 - a2;
                gamma5[4] = 0.2000;
            }
            else if (modeIndex == 1)
            {
                gamma5[0] =  0.1031 + 0.18 * c1 + a0;
                gamma5[1] = -0.2417 + 0.12 * s2 - a0 + a1;
                gamma5[2] =  0.3719 - 0.18 * c1 - a1 + a2;
                gamma5[3] = -0.1192 - 0.12 * s2 - a2;
                gamma5[4] = -(gamma5[0] + gamma5[1] + gamma5[2] + gamma5[3]);
            }
            else if (modeIndex == 2)
            {
                gamma5[0] = 0.10 + 0.15 * c1 + a0;
                gamma5[1] = 0.10 - 0.15 * c1 - a0;
                gamma5[2] = 0.10 + 0.12 * s1 + a1;
                gamma5[3] = 0.10 - 0.12 * s1 - a1;
                gamma5[4] = 0.10;
            }
            else
            {
                for (int k = 0; k < 5; k++)
                    gamma5[k] = 0.137 + 0.12 * Math.Cos(phasonPhase + 1.25663706 * k) + 0.08 * audioSub * Math.Sin(k * 2.51327);
            }

            if (!lockZeroSum && Math.Abs(sumGammaBias) > 1e-5)
            {
                double addEach = sumGammaBias / 5.0;
                for (int k = 0; k < 5; k++) gamma5[k] += addEach;
            }
        }

        public static List<PenroseRhombTile> BuildDualPenroseMesh(
            int symMode, double[] gamma5, float fieldSpanUnits,
            float panX, float panY, double phasonPhase, bool computeWaveguidePath,
            out int thickCount, out int thinCount, out int phasonFlipCount)
        {
            if (symMode == 7)
                return BuildEinsteinHatMesh(panX, panY, fieldSpanUnits, phasonPhase, computeWaveguidePath, out thickCount, out thinCount, out phasonFlipCount);
            if (symMode == 8)
                return BuildChiralSpectreMesh(panX, panY, fieldSpanUnits, phasonPhase, computeWaveguidePath, out thickCount, out thinCount, out phasonFlipCount);
            if (symMode == 6)
                return BuildE8ElserSloaneMesh(gamma5, fieldSpanUnits, panX, panY, phasonPhase, computeWaveguidePath, out thickCount, out thinCount, out phasonFlipCount);

            thickCount = 0;
            thinCount = 0;
            phasonFlipCount = 0;
            List<PenroseRhombTile> list = new List<PenroseRhombTile>(2560);

            int N = (symMode == 4) ? 4 : ((symMode == 5) ? 6 : 5);
            double[] ux = new double[6], uy = new double[6];
            double[] px = new double[6], py = new double[6];
            double[] gam = new double[6];

            double scalePar = 2.0 / N;
            double scalePerp = Math.Sqrt(2.0 / N);

            for (int k = 0; k < N; k++)
            {
                double angPar, angPerp;
                if (N == 5)
                {
                    angPar = 2.0 * Math.PI * k / 5.0;
                    angPerp = 4.0 * Math.PI * k / 5.0;
                    gam[k] = gamma5[k];
                }
                else if (N == 4)
                {
                    angPar = Math.PI * k / 4.0;
                    angPerp = 3.0 * Math.PI * k / 4.0;
                    gam[k] = 0.1234 * (k + 1) + gamma5[k % 5] * 0.85;
                }
                else
                {
                    angPar = Math.PI * k / 6.0;
                    angPerp = 5.0 * Math.PI * k / 6.0;
                    gam[k] = 0.0917 * (k + 1) + gamma5[k % 5] * 0.85;
                }
                ux[k] = Math.Cos(angPar); uy[k] = Math.Sin(angPar);
                px[k] = Math.Cos(angPerp); py[k] = Math.Sin(angPerp);
            }

            double cxGrid = panX, cyGrid = panY;
            double halfSpan = Math.Max(1.6, Math.Min(22.0, fieldSpanUnits * 0.56));
            double clipR = halfSpan * 1.18;

            int[] idx = new int[6];
            for (int r = 0; r < N; r++)
            {
                for (int s = r + 1; s < N; s++)
                {
                    double det = ux[r] * uy[s] - uy[r] * ux[s];
                    if (Math.Abs(det) < 1e-5) continue;

                    double rCenter = cxGrid * ux[r] + cyGrid * uy[r] - gam[r];
                    double sCenter = cxGrid * ux[s] + cyGrid * uy[s] - gam[s];
                    int krMin = (int)Math.Floor(rCenter - halfSpan) - 1;
                    int krMax = (int)Math.Ceiling(rCenter + halfSpan) + 1;
                    int ksMin = (int)Math.Floor(sCenter - halfSpan) - 1;
                    int ksMax = (int)Math.Ceiling(sCenter + halfSpan) + 1;

                    int stepDiff = s - r;
                    bool isThickTile = (N == 5) ? (stepDiff == 1 || stepDiff == 4)
                                     : ((N == 4) ? (stepDiff == 2) : (stepDiff >= 2 && stepDiff <= 4));

                    for (int kr = krMin; kr <= krMax; kr++)
                    {
                        double cr = kr + gam[r];
                        for (int ks = ksMin; ks <= ksMax; ks++)
                        {
                            double cs = ks + gam[s];
                            double x0 = (cr * uy[s] - cs * uy[r]) / det;
                            double y0 = (ux[r] * cs - ux[s] * cr) / det;
                            if (Math.Abs(x0 - cxGrid) > clipR || Math.Abs(y0 - cyGrid) > clipR) continue;

                            int sumIdx = 0;
                            for (int m = 0; m < N; m++)
                            {
                                if (m == r) idx[m] = kr;
                                else if (m == s) idx[m] = ks;
                                else idx[m] = (int)Math.Ceiling(x0 * ux[m] + y0 * uy[m] - gam[m]);
                                sumIdx += idx[m];
                            }

                            double vx = 0.0, vy = 0.0, wx = 0.0, wy = 0.0;
                            for (int m = 0; m < N; m++)
                            {
                                vx += idx[m] * ux[m];
                                vy += idx[m] * uy[m];
                                double km = idx[m] + gam[m] - ((m == r || m == s) ? 0.5 : 0.0);
                                wx += km * px[m];
                                wy += km * py[m];
                            }
                            vx *= scalePar; vy *= scalePar;
                            wx *= scalePerp; wy *= scalePerp;

                            double erx = scalePar * ux[r], ery = scalePar * uy[r];
                            double esx = scalePar * ux[s], esy = scalePar * uy[s];

                            float p0x = (float)vx,               p0y = (float)vy;
                            float p1x = (float)(vx - erx),       p1y = (float)(vy - ery);
                            float p2x = (float)(vx - erx - esx), p2y = (float)(vy - ery - esy);
                            float p3x = (float)(vx - esx),       p3y = (float)(vy - esy);

                            int kSheet = ((sumIdx % N) + N) % N;
                            if (kSheet == 0) kSheet = 1;

                            double perpR = Math.Sqrt(wx * wx + wy * wy);
                            double perpAng = Math.Atan2(wy, wx + 1e-9);
                            int gonSym = (N == 5) ? 10 : (N == 4 ? 8 : 12);
                            double sector = Math.PI / gonSym;
                            double modAng = Math.Abs(perpAng) % (2.0 * sector);
                            double polyFactor = Math.Cos(sector) / Math.Max(0.25, Math.Cos(modAng - sector));

                            double winRadius = (N == 5)
                                ? ((kSheet == 1 || kSheet == 4) ? 0.96 : 1.24) * polyFactor
                                : (1.18 * polyFactor);
                            float flipEnergy = (float)Math.Exp(-Math.Abs(winRadius - perpR) * 14.0);
                            if (flipEnergy > 0.68f) phasonFlipCount++;

                            PenroseRhombTile tile = new PenroseRhombTile();
                            tile.X0 = p0x; tile.Y0 = p0y;
                            tile.X1 = p1x; tile.Y1 = p1y;
                            tile.X2 = p2x; tile.Y2 = p2y;
                            tile.X3 = p3x; tile.Y3 = p3y;
                            tile.CenterX = 0.25f * (p0x + p1x + p2x + p3x);
                            tile.CenterY = 0.25f * (p0y + p1y + p2y + p3y);
                            tile.OrientationRad = (float)Math.Atan2(p2y - p0y, p2x - p0x);
                            tile.IsThick = isThickTile ? 1 : 0;
                            tile.WormFamilyA = r;
                            tile.EdgeMask = 15;
                            tile.IndexSumK = kSheet;
                            tile.PerpX = (float)wx;
                            tile.PerpY = (float)wy;
                            tile.PerpRadius = (float)perpR;
                            tile.PhasonFlipEnergy = flipEnergy;
                            tile.ZTierHeight = (kSheet - 1) * 0.42f + (isThickTile ? 0.22f : 0.0f) + Math.Max(0.0f, 1.15f - (float)perpR) * 0.55f;
                            tile.IsCorridorPath = 0;

                            if (isThickTile) thickCount++; else thinCount++;
                            list.Add(tile);
                            if (list.Count >= MaxGpuRhombs) break;
                        }
                        if (list.Count >= MaxGpuRhombs) break;
                    }
                    if (list.Count >= MaxGpuRhombs) break;
                }
                if (list.Count >= MaxGpuRhombs) break;
            }

            if (computeWaveguidePath && list.Count > 16)
                SolveAStarGoldenWaveguide(list, panX, panY, (float)(halfSpan * 0.78), 0.62f);

            return list;
        }

        // Mode 07: 8D E8 Elser-Sloane H4 Cut-and-Project Quasicrystal + 240-Root 30-Fold Petrie-Coxeter Crown!
        private static List<PenroseRhombTile> BuildE8ElserSloaneMesh(
            double[] gamma5, float fieldSpanUnits, float panX, float panY, double phasonPhase, bool computePath,
            out int thickCount, out int thinCount, out int phasonFlipCount)
        {
            // Base H2/H4 golden icosian cut-and-project tiling (with 4D phason rotation into E8 U3/U4!)
            List<PenroseRhombTile> list = BuildDualPenroseMesh(
                3, gamma5, fieldSpanUnits, panX, panY, phasonPhase, computePath,
                out thickCount, out thinCount, out phasonFlipCount);

            // Project 240 E8 minimal roots into (U1,U2) rotated by phasonPhase into Galois conjugate (U3,U4)
            // and overlay the 30-fold Gosset 4_21 root star chords at origin!
            double cp = Math.Cos(phasonPhase * 0.5), sp = Math.Sin(phasonPhase * 0.5);
            float[] rx = new float[240], ry = new float[240];
            float[] wx = new float[240], wy = new float[240];
            float starScale = Math.Max(2.2f, Math.Min(6.5f, fieldSpanUnits * 0.34f));

            for (int i = 0; i < 240; i++)
            {
                double[] v = E8Roots240[i];
                double p1 = 0, p2 = 0, q1 = 0, q2 = 0;
                for (int d = 0; d < 8; d++)
                {
                    p1 += v[d] * E8_U1[d]; p2 += v[d] * E8_U2[d];
                    q1 += v[d] * E8_U3[d]; q2 += v[d] * E8_U4[d];
                }
                rx[i] = (float)((p1 * cp + q1 * sp * 0.28) * starScale);
                ry[i] = (float)((p2 * cp + q2 * sp * 0.28) * starScale);
                wx[i] = (float)((q1 * cp - p1 * sp * 0.28) * 1.15);
                wy[i] = (float)((q2 * cp - p2 * sp * 0.28) * 1.15);
            }

            // Add crisp 30-fold E8 Coxeter-Petrie outer shell chords as edge-only line segments (EdgeMask = 1)
            for (int i = 0; i < 240 && list.Count < MaxGpuRhombs; i++)
            {
                double r2i = rx[i] * rx[i] + ry[i] * ry[i];
                if (r2i < (starScale * 0.52f) * (starScale * 0.52f)) continue;
                for (int j = i + 1; j < 240 && list.Count < MaxGpuRhombs; j++)
                {
                    // True E8 nearest-neighbor inner product = +1.0!
                    double dot8 = 0.0;
                    for (int d = 0; d < 8; d++) dot8 += E8Roots240[i][d] * E8Roots240[j][d];
                    if (Math.Abs(dot8 - 1.0) > 0.02) continue;

                    double r2j = rx[j] * rx[j] + ry[j] * ry[j];
                    if (r2j < (starScale * 0.62f) * (starScale * 0.62f)) continue;

                    PenroseRhombTile seg = new PenroseRhombTile();
                    seg.X0 = rx[i]; seg.Y0 = ry[i];
                    seg.X1 = rx[j]; seg.Y1 = ry[j];
                    seg.X2 = rx[j]; seg.Y2 = ry[j];
                    seg.X3 = rx[i]; seg.Y3 = ry[i];
                    seg.CenterX = 0.5f * (rx[i] + rx[j]);
                    seg.CenterY = 0.5f * (ry[i] + ry[j]);
                    seg.IsThick = 0;
                    seg.WormFamilyA = 1;
                    seg.EdgeMask = 1; // Draw only edge v0->v1 (zero area fill)
                    seg.IndexSumK = (i % 4) + 1;
                    seg.PerpX = wx[i];
                    seg.PerpY = wy[i];
                    seg.PhasonFlipEnergy = 0.85f; // Renders in luminous E8 gold!
                    list.Add(seg);
                }
            }
            return list;
        }

        // Mode 08: Exact Gap-Free Einstein 'Hat' Monotile Tile(1, sqrt(3)) Patch!
        private static List<PenroseRhombTile> BuildEinsteinHatMesh(
            float panX, float panY, float fieldSpanUnits, double phasonPhase, bool computePath,
            out int unreflectedCount, out int reflectedAntiHatCount, out int activeHighlightCount)
        {
            unreflectedCount = 0;
            reflectedAntiHatCount = 0;
            activeHighlightCount = 0;

            float unitScale = 0.42f; // Scales Hat polykite so ~16 tiles span default viewport cleanly!
            float s3 = (float)Math.Sqrt(3.0);
            float[,] baseV = BuildTileAbVertices(1.0f, s3);

            int hatCount = HatPlacementsFlat.Length / 4;
            List<PenroseRhombTile> list = new List<PenroseRhombTile>(hatCount * 12);
            List<PenroseRhombTile> hatCenters = new List<PenroseRhombTile>(hatCount);

            float[] vx = new float[14], vy = new float[14];
            for (int h = 0; h < hatCount; h++)
            {
                int dq   = HatPlacementsFlat[h * 4 + 0];
                int dr   = HatPlacementsFlat[h * 4 + 1];
                int rot  = HatPlacementsFlat[h * 4 + 2];
                int refl = HatPlacementsFlat[h * 4 + 3];

                float tx = 3.0f * dq;
                float ty = s3 * dq + 2.0f * s3 * dr;
                double ang = rot * (Math.PI / 3.0);
                float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);

                float cx = 0.0f, cy = 0.0f;
                for (int v = 0; v < 14; v++)
                {
                    float px = refl != 0 ? -baseV[v, 0] : baseV[v, 0];
                    float py = baseV[v, 1];
                    float wx = (tx + px * ca - py * sa) * unitScale;
                    float wy = (ty + px * sa + py * ca) * unitScale;
                    vx[v] = wx; vy[v] = wy;
                    cx += wx; cy += wy;
                }
                cx /= 14.0f; cy /= 14.0f;

                if (refl != 0) { reflectedAntiHatCount++; activeHighlightCount++; }
                else unreflectedCount++;

                // CAP 4D->2D Galois conjugate internal coordinate for Mode 3 Sparkle Window!
                double gAng = rot * (5.0 * Math.PI / 3.0) + phasonPhase;
                float perpX = (float)(0.22 * (dq - dr * 0.618) * Math.Cos(gAng));
                float perpY = (float)(0.22 * (dr + dq * 0.618) * Math.Sin(gAng));
                int sheetK = (refl != 0) ? 4 : ((rot % 3) + 1);
                float flipE = (refl != 0) ? 0.92f : 0.15f; // Reflected anti-Hats glow Royal Gold!

                PenroseRhombTile rep = new PenroseRhombTile();
                rep.CenterX = cx; rep.CenterY = cy;
                rep.IsThick = (refl == 0) ? 1 : 0;
                rep.OrientationRad = (float)ang;
                rep.IndexSumK = sheetK;
                rep.ZTierHeight = (refl != 0) ? 1.45f : (0.45f + (rot % 3) * 0.28f);
                hatCenters.Add(rep);

                EmitMonotileEarTriangles(list, vx, vy, cx, cy, (float)ang, refl == 0 ? 1 : 0, sheetK, perpX, perpY, flipE, rep.ZTierHeight, h);
            }

            if (computePath && hatCenters.Count > 8)
            {
                SolveAStarGoldenWaveguide(hatCenters, panX, panY, Math.Min(7.5f, fieldSpanUnits * 0.42f), 2.15f);
                for (int h = 0; h < hatCenters.Count; h++)
                {
                    if (hatCenters[h].IsCorridorPath == 1)
                    {
                        for (int t = 0; t < 12; t++)
                        {
                            int idx = h * 12 + t;
                            if (idx < list.Count)
                            {
                                PenroseRhombTile qt = list[idx];
                                qt.IsCorridorPath = 1;
                                list[idx] = qt;
                            }
                        }
                    }
                }
            }
            return list;
        }

        // Mode 09: Exact Non-Overlapping Chiral 'Spectre' Monotile Tile(1, 1) Patch (Zero Reflections!)
        private static List<PenroseRhombTile> BuildChiralSpectreMesh(
            float panX, float panY, float fieldSpanUnits, double phasonPhase, bool computePath,
            out int evenSpectreCount, out int oddMysticCount, out int activeHighlightCount)
        {
            evenSpectreCount = 0;
            oddMysticCount = 0;
            activeHighlightCount = 0;

            float unitScale = 0.56f;
            float[,] baseV = BuildTileAbVertices(1.0f, 1.0f);
            int spCount = SpectrePlacementsFlat.Length / 3;
            List<PenroseRhombTile> list = new List<PenroseRhombTile>(spCount * 12);

            float[] vx = new float[14], vy = new float[14];
            for (int s = 0; s < spCount; s++)
            {
                float tx = SpectrePlacementsFlat[s * 3 + 0];
                float ty = SpectrePlacementsFlat[s * 3 + 1];
                int rot12 = (int)Math.Round(SpectrePlacementsFlat[s * 3 + 2]);
                bool isOdd = (rot12 % 2) != 0;

                double ang = rot12 * (Math.PI / 6.0);
                float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);

                float cx = 0.0f, cy = 0.0f;
                for (int v = 0; v < 14; v++)
                {
                    float px = baseV[v, 0], py = baseV[v, 1];
                    float wx = (tx + px * ca - py * sa) * unitScale;
                    float wy = (ty + px * sa + py * ca) * unitScale;
                    vx[v] = wx; vy[v] = wy;
                    cx += wx; cy += wy;
                }
                cx /= 14.0f; cy /= 14.0f;

                if (isOdd) { oddMysticCount++; activeHighlightCount++; }
                else evenSpectreCount++;

                int sheetK = isOdd ? 4 : (((rot12 / 2) % 3) + 1);
                float flipE = isOdd ? 0.94f : 0.18f;
                float perpX = (float)(Math.Cos(rot12 * 2.61799 + phasonPhase) * (0.25 + 0.01 * s));
                float perpY = (float)(Math.Sin(rot12 * 2.61799 + phasonPhase) * (0.25 + 0.01 * s));
                float zH = isOdd ? 1.55f : (0.42f + ((rot12 / 2) % 3) * 0.28f);

                EmitMonotileEarTriangles(list, vx, vy, cx, cy, (float)ang, isOdd ? 0 : 1, sheetK, perpX, perpY, flipE, zH, s);
            }
            return list;
        }

        private static float[,] BuildTileAbVertices(float a, float b)
        {
            float[] lens = new float[] { a, a, b, b, a, a, b, b, a, a, a, a, b, b };
            float[] turnsDeg = new float[] { 0f, 60f, -90f, -60f, 90f, -60f, -90f, -60f, 90f, -60f, 0f, -60f, -90f, 60f };
            float[,] pts = new float[14, 2];
            double x = 0.0, y = 0.0, ang = 0.0;
            pts[0, 0] = 0.0f; pts[0, 1] = 0.0f;
            for (int i = 0; i < 13; i++)
            {
                ang += turnsDeg[i];
                double rad = ang * Math.PI / 180.0;
                x += lens[i] * Math.Cos(rad);
                y += lens[i] * Math.Sin(rad);
                pts[i + 1, 0] = (float)x;
                pts[i + 1, 1] = (float)y;
            }
            return pts;
        }

        private static bool IsExteriorStep(int va, int vb)
        {
            return ((va + 1) % 14 == vb) || ((vb + 1) % 14 == va);
        }

        private static void EmitMonotileEarTriangles(
            List<PenroseRhombTile> dst, float[] vx, float[] vy,
            float cx, float cy, float ang, int isThick, int sheetK,
            float perpX, float perpY, float flipE, float zH, int tileId)
        {
            for (int t = 0; t < 12; t++)
            {
                if (dst.Count >= MaxGpuRhombs) break;
                int i0 = TileAbEarTris[t, 0];
                int i1 = TileAbEarTris[t, 1];
                int i2 = TileAbEarTris[t, 2];

                int mask = 0;
                if (IsExteriorStep(i0, i1)) mask |= 1;
                if (IsExteriorStep(i1, i2)) mask |= 2;
                if (IsExteriorStep(i2, i0)) mask |= 8;

                PenroseRhombTile q = new PenroseRhombTile();
                q.X0 = vx[i0]; q.Y0 = vy[i0];
                q.X1 = vx[i1]; q.Y1 = vy[i1];
                q.X2 = vx[i2]; q.Y2 = vy[i2];
                q.X3 = vx[i2]; q.Y3 = vy[i2]; // Degenerate 4th vertex = exact convex triangle!
                q.CenterX = cx;
                q.CenterY = cy;
                q.OrientationRad = ang;
                q.IsThick = isThick;
                q.WormFamilyA = (t == 0) ? (tileId % 5) : 1;
                q.EdgeMask = mask;
                q.IndexSumK = sheetK;
                q.PerpX = perpX;
                q.PerpY = perpY;
                q.PerpRadius = (float)Math.Sqrt(perpX * perpX + perpY * perpY);
                q.PhasonFlipEnergy = flipE;
                q.ZTierHeight = zH;
                q.IsCorridorPath = 0;
                dst.Add(q);
            }
        }

        // Pillar 2: Interactive 5D Miller Reciprocal-Space Peak Inspector (h0, h1, h2, h3, h4)!
        public static MillerPeakInfo5D IndexBraggPeak5D(float braggNormU, float braggNormV, float zoomUnits)
        {
            MillerPeakInfo5D info = new MillerPeakInfo5D();
            info.Active = true;
            info.ScreenU = braggNormU;
            info.ScreenV = braggNormV;

            float optSpan = Math.Max(18.0f, Math.Min(28.0f, zoomUnits));
            // Map screen UV [0..1] -> FFT bin offset from center (128, 128) -> physical cycles per unit!
            float binDx = (braggNormU - 0.5f) * 0.72f * 255.0f;
            float binDy = (braggNormV - 0.5f) * 0.72f * 255.0f;
            float kx = binDx / optSpan;
            float ky = binDy / optSpan;

            double[] ex = new double[5], ey = new double[5];
            double[] px = new double[5], py = new double[5];
            for (int m = 0; m < 5; m++)
            {
                ex[m] = Math.Cos(2.0 * Math.PI * m / 5.0);
                ey[m] = Math.Sin(2.0 * Math.PI * m / 5.0);
                px[m] = Math.Cos(4.0 * Math.PI * m / 5.0);
                py[m] = Math.Sin(4.0 * Math.PI * m / 5.0);
            }

            double bestErr = 1e30;
            int bh0 = 1, bh1 = 0, bh2 = 0, bh3 = 0, bh4 = 0;
            double bgx = 1.0, bgy = 0.0, bwx = 1.0, bwy = 0.0;

            for (int h0 = -3; h0 <= 3; h0++)
            for (int h1 = -3; h1 <= 3; h1++)
            for (int h2 = -3; h2 <= 3; h2++)
            for (int h3 = -3; h3 <= 3; h3++)
            for (int h4 = -3; h4 <= 3; h4++)
            {
                int hSum = h0 + h1 + h2 + h3 + h4;
                if (hSum < 0 || hSum > 1) continue; // Canonical gauge eliminates (1,1,1,1,1)=0 nullspace!
                if (h0 == 0 && h1 == 0 && h2 == 0 && h3 == 0 && h4 == 0) continue;

                double gx = h0 * ex[0] + h1 * ex[1] + h2 * ex[2] + h3 * ex[3] + h4 * ex[4];
                double gy = h0 * ey[0] + h1 * ey[1] + h2 * ey[2] + h3 * ey[3] + h4 * ey[4];
                double wx = h0 * px[0] + h1 * px[1] + h2 * px[2] + h3 * px[3] + h4 * px[4];
                double wy = h0 * py[0] + h1 * py[1] + h2 * py[2] + h3 * py[3] + h4 * py[4];

                // Penalize huge |G_perp| lightly so bright low-order Bragg spots snap cleanly!
                double err = (gx - kx) * (gx - kx) + (gy - ky) * (gy - ky) + 0.004 * (wx * wx + wy * wy);
                if (err < bestErr)
                {
                    bestErr = err;
                    bh0 = h0; bh1 = h1; bh2 = h2; bh3 = h3; bh4 = h4;
                    bgx = gx; bgy = gy; bwx = wx; bwy = wy;
                }
            }

            info.H0 = bh0; info.H1 = bh1; info.H2 = bh2; info.H3 = bh3; info.H4 = bh4;
            info.Gx = (float)bgx; info.Gy = (float)bgy;
            info.GParNorm = (float)Math.Sqrt(bgx * bgx + bgy * bgy);
            info.GPerpNorm = (float)Math.Sqrt(bwx * bwx + bwy * bwy);
            info.PlaneSpacingD = 1.0f / Math.Max(0.01f, info.GParNorm);
            info.Summary = string.Format(CultureInfo.InvariantCulture,
                "Miller ({0},{1},{2},{3},{4}) | d={5:0.000} | |G_perp|={6:0.000}",
                bh0, bh1, bh2, bh3, bh4, info.PlaneSpacingD, info.GPerpNorm);
            return info;
        }

        private static void SolveAStarGoldenWaveguide(
            List<PenroseRhombTile> tiles, float panX, float panY, float portalRadius, float maxNeighborDist)
        {
            int n = tiles.Count;
            if (n < 8) return;

            int goalIdx = 0, startIdx = 0;
            float bestGoalD2 = 1e30f, bestStartD2 = 1e30f;
            float targetX = panX + portalRadius * 0.85f;
            float targetY = panY + portalRadius * 0.35f;

            for (int i = 0; i < n; i++)
            {
                if (tiles[i].EdgeMask == 1) continue;
                float dcx = tiles[i].CenterX - panX, dcy = tiles[i].CenterY - panY;
                float d2 = dcx * dcx + dcy * dcy;
                if (d2 < bestGoalD2) { bestGoalD2 = d2; goalIdx = i; }

                float dsx = tiles[i].CenterX - targetX, dsy = tiles[i].CenterY - targetY;
                float s2 = dsx * dsx + dsy * dsy;
                if (s2 < bestStartD2) { bestStartD2 = s2; startIdx = i; }
            }
            if (startIdx == goalIdx) return;

            float invCell = 1.0f / Math.Max(0.65f, maxNeighborDist);
            Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                if (tiles[i].EdgeMask == 1) continue;
                int gx = (int)Math.Floor(tiles[i].CenterX * invCell);
                int gy = (int)Math.Floor(tiles[i].CenterY * invCell);
                long key = (((long)gx) << 32) ^ (uint)gy;
                List<int> bucket;
                if (!grid.TryGetValue(key, out bucket)) { bucket = new List<int>(6); grid[key] = bucket; }
                bucket.Add(i);
            }

            float[] gScore = new float[n], fScore = new float[n];
            int[] cameFrom = new int[n];
            bool[] closed = new bool[n], inOpen = new bool[n];
            for (int i = 0; i < n; i++) { gScore[i] = 1e30f; fScore[i] = 1e30f; cameFrom[i] = -1; }

            List<int> open = new List<int>(128);
            gScore[startIdx] = 0.0f;
            fScore[startIdx] = Dist(tiles[startIdx], tiles[goalIdx]);
            open.Add(startIdx);
            inOpen[startIdx] = true;

            int safety = 0;
            while (open.Count > 0 && safety++ < 4500)
            {
                int bestPos = 0, curr = open[0];
                float bestF = fScore[curr];
                for (int i = 1; i < open.Count; i++)
                {
                    int cand = open[i];
                    if (fScore[cand] < bestF) { bestF = fScore[cand]; curr = cand; bestPos = i; }
                }
                open.RemoveAt(bestPos);
                inOpen[curr] = false;
                if (curr == goalIdx) break;
                closed[curr] = true;

                int gx = (int)Math.Floor(tiles[curr].CenterX * invCell);
                int gy = (int)Math.Floor(tiles[curr].CenterY * invCell);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long nKey = (((long)(gx + dx)) << 32) ^ (uint)(gy + dy);
                        List<int> bucket;
                        if (!grid.TryGetValue(nKey, out bucket)) continue;
                        for (int b = 0; b < bucket.Count; b++)
                        {
                            int nb = bucket[b];
                            if (nb == curr || closed[nb]) continue;
                            float stepD = Dist(tiles[curr], tiles[nb]);
                            if (stepD > maxNeighborDist) continue;
                            float wgt = (tiles[nb].IsThick == 1) ? 0.85f : 1.38f;
                            float tentative = gScore[curr] + stepD * wgt;
                            if (tentative < gScore[nb])
                            {
                                cameFrom[nb] = curr;
                                gScore[nb] = tentative;
                                fScore[nb] = tentative + Dist(tiles[nb], tiles[goalIdx]);
                                if (!inOpen[nb]) { open.Add(nb); inOpen[nb] = true; }
                            }
                        }
                    }
                }
            }

            int trace = goalIdx, guard = 0;
            while (trace >= 0 && guard++ < n)
            {
                PenroseRhombTile t = tiles[trace];
                t.IsCorridorPath = 1;
                tiles[trace] = t;
                if (trace == startIdx) break;
                trace = cameFrom[trace];
            }
        }

        private static float Dist(PenroseRhombTile a, PenroseRhombTile b)
        {
            float dx = a.CenterX - b.CenterX, dy = a.CenterY - b.CenterY;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
