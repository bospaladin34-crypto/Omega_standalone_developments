using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OmegaCommon;

namespace OmegaTriEngine
{
    // =========================================================================
    // Phase 1 (OmegaEngine v1.9.0): Real-Time 28px Top-Screen Diagnostics &
    // Non-Activating Interactive Command Bar + 250ms Skyrim Menu Auto-Fade
    // =========================================================================
    public sealed class OmegaTopBarController : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT_W32
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO_W32
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT_W32 ptScreenPos;
        }

        private const int CURSOR_SHOWING = 0x00000001;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO_W32 pci);

        private readonly OmegaEngineArenaState _arenaState;
        private readonly OmegaTriEngineUniformBus _bus;
        private OmegaTopDiagBarForm _barForm;
        private Timer _pollTimer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastTickSec = 0.0;

        public bool InShaderHudEnabled = true;
        public bool ShaderMasterEnabled = true;
        public bool AutoMenuFadeEnabled = true;
        public bool DesktopBarClickThrough = false;
        public int SymmetryOverride = -1;
        public float AuroraMultiplier = 1.0f;
        public float CausticMultiplier = 1.0f;
        public int AestheticPersona = 0;
        public float PersonaContrast = 1.0f;
        public float PersonaVignette = 0.18f;
        public float HdrBloomMultiplier = 1.0f;
        public float WaterElevationMeters = -0.14f;
        public float WaterRippleGain = 1.0f;
        public int ActiveSpellSchool = 0;
        public float SpellPulseIntensity = 0.0f;
        public float CurrentMenuFadeAlpha = 1.0f;
        public bool SimulatedMenuOpenOverride = false;
        public bool HasSimulatedMenuOverride = false;
        public bool LastDetectedMenuOpen = false;

        public static readonly string[] PersonaNames = new string[]
        {
            "Classic Sovngarde",
            "Obsidian Dark Fantasy",
            "Liminal Dreamcore",
            "Stark Nihilist Monolith"
        };

        public static readonly string[] PersonaShortTags = new string[]
        {
            "SOVN", "DARK", "DRMC", "NIHL"
        };

        public static readonly string[] SpellShortTags = new string[]
        {
            "NONE", "FIRE", "FRST", "SHCK", "SUN", "VOID"
        };

        private bool _prevF10, _prevAltF1, _prevAltF2, _prevAltF3, _prevAltF5, _prevAltF6, _prevAltF7, _prevAltF8, _prevAltF9;
        private OmegaTriEngineSharedBus.TopBarLiveSnapshot _lastSnap;

        public Action OnStateMutated;
        public Action OnFireThuumRequested;

        public OmegaTriEngineSharedBus.TopBarLiveSnapshot LastSnapshot { get { return _lastSnap; } }
        public bool IsDesktopBarVisible { get { return _barForm != null && !_barForm.IsDisposed && _barForm.Visible; } }
        public OmegaTopDiagBarForm BarForm { get { return _barForm; } }

        public OmegaTopBarController(OmegaEngineArenaState arenaState, OmegaTriEngineUniformBus bus)
        {
            _arenaState = arenaState;
            _bus = bus;
            OmegaTriEngineSharedBus.TopBarLiveSnapshot initSnap;
            if (_bus != null && _bus.ReadTopBarDiagnostics(out initSnap) && initSnap.V2Active)
            {
                InShaderHudEnabled = initSnap.InShaderHudEnabled;
                ShaderMasterEnabled = initSnap.ShaderMasterEnabled;
                AutoMenuFadeEnabled = initSnap.AutoMenuFadeEnabled;
                DesktopBarClickThrough = initSnap.DesktopClickThrough;
                SymmetryOverride = initSnap.SymmetryOverride;
                AuroraMultiplier = (initSnap.AuroraMultiplier > 0.05f) ? initSnap.AuroraMultiplier : 1.0f;
                CausticMultiplier = (initSnap.CausticMultiplier > 0.05f) ? initSnap.CausticMultiplier : 1.0f;
                AestheticPersona = initSnap.AestheticPersona;
                PersonaContrast = (initSnap.PersonaContrast > 0.1f) ? initSnap.PersonaContrast : 1.0f;
                PersonaVignette = initSnap.PersonaVignette;
                HdrBloomMultiplier = (initSnap.HdrBloomMultiplier > 0.05f && initSnap.HdrBloomMultiplier <= 3.5f) ? initSnap.HdrBloomMultiplier : 1.0f;
                WaterElevationMeters = initSnap.WaterElevationMeters;
                WaterRippleGain = (initSnap.WaterRippleGain > 0.05f) ? initSnap.WaterRippleGain : 1.0f;
                ActiveSpellSchool = initSnap.SpellSchoolId;
                SpellPulseIntensity = initSnap.SpellIntensity;
                CurrentMenuFadeAlpha = (initSnap.MenuFadeAlpha > 0.05f) ? initSnap.MenuFadeAlpha : 1.0f;
                _lastSnap = initSnap;
            }
            else
            {
                PushControlsToBus(false);
            }
        }

        public void StartTimer60Hz()
        {
            if (_pollTimer != null) return;
            _lastTickSec = _clock.Elapsed.TotalSeconds;
            _pollTimer = new Timer();
            _pollTimer.Interval = 16;
            _pollTimer.Tick += delegate { StepTick(null); };
            _pollTimer.Start();
        }

        public void ShowDesktopBar(bool show)
        {
            if (show)
            {
                if (_barForm == null || _barForm.IsDisposed)
                {
                    _barForm = new OmegaTopDiagBarForm(this);
                }
                if (!_barForm.Visible)
                {
                    InShaderHudEnabled = false;
                    PushControlsToBus(false);
                    _barForm.Show();
                    _barForm.ApplyClickThroughStyle(DesktopBarClickThrough);
                }
            }
            else
            {
                if (_barForm != null && !_barForm.IsDisposed && _barForm.Visible)
                {
                    _barForm.Hide();
                    InShaderHudEnabled = true;
                    PushControlsToBus(false);
                }
            }
        }

        public void ToggleDesktopBar()
        {
            ShowDesktopBar(!IsDesktopBarVisible);
        }

        public void ToggleClickThrough()
        {
            DesktopBarClickThrough = !DesktopBarClickThrough;
            if (_barForm != null && !_barForm.IsDisposed)
            {
                _barForm.ApplyClickThroughStyle(DesktopBarClickThrough);
                _barForm.Invalidate();
            }
            PushControlsToBus(false);
        }

        public void ToggleInShaderHud()
        {
            InShaderHudEnabled = !InShaderHudEnabled;
            PushControlsToBus(false);
        }

        public void ToggleShaderMaster()
        {
            ShaderMasterEnabled = !ShaderMasterEnabled;
            PushControlsToBus(false);
        }

        public void ToggleAutoMenuFade()
        {
            AutoMenuFadeEnabled = !AutoMenuFadeEnabled;
            if (!AutoMenuFadeEnabled)
            {
                CurrentMenuFadeAlpha = 1.0f;
                if (_bus != null) _bus.UpdateMenuFadeTelemetry(1.0f, false);
            }
            PushControlsToBus(false);
        }

        public void CycleSymmetry()
        {
            int cur = _lastSnap.ActiveSymmetry;
            if (_arenaState != null && SymmetryOverride < 0)
            {
                lock (_arenaState.Sync) { cur = _arenaState.SymmetryMode; }
            }
            else if (SymmetryOverride >= 0)
            {
                cur = SymmetryOverride;
            }
            int next = (cur + 1) & 7;
            SetSymmetryExplicit(next);
        }

        public void SetSymmetryExplicit(int sym0To7)
        {
            int clamped = Math.Max(0, Math.Min(7, sym0To7));
            SymmetryOverride = clamped;
            if (_arenaState != null)
            {
                lock (_arenaState.Sync) { _arenaState.SymmetryMode = clamped; }
            }
            PushControlsToBus(false);
            if (OnStateMutated != null) OnStateMutated();
        }

        public void CycleAuroraGain()
        {
            if (AuroraMultiplier < 0.8f) AuroraMultiplier = 1.0f;
            else if (AuroraMultiplier < 1.2f) AuroraMultiplier = 1.4f;
            else AuroraMultiplier = 0.55f;
            PushControlsToBus(false);
        }

        public void CycleCausticGain()
        {
            if (CausticMultiplier < 0.8f) CausticMultiplier = 1.0f;
            else if (CausticMultiplier < 1.2f) CausticMultiplier = 1.4f;
            else CausticMultiplier = 0.55f;
            PushControlsToBus(false);
        }

        public void SetPersonaExplicit(int persona0To3)
        {
            AestheticPersona = Math.Max(0, Math.Min(3, persona0To3));
            if (AestheticPersona == 0) { PersonaContrast = 1.00f; PersonaVignette = 0.14f; }
            else if (AestheticPersona == 1) { PersonaContrast = 1.18f; PersonaVignette = 0.30f; }
            else if (AestheticPersona == 2) { PersonaContrast = 0.94f; PersonaVignette = 0.22f; }
            else { PersonaContrast = 1.24f; PersonaVignette = 0.28f; }
            PushControlsToBus(false);
            if (OnStateMutated != null) OnStateMutated();
        }

        public void CyclePersona()
        {
            SetPersonaExplicit((AestheticPersona + 1) & 3);
        }

        public void CycleHdrBloomGain()
        {
            if (HdrBloomMultiplier < 0.8f) HdrBloomMultiplier = 1.0f;
            else if (HdrBloomMultiplier < 1.25f) HdrBloomMultiplier = 1.45f;
            else HdrBloomMultiplier = 0.55f;
            PushControlsToBus(false);
        }

        public void TriggerSpellPulseExplicit(int school0To5, float strength)
        {
            ActiveSpellSchool = Math.Max(0, Math.Min(5, school0To5));
            SpellPulseIntensity = (ActiveSpellSchool == 0) ? 0.0f : Math.Max(0.15f, Math.Min(2.5f, strength));
            PushControlsToBus(false);
        }

        public void CycleAndPulseSpellSchool()
        {
            int next = (ActiveSpellSchool + 1) % 6;
            TriggerSpellPulseExplicit(next, (next == 0) ? 0.0f : 1.15f);
        }

        public void TriggerThuumFromTopBar()
        {
            PushControlsToBus(true);
            if (_arenaState != null)
            {
                _arenaState.TriggerShockwave(1.38f, 10.5f);
            }
            if (OnFireThuumRequested != null)
            {
                OnFireThuumRequested();
            }
        }

        public void PushControlsToBus(bool fireThuum)
        {
            if (_bus != null)
            {
                _bus.WriteTopBarControls(
                    InShaderHudEnabled,
                    ShaderMasterEnabled,
                    AutoMenuFadeEnabled,
                    DesktopBarClickThrough,
                    SymmetryOverride,
                    AuroraMultiplier,
                    CausticMultiplier,
                    fireThuum);
                _bus.WritePhase2PersonaAndWorldCoupling(
                    AestheticPersona,
                    PersonaContrast,
                    PersonaVignette,
                    HdrBloomMultiplier,
                    WaterElevationMeters,
                    WaterRippleGain,
                    ActiveSpellSchool,
                    SpellPulseIntensity);
            }
        }

        private static bool DetectSkyrimForegroundAndMenuCursor()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                StringBuilder sb = new StringBuilder(128);
                int len = GetWindowTextW(fg, sb, 128);
                if (len <= 0) return false;
                string title = sb.ToString();
                if (title.IndexOf("Skyrim", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
                CURSORINFO_W32 ci = new CURSORINFO_W32();
                ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO_W32));
                if (GetCursorInfo(ref ci))
                {
                    if ((ci.flags & CURSOR_SHOWING) != 0)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public void StepMenuFadeRamp(float dtSec, bool menuOpen)
        {
            LastDetectedMenuOpen = menuOpen;
            float target = (!AutoMenuFadeEnabled || !menuOpen) ? 1.0f : 0.10f;
            float alpha = 1.0f - (float)Math.Exp(-Math.Max(0.001, dtSec) / 0.088);
            CurrentMenuFadeAlpha += (target - CurrentMenuFadeAlpha) * alpha;
            if (Math.Abs(target - CurrentMenuFadeAlpha) < 0.004f)
            {
                CurrentMenuFadeAlpha = target;
            }
            if (_bus != null)
            {
                _bus.UpdateMenuFadeTelemetry(CurrentMenuFadeAlpha, menuOpen);
            }
        }

        private void PollGlobalHotkeys()
        {
            bool f10 = (GetAsyncKeyState((int)Keys.F10) & 0x8000) != 0;
            if (f10 && !_prevF10) ToggleDesktopBar();
            _prevF10 = f10;

            bool alt = (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;
            bool af1 = alt && ((GetAsyncKeyState((int)Keys.F1) & 0x8000) != 0);
            bool af2 = alt && ((GetAsyncKeyState((int)Keys.F2) & 0x8000) != 0);
            bool af3 = alt && ((GetAsyncKeyState((int)Keys.F3) & 0x8000) != 0);
            bool af5 = alt && ((GetAsyncKeyState((int)Keys.F5) & 0x8000) != 0);
            bool af6 = alt && ((GetAsyncKeyState((int)Keys.F6) & 0x8000) != 0);
            bool af7 = alt && ((GetAsyncKeyState((int)Keys.F7) & 0x8000) != 0);
            bool af8 = alt && ((GetAsyncKeyState((int)Keys.F8) & 0x8000) != 0);
            bool af9 = alt && ((GetAsyncKeyState((int)Keys.F9) & 0x8000) != 0);

            if (af1 && !_prevAltF1) ToggleClickThrough();
            if (af2 && !_prevAltF2) ToggleInShaderHud();
            if (af3 && !_prevAltF3) CycleSymmetry();
            if (af5 && !_prevAltF5) ToggleShaderMaster();
            if (af6 && !_prevAltF6) ToggleAutoMenuFade();
            if (af7 && !_prevAltF7) TriggerThuumFromTopBar();
            if (af8 && !_prevAltF8) CyclePersona();
            if (af9 && !_prevAltF9) CycleAndPulseSpellSchool();

            _prevAltF1 = af1;
            _prevAltF2 = af2;
            _prevAltF3 = af3;
            _prevAltF5 = af5;
            _prevAltF6 = af6;
            _prevAltF7 = af7;
            _prevAltF8 = af8;
            _prevAltF9 = af9;
        }

        public void StepTick(float? explicitDtSec)
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = explicitDtSec.HasValue
                ? explicitDtSec.Value
                : (float)Math.Max(0.001, Math.Min(0.10, now - _lastTickSec));
            _lastTickSec = now;

            if (!explicitDtSec.HasValue)
            {
                PollGlobalHotkeys();
            }

            bool menuOpen = HasSimulatedMenuOverride
                ? SimulatedMenuOpenOverride
                : DetectSkyrimForegroundAndMenuCursor();
            StepMenuFadeRamp(dt, menuOpen);
            if (SpellPulseIntensity > 0.005f && !explicitDtSec.HasValue)
            {
                SpellPulseIntensity *= (float)Math.Exp(-Math.Max(0.001, dt) * 0.35);
                if (SpellPulseIntensity < 0.01f) SpellPulseIntensity = 0.0f;
                if (_bus != null)
                {
                    _bus.WritePhase2PersonaAndWorldCoupling(
                        AestheticPersona, PersonaContrast, PersonaVignette, HdrBloomMultiplier,
                        WaterElevationMeters, WaterRippleGain, ActiveSpellSchool, SpellPulseIntensity);
                }
            }

            if (_bus != null)
            {
                _bus.ReadTopBarDiagnostics(out _lastSnap);
            }
            if (_barForm != null && !_barForm.IsDisposed && _barForm.Visible)
            {
                _barForm.Invalidate();
            }
        }

        public string BuildTopBarJson()
        {
            if (_bus != null)
            {
                _bus.ReadTopBarDiagnostics(out _lastSnap);
            }
            StringBuilder sb = new StringBuilder(640);
            sb.Append("{\"ok\":true,\"version\":\"1.8.0\",\"oteb_bytes\":512,");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"desktop_bar_visible\":{0},", IsDesktopBarVisible ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"desktop_click_through\":{0},", DesktopBarClickThrough ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"in_shader_hud\":{0},", InShaderHudEnabled ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"shader_master_on\":{0},", ShaderMasterEnabled ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"auto_menu_fade\":{0},", AutoMenuFadeEnabled ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"menu_detected\":{0},", LastDetectedMenuOpen ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"menu_fade_alpha\":{0:0.000},", CurrentMenuFadeAlpha);
            int perIdx = Math.Max(0, Math.Min(3, AestheticPersona));
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"active_symmetry\":{0},\"symmetry_override\":{1},", _lastSnap.ActiveSymmetry, SymmetryOverride);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"aesthetic_persona\":{0},\"persona_name\":\"{1}\",\"hdr_bloom_mult\":{2:0.00},", perIdx, PersonaNames[perIdx], HdrBloomMultiplier);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"water_elevation_m\":{0:0.00},\"spell_school\":{1},\"spell_intensity\":{2:0.00},", WaterElevationMeters, ActiveSpellSchool, SpellPulseIntensity);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"aurora_mult\":{0:0.00},\"caustic_mult\":{1:0.00},", AuroraMultiplier, CausticMultiplier);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"skyrim_fps\":{0:0.0},\"frame_ms\":{1:0.00},", _lastSnap.SkyrimFps, _lastSnap.FrameTimeMs);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"skse64_live\":{0},\"reshade_live\":{1},\"reshade_frames\":{2},\"seqlock_seq\":{3}",
                _lastSnap.Skse64Live ? "true" : "false",
                _lastSnap.ReshadeLive ? "true" : "false",
                _lastSnap.AddonFrames,
                _lastSnap.Sequence);
            sb.Append("}");
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _pollTimer = null;
            }
            if (_barForm != null && !_barForm.IsDisposed)
            {
                try { _barForm.Close(); _barForm.Dispose(); } catch { }
                _barForm = null;
            }
        }
    }

    public sealed class OmegaTopDiagBarForm : Form
    {
        private const int WS_EX_TOPMOST     = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW  = 0x00000080;
        private const int WS_EX_LAYERED     = 0x00080000;
        private const int WS_EX_NOACTIVATE  = 0x08000000;

        private const int GWL_EXSTYLE       = -20;
        private const uint LWA_ALPHA        = 0x00000002u;
        private const int WM_MOUSEACTIVATE  = 0x0021;
        private const int MA_NOACTIVATE     = 3;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        private struct BarButtonSlot
        {
            public Rectangle Bounds;
            public string Label;
            public Color Accent;
            public Action OnClick;
        }

        private static readonly string[] SymShortNames = new string[]
        {
            "0:P3", "1:E8", "2:HAT", "3:SPC", "4:HPF", "5:H4", "6:HYP", "7:L24"
        };

        private readonly OmegaTopBarController _ctrl;
        private readonly BarButtonSlot[] _buttons = new BarButtonSlot[11];
        private int _hoverIdx = -1;
        private readonly Font _fntBold = new Font("Consolas", 8.5f, FontStyle.Bold);
        private readonly Font _fntSmall = new Font("Consolas", 8.0f, FontStyle.Regular);

        public int CurrentExStyle
        {
            get { return IsHandleCreated ? GetWindowLong(Handle, GWL_EXSTYLE) : CreateParams.ExStyle; }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
                if (_ctrl != null && _ctrl.DesktopBarClickThrough)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }

        public OmegaTopDiagBarForm(OmegaTopBarController ctrl)
        {
            _ctrl = ctrl;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Rectangle scr = (Screen.PrimaryScreen != null) ? Screen.PrimaryScreen.Bounds : new Rectangle(0, 0, 1920, 1080);
            Bounds = new Rectangle(scr.X, scr.Y, Math.Max(1024, scr.Width), 28);
            BackColor = Color.FromArgb(14, 16, 19);
            ForeColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetLayeredWindowAttributes(Handle, 0u, 236, LWA_ALPHA); } catch { }
            ApplyClickThroughStyle(_ctrl != null && _ctrl.DesktopBarClickThrough);
        }

        public void ApplyClickThroughStyle(bool clickThrough)
        {
            if (!IsHandleCreated) return;
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            ex |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            if (clickThrough) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(Handle, GWL_EXSTYLE, ex);
            try { SetLayeredWindowAttributes(Handle, 0u, clickThrough ? (byte)214 : (byte)240, LWA_ALPHA); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        private static Color GetPersonaAccent(int per)
        {
            if (per == 1) return Color.FromArgb(225, 29, 72);   // Obsidian Dark Fantasy
            if (per == 2) return Color.FromArgb(192, 132, 252); // Liminal Dreamcore
            if (per == 3) return Color.FromArgb(203, 213, 225); // Stark Nihilist
            return Color.FromArgb(245, 184, 81);                // Classic Sovngarde
        }

        private void RebuildButtons()
        {
            OmegaTriEngineSharedBus.TopBarLiveSnapshot s = _ctrl.LastSnapshot;
            int x = Width - 832;
            if (x < 480) x = 480;

            int sym = Math.Max(0, Math.Min(7, s.ActiveSymmetry));
            int per = Math.Max(0, Math.Min(3, _ctrl.AestheticPersona));
            int spl = Math.Max(0, Math.Min(5, _ctrl.ActiveSpellSchool));

            _buttons[0] = MakeSlot(ref x, 64, _ctrl.ShaderMasterEnabled ? "FX:ON" : "FX:BYP",
                _ctrl.ShaderMasterEnabled ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68),
                delegate { _ctrl.ToggleShaderMaster(); });

            _buttons[1] = MakeSlot(ref x, 72, "SYM " + SymShortNames[sym],
                Color.FromArgb(245, 184, 81),
                delegate { _ctrl.CycleSymmetry(); });

            _buttons[2] = MakeSlot(ref x, 70, _ctrl.InShaderHudEnabled ? "DX11:ON" : "DX11:OFF",
                _ctrl.InShaderHudEnabled ? Color.FromArgb(56, 189, 248) : Color.FromArgb(71, 85, 105),
                delegate { _ctrl.ToggleInShaderHud(); });

            _buttons[3] = MakeSlot(ref x, 72, _ctrl.AutoMenuFadeEnabled ? "FADE:250" : "FADE:OFF",
                _ctrl.AutoMenuFadeEnabled ? Color.FromArgb(245, 158, 11) : Color.FromArgb(71, 85, 105),
                delegate { _ctrl.ToggleAutoMenuFade(); });

            _buttons[4] = MakeSlot(ref x, 66, string.Format(CultureInfo.InvariantCulture, "AUR:{0:0.0}x", _ctrl.AuroraMultiplier),
                Color.FromArgb(45, 212, 191),
                delegate { _ctrl.CycleAuroraGain(); });

            _buttons[5] = MakeSlot(ref x, 66, string.Format(CultureInfo.InvariantCulture, "CAU:{0:0.0}x", _ctrl.CausticMultiplier),
                Color.FromArgb(96, 165, 250),
                delegate { _ctrl.CycleCausticGain(); });

            _buttons[6] = MakeSlot(ref x, 68, "THU'UM!",
                Color.FromArgb(168, 85, 247),
                delegate { _ctrl.TriggerThuumFromTopBar(); });

            _buttons[7] = MakeSlot(ref x, 76, "PER:" + OmegaTopBarController.PersonaShortTags[per],
                GetPersonaAccent(per),
                delegate { _ctrl.CyclePersona(); });

            _buttons[8] = MakeSlot(ref x, 68, string.Format(CultureInfo.InvariantCulture, "HDR:{0:0.0}x", _ctrl.HdrBloomMultiplier),
                Color.FromArgb(251, 146, 60),
                delegate { _ctrl.CycleHdrBloomGain(); });

            _buttons[9] = MakeSlot(ref x, 70, "SPL:" + OmegaTopBarController.SpellShortTags[spl],
                (spl > 0) ? Color.FromArgb(236, 72, 153) : Color.FromArgb(100, 116, 139),
                delegate { _ctrl.CycleAndPulseSpellSchool(); });

            _buttons[10] = MakeSlot(ref x, 86, _ctrl.DesktopBarClickThrough ? "THRU:ALT+F1" : "F10 / THRU",
                _ctrl.DesktopBarClickThrough ? Color.FromArgb(234, 179, 8) : Color.FromArgb(14, 165, 233),
                delegate { _ctrl.ToggleClickThrough(); });
        }

        private static BarButtonSlot MakeSlot(ref int curX, int w, string text, Color accent, Action act)
        {
            BarButtonSlot b = new BarButtonSlot();
            b.Bounds = new Rectangle(curX, 3, w, 21);
            b.Label = text;
            b.Accent = accent;
            b.OnClick = act;
            curX += w + 4;
            return b;
        }

        public bool SimulateButtonClickForAudit(int index)
        {
            RebuildButtons();
            if (index < 0 || index >= _buttons.Length || _buttons[index].OnClick == null) return false;
            _buttons[index].OnClick();
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = -1;
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].Bounds.Contains(e.Location)) { hit = i; break; }
            }
            if (hit != _hoverIdx)
            {
                _hoverIdx = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIdx != -1) { _hoverIdx = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            RebuildButtons();
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].Bounds.Contains(e.Location) && _buttons[i].OnClick != null)
                {
                    _buttons[i].OnClick();
                    Invalidate();
                    break;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            g.Clear(Color.FromArgb(14, 16, 19));
            using (LinearGradientBrush lineBr = new LinearGradientBrush(
                new Point(0, 27), new Point(Math.Max(64, Width), 27),
                Color.FromArgb(245, 184, 81), Color.FromArgb(92, 225, 230)))
            {
                g.FillRectangle(lineBr, 0, 27, Width, 1);
            }

            RebuildButtons();
            OmegaTriEngineSharedBus.TopBarLiveSnapshot s = _ctrl.LastSnapshot;

            using (SolidBrush goldBr = new SolidBrush(Color.FromArgb(245, 184, 81)))
            using (SolidBrush cyanBr = new SolidBrush(Color.FromArgb(92, 225, 230)))
            using (SolidBrush txtBr  = new SolidBrush(Color.FromArgb(226, 232, 240)))
            {
                g.DrawString("OMEGA v1.9", _fntBold, goldBr, 8f, 6f);

                DrawStatusLed(g, 92, "SK", s.Skse64Live, Color.FromArgb(52, 211, 153));
                DrawStatusLed(g, 138, "RS", s.ReshadeLive, Color.FromArgb(56, 189, 248));

                string tele = string.Format(CultureInfo.InvariantCulture,
                    "{0:0.0} FPS ({1:0.1}ms) | Fade:{2:0}%{3} | XYZ:({4:0.0},{5:0.0},{6:0.0}) | H1:{7:0}",
                    s.SkyrimFps, s.FrameTimeMs, _ctrl.CurrentMenuFadeAlpha * 100.0f,
                    _ctrl.LastDetectedMenuOpen ? "[MENU]" : "",
                    s.CamX, s.CamY, s.CamZ, s.SheafH1State);
                g.DrawString(tele, _fntSmall, s.ReshadeLive ? cyanBr : txtBr, 188f, 6.5f);
            }

            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            for (int i = 0; i < _buttons.Length; i++)
            {
                BarButtonSlot b = _buttons[i];
                if (b.Bounds.Width <= 0) continue;
                bool hov = (i == _hoverIdx);
                Color fill = Color.FromArgb(hov ? 78 : 44, b.Accent.R, b.Accent.G, b.Accent.B);
                using (SolidBrush fb = new SolidBrush(fill))
                using (Pen pen = new Pen(Color.FromArgb(hov ? 235 : 155, b.Accent.R, b.Accent.G, b.Accent.B), 1.0f))
                using (SolidBrush tb = new SolidBrush(Color.White))
                {
                    g.FillRectangle(fb, b.Bounds);
                    g.DrawRectangle(pen, b.Bounds.X, b.Bounds.Y, b.Bounds.Width - 1, b.Bounds.Height - 1);
                    g.DrawString(b.Label, _fntSmall, tb, b.Bounds, sf);
                }
            }
        }

        private void DrawStatusLed(Graphics g, int x, string tag, bool live, Color liveColor)
        {
            Color c = live ? liveColor : Color.FromArgb(71, 85, 105);
            using (SolidBrush bFill = new SolidBrush(Color.FromArgb(36, c.R, c.G, c.B)))
            using (SolidBrush bDot = new SolidBrush(c))
            using (Pen pen = new Pen(Color.FromArgb(140, c.R, c.G, c.B), 1.0f))
            {
                g.FillRectangle(bFill, x, 4, 42, 19);
                g.DrawRectangle(pen, x, 4, 41, 18);
                g.FillEllipse(bDot, x + 5, 10, 7, 7);
                g.DrawString(tag, _fntBold, bDot, x + 14, 6f);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fntBold.Dispose();
                _fntSmall.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
