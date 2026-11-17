using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaAVStudio
{
 internal sealed class ViewportPanel : Panel
 {
 public Bitmap CpuBitmap;
 public bool UseHardwareSwapChain;
 public ViewportPanel()
 {
  SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
  BackColor = Color.Black;
 }
 protected override void OnPaintBackground(PaintEventArgs e) { }
 protected override void OnPaint(PaintEventArgs e)
 {
  if (!UseHardwareSwapChain && CpuBitmap != null) e.Graphics.DrawImage(CpuBitmap, ClientRectangle);
 }
 }

 public sealed partial class OmegaAvForm : Form
 {
 [StructLayout(LayoutKind.Sequential)]
 private struct NativeMsg { public IntPtr hWnd; public uint msg; public IntPtr wParam; public IntPtr lParam; public uint time; public Point pt; }

 [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMsg msg, IntPtr hWnd, uint min, uint max, uint remove);

 private readonly string _avRoot, _rendersDir, _shadersDir, _inboxCmdPath;
 private int _liveW = 1920, _liveH = 1080;
 private int[] _fb = new int[1920 * 1080];
 private Bitmap _bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppPArgb);

 private readonly CymaticAudioEngine _audio = new CymaticAudioEngine();
 private readonly WasapiLoopbackEngine _wasapi = new WasapiLoopbackEngine();
 private readonly DirectComputeHlslEngine _gpu = new DirectComputeHlslEngine();
 private readonly PixelOledLoopbackServer _oledServer = new PixelOledLoopbackServer();
 private readonly CpuFluidGrid _cpuFluid = new CpuFluidGrid();
 private readonly OmegaInputDeck _inputDeck;
 private ControllerLearnForm _learnForm;
 private readonly MathShaderPreset[] _customPresets = OmegaPresetCatalog.GetPresets();

 private readonly double[] _bands = new double[8];
 private readonly short[] _waveL = new short[256], _waveR = new short[256];
 private readonly double[][] _rotRoots = new double[240][];

 private int _shaderMode = 5, _latticeMode = 1, _rasterMode = 0, _edgeBudget = 8000, _postFxMode = 1, _fluidMode = 1;
 private uint _vsyncInterval = 1u;
 private bool _useGpuHlsl = true, _wasapiEnabled = true, _showVectors = true, _useCustomProj8D = true;
 private bool _runningLoop = false, _modalPaused = false, _fullscreenVj = false, _ideWasVis = true, _syncingSliders = false;
 private bool _draggingOrbit = false, _rightAttract = false, _gravityHover = false;
 private bool _oledStreamActive = false, _oledEncoding = false;
 private double _zoom = 1.0, _yaw = 0.35, _pitch = 0.22, _prevTickSec = 0.0;
 private double _k1 = 1.0, _k2 = 1.0, _k3 = 1.0, _k4 = 1.0;
 private Point _lastMouse, _mouseStagePt = new Point(640, 360);

 private volatile CompiledMathProgram _activePixelProg;
 private volatile CompiledMathProgram _activeProjProg;

 private readonly Stopwatch _simClock = Stopwatch.StartNew(), _frameLimiter = Stopwatch.StartNew();
 private int _fpsFrames = 0, _lastRootIdx = 0, _lastLeechIdx = 0;
 private double _fpsLastSec = 0.0, _fpsLive = 60.0, _lastSqnrDb = 0.0, _lastLeechSqnrDb = 0.0, _lastOledSec = -5.0;

 private ViewportPanel _stage;
 private Panel _topPanel, _botPanel, _idePanel;
 private Label _lblHud, _lblJitStatus, _lblKVal;
 private Button _btnAudio, _btnWasapi, _btnGravity, _btnRecLive, _btnMidiDeck;
 private ComboBox _cmbShader, _cmbCompute, _cmbLattice, _cmbRaster, _cmbEdges, _cmbFluid, _cmbPostFx, _cmbChord, _cmbPreset;
 private TextBox _txtPixelCode, _txtProjCode;
 private CheckBox _chkAutoJit, _chkUseProj;
 private TrackBar _slK1, _slK2, _slK3, _slK4;
 private System.Windows.Forms.Timer _jitDebounce;

 public OmegaAvForm(string avRoot, string initialOpenPath)
 {
  _avRoot = avRoot;
  _rendersDir = Path.Combine(avRoot, "renders");
  _shadersDir = Path.Combine(avRoot, "shaders");
  _inboxCmdPath = Path.Combine(avRoot, "inbox_open.cmd");
  Directory.CreateDirectory(_rendersDir); Directory.CreateDirectory(_shadersDir);
  for (int i = 0; i < 240; i++) _rotRoots[i] = new double[8];
  _inputDeck = new OmegaInputDeck(avRoot);

  Text = "ACT-Omega Lab :: OmegaAV v0.7.1 (GPU Ping-Pong Reaction-Diffusion Fluid + Leech Velocity + MIDI & Xbox/DualSense Deck)";
  Size = new Size(1660, 910);
  StartPosition = FormStartPosition.CenterScreen;
  BackColor = Color.FromArgb(7, 10, 18); ForeColor = Color.White; KeyPreview = true;
  try
  {
  string ico = Path.Combine(Directory.GetParent(avRoot).FullName, "icons", "omega_av.ico");
  if (File.Exists(ico)) Icon = new Icon(ico);
  }
  catch { }

  _jitDebounce = new System.Windows.Forms.Timer(); _jitDebounce.Interval = 240;
  _jitDebounce.Tick += delegate { _jitDebounce.Stop(); CompileBothEditors(); };

  BuildUi();
  CreateControl();
  _stage.CreateControl();

  if (!_gpu.Initialize(_stage.Handle, _liveW, _liveH))
  {
  _useGpuHlsl = false;
  _cmbCompute.SelectedIndex = 4;
  SetLiveResolution(576, 324);
  }
  _stage.UseHardwareSwapChain = _useGpuHlsl && _gpu.HasSwapChain;
  _cpuFluid.Reset();

  LoadCustomPreset(0);
  _audio.BuildPreset(0);
  _audio.PlayLoop();
  _wasapi.Start();
  _oledServer.Start(19779);

  KeyDown += OnHotKey;
  Application.Idle += OnAppIdle;
  FormClosing += delegate
  {
  _runningLoop = false;
  Application.Idle -= OnAppIdle;
  try { if (_learnForm != null) _learnForm.Dispose(); } catch { }
  _inputDeck.Dispose();
  _wasapi.Stop(); _audio.Stop(); _oledServer.Stop(); _gpu.Dispose();
  };
  Shown += delegate { _runningLoop = true; if (!string.IsNullOrEmpty(initialOpenPath)) TryApplyExternalOpen(initialOpenPath); };
 }

 private static bool IsAppIdle()
 {
  NativeMsg msg;
  return !PeekMessage(out msg, IntPtr.Zero, 0u, 0u, 0u);
 }

 private void OnAppIdle(object sender, EventArgs e)
 {
  while (_runningLoop && !_modalPaused && Visible && WindowState != FormWindowState.Minimized && IsAppIdle())
  {
  RenderLiveFrameTick();
  }
 }

 private void TriggerSplashBoth()
 {
  _gpu.TriggerFluidSplash();
  _cpuFluid.Splash();
 }

 private void ToggleControllerDeck()
 {
  if (_learnForm == null || _learnForm.IsDisposed) _learnForm = new ControllerLearnForm(_inputDeck, TriggerSplashBoth);
  if (_learnForm.Visible) _learnForm.Hide();
  else { _learnForm.Show(this); _stage.Focus(); }
 }

 private void SetLiveResolution(int w, int h)
 {
  _stage.UseHardwareSwapChain = _useGpuHlsl && _gpu.HasSwapChain;
  if (_liveW == w && _liveH == h && _bmp != null && _fb != null && _fb.Length == w * h) return;
  Bitmap old = _bmp;
  _liveW = w; _liveH = h;
  _fb = new int[w * h];
  _bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
  _stage.CpuBitmap = _bmp;
  if (old != null) { try { old.Dispose(); } catch { } }
 }

 private void BuildUi()
 {
  _topPanel = new Panel(); _topPanel.Dock = DockStyle.Top; _topPanel.Height = 40; _topPanel.BackColor = Color.FromArgb(12, 18, 32);
  FlowLayoutPanel bar = new FlowLayoutPanel(); bar.Dock = DockStyle.Fill; bar.WrapContents = false; bar.Padding = new Padding(4, 5, 4, 3);

  _cmbShader = MakeCombo(new string[] {
  "1: E8 Quasicrystal", "2: E8 8D Voronoi", "3: 4D Julia SDF",
  "4: S3 Clifford Torus", "5: Chladni Plate", "6: LIVE HLSL/x64 JIT"
  }, 126);
  _cmbShader.SelectedIndex = 5;
  _cmbShader.SelectedIndexChanged += delegate { _shaderMode = _cmbShader.SelectedIndex; };

  _cmbCompute = MakeCombo(new string[] {
  "GPU: 1080p VSync", "GPU: 720p VSync", "GPU: 1440p VSync", "GPU: 1080p Turbo", "CPU: x64 (324p)"
  }, 118);
  _cmbCompute.SelectedIndex = 0;
  _cmbCompute.SelectedIndexChanged += delegate
  {
  int idx = _cmbCompute.SelectedIndex;
  if (idx < 4 && _gpu.IsAvailable)
  {
   _useGpuHlsl = true;
   _vsyncInterval = (idx == 3) ? 0u : 1u;
   if (idx == 1) SetLiveResolution(1280, 720);
   else if (idx == 2) SetLiveResolution(2560, 1440);
   else SetLiveResolution(1920, 1080);
  }
  else { _useGpuHlsl = false; SetLiveResolution(576, 324); }
  };

  _cmbLattice = MakeCombo(new string[] { "8D E8 (240v)", "24D Leech (720v)", "Dual E8 + Leech", "Phantom Fluid" }, 112);
  _cmbLattice.SelectedIndex = 1;
  _cmbLattice.SelectedIndexChanged += delegate { _latticeMode = _cmbLattice.SelectedIndex; };

  _cmbRaster = MakeCombo(new string[] { "Wu Sub-Pixel AA", "Bresenham Int", "Split Wu | Bres" }, 110);
  _cmbRaster.SelectedIndexChanged += delegate { _rasterMode = _cmbRaster.SelectedIndex; };

  _cmbEdges = MakeCombo(new string[] { "Chords: All", "Chords: 3,600", "Chords: 1,600", "Chords: Off" }, 96);
  _cmbEdges.SelectedIndexChanged += delegate
  {
  int idx = _cmbEdges.SelectedIndex;
  _edgeBudget = (idx == 0) ? 8000 : (idx == 1 ? 3600 : (idx == 2 ? 1600 : 0));
  };

  _cmbFluid = MakeCombo(new string[] {
  "Fluid: Off",
  "Fluid: Coral Mitosis",
  "Fluid: Soliton Spots",
  "Fluid: Labyrinth Maze",
  "Fluid: Spiral Vortex",
  "Fluid: Cymatic Audio"
  }, 128);
  _cmbFluid.SelectedIndex = 1;
  _cmbFluid.SelectedIndexChanged += delegate { _fluidMode = _cmbFluid.SelectedIndex; };

  _cmbPostFx = MakeCombo(new string[] { "FX: Clean", "FX: ACES+Prism", "FX: Kaleido 6x", "FX: Prism 8x" }, 104);
  _cmbPostFx.SelectedIndex = 1;
  _cmbPostFx.SelectedIndexChanged += delegate { _postFxMode = _cmbPostFx.SelectedIndex; };

  _btnMidiDeck = MakeBtn("Ctrl / MIDI", Color.FromArgb(124, 58, 237));
  _btnMidiDeck.Click += delegate { ToggleControllerDeck(); };

  Button btnSplash = MakeBtn("Splash", Color.FromArgb(13, 148, 136));
  btnSplash.Click += delegate { TriggerSplashBoth(); };

  _btnGravity = MakeBtn("Grav", Color.FromArgb(67, 56, 202));
  _btnGravity.Click += delegate
  {
  _gravityHover = !_gravityHover;
  _btnGravity.Text = _gravityHover ? "Grav:ON" : "Grav";
  _btnGravity.BackColor = _gravityHover ? Color.FromArgb(16, 185, 129) : Color.FromArgb(67, 56, 202);
  };

  _btnWasapi = MakeBtn("WASAPI", Color.FromArgb(14, 116, 144));
  _btnWasapi.Click += delegate
  {
  _wasapiEnabled = !_wasapiEnabled;
  _btnWasapi.Text = _wasapiEnabled ? "WASAPI" : "WAS:OFF";
  _btnWasapi.BackColor = _wasapiEnabled ? Color.FromArgb(14, 116, 144) : Color.FromArgb(71, 85, 105);
  };

  _cmbChord = MakeCombo(new string[] { "55Hz", "108BPM", "7777D" }, 68);
  _cmbChord.SelectedIndexChanged += delegate { _audio.BuildPreset(_cmbChord.SelectedIndex); _btnAudio.Text = "Synth"; };

  _btnAudio = MakeBtn("Synth", Color.FromArgb(6, 95, 70));
  _btnAudio.Click += delegate
  {
  if (_audio.IsPlaying) { _audio.Stop(); _btnAudio.Text = "Mute"; _btnAudio.BackColor = Color.FromArgb(71, 85, 105); }
  else { _audio.PlayLoop(); _btnAudio.Text = "Synth"; _btnAudio.BackColor = Color.FromArgb(6, 95, 70); }
  };

  Button btnWav = MakeBtn(".WAV", Color.FromArgb(30, 58, 138));
  btnWav.MouseUp += delegate(object sw, MouseEventArgs mew)
  {
  string vaultAud = Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "downloads", "Audio")) ? Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "downloads", "Audio") : _avRoot;
  if (mew.Button == MouseButtons.Right && Directory.Exists(vaultAud))
  {
   string[] wavs = Directory.GetFiles(vaultAud, "*.wav");
   ContextMenuStrip wMenu = new ContextMenuStrip();
   if (wavs.Length == 0) wMenu.Items.Add("(No .wav files in Vault Audio yet)").Enabled = false;
   for (int wi = 0; wi < wavs.Length && wi < 16; wi++)
   {
    string wp = wavs[wi];
    wMenu.Items.Add("Vault: " + Path.GetFileName(wp), null, delegate { TryApplyExternalOpen(wp); });
   }
   wMenu.Show(btnWav, mew.Location);
   return;
  }
  _modalPaused = true;
  using (OpenFileDialog ofd = new OpenFileDialog())
  {
   if (Directory.Exists(vaultAud)) ofd.InitialDirectory = vaultAud;
   ofd.Filter = "WAV Audio (*.wav)|*.wav";
   if (ofd.ShowDialog(this) == DialogResult.OK && _audio.LoadExternalWav(ofd.FileName)) _btnAudio.Text = "WAV";
  }
  _modalPaused = false;
  };

  _btnRecLive = MakeBtn("Rec", Color.FromArgb(153, 27, 27));
  _btnRecLive.Click += delegate { ToggleLivePerformanceRecord(); };

  Button btn1080 = MakeBtn("4K", Color.FromArgb(180, 83, 9));
  btn1080.Click += delegate { OpenOffline1080pExportDialog(); };

  Button btnIde = MakeBtn("IDE", Color.FromArgb(5, 150, 105));
  btnIde.Click += delegate { _idePanel.Visible = !_idePanel.Visible; };

  Button btnVj = MakeBtn("F11", Color.FromArgb(88, 28, 135));
  btnVj.Click += delegate { ToggleFullscreenVj(); };

  Button btnPx = MakeBtn("Pixels", Color.FromArgb(109, 40, 217));
  btnPx.Click += delegate { _oledStreamActive = true; BeamToConnectedPixels(); };

  bar.Controls.AddRange(new Control[] {
  _cmbShader, _cmbCompute, _cmbLattice, _cmbRaster, _cmbEdges, _cmbFluid, _cmbPostFx,
  _btnMidiDeck, btnSplash, _btnGravity, _btnWasapi, _cmbChord, _btnAudio, btnWav, _btnRecLive, btn1080, btnIde, btnVj, btnPx
  });
  _topPanel.Controls.Add(bar);

  BuildShaderIdeSidebar();

  _botPanel = new Panel(); _botPanel.Dock = DockStyle.Bottom; _botPanel.Height = 25; _botPanel.BackColor = Color.FromArgb(8, 12, 22);
  _lblHud = new Label(); _lblHud.Dock = DockStyle.Fill; _lblHud.Font = new Font("Consolas", 8.4f);
  _lblHud.ForeColor = Color.FromArgb(56, 189, 248); _lblHud.TextAlign = ContentAlignment.MiddleLeft; _lblHud.Padding = new Padding(10, 0, 0, 0);
  _botPanel.Controls.Add(_lblHud);

  _stage = new ViewportPanel(); _stage.Dock = DockStyle.Fill; _stage.CpuBitmap = _bmp;
  _stage.MouseDown += delegate(object s, MouseEventArgs e)
  {
  _stage.Focus(); _mouseStagePt = e.Location;
  if (e.Button == MouseButtons.Right) _rightAttract = true;
  else if (e.Button == MouseButtons.Left) { _draggingOrbit = true; _lastMouse = e.Location; }
  };
  _stage.MouseUp += delegate(object s, MouseEventArgs e)
  {
  if (e.Button == MouseButtons.Right) _rightAttract = false;
  if (e.Button == MouseButtons.Left) _draggingOrbit = false;
  };
  _stage.MouseMove += delegate(object s, MouseEventArgs e)
  {
  _mouseStagePt = e.Location;
  if (_draggingOrbit) { _yaw += (e.X - _lastMouse.X) * 0.012; _pitch += (e.Y - _lastMouse.Y) * 0.012; _lastMouse = e.Location; }
  };
  MouseWheel += delegate(object s, MouseEventArgs e)
  {
  if (!_txtPixelCode.Focused && !_txtProjCode.Focused)
   _zoom = Math.Max(0.42, Math.Min(3.8, _zoom + (e.Delta > 0 ? 0.12 : -0.12)));
  };

  AllowDrop = true; _stage.AllowDrop = true;
  DragEventHandler onDragEnter = delegate(object sde, DragEventArgs dea)
  {
   dea.Effect = (dea.Data != null && dea.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
  };
  DragEventHandler onDragDrop = delegate(object sdd, DragEventArgs dda)
  {
   string[] fs = dda.Data.GetData(DataFormats.FileDrop) as string[];
   if (fs != null && fs.Length > 0) TryApplyExternalOpen(fs[0]);
  };
  DragEnter += onDragEnter; DragDrop += onDragDrop;
  _stage.DragEnter += onDragEnter; _stage.DragDrop += onDragDrop;
  Controls.Add(_stage); Controls.Add(_idePanel); Controls.Add(_topPanel); Controls.Add(_botPanel);
 }

 private void ToggleFullscreenVj()
 {
  _fullscreenVj = !_fullscreenVj;
  if (_fullscreenVj)
  {
  _ideWasVis = _idePanel.Visible;
  _topPanel.Visible = false; _botPanel.Visible = false; _idePanel.Visible = false;
  FormBorderStyle = FormBorderStyle.None;
  WindowState = FormWindowState.Maximized;
  }
  else
  {
  FormBorderStyle = FormBorderStyle.Sizable;
  WindowState = FormWindowState.Normal;
  _topPanel.Visible = true; _botPanel.Visible = true; _idePanel.Visible = _ideWasVis;
  }
 }

 private void ApplyControllerFrameActions(double dt)
 {
  _inputDeck.PollAndUpdate(dt);

  // Sync k1..k4 sliders smoothly when MIDI CC or bound gamepad axis moves them
  bool anyK = false;
  TrackBar[] sls = new TrackBar[] { _slK1, _slK2, _slK3, _slK4 };
  for (int i = 0; i < 4; i++)
  {
  if (_inputDeck.TargetTouched[i])
  {
   int sv = Math.Max(10, Math.Min(250, (int)Math.Round(10.0 + _inputDeck.MacroValues[i] * 240.0)));
   if (sls[i].Value != sv) { _syncingSliders = true; sls[i].Value = sv; _syncingSliders = false; anyK = true; }
  }
  }
  if (anyK) UpdateKnobLabel();

  // Gamepad Left-Stick smooth camera orbit (unless Left-Stick was rebound to a knob)
  if (_inputDeck.PadConnected)
  {
  _yaw += _inputDeck.PadLx * dt * 1.45;
  _pitch = Math.Max(-1.45, Math.Min(1.45, _pitch + _inputDeck.PadLy * dt * 1.25));
  }

  if ((DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref _oledServer.RemoteWandUtcTicks)) < TimeSpan.FromSeconds(1.2).Ticks)
  {
   _yaw += _oledServer.RemoteWandX * dt * 1.65;
   _pitch = Math.Max(-1.40, Math.Min(1.40, _oledServer.RemoteWandY * 1.15));
  }
  if (_inputDeck.PulseFluidSplash || _oledServer.RemoteSplashRequested) { _oledServer.RemoteSplashRequested = false; TriggerSplashBoth(); }
  if (_oledServer.RemoteCycleFluidRequested) { _oledServer.RemoteCycleFluidRequested = false; _cmbFluid.SelectedIndex = (_cmbFluid.SelectedIndex + 1) % _cmbFluid.Items.Count; }
  if (_inputDeck.PulseNextFluid) _cmbFluid.SelectedIndex = (_cmbFluid.SelectedIndex + 1) % _cmbFluid.Items.Count;
  if (_inputDeck.PulsePrevFluid) _cmbFluid.SelectedIndex = (_cmbFluid.SelectedIndex + _cmbFluid.Items.Count - 1) % _cmbFluid.Items.Count;
  if (_inputDeck.PulseNextFx) _cmbPostFx.SelectedIndex = (_cmbPostFx.SelectedIndex + 1) % _cmbPostFx.Items.Count;
  if (_inputDeck.PulseNextLattice) _cmbLattice.SelectedIndex = (_cmbLattice.SelectedIndex + 1) % _cmbLattice.Items.Count;
  if (_inputDeck.PulseNextRaster) _cmbRaster.SelectedIndex = (_cmbRaster.SelectedIndex + 1) % _cmbRaster.Items.Count;
  if (_inputDeck.PulseNextPreset) _cmbPreset.SelectedIndex = (_cmbPreset.SelectedIndex + 1) % _cmbPreset.Items.Count;
  if (_inputDeck.PulsePrevPreset) _cmbPreset.SelectedIndex = (_cmbPreset.SelectedIndex + _cmbPreset.Items.Count - 1) % _cmbPreset.Items.Count;
 }

 private void RenderLiveFrameTick()
 {
  if (_useGpuHlsl && _gpu.HasSwapChain && _vsyncInterval == 0u)
  {
  double elapsedMs = _frameLimiter.Elapsed.TotalMilliseconds;
  if (elapsedMs < 3.8) { Thread.Sleep(1); return; }
  }
  _frameLimiter.Restart();

  double t = _simClock.Elapsed.TotalSeconds;
  double dt = Math.Max(0.001, Math.Min(0.1, t - _prevTickSec));
  _prevTickSec = t;

  ApplyControllerFrameActions(dt);

  bool capRecFrame = _liveRecActive && (t >= _liveRecNextSec);
  bool capOledFrame = (_oledStreamActive || _lastOledSec < 0.0) && !_oledEncoding && (t - _lastOledSec >= 0.12);
  bool gpuSwap = _useGpuHlsl && _gpu.IsAvailable && _gpu.HasSwapChain;
  bool needCpuFb = !gpuSwap || capRecFrame || capOledFrame;

  RenderSceneToBuffer(_liveW, _liveH, needCpuFb ? _fb : null, t, false, true);

  if (!gpuSwap)
  {
  BitmapData bd = _bmp.LockBits(new Rectangle(0, 0, _liveW, _liveH), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
  try { Marshal.Copy(_fb, 0, bd.Scan0, _fb.Length); } finally { _bmp.UnlockBits(bd); }
  _stage.Invalidate();
  }

  if (capRecFrame && _recQueue != null)
  {
  _liveRecNextSec = t + (1.0 / 30.0);
  if (_recQueue.Count < 48)
  {
   int[] clone = new int[_fb.Length];
   Buffer.BlockCopy(_fb, 0, clone, 0, _fb.Length * 4);
   RecFrameItem it = new RecFrameItem();
   it.Index = _liveRecFrames++; it.W = _liveW; it.H = _liveH; it.Pixels = clone;
   _recQueue.Enqueue(it);
   if ((_liveRecFrames & 7) == 0) _btnRecLive.Text = "STOP(" + _liveRecFrames + "f)";
  }
  }

  if (capOledFrame)
  {
  _lastOledSec = t; _oledEncoding = true;
  int srcW = _liveW, srcH = _liveH;
  int[] snap = new int[_fb.Length];
  Buffer.BlockCopy(_fb, 0, snap, 0, _fb.Length * 4);
  ThreadPool.QueueUserWorkItem(delegate
  {
   try
   {
   using (Bitmap sbmp = new Bitmap(srcW, srcH, PixelFormat.Format32bppPArgb))
   using (Bitmap obmp = new Bitmap(576, 324, PixelFormat.Format24bppRgb))
   {
    BitmapData bd = sbmp.LockBits(new Rectangle(0, 0, srcW, srcH), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
    try { Marshal.Copy(snap, 0, bd.Scan0, snap.Length); } finally { sbmp.UnlockBits(bd); }
    using (Graphics g = Graphics.FromImage(obmp)) { g.DrawImage(sbmp, 0, 0, 576, 324); }
    using (MemoryStream ms = new MemoryStream()) { obmp.Save(ms, ImageFormat.Jpeg); _oledServer.LatestJpeg = ms.ToArray(); }
   }
   }
   catch { }
   finally { _oledEncoding = false; }
  });
  }

  _fpsFrames++;
  if (t - _fpsLastSec >= 0.45)
  {
  _fpsLive = Math.Round(_fpsFrames / (t - _fpsLastSec), 1);
  _fpsFrames = 0; _fpsLastSec = t;
  long ramMb = Process.GetCurrentProcess().WorkingSet64 / 1048576L;
  string backend = (_useGpuHlsl && _gpu.IsAvailable)
   ? ("GPU DXGI " + _liveW + "x" + _liveH + (_vsyncInterval > 0 ? " VS" : ""))
   : ("CPU x64 " + _liveW + "x" + _liveH);
  string audStr = (_wasapiEnabled && _wasapi.IsCapturing) ? ("WASAPI Pk:" + (_wasapi.PeakLevel * 100.0f).ToString("0") + "%") : (_audio.IsPlaying ? "Synth 48k" : "Clock");
  string[] flNames = new string[] { "Fluid:Off", "RD:Coral", "RD:Soliton", "RD:Maze", "RD:Spiral", "RD:Audio" };
  string flStr = flNames[Math.Max(0, Math.Min(5, _fluidMode))];
  string ctrlStr = _inputDeck.PadConnected ? _inputDeck.ActivePadName : _inputDeck.ActiveMidiSummary;
  _lblHud.Text = "FPS:" + _fpsLive.ToString("0.0") + " | " + backend + " | " + flStr + " | " + ctrlStr + " | " + audStr + " | Leech:" + _lastLeechSqnrDb.ToString("0.0") + "dB | RAM:" + ramMb + "MB";
  PollBrowserInboxDrop();
  }
 }

 private void UpdateKnobLabel()
 {
  _k1 = _slK1.Value / 100.0; _k2 = _slK2.Value / 100.0; _k3 = _slK3.Value / 100.0; _k4 = _slK4.Value / 100.0;
  _lblKVal.Text = "Knobs -> k1=" + _k1.ToString("0.00") + " k2=" + _k2.ToString("0.00") + " k3=" + _k3.ToString("0.00") + " k4=" + _k4.ToString("0.00") +
  " | m3=" + _inputDeck.MacroValues[6].ToString("0.00");
 }

 private TrackBar MakeSlider(int x, int y)
 {
  TrackBar tb = new TrackBar(); tb.SetBounds(x, y, 108, 26); tb.AutoSize = false;
  tb.Minimum = 10; tb.Maximum = 250; tb.Value = 100; tb.TickStyle = TickStyle.None;
  tb.ValueChanged += delegate
  {
  if (!_syncingSliders)
  {
   _inputDeck.MacroValues[0] = (_slK1.Value - 10) / 240.0;
   _inputDeck.MacroValues[1] = (_slK2.Value - 10) / 240.0;
   _inputDeck.MacroValues[2] = (_slK3.Value - 10) / 240.0;
   _inputDeck.MacroValues[3] = (_slK4.Value - 10) / 240.0;
  }
  UpdateKnobLabel();
  };
  return tb;
 }

 private TextBox MakeCodeBox(int x, int y, int w, int h)
 {
  TextBox tb = new TextBox(); tb.SetBounds(x, y, w, h); tb.Multiline = true; tb.AcceptsTab = true;
  tb.ScrollBars = ScrollBars.Vertical; tb.WordWrap = false;
  tb.BackColor = Color.FromArgb(6, 9, 17); tb.ForeColor = Color.FromArgb(226, 232, 240);
  tb.Font = new Font("Consolas", 9.0f); tb.BorderStyle = BorderStyle.FixedSingle;
  tb.TextChanged += delegate { if (_chkAutoJit != null && _chkAutoJit.Checked) { _jitDebounce.Stop(); _jitDebounce.Start(); } };
  tb.KeyDown += delegate(object s, KeyEventArgs e)
  {
  if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _cmbShader.SelectedIndex = 5; CompileBothEditors(); }
  };
  return tb;
 }

 private static ComboBox MakeCombo(string[] items, int w)
 {
  ComboBox c = new ComboBox(); c.Width = w; c.DropDownStyle = ComboBoxStyle.DropDownList;
  c.BackColor = Color.FromArgb(15, 23, 42); c.ForeColor = Color.White; c.Font = new Font("Segoe UI Semibold", 7.8f);
  c.Items.AddRange(items); c.SelectedIndex = 0; c.Margin = new Padding(0, 1, 2, 0);
  return c;
 }

 private static Button MakeBtn(string txt, Color bg)
 {
  Button b = new Button(); b.Text = txt; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
  b.MinimumSize = new Size(22, 26); b.Padding = new Padding(2, 1, 2, 1); b.Margin = new Padding(0, 0, 2, 0);
  b.FlatStyle = FlatStyle.Flat; b.BackColor = bg; b.ForeColor = Color.White; b.Font = new Font("Segoe UI Semibold", 7.7f);
  b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
  return b;
 }

 private void OnHotKey(object sender, KeyEventArgs e)
 {
  if (e.KeyCode == Keys.F11) { e.SuppressKeyPress = true; ToggleFullscreenVj(); return; }
  if (e.KeyCode == Keys.Escape && _fullscreenVj) { e.SuppressKeyPress = true; ToggleFullscreenVj(); return; }
  if (e.Control && e.Shift && e.KeyCode == Keys.E) { e.SuppressKeyPress = true; OpenOffline1080pExportDialog(); return; }
  if (e.Control && e.KeyCode == Keys.R) { e.SuppressKeyPress = true; ToggleLivePerformanceRecord(); return; }
  if (e.Control && e.KeyCode == Keys.E) { e.SuppressKeyPress = true; if (!_fullscreenVj) _idePanel.Visible = !_idePanel.Visible; return; }
  if (_txtPixelCode.Focused || _txtProjCode.Focused) return;
  if (e.Shift && e.KeyCode == Keys.F) { _gpu.ResetFluidField(); _cpuFluid.Reset(); return; }
  if (e.KeyCode == Keys.D1) _cmbShader.SelectedIndex = 0;
  else if (e.KeyCode == Keys.D2) _cmbShader.SelectedIndex = 1;
  else if (e.KeyCode == Keys.D3) _cmbShader.SelectedIndex = 2;
  else if (e.KeyCode == Keys.D4) _cmbShader.SelectedIndex = 3;
  else if (e.KeyCode == Keys.D5) _cmbShader.SelectedIndex = 4;
  else if (e.KeyCode == Keys.D6) _cmbShader.SelectedIndex = 5;
  else if (e.KeyCode == Keys.F) _cmbFluid.SelectedIndex = (_cmbFluid.SelectedIndex + 1) % _cmbFluid.Items.Count;
  else if (e.KeyCode == Keys.S) TriggerSplashBoth();
  else if (e.KeyCode == Keys.K) ToggleControllerDeck();
  else if (e.KeyCode == Keys.G) _cmbCompute.SelectedIndex = (_cmbCompute.SelectedIndex + 1) % _cmbCompute.Items.Count;
  else if (e.KeyCode == Keys.B) _cmbPostFx.SelectedIndex = (_cmbPostFx.SelectedIndex + 1) % _cmbPostFx.Items.Count;
  else if (e.KeyCode == Keys.M) _btnGravity.PerformClick();
  else if (e.KeyCode == Keys.W) _btnWasapi.PerformClick();
  else if (e.KeyCode == Keys.L) _cmbLattice.SelectedIndex = (_cmbLattice.SelectedIndex + 1) % _cmbLattice.Items.Count;
  else if (e.KeyCode == Keys.R) _cmbRaster.SelectedIndex = (_cmbRaster.SelectedIndex + 1) % _cmbRaster.Items.Count;
  else if (e.KeyCode == Keys.E) _cmbEdges.SelectedIndex = (_cmbEdges.SelectedIndex + 1) % _cmbEdges.Items.Count;
  else if (e.KeyCode == Keys.V) _showVectors = !_showVectors;
  else if (e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; _btnAudio.PerformClick(); }
 }

 private void PollBrowserInboxDrop()
 {
  try
  {
   if (!File.Exists(_inboxCmdPath)) return;
   string target = File.ReadAllText(_inboxCmdPath, Encoding.UTF8).Trim();
   try { File.Delete(_inboxCmdPath); } catch { }
   if (!string.IsNullOrEmpty(target)) TryApplyExternalOpen(target);
  }
  catch { }
 }

 private void TryApplyExternalOpen(string filePath)
 {
  try
  {
   if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
   string ext = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();
   if (ext == ".wav")
   {
    if (_audio.LoadExternalWav(filePath))
    {
     _btnAudio.Text = "WAV";
     _btnAudio.BackColor = Color.FromArgb(2, 132, 199);
     _lblJitStatus.ForeColor = Color.FromArgb(56, 189, 248);
     _lblJitStatus.Text = "OmegaBrowser Hot-Loaded Audio -> " + Path.GetFileName(filePath);
    }
   }
   else if (ext == ".omsh" || ext == ".hlsl" || ext == ".glsl" || ext == ".txt")
   {
    string[] parts = File.ReadAllText(filePath, Encoding.UTF8).Split(new string[] { "===OMEGA_PROJ8D===" }, StringSplitOptions.None);
    _chkAutoJit.Checked = false;
    _txtPixelCode.Text = parts[0].Trim().Replace("\r\n", "\n").Replace("\n", "\r\n");
    if (parts.Length > 1) _txtProjCode.Text = parts[1].Trim().Replace("\r\n", "\n").Replace("\n", "\r\n");
    _chkAutoJit.Checked = true;
    _cmbShader.SelectedIndex = 5;
    CompileBothEditors();
    _lblJitStatus.Text = "OmegaBrowser Hot-Loaded Shader -> " + Path.GetFileName(filePath);
   }
  }
  catch { }
 }

 [STAThread]
 public static void Main(string[] args)
 {
  try { SetProcessDPIAware(); } catch { }
  Application.EnableVisualStyles();
  Application.SetCompatibleTextRenderingDefault(false);
  string openArg = "";
  if (args != null && args.Length > 0)
  {
   openArg = (args[0] == "--open" && args.Length > 1) ? args[1] : args[0];
  }
  Application.Run(new OmegaAvForm(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), openArg));
 }
 }
}
