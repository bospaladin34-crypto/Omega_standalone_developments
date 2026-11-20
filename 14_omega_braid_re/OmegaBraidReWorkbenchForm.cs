using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using OmegaPortableConfig;

namespace OmegaBraidRE
{
    public sealed class OmegaBraidReWorkbenchForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        public const int DefaultPort = 19794;

        private readonly OmegaHardwareProfile _hw;
        private readonly Panel _gpuPanel;
        private readonly OmegaBraidReDirectComputeGpu _gpu;
        private readonly ListView _lvFunctions;
        private readonly TabControl _tabs;
        private readonly RichTextBox _rtbSsa;
        private readonly RichTextBox _rtbStruct;
        private readonly RichTextBox _rtbDiff;
        private readonly RichTextBox _rtbPeClr;
        private readonly RichTextBox _rtbHooks;
        private SymbolResolutionReport _activeSymbols = new SymbolResolutionReport();
        private byte[] _activeRawImage = new byte[0];
        private readonly Label _lblStatus;
        private readonly System.Windows.Forms.Timer _renderTimer;
        private readonly Stopwatch _animClock = Stopwatch.StartNew();

        private List<LiftedBraidFunction> _oldFuncs = new List<LiftedBraidFunction>();
        private List<LiftedBraidFunction> _newFuncs = new List<LiftedBraidFunction>();
        private List<CrossPatchMatchEntry> _diffEntries = new List<CrossPatchMatchEntry>();
        private BinaryContainerReport _activeContainer = new BinaryContainerReport();
        private float[] _simMat64 = new float[64];
        private float _avgDeadFlagRatio = 0.833f;
        private bool _anyH1Defect = false;
        private float _topMatchSim = 1.0f;
        private string _activeSourceLabel = "Built-In 8-Function Creation-Engine Patch v1 (1.10.163) -> v2 (1.11.240.0)";

        private TcpListener _httpListener;
        private volatile bool _runningHttp;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaBraidReWorkbenchForm()
        {
            _hw = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            _gpu = new OmegaBraidReDirectComputeGpu();

            AutoScaleMode = AutoScaleMode.None;
            Text = "OmegaBraidRE v1.3.0 :: Zero-Crate PE .pdata / IAT / EAT / RIP-Strings / ECMA-335 #~ CIL Lifter & E8 Differ (:" + DefaultPort + ")";
            ClientSize = new Size(1220, 840);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(9, 13, 22);
            ForeColor = Color.White;
            KeyPreview = true;
            AllowDrop = true;

            try
            {
                string ico = Path.Combine(_hw.LabRoot, "icons", "omega_braid_re.ico");
                if (!File.Exists(ico)) ico = Path.Combine(_hw.LabRoot, "icons", "omega_sheaf.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            Label hdr = new Label();
            hdr.Text = "14. OMEGABRAIDRE v1.3.0 :: PE .PDATA + IAT/EAT + RIP-STRINGS + ECMA-335 #~ TYPEDEF/METHODDEF CIL + E8 DIFFER + SHEAF #[repr(C)]";
            hdr.SetBounds(16, 10, 1188, 20);
            hdr.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Bold, GraphicsUnit.Pixel);
            hdr.ForeColor = C(56, 189, 248);

            Button bDemoX64 = MakeBtn("8-Fn Patch v1->v2", 16, 36, 128, C(3, 105, 161));
            bDemoX64.Click += delegate { LoadBuiltInX64PatchDemo(); };

            Button bDemoPe = MakeBtn("PE .pdata+PDB Demo", 149, 36, 150, C(14, 116, 144));
            bDemoPe.Click += delegate { LoadSyntheticFullPeDemo(); };

            Button bDemoClr = MakeBtn("Self-Inspect #~ CIL", 304, 36, 144, C(15, 118, 110));
            bDemoClr.Click += delegate { LoadSingleBinaryFile(Assembly.GetExecutingAssembly().Location); _tabs.SelectedIndex = 3; };

            Button bOpenBin = MakeBtn("Open PE/ELF/PDB", 453, 36, 136, C(109, 40, 217));
            bOpenBin.Click += delegate { PromptInspectSingleBinary(); };

            Button bDiffBins = MakeBtn("Diff A vs B", 594, 36, 102, C(67, 56, 202));
            bDiffBins.Click += delegate { PromptDiffTwoBinaries(); };

            Button bAttachPid = MakeBtn("Attach Live PID", 701, 36, 126, C(190, 24, 93));
            bAttachPid.Click += delegate { PromptAttachLiveProcess(); };

            Button bHooksTab = MakeBtn("SKSE64/F4SE Hooks", 832, 36, 136, C(147, 51, 234));
            bHooksTab.Click += delegate { _tabs.SelectedIndex = 4; RenderSymbolsAndHooksText(); };

            Button bAudit = MakeBtn("16-Vec Audit", 973, 36, 108, C(5, 150, 105));
            bAudit.Click += delegate { RunInteractiveAuditPopup(); };

            Button bExport = MakeBtn("Export Hooks", 1086, 36, 118, C(180, 83, 9));
            bExport.Click += delegate { ExportRecoveredArtifacts(); };

            _gpuPanel = new Panel();
            _gpuPanel.SetBounds(16, 74, 1188, 196);
            _gpuPanel.BackColor = C(7, 11, 20);
            _gpuPanel.Paint += OnGpuPanelPaint;
            _gpuPanel.Resize += delegate { _gpu.ResizeSwapChain(_gpuPanel.ClientSize.Width, _gpuPanel.ClientSize.Height); };

            _lvFunctions = new ListView();
            _lvFunctions.SetBounds(16, 280, 448, 514);
            _lvFunctions.View = View.Details;
            _lvFunctions.FullRowSelect = true;
            _lvFunctions.GridLines = false;
            _lvFunctions.MultiSelect = false;
            _lvFunctions.BackColor = C(15, 23, 42);
            _lvFunctions.ForeColor = C(226, 232, 240);
            _lvFunctions.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _lvFunctions.Columns.Add("Symbol / Type::Method", 198);
            _lvFunctions.Columns.Add("VA / RVA", 90);
            _lvFunctions.Columns.Add("BB/Phi", 54);
            _lvFunctions.Columns.Add("Flds", 44);
            _lvFunctions.Columns.Add("H^1", 44);
            _lvFunctions.SelectedIndexChanged += OnFunctionSelectionChanged;

            _tabs = new TabControl();
            _tabs.SetBounds(472, 280, 732, 514);
            _tabs.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);

            TabPage tpSsa = new TabPage("1. SSA BraidIR + IAT/RIP");
            _rtbSsa = MakeCodeBox();
            tpSsa.Controls.Add(_rtbSsa);

            TabPage tpStruct = new TabPage("2. Sheaf #[repr(C)] Structs");
            _rtbStruct = MakeCodeBox();
            tpStruct.Controls.Add(_rtbStruct);

            TabPage tpDiff = new TabPage("3. E8 Patch Migration");
            _rtbDiff = MakeCodeBox();
            tpDiff.Controls.Add(_rtbDiff);

            TabPage tpPeClr = new TabPage("4. PE .pdata / IAT / EAT / .NET #~");
            _rtbPeClr = MakeCodeBox();
            tpPeClr.Controls.Add(_rtbPeClr);

            _tabs.TabPages.Add(tpSsa);
            _tabs.TabPages.Add(tpStruct);
            _tabs.TabPages.Add(tpDiff);
            TabPage tpHooks = new TabPage("5. PDB/DWARF + SKSE64/F4SE Hooks");
            _rtbHooks = MakeCodeBox();
            tpHooks.Controls.Add(_rtbHooks);

            _tabs.TabPages.Add(tpPeClr);
            _tabs.TabPages.Add(tpHooks);

            _lblStatus = new Label();
            _lblStatus.SetBounds(16, 804, 1188, 24);
            _lblStatus.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _lblStatus.ForeColor = C(52, 211, 153);
            _lblStatus.Text = "Initializing OmegaBraidRE v1.3.0...";

            Controls.AddRange(new Control[]
            {
                hdr, bDemoX64, bDemoPe, bDemoClr, bOpenBin, bDiffBins, bAttachPid, bHooksTab, bAudit, bExport,
                _gpuPanel, _lvFunctions, _tabs, _lblStatus
            });

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length >= 2) LoadTwoBinariesForDiff(files[0], files[1]);
                else if (files != null && files.Length == 1) LoadSingleBinaryFile(files[0]);
            };

            Shown += delegate
            {
                _gpu.Initialize(_gpuPanel.Handle, _gpuPanel.ClientSize.Width, _gpuPanel.ClientSize.Height);
                LoadBuiltInX64PatchDemo();
                StartHttpServer();
            };

            FormClosing += delegate
            {
                _runningHttp = false;
                try { if (_httpListener != null) _httpListener.Stop(); } catch { }
                _renderTimer.Stop();
                _gpu.Dispose();
            };

            _renderTimer = new System.Windows.Forms.Timer();
            _renderTimer.Interval = 33;
            _renderTimer.Tick += delegate
            {
                if (_gpu.IsGpuActive)
                {
                    _gpu.RenderBraidReSurface(
                        (float)_animClock.Elapsed.TotalSeconds,
                        _activeContainer.HistogramNorm256,
                        _simMat64,
                        _activeContainer.TextEntropyBits,
                        _avgDeadFlagRatio,
                        _anyH1Defect,
                        _topMatchSim);
                }
                else
                {
                    _gpuPanel.Invalidate();
                }
            };
            _renderTimer.Start();
        }

        private static Button MakeBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = C(51, 65, 85);
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 10.0f, FontStyle.Bold, GraphicsUnit.Pixel);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private static RichTextBox MakeCodeBox()
        {
            RichTextBox r = new RichTextBox();
            r.Dock = DockStyle.Fill;
            r.ReadOnly = true;
            r.WordWrap = false;
            r.BackColor = C(11, 17, 30);
            r.ForeColor = C(226, 232, 240);
            r.BorderStyle = BorderStyle.None;
            r.Font = new Font("Consolas", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            return r;
        }

        private void OnGpuPanelPaint(object sender, PaintEventArgs e)
        {
            if (!_gpu.IsGpuActive)
            {
                _gpu.RenderSoftwareFallback(
                    e.Graphics, _gpuPanel.ClientSize.Width, _gpuPanel.ClientSize.Height,
                    _activeContainer.HistogramNorm256, _simMat64,
                    _activeContainer.TextEntropyBits, _avgDeadFlagRatio);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys kc = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            if (shift && kc == Keys.F7)
            {
                SwitchToOrLaunchOmegaDeck();
                return true;
            }
            if (!shift && kc == Keys.F5)
            {
                RunInteractiveAuditPopup();
                return true;
            }
            if (!shift && kc == Keys.F6)
            {
                try { LoadSingleBinaryFile(Assembly.GetExecutingAssembly().Location); _tabs.SelectedIndex = 3; } catch { }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SwitchToOrLaunchOmegaDeck()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("OmegaLabLauncher");
                for (int i = 0; i < ps.Length; i++)
                {
                    IntPtr h = ps[i].MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        if (IsIconic(h)) ShowWindow(h, 9);
                        SetForegroundWindow(h);
                        return;
                    }
                }
                string deckExe = Path.Combine(_hw.LabRoot, "OmegaLabLauncher.exe");
                if (File.Exists(deckExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(deckExe);
                    psi.WorkingDirectory = _hw.LabRoot;
                    Process.Start(psi);
                }
            }
            catch { }
        }

        private void LoadBuiltInX64PatchDemo()
        {
            _activeSourceLabel = "Creation-Engine 8-Fn Patch v1 (1.10.163) -> Rebased/CET/RegSwapped v2 (1.11.240.0)";
            OmegaBraidReStressSuite.BuildDemo8FunctionPatchCorpus(out _oldFuncs, out _newFuncs);
            byte[] pe = OmegaBraidReStressSuite.BuildSyntheticFullPe32PlusImage();
            _activeContainer = OmegaBraidReCore.InspectContainer(pe);
            RefreshTablesAndViews();
        }

        private void LoadSyntheticFullPeDemo()
        {
            byte[] pe = OmegaBraidReStressSuite.BuildSyntheticFullPe32PlusImage();
            _activeRawImage = pe;
            _activeContainer = OmegaBraidReCore.InspectContainer(pe);
            _activeSymbols = OmegaBraidReSymbolsAndHooks.ParsePdbMsf7Symbols(OmegaBraidReSymbolsAndHooks.BuildSyntheticPdbMsf7(), _activeContainer);
            _oldFuncs = OmegaBraidReCore.AutoDiscoverFunctionsWithReport(pe, _activeContainer, 24);
            _newFuncs = new List<LiftedBraidFunction>(_oldFuncs);
            _activeSourceLabel = "Synthetic SovereignPerfF4SE.dll [PE32+ | .pdata=2 | EAT=2 | IAT=2 | ASCII+UTF16 RIP]";
            RefreshTablesAndViews();
        }

        private void PromptInspectSingleBinary()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Native PE (.exe/.dll), .NET CLR Assembly, or Linux/Android ELF64 (.so)";
                ofd.Filter = "Executable & Native Libraries (*.exe;*.dll;*.so;*.bin)|*.exe;*.dll;*.so;*.bin|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadSingleBinaryFile(ofd.FileName);
                }
            }
        }

        private void PromptDiffTwoBinaries()
        {
            using (OpenFileDialog ofd1 = new OpenFileDialog())
            {
                ofd1.Title = "Select Baseline / Old Patch Binary (v1)";
                ofd1.Filter = "Binaries (*.exe;*.dll;*.so)|*.exe;*.dll;*.so|All Files (*.*)|*.*";
                if (ofd1.ShowDialog(this) != DialogResult.OK) return;

                using (OpenFileDialog ofd2 = new OpenFileDialog())
                {
                    ofd2.Title = "Select Updated / Rebased Patch Binary (v2)";
                    ofd2.Filter = "Binaries (*.exe;*.dll;*.so)|*.exe;*.dll;*.so|All Files (*.*)|*.*";
                    if (ofd2.ShowDialog(this) != DialogResult.OK) return;
                    LoadTwoBinariesForDiff(ofd1.FileName, ofd2.FileName);
                }
            }
        }

        private void LoadSingleBinaryFile(string path)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                _activeRawImage = raw;
                _activeContainer = OmegaBraidReCore.InspectContainer(raw);
                ResolveSymbolsForLoadedBinary(path, raw, _activeContainer);
                _oldFuncs = OmegaBraidReCore.AutoDiscoverFunctionsWithReport(raw, _activeContainer, 64);
                _newFuncs = new List<LiftedBraidFunction>(_oldFuncs);
                string modeTag = _activeContainer.ClrInfo.IsClrAssembly
                    ? string.Format(CultureInfo.InvariantCulture, "CLR {0} | #~ Types={1} Methods={2}",
                        _activeContainer.ClrInfo.MetadataVersion, _activeContainer.ClrInfo.TypeDefCount, _activeContainer.ClrInfo.MethodDefCount)
                    : string.Format(CultureInfo.InvariantCulture, "{0} | .pdata={1} | IAT={2} | EAT={3}",
                        _activeContainer.Isa, _activeContainer.PdataFunctions.Count, _activeContainer.Imports.Count, _activeContainer.Exports.Count);
                _activeSourceLabel = Path.GetFileName(path) + " [" + _activeContainer.Format + " | " + modeTag + "]";
                RefreshTablesAndViews();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to inspect binary: " + ex.Message, "OmegaBraidRE v1.3.0", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LoadTwoBinariesForDiff(string oldPath, string newPath)
        {
            try
            {
                byte[] rawOld = File.ReadAllBytes(oldPath);
                byte[] rawNew = File.ReadAllBytes(newPath);
                BinaryContainerReport repOld = OmegaBraidReCore.InspectContainer(rawOld);
                _activeContainer = OmegaBraidReCore.InspectContainer(rawNew);
                _oldFuncs = OmegaBraidReCore.AutoDiscoverFunctionsWithReport(rawOld, repOld, 48);
                _newFuncs = OmegaBraidReCore.AutoDiscoverFunctionsWithReport(rawNew, _activeContainer, 48);
                _activeSourceLabel = Path.GetFileName(oldPath) + " -> " + Path.GetFileName(newPath);
                RefreshTablesAndViews();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to diff binaries: " + ex.Message, "OmegaBraidRE v1.3.0", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshTablesAndViews()
        {
            _diffEntries = OmegaBraidReCore.DiffFunctionTables(_oldFuncs, _newFuncs);
            for (int i = 0; i < 64; i++) _simMat64[i] = -0.25f;

            int n = Math.Min(8, Math.Min(_oldFuncs.Count, _newFuncs.Count));
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int d;
                    float cos;
                    _simMat64[r * 8 + c] = OmegaBraidReCore.ScoreCrossPatchSimilarity(_oldFuncs[r], _newFuncs[c], out d, out cos);
                }
            }

            int totSet = 0, totKilled = 0;
            _anyH1Defect = false;
            _lvFunctions.BeginUpdate();
            _lvFunctions.Items.Clear();
            for (int i = 0; i < _oldFuncs.Count; i++)
            {
                LiftedBraidFunction fn = _oldFuncs[i];
                totSet += fn.TotalFlagsSet;
                totKilled += fn.DeadFlagsKilled;
                if (fn.H1DefectCount > 0) _anyH1Defect = true;

                ListViewItem it = new ListViewItem(fn.Name);
                it.SubItems.Add("0x" + fn.BaseRva.ToString("X", CultureInfo.InvariantCulture));
                it.SubItems.Add(fn.Blocks.Count + "/" + fn.PhiInserted);
                it.SubItems.Add(fn.Fields.Count.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add(fn.H1DefectCount == 0 ? "0 OK" : (fn.H1DefectCount + " DEF"));
                it.Tag = fn;
                _lvFunctions.Items.Add(it);
            }
            _lvFunctions.EndUpdate();

            _avgDeadFlagRatio = (totSet > 0) ? ((float)totKilled / (float)totSet) : 0.833f;
            _topMatchSim = (_diffEntries.Count > 0) ? _diffEntries[0].CombinedScore : 1.0f;

            RenderDiffReportText();
            RenderPeAndClrTelemetryText();
            RenderSymbolsAndHooksText();
            if (_lvFunctions.Items.Count > 0) _lvFunctions.Items[0].Selected = true;

            // Keep live GUI working set tightly trimmed well below the 70 MB ceiling!
            OmegaBraidReDirectComputeGpu.TrimWorkingSetNow();

            double ramMb;
            using (Process p = Process.GetCurrentProcess()) { ramMb = p.WorkingSet64 / (1024.0 * 1024.0); }
            _lblStatus.Text = string.Format(CultureInfo.InvariantCulture,
                "[{0}] Funcs: {1} | DeadFlags: {2:0.0}% | RAM: {3:0.0} MB | :{4} LIVE | [Shift+F7=Deck] | GPU: {5}",
                _activeSourceLabel, _oldFuncs.Count, _avgDeadFlagRatio * 100.0f, ramMb, DefaultPort, _hw.PrimaryGpuName);
        }

        private void OnFunctionSelectionChanged(object sender, EventArgs e)
        {
            if (_lvFunctions.SelectedItems.Count == 0) return;
            LiftedBraidFunction fn = _lvFunctions.SelectedItems[0].Tag as LiftedBraidFunction;
            if (fn == null) return;

            StringBuilder sb = new StringBuilder(4096);
            string pdataNote = fn.DiscoveredViaPdata
                ? string.Format(CultureInfo.InvariantCulture, " | .pdata Exact Bounds (Prolog={0}B)", fn.PdataPrologSize)
                : "";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "=== SSA BRAIDIR LIFT: {0} @ 0x{1:X} ({2}, {3} bytes, {4} blocks, {5} edges{6}) ===",
                fn.Name, fn.BaseRva, fn.Isa, fn.ByteLength, fn.Blocks.Count, fn.EdgeCount, pdataNote));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "E8 Topo Root (2x): [{0}] | E8 Data Root (2x): [{1}] | Wildcard AOB: {2}",
                FormatRoot(fn.E8TopoRoot2x), FormatRoot(fn.E8DataRoot2x), fn.WildcardAobSignature));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Reidemeister Dead-Flag Elimination: {0}/{1} flag writes stripped", fn.DeadFlagsKilled, fn.TotalFlagsSet));
            if (fn.ImportedCalls.Count > 0)
            {
                sb.AppendLine("Resolved Callees     : " + string.Join(", ", fn.ImportedCalls.ToArray()));
            }
            if (fn.ReferencedStrings.Count > 0)
            {
                sb.AppendLine("Resolved Strings     : " + string.Join(", ", fn.ReferencedStrings.ToArray()));
            }
            sb.AppendLine();

            for (int b = 0; b < fn.Blocks.Count; b++)
            {
                BraidBasicBlockNode bb = fn.Blocks[b];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "--- BasicBlock BB{0} [0x{1:X2}..0x{2:X2}) | Preds={3} | Succs=({4}, {5}){6} ---",
                    bb.Id, bb.StartOffset, bb.EndOffset, bb.PredCount, bb.Succ0, bb.Succ1,
                    bb.HasPhiMerge ? " | [SSA PHI MERGE INSERTED]" : ""));
                for (int i = bb.InsnStart; i < bb.InsnStart + bb.InsnCount; i++)
                {
                    BraidSsaInstruction ins = fn.Instructions[i];
                    string ssaTag = (ins.SsaDstVer > 0)
                        ? string.Format(CultureInfo.InvariantCulture, "v{0} <- ", ins.SsaDstVer)
                        : "       ";
                    string flagTag = ins.DeadFlagsEliminated
                        ? "  // [DEAD-FLAGS ELIMINATED]"
                        : (ins.UsesFlags ? "  // [CONSUMES LIVE FLAGS]" : (ins.SetsFlags ? "  // [SETS LIVE FLAGS]" : ""));
                    string annTag = (ins.ResolvedAnnotation.Length > 0)
                        ? ("  // [" + ins.ResolvedAnnotation + "]")
                        : "";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  +0x{0:X2}  {1,-8} {2,-12} {3}{4}{5}",
                        ins.Offset, ssaTag, ins.Op, ins.MnemonicText, annTag, flagTag));
                }
                sb.AppendLine();
            }
            _rtbSsa.Text = sb.ToString();

            uint alignedSz;
            string safeName = MakeIdentifier(fn.Name) + "Frame";
            _rtbStruct.Text = OmegaBraidReCore.SynthesizePolyglotStructHeaders(fn, safeName, out alignedSz);
            RenderSymbolsAndHooksText();
        }

        private void RenderDiffReportText()
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("=== CONWAY-SLOANE E8 (240-ROOT) + 16D CFG CROSS-PATCH ADDRESS LIBRARY MIGRATION ===");
            sb.AppendLine("Source: " + _activeSourceLabel);
            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-32} {1,-14} -> {2,-14} {3,-10} {4,-8} {5}",
                "SYMBOL (PATCH V1)", "OLD RVA", "NEW RVA", "DELTA", "SCORE", "E8x2 DOT"));
            sb.AppendLine(new string('-', 96));

            for (int i = 0; i < _diffEntries.Count; i++)
            {
                CrossPatchMatchEntry m = _diffEntries[i];
                string deltaStr = (m.RvaDelta >= 0)
                    ? ("+0x" + m.RvaDelta.ToString("X", CultureInfo.InvariantCulture))
                    : ("-0x" + (-m.RvaDelta).ToString("X", CultureInfo.InvariantCulture));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-32} 0x{1,-12:X} -> 0x{2,-12:X} {3,-10} {4,6:0.0000}   {5,3}/16",
                    m.OldName, m.OldRva, m.NewRva, deltaStr, m.CombinedScore, m.DualE8Dot));
                sb.AppendLine("    Wildcard AOB (v2): " + m.MigratedAob);
            }
            _rtbDiff.Text = sb.ToString();
        }

        private void RenderPeAndClrTelemetryText()
        {
            BinaryContainerReport r = _activeContainer;
            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine("=== PE32+ / ELF64 CONTAINER, .PDATA UNWIND, IAT/EAT & ECMA-335 #~ .NET CLR INSPECTOR ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Format: {0} | Active ISA: {1} | ImageBase: 0x{2:X} | EntryRVA: 0x{3:X} | .text Entropy: {4:0.000} bits/byte",
                r.Format, r.Isa, r.ImageBase, r.EntryRva, r.TextEntropyBits));
            sb.AppendLine();

            if (r.ClrInfo.IsClrAssembly)
            {
                ClrAssemblyReport c = r.ClrInfo;
                sb.AppendLine("--- [ECMA-335 .NET CLR / MSIL MANAGED ASSEMBLY DETECTED] ---");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  Module             : {0} | CLR Runtime v{1}.{2} | Metadata RVA=0x{3:X} ({4} bytes)",
                    c.ModuleName.Length > 0 ? c.ModuleName : "(module)", c.MajorRuntime, c.MinorRuntime, c.MetaDataRva, c.MetaDataSize));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  EntryPoint Token   : 0x{0:X8} -> {1}",
                    c.EntryPointToken, c.EntryPointFullName.Length > 0 ? c.EntryPointFullName : "(DLL / None)"));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  BSJB & #~ Tables   : {0} ({1}) | TypeDef={2} | FieldDef={3} | MethodDef={4} | IL_ONLY={5}",
                    c.BsjbVerified ? "VERIFIED" : "NONE", c.MetadataVersion, c.TypeDefCount, c.FieldDefCount, c.MethodDefCount, c.IlOnly));
                for (int i = 0; i < c.Streams.Count; i++)
                {
                    ClrStreamDesc sd = c.Streams[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    Stream[{0}] {1,-10} Offset=+0x{2:X6}  Size={3,8} bytes", i, sd.Name, sd.Offset, sd.Size));
                }
                if (c.UserStringSamples.Count > 0)
                {
                    sb.AppendLine("  #US User Literals  : \"" + string.Join("\", \"", c.UserStringSamples.ToArray()) + "\"");
                }
                if (c.Methods.Count > 0)
                {
                    sb.AppendLine();
                    int shown = 0;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "--- ECMA-335 #~ METHODDEF TABLE (0x06) + TYPEDEF (0x02) OWNERS (top user methods of {0}) ---",
                        c.MethodDefCount));
                    for (int i = 0; i < c.Methods.Count && shown < 36; i++)
                    {
                        ClrMethodDefEntry md = c.Methods[i];
                        if (md.Rva == 0u || (md.IsCompilerGenerated && !md.IsEntryPoint)) continue;
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  Token 0x{0:X8}  RVA=0x{1:X8}  Flags=0x{2:X4}  {3}{4}",
                            md.Token, md.Rva, md.Flags, md.FullName, md.IsEntryPoint ? "  [ENTRYPOINT]" : ""));
                        shown++;
                    }
                }
                sb.AppendLine();
            }

            if (r.Sections.Count > 0)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "--- PE SECTION TABLE ({0} sections) ---", r.Sections.Count));
                for (int i = 0; i < r.Sections.Count; i++)
                {
                    PeSectionDesc s = r.Sections[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  [{0,2}] {1,-10} RVA=0x{2:X8}  VirtSize=0x{3:X8}  RawOff=0x{4:X8}  RawSize=0x{5:X8}  Flags=0x{6:X8}",
                        i, s.Name, s.VirtualAddress, s.VirtualSize, s.RawOffset, s.RawSize, s.Characteristics));
                }
                sb.AppendLine();
            }

            if (r.PdataFunctions.Count > 0)
            {
                int showPd = Math.Min(32, r.PdataFunctions.Count);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "--- .PDATA x64 RUNTIME_FUNCTION UNWIND TABLE (showing {0} of {1} exact functions) ---",
                    showPd, r.PdataFunctions.Count));
                for (int i = 0; i < showPd; i++)
                {
                    PdataFunctionEntry pf = r.PdataFunctions[i];
                    string sym;
                    string tag = r.ExportRvaToName.TryGetValue(pf.BeginRva, out sym) ? (" [" + sym + "]") : "";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  [0x{0:X8} .. 0x{1:X8})  Len={2,5}B  UnwindRVA=0x{3:X8}  Prolog={4,3}B  Flags=0x{5:X2}{6}",
                        pf.BeginRva, pf.EndRva, pf.EndRva - pf.BeginRva, pf.UnwindInfoRva, pf.PrologSize, pf.UnwindFlags, tag));
                }
                sb.AppendLine();
            }

            if (r.Exports.Count > 0)
            {
                int showEx = Math.Min(32, r.Exports.Count);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "--- EXPORT ADDRESS TABLE (EAT: {0} | showing {1} of {2}) ---",
                    r.ExportModuleName, showEx, r.Exports.Count));
                for (int i = 0; i < showEx; i++)
                {
                    PeExportEntry ex = r.Exports[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  Ord #{0,-4}  RVA=0x{1:X8}  {2}", ex.Ordinal, ex.FunctionRva, ex.Name));
                }
                sb.AppendLine();
            }

            if (r.Imports.Count > 0)
            {
                int showIm = Math.Min(48, r.Imports.Count);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "--- IMPORT ADDRESS TABLE (IAT: showing {0} of {1} imported APIs) ---",
                    showIm, r.Imports.Count));
                for (int i = 0; i < showIm; i++)
                {
                    PeImportEntry im = r.Imports[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  IAT[0x{0:X8}] -> {1}!{2} (Hint={3})",
                        im.IatSlotRva, im.DllName, im.FunctionName, im.HintOrOrdinal));
                }
            }

            _rtbPeClr.Text = sb.ToString();
        }

        private static string FormatRoot(sbyte[] r)
        {
            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < r.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(r[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string MakeIdentifier(string raw)
        {
            int brk = raw.IndexOf(' ');
            string clean = (brk > 0) ? raw.Substring(0, brk) : raw;
            StringBuilder sb = new StringBuilder(clean.Length);
            for (int i = 0; i < clean.Length; i++)
            {
                char c = clean[i];
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ':') sb.Append('_');
            }
            string s = sb.ToString().Trim('_');
            return s.Length > 0 ? s : "RecoveredObject";
        }

        private void ResolveSymbolsForLoadedBinary(string path, byte[] raw, BinaryContainerReport rep)
        {
            _activeSymbols = new SymbolResolutionReport();
            try
            {
                if (rep.Format == BraidContainerKind.Elf64)
                {
                    _activeSymbols = OmegaBraidReSymbolsAndHooks.ParseElfAndDwarfSymbols(raw);
                    return;
                }
                CodeViewRsdsInfo rsds = OmegaBraidReSymbolsAndHooks.ParsePeCodeViewRsds(raw, rep);
                _activeSymbols.Rsds = rsds;
                string siblingPdb = Path.ChangeExtension(path, ".pdb");
                if (!string.IsNullOrEmpty(siblingPdb) && File.Exists(siblingPdb))
                {
                    SymbolResolutionReport pdbRep = OmegaBraidReSymbolsAndHooks.ParsePdbMsf7Symbols(File.ReadAllBytes(siblingPdb), rep);
                    pdbRep.Rsds = rsds;
                    _activeSymbols = pdbRep;
                }
                else if (rsds.Found && !string.IsNullOrEmpty(rsds.PdbPath) && File.Exists(rsds.PdbPath))
                {
                    SymbolResolutionReport pdbRep = OmegaBraidReSymbolsAndHooks.ParsePdbMsf7Symbols(File.ReadAllBytes(rsds.PdbPath), rep);
                    pdbRep.Rsds = rsds;
                    _activeSymbols = pdbRep;
                }
            }
            catch { }
        }

        private void PromptAttachLiveProcess()
        {
            List<LiveProcessItem> procs = OmegaBraidReSymbolsAndHooks.ListAttachableProcesses();
            if (procs.Count == 0)
            {
                MessageBox.Show(this, "No readable processes enumerated.", "OmegaBraidRE v1.3.0", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (Form dlg = new Form())
            {
                dlg.Text = "Attach Read-Only to Live Process Memory (ReadProcessMemory)";
                dlg.ClientSize = new Size(640, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = C(15, 23, 42);
                dlg.ForeColor = Color.White;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                ListBox lb = new ListBox();
                lb.SetBounds(12, 12, 616, 356);
                lb.BackColor = C(9, 13, 22);
                lb.ForeColor = C(226, 232, 240);
                lb.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
                int selfIdx = 0;
                int myPid;
                using (Process me = Process.GetCurrentProcess()) { myPid = me.Id; }
                for (int i = 0; i < procs.Count; i++)
                {
                    lb.Items.Add(procs[i]);
                    if (procs[i].Pid == myPid) selfIdx = i;
                }
                lb.SelectedIndex = selfIdx;

                Button bOk = MakeBtn("Attach Read-Only", 360, 378, 150, C(190, 24, 93));
                bOk.DialogResult = DialogResult.OK;
                Button bCan = MakeBtn("Cancel", 518, 378, 110, C(51, 65, 85));
                bCan.DialogResult = DialogResult.Cancel;
                dlg.AcceptButton = bOk;
                dlg.CancelButton = bCan;
                dlg.Controls.AddRange(new Control[] { lb, bOk, bCan });

                if (dlg.ShowDialog(this) == DialogResult.OK && lb.SelectedItem is LiveProcessItem)
                {
                    LiveProcessItem sel = (LiveProcessItem)lb.SelectedItem;
                    LiveMemorySnapshot snap = OmegaBraidReSymbolsAndHooks.ReadLiveProcessPeImage(sel.Pid, 4 * 1024 * 1024);
                    if (!snap.Success)
                    {
                        MessageBox.Show(this, snap.ErrorMessage, "Live Read-Only Attach", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _activeRawImage = snap.PeImageBuffer;
                    _activeContainer = OmegaBraidReCore.InspectContainer(snap.PeImageBuffer);
                    ResolveSymbolsForLoadedBinary(sel.ModulePath, snap.PeImageBuffer, _activeContainer);
                    _oldFuncs = OmegaBraidReCore.AutoDiscoverFunctionsWithReport(snap.PeImageBuffer, _activeContainer, 64);
                    _newFuncs = new List<LiftedBraidFunction>(_oldFuncs);
                    _activeSourceLabel = string.Format(CultureInfo.InvariantCulture,
                        "LIVE PID {0} ({1} @ 0x{2:X} | {3} KB read)", sel.Pid, snap.ProcessName, snap.BaseAddress, snap.BytesRead / 1024);
                    RefreshTablesAndViews();
                    _tabs.SelectedIndex = 3;
                }
            }
        }

        private void RenderSymbolsAndHooksText()
        {
            if (_rtbHooks == null) return;
            StringBuilder sb = new StringBuilder(3072);
            sb.AppendLine("=== OMEGABRAIDRE v1.3.0 :: PDB MSF 7.00 / DWARF SYMBOLS & MOD CITY SKSE64/F4SE HOOK CODEGEN ===");
            if (_activeSymbols.Rsds != null && _activeSymbols.Rsds.Found)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "PE CodeView RSDS : GUID={0} | Age={1} | PDB={2}",
                    _activeSymbols.Rsds.GuidHex, _activeSymbols.Rsds.Age, _activeSymbols.Rsds.PdbPath));
            }
            if (!string.IsNullOrEmpty(_activeSymbols.SummaryLine))
            {
                sb.AppendLine("Debug Symbols    : " + _activeSymbols.SummaryLine);
            }
            if (_activeSymbols.Symbols.Count > 0)
            {
                int showN = Math.Min(24, _activeSymbols.Symbols.Count);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "--- RESOLVED DEBUG SYMBOLS (showing {0} of {1}) ---", showN, _activeSymbols.Symbols.Count));
                for (int i = 0; i < showN; i++)
                {
                    DebugSymbolEntry ds = _activeSymbols.Symbols[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  [{0,-13}] RVA/VA=0x{1:X8}  Len=0x{2:X4}  {3}", ds.Source, ds.RvaOrVa, ds.CodeSize, ds.Name));
                }
                sb.AppendLine();
            }

            LiftedBraidFunction fn = (_lvFunctions.SelectedItems.Count > 0)
                ? (_lvFunctions.SelectedItems[0].Tag as LiftedBraidFunction)
                : (_oldFuncs.Count > 0 ? _oldFuncs[0] : null);

            byte[] sampleCode = OmegaBraidReStressSuite.GetSampleActorV1Bytes();
            string fnName = (fn != null) ? fn.Name : "Actor::UpdatePhysics";
            ulong fnRva = (fn != null) ? fn.BaseRva : 0x12000UL;
            if (fn != null && fn.BaseRva >= _activeContainer.ImageBase && _activeContainer.ImageBase > 0UL)
            {
                fnRva = fn.BaseRva - _activeContainer.ImageBase;
            }
            int fileOff;
            if (_activeRawImage != null && _activeRawImage.Length > 64 && fnRva > 0UL &&
                OmegaBraidRePeAndClr.TryRvaToFileOffset(_activeRawImage, _activeContainer.Sections, (uint)fnRva, out fileOff) &&
                fileOff + 32 <= _activeRawImage.Length)
            {
                sampleCode = new byte[32];
                Buffer.BlockCopy(_activeRawImage, fileOff, sampleCode, 0, 32);
            }

            TrampolineProloguePlan plan = OmegaBraidReSymbolsAndHooks.AnalyzeTrampolinePrologue(fnName, fnRva, sampleCode);
            sb.AppendLine("--- x86_64 INSTRUCTION-BOUNDARY PROLOGUE ANALYSIS ---");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Target: {0} @ RVA 0x{1:X} | 5B-Steal={2}B ({3} insns) | 14B-Steal={4}B ({5} insns) | RIP-Rel={6}",
                plan.FunctionName, plan.TargetRva, plan.Rel32StealBytes, plan.StolenInsnCount5,
                plan.Abs64StealBytes, plan.StolenInsnCount14, plan.HasRipRelativeInSteal14 ? "YES (RELOC)" : "NONE"));
            sb.AppendLine("Strategy: " + plan.SafeHookStrategy);
            for (int i = 0; i < plan.DisassembledPrologue.Count; i++) sb.AppendLine("  " + plan.DisassembledPrologue[i]);
            sb.AppendLine();
            sb.AppendLine(OmegaBraidReSymbolsAndHooks.EmitSkse64F4seCpp23Plugin(fn, plan, "SkyrimSE / Fallout4"));
            sb.AppendLine();
            sb.AppendLine(OmegaBraidReSymbolsAndHooks.EmitSkse64F4seRustNoStdPlugin(fn, plan));
            _rtbHooks.Text = sb.ToString();
        }

        private void RunInteractiveAuditPopup()
        {
            BraidStressSummary sum = OmegaBraidReStressSuite.RunAll16Vectors();
            StringBuilder sb = new StringBuilder(1792);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "OMEGABRAIDRE v1.3.0 AUDIT: {0}/{1} PASSED ({2} trials in {3:0.0} ms | Trimmed RAM: {4:0.0} MB)",
                sum.PassedCount, sum.TotalCount, sum.TotalTrials, sum.TotalMs, sum.TrimmedWorkingSetMb));
            sb.AppendLine();
            for (int i = 0; i < sum.Vectors.Count; i++)
            {
                BraidStressVector v = sum.Vectors[i];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "[{0}] {1}: {2}",
                    v.Passed ? "PASS" : "FAIL", v.Code, v.Detail));
            }
            MessageBox.Show(this, sb.ToString(), "OmegaBraidRE v1.3.0 16-Vector Verification", MessageBoxButtons.OK,
                sum.AllPassed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ExportRecoveredArtifacts()
        {
            try
            {
                string outDir = Path.Combine(_hw.LabRoot, "downloads", "omega_braid_re_exports");
                Directory.CreateDirectory(outDir);
                UTF8Encoding utf8NoBom = new UTF8Encoding(false);

                File.WriteAllText(Path.Combine(outDir, "recovered_sheaf_structs.rs"), _rtbStruct.Text, utf8NoBom);
                File.WriteAllText(Path.Combine(outDir, "lifted_ssa_braidir_dump.txt"), _rtbSsa.Text, utf8NoBom);
                File.WriteAllText(Path.Combine(outDir, "pe_pdata_iat_eat_clr_report.txt"), _rtbPeClr.Text, utf8NoBom);
                File.WriteAllText(Path.Combine(outDir, "omega_skse64_f4se_hooks_and_symbols.hpp"), _rtbHooks.Text, utf8NoBom);

                StringBuilder jb = new StringBuilder(2048);
                jb.AppendLine("{");
                jb.AppendLine("  \"tool\": \"OmegaBraidRE v1.3.0\",");
                jb.AppendLine("  \"migrations\": [");
                for (int i = 0; i < _diffEntries.Count; i++)
                {
                    CrossPatchMatchEntry m = _diffEntries[i];
                    jb.Append(string.Format(CultureInfo.InvariantCulture,
                        "    {{ \"symbol\": \"{0}\", \"old_rva\": \"0x{1:X}\", \"new_rva\": \"0x{2:X}\", \"score\": {3:0.0000}, \"e8_dot\": {4}, \"aob\": \"{5}\" }}",
                        m.OldName, m.OldRva, m.NewRva, m.CombinedScore, m.DualE8Dot, m.MigratedAob));
                    jb.AppendLine(i + 1 < _diffEntries.Count ? "," : "");
                }
                jb.AppendLine("  ]");
                jb.AppendLine("}");
                File.WriteAllText(Path.Combine(outDir, "address_library_migration.json"), jb.ToString(), utf8NoBom);

                Process.Start("explorer.exe", "\"" + outDir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export error: " + ex.Message, "OmegaBraidRE v1.3.0", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void StartHttpServer()
        {
            _runningHttp = true;
            Thread t = new Thread(delegate()
            {
                try
                {
                    _httpListener = new TcpListener(IPAddress.Loopback, DefaultPort);
                    _httpListener.Start();
                    while (_runningHttp)
                    {
                        TcpClient cli = _httpListener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleHttpClient, cli);
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void HandleHttpClient(object state)
        {
            TcpClient cli = state as TcpClient;
            if (cli == null) return;
            try
            {
                cli.ReceiveTimeout = 1500;
                cli.SendTimeout = 1500;
                using (NetworkStream ns = cli.GetStream())
                {
                    byte[] reqBuf = new byte[2048];
                    int n = ns.Read(reqBuf, 0, reqBuf.Length);
                    string req = (n > 0) ? Encoding.ASCII.GetString(reqBuf, 0, n) : "";
                    string path = "/api/status";
                    int sp1 = req.IndexOf(' ');
                    int sp2 = (sp1 > 0) ? req.IndexOf(' ', sp1 + 1) : -1;
                    if (sp1 > 0 && sp2 > sp1) path = req.Substring(sp1 + 1, sp2 - sp1 - 1);

                    string body;
                    if (path.StartsWith("/api/audit", StringComparison.OrdinalIgnoreCase))
                    {
                        BraidStressSummary sum = OmegaBraidReStressSuite.RunAll13Vectors();
                        body = string.Format(CultureInfo.InvariantCulture,
                            "{{\"station\":\"14_omega_braid_re\",\"version\":\"1.2.0\",\"port\":{0},\"all_passed\":{1},\"passed\":{2},\"total\":{3},\"trials\":{4},\"ram_mb\":{5:0.0}}}",
                            DefaultPort, sum.AllPassed ? "true" : "false", sum.PassedCount, sum.TotalCount, sum.TotalTrials, sum.TrimmedWorkingSetMb);
                    }
                    else
                    {
                        body = string.Format(CultureInfo.InvariantCulture,
                            "{{\"station\":\"14_omega_braid_re\",\"version\":\"1.2.0\",\"port\":{0},\"online\":true,\"measured_live\":true,\"isa\":\"{1}\",\"pdata_count\":{2},\"imports\":{3},\"exports\":{4},\"is_clr\":{5},\"typedef_count\":{6},\"methoddef_count\":{7},\"functions_loaded\":{8},\"dead_flag_strip_ratio\":{9:0.000},\"h1_zero\":{10}}}",
                            DefaultPort, _activeContainer.Isa, _activeContainer.PdataFunctions.Count, _activeContainer.Imports.Count, _activeContainer.Exports.Count,
                            _activeContainer.ClrInfo.IsClrAssembly ? "true" : "false", _activeContainer.ClrInfo.TypeDefCount, _activeContainer.ClrInfo.MethodDefCount,
                            _oldFuncs.Count, _avgDeadFlagRatio, _anyH1Defect ? "false" : "true");
                    }

                    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                    string hdr = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "
                        + bodyBytes.Length.ToString(CultureInfo.InvariantCulture)
                        + "\r\nConnection: close\r\n\r\n";
                    byte[] hdrBytes = Encoding.ASCII.GetBytes(hdr);
                    ns.Write(hdrBytes, 0, hdrBytes.Length);
                    ns.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }
            catch { }
            finally
            {
                try { cli.Close(); } catch { }
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--stress-test", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            BraidStressSummary sum = OmegaBraidReStressSuite.RunAll16Vectors();
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[OMEGABRAIDRE v1.3.0 16-VECTOR AUDIT] {0}/{1} PASS | {2} trials | RAM={3:0.0} MB ({4:0.0} ms)",
                                sum.PassedCount, sum.TotalCount, sum.TotalTrials, sum.TrimmedWorkingSetMb, sum.TotalMs));
                            for (int k = 0; k < sum.Vectors.Count; k++)
                            {
                                BraidStressVector v = sum.Vectors[k];
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "  [{0}] {1,-30} -> {2}", v.Passed ? "PASS" : "FAIL", v.Code, v.Detail));
                            }
                            Console.Out.Flush();
                            return sum.AllPassed ? 0 : 1;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("[OMEGABRAIDRE STRESS EXCEPTION] " + ex.ToString());
                            Console.Error.Flush();
                            return 2;
                        }
                    }
                }
            }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaBraidReWorkbenchForm());
            return 0;
        }
    }
}
