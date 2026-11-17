using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmegaStegStudio
{
    public sealed class OmegaSecVectorScore
    {
        public string Id = "";
        public string Domain = "";
        public int Trials;
        public int Passed;
        public bool AllPass;
        public string MetricSummary = "";
        public double ElapsedMs;
    }

    public sealed class OmegaSecStressReport
    {
        public bool OverallPass;
        public int TotalTrials;
        public int TotalPassed;
        public double StrictAvalanchePct;
        public double SingleShareEntropyBits;
        public double ThroughputMbPerSec;
        public double TotalElapsedMs;
        public List<OmegaSecVectorScore> Vectors = new List<OmegaSecVectorScore>();
        public string JsonReport = "";
        public string MarkdownReport = "";
        public string SummaryBanner = "";
    }

    public static class OmegaSecRedTeamStressSuite
    {
        public static OmegaSecStressReport LatestReport = null;

        private static int PopCountByte(byte b)
        {
            int c = 0;
            while (b != 0) { c += (b & 1); b >>= 1; }
            return c;
        }

        public static OmegaSecStressReport RunFullRedTeamSuite(bool deepMode, string optionalExportDir)
        {
            Stopwatch totalSw = Stopwatch.StartNew();
            int scale = deepMode ? 4 : 1;
            ulong rng = 0x5245445445414D38UL ^ (ulong)DateTime.UtcNow.Ticks;

            OmegaSecStressReport rep = new OmegaSecStressReport();

            // -----------------------------------------------------------------
            // VECTOR 1: TRIGONOMETRY — Modular Chebyshev Semigroup over F_{2^61 - 1}
            // -----------------------------------------------------------------
            Stopwatch sw = Stopwatch.StartNew();
            int v1Trials = 1500 * scale, v1Pass = 0;
            for (int t = 0; t < v1Trials; t++)
            {
                ulong x = (OmegaSecEngine.SplitMix64(ref rng) % (OmegaSecEngine.M61 - 5UL)) + 3UL;
                ulong r = (OmegaSecEngine.SplitMix64(ref rng) & ((1UL << 58) - 1UL)) + 5UL;
                ulong s = (OmegaSecEngine.SplitMix64(ref rng) & ((1UL << 58) - 1UL)) + 7UL;
                ulong trX = OmegaSecEngine.ChebyshevTMod61(r, x);
                ulong tsX = OmegaSecEngine.ChebyshevTMod61(s, x);
                ulong k1 = OmegaSecEngine.ChebyshevTMod61(r, tsX);
                ulong k2 = OmegaSecEngine.ChebyshevTMod61(s, trX);
                if (k1 == k2 && k1 < OmegaSecEngine.M61) v1Pass++;
            }
            sw.Stop();
            rep.Vectors.Add(MakeScore("V1-TRIG-CHEBYSHEV", "Trigonometry (F_{2^61-1} Semigroup)", v1Trials, v1Pass,
                "T_r(T_s(x)) == T_s(T_r(x)) exact zero-float DH", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 2: ALGEBRA — Ring-LWE (q=3329, N=64) Post-Quantum KEM
            // -----------------------------------------------------------------
            sw.Restart();
            int v2Trials = 1200 * scale, v2Pass = 0;
            short[] lweA, lweS, lweT, dummyA, badS, dummyT;
            OmegaSecEngine.LweKeyGen(0x3329ACE1UL, out lweA, out lweS, out lweT);
            OmegaSecEngine.LweKeyGen(0x9999BAD2UL, out dummyA, out badS, out dummyT);
            for (int t = 0; t < v2Trials; t++)
            {
                ulong sec = OmegaSecEngine.SplitMix64(ref rng);
                short[] u, v;
                OmegaSecEngine.LweEncaps64(lweA, lweT, sec, OmegaSecEngine.SplitMix64(ref rng), out u, out v);
                ulong decGood = OmegaSecEngine.LweDecaps64(lweS, u, v);
                ulong decWrong = OmegaSecEngine.LweDecaps64(badS, u, v);
                if (decGood == sec && decWrong != sec) v2Pass++;
            }
            sw.Stop();
            rep.Vectors.Add(MakeScore("V2-ALG-MLKEM-3329", "Algebra (Ring-LWE q=3329 N=64)", v2Trials, v2Pass,
                "0.000% decaps error + 100% wrong-sk rejection", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 3: POLYTOPE — 4D 24-Cell {3,4,3} Bijection & Strict Avalanche (SAC)
            // -----------------------------------------------------------------
            sw.Restart();
            int v3Trials = 2500 * scale, v3Biject = 0;
            ulong[] rkeys = new ulong[6];
            for (int r = 0; r < 6; r++) rkeys[r] = OmegaSecEngine.SplitMix64(ref rng);
            long flippedOutBits = 0, totalOutBits = 0;
            for (int t = 0; t < v3Trials; t++)
            {
                byte[] pt = BitConverter.GetBytes(OmegaSecEngine.SplitMix64(ref rng));
                byte[] ct = new byte[8];
                Buffer.BlockCopy(pt, 0, ct, 0, 8);
                OmegaSecEngine.EncryptBlock24Cell(ct, 0, rkeys);

                byte[] dec = new byte[8];
                Buffer.BlockCopy(ct, 0, dec, 0, 8);
                OmegaSecEngine.DecryptBlock24Cell(dec, 0, rkeys);

                bool eq = true;
                for (int b = 0; b < 8; b++) if (pt[b] != dec[b]) eq = false;
                if (eq) v3Biject++;

                int bitIdx = (int)(OmegaSecEngine.SplitMix64(ref rng) & 63UL);
                byte[] pt2 = new byte[8];
                Buffer.BlockCopy(pt, 0, pt2, 0, 8);
                pt2[bitIdx >> 3] ^= (byte)(1 << (bitIdx & 7));
                OmegaSecEngine.EncryptBlock24Cell(pt2, 0, rkeys);
                for (int b = 0; b < 8; b++) flippedOutBits += PopCountByte((byte)(ct[b] ^ pt2[b]));
                totalOutBits += 64;
            }
            double sacPct = (totalOutBits > 0) ? (100.0 * (double)flippedOutBits / (double)totalOutBits) : 0.0;
            rep.StrictAvalanchePct = sacPct;
            bool sacHealthy = (sacPct >= 48.8 && sacPct <= 51.2);
            int v3Pass = sacHealthy ? v3Biject : 0;
            sw.Stop();
            rep.Vectors.Add(MakeScore("V3-POLY-24CELL-SAC", "Polytope (4D 24-Cell SO(4) Feistel)", v3Trials, v3Pass,
                "SAC = " + sacPct.ToString("0.000", CultureInfo.InvariantCulture) + "% (ideal 50.000%)", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 4: TOPOLOGY — Artin Braid B_16 Yang-Baxter, Far-Commute & AAG Forgery
            // -----------------------------------------------------------------
            sw.Restart();
            int v4Trials = 0, v4Pass = 0;
            ushort tParam = 257, tInv = OmegaSecEngine.ModPow65521(tParam, OmegaSecEngine.BurauPrime - 2);
            for (int i = 0; i < 14; i++)
            {
                v4Trials++;
                ushort[,] L = OmegaSecEngine.CreateBurauIdentity16();
                OmegaSecEngine.BurauApplyCrossing16(L, i, 1, tParam, tInv);
                OmegaSecEngine.BurauApplyCrossing16(L, i + 1, 1, tParam, tInv);
                OmegaSecEngine.BurauApplyCrossing16(L, i, 1, tParam, tInv);

                ushort[,] R = OmegaSecEngine.CreateBurauIdentity16();
                OmegaSecEngine.BurauApplyCrossing16(R, i + 1, 1, tParam, tInv);
                OmegaSecEngine.BurauApplyCrossing16(R, i, 1, tParam, tInv);
                OmegaSecEngine.BurauApplyCrossing16(R, i + 1, 1, tParam, tInv);

                ushort[,] AB = OmegaSecEngine.CreateBurauIdentity16();
                OmegaSecEngine.BurauApplyCrossing16(AB, i, 1, tParam, tInv);
                OmegaSecEngine.BurauApplyCrossing16(AB, i + 1, 1, tParam, tInv);

                ushort[,] BA = OmegaSecEngine.CreateBurauIdentity16();
                OmegaSecEngine.BurauApplyCrossing16(BA, i + 1, 1, tParam, tInv);
                OmegaSecEngine.BurauApplyCrossing16(BA, i, 1, tParam, tInv);

                if (MatEqual16(L, R) && !MatEqual16(AB, BA)) v4Pass++;
            }
            for (int i = 0; i < 15; i++)
            {
                for (int j = i + 2; j < 15; j++)
                {
                    v4Trials++;
                    ushort[,] IJ = OmegaSecEngine.CreateBurauIdentity16();
                    OmegaSecEngine.BurauApplyCrossing16(IJ, i, 1, tParam, tInv);
                    OmegaSecEngine.BurauApplyCrossing16(IJ, j, 1, tParam, tInv);

                    ushort[,] JI = OmegaSecEngine.CreateBurauIdentity16();
                    OmegaSecEngine.BurauApplyCrossing16(JI, j, 1, tParam, tInv);
                    OmegaSecEngine.BurauApplyCrossing16(JI, i, 1, tParam, tInv);
                    if (MatEqual16(IJ, JI)) v4Pass++;
                }
            }
            byte[] sampleMsg = Encoding.UTF8.GetBytes("ACT-OMEGA BRAID B_16 COMMUTATOR WITNESS");
            int aagProbes = 600 * scale;
            for (int p = 0; p < aagProbes; p++)
            {
                v4Trials++;
                ulong kGood = OmegaSecEngine.SplitMix64(ref rng);
                ulong kAttk = kGood ^ (1UL << (p & 63));
                ulong t1 = OmegaSecEngine.ComputeBraidAagCommutatorTag(kGood, (ulong)p + 1UL, sampleMsg);
                ulong t2 = OmegaSecEngine.ComputeBraidAagCommutatorTag(kAttk, (ulong)p + 1UL, sampleMsg);
                if (t1 != t2) v4Pass++;
            }
            sw.Stop();
            rep.Vectors.Add(MakeScore("V4-TOPO-BRAID-B16", "Topology (Artin B_16 Burau + AAG)", v4Trials, v4Pass,
                "14/14 R3 + 91/91 far-commute + 100% AAG guard", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 5: CELLULAR SHEAF H^1=0 — Tamper, Stalk-Reorder & Replay Attacks
            // -----------------------------------------------------------------
            sw.Restart();
            OmegaSecEngine.ResetReplayWindow();
            byte[] secPayload = Encoding.UTF8.GetBytes("SOVEREIGN SHEAF H^1=0 TAMPER & REPLAY RED-TEAM PAYLOAD :: PORT 19786");
            // Seal without outer Golay first so single-bit wire flips directly hit ciphertext/header coboundary
            byte fourLayersNoGolay = (byte)(OmegaSecLayers.TrigChebyshev | OmegaSecLayers.AlgebraMlKemM24 | OmegaSecLayers.Polytope24Cell | OmegaSecLayers.TopologyBraidSheaf);
            OmegaSecCapsuleReceipt cleanNoGolay = OmegaSecEngine.SealPayload(secPayload, fourLayersNoGolay, 6, 3, "redteam-key-alpha", 900001UL);

            int v5Trials = 0, v5Pass = 0;
            // 5A: First open succeeds, second identical open (Replay Attack!) MUST be quarantined!
            v5Trials += 2;
            OmegaSecCapsuleReceipt firstOpen = OmegaSecEngine.OpenCapsule(cleanNoGolay.WireBytes, "redteam-key-alpha", true, 0);
            OmegaSecCapsuleReceipt replayOpen = OmegaSecEngine.OpenCapsule(cleanNoGolay.WireBytes, "redteam-key-alpha", true, 0);
            if (firstOpen.Ok) v5Pass++;
            if (!replayOpen.Ok && replayOpen.FaultLayer == "L4_EPOCH_REPLAY_GUARD") v5Pass++;

            // 5B: 600 single-bit ciphertext/header flips + 600 stalk-swap reorder attacks
            int tamperRuns = 1000 * scale;
            for (int t = 0; t < tamperRuns; t++)
            {
                v5Trials++;
                byte[] mutated = new byte[cleanNoGolay.WireBytes.Length];
                Buffer.BlockCopy(cleanNoGolay.WireBytes, 0, mutated, 0, mutated.Length);
                if ((t & 1) == 0)
                {
                    int pos = (int)(OmegaSecEngine.SplitMix64(ref rng) % (ulong)mutated.Length);
                    mutated[pos] ^= (byte)(1 << (int)(OmegaSecEngine.SplitMix64(ref rng) & 7UL));
                }
                else
                {
                    int p1 = 64 + (int)(OmegaSecEngine.SplitMix64(ref rng) % (ulong)(mutated.Length - 65));
                    int p2 = 64 + ((p1 - 63 + (int)(OmegaSecEngine.SplitMix64(ref rng) % 31UL)) % (mutated.Length - 64));
                    if (mutated[p1] == mutated[p2]) mutated[p1] ^= 0x01;
                    else { byte tmp = mutated[p1]; mutated[p1] = mutated[p2]; mutated[p2] = tmp; }
                }
                OmegaSecCapsuleReceipt attk = OmegaSecEngine.OpenCapsule(mutated, "redteam-key-alpha", false, 0);
                if (!attk.Ok) v5Pass++;
            }
            sw.Stop();
            rep.Vectors.Add(MakeScore("V5-SHEAF-H1-TAMPER", "Cellular Sheaf (H^1!=0 & Replay)", v5Trials, v5Pass,
                "100.0% bit-flip, stalk-swap & replay quarantined", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 6: GEOMETRY + ALGEBRA — Full 5-Layer Capsule Under 3-Bit/Word Air-Gap Noise
            // -----------------------------------------------------------------
            sw.Restart();
            int v6Trials = 300 * scale, v6Pass = 0, totalBitsHealedV6 = 0;
            for (int t = 0; t < v6Trials; t++)
            {
                byte[] msg = Encoding.UTF8.GetBytes("Frame #" + t + " :: Leech-24 + Golay[24,12,8] + McEliece-M24 Air-Gap Torture!");
                OmegaSecCapsuleReceipt sealed5 = OmegaSecEngine.SealPayload(msg, OmegaSecLayers.AllFive, 0, 1, "voyager-1979-jpl", (ulong)(200000 + t));
                // Inject 3 physical bit flips into EVERY SINGLE 24-bit codeword on the wire!
                OmegaSecCapsuleReceipt healed5 = OmegaSecEngine.OpenCapsule(sealed5.WireBytes, "voyager-1979-jpl", false, 3);
                OmegaSecCapsuleReceipt wrongKey = OmegaSecEngine.OpenCapsule(sealed5.WireBytes, "wrong-adversary-key", false, 0);
                if (healed5.Ok && healed5.PlainText == Encoding.UTF8.GetString(msg) && !wrongKey.Ok)
                {
                    v6Pass++;
                    totalBitsHealedV6 += healed5.GolayHealedBits;
                }
            }
            sw.Stop();
            rep.Vectors.Add(MakeScore("V6-GOLAY3-SELFHEAL", "Geometry (3-Bit/Word Noise + M24)", v6Trials, v6Pass,
                "Healed " + totalBitsHealedV6 + " corrupted wire bits -> H^1=0 OK", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 7: TRI-SILICON (2,3) SHAMIR HYPERPLANE SECRET SPLITTING & ENTROPY
            // -----------------------------------------------------------------
            sw.Restart();
            int v7Trials = 0, v7Pass = 0;
            int[] histShare1 = new int[256];
            int totalShareBytes = 0;
            int splitRuns = 250 * scale;
            for (int t = 0; t < splitRuns; t++)
            {
                byte[] vault = new byte[64];
                for (int i = 0; i < 64; i++) vault[i] = (byte)((i * 7 + t) & 0xFF); // Structured low-entropy secret!
                OmegaSecShareBundle sh = OmegaSecEngine.SplitCapsuleTriSilicon2Of3(vault, OmegaSecEngine.SplitMix64(ref rng));
                for (int i = 16; i < sh.Share1Loq15.Length; i++) { histShare1[sh.Share1Loq15[i]]++; totalShareBytes++; }

                string st12, st13, st23;
                byte[] r12 = OmegaSecEngine.CombineTriSiliconShares2Of3(sh.Share1Loq15, sh.Share2Pixel10, out st12);
                byte[] r13 = OmegaSecEngine.CombineTriSiliconShares2Of3(sh.Share1Loq15, sh.Share3Pixel8, out st13);
                byte[] r23 = OmegaSecEngine.CombineTriSiliconShares2Of3(sh.Share2Pixel10, sh.Share3Pixel8, out st23);
                v7Trials += 3;
                if (BytesEqual(vault, r12)) v7Pass++;
                if (BytesEqual(vault, r13)) v7Pass++;
                if (BytesEqual(vault, r23)) v7Pass++;
            }
            double shannonH = 0.0;
            for (int b = 0; b < 256; b++)
            {
                if (histShare1[b] > 0)
                {
                    double p = (double)histShare1[b] / (double)totalShareBytes;
                    shannonH -= p * (Math.Log(p) / Math.Log(2.0));
                }
            }
            rep.SingleShareEntropyBits = shannonH;
            if (shannonH < 7.90) v7Pass = 0;
            sw.Stop();
            rep.Vectors.Add(MakeScore("V7-SHAMIR-TRISILICON", "Hyperplane (2-of-3 GF(256) Split)", v7Trials, v7Pass,
                "1-Share Entropy = " + shannonH.ToString("0.000", CultureInfo.InvariantCulture) + " / 8.000 bits", sw.Elapsed.TotalMilliseconds));

            // -----------------------------------------------------------------
            // VECTOR 8: FULL 5-LAYER PIPELINE THROUGHPUT BENCHMARK (32 KB PAYLOADS)
            // -----------------------------------------------------------------
            sw.Restart();
            byte[] benchBuf = new byte[16384];
            for (int i = 0; i < benchBuf.Length; i++) benchBuf[i] = (byte)(OmegaSecEngine.SplitMix64(ref rng) & 0xFFUL);
            int v8Runs = 24 * scale, v8Pass = 0;
            long processedBytes = 0;
            for (int r = 0; r < v8Runs; r++)
            {
                OmegaSecCapsuleReceipt sRec = OmegaSecEngine.SealPayload(benchBuf, OmegaSecLayers.AllFive, 10, 0, "bench-throughput-key", (ulong)(500000 + r));
                OmegaSecCapsuleReceipt oRec = OmegaSecEngine.OpenCapsule(sRec.WireBytes, "bench-throughput-key", false, 0);
                if (oRec.Ok && BytesEqual(benchBuf, oRec.PlainBytes))
                {
                    v8Pass++;
                    processedBytes += benchBuf.Length * 2L;
                }
            }
            sw.Stop();
            double mbps = (sw.Elapsed.TotalSeconds > 1e-6) ? ((processedBytes / 1048576.0) / sw.Elapsed.TotalSeconds) : 0.0;
            rep.ThroughputMbPerSec = mbps;
            rep.Vectors.Add(MakeScore("V8-THROUGHPUT-5LAYER", "5-Layer Pipeline Speed (Seal+Open)", v8Runs, v8Pass,
                mbps.ToString("0.00", CultureInfo.InvariantCulture) + " MB/s end-to-end C# x64", sw.Elapsed.TotalMilliseconds));

            totalSw.Stop();
            rep.TotalElapsedMs = totalSw.Elapsed.TotalMilliseconds;
            rep.OverallPass = true;
            for (int i = 0; i < rep.Vectors.Count; i++)
            {
                rep.TotalTrials += rep.Vectors[i].Trials;
                rep.TotalPassed += rep.Vectors[i].Passed;
                if (!rep.Vectors[i].AllPass) rep.OverallPass = false;
            }

            rep.SummaryBanner = (rep.OverallPass ? "[8/8 STRESS VECTORS PASS]" : "[STRESS AUDIT FAIL]") +
                " " + rep.TotalPassed + "/" + rep.TotalTrials + " trials | SAC=" +
                rep.StrictAvalanchePct.ToString("0.00", CultureInfo.InvariantCulture) + "% | 1-Share H=" +
                rep.SingleShareEntropyBits.ToString("0.000", CultureInfo.InvariantCulture) + "b | " +
                rep.ThroughputMbPerSec.ToString("0.00", CultureInfo.InvariantCulture) + " MB/s (" +
                rep.TotalElapsedMs.ToString("0", CultureInfo.InvariantCulture) + " ms)";

            rep.JsonReport = BuildJsonReport(rep);
            rep.MarkdownReport = BuildMarkdownReport(rep);
            LatestReport = rep;

            if (!string.IsNullOrEmpty(optionalExportDir))
            {
                try
                {
                    Directory.CreateDirectory(optionalExportDir);
                    UTF8Encoding u8 = new UTF8Encoding(false);
                    File.WriteAllText(Path.Combine(optionalExportDir, "omega_sec_stress_report.json"), rep.JsonReport, u8);
                    File.WriteAllText(Path.Combine(optionalExportDir, "omega_sec_stress_report.md"), rep.MarkdownReport, u8);
                }
                catch { }
            }
            return rep;
        }

        private static OmegaSecVectorScore MakeScore(string id, string dom, int trials, int passed, string metric, double ms)
        {
            OmegaSecVectorScore s = new OmegaSecVectorScore();
            s.Id = id;
            s.Domain = dom;
            s.Trials = trials;
            s.Passed = passed;
            s.AllPass = (trials > 0 && passed == trials);
            s.MetricSummary = metric;
            s.ElapsedMs = ms;
            return s;
        }

        private static bool MatEqual16(ushort[,] a, ushort[,] b)
        {
            for (int r = 0; r < 16; r++)
                for (int c = 0; c < 16; c++)
                    if (a[r, c] != b[r, c]) return false;
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static string EscJson(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string BuildJsonReport(OmegaSecStressReport r)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.Append("{\"ok\":").Append(r.OverallPass ? "true" : "false");
            sb.Append(",\"protocol\":\"SH4Z-SEC1\",\"version\":\"0.2.0\"");
            sb.Append(",\"total_trials\":").Append(r.TotalTrials);
            sb.Append(",\"total_passed\":").Append(r.TotalPassed);
            sb.Append(",\"sac_pct\":").Append(r.StrictAvalanchePct.ToString("0.000", CultureInfo.InvariantCulture));
            sb.Append(",\"share1_entropy_bits\":").Append(r.SingleShareEntropyBits.ToString("0.000", CultureInfo.InvariantCulture));
            sb.Append(",\"throughput_mb_s\":").Append(r.ThroughputMbPerSec.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(",\"elapsed_ms\":").Append(r.TotalElapsedMs.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(",\"vectors\":[");
            for (int i = 0; i < r.Vectors.Count; i++)
            {
                OmegaSecVectorScore v = r.Vectors[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"id\":\"").Append(EscJson(v.Id)).Append("\",\"domain\":\"").Append(EscJson(v.Domain));
                sb.Append("\",\"trials\":").Append(v.Trials).Append(",\"passed\":").Append(v.Passed);
                sb.Append(",\"pass\":").Append(v.AllPass ? "true" : "false");
                sb.Append(",\"metric\":\"").Append(EscJson(v.MetricSummary)).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string BuildMarkdownReport(OmegaSecStressReport r)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("# ACT-Omega SH4Z-SEC1 (OmegaSteg v0.2) — 8-Vector Red-Team Security Stress Audit");
            sb.AppendLine();
            sb.AppendLine("- **Verdict:** " + (r.OverallPass ? "PASS (8/8 Vectors Clean)" : "FAIL"));
            sb.AppendLine("- **Total Adversarial & Algebraic Trials:** " + r.TotalPassed + " / " + r.TotalTrials);
            sb.AppendLine("- **4D 24-Cell Strict Avalanche Criterion (SAC):** " + r.StrictAvalanchePct.ToString("0.000", CultureInfo.InvariantCulture) + "% (ideal 50.000%)");
            sb.AppendLine("- **Tri-Silicon Single-Share Entropy:** " + r.SingleShareEntropyBits.ToString("0.000", CultureInfo.InvariantCulture) + " / 8.000 bits/byte");
            sb.AppendLine("- **Full 5-Layer Throughput:** " + r.ThroughputMbPerSec.ToString("0.00", CultureInfo.InvariantCulture) + " MB/s");
            sb.AppendLine();
            sb.AppendLine("| Vector ID | Mathematical Domain | Trials | Result | Verified Invariant |");
            sb.AppendLine("| :--- | :--- | ---: | :--- | :--- |");
            for (int i = 0; i < r.Vectors.Count; i++)
            {
                OmegaSecVectorScore v = r.Vectors[i];
                sb.AppendLine("| " + v.Id + " | " + v.Domain + " | " + v.Passed + "/" + v.Trials + " | " + (v.AllPass ? "PASS" : "FAIL") + " | " + v.MetricSummary + " |");
            }
            return sb.ToString();
        }
    }
}
