using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OmegaStandaloneLab
{
    public sealed class ManualChapter
    {
        public string Title = "";
        public string Body = "";
        public ManualChapter(string title, string body) { Title = title; Body = body; }
        public override string ToString() { return Title; }
    }

    public sealed class OmegaLabManualDialog : Form
    {
        private readonly string _suiteRoot;
        private readonly List<ManualChapter> _chapters;
        private readonly ListBox _lstChapters;
        private readonly TextBox _txtSearch;
        private readonly RichTextBox _rtbReader;
        private readonly Label _lblMatchInfo;

        public OmegaLabManualDialog(string suiteRoot)
        {
            _suiteRoot = suiteRoot;
            _chapters = BuildAllChapters(suiteRoot);

            Text = "ACT-Omega Standalone Lab — Human User Manual, Step-by-Step Guide & FAQ";
            Size = new Size(1060, 740);
            MinimumSize = new Size(860, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(9, 13, 22);
            ForeColor = Color.White;
            KeyPreview = true;

            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 52;
            topBar.Padding = new Padding(12, 10, 12, 8);
            topBar.BackColor = Color.FromArgb(14, 21, 38);

            Label lSearch = new Label();
            lSearch.Text = "Search Manual & FAQ:";
            lSearch.AutoSize = true;
            lSearch.Location = new Point(12, 16);
            lSearch.Font = new Font("Segoe UI Semibold", 9.0f);
            lSearch.ForeColor = Color.FromArgb(56, 189, 248);

            _txtSearch = new TextBox();
            _txtSearch.SetBounds(160, 13, 300, 26);
            _txtSearch.BackColor = Color.FromArgb(7, 11, 19);
            _txtSearch.ForeColor = Color.White;
            _txtSearch.Font = new Font("Segoe UI", 9.2f);
            _txtSearch.BorderStyle = BorderStyle.FixedSingle;
            _txtSearch.TextChanged += delegate { OnSearchChanged(); };

            Button bClearSearch = MakeTopBtn("Clear", 468, 12, 64, Color.FromArgb(51, 65, 85));
            bClearSearch.Click += delegate { _txtSearch.Clear(); };

            Button bShowAll = MakeTopBtn("Read All Chapters", 540, 12, 138, Color.FromArgb(30, 58, 138));
            bShowAll.Click += delegate { RenderCombinedChapters(_txtSearch.Text.Trim()); };

            Button bExportMd = MakeTopBtn("Save Printable Manual (.md)", 686, 12, 198, Color.FromArgb(5, 150, 105));
            bExportMd.Click += delegate { ExportMarkdownHandbook(); };

            _lblMatchInfo = new Label();
            _lblMatchInfo.AutoSize = true;
            _lblMatchInfo.Location = new Point(894, 16);
            _lblMatchInfo.Font = new Font("Consolas", 8.5f);
            _lblMatchInfo.ForeColor = Color.FromArgb(250, 204, 21);
            _lblMatchInfo.Text = "11 Chapters";

            topBar.Controls.AddRange(new Control[] { lSearch, _txtSearch, bClearSearch, bShowAll, bExportMd, _lblMatchInfo });

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            Panel leftPane = new Panel();
            leftPane.Dock = DockStyle.Fill;
            leftPane.Padding = new Padding(8, 8, 4, 8);
            leftPane.BackColor = Color.FromArgb(11, 16, 29);

            Label lChapHdr = new Label();
            lChapHdr.Text = "CHAPTERS (Click to Read):";
            lChapHdr.Dock = DockStyle.Top;
            lChapHdr.Height = 24;
            lChapHdr.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lChapHdr.ForeColor = Color.FromArgb(167, 139, 250);

            _lstChapters = new ListBox();
            _lstChapters.Dock = DockStyle.Fill;
            _lstChapters.BackColor = Color.FromArgb(7, 11, 19);
            _lstChapters.ForeColor = Color.FromArgb(226, 232, 240);
            _lstChapters.Font = new Font("Segoe UI Semibold", 9.2f);
            _lstChapters.BorderStyle = BorderStyle.None;
            _lstChapters.ItemHeight = 28;
            for (int i = 0; i < _chapters.Count; i++) _lstChapters.Items.Add(_chapters[i]);
            _lstChapters.SelectedIndexChanged += delegate
            {
                if (_lstChapters.SelectedIndex >= 0 && _lstChapters.SelectedIndex < _chapters.Count)
                    RenderTextWithFormatting(_chapters[_lstChapters.SelectedIndex].Body, _txtSearch.Text.Trim());
            };

            leftPane.Controls.Add(_lstChapters);
            leftPane.Controls.Add(lChapHdr);

            Panel rightPane = new Panel();
            rightPane.Dock = DockStyle.Fill;
            rightPane.Padding = new Padding(12, 10, 12, 10);
            rightPane.BackColor = Color.FromArgb(8, 12, 21);

            _rtbReader = new RichTextBox();
            _rtbReader.Dock = DockStyle.Fill;
            _rtbReader.ReadOnly = true;
            _rtbReader.BorderStyle = BorderStyle.None;
            _rtbReader.BackColor = Color.FromArgb(8, 12, 21);
            _rtbReader.ForeColor = Color.FromArgb(226, 232, 240);
            _rtbReader.Font = new Font("Segoe UI", 10.0f);
            _rtbReader.DetectUrls = false;

            rightPane.Controls.Add(_rtbReader);

            split.Panel1.Controls.Add(leftPane);
            split.Panel2.Controls.Add(rightPane);

            Controls.Add(split);
            Controls.Add(topBar);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            Shown += delegate
            {
                try { split.SplitterDistance = 305; } catch { }
                if (_lstChapters.Items.Count > 0) _lstChapters.SelectedIndex = 0;
            };
        }

        private static Button MakeTopBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.3f);
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }

        private void OnSearchChanged()
        {
            string q = (_txtSearch.Text ?? "").Trim();
            if (q.Length == 0)
            {
                _lblMatchInfo.Text = "11 Chapters";
                if (_lstChapters.SelectedIndex >= 0)
                    RenderTextWithFormatting(_chapters[_lstChapters.SelectedIndex].Body, "");
                return;
            }
            RenderCombinedChapters(q);
        }

        private void RenderCombinedChapters(string highlightQuery)
        {
            StringBuilder sb = new StringBuilder();
            int matchChapters = 0;
            for (int i = 0; i < _chapters.Count; i++)
            {
                if (!string.IsNullOrEmpty(highlightQuery))
                {
                    bool hitTitle = _chapters[i].Title.IndexOf(highlightQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hitBody = _chapters[i].Body.IndexOf(highlightQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hitTitle && !hitBody) continue;
                }
                matchChapters++;
                sb.AppendLine(_chapters[i].Body.Trim());
                sb.AppendLine();
                sb.AppendLine("========================================================================");
                sb.AppendLine();
            }

            if (matchChapters == 0)
            {
                _lblMatchInfo.Text = "0 hits";
                RenderTextWithFormatting("=== NO MATCHES FOUND ===\r\n\r\nNo chapter mentioned '" + highlightQuery + "'. Try searching for words like: download, pixel, shader, midi, gemma, lean, rust, shortcut, port, or ram.", "");
                return;
            }

            int hits = RenderTextWithFormatting(sb.ToString(), highlightQuery);
            _lblMatchInfo.Text = string.IsNullOrEmpty(highlightQuery) ? "All 11 Chapters" : (hits + " match(es)");
        }

        private int RenderTextWithFormatting(string content, string highlightQuery)
        {
            _rtbReader.SuspendLayout();
            _rtbReader.Clear();
            _rtbReader.Text = content ?? "";

            // Colorize major headings, STEP/Q&A lines, and tips for easy human scanning
            string text = _rtbReader.Text;
            string[] lines = text.Split('\n');
            int pos = 0;
            Font h1Font = new Font("Segoe UI", 12.0f, FontStyle.Bold);
            Font h2Font = new Font("Segoe UI", 10.2f, FontStyle.Bold);
            Font monoFont = new Font("Consolas", 9.5f);

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                string tr = ln.Trim();
                if (tr.StartsWith("==="))
                {
                    _rtbReader.Select(pos, ln.Length);
                    _rtbReader.SelectionColor = Color.FromArgb(56, 189, 248);
                    _rtbReader.SelectionFont = h1Font;
                }
                else if (tr.StartsWith("[STEP ") || tr.StartsWith("HOW TO ") || tr.StartsWith("WHAT IT IS") || tr.StartsWith("SHORTCUTS") || tr.StartsWith("KEY FEATURES"))
                {
                    _rtbReader.Select(pos, ln.Length);
                    _rtbReader.SelectionColor = Color.FromArgb(52, 211, 153);
                    _rtbReader.SelectionFont = h2Font;
                }
                else if (tr.StartsWith("Q:") || tr.StartsWith("Q1") || tr.StartsWith("Q2") || tr.StartsWith("Q3") || tr.StartsWith("Q4") || tr.StartsWith("Q5") || tr.StartsWith("Q6") || tr.StartsWith("Q7") || tr.StartsWith("Q8") || tr.StartsWith("Q9") || tr.StartsWith("Q10") || tr.StartsWith("Q11") || tr.StartsWith("Q12"))
                {
                    _rtbReader.Select(pos, ln.Length);
                    _rtbReader.SelectionColor = Color.FromArgb(250, 204, 21);
                    _rtbReader.SelectionFont = h2Font;
                }
                else if (tr.StartsWith("A:"))
                {
                    _rtbReader.Select(pos, Math.Min(2, ln.Length));
                    _rtbReader.SelectionColor = Color.FromArgb(167, 139, 250);
                    _rtbReader.SelectionFont = h2Font;
                }
                else if (tr.StartsWith("  Cmd:") || tr.StartsWith("  Folder:") || tr.StartsWith("  Port "))
                {
                    _rtbReader.Select(pos, ln.Length);
                    _rtbReader.SelectionColor = Color.FromArgb(125, 211, 252);
                    _rtbReader.SelectionFont = monoFont;
                }
                pos += ln.Length + 1;
            }

            int matchCount = 0;
            int firstHitIdx = -1;
            if (!string.IsNullOrEmpty(highlightQuery))
            {
                int idx = 0;
                while (idx < text.Length)
                {
                    int found = text.IndexOf(highlightQuery, idx, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    if (firstHitIdx < 0) firstHitIdx = found;
                    _rtbReader.Select(found, highlightQuery.Length);
                    _rtbReader.SelectionBackColor = Color.FromArgb(180, 83, 9);
                    _rtbReader.SelectionColor = Color.White;
                    matchCount++;
                    idx = found + highlightQuery.Length;
                }
            }

            _rtbReader.Select(firstHitIdx >= 0 ? firstHitIdx : 0, 0);
            _rtbReader.ScrollToCaret();
            _rtbReader.ResumeLayout();
            return matchCount;
        }

        private void ExportMarkdownHandbook()
        {
            try
            {
                string outPath = Path.Combine(_suiteRoot, "OMEGA_LAB_HUMAN_USER_MANUAL.md");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ACT-Omega Standalone Lab — Complete Human User Manual & FAQ");
                sb.AppendLine();
                for (int i = 0; i < _chapters.Count; i++)
                {
                    sb.AppendLine("## " + _chapters[i].Title);
                    sb.AppendLine();
                    sb.AppendLine(_chapters[i].Body.Trim());
                    sb.AppendLine();
                }
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                Process.Start("explorer.exe", "/select,\"" + outPath + "\"");
                _lblMatchInfo.Text = "Saved .md!";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export error: " + ex.Message, "Export Manual", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static List<ManualChapter> BuildAllChapters(string root)
        {
            List<ManualChapter> list = new List<ManualChapter>();

            list.Add(new ManualChapter("1. Welcome & 60-Sec Quick Start",
                "=== 1. WELCOME TO ACT-OMEGA STANDALONE LAB (60-SECOND QUICK START) ===\r\n\r\n" +
                "WHAT IT IS (IN PLAIN ENGLISH)\r\n" +
                "ACT-Omega Standalone Lab is a lightweight, self-contained suite of four Windows 11 desktop tools plus one central Control Deck.\r\n" +
                "Every app is compiled directly on your laptop with zero bloated installers, zero cloud tracking, and tiny memory usage (~190 MB for the core trio).\r\n\r\n" +
                "MEET YOUR 4 APPS + CONTROL DECK:\r\n" +
                "  1. OmegaLabLauncher v1.4 (Main Deck)  -> Starts, focuses, or stops every app, manages Pixel phones, and syncs backups.\r\n" +
                "  2. OmegaBrowser v0.6.4   (Port :9778) -> Fast research browser with resumable downloads, PDF reader, and 1-click file beaming.\r\n" +
                "  3. OmegaMirror v0.9      (Port :19777)-> Streams your PC screen to your Pixel 8 / Pixel 10 with live touch control.\r\n" +
                "  4. OmegaAV v0.7.1          (Port :19779)-> GPU visual shader & fluid studio controlled by audio, MIDI knobs, or an Xbox/PS5 controller.\r\n" +
                "  5. OmegaForge v0.2.1       (Port :19780)-> Scans all compilers on your laptop, runs C#/Rust/C++/HLSL/Lean 4 code, and connects Local Gemma AI.\r\n\r\n" +
                "[STEP 1] LAUNCH ANYTHING WITH ONE CLICK (OR FUNCTION KEYS)\r\n" +
                "  - Click any colored 'Launch / Focus' button in the Main Launcher, or press F1 (Browser), F2 (Mirror), F3 (OmegaAV), or F4 (OmegaForge).\r\n" +
                "  - If an app is already open behind another window, clicking its button brings it straight to the front instead of opening duplicates.\r\n\r\n" +
                "[STEP 2] CONNECT YOUR PIXEL PHONES (OPTIONAL)\r\n" +
                "  - Plug your Pixel 8 or Pixel 10 in via USB (with USB Debugging enabled). The Launcher automatically detects it.\r\n" +
                "  - Click 'Promote USB -> Wireless (:5555) + Tunnels' if you want to unplug the cable and keep streaming over Wi-Fi!\r\n\r\n" +
                "[STEP 3] FREE ALL MEMORY WHENEVER YOU ARE DONE\r\n" +
                "  - Press F12 (or click the red 'Stop All 4 Standalone Lab Processes' button at the bottom of the Launcher) to close all Lab apps cleanly and free 100% of host RAM and RTX GPU VRAM."));

            list.Add(new ManualChapter("2. Main Control Deck (Launcher)",
                "=== 2. MAIN CONTROL DECK (OMEGALABLAUNCHER v1.3.2) ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "Your mission control window. Every 2 seconds it checks which apps are running, how much RAM they use, whether your Pixel phones are plugged in, and how much free space remains on your E:\\ drive.\r\n\r\n" +
                "HOW TO USE EVERY BUTTON ON THE DECK:\r\n" +
                "  - Launch / Focus Buttons (Rows 1 to 4): Starts Browser, Mirror, OmegaAV, or OmegaForge—or brings them to the front if already open.\r\n" +
                "  - Freeze BG Tabs (Row 1, Orange): Tells OmegaBrowser to put inactive background tabs to sleep so WebView2 uses far less RAM.\r\n" +
                "  - Beam :19777 / Beam :19779 -> Pixels: Opens an encrypted local USB/Wi-Fi tunnel and pops open your PC screen (:19777) or GPU visualizer (:19779) right on your Pixel phone screen.\r\n" +
                "  - Forge Workspace (Row 4, Blue): Opens <OMEGA_LAB_ROOT>\\09_omega_forge\\workspace where AI-generated programs and Lean 4 proofs are saved.\r\n\r\n" +
                "HOW TO USE THE DRAG-AND-DROP PIXEL DROPZONE:\r\n" +
                "  - Drag ANY file from Windows Explorer onto the middle 'DROPZONE' box (or click 'Push File -> Pixels').\r\n" +
                "  - It automatically copies that file straight into /sdcard/Download/ on every connected Pixel phone!\r\n" +
                "  - Bonus: If you drop a .wav audio file or .omsh shader file onto the Dropzone, it ALSO hot-loads straight into OmegaAV v0.7.1 immediately!\r\n" +
                "  - Click 'Pull Pixel DL' to grab the newest file from your Pixel phone's Download folder onto your PC's <OMEGA_LAB_ROOT>\\downloads folder.\r\n\r\n" +
                "SUITE-WIDE WORKFLOW BUTTONS:\r\n" +
                "  - Launch Full Tri-App Suite (Purple, F5): Arms all Pixel tunnels and opens Browser, Mirror, and OmegaAV together.\r\n" +
                "  - Split-OLED: If two Pixels are connected, puts the Browser Research Companion on Phone 1 and OmegaAV visuals on Phone 2!\r\n" +
                "  - Open Vaults & Downloads: Quick menu to open your Downloads, Corpus notes, Renders, Shaders, or Google Drive Bridge folder.\r\n" +
                "  - Push Golden -> Bridge (Green, F9): Copies your latest compiled apps, source code, and SHA-256 manifest to Google Drive (OMEGA_BRIDGE_EXPORT)."));

            list.Add(new ManualChapter("3. OmegaBrowser v0.6.4",
                "=== 3. OMEGABROWSER v0.6.1 — RESEARCH BROWSER & VAULT DOWNLOAD STUDIO ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "A distraction-free research and developer browser built around three dedicated workspaces (Research, Training, Dev), isolated login containers, zero-download PDF distillation, and a resumable HTTP 206 Download Vault.\r\n\r\n" +
                "HOW TO USE THE CTRL+J VAULT DOWNLOAD STUDIO:\r\n" +
                "  [STEP 1] Press Ctrl+J (or click the blue 'DL (Ctrl+J)' button on the toolbar) to open the Sovereign Vault Download Studio.\r\n" +
                "  [STEP 2] Every file you download on the web automatically routes into sorted folders inside <OMEGA_LAB_ROOT>\\downloads (Papers, Audio, Shaders, Models, Archives).\r\n" +
                "  [STEP 3] Paste any direct file URL into '+ HTTP 206' and click 'Start HTTP 206 Download'. You can click 'Pause' and 'Resume (HTTP 206)' anytime without losing progress!\r\n" +
                "  [STEP 4] Select any finished download row and click:\r\n" +
                "           - '-> Send to OmegaAV v0.6' to play downloaded .wav audio or .omsh shaders immediately.\r\n" +
                "           - '-> Push to Pixel 8 / 10' to beam a PDF paper or media file straight to your phone.\r\n" +
                "           - 'PDF -> +Corpus' to extract clean text into your local research corpus.\r\n\r\n" +
                "HOW TO USE BATCH LINK SNIFFER (CTRL+SHIFT+D):\r\n" +
                "  - On any web page full of PDFs, datasets, or audio links (like an arXiv listing or university course page), press Ctrl+Shift+D (or click 'Sniff').\r\n" +
                "  - Filter by .pdf, .wav, .zip, or .gguf, check the ones you want, and download all of them into your Vault in one click!\r\n\r\n" +
                "POWER RESEARCH FEATURES:\r\n" +
                "  - Split View (Ctrl+\\ or 'Split'): View two tabs side-by-side in one window (e.g., paper on left, docs on right).\r\n" +
                "  - Containers ('Cnt:' button): Isolate cookies per tab (Default, GitHub, Google, Scratch) so logins never mix.\r\n" +
                "  - Zero-Download PDF Reader ('Reader'): Distills open arXiv PDFs into clean dark-mode text without cluttering Downloads.\r\n" +
                "  - Concept Braid (Ctrl+Shift+B / 'Braid'): Synthesizes notes across all open tabs into one combined Markdown summary.\r\n" +
                "  - Page Zapper (Alt+X) & Clipper (Alt+C): Click annoying banners to erase them (Alt+X), or click any theorem/code box to save it (Alt+C).\r\n" +
                "  - Command Palette (Ctrl+K): Type '= 2^12 * 384' for instant math, 'conv 64 gb to mib' for units, or '!arxiv E8' / '!rs Vec' to search."));

            list.Add(new ManualChapter("4. OmegaMirror v0.9",
                "=== 4. OMEGAMIRROR v0.9 — DUAL-PIXEL SCREEN STREAM & TOUCH BRIDGE ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "Turns your Google Pixel 10 and Pixel 8 into fast secondary touchscreens and file companions for your Windows 11 laptop.\r\n\r\n" +
                "STEP-BY-STEP: STREAMING YOUR PC TO YOUR PIXEL PHONE\r\n" +
                "  [STEP 1] Plug your Pixel in via USB (or connect wirelessly on :5555 via the Main Launcher).\r\n" +
                "  [STEP 2] Launch OmegaMirror v0.9 (F2 in Launcher) and click 'Beam :19777 -> Pixels'.\r\n" +
                "  [STEP 3] Chrome on your Pixel immediately opens http://127.0.0.1:19777 over the zero-lag ADB reverse tunnel (works even on airplane Wi-Fi!).\r\n" +
                "  [STEP 4] Tap anywhere on your Pixel screen to click or control your PC desktop remotely, adjust FPS/quality, or transfer files.\r\n\r\n" +
                "DRAG-AND-DROP FILE SYNC:\r\n" +
                "  - Drag files straight onto the OmegaMirror window to push them to /sdcard/Download/ on your phone, or pull recent mobile screenshots/downloads back to <OMEGA_LAB_ROOT>\\downloads."));

            list.Add(new ManualChapter("5. OmegaAV v0.7.1",
                "=== 5. OMEGAAV v0.7.1 — GPU SHADER STUDIO, FLUID PHYSICS, MIDI & GAMEPAD ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "A real-time Direct3D 11 GPU compute visualizer, reaction-diffusion fluid simulator, audio analyzer, and video recorder.\r\n\r\n" +
                "STEP-BY-STEP: HOW TO USE OMEGAAV\r\n" +
                "  [STEP 1] Launch OmegaAV v0.7.1 (F3 in Launcher). It immediately runs real-time GPU compute shaders at 60 FPS.\r\n" +
                "  [STEP 2] Switch presets or edit live shader math (.omsh) to reshape waves, palettes, and sacred/lattice geometry.\r\n" +
                "  [STEP 3] Drop any .wav audio file into OmegaAV (or send one from OmegaBrowser's Ctrl+J Studio) to make the visuals and fluid react to bass, mids, highs, and chord velocities!\r\n" +
                "  [STEP 4] Toggle the GPU Ping-Pong Reaction-Diffusion Fluid Pass for organic liquid ripples driven by audio chords or mouse/touch splashes.\r\n\r\n" +
                "HARDWARE KNOBS: USB MIDI & XBOX / DUALSENSE GAMEPAD\r\n" +
                "  - Plug in any USB MIDI keyboard/knob controller OR an Xbox / PlayStation DualSense controller.\r\n" +
                "  - Analog sticks, triggers, and MIDI CC knobs automatically modulate fluid feed/kill rates, zoom, wave speed, and color shift in real time!\r\n\r\n" +
                "SAVING SNAPSHOTS, SHADERS & VIDEOS:\r\n" +
                "  - Export crisp PNG snapshots, save your active .omsh shader, or record lossless/uncompressed .avi and high-res render sequences into 03_omega_av\\renders.\r\n" +
                "  - On your Pixel phone (:19779), tap 'Save Frame' or 'Save .omsh' to download snapshots directly to your phone!"));

            list.Add(new ManualChapter("6. OmegaSynth v1.1 Final GM (Lattice DAW)",
                "=== 6. OMEGASYNTH v1.0 GOLDEN MASTER — 60FPS VSYNC, PATCH BANK, SOUND-SHAPE RESYNTHESIS & WEB PIPE (:19782) ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "A real-time 48,000 Hz stereo physical-modeling acoustic synthesizer and cymatic visualizer (press F10 in the Main Launcher).\r\n" +
                "It simulates coupled waves across all 240 roots (6,720 edges) of the 8D E8 lattice alongside a 256x256 DirectCompute (cs_5_0) 2D Chladni resonant plate with virtual quartz sand settling onto nodal lines.\r\n\r\n" +
                "[STEP 1] PLAY ON DESKTOP OR PIXEL 10 OLED MPE PAD\r\n" +
                "  - Drag your mouse across the live Chladni Sand Plate (X = Fretless Pitch, Y = Nodal Harmonic Brightness), or play keys Z..M and Q..U.\r\n" +
                "  - Click 'Beam MPE -> Pixel 10' (or 'Beam MPE -> Pixels' in the Launcher) to open http://127.0.0.1:19782/mpe on your Pixel 10 / Pixel 8.\r\n" +
                "  - Slide up to 8 fingers simultaneously for polyphonic fretless microtonal MPE, and tilt your phone left/right for live DeviceOrientationEvent gyroscope pitch-bend (tap 'Zero Tilt' anytime to calibrate your hand angle)!\r\n\r\n" +
                "[STEP 2] EXPLORE SCALES, EUCLIDEAN SEQUENCER & GEMMA PATCH AI\r\n" +
                "  - Switch between 6 physical resonators (E8 Crystal Continuum, Chladni Bronze Plate, Quartz Singing Bowl, Titanium Handpan, Bowed Glass, Sub-Lattice Gong) and 6 microtonal tunings (12-TET, Just Intonation, 31-EDO, Bohlen-Pierce, Golden Phi, E8 Coxeter).\r\n" +
                "  - Press SPACE to run the 16-Step Euclidean Lattice Arpeggiator, or type a mood into 'Gemma Patch AI' to auto-compose a patch!\r\n\r\n" +
                "[STEP 3] LIVE WASAPI LOOPBACK & .WAV HOT-SEND TO OMEGAAV v0.7.1\r\n" +
                "  - Click 'Sync -> OmegaAV (:19779)' at the top right. Because OmegaAV v0.7.1 listens via Windows WASAPI Loopback, every note you play in OmegaSynth drives OmegaAV's 24D Leech & Fluid shaders with zero cables!\r\n" +
                "  - Click 'Record 48kHz .WAV' to save lossless stereo renders into 04_omega_synth\\renders\\, and click 'Send .WAV -> OmegaAV' anytime.\r\n\r\n[STEP 4] v1.0 GM SOUND-SHAPE HOLOGRAM, SPECTRAL RESYNTHESIS & OMEGABROWSER YOUTUBE PIPE\r\n  - Drop ANY .wav, .mp3, .flac, .m4a, or .ogg file onto OmegaSynth (or open Tab 3) to watch its 3D Polar Cymatic Rose, Stereo Lissajous Knot, and 512x512 Chladni Sand shape bloom at 60/144Hz VSync!\r\n  - Click 'Clone Sound -> Playable MPE #09' to extract its top 8 harmonic ratios & decay envelope so you can play that exact sound polyphonically!\r\n  - In OmegaBrowser v0.6.4 (:9778), click '-> Synth' on any YouTube/SoundCloud tab to pipe live web audio into OmegaSynth with zero feedback, freeze 2s into MPE #09, or export .PNG & vector .SVG blueprints!"));

            list.Add(new ManualChapter("7. OmegaForge v0.2.1 (AI & Code)",
                "=== 6. OMEGAFORGE v0.2 — COMPILER MATRIX & LOCAL GEMMA AI WORKBENCH ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "Your all-in-one coding, theorem-proving, and local AI engineering studio on port :19780. It scans your laptop for every installed language/compiler and lets you run code manually OR connect local AI models to write and verify code automatically.\r\n\r\n" +
                "PANEL 1 (LEFT): MODULAR TOOLCHAIN & COMPILER MATRIX\r\n" +
                "  - Automatically finds up to 32 tools on your laptop: C# (csc & dotnet), Rust (rustc & cargo), C++ (Clang++ & MSVC cl.exe), DirectX Shaders (fxc & Shader Model 6 dxc), NVIDIA GPU telemetry, Lean 4 theorem prover, Python, Deno, Node.js, PowerShell, ADB, Git, DuckDB, SQLite, CMake, and Ollama.\r\n" +
                "  - Single-click any tool row to see its exact path & version, then click 'Load Selected Tool Template -> Scratchpad' to try a working example.\r\n" +
                "  - Click the checkbox next to a found [OK] tool to grant or revoke AI permission to run it. Use the quick preset buttons ('All Found', 'GPU/HLSL', 'Lean 4', 'Systems', 'ADB/SQL') to arm groups in 1 click.\r\n\r\n" +
                "PANEL 2 (CENTER): LOCAL GEMMA AI AGENT (v0.2 UPGRADES!)\r\n" +
                "  - AI Model Dropdown (Top Row 2): Live ready servers appear first! Choose:\r\n" +
                "      * gemma-4-12B-it-sh4z (:8097 Sovereign Manifold): Best for formal Lean 4 mathematical proofs, Rust kernels, and algebraic specs.\r\n" +
                "      * Ollama (:11434): Click 'Wake Ollama (:11434)' to start your local Ollama engine for conversational Q&A and general CLI tasks.\r\n" +
                "  - Context Gate Dropdown (Top Row 3):\r\n" +
                "      * Surgical Intent (1-3 Tools) [Default]: Sends ONLY the 1 to 3 tools matching your question so :8097 never gets distracted by unrelated tool names!\r\n" +
                "      * Raw Model (Zero System): Sends your exact text with no system prompt at all.\r\n" +
                "      * Full Armed Matrix: Sends every checked tool in the system prompt.\r\n" +
                "  - Live Streaming & Non-Repetition Meter: Watch tokens stream live with real-time tok/s speed and Unique 4-Gram % (95-100% confirms zero repetitive looping). Click 'Stop Stream' anytime to halt instantly!\r\n" +
                "  - Auto-Verify Fences & Lean 4: Whenever the AI outputs fenced Rust/C#/C++/HLSL/DXC/Python code OR a continuous stream of Lean 4 theorems, OmegaForge v0.2.1 automatically extracts it, reflows line breaks, compiles/typechecks it with your real compiler (lean.exe, rustc.exe, csc.exe, dxc.exe), and prints a SHA-256 verification receipt!\r\n" +
                "  - Dual-Brain Tandem (:11434 + :8097): Automatically runs live hardware probes (like nvidia-smi or adb) first and routes chat vs. formal proofs to the best local engine.\r\n\r\n" +
                "PANEL 3 (RIGHT): DIRECT POLYGLOT COMPILER SCRATCHPAD\r\n" +
                "  - Works 100% independently even if no AI server is running!\r\n" +
                "  - Pick any language or tool (csharp, rust, cpp, msvc, hlsl, dxc, lean4, python, deno, node, powershell, nvidia_smi, adb, duckdb), edit code, and press F5 ('Compile & Run') to see instant output and timing.\r\n" +
                "  - Hit a compiler error? Click 'Ask Gemma Fix' to send the code + compiler error straight to your local AI."));

            list.Add(new ManualChapter("8. OmegaShell v1.0.2 Golden Master (Code, Auto-Fmt & Android AVF)",
                "=== 7. OMEGASHELL v0.3 — MULTI-TAB CLOUD SHELL, C:\\ JUNCTION OFFLOADER & HARDWARE TUNER ===\r\n\r\n" +
                "WHAT IT IS (IN PLAIN ENGLISH)\r\n" +
                "OmegaShell v1.0.2 Golden Master (press F7 in the Main Launcher) is a 4-tab workstation cockpit on port :19781 that combines a fast Linux/PowerShell hybrid terminal, native Windows 11 multi-drive file access, a Local Cloud SHA-256 backup/snapshot vault, and a real Win32 Hardware Optimizer.\r\n\r\n" +
                "TAB 1: OSH TERMINAL & STRUCTURED OBJECT GRID\r\n" +
                "  - Type short, friendly commands like 'drives', 'ls E:\\ -r *.gguf', 'ps', 'ports', 'startup', or 'services'.\r\n" +
                "  - Pipe any table into clean filters without ugly syntax:\r\n" +
                "      ls E:\\ -r *.gguf | where size_mb gt 50 | sort -desc size_mb\r\n" +
                "      ports | where state == LISTEN\r\n" +
                "      ps | top 10 | @ai summarize memory usage\r\n" +
                "  - Every structured output automatically fills the sortable Grid on the right (click any column header to sort, or click 'Copy Grid as JSON').\r\n" +
                "  - Prefix commands with @p10, @p8, or @pixels to run commands directly on your connected Pixel phones over ADB!\r\n\r\n" +
                "TAB 2: WINDOWS 11 ALL-DRIVES & LOCAL CLOUD CAS\r\n" +
                "  - Click any drive button at the top (C:\\, E:\\, G:\\, or USB drives) or bookmark (@lab, @vault, @forge, @bridge) to browse files instantly.\r\n" +
                "  - Select any file to inspect its live SHA-256 checksum and text or binary hex header.\r\n" +
                "  - Click 'Pin Selected -> Cloud CAS' to store deduplicated copies in your local SHA-256 vault, or click 'Snapshot Folder' and 'Restore Selected Snap' for instant time-travel folder backups!\r\n\r\n" +
                "TAB 3: HARDWARE TUNER, STARTUP/SERVICES & LIVE TCP PORT KILLER\r\n" +
                "  - 1. Trim Background RAM: Uses Windows psapi EmptyWorkingSet to reclaim idle physical DDR5 from background apps while skipping your Protected Whitelist.\r\n" +
                "  - 2. Sovereign P-Core Boost + EcoQoS Parking: Pins compilers and Lab apps to high-speed P-cores while parking background bloat (Widgets, SearchHost, updaters) onto E-cores in Windows 11 Efficiency Mode! Click 'Restore Default Affinities' anytime to undo.\r\n" +
                "  - Sub-Tab B (Live TCP Port & Socket Killer): Lists every open port and connection in under 1 ms via iphlpapi. Type a stuck port like '8080' and click 'Free Port Now' to kill the stuck process, or click 'Drop Selected Socket (DeleteTCB)'.\r\n" +
                "  - Sub-Tab C (Startup Apps): Reversibly disable or re-enable Windows 11 startup apps using native StartupApproved flags without deleting registry keys.\r\n" +
                "  - Sub-Tab D (Windows Services & DirectCompute GPU VRAM): Inspect running Windows services (Start/Stop/Restart safely) and see which exact PIDs are using your DirectCompute GPU GPU VRAM.\r\n" +
                "  - Sub-Tab E (C:\\ Space-Hog Scanner & mklink /J Junction Offloader): Scans heavy AI model caches (.ollama, .cache\\huggingface, .lmstudio), Cargo/Rust targets, and Downloads on C:\\, copies 100% of bytes to F:\\OMEGA_JUNCTION_VAULT (2.33 TB) or E:\\, and swaps in a transparent Windows NTFS junction (mklink /J) with 1-click Undo!\r\n" +
                "  - Tab 2 (Docked Polyglot Code & Auto-Format Studio): Syntax-highlighted dark editor with line numbers, Ctrl+Shift+F Dual-Tier Auto-Formatter (rustfmt, clang-format, deno fmt + zero-dependency C#/PS1/Lean4/HLSL/JSON/OSH AST formatters), F6 Syntax Check, F5 Compile & Run, F8 Diff vs Disk, and 1-click Push -> @deb!\r\n" +
                "  - Tab 3 (Android Linux / Debian VM Bridge + Ed25519 SSH Keygen & 1-Click Setup): Probes Pixel 10 / Pixel 8 AVF pKVM hypervisor & Terminal app, generates Windows 11 OpenSSH Ed25519 keys with strict icacls ACLs, pushes omega_debian_setup.sh into /sdcard/Download/linux (/mnt/shared), arms TCP :22222 SSH tunnels, and executes live ARM64 commands via '@deb <cmd>'!\r\n" +
                "  - OSH v0.3 Programmable Shell Syntax: Supports variables ('let x = 80', '$x', '$env:PATH'), chains ('&&', '||', ';'), columnar group-by ('ps | group name | sort -desc sum_ram_mb'), inline math ('calc'), micro-benchmarking ('bench'), 'git-stat', and file redirects ('> report.json' / '.csv').\r\n" +
                "  - v0.2 Power Upgrades: Click '+ New Shell Tab (Ctrl+T)' for independent multi-tab sessions with persistent history, type 'edit <file>' for an inline UTF-8 editor, Ctrl/Shift-select multiple processes to save persistent Core Tier rules, or Right-Click any Grid cell to Filter, Sort, or Pipe straight to @ai!\r\n\r\n" +
                "TAB 4: OMEGALABS MONITOR & SHELL SETTINGS\r\n" +
                "  - Polls all 7 Lab & AI ports (:19777, :9778, :19779, :19780, :19781, :8097, :11434) every 2 seconds with 1-click Launch/Stop buttons, plus editors for your startup script (config\\init.oshrc) and Protected Optimizer Whitelist."));

            list.Add(new ManualChapter("9. Gemma Suite & Codegen Command",
                "=== 8. GEMMA SUITE COMMANDER & POLYGLOT CODEGEN (LAUNCHER v1.4 + SHELL v1.0 GM) ===\r\n\r\n" +
                "WHAT IT IS\r\n" +
                "Your local Gemma AI models (:8097 Sovereign Manifold gemma-4-12B-it-sh4z / E4B / SH-XI and :11434 Ollama) now directly command tools, coding suites, and verified code generation across BOTH OmegaLabLauncher v1.4 and OmegaShell v1.0.2 Golden Master.\r\n\r\n" +
                "[STEP 1] GEMMA COMMANDER IN OMEGALABLAUNCHER v1.4 (RIGHT PANEL / F8)\r\n" +
                "  - Pick any live model in the top-right dropdown (click Scan AI or Wake :11434 anytime).\r\n" +
                "  - Choose Dispatch mode: Suite ReAct Commander (Auto), or direct Codegen for Rust, C# 5.0, Lean 4, C++23, .omsh -> OmegaAV, or PowerShell 5.1.\r\n" +
                "  - Click any of the 4 instant presets (Audit Lab & Free RAM, Codegen Rust E8 Kernel, Codegen Lean 4 Proof, or Gen .omsh -> OmegaAV) or type your own goal and press Enter!\r\n\r\n" +
                "[STEP 2] GEMMA COMMANDER IN OMEGASHELL v1.0 GM (TAB 1 & TAB 2)\r\n" +
                "  - In Tab 1 (OSH & Gemma Command), use the purple Gemma Commander bar or type:\r\n" +
                "      gemma check listen ports and group top RAM processes\r\n" +
                "      codegen rust \"240-root E8 inner product benchmark\"\r\n" +
                "      models   |   use-model gemma-4-12B-it-sh4z\r\n" +
                "  - In Tab 2 (Code & Gemma Codegen), type a spec in the Gemma Codegen bar and click:\r\n" +
                "      * Codegen + Auto-Fmt + Verify -> Writes UTF-8 (No BOM), runs rustfmt/clang-format/deno fmt/AST, compiles & runs, and auto-heals compiler errors up to 2 passes until Exit 0!\r\n" +
                "      * Refactor Buffer -> Optimizes your current file and displays a line-by-line unified diff.\r\n" +
                "      * Auto-Fix Errors -> Reads compiler diagnostics and repairs the active buffer automatically."));

            list.Add(new ManualChapter("10. Shortcuts & Local Ports Map",
                "=== 9. MASTER KEYBOARD SHORTCUTS & LOCALHOST PORTS CHEAT-SHEET ===\r\n\r\n" +
                "MAIN LAUNCHER SHORTCUTS (OMEGALABLAUNCHER v1.3.2):\r\n" +
                "  F1  -> Launch or Focus OmegaBrowser v0.6.4\r\n" +
                "  F2  -> Launch or Focus OmegaMirror v0.9\r\n" +
                "  F3  -> Launch or Focus OmegaAV v0.7.1\r\n" +
                "  F4  -> Launch or Focus OmegaForge v0.2.1\r\n" +
                "  F5  -> Launch Full Core Suite + Arm All Pixel Tunnels\r\n" +
                "  F6  -> Open This Human Help & FAQ User Manual (also '?' key)\r\n" +
                "  F9  -> Push Golden Master Backup & Manifest to Google Drive Bridge\r\n" +
                "  F12 -> Stop All 4 Lab Processes & Free All RAM / GPU VRAM\r\n\r\n" +
                "OMEGABROWSER v0.6.1 SHORTCUTS:\r\n" +
                "  Ctrl+J       -> Open Sovereign Vault Download Studio (HTTP 206 Pause/Resume)\r\n" +
                "  Ctrl+Shift+D -> Batch Sniff All Page Links (.pdf, .wav, .zip, .gguf)\r\n" +
                "  Ctrl+K       -> Command Palette (Math '=', Unit 'conv', !arxiv / !rs / !gh bangs)\r\n" +
                "  Ctrl+\\       -> Toggle Side-by-Side Split View\r\n" +
                "  Ctrl+Shift+A -> Quick Search Across Open Tabs\r\n" +
                "  Ctrl+Shift+T -> Reopen Recently Closed Tab\r\n" +
                "  Ctrl+Shift+B -> Synthesize Multi-Tab Concept Braid\r\n" +
                "  Ctrl+Shift+R -> Run Selected Page Code in 4-Second Sandbox\r\n" +
                "  Alt+X        -> DOM Zapper (Click to remove page clutter)\r\n" +
                "  Alt+C        -> Subtree Clipper (Click to clip theorem/code to Corpus)\r\n" +
                "  Alt+V        -> Vim Keyboard Navigation & Link Hints ('f')\r\n\r\n" +
                "OMEGAFORGE v0.2 SHORTCUTS:\r\n" +
                "  Ctrl+Enter   -> Send Prompt to Local Gemma AI (in Prompt Box)\r\n" +
                "  F5           -> Compile & Run Code in Direct Polyglot Scratchpad\r\n\r\n" +
                "LOCALHOST PORT MAP (100% PRIVATE TO YOUR LAPTOP 127.0.0.1):\r\n" +
                "  Port :19777  -> OmegaMirror v0.9 (Desktop Stream & Touch UI)\r\n" +
                "  Port :9778   -> OmegaBrowser v0.6.4 (/companion, /api/status, /api/downloads)\r\n" +
                "  Port :19779  -> OmegaAV v0.7.1 (/frame.jpg, /download/snapshot.jpg, /download/active.omsh)\r\n" +
                "  Port :19780  -> OmegaForge v0.2.1 (/api/status, /api/tools, /api/models, /api/exec, /api/verify)\r\n" +
                "  Port :19781  -> OmegaShell v1.0.2 Golden Master (/api/status, /api/exec, /api/tuner, /api/format, /api/avf)\r\n" +
                "  Port :19782  -> OmegaSynth v1.1.1 Final GM (/mpe Pixel OLED Fretless Pad, /api/status, /api/browser-pipe, /api/load-file)\r\n" +
                "  Port :19784  -> OmegaSheaf v0.2.1 GM (/sheaf 2-Way OLED Cohomology Pad, /api/graph, /api/status, /api/touch)\r\n" +
                "  Port :19786  -> OmegaSteg v0.2.1 (/vault Unplugged Air-Gap Golay+Leech Transceiver, /api/status, /api/cmd)\r\n" +
                "  Port :19785  -> OmegaPenrose v1.0.2 Final GM (/pad 5D/8D Quasicrystal OLED Pad, /api/status, /api/touch, /api/cmd)\r\n" +
                "  Port :8097   -> Sovereign Manifold UTCP (gemma-4-12B-it-sh4z / E4B OpenAI-compatible surface)\r\n" +
                "  Port :11434  -> Ollama Local AI Server (/api/chat, /api/tags)"));

            list.Add(new ManualChapter("11. Troubleshooting & Human FAQ",
                "=== 10. TROUBLESHOOTING & HUMAN FAQ (12 COMMON QUESTIONS ANSWERED) ===\r\n\r\n" +
                "Q1: Why did gemma-4-12B-it-sh4z (:8097) output hundreds of Lean 4 theorems when I asked a short GPU question?\r\n" +
                "A: Port :8097 runs your specialized Sovereign Harmonic MoE manifold. In v0.1, sending all 25 compiler names (including 'Lean 4' and 'DXC') in the system prompt triggered its formal theorem bank. In OmegaForge v0.2.1, keep Context Gate set to 'Surgical Intent (1-3 Tools)' (or turn on 'Dual-Brain Tandem') so only the exact tool you asked about is sent!\r\n\r\n" +
                "Q2: OmegaForge says 'Could not reach http://127.0.0.1:...' — how do I fix it?\r\n" +
                "A: That just means the selected local AI server isn't awake yet! Open the 'AI Model' dropdown at the top of OmegaForge v0.2.1 and pick any model marked [OpenAI-Live] or [Ollama-Live], OR click the purple 'Wake Ollama (:11434)' button to start Ollama automatically.\r\n\r\n" +
                "Q3: Can I compile and test Rust, C#, C++, HLSL, DXC, or Lean 4 without running any AI model at all?\r\n" +
                "A: Yes! The Right Panel ('3. Direct Polyglot Compiler Scratchpad') works 100% offline with zero AI running. Pick your language in the dropdown, write or paste code, and press F5.\r\n\r\n" +
                "Q4: Why won't OmegaForge let me check the box next to g++, nvcc, ghc, or ffmpeg?\r\n" +
                "A: Items marked [--] were not found on your laptop yet, so v0.2 keeps them unchecked so the AI never tries to run a missing program. If you have one installed in a custom folder, click '+ Custom Tool' at the top to point to its .exe!\r\n\r\n" +
                "Q5: Where are all my downloaded PDFs, audio files, shaders, and compiled programs stored?\r\n" +
                "A: Everything stays neatly organized inside <OMEGA_LAB_ROOT>\\:\r\n" +
                "  Folder: <OMEGA_LAB_ROOT>\\downloads      (All Browser Ctrl+J & Pixel pulled files)\r\n" +
                "  Folder: <OMEGA_LAB_ROOT>\\03_omega_av    (Shaders in \\shaders, videos/PNGs in \\renders)\r\n" +
                "  Folder: <OMEGA_LAB_ROOT>\\09_omega_forge (AI & Scratchpad builds in \\workspace)\r\n" +
                "Click 'Open Vaults & Downloads' in the Main Launcher anytime to open any of these folders.\r\n\r\n" +
                "Q6: How do I pause a huge download and resume it later in OmegaBrowser?\r\n" +
                "A: Press Ctrl+J inside OmegaBrowser, paste or start the download via the Vault Studio, and click 'Pause'. When ready, click 'Resume (HTTP 206)'—it picks up from the exact byte where it stopped!\r\n\r\n" +
                "Q7: My Pixel phone isn't showing up in the Main Launcher. What should I check?\r\n" +
                "A: Unlock your Pixel screen, plug in a data-capable USB-C cable, and tap 'Allow' on the 'Allow USB debugging?' prompt on the phone. Within 2 seconds the Launcher status card will turn blue and show 'Pixels (1): USB:...'.\r\n\r\n" +
                "Q8: How do I stream to my Pixel phone without a Wi-Fi router or when Wi-Fi is congested?\r\n" +
                "A: Keep the USB cable plugged in and click 'Beam :19777 -> Pixels' or 'Beam :19779 -> Pixels'. It uses ADB USB reverse tunneling over pure USB-C wire speed (http://127.0.0.1) with zero Wi-Fi lag!\r\n\r\n" +
                "Q9: How do I control OmegaAV shaders with an Xbox/PlayStation controller or MIDI keyboard?\r\n" +
                "A: Just plug in or pair your controller/MIDI device before or while OmegaAV v0.7.1 is open—no extra drivers or packages required. Move the sticks/triggers or turn MIDI knobs to steer fluid and wave parameters live.\r\n\r\n" +
                "Q10: What does 'Unique 4-Gram: 99.8% (Zero-Loop Coherent)' mean in OmegaForge v0.2.1?\r\n" +
                "A: Small local AI models sometimes get stuck repeating the same sentence over and over. OmegaForge v0.2.1 measures every 4-word sequence in real time: 92%-100% means virtually zero repetition; if it ever drops low, just click the red 'Stop Stream' button.\r\n\r\n" +
                "Q11: How do I free up all memory before playing a heavy game or training a model?\r\n" +
                "A: Open OmegaLabLauncher and press F12 (or click the red 'Stop All 4 Standalone Lab Processes' button). It cleanly closes OmegaForge, OmegaAV, OmegaBrowser, and OmegaMirror and reports the exact MB of RAM freed.\r\n\r\n" +
                "Q12: How do I save or print this manual to read outside the app?\r\n" +
                "A: Click the green 'Save Printable Manual (.md)' button at the top of this window! It exports the complete handbook to <OMEGA_LAB_ROOT>\\OMEGA_LAB_HUMAN_USER_MANUAL.md."));

            list.Add(new ManualChapter("12. OmegaPenrose v1.0.2 Final GM (:19785) — 8D E8, Hat/Spectre, Hologram, Quantum & Solid Z-3D",
                "OMEGAPENROSE v1.0 FINAL GM (07_omega_penrose) — GOLDEN MASTER QUASICRYSTAL STUDIO\r\n" +
                "=====================================================================================\r\n\r\n" +
                "1. 9 APERIODIC SYMMETRIES (5D DE BRUIJN, 8D E8 ELSER-SLOANE, HAT & SPECTRE MONOTILES):\r\n" +
                "   - Symmetries 01..06 plus 07 (240-root E8 30-fold Coxeter-Icosian), 08 (Gap-free Einstein Hat Tile(1,sqrt3)\r\n" +
                "     with royal-gold Anti-Hats), and 09 (Zero-reflection Chiral Spectre Tile(1,1)).\r\n\r\n" +
                "2. INVERSE BRAGG LASER HOLOGRAM (CGH) & 5D MILLER PEAK INSPECTOR:\r\n" +
                "   - Drag-and-drop any .PNG/.JPG/.BMP image to run 20 Gerchberg-Saxton 2D FFT iterations and reconstruct\r\n" +
                "     it inside the -92 dB Blackman-Harris GPU laser diffractometer!\r\n" +
                "   - Click any Bragg diffraction spot to index its exact 5D Miller vector (h0,h1,h2,h3,h4) and plane spacing d.\r\n\r\n" +
                "3. DARWINIAN GENOME-MUTATING FLOW-LENIA & KOHMOTO-SUTHERLAND QUANTUM WAVEPACKETS:\r\n" +
                "   - Advects heritable (Mu, Sigma) genes across quasicrystal niches, or evolves unitary symplectic leapfrog\r\n" +
                "     2D tight-binding Schrodinger wavepackets (press Q).\r\n\r\n" +
                "4. HARDWARE Z-BUFFERED SOLID SPECULAR 3D JEWEL, BINARY .STL, .OPENROSE & .AVI:\r\n" +
                "   - InterlockedMax 14-bit depth buffer + Blinn-Phong specular 3D crystals, IEEE-754 Binary .STL 3D-print\r\n" +
                "     exporter, 10 factory .openrose patches, and 24-FPS Motion-JPEG .AVI loop recorder.\r\n"));
            list.Add(new ManualChapter("13. OmegaSheaf v1.0 GM (:19784) — Cellular Sheaf Cohomology Truth-Canvas",
                "OMEGASHEAF v1.0 GM (06_omega_sheaf) — CELLULAR SHEAF COHOMOLOGY TRUTH-CANVAS\r\n" +
                "=============================================================================\r\n" +
                "  1. Dual-Lens Sheaf Laplacian L_F = delta^T W delta (60 FPS RTX DirectCompute cs_5_0):\r\n" +
                "     Models paper claims, #[repr(C)] Rust/C++23/C# FFI structs, and localhost port\r\n" +
                "     contracts as 8D Conway-Sloane E8-quantized vertex stalks connected by SO(2)^4\r\n" +
                "     restriction maps. Global consensus (H^1 = 0) glows emerald; cross-paper or ABI\r\n" +
                "     paradox cycles (H^1 != 0) ignite in crimson with real-time L_F matrix heatmaps.\r\n" +
                "  2. Live Corpus & Polyglot FFI Codebase Scanners + 8 Built-In .osheaf Presets:\r\n" +
                "     Audits all 8 live OmegaLabs stations, replays the OmegaAV SRV ViewDimension=4\r\n" +
                "     DirectCompute bug, ingests OmegaBrowser .md papers, and scans Rust/C++/C#/TS dirs.\r\n" +
                "  3. 1-Click Reidemeister Surgery, Lean 4 Witness, .SVG, .JSONL & :19784 OLED Inspector:\r\n" +
                "     Rectifies frustrated cycles to H^1 = 0, exports formal Lean 4 witnesses to OmegaForge,\r\n" +
                "     syncs .OMSH shaders to OmegaAV (:19779), and streams to Pixels on :19784/sheaf.\r\n"));
            list.Add(new ManualChapter("14. OmegaSteg v0.2.1 (:19786) — Leech-24 Acoustic Air-Gap Modem & Visual Vault",
                "OMEGASTEG v1.0 GM (08_omega_steg) — LEECH-24 ACOUSTIC AIR-GAP MODEM & VAULT\r\n" +
                "===========================================================================\r\n" +
                "  1. Paley-11 [24, 12, 8] Extended Binary Golay + 24D Leech Lattice Codec:\r\n" +
                "     759 Steiner S(5,8,24) octads and 2,325 O(1) syndromes heal up to 3 bit\r\n" +
                "     flips in every 24-bit codeword with CRC-16 and SHA-256 verification.\r\n" +
                "  2. 48 kHz 24-Subcarrier Harmonic Chord Air-Gap Modem (TX Speakers / RX Mic):\r\n" +
                "     Broadcasts .omsh shaders or SSH keys as a 3-second harmonic chord to\r\n" +
                "     unplugged Pixels via standalone offline WebAudio HTML5 (/vault).\r\n" +
                "  3. Lossless 768x768 24-Fold Coxeter Leech Mandala .PNG Visual Vault:\r\n" +
                "     Embeds and self-heals Golay-protected shaders inside PNG art with 1-click\r\n" +
                "     hot-loading into OmegaAV v0.7.1 (:19779) and Lean 4 Forge export.\r\n"));
            list.Add(new ManualChapter("15. Pixel 10 Zero-Root ARM64 Rust + Vulkan Compute Worker (:19788)",
                "PIXEL 10 (TENSOR G5 + POWERVR DXT-48-1536) RUST + VULKAN WORKER (11_omega_edge_workers\rust_p10_worker)\r\n" +
                "=====================================================================================\r\n" +
                "  1. Unprivileged Linux 6.12 sched_setaffinity (syscall 122) 3-Lane Partition:\r\n" +
                "     - 0x03 (cpu0-1, 2x Cortex-A520 @ 2.246 GHz): 15.965 Hz (62.636 ms) Sensor/Clock\r\n" +
                "     - 0x7C (cpu2-6, 5x Cortex-A725 @ 3.052 GHz): 5-Thread SH4Z Ternary + E8 Pool\r\n" +
                "       (absorbs the retired Pixel 8 L2 compute role with zero root!)\r\n" +
                "     - 0x80 (cpu7,   1x Cortex-X4   @ 3.782 GHz): SH4Z-SEC1 Chebyshev M61 Wake Lease\r\n" +
                "  2. Dynamic Vulkan 1.x Compute (/system/lib64/libvulkan.so & Host vulkan-1.dll):\r\n" +
                "     Executes embedded 193-word Khronos SPIR-V 1.0 GLCompute kernel with 65,536-word\r\n" +
                "     bit-for-bit GPU==CPU verification on PowerVR DXT-48-1536 and Host DirectCompute GPU.\r\n" +
                "  3. OmegaShell v1.1.3 GM Integration (:19781 -> :19788):\r\n" +
                "     Run 'p10 bench', 'p10 daemon', 'p10 lease', '@p10-sensor', '@p10-compute', or '@p10-prime'.\r\n"));
            list.Add(new ManualChapter("16. OmegaBraidRE v1.3.0 (:19794) — Binary Lifter, #~ CIL & E8 Patch Differ",
                "OMEGABRAIDRE v1.3.0 (14_omega_braid_re, [Shift+F7]) — BINARY LIFTER & E8 PATCH DIFFER\r\n" +
                "=======================================================================================\r\n" +
                "  1. PE32+/PE32/ELF64 Inspector + .pdata Unwind + IAT/EAT + RIP-Relative Strings:\r\n" +
                "     Maps sections via overflow-safe TryRvaToFileOffset, decodes DataDirectory[3] .pdata\r\n" +
                "     RUNTIME_FUNCTION + UNWIND_INFO exact [Begin..End) bounds, resolves EAT exports and\r\n" +
                "     IAT + FF 25 linker thunks inline, and extracts ASCII & UTF-16LE .rdata strings.\r\n" +
                "  2. ECMA-335 #~ Metadata Tables (TypeDef 0x02 / Field 0x04 / MethodDef 0x06) & CIL Lifter:\r\n" +
                "     Detects managed .NET assemblies via BSJB, decodes exact Class::Method names, EntryPoint\r\n" +
                "     tokens, #US string literals, and lifts Tiny/Fat CIL method bodies directly from RVA.\r\n" +
                "  3. Dual-ISA (x86_64/ARM64) SSA BraidIR + 240-Root E8 Differ + Sheaf #[repr(C)] Structs:\r\n" +
                "     Strips 83.3% dead EFLAGS clobbers, renders a 60 FPS DirectCompute cs_5_0 8x8 E8 patch\r\n" +
                "     heatmap, and exports lockstep Rust #[repr(C)], C++23 static_assert, and C# structs.\r\n"));
            return list;
        }
    }
}
