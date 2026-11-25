using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OmegaPenroseStudio
{
    public sealed class PenroseStagePanel : Panel
    {
        public PenroseStagePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        }
    }

    public sealed partial class OmegaPenroseCockpitForm : Form
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

        [DllImport("user32.dll")]
        private static extern bool PeekMessageW(out NATIVE_MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private readonly string _penDir;
        private readonly string _suiteRoot;
        private readonly string _exportsDir;
        private readonly string _patchesDir;
        private readonly string _avDir;
        private readonly string _avExe;

        private readonly OmegaPenroseGpuEngine _gpu;
        private readonly OmegaPenrosePadServer _padServer;
        private readonly OmegaPenroseAudioPhasonPipe _audioPipe;
        private readonly OmegaPenroseAviRecorder _aviRecorder;
        private OmegaPenrosePatch[] _factoryBank;
        private PenroseStagePanel _stage;
        private Bitmap _cpuFallbackBmp;

        // UI Controls
        private Label _lblTelemetryRow2;
        private Label _lblStatusBot;
        private Label _lblRhombStats;
        private Label _lblMassStats;
        private Label _lblMillerStats;
        private Label _lblAudioStats;
        private Label _lbl3dStats;

        private Button _btnTopPause;
        private Button _btnTopAudio;
        private Button _btnTop3d;
        private Button _btnTopAutoDrift;
        private Button _btnTopRecAvi;
        private ComboBox _cmbPatchBank;
        private TabControl _rightTabs;

        private ComboBox _cmbSymmetry;
        private ComboBox _cmbViewMode;
        private ComboBox _cmbVsync;
        private ComboBox _cmbTheme;
        private ComboBox _cmbLifeMode;
        private ComboBox _cmbSpecies;
        private ComboBox _cmbBrushKind;
        private ComboBox _cmbFftSource;
        private ComboBox _cmbHoloTarget;
        private ComboBox _cmbFftWin;
        private ComboBox _cmbLaserColor;
        private ComboBox _cmb3dKind;
        private ComboBox _cmb3dShade;

        private TrackBar _trkZoom;
        private TrackBar _trkPhason;
        private TrackBar _trkSumGamma;
        private TrackBar _trkMu;
        private TrackBar _trkSigma;
        private TrackBar _trkMutation;
        private TrackBar _trkCoupling;
        private TrackBar _trkCrossSpecies;
        private TrackBar _trkSubSteps;
        private TrackBar _trkBraggGain;
        private TrackBar _trkBraggGate;
        private TrackBar _trkAudioPhasonGain;
        private TrackBar _trkCam3dDist;
        private TrackBar _trkCkScale;
        private TrackBar _trkCkZStep;

        private CheckBox _chkShowEdges;
        private CheckBox _chkShowJewel;
        private CheckBox _chkShowWaveguide;
        private CheckBox _chkShowAmmann;
        private CheckBox _chkShowWorms;
        private CheckBox _chkAutoPhasonDrift;
        private CheckBox _chkPhiRings;
        private CheckBox _chkShowMillerPlanes;
        private CheckBox _chkAudioPipe;
        private CheckBox _chkLockP3ZeroSum;
        private CheckBox _chkAutoRotate3d;
        private CheckBox _chkSyncAvOnGemma;
        private TextBox _txtGemmaPrompt;

        // Simulation, Genome, Hologram, Miller & 3D State
        private readonly double[] _gamma5 = new double[5];
        private List<PenroseRhombTile> _currentRhombs = new List<PenroseRhombTile>();
        private List<CrystalQuad3D> _currentQuads3d = new List<CrystalQuad3D>();
        private readonly float[] _proj3dBuffer = new float[OmegaPenroseIcosahedral3D.MaxGpuQuads * 12];
        private readonly float[] _seedFieldBuffer = new float[OmegaPenroseGpuEngine.GridN * OmegaPenroseGpuEngine.GridN * 4];
        private readonly float[] _seedGenomeBuffer = new float[OmegaPenroseGpuEngine.GridN * OmegaPenroseGpuEngine.GridN * 2];
        private MillerPeakInfo5D _millerPeak = new MillerPeakInfo5D();
        private string _activeHoloLabel = "E8 30-Fold Sacred Star";

        private int _thickCount = 0, _thinCount = 0, _phasonFlipCount = 0;
        private int _prolate3dCount = 0, _oblate3dCount = 0, _e8RootFacetCount = 0;
        private int _symMode = 0;
        private int _viewMode = 0;
        private int _themeIdx = 0;
        private int _lifeMode = 0;      // 0=RGB Flow, 1=Darwinian Flow, 2=Kohmoto-Sutherland Quantum, 3=Classic Scalar
        private int _brushSelectedKind = 1;
        private int _fftSource = 0;     // 0=Dirac Lattice, 1=Lattice+Life, 2=Bio, 3=Cymatic, 4=Gerchberg-Saxton Laser Hologram!
        private int _fftWinMode = 0;
        private int _laserMode = 0;
        private int _crystal3dKind = 0; // 0=6D Icosahedral, 1=Stepped Citadel, 2=8D E8 Gosset Star
        private int _shader3dMode = 0;  // 0=True Atomic Z-Buffered Solid Specular Jewel, 1=X-Ray Quartz
        private bool _paused = false;
        private bool _autoPhasonDrift = false;

        private float _zoomUnits = 14.0f;
        private float _panX = 0.0f, _panY = 0.0f;
        private float _phasonPhase = 0.0f;
        private float _sumGammaBias = 0.0f;
        private float _mu = 0.210f;
        private float _sigma = 0.0480f;
        private float _mutationRate = 0.22f;
        private int _kernelRadius = 13;
        private float _dt = 0.16f;
        private float _coupling = 0.34f;
        private float _crossSpecies = 0.42f;
        private int _subSteps = 2;
        private float _braggGainDb = 95.0f;
        private float _braggFloorGate = 0.14f;
        private float _audioPhasonGain = 0.65f;

        // 3D Orbit Camera State
        private float _yaw3d = 0.62f;
        private float _pitch3d = 0.44f;
        private float _camDist3d = 5.4f;
        private float _ckScaleUnits = 220.0f;
        private float _ckZStepUnits = 140.0f;

        // Mouse Interaction State
        private bool _leftDrag = false;
        private bool _rightPanDrag = false;
        private bool _orbit3dDrag = false;
        private Point _lastMousePt;
        private float _pendingBrushX = 0.5f, _pendingBrushY = 0.5f;
        private int _pendingBrushMode = 0;

        private readonly Stopwatch _simClock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer _uiTimer;
        private readonly Random _rng = new Random();
        private volatile bool _renderLoopActive = false;
        private bool _meshDirty = true;
        private bool _crystal3dDirty = true;
        private bool _syncingUi = false;
        private int _fpsFrames = 0;
        private double _fpsLastSec = 0.0;
        private double _lastFrameSec = 0.0;
        private int _measuredFps = 60;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaPenroseCockpitForm(string penDir)
        {
            _penDir = penDir;
            _suiteRoot = Directory.GetParent(penDir) != null ? Directory.GetParent(penDir).FullName : penDir;
            _exportsDir = Path.Combine(penDir, "exports");
            _patchesDir = Path.Combine(penDir, "patches");
            _avDir = Path.Combine(_suiteRoot, "03_omega_av");
            _avExe = Path.Combine(_avDir, "OmegaAV.exe");
            Directory.CreateDirectory(_exportsDir);
            Directory.CreateDirectory(_patchesDir);

            _gpu = new OmegaPenroseGpuEngine();
            _audioPipe = new OmegaPenroseAudioPhasonPipe();
            _aviRecorder = new OmegaPenroseAviRecorder();
            _factoryBank = OmegaPenrosePatch.BuildFactoryBank(_patchesDir);

            _padServer = new OmegaPenrosePadServer();
            _padServer.OnBuildStatusJson = BuildStatusJson;
            _padServer.OnPadInteraction = delegate(float nx, float ny, int kind)
            {
                if (IsDisposed) return;
                BeginInvoke((Action)delegate
                {
                    if (kind == 0)
                    {
                        _phasonPhase = (float)(nx * Math.PI * 2.0);
                        _sumGammaBias = _chkLockP3ZeroSum.Checked ? 0.0f : (ny - 0.5f);
                        _meshDirty = true;
                        _crystal3dDirty = true;
                    }
                    else if (kind == 2)
                    {
                        _yaw3d = (nx - 0.5f) * 6.283f;
                        _pitch3d = (ny - 0.5f) * 2.4f;
                    }
                    else if (kind == 3)
                    {
                        InspectMillerBraggSpot(nx, ny);
                    }
                    else
                    {
                        StampSpeciesAtNormalized(nx, ny, 0);
                    }
                });
            };
            _padServer.OnRemoteCommand = delegate(string act)
            {
                if (IsDisposed) return;
                BeginInvoke((Action)delegate
                {
                    if (act == "orbium") SeedPentagonalRingColony();
                    else if (act == "quantum") { _cmbLifeMode.SelectedIndex = 2; SeedQuantumRingState(); }
                    else if (act == "holo") ActivateProceduralHologram(0);
                    else if (act == "sym") { _cmbSymmetry.SelectedIndex = (_cmbSymmetry.SelectedIndex + 1) % _cmbSymmetry.Items.Count; }
                    else if (act == "soup") SeedRandomPrimordialSoup();
                    else if (act == "clear") ClearLeniaBiomass();
                    else if (act == "audio") ToggleAudioPipe(!_audioPipe.Enabled);
                    else if (act == "view") { _cmbViewMode.SelectedIndex = (_cmbViewMode.SelectedIndex + 1) % _cmbViewMode.Items.Count; }
                    else if (act == "zin") ApplyZoomDelta(-2.5f);
                    else if (act == "zout") ApplyZoomDelta(2.5f);
                });
            };

            AutoScaleMode = AutoScaleMode.None;
            AllowDrop = true;
            Text = "ACT-Omega Lab :: OmegaPenrose v1.0.2 Final GM (07_omega_penrose) — 8D E8 & Hat Monotile, Laser Hologram, Darwinian/Quantum Life & Solid Z-3D (:19785)";
            Size = new Size(1690, 960);
            MinimumSize = new Size(1280, 760);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(8, 12, 21);
            ForeColor = Color.White;
            KeyPreview = true;
            try
            {
                string ic = Path.Combine(_suiteRoot, "icons", "omega_penrose.ico");
                if (File.Exists(ic)) Icon = new Icon(ic);
            }
            catch { }

            DragEnter += OnWindowDragEnter;
            DragDrop += OnWindowDragDrop;

            // =========================================================================
            // TOP HEADER (125% DPI Zero-Clipping Layout)
            // =========================================================================
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 62;
            topBar.BackColor = C(11, 16, 29);

            Panel topRow1 = new Panel();
            topRow1.Dock = DockStyle.Top;
            topRow1.Height = 36;

            Label lBrand = new Label();
            lBrand.UseMnemonic = false;
            lBrand.Dock = DockStyle.Left;
            lBrand.Width = 340;
            lBrand.Padding = new Padding(14, 8, 0, 0);
            lBrand.Text = "OMEGAPENROSE v1.0.2 FINAL GM (:19785)";
            lBrand.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
            lBrand.ForeColor = C(56, 189, 248);

            FlowLayoutPanel rightHdr = new FlowLayoutPanel();
            rightHdr.Dock = DockStyle.Right;
            rightHdr.Width = 1072;
            rightHdr.FlowDirection = FlowDirection.RightToLeft;
            rightHdr.WrapContents = false;
            rightHdr.Padding = new Padding(0, 4, 10, 0);

            Button bGuide = TopBtn("? Guide", 68, C(180, 83, 9));
            bGuide.Click += delegate { ShowQuickGuide(); };

            Button bSyncAv = TopBtn("-> OmegaAV", 108, C(109, 40, 217));
            bSyncAv.Click += delegate { ExportOmshAndHotLoadAv(true); };

            Button bBeamPx = TopBtn("Beam -> Pixel", 104, C(5, 150, 105));
            bBeamPx.Click += delegate { BeamPadToPixelAsync(); };

            _btnTopRecAvi = TopBtn("Rec .AVI", 88, C(159, 18, 57));
            _btnTopRecAvi.Click += delegate { ToggleAviRecording(); };

            _btnTop3d = TopBtn("3D Jewel", 86, C(14, 116, 144));
            _btnTop3d.Click += delegate
            {
                _cmbViewMode.SelectedIndex = (_viewMode == 5) ? 4 : ((_viewMode == 4) ? 0 : 5);
                SyncHeaderButtons();
            };

            _btnTopAudio = TopBtn("Audio: OFF", 92, C(30, 58, 138));
            _btnTopAudio.Click += delegate { ToggleAudioPipe(!_audioPipe.Enabled); };

            _btnTopAutoDrift = TopBtn("5D Drift: OFF", 100, C(51, 65, 85));
            _btnTopAutoDrift.Click += delegate
            {
                _autoPhasonDrift = !_autoPhasonDrift;
                _chkAutoPhasonDrift.Checked = _autoPhasonDrift;
                SyncHeaderButtons();
            };

            Button bSpawn5 = TopBtn("Spawn Ring", 104, C(190, 24, 93));
            bSpawn5.Click += delegate
            {
                if (_lifeMode == 2) SeedQuantumRingState();
                else SeedPentagonalRingColony();
            };

            Button bClear = TopBtn("Clear", 64, C(127, 29, 29));
            bClear.Click += delegate { ClearLeniaBiomass(); };

            _btnTopPause = TopBtn("Sim: LIVE", 84, C(15, 118, 110));
            _btnTopPause.Click += delegate { _paused = !_paused; SyncHeaderButtons(); };

            rightHdr.Controls.AddRange(new Control[]
            {
                bGuide, bSyncAv, bBeamPx, _btnTopRecAvi, _btnTop3d, _btnTopAudio, _btnTopAutoDrift, bSpawn5, bClear, _btnTopPause
            });
            topRow1.Controls.Add(rightHdr);
            topRow1.Controls.Add(lBrand);

            _lblTelemetryRow2 = new Label();
            _lblTelemetryRow2.UseMnemonic = false;
            _lblTelemetryRow2.Dock = DockStyle.Fill;
            _lblTelemetryRow2.Padding = new Padding(14, 2, 10, 0);
            _lblTelemetryRow2.Font = new Font("Consolas", 8.3f, FontStyle.Bold);
            _lblTelemetryRow2.ForeColor = C(52, 211, 153);
            _lblTelemetryRow2.Text = "OmegaPenrose v1.0.2 Final GM Initializing RTX cs_5_0 10-Shader Pipeline...";

            topBar.Controls.Add(_lblTelemetryRow2);
            topBar.Controls.Add(topRow1);

            // =========================================================================
            // BOTTOM STATUS + TANDEM GEMMA DISCOVERY BAR
            // =========================================================================
            Panel botStrip = new Panel();
            botStrip.Dock = DockStyle.Bottom;
            botStrip.Height = 25;
            botStrip.BackColor = C(6, 9, 17);
            _lblStatusBot = new Label();
            _lblStatusBot.UseMnemonic = false;
            _lblStatusBot.Dock = DockStyle.Fill;
            _lblStatusBot.Font = new Font("Consolas", 8.1f);
            _lblStatusBot.ForeColor = C(56, 189, 248);
            _lblStatusBot.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatusBot.Padding = new Padding(12, 0, 0, 0);
            _lblStatusBot.Text = "v1.0 GM Ready :: Drop any Image->Laser Hologram | Click Bragg Spot->5D Miller Index | Wheel=Zoom | Right-Drag=Pan | Left-Drag=Paint/3D Orbit!";
            botStrip.Controls.Add(_lblStatusBot);

            Panel aiDock = new Panel();
            aiDock.Dock = DockStyle.Bottom;
            aiDock.Height = 46;
            aiDock.BackColor = C(12, 18, 32);
            BuildBottomGemmaDock(aiDock);

            // =========================================================================
            // RIGHT 4-TAB STUDIO RACK + .OPENROSE PATCH BANK BAR (Width = 482)
            // =========================================================================
            Panel rightRack = new Panel();
            rightRack.Dock = DockStyle.Right;
            rightRack.Width = 482;
            rightRack.BackColor = C(13, 19, 34);
            rightRack.Padding = new Padding(4, 4, 6, 4);
            BuildTabbedRightRack(rightRack);

            // =========================================================================
            // CENTER LIVE RTX cs_5_0 STAGE
            // =========================================================================
            Panel centerHost = new Panel();
            centerHost.Dock = DockStyle.Fill;
            centerHost.Padding = new Padding(10, 8, 6, 8);
            centerHost.BackColor = C(7, 10, 18);

            _stage = new PenroseStagePanel();
            _stage.Dock = DockStyle.Fill;
            _stage.BackColor = Color.Black;
            _stage.Cursor = Cursors.Cross;
            _stage.AllowDrop = true;
            _stage.DragEnter += OnWindowDragEnter;
            _stage.DragDrop += OnWindowDragDrop;
            _stage.MouseDown += OnStageMouseDown;
            _stage.MouseMove += OnStageMouseMove;
            _stage.MouseUp += delegate { _leftDrag = false; _rightPanDrag = false; _orbit3dDrag = false; };
            _stage.MouseDoubleClick += delegate { ResetZoomAndPan(); };
            _stage.MouseEnter += delegate { if (!_txtGemmaPrompt.Focused) _stage.Focus(); };
            _stage.MouseWheel += OnStageMouseWheel;
            _stage.Paint += OnStageCpuPaint;
            centerHost.Controls.Add(_stage);

            Controls.Add(centerHost);
            Controls.Add(rightRack);
            Controls.Add(aiDock);
            Controls.Add(botStrip);
            Controls.Add(topBar);

            KeyDown += OnFormKeyDown;
            MouseWheel += OnStageMouseWheel;

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 160;
            _uiTimer.Tick += delegate { UpdateUiTelemetryTick(); };

            Shown += delegate
            {
                try { timeBeginPeriod(1u); } catch { }
                _padServer.Start(19785);
                _audioPipe.Start();
                _gpu.Initialize(_stage.Handle, Math.Max(320, _stage.ClientSize.Width), Math.Max(240, _stage.ClientSize.Height));
                // Pre-upload default E8 Sacred Star Hologram phase mask so Mode 4 works instantaneously!
                string holoLbl;
                float[] ph0 = OmegaPenroseGerchbergSaxtonCgh.SynthesizeProceduralGlyph(0, out holoLbl);
                _gpu.UploadHologramPhase256(ph0);

                ApplySelectedSpeciesParameters(0);
                RebuildDualMeshNow();
                Rebuild3dCrystalNow();
                SeedPentagonalRingColony();
                _renderLoopActive = true;
                Application.Idle += OnAppIdleRenderLoop;
                _uiTimer.Start();
            };

            FormClosing += delegate
            {
                _renderLoopActive = false;
                Application.Idle -= OnAppIdleRenderLoop;
                _uiTimer.Stop();
                if (_aviRecorder.IsRecording) { try { _aviRecorder.StopAndSave(); } catch { } }
                DisposeTriEngineBus();
                _aviRecorder.Dispose();
                _audioPipe.Dispose();
                _padServer.Dispose();
                _gpu.Dispose();
                try { timeEndPeriod(1u); } catch { }
            };
        }

        private void OnAppIdleRenderLoop(object sender, EventArgs e)
        {
            NATIVE_MSG msg;
            while (_renderLoopActive && !IsDisposed && !PeekMessageW(out msg, IntPtr.Zero, 0u, 0u, 0u))
            {
                if (WindowState == FormWindowState.Minimized) { Thread.Sleep(16); break; }
                RenderOneTick();
            }
        }

        private void RenderOneTick()
        {
            double nowSec = _simClock.Elapsed.TotalSeconds;
            double dtFrame = nowSec - _lastFrameSec;
            if (!_gpu.IsGpuActive && dtFrame < 0.020) { Thread.Sleep(2); return; }
            if (_gpu.IsGpuActive && !_gpu.VSyncEnabled && dtFrame < 0.0025) return;
            _lastFrameSec = nowSec;

            _fpsFrames++;
            if (nowSec - _fpsLastSec >= 0.5)
            {
                _measuredFps = (int)Math.Round(_fpsFrames / Math.Max(0.001, nowSec - _fpsLastSec));
                _fpsFrames = 0;
                _fpsLastSec = nowSec;
            }

            float audGain = _audioPipe.Enabled ? _audioPhasonGain : 0.0f;
            float aSub  = _audioPipe.SubBassLevel * audGain;
            float aMid  = _audioPipe.MidHarmLevel * audGain;
            float aHi   = _audioPipe.HighAirLevel * audGain;
            float aBeat = _audioPipe.BeatPulse * audGain;

            if (!_paused)
            {
                if (_autoPhasonDrift || (_audioPipe.Enabled && (aSub > 0.02f || aBeat > 0.03f)))
                {
                    double speed = (_autoPhasonDrift ? 0.38 : 0.0) + aBeat * 1.45 + aSub * 0.55;
                    _phasonPhase = (float)((_phasonPhase + dtFrame * speed) % (Math.PI * 2.0));
                    _meshDirty = true;
                    if ((_crystal3dKind == 0 || _crystal3dKind == 2) && (_viewMode == 4 || _viewMode == 5))
                        _crystal3dDirty = true;
                }
                if (_chkAutoRotate3d.Checked && (_viewMode == 4 || _viewMode == 5) && !_orbit3dDrag)
                {
                    _yaw3d = (float)((_yaw3d + dtFrame * (0.42 + aMid * 0.85)) % (Math.PI * 2.0));
                }
            }

            if (_meshDirty)
            {
                RebuildDualMeshNow();
                _meshDirty = false;
                if (_crystal3dKind == 1) _crystal3dDirty = true;
            }

            if (_crystal3dDirty && (_viewMode == 4 || _viewMode == 5))
            {
                Rebuild3dCrystalNow();
                _crystal3dDirty = false;
            }

            int sw = Math.Max(64, _stage.ClientSize.Width);
            int sh = Math.Max(64, _stage.ClientSize.Height);

            float r3x = 0, r3y = 0, r3s = 300;
            if (_viewMode == 4 || _viewMode == 5)
            {
                if (_viewMode == 4)
                {
                    r3s = Math.Min((float)sw, (float)sh) * 0.94f;
                    r3x = (sw - r3s) * 0.5f;
                    r3y = (sh - r3s) * 0.5f;
                }
                else
                {
                    r3s = Math.Min(sw * 0.475f, sh * 0.93f);
                    r3x = sw - r3s - 18.0f;
                    r3y = (sh - r3s) * 0.5f;
                }
                int qCnt = OmegaPenroseIcosahedral3D.ProjectAndSortQuads(
                    _currentQuads3d, _yaw3d, _pitch3d, _camDist3d, r3x, r3y, r3s,
                    _shader3dMode == 0, _proj3dBuffer, OmegaPenroseIcosahedral3D.MaxGpuQuads);
                _gpu.UploadProjected3DQuads(_proj3dBuffer, qCnt);
            }

            if (_gpu.IsGpuActive)
            {
                int bMode = _pendingBrushMode;
                _pendingBrushMode = 0;
                _gpu.RenderGpuFrame(
                    sw, sh, (float)nowSec, _viewMode,
                    _zoomUnits, _panX, _panY,
                    _symMode, _phasonPhase, _gamma5,
                    _mu, _sigma, _kernelRadius, _dt, _coupling, _crossSpecies,
                    _lifeMode, _mutationRate,
                    _themeIdx, _paused,
                    _fftSource, _laserMode, _braggGainDb, _fftWinMode, _braggFloorGate,
                    _pendingBrushX, _pendingBrushY, bMode, 16.0f,
                    _chkPhiRings.Checked, _chkShowAmmann.Checked, _chkShowEdges.Checked, _chkShowWorms.Checked,
                    _chkShowJewel.Checked, _chkShowWaveguide.Checked,
                    aSub, aMid, aHi, aBeat,
                    r3x, r3y, r3s, _shader3dMode,
                    _millerPeak, _chkShowMillerPlanes != null && _chkShowMillerPlanes.Checked,
                    _subSteps);
            }
            else
            {
                _stage.Invalidate();
            }

            if (_aviRecorder.IsRecording)
                _aviRecorder.CaptureFrameIfDue(_stage, _simClock.ElapsedMilliseconds);
            PublishTriEngineBusFrame(nowSec);
        }

        private void BuildTabbedRightRack(Panel host)
        {
            // Top Patch Bank Bar (.openrose) — Matches OmegaSynth v1.1 Final GM!
            Panel patchBar = new Panel();
            patchBar.Dock = DockStyle.Top;
            patchBar.Height = 46;
            patchBar.BackColor = C(11, 16, 29);

            Label lPb = new Label();
            lPb.UseMnemonic = false;
            lPb.SetBounds(6, 2, 332, 15);
            lPb.Text = "PATCH BANK (.openrose — 10 GM Presets):";
            lPb.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lPb.ForeColor = C(56, 189, 248);

            _cmbPatchBank = new ComboBox();
            _cmbPatchBank.SetBounds(6, 18, 332, 24);
            _cmbPatchBank.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbPatchBank.BackColor = C(7, 11, 20);
            _cmbPatchBank.ForeColor = Color.White;
            _cmbPatchBank.FlatStyle = FlatStyle.Flat;
            _cmbPatchBank.Font = new Font("Consolas", 8.0f);
            for (int i = 0; i < _factoryBank.Length; i++) _cmbPatchBank.Items.Add(_factoryBank[i]);
            _cmbPatchBank.SelectedIndex = 0;
            _cmbPatchBank.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                OmegaPenrosePatch sel = _cmbPatchBank.SelectedItem as OmegaPenrosePatch;
                if (sel != null) ApplyPatchPreset(sel);
            };

            Button bSavePatch = AddRackBtn(patchBar, "Save", 344, 17, 58, C(5, 150, 105));
            bSavePatch.Click += delegate { SaveOpenrosePatchDialog(); };

            Button bLoadPatch = AddRackBtn(patchBar, "Load..", 406, 17, 60, C(30, 58, 138));
            bLoadPatch.Click += delegate { LoadOpenrosePatchDialog(); };

            patchBar.Controls.Add(lPb);
            patchBar.Controls.Add(_cmbPatchBank);

            _rightTabs = new TabControl();
            _rightTabs.Dock = DockStyle.Fill;
            _rightTabs.Font = new Font("Segoe UI Semibold", 7.5f);

            TabPage tabQuasi  = new TabPage("1. 8D/5D & Hat");
            tabQuasi.BackColor = C(13, 19, 34); tabQuasi.ForeColor = Color.White; tabQuasi.AutoScroll = true;

            TabPage tabLenia  = new TabPage("2. Darwin & Psi");
            tabLenia.BackColor = C(13, 19, 34); tabLenia.ForeColor = Color.White; tabLenia.AutoScroll = true;

            TabPage tabBragg  = new TabPage("3. Holo & Bragg");
            tabBragg.BackColor = C(13, 19, 34); tabBragg.ForeColor = Color.White; tabBragg.AutoScroll = true;

            TabPage tabExport = new TabPage("4. Z-3D & .STL");
            tabExport.BackColor = C(13, 19, 34); tabExport.ForeColor = Color.White; tabExport.AutoScroll = true;

            // =========================================================================
            // TAB 1: PILLAR 1 — 8D E8 ELSER-SLOANE, HAT & SPECTRE MONOTILES, JEWEL ZOOM
            // =========================================================================
            int y1 = 8;
            RackHeader(tabQuasi, "1. 9 APERIODIC SYMMETRIES (5D, 8D E8, HAT & SPECTRE)", ref y1);
            RackLabel(tabQuasi, "Quasicrystal / Monotile Family (01..09):", 10, y1); y1 += 17;
            _cmbSymmetry = RackCombo(tabQuasi, 10, y1, 424);
            for (int i = 0; i < OmegaPenroseDeBruijnMath.SymmetryModeNames.Length; i++)
                _cmbSymmetry.Items.Add(OmegaPenroseDeBruijnMath.SymmetryModeNames[i]);
            _cmbSymmetry.SelectedIndex = 0;
            _cmbSymmetry.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                _symMode = _cmbSymmetry.SelectedIndex;
                if (_symMode >= 7) { _panX = 0.0f; _panY = 0.0f; }
                _meshDirty = true;
                _crystal3dDirty = true;
                SetStatus("Symmetry -> " + _cmbSymmetry.SelectedItem);
            };
            y1 += 27;

            RackLabel(tabQuasi, "RTX cs_5_0 Viewport Mode (6 Views):", 10, y1);
            RackLabel(tabQuasi, "Frame Sync:", 268, y1); y1 += 17;
            _cmbViewMode = RackCombo(tabQuasi, 10, y1, 252);
            _cmbViewMode.Items.AddRange(new object[]
            {
                "0: Split Life/Psi | -92dB Laser Bragg",
                "1: Full 512x512 Jewel Quasicrystal + Life",
                "2: Full -92dB Laser Bragg / Hologram",
                "3: Split Quasicrystal | Galois Sparkles",
                "4: Full Z-Buffered Solid 3D Jewel",
                "5: Split 2D Quasicrystal | Solid 3D Jewel"
            });
            _cmbViewMode.SelectedIndex = 0;
            _cmbViewMode.SelectedIndexChanged += delegate
            {
                _viewMode = _cmbViewMode.SelectedIndex;
                if (_viewMode == 4 || _viewMode == 5) _crystal3dDirty = true;
                SyncHeaderButtons();
            };

            _cmbVsync = RackCombo(tabQuasi, 268, y1, 90);
            _cmbVsync.Items.AddRange(new object[] { "VSync ON", "Uncapped" });
            _cmbVsync.SelectedIndex = 0;
            _cmbVsync.SelectedIndexChanged += delegate { _gpu.VSyncEnabled = (_cmbVsync.SelectedIndex == 0); };

            Button bResetCam = AddRackBtn(tabQuasi, "Reset", 362, y1 - 1, 72, C(30, 58, 138));
            bResetCam.Click += delegate { ResetZoomAndPan(); };
            y1 += 28;

            RackLabel(tabQuasi, "Color Palette Theme:", 10, y1); y1 += 17;
            _cmbTheme = RackCombo(tabQuasi, 10, y1, 424);
            _cmbTheme.Items.AddRange(new object[]
            {
                "0: Sovereign Neon (Crimson / Cyan / Magenta)",
                "1: Solar Gold & Turquoise Cathedral",
                "2: Bioluminescent Emerald & Sapphire",
                "3: Obsidian Plasma & Violet Symbiont"
            });
            _cmbTheme.SelectedIndex = 0;
            _cmbTheme.SelectedIndexChanged += delegate { _themeIdx = _cmbTheme.SelectedIndex; };
            y1 += 28;

            RackHeader(tabQuasi, "2. JEWEL ZOOM, 5D/8D PHASON FLIPS & A* WAVEGUIDE", ref y1);
            Label lZoom = RackLabel(tabQuasi, "Jewel Camera Field Span (Wheel Zoom = 3.5..36 Tiles): [14.0]", 10, y1); y1 += 16;
            _trkZoom = RackSlider(tabQuasi, 10, y1, 35, 360, 140);
            _trkZoom.Scroll += delegate
            {
                _zoomUnits = _trkZoom.Value / 10.0f;
                lZoom.Text = string.Format(CultureInfo.InvariantCulture, "Jewel Camera Field Span (Wheel Zoom = 3.5..36 Tiles): [{0:0.0}]", _zoomUnits);
                _meshDirty = true;
            };
            y1 += 29;

            Label lPhas = RackLabel(tabQuasi, "5D / 8D Galois Phason Rotation Angle: [0 deg]", 10, y1); y1 += 16;
            _trkPhason = RackSlider(tabQuasi, 10, y1, 0, 360, 0);
            _trkPhason.Scroll += delegate
            {
                _phasonPhase = (float)(_trkPhason.Value * Math.PI / 180.0);
                lPhas.Text = "5D / 8D Galois Phason Rotation Angle: [" + _trkPhason.Value + " deg]";
                _meshDirty = true;
                _crystal3dDirty = true;
            };
            y1 += 29;

            Label lGamma = RackLabel(tabQuasi, "Sum(gamma) Shift Bias (0 = True Penrose P3): [0.00]", 10, y1); y1 += 16;
            _trkSumGamma = RackSlider(tabQuasi, 10, y1, -50, 50, 0);
            _trkSumGamma.Scroll += delegate
            {
                _sumGammaBias = _trkSumGamma.Value / 100.0f;
                lGamma.Text = string.Format(CultureInfo.InvariantCulture, "Sum(gamma) Shift Bias (0 = True Penrose P3): [{0:0.00}]", _sumGammaBias);
                _meshDirty = true;
            };
            y1 += 29;

            _chkShowEdges = RackCheck(tabQuasi, "Draw Seamless Exterior Tile Wireframe (Rhomb / Hat / Spectre)", 10, y1, true, C(125, 211, 252)); y1 += 21;
            _chkShowJewel = RackCheck(tabQuasi, "Stained-Glass Jewel Facet Shading (4 Galois K-Sheets)", 10, y1, true, C(192, 132, 252)); y1 += 21;
            _chkShowWaveguide = RackCheck(tabQuasi, "Solve & Highlight A* Golden Waveguide Corridor", 10, y1, true, C(52, 211, 153));
            _chkShowWaveguide.CheckedChanged += delegate { _meshDirty = true; _crystal3dDirty = true; };
            y1 += 21;
            _chkShowAmmann = RackCheck(tabQuasi, "Overlay Ammann Matching-Rule Potential Bars", 10, y1, true, C(250, 204, 21)); y1 += 21;
            _chkShowWorms = RackCheck(tabQuasi, "Highlight Conway Worm Pentagrid Ribbons", 10, y1, false, C(244, 114, 182)); y1 += 21;
            _chkAutoPhasonDrift = RackCheck(tabQuasi, "Continuous 5D / 8D Phason Drift (Live Topological Flips)", 10, y1, false, C(167, 139, 250));
            _chkAutoPhasonDrift.CheckedChanged += delegate { _autoPhasonDrift = _chkAutoPhasonDrift.Checked; SyncHeaderButtons(); };
            y1 += 24;

            _lblRhombStats = RackLabel(tabQuasi, "Tiles: -- | Primary/Dual Ratio: -- | Active Phason/Chiral Highlights: --", 10, y1);
            _lblRhombStats.ForeColor = C(52, 211, 153);
            _lblRhombStats.Size = new Size(424, 36);

            // =========================================================================
            // TAB 2: PILLAR 3 — DARWINIAN GENOME FLOW-LENIA & QUANTUM WAVEPACKETS
            // =========================================================================
            int y2 = 8;
            RackHeader(tabLenia, "1. DARWINIAN FLOW-LENIA & KOHMOTO QUANTUM ENGINE", ref y2);
            RackLabel(tabLenia, "Artificial-Life / Quantum Physics Solver:", 10, y2); y2 += 17;
            _cmbLifeMode = RackCombo(tabLenia, 10, y2, 424);
            _cmbLifeMode.Items.AddRange(new object[]
            {
                "0: 3-Channel RGB Mass-Conserving Flow-Lenia",
                "1: Darwinian Genome-Mutating Flow-Lenia (Heritable Mu/Sigma)",
                "2: Kohmoto-Sutherland 2D Quantum Tight-Binding Wavepackets",
                "3: Classic Single-Channel Scalar Lenia (Chan 2019)"
            });
            _cmbLifeMode.SelectedIndex = 0;
            _cmbLifeMode.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                int prev = _lifeMode;
                _lifeMode = _cmbLifeMode.SelectedIndex;
                if (_lifeMode == 2 && prev != 2) SeedQuantumRingState();
                else if (prev == 2 && _lifeMode != 2) SeedPentagonalRingColony();
                SetStatus("Physics Solver -> " + _cmbLifeMode.SelectedItem);
            };
            y2 += 28;

            RackLabel(tabLenia, "Ecosystem / Quantum / Soliton Preset:", 10, y2); y2 += 17;
            _cmbSpecies = RackCombo(tabLenia, 10, y2, 424);
            for (int i = 0; i < OmegaPenroseLeniaPresets.Species.Length; i++)
                _cmbSpecies.Items.Add(OmegaPenroseLeniaPresets.Species[i]);
            _cmbSpecies.SelectedIndex = 0;
            _cmbSpecies.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                ApplySelectedSpeciesParameters(_cmbSpecies.SelectedIndex);
                if (_cmbSpecies.SelectedIndex == 4) { _syncingUi = true; _cmbLifeMode.SelectedIndex = 2; _lifeMode = 2; _syncingUi = false; SeedQuantumRingState(); }
                else if (_cmbSpecies.SelectedIndex == 1) { _syncingUi = true; _cmbLifeMode.SelectedIndex = 1; _lifeMode = 1; _syncingUi = false; SeedPentagonalRingColony(); }
                else if (_lifeMode == 2) SeedQuantumRingState();
                else SeedPentagonalRingColony();
            };
            y2 += 28;

            RackLabel(tabLenia, "Left-Click Stage Brush (or Launch m=5 Quantum Vortex):", 10, y2); y2 += 17;
            _cmbBrushKind = RackCombo(tabLenia, 10, y2, 424);
            _cmbBrushKind.Items.AddRange(new object[]
            {
                "0: Paint Tri-Species Symbiont / m=5 Quantum Ring",
                "1: Paint Species R (Crimson-Gold Predator)",
                "2: Paint Species G (Cyan-Emerald Autotroph)",
                "3: Paint Species B (Magenta-Violet Worm Symbiont)",
                "4: Erase Biomass / Quantum Wavefunction"
            });
            _cmbBrushKind.SelectedIndex = 0;
            _cmbBrushKind.SelectedIndexChanged += delegate
            {
                int idx = _cmbBrushKind.SelectedIndex;
                _brushSelectedKind = (idx == 0) ? 1 : ((idx == 1) ? 3 : ((idx == 2) ? 4 : ((idx == 3) ? 5 : 2)));
            };
            y2 += 29;

            Button bSeedRing = AddRackBtn(tabLenia, "5-Fold RGB", 10, y2, 102, C(190, 24, 93));
            bSeedRing.Click += delegate { if (_lifeMode == 2) { _syncingUi = true; _cmbLifeMode.SelectedIndex = 0; _lifeMode = 0; _syncingUi = false; } SeedPentagonalRingColony(); };
            Button bSeedPsi = AddRackBtn(tabLenia, "Quantum Ring", 116, y2, 108, C(14, 116, 144));
            bSeedPsi.Click += delegate { _syncingUi = true; _cmbLifeMode.SelectedIndex = 2; _lifeMode = 2; _syncingUi = false; SeedQuantumRingState(); };
            Button bSeedSoup = AddRackBtn(tabLenia, "Darwin Soup", 228, y2, 104, C(109, 40, 217));
            bSeedSoup.Click += delegate { _syncingUi = true; _cmbLifeMode.SelectedIndex = 1; _lifeMode = 1; _syncingUi = false; SeedRandomPrimordialSoup(); };
            Button bWipeLife = AddRackBtn(tabLenia, "Clear", 336, y2, 98, C(127, 29, 29));
            bWipeLife.Click += delegate { ClearLeniaBiomass(); };
            y2 += 32;

            RackHeader(tabLenia, "2. DARWINIAN MUTATION, QUASICRYSTAL COUPLING & MASS", ref y2);
            Label lMut = RackLabel(tabLenia, "Darwinian Heritable Genome Mutation Rate: [22%]", 10, y2); y2 += 16;
            _trkMutation = RackSlider(tabLenia, 10, y2, 0, 100, 22);
            _trkMutation.Scroll += delegate
            {
                _mutationRate = _trkMutation.Value / 100.0f;
                lMut.Text = "Darwinian Heritable Genome Mutation Rate: [" + _trkMutation.Value + "%]";
            };
            y2 += 28;

            Label lMu = RackLabel(tabLenia, "Baseline Affinity Growth Center (Mu): [0.210]", 10, y2); y2 += 16;
            _trkMu = RackSlider(tabLenia, 10, y2, 80, 380, 210);
            _trkMu.Scroll += delegate
            {
                _mu = _trkMu.Value / 1000.0f;
                lMu.Text = string.Format(CultureInfo.InvariantCulture, "Baseline Affinity Growth Center (Mu): [{0:0.000}]", _mu);
            };
            y2 += 28;

            Label lSig = RackLabel(tabLenia, "Baseline Affinity Bell Width (Sigma): [0.0480]", 10, y2); y2 += 16;
            _trkSigma = RackSlider(tabLenia, 10, y2, 80, 900, 480);
            _trkSigma.Scroll += delegate
            {
                _sigma = _trkSigma.Value / 10000.0f;
                lSig.Text = string.Format(CultureInfo.InvariantCulture, "Baseline Affinity Bell Width (Sigma): [{0:0.0000}]", _sigma);
            };
            y2 += 28;

            Label lCoup = RackLabel(tabLenia, "Quasicrystal Potential / Quantum Localization V(x): [34%]", 10, y2); y2 += 16;
            _trkCoupling = RackSlider(tabLenia, 10, y2, 0, 100, 34);
            _trkCoupling.Scroll += delegate
            {
                _coupling = _trkCoupling.Value / 100.0f;
                lCoup.Text = "Quasicrystal Potential / Quantum Localization V(x): [" + _trkCoupling.Value + "%]";
            };
            y2 += 28;

            Label lCross = RackLabel(tabLenia, "Inter-Species Predation / Symbiosis Matrix: [42%]", 10, y2); y2 += 16;
            _trkCrossSpecies = RackSlider(tabLenia, 10, y2, 0, 100, 42);
            _trkCrossSpecies.Scroll += delegate
            {
                _crossSpecies = _trkCrossSpecies.Value / 100.0f;
                lCross.Text = "Inter-Species Predation / Symbiosis Matrix: [" + _trkCrossSpecies.Value + "%]";
            };
            y2 += 28;

            Label lStep = RackLabel(tabLenia, "GPU Sub-Steps per Frame: [2x]", 10, y2); y2 += 16;
            _trkSubSteps = RackSlider(tabLenia, 10, y2, 1, 4, 2);
            _trkSubSteps.Scroll += delegate
            {
                _subSteps = _trkSubSteps.Value;
                lStep.Text = "GPU Sub-Steps per Frame: [" + _subSteps + "x]";
            };
            y2 += 28;

            _lblMassStats = RackLabel(tabLenia, "Conserved Biomass :: R=-- | G=-- | B=--", 10, y2);
            _lblMassStats.ForeColor = C(52, 211, 153);
            _lblMassStats.Size = new Size(424, 34);

            // =========================================================================
            // TAB 3: PILLAR 2 — INVERSE LASER HOLOGRAM (CGH), 5D MILLER & AUDIO PIPE
            // =========================================================================
            int y3 = 8;
            RackHeader(tabBragg, "1. INVERSE LASER HOLOGRAM (CGH) & -92dB BRAGG BENCH", ref y3);
            RackLabel(tabBragg, "Optical Diffractometer / Hologram Mode:", 10, y3); y3 += 17;
            _cmbFftSource = RackCombo(tabBragg, 10, y3, 424);
            _cmbFftSource.Items.AddRange(new object[]
            {
                "0: Pure Zero-Mean Dirac Lattice Pin-Holes (-92dB Clean!)",
                "1: Quasicrystal Lattice + RGB Flow-Lenia Phase Grating",
                "2: Pure Biological / Quantum Wavepacket Diffraction",
                "3: Audio-Modulated Cymatic Phason Grating",
                "4: Gerchberg-Saxton Phase-Only Laser Hologram (CGH!)"
            });
            _cmbFftSource.SelectedIndex = 0;
            _cmbFftSource.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                _fftSource = _cmbFftSource.SelectedIndex;
                SetStatus("Bragg Optical Source -> " + _cmbFftSource.SelectedItem);
            };
            y3 += 27;

            RackLabel(tabBragg, "Gerchberg-Saxton 2D FFT Laser Hologram Target:", 10, y3); y3 += 17;
            _cmbHoloTarget = RackCombo(tabBragg, 10, y3, 272);
            for (int i = 0; i < OmegaPenroseGerchbergSaxtonCgh.BuiltInTargets.Length; i++)
                _cmbHoloTarget.Items.Add(OmegaPenroseGerchbergSaxtonCgh.BuiltInTargets[i]);
            _cmbHoloTarget.SelectedIndex = 0;
            _cmbHoloTarget.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                if (_cmbHoloTarget.SelectedIndex < 4) ActivateProceduralHologram(_cmbHoloTarget.SelectedIndex);
                else OpenCustomImageForHologram();
            };

            Button bLoadImgHolo = AddRackBtn(tabBragg, "Load Image -> Holo", 288, y3 - 1, 146, C(15, 118, 110));
            bLoadImgHolo.Click += delegate { OpenCustomImageForHologram(); };
            y3 += 28;

            RackLabel(tabBragg, "Aperture Window:", 10, y3);
            RackLabel(tabBragg, "Laser Wavelength:", 222, y3); y3 += 17;
            _cmbFftWin = RackCombo(tabBragg, 10, y3, 206);
            _cmbFftWin.Items.AddRange(new object[]
            {
                "0: Blackman-Harris (-92dB)",
                "1: Super-Gaussian Beam",
                "2: Hann Cosine (-31dB)",
                "3: Pin-Hole Airy Slit"
            });
            _cmbFftWin.SelectedIndex = 0;
            _cmbFftWin.SelectedIndexChanged += delegate { _fftWinMode = _cmbFftWin.SelectedIndex; };

            _cmbLaserColor = RackCombo(tabBragg, 222, y3, 212);
            _cmbLaserColor.Items.AddRange(new object[]
            {
                "0: 532nm Emerald Nd:YAG",
                "1: 633nm Ruby HeNe Red",
                "2: 405nm Deep Violet",
                "3: Polychromatic X-Ray Laue"
            });
            _cmbLaserColor.SelectedIndex = 0;
            _cmbLaserColor.SelectedIndexChanged += delegate { _laserMode = _cmbLaserColor.SelectedIndex; };
            y3 += 28;

            Label lGain = RackLabel(tabBragg, "Bragg / Hologram Logarithmic Exposure: [95]", 10, y3); y3 += 16;
            _trkBraggGain = RackSlider(tabBragg, 10, y3, 15, 260, 95);
            _trkBraggGain.Scroll += delegate
            {
                _braggGainDb = _trkBraggGain.Value;
                lGain.Text = "Bragg / Hologram Logarithmic Exposure: [" + _trkBraggGain.Value + "]";
            };
            y3 += 28;

            Label lGate = RackLabel(tabBragg, "Obsidian Background Noise-Floor Gate: [14%]", 10, y3); y3 += 16;
            _trkBraggGate = RackSlider(tabBragg, 10, y3, 0, 55, 14);
            _trkBraggGate.Scroll += delegate
            {
                _braggFloorGate = _trkBraggGate.Value / 100.0f;
                lGate.Text = "Obsidian Background Noise-Floor Gate: [" + _trkBraggGate.Value + "%]";
            };
            y3 += 28;

            _chkPhiRings = RackCheck(tabBragg, "Overlay Golden-Ratio Self-Similar Rings (r/Phi^2 .. r*Phi)", 10, y3, true, C(250, 204, 21));
            y3 += 22;

            RackHeader(tabBragg, "2. INTERACTIVE 5D MILLER PEAK INSPECTOR (CLICK BRAGG!)", ref y3);
            _chkShowMillerPlanes = RackCheck(tabBragg, "Overlay Standing 5D Miller Bragg Wave Planes on Real-Space", 10, y3, true, C(56, 189, 248));
            y3 += 22;

            _lblMillerStats = RackLabel(tabBragg, "Click any Bragg diffraction spot on stage to index (h0,h1,h2,h3,h4)!", 10, y3);
            _lblMillerStats.ForeColor = C(52, 211, 153);
            _lblMillerStats.Size = new Size(340, 22);

            Button bClrMiller = AddRackBtn(tabBragg, "Clear Peak", 354, y3 - 3, 80, C(51, 65, 85));
            bClrMiller.Click += delegate
            {
                _millerPeak.Active = false;
                _lblMillerStats.Text = "Miller reticle cleared (click any Bragg peak to inspect).";
            };
            y3 += 25;

            RackHeader(tabBragg, "3. LIVE WASAPI & OMEGASYNTH (:19782) PHASON PIPE", ref y3);
            _chkAudioPipe = RackCheck(tabBragg, "Enable Zero-Feedback WASAPI + OmegaSynth (:19782) Pipe", 10, y3, false, C(56, 189, 248));
            _chkAudioPipe.CheckedChanged += delegate { if (!_syncingUi) ToggleAudioPipe(_chkAudioPipe.Checked); };
            y3 += 21;

            _chkLockP3ZeroSum = RackCheck(tabBragg, "Lock Sum(gamma) = 0 During Audio Phason Kicks (True P3)", 10, y3, true, C(52, 211, 153));
            y3 += 22;

            Label lAudG = RackLabel(tabBragg, "Audio Beat -> Phason Flip & Bragg Pulse Gain: [65%]", 10, y3); y3 += 16;
            _trkAudioPhasonGain = RackSlider(tabBragg, 10, y3, 0, 150, 65);
            _trkAudioPhasonGain.Scroll += delegate
            {
                _audioPhasonGain = _trkAudioPhasonGain.Value / 100.0f;
                lAudG.Text = "Audio Beat -> Phason Flip & Bragg Pulse Gain: [" + _trkAudioPhasonGain.Value + "%]";
            };
            y3 += 28;

            _lblAudioStats = RackLabel(tabBragg, "Audio Pipe: IDLE (Click 'Audio: OFF' or press A to link music / :19782)", 10, y3);
            _lblAudioStats.ForeColor = C(125, 211, 252);
            _lblAudioStats.Size = new Size(424, 32);

            // =========================================================================
            // TAB 4: PILLAR 4 & 5 — HARDWARE Z-BUFFERED SOLID 3D, .STL, .OBJ, CK & .AVI
            // =========================================================================
            int y4 = 8;
            RackHeader(tabExport, "1. HARDWARE Z-BUFFERED SOLID SPECULAR 3D JEWEL", ref y4);
            RackLabel(tabExport, "3D Quasicrystal / Citadel Generator (Left-Drag to Orbit):", 10, y4); y4 += 17;
            _cmb3dKind = RackCombo(tabExport, 10, y4, 424);
            _cmb3dKind.Items.AddRange(new object[]
            {
                "0: 6D->3D Ammann-Kramer-Neri Icosahedral Golden Rhombohedra",
                "1: 5D/Hat->3D Watertight Stepped Citadel + A* Sanctum",
                "2: 8D->3D E8 Elser-Sloane Gosset 240-Root Faceted Star"
            });
            _cmb3dKind.SelectedIndex = 0;
            _cmb3dKind.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                _crystal3dKind = _cmb3dKind.SelectedIndex;
                _crystal3dDirty = true;
                if (_viewMode != 4 && _viewMode != 5) _cmbViewMode.SelectedIndex = 5;
            };
            y4 += 27;

            RackLabel(tabExport, "GPU 3D Depth & Material Pipeline:", 10, y4); y4 += 17;
            _cmb3dShade = RackCombo(tabExport, 10, y4, 424);
            _cmb3dShade.Items.AddRange(new object[]
            {
                "0: True Hardware Atomic Z-Buffered Solid Specular Jewel",
                "1: Translucent Additive X-Ray Quartz Crystal"
            });
            _cmb3dShade.SelectedIndex = 0;
            _cmb3dShade.SelectedIndexChanged += delegate { _shader3dMode = _cmb3dShade.SelectedIndex; };
            y4 += 27;

            _chkAutoRotate3d = RackCheck(tabExport, "Auto-Orbit 3D Crystal / Stepped Citadel Camera", 10, y4, true, C(125, 211, 252));
            y4 += 22;

            Label lCamD = RackLabel(tabExport, "3D Orbit Camera Distance: [5.4]", 10, y4); y4 += 16;
            _trkCam3dDist = RackSlider(tabExport, 10, y4, 28, 95, 54);
            _trkCam3dDist.Scroll += delegate
            {
                _camDist3d = _trkCam3dDist.Value / 10.0f;
                lCamD.Text = string.Format(CultureInfo.InvariantCulture, "3D Orbit Camera Distance: [{0:0.0}]", _camDist3d);
            };
            y4 += 29;

            Button bShow3dSplit = AddRackBtn(tabExport, "Split 2D | 3D", 10, y4, 118, C(14, 116, 144));
            bShow3dSplit.Click += delegate { _cmbViewMode.SelectedIndex = 5; };
            Button bExportStl = AddRackBtn(tabExport, "Export 3D Print (.STL)", 134, y4, 154, C(2, 132, 199));
            bExportStl.Click += delegate { ExportBinaryStlNow(); };
            Button bExportObj = AddRackBtn(tabExport, "Export .OBJ+.MTL", 294, y4, 140, C(5, 150, 105));
            bExportObj.Click += delegate { ExportWavefrontObjNow(); };
            y4 += 30;

            _lbl3dStats = RackLabel(tabExport, "3D Quads: -- (Hardware Z-Buffer Ready)", 10, y4);
            _lbl3dStats.ForeColor = C(52, 211, 153);
            y4 += 21;

            RackHeader(tabExport, "2. CREATION KIT STEPPED CITADEL & RUST L0 EXPORT", ref y4);
            Label lCkSp = RackLabel(tabExport, "Creation Kit XY Cell Spacing (Game Units): [220]", 10, y4); y4 += 16;
            _trkCkScale = RackSlider(tabExport, 10, y4, 80, 600, 220);
            _trkCkScale.Scroll += delegate
            {
                _ckScaleUnits = _trkCkScale.Value;
                lCkSp.Text = "Creation Kit XY Cell Spacing (Game Units): [" + _trkCkScale.Value + "]";
            };
            y4 += 28;

            Label lCkZ = RackLabel(tabExport, "Stepped Terrace Z-Elevation Step (Game Units): [140]", 10, y4); y4 += 16;
            _trkCkZStep = RackSlider(tabExport, 10, y4, 40, 400, 140);
            _trkCkZStep.Scroll += delegate
            {
                _ckZStepUnits = _trkCkZStep.Value;
                lCkZ.Text = "Stepped Terrace Z-Elevation Step (Game Units): [" + _trkCkZStep.Value + "]";
            };
            y4 += 30;

            Button bExpCk = AddRackBtn(tabExport, "Export Skyrim SE + Fallout 4 + Rust L0 Citadel Pack", 10, y4, 424, C(180, 83, 9));
            bExpCk.Click += delegate
            {
                string dir = OmegaPenroseExporters.ExportCreationKitCitadelPack(
                    _exportsDir, _currentRhombs, _panX, _panY, _ckScaleUnits, _ckZStepUnits, 2400.0f);
                SetStatus("Exported Stepped Citadel (.psc + .txt + .csv + .rs) -> " + dir);
                try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
            };
            y4 += 32;

            RackHeader(tabExport, "3. .OMSH SHADER, .SVG, .PNG & 24-FPS .AVI RECORDER", ref y4);
            Button bExpOmsh = AddRackBtn(tabExport, ".OMSH -> AV", 10, y4, 102, C(109, 40, 217));
            bExpOmsh.Click += delegate { ExportOmshAndHotLoadAv(true); };
            Button bExpSvg = AddRackBtn(tabExport, "Vector .SVG", 116, y4, 102, C(30, 58, 138));
            bExpSvg.Click += delegate
            {
                string p = OmegaPenroseExporters.ExportPenroseSvg(_exportsDir, _currentRhombs, _panX, _panY, _zoomUnits, _chkShowWorms.Checked);
                SetStatus("Exported Vector Blueprint .SVG -> " + p);
            };
            Button bExpPng = AddRackBtn(tabExport, "Save .PNG", 222, y4, 102, C(15, 118, 110));
            bExpPng.Click += delegate
            {
                string p = OmegaPenroseExporters.ExportStagePng(_exportsDir, _stage);
                SetStatus("Saved Stage Snapshot -> " + p);
            };
            Button bRecTab = AddRackBtn(tabExport, "Rec .AVI", 328, y4, 106, C(159, 18, 57));
            bRecTab.Click += delegate { ToggleAviRecording(); };
            y4 += 30;

            Button bOpenExp = AddRackBtn(tabExport, "Open 07_omega_penrose\\exports Folder", 10, y4, 424, C(51, 65, 85));
            bOpenExp.Click += delegate
            {
                Directory.CreateDirectory(_exportsDir);
                try { Process.Start("explorer.exe", "\"" + _exportsDir + "\""); } catch { }
            };

            _rightTabs.TabPages.AddRange(new TabPage[] { tabQuasi, tabLenia, tabBragg, tabExport });
            host.Controls.Add(_rightTabs);
            host.Controls.Add(patchBar);
        }

        private void BuildBottomGemmaDock(Panel host)
        {
            Label lAi = new Label();
            lAi.UseMnemonic = false;
            lAi.SetBounds(12, 13, 86, 20);
            lAi.Text = "Gemma AI:";
            lAi.Font = new Font("Segoe UI Semibold", 8.0f);
            lAi.ForeColor = C(192, 132, 252);

            _txtGemmaPrompt = new TextBox();
            _txtGemmaPrompt.SetBounds(100, 11, 344, 24);
            _txtGemmaPrompt.BackColor = C(7, 11, 20);
            _txtGemmaPrompt.ForeColor = Color.White;
            _txtGemmaPrompt.BorderStyle = BorderStyle.FixedSingle;
            _txtGemmaPrompt.Font = new Font("Consolas", 8.5f);
            _txtGemmaPrompt.Text = "Design an 8D E8 quasicrystal with Darwinian niche speciators";
            _txtGemmaPrompt.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; RunGemmaSynthesis(); }
            };

            Button bGen = new Button();
            bGen.UseMnemonic = false;
            bGen.SetBounds(450, 9, 184, 27);
            bGen.Text = "Synthesize via Gemma";
            bGen.FlatStyle = FlatStyle.Flat;
            bGen.FlatAppearance.BorderSize = 0;
            bGen.BackColor = C(124, 58, 237);
            bGen.ForeColor = Color.White;
            bGen.Font = new Font("Segoe UI Semibold", 7.8f);
            bGen.Click += delegate { RunGemmaSynthesis(); };

            _chkSyncAvOnGemma = new CheckBox();
            _chkSyncAvOnGemma.UseMnemonic = false;
            _chkSyncAvOnGemma.SetBounds(642, 13, 162, 20);
            _chkSyncAvOnGemma.Text = "+ Sync .omsh -> AV";
            _chkSyncAvOnGemma.Checked = true;
            _chkSyncAvOnGemma.ForeColor = C(167, 139, 250);
            _chkSyncAvOnGemma.Font = new Font("Segoe UI", 7.8f);

            Label lHints = new Label();
            lHints.UseMnemonic = false;
            lHints.SetBounds(810, 14, 860, 18);
            lHints.Text = "Drop Image=Laser Hologram | Click Bragg=5D Miller | Wheel=Zoom | Right-Drag=Pan | A=Audio | V=Rec .AVI";
            lHints.Font = new Font("Consolas", 7.8f);
            lHints.ForeColor = C(148, 163, 184);

            host.Controls.AddRange(new Control[] { lAi, _txtGemmaPrompt, bGen, _chkSyncAvOnGemma, lHints });
        }

        // =========================================================================
        // PATCH BANK (.OPENROSE), DRAG-DROP IMAGE->HOLOGRAM, 5D MILLER & .AVI
        // =========================================================================
        private void ApplyPatchPreset(OmegaPenrosePatch p)
        {
            _syncingUi = true;
            _symMode = Math.Max(0, Math.Min(8, p.SymMode));
            _cmbSymmetry.SelectedIndex = _symMode;
            _viewMode = Math.Max(0, Math.Min(5, p.ViewMode));
            _cmbViewMode.SelectedIndex = _viewMode;
            _themeIdx = Math.Max(0, Math.Min(3, p.ThemeIdx));
            _cmbTheme.SelectedIndex = _themeIdx;
            _lifeMode = Math.Max(0, Math.Min(3, p.LifeMode));
            _cmbLifeMode.SelectedIndex = _lifeMode;
            _fftSource = Math.Max(0, Math.Min(4, p.FftSource));
            _cmbFftSource.SelectedIndex = _fftSource;
            _fftWinMode = Math.Max(0, Math.Min(3, p.FftWin));
            _cmbFftWin.SelectedIndex = _fftWinMode;
            _laserMode = Math.Max(0, Math.Min(3, p.LaserMode));
            _cmbLaserColor.SelectedIndex = _laserMode;
            _crystal3dKind = Math.Max(0, Math.Min(2, p.Crystal3dKind));
            _cmb3dKind.SelectedIndex = _crystal3dKind;
            _shader3dMode = Math.Max(0, Math.Min(1, p.Shader3dSolid));
            _cmb3dShade.SelectedIndex = _shader3dMode;

            _zoomUnits = Math.Max(3.5f, Math.Min(36.0f, p.ZoomUnits));
            _trkZoom.Value = (int)Math.Round(_zoomUnits * 10.0f);
            _panX = 0.0f; _panY = 0.0f;
            _coupling = p.Coupling;
            _trkCoupling.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_coupling * 100.0f)));
            _mutationRate = p.MutationRate;
            _trkMutation.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_mutationRate * 100.0f)));

            int spIdx = Math.Max(0, Math.Min(OmegaPenroseLeniaPresets.Species.Length - 1, p.SpeciesIdx));
            _cmbSpecies.SelectedIndex = spIdx;
            _syncingUi = false;

            if (p.HologramGlyph >= 0) ActivateProceduralHologram(p.HologramGlyph);
            ApplySelectedSpeciesParameters(spIdx);
            _meshDirty = true;
            _crystal3dDirty = true;
            if (_lifeMode == 2) SeedQuantumRingState();
            else SeedPentagonalRingColony();
            SyncHeaderButtons();
            SetStatus("Loaded GM Patch -> " + p.Name);
        }

        private OmegaPenrosePatch SnapshotCurrentPatch(string title)
        {
            OmegaPenrosePatch p = new OmegaPenrosePatch();
            p.Name = title;
            p.SymMode = _symMode;
            p.ViewMode = _viewMode;
            p.ThemeIdx = _themeIdx;
            p.LifeMode = _lifeMode;
            p.SpeciesIdx = _cmbSpecies.SelectedIndex;
            p.FftSource = _fftSource;
            p.FftWin = _fftWinMode;
            p.LaserMode = _laserMode;
            p.Crystal3dKind = _crystal3dKind;
            p.Shader3dSolid = _shader3dMode;
            p.ZoomUnits = _zoomUnits;
            p.Coupling = _coupling;
            p.MutationRate = _mutationRate;
            p.HologramGlyph = (_fftSource == 4 && _cmbHoloTarget.SelectedIndex < 4) ? _cmbHoloTarget.SelectedIndex : -1;
            return p;
        }

        private void SaveOpenrosePatchDialog()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.InitialDirectory = _patchesDir;
                sfd.Filter = "OmegaPenrose Patch (*.openrose)|*.openrose";
                sfd.FileName = "custom_gm_" + DateTime.Now.ToString("HHmmss") + ".openrose";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    OmegaPenrosePatch p = SnapshotCurrentPatch(Path.GetFileNameWithoutExtension(sfd.FileName));
                    File.WriteAllText(sfd.FileName, p.Serialize(), new UTF8Encoding(false));
                    _cmbPatchBank.Items.Add(p);
                    _syncingUi = true; _cmbPatchBank.SelectedItem = p; _syncingUi = false;
                    SetStatus("Saved .openrose patch -> " + sfd.FileName);
                }
            }
        }

        private void LoadOpenrosePatchDialog()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.InitialDirectory = _patchesDir;
                ofd.Filter = "OmegaPenrose Patch (*.openrose)|*.openrose";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadPatchFile(ofd.FileName);
                }
            }
        }

        private void LoadPatchFile(string path)
        {
            OmegaPenrosePatch p = OmegaPenrosePatch.LoadFromFile(path);
            _cmbPatchBank.Items.Add(p);
            _syncingUi = true; _cmbPatchBank.SelectedItem = p; _syncingUi = false;
            ApplyPatchPreset(p);
        }

        private void ActivateProceduralHologram(int kind)
        {
            string lbl;
            float[] phase = OmegaPenroseGerchbergSaxtonCgh.SynthesizeProceduralGlyph(kind, out lbl);
            _activeHoloLabel = lbl;
            _gpu.UploadHologramPhase256(phase);
            _syncingUi = true;
            _fftSource = 4;
            _cmbFftSource.SelectedIndex = 4;
            if (kind >= 0 && kind < 4) _cmbHoloTarget.SelectedIndex = kind;
            if (_viewMode != 0 && _viewMode != 2) { _viewMode = 0; _cmbViewMode.SelectedIndex = 0; }
            _syncingUi = false;
            SyncHeaderButtons();
            SetStatus("Synthesized 256x256 Gerchberg-Saxton Phase-Only Laser Hologram -> " + lbl + "!");
        }

        private void OpenCustomImageForHologram()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Any Image (.PNG / .JPG / .BMP) -> Synthesize 2D FFT Laser Hologram";
                ofd.Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadImageAsLaserHologram(ofd.FileName);
                }
            }
        }

        private void LoadImageAsLaserHologram(string imgPath)
        {
            try
            {
                string lbl;
                float[] phase = OmegaPenroseGerchbergSaxtonCgh.SynthesizeFromImageFile(imgPath, out lbl);
                _activeHoloLabel = lbl;
                _gpu.UploadHologramPhase256(phase);
                _syncingUi = true;
                _fftSource = 4;
                _cmbFftSource.SelectedIndex = 4;
                _cmbHoloTarget.SelectedIndex = 4;
                if (_viewMode != 0 && _viewMode != 2) { _viewMode = 0; _cmbViewMode.SelectedIndex = 0; }
                _syncingUi = false;
                SyncHeaderButtons();
                SetStatus("Reconstructed Custom Image inside GPU 2D FFT Laser Diffractometer (20 GS iters): " + lbl + "!");
            }
            catch (Exception ex)
            {
                SetStatus("Hologram image error: " + ex.Message);
            }
        }

        private void OnWindowDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnWindowDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                string f = files[0];
                string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant();
                if (ext == ".openrose") LoadPatchFile(f);
                else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                    LoadImageAsLaserHologram(f);
            }
            catch { }
        }

        private void InspectMillerBraggSpot(float buvX, float buvY)
        {
            _millerPeak = OmegaPenroseDeBruijnMath.IndexBraggPeak5D(buvX, buvY, _zoomUnits);
            if (_lblMillerStats != null) _lblMillerStats.Text = _millerPeak.Summary;
            SetStatus("5D Reciprocal Bragg Inspector :: " + _millerPeak.Summary + " (Standing planes shown on left!)");
        }

        private void ToggleAviRecording()
        {
            if (!_aviRecorder.IsRecording)
            {
                _aviRecorder.Start(_exportsDir, Math.Max(320, _stage.ClientSize.Width), Math.Max(240, _stage.ClientSize.Height), 24);
                SyncHeaderButtons();
                SetStatus("Recording 24-FPS Motion-JPEG .AVI loop... Click 'STOP [.AVI]' (or press V) to save!");
            }
            else
            {
                string saved = _aviRecorder.StopAndSave();
                SyncHeaderButtons();
                if (!string.IsNullOrEmpty(saved))
                {
                    SetStatus("Saved 24-FPS .AVI Video Loop -> " + saved);
                    try { Process.Start("explorer.exe", "/select,\"" + saved + "\""); } catch { }
                }
            }
        }

        private void ToggleAudioPipe(bool on)
        {
            _audioPipe.Enabled = on;
            _syncingUi = true;
            _chkAudioPipe.Checked = on;
            _syncingUi = false;
            SyncHeaderButtons();
            SetStatus(on
                ? "Audio-Phason Pipe LIVE :: Zero-feedback WASAPI + OmegaSynth (:19782) modulating shifts & Bragg rings!"
                : "Audio-Phason Pipe OFF.");
        }

        private void ResetZoomAndPan()
        {
            _zoomUnits = 14.0f;
            _panX = 0.0f; _panY = 0.0f;
            _yaw3d = 0.62f; _pitch3d = 0.44f; _camDist3d = 5.4f;
            _syncingUi = true;
            _trkZoom.Value = 140;
            _trkCam3dDist.Value = 54;
            _syncingUi = false;
            _meshDirty = true;
            _crystal3dDirty = true;
            SetStatus("Reset Jewel Camera Zoom (14.0), Pan (0,0) & 3D Orbit!");
        }

        private void ApplyZoomDelta(float deltaTiles)
        {
            _zoomUnits = Math.Max(3.5f, Math.Min(36.0f, _zoomUnits + deltaTiles));
            _syncingUi = true;
            int tv = (int)Math.Round(_zoomUnits * 10.0f);
            _trkZoom.Value = Math.Max(_trkZoom.Minimum, Math.Min(_trkZoom.Maximum, tv));
            _syncingUi = false;
            _meshDirty = true;
            SetStatus(string.Format(CultureInfo.InvariantCulture, "Jewel Zoom -> {0:0.0} tiles", _zoomUnits));
        }

        private void OnStageMouseWheel(object sender, MouseEventArgs e)
        {
            if (_txtGemmaPrompt.Focused) return;
            Point clientPt = _stage.PointToClient(Cursor.Position);
            if (clientPt.X < 0 || clientPt.Y < 0 || clientPt.X > _stage.Width || clientPt.Y > _stage.Height)
                return;

            float steps = -e.Delta / 120.0f;
            if (_viewMode == 4 || (_viewMode == 5 && clientPt.X > _stage.Width / 2))
            {
                _camDist3d = Math.Max(2.8f, Math.Min(9.5f, _camDist3d + steps * 0.45f));
                _syncingUi = true;
                _trkCam3dDist.Value = Math.Max(28, Math.Min(95, (int)Math.Round(_camDist3d * 10.0f)));
                _syncingUi = false;
            }
            else
            {
                float scaleStep = Math.Max(0.45f, _zoomUnits * 0.11f) * steps;
                ApplyZoomDelta(scaleStep);
            }
        }

        private void RebuildDualMeshNow()
        {
            float audGain = _audioPipe.Enabled ? _audioPhasonGain : 0.0f;
            OmegaPenroseDeBruijnMath.GetPresetShifts5D(
                _symMode, _phasonPhase, _sumGammaBias,
                _audioPipe.SubBassLevel * audGain,
                _audioPipe.MidHarmLevel * audGain,
                _audioPipe.HighAirLevel * audGain,
                _chkLockP3ZeroSum != null && _chkLockP3ZeroSum.Checked,
                _gamma5);

            _currentRhombs = OmegaPenroseDeBruijnMath.BuildDualPenroseMesh(
                _symMode, _gamma5, _zoomUnits, _panX, _panY, _phasonPhase,
                _chkShowWaveguide != null && _chkShowWaveguide.Checked,
                out _thickCount, out _thinCount, out _phasonFlipCount);

            _gpu.UploadDualRhombMesh(_currentRhombs);

            if (_lblRhombStats != null)
            {
                double ratio = (_thinCount > 0) ? ((double)_thickCount / _thinCount) : 0.0;
                if (_symMode == 7)
                {
                    _lblRhombStats.Text = string.Format(CultureInfo.InvariantCulture,
                        "Einstein Hat Tile(1,sqrt3): 139 Tiles (Unrefl:{0} Anti-Hat:{1} | Ratio:{2:0.00} ~ Phi^4=6.85)",
                        _thickCount, _thinCount, ratio);
                }
                else if (_symMode == 8)
                {
                    _lblRhombStats.Text = string.Format(CultureInfo.InvariantCulture,
                        "Chiral Spectre Tile(1,1): 96 Zero-Reflection Tiles (Even:{0} Odd Mystic:{1})",
                        _thickCount, _thinCount);
                }
                else
                {
                    _lblRhombStats.Text = string.Format(CultureInfo.InvariantCulture,
                        "Primitives: {0} (Primary:{1} Dual:{2} | Ratio:{3:0.000}) | Active Flips: {4}",
                        _currentRhombs.Count, _thickCount, _thinCount, ratio, _phasonFlipCount);
                }
            }
        }

        private void Rebuild3dCrystalNow()
        {
            if (_crystal3dKind == 0)
            {
                _currentQuads3d = OmegaPenroseIcosahedral3D.BuildIcosahedralRhombohedra6D(
                    _phasonPhase, 3.2f, out _prolate3dCount, out _oblate3dCount);
                if (_lbl3dStats != null)
                    _lbl3dStats.Text = string.Format(CultureInfo.InvariantCulture,
                        "6D->3D Icosahedral Quads: {0} (Prolate:{1} Oblate:{2} | Solid Z-Buffer)",
                        _currentQuads3d.Count, _prolate3dCount, _oblate3dCount);
            }
            else if (_crystal3dKind == 2)
            {
                _currentQuads3d = OmegaPenroseIcosahedral3D.BuildE8Gosset3DStar(_phasonPhase, out _e8RootFacetCount);
                if (_lbl3dStats != null)
                    _lbl3dStats.Text = "8D->3D E8 Gosset 240-Root Star Facets: " + _e8RootFacetCount + " (Solid Z-Buffer)";
            }
            else
            {
                _currentQuads3d = OmegaPenroseIcosahedral3D.BuildSteppedCitadelFrom2D(_currentRhombs, _panX, _panY);
                if (_lbl3dStats != null)
                    _lbl3dStats.Text = "Watertight 3D Stepped Citadel Quads: " + _currentQuads3d.Count + " (.STL Print Ready!)";
            }
        }

        private void ExportBinaryStlNow()
        {
            Rebuild3dCrystalNow();
            string tag = (_crystal3dKind == 0) ? "6D_Icosahedral" : ((_crystal3dKind == 2) ? "8D_E8_GossetStar" : "Stepped_Citadel");
            string path = OmegaPenroseExporters.ExportBinaryStl3D(_exportsDir, _currentQuads3d, tag);
            SetStatus("Exported IEEE-754 Binary 3D-Printer .STL Mesh -> " + path);
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        private void ExportWavefrontObjNow()
        {
            Rebuild3dCrystalNow();
            string tag = (_crystal3dKind == 0) ? "6D_Icosahedral" : ((_crystal3dKind == 2) ? "8D_E8_GossetStar" : "Stepped_Citadel");
            string path = OmegaPenroseExporters.ExportWavefrontObj3D(_exportsDir, _currentQuads3d, tag);
            SetStatus("Exported Wavefront 3D Mesh (.OBJ + .MTL) -> " + path);
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        private void ApplySelectedSpeciesParameters(int idx)
        {
            idx = Math.Max(0, Math.Min(OmegaPenroseLeniaPresets.Species.Length - 1, idx));
            LeniaSpeciesSpec s = OmegaPenroseLeniaPresets.Species[idx];
            _mu = s.Mu; _sigma = s.Sigma; _kernelRadius = s.KernelRadius; _dt = s.Dt; _crossSpecies = s.FlowCrossGain;

            _syncingUi = true;
            if (_trkMu != null) _trkMu.Value = Math.Max(_trkMu.Minimum, Math.Min(_trkMu.Maximum, (int)Math.Round(_mu * 1000.0f)));
            if (_trkSigma != null) _trkSigma.Value = Math.Max(_trkSigma.Minimum, Math.Min(_trkSigma.Maximum, (int)Math.Round(_sigma * 10000.0f)));
            if (_trkCrossSpecies != null) _trkCrossSpecies.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_crossSpecies * 100.0f)));
            _syncingUi = false;

            _gpu.UploadLeniaKernel33(OmegaPenroseLeniaPresets.BuildNormalizedKernel33(s));
        }

        private void ClearLeniaBiomass()
        {
            Array.Clear(_seedFieldBuffer, 0, _seedFieldBuffer.Length);
            Array.Clear(_seedGenomeBuffer, 0, _seedGenomeBuffer.Length);
            _gpu.QueueRgbAndGenomeUpload(_seedFieldBuffer, _seedGenomeBuffer, _mu, _sigma);
            SetStatus("Cleared field to vacuum.");
        }

        private void SeedPentagonalRingColony()
        {
            Array.Clear(_seedFieldBuffer, 0, _seedFieldBuffer.Length);
            int nCells = OmegaPenroseGpuEngine.GridN * OmegaPenroseGpuEngine.GridN;
            for (int i = 0; i < nCells; i++) { _seedGenomeBuffer[i * 2] = _mu; _seedGenomeBuffer[i * 2 + 1] = _sigma; }

            int spIdx = (_cmbSpecies != null) ? Math.Max(0, _cmbSpecies.SelectedIndex) : 0;
            LeniaSpeciesSpec s = OmegaPenroseLeniaPresets.Species[spIdx];

            for (int k = 0; k < 5; k++)
            {
                double ang = 2.0 * Math.PI * k / 5.0;
                OmegaPenroseLeniaPresets.StampRgbOrganismIntoField(
                    _seedFieldBuffer, _seedGenomeBuffer, OmegaPenroseGpuEngine.GridN,
                    0.5f + 0.19f * (float)Math.Cos(ang), 0.5f + 0.19f * (float)Math.Sin(ang), s, (float)(ang + Math.PI), 2);

                double ang2 = ang + Math.PI / 5.0;
                OmegaPenroseLeniaPresets.StampRgbOrganismIntoField(
                    _seedFieldBuffer, _seedGenomeBuffer, OmegaPenroseGpuEngine.GridN,
                    0.5f + 0.31f * (float)Math.Cos(ang2), 0.5f + 0.31f * (float)Math.Sin(ang2), s, (float)ang2, (k % 2 == 0) ? 1 : 3);
            }
            OmegaPenroseLeniaPresets.StampRgbOrganismIntoField(
                _seedFieldBuffer, _seedGenomeBuffer, OmegaPenroseGpuEngine.GridN, 0.5f, 0.5f, s, 0.0f, 0);
            _gpu.QueueRgbAndGenomeUpload(_seedFieldBuffer, _seedGenomeBuffer, _mu, _sigma);
            SetStatus("Spawned 5-fold RGB / Darwinian Colony (" + s.Name + ")!");
        }

        private void SeedQuantumRingState()
        {
            Array.Clear(_seedFieldBuffer, 0, _seedFieldBuffer.Length);
            // Central m=5 Kohmoto-Sutherland ring state + 5 satellite wavepackets!
            OmegaPenroseLeniaPresets.StampQuantumWavepacketIntoField(
                _seedFieldBuffer, OmegaPenroseGpuEngine.GridN, 0.5f, 0.5f, 16.0f, 0.0f, 0.0f, 5);
            for (int k = 0; k < 5; k++)
            {
                double a = 2.0 * Math.PI * k / 5.0;
                float kx = -0.35f * (float)Math.Cos(a);
                float ky = -0.35f * (float)Math.Sin(a);
                OmegaPenroseLeniaPresets.StampQuantumWavepacketIntoField(
                    _seedFieldBuffer, OmegaPenroseGpuEngine.GridN,
                    0.5f + 0.24f * (float)Math.Cos(a), 0.5f + 0.24f * (float)Math.Sin(a), 11.0f, kx, ky, (k % 2 == 0) ? 3 : -3);
            }
            _gpu.QueueRgbAndGenomeUpload(_seedFieldBuffer, null, _mu, _sigma);
            SetStatus("Launched Unitary Symplectic Kohmoto-Sutherland 2D Quantum Wavepackets (|psi|^2 + Rainbow Phase)!");
        }

        private void SeedRandomPrimordialSoup()
        {
            Array.Clear(_seedFieldBuffer, 0, _seedFieldBuffer.Length);
            int nCells = OmegaPenroseGpuEngine.GridN * OmegaPenroseGpuEngine.GridN;
            for (int i = 0; i < nCells; i++) { _seedGenomeBuffer[i * 2] = _mu; _seedGenomeBuffer[i * 2 + 1] = _sigma; }

            int spIdx = (_cmbSpecies != null) ? Math.Max(0, _cmbSpecies.SelectedIndex) : 0;
            LeniaSpeciesSpec s = OmegaPenroseLeniaPresets.Species[spIdx];
            for (int i = 0; i < 24; i++)
            {
                float nx = 0.10f + (float)_rng.NextDouble() * 0.80f;
                float ny = 0.10f + (float)_rng.NextDouble() * 0.80f;
                OmegaPenroseLeniaPresets.StampRgbOrganismIntoField(
                    _seedFieldBuffer, _seedGenomeBuffer, OmegaPenroseGpuEngine.GridN, nx, ny, s, (float)(_rng.NextDouble() * 6.28), 1 + (i % 3));
            }
            _gpu.QueueRgbAndGenomeUpload(_seedFieldBuffer, _seedGenomeBuffer, _mu, _sigma);
            SetStatus("Seeded 24-creature Darwinian heritable-genome ecosystem soup!");
        }

        private void StampSpeciesAtNormalized(float nx, float ny, int ch)
        {
            _pendingBrushX = Math.Max(0.02f, Math.Min(0.98f, nx));
            _pendingBrushY = Math.Max(0.02f, Math.Min(0.98f, ny));
            _pendingBrushMode = (ch == 0) ? _brushSelectedKind : ch;
        }

        private void OnStageMouseDown(object sender, MouseEventArgs e)
        {
            _stage.Focus();
            _lastMousePt = e.Location;
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _rightPanDrag = true;
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                int w = Math.Max(64, _stage.ClientSize.Width);
                int h = Math.Max(64, _stage.ClientSize.Height);

                if (_viewMode == 4 || (_viewMode == 5 && e.X > w / 2))
                {
                    _orbit3dDrag = true;
                    return;
                }

                // Check if user clicked inside the Bragg Diffractometer panel (View 2 or Right side of View 0)!
                if (_viewMode == 2)
                {
                    float side = Math.Min(w, h) * 0.95f;
                    float tlx = (w - side) * 0.5f, tly = (h - side) * 0.5f;
                    float bu = (e.X - tlx) / side, bv = (e.Y - tly) / side;
                    if (bu >= 0f && bu <= 1f && bv >= 0f && bv <= 1f) { InspectMillerBraggSpot(bu, bv); return; }
                }
                else if (_viewMode == 0 && e.X > w / 2)
                {
                    float side = Math.Min(w * 0.475f, h * 0.93f);
                    float tlRx = w - side - 18.0f, tlRy = (h - side) * 0.5f;
                    float bu = (e.X - tlRx) / side, bv = (e.Y - tlRy) / side;
                    if (bu >= 0f && bu <= 1f && bv >= 0f && bv <= 1f) { InspectMillerBraggSpot(bu, bv); return; }
                }

                _leftDrag = true;
                HandleStageBrushMouse(e);
            }
        }

        private void OnStageMouseMove(object sender, MouseEventArgs e)
        {
            int dx = e.X - _lastMousePt.X;
            int dy = e.Y - _lastMousePt.Y;
            _lastMousePt = e.Location;

            if (_rightPanDrag)
            {
                float side = Math.Max(120.0f, Math.Min(_stage.ClientSize.Width * 0.48f, _stage.ClientSize.Height * 0.93f));
                float unitsPerPx = _zoomUnits / side;
                _panX -= dx * unitsPerPx;
                _panY -= dy * unitsPerPx;
                _meshDirty = true;
                return;
            }
            if (_orbit3dDrag)
            {
                _yaw3d += dx * 0.011f;
                _pitch3d = Math.Max(-1.35f, Math.Min(1.35f, _pitch3d + dy * 0.011f));
                return;
            }
            if (_leftDrag) HandleStageBrushMouse(e);
        }

        private void HandleStageBrushMouse(MouseEventArgs e)
        {
            int w = Math.Max(64, _stage.ClientSize.Width);
            int h = Math.Max(64, _stage.ClientSize.Height);
            float side, tlx, tly;
            if (_viewMode == 1) { side = Math.Min(w, h) * 0.95f; tlx = (w - side) * 0.5f; tly = (h - side) * 0.5f; }
            else if (_viewMode == 2 || _viewMode == 4) return;
            else { side = Math.Min(w * 0.475f, h * 0.93f); tlx = 18.0f; tly = (h - side) * 0.5f; }

            float ux = (e.X - tlx) / Math.Max(1.0f, side);
            float uy = (e.Y - tly) / Math.Max(1.0f, side);
            if (ux < 0.0f || ux > 1.0f || uy < 0.0f || uy > 1.0f) return;

            _pendingBrushX = ux;
            _pendingBrushY = uy;
            _pendingBrushMode = _brushSelectedKind;
        }

        private void OnStageCpuPaint(object sender, PaintEventArgs e)
        {
            if (_gpu.IsGpuActive) return;
            int w = Math.Max(64, _stage.ClientSize.Width), h = Math.Max(64, _stage.ClientSize.Height);
            if (_cpuFallbackBmp == null || _cpuFallbackBmp.Width != w || _cpuFallbackBmp.Height != h)
            {
                if (_cpuFallbackBmp != null) _cpuFallbackBmp.Dispose();
                _cpuFallbackBmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            _gpu.RenderCpuFallbackBitmap(_cpuFallbackBmp, _gamma5, _zoomUnits);
            e.Graphics.DrawImageUnscaled(_cpuFallbackBmp, 0, 0);
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (_txtGemmaPrompt.Focused) return;
            if (e.KeyCode == Keys.Space) { _paused = !_paused; SyncHeaderButtons(); e.Handled = true; }
            else if (e.KeyCode == Keys.R) { if (_lifeMode == 2) SeedQuantumRingState(); else SeedPentagonalRingColony(); e.Handled = true; }
            else if (e.KeyCode == Keys.Q) { _cmbLifeMode.SelectedIndex = 2; SeedQuantumRingState(); e.Handled = true; }
            else if (e.KeyCode == Keys.H) { ActivateProceduralHologram(0); e.Handled = true; }
            else if (e.KeyCode == Keys.S) { SeedRandomPrimordialSoup(); e.Handled = true; }
            else if (e.KeyCode == Keys.C) { ClearLeniaBiomass(); e.Handled = true; }
            else if (e.KeyCode == Keys.A) { ToggleAudioPipe(!_audioPipe.Enabled); e.Handled = true; }
            else if (e.KeyCode == Keys.V) { ToggleAviRecording(); e.Handled = true; }
            else if (e.KeyCode == Keys.D3) { _cmbViewMode.SelectedIndex = 5; e.Handled = true; }
        }

        private void SyncHeaderButtons()
        {
            _btnTopPause.Text = _paused ? "Sim: PAUSED" : "Sim: LIVE";
            _btnTopPause.BackColor = _paused ? C(180, 83, 9) : C(15, 118, 110);
            _btnTopAutoDrift.Text = _autoPhasonDrift ? "5D Drift: ON" : "5D Drift: OFF";
            _btnTopAutoDrift.BackColor = _autoPhasonDrift ? C(124, 58, 237) : C(51, 65, 85);
            _btnTopAudio.Text = _audioPipe.Enabled ? "Audio: LIVE" : "Audio: OFF";
            _btnTopAudio.BackColor = _audioPipe.Enabled ? C(217, 119, 6) : C(30, 58, 138);
            _btnTop3d.BackColor = (_viewMode == 4 || _viewMode == 5) ? C(5, 150, 105) : C(14, 116, 144);
            _btnTopRecAvi.Text = _aviRecorder.IsRecording ? ("STOP [" + _aviRecorder.FrameCount + "f]") : "Rec .AVI";
            _btnTopRecAvi.BackColor = _aviRecorder.IsRecording ? C(220, 38, 38) : C(159, 18, 57);
        }

        private void UpdateUiTelemetryTick()
        {
            if (_aviRecorder.IsRecording) SyncHeaderButtons();

            string symShort = (_symMode == 6) ? "8D-E8" : (_symMode == 7 ? "Hat(1,rt3)" : (_symMode == 8 ? "Spectre(1,1)" : ("Sym#" + (_symMode + 1))));
            string lifeShort = (_lifeMode == 2) ? "Quantum-Psi" : (_lifeMode == 1 ? "Darwin-RGB" : "Flow-RGB");
            string optShort = (_fftSource == 4) ? ("Holo:" + _activeHoloLabel) : (_millerPeak.Active ? ("Miller(" + _millerPeak.H0 + "," + _millerPeak.H1 + "," + _millerPeak.H2 + "," + _millerPeak.H3 + "," + _millerPeak.H4 + ")") : "-92dB Bragg");
            string audTag = _audioPipe.Enabled
                ? (_audioPipe.SynthLinkLive
                    ? string.Format(CultureInfo.InvariantCulture, "Synth({0},{1})", _audioPipe.SynthChladniM, _audioPipe.SynthChladniN)
                    : string.Format(CultureInfo.InvariantCulture, "WASAPI:{0:0}%", _audioPipe.SubBassLevel * 100.0f))
                : "Audio:OFF";

            _lblTelemetryRow2.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} ({1} FPS) | {2} Zoom:{3:0.0} Prims:{4} | {5} | {6} | {7}",
                _gpu.BackendLabel, _measuredFps, symShort, _zoomUnits, _currentRhombs.Count, lifeShort, optShort, audTag);

            if (_lblMassStats != null)
            {
                if (_lifeMode == 2)
                    _lblMassStats.Text = "Unitary Symplectic Leapfrog :: Total |psi|^2 Norm Conserved (Click stage -> launch m=5 vortex!)";
                else
                    _lblMassStats.Text = string.Format(CultureInfo.InvariantCulture,
                        "Conserved Biomass :: R={0:0} | G={1:0} | B={2:0} ({3})",
                        _gpu.TotalConservedMassR, _gpu.TotalConservedMassG, _gpu.TotalConservedMassB,
                        _lifeMode == 1 ? "Darwinian Mutating Genome" : "Exact 9-Tap Reintegration");
            }
            if (_lblAudioStats != null && _audioPipe.Enabled)
            {
                _lblAudioStats.Text = string.Format(CultureInfo.InvariantCulture,
                    "WASAPI Sub:{0:0}% Mid:{1:0}% Hi:{2:0}% | OmegaSynth(:19782): {3}",
                    _audioPipe.SubBassLevel * 100.0f, _audioPipe.MidHarmLevel * 100.0f, _audioPipe.HighAirLevel * 100.0f,
                    _audioPipe.SynthLinkLive
                        ? ("LINKED Chladni(" + _audioPipe.SynthChladniM + "," + _audioPipe.SynthChladniN + ") " + _audioPipe.SynthBpm + " BPM")
                        : "Standby (Speakers Active)");
            }
        }

        private void ExportOmshAndHotLoadAv(bool launchAv)
        {
            string p = OmegaPenroseExporters.ExportOmshShader(_exportsDir, _avDir, _symMode, _gamma5, _zoomUnits, true);
            if (launchAv && File.Exists(_avExe))
            {
                try
                {
                    if (Process.GetProcessesByName("OmegaAV").Length == 0)
                        Process.Start(new ProcessStartInfo(_avExe) { WorkingDirectory = _avDir });
                }
                catch { }
            }
            SetStatus("Synced quasicrystal .omsh -> OmegaAV (:19779) + saved " + Path.GetFileName(p));
        }

        private void RunGemmaSynthesis()
        {
            string prompt = _txtGemmaPrompt.Text;
            SetStatus("Querying local Gemma (:8097 / :11434) for GM quasicrystal + Darwinian/Quantum configuration...");
            OmegaPenroseGemmaSynthesizer.SynthesizeAsync(prompt, delegate(int sym, int lm, int sp, float phason, float coup, string note)
            {
                if (IsDisposed) return;
                BeginInvoke((Action)delegate
                {
                    _syncingUi = true;
                    _cmbSymmetry.SelectedIndex = sym; _symMode = sym;
                    _cmbLifeMode.SelectedIndex = lm; _lifeMode = lm;
                    _cmbSpecies.SelectedIndex = sp;
                    _phasonPhase = phason;
                    _coupling = coup;
                    _trkCoupling.Value = Math.Max(0, Math.Min(100, (int)Math.Round(coup * 100.0f)));
                    _syncingUi = false;

                    ApplySelectedSpeciesParameters(sp);
                    _meshDirty = true; _crystal3dDirty = true;
                    if (_lifeMode == 2) SeedQuantumRingState();
                    else SeedPentagonalRingColony();
                    if (_chkSyncAvOnGemma.Checked) ExportOmshAndHotLoadAv(false);
                    SetStatus(note + " :: " + OmegaPenroseDeBruijnMath.SymmetryModeNames[sym]);
                });
            });
        }

        private void BeamPadToPixelAsync()
        {
            SetStatus("Arming ADB USB tunnel tcp:19785 + opening v1.0 GM OLED Pad on Pixel...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string adb = FindAdbExe();
                    if (string.IsNullOrEmpty(adb)) { SetStatus("ADB not found; open http://<PC-IP>:19785/pad on phone."); return; }
                    RunProc(adb, "reverse tcp:19785 tcp:19785", 1800);
                    RunProc(adb, "shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19785/pad\"", 2200);
                    SetStatus("Beamed OmegaPenrose v1.0 GM Pad -> Pixel (:19785/pad)!");
                }
                catch (Exception ex) { SetStatus("Pixel beam note: " + ex.Message); }
            });
        }

        private static string FindAdbExe()
        {
            string[] cands = new string[]
            {
                @"C:\platform-tools\adb.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe"),
                "adb.exe"
            };
            for (int i = 0; i < cands.Length; i++) if (File.Exists(cands[i])) return cands[i];
            return "adb.exe";
        }

        private static void RunProc(string exe, string args, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true; psi.UseShellExecute = false;
            using (Process p = Process.Start(psi)) { if (p != null) p.WaitForExit(timeoutMs); }
        }

        private string BuildStatusJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"app\":\"OmegaPenrose v1.0.2 Final GM\",\"port\":19785,\"gpu\":{0},\"fps\":{1},\"zoom\":{2:0.0},\"primitives\":{3},\"symmetry\":{4},\"life_mode\":{5},\"fft_source\":{6},\"audio_pipe\":{7}}}",
                _gpu.IsGpuActive ? "true" : "false", _measuredFps, _zoomUnits, _currentRhombs.Count, _symMode, _lifeMode, _fftSource, _audioPipe.Enabled ? "true" : "false");
        }

        private void ShowQuickGuide()
        {
            MessageBox.Show(this,
                "OMEGAPENROSE v1.0.2 FINAL GM — 5-PILLAR GOLDEN MASTER GUIDE (:19785)\r\n\r\n" +
                "1. 8D E8 ELSER-SLOANE H4 & EINSTEIN 'HAT' / 'SPECTRE' MONOTILES (Tab 1):\r\n" +
                "   - Choose Symmetries 07 (240-root E8 30-fold Coxeter-Icosian), 08 (Gap-free Einstein Hat Tile(1,sqrt3) with royal-gold Anti-Hats), or 09 (Zero-reflection Chiral Spectre Tile(1,1))!\r\n\r\n" +
                "2. INVERSE LASER HOLOGRAM (CGH) & 5D MILLER PEAK INSPECTOR (Tab 3):\r\n" +
                "   - Drag-and-drop ANY image (.png/.jpg/.bmp) onto the window (or pick a sacred glyph) to run 20 Gerchberg-Saxton 2D FFT iterations and reconstruct it inside the live laser beam!\r\n" +
                "   - Left-click any Bragg diffraction spot to index its exact 5D Miller vector (h0..h4) and overlay real-space standing Bragg wave planes!\r\n\r\n" +
                "3. DARWINIAN GENOME FLOW-LENIA & QUANTUM WAVEPACKETS (Tab 2):\r\n" +
                "   - Mode 1 advects heritable (Mu, Sigma) genes with live mutation across quasicrystal niches.\r\n" +
                "   - Mode 2 runs a unitary symplectic leapfrog 2D Kohmoto-Sutherland tight-binding Schrodinger wavepacket solver (press Q)!\r\n\r\n" +
                "4. HARDWARE ATOMIC Z-BUFFERED SOLID 3D JEWEL & BINARY .STL (Tab 4):\r\n" +
                "   - GPU InterlockedMax 14-bit depth buffer renders opaque Blinn-Phong specular 6D/8D crystals & Citadels, plus 1-click 3D-printer Binary .STL, .OBJ/.MTL & Creation Kit packs!\r\n\r\n" +
                "5. 10-PRESET .OPENROSE PATCH BANK & 24-FPS .AVI VIDEO RECORDER:\r\n" +
                "   - Switch among 10 curated GM patches at the top of the right rack, or click 'Rec .AVI' (press V) to capture zero-NuGet MJPEG video loops!",
                "OmegaPenrose v1.0.2 Final GM — Studio Guide", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetStatus(string msg)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)delegate { _lblStatusBot.Text = msg; }); return; }
            _lblStatusBot.Text = msg;
        }

        private static Button TopBtn(string text, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.Size = new Size(w, 27);
            b.Margin = new Padding(4, 0, 0, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 7.7f);
            return b;
        }

        private static void RackHeader(Control p, string title, ref int y)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.SetBounds(8, y, 430, 18);
            l.Text = title;
            l.Font = new Font("Segoe UI", 7.9f, FontStyle.Bold);
            l.ForeColor = C(244, 114, 182);
            p.Controls.Add(l);
            y += 19;
        }

        private static Label RackLabel(Control p, string text, int x, int y)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.SetBounds(x, y, 426, 16);
            l.Text = text;
            l.Font = new Font("Segoe UI", 7.8f);
            l.ForeColor = C(203, 213, 225);
            p.Controls.Add(l);
            return l;
        }

        private static ComboBox RackCombo(Control p, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.SetBounds(x, y, w, 24);
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.BackColor = C(7, 11, 20);
            c.ForeColor = Color.White;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = new Font("Consolas", 8.0f);
            p.Controls.Add(c);
            return c;
        }

        private static TrackBar RackSlider(Control p, int x, int y, int min, int max, int val)
        {
            TrackBar t = new TrackBar();
            t.AutoSize = false;
            t.SetBounds(x, y, 424, 23);
            t.Minimum = min;
            t.Maximum = max;
            t.TickStyle = TickStyle.None;
            t.Value = Math.Max(min, Math.Min(max, val));
            t.BackColor = C(13, 19, 34);
            p.Controls.Add(t);
            return t;
        }

        private static CheckBox RackCheck(Control p, string text, int x, int y, bool chk, Color fg)
        {
            CheckBox c = new CheckBox();
            c.UseMnemonic = false;
            c.SetBounds(x, y, 428, 20);
            c.Text = text;
            c.Checked = chk;
            c.ForeColor = fg;
            c.Font = new Font("Segoe UI Semibold", 7.7f);
            p.Controls.Add(c);
            return c;
        }

        private static Button AddRackBtn(Control p, string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.SetBounds(x, y, w, 25);
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 7.7f);
            p.Controls.Add(b);
            return b;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            Application.Run(new OmegaPenroseCockpitForm(baseDir));
        }
    }
}
