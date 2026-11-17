using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmegaSynthStudio
{
    public sealed class SynthPresetSpec
    {
        public string Name;
        public int PlateBoundaryMode; // 0=Free Square, 1=Clamped Square, 2=Circular Bessel, 3=Hex E8 Torus
        public int EngineKind;        // 0=Modal/Bowed Chladni, 1=Karplus-Strong Dispersive Stiff String, 2=Formant Cymatic Choir
        public double[] ModeRatios;
        public double[] ModeWeights;
        public double BaseDecaySec;
        public double SustainBowMix;
        public double E8SympathyMix;
        public double ShimmerHallMix;
        public double PlateDispersion;
        public double StrikeMalletHardness;

        public SynthPresetSpec(
            string name, int boundary, int engineKind, double[] ratios, double[] weights,
            double decaySec, double bowMix, double e8Mix, double hallMix,
            double dispersion, double hardness)
        {
            Name = name;
            PlateBoundaryMode = boundary;
            EngineKind = engineKind;
            ModeRatios = ratios;
            ModeWeights = weights;
            BaseDecaySec = decaySec;
            SustainBowMix = bowMix;
            E8SympathyMix = e8Mix;
            ShimmerHallMix = hallMix;
            PlateDispersion = dispersion;
            StrikeMalletHardness = hardness;
        }
    }

    public static class E8WaveLatticeMath
    {
        public const int RootCount = 240;
        public const int NeighborsPerRoot = 56;
        public const int CustomResynthPresetIndex = 8;

        public static readonly double[][] Roots = new double[RootCount][];
        public static readonly int[] NeighborIdx = new int[RootCount * NeighborsPerRoot];
        public static readonly int[] NeighborCount = new int[RootCount];
        public static readonly int[] EdgeA;
        public static readonly int[] EdgeB;
        public static readonly byte[] RootRing = new byte[RootCount];

        public static readonly double[] U1 = new double[] { 0.0, 0.0, -0.11911770, -0.11911770, -0.19273649, -0.19273649, -0.21763887, -0.92193305 };
        public static readonly double[] U2 = new double[] { -0.09689907, 0.66982357, -0.46350566, 0.46350566, -0.23693033, 0.23693033, 0.0, 0.0 };
        public static readonly double[] U3 = new double[] { 0.0, 0.0, -0.38336132, -0.38336132, 0.23693033, 0.23693033, 0.74996791, -0.17704341 };
        public static readonly double[] U4 = new double[] { -0.15941060, 0.54488358, 0.11911770, -0.11911770, 0.56978596, -0.56978596, 0.0, 0.0 };

        public static readonly float[] CoxeterX = new float[RootCount];
        public static readonly float[] CoxeterY = new float[RootCount];
        public static readonly float[] CoxeterZ = new float[RootCount];
        public static readonly float[] CoxeterW = new float[RootCount];
        public static readonly float[] PickupWeightL = new float[RootCount];
        public static readonly float[] PickupWeightR = new float[RootCount];

        public static readonly string[] ScaleNames = new string[]
        {
            "12-TET Chromatic (Fretless Glide)",
            "5-Limit Just Intonation (Pure Ratios)",
            "31-EDO Huygens Meantone",
            "Bohlen-Pierce (13-Step Tritave 3:1)",
            "Golden Phi Harmonic Lattice",
            "E8 Coxeter Exponent Ratios (30-Fold)"
        };

        // Acoustic Vowel Formant Centers F1, F2, F3 in Hz for A, E, I, O, U morphing
        public static readonly double[,] VowelFormantsHz = new double[,]
        {
            { 730.0, 1090.0, 2440.0 }, // A (Ah)
            { 530.0, 1840.0, 2480.0 }, // E (Eh)
            { 270.0, 2290.0, 3010.0 }, // I (Ee)
            { 570.0,  840.0, 2410.0 }, // O (Oh)
            { 300.0,  870.0, 2240.0 }  // U (Oo)
        };

        private static readonly object _presetLock = new object();
        public static readonly SynthPresetSpec[] Presets = BuildPresets();
        public static string CustomResynthSourceLabel = "Default Harmonic Bell";

        static E8WaveLatticeMath()
        {
            BuildRoots();
            List<int> ea = new List<int>(6720);
            List<int> eb = new List<int>(6720);
            double[] ringCuts = new double[] { 0.30, 0.43, 0.52, 0.64, 0.73, 0.84, 1.02, 2.0 };

            double sumAbsL = 0.0, sumAbsR = 0.0;
            for (int i = 0; i < RootCount; i++)
            {
                double px = Dot8(Roots[i], U1);
                double py = Dot8(Roots[i], U2);
                double pz = Dot8(Roots[i], U3);
                double pw = Dot8(Roots[i], U4);
                CoxeterX[i] = (float)px;
                CoxeterY[i] = (float)py;
                CoxeterZ[i] = (float)pz;
                CoxeterW[i] = (float)pw;

                double rad = Math.Sqrt(px * px + py * py);
                byte rIdx = 7;
                for (byte c = 0; c < 8; c++)
                {
                    if (rad < ringCuts[c]) { rIdx = c; break; }
                }
                RootRing[i] = rIdx;

                double wl = px * 0.72 + pz * 0.28;
                double wr = py * 0.72 + pw * 0.28;
                PickupWeightL[i] = (float)wl;
                PickupWeightR[i] = (float)wr;
                sumAbsL += Math.Abs(wl);
                sumAbsR += Math.Abs(wr);
            }

            float normL = (float)(1.0 / Math.Max(1.0, sumAbsL * 0.18));
            float normR = (float)(1.0 / Math.Max(1.0, sumAbsR * 0.18));
            for (int i = 0; i < RootCount; i++)
            {
                PickupWeightL[i] *= normL;
                PickupWeightR[i] *= normR;
            }

            for (int i = 0; i < RootCount; i++)
            {
                int cnt = 0;
                for (int j = 0; j < RootCount; j++)
                {
                    if (i == j) continue;
                    double d = Dot8(Roots[i], Roots[j]);
                    if (d > 0.999 && d < 1.001)
                    {
                        if (cnt < NeighborsPerRoot)
                        {
                            NeighborIdx[i * NeighborsPerRoot + cnt] = j;
                            cnt++;
                        }
                        if (j > i) { ea.Add(i); eb.Add(j); }
                    }
                }
                NeighborCount[i] = cnt;
            }
            EdgeA = ea.ToArray();
            EdgeB = eb.ToArray();
        }

        private static void BuildRoots()
        {
            int n = 0;
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
                            Roots[n++] = v;
                        }
                    }
                }
            }
            for (int mask = 0; mask < 256; mask++)
            {
                int bits = 0;
                for (int k = 0; k < 8; k++) if (((mask >> k) & 1) != 0) bits++;
                if ((bits & 1) == 0)
                {
                    double[] v = new double[8];
                    for (int k = 0; k < 8; k++) v[k] = ((mask >> k) & 1) != 0 ? -0.5 : 0.5;
                    Roots[n++] = v;
                }
            }
        }

        public static void UpdateCustomResynthesisPreset(string sourceLabel, double[] ratios8, double[] weights8, double decaySec, double bowMix, int boundaryMode)
        {
            lock (_presetLock)
            {
                double[] r = new double[8];
                double[] w = new double[8];
                for (int i = 0; i < 8; i++)
                {
                    r[i] = (ratios8 != null && i < ratios8.Length && ratios8[i] > 0.15) ? Math.Min(16.0, ratios8[i]) : (i + 1.0);
                    w[i] = (weights8 != null && i < weights8.Length && weights8[i] >= 0.01) ? Math.Min(1.25, weights8[i]) : (1.0 / (i + 1.0));
                }
                string cleanSrc = string.IsNullOrEmpty(sourceLabel) ? "Cloned Audio" : sourceLabel.Trim();
                if (cleanSrc.Length > 24) cleanSrc = cleanSrc.Substring(0, 24);
                CustomResynthSourceLabel = cleanSrc;

                SynthPresetSpec p = Presets[CustomResynthPresetIndex];
                p.Name = "09. Spectral Resynthesis [" + cleanSrc + "]";
                p.PlateBoundaryMode = Math.Max(0, Math.Min(3, boundaryMode));
                p.EngineKind = 0;
                p.ModeRatios = r;
                p.ModeWeights = w;
                p.BaseDecaySec = Math.Max(0.45, Math.Min(8.0, decaySec));
                p.SustainBowMix = Math.Max(0.05, Math.Min(0.95, bowMix));
                p.E8SympathyMix = 0.52;
                p.ShimmerHallMix = 0.36;
                p.PlateDispersion = 0.12;
                p.StrikeMalletHardness = 0.62;
            }
        }

        public static double Dot8(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]
                 + a[4] * b[4] + a[5] * b[5] + a[6] * b[6] + a[7] * b[7];
        }

        public static int FindNearestExcitationRoot(double normX, double normY, double gyroB, double gyroG)
        {
            double ang1 = normX * Math.PI * 2.0;
            double ang2 = normY * Math.PI * 2.0;
            double q0 = Math.Cos(ang1), q1 = Math.Sin(ang1);
            double q2 = Math.Cos(ang2), q3 = Math.Sin(ang2);
            double q4 = Math.Sin(ang1 + ang2) * 0.7, q5 = Math.Cos(ang1 - ang2) * 0.7;
            double q6 = gyroB * 0.85, q7 = gyroG * 0.85;

            int best = 0;
            double bestDot = -999.0;
            for (int i = 0; i < RootCount; i++)
            {
                double[] r = Roots[i];
                double d = r[0] * q0 + r[1] * q1 + r[2] * q2 + r[3] * q3
                         + r[4] * q4 + r[5] * q5 + r[6] * q6 + r[7] * q7;
                if (d > bestDot) { bestDot = d; best = i; }
            }
            return best;
        }

        public static void InterpolateVowelFormants(double vowelMorph01, out double f1, out double f2, out double f3)
        {
            double pos = Math.Max(0.0, Math.Min(1.0, vowelMorph01)) * 4.0;
            int idx0 = Math.Min(3, (int)Math.Floor(pos));
            int idx1 = idx0 + 1;
            double frac = pos - idx0;
            f1 = VowelFormantsHz[idx0, 0] + (VowelFormantsHz[idx1, 0] - VowelFormantsHz[idx0, 0]) * frac;
            f2 = VowelFormantsHz[idx0, 1] + (VowelFormantsHz[idx1, 1] - VowelFormantsHz[idx0, 1]) * frac;
            f3 = VowelFormantsHz[idx0, 2] + (VowelFormantsHz[idx1, 2] - VowelFormantsHz[idx0, 2]) * frac;
        }

        public static double MapNormalizedXToHz(double xNorm, int baseMidiNote, double octaveSpan, int scaleIdx, double snapStrength, double pitchBendSemis)
        {
            double clampedX = Math.Max(0.0, Math.Min(1.0, xNorm));
            double continuousOct = clampedX * octaveSpan;
            double snappedOct = QuantizeOctaveCoordinate(continuousOct, scaleIdx);
            double blendedOct = continuousOct + (snappedOct - continuousOct) * Math.Max(0.0, Math.Min(1.0, snapStrength));

            double baseHz = 440.0 * Math.Pow(2.0, (baseMidiNote - 69.0) / 12.0);
            double periodBase = (scaleIdx == 3) ? 3.0 : 2.0;
            double bendFactor = Math.Pow(2.0, pitchBendSemis / 12.0);
            double hz = baseHz * Math.Pow(periodBase, blendedOct) * bendFactor;
            if (hz < 24.0) hz = 24.0;
            if (hz > 12000.0) hz = 12000.0;
            return hz;
        }

        private static readonly double[] JustRatios12 = new double[]
        {
            1.0, 16.0 / 15.0, 9.0 / 8.0, 6.0 / 5.0, 5.0 / 4.0, 4.0 / 3.0,
            45.0 / 32.0, 3.0 / 2.0, 8.0 / 5.0, 5.0 / 3.0, 9.0 / 5.0, 15.0 / 8.0, 2.0
        };

        private static readonly double[] GoldenRatios8 = new double[]
        {
            1.0, 1.127838, 1.236068, 1.381966, 1.500000, 1.618034, 1.788854, 1.894427, 2.0
        };

        private static readonly double[] E8CoxeterOctave = new double[]
        {
            1.0, 31.0 / 30.0, 37.0 / 30.0, 41.0 / 30.0, 43.0 / 30.0,
            47.0 / 30.0, 49.0 / 30.0, 53.0 / 30.0, 59.0 / 30.0, 2.0
        };

        public static double QuantizeOctaveCoordinate(double octPos, int scaleIdx)
        {
            double whole = Math.Floor(octPos);
            double frac = octPos - whole;
            if (scaleIdx == 0) return whole + Math.Round(frac * 12.0) / 12.0;
            if (scaleIdx == 2) return whole + Math.Round(frac * 31.0) / 31.0;
            if (scaleIdx == 3) return whole + Math.Round(frac * 13.0) / 13.0;

            double[] table = JustRatios12;
            if (scaleIdx == 4) table = GoldenRatios8;
            else if (scaleIdx == 5) table = E8CoxeterOctave;

            double bestOct = 0.0, bestErr = 999.0;
            for (int i = 0; i < table.Length; i++)
            {
                double degOct = Math.Log(table[i]) / Math.Log(2.0);
                double err = Math.Abs(frac - degOct);
                if (err < bestErr) { bestErr = err; bestOct = degOct; }
            }
            return whole + bestOct;
        }

        public static double EvalChladniModalGain(int partialIdx, double xNorm, double yNorm, int boundaryMode)
        {
            int[,] mnPairs = new int[,]
            {
                { 1, 1 }, { 1, 2 }, { 2, 1 }, { 2, 2 },
                { 1, 3 }, { 3, 2 }, { 3, 3 }, { 4, 3 }
            };
            int p = partialIdx & 7;
            if (p == 0) return 1.0;
            int m = mnPairs[p, 0], n = mnPairs[p, 1];
            double x = 0.08 + 0.84 * Math.Max(0.0, Math.Min(1.0, xNorm));
            double y = 0.08 + 0.84 * Math.Max(0.0, Math.Min(1.0, yNorm));
            if (boundaryMode == 2)
            {
                double dx = x - 0.5, dy = y - 0.5;
                double r = Math.Sqrt(dx * dx + dy * dy) * 2.0;
                double th = Math.Atan2(dy, dx);
                return 0.25 + 0.75 * Math.Abs(Math.Cos(r * Math.PI * (0.5 * m + 0.35 * n)) * Math.Cos(n * th + 0.4));
            }
            double c1 = Math.Cos(m * Math.PI * x) * Math.Cos(n * Math.PI * y);
            double c2 = Math.Cos(n * Math.PI * x) * Math.Cos(m * Math.PI * y);
            double sign = ((p & 1) == 0) ? 1.0 : -0.75;
            return Math.Max(0.14, Math.Min(1.25, 0.20 + Math.Abs(c1 + sign * c2) * 0.65));
        }

        public static void FormatNoteAndCents(double freqHz, out string noteLabel, out int centsOut)
        {
            double midiExact = 69.0 + 12.0 * (Math.Log(Math.Max(20.0, freqHz) / 440.0) / Math.Log(2.0));
            int midiNearest = (int)Math.Round(midiExact);
            centsOut = (int)Math.Round((midiExact - midiNearest) * 100.0);
            string[] names = new string[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int nIdx = ((midiNearest % 12) + 12) % 12;
            int oct = (midiNearest / 12) - 1;
            noteLabel = names[nIdx] + oct;
        }

        private static SynthPresetSpec[] BuildPresets()
        {
            return new SynthPresetSpec[]
            {
                new SynthPresetSpec("01. E8 Crystal Bowed Continuum (MPE Sustain)", 3, 0,
                    new double[] { 1.0000, 2.0000, 2.9985, 4.0020, 5.0040, 6.0080, 7.0150, 8.9920 },
                    new double[] { 1.00,   0.58,   0.40,   0.28,   0.20,   0.15,   0.11,   0.08 },
                    3.6, 0.86, 0.48, 0.38, 0.14, 0.45),

                new SynthPresetSpec("02. Chladni Bronze Square Plate (Kirchhoff Mallet)", 0, 0,
                    new double[] { 1.0000, 1.9860, 2.5040, 3.8720, 4.4920, 5.3310, 6.4750, 7.8900 },
                    new double[] { 1.00,   0.66,   0.52,   0.38,   0.29,   0.21,   0.15,   0.10 },
                    2.9, 0.18, 0.42, 0.34, 0.42, 0.76),

                new SynthPresetSpec("03. Circular Quartz Singing Bowl (Bessel Modes)", 2, 0,
                    new double[] { 1.0000, 2.0816, 3.4140, 3.8925, 5.0020, 5.9535, 6.8210, 8.2810 },
                    new double[] { 1.00,   0.62,   0.42,   0.30,   0.22,   0.14,   0.10,   0.07 },
                    5.2, 0.74, 0.52, 0.46, 0.28, 0.38),

                new SynthPresetSpec("04. Titanium Free-Edge Handpan (1 : 2 : 3 Harmonic)", 1, 0,
                    new double[] { 1.0000, 2.0000, 2.9970, 4.0120, 5.0350, 6.0180, 7.5200, 9.0400 },
                    new double[] { 1.00,   0.74,   0.54,   0.26,   0.18,   0.12,   0.08,   0.05 },
                    2.4, 0.08, 0.36, 0.32, 0.22, 0.68),

                new SynthPresetSpec("05. Bowed Glass Harmonica (Pure Slip-Stick)", 2, 0,
                    new double[] { 1.0000, 2.0008, 3.0025, 4.0060, 5.0120, 6.0200, 7.0320, 8.0480 },
                    new double[] { 1.00,   0.48,   0.32,   0.19,   0.12,   0.08,   0.05,   0.03 },
                    4.4, 0.96, 0.44, 0.48, 0.08, 0.25),

                new SynthPresetSpec("06. Sub-Lattice Sovereign Gong (Deep E8 Wave)", 0, 0,
                    new double[] { 0.5000, 1.0000, 1.4990, 2.0040, 2.6180, 3.3300, 4.2360, 5.6560 },
                    new double[] { 1.05,   0.85,   0.56,   0.44,   0.34,   0.26,   0.18,   0.13 },
                    6.0, 0.55, 0.68, 0.44, 0.55, 0.62),

                new SynthPresetSpec("07. Karplus-Strong Dispersive Stiff Harp (Waveguide)", 0, 1,
                    new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0 },
                    new double[] { 1.0, 0.5, 0.33, 0.25, 0.2, 0.16, 0.14, 0.12 },
                    3.8, 0.05, 0.46, 0.36, 0.18, 0.82),

                new SynthPresetSpec("08. Throat-Formant Cymatic Choir (A-E-I-O-U Morph)", 2, 2,
                    new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0 },
                    new double[] { 1.00, 0.84, 0.72, 0.60, 0.50, 0.42, 0.34, 0.28 },
                    4.2, 0.92, 0.50, 0.48, 0.06, 0.35),

                new SynthPresetSpec("09. Spectral Resynthesis [Default Harmonic Bell]", 2, 0,
                    new double[] { 1.0000, 1.6180, 2.4142, 3.1416, 4.2361, 5.2361, 6.8541, 8.4721 },
                    new double[] { 1.00,   0.68,   0.48,   0.35,   0.25,   0.18,   0.12,   0.08 },
                    3.8, 0.62, 0.52, 0.36, 0.12, 0.62)
            };
        }
    }
}
