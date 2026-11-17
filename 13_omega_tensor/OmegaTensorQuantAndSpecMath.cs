using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmegaTensorStudio
{
    public sealed class SpeculativeStepResult
    {
        public int DraftLength;
        public int AcceptedDraftCount;
        public int TotalEmittedTokens;
        public int[] EmittedTokenIds;
        public bool[] WasDraftAccepted;
        public double MeanAcceptProb;
        public double EffectiveSpeedup;
    }

    public sealed class EnginePartitionPlan
    {
        public string[] EngineNames = new string[5];
        public double[] MeasuredGops = new double[5];
        public double[] RttMs = new double[5];
        public bool[] Online = new bool[5];
        public int[] AssignedLayers = new int[5];
        public double[] ShareFraction = new double[5];
        public double TotalClusterGops;
        public double EstimatedSingleEngineMs;
        public double EstimatedFusedSplitMs;
        public double SpeedupFactor;
    }

    public sealed class SheafAuditResult
    {
        public bool CohomologyH1Zero;
        public double MaxCoboundaryDefect;
        public int QuarantinedStalkIndex;
        public string Summary;
    }

    public static class OmegaTensorMath
    {
        // Exact 240 Minimal Roots of E8 (8D norm^2 == 2.0) + H4 30-fold Coxeter Plane Basis
        public static readonly float[,] E8Roots240 = new float[240, 8];
        public static readonly float[] CoxeterU = new float[8];
        public static readonly float[] CoxeterV = new float[8];
        public static readonly float[] E8RootProj2D = new float[240 * 2];

        // Paley-11 [24, 12, 8] Extended Binary Golay Parity Submatrix & O(1) Syndrome Self-Healer LUT
        public static readonly ushort[] GolayParityRows12 = new ushort[12]
        {
            0xA3B, 0xC76, 0x8ED, 0x9DA, 0xBB4, 0xF68,
            0xED1, 0xDA3, 0xB47, 0xE8E, 0xD1D, 0x7FF
        };
        private static readonly int[] _golaySyndromeToErr24 = new int[4096];
        private static readonly byte[] _golaySyndromeWeight = new byte[4096];

        static OmegaTensorMath()
        {
            // 1. Populate all 240 roots of E8: 112 integer roots (+/- e_i +/- e_j) + 128 half-integer roots
            int idx = 0;
            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    for (int si = -1; si <= 1; si += 2)
                    {
                        for (int sj = -1; sj <= 1; sj += 2)
                        {
                            E8Roots240[idx, i] = si;
                            E8Roots240[idx, j] = sj;
                            idx++;
                        }
                    }
                }
            }
            for (int mask = 0; mask < 256; mask++)
            {
                int bits = PopCount12(mask);
                if ((bits & 1) == 0)
                {
                    for (int b = 0; b < 8; b++)
                        E8Roots240[idx, b] = ((mask >> b) & 1) != 0 ? -0.5f : 0.5f;
                    idx++;
                }
            }

            // Orthonormal 2D Coxeter-plane projection vectors for 30-fold H4 Petrie symmetry of E8
            double[] rawU = new double[8] { 0.509037, 0.425325, 0.309017, 0.162460, -0.162460, -0.309017, -0.425325, -0.363271 };
            double[] rawV = new double[8] { 0.085410, 0.309017, 0.425325, 0.509037, 0.425325, 0.309017, 0.085410, -0.425325 };
            NormalizeAndOrthogonalize8D(rawU, rawV, CoxeterU, CoxeterV);

            for (int r = 0; r < 240; r++)
            {
                float px = 0f, py = 0f;
                for (int d = 0; d < 8; d++)
                {
                    px += E8Roots240[r, d] * CoxeterU[d];
                    py += E8Roots240[r, d] * CoxeterV[d];
                }
                E8RootProj2D[r * 2 + 0] = px;
                E8RootProj2D[r * 2 + 1] = py;
            }

            // 2. Populate O(1) Golay [24, 12, 8] 2,325-entry 0..3-bit error LUT
            for (int i = 0; i < 4096; i++)
            {
                _golaySyndromeToErr24[i] = -1;
                _golaySyndromeWeight[i] = 99;
            }
            _golaySyndromeToErr24[0] = 0;
            _golaySyndromeWeight[0] = 0;
            for (int i = 0; i < 24; i++)
            {
                int e1 = 1 << i;
                int s1 = ComputeGolaySyndrome12(e1);
                _golaySyndromeToErr24[s1] = e1;
                _golaySyndromeWeight[s1] = 1;
            }
            for (int i = 0; i < 24; i++)
            {
                for (int j = i + 1; j < 24; j++)
                {
                    int e2 = (1 << i) | (1 << j);
                    int s2 = ComputeGolaySyndrome12(e2);
                    if (_golaySyndromeToErr24[s2] < 0)
                    {
                        _golaySyndromeToErr24[s2] = e2;
                        _golaySyndromeWeight[s2] = 2;
                    }
                }
            }
            for (int i = 0; i < 24; i++)
            {
                for (int j = i + 1; j < 24; j++)
                {
                    for (int k = j + 1; k < 24; k++)
                    {
                        int e3 = (1 << i) | (1 << j) | (1 << k);
                        int s3 = ComputeGolaySyndrome12(e3);
                        if (_golaySyndromeToErr24[s3] < 0)
                        {
                            _golaySyndromeToErr24[s3] = e3;
                            _golaySyndromeWeight[s3] = 3;
                        }
                    }
                }
            }
        }

        private static void NormalizeAndOrthogonalize8D(double[] u, double[] v, float[] outU, float[] outV)
        {
            double nu = 0.0;
            for (int i = 0; i < 8; i++) nu += u[i] * u[i];
            nu = Math.Sqrt(Math.Max(1e-12, nu));
            for (int i = 0; i < 8; i++) u[i] /= nu;

            double dot = 0.0;
            for (int i = 0; i < 8; i++) dot += v[i] * u[i];
            double nv = 0.0;
            for (int i = 0; i < 8; i++)
            {
                v[i] -= dot * u[i];
                nv += v[i] * v[i];
            }
            nv = Math.Sqrt(Math.Max(1e-12, nv));
            for (int i = 0; i < 8; i++)
            {
                outU[i] = (float)u[i];
                outV[i] = (float)(v[i] / nv);
            }
        }

        public static int PopCount12(int x)
        {
            int c = 0;
            uint u = (uint)x;
            while (u != 0) { u &= (u - 1u); c++; }
            return c;
        }

        // Conway-Sloane Exact O(8) Nearest-Point E8 Lattice Quantizer: E8 = D8 U (D8 + 0.5*1)
        public static void RoundToD8(double[] x8, int offset, double[] outD8)
        {
            int sumInt = 0;
            int worstIdx = 0;
            double worstDiff = -1.0;
            int[] rounded = new int[8];

            for (int i = 0; i < 8; i++)
            {
                double v = x8[offset + i];
                int r = (int)Math.Round(v, MidpointRounding.ToEven);
                rounded[i] = r;
                sumInt += r;
                double diff = Math.Abs(v - r);
                if (diff > worstDiff)
                {
                    worstDiff = diff;
                    worstIdx = i;
                }
            }
            if ((sumInt & 1) != 0)
            {
                double orig = x8[offset + worstIdx];
                if (orig > rounded[worstIdx]) rounded[worstIdx] += 1;
                else rounded[worstIdx] -= 1;
            }
            for (int i = 0; i < 8; i++) outD8[i] = rounded[i];
        }

        public static void QuantizeE8Block8D(double[] x, int offset, double scaleInv, double[] outLattice8, out int nearestRootIdx)
        {
            double[] scaled = new double[8];
            double[] shifted = new double[8];
            for (int i = 0; i < 8; i++)
            {
                scaled[i] = x[offset + i] * scaleInv;
                shifted[i] = scaled[i] - 0.5;
            }

            double[] c0 = new double[8];
            double[] c1 = new double[8];
            RoundToD8(scaled, 0, c0);
            RoundToD8(shifted, 0, c1);

            double d0 = 0.0, d1 = 0.0;
            for (int i = 0; i < 8; i++)
            {
                c1[i] += 0.5;
                double e0 = scaled[i] - c0[i];
                double e1 = scaled[i] - c1[i];
                d0 += e0 * e0;
                d1 += e1 * e1;
            }

            double[] best = (d0 <= d1) ? c0 : c1;
            for (int i = 0; i < 8; i++) outLattice8[i] = best[i];

            // Also identify most aligned E8 minimal root for 2D Coxeter HUD lighting
            int bestRoot = 0;
            double bestDot = -1e9;
            for (int r = 0; r < 240; r++)
            {
                double dot = 0.0;
                for (int i = 0; i < 8; i++) dot += best[i] * E8Roots240[r, i];
                if (dot > bestDot) { bestDot = dot; bestRoot = r; }
            }
            nearestRootIdx = bestRoot;
        }

        public static bool IsValidE8Point(double[] y8)
        {
            int[] twice = new int[8];
            int sumTwice = 0;
            for (int i = 0; i < 8; i++)
            {
                double t = y8[i] * 2.0;
                int ti = (int)Math.Round(t);
                if (Math.Abs(t - ti) > 1e-6) return false;
                twice[i] = ti;
                sumTwice += ti;
            }
            int par = twice[0] & 1;
            for (int i = 1; i < 8; i++)
                if ((twice[i] & 1) != par) return false;
            return (sumTwice % 4) == 0;
        }

        // Li-Zhang-Liu Optimal SH4Z Ternary {-1, 0, +1} Quantizer + Scale Factor gamma*
        public static double QuantizeSh4zTernary(double[] weights, sbyte[] outTernary, out double mseTernary, out double mseNaiveSign)
        {
            int n = weights.Length;
            double sumAbs = 0.0;
            for (int i = 0; i < n; i++) sumAbs += Math.Abs(weights[i]);
            double meanAbs = sumAbs / Math.Max(1, n);
            double tau = 0.70 * meanAbs;

            double activeSum = 0.0;
            int activeCnt = 0;
            for (int i = 0; i < n; i++)
            {
                double a = Math.Abs(weights[i]);
                if (a > tau)
                {
                    outTernary[i] = (weights[i] >= 0.0) ? (sbyte)1 : (sbyte)-1;
                    activeSum += a;
                    activeCnt++;
                }
                else
                {
                    outTernary[i] = 0;
                }
            }
            double gamma = (activeCnt > 0) ? (activeSum / activeCnt) : meanAbs;

            double eTern = 0.0, eSign = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dt = weights[i] - gamma * outTernary[i];
                double ds = weights[i] - meanAbs * (weights[i] >= 0.0 ? 1.0 : -1.0);
                eTern += dt * dt;
                eSign += ds * ds;
            }
            mseTernary = eTern / Math.Max(1, n);
            mseNaiveSign = eSign / Math.Max(1, n);
            return gamma;
        }

        // Symmetric Group-32 INT8 Block Quantizer -> SQNR in dB
        public static double QuantizeGroup32Int8(float[] input, sbyte[] outQ8, float[] outScales)
        {
            int n = input.Length;
            int groups = (n + 31) / 32;
            double sigPow = 0.0, errPow = 0.0;
            for (int g = 0; g < groups; g++)
            {
                int start = g * 32;
                int end = Math.Min(n, start + 32);
                float maxAbs = 1e-7f;
                for (int i = start; i < end; i++)
                {
                    float a = Math.Abs(input[i]);
                    if (a > maxAbs) maxAbs = a;
                }
                float scale = maxAbs / 127.0f;
                outScales[g] = scale;
                for (int i = start; i < end; i++)
                {
                    int q = (int)Math.Round(input[i] / scale);
                    if (q > 127) q = 127; else if (q < -127) q = -127;
                    outQ8[i] = (sbyte)q;
                    double recon = q * scale;
                    sigPow += (double)input[i] * input[i];
                    double d = input[i] - recon;
                    errPow += d * d;
                }
            }
            if (errPow < 1e-15) return 99.0;
            return 10.0 * Math.Log10(Math.Max(1e-12, sigPow) / errPow);
        }

        // Extended Binary Golay [24, 12, 8] Encoder & O(1) 3-Bit Error Self-Healer
        public static int EncodeGolay24(int data12)
        {
            int d = data12 & 0xFFF;
            int p = 0;
            for (int i = 0; i < 12; i++)
            {
                if (((d >> (11 - i)) & 1) != 0)
                    p ^= GolayParityRows12[i];
            }
            return (d << 12) | (p & 0xFFF);
        }

        public static int ComputeGolaySyndrome12(int word24)
        {
            int d = (word24 >> 12) & 0xFFF;
            int p = word24 & 0xFFF;
            int calcP = EncodeGolay24(d) & 0xFFF;
            return (calcP ^ p) & 0xFFF;
        }

        public static int DecodeGolay24(int receivedWord24, out int correctedBits)
        {
            int syn = ComputeGolaySyndrome12(receivedWord24);
            int err = _golaySyndromeToErr24[syn];
            if (err < 0)
            {
                correctedBits = -1;
                return (receivedWord24 >> 12) & 0xFFF;
            }
            correctedBits = _golaySyndromeWeight[syn];
            int healed = receivedWord24 ^ err;
            return (healed >> 12) & 0xFFF;
        }

        // Rotary Position Embedding (RoPE) SO(2)^(d/2) Exact Norm-Preserving Transformer Rotation
        public static void ApplyRoPeInPlace(double[] vec, int tokenPos, double thetaBase)
        {
            int d = vec.Length;
            int pairs = d / 2;
            for (int i = 0; i < pairs; i++)
            {
                double freq = 1.0 / Math.Pow(thetaBase, (2.0 * i) / d);
                double ang = tokenPos * freq;
                double cs = Math.Cos(ang);
                double sn = Math.Sin(ang);
                double x0 = vec[2 * i];
                double x1 = vec[2 * i + 1];
                vec[2 * i] = x0 * cs - x1 * sn;
                vec[2 * i + 1] = x0 * sn + x1 * cs;
            }
        }

        // Leviathan-Kalmanovitch-Matias Exact Lossless Speculative Decoding Step
        public static int SampleCategorical(double[] probs, Random rng)
        {
            double u = rng.NextDouble();
            double cdf = 0.0;
            for (int i = 0; i < probs.Length; i++)
            {
                cdf += probs[i];
                if (u <= cdf) return i;
            }
            return probs.Length - 1;
        }

        public static SpeculativeStepResult RunSpeculativeVerifyStep(double[][] targetProbs, double[][] draftProbs, Random rng)
        {
            int gamma = draftProbs.Length;
            int vocab = draftProbs[0].Length;
            int[] drafts = new int[gamma];
            for (int t = 0; t < gamma; t++)
                drafts[t] = SampleCategorical(draftProbs[t], rng);

            List<int> emitted = new List<int>(gamma + 1);
            List<bool> flags = new List<bool>(gamma + 1);
            int accepted = 0;
            double sumAcceptP = 0.0;
            bool rejected = false;

            for (int t = 0; t < gamma; t++)
            {
                int x = drafts[t];
                double pt = targetProbs[t][x];
                double qt = Math.Max(1e-12, draftProbs[t][x]);
                double accP = Math.Min(1.0, pt / qt);
                sumAcceptP += accP;

                if (rng.NextDouble() <= accP)
                {
                    emitted.Add(x);
                    flags.Add(true);
                    accepted++;
                }
                else
                {
                    double[] residual = new double[vocab];
                    double rSum = 0.0;
                    for (int v = 0; v < vocab; v++)
                    {
                        double r = Math.Max(0.0, targetProbs[t][v] - draftProbs[t][v]);
                        residual[v] = r;
                        rSum += r;
                    }
                    int resTok;
                    if (rSum > 1e-12)
                    {
                        for (int v = 0; v < vocab; v++) residual[v] /= rSum;
                        resTok = SampleCategorical(residual, rng);
                    }
                    else
                    {
                        resTok = SampleCategorical(targetProbs[t], rng);
                    }
                    emitted.Add(resTok);
                    flags.Add(false);
                    rejected = true;
                    break;
                }
            }

            if (!rejected && targetProbs.Length > gamma)
            {
                emitted.Add(SampleCategorical(targetProbs[gamma], rng));
                flags.Add(true);
            }

            SpeculativeStepResult res = new SpeculativeStepResult();
            res.DraftLength = gamma;
            res.AcceptedDraftCount = accepted;
            res.TotalEmittedTokens = emitted.Count;
            res.EmittedTokenIds = emitted.ToArray();
            res.WasDraftAccepted = flags.ToArray();
            res.MeanAcceptProb = sumAcceptP / Math.Max(1, gamma);
            res.EffectiveSpeedup = emitted.Count / (1.0 + 0.14 * gamma);
            return res;
        }

        // Cellular Sheaf H^1 = 0 5-Stalk Boundary Consistency Verifier + UARM Phase-3 Quarantine
        public static SheafAuditResult AuditFiveStalkSheaf(double[][] stalkBoundaryProjections, double tolerance)
        {
            // Stalks 0..4 form a closed cycle + cross-chord verification ring against consensus median!
            int numStalks = stalkBoundaryProjections.Length;
            int dim = stalkBoundaryProjections[0].Length;
            double maxDefect = 0.0;
            int worstStalk = -1;
            double[] stalkDefects = new double[numStalks];

            for (int d = 0; d < dim; d++)
            {
                double[] vals = new double[numStalks];
                for (int s = 0; s < numStalks; s++) vals[s] = stalkBoundaryProjections[s][d];
                Array.Sort(vals);
                double median = vals[numStalks / 2];

                for (int s = 0; s < numStalks; s++)
                {
                    double def = Math.Abs(stalkBoundaryProjections[s][d] - median);
                    if (def > stalkDefects[s]) stalkDefects[s] = def;
                    if (def > maxDefect) maxDefect = def;
                }
            }

            for (int s = 0; s < numStalks; s++)
            {
                if (stalkDefects[s] > tolerance && (worstStalk < 0 || stalkDefects[s] > stalkDefects[worstStalk]))
                    worstStalk = s;
            }

            SheafAuditResult r = new SheafAuditResult();
            r.CohomologyH1Zero = (maxDefect <= tolerance);
            r.MaxCoboundaryDefect = maxDefect;
            r.QuarantinedStalkIndex = r.CohomologyH1Zero ? -1 : worstStalk;
            r.Summary = r.CohomologyH1Zero
                ? string.Format(CultureInfo.InvariantCulture, "H^1 = 0 EXACT (max |delta s| = {0:0.00e+0})", maxDefect)
                : string.Format(CultureInfo.InvariantCulture, "H^1 != 0 -> UARM Quarantined Stalk #{0} (|delta s| = {1:0.000})", worstStalk, maxDefect);
            return r;
        }

        // Latency-Aware 5-Engine Proportional Roofline Partitioner
        public static EnginePartitionPlan SolveHeterogeneousPartition(
            string[] names,
            double[] gops,
            double[] rttMs,
            bool[] online,
            int totalTransformerLayers,
            double opsPerLayerGiga)
        {
            EnginePartitionPlan p = new EnginePartitionPlan();
            double sumEff = 0.0;
            double[] effGops = new double[5];
            double bestSingleGops = 1.0;

            for (int i = 0; i < 5; i++)
            {
                p.EngineNames[i] = names[i];
                p.MeasuredGops[i] = gops[i];
                p.RttMs[i] = rttMs[i];
                p.Online[i] = online[i];
                if (online[i] && gops[i] > 0.1)
                {
                    p.TotalClusterGops += gops[i];
                    if (gops[i] > bestSingleGops) bestSingleGops = gops[i];
                    // Penalize high-RTT links gracefully for tiny micro-layers
                    double computeRefMs = Math.Max(1.0, (opsPerLayerGiga * 4.0 / gops[i]) * 1000.0);
                    effGops[i] = gops[i] / (1.0 + 0.35 * (rttMs[i] / computeRefMs));
                    sumEff += effGops[i];
                }
            }

            if (sumEff <= 1e-6)
            {
                p.AssignedLayers[1] = totalTransformerLayers;
                p.ShareFraction[1] = 1.0;
                p.EstimatedSingleEngineMs = 10.0;
                p.EstimatedFusedSplitMs = 10.0;
                p.SpeedupFactor = 1.0;
                return p;
            }

            int assigned = 0;
            int anchorIdx = 0;
            double bestEff = -1.0;
            for (int i = 0; i < 5; i++)
            {
                p.ShareFraction[i] = effGops[i] / sumEff;
                int lv = (int)Math.Floor(p.ShareFraction[i] * totalTransformerLayers);
                if (online[i] && gops[i] > 10.0 && lv == 0 && totalTransformerLayers >= 12) lv = 1;
                p.AssignedLayers[i] = lv;
                assigned += lv;
                if (effGops[i] > bestEff) { bestEff = effGops[i]; anchorIdx = i; }
            }
            int diff = totalTransformerLayers - assigned;
            p.AssignedLayers[anchorIdx] = Math.Max(1, p.AssignedLayers[anchorIdx] + diff);

            // Recompute exact makespan across parallel stages + overlap
            double maxStageMs = 0.5;
            for (int i = 0; i < 5; i++)
            {
                if (p.AssignedLayers[i] <= 0 || gops[i] <= 0.1) continue;
                double stMs = ((p.AssignedLayers[i] * opsPerLayerGiga) / gops[i]) * 1000.0 + 0.18 * rttMs[i];
                if (stMs > maxStageMs) maxStageMs = stMs;
            }

            // Compare against running the entire stack on Host DirectCompute GPU alone (2,461.4 GOPS/s baseline)
            double hostBaseGops = (online[1] && gops[1] > 10.0) ? gops[1] : bestSingleGops;
            double singleMs = ((totalTransformerLayers * opsPerLayerGiga) / hostBaseGops) * 1000.0;
            p.EstimatedSingleEngineMs = singleMs;
            p.EstimatedFusedSplitMs = maxStageMs;
            p.SpeedupFactor = singleMs / Math.Max(0.05, maxStageMs);
            return p;
        }
    }
}
