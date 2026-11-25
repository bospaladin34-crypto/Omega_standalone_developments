using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaShellStudio
{
    public sealed partial class OmegaShellForm
    {
        private Label _lblAvfBanner;
        private Label _lblDebPrompt;
        private ComboBox _cmbAvfTarget;
        private TextBox _txtDebianUser, _txtDebianCmdInput, _txtAvfKeyAndScriptPreview;
        private RichTextBox _rtbDebianConsole;
        private DataGridView _gridAvfStatus, _gridAvfSharedFiles;
        private string _lastGuestOneLiner = "bash /mnt/shared/omega_debian_setup.sh || bash /mnt/shared/Download/omega_debian_setup.sh";

        private string SelectedAvfTargetSpec()
        {
            if (_cmbAvfTarget == null) return "p10";
            int idx = _cmbAvfTarget.SelectedIndex;
            if (idx == 1) return "p8";
            if (idx == 2) return "both";
            return "p10";
        }

        private void BuildTab3AndroidAvfUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            Panel topBannerBar = new Panel();
            topBannerBar.Dock = DockStyle.Top;
            topBannerBar.Height = 26;
            topBannerBar.Padding = new Padding(10, 4, 10, 2);
            topBannerBar.BackColor = Color.FromArgb(11, 18, 33);

            _lblAvfBanner = MakeLbl(
                "DUAL-PIXEL AVF DEBIAN 13 BRIDGE :: P10 (@deb10 :22222 / :19789 Virtio-GPU) + P8 (@deb8 :22232 / :19793 5.3GB 9c SMMLA)!",
                new Font("Consolas", 8.8f, FontStyle.Bold),
                Color.FromArgb(52, 211, 153));
            _lblAvfBanner.AutoSize = false;
            _lblAvfBanner.Dock = DockStyle.Fill;
            topBannerBar.Controls.Add(_lblAvfBanner);

            FlowLayoutPanel actBar = new FlowLayoutPanel();
            actBar.Dock = DockStyle.Top;
            actBar.AutoSize = true;
            actBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actBar.MinimumSize = new Size(0, 38);
            actBar.WrapContents = true;
            actBar.Padding = new Padding(8, 5, 8, 5);
            actBar.BackColor = Color.FromArgb(14, 21, 38);

            Label lTgt = MakeLbl("Node:", null, Color.FromArgb(56, 189, 248));
            lTgt.Padding = new Padding(2, 5, 2, 0);

            _cmbAvfTarget = new ComboBox();
            _cmbAvfTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbAvfTarget.Width = 204;
            _cmbAvfTarget.BackColor = Color.FromArgb(6, 9, 17);
            _cmbAvfTarget.ForeColor = Color.FromArgb(52, 211, 153);
            _cmbAvfTarget.Font = new Font("Consolas", 8.8f, FontStyle.Bold);
            _cmbAvfTarget.Items.AddRange(new object[]
            {
                "Pixel 10 (:22222 / :19789)",
                "Pixel 8 (:22232 / :19793)",
                "Dual P10 + P8 (Broadcast)"
            });
            _cmbAvfTarget.SelectedIndex = 0;
            _cmbAvfTarget.SelectedIndexChanged += async delegate
            {
                if (_lblDebPrompt != null)
                {
                    int ix = _cmbAvfTarget.SelectedIndex;
                    _lblDebPrompt.Text = ix == 1 ? "@deb8 [:22232]$ " : (ix == 2 ? "@deb-all [:22222+:22232]$ " : "@deb10 [:22222]$ ");
                }
                await RefreshAvfTabStateAsync();
            };

            Button bProbe = MakeBtn("1. Probe Dual AVF", Color.FromArgb(2, 132, 199));
            bProbe.Click += async delegate { await RefreshAvfTabStateAsync(); };

            Button bKeygen = MakeBtn("2. Ed25519 Key", Color.FromArgb(109, 40, 217));
            bKeygen.Click += async delegate { await GenerateAvfSshKeyFromUiAsync(false); };

            Button bOneClick = MakeBtn("3. 1-Click Setup", Color.FromArgb(5, 150, 105));
            bOneClick.Click += async delegate { await RunOneClickDebianSetupFromUiAsync(); };

            Button bAutoType = MakeBtn("4. Auto-Type SSH", Color.FromArgb(217, 119, 6));
            bAutoType.Click += async delegate { await RunAutoTypeDebianSshFromUiAsync(); };

            Button bVirtioDaemon = MakeBtn("5. Start @deb Daemon (:19789/:19793)", Color.FromArgb(4, 120, 87));
            bVirtioDaemon.Click += async delegate { await DeployAvfVirtioDaemonFromUiAsync(); };

            Button bDispUdot = MakeBtn("6. Dispatch @deb Job", Color.FromArgb(14, 116, 144));
            bDispUdot.Click += async delegate { await DispatchAvfUdotFromUiAsync(); };

            Button bNativeRust = MakeBtn("Bench @deb CLI", Color.FromArgb(6, 95, 70));
            bNativeRust.Click += async delegate { await RunNativeArm64RustBuildFromUiAsync(); };

            Button bCopyCmd = MakeBtn("Copy Zero-Mount", Color.FromArgb(180, 83, 9));
            bCopyCmd.Click += delegate
            {
                Clipboard.SetText(_lastGuestOneLiner);
                SetSingleLineStatus("Copied to clipboard: " + _lastGuestOneLiner);
            };

            Button bWake = MakeBtn("Wake + Keep-Alive", Color.FromArgb(88, 28, 135));
            bWake.Click += async delegate
            {
                string tgt = SelectedAvfTargetSpec();
                string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.WakeAndroidLinuxTerminalWithKeepAlive(tgt); });
                AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(52, 211, 153));
                SetSingleLineStatus(msg);
                await RefreshAvfTabStateAsync();
            };

            Button bTunnel = MakeBtn("Arm Dual SSH (:22222/:22232)", Color.FromArgb(30, 58, 138));
            bTunnel.Click += async delegate
            {
                string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.ArmAdbSshTunnels("both"); });
                AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(125, 211, 252));
                SetSingleLineStatus("Pinned P10 -> :22222/:19788/:19789 & P8 -> :22232/:19792/:19793.");
                await RefreshAvfTabStateAsync();
            };

            Button bSyncFolder = MakeBtn("Sync -> /mnt/shared", Color.FromArgb(15, 118, 110));
            bSyncFolder.Click += async delegate { await PickAndSyncToDebianSharedAsync(); };

            Button bGemmaDeb = MakeBtn("Gemma @deb Assist", Color.FromArgb(124, 58, 237));
            bGemmaDeb.Click += async delegate { await RunGemmaDebianAssistAsync(); };

            Label lUsr = MakeLbl("User:", null, Color.FromArgb(203, 213, 225));
            lUsr.Padding = new Padding(6, 5, 2, 0);

            _txtDebianUser = new TextBox();
            _txtDebianUser.Width = 68;
            _txtDebianUser.Text = _settings.DebianLinuxUser;
            _txtDebianUser.BackColor = Color.FromArgb(6, 9, 17);
            _txtDebianUser.ForeColor = Color.White;
            _txtDebianUser.TextChanged += delegate
            {
                _settings.DebianLinuxUser = _txtDebianUser.Text.Trim();
                _runtime.Variables["debian_user"] = _settings.DebianLinuxUser;
            };

            actBar.Controls.AddRange(new Control[]
            {
                lTgt, _cmbAvfTarget, bProbe, bKeygen, bOneClick, bAutoType, bVirtioDaemon, bDispUdot, bNativeRust, bCopyCmd, bWake, bTunnel, bSyncFolder, bGemmaDeb, lUsr, _txtDebianUser
            });

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            Panel leftTerm = new Panel();
            leftTerm.Dock = DockStyle.Fill;
            leftTerm.BackColor = Color.FromArgb(6, 9, 17);

            FlowLayoutPanel quickDeb = new FlowLayoutPanel();
            quickDeb.Dock = DockStyle.Top;
            quickDeb.Height = 34;
            quickDeb.Padding = new Padding(8, 4, 6, 2);
            quickDeb.WrapContents = false;
            quickDeb.BackColor = Color.FromArgb(14, 21, 38);

            string[,] debCmds = new string[,]
            {
                { "uname & CPU", "uname -a && lscpu | head -n 14" },
                { "RAM & Disk", "free -m && df -h" },
                { "Toolchains", "which gcc clang rustc cargo python3 git sshd 2>&1 || true" },
                { "ls /mnt/shared", "ls -la /mnt/shared /mnt/shared/Download/linux 2>/dev/null || ls -la /sdcard/Download/linux" },
                { "Worker Ports", "ss -tulpn 2>/dev/null | grep -E '2222|19789|19793' || ss -tln" }
            };
            for (int i = 0; i < debCmds.GetLength(0); i++)
            {
                string lbl = debCmds[i, 0];
                string dc = debCmds[i, 1];
                Button b = MakeBtn(lbl, Color.FromArgb(30, 58, 138));
                b.Click += async delegate { await ExecuteInteractiveDebianCmdAsync(dc); };
                quickDeb.Controls.Add(b);
            }

            Panel debInBar = new Panel();
            debInBar.Dock = DockStyle.Bottom;
            debInBar.Height = 38;
            debInBar.Padding = new Padding(8, 5, 8, 5);
            debInBar.BackColor = Color.FromArgb(12, 19, 34);

            _lblDebPrompt = MakeLbl("@deb10 [:22222]$ ", new Font("Consolas", 9.4f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            _lblDebPrompt.Dock = DockStyle.Left;
            _lblDebPrompt.Padding = new Padding(0, 4, 4, 0);

            Button bDebSend = MakeBtn("Run on @deb", Color.FromArgb(5, 150, 105));
            bDebSend.Dock = DockStyle.Right;
            bDebSend.Click += async delegate { await SubmitDebianInputBoxAsync(); };

            _txtDebianCmdInput = new TextBox();
            _txtDebianCmdInput.Dock = DockStyle.Fill;
            _txtDebianCmdInput.BackColor = Color.FromArgb(6, 9, 17);
            _txtDebianCmdInput.ForeColor = Color.White;
            _txtDebianCmdInput.Font = new Font("Consolas", 9.8f);
            _txtDebianCmdInput.BorderStyle = BorderStyle.FixedSingle;
            _txtDebianCmdInput.KeyDown += async delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    await SubmitDebianInputBoxAsync();
                }
            };

            debInBar.Controls.Add(_txtDebianCmdInput);
            debInBar.Controls.Add(_lblDebPrompt);
            debInBar.Controls.Add(bDebSend);

            _rtbDebianConsole = new RichTextBox();
            _rtbDebianConsole.Dock = DockStyle.Fill;
            _rtbDebianConsole.ReadOnly = true;
            _rtbDebianConsole.BorderStyle = BorderStyle.None;
            _rtbDebianConsole.WordWrap = false;
            _rtbDebianConsole.ScrollBars = RichTextBoxScrollBars.Both;
            _rtbDebianConsole.BackColor = Color.FromArgb(6, 9, 17);
            _rtbDebianConsole.ForeColor = Color.FromArgb(226, 232, 240);
            _rtbDebianConsole.Font = new Font("Consolas", 9.4f);

            Panel paddedConsole = WrapPaddedConsoleHost(_rtbDebianConsole, 8, 6);

            leftTerm.Controls.Add(paddedConsole);
            leftTerm.Controls.Add(quickDeb);
            leftTerm.Controls.Add(debInBar);

            SplitContainer rightSplit = new SplitContainer();
            rightSplit.Dock = DockStyle.Fill;
            rightSplit.Orientation = Orientation.Horizontal;
            rightSplit.BackColor = Color.FromArgb(30, 41, 59);
            rightSplit.SplitterWidth = 5;

            SplitContainer topGridsSplit = new SplitContainer();
            topGridsSplit.Dock = DockStyle.Fill;
            topGridsSplit.Orientation = Orientation.Horizontal;
            topGridsSplit.BackColor = Color.FromArgb(30, 41, 59);
            topGridsSplit.SplitterWidth = 5;

            Panel pStat = new Panel(); pStat.Dock = DockStyle.Fill;
            Label lStHdr = MakeLbl("DUAL-PIXEL AVF / pKVM HYPERVISOR & PINNED PORT MATRIX (:22222 P10 | :22232 P8):", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(56, 189, 248));
            lStHdr.AutoSize = false; lStHdr.Dock = DockStyle.Top; lStHdr.Height = 22; lStHdr.Padding = new Padding(6, 3, 0, 0); lStHdr.BackColor = Color.FromArgb(14, 21, 38);
            _gridAvfStatus = MakeStyledGrid();
            AttachPipeContextMenu(_gridAvfStatus, delegate { return "avf status"; });
            pStat.Controls.Add(_gridAvfStatus);
            pStat.Controls.Add(lStHdr);

            Panel pShared = new Panel(); pShared.Dock = DockStyle.Fill;
            Label lShHdr = MakeLbl("SHARED MOUNT INSPECTOR (/sdcard/Download/linux <-> /mnt/shared):", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            lShHdr.AutoSize = false; lShHdr.Dock = DockStyle.Top; lShHdr.Height = 22; lShHdr.Padding = new Padding(6, 3, 0, 0); lShHdr.BackColor = Color.FromArgb(14, 21, 38);
            _gridAvfSharedFiles = MakeStyledGrid();
            AttachPipeContextMenu(_gridAvfSharedFiles, delegate { return "avf ls"; });
            pShared.Controls.Add(_gridAvfSharedFiles);
            pShared.Controls.Add(lShHdr);

            topGridsSplit.Panel1.Controls.Add(pStat);
            topGridsSplit.Panel2.Controls.Add(pShared);

            Panel pScript = new Panel(); pScript.Dock = DockStyle.Fill;
            Label lScHdr = MakeLbl("GENERATED ED25519 PUBLIC KEY & 1-CLICK DEBIAN SETUP SCRIPT (omega_debian_setup.sh):", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(250, 204, 21));
            lScHdr.AutoSize = false; lScHdr.Dock = DockStyle.Top; lScHdr.Height = 22; lScHdr.Padding = new Padding(6, 3, 0, 0);
            lScHdr.BackColor = Color.FromArgb(14, 21, 38);

            _txtAvfKeyAndScriptPreview = new TextBox();
            _txtAvfKeyAndScriptPreview.Dock = DockStyle.Fill;
            _txtAvfKeyAndScriptPreview.Multiline = true;
            _txtAvfKeyAndScriptPreview.ReadOnly = true;
            _txtAvfKeyAndScriptPreview.ScrollBars = ScrollBars.Vertical;
            _txtAvfKeyAndScriptPreview.BackColor = Color.FromArgb(7, 11, 19);
            _txtAvfKeyAndScriptPreview.ForeColor = Color.FromArgb(203, 213, 225);
            _txtAvfKeyAndScriptPreview.Font = new Font("Consolas", 8.6f);
            _txtAvfKeyAndScriptPreview.BorderStyle = BorderStyle.None;

            Panel scPad = new Panel();
            scPad.Dock = DockStyle.Fill;
            scPad.Padding = new Padding(6, 4, 2, 4);
            scPad.BackColor = Color.FromArgb(7, 11, 19);
            scPad.Controls.Add(_txtAvfKeyAndScriptPreview);

            pScript.Controls.Add(scPad);
            pScript.Controls.Add(lScHdr);

            rightSplit.Panel1.Controls.Add(topGridsSplit);
            rightSplit.Panel2.Controls.Add(pScript);

            split.Panel1.Controls.Add(leftTerm);
            split.Panel2.Controls.Add(rightSplit);

            page.Controls.Add(split);
            page.Controls.Add(actBar);
            page.Controls.Add(topBannerBar);

            AppendColoredLine(_rtbDebianConsole,
                "=== DUAL-PIXEL ANDROID 17 AVF DEBIAN 13 CONSOLE (@deb10 + @deb8) ===\r\n" +
                "  * Pixel 10 (frankel / 58170..): SSH 127.0.0.1:22222 | Native :19788 (TPU+PowerVR) | 5.0GB AVF :19789 (Virtio-GPU)\r\n" +
                "  * Pixel 8  (shiba   / 3A121..): SSH 127.0.0.1:22232 | Native :19792 (Mali-G715+SMMLA) | 5.3GB AVF :19793 (9c SMMLA)\r\n" +
                "  * Choose 'Pixel 10', 'Pixel 8', or 'Dual P10 + P8' in the Node selector above, or run @deb10 / @deb8 / @deb-all in Tab 1!\r\n",
                Color.FromArgb(125, 211, 252));

            _mainTabs.SelectedIndexChanged += async delegate
            {
                if (_mainTabs.SelectedIndex == 2) await RefreshAvfTabStateAsync();
            };

            ApplySplitterOnTabVisible(page, delegate
            {
                split.SplitterDistance = Math.Max(640, (split.Width * 50) / 100);
                rightSplit.SplitterDistance = Math.Max(320, (rightSplit.Height * 54) / 100);
                topGridsSplit.SplitterDistance = Math.Max(140, (topGridsSplit.Height * 48) / 100);
            });
        }

        private async Task RunGemmaDebianAssistAsync()
        {
            string goal = (_txtDebianCmdInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(goal))
                goal = "Check the ARM64 kernel, memory, and installed C/Rust toolchains on my Pixel Debian environment";
            else
                _txtDebianCmdInput.Clear();

            string tgt = SelectedAvfTargetSpec();
            AppendColoredLine(_rtbDebianConsole, "@gemma-deb [" + tgt + " :: " + _settings.DefaultAiModel + "]> " + goal, Color.FromArgb(192, 132, 252));
            SetSingleLineStatus("Gemma @deb Assistant running on " + tgt + "...");
            GemmaMissionResult res = await Task.Run(delegate
            {
                return OmegaShellGemmaCommander.ExecuteAutonomousMission(_runtime, "On Pixel Android AVF Debian (" + tgt + "): " + goal, null);
            });
            AppendColoredLine(_rtbDebianConsole, res.FinalNarrative + "\r\n", Color.FromArgb(226, 232, 240));
            SetSingleLineStatus("Gemma @deb Assist completed in " + res.TotalMs + " ms.");
            await RefreshAvfTabStateAsync();
        }

        private async Task SubmitDebianInputBoxAsync()
        {
            string cmd = (_txtDebianCmdInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            _txtDebianCmdInput.Clear();
            if (cmd.StartsWith("@gemma ", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("gemma ", StringComparison.OrdinalIgnoreCase))
            {
                _txtDebianCmdInput.Text = cmd.Substring(cmd.IndexOf(' ') + 1);
                await RunGemmaDebianAssistAsync();
                return;
            }
            await ExecuteInteractiveDebianCmdAsync(cmd);
        }

        private async Task ExecuteInteractiveDebianCmdAsync(string cmd)
        {
            string user = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            string tgt = SelectedAvfTargetSpec();
            AppendColoredLine(_rtbDebianConsole, "@deb [" + user + "@" + tgt + "]$ " + cmd, Color.FromArgb(52, 211, 153));
            string sr = _shellRoot;
            string outText = await Task.Run(delegate { return AndroidAvfDebianBridge.RunCommandInPixelDebian(sr, user, cmd, tgt); });
            AppendColoredLine(_rtbDebianConsole, outText + "\r\n", Color.FromArgb(226, 232, 240));
            SetSingleLineStatus("Executed @deb (" + tgt + ") command: " + cmd);
        }

        private async Task RefreshAvfTabStateAsync()
        {
            string sr = _shellRoot;
            string tgt = SelectedAvfTargetSpec();
            List<OshRow> statRows = await Task.Run(delegate { return AndroidAvfDebianBridge.BuildAvfStatusRows(sr); });
            List<OshRow> fileRows = await Task.Run(delegate { return AndroidAvfDebianBridge.ListDebianSharedFilesRows(tgt); });
            if (fileRows.Count == 0)
            {
                fileRows.Add(new OshRow()
                    .Set("device", tgt)
                    .Set("name", "(empty)")
                    .Set("debian_path", "/mnt/shared/omega_workspace")
                    .Set("note", "Click '3. 1-Click Setup' or 'Sync -> /mnt/shared' to stage files"));
            }
            PopulateGridWithOshRows(_gridAvfStatus, statRows);
            PopulateGridWithOshRows(_gridAvfSharedFiles, fileRows);

            string pubPath = AndroidAvfDebianBridge.GetPublicKeyPath(_shellRoot);
            if (File.Exists(pubPath))
            {
                string pub = File.ReadAllText(pubPath, Encoding.UTF8).Trim();
                _txtAvfKeyAndScriptPreview.Text = "=== HOST ED25519 PUBLIC KEY ===\r\n" + pub + "\r\n\r\n=== GENERATED GUEST SCRIPT (omega_debian_setup.sh) ===\r\n" +
                    AndroidAvfDebianBridge.BuildDebianOneClickSetupScript(pub, AndroidAvfDebianBridge.DEFAULT_GUEST_AVF_SSH_PORT).Replace("\n", "\r\n");
            }
            else
            {
                _txtAvfKeyAndScriptPreview.Text = "Click '2. Ed25519 Key' or '3. 1-Click Setup' above to create your dedicated OpenSSH Ed25519 keypair and stage omega_debian_setup.sh onto Pixel 10 & Pixel 8!";
            }
        }

        private async Task GenerateAvfSshKeyFromUiAsync(bool force)
        {
            string sr = _shellRoot;
            string pub = "", priv = "";
            string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.EnsureOrGenerateEd25519KeyPair(sr, force, out pub, out priv); });
            AppendColoredLine(_rtbDebianConsole, msg + "\r\n" + pub, Color.FromArgb(192, 132, 252));
            SetSingleLineStatus(msg);
            await RefreshAvfTabStateAsync();
        }

        private async Task DeployAvfVirtioDaemonFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            string tgt = SelectedAvfTargetSpec();
            _lblAvfBanner.Text = "Deploying persistent systemd AVF Worker (" + tgt + " -> :19789 / :19793) inside Debian 13...";
            SetSingleLineStatus("Deploying & bridging persistent AVF @deb worker (" + tgt + ")...");
            AppendColoredLine(_rtbDebianConsole, "\r\n>>> [DEPLOYING PERSISTENT AVF @deb DAEMON (" + tgt + ")...]", Color.FromArgb(56, 189, 248));
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.DeployAndStartPersistentAvfVirtioDaemon(sr, usr, tgt); });
            AppendColoredLine(_rtbDebianConsole, res + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = res.IndexOf("[HOST BRIDGE LIVE]", StringComparison.OrdinalIgnoreCase) >= 0
                ? ("PERSISTENT AVF @deb DAEMON LIVE (" + tgt.ToUpperInvariant() + " -> :19789 / :19793)!")
                : "AVF daemon deployment finished -- inspect console below.";
            SetSingleLineStatus("AVF @deb (" + tgt + ") deployment complete.");
            await RefreshAvfTabStateAsync();
        }

        private async Task DispatchAvfUdotFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            string tgt = SelectedAvfTargetSpec();
            SetSingleLineStatus("Dispatching live compute job to AVF @deb (" + tgt + ")...");
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.DispatchAvfVirtioUdotJob(sr, usr, tgt); });
            AppendColoredLine(_rtbDebianConsole, "\r\n" + res + "\r\n", Color.FromArgb(52, 211, 153));
            SetSingleLineStatus("Completed live AVF @deb (" + tgt + ") dispatch.");
            await RefreshAvfTabStateAsync();
        }

        private async Task RunNativeArm64RustBuildFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            string tgt = SelectedAvfTargetSpec();
            _lblAvfBanner.Text = "Compiling & benchmarking native ARM64 worker inside AVF Debian 13 (" + tgt + ")...";
            SetSingleLineStatus("Compiling & benchmarking native aarch64 worker on " + tgt + "...");
            AppendColoredLine(_rtbDebianConsole, "\r\n>>> [STARTING NATIVE ARM64 BUILD & BENCH OVER @deb (" + tgt + ")...]", Color.FromArgb(56, 189, 248));
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.RunNativeArm64RustBuildAndBenchInDebian(sr, usr, tgt); });
            AppendColoredLine(_rtbDebianConsole, res + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = res.IndexOf("[AVF-BUILD", StringComparison.OrdinalIgnoreCase) >= 0
                ? ("NATIVE ARM64 WORKER VERIFIED LIVE INSIDE AVF DEBIAN 13 (" + tgt.ToUpperInvariant() + ")!")
                : "AVF Native build run finished -- inspect output below.";
            SetSingleLineStatus("AVF @deb (" + tgt + ") native build & benchmark complete.");
            await RefreshAvfTabStateAsync();
        }

        private async Task RunAutoTypeDebianSshFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            string tgt = SelectedAvfTargetSpec();
            _lblAvfBanner.Text = "Auto-Typing Zero-Mount OpenSSH (:2222) Bootstrap into " + tgt + " AVF Terminal...";
            SetSingleLineStatus("Typing zero-mount SSH setup into " + tgt + " Terminal via ADB...");
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.AutoTypeZeroMountSshIntoPixelTerminal(sr, usr, tgt); });
            AppendColoredLine(_rtbDebianConsole, res + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = res.IndexOf("LIVE", StringComparison.OrdinalIgnoreCase) >= 0
                ? ("AVF DEBIAN 13 SSH LIVE (" + tgt.ToUpperInvariant() + ")! Type any ARM64 Linux command below.")
                : "Auto-Type sent! Check Pixel Terminal screen, then click '1. Probe Dual AVF'.";
            SetSingleLineStatus("Auto-Type AVF SSH bootstrap complete (" + tgt + ").");
            await RefreshAvfTabStateAsync();
        }

        private async Task RunOneClickDebianSetupFromUiAsync()
        {
            string sr = _shellRoot;
            string tgt = SelectedAvfTargetSpec();
            string script = "", oneLiner = "";
            _lblAvfBanner.Text = "Running 1-Click Ed25519 Keygen + Staging omega_debian_setup.sh -> " + tgt + "...";
            string report = await Task.Run(delegate { return AndroidAvfDebianBridge.ExecuteOneClickDebianBootstrap(sr, tgt, out script, out oneLiner); });
            _lastGuestOneLiner = oneLiner;
            try { Clipboard.SetText(oneLiner); } catch { }
            AppendColoredLine(_rtbDebianConsole, report + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = "1-Click Bootstrap Staged (" + tgt + ")! Click '4. Auto-Type SSH' or '5. Start @deb Daemon'.";
            SetSingleLineStatus("Staged omega_debian_setup.sh -> " + tgt + " & copied zero-mount command to clipboard!");
            await RefreshAvfTabStateAsync();
        }

        private async Task PickAndSyncToDebianSharedAsync()
        {
            string tgt = SelectedAvfTargetSpec();
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.InitialDirectory = ActiveWorkingDir();
                ofd.Title = "Choose a source file to sync to " + tgt + " /mnt/shared/omega_workspace (or Cancel to pick a folder)";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string picked = ofd.FileName;
                    string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.SyncLocalPathToDebianShared(picked, tgt); });
                    AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(52, 211, 153));
                    await RefreshAvfTabStateAsync();
                    return;
                }
            }
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = ActiveWorkingDir();
                fbd.Description = "Choose a folder to sync to " + tgt + " /mnt/shared/omega_workspace:";
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    string dir = fbd.SelectedPath;
                    string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.SyncLocalPathToDebianShared(dir, tgt); });
                    AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(52, 211, 153));
                    await RefreshAvfTabStateAsync();
                }
            }
        }
    }
}