using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaBrowserV03
{
    public sealed class CodeSnippetBlock
    {
        public string Lang = "csharp";
        public string Code = "";
        public string Label = "";
        public override string ToString() { return Label; }
    }

    public static class OmegaV04Studios
    {
        public static void ShowDualPixelHubDialog(Form owner, string currentUrl, Action<string> onOpenPulledUrl, Action<string> onSetViewport, Action<string> onStatus)
        {
            Form dlg = MakeModal("Dual-Pixel Tri-Silicon Mesh :: Pixel 10 + Pixel 8 Bridge", 690, 440);
            Label info = Lbl("Simultaneous USB + Wireless ADB (:5555) • Subnet-Matched LAN • OLED Companion (:9778/companion)", 14, 10, 660);
            info.ForeColor = Color.FromArgb(56, 189, 248);

            ListBox list = new ListBox(); list.SetBounds(14, 36, 662, 140);
            list.BackColor = Color.FromArgb(15, 23, 42); list.ForeColor = Color.White; list.Font = new Font("Consolas", 9.5f);

            TextBox log = new TextBox(); log.SetBounds(14, 272, 662, 152); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.FromArgb(8, 12, 20); log.ForeColor = Color.FromArgb(167, 243, 208); log.Font = new Font("Consolas", 9f);

            List<PixelDeviceInfo> devs = new List<PixelDeviceInfo>();
            Action refresh = delegate()
            {
                devs = DualPixelMesh.DiscoverPixels();
                list.Items.Clear();
                for (int i = 0; i < devs.Count; i++) list.Items.Add(devs[i].Label + " (" + devs[i].Resolution + ")");
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                log.Text = devs.Count == 0
                    ? "No ADB devices online. Plug in Pixel 10 via USB or keep Pixel 8 on Windows Mobile Hotspot (:5555)."
                    : ("Detected " + devs.Count + " Pixel device(s). Select one above or broadcast to BOTH simultaneously!\r\nActive PC Tab: " + OmegaUrlCleaner.CleanUrl(currentUrl));
            };

            Button bScan = Btn("Rescan Pixels", 14, 186, 122, Color.FromArgb(30, 58, 138));
            bScan.Click += delegate { refresh(); };

            Button bPush1 = Btn("Push URL -> Selected", 144, 186, 165, Color.FromArgb(3, 105, 161));
            bPush1.Click += delegate
            {
                if (list.SelectedIndex < 0 || list.SelectedIndex >= devs.Count) return;
                string m = DualPixelMesh.PushUrlToTarget(devs[list.SelectedIndex].Serial, currentUrl);
                log.Text = m; if (onStatus != null) onStatus(m);
            };

            Button bPushAll = Btn("Broadcast URL -> ALL Pixels", 317, 186, 195, Color.FromArgb(15, 118, 110));
            bPushAll.Click += delegate
            {
                string m = DualPixelMesh.BroadcastToAllPixels(currentUrl, false);
                log.Text = m; if (onStatus != null) onStatus(m);
            };

            Button bWifi = Btn("Promote USB -> :5555", 520, 186, 156, Color.FromArgb(88, 28, 135));
            bWifi.Click += delegate
            {
                if (list.SelectedIndex < 0 || list.SelectedIndex >= devs.Count) return;
                log.Text = DualPixelMesh.PromoteUsbPixelToWireless(devs[list.SelectedIndex].Serial);
                refresh();
            };

            Button bOled = Btn("Beam OLED Companion (:9778) -> ALL", 14, 228, 248, Color.FromArgb(109, 40, 217));
            bOled.Click += delegate
            {
                string m = DualPixelMesh.BroadcastToAllPixels(currentUrl, true);
                log.Text = m + "\r\n(Reverse ADB tunnel tcp:9778 armed — Follow PC or Lock Reference on phone OLED!)";
                if (onStatus != null) onStatus(m);
            };

            Button bPull = Btn("Pull Open URL / Clip -> PC Tab", 270, 228, 214, Color.FromArgb(14, 116, 144));
            bPull.Click += delegate
            {
                if (list.SelectedIndex < 0 || list.SelectedIndex >= devs.Count) return;
                string pulled = DualPixelMesh.PullUrlOrClipFromPixel(devs[list.SelectedIndex].Serial);
                if (string.IsNullOrEmpty(pulled)) log.Text = "No foreground http(s) URL or URL clipboard found on " + devs[list.SelectedIndex].Model + ".";
                else { log.Text = "Pulled from " + devs[list.SelectedIndex].Model + " -> " + pulled; if (onOpenPulledUrl != null) onOpenPulledUrl(pulled); dlg.Close(); }
            };

            Button bVp8 = Btn("VP: P8", 492, 228, 88, Color.FromArgb(51, 65, 85));
            bVp8.Click += delegate { if (onSetViewport != null) onSetViewport("P8"); dlg.Close(); };

            Button bVp10 = Btn("VP: P10", 588, 228, 88, Color.FromArgb(51, 65, 85));
            bVp10.Click += delegate { if (onSetViewport != null) onSetViewport("P10"); dlg.Close(); };

            refresh();
            dlg.Controls.AddRange(new Control[] { info, list, bScan, bPush1, bPushAll, bWifi, bOled, bPull, bVp8, bVp10, log });
            dlg.ShowDialog(owner);
        }

        public static void ShowConceptBraidDialog(Form owner, List<BrowserTab> tabs, string labRoot, string shEndpoint, Func<BrowserTab, Task<ReceiptRecord>> extract384Async, Action<string> onStatus)
        {
            Form dlg = MakeModal("Multi-Tab Concept Braid Synthesizer (Ctrl+Shift+B) :: Fuse 2-6 Tabs with Provenance", 840, 560);
            Label l1 = Lbl("Select 2 to 6 Tabs Across Workspaces to Fuse (384D Core + Shared Concept Overlap + Receipts):", 14, 10, 810);
            l1.ForeColor = Color.FromArgb(56, 189, 248);

            CheckedListBox clb = new CheckedListBox(); clb.SetBounds(14, 34, 812, 130);
            clb.BackColor = Color.FromArgb(15, 23, 42); clb.ForeColor = Color.White; clb.Font = new Font("Consolas", 9f); clb.CheckOnClick = true;

            int preChecked = 0;
            for (int i = 0; i < tabs.Count; i++)
            {
                BrowserTab t = tabs[i];
                bool chk = preChecked < 4 && !t.Url.StartsWith("omega://", StringComparison.OrdinalIgnoreCase);
                if (chk) preChecked++;
                clb.Items.Add("[" + t.Workspace + " | " + t.Container + "] " + t.Title + "  (" + t.Url + ")", chk);
            }
            if (preChecked == 0) { for (int i = 0; i < Math.Min(2, clb.Items.Count); i++) clb.SetItemChecked(i, true); }

            TextBox outBox = new TextBox(); outBox.SetBounds(14, 214, 812, 330); outBox.Multiline = true; outBox.ScrollBars = ScrollBars.Both;
            outBox.BackColor = Color.FromArgb(8, 12, 20); outBox.ForeColor = Color.FromArgb(226, 232, 240); outBox.Font = new Font("Consolas", 9f);
            outBox.Text = "Click 'Synthesize Concept Braid' to extract 384D cores, compute cross-tab mathematical/code symbol overlap, and mint a composite provenance packet.";

            ReceiptRecord lastBraidRec = null;

            Button bBuild = Btn("Synthesize Concept Braid", 14, 170, 200, Color.FromArgb(109, 40, 217));
            bBuild.Click += async delegate
            {
                List<BrowserTab> chosen = new List<BrowserTab>();
                for (int i = 0; i < clb.CheckedIndices.Count && chosen.Count < 6; i++) chosen.Add(tabs[clb.CheckedIndices[i]]);
                if (chosen.Count < 1) { outBox.Text = "Check at least 1-2 tabs above first."; return; }

                bBuild.Enabled = false;
                outBox.Text = "Extracting 384D distilled cores + SHA-256 receipts across " + chosen.Count + " tab(s)...";
                List<ReceiptRecord> recs = new List<ReceiptRecord>();
                for (int i = 0; i < chosen.Count; i++) recs.Add(await extract384Async(chosen[i]));

                string braidMd = BuildBraidPacket(chosen, recs);
                lastBraidRec = OmegaVault.MintReceiptAndSaveHtml(labRoot, "Concept Braid (" + recs.Count + " Sources)", "omega://braid/" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), braidMd, braidMd, 0, 0);
                outBox.Text = braidMd.Replace("\r\n", "\n").Replace("\n", "\r\n");
                bBuild.Enabled = true;
            };

            Button bCopy = Btn("Copy Braid + Receipts", 222, 170, 175, Color.FromArgb(3, 105, 161));
            bCopy.Click += delegate
            {
                if (!string.IsNullOrEmpty(outBox.Text)) { Clipboard.SetDataObject(outBox.Text, true, 4, 40); if (onStatus != null) onStatus("Copied Multi-Tab Concept Braid to clipboard!"); }
            };

            Button bCorp = Btn("Save Braid -> +Corpus", 405, 170, 185, Color.FromArgb(15, 118, 110));
            bCorp.Click += delegate
            {
                if (lastBraidRec == null) return;
                CorpusSaveResult cs = OmegaVault.SaveToCorpus(labRoot, lastBraidRec, "concept_braid,sovereign_v04");
                if (onStatus != null) onStatus(cs.Duplicate ? "Braid already indexed in Corpus." : ("Saved Concept Braid -> " + (string.IsNullOrEmpty(cs.ManifoldPath) ? cs.PrimaryPath : cs.ManifoldPath)));
            };

            Button bShXi = Btn("Ask Local SH-XI on Braid", 598, 170, 228, Color.FromArgb(88, 28, 135));
            bShXi.Click += async delegate
            {
                if (lastBraidRec == null) return;
                bShXi.Enabled = false;
                string ans = await Task.Run(delegate { return LocalModelClient.QueryLocalShXi(shEndpoint, "768D Summary", "Find structural bridges, shared invariants, and implementation steps across these braided tabs.", lastBraidRec); });
                outBox.Text = (ans + "\n\n" + lastBraidRec.MarkdownBody).Replace("\r\n", "\n").Replace("\n", "\r\n");
                bShXi.Enabled = true;
            };

            dlg.Controls.AddRange(new Control[] { l1, clb, bBuild, bCopy, bCorp, bShXi, outBox });
            dlg.ShowDialog(owner);
        }

        private static string BuildBraidPacket(List<BrowserTab> tabs, List<ReceiptRecord> recs)
        {
            Dictionary<string, int> docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the","and","for","that","with","this","from","are","was","were","have","has","not","but","you","all","can","use","into","more","than","when","also","only","each","which","their","other","used","using","page","http","https","code","data","time","type","value","function","return","class","public","static","void","true","false"
            };
            for (int i = 0; i < recs.Count; i++)
            {
                HashSet<string> seenInDoc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                MatchCollection ms = Regex.Matches(recs[i].MarkdownBody ?? "", @"\b[a-zA-Z][a-zA-Z0-9_]{3,28}\b");
                for (int m = 0; m < ms.Count; m++)
                {
                    string w = ms[m].Value;
                    if (stop.Contains(w)) continue;
                    if (seenInDoc.Add(w))
                    {
                        int c; docFreq.TryGetValue(w, out c);
                        docFreq[w] = c + 1;
                    }
                }
            }
            List<KeyValuePair<string, int>> shared = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> kv in docFreq) if (kv.Value >= 2) shared.Add(kv);
            shared.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { int c = b.Value.CompareTo(a.Value); return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Sovereign Multi-Tab Concept Braid (" + recs.Count + " Tabs)");
            sb.AppendLine();
            sb.AppendLine("## 1. Cryptographic Provenance Manifest");
            char bt = (char)96;
            for (int i = 0; i < recs.Count; i++)
            {
                sb.AppendLine("- **[" + (i + 1) + "] " + recs[i].Title + "** (" + bt + tabs[i].Workspace + "/" + tabs[i].Container + bt + ") — " + bt + recs[i].Sha256.Substring(0, 16) + "..." + bt + " — " + recs[i].FinalUrl);
            }

            sb.AppendLine();
            sb.AppendLine("## 2. Cross-Tab Shared Invariants & Symbols");
            if (shared.Count == 0) sb.AppendLine("- Distinct non-overlapping vocabularies across selected tabs.");
            else
            {
                List<string> top = new List<string>();
                for (int i = 0; i < Math.Min(24, shared.Count); i++) top.Add(shared[i].Key + " (" + shared[i].Value + "/" + recs.Count + ")");
                sb.AppendLine("- **Shared Bridge Terms:** " + string.Join(", ", top.ToArray()));
            }

            sb.AppendLine();
            sb.AppendLine("## 3. Distilled 384D Source Cores");
            for (int i = 0; i < recs.Count; i++)
            {
                string body = recs[i].MarkdownBody ?? "";
                if (body.Length > 4500) body = body.Substring(0, 4500) + "\n...[truncated at 4,500 chars]...";
                sb.AppendLine("### Source [" + (i + 1) + "]: " + recs[i].Title);
                sb.AppendLine(body);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static void ShowPaperToCodeRunner(Form owner, string labRoot, string pageTitle, string pageUrl, string pageMd, string selectedCode, Action<string> onStatus)
        {
            Form dlg = MakeModal("Paper-to-Code Scratchpad Runner (Ctrl+Shift+R) :: Local 4s Sandbox", 820, 560);
            List<CodeSnippetBlock> blocks = ExtractCodeSnippets(pageMd, selectedCode);

            ComboBox cmbSnip = new ComboBox(); cmbSnip.SetBounds(14, 12, 410, 26); cmbSnip.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSnip.BackColor = Color.FromArgb(15, 23, 42); cmbSnip.ForeColor = Color.White;
            for (int i = 0; i < blocks.Count; i++) cmbSnip.Items.Add(blocks[i]);

            ComboBox cmbLang = new ComboBox(); cmbLang.SetBounds(432, 12, 170, 26); cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLang.BackColor = Color.FromArgb(15, 23, 42); cmbLang.ForeColor = Color.White;
            cmbLang.Items.AddRange(new object[] { "C# 5.0 (csc.exe)", "PowerShell 5.1", "Rust (rustc)" });
            cmbLang.SelectedIndex = 0;

            TextBox editor = new TextBox(); editor.SetBounds(14, 46, 792, 255); editor.Multiline = true; editor.ScrollBars = ScrollBars.Both; editor.AcceptsTab = true;
            editor.BackColor = Color.FromArgb(9, 13, 22); editor.ForeColor = Color.FromArgb(248, 250, 252); editor.Font = new Font("Consolas", 9.5f);

            TextBox output = new TextBox(); output.SetBounds(14, 348, 792, 196); output.Multiline = true; output.ReadOnly = true; output.ScrollBars = ScrollBars.Both;
            output.BackColor = Color.FromArgb(6, 10, 18); output.ForeColor = Color.FromArgb(167, 243, 208); output.Font = new Font("Consolas", 9.5f);
            output.Text = "Isolated temp sandbox ready (%TEMP%\\omega_runner_*, 4,000 ms hard timeout). Click 'Run (4s Sandbox)' to verify snippet.";

            cmbSnip.SelectedIndexChanged += delegate
            {
                CodeSnippetBlock b = cmbSnip.SelectedItem as CodeSnippetBlock;
                if (b == null) return;
                editor.Text = b.Code.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (b.Lang == "rust") cmbLang.SelectedIndex = 2;
                else if (b.Lang == "powershell") cmbLang.SelectedIndex = 1;
                else cmbLang.SelectedIndex = 0;
            };
            if (cmbSnip.Items.Count > 0) cmbSnip.SelectedIndex = 0;

            string lastOut = "";
            Button bRun = Btn("Run (4s Sandbox)", 610, 10, 196, Color.FromArgb(6, 95, 70));
            bRun.Click += async delegate
            {
                bRun.Enabled = false;
                string lang = cmbLang.Text; string src = editor.Text;
                output.Text = "Compiling & running in isolated temp directory (4,000 ms guard)...";
                lastOut = await Task.Run(delegate { return ExecuteInTempSandbox(lang, src); });
                output.Text = lastOut.Replace("\r\n", "\n").Replace("\n", "\r\n");
                bRun.Enabled = true;
            };

            Button bSave = Btn("Save [Paper + Verified Code + Output] Pair -> +Corpus", 14, 308, 420, Color.FromArgb(15, 118, 110));
            bSave.Click += delegate
            {
                char bt = (char)96;
                string fence = new string(bt, 3);
                string md = "## Verified Paper-to-Code Execution Trace\n- **Source:** " + pageTitle + " (" + OmegaUrlCleaner.CleanUrl(pageUrl) + ")\n- **Runtime:** " + cmbLang.Text + "\n\n### Source Code\n" + fence + "\n" + editor.Text.Trim() + "\n" + fence + "\n\n### Verified Output\n" + fence + "text\n" + (string.IsNullOrEmpty(lastOut) ? "(not executed yet)" : lastOut.Trim()) + "\n" + fence;
                ReceiptRecord rec = OmegaVault.MintReceiptAndSaveHtml(labRoot, pageTitle + " [Verified Code]", OmegaUrlCleaner.CleanUrl(pageUrl), md, md, 0, 0);
                CorpusSaveResult cs = OmegaVault.SaveToCorpus(labRoot, rec, "paper_to_code,verified_trace,sovereign_v04");
                string msg = cs.Duplicate ? "Execution trace already in Corpus." : ("Saved verified code+output pair -> " + (string.IsNullOrEmpty(cs.ManifoldPath) ? cs.PrimaryPath : cs.ManifoldPath));
                output.Text = msg + "\r\n\r\n" + output.Text;
                if (onStatus != null) onStatus(msg);
            };

            Button bCopy = Btn("Copy Code + Output", 444, 308, 210, Color.FromArgb(3, 105, 161));
            bCopy.Click += delegate { Clipboard.SetDataObject(editor.Text + "\r\n/* OUTPUT:\r\n" + lastOut + "\r\n*/", true, 4, 40); };

            Button bClose = Btn("Close", 664, 308, 142, Color.FromArgb(51, 65, 85));
            bClose.Click += delegate { dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { cmbSnip, cmbLang, bRun, editor, bSave, bCopy, bClose, output });
            dlg.ShowDialog(owner);
        }

        private static List<CodeSnippetBlock> ExtractCodeSnippets(string md, string sel)
        {
            List<CodeSnippetBlock> list = new List<CodeSnippetBlock>();
            if (!string.IsNullOrWhiteSpace(sel))
            {
                CodeSnippetBlock s = new CodeSnippetBlock();
                s.Code = sel.Trim(); s.Lang = GuessLang(s.Code, ""); s.Label = "Highlighted Selection [" + s.Lang + "]";
                list.Add(s);
            }
            string fence = new string((char)96, 3);
            string[] lines = (md ?? "").Replace("\r", "").Split('\n');
            bool inBlock = false; string lang = ""; StringBuilder cur = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string tr = lines[i].Trim();
                if (tr.StartsWith(fence))
                {
                    if (!inBlock) { inBlock = true; lang = tr.Substring(3).Trim().ToLowerInvariant(); cur.Clear(); }
                    else
                    {
                        inBlock = false;
                        string body = cur.ToString().Trim();
                        if (body.Length >= 8)
                        {
                            CodeSnippetBlock b = new CodeSnippetBlock();
                            b.Code = body; b.Lang = GuessLang(body, lang);
                            b.Label = "Page Block #" + (list.Count + 1) + " [" + b.Lang + "] (" + body.Split('\n').Length + " ln)";
                            list.Add(b);
                        }
                    }
                }
                else if (inBlock) cur.AppendLine(lines[i]);
            }
            if (list.Count == 0)
            {
                CodeSnippetBlock demo = new CodeSnippetBlock();
                demo.Lang = "csharp";
                demo.Label = "Demo :: E8 Lattice Norm & Harmonic Stride Check [csharp]";
                demo.Code = "double[] v = new double[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 };\ndouble normSq = 0;\nfor (int i = 0; i < v.Length; i++) normSq += v[i] * v[i];\nConsole.WriteLine(\"E8 half-integer vector ||v||^2 = \" + normSq + \" (even norm verified: \" + (Math.Abs(normSq - 2.0) < 1e-9) + \")\");";
                list.Add(demo);
            }
            return list;
        }

        private static string GuessLang(string code, string tag)
        {
            if (tag.Contains("rust") || tag == "rs" || code.Contains("fn ") || code.Contains("println!(")) return "rust";
            if (tag.Contains("ps") || tag.Contains("powershell") || code.Contains("Write-Host") || code.Contains("$PSVersionTable")) return "powershell";
            return "csharp";
        }

        private static string ExecuteInTempSandbox(string langLabel, string code)
        {
            string dir = Path.Combine(Path.GetTempPath(), "omega_runner_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (langLabel.StartsWith("PowerShell", StringComparison.OrdinalIgnoreCase))
                {
                    string ps1 = Path.Combine(dir, "run.ps1");
                    File.WriteAllText(ps1, code, new UTF8Encoding(false));
                    return RunGuardedProcess("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + ps1 + "\"", dir, 4000, sw);
                }
                if (langLabel.StartsWith("Rust", StringComparison.OrdinalIgnoreCase))
                {
                    string rustc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "rustc.exe");
                    if (!File.Exists(rustc)) rustc = "rustc.exe";
                    string rs = Path.Combine(dir, "main.rs");
                    string exe = Path.Combine(dir, "main.exe");
                    string wrapped = code.Contains("fn main") ? code : ("fn main() {\n" + code + "\n}\n");
                    File.WriteAllText(rs, wrapped, new UTF8Encoding(false));
                    string comp = RunGuardedProcess(rustc, "\"" + rs + "\" -o \"" + exe + "\"", dir, 6000, sw);
                    if (!File.Exists(exe)) return "[rustc compile error]\n" + comp;
                    return RunGuardedProcess(exe, "", dir, 4000, sw);
                }
                else
                {
                    string csc = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe";
                    string cs = Path.Combine(dir, "Snippet.cs");
                    string exe = Path.Combine(dir, "Snippet.exe");
                    string wrapped = (code.Contains("static void Main") || code.Contains("class "))
                        ? code
                        : ("using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Text;\nclass OmegaSnippet {\n    static void Main() {\n" + code + "\n    }\n}\n");
                    File.WriteAllText(cs, wrapped, new UTF8Encoding(false));
                    string comp = RunGuardedProcess(csc, "/nologo /optimize+ /out:\"" + exe + "\" \"" + cs + "\"", dir, 5000, sw);
                    if (!File.Exists(exe)) return "[csc.exe compile diagnostics]\n" + comp;
                    return RunGuardedProcess(exe, "", dir, 4000, sw);
                }
            }
            catch (Exception ex) { return "Sandbox error: " + ex.Message; }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static string RunGuardedProcess(string exe, string args, string workDir, int timeoutMs, Stopwatch sw)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.WorkingDirectory = workDir; psi.CreateNoWindow = true; psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                Task<string> tOut = p.StandardOutput.ReadToEndAsync();
                Task<string> tErr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return "[TIMEOUT] Terminated after " + timeoutMs + " ms sandbox guard.";
                }
                string o = tOut.GetAwaiter().GetResult();
                string e = tErr.GetAwaiter().GetResult();
                return "[Exit " + p.ExitCode + " | " + sw.ElapsedMilliseconds + " ms]\n" + (o ?? "") + (string.IsNullOrEmpty(e) ? "" : ("\n[stderr]\n" + e));
            }
        }

        private static Form MakeModal(string title, int w, int h)
        {
            Form f = new Form(); f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(w, h);
            f.BackColor = Color.FromArgb(12, 18, 30); f.ForeColor = Color.White;
            f.MaximizeBox = false; f.MinimizeBox = false; f.KeyPreview = true;
            f.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) f.Close(); };
            return f;
        }

        private static Label Lbl(string t, int x, int y, int w)
        {
            Label l = new Label(); l.Text = t; l.SetBounds(x, y, w, 20); l.ForeColor = Color.FromArgb(148, 163, 184); l.Font = new Font("Segoe UI Semibold", 9f); return l;
        }

        private static Button Btn(string txt, int x, int y, int w, Color bg)
        {
            Button b = new Button(); b.Text = txt; b.SetBounds(x, y, w, 32);
            b.FlatStyle = FlatStyle.Flat; b.BackColor = bg; b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.5f);
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }
    }
}
