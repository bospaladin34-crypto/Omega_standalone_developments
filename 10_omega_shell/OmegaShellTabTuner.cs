using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaShellStudio
{
    public sealed partial class OmegaShellForm
    {
        private Label _lblTunerLiveCards, _lblTunerActionResult, _lblDiskOffloadBanner;
        private TabControl _subTunerTabs;
        private DataGridView _gridProcs, _gridPorts, _gridStartup, _gridServices, _gridGpuVram, _gridSpaceHogs;
        private TextBox _txtProcFilter, _txtPortFilter, _txtQuickKillPort, _txtSvcFilter;
        private CheckBox _chkListenOnly, _chkSvcRunningOnly;
        private ComboBox _cmbOffloadVaultDrive;
        private List<TcpSocketItem> _cachedSockets = new List<TcpSocketItem>();
        private List<StartupAppItem> _cachedStartups = new List<StartupAppItem>();
        private List<SpaceHogEntry> _cachedSpaceHogs = new List<SpaceHogEntry>();

        internal void SelectDiskOffloaderSubTab()
        {
            if (_subTunerTabs != null && _subTunerTabs.TabPages.Count >= 5)
                _subTunerTabs.SelectedIndex = 4;
        }

        private void BuildTab3TunerUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            Panel topDeck = new Panel();
            topDeck.Dock = DockStyle.Top;
            topDeck.Height = 120;
            topDeck.Padding = new Padding(10, 6, 10, 6);
            topDeck.BackColor = Color.FromArgb(13, 20, 36);

            _lblTunerLiveCards = MakeLbl(
                "Probing Intel Hybrid P/E-Cores, Physical DDR5 Working Sets, Power Scheme & NVIDIA DirectCompute GPU...",
                new Font("Consolas", 9.1f, FontStyle.Bold),
                Color.FromArgb(56, 189, 248));
            _lblTunerLiveCards.AutoSize = false;
            _lblTunerLiveCards.Dock = DockStyle.Top;
            _lblTunerLiveCards.Height = 24;

            FlowLayoutPanel optBtns = new FlowLayoutPanel();
            optBtns.Dock = DockStyle.Top;
            optBtns.Height = 36;
            optBtns.Padding = new Padding(0, 4, 0, 2);
            optBtns.WrapContents = false;

            Button bTrimRam = MakeBtn("1. Trim Background RAM (EmptyWorkingSet)", Color.FromArgb(5, 150, 105));
            bTrimRam.Click += async delegate
            {
                string msg = await Task.Run(delegate { int c; long f; return Win32HardwareTuner.TrimWorkingSetMemory(_settings.ProtectedProcesses, out c, out f); });
                SetTunerFeedback(msg);
                await RefreshTunerTablesAsync("processes");
            };

            Button bBoost = MakeBtn("2. Sovereign P-Core Boost + EcoQoS Parking", Color.FromArgb(109, 40, 217));
            bBoost.Click += async delegate
            {
                string msg = await Task.Run(delegate
                {
                    return Win32HardwareTuner.ApplySovereignComputeBoost(_settings.ProtectedProcesses, _settings.ProcessCoreRules);
                });
                SetTunerFeedback(msg);
                await RefreshTunerTablesAsync("processes");
            };

            Button bRestore = MakeBtn("Restore Default Affinities", Color.FromArgb(51, 65, 85));
            bRestore.Click += async delegate
            {
                string msg = await Task.Run(delegate { return Win32HardwareTuner.RestoreAllProcessDefaults(); });
                SetTunerFeedback(msg);
                await RefreshTunerTablesAsync("processes");
            };

            Button bPwrBal = MakeBtn("Power: Balanced", Color.FromArgb(30, 58, 138));
            bPwrBal.Click += async delegate { SetTunerFeedback(await Task.Run(delegate { return Win32HardwareTuner.SwitchPowerPlan("balanced"); })); };

            Button bPwrHigh = MakeBtn("Power: High Perf", Color.FromArgb(180, 83, 9));
            bPwrHigh.Click += async delegate { SetTunerFeedback(await Task.Run(delegate { return Win32HardwareTuner.SwitchPowerPlan("high"); })); };

            Button bPwrUlt = MakeBtn("Power: Ultimate", Color.FromArgb(153, 27, 27));
            bPwrUlt.Click += async delegate { SetTunerFeedback(await Task.Run(delegate { return Win32HardwareTuner.SwitchPowerPlan("ultimate"); })); };

            Button bDryClean = MakeBtn("Preview Temp Clean", Color.FromArgb(15, 118, 110));
            bDryClean.Click += async delegate { SetTunerFeedback(await Task.Run(delegate { int fc; long fb; return Win32HardwareTuner.CleanSafeTempAndCaches(true, out fc, out fb); })); };

            Button bDoClean = MakeBtn("Apply Temp Clean", Color.FromArgb(6, 95, 70));
            bDoClean.Click += async delegate { SetTunerFeedback(await Task.Run(delegate { int fc; long fb; return Win32HardwareTuner.CleanSafeTempAndCaches(false, out fc, out fb); })); };

            optBtns.Controls.AddRange(new Control[] { bTrimRam, bBoost, bRestore, bPwrBal, bPwrHigh, bPwrUlt, bDryClean, bDoClean });

            _lblTunerActionResult = MakeLbl(
                "Ready :: Multi-select process rows (Ctrl/Shift+Click) in Sub-Tab A, or offload C:\\ space hogs via mklink /J in Sub-Tab E!",
                new Font("Consolas", 8.5f, FontStyle.Bold),
                Color.FromArgb(250, 204, 21));
            _lblTunerActionResult.AutoSize = false;
            _lblTunerActionResult.Dock = DockStyle.Bottom;
            _lblTunerActionResult.Height = 42;

            topDeck.Controls.Add(_lblTunerActionResult);
            topDeck.Controls.Add(optBtns);
            topDeck.Controls.Add(_lblTunerLiveCards);

            _subTunerTabs = new TabControl();
            _subTunerTabs.Dock = DockStyle.Fill;
            _subTunerTabs.Font = new Font("Segoe UI Semibold", 8.9f);

            TabPage stProcs = new TabPage("A. Processes, Multi-Select & Core Rules");
            TabPage stPorts = new TabPage("B. Live TCP Port & Socket Killer (iphlpapi)");
            TabPage stStart = new TabPage("C. Startup Apps Manager (Registry & Folders)");
            TabPage stSvcs  = new TabPage("D. Windows Services & DirectCompute GPU VRAM");
            TabPage stDisk  = new TabPage("E. C:\\ Space-Hog Scanner & mklink /J Offloader -> F:\\ / E:\\");

            BuildSubTabProcesses(stProcs);
            BuildSubTabPorts(stPorts);
            BuildSubTabStartup(stStart);
            BuildSubTabServices(stSvcs);
            BuildSubTabDiskOffloader(stDisk);

            _subTunerTabs.TabPages.AddRange(new TabPage[] { stProcs, stPorts, stStart, stSvcs, stDisk });
            _subTunerTabs.SelectedIndexChanged += async delegate
            {
                if (_subTunerTabs.SelectedIndex == 0) await RefreshTunerTablesAsync("processes");
                else if (_subTunerTabs.SelectedIndex == 1) await RefreshTunerTablesAsync("ports");
                else if (_subTunerTabs.SelectedIndex == 2) await RefreshTunerTablesAsync("startup");
                else if (_subTunerTabs.SelectedIndex == 3) await RefreshTunerTablesAsync("services");
                else if (_subTunerTabs.SelectedIndex == 4) await RefreshTunerTablesAsync("diskhogs");
            };

            page.Controls.Add(_subTunerTabs);
            page.Controls.Add(topDeck);
        }

        private void BuildSubTabProcesses(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(8, 12, 21);
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top; bar.Height = 38; bar.Padding = new Padding(8, 5, 8, 3); bar.WrapContents = false; bar.BackColor = Color.FromArgb(14, 21, 38);

            Label lF = MakeLbl("Filter:", null, Color.FromArgb(148, 163, 184));
            lF.Padding = new Padding(0, 5, 4, 0);
            _txtProcFilter = new TextBox(); _txtProcFilter.Width = 135; _txtProcFilter.BackColor = Color.FromArgb(6, 9, 17); _txtProcFilter.ForeColor = Color.White;
            _txtProcFilter.TextChanged += async delegate { await RefreshTunerTablesAsync("processes"); };

            Button bRef = MakeBtn("Refresh", Color.FromArgb(2, 132, 199));
            bRef.Click += async delegate { await RefreshTunerTablesAsync("processes"); };

            Button bPinP = MakeBtn("Pin Selected -> P-Cores", Color.FromArgb(5, 150, 105));
            bPinP.Click += async delegate { await ApplySelectedProcAffinityAsync(true); };

            Button bParkE = MakeBtn("Park Selected -> E-Cores (EcoQoS)", Color.FromArgb(109, 40, 217));
            bParkE.Click += async delegate { await ApplySelectedProcAffinityAsync(false); };

            Button bRuleP = MakeBtn("+ Rule: Always P-Cores", Color.FromArgb(6, 95, 70));
            bRuleP.Click += async delegate { await SaveSelectedProcCoreRuleAsync("pcores"); };

            Button bRuleE = MakeBtn("+ Rule: Always E-Cores", Color.FromArgb(88, 28, 135));
            bRuleE.Click += async delegate { await SaveSelectedProcCoreRuleAsync("eco"); };

            Button bRuleClr = MakeBtn("Clear Rule", Color.FromArgb(71, 85, 105));
            bRuleClr.Click += async delegate { await SaveSelectedProcCoreRuleAsync("clear"); };

            Button bWhitelist = MakeBtn("+ Whitelist", Color.FromArgb(30, 58, 138));
            bWhitelist.Click += delegate
            {
                int added = 0;
                for (int i = 0; i < _gridProcs.SelectedRows.Count; i++)
                {
                    string nm = (_gridProcs.SelectedRows[i].Cells["name"].Value ?? "").ToString();
                    if (!string.IsNullOrEmpty(nm)) { _settings.ProtectedProcesses.Add(nm); added++; }
                }
                if (added > 0)
                {
                    _settings.SaveToDisk(_configDir);
                    SetTunerFeedback("Added " + added + " selected process name(s) to Protected Optimizer Whitelist.");
                }
            };

            Button bKillP = MakeBtn("Kill PID(s)", Color.FromArgb(153, 27, 27));
            bKillP.Click += async delegate
            {
                if (_gridProcs.SelectedRows.Count == 0) return;
                List<string> pids = new List<string>();
                for (int i = 0; i < _gridProcs.SelectedRows.Count; i++)
                {
                    string ps = (_gridProcs.SelectedRows[i].Cells["pid"].Value ?? "").ToString();
                    if (!string.IsNullOrEmpty(ps)) pids.Add(ps);
                }
                for (int k = 0; k < pids.Count; k++)
                    await RunCommandFromUiAsync("kill " + pids[k], false);
                await RefreshTunerTablesAsync("processes");
            };

            bar.Controls.AddRange(new Control[] { lF, _txtProcFilter, bRef, bPinP, bParkE, bRuleP, bRuleE, bRuleClr, bWhitelist, bKillP });

            _gridProcs = MakeStyledGrid();
            _gridProcs.MultiSelect = true;
            _gridProcs.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs cf)
            {
                if (cf.ColumnIndex >= 0 && cf.ColumnIndex < _gridProcs.Columns.Count && _gridProcs.Columns[cf.ColumnIndex].Name == "core_tier" && cf.Value != null)
                {
                    string v = cf.Value.ToString();
                    if (v.StartsWith("P-CORES")) cf.CellStyle.ForeColor = Color.FromArgb(52, 211, 153);
                    else if (v.StartsWith("E-CORES")) cf.CellStyle.ForeColor = Color.FromArgb(192, 132, 252);
                    else if (v.StartsWith("OS-")) cf.CellStyle.ForeColor = Color.FromArgb(148, 163, 184);
                }
            };
            AttachPipeContextMenu(_gridProcs, delegate { return "ps"; });

            tab.Controls.Add(_gridProcs);
            tab.Controls.Add(bar);
        }

        private void BuildSubTabPorts(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(8, 12, 21);
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top; bar.Height = 36; bar.Padding = new Padding(8, 5, 8, 3); bar.WrapContents = false; bar.BackColor = Color.FromArgb(14, 21, 38);

            Label lF = MakeLbl("Search Port / Process:", null, Color.FromArgb(148, 163, 184));
            lF.Padding = new Padding(0, 5, 4, 0);
            _txtPortFilter = new TextBox(); _txtPortFilter.Width = 145; _txtPortFilter.BackColor = Color.FromArgb(6, 9, 17); _txtPortFilter.ForeColor = Color.White;
            _txtPortFilter.TextChanged += delegate { RenderFilteredSockets(); };

            _chkListenOnly = new CheckBox(); _chkListenOnly.Text = "LISTEN Only"; _chkListenOnly.Checked = true; _chkListenOnly.AutoSize = true; _chkListenOnly.Padding = new Padding(6, 4, 6, 0);
            _chkListenOnly.ForeColor = Color.FromArgb(52, 211, 153);
            _chkListenOnly.CheckedChanged += delegate { RenderFilteredSockets(); };

            Button bRef = MakeBtn("Refresh TCP Table", Color.FromArgb(2, 132, 199));
            bRef.Click += async delegate { await RefreshTunerTablesAsync("ports"); };

            Button bDropTcb = MakeBtn("Drop Selected Socket (DeleteTCB)", Color.FromArgb(180, 83, 9));
            bDropTcb.Click += async delegate
            {
                if (_gridPorts.SelectedRows.Count > 0)
                {
                    int rowIdx = _gridPorts.SelectedRows[0].Index;
                    if (rowIdx >= 0 && rowIdx < _filteredSockets.Count)
                    {
                        TcpSocketItem target = _filteredSockets[rowIdx];
                        SetTunerFeedback(Win32HardwareTuner.DropTcpSocketConnection(target));
                        await RefreshTunerTablesAsync("ports");
                    }
                }
            };

            Button bKillOwner = MakeBtn("Kill Selected Port Owner PID", Color.FromArgb(153, 27, 27));
            bKillOwner.Click += async delegate
            {
                if (_gridPorts.SelectedRows.Count > 0)
                {
                    int prt;
                    if (int.TryParse((_gridPorts.SelectedRows[0].Cells["port"].Value ?? "").ToString(), out prt))
                    {
                        SetTunerFeedback(Win32HardwareTuner.KillProcessOnPort(prt, _settings.ProtectedProcesses));
                        await RefreshTunerTablesAsync("ports");
                    }
                }
            };

            Label lQuick = MakeLbl("Free Port #:", null, Color.FromArgb(250, 204, 21));
            lQuick.Padding = new Padding(8, 5, 2, 0);
            _txtQuickKillPort = new TextBox(); _txtQuickKillPort.Width = 64; _txtQuickKillPort.Text = "8080"; _txtQuickKillPort.BackColor = Color.FromArgb(6, 9, 17); _txtQuickKillPort.ForeColor = Color.White;

            Button bFreeQuick = MakeBtn("Free Port Now", Color.FromArgb(185, 28, 28));
            bFreeQuick.Click += async delegate
            {
                int prt;
                if (int.TryParse(_txtQuickKillPort.Text.Trim().TrimStart(':'), out prt) && prt > 0)
                {
                    SetTunerFeedback(Win32HardwareTuner.KillProcessOnPort(prt, _settings.ProtectedProcesses));
                    await RefreshTunerTablesAsync("ports");
                }
            };

            bar.Controls.AddRange(new Control[] { lF, _txtPortFilter, _chkListenOnly, bRef, bDropTcb, bKillOwner, lQuick, _txtQuickKillPort, bFreeQuick });
            _gridPorts = MakeStyledGrid();
            AttachPipeContextMenu(_gridPorts, delegate { return "ports -listen"; });
            tab.Controls.Add(_gridPorts);
            tab.Controls.Add(bar);
        }

        private void BuildSubTabStartup(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(8, 12, 21);
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top; bar.Height = 36; bar.Padding = new Padding(8, 5, 8, 3); bar.WrapContents = false; bar.BackColor = Color.FromArgb(14, 21, 38);

            Button bRef = MakeBtn("Scan Registry & Startup Folders", Color.FromArgb(2, 132, 199));
            bRef.Click += async delegate { await RefreshTunerTablesAsync("startup"); };

            Button bDisable = MakeBtn("Disable Selected Startup App (Reversible)", Color.FromArgb(180, 83, 9));
            bDisable.Click += async delegate { await ToggleSelectedStartupItemAsync(false); };

            Button bEnable = MakeBtn("Re-Enable Selected Startup App", Color.FromArgb(5, 150, 105));
            bEnable.Click += async delegate { await ToggleSelectedStartupItemAsync(true); };

            Label lNote = MakeLbl(
                "Uses Windows 11 native StartupApproved flags + reversible shortcut parking (never deletes registry entries!).",
                null,
                Color.FromArgb(148, 163, 184));
            lNote.Padding = new Padding(8, 6, 0, 0);

            bar.Controls.AddRange(new Control[] { bRef, bDisable, bEnable, lNote });
            _gridStartup = MakeStyledGrid();
            AttachPipeContextMenu(_gridStartup, delegate { return "startup"; });
            tab.Controls.Add(_gridStartup);
            tab.Controls.Add(bar);
        }

        private void BuildSubTabServices(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(8, 12, 21);
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top; bar.Height = 36; bar.Padding = new Padding(8, 5, 8, 3); bar.WrapContents = false; bar.BackColor = Color.FromArgb(14, 21, 38);

            Label lS = MakeLbl("Search Service:", null, Color.FromArgb(148, 163, 184));
            lS.Padding = new Padding(0, 5, 4, 0);
            _txtSvcFilter = new TextBox(); _txtSvcFilter.Width = 165; _txtSvcFilter.BackColor = Color.FromArgb(6, 9, 17); _txtSvcFilter.ForeColor = Color.White;
            _txtSvcFilter.TextChanged += async delegate { await RefreshTunerTablesAsync("services"); };

            _chkSvcRunningOnly = new CheckBox(); _chkSvcRunningOnly.Text = "Running Only"; _chkSvcRunningOnly.Checked = true; _chkSvcRunningOnly.AutoSize = true;
            _chkSvcRunningOnly.Padding = new Padding(6, 4, 6, 0); _chkSvcRunningOnly.ForeColor = Color.FromArgb(52, 211, 153);
            _chkSvcRunningOnly.CheckedChanged += async delegate { await RefreshTunerTablesAsync("services"); };

            Button bRef = MakeBtn("Refresh Services + GPU VRAM", Color.FromArgb(2, 132, 199));
            bRef.Click += async delegate { await RefreshTunerTablesAsync("services"); };

            Button bStart = MakeBtn("Start Service", Color.FromArgb(5, 150, 105));
            bStart.Click += async delegate { await ControlSelectedServiceAsync("start"); };

            Button bStop = MakeBtn("Stop Service", Color.FromArgb(153, 27, 27));
            bStop.Click += async delegate { await ControlSelectedServiceAsync("stop"); };

            Button bRestart = MakeBtn("Restart Service", Color.FromArgb(109, 40, 217));
            bRestart.Click += async delegate { await ControlSelectedServiceAsync("restart"); };

            bar.Controls.AddRange(new Control[] { lS, _txtSvcFilter, _chkSvcRunningOnly, bRef, bStart, bStop, bRestart });

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            _gridServices = MakeStyledGrid();
            AttachPipeContextMenu(_gridServices, delegate { return "services"; });

            Panel gpuPanel = new Panel();
            gpuPanel.Dock = DockStyle.Fill;
            gpuPanel.BackColor = Color.FromArgb(9, 13, 22);

            Label lGpuHdr = MakeLbl(
                "NVIDIA DirectCompute GPU ACTIVE COMPUTE VRAM PROCESSES:",
                new Font("Segoe UI", 8.4f, FontStyle.Bold),
                Color.FromArgb(192, 132, 252));
            lGpuHdr.AutoSize = false;
            lGpuHdr.Dock = DockStyle.Top; lGpuHdr.Height = 24; lGpuHdr.Padding = new Padding(6, 4, 0, 0);
            lGpuHdr.BackColor = Color.FromArgb(14, 21, 38);

            _gridGpuVram = MakeStyledGrid();
            AttachPipeContextMenu(_gridGpuVram, delegate { return "gpu"; });
            gpuPanel.Controls.Add(_gridGpuVram);
            gpuPanel.Controls.Add(lGpuHdr);

            split.Panel1.Controls.Add(_gridServices);
            split.Panel2.Controls.Add(gpuPanel);

            tab.Controls.Add(split);
            tab.Controls.Add(bar);

            ApplySplitterOnTabVisible(tab, delegate { split.SplitterDistance = Math.Max(620, (split.Width * 64) / 100); });
        }

        private void BuildSubTabDiskOffloader(TabPage tab)
        {
            tab.BackColor = Color.FromArgb(8, 12, 21);

            Panel topInfo = new Panel();
            topInfo.Dock = DockStyle.Top;
            topInfo.Height = 68;
            topInfo.Padding = new Padding(8, 4, 8, 4);
            topInfo.BackColor = Color.FromArgb(14, 21, 38);

            _lblDiskOffloadBanner = MakeLbl(
                "C:\\ SPACE RELIEF & NTFS JUNCTION OFFLOADER :: Move heavy AI models, Rust/Cargo caches & downloads to F:\\ (2.33 TB) or E:\\ while apps still see original C:\\ paths via mklink /J!",
                new Font("Consolas", 8.6f, FontStyle.Bold),
                Color.FromArgb(52, 211, 153));
            _lblDiskOffloadBanner.AutoSize = false;
            _lblDiskOffloadBanner.Dock = DockStyle.Top;
            _lblDiskOffloadBanner.Height = 24;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.Padding = new Padding(0, 3, 0, 0);
            bar.WrapContents = false;

            Button bScanC = MakeBtn("1. Scan C:\\ Space Hogs", Color.FromArgb(2, 132, 199));
            bScanC.Click += async delegate { await RefreshTunerTablesAsync("diskhogs"); };

            Button bCustomC = MakeBtn("+ Measure Custom C:\\ Folder...", Color.FromArgb(30, 58, 138));
            bCustomC.Click += async delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Choose a non-system folder on C:\\ to measure and stage for mklink /J junction offload:";
                    fbd.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
                    {
                        string picked = fbd.SelectedPath;
                        SpaceHogEntry custom = await Task.Run(delegate
                        {
                            return DiskSpaceOffloader.MeasureDirectoryBounded("Custom C:\\ Folder", picked, "User-picked C:\\ directory", 6, 30000);
                        });
                        _cachedSpaceHogs.Insert(0, custom);
                        RenderSpaceHogGrid();
                        SetTunerFeedback("Measured custom folder '" + picked + "' -> " + custom.SizeMb.ToString("0.0") + " MB (" + custom.FileCount + " files).");
                    }
                }
            };

            Label lVault = MakeLbl("Target Vault:", null, Color.FromArgb(250, 204, 21));
            lVault.Padding = new Padding(8, 5, 4, 0);

            _cmbOffloadVaultDrive = new ComboBox();
            _cmbOffloadVaultDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbOffloadVaultDrive.Width = 225;
            _cmbOffloadVaultDrive.BackColor = Color.FromArgb(6, 9, 17);
            _cmbOffloadVaultDrive.ForeColor = Color.White;
            _cmbOffloadVaultDrive.Items.Add("F:\\ (2.33 TB Cold NTFS Vault)");
            _cmbOffloadVaultDrive.Items.Add("E:\\ (Hot Lab SSD Vault)");
            _cmbOffloadVaultDrive.SelectedIndex = Directory.Exists("F:\\") ? 0 : 1;

            Button bOffload = MakeBtn("2. Offload Selected -> Vault (mklink /J)", Color.FromArgb(5, 150, 105));
            bOffload.Click += async delegate { await OffloadSelectedHogWithJunctionAsync(); };

            Button bUndoJunc = MakeBtn("Undo Selected Junction -> Back to C:\\", Color.FromArgb(180, 83, 9));
            bUndoJunc.Click += async delegate { await UndoSelectedJunctionToCAsync(); };

            Button bOpenVault = MakeBtn("Open OMEGA_JUNCTION_VAULT", Color.FromArgb(109, 40, 217));
            bOpenVault.Click += delegate
            {
                string drv = (_cmbOffloadVaultDrive.SelectedIndex == 0) ? "F:\\" : "E:\\";
                string vdir = Path.Combine(drv, "OMEGA_JUNCTION_VAULT");
                try { Directory.CreateDirectory(vdir); Process.Start("explorer.exe", "\"" + vdir + "\""); } catch { }
            };

            bar.Controls.AddRange(new Control[] { bScanC, bCustomC, lVault, _cmbOffloadVaultDrive, bOffload, bUndoJunc, bOpenVault });

            topInfo.Controls.Add(bar);
            topInfo.Controls.Add(_lblDiskOffloadBanner);

            _gridSpaceHogs = MakeStyledGrid();
            _gridSpaceHogs.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs cf)
            {
                if (cf.ColumnIndex >= 0 && cf.ColumnIndex < _gridSpaceHogs.Columns.Count && _gridSpaceHogs.Columns[cf.ColumnIndex].Name == "state" && cf.Value != null)
                {
                    string st = cf.Value.ToString();
                    if (st == "JUNCTION_ACTIVE") cf.CellStyle.ForeColor = Color.FromArgb(52, 211, 153);
                    else cf.CellStyle.ForeColor = Color.FromArgb(250, 204, 21);
                }
            };
            AttachPipeContextMenu(_gridSpaceHogs, delegate { return "disk-hogs"; });

            tab.Controls.Add(_gridSpaceHogs);
            tab.Controls.Add(topInfo);
        }

        private async Task OffloadSelectedHogWithJunctionAsync()
        {
            if (_gridSpaceHogs.SelectedRows.Count == 0 || !_gridSpaceHogs.Columns.Contains("path"))
            {
                SetTunerFeedback("Select a C:\\ folder row first in the Space-Hog table.");
                return;
            }
            string srcPath = (_gridSpaceHogs.SelectedRows[0].Cells["path"].Value ?? "").ToString();
            string sizeMb = (_gridSpaceHogs.SelectedRows[0].Cells["size_mb"].Value ?? "0").ToString();
            string state = (_gridSpaceHogs.SelectedRows[0].Cells["state"].Value ?? "").ToString();

            if (state == "JUNCTION_ACTIVE")
            {
                SetTunerFeedback("'" + srcPath + "' is already an active NTFS junction!");
                return;
            }

            string blockReason;
            if (!DiskSpaceOffloader.IsSafeToJunctionOffload(srcPath, out blockReason))
            {
                MessageBox.Show(this, blockReason, "OS Guardrail Blocked Offload", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string drvRoot = (_cmbOffloadVaultDrive != null && _cmbOffloadVaultDrive.SelectedIndex == 0) ? "F:\\" : "E:\\";
            DialogResult ok = MessageBox.Show(
                this,
                "Offload C:\\ directory to " + drvRoot + "OMEGA_JUNCTION_VAULT with zero-breakage NTFS junction (mklink /J)?\r\n\r\n" +
                "  Source on C:\\ :  " + srcPath + "\r\n" +
                "  Size on C:\\   :  " + sizeMb + " MB\r\n" +
                "  Target Vault  :  " + drvRoot + "OMEGA_JUNCTION_VAULT\\\r\n\r\n" +
                "Safety Pipeline:\r\n" +
                "  1. Pre-checks for open file locks so active apps aren't interrupted.\r\n" +
                "  2. Copies 100% of files and verifies exact byte-count parity.\r\n" +
                "  3. Atomically swaps C:\\ folder into an mklink /J junction (instant 1-click Undo anytime).",
                "Confirm C:\\ -> " + drvRoot + " NTFS Junction Offload",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (ok != DialogResult.Yes) return;

            SetTunerFeedback("Copying '" + srcPath + "' -> " + drvRoot + "OMEGA_JUNCTION_VAULT with byte-parity check + mklink /J...");
            string sr = _shellRoot;
            string resMsg = await Task.Run(delegate { return DiskSpaceOffloader.OffloadDirectoryWithJunction(srcPath, drvRoot, sr); });
            SetTunerFeedback(resMsg);
            RefreshDriveExplorerButtons();
            await RefreshTunerTablesAsync("diskhogs");
        }

        private async Task UndoSelectedJunctionToCAsync()
        {
            if (_gridSpaceHogs.SelectedRows.Count == 0 || !_gridSpaceHogs.Columns.Contains("path")) return;
            string juncPath = (_gridSpaceHogs.SelectedRows[0].Cells["path"].Value ?? "").ToString();
            string sr = _shellRoot;
            SetTunerFeedback("Restoring junction '" + juncPath + "' back onto physical C:\\ SSD...");
            string resMsg = await Task.Run(delegate { return DiskSpaceOffloader.UndoJunctionBackToC(juncPath, sr); });
            SetTunerFeedback(resMsg);
            RefreshDriveExplorerButtons();
            await RefreshTunerTablesAsync("diskhogs");
        }

        private void RenderSpaceHogGrid()
        {
            List<OshRow> rows = new List<OshRow>();
            double reclaimableMb = 0.0;
            for (int i = 0; i < _cachedSpaceHogs.Count; i++)
            {
                SpaceHogEntry h = _cachedSpaceHogs[i];
                if (!h.IsJunction) reclaimableMb += h.SizeMb;
                rows.Add(new OshRow()
                    .Set("category", h.Category)
                    .Set("size_mb", h.SizeMb)
                    .Set("files", h.FileCount)
                    .Set("state", h.IsJunction ? "JUNCTION_ACTIVE" : "ON_C_DRIVE")
                    .Set("path", h.PathOnC)
                    .Set("vault_or_note", h.IsJunction ? h.JunctionTarget : h.SafeNote));
            }
            PopulateGridWithOshRows(_gridSpaceHogs, rows);

            if (_lblDiskOffloadBanner != null)
            {
                _lblDiskOffloadBanner.Text = "C:\\ SPACE SCAN COMPLETE :: " + Math.Round(reclaimableMb / 1024.0, 2) +
                    " GB reclaimable across " + _cachedSpaceHogs.Count + " candidate paths | Live Free: " + BuildCompactDriveRibbon();
            }
        }

        private List<TcpSocketItem> _filteredSockets = new List<TcpSocketItem>();

        private async Task RefreshTunerTablesAsync(string which)
        {
            if (which == "processes")
            {
                string flt = (_txtProcFilter != null ? _txtProcFilter.Text : "").Trim().ToLowerInvariant();
                Dictionary<string, string> rulesSnap = new Dictionary<string, string>(_settings.ProcessCoreRules, StringComparer.OrdinalIgnoreCase);
                List<OshRow> rows = await Task.Run(delegate { return OshShellRuntime.BuildProcessRows(rulesSnap); });
                if (flt.Length > 0)
                {
                    List<OshRow> sub = new List<OshRow>();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i].GetString("name").ToLowerInvariant().IndexOf(flt) >= 0 ||
                            rows[i].GetString("pid").IndexOf(flt) >= 0 ||
                            rows[i].GetString("core_tier").ToLowerInvariant().IndexOf(flt) >= 0)
                            sub.Add(rows[i]);
                    }
                    rows = sub;
                }
                PopulateGridWithOshRows(_gridProcs, rows);
            }
            else if (which == "ports")
            {
                _cachedSockets = await Task.Run(delegate { return Win32HardwareTuner.GetLiveTcpSockets(); });
                RenderFilteredSockets();
            }
            else if (which == "startup")
            {
                string sr = _shellRoot;
                _cachedStartups = await Task.Run(delegate { return Win32HardwareTuner.GetStartupApps(sr); });
                List<OshRow> rows = new List<OshRow>();
                for (int i = 0; i < _cachedStartups.Count; i++)
                {
                    StartupAppItem st = _cachedStartups[i];
                    rows.Add(new OshRow().Set("name", st.Name).Set("status", st.Enabled ? "ENABLED" : "DISABLED").Set("location", st.LocationTag).Set("command", st.Command));
                }
                PopulateGridWithOshRows(_gridStartup, rows);
            }
            else if (which == "services")
            {
                string q = (_txtSvcFilter != null ? _txtSvcFilter.Text : "").Trim();
                bool runOnly = _chkSvcRunningOnly != null && _chkSvcRunningOnly.Checked;
                List<WindowsServiceItem> svcs = await Task.Run(delegate { return Win32HardwareTuner.GetWindowsServices(q, runOnly); });
                List<OshRow> sRows = new List<OshRow>();
                for (int i = 0; i < svcs.Count; i++)
                    sRows.Add(new OshRow().Set("service", svcs[i].ServiceName).Set("status", svcs[i].Status).Set("start_mode", svcs[i].StartMode).Set("can_stop", svcs[i].CanStop).Set("display_name", svcs[i].DisplayName));
                PopulateGridWithOshRows(_gridServices, sRows);

                List<OshRow> gRows = await Task.Run(delegate { return Win32HardwareTuner.QueryNvidiaVramProcessRows(); });
                PopulateGridWithOshRows(_gridGpuVram, gRows);
            }
            else if (which == "diskhogs")
            {
                string sr = _shellRoot;
                SetTunerFeedback("Scanning C:\\ developer caches, AI model stores & active junctions...");
                _cachedSpaceHogs = await Task.Run(delegate { return DiskSpaceOffloader.ScanCDriveHogs(sr); });
                RenderSpaceHogGrid();
                SetTunerFeedback("Scanned C:\\ space hogs (" + _cachedSpaceHogs.Count + " items ranked by size). Select any ON_C_DRIVE row to offload via mklink /J!");
            }
        }

        private void RenderFilteredSockets()
        {
            string q = (_txtPortFilter != null ? _txtPortFilter.Text : "").Trim().ToLowerInvariant();
            bool listenOnly = _chkListenOnly != null && _chkListenOnly.Checked;
            _filteredSockets.Clear();
            List<OshRow> rows = new List<OshRow>();

            for (int i = 0; i < _cachedSockets.Count; i++)
            {
                TcpSocketItem sk = _cachedSockets[i];
                if (listenOnly && sk.StateName != "LISTEN") continue;
                if (q.Length > 0 && sk.LocalPort.ToString().IndexOf(q) < 0 && sk.ProcessName.ToLowerInvariant().IndexOf(q) < 0 && sk.StateName.ToLowerInvariant().IndexOf(q) < 0)
                    continue;
                _filteredSockets.Add(sk);
                rows.Add(new OshRow()
                    .Set("port", sk.LocalPort)
                    .Set("state", sk.StateName)
                    .Set("pid", sk.Pid)
                    .Set("process", sk.ProcessName)
                    .Set("ram_mb", sk.RamMb)
                    .Set("local_ip", sk.LocalIp)
                    .Set("remote", sk.RemoteIp + ":" + sk.RemotePort));
            }
            PopulateGridWithOshRows(_gridPorts, rows);
        }

        private async Task ApplySelectedProcAffinityAsync(bool toPCores)
        {
            if (_gridProcs.SelectedRows.Count == 0) return;
            List<int> pids = new List<int>();
            for (int i = 0; i < _gridProcs.SelectedRows.Count; i++)
            {
                int pid;
                if (int.TryParse((_gridProcs.SelectedRows[i].Cells["pid"].Value ?? "").ToString(), out pid) && pid > 4)
                    pids.Add(pid);
            }
            if (pids.Count == 0) return;

            long pMask, eMask, allMask;
            Win32HardwareTuner.GetHybridCoreMasks(out pMask, out eMask, out allMask);
            string msg = await Task.Run(delegate
            {
                int okCount = 0;
                string lastDetail = "";
                for (int k = 0; k < pids.Count; k++)
                {
                    string d;
                    bool ok = toPCores
                        ? Win32HardwareTuner.SetProcessEcoQoSAndAffinity(pids[k], false, pMask, ProcessPriorityClass.AboveNormal, out d)
                        : Win32HardwareTuner.SetProcessEcoQoSAndAffinity(pids[k], true, eMask, ProcessPriorityClass.BelowNormal, out d);
                    if (ok) okCount++;
                    lastDetail = d;
                }
                return pids.Count == 1
                    ? lastDetail
                    : ((toPCores ? "Pinned " : "Parked ") + okCount + "/" + pids.Count + " selected processes (" + lastDetail + ")");
            });
            SetTunerFeedback(msg);
            await RefreshTunerTablesAsync("processes");
        }

        private async Task SaveSelectedProcCoreRuleAsync(string mode)
        {
            if (_gridProcs.SelectedRows.Count == 0) return;
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _gridProcs.SelectedRows.Count; i++)
            {
                string nm = (_gridProcs.SelectedRows[i].Cells["name"].Value ?? "").ToString().Trim();
                if (!string.IsNullOrEmpty(nm)) names.Add(nm);
            }
            if (names.Count == 0) return;

            foreach (string nm in names)
            {
                if (mode == "clear") _settings.ProcessCoreRules.Remove(nm);
                else _settings.ProcessCoreRules[nm] = (mode == "pcores") ? "P-CORES" : "E-CORES";
            }
            _settings.SaveToDisk(_configDir);

            if (mode == "pcores") await ApplySelectedProcAffinityAsync(true);
            else if (mode == "eco") await ApplySelectedProcAffinityAsync(false);
            else await RefreshTunerTablesAsync("processes");

            SetTunerFeedback(mode == "clear"
                ? ("Cleared persistent Core Tier rule for " + names.Count + " process name(s).")
                : ("Saved persistent '" + (mode == "pcores" ? "P-CORES" : "E-CORES") + "' rule for: " + string.Join(", ", new List<string>(names).ToArray())));
        }

        private async Task ToggleSelectedStartupItemAsync(bool enable)
        {
            if (_gridStartup.SelectedRows.Count == 0) return;
            int idx = _gridStartup.SelectedRows[0].Index;
            if (idx < 0 || idx >= _cachedStartups.Count) return;
            StartupAppItem target = _cachedStartups[idx];
            string sr = _shellRoot;
            string msg = await Task.Run(delegate { return Win32HardwareTuner.SetStartupAppEnabled(target, enable, sr); });
            SetTunerFeedback(msg);
            await RefreshTunerTablesAsync("startup");
        }

        private async Task ControlSelectedServiceAsync(string act)
        {
            if (_gridServices.SelectedRows.Count == 0) return;
            string svcName = (_gridServices.SelectedRows[0].Cells["service"].Value ?? "").ToString();
            string msg = await Task.Run(delegate { return Win32HardwareTuner.ControlWindowsService(svcName, act); });
            SetTunerFeedback(msg);
            await RefreshTunerTablesAsync("services");
        }

        private void UpdateTunerLiveBanner(double cpuPct, MemorySnapshot mem, string gpuLine)
        {
            if (_lblTunerLiveCards == null) return;
            long pMask, eMask, allMask;
            Win32HardwareTuner.GetHybridCoreMasks(out pMask, out eMask, out allMask);
            _lblTunerLiveCards.Text = "CPU: " + cpuPct.ToString("0.0") + "% (P=0x" + pMask.ToString("X") + " / E=0x" + eMask.ToString("X") + ")  |  DDR5 RAM: " +
                mem.UsedPhysMb + "/" + mem.TotalPhysMb + " MB (" + mem.AvailPhysMb + " MB Free)  |  " + gpuLine;
        }

        private void SetTunerFeedback(string msg)
        {
            if (_lblTunerActionResult != null) _lblTunerActionResult.Text = msg;
            SetSingleLineStatus(msg);
        }
    }
}
