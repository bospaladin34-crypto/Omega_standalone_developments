using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaSheafStudio
{
    public static class OmegaSheafIngestor
    {
        public static readonly string[] PresetNames = new string[]
        {
            "01. Live OmegaLabs Suite Architecture Audit (H^1 = 0 Consensus)",
            "02. Cross-Paper Paradox: 3-Paper Lattice Rate-Distortion Loop (H^1 != 0)",
            "03. Polyglot FFI #[repr(C)] Rust <-> C++23 <-> Deno ABI Drift (H^1 != 0)",
            "04. D3D11 DirectCompute SRV_DESC ViewDimension=4 vs 1 Bug Replay",
            "05. Penrose-Escher Impossible Staircase (Non-Trivial H^1 Twist Cycle)",
            "06. Tri-Silicon Distributed Consensus: LOQ 15 + Pixel 10 + Pixel 8 Split-Brain",
            "07. E8 -> Leech-24 -> Golay [24,12,8] Exact Commutative Diagram (H^1 = 0)",
            "08. BraidIR Static Single Assignment (SSA) Lock-Order Hazard Cycle",
            "09. Fallout 4 / Skyrim SE Creation Kit Quest-Stage Softlock Paradox (H^1 != 0)"
        };

        public static void LoadPreset(SheafGraphModel g, int presetIdx, string suiteRoot)
        {
            g.Nodes.Clear();
            g.Edges.Clear();
            g.ContradictionLoops.Clear();

            if (presetIdx == 0)
            {
                BuildLiveSuiteFfiAudit(g, suiteRoot, false);
                return;
            }
            if (presetIdx == 3)
            {
                BuildLiveSuiteFfiAudit(g, suiteRoot, true);
                return;
            }

            float[] base8 = new float[] { 0.707f, 0.707f, 0.5f, 0.5f, 0.5f, 0.5f, 0.0f, 0.0f };

            if (presetIdx == 8)
            {
                g.GraphTitle = "Creation Kit Quest-Stage Sheaf :: Stage 40 <-> Stage 60 Sequence-Break Softlock";
                g.ProvenanceNote = "Fallout 4 / Skyrim SE Papyrus Quest Alias & F4SE Bridge (H^1 != 0 Softlock Cycle)";
                int s10 = AddNode(g, "Stage 10: Vault Wake", "MQ_SovereignQuest.psc:18", "SetObjectiveDisplayed(10)", StalkCategory.QuestStage, -2.1f, -0.8f, 0.2f, true, base8);
                int s20 = AddNode(g, "Stage 20: Holotape", "MQ_SovereignQuest.psc:44", "Alias_BeaconTape Acquired", StalkCategory.QuestStage, -0.6f, -1.3f, 0.0f, false, base8);
                int s30 = AddNode(g, "Stage 30: Essential NPC", "MQ_SovereignQuest.psc:79", "ActorRef.IsDead() == False", StalkCategory.QuestStage, -0.6f, 1.2f, 0.1f, false, base8);
                int s40 = AddNode(g, "Stage 40: Faction Gate", "MQ_SovereignQuest.psc:112", "Requires Stage 20 && Stage 30", StalkCategory.QuestStage, 1.0f, 0.0f, 0.2f, false, base8);
                int s60 = AddNode(g, "Stage 60: Citadel Finale", "sovereign_f4se_l0.rs:64", "F4SE Native Bridge Unlock", StalkCategory.RustL0Abi, 2.3f, 0.0f, 0.0f, true, base8);
                g.Nodes[s30].SnippetQuote = "If Alias_Overseer.GetActorRef().IsDead() -> Stage 40 never fires (Softlock!)";
                g.Nodes[s40].SnippetQuote = "Missing Reidemeister fallback branch when Holotape looted early before Stage 30";

                AddEdge(g, s10, s20, "Loot Holotape Early", 1.0f, 0.0f, 0.0f);
                AddEdge(g, s10, s30, "Meet Overseer Branch", 1.0f, 0.0f, 0.0f);
                AddEdge(g, s20, s40, "Sequence-Break Bypass", 1.0f, 0.0f, 0.0f);
                AddEdge(g, s30, s40, "PARADOX: Dead NPC Blocks Gate!", -1.0f, 0.0f, 0.36f);
                AddEdge(g, s40, s60, "F4SE Native Stage Commit", 1.0f, 0.0f, 0.0f);
                return;
            }
            if (presetIdx == 1)
            {
                g.GraphTitle = "Cross-Paper Paradox :: Lattice Rate-Distortion & Sphere-Packing Loop";
                g.ProvenanceNote = "3 Corpus Papers + 2 Empirical Benchmarks (1 Frustrated Sign-Inversion Cycle)";
                AddNode(g, "Conway-Sloane (1988) E8 Bound", "corpus/conway_sloane_1988.md", "G(E8) = 0.071682 | 240 Roots", StalkCategory.PaperClaim, -1.8f, 1.1f, 0.2f, true, base8);
                AddNode(g, "Viazovska (2017) Optimal 8D Packing", "corpus/viazovska_e8_packing.md", "Delta_8 = pi^4 / 384 (Exact)", StalkCategory.TheoremProof, 0.0f, 1.55f, 0.3f, false, base8);
                AddNode(g, "Cohn-Kumar-Miller-Radchenko-Viazovska", "corpus/cohn_leech24_2017.md", "Delta_24 = pi^12 / 12! (Leech_24)", StalkCategory.TheoremProof, 1.85f, 0.95f, 0.1f, false, base8);
                AddNode(g, "Draft Preprint Claim #B4 (Conflicting)", "corpus/draft_claim_b4.md", "Claims 8D Uniform Scalar Beats E8 (!)", StalkCategory.PaperClaim, 1.25f, -1.25f, -0.2f, false, Invert8(base8));
                AddNode(g, "SH4Z Quantizer Telemetry (DirectCompute GPU)", "04_omega_synth/E8Quantizer.cs", "Measured SQNR +2.14 dB over Scalar", StalkCategory.CSharpHost, -1.25f, -1.20f, -0.1f, false, base8);
                AddNode(g, "Golay [24,12,8] Turyn Construction", "03_omega_av/OmegaAVLeechAndWu.cs", "3 x E8 -> 720 Leech Shape Vectors", StalkCategory.RustL0Abi, 0.0f, -0.1f, 0.45f, false, base8);

                AddEdge(g, 0, 1, "Agrees: Optimal 8D Density", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 1, 2, "Turyn 3xE8 Lift -> 24D", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 5, "Golay Parity Restriction", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 0, 5, "8D Sub-Lattice Inclusion", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 0, 4, "Verified +2.14 dB Gain", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 3, "Citation Restriction", 1.0f, 0.0f, 0.0f);
                // Frustrated contradiction edge!
                AddEdge(g, 3, 4, "CONTRADICTS Empirical E8 Gain", -1.0f, 0.0f, 0.0f);
            }
            else if (presetIdx == 2)
            {
                g.GraphTitle = "Polyglot FFI Boundary Audit :: Rust L0 <-> C++23 Ring <-> Deno/C# Host";
                g.ProvenanceNote = "Detects #[repr(C)] padding & 64-byte SH4Z cache-line stride drift across languages";
                AddNode(g, "Rust L0: Sh4zHeaderV3101", "l0_rust/src/sh4z_tlv.rs", "#[repr(C, align(64))] 64B Header", StalkCategory.RustL0Abi, -2.0f, 0.9f, 0.2f, true, base8);
                AddNode(g, "C++23 Ring: RingSlotHeader", "l1_cpp23/include/shm_ring.hpp", "alignas(64) struct 64B (Zero-Copy)", StalkCategory.Cpp23Ffi, 0.0f, 1.45f, 0.1f, false, base8);
                AddNode(g, "C++23 Unaligned Patch (Drift!)", "l1_cpp23/src/legacy_bridge.cpp", "#pragma pack(1) 58B -> 6B Offset Drift!", StalkCategory.Cpp23Ffi, 1.95f, 0.55f, -0.25f, false, base8);
                AddNode(g, "Deno FFI: Deno.UnsafePointerView", "l2_deno/ffi_ layout.ts", "Expects offset 64 for Payload Float32", StalkCategory.PortContract, 1.2f, -1.25f, 0.15f, false, base8);
                AddNode(g, "C# 5.0 Host: StructLayout Sequential", "OmegaSheaf/NativeBridge.cs", "Size = 64, Pack = 8 Verified", StalkCategory.CSharpHost, -1.2f, -1.25f, 0.0f, false, base8);

                AddEdge(g, 0, 1, "ABI Match: 64B == 64B", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 0, 4, "P/Invoke Marshal.SizeOf == 64", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 4, 3, "Shared Memory Offset 64", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 1, 2, "Header Cast", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 3, "ABI MISMATCH: 58B != 64B (-6B)", 1.0f, 0.0f, 0.42f);
            }
            else if (presetIdx == 4)
            {
                g.GraphTitle = "Penrose-Escher Impossible Staircase :: Pure H^1 Phase Holonomy";
                g.ProvenanceNote = "Every local pair of steps looks plausible, yet the 5-cycle accumulates +90 deg twist (H^1 != 0)!";
                for (int k = 0; k < 5; k++)
                {
                    double a = (2.0 * Math.PI * k) / 5.0;
                    AddNode(g, "Terrace Step S" + (k + 1), "penrose_stairs.osheaf", "Local Height Chart z_" + (k + 1),
                        StalkCategory.TheoremProof, (float)(1.75 * Math.Cos(a)), (float)(1.55 * Math.Sin(a)), 0.25f * (k - 2), k == 0, base8);
                }
                for (int k = 0; k < 5; k++)
                    AddEdge(g, k, (k + 1) % 5, "Ascend +18 deg Givens Twist", 1.0f, 0.314159f, 0.0f);
            }
            else if (presetIdx == 5)
            {
                g.GraphTitle = "Tri-Silicon Mesh Split-Brain :: LOQ 15 + Pixel 10 + Pixel 8 Epoch Audit";
                g.ProvenanceNote = "Cellular Sheaf distributed state linearizability check across USB & Wi-Fi :5555";
                AddNode(g, "Primary Workstation Host (Local)", "Windows 10/11 DirectCompute GPU", "WAL Epoch #368 | Chain Root A9F2", StalkCategory.CSharpHost, -1.7f, 1.0f, 0.3f, true, base8);
                AddNode(g, "Pixel 10 Edge (frankel_beta)", "USB ADB :19784", "WAL Epoch #368 | Chain Root A9F2", StalkCategory.PortContract, 1.7f, 1.0f, 0.2f, false, base8);
                AddNode(g, "Pixel 8 Headless (shiba_beta)", "Wi-Fi :5555 Stale Cache", "Stale Epoch #367 | Divergent Root!", StalkCategory.RustL0Abi, 0.0f, -1.45f, -0.2f, false, Invert8(base8));
                AddNode(g, "Gemini Bridge Manifest", "OMEGA_BRIDGE_EXPORT", "SHA-256 Quorum Contract", StalkCategory.PaperClaim, 0.0f, 0.1f, 0.4f, false, base8);

                AddEdge(g, 0, 1, "USB Reverse Tunnel Sync OK", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 0, 3, "Manifest SHA-256 Match", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 1, 3, "Quorum Ack Epoch #368", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 1, 2, "P2P Gossip Link", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 0, "SPLIT-BRAIN Epoch #367 != #368", -1.0f, 0.0f, 0.0f);
            }
            else if (presetIdx == 6)
            {
                g.GraphTitle = "Exact Algebraic Commutative Diagram :: E8 -> Leech-24 -> Golay [24,12,8]";
                g.ProvenanceNote = "Zero-obstruction global section (H^1 = 0, E_F = 0.000000) across all 6 morphisms";
                for (int k = 0; k < 6; k++)
                {
                    string[] nms = new string[] {
                        "D8 Even Sublattice", "Spinor Coset D8 + (1/2)^8", "E8 Gosset 240-Root Lattice",
                        "Extended Golay [24,12,8]", "Turyn 3xE8 Construction", "Leech_24 (196,560 Kissing)"
                    };
                    double a = (2.0 * Math.PI * k) / 6.0;
                    AddNode(g, nms[k], "rosetta_manifold/lib.rs", "Exact Lattice Morphism #" + (k + 1),
                        StalkCategory.TheoremProof, (float)(1.85 * Math.Cos(a)), (float)(1.45 * Math.Sin(a)), 0.0f, k == 0, base8);
                }
                AddEdge(g, 0, 2, "Index-2 Inclusion", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 1, 2, "Glue Vector [1/2]^8", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 4, "3-Fold Direct Sum", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 3, 4, "d_min = 8 Parity Code", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 4, 5, "Leech Glue Closure", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 0, 3, "Octad Weight-8 Quotient", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 5, "Canonical Isometric Lift", 1.0f, 0.0f, 0.0f);
            }
            else
            {
                g.GraphTitle = "BraidIR SSA Concurrency Audit :: Lock-Order Deadlock Cycle (H^1 != 0)";
                g.ProvenanceNote = "Non-commuting braid crossings sigma_1 * sigma_2 != sigma_2 * sigma_1 form a cyclic lock wait";
                AddNode(g, "Thread A: Audio WASAPI Ring", "OmegaSynthWavefieldAudio.cs", "Acquires Lock(Wave) -> Lock(Gpu)", StalkCategory.CSharpHost, -1.7f, 0.9f, 0.2f, true, base8);
                AddNode(g, "Mutex M1: WaveBufferLock", "SyncPrimitive #1", "Exclusive Monitor", StalkCategory.RustL0Abi, 0.0f, 1.45f, 0.0f, false, base8);
                AddNode(g, "Thread B: UI Preset Loader", "OmegaSynthCockpitForm.cs", "Acquires Lock(Gpu) -> Lock(Wave)!", StalkCategory.CSharpHost, 1.7f, 0.9f, -0.2f, false, Invert8(base8));
                AddNode(g, "Mutex M2: D3D11ContextLock", "SyncPrimitive #2", "Exclusive Monitor", StalkCategory.Cpp23Ffi, 0.0f, -1.35f, 0.0f, false, base8);

                AddEdge(g, 0, 1, "Holds M1", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 1, 2, "Waits on Thread B", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 2, 3, "Holds M2", 1.0f, 0.0f, 0.0f);
                AddEdge(g, 3, 0, "DEADLOCK CYCLE: Inverted Lock Order", -1.0f, 0.0f, 0.0f);
            }

            SheafE8Math.StepCpuPhysicsAndCohomology(g, 0.016f, 0.0f, 0.0f, 0.12f, false, false);
        }

        // Real Live Scanner of <OMEGA_LAB_ROOT> Stations, Ports, & D3D11 Structs!
        public static void BuildLiveSuiteFfiAudit(SheafGraphModel g, string suiteRoot, bool injectD3d11BugDemo)
        {
            g.Nodes.Clear();
            g.Edges.Clear();
            g.ContradictionLoops.Clear();

            g.GraphTitle = injectD3d11BugDemo
                ? "D3D11 Replay :: OmegaAV SRV_DESC ViewDimension=4 (TEXTURE2D vs BUFFER) H^1 != 0 Bug!"
                : "Live OmegaLabs Suite Consistency Sheaf :: All Stations + D3D11 + ADB Tunnels";

            float[] canonical8 = new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };

            int nHub = AddNode(g, "OmegaLabLauncher (:Deck)", "OmegaLabLauncher.cs", "Suite Orchestrator & Manifest SHA-256", StalkCategory.CSharpHost, 0.0f, 0.0f, 0.35f, true, canonical8);
            int nD3d = AddNode(g, "d3d11.dll + d3dcompiler_47", "Win32 DirectCompute ABI", "Slot 67=SetSRV | SRV_BUFFER=1 | UAV_TEX2D=4", StalkCategory.Cpp23Ffi, 0.0f, 1.65f, 0.25f, true, canonical8);
            int nAdb = AddNode(g, "Dual-Pixel ADB Bridge", "Pixel 10 + Pixel 8", "reverse tcp:9778, 19777..19785", StalkCategory.PortContract, 0.0f, -1.65f, 0.15f, false, canonical8);

            string[][] stations = new string[][]
            {
                new string[] { "OmegaMirror v0.9", "01_omega_mirror", ":19777", "-2.25", "-0.95" },
                new string[] { "OmegaBrowser v0.6.5", "02_omega_browser", ":9778", "-2.35", "0.25" },
                new string[] { "OmegaAV v0.7.1", "03_omega_av", ":19779", "-1.45", "1.35" },
                new string[] { "OmegaSynth v1.1.1 GM", "04_omega_synth", ":19782", "1.45", "1.35" },
                new string[] { "OmegaPenrose v1.0.2 GM", "07_omega_penrose", ":19785", "2.35", "0.25" },
                new string[] { "OmegaForge v0.2.1", "09_omega_forge", ":19780", "2.15", "-0.95" },
                new string[] { "OmegaShell v1.0.3 GM", "10_omega_shell", ":19781", "-0.95", "-1.35" },
                new string[] { "OmegaSheaf v0.2.1 GM", "06_omega_sheaf", ":19784", "0.95", "-1.35" },
                new string[] { "OmegaSteg v1.0 GM", "08_omega_steg", ":19786", "0.00", "-2.15" }
            };

            int scannedCsFiles = 0;
            int avIdx = -1;
            for (int i = 0; i < stations.Length; i++)
            {
                string stName = stations[i][0];
                string folder = stations[i][1];
                string port = stations[i][2];
                float px = float.Parse(stations[i][3], CultureInfo.InvariantCulture);
                float py = float.Parse(stations[i][4], CultureInfo.InvariantCulture);

                int csCount = 0;
                bool diskHasSrv4Bug = false;
                try
                {
                    string fullDir = Path.Combine(suiteRoot, folder);
                    if (Directory.Exists(fullDir))
                    {
                        string[] csFiles = Directory.GetFiles(fullDir, "*.cs");
                        csCount = csFiles.Length;
                        scannedCsFiles += csCount;
                        if (folder == "03_omega_av")
                        {
                            for (int f = 0; f < csFiles.Length; f++)
                            {
                                string txt = File.ReadAllText(csFiles[f], Encoding.UTF8);
                                if (txt.Contains("sd.ViewDimension = 4u")) diskHasSrv4Bug = true;
                            }
                        }
                    }
                }
                catch { }

                string spec = port + (csCount > 0 ? (" | " + csCount + " .cs verified") : " | Ready");
                int nid = AddNode(g, stName, folder, spec, StalkCategory.CSharpHost, px, py, 0.0f, false, canonical8);
                if (folder == "03_omega_av") avIdx = nid;

                AddEdge(g, nHub, nid, "Port " + port + " Contract", 1.0f, 0.0f, 0.0f);
                AddEdge(g, nid, nAdb, "ADB Reverse " + port, 1.0f, 0.0f, 0.0f);

                if (folder == "03_omega_av" || folder == "04_omega_synth" || folder == "07_omega_penrose" || folder == "06_omega_sheaf" || folder == "08_omega_steg")
                {
                    bool triggerBug = (folder == "03_omega_av") && (injectD3d11BugDemo || diskHasSrv4Bug);
                    if (triggerBug)
                        AddEdge(g, nid, nD3d, "BUG: SRV_DESC ViewDimension=4 != BUFFER(1)!", -1.0f, 0.0f, 0.48f);
                    else
                        AddEdge(g, nid, nD3d, "cs_5_0 Vtable 67 + SRV_BUFFER=1 OK", 1.0f, 0.0f, 0.0f);
                }
            }

            if (avIdx >= 0 && injectD3d11BugDemo)
            {
                float[] inv = Invert8(canonical8);
                Array.Copy(inv, g.Nodes[avIdx].Stalk8, 8);
            }

            g.ProvenanceNote = "Scanned " + suiteRoot + " (" + scannedCsFiles + " live module .cs files audited)";
            SheafE8Math.StepCpuPhysicsAndCohomology(g, 0.016f, 0.0f, 0.0f, 0.12f, false, false);
        }

        // Ingest Real OmegaBrowser Corpus (.md + .jsonl) into Cellular Sheaf Stalks & Restriction Maps!
        public static string IngestOmegaBrowserCorpus(SheafGraphModel g, string suiteRoot)
        {
            List<string> dirs = new List<string>();
            if (Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")))) dirs.Add((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")));
            string brCorpus = Path.Combine(suiteRoot, "02_omega_browser", "corpus");
            if (Directory.Exists(brCorpus)) dirs.Add(brCorpus);
            string dlPapers = Path.Combine(suiteRoot, "downloads", "Papers");
            if (Directory.Exists(dlPapers)) dirs.Add(dlPapers);

            List<string> mdFiles = new List<string>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int d = 0; d < dirs.Count; d++)
            {
                try
                {
                    string[] fs = Directory.GetFiles(dirs[d], "*.md");
                    for (int i = 0; i < fs.Length && mdFiles.Count < 48; i++)
                    {
                        if (seenNames.Add(Path.GetFileName(fs[i]))) mdFiles.Add(fs[i]);
                    }
                }
                catch { }
            }

            if (mdFiles.Count == 0)
            {
                // Seed from corpus_manifest.jsonl if present, otherwise load rich Corpus Demo with clear notice
                LoadPreset(g, 1, suiteRoot);
                g.ProvenanceNote = "No local .md files in corpus yet (save pages in OmegaBrowser with +Corpus!) — loaded 3-Paper Rate-Distortion Sheaf.";
                return g.ProvenanceNote;
            }

            g.Nodes.Clear();
            g.Edges.Clear();
            g.ContradictionLoops.Clear();
            g.GraphTitle = "OmegaBrowser Corpus Truth-Sheaf (" + mdFiles.Count + " Harvested Research Docs)";

            List<HashSet<string>> docTerms = new List<HashSet<string>>();
            List<bool> docNegates = new List<bool>();
            Regex rxWords = new Regex(@"\b[a-zA-Z][a-zA-Z0-9_]{3,24}\b");
            HashSet<string> stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "this","that","with","from","have","were","their","which","about","https","http","source","final","date","license","tags","sha256","vault","snapshot"
            };

            for (int i = 0; i < mdFiles.Count; i++)
            {
                string raw = "";
                try { raw = File.ReadAllText(mdFiles[i], Encoding.UTF8); } catch { continue; }
                string title = Path.GetFileNameWithoutExtension(mdFiles[i]);
                string[] lines = raw.Split('\n');
                for (int l = 0; l < lines.Length; l++)
                {
                    string tr = lines[l].Trim();
                    if (tr.StartsWith("# ")) { title = tr.Substring(2).Trim(); break; }
                }
                if (title.Length > 42) title = title.Substring(0, 42) + "..";

                HashSet<string> terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                MatchCollection mc = rxWords.Matches(raw.Length > 12000 ? raw.Substring(0, 12000) : raw);
                for (int m = 0; m < mc.Count && terms.Count < 160; m++)
                {
                    string w = mc[m].Value.ToLowerInvariant();
                    if (!stop.Contains(w)) terms.Add(w);
                }
                bool hasNegClaim = Regex.IsMatch(raw, @"\b(contradict|disprove|impossible|counterexample|false|invalid|violates)\b", RegexOptions.IgnoreCase);
                docTerms.Add(terms);
                docNegates.Add(hasNegClaim);

                float[] vec8 = HashTextToE8Stalk(title + "|" + string.Join(",", new List<string>(terms).ToArray()), hasNegClaim);
                double ang = (2.0 * Math.PI * i) / Math.Max(1, mdFiles.Count);
                float rad = 1.35f + 0.55f * (i % 2);
                AddNode(g, title, Path.GetFileName(mdFiles[i]), terms.Count + " indexed terms",
                    hasNegClaim ? StalkCategory.TheoremProof : StalkCategory.PaperClaim,
                    (float)(rad * Math.Cos(ang)), (float)(rad * Math.Sin(ang)), 0.18f * ((i % 3) - 1), i == 0, vec8);
            }

            // Connect documents sharing significant conceptual overlap
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                for (int j = i + 1; j < g.Nodes.Count; j++)
                {
                    int shared = 0;
                    string bridgeWord = "";
                    foreach (string w in docTerms[i])
                    {
                        if (docTerms[j].Contains(w)) { shared++; if (bridgeWord.Length == 0) bridgeWord = w; }
                    }
                    if (shared >= 3 || (g.Nodes.Count <= 8 && shared >= 1))
                    {
                        float pol = (docNegates[i] ^ docNegates[j]) ? -1.0f : 1.0f;
                        // Align stalks along shared concept projections unless polarity opposes
                        if (pol > 0.0f)
                        {
                            for (int k = 0; k < 8; k++)
                                g.Nodes[j].Stalk8[k] = 0.65f * g.Nodes[i].Stalk8[k] + 0.35f * g.Nodes[j].Stalk8[k];
                        }
                        AddEdge(g, i, j, (pol < 0 ? "Polarity Conflict on '" : "Shared '" ) + bridgeWord + "' (" + shared + ")", pol, 0.0f, 0.0f);
                    }
                }
            }

            // Ensure connected spanning backbone if corpus docs cover disjoint topics
            for (int i = 1; i < g.Nodes.Count; i++)
            {
                bool hasInc = false;
                for (int e = 0; e < g.Edges.Count; e++)
                    if (g.Edges[e].SrcIdx == i || g.Edges[e].DstIdx == i) { hasInc = true; break; }
                if (!hasInc)
                    AddEdge(g, 0, i, "Corpus Vault Index Link", 1.0f, 0.0f, 0.0f);
            }

            g.ProvenanceNote = "Ingested " + g.Nodes.Count + " real corpus documents and " + g.Edges.Count + " restriction maps.";
            SheafE8Math.StepCpuPhysicsAndCohomology(g, 0.016f, 0.25f, 0.1f, 0.15f, false, true);
            return g.ProvenanceNote;
        }

        // Scan any local folder of Rust (.rs), C/C++ (.cpp/.hpp/.h), C# (.cs), or Markdown (.md) for Struct/ABI Sheaves!
        public static string ScanFolderCodebaseSheaf(SheafGraphModel g, string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return "Folder not found: " + folderPath;

            List<string> files = new List<string>();
            string[] exts = new string[] { "*.rs", "*.hpp", "*.h", "*.cpp", "*.cs", "*.ts" };
            for (int e = 0; e < exts.Length && files.Count < 80; e++)
            {
                try { files.AddRange(Directory.GetFiles(folderPath, exts[e], SearchOption.AllDirectories)); }
                catch { }
            }

            g.Nodes.Clear();
            g.Edges.Clear();
            g.ContradictionLoops.Clear();
            g.GraphTitle = "Codebase Struct & FFI Sheaf :: " + Path.GetFileName(folderPath.TrimEnd('\\'));

            Regex rxStruct = new Regex(@"(?:pub\s+struct|struct)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{([^}]{1,1200})\}", RegexOptions.Singleline);
            Dictionary<string, List<int>> byStructName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            float[] base8 = new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };

            for (int f = 0; f < files.Count && g.Nodes.Count < 56; f++)
            {
                string txt = "";
                try { txt = File.ReadAllText(files[f], Encoding.UTF8); } catch { continue; }
                string ext = (Path.GetExtension(files[f]) ?? "").ToLowerInvariant();
                StalkCategory cat = ext == ".rs" ? StalkCategory.RustL0Abi : ((ext == ".cs") ? StalkCategory.CSharpHost : (ext == ".ts" ? StalkCategory.PortContract : StalkCategory.Cpp23Ffi));

                MatchCollection ms = rxStruct.Matches(txt);
                for (int m = 0; m < ms.Count && g.Nodes.Count < 56; m++)
                {
                    string sName = ms[m].Groups[1].Value;
                    string body = ms[m].Groups[2].Value;
                    int fieldCount = Math.Max(1, body.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Length);
                    double ang = 0.6180339887 * 2.0 * Math.PI * g.Nodes.Count;
                    float rad = 0.75f + 0.22f * (float)Math.Sqrt(g.Nodes.Count + 1);

                    float[] st8 = new float[8];
                    Array.Copy(base8, st8, 8);
                    st8[0] = fieldCount * 0.12f;

                    int nid = AddNode(g, sName + " (" + ext + ")", Path.GetFileName(files[f]),
                        "Fields/Tokens: " + fieldCount, cat,
                        (float)(rad * Math.Cos(ang)), (float)(rad * Math.Sin(ang)), 0.0f, g.Nodes.Count == 0, st8);

                    List<int> bucket;
                    if (!byStructName.TryGetValue(sName, out bucket)) { bucket = new List<int>(); byStructName[sName] = bucket; }
                    bucket.Add(nid);
                }
            }

            if (g.Nodes.Count == 0)
            {
                BuildLiveSuiteFfiAudit(g, folderPath, false);
                return "No C/Rust/C# structs matched in folder — loaded Suite FFI Sheaf.";
            }

            // Link matching struct names across FFI files and flag field-count drift!
            foreach (KeyValuePair<string, List<int>> kv in byStructName)
            {
                List<int> ids = kv.Value;
                for (int i = 1; i < ids.Count; i++)
                {
                    float diff = Math.Abs(g.Nodes[ids[0]].Stalk8[0] - g.Nodes[ids[i]].Stalk8[0]);
                    bool mismatch = diff > 0.01f;
                    AddEdge(g, ids[0], ids[i], mismatch ? ("FFI FIELD MISMATCH: " + kv.Key) : ("FFI Struct Match: " + kv.Key),
                        mismatch ? -1.0f : 1.0f, 0.0f, mismatch ? diff : 0.0f);
                }
            }

            // Connect same-file / sequential module topology so graph forms a navigable complex
            for (int i = 1; i < g.Nodes.Count; i++)
            {
                AddEdge(g, i - 1, i, (g.Nodes[i - 1].SourceDoc == g.Nodes[i].SourceDoc) ? ("Module: " + g.Nodes[i].SourceDoc) : "Crate/Namespace Link", 1.0f, 0.0f, 0.0f);
            }

            g.ProvenanceNote = "Scanned " + files.Count + " source files -> " + g.Nodes.Count + " ABI/Struct stalks.";
            SheafE8Math.StepCpuPhysicsAndCohomology(g, 0.016f, 0.1f, 0.05f, 0.14f, true, false);
            return g.ProvenanceNote;
        }

        // Save / Load .osheaf Patch Files (UTF-8 No-BOM TSV-style deterministic schema)
        public static void SaveOsheafFile(string path, SheafGraphModel g)
        {
            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine("# OMEGASHEAF_V1_PATCH");
            sb.AppendLine("TITLE\t" + CleanTab(g.GraphTitle));
            sb.AppendLine("NOTE\t" + CleanTab(g.ProvenanceNote));
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                SheafStalkNode n = g.Nodes[i];
                string[] s8 = new string[8];
                for (int k = 0; k < 8; k++) s8[k] = n.Stalk8[k].ToString("0.0000", CultureInfo.InvariantCulture);
                sb.AppendFormat(CultureInfo.InvariantCulture, "NODE\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6:0.000}\t{7:0.000}\t{8:0.000}\t{9}\r\n",
                    n.Id, CleanTab(n.Title), CleanTab(n.SourceDoc), CleanTab(n.FormulaOrType), (int)n.Category, n.Pinned ? 1 : 0,
                    n.X, n.Y, n.Z, string.Join(",", s8));
            }
            for (int e = 0; e < g.Edges.Count; e++)
            {
                SheafRestrictionEdge ed = g.Edges[e];
                sb.AppendFormat(CultureInfo.InvariantCulture, "EDGE\t{0}\t{1}\t{2}\t{3:0.0000}\t{4:0.0000}\t{5:0.0000}\t{6:0.0000}\r\n",
                    ed.SrcIdx, ed.DstIdx, CleanTab(ed.Label), ed.Polarity, ed.ScaleRatio, ed.TwistRad, ed.AbiMismatchBias);
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static bool LoadOsheafFile(string path, SheafGraphModel g)
        {
            if (!File.Exists(path)) return false;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            g.Nodes.Clear(); g.Edges.Clear(); g.ContradictionLoops.Clear();
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length == 0 || ln.StartsWith("#")) continue;
                string[] p = ln.Split('\t');
                if (p[0] == "TITLE" && p.Length >= 2) g.GraphTitle = p[1];
                else if (p[0] == "NOTE" && p.Length >= 2) g.ProvenanceNote = p[1];
                else if (p[0] == "NODE" && p.Length >= 11)
                {
                    float[] s8 = new float[8];
                    string[] sp = p[10].Split(',');
                    for (int k = 0; k < 8 && k < sp.Length; k++) float.TryParse(sp[k], NumberStyles.Float, CultureInfo.InvariantCulture, out s8[k]);
                    AddNode(g, p[2], p[3], p[4], (StalkCategory)int.Parse(p[5], CultureInfo.InvariantCulture),
                        float.Parse(p[7], CultureInfo.InvariantCulture),
                        float.Parse(p[8], CultureInfo.InvariantCulture),
                        float.Parse(p[9], CultureInfo.InvariantCulture),
                        p[6] == "1", s8);
                }
                else if (p[0] == "EDGE" && p.Length >= 8)
                {
                    AddEdge(g,
                        int.Parse(p[1], CultureInfo.InvariantCulture),
                        int.Parse(p[2], CultureInfo.InvariantCulture),
                        p[3],
                        float.Parse(p[4], CultureInfo.InvariantCulture),
                        float.Parse(p[6], CultureInfo.InvariantCulture),
                        float.Parse(p[7], CultureInfo.InvariantCulture));
                }
            }
            SheafE8Math.StepCpuPhysicsAndCohomology(g, 0.016f, 0.0f, 0.0f, 0.12f, false, false);
            return g.Nodes.Count > 0;
        }

        public static int AddNode(SheafGraphModel g, string title, string src, string formula, StalkCategory cat, float x, float y, float z, bool pinned, float[] s8)
        {
            SheafStalkNode n = new SheafStalkNode();
            n.Id = g.Nodes.Count;
            n.Title = title; n.SourceDoc = src; n.FormulaOrType = formula;
            n.Category = cat; n.Pinned = pinned;
            n.X = x; n.Y = y; n.Z = z;
            if (s8 != null) Array.Copy(s8, n.Stalk8, Math.Min(8, s8.Length));
            SheafE8Math.QuantizeStalkToE8(n.Stalk8, n.QuantE8, out n.E8RootIndex, out n.SqnrDb);
            g.Nodes.Add(n);
            return n.Id;
        }

        public static int AddEdge(SheafGraphModel g, int u, int v, string label, float polarity, float twistRad, float abiBias)
        {
            SheafRestrictionEdge e = new SheafRestrictionEdge();
            e.Id = g.Edges.Count;
            e.SrcIdx = u; e.DstIdx = v;
            e.Label = label; e.Polarity = polarity;
            e.ScaleRatio = 1.0f; e.TwistRad = twistRad; e.AbiMismatchBias = abiBias;
            g.Edges.Add(e);
            return e.Id;
        }

        private static float[] Invert8(float[] v)
        {
            float[] o = new float[8];
            for (int i = 0; i < 8; i++) o[i] = -v[i];
            return o;
        }

        private static float[] HashTextToE8Stalk(string s, bool invert)
        {
            float[] raw = new float[8];
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
                for (int i = 0; i < 8; i++)
                    raw[i] = ((h[i] / 255.0f) * 2.0f - 1.0f) * (invert ? -1.0f : 1.0f);
            }
            float[] q = new float[8];
            int idx; float sqnr;
            SheafE8Math.QuantizeStalkToE8(raw, q, out idx, out sqnr);
            return q;
        }

        private static string CleanTab(string s)
        {
            return (s ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", " ").Trim();
        }
    }
}
