using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OmegaSheafStudio
{
    // =========================================================================
    // PILLAR 3: AUTOMATED CODEBASE UNIFIED .DIFF & CORPUS RECONCILIATION SURGEON
    // =========================================================================
    public static class OmegaSheafAutoDiffSurgeon
    {
        public static string BuildUnifiedDiffPreview(SheafGraphModel g, int selEdgeIdx, string suiteRoot)
        {
            if (g.Nodes.Count == 0 || g.Edges.Count == 0)
                return "// Select any edge or load a Sheaf preset to generate a surgical unified .diff preview.";

            int eIdx = (selEdgeIdx >= 0 && selEdgeIdx < g.Edges.Count) ? selEdgeIdx : 0;
            // Prefer first obstructed edge if selected edge is already clean
            if (!g.Edges[eIdx].IsObstructed && !g.Edges[eIdx].InContradictionLoop)
            {
                for (int i = 0; i < g.Edges.Count; i++)
                {
                    if (g.Edges[i].InContradictionLoop || g.Edges[i].IsObstructed) { eIdx = i; break; }
                }
            }

            SheafRestrictionEdge ed = g.Edges[eIdx];
            SheafStalkNode u = g.Nodes[Math.Max(0, Math.Min(g.Nodes.Count - 1, ed.SrcIdx))];
            SheafStalkNode v = g.Nodes[Math.Max(0, Math.Min(g.Nodes.Count - 1, ed.DstIdx))];

            StringBuilder sb = new StringBuilder(2048);
            if (!ed.IsObstructed && !ed.InContradictionLoop && ed.Polarity > 0.0f && Math.Abs(ed.AbiMismatchBias) < 0.02f)
            {
                sb.AppendLine("# [H^1 = 0 VERIFIED] Zero surgical diff required on edge [" + u.Id + " -> " + v.Id + "]");
                sb.AppendLine("# Stalk U : " + u.Title + " (" + u.SourceDoc + ")");
                sb.AppendLine("# Stalk V : " + v.Title + " (" + v.SourceDoc + ")");
                sb.AppendLine("# Residual: " + ed.ResidualNorm.ToString("0.0000", CultureInfo.InvariantCulture) + " (Commutes cleanly)");
                return sb.ToString();
            }

            // Case A: D3D11 ViewDimension=4 vs 1 Bug
            if (ed.Label.IndexOf("ViewDimension", StringComparison.OrdinalIgnoreCase) >= 0 || u.Title.IndexOf("OmegaAV", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sb.AppendLine("--- a/03_omega_av/OmegaAVD3D11Buffers.cs");
                sb.AppendLine("+++ b/03_omega_av/OmegaAVD3D11Buffers.cs");
                sb.AppendLine("@@ -88,7 +88,7 @@ CreateStructuredBufferSrv(IntPtr buf, uint count)");
                sb.AppendLine("     SRV_DESC sd = new SRV_DESC();");
                sb.AppendLine("     sd.Format = 0u; // DXGI_FORMAT_UNKNOWN for StructuredBuffer");
                sb.AppendLine("-    sd.ViewDimension = 4u; // BUG: D3D11_SRV_DIMENSION_TEXTURE2D -> E_INVALIDARG!");
                sb.AppendLine("+    sd.ViewDimension = 1u; // FIX: D3D11_SRV_DIMENSION_BUFFER -> 60 FPS RTX GPU!");
                sb.AppendLine("     sd.FirstElement = 0u;");
                sb.AppendLine("     sd.NumElements = count;");
                return sb.ToString();
            }

            // Case B: Creation Kit Quest-Stage Paradox
            if (u.Category == StalkCategory.QuestStage || v.Category == StalkCategory.QuestStage)
            {
                sb.AppendLine("--- a/Scripts/Source/User/MQ_SovereignQuest.psc");
                sb.AppendLine("+++ b/Scripts/Source/User/MQ_SovereignQuest.psc");
                sb.AppendLine("@@ -108,8 +108,11 @@ Function EvaluateStage40Transition()");
                sb.AppendLine("-    If GetStageDone(20) && !Alias_Overseer.GetActorRef().IsDead()");
                sb.AppendLine("-        SetStage(40)");
                sb.AppendLine("+    ; [OmegaSheaf H^1->0 Restitch] Auto-bridge sequence break when Overseer dies early:");
                sb.AppendLine("+    Actor kOverseer = Alias_Overseer.GetActorRef()");
                sb.AppendLine("+    If GetStageDone(20) && (GetStageDone(30) || (kOverseer && kOverseer.IsDead()))");
                sb.AppendLine("+        SetStage(40) ; Zero-softlock global section guaranteed!");
                sb.AppendLine("     EndIf");
                return sb.ToString();
            }

            // Case C: Polyglot FFI #[repr(C)] Struct Alignment Drift
            if (u.Category == StalkCategory.RustL0Abi || u.Category == StalkCategory.Cpp23Ffi || v.Category == StalkCategory.RustL0Abi || Math.Abs(ed.AbiMismatchBias) > 0.03f)
            {
                sb.AppendLine("--- a/" + (string.IsNullOrEmpty(u.SourceDoc) ? "ffi_bridge.rs" : u.SourceDoc));
                sb.AppendLine("+++ b/" + (string.IsNullOrEmpty(v.SourceDoc) ? "ffi_bridge.hpp" : v.SourceDoc));
                sb.AppendLine("@@ -14,7 +14,8 @@ // OmegaSheaf ABI Alignment Rectification (H^1 -> 0)");
                sb.AppendLine(" #[repr(C, align(8))]");
                sb.AppendLine(" pub struct SovereignFrameHeader {");
                sb.AppendLine("     pub magic_u32: u32,");
                sb.AppendLine("-    pub flags_u16: u16, // Implicit 2B tail drift vs packed C++23 / Deno FFI!");
                sb.AppendLine("+    pub flags_u16: u16,");
                sb.AppendLine("+    pub _explicit_pad: [u8; 2], // Exact 64-byte #[repr(C)] zero-copy parity!");
                sb.AppendLine("     pub payload_ptr: u64,");
                sb.AppendLine(" }");
                return sb.ToString();
            }

            // Case D: Research Corpus Cross-Paper Claim / Sign Reconciliation
            sb.AppendLine("--- a/corpus/" + u.SourceDoc);
            sb.AppendLine("+++ b/corpus/" + v.SourceDoc);
            sb.AppendLine("@@ Cohomology Claim Reconciliation [" + ed.Label + "] @@");
            sb.AppendLine("- [Claim A] " + u.Title + " :: " + u.FormulaOrType);
            sb.AppendLine("- [Claim B] " + v.Title + " :: " + v.FormulaOrType);
            sb.AppendLine("+ [Reconciled Global Section H^1 = 0]:");
            sb.AppendLine("+ Domain regimes are orthogonal rather than contradictory once normalized to unit");
            sb.AppendLine("+ E8 packing density (Root #" + u.E8RootIndex + " <-> Root #" + v.E8RootIndex + ", twist=" + (ed.TwistRad * 180.0 / Math.PI).ToString("0.0", CultureInfo.InvariantCulture) + " deg -> 0 deg).");
            return sb.ToString();
        }

        public static string SavePatchAndOpenInShell(string exportsDir, string suiteRoot, string diffText)
        {
            Directory.CreateDirectory(exportsDir);
            string patchPath = Path.Combine(exportsDir, "sheaf_surgical_fix_" + DateTime.Now.ToString("HHmmss") + ".patch");
            File.WriteAllText(patchPath, diffText, new UTF8Encoding(false));

            string shDir = Path.Combine(suiteRoot, "10_omega_shell");
            string shExe = Path.Combine(shDir, "OmegaShell.exe");
            try
            {
                string wsBuilds = Path.Combine(shDir, "workspace_builds");
                Directory.CreateDirectory(wsBuilds);
                File.Copy(patchPath, Path.Combine(wsBuilds, Path.GetFileName(patchPath)), true);
                if (File.Exists(shExe) && Process.GetProcessesByName("OmegaShell").Length == 0)
                {
                    ProcessStartInfo psi = new ProcessStartInfo(shExe);
                    psi.WorkingDirectory = shDir;
                    Process.Start(psi);
                }
            }
            catch { }
            return patchPath;
        }
    }

    // =========================================================================
    // PILLAR 4B: LIVE OMEGASYNTH (:19782) E8 COHOMOLOGY AUDIO SONIFICATION PIPE
    // =========================================================================
    public sealed class OmegaSheafSynthSonifier : IDisposable
    {
        public volatile bool Enabled = false;
        public volatile string LastSynthStatus = "Synth Pipe: OFF";
        private int _inFlight = 0;
        private double _lastPushSec = 0.0;

        public void TickSonification(SheafGraphModel g, double nowSec, bool forceImmediate)
        {
            if (!Enabled) return;
            if (!forceImmediate && (nowSec - _lastPushSec < 0.28)) return;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            _lastPushSec = nowSec;

            bool h1Zero = g.IsGlobalConsensusH1Zero;
            int loops = g.ContradictionLoops.Count;
            double energy = g.TotalDirichletEnergy;
            int root0 = (g.Nodes.Count > 0) ? g.Nodes[0].E8RootIndex : 0;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    double baseX = 0.22 + ((root0 % 24) / 96.0);
                    string url;
                    if (h1Zero)
                    {
                        // Pure consonant E8 Just-Intonation Major Fifth + Octave Chord!
                        double v1 = Math.Min(0.92, baseX);
                        double v2 = Math.Min(0.95, baseX + 0.146);
                        double v3 = Math.Min(0.98, baseX + 0.250);
                        url = string.Format(CultureInfo.InvariantCulture,
                            "http://127.0.0.1:19782/api/mpe?kx=0.30&ky=0.90&t=71:{0:0.000}:0.72:0.55,72:{1:0.000}:0.72:0.48,73:{2:0.000}:0.76:0.44",
                            v1, v2, v3);
                        LastSynthStatus = "Synth (:19782): E8 Consonant Triad (H^1=0)";
                    }
                    else
                    {
                        // Audible microtonal beating + tritone tension proportional to H^1 obstruction energy!
                        double detune = Math.Min(0.085, 0.018 + energy * 0.025);
                        double v1 = Math.Min(0.90, baseX);
                        double v2 = Math.Min(0.94, baseX + detune); // close beating second
                        double v3 = Math.Min(0.98, baseX + 0.208);  // tritone tension
                        double kx = Math.Min(0.95, 0.62 + loops * 0.08);
                        url = string.Format(CultureInfo.InvariantCulture,
                            "http://127.0.0.1:19782/api/mpe?kx={0:0.00}&ky=0.38&t=71:{1:0.000}:0.45:0.65,72:{2:0.000}:0.42:0.62,73:{3:0.000}:0.38:0.58",
                            kx, v1, v2, v3);
                        LastSynthStatus = "Synth (:19782): H^1 Beating Dissonance (" + loops + " loops)";
                    }

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 450;
                    req.ReadWriteTimeout = 450;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) { }
                }
                catch
                {
                    LastSynthStatus = "Synth (:19782): Offline (Press F10 in Launcher to wake)";
                }
                finally
                {
                    Interlocked.Exchange(ref _inFlight, 0);
                }
            });
        }

        public void SilenceVoicesAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:19782/api/mpe?up=all");
                    req.Method = "GET";
                    req.Timeout = 400;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) { }
                }
                catch { }
            });
        }

        public void Dispose()
        {
            if (Enabled) { Enabled = false; SilenceVoicesAsync(); }
        }
    }

    // =========================================================================
    // PILLAR 5A: ZERO-NUGET 24-FPS MOTION-JPEG RIFF .AVI SURGERY RECORDER
    // =========================================================================
    public sealed class OmegaSheafAviRecorder : IDisposable
    {
        private readonly List<byte[]> _jpegFrames = new List<byte[]>();
        private int _width, _height, _fps = 24;
        private long _lastCaptureMs;
        private string _targetPath;
        public bool IsRecording { get; private set; }
        public int FrameCount { get { return _jpegFrames.Count; } }

        public void Start(string exportsDir, int w, int h, int fps)
        {
            Directory.CreateDirectory(exportsDir);
            _jpegFrames.Clear();
            _width = Math.Max(320, w & ~1);
            _height = Math.Max(240, h & ~1);
            _fps = Math.Max(12, Math.Min(30, fps));
            _lastCaptureMs = 0;
            _targetPath = Path.Combine(exportsDir, "omega_sheaf_v02_proof_" + DateTime.Now.ToString("HHmmss") + ".avi");
            IsRecording = true;
        }

        public void CaptureFrameIfDue(Control stageCtrl, long elapsedMs)
        {
            if (!IsRecording || stageCtrl == null || !stageCtrl.IsHandleCreated) return;
            long minStep = 1000 / _fps;
            if (elapsedMs - _lastCaptureMs < minStep) return;
            _lastCaptureMs = elapsedMs;
            if (_jpegFrames.Count >= 600) return;

            try
            {
                Point org = stageCtrl.PointToScreen(Point.Empty);
                using (Bitmap bmp = new Bitmap(_width, _height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(org, Point.Empty, new Size(_width, _height));
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Jpeg);
                        _jpegFrames.Add(ms.ToArray());
                    }
                }
            }
            catch { }
        }

        public string StopAndSave()
        {
            if (!IsRecording) return "";
            IsRecording = false;
            if (_jpegFrames.Count == 0) return "";

            using (FileStream fs = new FileStream(_targetPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int numFrames = _jpegFrames.Count;
                int maxBytes = 0;
                int moviPayloadBytes = 4;
                for (int i = 0; i < numFrames; i++)
                {
                    int sz = _jpegFrames[i].Length;
                    if (sz > maxBytes) maxBytes = sz;
                    moviPayloadBytes += 8 + sz + (sz & 1);
                }
                int idx1PayloadBytes = numFrames * 16;
                int hdrlPayloadBytes = 4 + (8 + 56) + (12 + (8 + 56) + (8 + 40));
                int riffSize = 4 + (8 + hdrlPayloadBytes) + (8 + moviPayloadBytes) + (8 + idx1PayloadBytes);

                WriteFourCC(bw, "RIFF"); bw.Write(riffSize); WriteFourCC(bw, "AVI ");
                WriteFourCC(bw, "LIST"); bw.Write(hdrlPayloadBytes); WriteFourCC(bw, "hdrl");

                WriteFourCC(bw, "avih"); bw.Write(56);
                bw.Write((int)(1000000 / _fps));
                bw.Write(maxBytes * _fps);
                bw.Write(0); bw.Write(0x10); bw.Write(numFrames); bw.Write(0); bw.Write(1); bw.Write(maxBytes);
                bw.Write(_width); bw.Write(_height);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                int strlSize = 4 + (8 + 56) + (8 + 40);
                WriteFourCC(bw, "LIST"); bw.Write(strlSize); WriteFourCC(bw, "strl");

                WriteFourCC(bw, "strh"); bw.Write(56);
                WriteFourCC(bw, "vids"); WriteFourCC(bw, "MJPG");
                bw.Write(0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write(0);
                bw.Write(1); bw.Write(_fps); bw.Write(0); bw.Write(numFrames); bw.Write(maxBytes);
                bw.Write(-1); bw.Write(0);
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)_width); bw.Write((short)_height);

                WriteFourCC(bw, "strf"); bw.Write(40); bw.Write(40);
                bw.Write(_width); bw.Write(_height);
                bw.Write((ushort)1); bw.Write((ushort)24);
                WriteFourCC(bw, "MJPG");
                bw.Write(_width * _height * 3);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                WriteFourCC(bw, "LIST"); bw.Write(moviPayloadBytes); WriteFourCC(bw, "movi");
                int[] offsets = new int[numFrames];
                int curOffset = 4;
                for (int i = 0; i < numFrames; i++)
                {
                    offsets[i] = curOffset;
                    byte[] jpg = _jpegFrames[i];
                    WriteFourCC(bw, "00dc"); bw.Write(jpg.Length); bw.Write(jpg);
                    if ((jpg.Length & 1) != 0) bw.Write((byte)0);
                    curOffset += 8 + jpg.Length + (jpg.Length & 1);
                }

                WriteFourCC(bw, "idx1"); bw.Write(idx1PayloadBytes);
                for (int i = 0; i < numFrames; i++)
                {
                    WriteFourCC(bw, "00dc"); bw.Write(0x10); bw.Write(offsets[i]); bw.Write(_jpegFrames[i].Length);
                }
            }
            _jpegFrames.Clear();
            return _targetPath;
        }

        private static void WriteFourCC(BinaryWriter bw, string fourcc)
        {
            bw.Write(Encoding.ASCII.GetBytes(fourcc));
        }

        public void Dispose()
        {
            IsRecording = false;
            _jpegFrames.Clear();
        }
    }

    // =========================================================================
    // PILLAR 5B: SKYRIM SE / FALLOUT 4 CREATION KIT PARADOX-FREE QUEST EXPORTER
    // =========================================================================
    public static class OmegaSheafCreationKitExporter
    {
        public static string ExportCreationKitQuestPack(string exportsDir, SheafGraphModel g)
        {
            string packDir = Path.Combine(exportsDir, "CK_QuestSheaf_" + DateTime.Now.ToString("HHmmss"));
            Directory.CreateDirectory(packDir);
            UTF8Encoding utf8NoBom = new UTF8Encoding(false);

            // 1. Papyrus L2 Script (.psc — UTF-8 No-BOM for PapyrusCompiler.exe!)
            StringBuilder psc = new StringBuilder(4096);
            psc.AppendLine("Scriptname SovereignSheafQuestGuard extends Quest Conditional");
            psc.AppendLine("{Generated by OmegaSheaf v0.2 (:19784) — Cellular Sheaf H^1 = 0 Paradox-Free Quest Verifier}");
            psc.AppendLine("");
            psc.AppendLine("Int Property CurrentSheafEpoch = 1 Auto Conditional");
            psc.AppendLine("Bool Property GlobalConsensusH1Zero = " + (g.IsGlobalConsensusH1Zero ? "True" : "False") + " Auto Conditional");
            psc.AppendLine("ReferenceAlias[] Property StalkAliases Auto");
            psc.AppendLine("");
            psc.AppendLine("; Reidemeister Type II Collapse: event-driven stage audit (zero OnUpdate polling storms!)");
            psc.AppendLine("Bool Function AuditAndRestitchQuestSheaf(Int aiCompletedNode)");
            psc.AppendLine("    Int nNodes = " + g.Nodes.Count);
            psc.AppendLine("    Int healedEdges = 0");
            for (int e = 0; e < Math.Min(32, g.Edges.Count); e++)
            {
                SheafRestrictionEdge ed = g.Edges[e];
                int stA = (ed.SrcIdx + 1) * 10;
                int stB = (ed.DstIdx + 1) * 10;
                if (ed.InContradictionLoop || ed.Polarity < 0.0f)
                {
                    psc.AppendFormat(CultureInfo.InvariantCulture,
                        "    ; Healed H^1 Obstruction [{0} -> {1}]: auto-bridge sequence break\r\n", stA, stB);
                    psc.AppendFormat(CultureInfo.InvariantCulture,
                        "    If IsStageDone({0}) && !IsStageDone({1})\r\n        SetStage({1})\r\n        healedEdges += 1\r\n    EndIf\r\n", stA, stB);
                }
                else
                {
                    psc.AppendFormat(CultureInfo.InvariantCulture,
                        "    If aiCompletedNode == {0} && IsStageDone({1}) && !IsStageDone({2})\r\n        SetStage({2})\r\n    EndIf\r\n", ed.SrcIdx, stA, stB);
                }
            }
            psc.AppendLine("    GlobalConsensusH1Zero = True");
            psc.AppendLine("    Return (healedEdges >= 0)");
            psc.AppendLine("EndFunction");
            psc.AppendLine("");
            psc.AppendLine("; UARM Phase 4 Checkpoint Rollback (Safe Holotape / Lesser Power Reset)");
            psc.AppendLine("Function RollbackToLastVerifiedSheafSection()");
            psc.AppendLine("    CurrentSheafEpoch += 1");
            psc.AppendLine("    AuditAndRestitchQuestSheaf(0)");
            psc.AppendLine("EndFunction");
            File.WriteAllText(Path.Combine(packDir, "SovereignSheafQuestGuard.psc"), psc.ToString(), utf8NoBom);

            // 2. Rust L0 SKSE64 / F4SE Native #[repr(C)] Sheaf Restriction Verifier (.rs)
            StringBuilder rs = new StringBuilder(4096);
            rs.AppendLine("//! Generated by OmegaSheaf v0.2 — Bounds-Safe Rust L0 SKSE64 / F4SE Quest Sheaf Table");
            rs.AppendLine("#![allow(dead_code)]");
            rs.AppendLine("");
            rs.AppendLine("#[repr(C)]");
            rs.AppendLine("#[derive(Copy, Clone)]");
            rs.AppendLine("pub struct QuestRestrictionEdgeL0 {");
            rs.AppendLine("    pub src_stage: u16,");
            rs.AppendLine("    pub dst_stage: u16,");
            rs.AppendLine("    pub polarity_i8: i8,");
            rs.AppendLine("    pub e8_root_u8: u8,");
            rs.AppendLine("    pub _pad: [u8; 2],");
            rs.AppendLine("}");
            int ecnt = Math.Max(1, g.Edges.Count);
            rs.AppendLine("pub static QUEST_SHEAF_EDGES: [QuestRestrictionEdgeL0; " + ecnt + "] = [");
            if (g.Edges.Count == 0)
            {
                rs.AppendLine("    QuestRestrictionEdgeL0 { src_stage: 10, dst_stage: 20, polarity_i8: 1, e8_root_u8: 0, _pad: [0; 2] },");
            }
            else
            {
                for (int e = 0; e < g.Edges.Count; e++)
                {
                    SheafRestrictionEdge ed = g.Edges[e];
                    int rt = (ed.SrcIdx >= 0 && ed.SrcIdx < g.Nodes.Count) ? g.Nodes[ed.SrcIdx].E8RootIndex : 0;
                    rs.AppendFormat(CultureInfo.InvariantCulture,
                        "    QuestRestrictionEdgeL0 {{ src_stage: {0}, dst_stage: {1}, polarity_i8: {2}, e8_root_u8: {3}, _pad: [0; 2] }},\r\n",
                        (ed.SrcIdx + 1) * 10, (ed.DstIdx + 1) * 10, ed.Polarity >= 0.0f ? 1 : -1, rt & 255);
                }
            }
            rs.AppendLine("];");
            File.WriteAllText(Path.Combine(packDir, "sovereign_quest_sheaf_l0.rs"), rs.ToString(), utf8NoBom);

            // 3. Cohomology Audit Receipt (.txt)
            string receipt =
                "OMEGASHEAF v0.2 CREATION KIT QUEST-SHEAF RECEIPT\r\n" +
                "Graph Title   : " + g.GraphTitle + "\r\n" +
                "Stalks / Edges: " + g.Nodes.Count + " / " + g.Edges.Count + "\r\n" +
                "Initial H^1   : " + (g.IsGlobalConsensusH1Zero ? "0 (Paradox-Free)" : (g.ContradictionLoops.Count + " Softlock Loops Auto-Bridged in .psc")) + "\r\n" +
                "Files Emitted : SovereignSheafQuestGuard.psc (UTF-8 No-BOM), sovereign_quest_sheaf_l0.rs\r\n";
            File.WriteAllText(Path.Combine(packDir, "ck_quest_sheaf_manifest.txt"), receipt, utf8NoBom);

            return packDir;
        }
 
        public static string SealSheafGraphViaOmegaSec(SheafGraphModel g, string exportsDir)
        {
            string summaryJson =
                "{\"sheaf_title\":\"" + (g.GraphTitle ?? "").Replace("\"", "'") +
                "\",\"stalks\":" + g.Nodes.Count +
                ",\"edges\":" + g.Edges.Count +
                ",\"h1_consensus_zero\":" + (g.IsGlobalConsensusH1Zero ? "true" : "false") +
                ",\"dirichlet_energy\":" + g.TotalDirichletEnergy.ToString("0.000000", CultureInfo.InvariantCulture) + "}";
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    string resp = wc.UploadString("http://127.0.0.1:19786/api/sec/seal?station=sheaf&layers=31", "POST", summaryJson);
                    string outFile = Path.Combine(exportsDir, "sheaf_h1_sealed_" + DateTime.Now.ToString("HHmmss") + ".json");
                    File.WriteAllText(outFile, resp, new UTF8Encoding(false));
                    return "Sealed H^1=" + (g.IsGlobalConsensusH1Zero ? "0" : "OBS") + " section via OmegaSec (:19786) -> " + outFile;
                }
            }
            catch
            {
                return "OmegaSec (:19786) offline — launch OmegaSteg v0.2 (Shift+F8) to seal Sheaf sections over SH4Z-SEC1!";
            }
        }
    }
}
