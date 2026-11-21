using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OmegaSheafStudio
{
    public sealed class SheafStagePanel : Panel
    {
        public SheafStagePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        }
    }

    public sealed class OmegaSheafGuideDialog : Form
    {
        public OmegaSheafGuideDialog()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "OmegaSheaf v0.2.1 (v1.1 GM) :: 5-Pillar Cellular Sheaf Cohomology Truth-Canvas Guide";
            ClientSize = new Size(840, 620);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(9, 13, 24);
            ForeColor = Color.White;

            RichTextBox rtb = new RichTextBox();
            rtb.SetBounds(14, 14, 812, 546);
            rtb.ReadOnly = true;
            rtb.BackColor = Color.FromArgb(13, 19, 34);
            rtb.ForeColor = Color.FromArgb(226, 232, 240);
            rtb.BorderStyle = BorderStyle.FixedSingle;
            rtb.Font = new Font("Consolas", 9.3f);
            rtb.Text =
                "================================================================================\r\n" +
                " OMEGASHEAF v0.2.1 / v1.1 GM (:19784) :: ALL 5 PILLARS ACTIVE\r\n" +
                "================================================================================\r\n\r\n" +
                "PILLAR 1 — SUB-PIXEL SDF GPU GRAPH + EMBEDDED 5x6 HLSL FONT ATLAS:\r\n" +
                "  * Full-stride W PlotAdd + anti-aliased SDF capsules, directional restriction\r\n" +
                "    chevrons, 8-petal E8 stalk rosettes, and direct-on-GPU node title + E8#\r\n" +
                "    badges rendered at 60 FPS VSync in DirectCompute cs_5_0.\r\n\r\n" +
                "PILLAR 2 — SPECTRAL HODGE DECOMPOSITION & PERSISTENT COHOMOLOGY BARCODE:\r\n" +
                "  * Live Jacobi eigensolver on L_F decomposes edge tension into Exact Gradient,\r\n" +
                "    Curl Vorticity, and Harmonic H^1 Obstruction layers, rendered alongside a\r\n" +
                "    live Persistent Cohomology Barcode & Eigen-Spectrum in the right split pane.\r\n\r\n" +
                "PILLAR 3 — AUTOMATED CODEBASE .DIFF & CORPUS CITATION SURGEON:\r\n" +
                "  * Selecting any obstructed FFI, D3D11, Creation Kit, or corpus edge synthesizes\r\n" +
                "    a concrete unified .diff patch with 1-click handoff to OmegaShell Studio.\r\n\r\n" +
                "PILLAR 4 — 2-WAY PIXEL OLED TOUCH SURGERY PAD (:19784) + OMEGASYNTH SONIFICATION:\r\n" +
                "  * Drag stalks or tap crimson edges directly on your Pixel 10 / Pixel 8 OLED\r\n" +
                "    HTML5 canvas (/sheaf) to mutate the PC graph in <16ms, and toggle [Synth]\r\n" +
                "    to hear H^1 dissonance resolve into a pure E8 triad on OmegaSynth (:19782)!\r\n\r\n" +
                "PILLAR 5 — 24-FPS .AVI PROOF RECORDER & CREATION KIT QUEST-PARADOX EXPORTER:\r\n" +
                "  * Record Motion-JPEG .AVI surgery proofs and export UTF-8 No-BOM Fallout 4 /\r\n" +
                "    Skyrim SE SovereignSheafQuestGuard.psc + Rust L0 SKSE64/F4SE stage tables.\r\n";

            Button bOk = new Button();
            bOk.UseMnemonic = false;
            bOk.Text = "Close Guide";
            bOk.SetBounds(696, 570, 130, 36);
            bOk.FlatStyle = FlatStyle.Flat;
            bOk.BackColor = Color.FromArgb(16, 185, 129);
            bOk.ForeColor = Color.White;
            bOk.Font = new Font("Segoe UI Semibold", 9.0f);
            bOk.Click += delegate { Close(); };

            Controls.Add(rtb);
            Controls.Add(bOk);
        }
    }

    public sealed class OmegaSheafCockpitForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NATIVE_MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        private static extern bool PeekMessageW(out NATIVE_MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private readonly string _sheafDir;
        private readonly string _suiteRoot;
        private readonly string _exportsDir;
        private readonly string _patchesDir;
        private readonly string _avDir;
        private readonly string _avExe;
        private readonly string _fgDir;
        private readonly string _fgExe;

        private readonly SheafGraphModel _graph = new SheafGraphModel();
        private readonly OmegaSheafGpuEngine _gpu = new OmegaSheafGpuEngine();
        private readonly OmegaSheafHttpServer _http = new OmegaSheafHttpServer();
        private readonly OmegaSheafSynthSonifier _sonifier = new OmegaSheafSynthSonifier();
        private readonly OmegaSheafAviRecorder _avi = new OmegaSheafAviRecorder();

        private SheafStagePanel _stage;
        private Bitmap _cpuFallbackBmp;
        private Bitmap _oledThumbBmp;

        // Top & Bottom Bars
        private Label _lblTopVerdict;
        private Label _lblTelemetryRow2;
        private Label _lblHoverHud;
        private Label _lblStatusBot;
        private Button _btnTopPause;
        private Button _btnTopRectify;
        private Button _btnTopSynth;
        private Button _btnTopRecAvi;
        private Button _btnTopSplit;
        private Button _btnTopLabels;
        private ComboBox _cmbPatchBank;

        // Right-hand 4-tab controls
        private TabControl _rightTabs;
        private ComboBox _cmbViewMode;
        private ComboBox _cmbHodgeColor;
        private ComboBox _cmbVsync;
        private TrackBar _trkDiffusion;
        private TrackBar _trkE8Snap;
        private TrackBar _trkObsTol;
        private TrackBar _trkZoom;
        private Label _lblDiffVal;
        private Label _lblE8Val;
        private Label _lblTolVal;
        private Label _lblZoomVal;
        private CheckBox _chkSpringForces;
        private CheckBox _chkRunDiffusion;
        private CheckBox _chkGpuLabels;
        private Label _lblVerdictCard;
        private ListBox _lstLoops;

        // Tab 2: Corpus, FFI & Auto-Diff Surgeon
        private TextBox _txtDiffPreview;
        private Label _lblProvenance;

        // Tab 3: Stalk & Edge Surgery + Synth & Touch
        private ComboBox _cmbSelNode;
        private TextBox _txtNodeTitle;
        private TextBox _txtNodeSrc;
        private TextBox _txtNodeFormula;
        private CheckBox _chkNodePinned;
        private Label _lblNodeE8Stats;
        private ComboBox _cmbSelEdge;
        private ComboBox _cmbEdgePolarity;
        private TrackBar _trkEdgeTwist;
        private TrackBar _trkEdgeAbi;
        private Label _lblEdgeTwistVal;
        private Label _lblEdgeAbiVal;
        private Label _lblEdgeResidual;
        private Label _lblTouchSynthStats;

        // Tab 4: Lean 4, Creation Kit, .AVI & Bridge
        private TextBox _txtExportLog;
        private Button _btnTabAvi;
        private CheckBox _chkSyncAvOnGemma;
        private TextBox _txtGemmaPrompt;

        // Simulation & Camera State
        private bool _paused = false;
        private bool _springForces = true;
        private bool _runDiffusion = true;
        private bool _suppressUiEvents = false;

        private int _viewMode = 2; // 0=2D, 1=3D, 2=Split Graph | L_F + Barcode, 3=Wide Hodge Barcode
        private float _diffusionRate = 0.42f;
        private float _e8SnapStrength = 0.28f;
        private float _obsTolerance = 0.14f;
        private float _camZoom = 1.00f;
        private float _panX = 0.0f;
        private float _panY = 0.0f;
        private float _orbitYaw = 0.42f;
        private float _orbitPitch = 0.26f;

        private int _selNodeIdx = 0;
        private int _selEdgeIdx = 0;
        private int _hoverNodeIdx = -1;
        private int _hoverEdgeIdx = -1;
        private int _dragNodeIdx = -1;
        private bool _isRightPanning = false;
        private bool _isLeftOrbiting = false;
        private Point _lastMousePt;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastFrameSec = 0.0;
        private double _fpsAccumSec = 0.0;
        private int _fpsFrames = 0;
        private double _liveFps = 60.0;
        private double _lastOledSec = 0.0;
        private double _lastInboxSec = 0.0;
        private readonly Queue<string> _pendingRemoteCmds = new Queue<string>();

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaSheafCockpitForm(string initialFile)
        {
            _sheafDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            DirectoryInfo parent = Directory.GetParent(_sheafDir);
            _suiteRoot = (parent != null) ? parent.FullName : _sheafDir;
            _exportsDir = Path.Combine(_sheafDir, "exports");
            _patchesDir = Path.Combine(_sheafDir, "patches");
            _avDir = Path.Combine(_suiteRoot, "03_omega_av");
            _avExe = Path.Combine(_avDir, "OmegaAV.exe");
            _fgDir = Path.Combine(_suiteRoot, "09_omega_forge");
            _fgExe = Path.Combine(_fgDir, "OmegaForge.exe");

            Directory.CreateDirectory(_exportsDir);
            Directory.CreateDirectory(_patchesDir);

            AutoScaleMode = AutoScaleMode.None;
            Text = "ACT-Omega Lab :: OmegaSheaf v0.2.1 / v1.1 GM (06_omega_sheaf) — SDF GPU Font, Hodge Barcode, Auto-Diff, OLED Touch & CK (:19784)";
            ClientSize = new Size(1364, 824);
            MinimumSize = new Size(1200, 748);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(7, 11, 20);
            ForeColor = Color.White;
            KeyPreview = true;
            AllowDrop = true;

            try
            {
                string ico = Path.Combine(_suiteRoot, "icons", "omega_sheaf.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            BuildCockpitUi();
            SeedFactoryPatchesOnDisk();

            _http.OnRemoteCommand = delegate(string qs)
            {
                lock (_pendingRemoteCmds) { _pendingRemoteCmds.Enqueue(qs); }
            };
            _http.Start(19784);

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
            {
                HandleDroppedPath(initialFile);
            }
            else
            {
                _cmbPatchBank.SelectedIndex = 1; // 02: Cross-Paper Paradox (H^1 != 0)
                OmegaSheafIngestor.LoadPreset(_graph, 1, _suiteRoot);
                SheafE8Math.StepCpuPhysicsAndCohomology(_graph, 0.016f, 0.0f, 0.0f, _obsTolerance, false, false);
                SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                RefreshAllInspectorBindings();
            }

            Shown += delegate
            {
                timeBeginPeriod(1u);
                bool gpuOk = _gpu.Initialize(_stage.Handle, Math.Max(320, _stage.ClientSize.Width), Math.Max(240, _stage.ClientSize.Height));
                SetBotStatus(gpuOk
                    ? ("OMEGASHEAF v0.2.1 LIVE :: " + _gpu.BackendLabel + " + 2-Way OLED Touch Surgery Pad on http://127.0.0.1:19784/sheaf")
                    : ("OMEGASHEAF v0.2.1 :: Running GDI+ Fallback (" + _gpu.LastError + ")"));
                Application.Idle += OnApplicationIdle;
            };

            FormClosing += delegate
            {
                Application.Idle -= OnApplicationIdle;
                timeEndPeriod(1u);
                try { if (_avi.IsRecording) _avi.StopAndSave(); } catch { }
                try { _sonifier.Dispose(); } catch { }
                try { _http.Dispose(); } catch { }
                try { _gpu.Dispose(); } catch { }
                if (_cpuFallbackBmp != null) { _cpuFallbackBmp.Dispose(); _cpuFallbackBmp = null; }
                if (_oledThumbBmp != null) { _oledThumbBmp.Dispose(); _oledThumbBmp = null; }
            };
        }

        private void BuildCockpitUi()
        {
            // =========================================================================
            // TOP HEADER DECK (68 px — 100% 125%-DPI Clip-Free Widths!)
            // =========================================================================
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 68;
            topBar.BackColor = C(10, 15, 28);

            Label lblBrand = new Label();
            lblBrand.UseMnemonic = false;
            lblBrand.Text = "OMEGASHEAF v0.2.1 GM";
            lblBrand.SetBounds(12, 7, 198, 24);
            lblBrand.Font = new Font("Segoe UI", 11.0f, FontStyle.Bold);
            lblBrand.ForeColor = C(52, 211, 153);

            _lblTopVerdict = new Label();
            _lblTopVerdict.UseMnemonic = false;
            _lblTopVerdict.Text = "PROBING H^1 COHOMOLOGY...";
            _lblTopVerdict.SetBounds(214, 6, 266, 26);
            _lblTopVerdict.TextAlign = ContentAlignment.MiddleCenter;
            _lblTopVerdict.Font = new Font("Consolas", 8.6f, FontStyle.Bold);
            _lblTopVerdict.BackColor = C(6, 78, 59);
            _lblTopVerdict.ForeColor = Color.White;

            int bx = 488;
            _btnTopPause = MakeTopBtn("Sim: LIVE", bx, 6, 84, C(5, 150, 105)); bx += 90;
            _btnTopPause.Click += delegate { TogglePause(); };

            _btnTopRectify = MakeTopBtn("Rectify H^1->0", bx, 6, 118, C(16, 185, 129)); bx += 124;
            _btnTopRectify.Click += delegate { ExecuteRectifySurgery(); };

            Button btnTopCorpus = MakeTopBtn("Corpus .MD", bx, 6, 94, C(109, 40, 217)); bx += 100;
            btnTopCorpus.Click += delegate { RunCorpusIngest(); };

            _btnTopSynth = MakeTopBtn("Synth: OFF", bx, 6, 92, C(88, 28, 135)); bx += 98;
            _btnTopSynth.Click += delegate { ToggleSynthSonifier(); };

            _btnTopLabels = MakeTopBtn("Labels: ON", bx, 6, 90, C(3, 105, 161)); bx += 96;
            _btnTopLabels.Click += delegate
            {
                _gpu.ShowGpuLabels = !_gpu.ShowGpuLabels;
                _chkGpuLabels.Checked = _gpu.ShowGpuLabels;
                _btnTopLabels.Text = _gpu.ShowGpuLabels ? "Labels: ON" : "Labels: OFF";
            };

            _btnTopSplit = MakeTopBtn("Split: L_F", bx, 6, 86, C(14, 116, 144)); bx += 92;
            _btnTopSplit.Click += delegate
            {
                _viewMode = (_viewMode + 1) % 4;
                _cmbViewMode.SelectedIndex = _viewMode;
            };

            _btnTopRecAvi = MakeTopBtn("Rec .AVI", bx, 6, 82, C(159, 18, 57)); bx += 88;
            _btnTopRecAvi.Click += delegate { ToggleAviRecording(); };

            Button btnTopBeam = MakeTopBtn("Beam OLED", bx, 6, 94, C(67, 56, 202)); bx += 100;
            btnTopBeam.Click += delegate { BeamSheafToPixelsAsync(); };

            Button btnTopAv = MakeTopBtn("-> AV", bx, 6, 62, C(190, 24, 93)); bx += 68;
            btnTopAv.Click += delegate { SyncOmshAndFocusAv(true); };

            Button btnTopGuide = MakeTopBtn("?", bx, 6, 30, C(180, 83, 9));
            btnTopGuide.Click += delegate { using (OmegaSheafGuideDialog dlg = new OmegaSheafGuideDialog()) dlg.ShowDialog(this); };

            _lblTelemetryRow2 = new Label();
            _lblTelemetryRow2.UseMnemonic = false;
            _lblTelemetryRow2.SetBounds(12, 38, 1340, 22);
            _lblTelemetryRow2.Font = new Font("Consolas", 8.4f);
            _lblTelemetryRow2.ForeColor = C(125, 211, 252);
            _lblTelemetryRow2.Text = "RTX DirectCompute cs_5_0 SDF+Glyph VSync (60 FPS) | Stalks: 0 | Edges: 0 | Hodge: 0% Grad / 0% Curl / 0% H^1";

            topBar.Controls.AddRange(new Control[]
            {
                lblBrand, _lblTopVerdict, _btnTopPause, _btnTopRectify, btnTopCorpus,
                _btnTopSynth, _btnTopLabels, _btnTopSplit, _btnTopRecAvi, btnTopBeam, btnTopAv, btnTopGuide, _lblTelemetryRow2
            });

            // =========================================================================
            // BOTTOM GEMMA AI + STATUS BAR (62 px)
            // =========================================================================
            Panel botBar = new Panel();
            botBar.Dock = DockStyle.Bottom;
            botBar.Height = 62;
            botBar.BackColor = C(10, 15, 28);

            Label lblGm = new Label();
            lblGm.UseMnemonic = false;
            lblGm.Text = "Gemma AI:";
            lblGm.SetBounds(12, 8, 72, 24);
            lblGm.TextAlign = ContentAlignment.MiddleLeft;
            lblGm.Font = new Font("Segoe UI Semibold", 8.6f);
            lblGm.ForeColor = C(192, 132, 252);

            _txtGemmaPrompt = new TextBox();
            _txtGemmaPrompt.SetBounds(86, 8, 360, 24);
            _txtGemmaPrompt.BackColor = C(6, 10, 19);
            _txtGemmaPrompt.ForeColor = Color.White;
            _txtGemmaPrompt.BorderStyle = BorderStyle.FixedSingle;
            _txtGemmaPrompt.Font = new Font("Consolas", 9.0f);
            _txtGemmaPrompt.Text = "Build a 6-node Fallout 4 quest sheaf with a sequence-break softlock";
            _txtGemmaPrompt.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; RunGemmaSheafSynthAsync(); }
            };

            Button btnGmRun = MakeTopBtn("Synthesize Sheaf", 454, 6, 146, C(124, 58, 237));
            btnGmRun.Click += delegate { RunGemmaSheafSynthAsync(); };

            _chkSyncAvOnGemma = new CheckBox();
            _chkSyncAvOnGemma.Text = "+ Sync .omsh -> AV";
            _chkSyncAvOnGemma.SetBounds(608, 8, 144, 24);
            _chkSyncAvOnGemma.Font = new Font("Segoe UI", 8.2f);
            _chkSyncAvOnGemma.ForeColor = C(244, 114, 182);

            Label lblHints = new Label();
            lblHints.UseMnemonic = false;
            lblHints.Text = "Drag=Stalk | Wheel=Zoom | R=Rectify H^1->0 | L=GPU Labels | S=Synth | V=Rec .AVI";
            lblHints.SetBounds(758, 8, 594, 24);
            lblHints.TextAlign = ContentAlignment.MiddleLeft;
            lblHints.Font = new Font("Consolas", 8.0f);
            lblHints.ForeColor = C(148, 163, 184);

            _lblStatusBot = new Label();
            _lblStatusBot.UseMnemonic = false;
            _lblStatusBot.SetBounds(12, 36, 1340, 20);
            _lblStatusBot.Font = new Font("Consolas", 8.4f);
            _lblStatusBot.ForeColor = C(52, 211, 153);
            _lblStatusBot.Text = "Initializing RTX DirectCompute cs_5_0 SDF + Glyph Atlas Sheaf Engine...";

            botBar.Controls.AddRange(new Control[] { lblGm, _txtGemmaPrompt, btnGmRun, _chkSyncAvOnGemma, lblHints, _lblStatusBot });

            // =========================================================================
            // RIGHT CONTROL DECK (414 px — 4 Curated Tabs)
            // =========================================================================
            Panel rightDeck = new Panel();
            rightDeck.Dock = DockStyle.Right;
            rightDeck.Width = 414;
            rightDeck.BackColor = C(12, 18, 32);
            rightDeck.Padding = new Padding(8);

            Label lblBank = new Label();
            lblBank.UseMnemonic = false;
            lblBank.Text = "PATCH BANK (.osheaf — 9 Scenarios incl. CK Quest):";
            lblBank.SetBounds(8, 6, 290, 18);
            lblBank.Font = new Font("Consolas", 8.1f, FontStyle.Bold);
            lblBank.ForeColor = C(56, 189, 248);

            _cmbPatchBank = new ComboBox();
            _cmbPatchBank.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbPatchBank.SetBounds(8, 26, 280, 24);
            _cmbPatchBank.BackColor = C(15, 23, 42);
            _cmbPatchBank.ForeColor = Color.White;
            _cmbPatchBank.Font = new Font("Segoe UI", 8.3f);
            _cmbPatchBank.Items.AddRange(OmegaSheafIngestor.PresetNames);
            _cmbPatchBank.SelectedIndexChanged += delegate
            {
                if (_suppressUiEvents) return;
                int idx = _cmbPatchBank.SelectedIndex;
                if (idx >= 0)
                {
                    OmegaSheafIngestor.LoadPreset(_graph, idx, _suiteRoot);
                    _selNodeIdx = 0; _selEdgeIdx = 0;
                    SheafE8Math.StepCpuPhysicsAndCohomology(_graph, 0.016f, 0.0f, 0.0f, _obsTolerance, false, false);
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshAllInspectorBindings();
                    _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
                    SetBotStatus("Loaded Sheaf Preset: " + _graph.GraphTitle);
                }
            };

            Button btnSavePatch = MakeTopBtn("Save", 292, 24, 54, C(5, 150, 105));
            btnSavePatch.Height = 26;
            btnSavePatch.Click += delegate { SavePatchDialog(); };

            Button btnLoadPatch = MakeTopBtn("Load..", 350, 24, 56, C(3, 105, 161));
            btnLoadPatch.Height = 26;
            btnLoadPatch.Click += delegate { LoadPatchDialog(); };

            _rightTabs = new TabControl();
            _rightTabs.SetBounds(8, 56, 398, 630);
            _rightTabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _rightTabs.Font = new Font("Segoe UI Semibold", 8.1f);

            TabPage t1 = new TabPage("1. Hodge & Barcode");
            TabPage t2 = new TabPage("2. Auto-Diff & Scan");
            TabPage t3 = new TabPage("3. Surgery & Synth");
            TabPage t4 = new TabPage("4. Lean4, CK & .AVI");
            t1.BackColor = C(11, 17, 30); t1.ForeColor = Color.White;
            t2.BackColor = C(11, 17, 30); t2.ForeColor = Color.White;
            t3.BackColor = C(11, 17, 30); t3.ForeColor = Color.White;
            t4.BackColor = C(11, 17, 30); t4.ForeColor = Color.White;

            PopulateTab1(t1);
            PopulateTab2(t2);
            PopulateTab3(t3);
            PopulateTab4(t4);

            _rightTabs.TabPages.AddRange(new TabPage[] { t1, t2, t3, t4 });
            rightDeck.Controls.AddRange(new Control[] { lblBank, _cmbPatchBank, btnSavePatch, btnLoadPatch, _rightTabs });

            // =========================================================================
            // CENTER STAGE + LIVE STALK HOVER INSPECTOR BANNER
            // =========================================================================
            Panel centerWrap = new Panel();
            centerWrap.Dock = DockStyle.Fill;
            centerWrap.BackColor = C(5, 8, 16);
            centerWrap.Padding = new Padding(8, 6, 4, 6);

            _lblHoverHud = new Label();
            _lblHoverHud.UseMnemonic = false;
            _lblHoverHud.Dock = DockStyle.Top;
            _lblHoverHud.Height = 24;
            _lblHoverHud.BackColor = C(13, 21, 38);
            _lblHoverHud.ForeColor = C(250, 204, 21);
            _lblHoverHud.Font = new Font("Consolas", 8.3f, FontStyle.Bold);
            _lblHoverHud.TextAlign = ContentAlignment.MiddleLeft;
            _lblHoverHud.Text = "  STALK INSPECTOR :: Click or drag any E8 vertex rosette or restriction arrow on PC or Pixel (:19784/sheaf)!";

            _stage = new SheafStagePanel();
            _stage.Dock = DockStyle.Fill;
            _stage.BackColor = C(7, 11, 20);
            _stage.AllowDrop = true;

            _stage.Paint += delegate(object s, PaintEventArgs pe)
            {
                if (!_gpu.IsGpuActive && _cpuFallbackBmp != null)
                    pe.Graphics.DrawImageUnscaled(_cpuFallbackBmp, 0, 0);
            };

            _stage.MouseDown += OnStageMouseDown;
            _stage.MouseMove += OnStageMouseMove;
            _stage.MouseUp += OnStageMouseUp;
            _stage.MouseWheel += OnStageMouseWheel;
            MouseWheel += OnStageMouseWheel;

            DragEventHandler onDe = delegate(object s, DragEventArgs de)
            {
                de.Effect = (de.Data != null && de.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragEventHandler onDd = delegate(object s, DragEventArgs dd)
            {
                string[] fs = dd.Data.GetData(DataFormats.FileDrop) as string[];
                if (fs != null && fs.Length > 0) HandleDroppedPath(fs[0]);
            };
            DragEnter += onDe; DragDrop += onDd;
            _stage.DragEnter += onDe; _stage.DragDrop += onDd;

            centerWrap.Controls.Add(_stage);
            centerWrap.Controls.Add(_lblHoverHud);

            Controls.Add(centerWrap);
            Controls.Add(rightDeck);
            Controls.Add(botBar);
            Controls.Add(topBar);

            KeyDown += OnCockpitKeyDown;
        }

        private void PopulateTab1(TabPage tp)
        {
            int y = 8;
            AddSecHeader(tp, "1. SDF GPU VIEWPORT & HODGE SPECTRAL COLORING", ref y);

            _cmbViewMode = new ComboBox();
            _cmbViewMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbViewMode.SetBounds(10, y, 218, 24);
            _cmbViewMode.BackColor = C(15, 23, 42);
            _cmbViewMode.ForeColor = Color.White;
            _cmbViewMode.Items.AddRange(new object[]
            {
                "0: Full 2D SDF Sheaf Truth-Canvas",
                "1: 3D E8 Stalk Manifold Orbit",
                "2: Split Canvas | L_F + Barcode",
                "3: Wide Hodge Persistence & Eigs"
            });
            _cmbViewMode.SelectedIndex = 2;
            _cmbViewMode.SelectedIndexChanged += delegate
            {
                _viewMode = _cmbViewMode.SelectedIndex;
                _btnTopSplit.Text = (_viewMode == 2) ? "Split: L_F" : ((_viewMode == 3) ? "Barcode" : ((_viewMode == 1) ? "3D Orbit" : "2D Full"));
            };

            _cmbVsync = new ComboBox();
            _cmbVsync.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbVsync.SetBounds(232, y, 90, 24);
            _cmbVsync.BackColor = C(15, 23, 42);
            _cmbVsync.ForeColor = Color.White;
            _cmbVsync.Items.AddRange(new object[] { "VSync ON", "Uncapped" });
            _cmbVsync.SelectedIndex = 0;
            _cmbVsync.SelectedIndexChanged += delegate { _gpu.VSyncEnabled = (_cmbVsync.SelectedIndex == 0); };

            Button btnResetCam = MakeTopBtn("Reset", 326, y - 1, 54, C(30, 58, 138));
            btnResetCam.Height = 26;
            btnResetCam.Click += delegate { _camZoom = 1.0f; _panX = 0.0f; _panY = 0.0f; _orbitYaw = 0.42f; _orbitPitch = 0.26f; _trkZoom.Value = 100; };
            tp.Controls.AddRange(new Control[] { _cmbViewMode, _cmbVsync, btnResetCam });
            y += 28;

            _cmbHodgeColor = new ComboBox();
            _cmbHodgeColor.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbHodgeColor.SetBounds(10, y, 370, 24);
            _cmbHodgeColor.BackColor = C(15, 23, 42);
            _cmbHodgeColor.ForeColor = Color.White;
            _cmbHodgeColor.Items.AddRange(new object[]
            {
                "Edge Layer 0 :: Cohomology Truth (Emerald H^1=0 / Crimson Loop)",
                "Edge Layer 1 :: Hodge Exact Gradient Flow ||d(phi)||_2 (Cyan)",
                "Edge Layer 2 :: Hodge Co-Exact Curl Vorticity (Amber)",
                "Edge Layer 3 :: Pure Harmonic H^1 Obstruction Isolation (Magenta)"
            });
            _cmbHodgeColor.SelectedIndex = 0;
            _cmbHodgeColor.SelectedIndexChanged += delegate { _gpu.HodgeColorMode = _cmbHodgeColor.SelectedIndex; };
            tp.Controls.Add(_cmbHodgeColor);
            y += 30;

            AddSecHeader(tp, "2. FILTRATION SCRUBBER (EPS), DIFFUSION & E8 SNAP", ref y);

            _lblTolVal = AddSliderRow(tp, "Persistence Filtration Scrubber (eps): [0.14]", 3, 95, 14, ref y, out _trkObsTol);
            _trkObsTol.Scroll += delegate
            {
                _obsTolerance = _trkObsTol.Value * 0.01f;
                _lblTolVal.Text = "Persistence Filtration Scrubber (eps): [" + _obsTolerance.ToString("0.00", CultureInfo.InvariantCulture) + "]";
                SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                RefreshLoopListUi();
            };

            _lblDiffVal = AddSliderRow(tp, "Harmonic Sheaf Diffusion Rate (eta): [0.42]", 0, 100, 42, ref y, out _trkDiffusion);
            _trkDiffusion.Scroll += delegate { _diffusionRate = _trkDiffusion.Value * 0.01f; _lblDiffVal.Text = "Harmonic Sheaf Diffusion Rate (eta): [" + _diffusionRate.ToString("0.00", CultureInfo.InvariantCulture) + "]"; };

            _lblE8Val = AddSliderRow(tp, "Conway-Sloane 240-Root E8 Snap Pull: [0.28]", 0, 100, 28, ref y, out _trkE8Snap);
            _trkE8Snap.Scroll += delegate { _e8SnapStrength = _trkE8Snap.Value * 0.01f; _lblE8Val.Text = "Conway-Sloane 240-Root E8 Snap Pull: [" + _e8SnapStrength.ToString("0.00", CultureInfo.InvariantCulture) + "]"; };

            _lblZoomVal = AddSliderRow(tp, "Truth-Canvas Camera Zoom: [1.00x]", 35, 320, 100, ref y, out _trkZoom);
            _trkZoom.Scroll += delegate { _camZoom = _trkZoom.Value * 0.01f; _lblZoomVal.Text = "Truth-Canvas Camera Zoom: [" + _camZoom.ToString("0.00", CultureInfo.InvariantCulture) + "x]"; };

            _chkSpringForces = AddCheck(tp, "Run 3D Coulomb-Hooke Spring Layout", true, ref y);
            _chkSpringForces.CheckedChanged += delegate { _springForces = _chkSpringForces.Checked; };

            _chkRunDiffusion = AddCheck(tp, "Run Live Harmonic Sheaf Laplacian Flow", true, ref y);
            _chkRunDiffusion.CheckedChanged += delegate { _runDiffusion = _chkRunDiffusion.Checked; };

            _chkGpuLabels = AddCheck(tp, "Render Crisp 5x6 HLSL Bitmap Node Titles on GPU", true, ref y);
            _chkGpuLabels.CheckedChanged += delegate
            {
                _gpu.ShowGpuLabels = _chkGpuLabels.Checked;
                _btnTopLabels.Text = _gpu.ShowGpuLabels ? "Labels: ON" : "Labels: OFF";
            };

            y += 2;
            AddSecHeader(tp, "3. COHOMOLOGY H^1 VERDICT & FRUSTRATED LOOPS", ref y);

            _lblVerdictCard = new Label();
            _lblVerdictCard.UseMnemonic = false;
            _lblVerdictCard.SetBounds(10, y, 370, 34);
            _lblVerdictCard.Font = new Font("Consolas", 8.2f, FontStyle.Bold);
            _lblVerdictCard.BackColor = C(6, 78, 59);
            _lblVerdictCard.ForeColor = Color.White;
            _lblVerdictCard.TextAlign = ContentAlignment.MiddleCenter;
            _lblVerdictCard.Text = "GLOBAL CONSENSUS H^1 = 0";
            tp.Controls.Add(_lblVerdictCard);
            y += 38;

            _lstLoops = new ListBox();
            _lstLoops.SetBounds(10, y, 370, 74);
            _lstLoops.BackColor = C(7, 11, 20);
            _lstLoops.ForeColor = C(253, 164, 175);
            _lstLoops.Font = new Font("Consolas", 8.0f);
            _lstLoops.BorderStyle = BorderStyle.FixedSingle;
            _lstLoops.SelectedIndexChanged += delegate
            {
                int li = _lstLoops.SelectedIndex;
                if (li >= 0 && li < _graph.ContradictionLoops.Count)
                {
                    FrustratedCycleReport rep = _graph.ContradictionLoops[li];
                    if (rep.NodeIds.Count > 0) SelectNode(rep.NodeIds[0]);
                    if (rep.EdgeIds.Count > 0) SelectEdge(rep.EdgeIds[0]);
                }
            };
            tp.Controls.Add(_lstLoops);
            y += 78;

            Button btnRectifyBig = MakeTopBtn("1-Click Reidemeister Surgery :: Rectify All H^1 -> 0", 10, y, 370, C(16, 185, 129));
            btnRectifyBig.Height = 30;
            btnRectifyBig.Click += delegate { ExecuteRectifySurgery(); };
            tp.Controls.Add(btnRectifyBig);
        }

        private void PopulateTab2(TabPage tp)
        {
            int y = 8;
            AddSecHeader(tp, "1. LIVE SUITE, CORPUS & POLYGLOT CODEBASE SCANNERS", ref y);

            Button bSuiteOk = MakeTopBtn("1. Audit Live OmegaLabs Suite Ports & FFI (H^1=0)", 10, y, 370, C(5, 150, 105));
            bSuiteOk.Height = 29;
            bSuiteOk.Click += delegate { LoadLiveSuiteAudit(false); };
            tp.Controls.Add(bSuiteOk);
            y += 33;

            Button bSuiteBug = MakeTopBtn("2. Replay D3D11 SRV ViewDimension=4 Bug (H^1!=0)", 10, y, 370, C(190, 18, 60));
            bSuiteBug.Height = 29;
            bSuiteBug.Click += delegate { LoadLiveSuiteAudit(true); };
            tp.Controls.Add(bSuiteBug);
            y += 33;

            Button bCorpus = MakeTopBtn("3. Ingest OmegaBrowser Research Corpus (*.md)", 10, y, 182, C(109, 40, 217));
            bCorpus.Height = 29;
            bCorpus.Click += delegate { RunCorpusIngest(); };

            Button bScanFolder = MakeTopBtn("4. Scan Codebase Dir...", 198, y, 182, C(3, 105, 161));
            bScanFolder.Height = 29;
            bScanFolder.Click += delegate { RunFolderCodebaseScan(); };
            tp.Controls.AddRange(new Control[] { bCorpus, bScanFolder });
            y += 35;

            _lblProvenance = new Label();
            _lblProvenance.UseMnemonic = false;
            _lblProvenance.SetBounds(10, y, 370, 32);
            _lblProvenance.Font = new Font("Consolas", 8.0f);
            _lblProvenance.ForeColor = C(125, 211, 252);
            _lblProvenance.Text = "Ready to scan corpus papers or polyglot FFI structs.";
            tp.Controls.Add(_lblProvenance);
            y += 36;

            AddSecHeader(tp, "2. PILLAR 3 :: AUTO-DIFF & CITATION SURGEON PREVIEW", ref y);

            _txtDiffPreview = new TextBox();
            _txtDiffPreview.Multiline = true;
            _txtDiffPreview.ReadOnly = true;
            _txtDiffPreview.ScrollBars = ScrollBars.Both;
            _txtDiffPreview.WordWrap = false;
            _txtDiffPreview.SetBounds(10, y, 370, 304);
            _txtDiffPreview.BackColor = C(6, 10, 19);
            _txtDiffPreview.ForeColor = C(167, 243, 208);
            _txtDiffPreview.BorderStyle = BorderStyle.FixedSingle;
            _txtDiffPreview.Font = new Font("Consolas", 8.1f);
            tp.Controls.Add(_txtDiffPreview);
            y += 310;

            Button bRefreshDiff = MakeTopBtn("Refresh .diff", 10, y, 114, C(14, 116, 144));
            bRefreshDiff.Height = 30;
            bRefreshDiff.Click += delegate { RefreshAutoDiffBox(); };

            Button bOpenShell = MakeTopBtn("Save .patch + Open Shell Studio (:19781)", 130, y, 250, C(2, 132, 199));
            bOpenShell.Height = 30;
            bOpenShell.Click += delegate
            {
                RefreshAutoDiffBox();
                string p = OmegaSheafAutoDiffSurgeon.SavePatchAndOpenInShell(_exportsDir, _suiteRoot, _txtDiffPreview.Text);
                SetBotStatus("Saved surgical .patch & staged to OmegaShell Studio -> " + p);
            };
            tp.Controls.AddRange(new Control[] { bRefreshDiff, bOpenShell });
        }

        private void PopulateTab3(TabPage tp)
        {
            int y = 8;
            AddSecHeader(tp, "1. VERTEX STALK F(v) & 240-ROOT E8 INSPECTOR", ref y);

            _cmbSelNode = new ComboBox();
            _cmbSelNode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSelNode.SetBounds(10, y, 370, 24);
            _cmbSelNode.BackColor = C(15, 23, 42);
            _cmbSelNode.ForeColor = Color.White;
            _cmbSelNode.SelectedIndexChanged += delegate
            {
                if (!_suppressUiEvents && _cmbSelNode.SelectedIndex >= 0) SelectNode(_cmbSelNode.SelectedIndex);
            };
            tp.Controls.Add(_cmbSelNode);
            y += 27;

            _txtNodeTitle = AddLabeledBox(tp, "Title:", 10, y, 370); y += 25;
            _txtNodeSrc = AddLabeledBox(tp, "Source:", 10, y, 370); y += 25;
            _txtNodeFormula = AddLabeledBox(tp, "Claim/ABI:", 10, y, 370); y += 27;

            _chkNodePinned = new CheckBox();
            _chkNodePinned.Text = "Pin Axiom Stalk (Freeze in Diffusion)";
            _chkNodePinned.SetBounds(10, y, 240, 22);
            _chkNodePinned.ForeColor = C(250, 204, 21);
            _chkNodePinned.CheckedChanged += delegate
            {
                if (_suppressUiEvents) return;
                if (_selNodeIdx >= 0 && _selNodeIdx < _graph.Nodes.Count)
                    _graph.Nodes[_selNodeIdx].Pinned = _chkNodePinned.Checked;
            };

            Button btnApplyNode = MakeTopBtn("Apply", 254, y - 2, 60, C(3, 105, 161));
            btnApplyNode.Height = 25;
            btnApplyNode.Click += delegate
            {
                if (_selNodeIdx >= 0 && _selNodeIdx < _graph.Nodes.Count)
                {
                    SheafStalkNode nd = _graph.Nodes[_selNodeIdx];
                    nd.Title = _txtNodeTitle.Text.Trim();
                    nd.SourceDoc = _txtNodeSrc.Text.Trim();
                    nd.FormulaOrType = _txtNodeFormula.Text.Trim();
                    RefreshAllInspectorBindings();
                }
            };

            Button btnAddNode = MakeTopBtn("+Node", 318, y - 2, 62, C(5, 150, 105));
            btnAddNode.Height = 25;
            btnAddNode.Click += delegate
            {
                float[] s8 = new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
                int id = OmegaSheafIngestor.AddNode(_graph, "Axiom #" + _graph.Nodes.Count, "User", "E8 Stalk", StalkCategory.TheoremProof, 0.0f, 0.0f, 0.2f, false, s8);
                if (_selNodeIdx >= 0 && _selNodeIdx < id)
                    OmegaSheafIngestor.AddEdge(_graph, _selNodeIdx, id, "Restriction Link", 1.0f, 0.0f, 0.0f);
                SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                _selNodeIdx = id;
                RefreshAllInspectorBindings();
            };
            tp.Controls.AddRange(new Control[] { _chkNodePinned, btnApplyNode, btnAddNode });
            y += 26;

            _lblNodeE8Stats = new Label();
            _lblNodeE8Stats.UseMnemonic = false;
            _lblNodeE8Stats.SetBounds(10, y, 370, 22);
            _lblNodeE8Stats.Font = new Font("Consolas", 8.0f);
            _lblNodeE8Stats.ForeColor = C(52, 211, 153);
            _lblNodeE8Stats.Text = "E8 Root #0 | SQNR: 24.0 dB | Tension: 0.000";
            tp.Controls.Add(_lblNodeE8Stats);
            y += 26;

            AddSecHeader(tp, "2. RESTRICTION EDGE SURGERY & POLARITY", ref y);

            _cmbSelEdge = new ComboBox();
            _cmbSelEdge.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSelEdge.SetBounds(10, y, 370, 24);
            _cmbSelEdge.BackColor = C(15, 23, 42);
            _cmbSelEdge.ForeColor = Color.White;
            _cmbSelEdge.SelectedIndexChanged += delegate
            {
                if (!_suppressUiEvents && _cmbSelEdge.SelectedIndex >= 0) SelectEdge(_cmbSelEdge.SelectedIndex);
            };
            tp.Controls.Add(_cmbSelEdge);
            y += 28;

            _cmbEdgePolarity = new ComboBox();
            _cmbEdgePolarity.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbEdgePolarity.SetBounds(10, y, 370, 24);
            _cmbEdgePolarity.BackColor = C(15, 23, 42);
            _cmbEdgePolarity.ForeColor = Color.White;
            _cmbEdgePolarity.Items.AddRange(new object[]
            {
                "+1.0 :: Consistent Orientation (Commuting Restriction)",
                "-1.0 :: Inverted Sign Paradox (Creates H^1 != 0 Loop!)"
            });
            _cmbEdgePolarity.SelectedIndex = 0;
            _cmbEdgePolarity.SelectedIndexChanged += delegate
            {
                if (_suppressUiEvents) return;
                if (_selEdgeIdx >= 0 && _selEdgeIdx < _graph.Edges.Count)
                {
                    _graph.Edges[_selEdgeIdx].Polarity = (_cmbEdgePolarity.SelectedIndex == 0) ? 1.0f : -1.0f;
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshLoopListUi();
                    RefreshAutoDiffBox();
                }
            };
            tp.Controls.Add(_cmbEdgePolarity);
            y += 28;

            _lblEdgeTwistVal = AddSliderRow(tp, "SO(2)^4 Givens Phase Twist Angle: [0 deg]", -180, 180, 0, ref y, out _trkEdgeTwist);
            _trkEdgeTwist.Scroll += delegate
            {
                if (_selEdgeIdx >= 0 && _selEdgeIdx < _graph.Edges.Count)
                {
                    float deg = _trkEdgeTwist.Value;
                    _graph.Edges[_selEdgeIdx].TwistRad = deg * (float)(Math.PI / 180.0);
                    _lblEdgeTwistVal.Text = "SO(2)^4 Givens Phase Twist Angle: [" + deg.ToString("0") + " deg]";
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshLoopListUi();
                }
            };

            _lblEdgeAbiVal = AddSliderRow(tp, "FFI / ABI Struct Byte-Drift Bias: [0.00]", 0, 100, 0, ref y, out _trkEdgeAbi);
            _trkEdgeAbi.Scroll += delegate
            {
                if (_selEdgeIdx >= 0 && _selEdgeIdx < _graph.Edges.Count)
                {
                    float ab = _trkEdgeAbi.Value * 0.01f;
                    _graph.Edges[_selEdgeIdx].AbiMismatchBias = ab;
                    _lblEdgeAbiVal.Text = "FFI / ABI Struct Byte-Drift Bias: [" + ab.ToString("0.00", CultureInfo.InvariantCulture) + "]";
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshLoopListUi();
                    RefreshAutoDiffBox();
                }
            };

            _lblEdgeResidual = new Label();
            _lblEdgeResidual.UseMnemonic = false;
            _lblEdgeResidual.SetBounds(10, y, 370, 20);
            _lblEdgeResidual.Font = new Font("Consolas", 8.1f, FontStyle.Bold);
            _lblEdgeResidual.ForeColor = C(56, 189, 248);
            _lblEdgeResidual.Text = "Residual = 0.0000 | Grad=0.00 Curl=0.00 H^1=0.00";
            tp.Controls.Add(_lblEdgeResidual);
            y += 24;

            Button btnFlipEdge = MakeTopBtn("Inject Sign Paradox (-1)", 10, y, 180, C(190, 18, 60));
            btnFlipEdge.Height = 28;
            btnFlipEdge.Click += delegate
            {
                if (_selEdgeIdx >= 0 && _selEdgeIdx < _graph.Edges.Count)
                {
                    _graph.Edges[_selEdgeIdx].Polarity *= -1.0f;
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    SelectEdge(_selEdgeIdx);
                    RefreshLoopListUi();
                    _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
                }
            };

            Button btnHealEdge = MakeTopBtn("Heal Selected Edge", 198, y, 182, C(16, 185, 129));
            btnHealEdge.Height = 28;
            btnHealEdge.Click += delegate
            {
                if (_selEdgeIdx >= 0 && _selEdgeIdx < _graph.Edges.Count)
                {
                    SheafRestrictionEdge ed = _graph.Edges[_selEdgeIdx];
                    ed.Polarity = 1.0f; ed.TwistRad = 0.0f; ed.AbiMismatchBias = 0.0f; ed.ScaleRatio = 1.0f;
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    SelectEdge(_selEdgeIdx);
                    RefreshLoopListUi();
                    _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
                }
            };
            tp.Controls.AddRange(new Control[] { btnFlipEdge, btnHealEdge });
            y += 34;

            AddSecHeader(tp, "3. PILLAR 4 :: OMEGASYNTH (:19782) & PIXEL TOUCH", ref y);

            Button bToggleSynth = MakeTopBtn("Toggle OmegaSynth (:19782) H^1 Audio Pipe", 10, y, 370, C(124, 58, 237));
            bToggleSynth.Height = 28;
            bToggleSynth.Click += delegate { ToggleSynthSonifier(); };
            tp.Controls.Add(bToggleSynth);
            y += 32;

            _lblTouchSynthStats = new Label();
            _lblTouchSynthStats.UseMnemonic = false;
            _lblTouchSynthStats.SetBounds(10, y, 370, 34);
            _lblTouchSynthStats.Font = new Font("Consolas", 8.0f);
            _lblTouchSynthStats.ForeColor = C(192, 132, 252);
            _lblTouchSynthStats.Text = "Synth Pipe: OFF | Pixel Touch (:19784/sheaf): 0 pkts";
            tp.Controls.Add(_lblTouchSynthStats);
        }

        private void PopulateTab4(TabPage tp)
        {
            int y = 10;
            AddSecHeader(tp, "1. FORMAL LEAN 4 COHOMOLOGY WITNESS & FORGE", ref y);

            Button bLean = MakeTopBtn("Export Formal Lean 4 (.lean)", 10, y, 182, C(109, 40, 217));
            bLean.Height = 31;
            bLean.Click += delegate { ExportLeanWitness(false); };

            Button bLeanForge = MakeTopBtn("Export + Verify in Forge", 198, y, 182, C(5, 150, 105));
            bLeanForge.Height = 31;
            bLeanForge.Click += delegate { ExportLeanWitness(true); };
            tp.Controls.AddRange(new Control[] { bLean, bLeanForge });
            y += 38;

            AddSecHeader(tp, "2. PILLAR 5 :: CREATION KIT QUEST PACK & 24-FPS .AVI", ref y);

            Button bCkExport = MakeTopBtn("Export Skyrim SE + Fallout 4 Quest-Sheaf (.psc + .rs)", 10, y, 370, C(180, 83, 9));
            bCkExport.Height = 32;
            bCkExport.Click += delegate
            {
                string dir = OmegaSheafCreationKitExporter.ExportCreationKitQuestPack(_exportsDir, _graph);
                AppendExportLog("Exported Creation Kit Quest-Sheaf Pack -> " + dir);
                SetBotStatus("Exported Paradox-Free Creation Kit .psc + Rust L0 Pack -> " + Path.GetFileName(dir));
            };
            tp.Controls.Add(bCkExport);
            y += 38;

            _btnTabAvi = MakeTopBtn("Start 24-FPS Motion-JPEG .AVI Surgery Recording", 10, y, 370, C(159, 18, 57));
            _btnTabAvi.Height = 31;
            _btnTabAvi.Click += delegate { ToggleAviRecording(); };
            tp.Controls.Add(_btnTabAvi);
            y += 38;

            AddSecHeader(tp, "3. VECTOR .SVG, SHA-256 .JSONL, .PNG & BRIDGES", ref y);

            Button bSvg = MakeTopBtn("Vector .SVG", 10, y, 118, C(3, 105, 161));
            bSvg.Height = 30;
            bSvg.Click += delegate
            {
                _gpu.PackAndUploadSheaf(_graph, Math.Max(640, _stage.Width), Math.Max(420, _stage.Height), _viewMode, _camZoom, _panX, _panY, _orbitYaw, _orbitPitch, _selNodeIdx, _selEdgeIdx);
                string p = OmegaSheafExporters.ExportVectorSvg(_exportsDir, _graph, _gpu.ScreenNodeX, _gpu.ScreenNodeY);
                AppendExportLog("Saved Vector Blueprint -> " + p);
            };

            Button bJsonl = MakeTopBtn("SHA-256 .JSONL", 135, y, 120, C(14, 116, 144));
            bJsonl.Height = 30;
            bJsonl.Click += delegate
            {
                string p = OmegaSheafExporters.ExportJsonlAuditLedger(_exportsDir, _graph);
                AppendExportLog("Saved SHA-256 Hodge Ledger -> " + p);
            };

            Button bPng = MakeTopBtn("Save .PNG", 262, y, 118, C(15, 118, 110));
            bPng.Height = 30;
            bPng.Click += delegate { SaveSnapshotPng(); };
            tp.Controls.AddRange(new Control[] { bSvg, bJsonl, bPng });
            y += 36;

            Button bOmsh = MakeTopBtn("Sync .OMSH -> OmegaAV", 10, y, 182, C(190, 24, 93));
            bOmsh.Height = 30;
            bOmsh.Click += delegate { SyncOmshAndFocusAv(true); };

            Button bBeam = MakeTopBtn("Beam 2-Way OLED (:19784)", 198, y, 182, C(67, 56, 202));
            bBeam.Height = 30;
            bBeam.Click += delegate { BeamSheafToPixelsAsync(); };
            tp.Controls.AddRange(new Control[] { bOmsh, bBeam });
            y += 36;

            Button bSealSec = MakeTopBtn("Seal H^1 -> OmegaSec (:19786)", 10, y, 188, C(5, 150, 105));
            bSealSec.Height = 28;
            bSealSec.Click += delegate
            {
                string msg = OmegaSheafCreationKitExporter.SealSheafGraphViaOmegaSec(_graph, _exportsDir);
                AppendExportLog(msg);
                SetBotStatus(msg);
            };

            Button bOpenExp = MakeTopBtn("Open exports Folder", 204, y, 176, C(30, 58, 138));
            bOpenExp.Height = 28;
            bOpenExp.Click += delegate { Process.Start("explorer.exe", "\"" + _exportsDir + "\""); };
            tp.Controls.AddRange(new Control[] { bSealSec, bOpenExp });
            y += 34;

            _txtExportLog = new TextBox();
            _txtExportLog.Multiline = true;
            _txtExportLog.ReadOnly = true;
            _txtExportLog.ScrollBars = ScrollBars.Vertical;
            _txtExportLog.SetBounds(10, y, 370, 168);
            _txtExportLog.BackColor = C(6, 10, 19);
            _txtExportLog.ForeColor = C(167, 243, 208);
            _txtExportLog.BorderStyle = BorderStyle.FixedSingle;
            _txtExportLog.Font = new Font("Consolas", 8.0f);
            _txtExportLog.Text = "OmegaSheaf v0.2.1 All 5 Pillars Ready.\r\n2-Way Pixel OLED Pad: http://127.0.0.1:19784/sheaf\r\n";
            tp.Controls.Add(_txtExportLog);
        }

        // =========================================================================
        // 60 FPS VSYNC APPLICATION.IDLE RENDER, HODGE, SONIFIER & .AVI LOOP
        // =========================================================================
        private static bool IsAppStillIdle()
        {
            NATIVE_MSG msg;
            return !PeekMessageW(out msg, IntPtr.Zero, 0u, 0u, 0u);
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            while (Visible && WindowState != FormWindowState.Minimized && IsAppStillIdle())
            {
                StepAndRenderFrame();
            }
        }

        private void StepAndRenderFrame()
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.045, Math.Max(0.001, now - _lastFrameSec));
            _lastFrameSec = now;

            DrainRemoteCommands();
            if (now - _lastInboxSec > 1.2)
            {
                _lastInboxSec = now;
                PollInboxCmd();
            }

            if (!_paused)
            {
                SheafE8Math.StepCpuPhysicsAndCohomology(
                    _graph, dt, _diffusionRate, _e8SnapStrength, _obsTolerance,
                    _springForces && (_dragNodeIdx < 0), _runDiffusion);
            }

            int w = Math.Max(320, _stage.ClientSize.Width);
            int h = Math.Max(240, _stage.ClientSize.Height);

            bool renderedGpu = false;
            if (_gpu.IsGpuActive)
            {
                renderedGpu = _gpu.RenderGpuFrame(
                    _graph, w, h, _viewMode, (float)now, _obsTolerance,
                    _camZoom, _panX, _panY, _orbitYaw, _orbitPitch,
                    _selNodeIdx, _selEdgeIdx, null);
            }

            if (!renderedGpu)
            {
                if (_cpuFallbackBmp == null || _cpuFallbackBmp.Width != w || _cpuFallbackBmp.Height != h)
                {
                    if (_cpuFallbackBmp != null) _cpuFallbackBmp.Dispose();
                    _cpuFallbackBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                }
                _gpu.RenderCpuFallback(_cpuFallbackBmp, _graph, _viewMode, (float)now, _camZoom, _panX, _panY, _orbitYaw, _orbitPitch, _selNodeIdx, _selEdgeIdx);
                _stage.Invalidate();
            }

            if (_avi.IsRecording)
                _avi.CaptureFrameIfDue(_stage, _clock.ElapsedMilliseconds);

            _sonifier.TickSonification(_graph, now, false);

            _fpsFrames++;
            if (now - _fpsAccumSec >= 0.45)
            {
                _liveFps = _fpsFrames / Math.Max(0.001, now - _fpsAccumSec);
                _fpsFrames = 0;
                _fpsAccumSec = now;

                SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                UpdateLiveTelemetryBanner();
            }

            if (now - _lastOledSec >= 0.16)
            {
                _lastOledSec = now;
                UpdateHttpOledStream((float)now);
            }
        }

        private void UpdateLiveTelemetryBanner()
        {
            bool h1z = _graph.IsGlobalConsensusH1Zero;
            _lblTopVerdict.BackColor = h1z ? C(6, 95, 70) : C(159, 18, 57);
            _lblTopVerdict.Text = h1z
                ? ("CONSENSUS H^1 = 0 (" + _graph.ConsistentEdgeCount + "/" + _graph.Edges.Count + " OK)")
                : ("PARADOX H^1 != 0 (" + _graph.ContradictionLoops.Count + " LOOPS | " + _graph.ObstructedEdgeCount + " BAD)");

            _lblVerdictCard.BackColor = h1z ? C(6, 78, 59) : C(127, 29, 29);
            _lblVerdictCard.Text = h1z
                ? ("GLOBAL SECTION VERIFIED :: H^1 = 0 | E_F = " + _graph.TotalDirichletEnergy.ToString("0.0000", CultureInfo.InvariantCulture))
                : ("COHOMOLOGY OBSTRUCTION :: " + _graph.ContradictionLoops.Count + " Frustrated Cycle(s) | E_F = " + _graph.TotalDirichletEnergy.ToString("0.000", CultureInfo.InvariantCulture));

            string aviTag = _avi.IsRecording ? (" | REC .AVI [" + _avi.FrameCount + "f]") : "";
            string modeTag = _gpu.IsGpuActive ? (_gpu.BackendLabel + " (" + _liveFps.ToString("0.0", CultureInfo.InvariantCulture) + " FPS)") : ("CPU GDI+ (" + _liveFps.ToString("0.0", CultureInfo.InvariantCulture) + " FPS)");
            _lblTelemetryRow2.Text =
                modeTag + " | Stalks:" + _graph.Nodes.Count + " Edges:" + _graph.Edges.Count +
                " | Hodge[Grad:" + _graph.ExactGradEnergyPct.ToString("0", CultureInfo.InvariantCulture) + "% Curl:" + _graph.CurlCycleEnergyPct.ToString("0", CultureInfo.InvariantCulture) + "% H^1:" + _graph.HarmonicH1EnergyPct.ToString("0", CultureInfo.InvariantCulture) + "%]" +
                " | l_min:" + _graph.SmallestLaplacianEig.ToString("0.00", CultureInfo.InvariantCulture) + aviTag;

            if (_selNodeIdx >= 0 && _selNodeIdx < _graph.Nodes.Count)
            {
                SheafStalkNode nd = _graph.Nodes[_selNodeIdx];
                _lblNodeE8Stats.Text = "E8 Root #" + nd.E8RootIndex + " | SQNR: " + nd.SqnrDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB | Tension: " + nd.NodeTension.ToString("0.000", CultureInfo.InvariantCulture) + (nd.InContradictionLoop ? " [H^1 LOOP]" : " [OK]");
                _lblNodeE8Stats.ForeColor = nd.InContradictionLoop ? C(251, 113, 133) : C(52, 211, 153);
            }
            if (_selEdgeIdx >= 0 && _selEdgeIdx < _graph.Edges.Count)
            {
                SheafRestrictionEdge ed = _graph.Edges[_selEdgeIdx];
                _lblEdgeResidual.Text = "Res=" + ed.ResidualNorm.ToString("0.000", CultureInfo.InvariantCulture) + " | Grad=" + ed.ExactGradNorm.ToString("0.00", CultureInfo.InvariantCulture) + " Curl=" + ed.CurlVorticity.ToString("0.00", CultureInfo.InvariantCulture) + " H^1=" + ed.HarmonicObsNorm.ToString("0.00", CultureInfo.InvariantCulture);
                _lblEdgeResidual.ForeColor = ed.IsObstructed ? C(251, 113, 133) : C(52, 211, 153);
            }
            _lblTouchSynthStats.Text = _sonifier.LastSynthStatus + "\r\nPixel 2-Way Touch (:19784/sheaf): " + _http.TouchPacketsCount + " touch events";
        }

        private void UpdateHttpOledStream(float simTime)
        {
            try
            {
                StringBuilder gj = new StringBuilder(4096);
                gj.Append("{\"h1_zero\":").Append(_graph.IsGlobalConsensusH1Zero ? "true" : "false")
                  .Append(",\"loops\":").Append(_graph.ContradictionLoops.Count)
                  .Append(",\"energy\":\"").Append(_graph.TotalDirichletEnergy.ToString("0.0000", CultureInfo.InvariantCulture)).Append("\"")
                  .Append(",\"fps\":\"").Append(_liveFps.ToString("0.0", CultureInfo.InvariantCulture)).Append("\",\"nodes\":[");

                for (int i = 0; i < _graph.Nodes.Count; i++)
                {
                    if (i > 0) gj.Append(',');
                    SheafStalkNode nd = _graph.Nodes[i];
                    float nx = Math.Max(0.06f, Math.Min(0.94f, 0.50f + nd.X / 5.4f));
                    float ny = Math.Max(0.08f, Math.Min(0.92f, 0.50f + nd.Y / 4.0f));
                    string shortT = nd.Title.Length > 15 ? nd.Title.Substring(0, 15) : nd.Title;
                    gj.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"nx\":{0:0.000},\"ny\":{1:0.000},\"t\":\"{2}\",\"r\":{3},\"loop\":{4},\"pin\":{5}}}",
                        nx, ny, OmegaSheafExporters.JsonEsc(shortT), nd.E8RootIndex,
                        nd.InContradictionLoop ? "true" : "false", nd.Pinned ? "true" : "false");
                }
                gj.Append("],\"edges\":[");
                for (int e = 0; e < _graph.Edges.Count; e++)
                {
                    if (e > 0) gj.Append(',');
                    SheafRestrictionEdge ed = _graph.Edges[e];
                    gj.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"u\":{0},\"v\":{1},\"loop\":{2},\"obs\":{3}}}",
                        ed.SrcIdx, ed.DstIdx, ed.InContradictionLoop ? "true" : "false", ed.IsObstructed ? "true" : "false");
                }
                gj.Append("]}");
                _http.LatestGraphJson = gj.ToString();

                _http.LatestStatusJson =
                    "{\"ok\":true,\"port\":19784,\"module\":\"OmegaSheaf v0.2.1 GM\",\"title\":\"" + OmegaSheafExporters.JsonEsc(_graph.GraphTitle) + "\"," +
                    "\"nodes\":" + _graph.Nodes.Count + ",\"edges\":" + _graph.Edges.Count + "," +
                    "\"h1_zero\":" + (_graph.IsGlobalConsensusH1Zero ? "true" : "false") + "," +
                    "\"betti_h0\":" + _graph.BettiH0 + ",\"betti_h1\":" + _graph.BettiH1 + "," +
                    "\"loops\":" + _graph.ContradictionLoops.Count + "," +
                    "\"energy\":\"" + _graph.TotalDirichletEnergy.ToString("0.0000", CultureInfo.InvariantCulture) + "\"," +
                    "\"fps\":\"" + _liveFps.ToString("0.0", CultureInfo.InvariantCulture) + "\"}";
            }
            catch { }
        }

        // =========================================================================
        // INTERACTIVE STAGE PICKING, DRAGGING & 2-WAY PIXEL TOUCH HANDLERS
        // =========================================================================
        private int HitTestNode(int mx, int my)
        {
            int n = Math.Min(_graph.Nodes.Count, OmegaSheafGpuEngine.MaxGpuNodes);
            int best = -1;
            float bestD2 = 1e9f;
            for (int i = 0; i < n; i++)
            {
                float dx = mx - _gpu.ScreenNodeX[i];
                float dy = my - _gpu.ScreenNodeY[i];
                float r = Math.Max(16.0f, _gpu.ScreenNodeR[i] * 1.45f);
                float d2 = dx * dx + dy * dy;
                if (d2 <= r * r && d2 < bestD2) { bestD2 = d2; best = i; }
            }
            return best;
        }

        private int HitTestEdge(int mx, int my)
        {
            int m = Math.Min(_graph.Edges.Count, OmegaSheafGpuEngine.MaxGpuEdges);
            int best = -1;
            float bestD = 10.0f;
            for (int e = 0; e < m; e++)
            {
                SheafRestrictionEdge ed = _graph.Edges[e];
                if (ed.SrcIdx < 0 || ed.SrcIdx >= _graph.Nodes.Count || ed.DstIdx < 0 || ed.DstIdx >= _graph.Nodes.Count) continue;
                float ax = _gpu.ScreenNodeX[ed.SrcIdx], ay = _gpu.ScreenNodeY[ed.SrcIdx];
                float bx = _gpu.ScreenNodeX[ed.DstIdx], by = _gpu.ScreenNodeY[ed.DstIdx];
                float abx = bx - ax, aby = by - ay;
                float len2 = abx * abx + aby * aby + 1e-4f;
                float h = Math.Max(0.08f, Math.Min(0.92f, ((mx - ax) * abx + (my - ay) * aby) / len2));
                float px = ax + abx * h, py = ay + aby * h;
                float d = (float)Math.Sqrt((mx - px) * (mx - px) + (my - py) * (my - py));
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private void OnStageMouseDown(object sender, MouseEventArgs e)
        {
            _stage.Focus();
            _lastMousePt = e.Location;
            if (e.Button == MouseButtons.Right) { _isRightPanning = true; return; }
            if (e.Button == MouseButtons.Left)
            {
                int hitN = HitTestNode(e.X, e.Y);
                if (hitN >= 0) { SelectNode(hitN); _dragNodeIdx = hitN; return; }
                int hitE = HitTestEdge(e.X, e.Y);
                if (hitE >= 0) { SelectEdge(hitE); return; }
                if (_viewMode == 1) _isLeftOrbiting = true;
            }
        }

        private void OnStageMouseMove(object sender, MouseEventArgs e)
        {
            int dx = e.X - _lastMousePt.X;
            int dy = e.Y - _lastMousePt.Y;
            _lastMousePt = e.Location;

            if (_isRightPanning) { _panX += dx; _panY += dy; return; }
            if (_dragNodeIdx >= 0 && _dragNodeIdx < _graph.Nodes.Count)
            {
                float stageW = (_viewMode == 2) ? (_stage.ClientSize.Width * 0.62f) : ((_viewMode == 3) ? (_stage.ClientSize.Width * 0.42f) : _stage.ClientSize.Width);
                float scale = Math.Max(24.0f, Math.Min(stageW, _stage.ClientSize.Height) * 0.20f * _camZoom);
                SheafStalkNode nd = _graph.Nodes[_dragNodeIdx];
                nd.X = Math.Max(-3.5f, Math.Min(3.5f, nd.X + dx / scale));
                nd.Y = Math.Max(-2.5f, Math.Min(2.5f, nd.Y + dy / scale));
                nd.Vx = 0.0f; nd.Vy = 0.0f;
                return;
            }
            if (_isLeftOrbiting)
            {
                _orbitYaw += dx * 0.008f;
                _orbitPitch = Math.Max(-1.25f, Math.Min(1.25f, _orbitPitch + dy * 0.008f));
                return;
            }

            _hoverNodeIdx = HitTestNode(e.X, e.Y);
            if (_hoverNodeIdx >= 0 && _hoverNodeIdx < _graph.Nodes.Count)
            {
                SheafStalkNode nd = _graph.Nodes[_hoverNodeIdx];
                _lblHoverHud.ForeColor = nd.InContradictionLoop ? C(251, 113, 133) : C(52, 211, 153);
                _lblHoverHud.Text = "  STALK [" + nd.Id + "] " + nd.Title + " (" + nd.SourceDoc + ") :: " + nd.FormulaOrType + " | E8 Root #" + nd.E8RootIndex + " (" + nd.SqnrDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB)";
                return;
            }
            _hoverEdgeIdx = HitTestEdge(e.X, e.Y);
            if (_hoverEdgeIdx >= 0 && _hoverEdgeIdx < _graph.Edges.Count)
            {
                SheafRestrictionEdge ed = _graph.Edges[_hoverEdgeIdx];
                _lblHoverHud.ForeColor = ed.InContradictionLoop ? C(251, 113, 133) : C(125, 211, 252);
                _lblHoverHud.Text = "  RESTRICTION [" + ed.SrcIdx + "->" + ed.DstIdx + "] " + ed.Label + " :: Polarity=" + ed.Polarity.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " | H^1=" + ed.HarmonicObsNorm.ToString("0.000", CultureInfo.InvariantCulture);
            }
        }

        private void OnStageMouseUp(object sender, MouseEventArgs e)
        {
            _dragNodeIdx = -1;
            _isRightPanning = false;
            _isLeftOrbiting = false;
        }

        private void OnStageMouseWheel(object sender, MouseEventArgs e)
        {
            if (!_stage.ClientRectangle.Contains(_stage.PointToClient(Cursor.Position))) return;
            float factor = (e.Delta > 0) ? 1.10f : 0.91f;
            _camZoom = Math.Max(0.35f, Math.Min(3.20f, _camZoom * factor));
            _trkZoom.Value = Math.Max(35, Math.Min(320, (int)Math.Round(_camZoom * 100.0f)));
            _lblZoomVal.Text = "Truth-Canvas Camera Zoom: [" + _camZoom.ToString("0.00", CultureInfo.InvariantCulture) + "x]";
        }

        private void OnCockpitKeyDown(object sender, KeyEventArgs e)
        {
            if (ActiveControl is TextBox) return;
            if (e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; TogglePause(); }
            else if (e.KeyCode == Keys.R) { e.SuppressKeyPress = true; ExecuteRectifySurgery(); }
            else if (e.KeyCode == Keys.L)
            {
                e.SuppressKeyPress = true;
                _gpu.ShowGpuLabels = !_gpu.ShowGpuLabels;
                _chkGpuLabels.Checked = _gpu.ShowGpuLabels;
                _btnTopLabels.Text = _gpu.ShowGpuLabels ? "Labels: ON" : "Labels: OFF";
            }
            else if (e.KeyCode == Keys.S) { e.SuppressKeyPress = true; ToggleSynthSonifier(); }
            else if (e.KeyCode == Keys.V) { e.SuppressKeyPress = true; ToggleAviRecording(); }
            else if (e.KeyCode == Keys.F1) { using (OmegaSheafGuideDialog dlg = new OmegaSheafGuideDialog()) dlg.ShowDialog(this); }
            else if (e.KeyCode == Keys.F9) { e.SuppressKeyPress = true; using (OmegaSheafPillar6SurgeonDialog dlg = new OmegaSheafPillar6SurgeonDialog()) dlg.ShowDialog(this); }
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _btnTopPause.Text = _paused ? "Sim: PAUSED" : "Sim: LIVE";
            _btnTopPause.BackColor = _paused ? C(180, 83, 9) : C(5, 150, 105);
        }

        private void ToggleSynthSonifier()
        {
            _sonifier.Enabled = !_sonifier.Enabled;
            _btnTopSynth.Text = _sonifier.Enabled ? "Synth: LIVE" : "Synth: OFF";
            _btnTopSynth.BackColor = _sonifier.Enabled ? C(190, 24, 93) : C(88, 28, 135);
            if (_sonifier.Enabled)
            {
                _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
                SetBotStatus("OmegaSynth (:19782) Cohomology Sonification LIVE — H^1 dissonance resolves into E8 chords on Rectify!");
            }
            else
            {
                _sonifier.SilenceVoicesAsync();
                _sonifier.LastSynthStatus = "Synth Pipe: OFF";
                SetBotStatus("OmegaSynth (:19782) Sonification muted.");
            }
        }

        private void ToggleAviRecording()
        {
            if (!_avi.IsRecording)
            {
                _avi.Start(_exportsDir, Math.Max(320, _stage.ClientSize.Width), Math.Max(240, _stage.ClientSize.Height), 24);
                _btnTopRecAvi.Text = "STOP .AVI";
                _btnTopRecAvi.BackColor = C(220, 38, 38);
                _btnTabAvi.Text = "STOP & SAVE 24-FPS .AVI RECORDING";
                SetBotStatus("Recording 24-FPS Motion-JPEG .AVI surgery proof — drag nodes or click Rectify H^1->0, then click STOP .AVI!");
            }
            else
            {
                string saved = _avi.StopAndSave();
                _btnTopRecAvi.Text = "Rec .AVI";
                _btnTopRecAvi.BackColor = C(159, 18, 57);
                _btnTabAvi.Text = "Start 24-FPS Motion-JPEG .AVI Surgery Recording";
                if (!string.IsNullOrEmpty(saved))
                {
                    AppendExportLog("Saved 24-FPS Motion-JPEG .AVI -> " + saved);
                    SetBotStatus("Saved 24-FPS .AVI Proof Video -> " + saved);
                }
            }
        }

        private void ExecuteRectifySurgery()
        {
            int healed = SheafE8Math.RectifyAllObstructions(_graph);
            SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
            RefreshAllInspectorBindings();
            UpdateLiveTelemetryBanner();
            _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
            SetBotStatus("Reidemeister Cohomology Surgery Complete :: Healed " + healed + " obstructed restriction map(s) -> Global Consensus H^1 = 0!");
        }

        private void LoadLiveSuiteAudit(bool injectBug)
        {
            OmegaSheafIngestor.BuildLiveSuiteFfiAudit(_graph, _suiteRoot, injectBug);
            _selNodeIdx = 0; _selEdgeIdx = 0;
            SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
            RefreshAllInspectorBindings();
            _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
            SetBotStatus(_graph.GraphTitle);
        }

        private void RunCorpusIngest()
        {
            string msg = OmegaSheafIngestor.IngestOmegaBrowserCorpus(_graph, _suiteRoot);
            _selNodeIdx = 0; _selEdgeIdx = 0;
            SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
            RefreshAllInspectorBindings();
            _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
            SetBotStatus(msg);
        }

        private void RunFolderCodebaseScan()
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select any Rust / C++23 / C# / TypeScript codebase folder to extract FFI structs & contracts into a Cellular Sheaf";
                fbd.SelectedPath = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'))) ? (Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')) : _suiteRoot;
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    string msg = OmegaSheafIngestor.ScanFolderCodebaseSheaf(_graph, fbd.SelectedPath);
                    _selNodeIdx = 0; _selEdgeIdx = 0;
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshAllInspectorBindings();
                    SetBotStatus(msg);
                }
            }
        }

        private void ExportLeanWitness(bool verifyInForge)
        {
            SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
            string leanPath = OmegaSheafExporters.ExportLean4CohomologyWitness(_exportsDir, _graph);
            AppendExportLog("Exported Lean 4 Cohomology Witness -> " + leanPath);
            SetBotStatus("Exported formal Lean 4 witness: " + Path.GetFileName(leanPath));

            if (verifyInForge)
            {
                try
                {
                    string fgWs = Path.Combine(_fgDir, "workspace");
                    Directory.CreateDirectory(fgWs);
                    string staged = Path.Combine(fgWs, Path.GetFileName(leanPath));
                    File.Copy(leanPath, staged, true);
                    AppendExportLog("Staged into OmegaForge workspace -> " + staged);
                    if (File.Exists(_fgExe) && Process.GetProcessesByName("OmegaForge").Length == 0)
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(_fgExe);
                        psi.WorkingDirectory = _fgDir;
                        Process.Start(psi);
                    }
                }
                catch (Exception ex) { AppendExportLog("Forge handoff note: " + ex.Message); }
            }
        }

        private void SyncOmshAndFocusAv(bool launchAv)
        {
            string omshPath = OmegaSheafExporters.ExportOmshToOmegaAv(_exportsDir, _avDir, _graph);
            AppendExportLog("Synced Sheaf Cohomology .OMSH shader -> " + omshPath);
            SetBotStatus("Beamed Sheaf Cohomology .OMSH shader -> OmegaAV v0.7.1 (:19779)!");
            if (launchAv && File.Exists(_avExe) && Process.GetProcessesByName("OmegaAV").Length == 0)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(_avExe);
                    psi.WorkingDirectory = _avDir;
                    Process.Start(psi);
                }
                catch { }
            }
        }

        private void SaveSnapshotPng()
        {
            try
            {
                int w = Math.Max(640, _stage.ClientSize.Width);
                int h = Math.Max(420, _stage.ClientSize.Height);
                string outPng = Path.Combine(_exportsDir, "omega_sheaf_v02_" + DateTime.Now.ToString("HHmmss") + ".png");
                if (_gpu.IsGpuActive)
                {
                    int[] rb = new int[w * h];
                    if (_gpu.RenderGpuFrame(_graph, w, h, _viewMode, (float)_clock.Elapsed.TotalSeconds, _obsTolerance, _camZoom, _panX, _panY, _orbitYaw, _orbitPitch, _selNodeIdx, _selEdgeIdx, rb))
                    {
                        using (Bitmap bm = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                        {
                            BitmapData bd = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                            Marshal.Copy(rb, 0, bd.Scan0, w * h);
                            bm.UnlockBits(bd);
                            bm.Save(outPng, ImageFormat.Png);
                        }
                        AppendExportLog("Saved GPU DirectCompute PNG -> " + outPng);
                        SetBotStatus("Saved snapshot: " + outPng);
                        return;
                    }
                }
                using (Bitmap bm = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    _gpu.RenderCpuFallback(bm, _graph, _viewMode, (float)_clock.Elapsed.TotalSeconds, _camZoom, _panX, _panY, _orbitYaw, _orbitPitch, _selNodeIdx, _selEdgeIdx);
                    bm.Save(outPng, ImageFormat.Png);
                }
                AppendExportLog("Saved PNG -> " + outPng);
            }
            catch (Exception ex) { AppendExportLog("PNG error: " + ex.Message); }
        }

        private void BeamSheafToPixelsAsync()
        {
            SetBotStatus("Arming ADB reverse tcp:19784 -> Pixel 10 & Pixel 8 2-Way OLED Touch Surgery Pad...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                int armed = 0;
                try
                {
                    string adb = ResolveAdbExe();
                    if (!string.IsNullOrEmpty(adb))
                    {
                        string devOut = RunProcOut(adb, "devices", 2000);
                        string[] lines = devOut.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string ln = lines[i].Trim();
                            if (ln.EndsWith("\tdevice"))
                            {
                                string ser = ln.Split('\t')[0].Trim();
                                RunProcOut(adb, "-s " + ser + " reverse tcp:19784 tcp:19784", 2000);
                                RunProcOut(adb, "-s " + ser + " shell am start -a android.intent.action.VIEW -d http://127.0.0.1:19784/sheaf", 2500);
                                armed++;
                            }
                        }
                    }
                }
                catch { }
                BeginInvoke((Action)delegate
                {
                    if (armed > 0)
                        SetBotStatus("Beamed 2-Way OLED Touch Pad http://127.0.0.1:19784/sheaf to " + armed + " Pixel(s)!");
                    else
                    {
                        Process.Start("http://127.0.0.1:19784/sheaf");
                        SetBotStatus("Opened 2-Way Touch Pad http://127.0.0.1:19784/sheaf locally.");
                    }
                });
            });
        }

        private void RunGemmaSheafSynthAsync()
        {
            string prompt = _txtGemmaPrompt.Text.Trim();
            if (prompt.Length == 0) return;
            SetBotStatus("Synthesizing Cellular Sheaf topology via Gemma / Algebraic Engine...");
            bool syncAv = _chkSyncAvOnGemma.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                SheafGraphModel temp = new SheafGraphModel();
                string note = OmegaSheafExporters.SynthesizeSheafViaGemma(temp, prompt);
                SheafE8Math.StepCpuPhysicsAndCohomology(temp, 0.016f, 0.0f, 0.0f, _obsTolerance, false, false);
                SheafE8Math.AuditCycleHolonomyAndBetti(temp, _obsTolerance);
                BeginInvoke((Action)delegate
                {
                    CopyGraphInto(temp, _graph);
                    _selNodeIdx = 0; _selEdgeIdx = 0;
                    RefreshAllInspectorBindings();
                    if (syncAv) SyncOmshAndFocusAv(false);
                    _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
                    SetBotStatus(note + " | Stalks:" + _graph.Nodes.Count + " | H^1=" + (_graph.IsGlobalConsensusH1Zero ? "0 (Consensus)" : ("!=0 (" + _graph.ContradictionLoops.Count + " Loops)")));
                });
            });
        }

        private static void CopyGraphInto(SheafGraphModel src, SheafGraphModel dst)
        {
            dst.GraphTitle = src.GraphTitle;
            dst.ProvenanceNote = src.ProvenanceNote;
            dst.Nodes.Clear();
            for (int i = 0; i < src.Nodes.Count; i++) dst.Nodes.Add(src.Nodes[i].Clone());
            dst.Edges.Clear();
            for (int e = 0; e < src.Edges.Count; e++) dst.Edges.Add(src.Edges[e].Clone());
            SheafE8Math.AuditCycleHolonomyAndBetti(dst, 0.14f);
        }

        private void DrainRemoteCommands()
        {
            while (true)
            {
                string qs = null;
                lock (_pendingRemoteCmds)
                {
                    if (_pendingRemoteCmds.Count > 0) qs = _pendingRemoteCmds.Dequeue();
                }
                if (qs == null) break;

                Dictionary<string, string> kv = ParseQuery(qs);
                string act;
                if (!kv.TryGetValue("act", out act)) act = qs.Trim().ToLowerInvariant();
                else act = act.Trim().ToLowerInvariant();

                if (act == "rectify") ExecuteRectifySurgery();
                else if (act == "paradox") _cmbPatchBank.SelectedIndex = 1;
                else if (act == "suite") LoadLiveSuiteAudit(false);
                else if (act == "corpus") RunCorpusIngest();
                else if (act == "ck" && _cmbPatchBank.Items.Count > 8) _cmbPatchBank.SelectedIndex = 8;
                else if (act == "sel_node")
                {
                    int nid;
                    if (kv.ContainsKey("id") && int.TryParse(kv["id"], out nid) && nid >= 0 && nid < _graph.Nodes.Count)
                    {
                        _graph.Nodes[nid].Pinned = !_graph.Nodes[nid].Pinned;
                        SelectNode(nid);
                    }
                }
                else if (act == "drag")
                {
                    int nid; float nx, ny;
                    if (kv.ContainsKey("id") && int.TryParse(kv["id"], out nid) &&
                        kv.ContainsKey("nx") && float.TryParse(kv["nx"], NumberStyles.Float, CultureInfo.InvariantCulture, out nx) &&
                        kv.ContainsKey("ny") && float.TryParse(kv["ny"], NumberStyles.Float, CultureInfo.InvariantCulture, out ny) &&
                        nid >= 0 && nid < _graph.Nodes.Count)
                    {
                        _graph.Nodes[nid].X = (nx - 0.50f) * 5.4f;
                        _graph.Nodes[nid].Y = (ny - 0.50f) * 4.0f;
                        _graph.Nodes[nid].Vx = 0.0f;
                        _graph.Nodes[nid].Vy = 0.0f;
                    }
                }
                else if (act == "toggle_edge")
                {
                    int eid;
                    if (kv.ContainsKey("id") && int.TryParse(kv["id"], out eid) && eid >= 0 && eid < _graph.Edges.Count)
                    {
                        SheafRestrictionEdge ed = _graph.Edges[eid];
                        if (ed.InContradictionLoop || ed.IsObstructed || ed.Polarity < 0.0f)
                        {
                            ed.Polarity = 1.0f; ed.TwistRad = 0.0f; ed.AbiMismatchBias = 0.0f;
                        }
                        else ed.Polarity = -1.0f;
                        SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                        SelectEdge(eid);
                        RefreshLoopListUi();
                        _sonifier.TickSonification(_graph, _clock.Elapsed.TotalSeconds, true);
                    }
                }
                else if (act == "eps")
                {
                    float ev;
                    if (kv.ContainsKey("val") && float.TryParse(kv["val"], NumberStyles.Float, CultureInfo.InvariantCulture, out ev))
                    {
                        _obsTolerance = Math.Max(0.03f, Math.Min(0.95f, ev));
                        _trkObsTol.Value = Math.Max(3, Math.Min(95, (int)Math.Round(_obsTolerance * 100.0f)));
                        _lblTolVal.Text = "Persistence Filtration Scrubber (eps): [" + _obsTolerance.ToString("0.00", CultureInfo.InvariantCulture) + "]";
                        SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                        RefreshLoopListUi();
                    }
                }
            }
        }

        private static Dictionary<string, string> ParseQuery(string qs)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = (qs ?? "").Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq > 0)
                    d[Uri.UnescapeDataString(parts[i].Substring(0, eq))] = Uri.UnescapeDataString(parts[i].Substring(eq + 1));
            }
            return d;
        }

        private void PollInboxCmd()
        {
            try
            {
                string cmdFile = Path.Combine(_sheafDir, "sheaf_inbox.cmd");
                if (!File.Exists(cmdFile)) return;
                string target = File.ReadAllText(cmdFile, Encoding.UTF8).Trim();
                File.Delete(cmdFile);
                if (target.Length > 0 && (File.Exists(target) || Directory.Exists(target)))
                    HandleDroppedPath(target);
            }
            catch { }
        }

        private void HandleDroppedPath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    string msg = OmegaSheafIngestor.ScanFolderCodebaseSheaf(_graph, path);
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshAllInspectorBindings();
                    SetBotStatus(msg);
                    return;
                }
                string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                if (ext == ".osheaf")
                {
                    if (OmegaSheafIngestor.LoadOsheafFile(path, _graph))
                    {
                        SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                        RefreshAllInspectorBindings();
                        SetBotStatus("Loaded .osheaf patch: " + Path.GetFileName(path));
                    }
                }
                else if (ext == ".md" || ext == ".rs" || ext == ".cs" || ext == ".hpp" || ext == ".h" || ext == ".cpp" || ext == ".ts")
                {
                    string dir = Path.GetDirectoryName(path);
                    string msg = (ext == ".md")
                        ? OmegaSheafIngestor.IngestOmegaBrowserCorpus(_graph, _suiteRoot)
                        : OmegaSheafIngestor.ScanFolderCodebaseSheaf(_graph, dir);
                    SheafE8Math.AuditCycleHolonomyAndBetti(_graph, _obsTolerance);
                    RefreshAllInspectorBindings();
                    SetBotStatus(msg);
                }
            }
            catch (Exception ex) { SetBotStatus("Drop load error: " + ex.Message); }
        }

        private void SeedFactoryPatchesOnDisk()
        {
            try
            {
                string[] slugs = new string[]
                {
                    "01_suite_v181_consensus.osheaf",
                    "02_cross_paper_paradox.osheaf",
                    "03_rust_cpp_deno_ffi_drift.osheaf",
                    "04_d3d11_srv_viewdim4_bug.osheaf",
                    "05_penrose_staircase_holonomy.osheaf",
                    "06_trisilicon_split_brain.osheaf",
                    "07_e8_leech_golay_exact.osheaf",
                    "08_braidir_ssa_lock_hazard.osheaf",
                    "09_ck_quest_softlock_paradox.osheaf"
                };
                SheafGraphModel tmp = new SheafGraphModel();
                for (int i = 0; i < slugs.Length && i < OmegaSheafIngestor.PresetNames.Length; i++)
                {
                    string fp = Path.Combine(_patchesDir, slugs[i]);
                    OmegaSheafIngestor.LoadPreset(tmp, i, _suiteRoot);
                    OmegaSheafIngestor.SaveOsheafFile(fp, tmp);
                }
            }
            catch { }
        }

        private void SavePatchDialog()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.InitialDirectory = _patchesDir;
                sfd.Filter = "OmegaSheaf Patch (*.osheaf)|*.osheaf";
                sfd.FileName = "custom_sheaf_" + DateTime.Now.ToString("HHmmss") + ".osheaf";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    OmegaSheafIngestor.SaveOsheafFile(sfd.FileName, _graph);
                    SetBotStatus("Saved .osheaf patch -> " + sfd.FileName);
                }
            }
        }

        private void LoadPatchDialog()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.InitialDirectory = _patchesDir;
                ofd.Filter = "OmegaSheaf Patch (*.osheaf)|*.osheaf|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK) HandleDroppedPath(ofd.FileName);
            }
        }

        private void RefreshAllInspectorBindings()
        {
            _suppressUiEvents = true;
            try
            {
                _lblProvenance.Text = _graph.ProvenanceNote;

                _cmbSelNode.Items.Clear();
                for (int i = 0; i < _graph.Nodes.Count; i++)
                    _cmbSelNode.Items.Add("[" + i + "] " + _graph.Nodes[i].Title + " (" + _graph.Nodes[i].SourceDoc + ")");
                if (_selNodeIdx >= _graph.Nodes.Count) _selNodeIdx = Math.Max(0, _graph.Nodes.Count - 1);
                if (_cmbSelNode.Items.Count > 0) _cmbSelNode.SelectedIndex = _selNodeIdx;

                _cmbSelEdge.Items.Clear();
                for (int e = 0; e < _graph.Edges.Count; e++)
                {
                    SheafRestrictionEdge ed = _graph.Edges[e];
                    _cmbSelEdge.Items.Add("[" + e + "] " + ed.SrcIdx + " -> " + ed.DstIdx + " :: " + ed.Label);
                }
                if (_selEdgeIdx >= _graph.Edges.Count) _selEdgeIdx = Math.Max(0, _graph.Edges.Count - 1);
                if (_cmbSelEdge.Items.Count > 0) _cmbSelEdge.SelectedIndex = _selEdgeIdx;

                PopulateSelectedNodeFields();
                PopulateSelectedEdgeFields();
                RefreshLoopListUi();
                RefreshAutoDiffBox();
                UpdateLiveTelemetryBanner();
            }
            finally { _suppressUiEvents = false; }
        }

        private void RefreshAutoDiffBox()
        {
            if (_txtDiffPreview == null) return;
            _txtDiffPreview.Text = OmegaSheafAutoDiffSurgeon.BuildUnifiedDiffPreview(_graph, _selEdgeIdx, _suiteRoot);
        }

        private void SelectNode(int idx)
        {
            if (idx < 0 || idx >= _graph.Nodes.Count) return;
            _selNodeIdx = idx;
            _suppressUiEvents = true;
            try
            {
                if (_cmbSelNode.Items.Count > idx) _cmbSelNode.SelectedIndex = idx;
                PopulateSelectedNodeFields();
            }
            finally { _suppressUiEvents = false; }
        }

        private void PopulateSelectedNodeFields()
        {
            if (_selNodeIdx < 0 || _selNodeIdx >= _graph.Nodes.Count) return;
            SheafStalkNode nd = _graph.Nodes[_selNodeIdx];
            _txtNodeTitle.Text = nd.Title;
            _txtNodeSrc.Text = nd.SourceDoc;
            _txtNodeFormula.Text = nd.FormulaOrType;
            _chkNodePinned.Checked = nd.Pinned;
            _lblHoverHud.ForeColor = nd.InContradictionLoop ? C(251, 113, 133) : C(52, 211, 153);
            _lblHoverHud.Text = "  SELECTED STALK [" + nd.Id + "] " + nd.Title + " (" + nd.SourceDoc + ") :: " + nd.FormulaOrType + " | E8 Root #" + nd.E8RootIndex;
        }

        private void SelectEdge(int idx)
        {
            if (idx < 0 || idx >= _graph.Edges.Count) return;
            _selEdgeIdx = idx;
            _suppressUiEvents = true;
            try
            {
                if (_cmbSelEdge.Items.Count > idx) _cmbSelEdge.SelectedIndex = idx;
                PopulateSelectedEdgeFields();
                RefreshAutoDiffBox();
            }
            finally { _suppressUiEvents = false; }
        }

        private void PopulateSelectedEdgeFields()
        {
            if (_selEdgeIdx < 0 || _selEdgeIdx >= _graph.Edges.Count) return;
            SheafRestrictionEdge ed = _graph.Edges[_selEdgeIdx];
            _cmbEdgePolarity.SelectedIndex = (ed.Polarity >= 0.0f) ? 0 : 1;
            int deg = Math.Max(-180, Math.Min(180, (int)Math.Round(ed.TwistRad * 180.0 / Math.PI)));
            _trkEdgeTwist.Value = deg;
            _lblEdgeTwistVal.Text = "SO(2)^4 Givens Phase Twist Angle: [" + deg + " deg]";
            int ab = Math.Max(0, Math.Min(100, (int)Math.Round(ed.AbiMismatchBias * 100.0f)));
            _trkEdgeAbi.Value = ab;
            _lblEdgeAbiVal.Text = "FFI / ABI Struct Byte-Drift Bias: [" + (ab * 0.01f).ToString("0.00", CultureInfo.InvariantCulture) + "]";
        }

        private void RefreshLoopListUi()
        {
            _lstLoops.Items.Clear();
            if (_graph.ContradictionLoops.Count == 0)
            {
                _lstLoops.ForeColor = C(110, 231, 183);
                _lstLoops.Items.Add("[H^1 = 0] Zero frustrated cycles! All stalks commute globally.");
            }
            else
            {
                _lstLoops.ForeColor = C(253, 164, 175);
                for (int i = 0; i < _graph.ContradictionLoops.Count; i++)
                    _lstLoops.Items.Add(_graph.ContradictionLoops[i].Summary);
            }
        }

        private void AppendExportLog(string line)
        {
            if (_txtExportLog == null) return;
            _txtExportLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + "\r\n");
        }

        private void SetBotStatus(string msg)
        {
            if (_lblStatusBot != null && !IsDisposed) _lblStatusBot.Text = msg;
        }

        private static Button MakeTopBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.1f);
            b.FlatAppearance.BorderColor = C(56, 189, 248);
            return b;
        }

        private static void AddSecHeader(TabPage tp, string text, ref int y)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = text;
            l.SetBounds(10, y, 370, 18);
            l.Font = new Font("Consolas", 8.2f, FontStyle.Bold);
            l.ForeColor = C(244, 114, 182);
            tp.Controls.Add(l);
            y += 19;
        }

        private static Label AddSliderRow(TabPage tp, string title, int min, int max, int val, ref int y, out TrackBar trk)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = title;
            l.SetBounds(10, y, 370, 17);
            l.Font = new Font("Segoe UI", 8.0f);
            l.ForeColor = C(226, 232, 240);
            tp.Controls.Add(l);
            y += 17;

            trk = new TrackBar();
            trk.AutoSize = false;
            trk.Minimum = min;
            trk.Maximum = max;
            trk.Value = val;
            trk.TickStyle = TickStyle.None;
            trk.SetBounds(8, y, 374, 23);
            trk.BackColor = C(11, 17, 30);
            tp.Controls.Add(trk);
            y += 25;
            return l;
        }

        private static CheckBox AddCheck(TabPage tp, string text, bool chk, ref int y)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.Checked = chk;
            cb.SetBounds(12, y, 366, 20);
            cb.Font = new Font("Segoe UI", 8.1f);
            cb.ForeColor = C(125, 211, 252);
            tp.Controls.Add(cb);
            y += 21;
            return cb;
        }

        private static TextBox AddLabeledBox(TabPage tp, string lbl, int x, int y, int totalW)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = lbl;
            l.SetBounds(x, y + 2, 68, 20);
            l.Font = new Font("Segoe UI", 8.0f);
            l.ForeColor = C(148, 163, 184);
            tp.Controls.Add(l);

            TextBox tb = new TextBox();
            tb.SetBounds(x + 70, y, totalW - 70, 22);
            tb.BackColor = C(6, 10, 19);
            tb.ForeColor = Color.White;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = new Font("Consolas", 8.3f);
            tp.Controls.Add(tb);
            return tb;
        }

        private static string ResolveAdbExe()
        {
            string p1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe");
            if (File.Exists(p1)) return p1;
            if (File.Exists(@"C:\platform-tools\adb.exe")) return @"C:\platform-tools\adb.exe";
            return "adb.exe";
        }

        private static string RunProcOut(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(timeoutMs);
                    return s ?? "";
                }
            }
            catch { return ""; }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--stress-test", StringComparison.OrdinalIgnoreCase))
            {
                OmegaSheafModSurgeon.Track05StressSummary sr = OmegaSheafModSurgeon.OmegaSheafModStressSuite.RunAll8Vectors();
                Console.WriteLine("[OMEGASHEAF+MODSURGEON 8-VECTOR AUDIT] " + sr.PassedCount + "/" + sr.TotalCount + " PASS (" + sr.TotalTrials + " trials | RAM=" + sr.TrimmedWorkingSetMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB)");
                for (int vi = 0; vi < sr.Vectors.Count; vi++) { if (!sr.Vectors[vi].Passed) Console.WriteLine("  [FAIL] " + sr.Vectors[vi].Code + " -> " + sr.Vectors[vi].Detail); }
                Environment.Exit(sr.AllPassed ? 0 : 1);
                return;
            }
            OmegaPortableConfig.OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--surgeon", StringComparison.OrdinalIgnoreCase))
            {
                Application.Run(new OmegaSheafPillar6SurgeonDialog());
                return;
            }
            string initFile = (args != null && args.Length > 0) ? args[0] : "";
            Application.Run(new OmegaSheafCockpitForm(initFile));
        }
    }
}
