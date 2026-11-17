using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaShellStudio
{
    public sealed partial class OmegaShellForm
    {
        private const int WM_SETREDRAW = 0x000B;

        private ListBox _lstCodeWorkspaceFiles;
        private RichTextBox _rtbCodeEditor, _rtbCodeGutter, _rtbCodeBuildLog;
        private DataGridView _gridCodeDiagnostics;
        private ComboBox _cmbCodeLang, _cmbCodeGemmaModel;
        private TextBox _txtCodegenPrompt;
        private CheckBox _chkFmtOnSave, _chkZeroBacktickPs1, _chkAutoHealCodegen;
        private Label _lblCodeFileBanner, _lblCodeCursorStat;
        private string _activeCodeFilePath = "";
        private string _diskSavedSnapshot = "";
        private bool _suppressHighlight = false;
        private List<string> _codeQuickPaths = new List<string>();

        private void BuildTab2CodeStudioUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            FlowLayoutPanel topBar = new FlowLayoutPanel();
            topBar.Dock = DockStyle.Top;
            topBar.AutoSize = true;
            topBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            topBar.MinimumSize = new Size(0, 36);
            topBar.Padding = new Padding(8, 4, 8, 4);
            topBar.WrapContents = true;
            topBar.BackColor = Color.FromArgb(14, 21, 38);

            Label lLang = MakeLbl("Lang:", null, Color.FromArgb(148, 163, 184));
            lLang.Padding = new Padding(0, 5, 3, 0);

            _cmbCodeLang = new ComboBox();
            _cmbCodeLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCodeLang.Width = 100;
            _cmbCodeLang.BackColor = Color.FromArgb(6, 9, 17);
            _cmbCodeLang.ForeColor = Color.White;
            _cmbCodeLang.Items.AddRange(new object[] { "rust", "csharp", "cpp", "typescript", "lean4", "powershell", "hlsl", "json", "osh", "bash" });
            _cmbCodeLang.SelectedIndex = 0;
            _cmbCodeLang.SelectedIndexChanged += delegate { ApplySyntaxHighlightToCodeBuffer(); };

            Button bOpen = MakeBtn("Open...", Color.FromArgb(2, 132, 199));
            bOpen.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.InitialDirectory = ActiveWorkingDir();
                    ofd.Filter = "All Code Files (*.rs;*.cs;*.cpp;*.c;*.h;*.ts;*.js;*.lean;*.ps1;*.hlsl;*.omsh;*.json;*.osh;*.sh)|*.rs;*.cs;*.cpp;*.c;*.h;*.ts;*.js;*.lean;*.ps1;*.hlsl;*.omsh;*.json;*.osh;*.sh|All Files (*.*)|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK) OpenFileInCodeStudio(ofd.FileName);
                }
            };

            Button bSave = MakeBtn("Save (Ctrl+S)", Color.FromArgb(5, 150, 105));
            bSave.Click += delegate { SaveActiveCodeBufferToDisk(false); };

            Button bFmtBuf = MakeBtn("Auto-Fmt (Ctrl+Shift+F)", Color.FromArgb(109, 40, 217));
            bFmtBuf.Click += delegate { FormatActiveCodeBufferInPlace(); };

            Button bFmtDir = MakeBtn("Fmt Folder...", Color.FromArgb(88, 28, 135));
            bFmtDir.Click += async delegate { await FormatPickedDirectoryAsync(); };

            Button bCheck = MakeBtn("Check (F6)", Color.FromArgb(180, 83, 9));
            bCheck.Click += async delegate { await BuildOrRunActiveCodeAsync(true); };

            Button bRun = MakeBtn("Compile & Run (F5)", Color.FromArgb(6, 95, 70));
            bRun.Click += async delegate { await BuildOrRunActiveCodeAsync(false); };

            Button bDiff = MakeBtn("Diff Disk (F8)", Color.FromArgb(30, 58, 138));
            bDiff.Click += delegate { ShowDiffAgainstDiskSnapshot(); };

            Button bSyncDeb = MakeBtn("Push -> @deb", Color.FromArgb(15, 118, 110));
            bSyncDeb.Click += async delegate { await SyncActiveCodeToPixelDebianAsync(); };

            _chkFmtOnSave = new CheckBox();
            _chkFmtOnSave.Text = "Fmt on Save";
            _chkFmtOnSave.AutoSize = true;
            _chkFmtOnSave.Padding = new Padding(4, 4, 3, 0);
            _chkFmtOnSave.ForeColor = Color.FromArgb(52, 211, 153);
            _chkFmtOnSave.Checked = _settings.FormatOnSave;
            _chkFmtOnSave.CheckedChanged += delegate { _settings.FormatOnSave = _chkFmtOnSave.Checked; _settings.SaveToDisk(_configDir); };

            _chkZeroBacktickPs1 = new CheckBox();
            _chkZeroBacktickPs1.Text = "0-Backtick .ps1";
            _chkZeroBacktickPs1.AutoSize = true;
            _chkZeroBacktickPs1.Padding = new Padding(3, 4, 2, 0);
            _chkZeroBacktickPs1.ForeColor = Color.FromArgb(250, 204, 21);
            _chkZeroBacktickPs1.Checked = _settings.EnforceZeroBacktickPs1;
            _chkZeroBacktickPs1.CheckedChanged += delegate { _settings.EnforceZeroBacktickPs1 = _chkZeroBacktickPs1.Checked; _settings.SaveToDisk(_configDir); };

            topBar.Controls.AddRange(new Control[]
            {
                lLang, _cmbCodeLang, bOpen, bSave, bFmtBuf, bFmtDir, bCheck, bRun, bDiff, bSyncDeb, _chkFmtOnSave, _chkZeroBacktickPs1
            });

            Panel gemmaCodeBar = new Panel();
            gemmaCodeBar.Dock = DockStyle.Top;
            gemmaCodeBar.Height = 38;
            gemmaCodeBar.Padding = new Padding(8, 5, 8, 5);
            gemmaCodeBar.BackColor = Color.FromArgb(18, 16, 40);

            FlowLayoutPanel gcLeft = new FlowLayoutPanel();
            gcLeft.Dock = DockStyle.Left;
            gcLeft.AutoSize = true;
            gcLeft.WrapContents = false;

            Label lGc = MakeLbl("Gemma Codegen:", new Font("Segoe UI Semibold", 8.4f), Color.FromArgb(192, 132, 252));
            lGc.Padding = new Padding(0, 5, 4, 0);

            _cmbCodeGemmaModel = new ComboBox();
            _cmbCodeGemmaModel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCodeGemmaModel.Width = 255;
            _cmbCodeGemmaModel.DropDownWidth = 380;
            _cmbCodeGemmaModel.BackColor = Color.FromArgb(6, 9, 17);
            _cmbCodeGemmaModel.ForeColor = Color.White;
            _cmbCodeGemmaModel.SelectedIndexChanged += delegate
            {
                GemmaLiveModelItem sel = _cmbCodeGemmaModel.SelectedItem as GemmaLiveModelItem;
                if (sel != null) ApplySelectedGemmaModelItem(sel);
            };

            gcLeft.Controls.AddRange(new Control[] { lGc, _cmbCodeGemmaModel });

            FlowLayoutPanel gcRight = new FlowLayoutPanel();
            gcRight.Dock = DockStyle.Right;
            gcRight.AutoSize = true;
            gcRight.WrapContents = false;

            Button bCodegenRun = MakeBtn("Codegen + Auto-Fmt + Verify", Color.FromArgb(5, 150, 105));
            bCodegenRun.Click += async delegate { await RunGemmaCodegenIntoStudioAsync(false); };

            Button bRefactorBuf = MakeBtn("Refactor Buffer", Color.FromArgb(124, 58, 237));
            bRefactorBuf.Click += async delegate { await RunGemmaCodegenIntoStudioAsync(true); };

            Button bAutoFixErr = MakeBtn("Auto-Fix Errors", Color.FromArgb(180, 83, 9));
            bAutoFixErr.Click += async delegate { await AskGemmaAutoFixCurrentBufferAsync(); };

            Button bStopCg = MakeBtn("Stop", Color.FromArgb(153, 27, 27));
            bStopCg.Click += delegate
            {
                OmegaShellGemmaCommander.AbortActiveStream();
                SetSingleLineStatus("Stopped active Gemma codegen stream.");
            };

            _chkAutoHealCodegen = new CheckBox();
            _chkAutoHealCodegen.Text = "Auto-Heal";
            _chkAutoHealCodegen.AutoSize = true;
            _chkAutoHealCodegen.Padding = new Padding(4, 4, 2, 0);
            _chkAutoHealCodegen.ForeColor = Color.FromArgb(52, 211, 153);
            _chkAutoHealCodegen.Checked = _settings.AutoHealCodegen;
            _chkAutoHealCodegen.CheckedChanged += delegate { _settings.AutoHealCodegen = _chkAutoHealCodegen.Checked; _settings.SaveToDisk(_configDir); };

            gcRight.Controls.AddRange(new Control[] { bCodegenRun, bRefactorBuf, bAutoFixErr, bStopCg, _chkAutoHealCodegen });

            _txtCodegenPrompt = new TextBox();
            _txtCodegenPrompt.Dock = DockStyle.Fill;
            _txtCodegenPrompt.BackColor = Color.FromArgb(8, 11, 22);
            _txtCodegenPrompt.ForeColor = Color.FromArgb(226, 232, 240);
            _txtCodegenPrompt.Font = new Font("Consolas", 9.3f);
            _txtCodegenPrompt.BorderStyle = BorderStyle.FixedSingle;
            _txtCodegenPrompt.Text = "Write an optimized 240-root E8 lattice inner-product benchmark in the selected language";
            _txtCodegenPrompt.KeyDown += async delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    await RunGemmaCodegenIntoStudioAsync(false);
                }
            };

            gemmaCodeBar.Controls.Add(_txtCodegenPrompt);
            gemmaCodeBar.Controls.Add(gcLeft);
            gemmaCodeBar.Controls.Add(gcRight);

            Panel fileBar = new Panel();
            fileBar.Dock = DockStyle.Top;
            fileBar.Height = 26;
            fileBar.Padding = new Padding(10, 4, 10, 2);
            fileBar.BackColor = Color.FromArgb(10, 16, 29);

            _lblCodeCursorStat = MakeLbl("Ln 1, Col 1 | UTF-8 (No BOM)", new Font("Consolas", 8.5f), Color.FromArgb(125, 211, 252));
            _lblCodeCursorStat.Dock = DockStyle.Right;

            _lblCodeFileBanner = MakeLbl("Scratch Buffer (Unsaved — Click Save or Codegen + Verify)", new Font("Consolas", 8.8f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            _lblCodeFileBanner.Dock = DockStyle.Fill;

            fileBar.Controls.Add(_lblCodeFileBanner);
            fileBar.Controls.Add(_lblCodeCursorStat);

            SplitContainer outerSplit = new SplitContainer();
            outerSplit.Dock = DockStyle.Fill;
            outerSplit.BackColor = Color.FromArgb(30, 41, 59);
            outerSplit.SplitterWidth = 5;

            Panel sidePane = new Panel();
            sidePane.Dock = DockStyle.Fill;
            sidePane.Padding = new Padding(6);
            sidePane.BackColor = Color.FromArgb(9, 14, 25);

            Label lTpl = MakeLbl("1-CLICK POLYGLOT TEMPLATES:", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(56, 189, 248));
            lTpl.AutoSize = false;
            lTpl.Dock = DockStyle.Top;
            lTpl.Height = 22;

            TableLayoutPanel tplGrid = new TableLayoutPanel();
            tplGrid.Dock = DockStyle.Top;
            tplGrid.ColumnCount = 2;
            tplGrid.RowCount = 4;
            tplGrid.Height = 156;
            tplGrid.Padding = new Padding(0, 2, 0, 4);
            tplGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tplGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int r = 0; r < 4; r++) tplGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 37f));

            string[] tpls = new string[] { "Rust E8", "C# 5.0", "C++23", "Lean 4", "TypeScript", "PS 5.1", "HLSL / AV", "OSH v1.0" };
            for (int i = 0; i < tpls.Length; i++)
            {
                string tName = tpls[i];
                Button bt = MakeBtn(tName, Color.FromArgb(30, 58, 138));
                bt.AutoSize = false;
                bt.MinimumSize = Size.Empty;
                bt.Padding = Padding.Empty;
                bt.TextAlign = ContentAlignment.MiddleCenter;
                bt.Dock = DockStyle.Fill;
                bt.Margin = new Padding(2, 2, 2, 2);
                bt.Click += delegate { LoadCodeTemplate(tName); };
                tplGrid.Controls.Add(bt, i % 2, i / 2);
            }

            Button bScanWs = MakeBtn("Scan CWD Code Files", Color.FromArgb(15, 118, 110));
            bScanWs.AutoSize = false;
            bScanWs.Dock = DockStyle.Top;
            bScanWs.Height = 28;
            bScanWs.Click += delegate { RefreshWorkspaceCodeFileList(); };

            Label lWsFiles = MakeLbl("WORKSPACE CODE FILES (Double-Click):", new Font("Segoe UI", 7.8f, FontStyle.Bold), Color.FromArgb(148, 163, 184));
            lWsFiles.AutoSize = false;
            lWsFiles.Dock = DockStyle.Top;
            lWsFiles.Height = 20;
            lWsFiles.Padding = new Padding(0, 4, 0, 0);

            _lstCodeWorkspaceFiles = new ListBox();
            _lstCodeWorkspaceFiles.Dock = DockStyle.Fill;
            _lstCodeWorkspaceFiles.BackColor = Color.FromArgb(6, 9, 17);
            _lstCodeWorkspaceFiles.ForeColor = Color.FromArgb(226, 232, 240);
            _lstCodeWorkspaceFiles.Font = new Font("Consolas", 8.6f);
            _lstCodeWorkspaceFiles.BorderStyle = BorderStyle.None;
            _lstCodeWorkspaceFiles.DoubleClick += delegate
            {
                int idx = _lstCodeWorkspaceFiles.SelectedIndex;
                if (idx >= 0 && idx < _codeQuickPaths.Count && File.Exists(_codeQuickPaths[idx]))
                    OpenFileInCodeStudio(_codeQuickPaths[idx]);
            };

            sidePane.Controls.Add(_lstCodeWorkspaceFiles);
            sidePane.Controls.Add(lWsFiles);
            sidePane.Controls.Add(bScanWs);
            sidePane.Controls.Add(tplGrid);
            sidePane.Controls.Add(lTpl);

            SplitContainer edVert = new SplitContainer();
            edVert.Dock = DockStyle.Fill;
            edVert.Orientation = Orientation.Horizontal;
            edVert.BackColor = Color.FromArgb(30, 41, 59);
            edVert.SplitterWidth = 5;

            Panel editorHost = new Panel();
            editorHost.Dock = DockStyle.Fill;
            editorHost.BackColor = Color.FromArgb(6, 9, 17);

            _rtbCodeGutter = new RichTextBox();
            _rtbCodeGutter.Dock = DockStyle.Left;
            _rtbCodeGutter.Width = 50;
            _rtbCodeGutter.ReadOnly = true;
            _rtbCodeGutter.ScrollBars = RichTextBoxScrollBars.None;
            _rtbCodeGutter.BorderStyle = BorderStyle.None;
            _rtbCodeGutter.BackColor = Color.FromArgb(11, 17, 30);
            _rtbCodeGutter.ForeColor = Color.FromArgb(100, 116, 139);
            _rtbCodeGutter.Font = new Font("Consolas", 10.0f);
            _rtbCodeGutter.WordWrap = false;

            _rtbCodeEditor = new RichTextBox();
            _rtbCodeEditor.Dock = DockStyle.Fill;
            _rtbCodeEditor.AcceptsTab = true;
            _rtbCodeEditor.WordWrap = false;
            _rtbCodeEditor.ScrollBars = RichTextBoxScrollBars.Both;
            _rtbCodeEditor.BorderStyle = BorderStyle.None;
            _rtbCodeEditor.BackColor = Color.FromArgb(6, 9, 17);
            _rtbCodeEditor.ForeColor = Color.FromArgb(226, 232, 240);
            _rtbCodeEditor.Font = new Font("Consolas", 10.0f);

            _rtbCodeEditor.SelectionChanged += delegate { UpdateCodeCursorStatus(); SyncCodeGutterScroll(); };
            _rtbCodeEditor.VScroll += delegate { SyncCodeGutterScroll(); };
            _rtbCodeEditor.TextChanged += delegate { if (!_suppressHighlight) { RefreshCodeLineNumbers(); SyncCodeGutterScroll(); } };
            _rtbCodeEditor.KeyDown += async delegate(object s, KeyEventArgs ke)
            {
                if (ke.Control && ke.Shift && ke.KeyCode == Keys.F)
                {
                    ke.SuppressKeyPress = true;
                    FormatActiveCodeBufferInPlace();
                }
                else if (ke.Control && ke.KeyCode == Keys.S)
                {
                    ke.SuppressKeyPress = true;
                    SaveActiveCodeBufferToDisk(false);
                }
                else if (ke.KeyCode == Keys.F5)
                {
                    ke.SuppressKeyPress = true;
                    await BuildOrRunActiveCodeAsync(false);
                }
                else if (ke.KeyCode == Keys.F6)
                {
                    ke.SuppressKeyPress = true;
                    await BuildOrRunActiveCodeAsync(true);
                }
                else if (ke.KeyCode == Keys.F8)
                {
                    ke.SuppressKeyPress = true;
                    ShowDiffAgainstDiskSnapshot();
                }
                else if (ke.KeyCode == Keys.Tab && !ke.Control && !ke.Alt)
                {
                    ke.SuppressKeyPress = true;
                    _rtbCodeEditor.SelectedText = "    ";
                }
                else if (ke.KeyCode == Keys.Enter && !ke.Control)
                {
                    int pos = _rtbCodeEditor.SelectionStart;
                    int lnIdx = _rtbCodeEditor.GetLineFromCharIndex(pos);
                    string[] ls = _rtbCodeEditor.Lines;
                    if (lnIdx >= 0 && lnIdx < ls.Length)
                    {
                        string curLn = ls[lnIdx];
                        int sp = 0;
                        while (sp < curLn.Length && curLn[sp] == ' ') sp++;
                        if (curLn.TrimEnd().EndsWith("{")) sp += 4;
                        ke.SuppressKeyPress = true;
                        _rtbCodeEditor.SelectedText = "\n" + new string(' ', sp);
                    }
                }
            };

            Panel edPad = new Panel();
            edPad.Dock = DockStyle.Fill;
            edPad.Padding = new Padding(6, 4, 0, 0);
            edPad.BackColor = Color.FromArgb(6, 9, 17);
            edPad.Controls.Add(_rtbCodeEditor);

            Panel gutPad = new Panel();
            gutPad.Dock = DockStyle.Left;
            gutPad.Width = 52;
            gutPad.Padding = new Padding(2, 4, 2, 0);
            gutPad.BackColor = Color.FromArgb(11, 17, 30);
            gutPad.Controls.Add(_rtbCodeGutter);

            editorHost.Controls.Add(edPad);
            editorHost.Controls.Add(gutPad);

            SplitContainer botSplit = new SplitContainer();
            botSplit.Dock = DockStyle.Fill;
            botSplit.BackColor = Color.FromArgb(30, 41, 59);
            botSplit.SplitterWidth = 5;

            Panel logHost = new Panel(); logHost.Dock = DockStyle.Fill;
            Label lLogHdr = MakeLbl("COMPILER, GEMMA CODEGEN & EXECUTION RECEIPTS:", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            lLogHdr.AutoSize = false; lLogHdr.Dock = DockStyle.Top; lLogHdr.Height = 22; lLogHdr.Padding = new Padding(6, 3, 0, 0); lLogHdr.BackColor = Color.FromArgb(14, 21, 38);

            _rtbCodeBuildLog = new RichTextBox();
            _rtbCodeBuildLog.Dock = DockStyle.Fill;
            _rtbCodeBuildLog.ReadOnly = true;
            _rtbCodeBuildLog.BorderStyle = BorderStyle.None;
            _rtbCodeBuildLog.BackColor = Color.FromArgb(7, 11, 19);
            _rtbCodeBuildLog.ForeColor = Color.FromArgb(226, 232, 240);
            _rtbCodeBuildLog.Font = new Font("Consolas", 9.0f);
            logHost.Controls.Add(WrapPaddedConsoleHost(_rtbCodeBuildLog, 8, 4));
            logHost.Controls.Add(lLogHdr);

            Panel diagHost = new Panel(); diagHost.Dock = DockStyle.Fill;
            Label lDiagHdr = MakeLbl("DIAGNOSTICS & UNIFIED DIFF INSPECTOR (Double-Click Row -> Jump to Line):", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(250, 204, 21));
            lDiagHdr.AutoSize = false; lDiagHdr.Dock = DockStyle.Top; lDiagHdr.Height = 22; lDiagHdr.Padding = new Padding(6, 3, 0, 0); lDiagHdr.BackColor = Color.FromArgb(14, 21, 38);

            _gridCodeDiagnostics = MakeStyledGrid();
            _gridCodeDiagnostics.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs ce)
            {
                if (ce.RowIndex < 0 || ce.RowIndex >= _gridCodeDiagnostics.Rows.Count) return;
                if (_gridCodeDiagnostics.Columns.Contains("line"))
                {
                    int targetLine;
                    if (int.TryParse((_gridCodeDiagnostics.Rows[ce.RowIndex].Cells["line"].Value ?? "1").ToString(), out targetLine))
                        JumpCodeEditorToLine(targetLine);
                }
            };
            AttachPipeContextMenu(_gridCodeDiagnostics, null);
            diagHost.Controls.Add(_gridCodeDiagnostics);
            diagHost.Controls.Add(lDiagHdr);

            botSplit.Panel1.Controls.Add(logHost);
            botSplit.Panel2.Controls.Add(diagHost);

            edVert.Panel1.Controls.Add(editorHost);
            edVert.Panel2.Controls.Add(botSplit);

            outerSplit.Panel1.Controls.Add(sidePane);
            outerSplit.Panel2.Controls.Add(edVert);

            page.Controls.Add(outerSplit);
            page.Controls.Add(fileBar);
            page.Controls.Add(gemmaCodeBar);
            page.Controls.Add(topBar);

            LoadCodeTemplate("Rust E8");

            ApplySplitterOnTabVisible(page, delegate
            {
                outerSplit.SplitterDistance = 244;
                edVert.SplitterDistance = Math.Max(360, (edVert.Height * 64) / 100);
                botSplit.SplitterDistance = Math.Max(460, (botSplit.Width * 52) / 100);
                RefreshWorkspaceCodeFileList();
            });
        }

        private void SyncCodeGutterScroll()
        {
            if (_rtbCodeEditor == null || _rtbCodeGutter == null || !_rtbCodeEditor.IsHandleCreated || !_rtbCodeGutter.IsHandleCreated)
                return;
            try
            {
                int edFirst = (int)SendMessage(_rtbCodeEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                int gtFirst = (int)SendMessage(_rtbCodeGutter.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                int delta = edFirst - gtFirst;
                if (delta != 0)
                    SendMessage(_rtbCodeGutter.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            }
            catch { }
        }

        private async Task RunGemmaCodegenIntoStudioAsync(bool includeCurrentBufferContext)
        {
            string lang = (_cmbCodeLang.SelectedItem ?? "rust").ToString();
            string prompt = (_txtCodegenPrompt.Text ?? "").Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                SetSingleLineStatus("Type a code generation or refactoring prompt in the Gemma Codegen box first.");
                return;
            }

            string existing = includeCurrentBufferContext ? (_rtbCodeEditor.Text ?? "") : "";
            string targetFile = includeCurrentBufferContext ? _activeCodeFilePath : "";
            bool autoHeal = _chkAutoHealCodegen != null && _chkAutoHealCodegen.Checked;
            string sr = _shellRoot;

            _rtbCodeBuildLog.ForeColor = Color.FromArgb(192, 132, 252);
            _rtbCodeBuildLog.Text = "[Gemma Polyglot Codegen :: " + _settings.DefaultAiModel + "] Generating " + lang + " code...";

            GemmaCodegenReceipt rec = await Task.Run(delegate
            {
                return OmegaShellGemmaCommander.GenerateFormatAndVerifyCode(
                    _settings,
                    sr,
                    lang,
                    prompt,
                    existing,
                    targetFile,
                    true,
                    autoHeal,
                    delegate(string pMsg) { SetSingleLineStatus(pMsg); });
            });

            string prevSnap = _diskSavedSnapshot;
            _activeCodeFilePath = rec.FilePath;
            _suppressHighlight = true;
            _rtbCodeEditor.Text = rec.SourceCode;
            _suppressHighlight = false;
            ApplySyntaxHighlightToCodeBuffer();
            RefreshCodeLineNumbers();

            string sha12 = rec.Sha256Receipt.Length >= 12 ? rec.Sha256Receipt.Substring(0, 12) : rec.Sha256Receipt;
            _lblCodeFileBanner.Text = rec.FilePath + "  [GEMMA " + (rec.Ok ? "VERIFIED EXIT 0" : ("EXIT " + rec.ExitCode)) + " | " + rec.FormatterEngine + " | SHA-256: " + sha12 + "]";

            _rtbCodeBuildLog.ForeColor = rec.Ok ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
            _rtbCodeBuildLog.Text =
                "[Gemma Codegen :: " + rec.EngineUsedLabel + " | " + rec.TokensPerSec + " tok/s | Formatter: " + rec.FormatterEngine +
                " | Repairs: " + rec.RepairIterations + " | Exit: " + rec.ExitCode + " | " + rec.ElapsedMs + " ms | SHA-256: " + sha12 + "]\r\n" +
                rec.BuildOutput;
            _rtbCodeBuildLog.SelectionStart = 0;
            _rtbCodeBuildLog.ScrollToCaret();

            if (includeCurrentBufferContext && !string.IsNullOrEmpty(prevSnap))
            {
                string diffSum;
                List<OshRow> diffRows = CodeFormatterAndCompiler.ComputeUnifiedDiffRows("before", prevSnap, "refactored", rec.SourceCode, out diffSum);
                PopulateGridWithOshRows(_gridCodeDiagnostics, diffRows);
            }
            else
            {
                List<OshRow> dRows = new List<OshRow>();
                for (int i = 0; i < rec.Diagnostics.Count; i++)
                {
                    CompilerDiagnosticItem d = rec.Diagnostics[i];
                    dRows.Add(new OshRow().Set("severity", d.Severity).Set("line", d.Line).Set("col", d.Column).Set("code", d.Code).Set("message", d.Message));
                }
                PopulateGridWithOshRows(_gridCodeDiagnostics, dRows);
            }

            _diskSavedSnapshot = rec.SourceCode;
            RefreshWorkspaceCodeFileList();
            SetSingleLineStatus((rec.Ok ? "[GEMMA VERIFIED EXIT 0] " : "[GEMMA CODEGEN] ") + Path.GetFileName(rec.FilePath) + " (" + rec.TokensPerSec + " tok/s, " + rec.ElapsedMs + " ms)");
        }

        private async Task AskGemmaAutoFixCurrentBufferAsync()
        {
            string lang = (_cmbCodeLang.SelectedItem ?? "rust").ToString();
            string curCode = _rtbCodeEditor.Text ?? "";
            string buildLog = _rtbCodeBuildLog.Text ?? "";
            string fixGoal = "Fix all compiler errors or warnings reported below while preserving exact program behavior:\r\n" + buildLog;

            string sr = _shellRoot;
            string fp = _activeCodeFilePath;
            _rtbCodeBuildLog.ForeColor = Color.FromArgb(250, 204, 21);
            _rtbCodeBuildLog.Text = "Gemma (" + _settings.DefaultAiModel + ") analyzing compiler diagnostics & repairing buffer...";

            GemmaCodegenReceipt rec = await Task.Run(delegate
            {
                return OmegaShellGemmaCommander.GenerateFormatAndVerifyCode(
                    _settings,
                    sr,
                    lang,
                    fixGoal,
                    curCode,
                    fp,
                    true,
                    true,
                    delegate(string pMsg) { SetSingleLineStatus(pMsg); });
            });

            _activeCodeFilePath = rec.FilePath;
            _diskSavedSnapshot = rec.SourceCode;
            _suppressHighlight = true;
            _rtbCodeEditor.Text = rec.SourceCode;
            _suppressHighlight = false;
            ApplySyntaxHighlightToCodeBuffer();
            RefreshCodeLineNumbers();

            _rtbCodeBuildLog.ForeColor = rec.Ok ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
            _rtbCodeBuildLog.Text = "[Gemma Auto-Fix :: Exit " + rec.ExitCode + " | " + rec.ElapsedMs + " ms]\r\n" + rec.BuildOutput;
            _rtbCodeBuildLog.SelectionStart = 0;
            _rtbCodeBuildLog.ScrollToCaret();
            SetSingleLineStatus(rec.Ok ? ("Gemma repaired " + Path.GetFileName(rec.FilePath) + " -> Exit 0!") : ("Auto-fix finished with Exit " + rec.ExitCode));
        }

        internal void OpenFileInCodeStudio(string filePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string content = File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : "";

                _activeCodeFilePath = filePath;
                _diskSavedSnapshot = content;
                string lang = CodeFormatterAndCompiler.DetectLanguageFromPath(filePath);
                int idx = _cmbCodeLang.Items.IndexOf(lang);
                if (idx >= 0) _cmbCodeLang.SelectedIndex = idx;

                _suppressHighlight = true;
                _rtbCodeEditor.Text = content;
                _suppressHighlight = false;

                ApplySyntaxHighlightToCodeBuffer();
                RefreshCodeLineNumbers();
                _lblCodeFileBanner.Text = filePath;
                _mainTabs.SelectedIndex = 1;
                _rtbCodeEditor.Focus();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Code Studio open error: " + ex.Message;
            }
        }

        private void LoadCodeTemplate(string tplName)
        {
            string code = "";
            string lang = "rust";
            string defaultExt = ".rs";

            if (tplName == "Rust E8")
            {
                lang = "rust"; defaultExt = ".rs";
                code = "// ACT-Omega L0 Rust Norm Kernel (Ctrl+Shift+F = Auto-Format | F5 = Compile & Run)\r\nfn main() {\r\n    let mut norm_sum: f64 = 0.0;\r\n    for k in 1..=240 {\r\n        norm_sum += (k as f64).sqrt();\r\n    }\r\n    println!(\"[Rust L0 OK] 240-Root Checksum = {:.6}\", norm_sum);\r\n}\r\n";
            }
            else if (tplName == "C# 5.0")
            {
                lang = "csharp"; defaultExt = ".cs";
                code = "using System;\r\n\r\npublic static class Program\r\n{\r\n    public static void Main()\r\n    {\r\n        long sum = 0;\r\n        for (int i = 1; i <= 1024; i++) sum += (long)i * i;\r\n        Console.WriteLine(\"[C# 5.0 x64 OK] Sum of squares = \" + sum);\r\n    }\r\n}\r\n";
            }
            else if (tplName == "C++23")
            {
                lang = "cpp"; defaultExt = ".cpp";
                code = "#include <iostream>\r\n#include <cmath>\r\n\r\nint main() {\r\n    double acc = 0.0;\r\n    for (int i = 1; i <= 256; ++i) acc += std::sqrt(static_cast<double>(i));\r\n    std::cout << \"[Clang++ C++23 OK] Acc = \" << acc << \"\\n\";\r\n    return 0;\r\n}\r\n";
            }
            else if (tplName == "Lean 4")
            {
                lang = "lean4"; defaultExt = ".lean";
                code = "-- Lean 4 Formal Verification Scratchpad (F5 = Verify via lean.exe)\ndef e8Roots : Nat := 240\n\ntheorem thm_e8_partition : 112 + 128 = e8Roots := by\n  rfl\n";
            }
            else if (tplName == "TypeScript")
            {
                lang = "typescript"; defaultExt = ".ts";
                code = "// Deno TypeScript Kernel (Ctrl+Shift+F = deno fmt | F5 = deno run)\r\nconst roots = Array.from({ length: 240 }, (_, i) => Math.sqrt(i + 1));\r\nconst total = roots.reduce((a, b) => a + b, 0);\r\nconsole.log(\"[Deno TS OK] 240-Root Sum =\", total.toFixed(6));\r\n";
            }
            else if (tplName == "PS 5.1")
            {
                lang = "powershell"; defaultExt = ".ps1";
                code = "# Zero-Backtick PowerShell 5.1 Script\r\n$Drives = Get-PSDrive -PSProvider FileSystem\r\nforeach ($d in $Drives) {\r\n    Write-Output ($d.Name + ': Free GB = ' + [Math]::Round($d.Free / 1GB, 2))\r\n}\r\n";
            }
            else if (tplName == "HLSL / AV")
            {
                lang = "hlsl"; defaultExt = ".hlsl";
                code = "// DirectCompute cs_5_0 Kernel (F5 = Compile via fxc.exe or stage .omsh to OmegaAV)\r\nRWStructuredBuffer<float4> OutField : register(u0);\r\n\r\n[numthreads(8, 8, 1)]\r\nvoid CSMain(uint3 id : SV_DispatchThreadID)\r\n{\r\n    float u = (float)id.x * 0.015625f;\r\n    float v = (float)id.y * 0.015625f;\r\n    OutField[id.y * 64 + id.x] = float4(sin(u), cos(v), sin(u + v), 1.0f);\r\n}\r\n";
            }
            else
            {
                lang = "osh"; defaultExt = ".osh";
                code = "# OmegaShell v1.0 GM Automation Script\r\nlet target_mb = 80\r\nps | group name | where sum_ram_mb gt $target_mb | sort -desc sum_ram_mb\r\n";
            }

            string wsDir = Path.Combine(_shellRoot, "workspace_builds");
            Directory.CreateDirectory(wsDir);
            _activeCodeFilePath = Path.Combine(wsDir, "scratch_" + lang + defaultExt);
            _diskSavedSnapshot = code;

            int idx = _cmbCodeLang.Items.IndexOf(lang);
            if (idx >= 0) _cmbCodeLang.SelectedIndex = idx;

            _suppressHighlight = true;
            _rtbCodeEditor.Text = code;
            _suppressHighlight = false;
            ApplySyntaxHighlightToCodeBuffer();
            RefreshCodeLineNumbers();
            _lblCodeFileBanner.Text = _activeCodeFilePath + " (Template ready)";
        }

        private void FormatActiveCodeBufferInPlace()
        {
            string lang = (_cmbCodeLang.SelectedItem ?? "rust").ToString();
            string engine;
            int changed;
            int oldCaret = _rtbCodeEditor.SelectionStart;

            string formatted = CodeFormatterAndCompiler.FormatBufferText(
                _rtbCodeEditor.Text,
                lang,
                _chkZeroBacktickPs1 != null && _chkZeroBacktickPs1.Checked,
                out engine,
                out changed);

            _suppressHighlight = true;
            _rtbCodeEditor.Text = formatted;
            _suppressHighlight = false;
            ApplySyntaxHighlightToCodeBuffer();
            RefreshCodeLineNumbers();
            _rtbCodeEditor.SelectionStart = Math.Min(oldCaret, _rtbCodeEditor.TextLength);

            string msg = "[Auto-Formatter :: " + engine + "] Formatted buffer (" + changed + " line(s) normalized, UTF-8 clean).";
            _rtbCodeBuildLog.Text = msg;
            SetSingleLineStatus(msg);
        }

        private bool SaveActiveCodeBufferToDisk(bool quiet)
        {
            try
            {
                if (string.IsNullOrEmpty(_activeCodeFilePath))
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.InitialDirectory = ActiveWorkingDir();
                        sfd.FileName = "script.rs";
                        if (sfd.ShowDialog(this) != DialogResult.OK) return false;
                        _activeCodeFilePath = sfd.FileName;
                    }
                }

                if (_chkFmtOnSave != null && _chkFmtOnSave.Checked)
                    FormatActiveCodeBufferInPlace();

                string dir = Path.GetDirectoryName(_activeCodeFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string textToWrite = _rtbCodeEditor.Text ?? "";
                File.WriteAllText(_activeCodeFilePath, textToWrite, new UTF8Encoding(false));
                _diskSavedSnapshot = textToWrite;

                string sha = OshFormat.Sha256File(_activeCodeFilePath);
                string sha12 = sha.Length >= 12 ? sha.Substring(0, 12) : sha;
                _lblCodeFileBanner.Text = _activeCodeFilePath + "  [SAVED | SHA-256: " + sha12 + "]";
                if (!quiet) SetSingleLineStatus("Saved UTF-8 (No BOM): " + _activeCodeFilePath + " (SHA-256: " + sha12 + ")");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Save error: " + ex.Message, "Code Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private async Task BuildOrRunActiveCodeAsync(bool checkOnly)
        {
            if (!SaveActiveCodeBufferToDisk(true)) return;
            string fp = _activeCodeFilePath;
            string lang = (_cmbCodeLang.SelectedItem ?? "rust").ToString();

            if (lang == "osh")
            {
                _mainTabs.SelectedIndex = 0;
                string[] lines = File.ReadAllLines(fp, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (ln.Length > 0 && !ln.StartsWith("#")) await RunCommandFromUiAsync(ln, false);
                }
                return;
            }

            _rtbCodeBuildLog.Text = (checkOnly ? "Checking syntax: " : "Compiling & running: ") + fp + " ...";
            string sr = _shellRoot;
            CodeBuildExecResult br = await Task.Run(delegate { return CodeFormatterAndCompiler.CompileAndRunFile(fp, checkOnly, sr); });

            string sha12 = br.Sha256Receipt.Length >= 12 ? br.Sha256Receipt.Substring(0, 12) : br.Sha256Receipt;
            _rtbCodeBuildLog.ForeColor = br.Ok ? Color.FromArgb(226, 232, 240) : Color.FromArgb(248, 113, 113);
            _rtbCodeBuildLog.Text = "[" + Path.GetFileName(br.CompilerUsed) + " | Exit: " + br.ExitCode + " | Time: " + br.ElapsedMs + " ms | SHA-256: " + sha12 + "]\r\n" + br.OutputText;
            _rtbCodeBuildLog.SelectionStart = 0;
            _rtbCodeBuildLog.ScrollToCaret();

            List<OshRow> dRows = new List<OshRow>();
            for (int i = 0; i < br.Diagnostics.Count; i++)
            {
                CompilerDiagnosticItem d = br.Diagnostics[i];
                dRows.Add(new OshRow().Set("severity", d.Severity).Set("line", d.Line).Set("col", d.Column).Set("code", d.Code).Set("message", d.Message));
            }
            PopulateGridWithOshRows(_gridCodeDiagnostics, dRows);
            SetSingleLineStatus((br.Ok ? "[BUILD OK] " : "[BUILD ERROR] ") + Path.GetFileName(fp) + " -> Exit " + br.ExitCode + " in " + br.ElapsedMs + " ms (" + dRows.Count + " diagnostic(s)).");
        }

        private void ShowDiffAgainstDiskSnapshot()
        {
            string diskText = (!string.IsNullOrEmpty(_activeCodeFilePath) && File.Exists(_activeCodeFilePath))
                ? File.ReadAllText(_activeCodeFilePath, Encoding.UTF8)
                : _diskSavedSnapshot;

            string sum;
            List<OshRow> diffRows = CodeFormatterAndCompiler.ComputeUnifiedDiffRows("disk", diskText, "buffer", _rtbCodeEditor.Text ?? "", out sum);
            PopulateGridWithOshRows(_gridCodeDiagnostics, diffRows);
            _rtbCodeBuildLog.ForeColor = Color.FromArgb(125, 211, 252);
            _rtbCodeBuildLog.Text = sum;
            SetSingleLineStatus(sum);
        }

        private async Task FormatPickedDirectoryAsync()
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = ActiveWorkingDir();
                fbd.Description = "Choose a project folder to auto-format (Rust, C#, C++, TS, Lean 4, PS1, JSON, OSH):";
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                string picked = fbd.SelectedPath;
                bool zeroBt = _chkZeroBacktickPs1 != null && _chkZeroBacktickPs1.Checked;
                _rtbCodeBuildLog.Text = "Formatting folder: " + picked + " ...";
                List<FormatResultItem> items = await Task.Run(delegate { return CodeFormatterAndCompiler.FormatPath(picked, false, zeroBt); });
                List<OshRow> rows = new List<OshRow>();
                int modCount = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Changed) modCount++;
                    rows.Add(new OshRow().Set("file", Path.GetFileName(items[i].FilePath)).Set("lang", items[i].Language).Set("engine", items[i].EngineUsed).Set("status", items[i].Status).Set("changed_lines", items[i].ChangedLines));
                }
                PopulateGridWithOshRows(_gridCodeDiagnostics, rows);
                string msg = "Formatted " + modCount + " of " + items.Count + " code file(s) in " + picked;
                _rtbCodeBuildLog.Text = msg;
                SetSingleLineStatus(msg);
            }
        }

        private async Task SyncActiveCodeToPixelDebianAsync()
        {
            if (!SaveActiveCodeBufferToDisk(true)) return;
            string fp = _activeCodeFilePath;
            string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.SyncLocalPathToDebianShared(fp, ""); });
            _rtbCodeBuildLog.ForeColor = Color.FromArgb(52, 211, 153);
            _rtbCodeBuildLog.Text = msg;
            SetSingleLineStatus("Synced " + Path.GetFileName(fp) + " -> Pixel /mnt/shared/omega_workspace!");
        }

        private void RefreshWorkspaceCodeFileList()
        {
            if (_lstCodeWorkspaceFiles == null) return;
            _lstCodeWorkspaceFiles.Items.Clear();
            _codeQuickPaths.Clear();
            string[] candidateDirs = new string[] { ActiveWorkingDir(), Path.Combine(_shellRoot, "workspace_builds") };
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int d = 0; d < candidateDirs.Length; d++)
            {
                string dir = candidateDirs[d];
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    string[] files = Directory.GetFiles(dir);
                    for (int i = 0; i < files.Length && _codeQuickPaths.Count < 160; i++)
                    {
                        if (!seen.Add(files[i])) continue;
                        string lang = CodeFormatterAndCompiler.DetectLanguageFromPath(files[i]);
                        if (lang == "text") continue;
                        if (d == 1)
                        {
                            try
                            {
                                FileInfo wfi = new FileInfo(files[i]);
                                if (wfi.Length > 0 && wfi.Length < 400)
                                {
                                    string headTxt = File.ReadAllText(files[i], Encoding.UTF8);
                                    if (headTxt.StartsWith("UNMEASURED/OFFLINE:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        File.Delete(files[i]);
                                        continue;
                                    }
                                }
                            }
                            catch { }
                        }
                        _codeQuickPaths.Add(files[i]);
                        _lstCodeWorkspaceFiles.Items.Add((d == 1 ? "[ws] " : "") + Path.GetFileName(files[i]));
                    }
                }
                catch { }
            }
        }

        private void JumpCodeEditorToLine(int lineOneBased)
        {
            int idx = _rtbCodeEditor.GetFirstCharIndexFromLine(Math.Max(0, lineOneBased - 1));
            if (idx >= 0)
            {
                _rtbCodeEditor.SelectionStart = idx;
                _rtbCodeEditor.SelectionLength = 0;
                _rtbCodeEditor.ScrollToCaret();
                _rtbCodeEditor.Focus();
            }
        }

        private void RefreshCodeLineNumbers()
        {
            if (_rtbCodeGutter == null || _rtbCodeEditor == null) return;
            int lineCount = Math.Max(1, _rtbCodeEditor.Lines.Length);
            StringBuilder sb = new StringBuilder(lineCount * 5);
            for (int i = 1; i <= lineCount && i <= 2500; i++)
                sb.Append(i.ToString().PadLeft(4)).Append('\n');
            _rtbCodeGutter.Text = sb.ToString();
        }

        private void UpdateCodeCursorStatus()
        {
            if (_rtbCodeEditor == null || _lblCodeCursorStat == null) return;
            int pos = _rtbCodeEditor.SelectionStart;
            int ln = _rtbCodeEditor.GetLineFromCharIndex(pos);
            int col = pos - _rtbCodeEditor.GetFirstCharIndexFromLine(ln);
            _lblCodeCursorStat.Text = "Ln " + (ln + 1) + ", Col " + (col + 1) + " | " + _rtbCodeEditor.TextLength + " chars | UTF-8 (No BOM)";
        }

        private void ApplySyntaxHighlightToCodeBuffer()
        {
            if (_rtbCodeEditor == null || _suppressHighlight) return;
            if (_rtbCodeEditor.TextLength > 65536) return;

            _suppressHighlight = true;
            int savedStart = _rtbCodeEditor.SelectionStart;
            int savedLen = _rtbCodeEditor.SelectionLength;

            if (_rtbCodeEditor.IsHandleCreated)
                SendMessage(_rtbCodeEditor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            try
            {
                _rtbCodeEditor.SelectAll();
                _rtbCodeEditor.SelectionColor = Color.FromArgb(226, 232, 240);

                string text = _rtbCodeEditor.Text;
                MatchCollection kwMatches = Regex.Matches(text, @"\b(fn|let|mut|pub|impl|struct|enum|trait|match|use|mod|crate|where|async|await|using|namespace|class|sealed|static|void|int|long|double|float|float4|uint3|bool|string|const|return|if|else|for|foreach|while|try|catch|finally|new|out|ref|theorem|lemma|def|by|rfl|omega|simp|function|param|ports|drives|offload|avf|gemma|codegen|group|sort|select)\b");
                foreach (Match m in kwMatches)
                {
                    _rtbCodeEditor.Select(m.Index, m.Length);
                    _rtbCodeEditor.SelectionColor = Color.FromArgb(56, 189, 248);
                }

                MatchCollection strMatches = Regex.Matches(text, "\"(?:\\\\.|[^\"\\\\])*\"");
                foreach (Match m in strMatches)
                {
                    _rtbCodeEditor.Select(m.Index, m.Length);
                    _rtbCodeEditor.SelectionColor = Color.FromArgb(250, 204, 21);
                }

                MatchCollection cmtMatches = Regex.Matches(text, @"(?://|--|#).*$", RegexOptions.Multiline);
                foreach (Match m in cmtMatches)
                {
                    _rtbCodeEditor.Select(m.Index, m.Length);
                    _rtbCodeEditor.SelectionColor = Color.FromArgb(100, 116, 139);
                }
            }
            catch { }
            finally
            {
                _rtbCodeEditor.Select(savedStart, savedLen);
                if (_rtbCodeEditor.IsHandleCreated)
                {
                    SendMessage(_rtbCodeEditor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _rtbCodeEditor.Invalidate();
                }
                if (!_rtbCodeEditor.WordWrap && _rtbCodeEditor.IsHandleCreated)
                {
                    try { SendMessage(_rtbCodeEditor.Handle, WM_HSCROLL, (IntPtr)SB_LEFT, IntPtr.Zero); } catch { }
                }
                SyncCodeGutterScroll();
                _suppressHighlight = false;
            }
        }
    }
}
