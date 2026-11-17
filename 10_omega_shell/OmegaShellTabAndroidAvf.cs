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
        private TextBox _txtDebianUser, _txtDebianCmdInput, _txtAvfKeyAndScriptPreview;
        private RichTextBox _rtbDebianConsole;
        private DataGridView _gridAvfStatus, _gridAvfSharedFiles;
        private string _lastGuestOneLiner = "bash /mnt/shared/omega_debian_setup.sh || bash /mnt/shared/Download/omega_debian_setup.sh";

        private void BuildTab3AndroidAvfUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            Panel topBannerBar = new Panel();
            topBannerBar.Dock = DockStyle.Top;
            topBannerBar.Height = 26;
            topBannerBar.Padding = new Padding(10, 4, 10, 2);
            topBannerBar.BackColor = Color.FromArgb(11, 18, 33);

            _lblAvfBanner = MakeLbl(
                "ANDROID 17 AVF DEBIAN 13 BRIDGE (pKVM + crosvm) :: Click '3. 1-Click Setup' then '4. Auto-Type -> Pixel' for Zero-Mount SSH (:22222)!",
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

            Button bProbe = MakeBtn("1. Probe AVF", Color.FromArgb(2, 132, 199));
            bProbe.Click += async delegate { await RefreshAvfTabStateAsync(); };

            Button bKeygen = MakeBtn("2. Ed25519 Keygen", Color.FromArgb(109, 40, 217));
            bKeygen.Click += async delegate { await GenerateAvfSshKeyFromUiAsync(false); };

            Button bOneClick = MakeBtn("3. 1-Click Setup", Color.FromArgb(5, 150, 105));
            bOneClick.Click += async delegate { await RunOneClickDebianSetupFromUiAsync(); };

            Button bAutoType = MakeBtn("4. Auto-Type -> Pixel", Color.FromArgb(217, 119, 6));
            bAutoType.Click += async delegate { await RunAutoTypeDebianSshFromUiAsync(); };

            Button bVirtioDaemon = MakeBtn("5. Start @deb Virtio-GPU (:19789)", Color.FromArgb(4, 120, 87));
            bVirtioDaemon.Click += async delegate { await DeployAvfVirtioDaemonFromUiAsync(); };

            Button bDispUdot = MakeBtn("6. Dispatch 443G UDot (:19789)", Color.FromArgb(14, 116, 144));
            bDispUdot.Click += async delegate { await DispatchAvfUdotFromUiAsync(); };

            Button bNativeRust = MakeBtn("Bench @deb CLI", Color.FromArgb(6, 95, 70));
            bNativeRust.Click += async delegate { await RunNativeArm64RustBuildFromUiAsync(); };

            Button bCopyCmd = MakeBtn("Copy Zero-Mount Cmd", Color.FromArgb(180, 83, 9));
            bCopyCmd.Click += delegate
            {
                Clipboard.SetText(_lastGuestOneLiner);
                SetSingleLineStatus("Copied to clipboard: " + _lastGuestOneLiner);
            };

            Button bWake = MakeBtn("Wake + Keep-Alive", Color.FromArgb(88, 28, 135));
            bWake.Click += async delegate
            {
                string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.WakeAndroidLinuxTerminalWithKeepAlive(""); });
                AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(52, 211, 153));
                SetSingleLineStatus(msg);
                await RefreshAvfTabStateAsync();
            };

            Button bTunnel = MakeBtn("Arm SSH (:22222)", Color.FromArgb(30, 58, 138));
            bTunnel.Click += async delegate
            {
                string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.ArmAdbSshTunnels(""); });
                AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(125, 211, 252));
                SetSingleLineStatus("Armed ADB TCP port forwards (:22222 -> AVF :2222, :28022 -> Termux :8022).");
                await RefreshAvfTabStateAsync();
            };

            Button bSyncFolder = MakeBtn("Sync -> /mnt/shared", Color.FromArgb(15, 118, 110));
            bSyncFolder.Click += async delegate { await PickAndSyncToDebianSharedAsync(); };

            Button bGemmaDeb = MakeBtn("Gemma @deb Assist", Color.FromArgb(124, 58, 237));
            bGemmaDeb.Click += async delegate { await RunGemmaDebianAssistAsync(); };

            Label lUsr = MakeLbl("User:", null, Color.FromArgb(203, 213, 225));
            lUsr.Padding = new Padding(6, 5, 2, 0);

            _txtDebianUser = new TextBox();
            _txtDebianUser.Width = 78;
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
                bProbe, bKeygen, bOneClick, bAutoType, bVirtioDaemon, bDispUdot, bNativeRust, bCopyCmd, bWake, bTunnel, bSyncFolder, bGemmaDeb, lUsr, _txtDebianUser
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
                { "Toolchain Check", "which gcc clang rustc cargo python3 git sshd 2>&1 || true" },
                { "ls /mnt/shared", "ls -la /mnt/shared /mnt/shared/Download/linux 2>/dev/null || ls -la /sdcard/Download/linux" },
                { "Listening Ports", "ss -tulpn 2>/dev/null || netstat -tulpn 2>/dev/null" }
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

            Label lDebPrompt = MakeLbl("@deb [pixel-debian]$ ", new Font("Consolas", 9.4f, FontStyle.Bold), Color.FromArgb(52, 211, 153));
            lDebPrompt.Dock = DockStyle.Left;
            lDebPrompt.Padding = new Padding(0, 4, 4, 0);

            Button bDebSend = MakeBtn("Run on Debian", Color.FromArgb(5, 150, 105));
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
            debInBar.Controls.Add(lDebPrompt);
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
            Label lStHdr = MakeLbl("PIXEL AVF / pKVM HYPERVISOR & OPENSSH TUNNEL MATRIX:", new Font("Segoe UI", 8.2f, FontStyle.Bold), Color.FromArgb(56, 189, 248));
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
                "=== ANDROID LINUX / DEBIAN VIRTUALIZATION CONSOLE (@deb) ===\r\n" +
                "  Step 1: Click '3. 1-Click Setup' (grants storage + creates Ed25519 key + stages zero-mount Base64 cmd).\r\n" +
                "  Step 2: With Pixel Terminal open on your phone, click '4. Auto-Type -> Pixel' (types & starts sshd :2222 hands-free!).\r\n" +
                "  Step 3: Run any ARM64 Debian Linux command here, or prefix '@gemma <goal>' for AI assistance!\r\n",
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

            AppendColoredLine(_rtbDebianConsole, "@gemma-deb [" + _settings.DefaultAiModel + "]> " + goal, Color.FromArgb(192, 132, 252));
            SetSingleLineStatus("Gemma @deb Assistant running on Pixel Linux...");
            GemmaMissionResult res = await Task.Run(delegate
            {
                return OmegaShellGemmaCommander.ExecuteAutonomousMission(_runtime, "On Pixel Android AVF Debian (@deb): " + goal, null);
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
            AppendColoredLine(_rtbDebianConsole, "@deb [" + user + "@pixel]$ " + cmd, Color.FromArgb(52, 211, 153));
            string sr = _shellRoot;
            string outText = await Task.Run(delegate { return AndroidAvfDebianBridge.RunCommandInPixelDebian(sr, user, cmd, ""); });
            AppendColoredLine(_rtbDebianConsole, outText + "\r\n", Color.FromArgb(226, 232, 240));
            SetSingleLineStatus("Executed @deb command on Pixel Linux (" + cmd + ")");
        }

        private async Task RefreshAvfTabStateAsync()
        {
            string sr = _shellRoot;
            List<OshRow> statRows = await Task.Run(delegate { return AndroidAvfDebianBridge.BuildAvfStatusRows(sr); });
            List<OshRow> fileRows = await Task.Run(delegate { return AndroidAvfDebianBridge.ListDebianSharedFilesRows(""); });
            if (fileRows.Count == 0)
            {
                fileRows.Add(new OshRow()
                    .Set("name", "(empty)")
                    .Set("debian_path", "/mnt/shared/omega_workspace")
                    .Set("note", "Click '3. 1-Click Debian Setup' or 'Sync -> /mnt/shared' to stage files"));
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
                _txtAvfKeyAndScriptPreview.Text = "Click '2. Ed25519 Keygen' or '3. 1-Click Debian Setup' above to create your dedicated OpenSSH Ed25519 keypair and stage omega_debian_setup.sh onto your Pixel!";
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
            _lblAvfBanner.Text = "Deploying persistent systemd Virtio-GPU GFXStream Worker (:19789) inside AVF Debian 13...";
            SetSingleLineStatus("Deploying & bridging persistent AVF @deb Virtio-GPU worker on 127.0.0.1:19789...");
            AppendColoredLine(_rtbDebianConsole, "\r\n>>> [DEPLOYING PERSISTENT AVF @deb VIRTIO-GPU DAEMON (:19789)...]", Color.FromArgb(56, 189, 248));
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.DeployAndStartPersistentAvfVirtioDaemon(sr, usr, ""); });
            AppendColoredLine(_rtbDebianConsole, res + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = res.IndexOf("[HOST BRIDGE LIVE]", StringComparison.OrdinalIgnoreCase) >= 0
                ? "PERSISTENT AVF @deb VIRTIO-GPU DAEMON LIVE ON http://127.0.0.1:19789 (systemd enabled)!"
                : "AVF Virtio-GPU daemon step finished — inspect console below.";
            SetSingleLineStatus("AVF @deb Virtio-GPU (:19789) deployment complete.");
            await RefreshAvfTabStateAsync();
        }

        private async Task DispatchAvfUdotFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            SetSingleLineStatus("Dispatching live K10_HW_UDOT_PACK4U8 (OpUDot 4x8b) job to AVF Virtio-GPU (:19789)...");
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.DispatchAvfVirtioUdotJob(sr, usr, ""); });
            AppendColoredLine(_rtbDebianConsole, "\r\n" + res + "\r\n", Color.FromArgb(52, 211, 153));
            SetSingleLineStatus("Completed live AVF @deb Virtio-GPU (:19789) K10 dispatch.");
            await RefreshAvfTabStateAsync();
        }

        private async Task RunNativeArm64RustBuildFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            _lblAvfBanner.Text = "Streaming v0.7.0 Rust Source -> AVF Debian 13 (:22222) & Compiling Native ARM64 Binary on Tensor G5...";
            SetSingleLineStatus("Compiling & benchmarking native aarch64-unknown-linux-gnu Rust inside Pixel 10 AVF Debian 13...");
            AppendColoredLine(_rtbDebianConsole, "\r\n>>> [STARTING NATIVE ARM64 RUST BUILD OVER @deb 127.0.0.1:22222...]", Color.FromArgb(56, 189, 248));
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.RunNativeArm64RustBuildAndBenchInDebian(sr, usr, ""); });
            AppendColoredLine(_rtbDebianConsole, res + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = res.IndexOf("[AVF-BUILD PASS]", StringComparison.OrdinalIgnoreCase) >= 0
                ? "NATIVE ARM64 RUST COMPILED & VERIFIED LIVE INSIDE PIXEL 10 AVF DEBIAN 13 (:22222)!"
                : "AVF Native Rust run finished — inspect output below.";
            SetSingleLineStatus("AVF @deb native ARM64 Rust build & benchmark complete.");
            await RefreshAvfTabStateAsync();
        }

        private async Task RunAutoTypeDebianSshFromUiAsync()
        {
            string sr = _shellRoot;
            string usr = (_txtDebianUser != null ? _txtDebianUser.Text : _settings.DebianLinuxUser).Trim();
            _lblAvfBanner.Text = "Auto-Typing Zero-Mount OpenSSH (:2222) Bootstrap into Pixel 10 AVF Terminal & Verifying :22222...";
            SetSingleLineStatus("Typing zero-mount Base64 SSH setup into Pixel Terminal via ADB...");
            string res = await Task.Run(delegate { return AndroidAvfDebianBridge.AutoTypeZeroMountSshIntoPixelTerminal(sr, usr, ""); });
            AppendColoredLine(_rtbDebianConsole, res + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = res.IndexOf("LIVE", StringComparison.OrdinalIgnoreCase) >= 0
                ? "AVF DEBIAN 13 SSH LIVE ON 127.0.0.1:22222! Type any ARM64 Linux command below."
                : "Auto-Type sent! Check Pixel Terminal screen, then click '1. Probe AVF'.";
            SetSingleLineStatus("Auto-Type AVF SSH bootstrap complete.");
            await RefreshAvfTabStateAsync();
        }

        private async Task RunOneClickDebianSetupFromUiAsync()
        {
            string sr = _shellRoot;
            string script = "", oneLiner = "";
            _lblAvfBanner.Text = "Running 1-Click Ed25519 Keygen + Pushing omega_debian_setup.sh -> Pixel /mnt/shared + Arming TCP :22222...";
            string report = await Task.Run(delegate { return AndroidAvfDebianBridge.ExecuteOneClickDebianBootstrap(sr, "", out script, out oneLiner); });
            _lastGuestOneLiner = oneLiner;
            try { Clipboard.SetText(oneLiner); } catch { }
            AppendColoredLine(_rtbDebianConsole, report + "\r\n", Color.FromArgb(52, 211, 153));
            _lblAvfBanner.Text = "1-Click Bootstrap Ready! Paste/run in Pixel Terminal:  " + oneLiner;
            SetSingleLineStatus("Staged omega_debian_setup.sh -> /mnt/shared & copied '" + oneLiner + "' to clipboard!");
            await RefreshAvfTabStateAsync();
        }

        private async Task PickAndSyncToDebianSharedAsync()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.InitialDirectory = ActiveWorkingDir();
                ofd.Title = "Choose a source file to sync to Pixel /mnt/shared/omega_workspace (or Cancel to pick a folder)";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string picked = ofd.FileName;
                    string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.SyncLocalPathToDebianShared(picked, ""); });
                    AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(52, 211, 153));
                    await RefreshAvfTabStateAsync();
                    return;
                }
            }
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = ActiveWorkingDir();
                fbd.Description = "Choose a folder to sync to Pixel /mnt/shared/omega_workspace:";
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    string dir = fbd.SelectedPath;
                    string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.SyncLocalPathToDebianShared(dir, ""); });
                    AppendColoredLine(_rtbDebianConsole, msg, Color.FromArgb(52, 211, 153));
                    await RefreshAvfTabStateAsync();
                }
            }
        }
    }
}
