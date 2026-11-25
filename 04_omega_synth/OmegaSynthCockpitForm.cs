using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OmegaSynthStudio
{
    public sealed class SmoothStagePanel : Panel
    {
        public SmoothStagePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    public sealed partial class OmegaSynthCockpitForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NATIVE_MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PeekMessageW(out NATIVE_MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

        private readonly string _synthDir, _suiteRoot, _rendersDir, _patchesDir, _printsDir, _inboxCmdPath, _avDir, _avExe, _brDir, _brExe;
        private readonly string _startupFileArg;
        private readonly OmegaWavefieldAudioEngine _audio;
        private readonly OmegaCymaticsGpuEngine _gpu;
        private readonly OmegaPixelMpeAndGamepadServer _mpeServer;

        private readonly SmoothStagePanel _stage;
        private TabControl _rightTabs;
        private Bitmap _cpuBmp;
        private Label _lblTelemetryRow2, _lblStatusBot, _lblGyroReadout, _lblRecTimer, _lblLiveNoteReadout, _lblLoopStatus;
        private Label _lblAudioDnaSummary, _lblPipeTrackInfo, _lblMicLiveReadout;
        private ComboBox _cmbPatchBank, _cmbPreset, _cmbScale, _cmbViewMode, _cmbBaseOct, _cmbVsyncMode, _cmbMicDevice, _cmbMicMode;
        private NumericUpDown _nudM, _nudN;
        private CheckBox _chkAutoMN, _chkFormantOn, _chkSyncAvOmsh, _chkClipSymFx, _chkPipeSymDrone;
        private TrackBar _trkSnap, _trkE8Mix, _trkHall, _trkDecay, _trkVol, _trkFormant, _trkBpm, _trkClipScrub, _trkMicGain, _trkMicGate, _trkEvolveDensity;
        private Button _btnDrone, _btnGyro, _btnArp, _btnRecWav, _btnLoopRec, _btnLaneMute;
        private Button _btnTopPipe, _btnTopMic, _btnTopEvolve, _btnClipPlay, _btnClipLoop, _btnPipeToggle, _btnMicToggle, _btnAutoEvolve, _btnSweepGen;
        private readonly Button[] _laneSelBtns = new Button[4];
        private readonly Button[] _stepBtns = new Button[16];
        private NumericUpDown _nudLaneLen;
        private ProgressBar _pbLeft, _pbRight, _pbMicLevel;
        private TextBox _txtGemmaPatch, _txtBrowserUrl;

        private readonly float[] _e8Snap = new float[240];
        private readonly short[] _waveL = new short[256];
        private readonly short[] _waveR = new short[256];
        private readonly float[] _spec32 = new float[32];
        private readonly MpeVoiceSnapshot[] _voicesSnap = new MpeVoiceSnapshot[8];

        private readonly Stopwatch _simClock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer _uiTelemetryTimer;
        private volatile bool _renderLoopActive = false;
        private bool _mouseExciting = false;
        private bool _scrubbingClip = false;
        private int _fpsFrames = 0;
        private double _fpsLastSec = 0.0;
        private double _lastFrameSec = 0.0;
        private int _measuredFps = 60;
        private bool _syncingUi = false;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaSynthCockpitForm(string synthDir, string startupFileArg)
        {
            _synthDir = synthDir;
            _startupFileArg = startupFileArg ?? "";
            _suiteRoot = Directory.GetParent(synthDir) != null ? Directory.GetParent(synthDir).FullName : synthDir;
            _rendersDir = Path.Combine(synthDir, "renders");
            _patchesDir = Path.Combine(synthDir, "patches");
            _printsDir = Path.Combine(synthDir, "prints");
            Directory.CreateDirectory(_printsDir);
            _inboxCmdPath = Path.Combine(synthDir, "synth_inbox.cmd");
            _avDir = Path.Combine(_suiteRoot, "03_omega_av");
            _avExe = Path.Combine(_avDir, "OmegaAV.exe");
            _brDir = Path.Combine(_suiteRoot, "02_omega_browser");
            _brExe = Path.Combine(_brDir, "OmegaBrowser.exe");
            Directory.CreateDirectory(_rendersDir);
            Directory.CreateDirectory(_patchesDir);

            _audio = new OmegaWavefieldAudioEngine();
            _gpu = new OmegaCymaticsGpuEngine();
            _mpeServer = new OmegaPixelMpeAndGamepadServer(_audio, _gpu, _rendersDir);
            _mpeServer.OnUiStateMutated = delegate
            {
                if (!IsDisposed) { try { BeginInvoke((Action)SyncControlsFromEngine); } catch { } }
            };
            _mpeServer.OnExternalFileLoadRequested = delegate(string path)
            {
                if (!IsDisposed)
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            BringSelfToFront();
                            LoadSoundOrPatchFileAsync(path, true);
                        });
                    }
                    catch { }
                }
            };
            _mpeServer.OnBrowserPipeRequested = delegate(string title, string url, bool enable)
            {
                if (!IsDisposed)
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            BringSelfToFront();
                            ActivateLiveBrowserPipeUi(enable, title, url);
                        });
                    }
                    catch { }
                }
            };

            AutoScaleMode = AutoScaleMode.None;
            Text = "ACT-Omega Lab :: OmegaSynth v1.1.1 Final GM (04_omega_synth) — RTX 60fps VSync, Laptop Mic, Sound-Shape Resynthesis & Web Pipe (:19782)";
            Size = new Size(1680, 960);
            MinimumSize = new Size(1240, 750);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(8, 12, 21);
            ForeColor = Color.White;
            KeyPreview = true;
            AllowDrop = true;
            try
            {
                string ic = Path.Combine(_suiteRoot, "icons", "omega_synth.ico");
                if (File.Exists(ic)) Icon = new Icon(ic);
            }
            catch { }

            // =========================================================================
            // TOP HEADER (125% DPI Safe + Direct Mic / Web-Pipe / Evolve Quick Toggles)
            // =========================================================================
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 60;
            topBar.BackColor = C(11, 16, 29);

            Panel topRow1 = new Panel();
            topRow1.Dock = DockStyle.Top;
            topRow1.Height = 35;
            topRow1.BackColor = Color.Transparent;

            Label lBrand = new Label();
            lBrand.UseMnemonic = false;
            lBrand.Dock = DockStyle.Left;
            lBrand.Width = 315;
            lBrand.Padding = new Padding(14, 7, 0, 0);
            lBrand.Text = "OMEGASYNTH v1.1.1 FINAL GM (:19782)";
            lBrand.Font = new Font("Segoe UI", 10.2f, FontStyle.Bold);
            lBrand.ForeColor = C(244, 114, 182);

            FlowLayoutPanel rightHdrBtns = new FlowLayoutPanel();
            rightHdrBtns.Dock = DockStyle.Right;
            rightHdrBtns.Width = 760;
            rightHdrBtns.FlowDirection = FlowDirection.RightToLeft;
            rightHdrBtns.WrapContents = false;
            rightHdrBtns.Padding = new Padding(0, 3, 10, 0);
            rightHdrBtns.BackColor = Color.Transparent;

            Button bHelp = TopBtn("? Guide", 76, C(180, 83, 9));
            bHelp.Click += delegate { ShowQuickGuide(); };

            Button bSyncAv = TopBtn("Sync -> AV", 102, C(109, 40, 217));
            bSyncAv.Click += delegate { LaunchOrSyncOmegaAv(); };

            Button bBeamPx = TopBtn("Beam -> Pixel", 122, C(5, 150, 105));
            bBeamPx.Click += delegate { BeamMpePadAsync(); };

            _btnTopEvolve = TopBtn("Evolve: OFF", 104, C(67, 56, 202));
            _btnTopEvolve.Click += delegate
            {
                _rightTabs.SelectedIndex = 3;
                ToggleAutoEvolveUi();
            };

            _btnTopPipe = TopBtn("Web Pipe: OFF", 118, C(14, 116, 144));
            _btnTopPipe.Click += delegate
            {
                _rightTabs.SelectedIndex = 2;
                ActivateLiveBrowserPipeUi(!_audio.WebPipeActive, _audio.WasapiPipe.PipeTitle, _audio.WasapiPipe.PipeUrl);
            };

            _btnTopMic = TopBtn("Mic: OFF", 98, C(15, 118, 110));
            _btnTopMic.Click += delegate
            {
                _rightTabs.SelectedIndex = 3;
                ToggleLaptopMicUi(!_audio.MicInputActive);
            };

            Button bPanic = TopBtn("Panic", 68, C(153, 27, 27));
            bPanic.Click += delegate
            {
                _audio.ClipPlaying = false;
                _audio.AutoEvolveEnabled = false;
                _audio.LabToneMode = 0;
                _audio.SetWebPipeEnabled(false, null, null);
                _audio.SetMicInputEnabled(false, -1);
                _audio.ReleaseAllVoices(true);
                SyncControlsFromEngine();
                SetStatus("All MPE, clip, laptop mic, web-pipe, auto-evolve, lab sweep & looper voices silenced.");
            };

            rightHdrBtns.Controls.AddRange(new Control[] { bHelp, bSyncAv, bBeamPx, _btnTopEvolve, _btnTopPipe, _btnTopMic, bPanic });
            topRow1.Controls.Add(rightHdrBtns);
            topRow1.Controls.Add(lBrand);

            _lblTelemetryRow2 = new Label();
            _lblTelemetryRow2.UseMnemonic = false;
            _lblTelemetryRow2.Dock = DockStyle.Fill;
            _lblTelemetryRow2.Padding = new Padding(14, 2, 10, 0);
            _lblTelemetryRow2.Font = new Font("Consolas", 8.6f, FontStyle.Bold);
            _lblTelemetryRow2.ForeColor = C(56, 189, 248);
            _lblTelemetryRow2.Text = "48.0 kHz Stereo FDTD + KS LIVE | Initializing RTX cs_5_0 VSync... | Drop any .WAV/.MP3/.FLAC/.OSYNTH!";

            topBar.Controls.Add(_lblTelemetryRow2);
            topBar.Controls.Add(topRow1);

            // =========================================================================
            // BOTTOM STATUS + 4-LANE POLYRHYTHM SEQUENCER & TANDEM GEMMA DOCK
            // =========================================================================
            Panel botStrip = new Panel();
            botStrip.Dock = DockStyle.Bottom;
            botStrip.Height = 25;
            botStrip.BackColor = C(6, 9, 17);
            _lblStatusBot = new Label();
            _lblStatusBot.UseMnemonic = false;
            _lblStatusBot.Dock = DockStyle.Fill;
            _lblStatusBot.Font = new Font("Consolas", 8.4f);
            _lblStatusBot.ForeColor = C(52, 211, 153);
            _lblStatusBot.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatusBot.Padding = new Padding(12, 0, 0, 0);
            _lblStatusBot.Text = "OmegaSynth v1.1.1 Final GM Ready :: 60fps Hardware VSync | Drop a Sound File to see its Geometric Shape & Resynthesize!";
            botStrip.Controls.Add(_lblStatusBot);

            Panel seqDock = new Panel();
            seqDock.Dock = DockStyle.Bottom;
            seqDock.Height = 136;
            seqDock.BackColor = C(12, 18, 32);
            seqDock.Padding = new Padding(10, 4, 10, 4);
            BuildPolyrhythmSequencerDock(seqDock);

            // =========================================================================
            // RIGHT CONTROL RACK: PERSISTENT PATCH BANK + 3-TAB STUDIO RACK
            // =========================================================================
            Panel rightRackHost = new Panel();
            rightRackHost.Dock = DockStyle.Right;
            rightRackHost.Width = 472;
            rightRackHost.BackColor = C(13, 19, 34);
            rightRackHost.Padding = new Padding(4, 4, 6, 4);
            BuildTabbedRightRack(rightRackHost);

            // =========================================================================
            // CENTER LIVE RTX cs_5_0 512x512 CHLADNI + SOUND-SHAPE HOLOGRAM STAGE
            // =========================================================================
            Panel centerHost = new Panel();
            centerHost.Dock = DockStyle.Fill;
            centerHost.Padding = new Padding(10, 8, 6, 8);
            centerHost.BackColor = C(7, 10, 18);

            _stage = new SmoothStagePanel();
            _stage.Dock = DockStyle.Fill;
            _stage.BackColor = Color.Black;
            _stage.Cursor = Cursors.Cross;
            _stage.AllowDrop = true;
            _stage.MouseDown += OnStageMouseDown;
            _stage.MouseMove += OnStageMouseMove;
            _stage.MouseUp += OnStageMouseUp;
            _stage.Paint += OnStageCpuPaint;
            centerHost.Controls.Add(_stage);

            DragEventHandler onDe = delegate(object s, DragEventArgs de)
            {
                de.Effect = (de.Data != null && de.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragEventHandler onDd = delegate(object s, DragEventArgs dd)
            {
                string[] files = dd.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) LoadSoundOrPatchFileAsync(files[0], true);
            };
            DragEnter += onDe; DragDrop += onDd;
            _stage.DragEnter += onDe; _stage.DragDrop += onDd;

            Controls.Add(centerHost);
            Controls.Add(rightRackHost);
            Controls.Add(seqDock);
            Controls.Add(botStrip);
            Controls.Add(topBar);

            KeyDown += OnSynthKeyDown;
            KeyUp += OnSynthKeyUp;

            _uiTelemetryTimer = new System.Windows.Forms.Timer();
            _uiTelemetryTimer.Interval = 125;
            _uiTelemetryTimer.Tick += delegate { UpdateUiTelemetryAndInboxTick(); };

            Shown += delegate
            {
                try { timeBeginPeriod(1u); } catch { }
                _audio.Start();
                _mpeServer.Start(19782);
                _gpu.Initialize(_stage.Handle, Math.Max(320, _stage.ClientSize.Width), Math.Max(240, _stage.ClientSize.Height));
                RefreshPatchBankDropdown();
                SyncControlsFromEngine();
                _renderLoopActive = true;
                Application.Idle += OnAppIdleRenderLoop;
                _uiTelemetryTimer.Start();

                if (!string.IsNullOrEmpty(_startupFileArg) && File.Exists(_startupFileArg))
                    LoadSoundOrPatchFileAsync(_startupFileArg, true);
                else
                    _audio.TriggerChordPreview(0);
            };

            FormClosing += delegate
            {
                _renderLoopActive = false;
                Application.Idle -= OnAppIdleRenderLoop;
                _uiTelemetryTimer.Stop();
                try { timeEndPeriod(1u); } catch { }
                DisposeTriEngineBus();
                _mpeServer.Dispose();
                _audio.Dispose();
                _gpu.Dispose();
            };
        }

        private static bool HasPendingWin32Message()
        {
            NATIVE_MSG msg;
            return PeekMessageW(out msg, IntPtr.Zero, 0u, 0u, 0u);
        }

        private void OnAppIdleRenderLoop(object sender, EventArgs e)
        {
            while (_renderLoopActive && !IsDisposed && Visible && WindowState != FormWindowState.Minimized && !HasPendingWin32Message())
            {
                RenderFastGpuFrame();
            }
        }

        private void RenderFastGpuFrame()
        {
            double t = _simClock.Elapsed.TotalSeconds;
            double dt = t - _lastFrameSec;
            if (!_gpu.IsGpuActive && dt < 0.016)
            {
                Thread.Sleep(4);
                return;
            }
            if (_gpu.IsGpuActive && !_gpu.VSyncEnabled && dt < 0.0025)
            {
                Thread.Sleep(1);
            }
            _lastFrameSec = t;

            _mpeServer.PollGamepadExpression();

            _fpsFrames++;
            if (t - _fpsLastSec >= 1.0)
            {
                _measuredFps = _fpsFrames;
                _fpsFrames = 0;
                _fpsLastSec = t;
            }

            _audio.CopyTelemetrySnapshots(_e8Snap, _waveL, _waveR, _spec32, _voicesSnap);
            PublishTriEngineBusFrame(t);

            int sw = Math.Max(64, _stage.ClientSize.Width);
            int sh = Math.Max(64, _stage.ClientSize.Height);
            int viewMode = _cmbViewMode.SelectedIndex;
            SynthPresetSpec pr = E8WaveLatticeMath.Presets[_audio.PresetIndex % E8WaveLatticeMath.Presets.Length];

            if (_gpu.IsGpuActive)
            {
                _gpu.RenderGpuFrame(sw, sh, t, viewMode, pr.PlateBoundaryMode,
                    _audio.ChladniM, _audio.ChladniN, _audio.LeadVoiceRoot,
                    _audio.PeakLevelL, _audio.PeakLevelR, _audio.GyroBetaDeg, _audio.GyroGammaDeg,
                    _e8Snap, _waveL, _waveR, _spec32, _voicesSnap);
            }
            else
            {
                _stage.Invalidate();
            }
        }

        private void BuildTabbedRightRack(Panel host)
        {
            // Persistent Top .osynth Patch Bank Bar!
            Panel patchStrip = new Panel();
            patchStrip.Dock = DockStyle.Top;
            patchStrip.Height = 52;
            patchStrip.BackColor = C(10, 15, 28);
            patchStrip.Padding = new Padding(6, 4, 6, 4);

            RackLabel(patchStrip, "PATCH BANK (.osynth):", 6, 3);
            _cmbPatchBank = RackCombo(patchStrip, 6, 21, 272);
            _cmbPatchBank.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                OmegaPatchBankEntry ent = _cmbPatchBank.SelectedItem as OmegaPatchBankEntry;
                if (ent != null && File.Exists(ent.FilePath))
                {
                    string loadedName = OmegaSynthPatchBank.LoadPatchIntoEngine(ent.FilePath, _audio, _gpu);
                    RefreshPresetDropdownNames();
                    SyncControlsFromEngine();
                    SetStatus("Loaded Patch Bank -> " + loadedName);
                }
            };

            Button bSavePatch = AddRackBtn(patchStrip, "Save", 284, 19, 68, C(5, 150, 105));
            bSavePatch.Click += delegate { SaveCurrentPatchDialog(); };

            Button bLoadPatch = AddRackBtn(patchStrip, "Load...", 356, 19, 74, C(3, 105, 161));
            bLoadPatch.Click += delegate { LoadPatchFileDialog(); };

            _rightTabs = new TabControl();
            _rightTabs.Dock = DockStyle.Fill;
            _rightTabs.Font = new Font("Segoe UI Semibold", 7.8f);

            TabPage tabSynth = new TabPage("1. Plate & DSP");
            tabSynth.BackColor = C(13, 19, 34); tabSynth.ForeColor = Color.White; tabSynth.AutoScroll = true;

            TabPage tabStudio = new TabPage("2. Looper & Pad");
            tabStudio.BackColor = C(13, 19, 34); tabStudio.ForeColor = Color.White; tabStudio.AutoScroll = true;

            TabPage tabAudioLab = new TabPage("3. Shape & Pipe");
            tabAudioLab.BackColor = C(13, 19, 34); tabAudioLab.ForeColor = Color.White; tabAudioLab.AutoScroll = true;

            TabPage tabMicEvolve = new TabPage("4. Mic & Evolve");
            tabMicEvolve.BackColor = C(13, 19, 34); tabMicEvolve.ForeColor = Color.White; tabMicEvolve.AutoScroll = true;

            // =========================================================================
            // TAB 1: RESONATORS (01..09), VSYNC, CHLADNI (m,n), FORMANT & SLIDERS
            // =========================================================================
            int y = 6;
            RackHeader(tabSynth, "PHYSICAL / RESYNTH RESONATOR & SCALE", ref y);

            RackLabel(tabSynth, "Resonator Engine (01..08 Physical + 09 Cloned DNA):", 10, y); y += 17;
            _cmbPreset = RackCombo(tabSynth, 10, y, 404);
            RefreshPresetDropdownNames();
            _cmbPreset.SelectedIndex = 0;
            _cmbPreset.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                _audio.PresetIndex = _cmbPreset.SelectedIndex;
                _gpu.ResetSandAndWavefield();
                SetStatus("Loaded engine: " + E8WaveLatticeMath.Presets[_audio.PresetIndex].Name);
            };
            y += 27;

            RackLabel(tabSynth, "Microtonal Scale:", 10, y);
            RackLabel(tabSynth, "Base Pitch:", 292, y); y += 17;
            _cmbScale = RackCombo(tabSynth, 10, y, 274);
            for (int i = 0; i < E8WaveLatticeMath.ScaleNames.Length; i++) _cmbScale.Items.Add(E8WaveLatticeMath.ScaleNames[i]);
            _cmbScale.SelectedIndex = 0;
            _cmbScale.SelectedIndexChanged += delegate { if (!_syncingUi) _audio.ScaleIndex = _cmbScale.SelectedIndex; };

            _cmbBaseOct = RackCombo(tabSynth, 292, y, 122);
            _cmbBaseOct.Items.AddRange(new object[] { "C2 (36)", "C3 (48)", "A3 (57)", "C4 (60)" });
            _cmbBaseOct.SelectedIndex = 1;
            _cmbBaseOct.SelectedIndexChanged += delegate
            {
                int[] notes = new int[] { 36, 48, 57, 60 };
                if (_cmbBaseOct.SelectedIndex >= 0 && _cmbBaseOct.SelectedIndex < notes.Length) _audio.BaseMidiNote = notes[_cmbBaseOct.SelectedIndex];
            };
            y += 27;

            RackLabel(tabSynth, "RTX cs_5_0 Viewport Mode:", 10, y);
            RackLabel(tabSynth, "Frame Sync:", 248, y); y += 17;
            _cmbViewMode = RackCombo(tabSynth, 10, y, 232);
            _cmbViewMode.Items.AddRange(new object[]
            {
                "0: Split Sand + 8D E8 + Waterfall",
                "1: Full 512x512 Chladni Sand Plate",
                "2: Full 240-Root 8D E8 Polytope",
                "3: Sound-Shape Rose + Lissajous"
            });
            _cmbViewMode.SelectedIndex = 0;

            _cmbVsyncMode = RackCombo(tabSynth, 248, y, 92);
            _cmbVsyncMode.Items.AddRange(new object[] { "VSync ON", "Uncapped" });
            _cmbVsyncMode.SelectedIndex = 0;
            _cmbVsyncMode.SelectedIndexChanged += delegate
            {
                _gpu.VSyncEnabled = (_cmbVsyncMode.SelectedIndex == 0);
                SetStatus(_gpu.VSyncEnabled ? "DXGI Hardware VSync ENABLED (native 60/144 Hz refresh)." : "Uncapped GPU frame pacing ENABLED.");
            };

            Button bSand = AddRackBtn(tabSynth, "Sand", 346, y - 2, 68, C(30, 58, 138));
            bSand.Click += delegate { _gpu.ResetSandAndWavefield(); SetStatus("Re-sprinkled virtual quartz sand across 512x512 plate!"); };
            y += 30;

            RackHeader(tabSynth, "CHLADNI (m, n) EIGENMODE & AUDITION CHORDS", ref y);
            RackLabel(tabSynth, "m:", 10, y + 4);
            _nudM = new NumericUpDown(); _nudM.Minimum = 1; _nudM.Maximum = 12; _nudM.Value = 3;
            _nudM.SetBounds(30, y + 1, 50, 24); _nudM.BackColor = C(6, 9, 17); _nudM.ForeColor = Color.White;
            tabSynth.Controls.Add(_nudM);

            RackLabel(tabSynth, "n:", 88, y + 4);
            _nudN = new NumericUpDown(); _nudN.Minimum = 1; _nudN.Maximum = 12; _nudN.Value = 5;
            _nudN.SetBounds(108, y + 1, 50, 24); _nudN.BackColor = C(6, 9, 17); _nudN.ForeColor = Color.White;
            tabSynth.Controls.Add(_nudN);

            _chkAutoMN = new CheckBox();
            _chkAutoMN.UseMnemonic = false;
            _chkAutoMN.Text = "Auto Pitch->(m,n)";
            _chkAutoMN.Checked = true;
            _chkAutoMN.SetBounds(166, y + 2, 156, 22);
            _chkAutoMN.ForeColor = C(125, 211, 252);
            tabSynth.Controls.Add(_chkAutoMN);

            Button bLockMN = AddRackBtn(tabSynth, "Dial 4,7", 330, y - 1, 84, C(180, 83, 9));
            bLockMN.Click += delegate
            {
                _chkAutoMN.Checked = false;
                _audio.AutoChladniFromPitch = false;
                _nudM.Value = 4; _nudN.Value = 7;
                _audio.ChladniM = 4; _audio.ChladniN = 7;
                _audio.TriggerChordPreview(0);
            };
            _nudM.ValueChanged += delegate { if (!_syncingUi) { _chkAutoMN.Checked = false; _audio.AutoChladniFromPitch = false; _audio.ChladniM = (int)_nudM.Value; } };
            _nudN.ValueChanged += delegate { if (!_syncingUi) { _chkAutoMN.Checked = false; _audio.AutoChladniFromPitch = false; _audio.ChladniN = (int)_nudN.Value; } };
            _chkAutoMN.CheckedChanged += delegate { _audio.AutoChladniFromPitch = _chkAutoMN.Checked; };
            y += 29;

            Button bCh1 = AddRackBtn(tabSynth, "Play E8 9th", 10, y, 130, C(6, 95, 70));
            bCh1.Click += delegate { _audio.TriggerChordPreview(0); };
            Button bCh2 = AddRackBtn(tabSynth, "Just Ratio", 146, y, 130, C(15, 118, 110));
            bCh2.Click += delegate { _audio.TriggerChordPreview(1); };
            Button bCh3 = AddRackBtn(tabSynth, "Quartal Bell", 282, y, 132, C(67, 56, 202));
            bCh3.Click += delegate { _audio.TriggerChordPreview(2); };
            y += 33;

            RackHeader(tabSynth, "CONTINUUM, E8 SYMPATHY & FORMANT MORPH", ref y);
            _trkSnap = RackSlider(tabSynth, "Scale Snap (0%=Fretless, 100%=Locked):", 0, 100, 35, ref y, delegate(int v) { _audio.ScaleSnap = v / 100.0f; });
            _trkE8Mix = RackSlider(tabSynth, "240-Root 8D E8 Sympathetic Coupling:", 0, 100, 48, ref y, delegate(int v) { _audio.E8CouplingOverride = v / 100.0f; });
            _trkHall = RackSlider(tabSynth, "Cymatic Hall FDN Shimmer:", 0, 100, 38, ref y, delegate(int v) { _audio.HallShimmerOverride = v / 100.0f; });
            _trkDecay = RackSlider(tabSynth, "Resonator Decay (0.25x .. 2.50x):", 25, 250, 100, ref y, delegate(int v) { _audio.DecayMultiplier = v / 100.0f; });

            _chkFormantOn = new CheckBox();
            _chkFormantOn.UseMnemonic = false;
            _chkFormantOn.Text = "Enable Throat Vowel Formant Filter (A-E-I-O-U):";
            _chkFormantOn.SetBounds(12, y, 390, 20);
            _chkFormantOn.ForeColor = C(244, 114, 182);
            _chkFormantOn.CheckedChanged += delegate { _audio.FormantFilterEnabled = _chkFormantOn.Checked; };
            tabSynth.Controls.Add(_chkFormantOn);
            y += 19;
            _trkFormant = RackSlider(tabSynth, "Vowel Morph (0%=Ah, 25%=Eh, 50%=Ee, 75%=Oh, 100%=Oo):", 0, 100, 25, ref y, delegate(int v) { _audio.FormantVowelMorph = v / 100.0f; });
            _trkVol = RackSlider(tabSynth, "Master 48 kHz Output Gain:", 0, 100, 78, ref y, delegate(int v) { _audio.MasterVolume = v / 100.0f; });

            // =========================================================================
            // TAB 2: LIVE MPE GESTURE LOOPER, WAV/MIDI EXPORT, PIXEL GYRO & GAMEPAD
            // =========================================================================
            int y2 = 10;
            RackHeader(tabStudio, "1. LIVE MPE GESTURE OVERDUB LOOPER (60 Hz)", ref y2);

            _lblLoopStatus = new Label();
            _lblLoopStatus.UseMnemonic = false;
            _lblLoopStatus.SetBounds(10, y2, 404, 22);
            _lblLoopStatus.Font = new Font("Consolas", 8.6f, FontStyle.Bold);
            _lblLoopStatus.ForeColor = C(250, 204, 21);
            _lblLoopStatus.Text = "Looper: EMPTY (4 Bars) — Click REC & slide fingers!";
            tabStudio.Controls.Add(_lblLoopStatus);
            y2 += 26;

            _btnLoopRec = AddRackBtn(tabStudio, "REC / Overdub MPE Loop", 10, y2, 210, C(180, 83, 9));
            _btnLoopRec.Click += delegate { _audio.SeqLooper.ToggleRecOrOverdub(_audio.ArpBpm); SyncControlsFromEngine(); };

            Button bLoopClr = AddRackBtn(tabStudio, "Clear Loop", 226, y2, 94, C(127, 29, 29));
            bLoopClr.Click += delegate { _audio.SeqLooper.ClearLoop(); _audio.ReleaseLooperVoicesOnly(); SyncControlsFromEngine(); };

            ComboBox cmbBars = RackCombo(tabStudio, 326, y2 + 2, 88);
            cmbBars.Items.AddRange(new object[] { "2 Bars", "4 Bars", "8 Bars" });
            cmbBars.SelectedIndex = 1;
            cmbBars.SelectedIndexChanged += delegate
            {
                int[] b = new int[] { 2, 4, 8 };
                _audio.SeqLooper.LoopBars = b[cmbBars.SelectedIndex];
            };
            y2 += 38;

            RackHeader(tabStudio, "2. PIXEL 10 SPLIT MPE, DRONE & GAMEPAD", ref y2);
            _btnDrone = AddRackBtn(tabStudio, "Drone Latch: OFF", 10, y2, 198, C(51, 65, 85));
            _btnDrone.Click += delegate
            {
                _audio.DroneLatchMode = !_audio.DroneLatchMode;
                if (!_audio.DroneLatchMode) _audio.ReleaseAllVoices(true);
                SyncControlsFromEngine();
            };

            _btnGyro = AddRackBtn(tabStudio, "Pixel Gyro Bend: ON", 216, y2, 198, C(5, 150, 105));
            _btnGyro.Click += delegate { _audio.GyroPitchBendEnabled = !_audio.GyroPitchBendEnabled; SyncControlsFromEngine(); };
            y2 += 34;

            Button bBeam2 = AddRackBtn(tabStudio, "Beam Split Kaoss+MPE -> Pixel", 10, y2, 246, C(5, 150, 105));
            bBeam2.Click += delegate { BeamMpePadAsync(); };

            Button bRescanPad = AddRackBtn(tabStudio, "Rescan MIDI/Pad", 262, y2, 152, C(14, 116, 144));
            bRescanPad.Click += delegate { _mpeServer.RescanControllers(); SetStatus("Rescanned USB MIDI + Xbox/DualSense controllers: " + _mpeServer.ControllerStatus); };
            y2 += 36;

            _lblGyroReadout = new Label();
            _lblGyroReadout.UseMnemonic = false;
            _lblGyroReadout.SetBounds(10, y2, 404, 38);
            _lblGyroReadout.Font = new Font("Consolas", 8.2f);
            _lblGyroReadout.ForeColor = C(192, 132, 252);
            _lblGyroReadout.Text = "Pixel MPE: :19782/mpe | Tilt B:0.0 G:0.0\r\nGamepad: RT/R2=Bow, L-Stick=Pitch/Y, R-Stick=Formant";
            tabStudio.Controls.Add(_lblGyroReadout);
            y2 += 44;

            RackHeader(tabStudio, "3. 48kHz .WAV, .MID EXPORT & OMEGAAV BRIDGE", ref y2);
            RackLabel(tabStudio, "Stereo 48kHz Master Bus:", 10, y2); y2 += 18;
            _pbLeft = new ProgressBar(); _pbLeft.SetBounds(10, y2, 198, 14); _pbLeft.Maximum = 100;
            _pbRight = new ProgressBar(); _pbRight.SetBounds(216, y2, 198, 14); _pbRight.Maximum = 100;
            tabStudio.Controls.Add(_pbLeft); tabStudio.Controls.Add(_pbRight);
            y2 += 22;

            _btnRecWav = AddRackBtn(tabStudio, "Record 48kHz .WAV", 10, y2, 198, C(185, 28, 28));
            _btnRecWav.Click += delegate { ToggleWavRecording(); };

            Button bExpMid = AddRackBtn(tabStudio, "Export 4-Lane .MID", 216, y2, 198, C(3, 105, 161));
            bExpMid.Click += delegate
            {
                string mid = _audio.SeqLooper.ExportStandardMidiFile(_rendersDir, _audio.ArpBpm, _audio.BaseMidiNote, _audio.OctaveSpan);
                _lblRecTimer.Text = "Exported SMF: " + Path.GetFileName(mid);
                SetStatus("Exported 4-Lane Polyrhythmic Standard MIDI File -> " + mid);
            };
            y2 += 34;

            Button bSendAv = AddRackBtn(tabStudio, "Send Last .WAV -> OmegaAV", 10, y2, 246, C(109, 40, 217));
            bSendAv.Click += delegate { SendLastWavToOmegaAv(); };

            Button bOpenRnd = AddRackBtn(tabStudio, "Open Renders", 262, y2, 152, C(30, 58, 138));
            bOpenRnd.Click += delegate { Process.Start("explorer.exe", "\"" + _rendersDir + "\""); };
            y2 += 36;

            _lblRecTimer = new Label();
            _lblRecTimer.UseMnemonic = false;
            _lblRecTimer.SetBounds(10, y2, 404, 24);
            _lblRecTimer.Font = new Font("Consolas", 8.2f);
            _lblRecTimer.ForeColor = C(148, 163, 184);
            _lblRecTimer.Text = "Renders -> 04_omega_synth\\renders\\ (.wav, .mid, .png, .svg)";
            tabStudio.Controls.Add(_lblRecTimer);

            // =========================================================================
            // TAB 3: SOUND FILE -> GEOMETRIC SHAPE, RESYNTHESIS & OMEGABROWSER PIPE!
            // =========================================================================
            int y3 = 8;
            RackHeader(tabAudioLab, "1. UPLOAD SOUND FILE -> GEOMETRIC SHAPE & RESYNTH", ref y3);

            Button bOpenAudio = AddRackBtn(tabAudioLab, "Load Audio File...", 10, y3, 210, C(3, 105, 161));
            bOpenAudio.Click += delegate { BrowseAndLoadAudioClip(); };

            _btnClipPlay = AddRackBtn(tabAudioLab, "Play", 226, y3, 92, C(5, 150, 105));
            _btnClipPlay.Click += delegate
            {
                if (_audio.ActiveClip == null) { BrowseAndLoadAudioClip(); return; }
                _audio.WebPipeActive = false;
                _audio.ClipPlaying = !_audio.ClipPlaying;
                if (_audio.ClipPlaying && _cmbViewMode.SelectedIndex == 0) _cmbViewMode.SelectedIndex = 3;
                SyncControlsFromEngine();
            };

            _btnClipLoop = AddRackBtn(tabAudioLab, "Loop: ON", 324, y3, 100, C(15, 118, 110));
            _btnClipLoop.Click += delegate { _audio.ClipLooping = !_audio.ClipLooping; SyncControlsFromEngine(); };
            y3 += 33;

            _trkClipScrub = new TrackBar();
            _trkClipScrub.AutoSize = false;
            _trkClipScrub.TickStyle = TickStyle.None;
            _trkClipScrub.Minimum = 0; _trkClipScrub.Maximum = 1000; _trkClipScrub.Value = 0;
            _trkClipScrub.SetBounds(8, y3, 418, 20);
            _trkClipScrub.MouseDown += delegate { _scrubbingClip = true; };
            _trkClipScrub.MouseUp += delegate { _audio.SeekClipNormalized(_trkClipScrub.Value / 1000.0); _scrubbingClip = false; };
            _trkClipScrub.Scroll += delegate { if (_scrubbingClip) _audio.SeekClipNormalized(_trkClipScrub.Value / 1000.0); };
            tabAudioLab.Controls.Add(_trkClipScrub);
            y3 += 23;

            Button bCloneFile = AddRackBtn(tabAudioLab, "Clone -> Playable MPE #09", 10, y3, 232, C(190, 24, 93));
            bCloneFile.Click += delegate { CloneActiveClipIntoResynthPreset(); };

            _chkClipSymFx = new CheckBox();
            _chkClipSymFx.UseMnemonic = false;
            _chkClipSymFx.Text = "E8 + Formant FX";
            _chkClipSymFx.SetBounds(250, y3 + 3, 178, 22);
            _chkClipSymFx.Font = new Font("Segoe UI Semibold", 7.8f);
            _chkClipSymFx.ForeColor = C(192, 132, 252);
            _chkClipSymFx.CheckedChanged += delegate { _audio.ClipSympatheticFx = _chkClipSymFx.Checked; };
            tabAudioLab.Controls.Add(_chkClipSymFx);
            y3 += 33;

            _lblAudioDnaSummary = new Label();
            _lblAudioDnaSummary.UseMnemonic = false;
            _lblAudioDnaSummary.SetBounds(10, y3, 416, 42);
            _lblAudioDnaSummary.Font = new Font("Consolas", 7.8f);
            _lblAudioDnaSummary.ForeColor = C(125, 211, 252);
            _lblAudioDnaSummary.Text = "Drop any .wav/.mp3/.flac/.m4a/.ogg onto the window to view\r\nits 3D Cymatic Rose, Lissajous Knot & Chladni(m,n) shape!";
            tabAudioLab.Controls.Add(_lblAudioDnaSummary);
            y3 += 46;

            RackHeader(tabAudioLab, "2. OMEGABROWSER (:9778) YOUTUBE & WEB-AUDIO PIPE", ref y3);

            _btnPipeToggle = AddRackBtn(tabAudioLab, "Web/YouTube Pipe: OFF", 10, y3, 208, C(14, 116, 144));
            _btnPipeToggle.Click += delegate
            {
                ActivateLiveBrowserPipeUi(!_audio.WebPipeActive, _audio.WasapiPipe.PipeTitle, _audio.WasapiPipe.PipeUrl);
            };

            Button bFreezeWeb = AddRackBtn(tabAudioLab, "Freeze 2s -> MPE #09", 224, y3, 200, C(124, 58, 237));
            bFreezeWeb.Click += delegate { FreezeLiveWebAudioToResynth(); };
            y3 += 33;

            _txtBrowserUrl = new TextBox();
            _txtBrowserUrl.SetBounds(10, y3 + 2, 212, 24);
            _txtBrowserUrl.BackColor = C(6, 9, 17); _txtBrowserUrl.ForeColor = Color.White;
            _txtBrowserUrl.BorderStyle = BorderStyle.FixedSingle; _txtBrowserUrl.Font = new Font("Consolas", 8.1f);
            _txtBrowserUrl.Text = "https://www.youtube.com";
            tabAudioLab.Controls.Add(_txtBrowserUrl);

            Button bOpenInBr = AddRackBtn(tabAudioLab, "Open & Pipe", 228, y3, 98, C(2, 132, 199));
            bOpenInBr.Click += delegate { OpenUrlInOmegaBrowserAndPipe(); };

            Button bPlayPauseBr = AddRackBtn(tabAudioLab, "Play/Pause", 332, y3, 92, C(67, 56, 202));
            bPlayPauseBr.Click += delegate { RemoteControlOmegaBrowserMedia("toggle"); };
            y3 += 33;

            _chkPipeSymDrone = new CheckBox();
            _chkPipeSymDrone.UseMnemonic = false;
            _chkPipeSymDrone.Text = "Add Pitch-Locked E8 Sympathetic Drone (Zero Feedback)";
            _chkPipeSymDrone.SetBounds(12, y3, 414, 22);
            _chkPipeSymDrone.Font = new Font("Segoe UI Semibold", 7.8f);
            _chkPipeSymDrone.ForeColor = C(52, 211, 153);
            _chkPipeSymDrone.CheckedChanged += delegate
            {
                _audio.WebPipeSympatheticDrone = _chkPipeSymDrone.Checked;
                if (!_chkPipeSymDrone.Checked) _audio.ReleaseVoice(790);
            };
            tabAudioLab.Controls.Add(_chkPipeSymDrone);
            y3 += 24;

            _lblPipeTrackInfo = new Label();
            _lblPipeTrackInfo.UseMnemonic = false;
            _lblPipeTrackInfo.SetBounds(10, y3, 416, 42);
            _lblPipeTrackInfo.Font = new Font("Consolas", 7.8f);
            _lblPipeTrackInfo.ForeColor = C(250, 204, 21);
            _lblPipeTrackInfo.Text = "Click [-> Synth] in OmegaBrowser v0.6.3 toolbar on any\r\nYouTube/SoundCloud tab for 60fps WASAPI live geometry!";
            tabAudioLab.Controls.Add(_lblPipeTrackInfo);
            y3 += 46;

            RackHeader(tabAudioLab, "3. EXPORT GEOMETRY ART (.PNG / .SVG / CAPSULE)", ref y3);

            Button bExpPng = AddRackBtn(tabAudioLab, "Export .PNG", 10, y3, 132, C(5, 150, 105));
            bExpPng.Click += delegate { ExportActiveGeometryPng(); };

            Button bExpSvg = AddRackBtn(tabAudioLab, "Export .SVG", 148, y3, 132, C(109, 40, 217));
            bExpSvg.Click += delegate { ExportActiveGeometrySvg(); };

            Button bExpCap = AddRackBtn(tabAudioLab, "Save Capsule", 286, y3, 138, C(180, 83, 9));
            bExpCap.Click += delegate { ExportCompleteSoundCapsule(); };

            // =========================================================================
            // TAB 4: OPTIONAL LAPTOP MIC INPUT (ZERO-FEEDBACK) & GENERATIVE AUTO-EVOLVE!
            // =========================================================================
            int y4 = 8;
            RackHeader(tabMicEvolve, "1. OPTIONAL LAPTOP MIC AMBIENT & VOICE INPUT", ref y4);

            RackLabel(tabMicEvolve, "Microphone Input Device (Opt-In, Zero Speaker Echo):", 10, y4); y4 += 17;
            _cmbMicDevice = RackCombo(tabMicEvolve, 10, y4, 322);
            RefreshMicDeviceList();

            Button bRefMic = AddRackBtn(tabMicEvolve, "Refresh", 338, y4 - 2, 86, C(30, 58, 138));
            bRefMic.Click += delegate
            {
                RefreshMicDeviceList();
                SetStatus("Refreshed Windows audio input / laptop microphone devices.");
            };
            y4 += 29;

            _btnMicToggle = AddRackBtn(tabMicEvolve, "Laptop Mic: OFF", 10, y4, 206, C(15, 118, 110));
            _btnMicToggle.Click += delegate { ToggleLaptopMicUi(!_audio.MicInputActive); };

            Button bSampleMic = AddRackBtn(tabMicEvolve, "Sample Mic 2s -> MPE #09", 222, y4, 202, C(190, 24, 93));
            bSampleMic.Click += delegate { SampleLiveMicToResynth(); };
            y4 += 33;

            RackLabel(tabMicEvolve, "Live Mic Synthesis Mode (100% Feedback-Protected):", 10, y4); y4 += 17;
            _cmbMicMode = RackCombo(tabMicEvolve, 10, y4, 414);
            _cmbMicMode.Items.AddRange(new object[]
            {
                "0: Silent Cymatic & Rose Visualizer (Zero Speaker Audio)",
                "1: Sing / Whistle Pitch Tracker -> Bowed Resonator",
                "2: Ambient Room Noise -> E8 Harmonic Triad Pad",
                "3: Clap / Desk-Tap Transient -> Plate Ripple Striker"
            });
            _cmbMicMode.SelectedIndex = 0;
            _cmbMicMode.SelectedIndexChanged += delegate
            {
                _audio.MicSynthMode = _cmbMicMode.SelectedIndex;
                _audio.ReleaseVoice(791); _audio.ReleaseVoice(792); _audio.ReleaseVoice(793); _audio.ReleaseVoice(794);
                SetStatus("Laptop Mic mode -> " + _cmbMicMode.SelectedItem);
            };
            y4 += 28;

            _pbMicLevel = new ProgressBar();
            _pbMicLevel.SetBounds(10, y4, 414, 13);
            _pbMicLevel.Maximum = 100;
            tabMicEvolve.Controls.Add(_pbMicLevel);
            y4 += 17;

            _lblMicLiveReadout = new Label();
            _lblMicLiveReadout.UseMnemonic = false;
            _lblMicLiveReadout.SetBounds(10, y4, 416, 40);
            _lblMicLiveReadout.Font = new Font("Consolas", 7.8f);
            _lblMicLiveReadout.ForeColor = C(52, 211, 153);
            _lblMicLiveReadout.Text = "Mic OFF (Opt-In). Click [Laptop Mic] to pick up room noise,\r\nclaps, humming, or whistling with zero speaker feedback!";
            tabMicEvolve.Controls.Add(_lblMicLiveReadout);
            y4 += 43;

            _trkMicGain = RackSlider(tabMicEvolve, "Mic Preamp Boost (50%..800%):", 50, 800, 250, ref y4, delegate(int v)
            {
                _audio.MicCapture.GainBoost = v / 100.0f;
            });
            _trkMicGate = RackSlider(tabMicEvolve, "Ambient Noise Gate Threshold:", 0, 25, 2, ref y4, delegate(int v)
            {
                _audio.MicCapture.NoiseGate = v / 100.0f;
            });

            RackHeader(tabMicEvolve, "2. GENERATIVE AMBIENT DRIFT & LAB REFERENCE", ref y4);

            _btnAutoEvolve = AddRackBtn(tabMicEvolve, "Ambient Auto-Evolve Drift: OFF", 10, y4, 414, C(67, 56, 202));
            _btnAutoEvolve.Click += delegate { ToggleAutoEvolveUi(); };
            y4 += 32;

            _trkEvolveDensity = RackSlider(tabMicEvolve, "Generative Drift Speed & Chord Density:", 5, 100, 50, ref y4, delegate(int v)
            {
                _audio.AutoEvolveDensity = v / 100.0f;
            });

            _btnSweepGen = AddRackBtn(tabMicEvolve, "Sweep", 10, y4, 98, C(180, 83, 9));
            _btnSweepGen.Click += delegate { ToggleLabGenerator(1, "Chladni Eigen-Sweep (55 -> 880 Hz)"); };

            Button bFork432 = AddRackBtn(tabMicEvolve, "432 Hz", 114, y4, 98, C(15, 118, 110));
            bFork432.Click += delegate { ToggleLabGenerator(2, "432 Hz Veridian Reference Fork"); };

            Button bFork528 = AddRackBtn(tabMicEvolve, "528 Hz", 218, y4, 98, C(30, 58, 138));
            bFork528.Click += delegate { ToggleLabGenerator(3, "528 Hz Solfeggio Plate Tone"); };

            Button bBinBeat = AddRackBtn(tabMicEvolve, "15.97Hz", 322, y4, 102, C(88, 28, 135));
            bBinBeat.Click += delegate { ToggleLabGenerator(4, "15.965 Hz Aperiodic Binaural Beat (216/231.97 Hz)"); };

            _rightTabs.TabPages.Add(tabSynth);
            _rightTabs.TabPages.Add(tabStudio);
            _rightTabs.TabPages.Add(tabAudioLab);
            _rightTabs.TabPages.Add(tabMicEvolve);

            host.Controls.Add(_rightTabs);
            host.Controls.Add(patchStrip);
        }

        private void RefreshPresetDropdownNames()
        {
            int prev = _cmbPreset != null ? _cmbPreset.SelectedIndex : 0;
            if (_cmbPreset == null) return;
            _syncingUi = true;
            try
            {
                _cmbPreset.Items.Clear();
                for (int i = 0; i < E8WaveLatticeMath.Presets.Length; i++)
                    _cmbPreset.Items.Add(E8WaveLatticeMath.Presets[i].Name);
                _cmbPreset.SelectedIndex = Math.Max(0, Math.Min(_cmbPreset.Items.Count - 1, prev));
            }
            finally { _syncingUi = false; }
        }

        private void RefreshPatchBankDropdown()
        {
            if (_cmbPatchBank == null) return;
            _syncingUi = true;
            try
            {
                List<OmegaPatchBankEntry> list = OmegaSynthPatchBank.EnsureFactoryPatchesAndList(_patchesDir);
                _cmbPatchBank.Items.Clear();
                for (int i = 0; i < list.Count; i++) _cmbPatchBank.Items.Add(list[i]);
                if (_cmbPatchBank.Items.Count > 0 && _cmbPatchBank.SelectedIndex < 0) _cmbPatchBank.SelectedIndex = 0;
            }
            finally { _syncingUi = false; }
        }

        private void SaveCurrentPatchDialog()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.InitialDirectory = _patchesDir;
                sfd.Filter = "OmegaSynth v1.0 Patch (*.osynth)|*.osynth";
                sfd.FileName = "custom_patch_" + DateTime.Now.ToString("HHmmss") + ".osynth";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    string pName = Path.GetFileNameWithoutExtension(sfd.FileName);
                    OmegaSynthPatchBank.SaveEnginePatchToFile(sfd.FileName, pName, _audio);
                    RefreshPatchBankDropdown();
                    SetStatus("Saved .osynth Patch + 4-Lane Sequencer + MPE Loop -> " + sfd.FileName);
                }
            }
        }

        private void LoadPatchFileDialog()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.InitialDirectory = _patchesDir;
                ofd.Filter = "OmegaSynth Patch & Audio (*.osynth;*.wav;*.mp3;*.flac;*.m4a;*.ogg)|*.osynth;*.wav;*.mp3;*.flac;*.m4a;*.ogg|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadSoundOrPatchFileAsync(ofd.FileName, true);
                }
            }
        }

        private void BrowseAndLoadAudioClip()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                string dlAudio = Path.Combine(_suiteRoot, "downloads", "Audio");
                ofd.InitialDirectory = Directory.Exists(dlAudio) ? dlAudio : _rendersDir;
                ofd.Filter = "Audio Files (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.webm)|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.webm|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadSoundOrPatchFileAsync(ofd.FileName, true);
                }
            }
        }

        private void LoadSoundOrPatchFileAsync(string filePath, bool autoCloneAndPlay)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            string ext = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();
            if (ext == ".osynth")
            {
                string nm = OmegaSynthPatchBank.LoadPatchIntoEngine(filePath, _audio, _gpu);
                RefreshPresetDropdownNames();
                RefreshPatchBankDropdown();
                SyncControlsFromEngine();
                SetStatus("Loaded .osynth Patch -> " + nm);
                return;
            }

            SetStatus("Decoding & extracting Geometric Sound-Shape + 8-Partial Spectral DNA: " + Path.GetFileName(filePath) + "...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                OmegaDecodedAudioClip clip = OmegaAudioFileDecoder.DecodeAndAnalyzeTo48kHz(filePath, out err);
                if (IsDisposed) return;
                BeginInvoke((Action)delegate
                {
                    if (clip == null)
                    {
                        SetStatus("Audio decode error (" + Path.GetFileName(filePath) + "): " + err);
                        return;
                    }
                    _audio.SetWebPipeEnabled(false, null, null);
                    _audio.SetLoadedAudioClip(clip, autoCloneAndPlay);
                    E8WaveLatticeMath.UpdateCustomResynthesisPreset(
                        Path.GetFileNameWithoutExtension(clip.DisplayName),
                        clip.ClonedRatios, clip.ClonedWeights, clip.ClonedDecaySec, clip.ClonedBowMix, 2);
                    RefreshPresetDropdownNames();

                    _cmbViewMode.SelectedIndex = 3; // Auto-switch to Sound-Shape Rose + Lissajous + Chladni Hologram!
                    _rightTabs.SelectedIndex = 2;
                    _lblAudioDnaSummary.Text = string.Format(CultureInfo.InvariantCulture,
                        "{0} ({1:0.0}s) | Root: {2} ({3:0.0} Hz)\r\nShape: Chladni({4},{5}) | Rose Petals: {6} | Centroid: {7:0} Hz",
                        clip.DisplayName, clip.DurationSec, clip.DominantNote, clip.DominantHz,
                        clip.SuggestedM, clip.SuggestedN, clip.RosePetals, clip.CentroidHz);
                    SyncControlsFromEngine();
                    SetStatus("Sound-Shape LIVE: " + clip.DisplayName + " -> Chladni(" + clip.SuggestedM + "," + clip.SuggestedN + ") + Cloned into MPE Preset #09!");
                });
            });
        }

        private void CloneActiveClipIntoResynthPreset()
        {
            OmegaDecodedAudioClip clip = _audio.ActiveClip;
            if (clip == null)
            {
                if (_audio.WebPipeActive) { FreezeLiveWebAudioToResynth(); return; }
                BrowseAndLoadAudioClip();
                return;
            }
            E8WaveLatticeMath.UpdateCustomResynthesisPreset(
                Path.GetFileNameWithoutExtension(clip.DisplayName),
                clip.ClonedRatios, clip.ClonedWeights, clip.ClonedDecaySec, clip.ClonedBowMix, 2);
            _audio.PresetIndex = E8WaveLatticeMath.CustomResynthPresetIndex;
            _audio.ClipPlaying = false;
            RefreshPresetDropdownNames();
            SyncControlsFromEngine();
            _audio.TriggerChordPreview(0);
            SetStatus("Cloned [" + clip.DisplayName + "] into Playable MPE Preset #09 — play keys Z..M or Pixel 10 Ribbon!");
        }

        private void ActivateLiveBrowserPipeUi(bool enable, string title, string url)
        {
            _audio.SetWebPipeEnabled(enable, title, url);
            if (enable)
            {
                if (_cmbViewMode.SelectedIndex == 0) _cmbViewMode.SelectedIndex = 3;
                string lbl = string.IsNullOrEmpty(_audio.WasapiPipe.PipeTitle) ? "Live Web / YouTube Audio" : _audio.WasapiPipe.PipeTitle;
                _lblPipeTrackInfo.Text = "LIVE PIPE: " + (lbl.Length > 46 ? lbl.Substring(0, 46) : lbl) + "\r\nWASAPI 48kHz Zero-Feedback -> Cymatic Rose + Chladni!";
                SetStatus("Live Web/YouTube WASAPI Pipe ACTIVE — visualizing " + lbl + " at " + _measuredFps + " FPS!");
            }
            else
            {
                _lblPipeTrackInfo.Text = "Web Pipe Paused. Click [Live Web/YouTube Pipe] or [-> Synth] in OmegaBrowser.";
                SetStatus("Live Web/YouTube WASAPI Pipe paused.");
            }
            SyncControlsFromEngine();
        }

        private void FreezeLiveWebAudioToResynth()
        {
            if (!_audio.WasapiPipe.IsCapturing)
            {
                _audio.WasapiPipe.Start();
                SetStatus("Started WASAPI Pipe — play 2 seconds of YouTube/Web audio and click Freeze again!");
                return;
            }
            OmegaDecodedAudioClip frozen = _audio.WasapiPipe.CaptureFreezeClip(2000);
            E8WaveLatticeMath.UpdateCustomResynthesisPreset(
                "WebPipe " + frozen.DominantNote,
                frozen.ClonedRatios, frozen.ClonedWeights, frozen.ClonedDecaySec, frozen.ClonedBowMix, 2);
            _audio.SetWebPipeEnabled(false, null, null);
            _audio.PresetIndex = E8WaveLatticeMath.CustomResynthPresetIndex;
            _audio.ChladniM = frozen.SuggestedM;
            _audio.ChladniN = frozen.SuggestedN;
            RefreshPresetDropdownNames();
            SyncControlsFromEngine();
            _audio.TriggerChordPreview(0);
            _lblPipeTrackInfo.Text = string.Format(CultureInfo.InvariantCulture,
                "FROZEN -> MPE #09! Root: {0} ({1:0.0} Hz) | Chladni({2},{3})",
                frozen.DominantNote, frozen.DominantHz, frozen.SuggestedM, frozen.SuggestedN);
            SetStatus("Froze 2.0s of Web/YouTube audio -> Cloned into Playable MPE Preset #09 (" + frozen.DominantNote + ")!");
        }

        private void OpenUrlInOmegaBrowserAndPipe()
        {
            string u = (_txtBrowserUrl.Text ?? "").Trim();
            if (string.IsNullOrEmpty(u)) u = "https://www.youtube.com";
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                u = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(u);

            ActivateLiveBrowserPipeUi(true, "OmegaBrowser -> " + u, u);
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (!ProbeLocalPort(9778) && File.Exists(_brExe))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(_brExe);
                        psi.WorkingDirectory = _brDir;
                        Process.Start(psi);
                        for (int i = 0; i < 20; i++) { Thread.Sleep(250); if (ProbeLocalPort(9778)) break; }
                    }
                    catch { }
                }
                if (ProbeLocalPort(9778))
                {
                    HttpGetFast("http://127.0.0.1:9778/api/media?action=open&url=" + Uri.EscapeDataString(u), 3000);
                    SetStatus("Opened " + u + " in OmegaBrowser (:9778) -> Piping live audio into OmegaSynth!");
                }
                else
                {
                    try { Process.Start(u); } catch { }
                    SetStatus("Opened " + u + " -> Capturing live WASAPI audio into OmegaSynth!");
                }
            });
        }

        private void RemoteControlOmegaBrowserMedia(string action)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (!ProbeLocalPort(9778)) { SetStatus("OmegaBrowser (:9778) is not running yet — click 'Open & Pipe' first!"); return; }
                string resp = HttpGetFast("http://127.0.0.1:9778/api/media?action=" + Uri.EscapeDataString(action), 2500);
                if (!string.IsNullOrEmpty(resp))
                {
                    Match mt = Regex.Match(resp, "\"title\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                    if (mt.Success && !IsDisposed)
                    {
                        string t = mt.Groups[1].Value;
                        BeginInvoke((Action)delegate { _lblPipeTrackInfo.Text = "OmegaBrowser Tab: " + (t.Length > 44 ? t.Substring(0, 44) : t); });
                    }
                }
            });
        }

        private void ExportActiveGeometryPng()
        {
            string title = _audio.WebPipeActive ? _audio.WasapiPipe.PipeTitle
                : (_audio.ActiveClip != null ? _audio.ActiveClip.DisplayName : E8WaveLatticeMath.Presets[_audio.PresetIndex].Name);
            string p = OmegaCymaticGeometryExporter.ExportHighResPngPrint(
                _rendersDir, _stage, title, _audio.ChladniM, _audio.ChladniN, _spec32, _waveL, _waveR);
            SetStatus("Exported Acoustic Geometry .PNG Print -> " + p);
        }

        private void ExportActiveGeometrySvg()
        {
            string title = _audio.WebPipeActive ? _audio.WasapiPipe.PipeTitle
                : (_audio.ActiveClip != null ? _audio.ActiveClip.DisplayName : E8WaveLatticeMath.Presets[_audio.PresetIndex].Name);
            string p = OmegaCymaticGeometryExporter.ExportVectorSvgBlueprint(
                _rendersDir, title, _audio.ChladniM, _audio.ChladniN, _spec32, _waveL, _waveR);
            SetStatus("Exported Vector Cymatic Nodal Blueprint (.SVG) -> " + p);
        }

        private void ExportCompleteSoundCapsule()
        {
            string title = _audio.MicInputActive ? "LaptopMic_Cymatic"
                : (_audio.WebPipeActive ? _audio.WasapiPipe.PipeTitle
                : (_audio.ActiveClip != null ? _audio.ActiveClip.DisplayName : E8WaveLatticeMath.Presets[_audio.PresetIndex].Name));
            string dir = OmegaCymaticGeometryExporter.ExportSoundShapeCapsule(
                _printsDir, _stage, title, _audio, _spec32, _waveL, _waveR);
            SetStatus("Saved Complete Sound-Shape Capsule (.PNG + .SVG + .OSYNTH) -> " + dir);
        }

        private void RefreshMicDeviceList()
        {
            if (_cmbMicDevice == null) return;
            List<string> devs = OmegaAmbientMicCapture.ListInputDevices();
            int prev = _cmbMicDevice.SelectedIndex;
            _cmbMicDevice.Items.Clear();
            for (int i = 0; i < devs.Count; i++) _cmbMicDevice.Items.Add(devs[i]);
            _cmbMicDevice.SelectedIndex = (prev >= 0 && prev < _cmbMicDevice.Items.Count) ? prev : 0;
        }

        private void ToggleLaptopMicUi(bool enable)
        {
            int devChoice = (_cmbMicDevice != null && _cmbMicDevice.SelectedIndex > 0) ? (_cmbMicDevice.SelectedIndex - 1) : -1;
            _audio.SetMicInputEnabled(enable, devChoice);
            if (enable)
            {
                if (_cmbViewMode.SelectedIndex == 0) _cmbViewMode.SelectedIndex = 3;
                SetStatus("Laptop Microphone LIVE (Zero-Feedback) — room noise, claps & voice sculpt 60fps cymatic geometry!");
            }
            else
            {
                SetStatus("Laptop Microphone input turned OFF.");
            }
            SyncControlsFromEngine();
        }

        private void SampleLiveMicToResynth()
        {
            if (!_audio.MicInputActive || !_audio.MicCapture.IsCapturing)
            {
                ToggleLaptopMicUi(true);
                SetStatus("Started Laptop Mic — hum, whistle, or tap an object for 2 seconds, then click 'Sample Mic 2s -> MPE #09'!");
                return;
            }
            OmegaDecodedAudioClip sample = _audio.MicCapture.CaptureMicFreezeClip(2000);
            E8WaveLatticeMath.UpdateCustomResynthesisPreset(
                "Mic " + sample.DominantNote,
                sample.ClonedRatios, sample.ClonedWeights, sample.ClonedDecaySec, sample.ClonedBowMix, 2);
            _audio.SetMicInputEnabled(false, -1);
            _audio.PresetIndex = E8WaveLatticeMath.CustomResynthPresetIndex;
            _audio.ChladniM = sample.SuggestedM;
            _audio.ChladniN = sample.SuggestedN;
            RefreshPresetDropdownNames();
            SyncControlsFromEngine();
            _audio.TriggerChordPreview(0);
            _lblMicLiveReadout.Text = string.Format(CultureInfo.InvariantCulture,
                "SAMPLED MIC -> MPE #09! Root: {0} ({1:0.0} Hz) | Chladni({2},{3})",
                sample.DominantNote, sample.DominantHz, sample.SuggestedM, sample.SuggestedN);
            SetStatus("Cloned 2.0s of Laptop Mic audio into Playable MPE Preset #09 (" + sample.DominantNote + ")!");
        }

        private void ToggleAutoEvolveUi()
        {
            _audio.AutoEvolveEnabled = !_audio.AutoEvolveEnabled;
            SyncControlsFromEngine();
            SetStatus(_audio.AutoEvolveEnabled
                ? "Generative Ambient Auto-Evolve LIVE — Golden-Phi chord walker + Chladni(m,n) drift active!"
                : "Generative Ambient Auto-Evolve paused.");
        }

        private void ToggleLabGenerator(int mode, string label)
        {
            _audio.LabToneMode = (_audio.LabToneMode == mode) ? 0 : mode;
            _btnSweepGen.BackColor = (_audio.LabToneMode == 1) ? C(220, 38, 38) : C(180, 83, 9);
            SetStatus(_audio.LabToneMode == 0 ? "Acoustic Lab Generator OFF." : ("Acoustic Lab Generator LIVE: " + label));
        }

        private void BringSelfToFront()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                ShowWindow(Handle, 9);
                SetForegroundWindow(Handle);
                Activate();
            }
            catch { }
        }

        private void BuildPolyrhythmSequencerDock(Panel seqDock)
        {
            _btnArp = AddRackBtn(seqDock, "4-Lane Polyrhythm: OFF (Space)", 10, 4, 218, C(51, 65, 85));
            _btnArp.Click += delegate { _audio.ArpEnabled = !_audio.ArpEnabled; SyncControlsFromEngine(); };

            string[] lnTitles = new string[] { "L1:SubBass", "L2:Chladni", "L3:KS Harp", "L4:Bowed" };
            for (int l = 0; l < 4; l++)
            {
                int lIdx = l;
                Button lb = AddRackBtn(seqDock, lnTitles[l], 234 + l * 96, 4, 92, l == 0 ? C(190, 24, 93) : C(30, 41, 59));
                lb.Click += delegate
                {
                    _audio.SeqLooper.SelectedEditLane = lIdx;
                    SyncControlsFromEngine();
                };
                _laneSelBtns[l] = lb;
            }

            _btnLaneMute = AddRackBtn(seqDock, "Lane ON", 622, 4, 82, C(5, 150, 105));
            _btnLaneMute.Click += delegate
            {
                PolyrhythmLane ln = _audio.SeqLooper.Lanes[_audio.SeqLooper.SelectedEditLane];
                ln.Muted = !ln.Muted;
                SyncControlsFromEngine();
            };

            RackLabel(seqDock, "Steps:", 712, 10);
            _nudLaneLen = new NumericUpDown();
            _nudLaneLen.Minimum = 4; _nudLaneLen.Maximum = 16; _nudLaneLen.Value = 16;
            _nudLaneLen.SetBounds(756, 6, 48, 24);
            _nudLaneLen.BackColor = C(6, 9, 17); _nudLaneLen.ForeColor = Color.White;
            _nudLaneLen.ValueChanged += delegate
            {
                if (_syncingUi) return;
                PolyrhythmLane ln = _audio.SeqLooper.Lanes[_audio.SeqLooper.SelectedEditLane];
                ln.StepLength = (int)_nudLaneLen.Value;
                RefreshStepButtons();
            };
            seqDock.Controls.Add(_nudLaneLen);

            Button bEuc5 = AddRackBtn(seqDock, "E(5)", 812, 4, 54, C(15, 118, 110));
            bEuc5.Click += delegate { int li = _audio.SeqLooper.SelectedEditLane; _audio.SeqLooper.ApplyEuclideanToLane(li, 5, _audio.SeqLooper.Lanes[li].StepLength, 1); RefreshStepButtons(); };
            Button bEuc7 = AddRackBtn(seqDock, "E(7)", 870, 4, 54, C(67, 56, 202));
            bEuc7.Click += delegate { int li = _audio.SeqLooper.SelectedEditLane; _audio.SeqLooper.ApplyEuclideanToLane(li, 7, _audio.SeqLooper.Lanes[li].StepLength, 3); RefreshStepButtons(); };

            Label lBpm = new Label();
            lBpm.UseMnemonic = false;
            lBpm.Text = "BPM: 112";
            lBpm.SetBounds(934, 9, 74, 18);
            lBpm.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
            lBpm.ForeColor = C(250, 204, 21);
            seqDock.Controls.Add(lBpm);

            _trkBpm = new TrackBar();
            _trkBpm.AutoSize = false; _trkBpm.TickStyle = TickStyle.None;
            _trkBpm.Minimum = 45; _trkBpm.Maximum = 220; _trkBpm.Value = 112;
            _trkBpm.SetBounds(1010, 5, 130, 24);
            _trkBpm.Scroll += delegate { _audio.ArpBpm = _trkBpm.Value; lBpm.Text = "BPM: " + _trkBpm.Value; };
            seqDock.Controls.Add(_trkBpm);

            for (int i = 0; i < 16; i++)
            {
                int stepIdx = i;
                Button sb = AddRackBtn(seqDock, "", 10 + i * 68, 40, 64, C(22, 30, 48));
                sb.Height = 38;
                sb.Font = new Font("Consolas", 8.2f, FontStyle.Bold);
                sb.Click += delegate
                {
                    PolyrhythmLane ln = _audio.SeqLooper.Lanes[_audio.SeqLooper.SelectedEditLane];
                    ln.Gate[stepIdx] = !ln.Gate[stepIdx];
                    RefreshStepButtons();
                };
                sb.MouseUp += delegate(object s, MouseEventArgs me)
                {
                    if (me.Button == MouseButtons.Right)
                    {
                        PolyrhythmLane ln = _audio.SeqLooper.Lanes[_audio.SeqLooper.SelectedEditLane];
                        ln.Degree[stepIdx] = (ln.Degree[stepIdx] + 2) % 25;
                        RefreshStepButtons();
                    }
                };
                _stepBtns[i] = sb;
            }

            _lblLiveNoteReadout = new Label();
            _lblLiveNoteReadout.UseMnemonic = false;
            _lblLiveNoteReadout.SetBounds(1104, 40, 550, 38);
            _lblLiveNoteReadout.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblLiveNoteReadout.BackColor = C(7, 11, 20);
            _lblLiveNoteReadout.ForeColor = C(52, 211, 153);
            _lblLiveNoteReadout.Font = new Font("Consolas", 8.6f, FontStyle.Bold);
            _lblLiveNoteReadout.TextAlign = ContentAlignment.MiddleLeft;
            _lblLiveNoteReadout.Padding = new Padding(10, 0, 6, 0);
            _lblLiveNoteReadout.Text = "LIVE TUNER :: Idle (Drag Chladni Plate, Drop Audio File, or Pipe YouTube)";
            seqDock.Controls.Add(_lblLiveNoteReadout);

            RackLabel(seqDock, "Gemma AI:", 10, 94);
            _txtGemmaPatch = new TextBox();
            _txtGemmaPatch.SetBounds(86, 90, 662, 26);
            _txtGemmaPatch.BackColor = C(6, 9, 17); _txtGemmaPatch.ForeColor = Color.White;
            _txtGemmaPatch.BorderStyle = BorderStyle.FixedSingle; _txtGemmaPatch.Font = new Font("Consolas", 9.0f);
            _txtGemmaPatch.Text = "Compose a 4-lane polyrhythmic Karplus-Strong harp + Golden Phi quartz bowl piece at 96 BPM";
            seqDock.Controls.Add(_txtGemmaPatch);

            Button bGemma = AddRackBtn(seqDock, "Compose 4-Lane Patch", 756, 87, 184, C(124, 58, 237));
            bGemma.Click += delegate { Compose4LaneViaTandemGemmaAsync(); };

            _chkSyncAvOmsh = new CheckBox();
            _chkSyncAvOmsh.UseMnemonic = false;
            _chkSyncAvOmsh.Text = "+ Gen .omsh -> OmegaAV";
            _chkSyncAvOmsh.Checked = true;
            _chkSyncAvOmsh.SetBounds(948, 91, 184, 22);
            _chkSyncAvOmsh.ForeColor = C(192, 132, 252);
            seqDock.Controls.Add(_chkSyncAvOmsh);

            Label lKeys = new Label();
            lKeys.UseMnemonic = false;
            lKeys.Text = "Keys: Z..M / Q..U | Space=Arp | Drop .WAV/.MP3=Shape | Xbox RT=Bow";
            lKeys.SetBounds(1140, 92, 510, 20);
            lKeys.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lKeys.Font = new Font("Consolas", 8.0f);
            lKeys.ForeColor = C(148, 163, 184);
            seqDock.Controls.Add(lKeys);

            RefreshStepButtons();
        }

        private void RefreshStepButtons()
        {
            PolyrhythmLane ln = _audio.SeqLooper.Lanes[_audio.SeqLooper.SelectedEditLane];
            int cur = ln.Cursor % Math.Max(1, ln.StepLength);
            for (int i = 0; i < 16; i++)
            {
                bool activeLen = (i < ln.StepLength);
                bool on = activeLen && ln.Gate[i];
                bool isPlay = _audio.ArpEnabled && activeLen && (i == cur);
                _stepBtns[i].Enabled = activeLen;
                _stepBtns[i].Text = !activeLen ? ".." : ((i + 1).ToString("00") + ":" + (on ? ("+" + ln.Degree[i]) : "--"));
                _stepBtns[i].BackColor = !activeLen ? C(11, 15, 24) : (isPlay ? C(217, 119, 6) : (on ? C(5, 150, 105) : C(22, 30, 48)));
            }
        }

        private void SyncControlsFromEngine()
        {
            _syncingUi = true;
            try
            {
                if (_cmbPreset.SelectedIndex != _audio.PresetIndex && _audio.PresetIndex < _cmbPreset.Items.Count)
                    _cmbPreset.SelectedIndex = _audio.PresetIndex;
                if (_cmbScale.SelectedIndex != _audio.ScaleIndex && _audio.ScaleIndex < _cmbScale.Items.Count)
                    _cmbScale.SelectedIndex = _audio.ScaleIndex;

                _nudM.Value = Math.Max(1, Math.Min(12, _audio.ChladniM));
                _nudN.Value = Math.Max(1, Math.Min(12, _audio.ChladniN));
                _chkAutoMN.Checked = _audio.AutoChladniFromPitch;
                _chkFormantOn.Checked = _audio.FormantFilterEnabled;

                _trkSnap.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_audio.ScaleSnap * 100.0f)));
                _trkE8Mix.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_audio.E8CouplingOverride * 100.0f)));
                _trkHall.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_audio.HallShimmerOverride * 100.0f)));
                _trkDecay.Value = Math.Max(25, Math.Min(250, (int)Math.Round(_audio.DecayMultiplier * 100.0f)));
                _trkFormant.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_audio.FormantVowelMorph * 100.0f)));
                _trkVol.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_audio.MasterVolume * 100.0f)));
                _trkBpm.Value = Math.Max(45, Math.Min(220, _audio.ArpBpm));

                _btnDrone.Text = _audio.DroneLatchMode ? "Drone Latch: ON" : "Drone Latch: OFF";
                _btnDrone.BackColor = _audio.DroneLatchMode ? C(5, 150, 105) : C(51, 65, 85);

                _btnGyro.Text = _audio.GyroPitchBendEnabled ? "Pixel Gyro Bend: ON" : "Pixel Gyro Bend: OFF";
                _btnGyro.BackColor = _audio.GyroPitchBendEnabled ? C(5, 150, 105) : C(71, 85, 105);

                _btnArp.Text = _audio.ArpEnabled ? "4-Lane Polyrhythm: LIVE" : "4-Lane Polyrhythm: OFF (Space)";
                _btnArp.BackColor = _audio.ArpEnabled ? C(5, 150, 105) : C(51, 65, 85);

                _btnRecWav.Text = _audio.IsRecordingWav ? "STOP & Save .WAV" : "Record 48kHz .WAV";
                _btnRecWav.BackColor = _audio.IsRecordingWav ? C(220, 38, 38) : C(153, 27, 27);

                _btnClipPlay.Text = _audio.ClipPlaying ? "Pause" : "Play";
                _btnClipPlay.BackColor = _audio.ClipPlaying ? C(217, 119, 6) : C(5, 150, 105);
                _btnClipLoop.Text = _audio.ClipLooping ? "Loop: ON" : "Loop: OFF";
                _btnClipLoop.BackColor = _audio.ClipLooping ? C(15, 118, 110) : C(51, 65, 85);

                _btnPipeToggle.Text = _audio.WebPipeActive ? "Web/YouTube Pipe: LIVE" : "Web/YouTube Pipe: OFF";
                _btnPipeToggle.BackColor = _audio.WebPipeActive ? C(220, 38, 38) : C(14, 116, 144);
                _btnTopPipe.Text = _audio.WebPipeActive ? "Web Pipe: ON" : "Web Pipe: OFF";
                _btnTopPipe.BackColor = _audio.WebPipeActive ? C(220, 38, 38) : C(14, 116, 144);

                _btnMicToggle.Text = _audio.MicInputActive ? "Laptop Mic: LIVE" : "Laptop Mic: OFF";
                _btnMicToggle.BackColor = _audio.MicInputActive ? C(220, 38, 38) : C(15, 118, 110);
                _btnTopMic.Text = _audio.MicInputActive ? "Mic: LIVE" : "Mic: OFF";
                _btnTopMic.BackColor = _audio.MicInputActive ? C(220, 38, 38) : C(15, 118, 110);

                _btnAutoEvolve.Text = _audio.AutoEvolveEnabled ? "Ambient Auto-Evolve Drift: LIVE" : "Ambient Auto-Evolve Drift: OFF";
                _btnAutoEvolve.BackColor = _audio.AutoEvolveEnabled ? C(5, 150, 105) : C(67, 56, 202);
                _btnTopEvolve.Text = _audio.AutoEvolveEnabled ? "Evolve: ON" : "Evolve: OFF";
                _btnTopEvolve.BackColor = _audio.AutoEvolveEnabled ? C(5, 150, 105) : C(67, 56, 202);

                int selLane = _audio.SeqLooper.SelectedEditLane;
                for (int l = 0; l < 4; l++)
                {
                    bool muted = _audio.SeqLooper.Lanes[l].Muted;
                    _laneSelBtns[l].BackColor = (l == selLane) ? C(190, 24, 93) : (muted ? C(30, 35, 46) : C(30, 58, 138));
                }
                PolyrhythmLane curLn = _audio.SeqLooper.Lanes[selLane];
                _btnLaneMute.Text = curLn.Muted ? "MUTED" : "Lane ON";
                _btnLaneMute.BackColor = curLn.Muted ? C(127, 29, 29) : C(5, 150, 105);
                _nudLaneLen.Value = Math.Max(4, Math.Min(16, curLn.StepLength));

                int lst = _audio.SeqLooper.LoopState;
                _btnLoopRec.Text = lst == 0 ? "REC / Overdub MPE Loop" : (lst == 1 ? "Recording... (Click=Play)" : (lst == 2 ? "Overdub onto Loop" : "Overdubbing... (Click=Lock)"));
                _btnLoopRec.BackColor = lst == 1 ? C(220, 38, 38) : (lst >= 2 ? C(217, 119, 6) : C(180, 83, 9));

                RefreshStepButtons();
            }
            finally { _syncingUi = false; }
        }

        private void UpdateUiTelemetryAndInboxTick()
        {
            if (IsDisposed || WindowState == FormWindowState.Minimized) return;

            // Check synth_inbox.cmd for hot-dropped files from OmegaBrowser or OmegaLabLauncher!
            if (File.Exists(_inboxCmdPath))
            {
                try
                {
                    string cmdPath = File.ReadAllText(_inboxCmdPath, Encoding.UTF8).Trim();
                    File.Delete(_inboxCmdPath);
                    if (!string.IsNullOrEmpty(cmdPath) && File.Exists(cmdPath))
                    {
                        BringSelfToFront();
                        LoadSoundOrPatchFileAsync(cmdPath, true);
                    }
                }
                catch { }
            }

            _pbLeft.Value = Math.Max(0, Math.Min(100, (int)(_audio.PeakLevelL * 100.0f)));
            _pbRight.Value = Math.Max(0, Math.Min(100, (int)(_audio.PeakLevelR * 100.0f)));

            if (!_scrubbingClip && _audio.ClipTotalSamples > 0)
            {
                int posPermil = (int)Math.Round(1000.0 * _audio.ClipPlayheadSample / Math.Max(1, _audio.ClipTotalSamples));
                _trkClipScrub.Value = Math.Max(0, Math.Min(1000, posPermil));
            }

            if (_pbMicLevel != null)
                _pbMicLevel.Value = Math.Max(0, Math.Min(100, (int)(_audio.MicCapture.PeakLevel * 100.0f)));
            if (_audio.MicInputActive && _lblMicLiveReadout != null)
            {
                string mNote = "--"; int mCents = 0;
                if (_audio.MicCapture.DetectedHz > 50.0f) E8WaveLatticeMath.FormatNoteAndCents(_audio.MicCapture.DetectedHz, out mNote, out mCents);
                _lblMicLiveReadout.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} | RMS:{1:0}% | Pitch:{2:0.0}Hz ({3})\r\nMode #{4} | Zero-Feedback Speaker Guard: ACTIVE",
                    _audio.MicCapture.StatusMessage, _audio.MicCapture.RmsLevel * 100.0f,
                    _audio.MicCapture.DetectedHz, mNote, _audio.MicSynthMode);
            }

            string srcBadge = _audio.MicInputActive ? "LAPTOP-MIC LIVE"
                : (_audio.WebPipeActive ? "WEB-PIPE LIVE"
                : (_audio.ClipPlaying ? "FILE-SHAPE LIVE"
                : (_audio.AutoEvolveEnabled ? "AUTO-EVOLVE LIVE" : _audio.AudioStatus)));
            _lblTelemetryRow2.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} ({2} FPS) | Chladni({3},{4}) | Voices:{5}/10 | {6}",
                srcBadge, _gpu.BackendLabel, _measuredFps, _audio.ChladniM, _audio.ChladniN,
                _audio.ActiveVoiceCount, _mpeServer.ControllerStatus);

            _lblGyroReadout.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} | Tilt B:{1:0.0} G:{2:0.0} deg\r\nGamepad: RT/R2=Bow, L-Stick=Pitch/Y, R-Stick=Formant",
                _mpeServer.ConnectedPixelHint, _audio.GyroBetaDeg, _audio.GyroGammaDeg);

            int lst = _audio.SeqLooper.LoopState;
            if (lst > 0)
            {
                _lblLoopStatus.Text = string.Format(CultureInfo.InvariantCulture,
                    "Looper {0} :: {1:0.0}s / {2:0.0}s ({3} gestures)",
                    lst == 1 ? "REC" : (lst == 2 ? "PLAY" : "OVERDUB"),
                    _audio.SeqLooper.LoopPlayheadMs / 1000.0, _audio.SeqLooper.LoopDurationMs / 1000.0, _audio.SeqLooper.EventCount);
            }

            if (_audio.IsRecordingWav)
                _lblRecTimer.Text = "RECORDING 48 kHz STEREO .WAV :: " + _audio.RecordedSeconds + "s elapsed...";

            if (_audio.AutoChladniFromPitch)
            {
                _syncingUi = true;
                try
                {
                    _nudM.Value = Math.Max(1, Math.Min(12, _audio.ChladniM));
                    _nudN.Value = Math.Max(1, Math.Min(12, _audio.ChladniN));
                }
                finally { _syncingUi = false; }
            }

            if (_audio.ArpEnabled) RefreshStepButtons();
            UpdateLiveTunerReadout();
        }

        private void UpdateLiveTunerReadout()
        {
            if (_audio.LeadVoiceHz <= 20.0)
            {
                _lblLiveNoteReadout.Text = "LIVE TUNER :: Idle | Chladni(" + _audio.ChladniM + "," + _audio.ChladniN + ") | Space=4-Lane Arp";
                return;
            }
            string noteLbl; int cents;
            E8WaveLatticeMath.FormatNoteAndCents(_audio.LeadVoiceHz, out noteLbl, out cents);
            string cStr = (cents >= 0 ? "+" : "") + cents + "c";
            string prefix = _audio.MicInputActive ? "LAPTOP MIC" : (_audio.WebPipeActive ? "WEB PIPE" : (_audio.ClipPlaying ? "SOUND SHAPE" : "VOICE"));
            _lblLiveNoteReadout.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}: {1:0.00} Hz ({2} {3}) | Chladni({4},{5}) | E8 Root #{6}",
                prefix, _audio.LeadVoiceHz, noteLbl, cStr, _audio.ChladniM, _audio.ChladniN, _audio.LeadVoiceRoot);
        }

        private void OnStageCpuPaint(object sender, PaintEventArgs e)
        {
            if (_gpu.IsGpuActive) return;
            int w = Math.Max(64, _stage.ClientSize.Width), h = Math.Max(64, _stage.ClientSize.Height);
            if (_cpuBmp == null || _cpuBmp.Width != w || _cpuBmp.Height != h)
            {
                if (_cpuBmp != null) _cpuBmp.Dispose();
                _cpuBmp = new Bitmap(w, h);
            }
            _gpu.RenderCpuFallbackBitmap(_cpuBmp, _audio.ChladniM, _audio.ChladniN, _cmbViewMode != null ? _cmbViewMode.SelectedIndex : 0, _spec32, _waveL, _waveR);
            e.Graphics.DrawImageUnscaled(_cpuBmp, 0, 0);
        }

        private void MapMouseToMpe(int mx, int my, out double nx, out double ny)
        {
            int w = Math.Max(1, _stage.ClientSize.Width);
            int h = Math.Max(1, _stage.ClientSize.Height);
            double availH = Math.Max(120.0, h - 64.0);
            int vm = _cmbViewMode.SelectedIndex;
            if (vm == 0 || vm == 3)
            {
                double side = Math.Min(w * 0.48, availH * 0.93);
                double tlx = 18.0, tly = (availH - side) * 0.5;
                if (mx <= tlx + side + 24)
                {
                    nx = Math.Max(0.0, Math.Min(1.0, (mx - tlx) / Math.Max(1.0, side)));
                    ny = Math.Max(0.0, Math.Min(1.0, 1.0 - (my - tly) / Math.Max(1.0, side)));
                    return;
                }
            }
            nx = Math.Max(0.0, Math.Min(1.0, (double)mx / w));
            ny = Math.Max(0.0, Math.Min(1.0, 1.0 - (double)my / availH));
        }

        private void OnStageMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _mouseExciting = true;
            double nx, ny; MapMouseToMpe(e.X, e.Y, out nx, out ny);
            _audio.SetOrTriggerVoice(777, nx, ny, 0.88, false);
        }

        private void OnStageMouseMove(object sender, MouseEventArgs e)
        {
            if (!_mouseExciting) return;
            double nx, ny; MapMouseToMpe(e.X, e.Y, out nx, out ny);
            _audio.SetOrTriggerVoice(777, nx, ny, 0.88, false);
        }

        private void OnStageMouseUp(object sender, MouseEventArgs e)
        {
            if (_mouseExciting) { _mouseExciting = false; _audio.ReleaseVoice(777); }
        }

        private static int MapKeyToSemitone(Keys k)
        {
            switch (k)
            {
                case Keys.Z: return 0; case Keys.S: return 1; case Keys.X: return 2; case Keys.D: return 3;
                case Keys.C: return 4; case Keys.V: return 5; case Keys.G: return 6; case Keys.B: return 7;
                case Keys.H: return 8; case Keys.N: return 9; case Keys.J: return 10; case Keys.M: return 11;
                case Keys.Oemcomma: return 12;
                case Keys.Q: return 12; case Keys.D2: return 13; case Keys.W: return 14; case Keys.D3: return 15;
                case Keys.E: return 16; case Keys.R: return 17; case Keys.D5: return 18; case Keys.T: return 19;
                case Keys.D6: return 20; case Keys.Y: return 21; case Keys.D7: return 22; case Keys.U: return 23;
                case Keys.I: return 24;
                default: return -1;
            }
        }

        private void OnSynthKeyDown(object sender, KeyEventArgs e)
        {
            if (ActiveControl == _txtGemmaPatch)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Compose4LaneViaTandemGemmaAsync(); }
                return;
            }
            if (ActiveControl == _txtBrowserUrl)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OpenUrlInOmegaBrowserAndPipe(); }
                return;
            }
            if (e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                _audio.ArpEnabled = !_audio.ArpEnabled;
                SyncControlsFromEngine();
                return;
            }
            int semi = MapKeyToSemitone(e.KeyCode);
            if (semi >= 0)
            {
                double x = Math.Max(0.0, Math.Min(1.0, semi / (12.0 * _audio.OctaveSpan)));
                _audio.SetOrTriggerVoice(300 + semi, x, 0.62, 0.85, false);
            }
        }

        private void OnSynthKeyUp(object sender, KeyEventArgs e)
        {
            int semi = MapKeyToSemitone(e.KeyCode);
            if (semi >= 0) _audio.ReleaseVoice(300 + semi);
        }

        private void BeamMpePadAsync()
        {
            SetStatus("Arming ADB reverse tunnel tcp:19782 -> beaming Split Kaoss + Fretless MPE Pad to Pixel...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                SetStatus(OmegaPixelMpeAndGamepadServer.BeamMpePadToPixels(19782));
            });
        }

        private void LaunchOrSyncOmegaAv()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("OmegaAV");
                for (int i = 0; i < ps.Length; i++)
                {
                    IntPtr h = ps[i].MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        if (IsIconic(h)) ShowWindow(h, 9);
                        SetForegroundWindow(h);
                        SetStatus("Focused OmegaAV v0.7.1 (:19779) — WASAPI Loopback is live-capturing OmegaSynth 48kHz audio!");
                        return;
                    }
                }
                if (File.Exists(_avExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(_avExe);
                    psi.WorkingDirectory = _avDir;
                    Process.Start(psi);
                    SetStatus("Launched OmegaAV v0.7.1 (:19779) — WASAPI Loopback captures OmegaSynth 48kHz audio!");
                }
                else SetStatus("OmegaAV executable not found at: " + _avExe);
            }
            catch (Exception ex) { SetStatus("OmegaAV launch error: " + ex.Message); }
        }

        private void ToggleWavRecording()
        {
            if (!_audio.IsRecordingWav)
            {
                _audio.BeginWavRecording();
                SyncControlsFromEngine();
                SetStatus("Recording lossless 48,000 Hz 16-bit Stereo PCM .WAV...");
            }
            else
            {
                string saved = _audio.StopAndSaveWavRecording(_rendersDir);
                SyncControlsFromEngine();
                if (!string.IsNullOrEmpty(saved))
                {
                    _lblRecTimer.Text = "Saved: " + Path.GetFileName(saved);
                    SetStatus("Saved 48 kHz .WAV render -> " + saved);
                }
            }
        }

        private void SendLastWavToOmegaAv()
        {
            string wav = _audio.LastSavedWavPath;
            if (string.IsNullOrEmpty(wav) || !File.Exists(wav))
            {
                SetStatus("Record a .WAV clip first (Tab 2 -> 'Record 48kHz .WAV')!");
                return;
            }
            try
            {
                string inbox = Path.Combine(_avDir, "audio_inbox");
                Directory.CreateDirectory(inbox);
                string dst = Path.Combine(inbox, Path.GetFileName(wav));
                File.Copy(wav, dst, true);
                File.WriteAllText(Path.Combine(_avDir, "inbox_open.cmd"), dst, new UTF8Encoding(false));
                LaunchOrSyncOmegaAv();
                SetStatus("Hot-loaded " + Path.GetFileName(wav) + " into OmegaAV v0.7.1 (:19779)!");
            }
            catch (Exception ex) { SetStatus("Send to OmegaAV error: " + ex.Message); }
        }

        private static bool ProbeLocalPort(int port)
        {
            try { using (TcpClient tc = new TcpClient()) { IAsyncResult ar = tc.BeginConnect("127.0.0.1", port, null, null); if (ar.AsyncWaitHandle.WaitOne(120) && tc.Connected) { tc.EndConnect(ar); return true; } } }
            catch { }
            return false;
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

        private void Compose4LaneViaTandemGemmaAsync()
        {
            string prompt = (_txtGemmaPatch.Text ?? "").Trim();
            if (prompt.Length == 0) return;
            bool syncOmsh = _chkSyncAvOmsh.Checked;
            SetStatus("Tandem Gemma (:11434 / :8097) composing 4-lane polyrhythmic patch" + (syncOmsh ? " + synced .omsh shader..." : "..."));

            ThreadPool.QueueUserWorkItem(delegate
            {
                int preset = 6, scale = 4, bpm = 96, mMode = 4, nMode = 7;
                string pLow = prompt.ToLowerInvariant();
                if (pLow.Contains("resynth") || pLow.Contains("clone")) preset = 8;
                else if (pLow.Contains("choir") || pLow.Contains("throat") || pLow.Contains("vowel")) preset = 7;
                else if (pLow.Contains("harp") || pLow.Contains("karplus") || pLow.Contains("pluck") || pLow.Contains("string")) preset = 6;
                else if (pLow.Contains("bowl") || pLow.Contains("quartz") || pLow.Contains("tibetan")) preset = 2;
                else if (pLow.Contains("bronze") || pLow.Contains("plate")) preset = 1;
                else if (pLow.Contains("handpan") || pLow.Contains("titanium")) preset = 3;
                else if (pLow.Contains("glass")) preset = 4;
                else if (pLow.Contains("gong") || pLow.Contains("bass")) preset = 5;
                else preset = 0;

                if (pLow.Contains("just")) scale = 1;
                else if (pLow.Contains("31")) scale = 2;
                else if (pLow.Contains("bohlen")) scale = 3;
                else if (pLow.Contains("phi") || pLow.Contains("golden")) scale = 4;
                else if (pLow.Contains("e8") || pLow.Contains("coxeter")) scale = 5;

                Match mb = Regex.Match(prompt, "(\\d{2,3})\\s*bpm", RegexOptions.IgnoreCase);
                if (mb.Success) bpm = Math.Max(45, Math.Min(220, int.Parse(mb.Groups[1].Value)));

                string sys = "Output ONLY compact JSON: {\"preset\":0..8,\"scale\":0..5,\"bpm\":60..180,\"m\":2..9,\"n\":3..11,\"l1_k\":4,\"l2_k\":5,\"l3_k\":7,\"l4_k\":3}";
                int l1k = 4, l2k = 5, l3k = 7, l4k = 3;
                string aiRaw = "";
                if (ProbeLocalPort(11434))
                {
                    aiRaw = PostJsonFast("http://127.0.0.1:11434/api/chat",
                        "{\"model\":\"gemma3:4b\",\"stream\":false,\"messages\":[{\"role\":\"system\",\"content\":\""
                        + JsonEsc(sys) + "\"},{\"role\":\"user\",\"content\":\"" + JsonEsc(prompt) + "\"}]}", 5500);
                }
                if ((string.IsNullOrEmpty(aiRaw) || aiRaw.IndexOf("UNMEASURED", StringComparison.OrdinalIgnoreCase) >= 0) && ProbeLocalPort(8097))
                {
                    aiRaw = PostJsonFast("http://127.0.0.1:8097/v1/chat/completions",
                        "{\"model\":\"gemma-4-E4B-it-sh4z\",\"max_tokens\":160,\"temperature\":0.2,\"messages\":[{\"role\":\"system\",\"content\":\""
                        + JsonEsc(sys) + "\"},{\"role\":\"user\",\"content\":\"" + JsonEsc(prompt) + "\"}]}", 5500);
                }

                if (!string.IsNullOrEmpty(aiRaw) && aiRaw.IndexOf("UNMEASURED", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    preset = ParseIntField(aiRaw, "preset", preset, 0, E8WaveLatticeMath.Presets.Length - 1);
                    scale  = ParseIntField(aiRaw, "scale",  scale,  0, 5);
                    bpm    = ParseIntField(aiRaw, "bpm",    bpm,    45, 220);
                    mMode  = ParseIntField(aiRaw, "m",      mMode,  1, 11);
                    nMode  = ParseIntField(aiRaw, "n",      nMode,  2, 12);
                    l1k    = ParseIntField(aiRaw, "l1_k",   l1k,    2, 12);
                    l2k    = ParseIntField(aiRaw, "l2_k",   l2k,    2, 12);
                    l3k    = ParseIntField(aiRaw, "l3_k",   l3k,    2, 12);
                    l4k    = ParseIntField(aiRaw, "l4_k",   l4k,    2, 12);
                }
                if (mMode == nMode) nMode = Math.Min(12, mMode + 1);

                _audio.PresetIndex = preset;
                _audio.ScaleIndex = scale;
                _audio.ArpBpm = bpm;
                _audio.AutoChladniFromPitch = false;
                _audio.ChladniM = mMode;
                _audio.ChladniN = nMode;
                _audio.FormantFilterEnabled = (preset == 7);

                _audio.SeqLooper.Lanes[2].PresetOverride = preset;
                _audio.SeqLooper.Lanes[3].Muted = false;
                _audio.SeqLooper.ApplyEuclideanToLane(0, l1k, 16, preset);
                _audio.SeqLooper.ApplyEuclideanToLane(1, l2k, 12, scale + 2);
                _audio.SeqLooper.ApplyEuclideanToLane(2, l3k,  9, mMode);
                _audio.SeqLooper.ApplyEuclideanToLane(3, l4k, 16, nMode);
                _audio.ArpEnabled = true;
                _gpu.ResetSandAndWavefield();

                string omshNote = "";
                if (syncOmsh && Directory.Exists(_avDir))
                {
                    try
                    {
                        string shDir = Path.Combine(_avDir, "shaders");
                        Directory.CreateDirectory(shDir);
                        string omshPath = Path.Combine(shDir, "synth_chladni_" + mMode + "_" + nMode + ".omsh");
                        string omshCode =
                            "// Synced OmegaSynth v1.0 GM Chladni (" + mMode + "," + nMode + ") + E8 Cymatic Shader\r\n" +
                            "ch = cos(x * 3.14159 * " + mMode + ".0) * cos(y * 3.14159 * " + nMode + ".0) - cos(x * 3.14159 * " + nMode + ".0) * cos(y * 3.14159 * " + mMode + ".0);\r\n" +
                            "node = exp(-abs(ch) * (6.5 + bass * 4.0));\r\n" +
                            "v = node + 0.35 * sin(rad * 12.0 - t * 2.2 + high * 2.0);\r\n" +
                            "r = min(1.0, node * 1.15 + bass * 0.35);\r\n" +
                            "g = min(1.0, node * 0.85 + mid * 0.45 + 0.15 * rdV);\r\n" +
                            "b = min(1.0, 0.25 + 0.75 * abs(sin(ch * 2.0 + t * 0.6)) + high * 0.35);\r\n";
                        File.WriteAllText(omshPath, omshCode, new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(_avDir, "inbox_open.cmd"), omshPath, new UTF8Encoding(false));
                        omshNote = " + Synced Chladni(" + mMode + "," + nMode + ") .omsh -> OmegaAV!";
                    }
                    catch { }
                }

                if (!IsDisposed)
                {
                    BeginInvoke((Action)delegate
                    {
                        SyncControlsFromEngine();
                        SetStatus("Tandem Gemma LIVE: " + E8WaveLatticeMath.Presets[preset].Name + " | Chladni(" + mMode + "," + nMode + ") @ " + bpm + " BPM" + omshNote);
                    });
                }
            });
        }

        private static int ParseIntField(string json, string key, int defVal, int min, int max)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\\\?\"?\\s*:\\s*(\\d+)");
            if (!m.Success) return defVal;
            int v;
            if (!int.TryParse(m.Groups[1].Value, out v)) return defVal;
            return Math.Max(min, Math.Min(max, v));
        }

        private static string PostJsonFast(string url, string body, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "POST"; r.ContentType = "application/json; charset=utf-8";
                r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs;
                byte[] b = Encoding.UTF8.GetBytes(body);
                r.ContentLength = b.Length;
                using (Stream st = r.GetRequestStream()) st.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string JsonEsc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private void ShowQuickGuide()
        {
            MessageBox.Show(this,
                "OMEGASYNTH v1.1 FINAL GOLDEN MASTER — COMPLETE STUDIO GUIDE\r\n\r\n" +
                "1. RTX DirectCompute cs_5_0 60/144Hz VSYNC & 8 FACTORY .OSYNTH PATCHES:\r\n" +
                "   - Full GPU 512x512 Chladni quartz sand, 240-root 8D E8 lattice, 3D Cymatic Rose & Stereo Lissajous!\r\n\r\n" +
                "2. OPTIONAL LAPTOP MICROPHONE AMBIENT & VOICE INPUT (TAB 4 - ZERO FEEDBACK!):\r\n" +
                "   - Click [Mic: OFF] in the header or Tab 4 to pick up ambient room noise, claps, whistling, or humming!\r\n" +
                "   - Choose Mode 0 (Silent Cymatic Visualizer), Mode 1 (Sing/Whistle Pitch -> Bowed Resonator), Mode 2 (Room -> E8 Chord), or Mode 3 (Clap/Tap -> Plate Ripples), or click 'Sample Mic 2s -> MPE #09'!\r\n\r\n" +
                "3. SOUND-SHAPE DECODER, OMEGABROWSER (:9778) PIPE & GENERATIVE DRIFT (TABS 3 & 4):\r\n" +
                "   - Drop any .wav/.mp3/.flac/.m4a/.ogg to see its shape & clone into MPE #09, pipe YouTube from OmegaBrowser, or toggle 'Evolve: ON' for hands-free Golden-Phi ambient soundscapes!",
                "OmegaSynth v1.1 Final Golden Master Guide", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetStatus(string msg)
        {
            if (IsDisposed) return;
            Action a = delegate { _lblStatusBot.Text = msg; };
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
            else a();
        }

        private static Button TopBtn(string text, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.Size = new Size(w, 29);
            b.MinimumSize = new Size(30, 28);
            b.Margin = new Padding(4, 0, 0, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.1f);
            b.FlatAppearance.BorderColor = C(56, 189, 248);
            return b;
        }

        private static Button AddRackBtn(Control p, string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.MinimumSize = new Size(30, 27);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.1f);
            b.FlatAppearance.BorderColor = C(56, 189, 248);
            p.Controls.Add(b);
            return b;
        }

        private static void RackHeader(Control p, string text, ref int y)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = text;
            l.SetBounds(10, y, 404, 18);
            l.Font = new Font("Segoe UI", 8.3f, FontStyle.Bold);
            l.ForeColor = C(244, 114, 182);
            p.Controls.Add(l);
            y += 19;
        }

        private static void RackLabel(Control p, string text, int x, int y)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.Font = new Font("Segoe UI Semibold", 8.0f);
            l.ForeColor = C(203, 213, 225);
            p.Controls.Add(l);
        }

        private static ComboBox RackCombo(Control p, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(x, y, w, 24);
            c.BackColor = C(6, 9, 17);
            c.ForeColor = Color.White;
            c.Font = new Font("Consolas", 8.3f);
            p.Controls.Add(c);
            return c;
        }

        private static TrackBar RackSlider(Control p, string title, int min, int max, int val, ref int y, Action<int> onChange)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.SetBounds(10, y, 404, 15);
            l.Font = new Font("Segoe UI", 7.8f);
            l.ForeColor = C(148, 163, 184);
            l.Text = title + " [" + val + "%]";
            p.Controls.Add(l);
            y += 15;

            TrackBar tb = new TrackBar();
            tb.AutoSize = false;
            tb.TickStyle = TickStyle.None;
            tb.Minimum = min;
            tb.Maximum = max;
            tb.Value = val;
            tb.SetBounds(8, y, 406, 20);
            tb.Scroll += delegate
            {
                l.Text = title + " [" + tb.Value + "%]";
                onChange(tb.Value);
            };
            p.Controls.Add(tb);
            y += 22;
            return tb;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--stress-test", StringComparison.OrdinalIgnoreCase))
            {
                OmegaSpatialCreatorRig.Track04StressSummary sr = OmegaSpatialCreatorRig.OmegaSpatialStressSuite.RunAll8Vectors();
                Console.WriteLine("[OMEGASYNTH+SPATIAL 8-VECTOR AUDIT] " + sr.PassedCount + "/" + sr.TotalCount + " PASS (" + sr.TotalTrials + " trials | RAM=" + sr.TrimmedWorkingSetMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB)");
                for (int vi = 0; vi < sr.Vectors.Count; vi++) { if (!sr.Vectors[vi].Passed) Console.WriteLine("  [FAIL] " + sr.Vectors[vi].Code + " -> " + sr.Vectors[vi].Detail); }
                Environment.Exit(sr.AllPassed ? 0 : 1);
                return;
            }
            OmegaPortableConfig.OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string arg0 = (args != null && args.Length > 0) ? (args[0] ?? "").Trim().Trim('"') : "";
            Application.Run(new OmegaSynthCockpitForm(dir, arg0));
        }
    }
}
