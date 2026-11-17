using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaShellStudio
{
    public sealed partial class OmegaShellForm : Form, IOshApiHost
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        internal const int WM_HSCROLL = 0x0114;
        internal const int SB_LEFT = 6;
        internal const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        internal const int EM_LINESCROLL = 0x00B6;

        private sealed class ShellSessionState
        {
            public int Id;
            public TabPage Page;
            public RichTextBox Console;
            public string SessionCwd;
        }

        private readonly string _shellRoot, _suiteRoot, _configDir, _oshrcPath;
        private readonly ShellSettingsProfile _settings;
        private readonly OshShellRuntime _runtime;
        private readonly OshLocalApiServer _apiServer;
        private readonly System.Windows.Forms.Timer _telemetryTimer;

        private TabControl _mainTabs, _shellSessionTabs;
        private readonly List<ShellSessionState> _sessions = new List<ShellSessionState>();
        private List<GemmaLiveModelItem> _discoveredModels = new List<GemmaLiveModelItem>();
        private ComboBox _cmbTopGemmaModel;
        private bool _syncingModelCombo = false;

        internal static void ApplySplitterOnTabVisible(Control tabOrHost, Action applyAction)
        {
            bool applied = false;
            EventHandler tryApply = null;
            tryApply = delegate
            {
                if (applied || tabOrHost == null || tabOrHost.IsDisposed) return;
                if (!tabOrHost.Visible || tabOrHost.ClientSize.Width < 420 || tabOrHost.ClientSize.Height < 260) return;
                applied = true;
                try { applyAction(); } catch { applied = false; }
            };
            tabOrHost.VisibleChanged += tryApply;
            tabOrHost.SizeChanged += tryApply;
        }
        private TextBox _txtGemmaMissionBar;
        private int _nextSessionId = 1;
        private CheckBox _chkWrapConsole;
        private Label _lblTopBrand, _lblTopTelemetry, _lblPromptCwd, _lblStatus;
        private DataGridView _gridPipeRows;
        private TextBox _txtCmdInput;
        private int _historyCursor = -1;
        private string _lastPipelineCmd = "drives";
        private volatile bool _telemetryBusy = false;

        public OmegaShellForm(string shellRoot)
        {
            _shellRoot = shellRoot;
            _suiteRoot = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'))) ? (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')) : Directory.GetParent(shellRoot).FullName;
            _configDir = Path.Combine(shellRoot, "config");
            _oshrcPath = Path.Combine(_configDir, "init.oshrc");
            Directory.CreateDirectory(_configDir);

            _settings = ShellSettingsProfile.CreateDefault(_configDir);
            _runtime = new OshShellRuntime(shellRoot, _settings);
            _runtime.OnWorkingDirectoryChanged += delegate(string newCwd)
            {
                if (!IsDisposed)
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            UpdatePromptLabel();
                            SyncExplorerToDirectory(newCwd);
                            RefreshWorkspaceCodeFileList();
                        });
                    }
                    catch { }
                }
            };

            EnsureDefaultOshrc();

            Text = "ACT-Omega Lab :: OmegaShell v1.1.6 Golden Master (10_omega_shell) — Gemma Command, Polyglot Code Studio, AVF Debian & Win32 Tuner (:19781)";
            Size = new Size(1680, 960);
            MinimumSize = new Size(1260, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(8, 12, 21);
            ForeColor = Color.White;
            KeyPreview = true;
            try
            {
                string ico = Path.Combine(_suiteRoot, "icons", "omega_shell.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            BuildMainCockpitUi();

            _apiServer = new OshLocalApiServer(19781, this);
            _apiServer.Start();

            _telemetryTimer = new System.Windows.Forms.Timer();
            _telemetryTimer.Interval = 2200;
            _telemetryTimer.Tick += delegate { RefreshLiveTelemetryAsync(); };

            Shown += async delegate
            {
                Win32HardwareTuner.SampleCpuUsagePct();
                RefreshLiveTelemetryAsync();
                _telemetryTimer.Start();
                RefreshDriveExplorerButtons();
                NavigateExplorerDirectory(_runtime.CurrentDir);
                RefreshCasAndSnapshotLists();
                await RefreshLiveGemmaModelSelectorsAsync();
                await RefreshTunerTablesAsync("processes");
                await RunOshrcStartupAsync();
                _txtCmdInput.Focus();
            };

            FormClosing += delegate
            {
                _telemetryTimer.Stop();
                _settings.SaveToDisk(_configDir);
                if (_apiServer != null) _apiServer.Stop();
            };
        }

        private ShellSessionState ActiveSession
        {
            get
            {
                if (_shellSessionTabs != null && _shellSessionTabs.SelectedIndex >= 0 && _shellSessionTabs.SelectedIndex < _sessions.Count)
                    return _sessions[_shellSessionTabs.SelectedIndex];
                return _sessions.Count > 0 ? _sessions[0] : null;
            }
        }

        internal void SetSingleLineStatus(string msg)
        {
            if (_lblStatus == null || IsDisposed) return;
            string clean = (msg ?? "").Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " ").Trim();
            if (clean.Length > 190) clean = clean.Substring(0, 187) + "...";
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)delegate { _lblStatus.Text = clean; }); } catch { }
            }
            else
            {
                _lblStatus.Text = clean;
            }
        }

        internal static Panel WrapPaddedConsoleHost(RichTextBox rtb, int leftPad, int topPad)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(leftPad, topPad, 2, 4);
            host.BackColor = rtb.BackColor;
            host.Controls.Add(rtb);

            EventHandler lockLeft = delegate
            {
                if (!rtb.WordWrap && rtb.IsHandleCreated)
                {
                    try { SendMessage(rtb.Handle, WM_HSCROLL, (IntPtr)SB_LEFT, IntPtr.Zero); } catch { }
                }
            };
            rtb.HandleCreated += lockLeft;
            rtb.VisibleChanged += lockLeft;
            rtb.Resize += lockLeft;
            return host;
        }

        internal async Task RefreshLiveGemmaModelSelectorsAsync()
        {
            string fbEp = _settings.DefaultAiEndpoint;
            string fbMd = _settings.DefaultAiModel;
            List<GemmaLiveModelItem> found = await Task.Run(delegate
            {
                return OmegaShellGemmaCommander.DiscoverLiveModels(fbEp, fbMd);
            });
            _discoveredModels = found;
            BindModelCombo(_cmbTopGemmaModel, found);
            BindModelCombo(_cmbCodeGemmaModel, found);
        }

        private void BindModelCombo(ComboBox cmb, List<GemmaLiveModelItem> models)
        {
            if (cmb == null || IsDisposed) return;
            _syncingModelCombo = true;
            try
            {
                cmb.Items.Clear();
                int selIdx = 0;
                // Prefer Tandem router on startup if previous config pointed to raw :8097 so C#/PS1/C++ codegen never hits UNMEASURED/OFFLINE
                string targetTag = _settings.DefaultAiModel;
                if (_settings.DefaultAiEndpoint != null && _settings.DefaultAiEndpoint.Contains(":8097"))
                    targetTag = "auto-gemma-router";

                for (int i = 0; i < models.Count; i++)
                {
                    cmb.Items.Add(models[i]);
                    if (string.Equals(models[i].ModelTag, targetTag, StringComparison.OrdinalIgnoreCase))
                        selIdx = i;
                }
                if (cmb.Items.Count > 0) cmb.SelectedIndex = selIdx;
            }
            finally { _syncingModelCombo = false; }
        }

        internal void ApplySelectedGemmaModelItem(GemmaLiveModelItem item)
        {
            if (item == null || _syncingModelCombo) return;
            _syncingModelCombo = true;
            try
            {
                _settings.DefaultAiModel = item.ModelTag;
                _settings.DefaultAiEndpoint = item.EndpointUrl;
                _settings.SaveToDisk(_configDir);
                if (_txtAiEpSet != null) _txtAiEpSet.Text = _settings.DefaultAiEndpoint;
                if (_txtAiModelSet != null) _txtAiModelSet.Text = _settings.DefaultAiModel;
                SyncComboSelection(_cmbTopGemmaModel, item.ModelTag);
                SyncComboSelection(_cmbCodeGemmaModel, item.ModelTag);
            }
            finally { _syncingModelCombo = false; }
        }

        private static void SyncComboSelection(ComboBox cmb, string modelTag)
        {
            if (cmb == null || cmb.IsDisposed) return;
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                GemmaLiveModelItem it = cmb.Items[i] as GemmaLiveModelItem;
                if (it != null && string.Equals(it.ModelTag, modelTag, StringComparison.OrdinalIgnoreCase))
                {
                    if (cmb.SelectedIndex != i) cmb.SelectedIndex = i;
                    break;
                }
            }
        }

        private void EnsureDefaultOshrc()
        {
            if (File.Exists(_oshrcPath)) return;
            string def =
                "# OmegaShell v1.1.6 Golden Master Startup Script (.oshrc) — Runs automatically on launch\r\n" +
                "# Lines starting with # are comments. Define aliases, let variables, or startup checks:\r\n" +
                "kv set shell.owner local-operator\r\n" +
                "cloud status\r\n";
            File.WriteAllText(_oshrcPath, def, new UTF8Encoding(false));
        }

        private async Task RunOshrcStartupAsync()
        {
            if (!File.Exists(_oshrcPath)) return;
            string[] lines = File.ReadAllLines(_oshrcPath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length == 0 || ln.StartsWith("#")) continue;
                await RunCommandFromUiAsync(ln, true);
            }
        }

        private void BuildMainCockpitUi()
        {
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 62;
            topBar.BackColor = Color.FromArgb(12, 19, 34);

            Panel topRow1 = new Panel();
            topRow1.Dock = DockStyle.Top;
            topRow1.Height = 34;
            topRow1.Padding = new Padding(10, 4, 10, 2);
            topRow1.BackColor = Color.FromArgb(10, 16, 30);

            FlowLayoutPanel topRight = new FlowLayoutPanel();
            topRight.Dock = DockStyle.Right;
            topRight.AutoSize = true;
            topRight.WrapContents = false;

            Button bSpaceRelief = MakeBtn("C:\\ Relief", Color.FromArgb(185, 28, 28));
            bSpaceRelief.Click += async delegate
            {
                _mainTabs.SelectedIndex = 4;
                SelectDiskOffloaderSubTab();
                await RefreshTunerTablesAsync("diskhogs");
            };

            Button bQuickTrim = MakeBtn("Trim RAM", Color.FromArgb(5, 150, 105));
            bQuickTrim.Click += async delegate { await RunCommandFromUiAsync("opt trim-ram", false); };

            Button bQuickBoost = MakeBtn("P-Boost", Color.FromArgb(109, 40, 217));
            bQuickBoost.Click += async delegate { await RunCommandFromUiAsync("opt boost", false); };

            Button bHelp = MakeBtn("? Guide", Color.FromArgb(180, 83, 9));
            bHelp.Click += async delegate { _mainTabs.SelectedIndex = 0; await RunCommandFromUiAsync("help", false); };

            topRight.Controls.AddRange(new Control[] { bSpaceRelief, bQuickTrim, bQuickBoost, bHelp });

            _lblTopBrand = MakeLbl("OMEGASHELL v1.1.6 GM (:19781)", new Font("Consolas", 9.4f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            _lblTopBrand.AutoSize = false;
            _lblTopBrand.Dock = DockStyle.Left;
            _lblTopBrand.Width = 250;
            _lblTopBrand.TextAlign = ContentAlignment.MiddleLeft;

            topRow1.Controls.Add(topRight);
            topRow1.Controls.Add(_lblTopBrand);

            Panel topRow2 = new Panel();
            topRow2.Dock = DockStyle.Fill;
            topRow2.Padding = new Padding(10, 2, 10, 2);
            topRow2.BackColor = Color.FromArgb(15, 23, 42);

            _lblTopTelemetry = MakeLbl(
                "Initializing Win32 Hybrid P/E-Cores, DDR5 Working Sets, DirectCompute GPU, C:/E:/F: Drives, Gemma :8097 & Pixel AVF...",
                new Font("Consolas", 8.8f, FontStyle.Bold),
                Color.FromArgb(56, 189, 248));
            _lblTopTelemetry.AutoSize = false;
            _lblTopTelemetry.Dock = DockStyle.Fill;
            _lblTopTelemetry.TextAlign = ContentAlignment.MiddleLeft;
            topRow2.Controls.Add(_lblTopTelemetry);

            topBar.Controls.Add(topRow2);
            topBar.Controls.Add(topRow1);

            Panel botBar = new Panel();
            botBar.Dock = DockStyle.Bottom;
            botBar.Height = 25;
            botBar.BackColor = Color.FromArgb(6, 9, 16);

            _lblStatus = MakeLbl(
                "OmegaShell v1.1.6 Golden Master ONLINE (:19781) | Gemma Tool & Codegen Commander | Code Studio (Ctrl+Shift+F) | Android AVF (@deb)!",
                new Font("Consolas", 8.4f),
                Color.FromArgb(52, 211, 153));
            _lblStatus.AutoSize = false;
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Padding = new Padding(10, 0, 0, 0);
            botBar.Controls.Add(_lblStatus);

            _mainTabs = new TabControl();
            _mainTabs.Dock = DockStyle.Fill;
            _mainTabs.Font = new Font("Segoe UI Semibold", 9.1f);
            _mainTabs.Padding = new Point(12, 5);

            TabPage tabShell = new TabPage("1. OSH & Gemma Command");
            TabPage tabCode = new TabPage("2. Code & Gemma Codegen");
            TabPage tabAvf = new TabPage("3. Android AVF / Debian");
            TabPage tabDrives = new TabPage("4. Drives & Cloud CAS");
            TabPage tabTuner = new TabPage("5. Hardware & C: Offloader");
            TabPage tabMonitor = new TabPage("6. Lab Monitor & Settings");

            BuildTab1ShellUi(tabShell);
            BuildTab2CodeStudioUi(tabCode);
            BuildTab3AndroidAvfUi(tabAvf);
            BuildTab2ExplorerAndCloudUi(tabDrives);
            BuildTab3TunerUi(tabTuner);
            BuildTab4SettingsMonitorUi(tabMonitor);

            _mainTabs.TabPages.AddRange(new TabPage[] { tabShell, tabCode, tabAvf, tabDrives, tabTuner, tabMonitor });

            Controls.Add(_mainTabs);
            Controls.Add(topBar);
            Controls.Add(botBar);
        }

        private void BuildTab1ShellUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            FlowLayoutPanel quickStrip = new FlowLayoutPanel();
            quickStrip.Dock = DockStyle.Top;
            quickStrip.Height = 35;
            quickStrip.Padding = new Padding(8, 4, 8, 2);
            quickStrip.WrapContents = false;
            quickStrip.BackColor = Color.FromArgb(14, 21, 38);

            string[,] starters = new string[,]
            {
                { "Cloud & Tiers", "cloud status" },
                { "Live AI Models", "models" },
                { "Formatters", "toolchains" },
                { "AVF Debian", "avf status" },
                { "Group RAM", "ps | group name | sort -desc sum_ram_mb | top 14" },
                { "LISTEN Ports", "ports -listen" },
                { "C:\\ Space Hogs", "disk-hogs" },
                { "Git Status", "git-stat" },
                { "GPU & VRAM", "gpu" }
            };

            for (int i = 0; i < starters.GetLength(0); i++)
            {
                string label = starters[i, 0];
                string cmd = starters[i, 1];
                Color bg = label.StartsWith("C:\\") ? Color.FromArgb(153, 27, 27)
                    : (label == "AVF Debian" ? Color.FromArgb(15, 118, 110)
                    : (label == "Live AI Models" ? Color.FromArgb(109, 40, 217) : Color.FromArgb(30, 58, 138)));
                Button b = MakeBtn(label, bg);
                b.Click += async delegate { await RunCommandFromUiAsync(cmd, false); };
                quickStrip.Controls.Add(b);
            }

            Button bRunOsh = MakeBtn("Run .osh...", Color.FromArgb(6, 95, 70));
            bRunOsh.Click += async delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.InitialDirectory = ActiveWorkingDir();
                    ofd.Filter = "OmegaShell Scripts (*.osh;*.oshrc)|*.osh;*.oshrc|All Files (*.*)|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        string[] lines = File.ReadAllLines(ofd.FileName, Encoding.UTF8);
                        for (int k = 0; k < lines.Length; k++)
                        {
                            string ln = lines[k].Trim();
                            if (ln.Length > 0 && !ln.StartsWith("#")) await RunCommandFromUiAsync(ln, false);
                        }
                    }
                }
            };

            Button bCopyJson = MakeBtn("Copy JSON", Color.FromArgb(51, 65, 85));
            bCopyJson.Click += delegate
            {
                if (_lastGridRows != null && _lastGridRows.Count > 0)
                {
                    Clipboard.SetText(CodeFormatterAndCompiler.FormatJsonPretty(OshTableFormatter.FormatJson(_lastGridRows)));
                    SetSingleLineStatus("Copied " + _lastGridRows.Count + " structured row(s) to clipboard as formatted JSON!");
                }
            };

            Button bClear = MakeBtn("Clear Tab", Color.FromArgb(71, 85, 105));
            bClear.Click += delegate
            {
                ShellSessionState s = ActiveSession;
                if (s != null) { s.Console.Clear(); AppendShellBanner(s.Console); }
            };

            quickStrip.Controls.AddRange(new Control[] { bRunOsh, bCopyJson, bClear });

            Panel gemmaCmdStrip = new Panel();
            gemmaCmdStrip.Dock = DockStyle.Top;
            gemmaCmdStrip.Height = 38;
            gemmaCmdStrip.Padding = new Padding(8, 5, 8, 5);
            gemmaCmdStrip.BackColor = Color.FromArgb(18, 16, 40);

            FlowLayoutPanel gemmaLeft = new FlowLayoutPanel();
            gemmaLeft.Dock = DockStyle.Left;
            gemmaLeft.AutoSize = true;
            gemmaLeft.WrapContents = false;

            Label lGm = MakeLbl("Gemma Commander:", new Font("Segoe UI Semibold", 8.5f), Color.FromArgb(192, 132, 252));
            lGm.Padding = new Padding(0, 5, 4, 0);

            _cmbTopGemmaModel = new ComboBox();
            _cmbTopGemmaModel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbTopGemmaModel.Width = 255;

            _cmbTopGemmaModel.DropDownWidth = 380;
            _cmbTopGemmaModel.BackColor = Color.FromArgb(6, 9, 17);
            _cmbTopGemmaModel.ForeColor = Color.White;
            _cmbTopGemmaModel.SelectedIndexChanged += delegate
            {
                GemmaLiveModelItem sel = _cmbTopGemmaModel.SelectedItem as GemmaLiveModelItem;
                if (sel != null) ApplySelectedGemmaModelItem(sel);
            };

            Button bReScanAi = MakeBtn("Scan AI", Color.FromArgb(51, 65, 85));
            bReScanAi.Click += async delegate { await RefreshLiveGemmaModelSelectorsAsync(); };

            Button bWakeOl = MakeBtn("Wake :11434", Color.FromArgb(88, 28, 135));
            bWakeOl.Click += async delegate
            {
                string wm = await Task.Run(delegate { return OmegaShellGemmaCommander.WakeOllamaServer(); });
                SetSingleLineStatus(wm);
                await RefreshLiveGemmaModelSelectorsAsync();
            };

            gemmaLeft.Controls.AddRange(new Control[] { lGm, _cmbTopGemmaModel, bReScanAi, bWakeOl });

            FlowLayoutPanel gemmaRight = new FlowLayoutPanel();
            gemmaRight.Dock = DockStyle.Right;
            gemmaRight.AutoSize = true;
            gemmaRight.WrapContents = false;

            Button bRunGemmaMission = MakeBtn("Run Gemma Mission", Color.FromArgb(124, 58, 237));
            bRunGemmaMission.Click += async delegate { await SubmitGemmaMissionBarAsync(); };

            Button bQuickRustCg = MakeBtn("Codegen Rust", Color.FromArgb(5, 150, 105));
            bQuickRustCg.Click += async delegate
            {
                string spec = string.IsNullOrWhiteSpace(_txtGemmaMissionBar.Text)
                    ? "Write a fast 240-root E8 norm checksum benchmark in Rust and print the result"
                    : _txtGemmaMissionBar.Text.Trim();
                await RunCommandFromUiAsync("codegen rust \"" + spec.Replace("\"", "'") + "\"", false);
            };

            Button bStopGemma = MakeBtn("Stop", Color.FromArgb(153, 27, 27));
            bStopGemma.Click += delegate
            {
                OmegaShellGemmaCommander.AbortActiveStream();
                SetSingleLineStatus("Aborted active Gemma stream.");
            };

            gemmaRight.Controls.AddRange(new Control[] { bRunGemmaMission, bQuickRustCg, bStopGemma });

            _txtGemmaMissionBar = new TextBox();
            _txtGemmaMissionBar.Dock = DockStyle.Fill;
            _txtGemmaMissionBar.BackColor = Color.FromArgb(8, 11, 22);
            _txtGemmaMissionBar.ForeColor = Color.FromArgb(226, 232, 240);
            _txtGemmaMissionBar.Font = new Font("Consolas", 9.3f);
            _txtGemmaMissionBar.BorderStyle = BorderStyle.FixedSingle;
            _txtGemmaMissionBar.Text = "Inspect listen ports and top RAM processes, or generate and verify a Rust benchmark";
            _txtGemmaMissionBar.KeyDown += async delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    await SubmitGemmaMissionBarAsync();
                }
            };

            gemmaCmdStrip.Controls.Add(_txtGemmaMissionBar);
            gemmaCmdStrip.Controls.Add(gemmaLeft);
            gemmaCmdStrip.Controls.Add(gemmaRight);

            Panel inputBar = new Panel();
            inputBar.Dock = DockStyle.Bottom;
            inputBar.Height = 40;
            inputBar.Padding = new Padding(8, 6, 8, 6);
            inputBar.BackColor = Color.FromArgb(12, 19, 34);

            _lblPromptCwd = MakeLbl("", new Font("Consolas", 9.4f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            _lblPromptCwd.Dock = DockStyle.Left;
            _lblPromptCwd.Padding = new Padding(0, 4, 6, 0);

            Button bExec = MakeBtn("Execute (Enter)", Color.FromArgb(109, 40, 217));
            bExec.Dock = DockStyle.Right;
            bExec.Click += async delegate { await SubmitTerminalInputAsync(); };

            _txtCmdInput = new TextBox();
            _txtCmdInput.Dock = DockStyle.Fill;
            _txtCmdInput.BackColor = Color.FromArgb(6, 9, 17);
            _txtCmdInput.ForeColor = Color.White;
            _txtCmdInput.Font = new Font("Consolas", 10.0f);
            _txtCmdInput.BorderStyle = BorderStyle.FixedSingle;
            _txtCmdInput.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs pke)
            {
                if (pke.KeyCode == Keys.Tab) pke.IsInputKey = true;
            };
            _txtCmdInput.KeyDown += async delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    await SubmitTerminalInputAsync();
                }
                else if (ke.KeyCode == Keys.Up)
                {
                    ke.SuppressKeyPress = true;
                    if (_runtime.History.Count > 0)
                    {
                        if (_historyCursor < 0) _historyCursor = _runtime.History.Count - 1;
                        else if (_historyCursor > 0) _historyCursor--;
                        _txtCmdInput.Text = _runtime.History[_historyCursor];
                        _txtCmdInput.SelectionStart = _txtCmdInput.Text.Length;
                    }
                }
                else if (ke.KeyCode == Keys.Down)
                {
                    ke.SuppressKeyPress = true;
                    if (_historyCursor >= 0 && _historyCursor < _runtime.History.Count - 1)
                    {
                        _historyCursor++;
                        _txtCmdInput.Text = _runtime.History[_historyCursor];
                        _txtCmdInput.SelectionStart = _txtCmdInput.Text.Length;
                    }
                    else
                    {
                        _historyCursor = -1;
                        _txtCmdInput.Clear();
                    }
                }
                else if (ke.KeyCode == Keys.Tab)
                {
                    ke.SuppressKeyPress = true;
                    PerformTabCompletion();
                }
                else if (ke.Control && ke.KeyCode == Keys.T)
                {
                    ke.SuppressKeyPress = true;
                    CreateNewShellSessionTab(ActiveWorkingDir());
                }
            };

            inputBar.Controls.Add(_txtCmdInput);
            inputBar.Controls.Add(_lblPromptCwd);
            inputBar.Controls.Add(bExec);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            Panel leftSessionHost = new Panel();
            leftSessionHost.Dock = DockStyle.Fill;
            leftSessionHost.BackColor = Color.FromArgb(8, 12, 21);

            FlowLayoutPanel sessBar = new FlowLayoutPanel();
            sessBar.Dock = DockStyle.Top;
            sessBar.Height = 31;
            sessBar.Padding = new Padding(6, 3, 6, 2);
            sessBar.WrapContents = false;
            sessBar.BackColor = Color.FromArgb(11, 17, 30);

            Button bNewSess = MakeBtn("+ New Shell Tab (Ctrl+T)", Color.FromArgb(5, 150, 105));
            bNewSess.Click += delegate { CreateNewShellSessionTab(ActiveWorkingDir()); _txtCmdInput.Focus(); };

            Button bCloseSess = MakeBtn("Close Tab", Color.FromArgb(127, 29, 29));
            bCloseSess.Click += delegate { CloseActiveShellSessionTab(); };

            _chkWrapConsole = new CheckBox();
            _chkWrapConsole.Text = "Wrap Lines";
            _chkWrapConsole.AutoSize = true;
            _chkWrapConsole.Padding = new Padding(8, 3, 6, 0);
            _chkWrapConsole.ForeColor = Color.FromArgb(125, 211, 252);
            _chkWrapConsole.Checked = false;
            _chkWrapConsole.CheckedChanged += delegate
            {
                bool wr = _chkWrapConsole.Checked;
                for (int i = 0; i < _sessions.Count; i++) _sessions[i].Console.WordWrap = wr;
            };

            Label lSessHint = MakeLbl("Zero-Drift Column-0 Lock Active", new Font("Segoe UI", 8.0f), Color.FromArgb(148, 163, 184));
            lSessHint.Padding = new Padding(6, 5, 0, 0);

            sessBar.Controls.AddRange(new Control[] { bNewSess, bCloseSess, _chkWrapConsole, lSessHint });

            _shellSessionTabs = new TabControl();
            _shellSessionTabs.Dock = DockStyle.Fill;
            _shellSessionTabs.Font = new Font("Segoe UI Semibold", 8.6f);
            _shellSessionTabs.SelectedIndexChanged += delegate
            {
                ShellSessionState s = ActiveSession;
                if (s != null)
                {
                    _runtime.CurrentDir = s.SessionCwd;
                    UpdatePromptLabel();
                    SyncExplorerToDirectory(s.SessionCwd);
                }
            };

            CreateNewShellSessionTab(_runtime.CurrentDir);

            leftSessionHost.Controls.Add(_shellSessionTabs);
            leftSessionHost.Controls.Add(sessBar);

            Panel gridHost = new Panel();
            gridHost.Dock = DockStyle.Fill;
            gridHost.BackColor = Color.FromArgb(9, 13, 22);

            Label lGridHdr = MakeLbl(
                "STRUCTURED OBJECT PIPELINE & GEMMA TRACE INSPECTOR (Click Header = Sort | Right-Click Cell = Filter / Group / @gemma):",
                new Font("Segoe UI", 8.3f, FontStyle.Bold),
                Color.FromArgb(56, 189, 248));
            lGridHdr.AutoSize = false;
            lGridHdr.Dock = DockStyle.Top;
            lGridHdr.Height = 25;
            lGridHdr.Padding = new Padding(6, 4, 0, 0);
            lGridHdr.BackColor = Color.FromArgb(14, 21, 38);

            _gridPipeRows = MakeStyledGrid();
            AttachPipeContextMenu(_gridPipeRows, null);
            gridHost.Controls.Add(_gridPipeRows);
            gridHost.Controls.Add(lGridHdr);

            split.Panel1.Controls.Add(leftSessionHost);
            split.Panel2.Controls.Add(gridHost);

            page.Controls.Add(split);
            page.Controls.Add(gemmaCmdStrip);
            page.Controls.Add(quickStrip);
            page.Controls.Add(inputBar);

            UpdatePromptLabel();
            Shown += delegate { try { split.SplitterDistance = Math.Max(680, (ClientSize.Width * 54) / 100); } catch { } };
        }

        private async Task SubmitGemmaMissionBarAsync()
        {
            string goal = (_txtGemmaMissionBar.Text ?? "").Trim();
            if (string.IsNullOrEmpty(goal)) return;

            ShellSessionState sess = ActiveSession;
            RichTextBox rtb = sess != null ? sess.Console : null;
            if (rtb != null)
                AppendColoredLine(rtb, "@gemma [" + _settings.DefaultAiModel + "]> " + goal, Color.FromArgb(192, 132, 252));

            SetSingleLineStatus("Gemma Commander (" + _settings.DefaultAiModel + ") executing autonomous mission...");
            GemmaMissionResult res = await Task.Run(delegate
            {
                return OmegaShellGemmaCommander.ExecuteAutonomousMission(
                    _runtime,
                    goal,
                    delegate(string stepMsg)
                    {
                        SetSingleLineStatus(stepMsg);
                        if (rtb != null && !IsDisposed)
                        {
                            try { BeginInvoke((Action)delegate { AppendColoredLine(rtb, stepMsg, Color.FromArgb(125, 211, 252)); }); } catch { }
                        }
                    });
            });

            if (rtb != null)
            {
                AppendColoredLine(rtb, res.FinalNarrative, res.Ok ? Color.FromArgb(226, 232, 240) : Color.FromArgb(248, 113, 113));
                AppendColoredLine(rtb, "  [Gemma Mission Complete: " + res.TotalMs + " ms | " + res.TokensPerSec + " tok/s | Steps: " + res.TraceRows.Count + "]\r\n", Color.FromArgb(100, 116, 139));
            }
            if (res.TraceRows != null && res.TraceRows.Count > 0)
            {
                _lastGridRows = res.TraceRows;
                PopulateGridWithOshRows(_gridPipeRows, res.TraceRows);
            }
            if (!string.IsNullOrEmpty(res.GeneratedFilePath) && File.Exists(res.GeneratedFilePath))
            {
                OpenFileInCodeStudio(res.GeneratedFilePath);
            }
            SetSingleLineStatus("Gemma Mission Complete in " + res.TotalMs + " ms (" + res.TokensPerSec + " tok/s)!");
        }

        private void CreateNewShellSessionTab(string startDir)
        {
            int id = _nextSessionId++;
            string cwd = Directory.Exists(startDir) ? startDir : _runtime.CurrentDir;
            TabPage tp = new TabPage("OSH #" + id + " [" + ShortLeaf(cwd) + "]");
            tp.BackColor = Color.FromArgb(6, 9, 17);

            RichTextBox rtb = new RichTextBox();
            rtb.Dock = DockStyle.Fill;
            rtb.ReadOnly = true;
            rtb.BorderStyle = BorderStyle.None;
            rtb.BackColor = Color.FromArgb(6, 9, 17);
            rtb.ForeColor = Color.FromArgb(226, 232, 240);
            rtb.Font = new Font("Consolas", _settings.FontSize);
            rtb.WordWrap = false;
            rtb.ScrollBars = RichTextBoxScrollBars.Both;
            tp.Controls.Add(WrapPaddedConsoleHost(rtb, 8, 6));

            ShellSessionState st = new ShellSessionState();
            st.Id = id;
            st.Page = tp;
            st.Console = rtb;
            st.SessionCwd = cwd;

            _sessions.Add(st);
            _shellSessionTabs.TabPages.Add(tp);
            _shellSessionTabs.SelectedTab = tp;

            AppendShellBanner(rtb);
            UpdatePromptLabel();
        }

        private void CloseActiveShellSessionTab()
        {
            if (_sessions.Count <= 1)
            {
                SetSingleLineStatus("Keeping primary OSH session open (use Clear Tab to reset console).");
                return;
            }
            int idx = _shellSessionTabs.SelectedIndex;
            if (idx < 0 || idx >= _sessions.Count) return;
            _sessions.RemoveAt(idx);
            _shellSessionTabs.TabPages.RemoveAt(idx);
            UpdatePromptLabel();
        }

        private string ActiveWorkingDir()
        {
            ShellSessionState s = ActiveSession;
            return (s != null && !string.IsNullOrEmpty(s.SessionCwd)) ? s.SessionCwd : _runtime.CurrentDir;
        }

        private static string ShortLeaf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "E:\\";
            string trimmed = dir.TrimEnd('\\');
            if (trimmed.Length <= 3) return dir;
            string leaf = Path.GetFileName(trimmed);
            return Path.GetPathRoot(dir) + (string.IsNullOrEmpty(leaf) ? "" : leaf);
        }

        private List<OshRow> _lastGridRows = new List<OshRow>();

        private async Task SubmitTerminalInputAsync()
        {
            string cmd = (_txtCmdInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            _txtCmdInput.Clear();
            _historyCursor = -1;
            await RunCommandFromUiAsync(cmd, false);
        }

        private async Task RunCommandFromUiAsync(string cmdLine, bool quietBanner)
        {
            ShellSessionState sess = ActiveSession;
            string sessDir = sess != null ? sess.SessionCwd : _runtime.CurrentDir;
            RichTextBox rtb = sess != null ? sess.Console : null;

            if (!quietBanner && rtb != null)
            {
                int sid = sess != null ? sess.Id : 1;
                AppendColoredLine(rtb, "osh#" + sid + " [" + ShortCwd(sessDir) + "]> " + cmdLine, Color.FromArgb(52, 211, 153));
            }

            SetSingleLineStatus("Executing OSH v1.0 pipeline: " + cmdLine + " ...");
            string updatedDir = sessDir;
            OshExecOutput res = await Task.Run(delegate
            {
                string localDir = updatedDir;
                OshExecOutput o = _runtime.ExecutePipelineInDir(cmdLine, ref localDir, true);
                updatedDir = localDir;
                return o;
            });

            if (sess != null)
            {
                sess.SessionCwd = updatedDir;
                sess.Page.Text = "OSH #" + sess.Id + " [" + ShortLeaf(updatedDir) + "]";
            }

            string body = res.RenderConsoleText();
            if (!string.IsNullOrEmpty(body) && rtb != null)
                AppendColoredLine(rtb, body, res.Ok ? Color.FromArgb(226, 232, 240) : Color.FromArgb(248, 113, 113));

            string sha12 = (res.Sha256Receipt != null && res.Sha256Receipt.Length >= 12) ? res.Sha256Receipt.Substring(0, 12) : "n/a";
            if (rtb != null)
                AppendColoredLine(rtb, "  [Receipt: " + res.ElapsedMs + " ms | Rows: " + res.Rows.Count + " | SHA-256: " + sha12 + "]\r\n", Color.FromArgb(100, 116, 139));

            if (res.Rows != null && res.Rows.Count > 0)
            {
                _lastPipelineCmd = cmdLine;
                _lastGridRows = res.Rows;
                PopulateGridWithOshRows(_gridPipeRows, res.Rows);
            }

            if (!string.IsNullOrEmpty(res.EditTargetFile))
            {
                OpenFileInCodeStudio(res.EditTargetFile);
            }

            UpdatePromptLabel();
            SetSingleLineStatus("OSH v1.0 GM Ready | Last command took " + res.ElapsedMs + " ms (SHA-256: " + sha12 + ")");
        }

        private void PerformTabCompletion()
        {
            string cur = _txtCmdInput.Text ?? "";
            string[] verbs = new string[]
            {
                "gemma", "@gemma", "codegen", "models", "use-model", "code", "fmt", "build", "check", "diff", "toolchains",
                "git-stat", "calc", "let", "vars", "bench", "group", "avf", "@deb", "@avf", "drives", "disk-hogs", "offload",
                "junctions", "undo-junction", "edit", "history", "ls", "cd", "pwd", "cat", "find", "grep", "where", "sort",
                "select", "top", "count", "sum", "json", "csv", "ps", "kill", "ports", "kill-port", "services", "svc",
                "startup", "gpu", "ram", "opt", "cloud", "cas", "snap", "kv", "mesh", "forge", "browser", "av",
                "@p10", "@p8", "@pixels", "@ai", "alias", "help"
            };

            string activeDir = ActiveWorkingDir();
            if (!cur.Contains(" "))
            {
                for (int i = 0; i < verbs.Length; i++)
                {
                    if (verbs[i].StartsWith(cur, StringComparison.OrdinalIgnoreCase))
                    {
                        _txtCmdInput.Text = verbs[i] + " ";
                        _txtCmdInput.SelectionStart = _txtCmdInput.Text.Length;
                        return;
                    }
                }
            }
            else
            {
                int lastSp = cur.LastIndexOf(' ');
                string prefix = cur.Substring(0, lastSp + 1);
                string frag = cur.Substring(lastSp + 1).Trim('"');
                try
                {
                    string searchDir = activeDir;
                    string namePre = frag;
                    if (frag.Contains("\\") || frag.Contains("/"))
                    {
                        string resolved = MultiDrivePathEngine.ResolvePath(activeDir, frag, _shellRoot);
                        searchDir = Path.GetDirectoryName(resolved) ?? activeDir;
                        namePre = Path.GetFileName(resolved) ?? "";
                    }
                    if (Directory.Exists(searchDir))
                    {
                        string[] entries = Directory.GetFileSystemEntries(searchDir);
                        for (int i = 0; i < entries.Length; i++)
                        {
                            string fn = Path.GetFileName(entries[i]);
                            if (fn.StartsWith(namePre, StringComparison.OrdinalIgnoreCase))
                            {
                                string completed = entries[i].Contains(" ") ? ("\"" + entries[i] + "\"") : entries[i];
                                _txtCmdInput.Text = prefix + completed;
                                _txtCmdInput.SelectionStart = _txtCmdInput.Text.Length;
                                return;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static void AppendShellBanner(RichTextBox rtb)
        {
            AppendColoredLine(rtb,
                "=== OMEGASHELL v1.0 GOLDEN MASTER :: GEMMA COMMAND + POLYGLOT CODEGEN + AVF DEBIAN + OSH PIPES ===\r\n" +
                "  * Gemma Command:   gemma check listen ports & free RAM  |  codegen rust \"240-root E8 norm benchmark\"\r\n" +
                "  * Code & Auto-Fmt: code main.rs  |  fmt . -check  |  models  |  use-model gemma-4-12B-it-sh4z\r\n" +
                "  * Android Debian:  avf status  |  avf setup  |  @deb uname -a && cargo --version\r\n" +
                "  * OSH v1.0 Pipes:  let lim = 80; ps | group name | where sum_ram_mb gt $lim > ram.json\r\n",
                Color.FromArgb(125, 211, 252));
        }

        internal static void AppendColoredLine(RichTextBox rtb, string text, Color fg)
        {
            if (rtb == null || rtb.IsDisposed) return;
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = fg;
            rtb.AppendText((text ?? "") + "\r\n");

            if (!rtb.WordWrap)
            {
                int lastLine = Math.Max(0, rtb.GetLineFromCharIndex(Math.Max(0, rtb.TextLength - 2)));
                int colZeroIdx = rtb.GetFirstCharIndexFromLine(lastLine);
                rtb.SelectionStart = colZeroIdx >= 0 ? colZeroIdx : 0;
                rtb.SelectionLength = 0;
                rtb.ScrollToCaret();
                if (rtb.IsHandleCreated)
                {
                    try { SendMessage(rtb.Handle, WM_HSCROLL, (IntPtr)SB_LEFT, IntPtr.Zero); } catch { }
                }
            }
            else
            {
                rtb.SelectionStart = rtb.TextLength;
                rtb.ScrollToCaret();
            }
        }

        private void UpdatePromptLabel()
        {
            ShellSessionState s = ActiveSession;
            int id = s != null ? s.Id : 1;
            string cwd = s != null ? s.SessionCwd : _runtime.CurrentDir;
            _lblPromptCwd.Text = "osh#" + id + " [" + ShortCwd(cwd) + "]> ";
        }

        private static string ShortCwd(string p)
        {
            if (string.IsNullOrEmpty(p)) return "E:\\";
            if (p.Length <= 34) return p;
            return Path.GetPathRoot(p) + "...\\" + Path.GetFileName(p.TrimEnd('\\'));
        }

        private void RefreshLiveTelemetryAsync()
        {
            if (_telemetryBusy || IsDisposed) return;
            _telemetryBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    double cpu = Win32HardwareTuner.SampleCpuUsagePct();
                    MemorySnapshot mem = Win32HardwareTuner.QueryMemoryStatus();
                    string gpu = Win32HardwareTuner.QueryNvidiaGpuLine();
                    bool fgOn = MeshAndLabBridge.ProbePortFast(19780, 60);
                    bool brOn = MeshAndLabBridge.ProbePortFast(9778, 60);
                    bool avOn = MeshAndLabBridge.ProbePortFast(19779, 60);
                    bool mrOn = MeshAndLabBridge.ProbePortFast(19777, 60);
                    bool utcpOn = MeshAndLabBridge.ProbePortFast(8097, 60);
                    bool olOn = MeshAndLabBridge.ProbePortFast(11434, 60);
                    bool avfSsh = MeshAndLabBridge.ProbePortFast(AndroidAvfDebianBridge.DEFAULT_LOCAL_AVF_SSH_PORT, 45);
                    int portsUp = 1 + (fgOn ? 1 : 0) + (brOn ? 1 : 0) + (avOn ? 1 : 0) + (mrOn ? 1 : 0) + (utcpOn ? 1 : 0) + (olOn ? 1 : 0);

                    string drvSummary = BuildCompactDriveRibbon();
                    string avfTag = avfSsh ? " | @deb:SSH" : "";
                    string topLine = "CPU: " + cpu.ToString("0.0") + "% | DDR5 Free: " + mem.AvailPhysMb + "/" + mem.TotalPhysMb + " MB | " +
                        gpu + " | " + drvSummary + " | Mesh: " + portsUp + "/7" + avfTag;

                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)delegate
                        {
                            _lblTopTelemetry.Text = topLine;
                            UpdateTunerLiveBanner(cpu, mem, gpu);
                            if (_mainTabs.SelectedIndex == 5) RefreshLabMonitorCardsAsync();
                        });
                    }
                }
                catch { }
                finally { _telemetryBusy = false; }
            });
        }

        private static string BuildCompactDriveRibbon()
        {
            List<string> parts = new List<string>();
            string[] roots = new string[] { "C:\\", "E:\\", "F:\\" };
            for (int i = 0; i < roots.Length; i++)
            {
                try
                {
                    DriveInfo di = new DriveInfo(roots[i]);
                    if (di.IsReady)
                    {
                        double freeGb = di.AvailableFreeSpace / 1073741824.0;
                        parts.Add(roots[i].TrimEnd('\\') + " " + freeGb.ToString("0.0") + "GB");
                    }
                }
                catch { }
            }
            return string.Join(" / ", parts.ToArray());
        }

        internal void AttachPipeContextMenu(DataGridView g, Func<string> baseVerbProvider)
        {
            string clickedCol = "";
            string clickedVal = "";

            ContextMenuStrip cms = new ContextMenuStrip();
            cms.BackColor = Color.FromArgb(15, 23, 42);
            cms.ForeColor = Color.White;
            cms.Font = new Font("Segoe UI Semibold", 8.8f);

            ToolStripMenuItem miEq = new ToolStripMenuItem("Filter in OSH: where col == val");
            ToolStripMenuItem miNeq = new ToolStripMenuItem("Exclude in OSH: where col != val");
            ToolStripMenuItem miGroup = new ToolStripMenuItem("Group & Sum in OSH: group col");
            ToolStripMenuItem miSortDesc = new ToolStripMenuItem("Sort Descending in OSH");
            ToolStripMenuItem miSortAsc = new ToolStripMenuItem("Sort Ascending in OSH");
            ToolStripMenuItem miAi = new ToolStripMenuItem("Pipe Grid Rows -> Gemma Commander (@gemma)");
            ToolStripMenuItem miCopyCell = new ToolStripMenuItem("Copy Clicked Cell Value");
            ToolStripMenuItem miCopyRowJson = new ToolStripMenuItem("Copy Selected Row(s) as JSON");

            Func<string> getBaseCmd = delegate
            {
                if (baseVerbProvider != null)
                {
                    string bv = baseVerbProvider();
                    if (!string.IsNullOrEmpty(bv)) return bv;
                }
                return string.IsNullOrEmpty(_lastPipelineCmd) ? "drives" : _lastPipelineCmd;
            };

            miEq.Click += async delegate
            {
                if (string.IsNullOrEmpty(clickedCol)) return;
                string qVal = clickedVal.Contains(" ") ? ("\"" + clickedVal + "\"") : clickedVal;
                string pipeCmd = getBaseCmd() + " | where " + clickedCol + " == " + qVal;
                _mainTabs.SelectedIndex = 0;
                await RunCommandFromUiAsync(pipeCmd, false);
            };

            miNeq.Click += async delegate
            {
                if (string.IsNullOrEmpty(clickedCol)) return;
                string qVal = clickedVal.Contains(" ") ? ("\"" + clickedVal + "\"") : clickedVal;
                string pipeCmd = getBaseCmd() + " | where " + clickedCol + " != " + qVal;
                _mainTabs.SelectedIndex = 0;
                await RunCommandFromUiAsync(pipeCmd, false);
            };

            miGroup.Click += async delegate
            {
                if (string.IsNullOrEmpty(clickedCol)) return;
                string pipeCmd = getBaseCmd() + " | group " + clickedCol;
                _mainTabs.SelectedIndex = 0;
                await RunCommandFromUiAsync(pipeCmd, false);
            };

            miSortDesc.Click += async delegate
            {
                if (string.IsNullOrEmpty(clickedCol)) return;
                string pipeCmd = getBaseCmd() + " | sort -desc " + clickedCol;
                _mainTabs.SelectedIndex = 0;
                await RunCommandFromUiAsync(pipeCmd, false);
            };

            miSortAsc.Click += async delegate
            {
                if (string.IsNullOrEmpty(clickedCol)) return;
                string pipeCmd = getBaseCmd() + " | sort " + clickedCol;
                _mainTabs.SelectedIndex = 0;
                await RunCommandFromUiAsync(pipeCmd, false);
            };

            miAi.Click += async delegate
            {
                string pipeCmd = getBaseCmd() + " | top 20 | @gemma \"Analyze these rows and suggest concrete optimization actions\"";
                _mainTabs.SelectedIndex = 0;
                await RunCommandFromUiAsync(pipeCmd, false);
            };

            miCopyCell.Click += delegate
            {
                if (!string.IsNullOrEmpty(clickedVal))
                {
                    Clipboard.SetText(clickedVal);
                    SetSingleLineStatus("Copied cell [" + clickedCol + "] = " + clickedVal);
                }
            };

            miCopyRowJson.Click += delegate
            {
                List<OshRow> selRows = ExtractSelectedOshRowsFromGrid(g);
                if (selRows.Count > 0)
                {
                    Clipboard.SetText(CodeFormatterAndCompiler.FormatJsonPretty(OshTableFormatter.FormatJson(selRows)));
                    SetSingleLineStatus("Copied " + selRows.Count + " selected row(s) as JSON!");
                }
            };

            cms.Items.AddRange(new ToolStripItem[]
            {
                miEq, miNeq, miGroup, new ToolStripSeparator(),
                miSortDesc, miSortAsc, new ToolStripSeparator(),
                miAi, new ToolStripSeparator(),
                miCopyCell, miCopyRowJson
            });

            g.CellMouseDown += delegate(object s, DataGridViewCellMouseEventArgs me)
            {
                if (me.Button == MouseButtons.Right && me.RowIndex >= 0 && me.RowIndex < g.Rows.Count && me.ColumnIndex >= 0 && me.ColumnIndex < g.Columns.Count)
                {
                    if (!g.Rows[me.RowIndex].Selected)
                    {
                        g.ClearSelection();
                        g.Rows[me.RowIndex].Selected = true;
                    }
                    clickedCol = g.Columns[me.ColumnIndex].Name;
                    clickedVal = (g.Rows[me.RowIndex].Cells[me.ColumnIndex].Value ?? "").ToString();
                    string shortVal = clickedVal.Length > 26 ? (clickedVal.Substring(0, 23) + "...") : clickedVal;
                    miEq.Text = "Filter in OSH: where " + clickedCol + " == \"" + shortVal + "\"";
                    miNeq.Text = "Exclude in OSH: where " + clickedCol + " != \"" + shortVal + "\"";
                    miGroup.Text = "Group & Sum in OSH: group " + clickedCol;
                    miSortDesc.Text = "Sort Descending by [" + clickedCol + "]";
                    miSortAsc.Text = "Sort Ascending by [" + clickedCol + "]";
                }
            };

            g.ContextMenuStrip = cms;
        }

        private static List<OshRow> ExtractSelectedOshRowsFromGrid(DataGridView g)
        {
            List<OshRow> list = new List<OshRow>();
            if (g == null || g.SelectedRows.Count == 0) return list;
            for (int r = 0; r < g.SelectedRows.Count; r++)
            {
                DataGridViewRow dr = g.SelectedRows[r];
                OshRow orow = new OshRow();
                for (int c = 0; c < g.Columns.Count; c++)
                {
                    string cn = g.Columns[c].Name;
                    orow.Set(cn, dr.Cells[c].Value ?? "");
                }
                list.Add(orow);
            }
            return list;
        }

        internal static DataGridView MakeStyledGrid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.ShowCellToolTips = true;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.BackgroundColor = Color.FromArgb(7, 11, 19);
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Color.FromArgb(30, 41, 59);
            g.EnableHeadersVisualStyles = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 23, 42);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(56, 189, 248);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f);
            g.ColumnHeadersHeight = 28;

            g.DefaultCellStyle.BackColor = Color.FromArgb(9, 14, 25);
            g.DefaultCellStyle.ForeColor = Color.FromArgb(226, 232, 240);
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 64, 175);
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Font = new Font("Consolas", 8.6f);
            g.RowTemplate.Height = 24;
            return g;
        }

        internal static void PopulateGridWithOshRows(DataGridView g, List<OshRow> rows)
        {
            g.SuspendLayout();
            g.Rows.Clear();
            g.Columns.Clear();
            if (rows == null || rows.Count == 0) { g.ResumeLayout(); return; }

            List<string> cols = OshTableFormatter.CollectColumns(rows);
            int sampleLim = Math.Min(rows.Count, 40);
            for (int c = 0; c < cols.Count; c++)
            {
                int maxChars = Math.Max(4, cols[c].Length);
                for (int r = 0; r < sampleLim; r++)
                {
                    int cl = rows[r].GetString(cols[c]).Length;
                    if (cl > maxChars) maxChars = cl;
                }
                int idx = g.Columns.Add(cols[c], cols[c]);
                float fw = Math.Max(45f, Math.Min(240f, maxChars * 6.5f));
                g.Columns[idx].FillWeight = fw;
            }

            int limit = Math.Min(rows.Count, 500);
            for (int r = 0; r < limit; r++)
            {
                object[] cells = new object[cols.Count];
                for (int c = 0; c < cols.Count; c++)
                {
                    object v;
                    cells[c] = rows[r].Values.TryGetValue(cols[c], out v) && v != null ? v : "";
                }
                g.Rows.Add(cells);
            }
            g.ResumeLayout();
        }

        internal static Button MakeBtn(string text, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(30, 28);
            b.Padding = new Padding(6, 1, 6, 1);
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.Margin = new Padding(0, 0, 5, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.2f);
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }

        internal static Label MakeLbl(string text, Font font, Color fg)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = text;
            l.AutoSize = true;
            if (font != null) l.Font = font;
            l.ForeColor = fg;
            return l;
        }

        public string HandleStatusJson()
        {
            MemorySnapshot ms = Win32HardwareTuner.QueryMemoryStatus();
            return "{\"ok\":true,\"service\":\"OmegaShell\",\"version\":\"1.0.1-GM\",\"port\":19781,\"gemma_model\":\"" +
                OshFormat.JsonEsc(_settings.DefaultAiModel) + "\",\"gemma_endpoint\":\"" + OshFormat.JsonEsc(_settings.DefaultAiEndpoint) +
                "\",\"sessions\":" + _sessions.Count + ",\"cwd\":\"" + OshFormat.JsonEsc(ActiveWorkingDir()) + "\",\"avail_ram_mb\":" + ms.AvailPhysMb + "}";
        }

        public string HandleExecJson(string bodyJson)
        {
            string cmd = "";
            Match m = Regex.Match(bodyJson ?? "", "\"cmd\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (m.Success) cmd = OshFormat.JsonUnesc(m.Groups[1].Value);
            OshExecOutput r = _runtime.ExecutePipeline(cmd);
            return "{\"ok\":" + (r.Ok ? "true" : "false") + ",\"elapsed_ms\":" + r.ElapsedMs + ",\"sha256\":\"" + OshFormat.JsonEsc(r.Sha256Receipt) + "\",\"rows\":" + OshTableFormatter.FormatJson(r.Rows) + ",\"text\":\"" + OshFormat.JsonEsc(r.TextOutput) + "\"}";
        }

        public string HandleTunerJson()
        {
            MemorySnapshot ms = Win32HardwareTuner.QueryMemoryStatus();
            double cpu = Win32HardwareTuner.SampleCpuUsagePct();
            return "{\"ok\":true,\"version\":\"1.0.1-GM\",\"cpu_pct\":" + cpu.ToString("0.0", CultureInfo.InvariantCulture) + ",\"avail_phys_mb\":" + ms.AvailPhysMb + ",\"total_phys_mb\":" + ms.TotalPhysMb + ",\"gpu\":\"" + OshFormat.JsonEsc(Win32HardwareTuner.QueryNvidiaGpuLine()) + "\"}";
        }

        public string HandleFormatJson(string bodyJson)
        {
            string lang = "rust";
            string code = "";
            Match ml = Regex.Match(bodyJson ?? "", "\"lang\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (ml.Success) lang = OshFormat.JsonUnesc(ml.Groups[1].Value);
            Match mc = Regex.Match(bodyJson ?? "", "\"code\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mc.Success) code = OshFormat.JsonUnesc(mc.Groups[1].Value);
            string eng;
            int chg;
            string formatted = CodeFormatterAndCompiler.FormatBufferText(code, lang, _settings.EnforceZeroBacktickPs1, out eng, out chg);
            return "{\"ok\":true,\"engine\":\"" + OshFormat.JsonEsc(eng) + "\",\"changed_lines\":" + chg + ",\"formatted\":\"" + OshFormat.JsonEsc(formatted) + "\"}";
        }

        public string HandleAvfStatusJson()
        {
            List<OshRow> rows = AndroidAvfDebianBridge.BuildAvfStatusRows(_shellRoot);
            return "{\"ok\":true,\"version\":\"1.0.1-GM\",\"avf_nodes\":" + OshTableFormatter.FormatJson(rows) + "}";
        }

        public string HandleModelsJson()
        {
            List<GemmaLiveModelItem> m = OmegaShellGemmaCommander.DiscoverLiveModels(_settings.DefaultAiEndpoint, _settings.DefaultAiModel);
            List<OshRow> rows = new List<OshRow>();
            for (int i = 0; i < m.Count; i++)
                rows.Add(new OshRow().Set("provider", m[i].Provider).Set("model", m[i].ModelTag).Set("endpoint", m[i].EndpointUrl).Set("online", m[i].Online));
            return "{\"ok\":true,\"active_model\":\"" + OshFormat.JsonEsc(_settings.DefaultAiModel) + "\",\"models\":" + OshTableFormatter.FormatJson(rows) + "}";
        }

        public string HandleGemmaMissionJson(string bodyJson)
        {
            string prompt = "";
            string modelOverride = "";
            string epOverride = "";
            Match mp = Regex.Match(bodyJson ?? "", "\"prompt\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mp.Success) prompt = OshFormat.JsonUnesc(mp.Groups[1].Value);
            Match mm = Regex.Match(bodyJson ?? "", "\"model\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mm.Success) modelOverride = OshFormat.JsonUnesc(mm.Groups[1].Value);
            Match me = Regex.Match(bodyJson ?? "", "\"endpoint\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (me.Success) epOverride = OshFormat.JsonUnesc(me.Groups[1].Value);

            if (!string.IsNullOrWhiteSpace(modelOverride)) _settings.DefaultAiModel = modelOverride;
            if (!string.IsNullOrWhiteSpace(epOverride)) _settings.DefaultAiEndpoint = epOverride;

            GemmaMissionResult res = OmegaShellGemmaCommander.ExecuteAutonomousMission(_runtime, prompt, null);
            if (!string.IsNullOrEmpty(res.GeneratedFilePath) && File.Exists(res.GeneratedFilePath) && !IsDisposed)
            {
                try { BeginInvoke((Action)delegate { OpenFileInCodeStudio(res.GeneratedFilePath); }); } catch { }
            }
            return "{\"ok\":" + (res.Ok ? "true" : "false") +
                ",\"model\":\"" + OshFormat.JsonEsc(res.ModelUsed) +
                "\",\"tok_s\":" + res.TokensPerSec.ToString("0.0", CultureInfo.InvariantCulture) +
                ",\"elapsed_ms\":" + res.TotalMs +
                ",\"generated_file\":\"" + OshFormat.JsonEsc(res.GeneratedFilePath) +
                "\",\"narrative\":\"" + OshFormat.JsonEsc(res.FinalNarrative) +
                "\",\"steps\":" + OshTableFormatter.FormatJson(res.TraceRows) + "}";
        }

        public string HandleCodegenJson(string bodyJson)
        {
            string lang = "rust";
            string prompt = "";
            string openUi = "1";
            Match ml = Regex.Match(bodyJson ?? "", "\"lang\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (ml.Success) lang = OshFormat.JsonUnesc(ml.Groups[1].Value);
            Match mp = Regex.Match(bodyJson ?? "", "\"prompt\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mp.Success) prompt = OshFormat.JsonUnesc(mp.Groups[1].Value);
            Match mo = Regex.Match(bodyJson ?? "", "\"open_studio\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mo.Success) openUi = OshFormat.JsonUnesc(mo.Groups[1].Value);

            GemmaCodegenReceipt rec = OmegaShellGemmaCommander.GenerateFormatAndVerifyCode(
                _settings, _shellRoot, lang, prompt, "", "", true, _settings.AutoHealCodegen, null);

            if (openUi != "0" && File.Exists(rec.FilePath) && !IsDisposed)
            {
                try { BeginInvoke((Action)delegate { OpenFileInCodeStudio(rec.FilePath); }); } catch { }
            }

            return "{\"ok\":" + (rec.Ok ? "true" : "false") +
                ",\"lang\":\"" + OshFormat.JsonEsc(rec.Language) +
                "\",\"file\":\"" + OshFormat.JsonEsc(rec.FilePath) +
                "\",\"formatter\":\"" + OshFormat.JsonEsc(rec.FormatterEngine) +
                "\",\"exit_code\":" + rec.ExitCode +
                ",\"repairs\":" + rec.RepairIterations +
                ",\"tok_s\":" + rec.TokensPerSec.ToString("0.0", CultureInfo.InvariantCulture) +
                ",\"elapsed_ms\":" + rec.ElapsedMs +
                ",\"sha256\":\"" + OshFormat.JsonEsc(rec.Sha256Receipt) +
                "\",\"output\":\"" + OshFormat.JsonEsc(rec.BuildOutput) + "\"}";
        }

        [STAThread]
        public static void Main(string[] args)
        {
            OmegaPortableConfig.OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--stress-test", StringComparison.OrdinalIgnoreCase))
            {
                OmegaNativeWorkbench.Track01StressSummary swb = OmegaNativeWorkbench.OmegaWorkbenchStressSuite.RunAll8Vectors();
                Console.WriteLine("[OMEGASHELL+SANDBOX 8-VECTOR AUDIT] " + swb.PassedCount + "/" + swb.TotalCount + " PASS (" + swb.TotalTrials + " trials)");
                Environment.Exit(swb.AllPassed ? 0 : 1);
                return;
            }
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate { };
                AppDomain.CurrentDomain.UnhandledException += delegate { };
            }
            catch { }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Application.Run(new OmegaShellForm(root));
        }
    }
}
