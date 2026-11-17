using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OmegaShellStudio
{
    public sealed partial class OmegaShellForm
    {
        private DataGridView _gridLabServices;
        private TextBox _txtOshrcEditor, _txtWhitelistEditor, _txtAiEpSet, _txtAiModelSet;
        private NumericUpDown _nudFontSet;

        private void BuildTab4SettingsMonitorUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            Panel monPane = new Panel();
            monPane.Dock = DockStyle.Fill;
            monPane.Padding = new Padding(10);
            monPane.BackColor = Color.FromArgb(9, 13, 22);

            Label lMonHdr = MakeLbl(
                "OMEGALABS v2.4.1 DODECA-SUITE & LOCAL GEMMA LIVE HEARTBEAT MONITOR (iphlpapi Port-to-PID):",
                new Font("Segoe UI", 9.2f, FontStyle.Bold),
                Color.FromArgb(56, 189, 248));
            lMonHdr.AutoSize = false;
            lMonHdr.Dock = DockStyle.Top;
            lMonHdr.Height = 26;

            FlowLayoutPanel monActions = new FlowLayoutPanel();
            monActions.Dock = DockStyle.Top;
            monActions.AutoSize = true;
            monActions.MinimumSize = new Size(0, 38);
            monActions.WrapContents = true;
            monActions.Padding = new Padding(0, 4, 0, 4);

            Button bRefreshMon = MakeBtn("Refresh", Color.FromArgb(2, 132, 199));
            bRefreshMon.Click += delegate { RefreshLabMonitorCardsAsync(); };

            Button bLaunchSel = MakeBtn("Launch Selected", Color.FromArgb(5, 150, 105));
            bLaunchSel.Click += delegate { LaunchOrStopSelectedLabRow(true); };

            Button bStopSel = MakeBtn("Stop Selected", Color.FromArgb(153, 27, 27));
            bStopSel.Click += delegate { LaunchOrStopSelectedLabRow(false); };

            Button bOpenLauncher = MakeBtn("Main Launcher v2.4.1", Color.FromArgb(109, 40, 217));
            bOpenLauncher.Click += delegate
            {
                string lx = Path.Combine(_suiteRoot, "OmegaLabLauncher.exe");
                if (File.Exists(lx)) Process.Start(lx);
            };

            Button bOpenSuiteDir = MakeBtn("Lab Root (E:\\)", Color.FromArgb(15, 118, 110));
            bOpenSuiteDir.Click += delegate { if (Directory.Exists(_suiteRoot)) Process.Start("explorer.exe", "\"" + _suiteRoot + "\""); };

            monActions.Controls.AddRange(new Control[] { bRefreshMon, bLaunchSel, bStopSel, bOpenLauncher, bOpenSuiteDir });

            _gridLabServices = MakeStyledGrid();
            _gridLabServices.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs cf)
            {
                if (cf.ColumnIndex >= 0 && cf.ColumnIndex < _gridLabServices.Columns.Count && _gridLabServices.Columns[cf.ColumnIndex].Name == "status" && cf.Value != null)
                {
                    cf.CellStyle.ForeColor = (cf.Value.ToString() == "ONLINE") ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                }
            };
            AttachPipeContextMenu(_gridLabServices, delegate { return "ports -listen"; });

            monPane.Controls.Add(_gridLabServices);
            monPane.Controls.Add(monActions);
            monPane.Controls.Add(lMonHdr);

            Panel setPane = new Panel();
            setPane.Dock = DockStyle.Fill;
            setPane.Padding = new Padding(12);
            setPane.BackColor = Color.FromArgb(10, 15, 27);

            Label lSetHdr = MakeLbl(
                "OMEGASHELL v1.1.5 GM SETTINGS, GEMMA ENDPOINT, .OSHRC & PROTECTED WHITELIST",
                new Font("Segoe UI", 9.2f, FontStyle.Bold),
                Color.FromArgb(192, 132, 252));
            lSetHdr.AutoSize = false;
            lSetHdr.Dock = DockStyle.Top;
            lSetHdr.Height = 26;

            FlowLayoutPanel prefsRow = new FlowLayoutPanel();
            prefsRow.Dock = DockStyle.Top;
            prefsRow.AutoSize = true;
            prefsRow.MinimumSize = new Size(0, 40);
            prefsRow.WrapContents = true;
            prefsRow.Padding = new Padding(0, 5, 0, 4);

            Label lFs = MakeLbl("Font:", null, Color.FromArgb(203, 213, 225));
            lFs.Padding = new Padding(0, 6, 4, 0);
            _nudFontSet = new NumericUpDown(); _nudFontSet.Width = 54; _nudFontSet.DecimalPlaces = 1; _nudFontSet.Minimum = 8.0m; _nudFontSet.Maximum = 16.0m; _nudFontSet.Increment = 0.5m;
            _nudFontSet.Value = (decimal)_settings.FontSize; _nudFontSet.BackColor = Color.FromArgb(6, 9, 17); _nudFontSet.ForeColor = Color.White;

            Label lEp = MakeLbl("Gemma URL:", null, Color.FromArgb(203, 213, 225));
            lEp.Padding = new Padding(6, 6, 4, 0);
            _txtAiEpSet = new TextBox(); _txtAiEpSet.Width = 205; _txtAiEpSet.Text = _settings.DefaultAiEndpoint;
            _txtAiEpSet.BackColor = Color.FromArgb(6, 9, 17); _txtAiEpSet.ForeColor = Color.FromArgb(125, 211, 252);

            Label lMd = MakeLbl("Model:", null, Color.FromArgb(203, 213, 225));
            lMd.Padding = new Padding(6, 6, 4, 0);
            _txtAiModelSet = new TextBox(); _txtAiModelSet.Width = 140; _txtAiModelSet.Text = _settings.DefaultAiModel;
            _txtAiModelSet.BackColor = Color.FromArgb(6, 9, 17); _txtAiModelSet.ForeColor = Color.White;

            Button bSaveSet = MakeBtn("Save Settings", Color.FromArgb(5, 150, 105));
            bSaveSet.Click += delegate { SaveAllShellSettingsFromUi(); };

            prefsRow.Controls.AddRange(new Control[] { lFs, _nudFontSet, lEp, _txtAiEpSet, lMd, _txtAiModelSet, bSaveSet });

            SplitContainer edSplit = new SplitContainer();
            edSplit.Dock = DockStyle.Fill;
            edSplit.Orientation = Orientation.Horizontal;
            edSplit.BackColor = Color.FromArgb(30, 41, 59);
            edSplit.SplitterWidth = 5;

            Panel oshrcPanel = new Panel(); oshrcPanel.Dock = DockStyle.Fill;
            Label lOshrc = MakeLbl(
                "STARTUP SCRIPT (config\\init.oshrc — runs automatically when OmegaShell starts):",
                new Font("Segoe UI Semibold", 8.4f),
                Color.FromArgb(52, 211, 153));
            lOshrc.AutoSize = false;
            lOshrc.Dock = DockStyle.Top; lOshrc.Height = 22;

            _txtOshrcEditor = new TextBox(); _txtOshrcEditor.Dock = DockStyle.Fill; _txtOshrcEditor.Multiline = true; _txtOshrcEditor.ScrollBars = ScrollBars.Vertical;
            _txtOshrcEditor.BackColor = Color.FromArgb(6, 9, 17); _txtOshrcEditor.ForeColor = Color.FromArgb(226, 232, 240); _txtOshrcEditor.Font = new Font("Consolas", 9.0f);
            _txtOshrcEditor.Text = File.Exists(_oshrcPath) ? File.ReadAllText(_oshrcPath, Encoding.UTF8) : "";
            oshrcPanel.Controls.Add(_txtOshrcEditor);
            oshrcPanel.Controls.Add(lOshrc);

            Panel wlPanel = new Panel(); wlPanel.Dock = DockStyle.Fill;
            Label lWl = MakeLbl(
                "PROTECTED OPTIMIZER WHITELIST (One process name per line — skipped by RAM Trimmer & EcoQoS Parker):",
                new Font("Segoe UI Semibold", 8.4f),
                Color.FromArgb(250, 204, 21));
            lWl.AutoSize = false;
            lWl.Dock = DockStyle.Top; lWl.Height = 22;

            _txtWhitelistEditor = new TextBox(); _txtWhitelistEditor.Dock = DockStyle.Fill; _txtWhitelistEditor.Multiline = true; _txtWhitelistEditor.ScrollBars = ScrollBars.Vertical;
            _txtWhitelistEditor.BackColor = Color.FromArgb(6, 9, 17); _txtWhitelistEditor.ForeColor = Color.FromArgb(226, 232, 240); _txtWhitelistEditor.Font = new Font("Consolas", 8.8f);
            List<string> wlSorted = new List<string>(_settings.ProtectedProcesses);
            wlSorted.Sort(StringComparer.OrdinalIgnoreCase);
            _txtWhitelistEditor.Text = string.Join("\r\n", wlSorted.ToArray());
            wlPanel.Controls.Add(_txtWhitelistEditor);
            wlPanel.Controls.Add(lWl);

            edSplit.Panel1.Controls.Add(oshrcPanel);
            edSplit.Panel2.Controls.Add(wlPanel);

            setPane.Controls.Add(edSplit);
            setPane.Controls.Add(prefsRow);
            setPane.Controls.Add(lSetHdr);

            split.Panel1.Controls.Add(monPane);
            split.Panel2.Controls.Add(setPane);
            page.Controls.Add(split);

            _mainTabs.SelectedIndexChanged += delegate
            {
                if (_mainTabs.SelectedIndex == 5) RefreshLabMonitorCardsAsync();
            };

            ApplySplitterOnTabVisible(page, delegate
            {
                split.SplitterDistance = Math.Max(620, (split.Width * 48) / 100);
                edSplit.SplitterDistance = Math.Max(220, (edSplit.Height * 50) / 100);
                RefreshLabMonitorCardsAsync();
            });
        }

        private void RefreshLabMonitorCardsAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<TcpSocketItem> liveSocks = Win32HardwareTuner.GetLiveTcpSockets();
                    List<OshRow> rows = new List<OshRow>();
                    AddLabAppRow(rows, liveSocks, "OmegaMirror v0.9", "OmegaMirror", 19777, Path.Combine(_suiteRoot, "01_omega_mirror", "OmegaMirror.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaBrowser v0.6.6", "OmegaBrowser", 9778, Path.Combine(_suiteRoot, "02_omega_browser", "OmegaBrowser.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaAV v0.7.1", "OmegaAV", 19779, Path.Combine(_suiteRoot, "03_omega_av", "OmegaAV.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaSynth v1.1.1 GM", "OmegaSynth", 19782, Path.Combine(_suiteRoot, "04_omega_synth", "OmegaSynth.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaSonar v1.1.0 GM (:19783)", "OmegaSonar", 19783, Path.Combine(_suiteRoot, "05_omega_sonar", "OmegaSonar.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaSheaf v0.2.2 GM", "OmegaSheaf", 19784, Path.Combine(_suiteRoot, "06_omega_sheaf", "OmegaSheaf.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaPenrose v1.0.2 GM", "OmegaPenrose", 19785, Path.Combine(_suiteRoot, "07_omega_penrose", "OmegaPenrose.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaSteg v0.2.1 OmegaSec", "OmegaSteg", 19786, Path.Combine(_suiteRoot, "08_omega_steg", "OmegaSteg.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaForge v0.2.2", "OmegaForge", 19780, Path.Combine(_suiteRoot, "09_omega_forge", "OmegaForge.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaShell v1.1.6 GM", "OmegaShell", 19781, Path.Combine(_suiteRoot, "10_omega_shell", "OmegaShell.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaConductor v1.0.0 GM (:19790)", "OmegaConductor", 19790, Path.Combine(_suiteRoot, "12_omega_conductor", "OmegaConductor.exe"));
                    AddLabAppRow(rows, liveSocks, "OmegaTensor v1.0.2 GM (:19791)", "OmegaTensor", 19791, Path.Combine(_suiteRoot, "13_omega_tensor", "OmegaTensor.exe"));
                    AddLabAppRow(rows, liveSocks, "Pixel 10 10-Kernel Tensor", "adb", 19788, Path.Combine(_suiteRoot, "11_omega_edge_workers\rust_p10_worker", "Deploy-And-Run-Pixel10Worker.ps1"));
                    AddLabAppRow(rows, liveSocks, "AVF Virtio-GPU Worker (:19789)", "ssh/adb", 19789, "http://127.0.0.1:19789/api/health");
                    AddLabAppRow(rows, liveSocks, "Android AVF SSH (:22222)", "adb", 22222, "adb forward tcp:22222 tcp:2222");
                    AddLabAppRow(rows, liveSocks, "Sovereign Manifold UTCP", "sovereign", 8097, (Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')));
                    AddLabAppRow(rows, liveSocks, "Ollama Local Engine", "ollama", 11434, "ollama.exe");

                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)delegate { PopulateGridWithOshRows(_gridLabServices, rows); });
                    }
                }
                catch { }
            });
        }

        private static void AddLabAppRow(List<OshRow> rows, List<TcpSocketItem> liveSocks, string title, string procName, int port, string exePath)
        {
            bool portUp = MeshAndLabBridge.ProbePortFast(port, 75);
            Process[] ps = Process.GetProcessesByName(procName);
            long mb = 0L;
            int pid = 0;
            string resolvedProcKey = procName;
            string resolvedExe = exePath;

            if (procName != "adb")
            {
                for (int i = 0; i < ps.Length; i++)
                {
                    try { mb += ps[i].WorkingSet64 / 1048576L; if (pid == 0) pid = ps[i].Id; } catch { }
                    finally { try { ps[i].Dispose(); } catch { } }
                }
            }
            else
            {
                for (int i = 0; i < ps.Length; i++) { try { ps[i].Dispose(); } catch { } }
            }

            if (pid == 0 && liveSocks != null)
            {
                for (int k = 0; k < liveSocks.Count; k++)
                {
                    TcpSocketItem sk = liveSocks[k];
                    if (sk.LocalPort == port && (sk.StateName == "LISTEN" || portUp) && sk.Pid > 0)
                    {
                        pid = sk.Pid;
                        mb = sk.RamMb;
                        if (!string.IsNullOrEmpty(sk.ProcessName)) resolvedProcKey = sk.ProcessName;
                        try
                        {
                            using (Process p = Process.GetProcessById(pid))
                            {
                                if (mb <= 0) mb = p.WorkingSet64 / 1048576L;
                                string modPath = p.MainModule != null ? p.MainModule.FileName : "";
                                if (!string.IsNullOrEmpty(modPath)) resolvedExe = modPath;
                            }
                        }
                        catch { }
                        break;
                    }
                }
            }

            bool running = (procName == "adb") ? portUp : (ps.Length > 0 || pid > 0 || portUp);
            rows.Add(new OshRow()
                .Set("component", title)
                .Set("status", running ? "ONLINE" : "OFFLINE")
                .Set("port", ":" + port)
                .Set("pid", pid > 0 ? pid.ToString() : "-")
                .Set("ram_mb", mb)
                .Set("proc_key", resolvedProcKey)
                .Set("exe", resolvedExe));
        }

        private void LaunchOrStopSelectedLabRow(bool launch)
        {
            if (_gridLabServices.SelectedRows.Count == 0) return;
            string procKey = (_gridLabServices.SelectedRows[0].Cells["proc_key"].Value ?? "").ToString();
            string exePath = (_gridLabServices.SelectedRows[0].Cells["exe"].Value ?? "").ToString();
            string pidCell = (_gridLabServices.SelectedRows[0].Cells["pid"].Value ?? "").ToString();

            if (launch)
            {
                if (exePath.StartsWith("adb forward"))
                {
                    SetSingleLineStatus(AndroidAvfDebianBridge.ArmAdbSshTunnels(""));
                }
                else if (File.Exists(exePath))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exePath);
                    psi.WorkingDirectory = Path.GetDirectoryName(exePath);
                    Process.Start(psi);
                    SetSingleLineStatus("Launched " + procKey + " -> " + exePath);
                }
                else if (procKey.IndexOf("ollama", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ProcessStartInfo psi = new ProcessStartInfo("ollama.exe", "serve");
                    psi.CreateNoWindow = true; psi.UseShellExecute = false;
                    try { Process.Start(psi); SetSingleLineStatus("Started ollama serve on :11434."); } catch { }
                }
                else if (Directory.Exists(exePath))
                {
                    Process.Start("explorer.exe", "\"" + exePath + "\"");
                }
            }
            else
            {
                if (string.Equals(procKey, "OmegaShell", StringComparison.OrdinalIgnoreCase))
                {
                    SetSingleLineStatus("OmegaShell is the current active window.");
                    return;
                }
                int pid;
                if (int.TryParse(pidCell, out pid) && pid > 4)
                {
                    try { using (Process p = Process.GetProcessById(pid)) p.Kill(); } catch { }
                }
                else
                {
                    Process[] ps = Process.GetProcessesByName(procKey);
                    for (int i = 0; i < ps.Length; i++) { try { ps[i].Kill(); } catch { } finally { ps[i].Dispose(); } }
                }
                SetSingleLineStatus("Stopped " + procKey + " (PID " + pidCell + ").");
            }
            RefreshLabMonitorCardsAsync();
        }

        private async void SaveAllShellSettingsFromUi()
        {
            _settings.FontSize = (float)_nudFontSet.Value;
            _settings.DefaultAiEndpoint = _txtAiEpSet.Text.Trim();
            _settings.DefaultAiModel = _txtAiModelSet.Text.Trim();

            _settings.ProtectedProcesses.Clear();
            string[] lines = (_txtWhitelistEditor.Text ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string w = lines[i].Trim();
                if (w.Length > 0) _settings.ProtectedProcesses.Add(w);
            }

            _settings.SaveToDisk(_configDir);
            File.WriteAllText(_oshrcPath, _txtOshrcEditor.Text ?? "", new UTF8Encoding(false));
            for (int s = 0; s < _sessions.Count; s++)
            {
                _sessions[s].Console.Font = new Font("Consolas", _settings.FontSize);
            }
            await RefreshLiveGemmaModelSelectorsAsync();
            SetSingleLineStatus("Saved OmegaShell v1.1.6 GM preferences, Gemma endpoint, .oshrc startup script, and Protected Whitelist!");
        }
    }
}
