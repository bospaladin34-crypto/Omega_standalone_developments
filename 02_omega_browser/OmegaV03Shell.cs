using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OmegaBrowserV03
{
    public sealed class BrowserTab
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Workspace = "Research";
        public string Container = "Default";
        public string Title = "New Tab";
        public string Url = "omega://home";
        public bool Frozen = false;
        public bool ReaderOn = false;
        public string ReaderMode = "full";
        public long LastLoadMs = 0;
        public DateTime LastActiveUtc = DateTime.UtcNow;
        public Stopwatch NavWatch = new Stopwatch();
        public List<string> History = new List<string>();
        public WebView2 View;
    }


    public sealed partial class BrowserForm : Form, IAgentBrowserHost
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static readonly string[] ContainerNames = new string[] { "Default", "arXiv-Lab", "GitHub-1", "GitHub-2", "Google", "Untrusted" };

        private readonly string _labRoot, _homeDir, _dataRoot, _allowPath, _rulesPath, _sessionPath, _prefsPath, _draftsPath;
        private readonly Dictionary<string, CoreWebView2Environment> _envs = new Dictionary<string, CoreWebView2Environment>(StringComparer.OrdinalIgnoreCase);
        private WebView2 _prewarmedView;

        private readonly List<BrowserTab> _tabs = new List<BrowserTab>();
        private readonly Stack<ClosedTabRecord> _closedTabs = new Stack<ClosedTabRecord>();
        private readonly List<DownloadTrackItem> _downloads = new List<DownloadTrackItem>();
        private readonly HashSet<string> _collapsedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private BrowserTab _active;
        private BrowserTab _splitRight;
        private bool _splitMode = false;
        private bool _verticalTabsOn = true;
        private bool _vimMode = false;
        private bool _zenMode = false;
        private string _viewportPreset = "";
        private double _globalTextScale = 1.0;
        private int _focusSecondsLeft = 0;

        private string _workspace = "Research";
        private string _activeContainer = "Default";
        private string _desktopUa = "";
        private string _lastCompanionSha = "";
        private string _lastCompanionTitle = "OmegaBrowser v0.6.4 Ready";
        private string _lastCompanionUrl = "omega://home";
        private string _lastCompanionDistilled = "Open any arXiv paper, PDF, MIT OCW lecture, or Rust doc on your PC to stream 384D theorems, equations, and signatures to your Pixel OLED screen.";

        private readonly Dictionary<string, bool> _wsShield = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SiteShieldStat> _siteStats = new Dictionary<string, SiteShieldStat>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DomainPref> _domainPrefs = new Dictionary<string, DomainPref>(StringComparer.OrdinalIgnoreCase);
        private List<string> _blockFilters = new List<string>();
        private HashSet<string> _allowedSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private long _totalBlocked = 0, _total1P = 0, _total3P = 0, _cookiesRejected = 0, _autoplayBlocked = 0;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;
        private DateTime _lastCpuCheck = DateTime.UtcNow;
        private double _cpuPct = 0.0;
        private int _pixelPollTick = 0;

        private AgentApiServer _api;
        private SpeechSynthesizer _tts;
        private List<string> _ttsParagraphs = new List<string>();
        private int _ttsIndex = -1;
        private bool _ttsPaused = false;

        private Panel _tabStrip, _navBar, _labBar, _findBar, _vTabPanel, _shSidebar, _dlShelf, _statusBar;
        private SplitContainer _splitStage;
        private FlowLayoutPanel _tabFlow, _vTabFlow, _dlFlow;
        private Button _btnVTabToggle, _btnWs, _btnContainer, _btnShield, _btnZoom, _btnReader, _btnTts, _btnCopyMd, _btnCorpus;
        private Button _btnPixels, _btnBeamOled, _btnPullPx, _btnBraid, _btnZap, _btnClip, _btnRunCode;
        private Button _btnNote, _btnTm, _btnShot, _btnQr, _btnSplit, _btnVim, _btnZen, _btnFreeze, _btnBridge, _btnDlStudio, _btnSniffer, _btnSynthPipe, _btnAsk, _btnCmdK;
        private TextBox _omnibox, _txtFind, _shPrompt, _shOutput;
        private CheckBox _chkFindCase;
        private Label _lblFindHits, _lblVTabHeader, _lblStatusL, _lblStatusR, _lblCtxMeter;
        private ComboBox _shEndpoint, _shMode;
        private System.Windows.Forms.Timer _houseTmr;

        public BrowserForm(string labRoot)
        {
            _labRoot = labRoot;
            _homeDir = Path.Combine(labRoot, "home");
            _dataRoot = Path.Combine(labRoot, "userdata_v03");
            _allowPath = Path.Combine(labRoot, "agent_allowlist.txt");
            _rulesPath = Path.Combine(labRoot, "shield_rules.txt");
            _sessionPath = Path.Combine(labRoot, "session_v03.tsv");
            _prefsPath = Path.Combine(labRoot, "domain_prefs.tsv");
            _draftsPath = Path.Combine(labRoot, "vault", "form_drafts.tsv");

            Directory.CreateDirectory(_homeDir); Directory.CreateDirectory(_dataRoot);
            OmegaSecurity.EnsureFiles(_allowPath, _rulesPath);
            OmegaSecurity.LoadShieldRules(_rulesPath, out _blockFilters, out _allowedSites);
            _domainPrefs = OmegaSecurity.LoadDomainPrefs(_prefsPath);
            _wsShield["Research"] = true; _wsShield["Training"] = true; _wsShield["Dev"] = true;

            Text = "OmegaBrowser v0.6.4 :: Sovereign Tri-Silicon Edition (Ctrl+J HTTP-206 Vault Studio)";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1540, 930);
            BackColor = Color.FromArgb(9, 13, 22); ForeColor = Color.White; KeyPreview = true;
            try
            {
                string icoPath = Path.Combine(Directory.GetParent(labRoot).FullName, "icons", "omega_browser.ico");
                if (File.Exists(icoPath)) Icon = new Icon(icoPath);
            }
            catch { }

            BuildChrome();
            InitTts();
            KeyDown += OnAnyKeyDown;
            Shown += async delegate { await InitEngineAsync(); };
            Resize += delegate { LayoutViewports(); };
            FormClosing += OnBrowserClosing;
        }

        private void BuildChrome()
        {
            _tabStrip = new Panel(); _tabStrip.Dock = DockStyle.Top; _tabStrip.Height = 36; _tabStrip.BackColor = Color.FromArgb(7, 11, 19);
            FlowLayoutPanel topLeft = new FlowLayoutPanel(); topLeft.Dock = DockStyle.Left; topLeft.AutoSize = true; topLeft.WrapContents = false; topLeft.Padding = new Padding(4, 4, 0, 0);
            _btnVTabToggle = AutoBtn("V-Tabs", Color.FromArgb(30, 58, 138)); _btnVTabToggle.Click += delegate { _verticalTabsOn = !_verticalTabsOn; _vTabPanel.Visible = _verticalTabsOn && !_zenMode; };
            _btnWs = AutoBtn("WS: Research", Color.FromArgb(2, 132, 199)); _btnWs.Click += delegate { CycleWorkspace(); };
            _btnContainer = AutoBtn("Cnt: Default", Color.FromArgb(67, 56, 202)); _btnContainer.Click += delegate { ShowContainerMenu(); };
            topLeft.Controls.AddRange(new Control[] { _btnVTabToggle, _btnWs, _btnContainer });

            _tabFlow = new FlowLayoutPanel(); _tabFlow.Dock = DockStyle.Fill; _tabFlow.WrapContents = false; _tabFlow.AutoScroll = true; _tabFlow.Padding = new Padding(4, 4, 4, 0);
            _tabStrip.Controls.Add(_tabFlow); _tabStrip.Controls.Add(topLeft);

            _navBar = new Panel(); _navBar.Dock = DockStyle.Top; _navBar.Height = 38; _navBar.BackColor = Color.FromArgb(15, 23, 42); _navBar.Padding = new Padding(4, 4, 4, 4);
            FlowLayoutPanel leftNav = new FlowLayoutPanel(); leftNav.Dock = DockStyle.Left; leftNav.AutoSize = true; leftNav.WrapContents = false;
            Button bBack = AutoBtn("<", null); bBack.Click += delegate { if (_active != null && _active.View != null && _active.View.CanGoBack) _active.View.GoBack(); };
            Button bFwd = AutoBtn(">", null); bFwd.Click += delegate { if (_active != null && _active.View != null && _active.View.CanGoForward) _active.View.GoForward(); };
            Button bRel = AutoBtn("Reload", null); bRel.Click += delegate { if (_active != null && _active.View != null) _active.View.Reload(); };
            Button bHome = AutoBtn("Home", null); bHome.Click += delegate { NavigateActive("omega://home"); };
            leftNav.Controls.AddRange(new Control[] { bBack, bFwd, bRel, bHome });

            FlowLayoutPanel rightNav = new FlowLayoutPanel(); rightNav.Dock = DockStyle.Right; rightNav.AutoSize = true; rightNav.WrapContents = false;
            _btnShield = AutoBtn("Shield: ON", Color.FromArgb(6, 95, 70)); _btnShield.Click += delegate { OpenShieldModal(); };
            _btnZoom = AutoBtn("100%", Color.FromArgb(30, 41, 59)); _btnZoom.Click += delegate { ShowZoomAndScaleMenu(); };
            _btnSplit = AutoBtn("Split", Color.FromArgb(30, 64, 175)); _btnSplit.Click += async delegate { await ToggleSplitViewAsync(); };
            _btnReader = AutoBtn("Reader", Color.FromArgb(88, 28, 135)); _btnReader.Click += delegate { ShowReaderMenu(); };
            _btnCopyMd = AutoBtn("Copy MD+", Color.FromArgb(3, 105, 161)); _btnCopyMd.Click += async delegate { await CopyMarkdownWithReceiptAsync(); };
            _btnCorpus = AutoBtn("+Corpus", Color.FromArgb(15, 118, 110)); _btnCorpus.Click += async delegate { await SaveActiveToCorpusAsync(""); };
            _btnAsk = AutoBtn("SH-XI", Color.FromArgb(109, 40, 217)); _btnAsk.Click += delegate { _shSidebar.Visible = !_shSidebar.Visible; };
            _btnCmdK = AutoBtn("Ctrl+K", Color.FromArgb(30, 41, 59)); _btnCmdK.Click += delegate { OpenCommandPalette(); };
            rightNav.Controls.AddRange(new Control[] { _btnShield, _btnZoom, _btnSplit, _btnReader, _btnCopyMd, _btnCorpus, _btnAsk, _btnCmdK });

            Panel omniWrap = new Panel(); omniWrap.Dock = DockStyle.Fill; omniWrap.Padding = new Padding(4, 2, 4, 2);
            _omnibox = new TextBox(); _omnibox.Dock = DockStyle.Fill; _omnibox.BackColor = Color.FromArgb(9, 13, 22); _omnibox.ForeColor = Color.White;
            _omnibox.Font = new Font("Consolas", 10f); _omnibox.BorderStyle = BorderStyle.FixedSingle;
            _omnibox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NavigateActive(_omnibox.Text); }
                else OnAnyKeyDown(s, e);
            };
            omniWrap.Controls.Add(_omnibox);
            _navBar.Controls.Add(omniWrap); _navBar.Controls.Add(leftNav); _navBar.Controls.Add(rightNav);

            _labBar = new Panel(); _labBar.Dock = DockStyle.Top; _labBar.Height = 33; _labBar.BackColor = Color.FromArgb(11, 18, 33); _labBar.Padding = new Padding(6, 3, 6, 2);
            FlowLayoutPanel labFlow = new FlowLayoutPanel(); labFlow.Dock = DockStyle.Fill; labFlow.WrapContents = false; labFlow.AutoScroll = false;
            _btnPixels = AutoBtn("Pixels", Color.FromArgb(15, 118, 110)); _btnPixels.Click += delegate { OpenDualPixelHub(); };
            _btnBeamOled = AutoBtn("Beam OLED", Color.FromArgb(109, 40, 217)); _btnBeamOled.Click += async delegate { await BeamOledCompanionAsync(); };
            _btnPullPx = AutoBtn("Pull Pixel", Color.FromArgb(14, 116, 144)); _btnPullPx.Click += async delegate { await QuickPullPixelUrlAsync(); };
            _btnBraid = AutoBtn("Braid", Color.FromArgb(88, 28, 135)); _btnBraid.Click += delegate { OpenConceptBraidModal(); };
            _btnDlStudio = AutoBtn("DL (Ctrl+J)", Color.FromArgb(2, 132, 199)); _btnDlStudio.Click += delegate { OpenDownloadStudio(); };
            _btnSniffer = AutoBtn("Sniff", Color.FromArgb(67, 56, 202)); _btnSniffer.Click += async delegate { await OpenBatchLinkSnifferAsync(); };
            _btnSynthPipe = AutoBtn("-> Synth", Color.FromArgb(190, 24, 93)); _btnSynthPipe.Click += async delegate { await PipeActiveTabAudioToOmegaSynthAsync(); };
            _btnZap = AutoBtn("Zap", Color.FromArgb(153, 27, 27)); _btnZap.Click += delegate { ShowZapMenu(); };
            _btnClip = AutoBtn("Clip", Color.FromArgb(6, 95, 70)); _btnClip.Click += async delegate { await ToggleDomToolAsync("clip"); };
            _btnRunCode = AutoBtn("Code", Color.FromArgb(3, 105, 161)); _btnRunCode.Click += async delegate { await OpenPaperToCodeAsync(""); };
            _btnTts = AutoBtn("TTS", Color.FromArgb(67, 56, 202)); _btnTts.Click += delegate { ShowTtsMenu(); };
            _btnNote = AutoBtn("Note", Color.FromArgb(113, 63, 18)); _btnNote.Click += async delegate { await OpenStickyAndHighlightAsync(""); };
            _btnTm = AutoBtn("TM", Color.FromArgb(14, 116, 144)); _btnTm.Click += async delegate { await OpenTimeMachineAsync(); };
            _btnShot = AutoBtn("Shot+", Color.FromArgb(30, 64, 110)); _btnShot.Click += delegate { ShowScreenshotMenu(); };
            _btnQr = AutoBtn("QR", Color.FromArgb(51, 65, 85)); _btnQr.Click += delegate { OpenQrModal(); };
            _btnVim = AutoBtn("Vim", Color.FromArgb(30, 41, 59)); _btnVim.Click += async delegate { await ToggleVimModeAsync(); };
            _btnZen = AutoBtn("Zen", Color.FromArgb(30, 41, 59)); _btnZen.Click += delegate { ToggleZenMode(); };
            _btnFreeze = AutoBtn("Freeze", Color.FromArgb(120, 53, 15)); _btnFreeze.Click += delegate { ShowFreezeMemoryMenu(); };
            _btnBridge = AutoBtn("Bridge", Color.FromArgb(5, 150, 105)); _btnBridge.Click += async delegate { await SyncToGeminiBridgeAsync(); };
            labFlow.Controls.AddRange(new Control[] { _btnDlStudio, _btnSniffer, _btnSynthPipe, _btnPixels, _btnBeamOled, _btnPullPx, _btnBraid, _btnZap, _btnClip, _btnRunCode, _btnTts, _btnNote, _btnTm, _btnShot, _btnQr, _btnVim, _btnZen, _btnFreeze, _btnBridge });
            _labBar.Controls.Add(labFlow);

            BuildFindBar(); BuildVerticalTabs(); BuildShSidebar(); BuildDownloadShelf();

            _statusBar = new Panel(); _statusBar.Dock = DockStyle.Bottom; _statusBar.Height = 24; _statusBar.BackColor = Color.FromArgb(7, 11, 19);
            _lblStatusL = new Label(); _lblStatusL.Dock = DockStyle.Fill; _lblStatusL.ForeColor = Color.FromArgb(148, 163, 184); _lblStatusL.TextAlign = ContentAlignment.MiddleLeft; _lblStatusL.Padding = new Padding(8, 0, 0, 0); _lblStatusL.Font = new Font("Segoe UI", 8.5f);
            _lblStatusR = new Label(); _lblStatusR.Dock = DockStyle.Right; _lblStatusR.Width = 560; _lblStatusR.ForeColor = Color.FromArgb(56, 189, 248); _lblStatusR.TextAlign = ContentAlignment.MiddleRight; _lblStatusR.Padding = new Padding(0, 0, 10, 0); _lblStatusR.Font = new Font("Consolas", 8.5f);
            _lblStatusR.Cursor = Cursors.Hand; _lblStatusR.Click += delegate { NavigateActive("omega://performance"); };
            _statusBar.Controls.Add(_lblStatusL); _statusBar.Controls.Add(_lblStatusR);

            _splitStage = new SplitContainer(); _splitStage.Dock = DockStyle.Fill; _splitStage.Orientation = Orientation.Vertical;
            _splitStage.BackColor = Color.FromArgb(30, 41, 59); _splitStage.SplitterWidth = 5;
            _splitStage.Panel1.BackColor = Color.FromArgb(6, 9, 15); _splitStage.Panel2.BackColor = Color.FromArgb(6, 9, 15);
            _splitStage.Panel2Collapsed = true;

            Controls.Add(_splitStage); Controls.Add(_vTabPanel); Controls.Add(_shSidebar);
            Controls.Add(_findBar); Controls.Add(_labBar); Controls.Add(_navBar); Controls.Add(_tabStrip);
            Controls.Add(_dlShelf); Controls.Add(_statusBar);
        }

        private void BuildFindBar()
        {
            _findBar = new Panel(); _findBar.Dock = DockStyle.Top; _findBar.Height = 34; _findBar.Visible = false; _findBar.BackColor = Color.FromArgb(17, 28, 48);
            FlowLayoutPanel fp = new FlowLayoutPanel(); fp.Dock = DockStyle.Fill; fp.Padding = new Padding(8, 3, 8, 3); fp.WrapContents = false;
            Label lf = new Label(); lf.Text = "Find:"; lf.AutoSize = true; lf.Padding = new Padding(0, 5, 4, 0); lf.ForeColor = Color.FromArgb(56, 189, 248);
            _txtFind = new TextBox(); _txtFind.Width = 220; _txtFind.BackColor = Color.FromArgb(9, 13, 22); _txtFind.ForeColor = Color.White; _txtFind.BorderStyle = BorderStyle.FixedSingle;
            _chkFindCase = new CheckBox(); _chkFindCase.Text = "Match Case"; _chkFindCase.AutoSize = true; _chkFindCase.Padding = new Padding(6, 3, 6, 0);
            Button bPrev = AutoBtn("Prev", null); Button bNext = AutoBtn("Next", null); Button bCls = AutoBtn("X", Color.FromArgb(127, 29, 29));
            _lblFindHits = new Label(); _lblFindHits.Text = "0/0"; _lblFindHits.AutoSize = true; _lblFindHits.Padding = new Padding(8, 5, 0, 0); _lblFindHits.Font = new Font("Consolas", 9f);

            _txtFind.TextChanged += async delegate { await RunFindInPageAsync(0); };
            _chkFindCase.CheckedChanged += async delegate { await RunFindInPageAsync(0); };
            _txtFind.KeyDown += async delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RunFindInPageAsync(e.Shift ? -1 : 1); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseFindBar(); }
            };
            bPrev.Click += async delegate { await RunFindInPageAsync(-1); };
            bNext.Click += async delegate { await RunFindInPageAsync(1); };
            bCls.Click += delegate { CloseFindBar(); };
            fp.Controls.AddRange(new Control[] { lf, _txtFind, _chkFindCase, bPrev, bNext, _lblFindHits, bCls });
            _findBar.Controls.Add(fp);
        }

        private void BuildVerticalTabs()
        {
            _vTabPanel = new Panel(); _vTabPanel.Dock = DockStyle.Left; _vTabPanel.Width = 212; _vTabPanel.BackColor = Color.FromArgb(10, 15, 27);
            Panel hdr = new Panel(); hdr.Dock = DockStyle.Top; hdr.Height = 58; hdr.BackColor = Color.FromArgb(15, 23, 42);
            _lblVTabHeader = new Label(); _lblVTabHeader.SetBounds(8, 5, 196, 18); _lblVTabHeader.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); _lblVTabHeader.ForeColor = Color.FromArgb(56, 189, 248); _lblVTabHeader.Text = "RESEARCH TABS (BLUE)";
            Button bSearch = AutoBtn("Search", Color.FromArgb(3, 105, 161)); bSearch.Location = new Point(6, 26); bSearch.Click += delegate { OpenTabSearchModal(); };
            Button bDedup = AutoBtn("Dedupe", Color.FromArgb(153, 27, 27)); bDedup.Location = new Point(68, 26); bDedup.Click += delegate { CloseDuplicateTabs(); };
            Button bReopen = AutoBtn("Reopen", Color.FromArgb(14, 116, 144)); bReopen.Location = new Point(134, 26); bReopen.Click += async delegate { await ReopenLastClosedTabAsync(); };
            hdr.Controls.AddRange(new Control[] { _lblVTabHeader, bSearch, bDedup, bReopen });

            _vTabFlow = new FlowLayoutPanel(); _vTabFlow.Dock = DockStyle.Fill; _vTabFlow.FlowDirection = FlowDirection.TopDown;
            _vTabFlow.WrapContents = false; _vTabFlow.AutoScroll = true; _vTabFlow.Padding = new Padding(4);
            _vTabPanel.Controls.Add(_vTabFlow); _vTabPanel.Controls.Add(hdr);
        }

        private void BuildDownloadShelf()
        {
            _dlShelf = new Panel(); _dlShelf.Dock = DockStyle.Bottom; _dlShelf.Height = 34; _dlShelf.Visible = false; _dlShelf.BackColor = Color.FromArgb(12, 20, 36);
            Button bHide = AutoBtn("Hide", Color.FromArgb(51, 65, 85)); bHide.Dock = DockStyle.Right; bHide.Click += delegate { _dlShelf.Visible = false; };
            Button bStudio = AutoBtn("Studio (Ctrl+J)", Color.FromArgb(2, 132, 199)); bStudio.Dock = DockStyle.Right; bStudio.Click += delegate { OpenDownloadStudio(); };
            _dlFlow = new FlowLayoutPanel(); _dlFlow.Dock = DockStyle.Fill; _dlFlow.WrapContents = false; _dlFlow.AutoScroll = true; _dlFlow.Padding = new Padding(6, 3, 6, 2);
            _dlShelf.Controls.Add(_dlFlow); _dlShelf.Controls.Add(bStudio); _dlShelf.Controls.Add(bHide);
        }

        private void BuildShSidebar()
        {
            _shSidebar = new Panel(); _shSidebar.Dock = DockStyle.Right; _shSidebar.Width = 350; _shSidebar.BackColor = Color.FromArgb(12, 18, 31); _shSidebar.Visible = false;
            Label hdr = new Label(); hdr.Text = "ASK SH-XI :: LOCAL-ONLY SIDEBAR"; hdr.SetBounds(10, 10, 330, 20); hdr.ForeColor = Color.FromArgb(167, 139, 250); hdr.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _lblCtxMeter = new Label(); _lblCtxMeter.SetBounds(10, 32, 330, 18); _lblCtxMeter.ForeColor = Color.FromArgb(56, 189, 248); _lblCtxMeter.Font = new Font("Consolas", 8.5f); _lblCtxMeter.Text = "Context: 0 / 8,192 tok";
            _shEndpoint = new ComboBox(); _shEndpoint.SetBounds(10, 54, 330, 24); _shEndpoint.BackColor = Color.FromArgb(15, 23, 42); _shEndpoint.ForeColor = Color.White;
            _shEndpoint.Items.AddRange(new object[] { "http://127.0.0.1:8080/v1/chat/completions", "http://127.0.0.1:11434/api/generate", "http://127.0.0.1:8000/v1/chat/completions", "offline://local-distiller" });
            _shEndpoint.SelectedIndex = 0;
            _shMode = new ComboBox(); _shMode.SetBounds(10, 84, 205, 24); _shMode.DropDownStyle = ComboBoxStyle.DropDownList; _shMode.BackColor = Color.FromArgb(15, 23, 42); _shMode.ForeColor = Color.White;
            _shMode.Items.AddRange(new object[] { "384D Distill", "768D Summary", "Extract Code", "Extract Theorems" }); _shMode.SelectedIndex = 0;
            Button bRun = AutoBtn("Run SH-XI", Color.FromArgb(109, 40, 217)); bRun.SetBounds(222, 82, 118, 27); bRun.Click += async delegate { await RunAskShXiAsync(); };
            _shPrompt = new TextBox(); _shPrompt.SetBounds(10, 114, 330, 24); _shPrompt.BackColor = Color.FromArgb(9, 13, 22); _shPrompt.ForeColor = Color.White; _shPrompt.BorderStyle = BorderStyle.FixedSingle;
            _shOutput = new TextBox(); _shOutput.SetBounds(10, 144, 330, 560); _shOutput.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _shOutput.Multiline = true; _shOutput.ReadOnly = true; _shOutput.ScrollBars = ScrollBars.Vertical; _shOutput.BackColor = Color.FromArgb(8, 12, 20); _shOutput.ForeColor = Color.FromArgb(226, 232, 240); _shOutput.Font = new Font("Consolas", 9f);
            _shSidebar.Controls.AddRange(new Control[] { hdr, _lblCtxMeter, _shEndpoint, _shMode, bRun, _shPrompt, _shOutput });
        }

        private void InitTts()
        {
            try
            {
                _tts = new SpeechSynthesizer();
                _tts.SpeakCompleted += delegate
                {
                    if (!_ttsPaused && _ttsIndex >= 0 && _ttsIndex + 1 < _ttsParagraphs.Count)
                        BeginInvoke((Action)SpeakNextParagraph);
                };
            }
            catch { _tts = null; }
        }

        private async Task<CoreWebView2Environment> GetContainerEnvAsync(string container)
        {
            string key = string.IsNullOrWhiteSpace(container) ? "Default" : container.Trim();
            CoreWebView2Environment env;
            if (_envs.TryGetValue(key, out env)) return env;
            string dir = Path.Combine(_dataRoot, "containers", key);
            Directory.CreateDirectory(dir);
            CoreWebView2EnvironmentOptions opts = new CoreWebView2EnvironmentOptions("--disable-features=msSmartScreenProtection --autoplay-policy=user-gesture-required --enable-smooth-scrolling");
            env = await CoreWebView2Environment.CreateAsync(null, dir, opts);
            _envs[key] = env;
            return env;
        }

        private async Task InitEngineAsync()
        {
            InitDownloadStudioSubsystem();
            await GetContainerEnvAsync("Default");
            await RestoreOrSeedSessionAsync();
            Task ignored = PrewarmDefaultAsync();

            _api = new AgentApiServer(9778, _labRoot, this);
            _api.Start();

            _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
            _lastCpuCheck = DateTime.UtcNow;

            _houseTmr = new System.Windows.Forms.Timer(); _houseTmr.Interval = 1000;
            _houseTmr.Tick += async delegate { await HousekeepingTickAsync(); };
            _houseTmr.Start();
            await RefreshPixelBadgeAsync();
            UpdateTelemetryBar();
        }

        private async Task PrewarmDefaultAsync()
        {
            try
            {
                if (_prewarmedView != null) return;
                CoreWebView2Environment env = await GetContainerEnvAsync("Default");
                WebView2 w = new WebView2(); w.Visible = false; w.Size = new Size(800, 600);
                _splitStage.Panel1.Controls.Add(w);
                await w.EnsureCoreWebView2Async(env);
                await ConfigureCoreAsync(w.CoreWebView2, env);
                _prewarmedView = w;
            }
            catch { }
        }

        private static Button AutoBtn(string txt, Color? bg)
        {
            Button b = new Button(); b.Text = txt; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(26, 25); b.Padding = new Padding(4, 1, 4, 1); b.Margin = new Padding(0, 0, 2, 0);
            b.FlatStyle = FlatStyle.Flat; b.BackColor = bg.HasValue ? bg.Value : Color.FromArgb(30, 41, 59);
            b.ForeColor = Color.White; b.Font = new Font("Segoe UI Semibold", 8.2f);
            b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = Color.FromArgb(45, 68, 105);
            return b;
        }
    }

    public static class CompanionDeckRenderer
    {
        public static ApiResult RenderHtmlPage()
        {
            string html = "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'>" +
                "<meta name='viewport' content='width=device-width,initial-scale=1,maximum-scale=1'>" +
                "<title>Omega OLED Companion Deck</title>" +
                "<style>" +
                "*{box-sizing:border-box;margin:0;padding:0;font-family:'Segoe UI',system-ui,sans-serif}" +
                "body{background:#000000;color:#e2e8f0;padding:14px;min-height:100vh}" +
                ".top{display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid #1e293b;padding-bottom:10px;margin-bottom:10px}" +
                ".pill{font-size:11px;font-weight:700;padding:3px 9px;border-radius:99px;background:#09192c;color:#38bdf8;border:1px solid #0284c7}" +
                "h1{font-size:16px;color:#f8fafc;line-height:1.35;margin-bottom:6px}" +
                ".meta{font:11px Consolas,monospace;color:#10b981;margin-bottom:10px;word-break:break-all}" +
                ".bar{display:flex;gap:7px;margin-bottom:12px;flex-wrap:wrap}" +
                "button{flex:1;min-width:95px;padding:9px 8px;border-radius:8px;border:1px solid #1e3a5f;background:#0b1324;color:#f8fafc;font-weight:700;font-size:12px}" +
                "button.lock{background:#7c2d12;border-color:#f97316}" +
                "pre{white-space:pre-wrap;word-break:break-word;font:12.5px/1.6 Consolas,monospace;color:#cbd5e1;background:#050811;border:1px solid #172554;border-radius:10px;padding:12px}" +
                "</style></head><body>" +
                "<div class='top'><span class='pill' id='ws'>OMEGA OLED DECK v0.6.3</span><span class='pill' id='mode'>FOLLOW PC</span></div>" +
                "<h1 id='title'>Connecting to OmegaBrowser :9778...</h1>" +
                "<div class='meta' id='sha'>SHA-256: waiting...</div>" +
                "<div class='bar'>" +
                "<button id='btnLock' onclick='toggleLock()'>Lock Reference</button>" +
                "<button onclick='act(\"/api/corpus/save\")'>+ Save Corpus</button>" +
                "<button onclick='act(\"/api/freeze\")'>Freeze PC BG</button>" +
                "</div>" +
                "<pre id='body'>Open any arXiv paper, PDF, MIT OCW lecture, or Rust doc on your PC to stream 384D equations, theorems, and signatures here at zero OLED black power.</pre>" +
                "<script>" +
                "var locked=false, lastSha='';" +
                "function toggleLock(){ locked=!locked; var b=document.getElementById('btnLock'); b.className=locked?'lock':''; b.textContent=locked?'Unlock (Pinned)':'Lock Reference'; document.getElementById('mode').textContent=locked?'PINNED REF':'FOLLOW PC'; }" +
                "function act(p){ fetch(p).then(function(r){return r.json();}).then(function(d){ document.getElementById('sha').textContent='Action OK -> '+JSON.stringify(d).slice(0,72); }); }" +
                "function poll(){ if(locked) return; fetch('/api/companion/state').then(function(r){return r.json();}).then(function(d){ if(!d||!d.ok) return; document.getElementById('ws').textContent='WS: '+d.workspace+' | 384D'; if(d.sha256===lastSha) return; lastSha=d.sha256; document.getElementById('title').textContent=d.title; document.getElementById('sha').textContent='SHA-256: '+d.sha256.slice(0,20)+'... | '+d.url.slice(0,56); document.getElementById('body').textContent=d.distilled||'No text extracted.'; }).catch(function(){}); }" +
                "poll(); setInterval(poll,2600);" +
                "</script></body></html>";
            ApiResult r = new ApiResult();
            r.Status = 200;
            r.ContentType = "text/html; charset=utf-8";
            r.Body = Encoding.UTF8.GetBytes(html);
            return r;
        }
    }
}
