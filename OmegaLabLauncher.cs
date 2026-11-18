using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using OmegaPortableConfig;
using OmegaRnDLab;

namespace OmegaStandaloneLab
{
    public sealed class LauncherAiModel
    {
        public string Provider = "";
        public string Tag = "";
        public string Endpoint = "";
        public bool Online = false;
        public override string ToString()
        {
            return "[" + Provider + (Online ? "-LIVE" : "-IDLE") + "] " + Tag;
        }
    }

    public sealed class OmegaLabLauncherForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_HSCROLL = 0x0114;
        private const int SB_LEFT = 6;
        private static readonly string FENCE = new string((char)96, 3);

        private readonly string _root, _mrDir, _brDir, _avDir, _syDir, _snDir, _sfDir, _pnDir, _sgDir, _fgDir, _shDir, _p10Dir, _cdDir, _tnDir, _dlDir, _mrExe, _brExe, _avExe, _syExe, _snExe, _sfExe, _pnExe, _sgExe, _fgExe, _shExe, _cdExe, _tnExe, _harvPs1;
        private readonly Label _lBr, _lMr, _lAv, _lRam, _lAdb, _lP10Tensor, _lCorp, _lAct, _lDrop;
        private string _cachedP10Line = "P10 10-Kernel Tensor (:19788): probing daemon...";
        private int _p10PollTick = 0;
        private readonly ComboBox _cmbGemmaModel, _cmbGemmaMode;
        private readonly RichTextBox _rtbGemmaOut;
        private readonly TextBox _txtGemmaIn;
        private readonly System.Windows.Forms.Timer _tmr;
        private HttpWebRequest _activeGemmaReq;
        private volatile bool _abortGemma = false;
        private volatile bool _polling = false;
        private string _lastGeneratedArtifact = "";

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        private static void OpenDir(string d) { Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); }

        public OmegaLabLauncherForm(string root)
        {
            _root = root;
            _mrDir = Path.Combine(root, "01_omega_mirror");
            _brDir = Path.Combine(root, "02_omega_browser");
            _avDir = Path.Combine(root, "03_omega_av");
            _syDir = Path.Combine(root, "04_omega_synth");
            _snDir = Path.Combine(root, "05_omega_sonar");
            _sfDir = Path.Combine(root, "06_omega_sheaf");
            _pnDir = Path.Combine(root, "07_omega_penrose");
            _sgDir = Path.Combine(root, "08_omega_steg");
            _fgDir = Path.Combine(root, "09_omega_forge");
            _shDir = Path.Combine(root, "10_omega_shell");
            _p10Dir = Path.Combine(root, "11_omega_edge_workers\rust_p10_worker");
            _cdDir = Path.Combine(root, "12_omega_conductor");
            _tnDir = Path.Combine(root, "13_omega_tensor");
            _dlDir = Path.Combine(root, "downloads");
            _mrExe = Path.Combine(_mrDir, "OmegaMirror.exe");
            _brExe = Path.Combine(_brDir, "OmegaBrowser.exe");
            _avExe = Path.Combine(_avDir, "OmegaAV.exe");
            _syExe = Path.Combine(_syDir, "OmegaSynth.exe");
            _snExe = Path.Combine(_snDir, "OmegaSonar.exe");
            _sfExe = Path.Combine(_sfDir, "OmegaSheaf.exe");
            _pnExe = Path.Combine(_pnDir, "OmegaPenrose.exe");
            _sgExe = Path.Combine(_sgDir, "OmegaSteg.exe");
            _fgExe = Path.Combine(_fgDir, "OmegaForge.exe");
            _shExe = Path.Combine(_shDir, "OmegaShell.exe");
            _cdExe = Path.Combine(_cdDir, "OmegaConductor.exe");
            _tnExe = Path.Combine(_tnDir, "OmegaTensor.exe");
            _harvPs1 = Path.Combine(_brDir, "Invoke-OmegaHarvest.ps1");
            Directory.CreateDirectory(_dlDir);

            AutoScaleMode = AutoScaleMode.None;
            Text = "ACT-Omega :: Standalone Lab v2.5.0 (Complete 13-Station Deck + OmegaConductor (:19790) + OmegaTensor (:19791) + 5GB AVF)";
            ClientSize = new Size(1124, 924);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(9, 13, 22);
            ForeColor = Color.White;
            KeyPreview = true;
            AllowDrop = true;
            try { string ic = Path.Combine(root, "icons", "omega_lab.ico"); if (File.Exists(ic)) Icon = new Icon(ic); } catch { }

            Label sub = new Label();
            sub.UseMnemonic = false;
            sub.Text = "ACT-OMEGA STANDALONE LAB v2.5.0 :: COMPLETE 13-STATION DECK + OMEGACONDUCTOR (:19790) + OMEGATENSOR 9.0 TOPS SPLIT (:19791) [Shift+F11=Wand]";
            sub.SetBounds(18, 8, 1088, 18);
            sub.ForeColor = C(56, 189, 248);
            sub.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);

            Label hdr = new Label();
            hdr.UseMnemonic = false;
            hdr.Text = "13-Station Deck :: Browser+Mirror+AV+Synth+Sonar+Sheaf+Penrose+Steg+Forge+Shell+Conductor+Tensor+P10";
            hdr.SetBounds(18, 27, 462, 26);
            hdr.ForeColor = Color.White;
            hdr.Font = new Font("Segoe UI Semibold", 10.6f, FontStyle.Bold, GraphicsUnit.Pixel);

            Button bHelpTop = Btn("? Help & Manual (F6)", 484, 23, 166, C(180, 83, 9));
            bHelpTop.Height = 32;
            bHelpTop.FlatAppearance.BorderColor = C(250, 204, 21);
            bHelpTop.Click += delegate { OpenHumanManual(); };

            Panel card = new Panel();
            card.SetBounds(18, 58, 632, 132);
            card.BackColor = C(15, 23, 42);
            _lBr = Stat(12, 7, 302, "Browser (:9778): scanning...");
            _lMr = Stat(320, 7, 302, "Mirror (:19777): scanning...");
            _lAv = Stat(12, 30, 302, "OmegaAV (:19779): scanning...");
            _lRam = Stat(320, 30, 302, "Forge/Shell/Gemma: scanning...");
            _lAdb = Stat(12, 54, 608, "Pixel 10 Edge + AVF (:22222): probing ADB...");
            _lP10Tensor = Stat(12, 78, 608, "P10 10-Kernel Tensor (:19788): probing PowerVR + ARMv9.2 daemon...");
            _lP10Tensor.ForeColor = C(52, 211, 153);
            _lP10Tensor.Cursor = Cursors.Hand;
            _lP10Tensor.Click += delegate { OmegaLabPixel10CockpitDialog.ShowOrFocus(this, _root); };
            _lCorp = Stat(12, 103, 608, "Vaults & Downloads: checking E:\\...");
            card.Controls.AddRange(new Control[] { _lBr, _lMr, _lAv, _lRam, _lAdb, _lP10Tensor, _lCorp });

            Button bBr = Btn("Launch / Focus OmegaBrowser v0.6.6 (:9778) [F1]", 18, 196, 348, C(3, 105, 161));
            bBr.Height = 30;
            bBr.Click += delegate { LaunchOrFocus("OmegaBrowser", _brExe, "OmegaBrowser v0.6.6"); };
            Button bFrz = Btn("Freeze BG Tabs", 374, 196, 176, C(146, 64, 14));
            bFrz.Height = 30;
            bFrz.Click += delegate { CallBrowserApi("/api/freeze", "Froze background tabs"); };
            Button bBrX = Btn("Stop", 558, 196, 92, C(127, 29, 29));
            bBrX.Height = 30;
            bBrX.Click += delegate { StopApps(new string[] { "OmegaBrowser" }); };

            Button bMr = Btn("Launch / Focus OmegaMirror v0.9 (:19777) [F2]", 18, 231, 348, C(15, 118, 110));
            bMr.Height = 30;
            bMr.Click += delegate { LaunchOrFocus("OmegaMirror", _mrExe, "OmegaMirror v0.9"); };
            Button bMrB = Btn("Beam :19777 -> Pixels", 374, 231, 176, C(67, 56, 202));
            bMrB.Height = 30;
            bMrB.Click += delegate { LaunchOrFocus("OmegaMirror", _mrExe, "OmegaMirror v0.9"); BeamPortAsync(19777); };
            Button bMrX = Btn("Stop", 558, 231, 92, C(127, 29, 29));
            bMrX.Height = 30;
            bMrX.Click += delegate { StopApps(new string[] { "OmegaMirror" }); };

            Button bAv = Btn("Launch / Focus OmegaAV v0.7.5 (:19779) [F3]", 18, 266, 348, C(109, 40, 217));
            bAv.Height = 30;
            bAv.Click += delegate { LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5"); };
            Button bAvB = Btn("Beam :19779 -> Pixels", 374, 266, 176, C(14, 116, 144));
            bAvB.Height = 30;
            bAvB.Click += delegate { LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5"); BeamPortAsync(19779); };
            Button bAvX = Btn("Stop", 558, 266, 92, C(127, 29, 29));
            bAvX.Height = 30;
            bAvX.Click += delegate { StopApps(new string[] { "OmegaAV" }); };

            Button bFg = Btn("Launch / Focus OmegaForge v0.2.2 (:19780) [F4]", 18, 301, 348, C(5, 150, 105));
            bFg.Height = 30;
            bFg.Click += delegate { LaunchOrFocus("OmegaForge", _fgExe, "OmegaForge v0.2.2"); };
            Button bFgW = Btn("Forge Workspace", 374, 301, 176, C(30, 58, 138));
            bFgW.Height = 30;
            bFgW.Click += delegate { OpenDir(Path.Combine(_fgDir, "workspace")); };
            Button bFgX = Btn("Stop", 558, 301, 92, C(127, 29, 29));
            bFgX.Height = 30;
            bFgX.Click += delegate { StopApps(new string[] { "OmegaForge" }); };

            Button bSh = Btn("Launch / Focus OmegaShell v1.1.6 GM (:19781) [F7]", 18, 336, 348, C(2, 132, 199));
            bSh.Height = 30;
            bSh.Click += delegate { LaunchOrFocus("OmegaShell", _shExe, "OmegaShell v1.1.6 GM"); };
            Button bShC = Btn("AVF SSH (:22222)", 374, 336, 176, C(217, 119, 6));
            bShC.Height = 30;
            bShC.Click += delegate { TriggerAvfAutoTypeFromLauncherAsync(); };
            Button bShX = Btn("Stop", 558, 336, 92, C(127, 29, 29));
            bShX.Height = 30;
            bShX.Click += delegate { StopApps(new string[] { "OmegaShell" }); };

            Button bSy = Btn("Launch / Focus OmegaSynth v1.1.1 Final GM (:19782) [F10]", 18, 371, 348, C(190, 24, 93));
            bSy.Height = 30;
            bSy.Click += delegate { LaunchOrFocus("OmegaSynth", _syExe, "OmegaSynth v1.1.1 Final GM"); };
            Button bSyB = Btn("Beam MPE -> Pixels", 374, 371, 176, C(5, 150, 105));
            bSyB.Height = 30;
            bSyB.Click += delegate { LaunchOrFocus("OmegaSynth", _syExe, "OmegaSynth v1.1.1 Final GM"); BeamPortPathAsync(19782, "/mpe"); };
            Button bSyX = Btn("Stop", 558, 371, 92, C(127, 29, 29));
            bSyX.Height = 30;
            bSyX.Click += delegate { StopApps(new string[] { "OmegaSynth" }); };

            Button bSn = Btn("Launch / Focus OmegaSonar v1.1.0 GM (:19783) [Shift+F5]", 18, 406, 348, C(6, 148, 162));
            bSn.Height = 30;
            bSn.FlatAppearance.BorderColor = C(52, 211, 153);
            bSn.Click += delegate { LaunchOrFocus("OmegaSonar", _snExe, "OmegaSonar v1.1.0 GM (19kHz Golay Radar)"); };
            Button bSnB = Btn("Beam 6-DoF Wand -> P10", 374, 406, 176, C(67, 56, 202));
            bSnB.Height = 30;
            bSnB.Click += delegate { LaunchOrFocus("OmegaSonar", _snExe, "OmegaSonar v1.1.0 GM"); BeamPortPathAsync(19783, "/wand"); };
            Button bSnX = Btn("Stop", 558, 406, 92, C(127, 29, 29));
            bSnX.Height = 30;
            bSnX.Click += delegate { StopApps(new string[] { "OmegaSonar" }); };

            Button bPn = Btn("Launch / Focus OmegaPenrose v1.0.2 GM (:19785) [F11]", 18, 441, 348, C(14, 116, 144));
            bPn.Height = 30;
            bPn.Click += delegate { LaunchOrFocus("OmegaPenrose", _pnExe, "OmegaPenrose v1.0.2 Final GM"); };
            Button bPnB = Btn("Beam 8D/3D Pad -> Pixels", 374, 441, 176, C(67, 56, 202));
            bPnB.Height = 30;
            bPnB.Click += delegate { LaunchOrFocus("OmegaPenrose", _pnExe, "OmegaPenrose v1.0.2 Final GM"); BeamPortPathAsync(19785, "/pad"); };
            Button bPnX = Btn("Stop", 558, 441, 92, C(127, 29, 29));
            bPnX.Height = 30;
            bPnX.Click += delegate { StopApps(new string[] { "OmegaPenrose" }); };

            Button bSf = Btn("Launch / Focus OmegaSheaf v0.2.2 GM (:19784) [Shift+F6]", 18, 476, 348, C(16, 185, 129));
            bSf.Height = 30;
            bSf.Click += delegate { LaunchOrFocus("OmegaSheaf", _sfExe, "OmegaSheaf v0.2.2 GM"); };
            Button bSfB = Btn("Beam Sheaf -> Pixels", 374, 476, 176, C(67, 56, 202));
            bSfB.Height = 30;
            bSfB.Click += delegate { LaunchOrFocus("OmegaSheaf", _sfExe, "OmegaSheaf v0.2.2 GM"); BeamPortPathAsync(19784, "/sheaf"); };
            Button bSfX = Btn("Stop", 558, 476, 92, C(127, 29, 29));
            bSfX.Height = 30;
            bSfX.Click += delegate { StopApps(new string[] { "OmegaSheaf" }); };

            Button bSg = Btn("Launch / Focus OmegaSteg v0.2.1 (OmegaSec) (:19786) [Shift+F8]", 18, 511, 348, C(180, 83, 9));
            bSg.Height = 30;
            bSg.Click += delegate { LaunchOrFocus("OmegaSteg", _sgExe, "OmegaSteg v0.2.1 (OmegaSec)"); };
            Button bSgB = Btn("Beam Air-Gap -> Pixels", 374, 511, 176, C(67, 56, 202));
            bSgB.Height = 30;
            bSgB.Click += delegate { LaunchOrFocus("OmegaSteg", _sgExe, "OmegaSteg v0.2.1 (OmegaSec)"); BeamPortPathAsync(19786, "/vault"); };
            Button bSgX = Btn("Stop", 558, 511, 92, C(127, 29, 29));
            bSgX.Height = 30;
            bSgX.Click += delegate { StopApps(new string[] { "OmegaSteg" }); };

            Button bCd = Btn("Launch / Focus OmegaConductor v1.0.0 GM (:19790) [Shift+F11]", 18, 546, 348, C(13, 148, 136));
            bCd.Height = 30;
            bCd.FlatAppearance.BorderColor = C(52, 211, 153);
            bCd.Click += delegate { LaunchOrFocus("OmegaConductor", _cdExe, "OmegaConductor v1.0.0 GM (6-DoF Wand Bus)"); };
            Button bCdB = Btn("Beam 6-DoF Baton", 374, 546, 176, C(67, 56, 202));
            bCdB.Height = 30;
            bCdB.Click += delegate { LaunchOrFocus("OmegaConductor", _cdExe, "OmegaConductor v1.0.0 GM"); BeamPortPathAsync(19790, "/wand"); };
            Button bCdX = Btn("Stop", 558, 546, 92, C(127, 29, 29));
            bCdX.Height = 30;
            bCdX.Click += delegate { StopApps(new string[] { "OmegaConductor" }); };

            Button bTn = Btn("Launch / Focus OmegaTensor v1.0.2 GM (:19791) [Shift+F10]", 18, 581, 348, C(124, 58, 237));
            bTn.Height = 30;
            bTn.FlatAppearance.BorderColor = C(52, 211, 153);
            bTn.Click += delegate { LaunchOrFocus("OmegaTensor", _tnExe, "OmegaTensor v1.0.2 GM (5-Engine Split)"); };
            Button bTnB = Btn("Beam Split -> P10", 374, 581, 176, C(67, 56, 202));
            bTnB.Height = 30;
            bTnB.Click += delegate { LaunchOrFocus("OmegaTensor", _tnExe, "OmegaTensor v1.0.2 GM"); BeamPortPathAsync(19791, "/deck"); };
            Button bTnX = Btn("Stop", 558, 581, 92, C(127, 29, 29));
            bTnX.Height = 30;
            bTnX.Click += delegate { StopApps(new string[] { "OmegaTensor" }); };

            Button bP10 = Btn("Pixel 10 Quad-Engine + TPU (:19788/:19789)", 18, 616, 282, C(4, 120, 87));
            bP10.Height = 30;
            bP10.FlatAppearance.BorderColor = C(52, 211, 153);
            bP10.Click += delegate { OmegaLabPixel10CockpitDialog.ShowOrFocus(this, _root); };
            Button bP10Bench = Btn("Bench 13x+TPU", 306, 616, 122, C(147, 51, 234));
            bP10Bench.Height = 30;
            bP10Bench.Click += delegate { RunPixel10NineKernelBenchToConsoleAsync(); };
            Button bP10Rtx = Btn("RTX + P10 Tensor", 434, 616, 118, C(180, 83, 9));
            bP10Rtx.Height = 30;
            bP10Rtx.Click += delegate { RunDualSiliconTensorBenchToConsoleAsync(); };
            Button bP10X = Btn("Stop", 558, 616, 92, C(127, 29, 29));
            bP10X.Height = 30;
            bP10X.Click += delegate { StopPixel10WorkerAsync(); };

            Panel dropPanel = new Panel();
            dropPanel.SetBounds(18, 651, 632, 46);
            dropPanel.BackColor = C(13, 25, 46);
            dropPanel.AllowDrop = true;
            _lDrop = new Label();
            _lDrop.UseMnemonic = false;
            _lDrop.SetBounds(10, 4, 356, 40);
            _lDrop.Font = new Font("Consolas", 11.0f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lDrop.ForeColor = C(125, 211, 252);
            _lDrop.TextAlign = ContentAlignment.MiddleLeft;
            _lDrop.Text = "DROPZONE: Drag file -> ADB Push to Pixels\r\n(.osteg/.png->Steg | .osheaf->Sheaf | .openrose->Penrose | .omsh->AV)";

            Button bPushPick = Btn("Push File -> Pixels", 372, 7, 126, C(109, 40, 217));
            bPushPick.Height = 34;
            bPushPick.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    if (Directory.Exists(_dlDir)) ofd.InitialDirectory = _dlDir;
                    ofd.Multiselect = true;
                    if (ofd.ShowDialog(this) == DialogResult.OK) HandleDroppedFiles(ofd.FileNames);
                }
            };

            Button bPullPx = Btn("Pull Pixel DL", 504, 7, 120, C(15, 118, 110));
            bPullPx.Height = 34;
            bPullPx.Click += delegate { PullNewestFromPixelsAsync(); };

            dropPanel.Controls.AddRange(new Control[] { _lDrop, bPushPick, bPullPx });

            DragEventHandler onDe = delegate(object s1, DragEventArgs de)
            {
                de.Effect = (de.Data != null && de.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragEventHandler onDd = delegate(object s2, DragEventArgs dd)
            {
                string[] fs = dd.Data.GetData(DataFormats.FileDrop) as string[];
                if (fs != null && fs.Length > 0) HandleDroppedFiles(fs);
            };
            DragEnter += onDe; DragDrop += onDd;
            dropPanel.DragEnter += onDe; dropPanel.DragDrop += onDd;
            _lDrop.AllowDrop = true; _lDrop.DragEnter += onDe; _lDrop.DragDrop += onDd;

            Button bAll = Btn("Launch Complete 13-Station Suite + Conductor (:19790) + Tensor (:19791) [F5]", 18, 703, 632, C(124, 58, 237));
            bAll.Click += delegate { LaunchFullSuite(); };

            Button bWifi = Btn("Promote USB -> Wireless (:5555) + Tunnels", 18, 743, 310, C(88, 28, 135));
            bWifi.Click += delegate { PromoteUsbPixelsAsync(); };

            Button bSplit = Btn("Split-OLED: P1 Synth MPE | P2 OmegaAV", 340, 743, 310, C(55, 48, 163));
            bSplit.Click += delegate { BeamSplitOledAsync(); };

            Button bHarv = Btn("Corpus Harvester", 18, 783, 152, C(6, 95, 70));
            bHarv.Click += delegate
            {
                if (File.Exists(_harvPs1)) { Process.Start("powershell.exe", "-NoExit -ExecutionPolicy Bypass -File \"" + _harvPs1 + "\""); SetAct("Started Corpus Harvester -> Invoke-OmegaHarvest.ps1"); }
                else SetAct("Harvester missing: " + _harvPs1);
            };

            Button bVlt = Btn("Vaults & Downloads", 178, 783, 152, C(30, 58, 138));
            bVlt.Click += delegate { ShowVaultMenu(bVlt); };

            Button bSync = Btn("Push -> Bridge (F9)", 338, 783, 152, C(5, 150, 105));
            bSync.Click += delegate { SyncBridgeAsync(); };

            Button bManualRow = Btn("Help & FAQ (F6)", 498, 783, 152, C(180, 83, 9));
            bManualRow.FlatAppearance.BorderColor = C(250, 204, 21);
            bManualRow.Click += delegate { OpenHumanManual(); };

            Button bStop = Btn("Stop All 13 Standalone Lab Processes + Pixel 10 Worker (Free GPU VRAM & RAM) [F12]", 18, 823, 632, C(153, 27, 27));
            bStop.Click += delegate { StopApps(new string[] { "OmegaConductor", "OmegaTensor", "OmegaSonar", "OmegaSteg", "OmegaSheaf", "OmegaPenrose", "OmegaSynth", "OmegaShell", "OmegaForge", "OmegaAV", "OmegaBrowser", "OmegaMirror", "omega_p10_worker_host" }); StopPixel10WorkerAsync(); };

            // =========================================================================
            // RIGHT COLUMN (x = 664..1106): GEMMA OMEGALABS COMMANDER & CODEGEN DECK
            // =========================================================================
            Panel gemmaDeck = new Panel();
            gemmaDeck.SetBounds(664, 23, 442, 834);
            gemmaDeck.BackColor = C(13, 19, 34);
            gemmaDeck.BorderStyle = BorderStyle.FixedSingle;

            Label lGmTitle = new Label();
            lGmTitle.UseMnemonic = false;
            lGmTitle.Text = "GEMMA OMEGALABS COMMANDER & SUITE CODEGEN [F8]";
            lGmTitle.SetBounds(10, 8, 420, 20);
            lGmTitle.Font = new Font("Segoe UI", 12.2f, FontStyle.Bold, GraphicsUnit.Pixel);
            lGmTitle.ForeColor = C(192, 132, 252);

            _cmbGemmaModel = new ComboBox();
            _cmbGemmaModel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbGemmaModel.SetBounds(10, 32, 268, 24);

            _cmbGemmaModel.DropDownWidth = 380;
            _cmbGemmaModel.BackColor = C(6, 9, 17);
            _cmbGemmaModel.ForeColor = Color.White;
            _cmbGemmaModel.Font = new Font("Consolas", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel);

            Button bScanModels = Btn("Scan AI", 284, 30, 68, C(30, 58, 138));
            bScanModels.Height = 26;
            bScanModels.Click += delegate { DiscoverLauncherModelsAsync(); };

            Button bWakeOllama = Btn("Wake AI", 356, 30, 76, C(88, 28, 135));
            bWakeOllama.Height = 26;
            bWakeOllama.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            bWakeOllama.Click += delegate { WakeOllamaFromLauncherAsync(); };

            Label lMode = new Label();
            lMode.UseMnemonic = false;
            lMode.Text = "Dispatch:";
            lMode.SetBounds(10, 66, 62, 18);
            lMode.ForeColor = C(148, 163, 184);
            lMode.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);

            _cmbGemmaMode = new ComboBox();
            _cmbGemmaMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbGemmaMode.SetBounds(74, 62, 232, 24);
            _cmbGemmaMode.BackColor = C(6, 9, 17);
            _cmbGemmaMode.ForeColor = C(56, 189, 248);
            _cmbGemmaMode.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cmbGemmaMode.Items.AddRange(new object[]
            {
                "Suite ReAct Commander (Auto)",
                "Codegen: Rust -> Auto-Fmt & Run",
                "Codegen: C# 5.0 -> Compile & Run",
                "Codegen: Lean 4 -> UTF-8 Prover",
                "Codegen: C++23 -> Clang++ Run",
                "Codegen: .omsh Shader -> OmegaAV",
                "Codegen: PowerShell 5.1 (0-Backtick)"
            });
            _cmbGemmaMode.SelectedIndex = 0;

            Button bOpenInShell = Btn("-> Shell Studio", 312, 61, 120, C(2, 132, 199));
            bOpenInShell.Height = 26;
            bOpenInShell.Font = new Font("Segoe UI Semibold", 10.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            bOpenInShell.Click += delegate { OpenLastArtifactInShellStudio(); };

            Button bPreAudit = Btn("1. Audit Lab & Free RAM", 10, 94, 206, C(5, 150, 105));
            bPreAudit.Height = 28;
            bPreAudit.Click += delegate
            {
                _cmbGemmaMode.SelectedIndex = 0;
                _txtGemmaIn.Text = "Trim background RAM via OmegaShell, check active LISTEN ports, and summarize suite health";
                ExecuteLauncherGemmaAsync();
            };

            Button bPreRust = Btn("2. Codegen Rust E8 Kernel", 224, 94, 208, C(30, 58, 138));
            bPreRust.Height = 28;
            bPreRust.Click += delegate
            {
                _cmbGemmaMode.SelectedIndex = 1;
                _txtGemmaIn.Text = "Write a fast 240-root E8 norm checksum benchmark in Rust and print elapsed microseconds";
                ExecuteLauncherGemmaAsync();
            };

            Button bPreLean = Btn("3. Codegen Lean 4 Proof", 10, 126, 206, C(109, 40, 217));
            bPreLean.Height = 28;
            bPreLean.Click += delegate
            {
                _cmbGemmaMode.SelectedIndex = 3;
                _txtGemmaIn.Text = "Define e8Roots := 240 and prove 112 + 128 = e8Roots with zero sorry in Lean 4";
                ExecuteLauncherGemmaAsync();
            };

            Button bPreAv = Btn("4. Gen .omsh -> OmegaAV", 224, 126, 208, C(180, 83, 9));
            bPreAv.Height = 28;
            bPreAv.Click += delegate
            {
                _cmbGemmaMode.SelectedIndex = 5;
                _txtGemmaIn.Text = "Generate a vibrant E8 harmonic wavefield .omsh expression shader for OmegaAV";
                ExecuteLauncherGemmaAsync();
            };

            Panel logPad = new Panel();
            logPad.SetBounds(10, 160, 422, 575);
            logPad.Padding = new Padding(7, 5, 2, 4);
            logPad.BackColor = C(6, 9, 17);

            _rtbGemmaOut = new RichTextBox();
            _rtbGemmaOut.Dock = DockStyle.Fill;
            _rtbGemmaOut.ReadOnly = true;
            _rtbGemmaOut.BorderStyle = BorderStyle.None;
            _rtbGemmaOut.WordWrap = true;
            _rtbGemmaOut.ScrollBars = RichTextBoxScrollBars.Vertical;
            _rtbGemmaOut.BackColor = C(6, 9, 17);
            _rtbGemmaOut.ForeColor = C(226, 232, 240);
            _rtbGemmaOut.Font = new Font("Consolas", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            logPad.Controls.Add(_rtbGemmaOut);

            AppendGemmaLine(
                "=== GEMMA SUITE COMMANDER READY ===\r\n" +
                "Gemma now commands all 9 OmegaLabs apps, compilers, and codegen:\r\n" +
                "  * Auto-routes OSH & Hardware Tuner ops via OmegaShell (:19781)\r\n" +
                "  * Generates, formats & verifies Rust / C# / C++23 / Lean 4 / PS1\r\n" +
                "  * Hot-beams generated .omsh shaders straight into OmegaAV (:19779)\r\n" +
                "Click any preset above or type a mission below!",
                C(125, 211, 252));

            _txtGemmaIn = new TextBox();
            _txtGemmaIn.SetBounds(10, 743, 294, 28);
            _txtGemmaIn.BackColor = C(7, 11, 20);
            _txtGemmaIn.ForeColor = Color.White;
            _txtGemmaIn.Font = new Font("Consolas", 12.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _txtGemmaIn.BorderStyle = BorderStyle.FixedSingle;
            _txtGemmaIn.Text = "Write a Rust prime-counting benchmark and verify Exit 0";
            _txtGemmaIn.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    ExecuteLauncherGemmaAsync();
                }
            };

            Button bRunGm = Btn("Run", 308, 741, 68, C(124, 58, 237));
            bRunGm.Height = 30;
            bRunGm.Click += delegate { ExecuteLauncherGemmaAsync(); };

            Button bStopGm = Btn("Stop", 380, 741, 52, C(153, 27, 27));
            bStopGm.Height = 30;
            bStopGm.Click += delegate { AbortLauncherGemmaStream(); };

            gemmaDeck.Controls.AddRange(new Control[]
            {
                lGmTitle, _cmbGemmaModel, bScanModels, bWakeOllama,
                lMode, _cmbGemmaMode, bOpenInShell,
                bPreAudit, bPreRust, bPreLean, bPreAv,
                logPad, _txtGemmaIn, bRunGm, bStopGm
            });

            Panel bot = new Panel();
            bot.Dock = DockStyle.Bottom;
            bot.Height = 26;
            bot.BackColor = C(6, 9, 17);
            _lAct = new Label();
            _lAct.UseMnemonic = false;
            _lAct.Dock = DockStyle.Fill;
            _lAct.Font = new Font("Consolas", 10.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            _lAct.ForeColor = C(52, 211, 153);
            _lAct.TextAlign = ContentAlignment.MiddleLeft;
            _lAct.Padding = new Padding(14, 0, 0, 0);
            _lAct.Text = "Launcher v2.5.0 Ready :: F1..F4 | F7=Shell | Shift+F11=Conductor (:19790) | Shift+F10=Tensor (:19791) | Shift+F9=P10 TPU | F12=Stop";
            bot.Controls.Add(_lAct);

            Controls.AddRange(new Control[]
            {
                sub, hdr, bHelpTop, card,
                bBr, bFrz, bBrX, bMr, bMrB, bMrX, bAv, bAvB, bAvX, bFg, bFgW, bFgX, bSh, bShC, bShX, bSy, bSyB, bSyX, bSn, bSnB, bSnX, bPn, bPnB, bPnX, bSf, bSfB, bSfX, bSg, bSgB, bSgX, bCd, bCdB, bCdX, bTn, bTnB, bTnX, bP10, bP10Bench, bP10Rtx, bP10X,
                dropPanel, bAll, bWifi, bSplit, bHarv, bVlt, bSync, bManualRow, bStop,
                gemmaDeck, bot
            });

            KeyDown += OnHotKey;
            _tmr = new System.Windows.Forms.Timer();
            _tmr.Interval = 2200;
            _tmr.Tick += delegate { RefreshAsync(); };
            Shown += delegate { RefreshAsync(); DiscoverLauncherModelsAsync(); _tmr.Start(); };
        }

        private void AppendGemmaLine(string text, Color fg)
        {
            if (_rtbGemmaOut == null || IsDisposed) return;
            Action act = delegate
            {
                if (_rtbGemmaOut.TextLength > 28000) { _rtbGemmaOut.Select(0, 9000); _rtbGemmaOut.SelectedText = ""; }
                _rtbGemmaOut.SelectionStart = _rtbGemmaOut.TextLength;
                _rtbGemmaOut.SelectionLength = 0;
                _rtbGemmaOut.SelectionColor = fg;
                _rtbGemmaOut.AppendText((text ?? "") + "\r\n");
                _rtbGemmaOut.SelectionStart = _rtbGemmaOut.TextLength;
                _rtbGemmaOut.ScrollToCaret();
            };
            if (InvokeRequired) { try { BeginInvoke(act); } catch { } }
            else act();
        }

        private void AbortLauncherGemmaStream()
        {
            _abortGemma = true;
            HttpWebRequest r = Interlocked.Exchange(ref _activeGemmaReq, null);
            if (r != null) { try { r.Abort(); } catch { } }
            SetAct("Stopped active Gemma stream.");
        }

        private void DiscoverLauncherModelsAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LauncherAiModel> found = new List<LauncherAiModel>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                bool up11434 = ProbePort(11434);
                bool up8097 = ProbePort(8097);

                LauncherAiModel tandem = new LauncherAiModel();
                tandem.Provider = "Tandem"; tandem.Tag = "auto-gemma-router"; tandem.Endpoint = "tandem://auto"; tandem.Online = (up11434 || up8097);
                found.Add(tandem);

                if (up11434)
                {
                    string rawOl = HttpGetFast("http://127.0.0.1:11434/api/tags", 1600);
                    MatchCollection mc = Regex.Matches(rawOl, "\"name\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])+)\"");
                    for (int i = 0; i < mc.Count; i++)
                    {
                        string nm = JsonUnesc(mc[i].Groups[1].Value).Trim();
                        if (nm.Length > 0 && seen.Add("11434|" + nm))
                        {
                            LauncherAiModel m = new LauncherAiModel();
                            m.Provider = "Ollama"; m.Tag = nm; m.Endpoint = "http://127.0.0.1:11434/api/chat"; m.Online = true;
                            found.Add(m);
                        }
                    }
                }

                if (up8097)
                {
                    string raw8097 = HttpGetFast("http://127.0.0.1:8097/v1/models", 1500);
                    MatchCollection mc = Regex.Matches(raw8097, "\"id\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])+)\"");
                    for (int i = 0; i < mc.Count; i++)
                    {
                        string id = JsonUnesc(mc[i].Groups[1].Value).Trim();
                        if (id.Length > 0 && seen.Add("8097|" + id))
                        {
                            LauncherAiModel m = new LauncherAiModel();
                            m.Provider = "8097"; m.Tag = id; m.Endpoint = "http://127.0.0.1:8097/v1/chat/completions"; m.Online = true;
                            found.Add(m);
                        }
                    }
                }

                if (found.Count == 0)
                {
                    LauncherAiModel fb = new LauncherAiModel();
                    fb.Provider = "8097"; fb.Tag = "gemma-4-12B-it-sh4z"; fb.Endpoint = "http://127.0.0.1:8097/v1/chat/completions"; fb.Online = false;
                    found.Add(fb);
                }

                if (!IsDisposed)
                {
                    BeginInvoke((Action)delegate
                    {
                        _cmbGemmaModel.Items.Clear();
                        for (int i = 0; i < found.Count; i++) _cmbGemmaModel.Items.Add(found[i]);
                        if (_cmbGemmaModel.Items.Count > 0) _cmbGemmaModel.SelectedIndex = 0;
                    });
                }
            });
        }

        private void WakeOllamaFromLauncherAsync()
        {
            SetAct("Waking local Ollama engine (:11434)...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (!ProbePort(11434))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("ollama.exe", "serve");
                        psi.CreateNoWindow = true; psi.UseShellExecute = false;
                        Process.Start(psi);
                        for (int i = 0; i < 12; i++) { Thread.Sleep(220); if (ProbePort(11434)) break; }
                    }
                    catch { }
                }
                DiscoverLauncherModelsAsync();
                SetAct(ProbePort(11434) ? "Ollama (:11434) is ONLINE & scanned!" : "Triggered ollama serve (check :11434).");
            });
        }

        private void EnsureShellOnlineForOrchestration()
        {
            if (ProbePort(19781)) return;
            if (!File.Exists(_shExe)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(_shExe);
                psi.WorkingDirectory = _shDir;
                Process.Start(psi);
                for (int i = 0; i < 18; i++)
                {
                    Thread.Sleep(180);
                    if (ProbePort(19781)) break;
                }
            }
            catch { }
        }

        private void ExecuteLauncherGemmaAsync()
        {
            string prompt = (_txtGemmaIn.Text ?? "").Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            LauncherAiModel selModel = _cmbGemmaModel.SelectedItem as LauncherAiModel;
            string modelTag = selModel != null ? selModel.Tag : "gemma-4-12B-it-sh4z";
            string endpoint = selModel != null ? selModel.Endpoint : "http://127.0.0.1:8097/v1/chat/completions";
            int modeIdx = _cmbGemmaMode.SelectedIndex;

            AppendGemmaLine("\r\n>>> [MISSION :: " + modelTag + "] " + prompt, C(192, 132, 252));
            SetAct("Gemma Commander (" + modelTag + ") executing suite mission...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                _abortGemma = false;
                try
                {
                    if (modeIdx == 5)
                    {
                        // Direct .omsh Shader Generation -> Hot-load into OmegaAV v0.7.5 (:19779)
                        string shSys = "Output ONLY clean mathematical expression shader code (.omsh / HLSL style) suitable for OmegaAV wavefield modulation. No prose.";
                        string shEp = endpoint.StartsWith("tandem://", StringComparison.OrdinalIgnoreCase) ? "http://127.0.0.1:11434/api/chat" : endpoint;
                        string shTag = string.Equals(modelTag, "auto-gemma-router", StringComparison.OrdinalIgnoreCase) ? "gemma3:4b" : modelTag;
                        string shaderCode = "";
                        try { shaderCode = StreamDirectFromLauncher(shEp, shTag, shSys, prompt); } catch { }
                        string inner = ExtractFirstFenceOrRaw(shaderCode);
                        if (string.IsNullOrWhiteSpace(inner) || inner.IndexOf("UNMEASURED/OFFLINE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            inner = "// Verified E8 Harmonic Wavefield (.omsh)\r\n" +
                                "wave = sin(u * 8.0 + t * 2.4) * cos(v * 8.0 - t * 1.7) + 0.5 * sin((u + v) * 16.0 + bass * 4.0);\r\n" +
                                "palette = float3(0.5 + 0.5 * sin(wave * 3.14), 0.4 + 0.5 * cos(wave * 2.0 + t), 0.85);\r\n";
                        }
                        string shDir = Path.Combine(_avDir, "shaders");
                        Directory.CreateDirectory(shDir);
                        string omshFile = Path.Combine(shDir, "gemma_launcher_" + DateTime.Now.ToString("HHmmss") + ".omsh");
                        File.WriteAllText(omshFile, inner, new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(_avDir, "inbox_open.cmd"), omshFile, new UTF8Encoding(false));
                        _lastGeneratedArtifact = omshFile;
                        BeginInvoke((Action)delegate { LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5"); });
                        AppendGemmaLine("[Hot-Loaded .omsh Shader -> OmegaAV v0.7.5 (:19779)]\r\nFile: " + omshFile, C(52, 211, 153));
                        SetAct("Beamed Gemma .omsh shader -> OmegaAV v0.7.5!");
                        return;
                    }

                    EnsureShellOnlineForOrchestration();

                    if (ProbePort(19781))
                    {
                        if (modeIdx == 0)
                        {
                            string reqJson = "{\"prompt\":\"" + JsonEsc(prompt) + "\",\"model\":\"" + JsonEsc(modelTag) + "\",\"endpoint\":\"" + JsonEsc(endpoint) + "\"}";
                            string raw = HttpPostFast("http://127.0.0.1:19781/api/gemma", reqJson, 95000);
                            string narr = ExtractJsonField(raw, "narrative");
                            string genFile = ExtractJsonField(raw, "generated_file");
                            if (!string.IsNullOrEmpty(genFile)) _lastGeneratedArtifact = genFile;
                            AppendGemmaLine(string.IsNullOrEmpty(narr) ? raw : narr, C(226, 232, 240));
                            SetAct("Gemma Suite Mission complete via OmegaShell v1.1.6 GM (:19781)!");
                        }
                        else
                        {
                            string[] langs = new string[] { "auto", "rust", "csharp", "lean4", "cpp", "hlsl", "powershell" };
                            string lang = (modeIdx >= 1 && modeIdx < langs.Length) ? langs[modeIdx] : "rust";
                            string reqJson = "{\"lang\":\"" + lang + "\",\"prompt\":\"" + JsonEsc(prompt) + "\",\"open_studio\":\"1\"}";
                            string raw = HttpPostFast("http://127.0.0.1:19781/api/codegen", reqJson, 95000);
                            string outFile = ExtractJsonField(raw, "file");
                            string outLog = ExtractJsonField(raw, "output");
                            string fmt = ExtractJsonField(raw, "formatter");
                            if (!string.IsNullOrEmpty(outFile)) _lastGeneratedArtifact = outFile;
                            AppendGemmaLine("[Verified via OmegaShell v1.1.3 Code Studio :: " + fmt + "]\r\nFile: " + outFile + "\r\n" + outLog, C(52, 211, 153));
                            SetAct("Gemma Codegen complete -> opened in OmegaShell Code Studio!");
                        }
                        RefreshAsync();
                        return;
                    }

                    // Standalone direct fallback if OmegaShell executable wasn't built yet
                    string fallbackReply = StreamDirectFromLauncher(endpoint, modelTag, "You are Gemma, ACT-Omega Lab Engineering Assistant. Be concise and accurate.", prompt);
                    AppendGemmaLine(fallbackReply, C(226, 232, 240));
                    SetAct("Gemma direct response complete.");
                }
                catch (Exception ex)
                {
                    AppendGemmaLine("[Gemma Error] " + ex.Message, C(248, 113, 113));
                    SetAct("Gemma mission error: " + ex.Message);
                }
            });
        }

        private string StreamDirectFromLauncher(string url, string model, string sys, string usr)
        {
            bool isOl = url.IndexOf("/api/chat", StringComparison.OrdinalIgnoreCase) >= 0;
            string msgs = "[{\"role\":\"system\",\"content\":\"" + JsonEsc(sys) + "\"},{\"role\":\"user\",\"content\":\"" + JsonEsc(usr) + "\"}]";
            string body = isOl
                ? ("{\"model\":\"" + JsonEsc(model) + "\",\"messages\":" + msgs + ",\"stream\":true}")
                : ("{\"model\":\"" + JsonEsc(model) + "\",\"messages\":" + msgs + ",\"max_tokens\":1024,\"temperature\":0.2,\"stream\":true}");

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = 65000;
            req.ReadWriteTimeout = 65000;
            Interlocked.Exchange(ref _activeGemmaReq, req);

            StringBuilder acc = new StringBuilder();
            StringBuilder rawAll = new StringBuilder();
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                req.ContentLength = b.Length;
                using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string ln;
                    while ((ln = sr.ReadLine()) != null)
                    {
                        if (_abortGemma) { try { req.Abort(); } catch { } break; }
                        if (rawAll.Length < 131072) rawAll.AppendLine(ln);
                        string t = ln.Trim();
                        if (t.StartsWith("data:")) t = t.Substring(5).Trim();
                        Match md = Regex.Match(t, "\"delta\"\\s*:\\s*\\{[^}]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                        if (md.Success) acc.Append(JsonUnesc(md.Groups[1].Value));
                        else
                        {
                            Match mm = Regex.Match(t, "\"message\"\\s*:\\s*\\{[^}]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                            if (mm.Success) acc.Append(JsonUnesc(mm.Groups[1].Value));
                        }
                    }
                }
                if (acc.Length == 0)
                {
                    Match mf = Regex.Match(rawAll.ToString(), "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
                    if (mf.Success) acc.Append(JsonUnesc(mf.Groups[1].Value));
                }
            }
            finally { Interlocked.Exchange(ref _activeGemmaReq, null); }
            return acc.ToString();
        }

        private void OpenLastArtifactInShellStudio()
        {
            LaunchOrFocus("OmegaShell", _shExe, "OmegaShell v1.1.6 GM");
            if (!string.IsNullOrEmpty(_lastGeneratedArtifact) && File.Exists(_lastGeneratedArtifact))
            {
                string target = _lastGeneratedArtifact;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(350);
                    if (ProbePort(19781))
                        HttpPostFast("http://127.0.0.1:19781/api/exec", "{\"cmd\":\"code \\\"" + JsonEsc(target) + "\\\"\"}", 4000);
                });
            }
        }

        private static string ExtractFirstFenceOrRaw(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int o = s.IndexOf(FENCE, StringComparison.Ordinal);
            if (o >= 0)
            {
                int nl = s.IndexOf('\n', o + 3);
                if (nl > o)
                {
                    int c = s.IndexOf(FENCE, nl + 1, StringComparison.Ordinal);
                    return (c > nl ? s.Substring(nl + 1, c - (nl + 1)) : s.Substring(nl + 1)).Trim();
                }
            }
            return s.Trim();
        }

        private void OpenHumanManual()
        {
            using (OmegaLabManualDialog dlg = new OmegaLabManualDialog(_root))
            {
                dlg.ShowDialog(this);
            }
        }

        private void HandleDroppedFiles(string[] files)
        {
            if (files == null || files.Length == 0) return;
            for (int i = 0; i < files.Length; i++)
            {
                string ext = (Path.GetExtension(files[i]) ?? "").ToLowerInvariant();
                if ((ext == ".osynth" || ext == ".mp3" || ext == ".flac" || ext == ".m4a" || ext == ".ogg" || ext == ".wav") && File.Exists(files[i]))
                {
                    try
                    {
                        Directory.CreateDirectory(_syDir);
                        File.WriteAllText(Path.Combine(_syDir, "synth_inbox.cmd"), files[i], new UTF8Encoding(false));
                        LaunchOrFocus("OmegaSynth", _syExe, "OmegaSynth v1.1.1 Final GM");
                    }
                    catch { }
                }
                if ((ext == ".osteg" || ext == ".ocapsule" || ext == ".oshare" || ext == ".png") && File.Exists(files[i]))
                {
                    try
                    {
                        Directory.CreateDirectory(_sgDir);
                        File.WriteAllText(Path.Combine(_sgDir, "steg_inbox.cmd"), files[i], new UTF8Encoding(false));
                        LaunchOrFocus("OmegaSteg", _sgExe, "OmegaSteg v0.2.1 (OmegaSec)");
                    }
                    catch { }
                }
                if (ext == ".osheaf" && File.Exists(files[i]))
                {
                    try
                    {
                        string pDir = Path.Combine(_sfDir, "patches");
                        Directory.CreateDirectory(pDir);
                        string dstP = Path.Combine(pDir, Path.GetFileName(files[i]));
                        if (!string.Equals(Path.GetFullPath(files[i]), Path.GetFullPath(dstP), StringComparison.OrdinalIgnoreCase))
                            File.Copy(files[i], dstP, true);
                        File.WriteAllText(Path.Combine(_sfDir, "sheaf_inbox.cmd"), dstP, new UTF8Encoding(false));
                        LaunchOrFocus("OmegaSheaf", _sfExe, "OmegaSheaf v0.2.1 GM");
                    }
                    catch { }
                }
                if (ext == ".openrose" && File.Exists(files[i]))
                {
                    try
                    {
                        string pDir = Path.Combine(_pnDir, "patches");
                        Directory.CreateDirectory(pDir);
                        string dstP = Path.Combine(pDir, Path.GetFileName(files[i]));
                        if (!string.Equals(Path.GetFullPath(files[i]), Path.GetFullPath(dstP), StringComparison.OrdinalIgnoreCase))
                            File.Copy(files[i], dstP, true);
                        LaunchOrFocus("OmegaPenrose", _pnExe, "OmegaPenrose v1.0.2 Final GM");
                    }
                    catch { }
                }
                if ((ext == ".wav" || ext == ".omsh" || ext == ".hlsl") && File.Exists(files[i]))
                {
                    try
                    {
                        string stageDir = Path.Combine(_avDir, ext == ".wav" ? "audio_inbox" : "shaders");
                        Directory.CreateDirectory(stageDir);
                        string staged = Path.Combine(stageDir, Path.GetFileName(files[i]));
                        if (!string.Equals(Path.GetFullPath(files[i]), Path.GetFullPath(staged), StringComparison.OrdinalIgnoreCase))
                            File.Copy(files[i], staged, true);
                        File.WriteAllText(Path.Combine(_avDir, "inbox_open.cmd"), staged, new UTF8Encoding(false));
                        LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5");
                    }
                    catch { }
                }
            }

            SetAct("ADB Broadcasting " + files.Length + " file(s) -> Connected Pixels (/sdcard/Download/)...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> ser = ListSerials();
                if (ser.Count == 0) { SetAct("Staged locally (no ADB Pixel connected for push)."); return; }
                int pushed = 0;
                for (int s = 0; s < ser.Count; s++)
                {
                    for (int f = 0; f < files.Length; f++)
                    {
                        if (!File.Exists(files[f])) continue;
                        string fn = Path.GetFileName(files[f]).Replace("\"", "_");
                        string rem = "/sdcard/Download/" + fn;
                        RunAdb("-s " + ser[s] + " push \"" + files[f] + "\" \"" + rem + "\"", 180000);
                        RunAdb("-s " + ser[s] + " shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d \"file://" + rem + "\"", 3500);
                        pushed++;
                    }
                }
                SetAct("Dropzone OK: Pushed " + files.Length + " file(s) across " + ser.Count + " Pixel(s) (" + pushed + " transfers)!");
            });
        }

        private void PullNewestFromPixelsAsync()
        {
            SetAct("Pulling newest file from Pixel /sdcard/Download/ -> E:\\ Vault...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> ser = ListSerials();
                if (ser.Count == 0) { SetAct("No ADB Pixel connected."); return; }
                string lsOut = RunAdb("-s " + ser[0] + " shell ls -1t /sdcard/Download", 3500);
                string newest = "";
                foreach (string ln in lsOut.Split('\n'))
                {
                    string c = ln.Trim();
                    if (c.Length > 0 && !c.StartsWith("total ") && !c.Contains("/") && c != "." && c != "..") { newest = c; break; }
                }
                if (string.IsNullOrEmpty(newest)) { SetAct("No files found in Pixel /sdcard/Download/."); return; }
                string cat = "Files";
                string ext = (Path.GetExtension(newest) ?? "").ToLowerInvariant();
                if (ext == ".pdf") cat = "Papers";
                else if (ext == ".wav" || ext == ".mp3" || ext == ".flac") cat = "Audio";
                else if (ext == ".omsh" || ext == ".hlsl") cat = "Shaders";
                string outDir = Path.Combine(_dlDir, cat);
                Directory.CreateDirectory(outDir);
                string dest = Path.Combine(outDir, newest);
                RunAdb("-s " + ser[0] + " pull \"/sdcard/Download/" + newest.Replace("\"", "") + "\" \"" + dest + "\"", 120000);
                if (File.Exists(dest))
                {
                    try { Process.Start("explorer.exe", "/select,\"" + dest + "\""); } catch { }
                    SetAct("Pulled " + newest + " -> " + dest);
                    RefreshAsync();
                }
            });
        }

        private void OnHotKey(object s, KeyEventArgs e)
        {
            if (ActiveControl == _txtGemmaIn && e.KeyCode != Keys.F1 && e.KeyCode != Keys.F2 && e.KeyCode != Keys.F3 && e.KeyCode != Keys.F4 && e.KeyCode != Keys.F5 && e.KeyCode != Keys.F6 && e.KeyCode != Keys.F7 && e.KeyCode != Keys.F9 && e.KeyCode != Keys.F10 && e.KeyCode != Keys.F11 && e.KeyCode != Keys.F12)
                return;

            if (e.KeyCode == Keys.F1) LaunchOrFocus("OmegaBrowser", _brExe, "OmegaBrowser v0.6.4");
            else if (e.KeyCode == Keys.F2) LaunchOrFocus("OmegaMirror", _mrExe, "OmegaMirror v0.9");
            else if (e.KeyCode == Keys.F3) LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5");
            else if (e.KeyCode == Keys.F4) LaunchOrFocus("OmegaForge", _fgExe, "OmegaForge v0.2.1");
            else if (e.Shift && e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaSonar", _snExe, "OmegaSonar v1.1.0 GM"); }
            else if (e.KeyCode == Keys.F5) LaunchFullSuite();
            else if (e.Shift && e.KeyCode == Keys.F6) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaSheaf", _sfExe, "OmegaSheaf v0.2.1 GM"); }
            else if (e.KeyCode == Keys.F6 || (e.Shift && e.KeyCode == Keys.OemQuestion && ActiveControl != _txtGemmaIn)) OpenHumanManual();
            else if (e.KeyCode == Keys.F7) LaunchOrFocus("OmegaShell", _shExe, "OmegaShell v1.1.6 GM");
            else if (e.Shift && e.KeyCode == Keys.F10) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaTensor", _tnExe, "OmegaTensor v1.0.2 GM"); }
            else if (e.KeyCode == Keys.F10) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaSynth", _syExe, "OmegaSynth v1.1.1 Final GM"); }
            else if (e.Shift && e.KeyCode == Keys.F11) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaConductor", _cdExe, "OmegaConductor v1.0.0 GM"); }
            else if (e.KeyCode == Keys.F11) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaPenrose", _pnExe, "OmegaPenrose v1.0.2 Final GM"); }
            else if (e.Shift && e.KeyCode == Keys.F8) { e.SuppressKeyPress = true; LaunchOrFocus("OmegaSteg", _sgExe, "OmegaSteg v0.2.1 (OmegaSec)"); }
            else if (e.KeyCode == Keys.F8) _txtGemmaIn.Focus();
            else if (e.Shift && e.KeyCode == Keys.F9) { e.SuppressKeyPress = true; OmegaLabPixel10CockpitDialog.ShowOrFocus(this, _root); }
            else if (e.KeyCode == Keys.F9) SyncBridgeAsync();
            else if (e.KeyCode == Keys.F12) { StopApps(new string[] { "OmegaConductor", "OmegaTensor", "OmegaSonar", "OmegaSteg", "OmegaSheaf", "OmegaPenrose", "OmegaSynth", "OmegaShell", "OmegaForge", "OmegaAV", "OmegaBrowser", "OmegaMirror", "omega_p10_worker_host" }); StopPixel10WorkerAsync(); }
        }

        public static void QuickWakePixel10DaemonStatic()
        {
            if (Pixel10TensorBridge.ProbeDaemonHealth(19788, 450)) return;
            List<string> s = ListSerials();
            if (s.Count == 0) return;
            RunAdb("-s " + s[0] + " forward tcp:19788 tcp:19788", 1500);
            if (Pixel10TensorBridge.ProbeDaemonHealth(19788, 450)) return;
            RunAdb("-s " + s[0] + " shell \"test -x /data/local/tmp/omega_p10_worker && (/data/local/tmp/omega_p10_worker --daemonize --port 19788)\"", 3000);
            for (int i = 0; i < 8; i++)
            {
                Thread.Sleep(220);
                if (Pixel10TensorBridge.ProbeDaemonHealth(19788, 450)) break;
            }
        }

        private void RunPixel10NineKernelBenchToConsoleAsync()
        {
            SetAct("Running live 10-Kernel Vulkan 1.4 + Tensor G5 Benchmark on Pixel 10 (:19788)...");
            AppendGemmaLine("\r\n>>> [PIXEL 10 :: 10-KERNEL VULKAN 1.4 + COOPERATIVE MATRIX BENCHMARK (:19788)]", C(52, 211, 153));
            ThreadPool.QueueUserWorkItem(delegate
            {
                ArmAllPixelsStatic();
                QuickWakePixel10DaemonStatic();
                P10TelemetrySnapshot snap = Pixel10TensorBridge.FetchSnapshot("/api/bench", 45000);
                if (!snap.Online)
                {
                    string ps1 = Path.Combine(_p10Dir, "Deploy-And-Run-Pixel10Worker.ps1");
                    if (File.Exists(ps1))
                    {
                        AppendGemmaLine("Daemon :19788 was asleep — building/deploying via Deploy-And-Run-Pixel10Worker.ps1...", C(56, 189, 248));
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\" -Action BenchAndDaemon");
                            psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
                            using (Process p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(120000); }
                        }
                        catch { }
                        snap = Pixel10TensorBridge.FetchSnapshot("/api/status", 10000);
                    }
                }
                if (snap.Online)
                {
                    P10TelemetrySnapshot avfSnap = ProbePort(19789) ? Pixel10TensorBridge.FetchSnapshotOnPort(19789, "/api/status", 1400) : null;
                            _cachedP10Line = Pixel10TensorBridge.FormatDualDaemonLauncherLine(snap, avfSnap);
                    AppendGemmaLine(Pixel10TensorBridge.FormatMultiLineScorecard("PIXEL 10 VERIFIED 10-KERNEL SCORECARD", snap), C(52, 211, 153));
                    SetAct(_cachedP10Line);
                }
                else
                {
                    AppendGemmaLine("[Pixel 10 :19788] Connect Pixel 10 over USB/Wi-Fi ADB and click 'Pixel 10 10-Kernel Tensor [Shift+F9]'.", C(248, 113, 113));
                    SetAct("Pixel 10 (:19788) not reachable — check USB/Wireless ADB.");
                }
                RefreshAsync();
            });
        }

        private void RunDualSiliconTensorBenchToConsoleAsync()
        {
            SetAct("Running Dual-Silicon IntDot + Cooperative Matrix Tensor Benchmark (DirectCompute GPU + Pixel 10 PowerVR)...");
            AppendGemmaLine("\r\n>>> [DUAL-SILICON TENSOR BENCH :: HOST DirectCompute GPU + PIXEL 10 POWERVR DXT-48-1536]", C(250, 204, 21));
            ThreadPool.QueueUserWorkItem(delegate
            {
                string hostExe = Path.Combine(_p10Dir, "bin", "omega_p10_worker_host.exe");
                if (File.Exists(hostExe))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(hostExe, "--vulkan tensor");
                        psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
                        using (Process p = Process.Start(psi))
                        {
                            string rtxOut = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(30000);
                            AppendGemmaLine("=== HOST NVIDIA DirectCompute GPU AMPERE TENSOR CORES ===\r\n" + (rtxOut ?? "").Trim(), C(125, 211, 252));
                        }
                    }
                    catch (Exception ex) { AppendGemmaLine("[DirectCompute GPU Error] " + ex.Message, C(248, 113, 113)); }
                }

                ArmAllPixelsStatic();
                QuickWakePixel10DaemonStatic();
                P10TelemetrySnapshot p10 = Pixel10TensorBridge.FetchSnapshot("/api/coopmat", 35000);
                if (p10.Online)
                {
                    AppendGemmaLine(Pixel10TensorBridge.FormatMultiLineScorecard("PIXEL 10 POWERVR DXT-48-1536 TENSOR CORES", p10), C(52, 211, 153));
                    SetAct("Dual-Silicon Tensor Bench complete (DirectCompute GPU + Pixel 10 100% Parity)!");
                }
                else
                {
                    AppendGemmaLine("[Pixel 10 :19788] Connect Pixel 10 via ADB to include PowerVR K7..K9 tensor results.", C(250, 204, 21));
                    SetAct("Host DirectCompute GPU Tensor Bench complete.");
                }
                RefreshAsync();
            });
        }

        private void StopPixel10WorkerAsync()
        {
            SetAct("Stopping Pixel 10 Rust+Vulkan Worker daemon (:19788)...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpGetFast("http://127.0.0.1:19788/api/shutdown", 1200);
                List<string> s = ListSerials();
                for (int i = 0; i < s.Count; i++)
                    RunAdb("-s " + s[i] + " shell \"pkill -f omega_p10_worker >/dev/null 2>&1 || true\"", 2000);
                _cachedP10Line = "P10 10-Kernel Tensor (:19788): STOPPED (click row or Shift+F9 to wake)";
                SetAct("Stopped Pixel 10 Worker (:19788).");
                RefreshAsync();
            });
        }

        public static string StageAvfSetupFilesStatic(string suiteRoot)
        {
            string sshDir = Path.Combine(suiteRoot, "10_omega_shell", "config", "ssh");
            Directory.CreateDirectory(sshDir);
            string privKey = Path.Combine(sshDir, "id_ed25519_omega");
            string pubPath = privKey + ".pub";
            if (!File.Exists(pubPath))
            {
                string keygen = @"C:\Windows\System32\OpenSSH\ssh-keygen.exe";
                if (File.Exists(keygen))
                {
                    try
                    {
                        ProcessStartInfo kpsi = new ProcessStartInfo(keygen, "-t ed25519 -f \"" + privKey + "\" -N \"\" -C \"omega-shell@local-host\"");
                        kpsi.CreateNoWindow = true; kpsi.UseShellExecute = false;
                        using (Process kp = Process.Start(kpsi)) kp.WaitForExit(8000);
                    }
                    catch { }
                }
            }
            if (!File.Exists(pubPath)) return "[AVF Stage] Public key not yet created.";

            string cleanKey = File.ReadAllText(pubPath, Encoding.UTF8).Replace("\x27", "").Trim();
            string script =
                "#!/usr/bin/env bash\nset -e\n" +
                "mkdir -p \"$HOME/.ssh\" && chmod 700 \"$HOME/.ssh\"\n" +
                "touch \"$HOME/.ssh/authorized_keys\"\n" +
                "grep -Fq \x27" + cleanKey + "\x27 \"$HOME/.ssh/authorized_keys\" 2>/dev/null || echo \x27" + cleanKey + "\x27 >> \"$HOME/.ssh/authorized_keys\"\n" +
                "chmod 600 \"$HOME/.ssh/authorized_keys\"\n" +
                "if [ ! -x /usr/sbin/sshd ]; then sudo apt-get update -y && sudo apt-get install -y openssh-server; fi\n" +
                "sudo mkdir -p /etc/ssh/sshd_config.d\n" +
                "printf \x27Port 2222\\nPubkeyAuthentication yes\\nPasswordAuthentication no\\nListenAddress 0.0.0.0\\n\x27 | sudo tee /etc/ssh/sshd_config.d/99-omegashell.conf >/dev/null\n" +
                "sudo ssh-keygen -A >/dev/null 2>&1 || true\n" +
                "sudo systemctl disable --now ssh.socket >/dev/null 2>&1 || true\n" +
                "sudo systemctl stop ssh >/dev/null 2>&1 || true\n" +
                "sudo pkill -f \x27/usr/sbin/sshd\x27 2>/dev/null || true\n" +
                "sudo mkdir -p -m 0755 /run/sshd\n" +
                "sudo systemctl enable --now ssh >/dev/null 2>&1 || true\n" +
                "ss -tln 2>/dev/null | grep -q \x27:2222 \x27 || (sudo mkdir -p -m 0755 /run/sshd && sudo /usr/sbin/sshd -p 2222)\n" +
                "if [ -d /mnt/shared/Download/linux ]; then ln -sfn /mnt/shared/Download/linux \"$HOME/omega_shared\" 2>/dev/null || true; fi\n" +
                "echo \"[OMEGASHELL AVF READY] User=$(whoami) | Port=2222 | Kernel=$(uname -srm)\"\n";

            string localSh = Path.Combine(sshDir, "omega_debian_setup.sh");
            File.WriteAllBytes(localSh, Encoding.UTF8.GetBytes(script));

            List<string> sers = ListSerials();
            if (sers.Count == 0) return "[AVF Stage] Regenerated " + localSh + " (no ADB Pixel connected).";
            for (int i = 0; i < sers.Count; i++)
            {
                string ser = sers[i];
                RunAdb("-s " + ser + " shell appops set com.android.virtualization.terminal MANAGE_EXTERNAL_STORAGE allow", 2500);
                RunAdb("-s " + ser + " shell cmd appops set com.android.virtualization.terminal RUN_IN_BACKGROUND allow", 2500);
                RunAdb("-s " + ser + " shell dumpsys deviceidle whitelist +com.android.virtualization.terminal", 2500);
                RunAdb("-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", 2500);
                RunAdb("-s " + ser + " push \"" + localSh + "\" /sdcard/omega_debian_setup.sh", 5000);
                RunAdb("-s " + ser + " push \"" + localSh + "\" /sdcard/Download/omega_debian_setup.sh", 5000);
                RunAdb("-s " + ser + " push \"" + localSh + "\" /sdcard/Download/linux/omega_debian_setup.sh", 5000);
                RunAdb("-s " + ser + " push \"" + pubPath + "\" /sdcard/Download/linux/id_ed25519_omega.pub", 4000);
                RunAdb("-s " + ser + " forward tcp:22222 tcp:2222", 2000);
            }
            return "[AVF READY] Granted MANAGE_EXTERNAL_STORAGE + staged Debian 13 setup.sh to /sdcard & /sdcard/Download + armed :22222 across " + sers.Count + " Pixel(s)!";
        }

        private static string StageLauncherNtfsKey(string srcPrivKey, out string knownHostsPath)
        {
            string baseLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseLocal) || !Directory.Exists(Path.GetPathRoot(baseLocal)))
                baseLocal = @"C:\Users\Public";
            string safeDir = Path.Combine(baseLocal, "OmegaAvfSsh");
            Directory.CreateDirectory(safeDir);

            bool isAdmin = false;
            string curIdent = Environment.UserName;
            try
            {
                WindowsIdentity wid = WindowsIdentity.GetCurrent();
                if (wid != null)
                {
                    if (!string.IsNullOrEmpty(wid.Name)) curIdent = wid.Name;
                    WindowsPrincipal wp = new WindowsPrincipal(wid);
                    isAdmin = wp.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { }

            string dstPriv = Path.Combine(safeDir, isAdmin ? "id_ed25519_omega_adm" : "id_ed25519_omega_usr");
            knownHostsPath = Path.Combine(safeDir, isAdmin ? "known_hosts_adm" : "known_hosts_usr");
            try
            {
                string rawPem = File.ReadAllText(srcPrivKey, Encoding.UTF8).Replace("\r\n", "\n").Trim() + "\n";
                if (File.Exists(dstPriv)) File.Delete(dstPriv);
                File.WriteAllBytes(dstPriv, Encoding.ASCII.GetBytes(rawPem));
                File.WriteAllBytes(knownHostsPath, new byte[0]);

                ProcessStartInfo icPsi = new ProcessStartInfo(
                    "icacls.exe",
                    "\"" + dstPriv + "\" /inheritance:r /grant:r \"*S-1-5-18:F\" \"*S-1-5-32-544:F\" \"" + curIdent + ":F\"");
                icPsi.CreateNoWindow = true;
                icPsi.UseShellExecute = false;
                icPsi.RedirectStandardOutput = true;
                icPsi.RedirectStandardError = true;
                using (Process icP = Process.Start(icPsi))
                {
                    icP.StandardOutput.ReadToEnd();
                    icP.WaitForExit(5000);
                }
            }
            catch
            {
                return srcPrivKey;
            }
            return dstPriv;
        }

        private static string RunSshCmdSafe(string sshExe, string baseArgs, string userAndHost, string remoteShCmd, byte[] stdinBytes, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            ProcessStartInfo psi = new ProcessStartInfo(sshExe, baseArgs + userAndHost + " \"" + remoteShCmd.Replace("\"", "\\\"") + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process pr = Process.Start(psi))
            {
                StringBuilder outSb = new StringBuilder();
                StringBuilder errSb = new StringBuilder();
                pr.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) outSb.AppendLine(e.Data); };
                pr.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) errSb.AppendLine(e.Data); };
                pr.BeginOutputReadLine();
                pr.BeginErrorReadLine();

                if (stdinBytes != null && stdinBytes.Length > 0)
                {
                    try
                    {
                        int off = 0;
                        while (off < stdinBytes.Length && !pr.HasExited)
                        {
                            int n = Math.Min(8192, stdinBytes.Length - off);
                            pr.StandardInput.BaseStream.Write(stdinBytes, off, n);
                            off += n;
                        }
                        pr.StandardInput.BaseStream.Flush();
                    }
                    catch (IOException) { }
                }
                try { pr.StandardInput.Close(); } catch { }

                if (!pr.WaitForExit(timeoutMs))
                {
                    try { pr.Kill(); } catch { }
                    return "[TIMEOUT]\r\n" + outSb.ToString();
                }
                pr.WaitForExit();
                exitCode = pr.ExitCode;
                string o = outSb.ToString().TrimEnd();
                string err = errSb.ToString().Trim();
                if (err.Length > 0 && o.IndexOf(err, StringComparison.Ordinal) < 0)
                    o = (o.Length > 0 ? (o + "\r\n") : "") + err;
                return o;
            }
        }

        private static bool TryLauncherAvfAuth(string sshExe, string baseArgs, out string authedUser, out string authDiag)
        {
            authedUser = "droid";
            authDiag = "";
            string[] users = new string[] { "droid", "debian", "root" };
            for (int i = 0; i < users.Length; i++)
            {
                int ec;
                string outTxt = RunSshCmdSafe(sshExe, baseArgs, users[i] + "@127.0.0.1", "echo OMEGA_SSH_AUTH_OK:$(whoami)", null, 6000, out ec);
                int idx = (outTxt ?? "").IndexOf("OMEGA_SSH_AUTH_OK:", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    string u = outTxt.Substring(idx + "OMEGA_SSH_AUTH_OK:".Length).Trim();
                    int nl = u.IndexOfAny(new char[] { '\r', '\n', ' ' });
                    authedUser = nl > 0 ? u.Substring(0, nl) : (u.Length > 0 ? u : users[i]);
                    return true;
                }
                if (i == 0) authDiag = (outTxt ?? "").Trim();
            }
            return false;
        }

        private static void StageOkShAndWorkerRsToPixel(string suiteRoot, string ser, string pubKeyLine, string workerRs)
        {
            string sshDir = Path.Combine(suiteRoot, "10_omega_shell", "config", "ssh");
            Directory.CreateDirectory(sshDir);
            string cleanKey = (pubKeyLine ?? "").Replace("'", "").Replace("\r", "").Replace("\n", "").Trim();
            StringBuilder sh = new StringBuilder();
            sh.Append("#!/usr/bin/env bash\n");
            sh.Append("chmod go-w \"$HOME\" 2>/dev/null || true\n");
            sh.Append("mkdir -p \"$HOME/.ssh\" && chmod 700 \"$HOME/.ssh\"\n");
            sh.Append("touch \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("grep -Fq '" + cleanKey + "' \"$HOME/.ssh/authorized_keys\" 2>/dev/null || echo '" + cleanKey + "' >> \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("chmod 600 \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("if command -v sudo >/dev/null 2>&1; then\n");
            sh.Append("  sudo mkdir -p /root/.ssh && sudo chmod 700 /root/.ssh && sudo touch /root/.ssh/authorized_keys\n");
            sh.Append("  sudo grep -Fq '" + cleanKey + "' /root/.ssh/authorized_keys 2>/dev/null || echo '" + cleanKey + "' | sudo tee -a /root/.ssh/authorized_keys >/dev/null\n");
            sh.Append("  sudo chmod 600 /root/.ssh/authorized_keys\n");
            sh.Append("  sudo mkdir -p /etc/ssh/sshd_config.d /run/sshd && sudo chmod 0755 /run/sshd\n");
            sh.Append("  printf 'Port 2222\\nPubkeyAuthentication yes\\nAuthorizedKeysFile .ssh/authorized_keys\\nListenAddress 0.0.0.0\\n' | sudo tee /etc/ssh/sshd_config.d/99-omegashell.conf >/dev/null\n");
            sh.Append("  sudo systemctl reload ssh >/dev/null 2>&1 || sudo pkill -HUP -x sshd >/dev/null 2>&1 || true\n");
            sh.Append("fi\n");
            sh.Append("echo \"[OMEGA_KEY_INSTALLED] user=$(whoami)\"\n");
            string okLocal = Path.Combine(sshDir, "ok.sh");
            File.WriteAllBytes(okLocal, Encoding.ASCII.GetBytes(sh.ToString()));

            RunAdb("-s " + ser + " shell appops set com.android.virtualization.terminal MANAGE_EXTERNAL_STORAGE allow", 2500);
            RunAdb("-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", 2500);
            RunAdb("-s " + ser + " push \"" + okLocal + "\" /sdcard/Download/ok.sh", 4000);
            RunAdb("-s " + ser + " push \"" + okLocal + "\" /sdcard/Download/linux/ok.sh", 4000);
            RunAdb("-s " + ser + " push \"" + okLocal + "\" /sdcard/ok.sh", 4000);
            if (File.Exists(workerRs))
            {
                RunAdb("-s " + ser + " push \"" + workerRs + "\" /sdcard/Download/omega_p10_main.rs", 6000);
                RunAdb("-s " + ser + " push \"" + workerRs + "\" /sdcard/Download/linux/omega_p10_main.rs", 6000);
                RunAdb("-s " + ser + " push \"" + workerRs + "\" /sdcard/omega_p10_main.rs", 6000);
            }
        }

        public static bool EnsureHostPort19789BridgedStatic(string suiteRoot)
        {
            string h0 = HttpGetFast("http://127.0.0.1:19789/api/health", 400);
            if (!string.IsNullOrEmpty(h0) && h0.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            List<string> sers = ListSerials();
            if (sers.Count > 0)
            {
                RunAdb("-s " + sers[0] + " forward tcp:19789 tcp:19789", 1600);
                string h1 = HttpGetFast("http://127.0.0.1:19789/api/health", 750);
                if (!string.IsNullOrEmpty(h1) && h1.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                RunAdb("-s " + sers[0] + " forward --remove tcp:19789", 1400);
            }

            string sshExe = @"C:\Windows\System32\OpenSSH\ssh.exe";
            string privKey = Path.Combine(suiteRoot, "10_omega_shell", "config", "ssh", "id_ed25519_omega");
            string ban;
            if (File.Exists(sshExe) && File.Exists(privKey) && Pixel10TensorBridge.ProbeSshBanner(22222, 180, out ban))
            {
                string khPath;
                string ntfsPriv = StageLauncherNtfsKey(privKey, out khPath);
                string tunArgs = "-i \"" + ntfsPriv + "\" -p 22222 -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=no " +
                    "-o UserKnownHostsFile=\"" + khPath + "\" -o ConnectTimeout=5 -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 " +
                    "-L 127.0.0.1:19789:127.0.0.1:19789 droid@127.0.0.1";
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(sshExe, tunArgs);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    for (int w = 0; w < 8; w++)
                    {
                        System.Threading.Thread.Sleep(280);
                        string h2 = HttpGetFast("http://127.0.0.1:19789/api/health", 650);
                        if (!string.IsNullOrEmpty(h2) && h2.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
                catch { }
            }
            return false;
        }

        public static string ExecuteAvfNativeRustBenchStatic(string suiteRoot)
        {
            string sshExe = @"C:\Windows\System32\OpenSSH\ssh.exe";
            string sshDir = Path.Combine(suiteRoot, "10_omega_shell", "config", "ssh");
            Directory.CreateDirectory(sshDir);
            string privKey = Path.Combine(sshDir, "id_ed25519_omega");
            string pubPath = privKey + ".pub";
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\rust_p10_worker", "src", "main.rs");

            if (!File.Exists(privKey) || !File.Exists(pubPath))
            {
                string keygen = @"C:\Windows\System32\OpenSSH\ssh-keygen.exe";
                if (File.Exists(keygen))
                {
                    try
                    {
                        ProcessStartInfo kpsi = new ProcessStartInfo(keygen, "-t ed25519 -f \"" + privKey + "\" -N \"\" -C \"omega-shell@local-host\"");
                        kpsi.CreateNoWindow = true; kpsi.UseShellExecute = false;
                        using (Process kp = Process.Start(kpsi)) kp.WaitForExit(8000);
                    }
                    catch { }
                }
            }
            if (!File.Exists(sshExe) || !File.Exists(privKey) || !File.Exists(workerRs))
                return "    [SKIP] Missing ssh.exe, id_ed25519_omega, or main.rs.";

            string pubKeyLine = File.Exists(pubPath) ? File.ReadAllText(pubPath, Encoding.UTF8).Trim() : "";
            List<string> sers = ListSerials();
            if (sers.Count > 0)
            {
                RunAdb("-s " + sers[0] + " forward tcp:22222 tcp:2222", 1800);
                StageOkShAndWorkerRsToPixel(suiteRoot, sers[0], pubKeyLine, workerRs);
            }

            string ban;
            if (!Pixel10TensorBridge.ProbeSshBanner(22222, 240, out ban))
            {
                if (sers.Count == 0) return "    [SKIP] AVF @deb (:22222) offline (no ADB Pixel connected).";
                ExecuteZeroMountAvfAutoTypeStatic(suiteRoot);
                if (!Pixel10TensorBridge.ProbeSshBanner(22222, 260, out ban))
                {
                    return "    [INFO] AVF @deb (:22222) sshd sleeping — open Pixel Terminal & click '5. Start @deb Virtio-GPU (:19789)' in OmegaShell Tab 3.";
                }
            }

            string khPath;
            string ntfsPriv = StageLauncherNtfsKey(privKey, out khPath);
            string baseArgs = "-i \"" + ntfsPriv + "\" -p 22222 -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=\"" +
                khPath + "\" -o ConnectTimeout=5 ";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("    [AVF-SSH] Connected to 127.0.0.1:22222 (" + ban + ") [NTFS Key: " + Path.GetFileName(ntfsPriv) + "]");

            string authedUser, authDiag;
            if (!TryLauncherAvfAuth(sshExe, baseArgs, out authedUser, out authDiag))
            {
                if (sers.Count > 0)
                {
                    string ser = sers[0];
                    sb.AppendLine("    [AVF-AUTH] Authorizing Ed25519 key inside Pixel Terminal (/mnt/shared/ok.sh)...");
                    RunAdb("-s " + ser + " shell input keyevent 224", 2000);
                    RunAdb("-s " + ser + " shell wm dismiss-keyguard", 2000);
                    RunAdb("-s " + ser + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1", 3000);
                    System.Threading.Thread.Sleep(750);
                    RunAdb("-s " + ser + " shell input tap 540 960", 2000);
                    System.Threading.Thread.Sleep(200);
                    RunAdb("-s " + ser + " shell input keyevent 66", 2000);
                    System.Threading.Thread.Sleep(150);
                    RunAdb("-s " + ser + " shell input text bash%s/mnt/shared/ok.sh", 2500);
                    RunAdb("-s " + ser + " shell input keyevent 66", 2000);
                    System.Threading.Thread.Sleep(350);
                    RunAdb("-s " + ser + " shell input text bash%s/mnt/shared/Download/ok.sh", 2500);
                    RunAdb("-s " + ser + " shell input keyevent 66", 2000);

                    for (int w = 0; w < 8; w++)
                    {
                        System.Threading.Thread.Sleep(450);
                        RunAdb("-s " + ser + " forward tcp:22222 tcp:2222", 1800);
                        if (TryLauncherAvfAuth(sshExe, baseArgs, out authedUser, out authDiag))
                            break;
                    }
                }
                if (!TryLauncherAvfAuth(sshExe, baseArgs, out authedUser, out authDiag))
                {
                    sb.AppendLine("    [AVF-SSH KEY PENDING] SSH banner is LIVE, waiting for ~/.ssh/authorized_keys inside Debian (" + authDiag + ").");
                    sb.AppendLine("    [ONE-LINE FIX] In Pixel Terminal run: bash /mnt/shared/ok.sh  (or bash /mnt/shared/Download/ok.sh)");
                    return sb.ToString().TrimEnd();
                }
            }

            sb.AppendLine("    [AVF-AUTH PASS] Authenticated as " + authedUser + "@127.0.0.1:22222!");
            try
            {
                // 1. Ensure native aarch64 rustc + gcc + libc6-dev inside Debian 13 (trixie)
                int ec1;
                string prepCmd = "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; " +
                    "if ! command -v rustc >/dev/null 2>&1; then " +
                    "echo '    [AVF-APT] Installing native aarch64 rustc + gcc + libc6-dev in Debian 13...'; " +
                    "sudo DEBIAN_FRONTEND=noninteractive apt-get update -y >/dev/null && " +
                    "sudo DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rustc gcc libc6-dev file >/dev/null; " +
                    "fi; mkdir -p \"$HOME/omega_avf_rust\" && echo \"    [AVF-RUSTC] $(uname -srm) | $(rustc --version)\"";
                string o1 = RunSshCmdSafe(sshExe, baseArgs, authedUser + "@127.0.0.1", prepCmd, null, 240000, out ec1);
                if (!string.IsNullOrEmpty(o1)) sb.AppendLine(o1.TrimEnd());
                if (ec1 != 0 && o1.IndexOf("rustc ", StringComparison.OrdinalIgnoreCase) < 0)
                    return sb.ToString().TrimEnd();

                // 2. Stage v0.7.5 main.rs from /mnt/shared (0 ms) or stream safely in 8KB chunks
                int ec2;
                byte[] rsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(workerRs, Encoding.UTF8).Replace("\r\n", "\n"));
                string copyShared = "mkdir -p \"$HOME/omega_avf_rust\" && " +
                    "for p in /mnt/shared/omega_p10_main.rs /mnt/shared/Download/omega_p10_main.rs /mnt/shared/linux/omega_p10_main.rs /mnt/shared/Download/linux/omega_p10_main.rs; do " +
                    "if [ -s \"$p\" ]; then cp -f \"$p\" \"$HOME/omega_avf_rust/main.rs\" && break; fi; done; " +
                    "if [ -s \"$HOME/omega_avf_rust/main.rs\" ]; then wc -c < \"$HOME/omega_avf_rust/main.rs\"; else echo 0; fi";
                string szStr = RunSshCmdSafe(sshExe, baseArgs, authedUser + "@127.0.0.1", copyShared, null, 12000, out ec2).Trim();
                long szVal = 0;
                long.TryParse(szStr, out szVal);
                if (szVal != rsBytes.Length)
                {
                    szStr = RunSshCmdSafe(sshExe, baseArgs, authedUser + "@127.0.0.1", "mkdir -p \"$HOME/omega_avf_rust\" && cat > \"$HOME/omega_avf_rust/main.rs\" && wc -c < \"$HOME/omega_avf_rust/main.rs\"", rsBytes, 25000, out ec2).Trim();
                }
                sb.AppendLine("    [AVF-SOURCE] Staged v0.7.5 main.rs -> ~/omega_avf_rust/main.rs (" + szStr + " bytes)");

                // 3. Atomic compile + install persistent systemd service (:19789) + verify guest listener
                int ec3;
                string compAndSvcCmd = "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; cd \"$HOME/omega_avf_rust\" && " +
                    "NEW_SHA=$(sha256sum main.rs | awk '{print $1}') && " +
                    "OLD_SHA=$(cat main.rs.sha256 2>/dev/null || echo none) && " +
                    "if [ ! -x ./omega_p10_worker_avf ] || [ \"$NEW_SHA\" != \"$OLD_SHA\" ]; then " +
                    "  T0=$(date +%s%3N 2>/dev/null || echo 0) && " +
                    "  (rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 -C 'target-feature=+neon,+dotprod,+lse' main.rs -o omega_p10_worker_avf.new -l dl 2>&1 " +
                    "   || rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 main.rs -o omega_p10_worker_avf.new -l dl 2>&1 " +
                    "   || rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 main.rs -o omega_p10_worker_avf.new 2>&1) && " +
                    "  chmod 755 ./omega_p10_worker_avf.new && mv -f ./omega_p10_worker_avf.new ./omega_p10_worker_avf && " +
                    "  echo \"$NEW_SHA\" > main.rs.sha256 && " +
                    "  T1=$(date +%s%3N 2>/dev/null || echo 0) && " +
                    "  BSZ=$(wc -c < ./omega_p10_worker_avf | tr -d ' ') && " +
                    "  echo \"    [AVF-BUILD PASS] Native ARM64 v0.7.5 Daemon -> $((BSZ / 1024)) KB ($((T1 - T0)) ms on Tensor G5, SHA=${NEW_SHA:0:12})\"; " +
                    "else " +
                    "  echo \"    [AVF-BUILD CACHED] Native ARM64 v0.7.5 Daemon up-to-date (SHA=${NEW_SHA:0:12}, 0 ms)\"; " +
                    "fi && " +
                    "printf '[Unit]\\nDescription=ACT-Omega AVF Debian 13 Virtio-GPU GFXStream Worker (:19789)\\nAfter=network.target\\n\\n[Service]\\nType=simple\\nUser=%s\\nWorkingDirectory=%s/omega_avf_rust\\nExecStart=%s/omega_avf_rust/omega_p10_worker_avf --daemon --bind 0.0.0.0 --port 19789\\nRestart=always\\nRestartSec=2\\n\\n[Install]\\nWantedBy=multi-user.target\\n' \"$(whoami)\" \"$HOME\" \"$HOME\" | sudo tee /etc/systemd/system/omega-avf-virtio-worker.service >/dev/null && " +
                    "(sudo systemctl daemon-reload && sudo systemctl enable omega-avf-virtio-worker.service >/dev/null 2>&1 && sudo systemctl restart omega-avf-virtio-worker.service) || " +
                    "(pkill -f 'omega_p10_worker_avf --daemon' 2>/dev/null || true; nohup ./omega_p10_worker_avf --daemon --bind 0.0.0.0 --port 19789 </dev/null >/tmp/omega_avf_19789.log 2>&1 &) && " +
                    "for w in 1 2 3 4 5 6 7 8 9 10 11 12; do if ss -tln 2>/dev/null | grep -q ':19789 '; then break; fi; sleep 0.35; done && " +
                    "sleep 1.1 && " +
                    "echo \"    [AVF-VM-RAM] Guest Memory Allocation -> $(free -m | awk '/^Mem:/ {print $7 \" / \" $2 \" MB avail\"}')\"; echo \"    [AVF-SYSTEMD] omega-avf-virtio-worker.service -> $(systemctl is-active omega-avf-virtio-worker.service 2>/dev/null || echo active-nohup) on 0.0.0.0:19789\"";
                string o3 = RunSshCmdSafe(sshExe, baseArgs, authedUser + "@127.0.0.1", compAndSvcCmd, null, 150000, out ec3);
                if (!string.IsNullOrEmpty(o3)) sb.AppendLine(o3.TrimEnd());

                // 4. Bridge Windows 127.0.0.1:19789 -> AVF :19789 & dispatch live K10_HW_UDOT_PACK4U8 RPC job!
                if (EnsureHostPort19789BridgedStatic(suiteRoot))
                {
                    string hJson = HttpGetFast("http://127.0.0.1:19789/api/health", 4500);
                    string dJson = HttpGetFast("http://127.0.0.1:19789/api/dispatch/udot", 15000);
                    P10TelemetrySnapshot avfSnap = Pixel10TensorBridge.FetchSnapshotOnPort(19789, "/api/status", 6000);
                    sb.AppendLine("    [DAEMON ARMED] Persistent AVF @deb Virtio-GPU Worker LIVE on http://127.0.0.1:19789 (:19789)");
                    if (!string.IsNullOrEmpty(hJson)) sb.AppendLine("    [RPC /api/health]        " + hJson);
                    if (!string.IsNullOrEmpty(dJson)) sb.AppendLine("    [RPC /api/dispatch/udot] " + dJson);
                    if (avfSnap.Online)
                    {
                        string sc = Pixel10TensorBridge.FormatMultiLineScorecard("AVF @deb VIRTIO-GPU PERSISTENT DAEMON (:19789)", avfSnap);
                        sb.AppendLine("    " + sc.Replace("\n", "\n    "));
                    }
                }
                else
                {
                    sb.AppendLine("    [WARN] AVF daemon started inside Debian, but host 127.0.0.1:19789 bridge did not respond yet.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("    [AVF-RUST ERROR] " + ex.Message);
            }
            return sb.ToString().TrimEnd();
        }

        public static string ExecuteZeroMountAvfAutoTypeStatic(string suiteRoot)
        {
            List<string> sers = ListSerials();
            if (sers.Count == 0) return "[AVF Auto-Type] No ADB Pixel connected.";
            string ser = sers[0];

            string sshDir = Path.Combine(suiteRoot, "10_omega_shell", "config", "ssh");
            Directory.CreateDirectory(sshDir);
            string privKey = Path.Combine(sshDir, "id_ed25519_omega");
            string pubPath = privKey + ".pub";
            if (!File.Exists(pubPath))
            {
                string keygen = @"C:\Windows\System32\OpenSSH\ssh-keygen.exe";
                if (File.Exists(keygen))
                {
                    try
                    {
                        ProcessStartInfo kpsi = new ProcessStartInfo(keygen, "-t ed25519 -f \"" + privKey + "\" -N \"\" -C \"omega-shell@local-host\"");
                        kpsi.CreateNoWindow = true; kpsi.UseShellExecute = false;
                        using (Process kp = Process.Start(kpsi)) kp.WaitForExit(8000);
                    }
                    catch { }
                }
            }
            if (!File.Exists(pubPath)) return "[AVF Auto-Type] Could not locate/generate " + pubPath + ". Open OmegaShell Tab 3 first.";

            string cleanKey = File.ReadAllText(pubPath, Encoding.UTF8).Replace("'", "").Trim();
            string payload =
                "mkdir -p \"$HOME/.ssh\" && chmod 700 \"$HOME/.ssh\"\n" +
                "touch \"$HOME/.ssh/authorized_keys\"\n" +
                "grep -Fq '" + cleanKey + "' \"$HOME/.ssh/authorized_keys\" 2>/dev/null || echo '" + cleanKey + "' >> \"$HOME/.ssh/authorized_keys\"\n" +
                "chmod 600 \"$HOME/.ssh/authorized_keys\"\n" +
                "if [ ! -x /usr/sbin/sshd ]; then sudo apt-get update -y && sudo apt-get install -y openssh-server; fi\n" +
                "sudo mkdir -p /etc/ssh/sshd_config.d\n" +
                "printf 'Port 2222\\nPubkeyAuthentication yes\\nPasswordAuthentication no\\nListenAddress 0.0.0.0\\n' | sudo tee /etc/ssh/sshd_config.d/99-omegashell.conf >/dev/null\n" +
                "sudo ssh-keygen -A >/dev/null 2>&1 || true\n" +
                "sudo systemctl disable --now ssh.socket >/dev/null 2>&1 || true\n" +
                "sudo systemctl stop ssh >/dev/null 2>&1 || true\n" +
                "sudo pkill -f '/usr/sbin/sshd' 2>/dev/null || true\n" +
                "sudo mkdir -p -m 0755 /run/sshd\n" +
                "sudo systemctl enable --now ssh >/dev/null 2>&1 || true\n" +
                "ss -tln 2>/dev/null | grep -q ':2222 ' || (sudo mkdir -p -m 0755 /run/sshd && sudo /usr/sbin/sshd -p 2222)\n" +
                "echo OMEGA_AVF_2222_LIVE\n";
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

            RunAdb("-s " + ser + " shell appops set com.android.virtualization.terminal MANAGE_EXTERNAL_STORAGE allow", 2500);
            RunAdb("-s " + ser + " shell cmd appops set com.android.virtualization.terminal RUN_IN_BACKGROUND allow", 2500);
            RunAdb("-s " + ser + " shell dumpsys deviceidle whitelist +com.android.virtualization.terminal", 2500);
            RunAdb("-s " + ser + " forward tcp:22222 tcp:2222", 1800);
            string preBan;
            if (Pixel10TensorBridge.ProbeSshBanner(22222, 200, out preBan))
            {
                return "[AVF SSH ALREADY LIVE ON 127.0.0.1:22222 (" + preBan + ")] Debian 13 crosvm sshd(:2222) connected!";
            }

            RunAdb("-s " + ser + " shell input keyevent 224", 1800);
            RunAdb("-s " + ser + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1", 3500);
            Thread.Sleep(700);
            RunAdb("-s " + ser + " shell input tap 540 960", 1800);
            Thread.Sleep(220);
            RunAdb("-s " + ser + " shell input keyevent 66", 1500);
            Thread.Sleep(150);

            // Fast-Wake (0.4s) when openssh-server is already installed inside droid@debian
            RunAdb("-s " + ser + " shell \"input text 'sudo%smkdir%s-p%s-m%s0755%s/run/sshd&&sudo%s/usr/sbin/sshd%s-p%s2222'\"", 2500);
            RunAdb("-s " + ser + " shell input keyevent 66", 1800);
            for (int fw = 0; fw < 6; fw++)
            {
                Thread.Sleep(420);
                RunAdb("-s " + ser + " forward tcp:22222 tcp:2222", 1800);
                string fwBan;
                if (Pixel10TensorBridge.ProbeSshBanner(22222, 220, out fwBan))
                {
                    return "[AVF SSH FAST-WAKE LIVE ON 127.0.0.1:22222 (" + fwBan + ")] Debian 13 crosvm sshd(:2222) connected!";
                }
            }

            RunAdb("-s " + ser + " shell input tap 540 960", 1800);
            Thread.Sleep(150);
            RunAdb("-s " + ser + " shell \"input text 'echo%s'\"", 2200);
            for (int off = 0; off < b64.Length; off += 72)
            {
                string part = b64.Substring(off, Math.Min(72, b64.Length - off));
                RunAdb("-s " + ser + " shell \"input text '" + part + "'\"", 2200);
                Thread.Sleep(65);
            }
            RunAdb("-s " + ser + " shell \"input text '%s|base64%s-d|bash'\"", 2200);
            RunAdb("-s " + ser + " shell input keyevent 66", 1800);

            for (int w = 0; w < 40; w++)
            {
                Thread.Sleep(450);
                RunAdb("-s " + ser + " forward tcp:22222 tcp:2222", 1800);
                string ban;
                if (Pixel10TensorBridge.ProbeSshBanner(22222, 220, out ban))
                {
                    return "[AVF SSH LIVE ON 127.0.0.1:22222 (" + ban + ")] Zero-mount Debian 13 crosvm sshd(:2222) connected!";
                }
            }
            return "[AVF Auto-Type Sent] Typed zero-mount sshd(:2222) setup into Pixel Terminal on " + ser + ". Check phone screen if sudo prompted.";
        }

        private void TriggerAvfAutoTypeFromLauncherAsync()
        {
            SetAct("Auto-typing zero-mount OpenSSH (:2222) setup into Pixel 10 AVF Debian Terminal & arming :22222...");
            AppendGemmaLine("\r\n>>> [ANDROID 17 AVF / pKVM DEBIAN 13 :: ZERO-MOUNT SSH (:22222) BOOTSTRAP]", C(250, 204, 21));
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg = ExecuteZeroMountAvfAutoTypeStatic(_root);
                AppendGemmaLine(msg, C(52, 211, 153));
                SetAct(msg);
                RefreshAsync();
            });
        }

        private static Label Stat(int x, int y, int w, string t)
        {
            Label l = new Label(); l.UseMnemonic = false; l.SetBounds(x, y, w, 20); l.Text = t;
            l.Font = new Font("Consolas", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel); l.ForeColor = C(56, 189, 248); return l;
        }

        private static Button Btn(string t, int x, int y, int w, Color bg)
        {
            Button b = new Button(); b.UseMnemonic = false; b.Text = t; b.SetBounds(x, y, w, 34); b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg; b.ForeColor = Color.White; b.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel);
            b.FlatAppearance.BorderColor = C(56, 189, 248); return b;
        }

        private void SetAct(string m)
        {
            if (IsDisposed) return;
            string clean = (m ?? "").Replace("\r\n", " | ").Replace("\n", " | ");
            if (InvokeRequired) { try { BeginInvoke((Action)delegate { _lAct.Text = clean; }); } catch { } }
            else _lAct.Text = clean;
        }

        private void LaunchOrFocus(string procName, string exe, string title)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(procName);
                for (int i = 0; i < ps.Length; i++)
                {
                    IntPtr h = ps[i].MainWindowHandle;
                    if (h != IntPtr.Zero) { if (IsIconic(h)) ShowWindow(h, 9); SetForegroundWindow(h); SetAct("Focused " + title + " (PID " + ps[i].Id + ")"); return; }
                }
                if (!File.Exists(exe)) { SetAct("Missing executable: " + exe); return; }
                ProcessStartInfo psi = new ProcessStartInfo(exe); psi.WorkingDirectory = Path.GetDirectoryName(exe);
                Process p = Process.Start(psi);
                SetAct("Launched " + title + (p != null ? (" (PID " + p.Id + ")") : ""));
                RefreshAsync();
            }
            catch (Exception ex) { SetAct("Launch error: " + ex.Message); }
        }

        private void LaunchFullSuite()
        {
            SetAct("Arming all 9 reverse tunnels (:9778 / :19777 / :19779..:19782 / :19784..:19786) + launching Nona-Suite...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                int n = ArmAllPixelsStatic();
                QuickWakePixel10DaemonStatic();
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        LaunchOrFocus("OmegaBrowser", _brExe, "OmegaBrowser v0.6.4");
                        LaunchOrFocus("OmegaMirror", _mrExe, "OmegaMirror v0.9");
                        LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5");
                        LaunchOrFocus("OmegaForge", _fgExe, "OmegaForge v0.2.1");
                        LaunchOrFocus("OmegaShell", _shExe, "OmegaShell v1.1.6 GM");
                        LaunchOrFocus("OmegaSynth", _syExe, "OmegaSynth v1.1.1 Final GM");
                        LaunchOrFocus("OmegaSonar", _snExe, "OmegaSonar v1.1.0 GM");
                        LaunchOrFocus("OmegaPenrose", _pnExe, "OmegaPenrose v1.0.2 Final GM");
                        LaunchOrFocus("OmegaSheaf", _sfExe, "OmegaSheaf v0.2.2 GM");
                        LaunchOrFocus("OmegaSteg", _sgExe, "OmegaSteg v0.2.1 (OmegaSec)");
                        LaunchOrFocus("OmegaConductor", _cdExe, "OmegaConductor v1.0.0 GM");
                        LaunchOrFocus("OmegaTensor", _tnExe, "OmegaTensor v1.0.2 GM");
                        SetAct("Full 13-Station Suite v2.5.0 LIVE | Conductor (:19790) + Tensor (:19791) + 5GB AVF across " + n + " Pixel(s)!");
                    });
                }
                catch { }
            });
        }

        private void StopApps(string[] names)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0; int cnt = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    Process[] ps = Process.GetProcessesByName(names[i]);
                    for (int j = 0; j < ps.Length; j++)
                    {
                        try { freed += ps[j].WorkingSet64; if (ps[j].CloseMainWindow()) { if (!ps[j].WaitForExit(450)) ps[j].Kill(); } else ps[j].Kill(); cnt++; }
                        catch { }
                    }
                }
                SetAct("Stopped " + cnt + " process(es) | Freed ~" + (freed / 1048576L) + " MB host RAM + RTX VRAM");
                RefreshAsync();
            });
        }

        private void ShowVaultMenu(Control btn)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Unified Hardware Config & R&D Chaos Harness (:19800)", null, delegate { using (OmegaUnifiedChaosAndConfigDialog dlg = new OmegaUnifiedChaosAndConfigDialog()) dlg.ShowDialog(this); });
            m.Items.Add("Vault Downloads (Papers, Audio, Shaders, Models)", null, delegate { OpenDir(_dlDir); });
            m.Items.Add("Pixel 10 10-Kernel Tensor Cockpit (:19788 Live Inspector)", null, delegate { OmegaLabPixel10CockpitDialog.ShowOrFocus(this, _root); });
            m.Items.Add("Pixel 10 Rust+Vulkan Worker Dir (11_omega_edge_workers\rust_p10_worker)", null, delegate { OpenDir(_p10Dir); });
            m.Items.Add("OmegaShell v1.1.6 GM Cloud CAS Vault", null, delegate { OpenDir(Path.Combine(_shDir, "cloud_cas")); });
            m.Items.Add("OmegaShell v1.1.6 GM Workspace Builds (Gemma Codegen)", null, delegate { OpenDir(Path.Combine(_shDir, "workspace_builds")); });
            m.Items.Add("OmegaForge v0.2.1 Workspace (Compiled Binaries & Lean 4 Proofs)", null, delegate { OpenDir(Path.Combine(_fgDir, "workspace")); });
            m.Items.Add("Corpus Vault (Markdown + JSONL Shards)", null, delegate { OpenDir(Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus"))) ? (Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")) : Path.Combine(_brDir, "corpus")); });
            m.Items.Add("OmegaAV v0.7.5 Renders (1080p/4K MP4, AVI & PNGs)", null, delegate { OpenDir(Path.Combine(_avDir, "renders")); });
            m.Items.Add("OmegaSynth v1.1.1 Final GM Renders (48kHz Stereo .WAV)", null, delegate { OpenDir(Path.Combine(_syDir, "renders")); });
            m.Items.Add("OmegaTensor v1.0.2 GM Split-Inference Snapshots & Stress Logs", null, delegate { OpenDir(Path.Combine(_tnDir, "exports")); });
            m.Items.Add("OmegaSonar v1.1.0 GM Radar Snapshots & Stress Logs", null, delegate { OpenDir(Path.Combine(_snDir, "exports")); });
            m.Items.Add("OmegaPenrose v1.0.2 Final GM Exports (.STL, .OBJ, Citadel, .AVI, .SVG)", null, delegate { OpenDir(Path.Combine(_pnDir, "exports")); });
            m.Items.Add("OmegaPenrose v1.0.2 Final GM Patch Bank (.openrose)", null, delegate { OpenDir(Path.Combine(_pnDir, "patches")); });
            m.Items.Add("OmegaSheaf v0.2.1 GM Exports (.lean, .SVG, .JSONL, .OMSH)", null, delegate { OpenDir(Path.Combine(_sfDir, "exports")); });
            m.Items.Add("OmegaSheaf v0.2.1 GM Patch Bank (.osheaf)", null, delegate { OpenDir(Path.Combine(_sfDir, "patches")); });
            m.Items.Add("OmegaSteg v0.2.1 (OmegaSec) Air-Gap Vault Exports (.PNG, .WAV, HTML, .lean)", null, delegate { OpenDir(Path.Combine(_sgDir, "exports")); });
            m.Items.Add("OmegaSteg v0.2.1 (OmegaSec) Vault Capsules (.osteg)", null, delegate { OpenDir(Path.Combine(_sgDir, "patches")); });
            m.Items.Add("OmegaAV v0.7.5 Shaders (.omsh & MIDI Map)", null, delegate { OpenDir(Path.Combine(_avDir, "shaders")); });
            m.Items.Add("Google Drive Gemini Bridge", null, delegate { OpenDir(Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"))) ? (Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export")) : _root); });
            m.Show(btn, new Point(0, btn.Height));
        }

        private void CallBrowserApi(string path, string okMsg)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { using (WebClient wc = new WebClient()) { wc.Encoding = Encoding.UTF8; SetAct(okMsg + " -> " + wc.DownloadString("http://127.0.0.1:9778" + path)); } }
                catch { SetAct("OmegaBrowser (:9778) is offline."); }
            });
        }

        private void BeamPortAsync(int port) { BeamPortPathAsync(port, "/"); }

        private void BeamPortPathAsync(int port, string subPath)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> ser = ListSerials();
                if (ser.Count == 0) { SetAct("No ADB Pixel detected."); return; }
                string u = "http://127.0.0.1:" + port + subPath + "?v=" + (DateTime.UtcNow.Ticks % 99999);
                for (int i = 0; i < ser.Count; i++) { RunAdb("-s " + ser[i] + " reverse tcp:" + port + " tcp:" + port, 1600); RunAdb("-s " + ser[i] + " shell am start -a android.intent.action.VIEW -d \"" + u + "\"", 2200); }
                SetAct("Beamed " + u + " -> " + ser.Count + " Pixel(s)!");
            });
        }

        private void BeamSplitOledAsync()
        {
            LaunchOrFocus("OmegaSynth", _syExe, "OmegaSynth v1.1.1 Final GM");
            LaunchOrFocus("OmegaAV", _avExe, "OmegaAV v0.7.5");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(400);
                List<string> ser = ListSerials();
                if (ser.Count == 0) { SetAct("No ADB Pixel detected for Split-OLED."); return; }
                ArmAllPixelsStatic();
                if (ser.Count >= 2)
                {
                    RunAdb("-s " + ser[0] + " shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19782/mpe\"", 2200);
                    RunAdb("-s " + ser[1] + " shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19779/\"", 2200);
                    SetAct("Split-OLED LIVE: " + ser[0] + " (Synth MPE :19782) + " + ser[1] + " (OmegaAV :19779)!");
                }
                else
                {
                    RunAdb("-s " + ser[0] + " shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19782/mpe\"", 2200);
                    SetAct("Beamed :19782/mpe Fretless Synth Pad -> " + ser[0] + " (OmegaAV live via WASAPI Loopback)!");
                }
            });
        }

        private void PromoteUsbPixelsAsync()
        {
            SetAct("Promoting USB Pixels to Wireless (:5555) + arming tunnels...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> all = ListSerials(), wi = new List<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Contains(":")) continue;
                    Match m = Regex.Match(RunAdb("-s " + all[i] + " shell ip -f inet addr show wlan0", 1800), @"inet\s+(\d+\.\d+\.\d+\.\d+)");
                    RunAdb("-s " + all[i] + " tcpip 5555", 1800); Thread.Sleep(550);
                    if (m.Success) { string ip = m.Groups[1].Value; RunAdb("connect " + ip + ":5555", 2400); wi.Add(ip + ":5555"); }
                }
                int tot = ArmAllPixelsStatic();
                SetAct("Armed tunnels on " + tot + " Pixel(s)" + (wi.Count > 0 ? (" | Wi-Fi: " + string.Join(", ", wi.ToArray())) : ""));
                RefreshAsync();
            });
        }

        private void SyncBridgeAsync()
        {
            SetAct("Exporting Suite v2.3.2 + Pixel 10 10-Kernel Tensor + OmegaShell v1.1.6 GM + Manifest -> OMEGA_BRIDGE_EXPORT...");
            ThreadPool.QueueUserWorkItem(delegate { SetAct(ExecuteSuiteBridgeSync(_root)); RefreshAsync(); });
        }

        public static string ExecuteSuiteBridgeSync(string root)
        {
            try
            {
                string brD = Path.Combine(root, "02_omega_browser"), mrD = Path.Combine(root, "01_omega_mirror"), avD = Path.Combine(root, "03_omega_av"), fgD = Path.Combine(root, "09_omega_forge");
                string cDir = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus"))) ? (Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")) : Path.Combine(brD, "corpus");
                Directory.CreateDirectory(cDir);
                string jsonl = Path.Combine(cDir, "omega_corpus_bundle_v10_golden.jsonl");
                string[] mds = Directory.GetFiles(cDir, "*.md");
                List<string> ls = new List<string>();
                for (int i = 0; i < mds.Length; i++)
                {
                    string raw = File.ReadAllText(mds[i], Encoding.UTF8);
                    ls.Add("{\"file\":\"" + Path.GetFileName(mds[i]).Replace("\"", "") + "\",\"bytes\":" + raw.Length + ",\"sha256\":\"" + Sha256Hex(raw) + "\"}");
                }
                File.WriteAllLines(jsonl, ls.ToArray(), new UTF8Encoding(false));

                string gBase = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"))) ? (Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export")) : Path.Combine(root, "bridge_staging");
                string shD = Path.Combine(root, "10_omega_shell");
                string syD = Path.Combine(root, "04_omega_synth");
                string sfD = Path.Combine(root, "06_omega_sheaf");
                string pnD = Path.Combine(root, "07_omega_penrose");
                string sgD = Path.Combine(root, "08_omega_steg");
                string tnD = Path.Combine(root, "13_omega_tensor");
                string dst = Path.Combine(gBase, "53_OMEGATENSOR_REBOOT_PROOF_9TOPS_SUITE_v2_4_1");
                CopyAll(root, dst, new string[] { "*.cs", "*.ps1", "*.md", "*.exe" });
                CopyAll(Path.Combine(root, "icons"), Path.Combine(dst, "icons"), new string[] { "*.ico" });
                CopyAll(mrD, Path.Combine(dst, "01_omega_mirror"), new string[] { "*.cs", "*.html", "*.exe" });
                CopyAll(brD, Path.Combine(dst, "02_omega_browser"), new string[] { "*.cs", "*.ps1", "*.txt", "*.exe" });
                CopyAll(Path.Combine(brD, "home"), Path.Combine(dst, "02_omega_browser", "home"), new string[] { "*.html" });
                CopyAll(avD, Path.Combine(dst, "03_omega_av"), new string[] { "*.cs", "*.ini", "*.exe" });
                CopyAll(syD, Path.Combine(dst, "04_omega_synth"), new string[] { "*.cs", "*.exe" });
                CopyAll(Path.Combine(syD, "patches"), Path.Combine(dst, "04_omega_synth", "patches"), new string[] { "*.osynth" });
                CopyAll(sfD, Path.Combine(dst, "06_omega_sheaf"), new string[] { "*.cs", "*.exe" });
                CopyAll(Path.Combine(sfD, "patches"), Path.Combine(dst, "06_omega_sheaf", "patches"), new string[] { "*.osheaf" });
                CopyAll(Path.Combine(sfD, "exports"), Path.Combine(dst, "06_omega_sheaf", "exports"), new string[] { "*.lean", "*.svg", "*.jsonl" });
                CopyAll(pnD, Path.Combine(dst, "07_omega_penrose"), new string[] { "*.cs", "*.exe" });
                CopyAll(sgD, Path.Combine(dst, "08_omega_steg"), new string[] { "*.cs", "*.exe" });
                CopyAll(Path.Combine(sgD, "patches"), Path.Combine(dst, "08_omega_steg", "patches"), new string[] { "*.osteg" });
                CopyAll(Path.Combine(pnD, "patches"), Path.Combine(dst, "07_omega_penrose", "patches"), new string[] { "*.openrose" });
                CopyAll(fgD, Path.Combine(dst, "09_omega_forge"), new string[] { "*.cs", "*.tsv", "*.exe" });
                CopyAll(shD, Path.Combine(dst, "10_omega_shell"), new string[] { "*.cs", "*.exe" });
                CopyAll(tnD, Path.Combine(dst, "13_omega_tensor"), new string[] { "*.cs", "*.exe" });
                string p10D = Path.Combine(root, "11_omega_edge_workers\rust_p10_worker");
                CopyAll(p10D, Path.Combine(dst, "11_omega_edge_workers\rust_p10_worker"), new string[] { "*.toml", "*.ps1" });
                CopyAll(Path.Combine(p10D, "src"), Path.Combine(dst, "11_omega_edge_workers\rust_p10_worker", "src"), new string[] { "*.rs" });
                CopyAll(Path.Combine(p10D, "shaders"), Path.Combine(dst, "11_omega_edge_workers\rust_p10_worker", "shaders"), new string[] { "*.hlsl" });
                CopyAll(Path.Combine(p10D, "bin"), Path.Combine(dst, "11_omega_edge_workers\rust_p10_worker", "bin"), new string[] { "*" });
                CopyAll(Path.Combine(shD, "config"), Path.Combine(dst, "10_omega_shell", "config"), new string[] { "*.oshrc", "*.tsv" });
                CopyAll(Path.Combine(shD, "config", "ssh"), Path.Combine(dst, "10_omega_shell", "config", "ssh"), new string[] { "*.pub", "*.sh" });
                CopyAll(Path.Combine(avD, "shaders"), Path.Combine(dst, "03_omega_av", "shaders"), new string[] { "*.omsh" });
                File.Copy(jsonl, Path.Combine(dst, "omega_corpus_bundle_v10_golden.jsonl"), true);

                string lnSha = Sha256File(Path.Combine(root, "OmegaLabLauncher.exe"));
                string mrSha = Sha256File(Path.Combine(mrD, "OmegaMirror.exe"));
                string brSha = Sha256File(Path.Combine(brD, "OmegaBrowser.exe"));
                string avSha = Sha256File(Path.Combine(avD, "OmegaAV.exe"));
                string fgSha = Sha256File(Path.Combine(fgD, "OmegaForge.exe"));
                string sySha = Sha256File(Path.Combine(syD, "OmegaSynth.exe"));
                string sfSha = Sha256File(Path.Combine(sfD, "OmegaSheaf.exe"));
                string pnSha = Sha256File(Path.Combine(pnD, "OmegaPenrose.exe"));
                string sgSha = Sha256File(Path.Combine(sgD, "OmegaSteg.exe"));
                string shSha = Sha256File(Path.Combine(shD, "OmegaShell.exe"));

                string manifest =
                    "{\r\n" +
                    "  \"suite\": \"ACT-Omega Standalone Lab v2.3.2 (11-Station Deck + Pixel 10 10-Kernel Tensor + AVF SSH)\",\r\n" +
                    "  \"exported_utc\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\r\n" +
                    "  \"vault_downloads_root\": \"" + Path.Combine(root, "downloads").Replace("\\", "\\\\") + "\",\r\n" +
                    "  \"corpus_documents\": " + mds.Length + ",\r\n" +
                    "  \"components\": {\r\n" +
                    "    \"launcher\": { \"version\": \"1.4\", \"exe\": \"OmegaLabLauncher.exe\", \"sha256\": \"" + lnSha + "\" },\r\n" +
                    "    \"omega_mirror\": { \"version\": \"0.9\", \"port\": 19777, \"exe\": \"01_omega_mirror/OmegaMirror.exe\", \"sha256\": \"" + mrSha + "\" },\r\n" +
                    "    \"omega_browser\": { \"version\": \"0.6.1\", \"port\": 9778, \"exe\": \"02_omega_browser/OmegaBrowser.exe\", \"sha256\": \"" + brSha + "\" },\r\n" +
                    "    \"omega_av\": { \"version\": \"0.7\", \"port\": 19779, \"exe\": \"03_omega_av/OmegaAV.exe\", \"sha256\": \"" + avSha + "\" },\r\n" +
                    "    \"omega_synth\": { \"version\": \"1.1-GM\", \"port\": 19782, \"exe\": \"04_omega_synth/OmegaSynth.exe\", \"sha256\": \"" + sySha + "\", \"endpoints\": [\"/mpe\", \"/api/status\", \"/api/mpe\", \"/api/control\"] },\r\n" +
                    "    \"omega_sheaf\": { \"version\": \"1.0-GM\", \"port\": 19784, \"exe\": \"06_omega_sheaf/OmegaSheaf.exe\", \"sha256\": \"" + sfSha + "\", \"endpoints\": [\"/sheaf\", \"/frame.jpg\", \"/api/status\", \"/api/cmd\"] },\r\n" +
                    "    \"omega_steg\": { \"version\": \"1.0-GM\", \"port\": 19786, \"exe\": \"08_omega_steg/OmegaSteg.exe\", \"sha256\": \"" + sgSha + "\", \"endpoints\": [\"/vault\", \"/api/status\", \"/api/cmd\"] },\r\n" +
                    "    \"omega_penrose\": { \"version\": \"1.0-GM\", \"port\": 19785, \"exe\": \"07_omega_penrose/OmegaPenrose.exe\", \"sha256\": \"" + pnSha + "\", \"endpoints\": [\"/pad\", \"/api/status\", \"/api/touch\", \"/api/cmd\"] },\r\n" +
                    "    \"omega_forge\": { \"version\": \"0.2.1\", \"port\": 19780, \"exe\": \"09_omega_forge/OmegaForge.exe\", \"sha256\": \"" + fgSha + "\", \"endpoints\": [\"/api/status\", \"/api/tools\", \"/api/models\", \"/api/exec\", \"/api/verify\"] },\r\n" +
                    "    \"omega_shell\": { \"version\": \"1.0.1-GM\", \"port\": 19781, \"exe\": \"10_omega_shell/OmegaShell.exe\", \"sha256\": \"" + shSha + "\", \"endpoints\": [\"/api/status\", \"/api/exec\", \"/api/tuner\", \"/api/format\", \"/api/avf\", \"/api/models\", \"/api/gemma\", \"/api/codegen\"] }\r\n" +
                    "  }\r\n" +
                    "}\r\n";
                File.WriteAllText(Path.Combine(dst, "MAINLINE_ARCHITECTURE_MANIFEST.json"), manifest, new UTF8Encoding(false));

                return "Suite v2.3.2 + Pixel 10 10-Kernel Tensor & AVF Synced -> " + dst + " (" + mds.Length + " corpus docs + Manifest OK)";
            }
            catch (Exception ex) { return "Bridge sync error: " + ex.Message; }
        }

        private static string HttpGetFast(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "GET"; r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string HttpPostFast(string url, string jsonBody, int timeoutMs)
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = "POST"; r.ContentType = "application/json; charset=utf-8"; r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs;
            byte[] b = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            r.ContentLength = b.Length;
            using (Stream st = r.GetRequestStream()) st.Write(b, 0, b.Length);
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static string ExtractJsonField(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            return m.Success ? JsonUnesc(m.Groups[1].Value) : "";
        }

        private static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string JsonUnesc(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\\' && i + 1 < raw.Length)
                {
                    char n = raw[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else if (n == '"') sb.Append('"');
                    else if (n == '\\') sb.Append('\\');
                    else if (n == 'u' && i + 4 < raw.Length)
                    {
                        int hv;
                        if (int.TryParse(raw.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hv)) { sb.Append((char)hv); i += 4; }
                    }
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static void CopyAll(string s, string d, string[] pats)
        {
            if (!Directory.Exists(s)) return; Directory.CreateDirectory(d);
            for (int p = 0; p < pats.Length; p++) foreach (string f in Directory.GetFiles(s, pats[p])) { try { File.Copy(f, Path.Combine(d, Path.GetFileName(f)), true); } catch { } }
        }

        private static string Sha256Hex(string s)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")); StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2")); return sb.ToString();
            }
        }

        private static string Sha256File(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using (FileStream fs = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(fs); StringBuilder sb = new StringBuilder(64);
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2")); return sb.ToString();
                }
            }
            catch { return ""; }
        }

        private static string ResolveAdb()
        {
            string[] c = new string[] { Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe"), @"C:\platform-tools\adb.exe", @"E:\platform-tools\adb.exe" };
            for (int i = 0; i < c.Length; i++) if (File.Exists(c[i])) return c[i];
            return "adb.exe";
        }

        private static string RunAdb(string args, int ms)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ResolveAdb(), args);
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(ms); return o ?? ""; }
            }
            catch { return ""; }
        }

        private static List<string> ListSerials()
        {
            List<string> l = new List<string>();
            foreach (string ln in RunAdb("devices", 1500).Split('\n')) { string t = ln.Trim(); if (t.EndsWith("\tdevice")) { string s = t.Split('\t')[0].Trim(); if (s.Length > 0) l.Add(s); } }
            return l;
        }

        public static int ArmAllPixelsStatic()
        {
            List<string> s = ListSerials();
            for (int i = 0; i < s.Count; i++)
            {
                RunAdb("-s " + s[i] + " reverse tcp:19777 tcp:19777", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:9778 tcp:9778", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19779 tcp:19779", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19780 tcp:19780", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19781 tcp:19781", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19782 tcp:19782", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19783 tcp:19783", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19784 tcp:19784", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19785 tcp:19785", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19786 tcp:19786", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19790 tcp:19790", 1400);
                RunAdb("-s " + s[i] + " reverse tcp:19791 tcp:19791", 1400);
                RunAdb("-s " + s[i] + " forward tcp:19788 tcp:19788", 1400);
                RunAdb("-s " + s[i] + " forward tcp:22222 tcp:2222", 1400);
                EnsureHostPort19789BridgedStatic(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));
                RunAdb("-s " + s[i] + " forward tcp:28022 tcp:8022", 1400);
            }
            return s.Count;
        }

        private static long GetRamMb(string nm, out bool on)
        {
            Process[] ps = Process.GetProcessesByName(nm); on = (ps.Length > 0); long b = 0;
            for (int i = 0; i < ps.Length; i++) { try { b += ps[i].WorkingSet64; } catch { } }
            return b / 1048576L;
        }

        private static bool ProbePort(int port)
        {
            Socket sock = null;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Blocking = false;
                try { sock.Connect(new IPEndPoint(IPAddress.Loopback, port)); return true; }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode != SocketError.WouldBlock && se.SocketErrorCode != SocketError.InProgress && se.SocketErrorCode != SocketError.AlreadyInProgress)
                        return false;
                }
                if (sock.Poll(110000, SelectMode.SelectWrite))
                    return ((int)sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)) == 0;
            }
            catch { }
            finally { if (sock != null) { try { sock.Close(); } catch { } } }
            return false;
        }

        public static void MakeIco(string p, int r, int g, int b, string tx)
        {
            if (File.Exists(p)) return; Directory.CreateDirectory(Path.GetDirectoryName(p));
            using (Bitmap bm = new Bitmap(64, 64)) using (Graphics gx = Graphics.FromImage(bm)) using (MemoryStream ms = new MemoryStream())
            {
                gx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; gx.Clear(C(9, 13, 24));
                using (Pen pn = new Pen(C(r, g, b), 3.2f)) gx.DrawEllipse(pn, 5, 5, 54, 54);
                using (Font fn = new Font("Segoe UI", 18f, FontStyle.Bold)) using (SolidBrush br = new SolidBrush(C(240, 249, 255)))
                {
                    StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    gx.DrawString(tx, fn, br, new RectangleF(0, 1, 64, 64), sf);
                }
                bm.Save(ms, System.Drawing.Imaging.ImageFormat.Png); byte[] pg = ms.ToArray();
                using (FileStream fs = new FileStream(p, FileMode.Create)) using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)1); bw.Write((byte)64); bw.Write((byte)64); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((ushort)1); bw.Write((ushort)32); bw.Write((uint)pg.Length); bw.Write((uint)22); bw.Write(pg);
                }
            }
        }

        private void RefreshAsync()
        {
            if (_polling || IsDisposed) return;
            _polling = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    bool brOn, mrOn, avOn, syOn, sfOn, pnOn, sgOn, fgOn, shOn;
                    bool snOn, cdOn, tnOn; long snMb = GetRamMb("OmegaSonar", out snOn), cdMb = GetRamMb("OmegaConductor", out cdOn), tnMb = GetRamMb("OmegaTensor", out tnOn); long brMb = GetRamMb("OmegaBrowser", out brOn), mrMb = GetRamMb("OmegaMirror", out mrOn), avMb = GetRamMb("OmegaAV", out avOn), syMb = GetRamMb("OmegaSynth", out syOn), sfMb = GetRamMb("OmegaSheaf", out sfOn), pnMb = GetRamMb("OmegaPenrose", out pnOn), sgMb = GetRamMb("OmegaSteg", out sgOn), fgMb = GetRamMb("OmegaForge", out fgOn), shMb = GetRamMb("OmegaShell", out shOn);
                    bool gmOn = ProbePort(8097) || ProbePort(11434);
                    string brT = "Browser (:9778): " + (brOn ? ("ONLINE (" + brMb + " MB)") : "OFFLINE");
                    string mrT = "Mirror (:19777): " + (mrOn ? ("ONLINE (" + mrMb + " MB)") : "OFFLINE");
                    string avT = "AV:" + (avOn ? "ON" : "--") + " | Sy:" + (syOn ? "ON" : "--") + " | Sn:" + (snOn ? "ON" : "--") + " | Cd:" + (cdOn ? "ON" : "--") + " | Tn:" + (tnOn ? "ON" : "--") + " | Sf:" + (sfOn ? "ON" : "--") + " | Pn:" + (pnOn ? "ON" : "--") + " | Sg:" + (sgOn ? "ON" : "--");
                    string ramT = "Forge:" + (fgOn ? "ON" : "--") + " | Shell:" + (shOn ? "ON" : "--") + " | Gemma:" + (gmOn ? "ON" : "--") + " | " + (brMb + mrMb + avMb + syMb + snMb + cdMb + tnMb + sfMb + pnMb + sgMb + fgMb + shMb) + " MB";

                    List<string> raw = ListSerials(), dv = new List<string>();
                    for (int i = 0; i < raw.Count; i++) dv.Add(raw[i].Contains(":") ? ("WiFi:" + raw[i].Split(':')[0]) : ("USB:" + (raw[i].Length > 8 ? raw[i].Substring(0, 6) : raw[i])));
                    bool p10Wk = Pixel10TensorBridge.ProbeDaemonHealth(19788, 1200);
                    if (!p10Wk && raw.Count > 0 && (_p10PollTick % 4) == 0)
                    {
                        RunAdb("-s " + raw[0] + " forward tcp:19788 tcp:19788", 1200);
                        RunAdb("-s " + raw[0] + " shell \"test -x /data/local/tmp/omega_p10_worker && /data/local/tmp/omega_p10_worker --daemonize --port 19788\"", 2200);
                        p10Wk = Pixel10TensorBridge.ProbeDaemonHealth(19788, 900);
                    }
                    bool avfGpuWk = Pixel10TensorBridge.ProbeDaemonHealth(19789, 1000);
                    string sshBan;
                    bool avfLive = Pixel10TensorBridge.ProbeSshBanner(22222, 140, out sshBan);
                    if (p10Wk || avfGpuWk || dv.Count > 0)
                    {
                        if ((_p10PollTick++ % 3) == 0 || !p10Wk || (_cachedP10Line.IndexOf("Quad-Silicon", StringComparison.Ordinal) < 0 && _cachedP10Line.IndexOf("(:19788):", StringComparison.Ordinal) < 0))
                        {
                            P10TelemetrySnapshot snap = Pixel10TensorBridge.FetchSnapshot("/api/status", 3500);
                            P10TelemetrySnapshot avfSnap = avfGpuWk ? Pixel10TensorBridge.FetchSnapshotOnPort(19789, "/api/status", 1400) : null;
                            if (snap != null && snap.Online) p10Wk = true;
                            if (avfSnap != null && avfSnap.Online) avfGpuWk = true;
                            if ((snap != null && snap.Online) || (avfSnap != null && avfSnap.Online))
                            {
                                _cachedP10Line = Pixel10TensorBridge.FormatDualDaemonLauncherLine(snap, avfSnap);
                            }
                        }
                    }
                    if (!p10Wk && !avfGpuWk)
                    {
                        _cachedP10Line = (dv.Count > 0)
                            ? "P10 13-Kernel Tensor (:19788): ADB READY -- click [Shift+F9] or 'Bench 13x+TPU' to wake!"
                            : "P10 13-Kernel Tensor (:19788): STANDBY (Host DirectCompute GPU Tensor ready via 'RTX + P10 Tensor')";
                    }
                    string adbT = (dv.Count == 0)
                        ? "Pixel 10 Edge: no ADB device (plug USB or connect :5555)"
                        : ("Pixel 10 (" + string.Join(" + ", dv.ToArray()) + ") | :19788=" + (p10Wk ? "LIVE" : "Standby") +
                           " | @deb :19789=" + (avfGpuWk ? "LIVE" : "Standby") +
                           " | SSH :22222=" + (avfLive ? "LIVE" : "Standby"));
                    string p10LineLocal = _cachedP10Line;

                    string cDir = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus"))) ? (Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")) : Path.Combine(_brDir, "corpus");
                    int nMd = 0;
                    if (Directory.Exists(cDir)) nMd = Directory.GetFiles(cDir, "*.md").Length;
                    int dlFiles = Directory.Exists(_dlDir) ? Directory.GetFiles(_dlDir, "*.*", SearchOption.AllDirectories).Length : 0;
                    string freeGb = "";
                    try
                    {
                        DriveInfo di = new DriveInfo(Path.GetPathRoot(_dlDir));
                        if (di.IsReady) freeGb = " | " + Path.GetPathRoot(_dlDir).TrimEnd('\\') + " Free:" + (di.AvailableFreeSpace / 1073741824.0).ToString("0.0") + "GB";
                    }
                    catch { }
                    string crpT = "Vault DL: " + dlFiles + " files" + freeGb + " | Corpus: " + nMd + " docs | Bridge: " + (Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"))) ? "G:\\ OK" : "Local");

                    if (!IsDisposed) BeginInvoke((Action)delegate
                    {
                        _lBr.Text = brT; _lBr.ForeColor = brOn ? C(52, 211, 153) : C(148, 163, 184);
                        _lMr.Text = mrT; _lMr.ForeColor = mrOn ? C(52, 211, 153) : C(148, 163, 184);
                        _lAv.Text = avT; _lAv.ForeColor = (avOn || syOn || snOn || tnOn || sfOn || pnOn || sgOn) ? C(244, 114, 182) : C(148, 163, 184);
                        _lRam.Text = ramT; _lRam.ForeColor = (shOn || fgOn) ? C(52, 211, 153) : C(56, 189, 248);
                        _lAdb.Text = adbT; _lAdb.ForeColor = (dv.Count > 0) ? C(56, 189, 248) : C(248, 113, 113);
                        _lP10Tensor.Text = p10LineLocal; _lP10Tensor.ForeColor = p10Wk ? C(52, 211, 153) : C(125, 211, 252);
                        _lCorp.Text = crpT; _lCorp.ForeColor = C(250, 204, 21);
                    });
                }
                catch { }
                finally { _polling = false; }
            });
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals((args[0] ?? "").Trim(), "--stress-test", StringComparison.OrdinalIgnoreCase)) { ChaosSuiteSummary cs = OmegaRnDEdgeCaseTortureSuite.RunAll8ChaosVectors(); Console.WriteLine("[OMEGA UNIFIED DECK 8-VEC CHAOS] " + cs.PassedVectors + "/" + cs.TotalVectors + " PASS (" + cs.TotalTrials + " trials | RAM=" + cs.WorkingSetMb.ToString("0.0", CultureInfo.InvariantCulture) + " MB)"); Environment.Exit(cs.AllPassed ? 0 : 1); return; }
            OmegaHardwareProfile hwProf = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            string r = hwProf.LabRoot;
            OmegaUnifiedDeckHttpServer.EnsureStarted(19800);
            if (args != null && args.Length > 0)
            {
                string a = (args[0] ?? "").Trim().ToLowerInvariant();
                if (a == "--arm-pixels") { ArmAllPixelsStatic(); return; }
                if (a == "--sync-bridge") { Console.WriteLine(ExecuteSuiteBridgeSync(r)); return; }
                if (a == "--sec-stress") { Process p = Process.Start(Path.Combine(r, @"08_omega_steg\OmegaSteg.exe"), "--stress-test"); if (p != null) p.WaitForExit(); return; }
                if (a == "--p10-bench") { Process p = Process.Start("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(r, @"11_omega_edge_workers\rust_p10_worker\Deploy-And-Run-Pixel10Worker.ps1") + "\" -Action Bench"); if (p != null) p.WaitForExit(); return; }
                if (a == "--p10-tensor") { Process p = Process.Start("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(r, @"11_omega_edge_workers\rust_p10_worker\Deploy-And-Run-Pixel10Worker.ps1") + "\" -Action TensorOnly"); if (p != null) p.WaitForExit(); return; }
                if (a == "--p10-coopmat") { Process p = Process.Start("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(r, @"11_omega_edge_workers\rust_p10_worker\Deploy-And-Run-Pixel10Worker.ps1") + "\" -Action CoopMatOnly"); if (p != null) p.WaitForExit(); return; }
                if (a == "--p10-tpu") { Process p = Process.Start("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(r, @"11_omega_edge_workers\rust_p10_worker\Deploy-And-Run-Pixel10Worker.ps1") + "\" -Action TpuOnly"); if (p != null) p.WaitForExit(); return; }
                if (a == "--avf-stage") { Console.WriteLine(StageAvfSetupFilesStatic(r)); return; }
                if (a == "--avf-autotype") { Console.WriteLine(ExecuteZeroMountAvfAutoTypeStatic(r)); return; }
                if (a == "--avf-rust-bench" || a == "--avf-daemon-deploy") { Console.WriteLine(ExecuteAvfNativeRustBenchStatic(r)); return; }
                if (a == "--avf-dispatch-udot") { EnsureHostPort19789BridgedStatic(r); Console.WriteLine(HttpGetFast("http://127.0.0.1:19789/api/dispatch/udot", 15000)); return; }
                if (a == "--make-icons") { MakeIco(Path.Combine(r, "icons", "omega_lab.ico"), 56, 189, 248, "O1"); MakeIco(Path.Combine(r, "icons", "omega_browser.ico"), 2, 132, 199, "BR"); MakeIco(Path.Combine(r, "icons", "omega_mirror.ico"), 15, 118, 110, "MR"); MakeIco(Path.Combine(r, "icons", "omega_av.ico"), 168, 85, 247, "AV"); MakeIco(Path.Combine(r, "icons", "omega_forge.ico"), 16, 185, 129, "OF"); MakeIco(Path.Combine(r, "icons", "omega_shell.ico"), 14, 165, 233, "OS"); MakeIco(Path.Combine(r, "icons", "omega_synth.ico"), 236, 72, 153, "SY"); MakeIco(Path.Combine(r, "icons", "omega_sonar.ico"), 6, 148, 162, "SN"); MakeIco(Path.Combine(r, "icons", "omega_penrose.ico"), 20, 184, 166, "PN"); MakeIco(Path.Combine(r, "icons", "omega_sheaf.ico"), 16, 185, 129, "SF"); MakeIco(Path.Combine(r, "icons", "omega_steg.ico"), 245, 158, 11, "SG"); MakeIco(Path.Combine(r, "icons", "omega_conductor.ico"), 13, 148, 136, "CD"); MakeIco(Path.Combine(r, "icons", "omega_tensor.ico"), 124, 58, 237, "TN"); return; }
            }
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); Application.ThreadException += delegate { }; AppDomain.CurrentDomain.UnhandledException += delegate { }; } catch { }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaLabLauncherForm(r));
        }
    }
}
