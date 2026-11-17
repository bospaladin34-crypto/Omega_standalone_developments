using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OmegaAVStudio
{
 // Zero-NuGet Hardware Performance Deck:
 // 1. Multi-Port Win32 Multimedia MIDI Input (winmm.dll midiInOpen + MIM_DATA lock-free callback)
 // 2. Xbox Series/One XInput (dynamic xinput1_4 / xinput9_1_0 / xinput1_3)
 // 3. Sony PS5 DualSense / PS4 DualShock 4 & Generic USB/BT HID (winmm.dll joyGetPosEx + VID 0x054C auto-calibration)
 // 4. Live 1-Click Knob/Stick/Trigger Learn Router with persistent controller_map.ini
 public sealed class OmegaInputDeck : IDisposable
 {
 private const uint CALLBACK_FUNCTION = 0x00030000u;
 private const uint MIM_DATA = 0x03C3u;
 private const uint JOY_RETURNALL = 0x00FFu;

 [UnmanagedFunctionPointer(CallingConvention.StdCall)]
 private delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

 [UnmanagedFunctionPointer(CallingConvention.StdCall)]
 private delegate uint XInputGetStateDel(uint dwUserIndex, out XINPUT_STATE pState);

 [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
 private struct MIDIINCAPSA
 {
  public ushort wMid;
  public ushort wPid;
  public uint vDriverVersion;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
  public string szPname;
  public uint dwSupport;
 }

 [StructLayout(LayoutKind.Sequential)]
 private struct XINPUT_GAMEPAD
 {
  public ushort wButtons;
  public byte bLeftTrigger;
  public byte bRightTrigger;
  public short sThumbLX;
  public short sThumbLY;
  public short sThumbRX;
  public short sThumbRY;
 }

 [StructLayout(LayoutKind.Sequential)]
 private struct XINPUT_STATE
 {
  public uint dwPacketNumber;
  public XINPUT_GAMEPAD Gamepad;
 }

 [StructLayout(LayoutKind.Sequential)]
 private struct JOYINFOEX
 {
  public uint dwSize;
  public uint dwFlags;
  public uint dwXpos;
  public uint dwYpos;
  public uint dwZpos;
  public uint dwRpos;
  public uint dwUpos;
  public uint dwVpos;
  public uint dwButtons;
  public uint dwButtonNumber;
  public uint dwPOV;
  public uint dwReserved1;
  public uint dwReserved2;
 }

 [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
 private struct JOYCAPSA
 {
  public ushort wMid;
  public ushort wPid;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
  public string szPname;
  public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
  public uint wNumButtons, wPeriodMin, wPeriodMax;
  public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
  public uint wCaps, wMaxAxes, wNumAxes, wMaxButtons;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
  public string szRegKey;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
  public string szOEMVxD;
 }

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint midiInGetNumDevs();

 [DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsA", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
 private static extern uint midiInGetDevCapsA(UIntPtr uDeviceID, out MIDIINCAPSA caps, uint cbCaps);

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint midiInOpen(out IntPtr phmi, uint uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, uint fdwOpen);

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint midiInStart(IntPtr hmi);

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint midiInStop(IntPtr hmi);

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint midiInReset(IntPtr hmi);

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint midiInClose(IntPtr hmi);

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint joyGetNumDevs();

 [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

 [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsA", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
 private static extern uint joyGetDevCapsA(UIntPtr uJoyID, out JOYCAPSA pjc, uint cbjc);

 [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
 private static extern IntPtr LoadLibrary(string lpFileName);

 [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
 private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

 // Source kinds for each of the 8 macro targets (0..3 = k1..k4, 4..7 = m1..m4)
 // SourceType: 0 = MIDI CC (SourceIndex = 0..127, or 128 = PitchBend), 1 = Gamepad Axis (0=LX, 1=LY, 2=RX, 3=RY, 4=LT/L2, 5=RT/R2)
 public static readonly string[] TargetLabels = new string[] {
  "k1 (Shader Knob 1)", "k2 (Shader Knob 2)", "k3 (Shader Knob 3)", "k4 (Shader Knob 4)",
  "m1 (Fluid Feed F)", "m2 (Fluid Kill k)", "m3 (Leech Advect)", "m4 (Fluid Glow/Lens)"
 };

 public readonly int[] BoundSourceType = new int[8];
 public readonly int[] BoundSourceIndex = new int[8];
 public readonly double[] MacroValues = new double[] { 0.50, 0.50, 0.50, 0.50, 0.50, 0.50, 0.65, 0.60 };
 public readonly bool[] TargetTouched = new bool[8];

 // Live normalized controller outputs [-1..1] for sticks, [0..1] for triggers & MIDI note
 public double PadLx, PadLy, PadRx, PadRy, PadLt, PadRt;
 public double MidiNoteNorm = 0.5, MidiVelNorm = 0.0;
 public bool PadConnected = false, IsDualSense = false;
 public string ActivePadName = "No Gamepad";
 public string ActiveMidiSummary = "MIDI: 0 ports";
 public string LastMidiEventText = "Waiting for MIDI CC / Note...";
 public volatile int LearnTargetSlot = -1;

 // Rising-edge button impulses consumed by OmegaAvForm each frame
 public bool PulseFluidSplash, PulseNextFluid, PulsePrevFluid, PulseNextFx, PulseNextLattice, PulseNextRaster, PulseNextPreset, PulsePrevPreset;
 public bool PadRightStickClick;

 private readonly string _configPath;
 private readonly List<IntPtr> _midiHandles = new List<IntPtr>();
 private readonly List<MidiInProc> _pinnedCallbacks = new List<MidiInProc>();
 private readonly List<string> _midiNames = new List<string>();
 private readonly int[] _ccRaw = new int[129];
 private readonly int[] _ccPrev = new int[129];
 private volatile int _pendingMidiNote = -1;
 private volatile int _pendingMidiVel = 0;
 private volatile int _lastMidiPacked = 0;

 private IntPtr _xinputLib = IntPtr.Zero;
 private XInputGetStateDel _xinputGetState = null;
 private ushort _prevXButtons = 0;
 private uint _prevJoyButtons = 0;
 private uint _prevJoyPov = 65535u;
 private int _joyCalibSlot = -1;
 private double _joyRestU = 0.0, _joyRestV = 0.0;
 private readonly double[] _prevAxisSample = new double[6];
 private double _rescanTimer = 0.0;
 private int _cachedJoySlot = -1;
 private bool _cachedJoySony = false;
 private string _cachedJoyName = "";

 public OmegaInputDeck(string avRoot)
 {
  _configPath = Path.Combine(avRoot, "controller_map.ini");
  for (int i = 0; i < 129; i++) { _ccRaw[i] = -1; _ccPrev[i] = -1; }

  // Default out-of-the-box bindings:
  // k1..k4 <- MIDI CC 74 (Cutoff), CC 71 (Reso), CC 73 (Attack), CC 72 (Release) [also CC 16..19 fallback]
  // m1..m2 <- MIDI CC 1 (Mod Wheel), CC 75
  // m3..m4 <- Gamepad LT/L2 (Axis 4) and RT/R2 (Axis 5) out of the box!
  BoundSourceType[0] = 0; BoundSourceIndex[0] = 74;
  BoundSourceType[1] = 0; BoundSourceIndex[1] = 71;
  BoundSourceType[2] = 0; BoundSourceIndex[2] = 73;
  BoundSourceType[3] = 0; BoundSourceIndex[3] = 72;
  BoundSourceType[4] = 0; BoundSourceIndex[4] = 1;
  BoundSourceType[5] = 0; BoundSourceIndex[5] = 75;
  BoundSourceType[6] = 1; BoundSourceIndex[6] = 4;
  BoundSourceType[7] = 1; BoundSourceIndex[7] = 5;

  InitXInputLibrary();
  LoadBindings();
  RescanMidiDevices();
  ScanWinMmJoystick();
 }

 private void InitXInputLibrary()
 {
  string[] dlls = new string[] { "xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll" };
  for (int i = 0; i < dlls.Length; i++)
  {
  try
  {
   IntPtr h = LoadLibrary(dlls[i]);
   if (h != IntPtr.Zero)
   {
   IntPtr fn = GetProcAddress(h, "XInputGetState");
   if (fn != IntPtr.Zero)
   {
    _xinputLib = h;
    _xinputGetState = (XInputGetStateDel)Marshal.GetDelegateForFunctionPointer(fn, typeof(XInputGetStateDel));
    return;
   }
   }
  }
  catch { }
  }
 }

 public void RescanMidiDevices()
 {
  CloseAllMidi();
  try
  {
  uint n = midiInGetNumDevs();
  for (uint i = 0; i < n && i < 8u; i++)
  {
   MIDIINCAPSA caps;
   string devName = "MIDI In #" + i;
   if (midiInGetDevCapsA((UIntPtr)i, out caps, (uint)Marshal.SizeOf(typeof(MIDIINCAPSA))) == 0u && !string.IsNullOrEmpty(caps.szPname))
   devName = caps.szPname.Trim();

   MidiInProc cb = OnMidiCallback;
   IntPtr hMidi;
   if (midiInOpen(out hMidi, i, cb, (IntPtr)i, CALLBACK_FUNCTION) == 0u && hMidi != IntPtr.Zero)
   {
   _pinnedCallbacks.Add(cb);
   _midiHandles.Add(hMidi);
   _midiNames.Add(devName);
   midiInStart(hMidi);
   }
  }
  }
  catch { }
  ActiveMidiSummary = (_midiNames.Count > 0)
  ? ("MIDI(" + _midiNames.Count + "): " + _midiNames[0])
  : "MIDI: 0 ports";
 }

 private void OnMidiCallback(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
 {
  if (wMsg != MIM_DATA) return;
  uint raw = (uint)(dwParam1.ToInt64() & 0xFFFFFFFFL);
  int status = (int)(raw & 0xFFu);
  int cmd = status & 0xF0;
  int d1 = (int)((raw >> 8) & 0x7Fu);
  int d2 = (int)((raw >> 16) & 0x7Fu);

  if (cmd == 0xB0)
  {
  _ccRaw[d1] = d2;
  _lastMidiPacked = (1 << 24) | ((status & 0x0F) << 16) | (d1 << 8) | d2;
  }
  else if (cmd == 0xE0)
  {
  int bend14 = (d2 << 7) | d1;
  _ccRaw[128] = bend14 >> 7;
  _lastMidiPacked = (2 << 24) | ((status & 0x0F) << 16) | (128 << 8) | (bend14 >> 7);
  }
  else if (cmd == 0x90 && d2 > 0)
  {
  _pendingMidiNote = d1;
  _pendingMidiVel = d2;
  _lastMidiPacked = (3 << 24) | ((status & 0x0F) << 16) | (d1 << 8) | d2;
  }
 }

 public string DescribeBinding(int slot)
 {
  if (slot < 0 || slot >= 8) return "";
  if (BoundSourceType[slot] == 0)
  return (BoundSourceIndex[slot] == 128) ? "MIDI PitchBend" : ("MIDI CC#" + BoundSourceIndex[slot]);
  string[] ax = new string[] { "Pad L-Stick X", "Pad L-Stick Y", "Pad R-Stick X", "Pad R-Stick Y", "Pad L2 / LT", "Pad R2 / RT" };
  int idx = Math.Max(0, Math.Min(5, BoundSourceIndex[slot]));
  return ax[idx];
 }

 public void SaveBindings()
 {
  try
  {
  StringBuilder sb = new StringBuilder();
  sb.AppendLine("# OmegaAV v0.7.1 Zero-NuGet MIDI & Xbox/DualSense Knob Map");
  for (int i = 0; i < 8; i++)
   sb.AppendLine(i + "=" + BoundSourceType[i] + "," + BoundSourceIndex[i] + "," + MacroValues[i].ToString("0.000", CultureInfo.InvariantCulture));
  File.WriteAllText(_configPath, sb.ToString(), new UTF8Encoding(false));
  }
  catch { }
 }

 public void LoadBindings()
 {
  try
  {
  if (!File.Exists(_configPath)) return;
  string[] lines = File.ReadAllLines(_configPath, Encoding.UTF8);
  for (int i = 0; i < lines.Length; i++)
  {
   string ln = lines[i].Trim();
   if (ln.Length == 0 || ln.StartsWith("#")) continue;
   string[] kv = ln.Split('=');
   if (kv.Length != 2) continue;
   int slot;
   if (!int.TryParse(kv[0].Trim(), out slot) || slot < 0 || slot >= 8) continue;
   string[] parts = kv[1].Split(',');
   if (parts.Length >= 2)
   {
   int st, si;
   if (int.TryParse(parts[0].Trim(), out st) && int.TryParse(parts[1].Trim(), out si))
   {
    BoundSourceType[slot] = (st == 1) ? 1 : 0;
    BoundSourceIndex[slot] = Math.Max(0, Math.Min(128, si));
   }
   double mv;
   if (parts.Length >= 3 && double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out mv))
    MacroValues[slot] = Math.Max(0.0, Math.Min(1.0, mv));
   }
  }
  }
  catch { }
 }

 private void ScanWinMmJoystick()
 {
  _cachedJoySlot = -1; _cachedJoySony = false; _cachedJoyName = "";
  try
  {
  uint n = joyGetNumDevs();
  JOYINFOEX ji = new JOYINFOEX();
  ji.dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
  ji.dwFlags = JOY_RETURNALL;
  for (uint u = 0; u < n && u < 8u; u++)
  {
   if (joyGetPosEx(u, ref ji) == 0u)
   {
   JOYCAPSA caps;
   string nm = "HID Gamepad #" + u;
   bool isSony = false;
   if (joyGetDevCapsA((UIntPtr)u, out caps, (uint)Marshal.SizeOf(typeof(JOYCAPSA))) == 0u)
   {
    if (!string.IsNullOrEmpty(caps.szPname)) nm = caps.szPname.Trim();
    if (caps.wMid == 0x054C || nm.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("Wireless Controller", StringComparison.OrdinalIgnoreCase) >= 0)
    {
    isSony = true;
    nm = (caps.wPid == 0x0CE6 || caps.wPid == 0x0DF2) ? "Sony DualSense" : "Sony DualShock/PS";
    }
   }
   _cachedJoySlot = (int)u;
   _cachedJoySony = isSony;
   _cachedJoyName = nm;
   if (_joyCalibSlot != (int)u)
   {
    _joyCalibSlot = (int)u;
    _joyRestU = ji.dwUpos;
    _joyRestV = ji.dwVpos;
   }
   return;
   }
  }
  }
  catch { }
 }

 public void PollAndUpdate(double dt)
 {
  PulseFluidSplash = false; PulseNextFluid = false; PulsePrevFluid = false;
  PulseNextFx = false; PulseNextLattice = false; PulseNextRaster = false;
  PulseNextPreset = false; PulsePrevPreset = false;

  for (int i = 0; i < 8; i++) TargetTouched[i] = false;

  // 1. Process lock-free MIDI messages
  int packed = _lastMidiPacked;
  if (packed != 0)
  {
  int kind = (packed >> 24) & 0xFF;
  int ch = ((packed >> 16) & 0x0F) + 1;
  int d1 = (packed >> 8) & 0xFF;
  int d2 = packed & 0xFF;
  if (kind == 1) LastMidiEventText = "MIDI Ch" + ch + " CC#" + d1 + " = " + d2;
  else if (kind == 2) LastMidiEventText = "MIDI Ch" + ch + " PitchBend = " + d2;
  else if (kind == 3) LastMidiEventText = "MIDI Ch" + ch + " Note " + d1 + " Vel=" + d2;
  }

  int note = Interlocked.Exchange(ref _pendingMidiNote, -1);
  if (note >= 0)
  {
  MidiNoteNorm = note / 127.0;
  MidiVelNorm = Math.Max(0.15, _pendingMidiVel / 127.0);
  PulseFluidSplash = true;
  }
  else
  {
  MidiVelNorm = Math.Max(0.0, MidiVelNorm - dt * 1.8);
  }

  int learnSlot = LearnTargetSlot;
  for (int cc = 0; cc < 129; cc++)
  {
  int val = _ccRaw[cc];
  if (val < 0 || val == _ccPrev[cc]) continue;
  int prev = _ccPrev[cc];
  _ccPrev[cc] = val;

  if (learnSlot >= 0 && learnSlot < 8 && prev >= 0 && Math.Abs(val - prev) >= 2)
  {
   BoundSourceType[learnSlot] = 0;
   BoundSourceIndex[learnSlot] = cc;
   LearnTargetSlot = -1;
   learnSlot = -1;
   SaveBindings();
  }
  // Convenience secondary CCs 16..19 -> k1..k4 if user hasn't rebound them
  for (int s = 0; s < 8; s++)
  {
   if (BoundSourceType[s] == 0 && (BoundSourceIndex[s] == cc || (cc >= 16 && cc <= 19 && (cc - 16) == s && BoundSourceIndex[s] >= 71 && BoundSourceIndex[s] <= 74)))
   {
   MacroValues[s] = val / 127.0;
   TargetTouched[s] = true;
   }
  }
  }

  // 2. Poll Xbox XInput first, then Sony DualSense / WinMM Joystick
  bool gotXInput = false;
  if (_xinputGetState != null)
  {
  for (uint u = 0; u < 4u; u++)
  {
   XINPUT_STATE st;
   if (_xinputGetState(u, out st) == 0u)
   {
   gotXInput = true;
   PadConnected = true;
   IsDualSense = false;
   ActivePadName = "Xbox XInput #" + u;
   PadLx = ApplyDeadzone(st.Gamepad.sThumbLX / 32767.0, 0.14);
   PadLy = ApplyDeadzone(st.Gamepad.sThumbLY / 32767.0, 0.14);
   PadRx = ApplyDeadzone(st.Gamepad.sThumbRX / 32767.0, 0.14);
   PadRy = ApplyDeadzone(st.Gamepad.sThumbRY / 32767.0, 0.14);
   PadLt = (st.Gamepad.bLeftTrigger > 10) ? ((st.Gamepad.bLeftTrigger - 10) / 245.0) : 0.0;
   PadRt = (st.Gamepad.bRightTrigger > 10) ? ((st.Gamepad.bRightTrigger - 10) / 245.0) : 0.0;

   ushort b = st.Gamepad.wButtons;
   ushort edge = (ushort)(b & ~_prevXButtons);
   _prevXButtons = b;
   PadRightStickClick = ((b & 0x0080) != 0);
   if ((edge & 0x1000) != 0) PulseFluidSplash = true;   // A
   if ((edge & 0x2000) != 0) PulseNextFx = true;        // B
   if ((edge & 0x4000) != 0) PulseNextLattice = true;   // X
   if ((edge & 0x8000) != 0) PulseNextRaster = true;    // Y
   if ((edge & 0x0001) != 0) PulseNextFluid = true;     // DPad Up
   if ((edge & 0x0002) != 0) PulsePrevFluid = true;     // DPad Down
   if ((edge & 0x0200) != 0 || (edge & 0x0008) != 0) PulseNextPreset = true; // RB / DPad Right
   if ((edge & 0x0100) != 0 || (edge & 0x0004) != 0) PulsePrevPreset = true; // LB / DPad Left
   break;
   }
  }
  }

  if (!gotXInput)
  {
  _rescanTimer += dt;
  if (_cachedJoySlot < 0 && _rescanTimer >= 1.5) { _rescanTimer = 0.0; ScanWinMmJoystick(); }
  if (_cachedJoySlot >= 0)
  {
   JOYINFOEX ji = new JOYINFOEX();
   ji.dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
   ji.dwFlags = JOY_RETURNALL;
   if (joyGetPosEx((uint)_cachedJoySlot, ref ji) == 0u)
   {
   PadConnected = true;
   IsDualSense = _cachedJoySony;
   ActivePadName = _cachedJoyName;

   PadLx = ApplyDeadzone((ji.dwXpos - 32767.5) / 32767.5, 0.14);
   PadLy = ApplyDeadzone((32767.5 - ji.dwYpos) / 32767.5, 0.14);
   PadRx = ApplyDeadzone((ji.dwZpos - 32767.5) / 32767.5, 0.14);
   PadRy = ApplyDeadzone((32767.5 - ji.dwRpos) / 32767.5, 0.14);

   uint jb = ji.dwButtons;
   double uDelta = Math.Abs((double)ji.dwUpos - _joyRestU);
   double vDelta = Math.Abs((double)ji.dwVpos - _joyRestV);
   double ltAn = (uDelta > 2400.0) ? Math.Min(1.0, uDelta / Math.Max(16384.0, 65535.0 - Math.Min(49000.0, _joyRestU))) : 0.0;
   double rtAn = (vDelta > 2400.0) ? Math.Min(1.0, vDelta / Math.Max(16384.0, 65535.0 - Math.Min(49000.0, _joyRestV))) : 0.0;
   if (_cachedJoySony)
   {
    if ((jb & 0x0040u) != 0u && ltAn < 0.15) ltAn = 1.0; // DualSense L2 digital bit 6
    if ((jb & 0x0080u) != 0u && rtAn < 0.15) rtAn = 1.0; // DualSense R2 digital bit 7
   }
   PadLt = ltAn;
   PadRt = rtAn;

   uint jEdge = jb & ~_prevJoyButtons;
   _prevJoyButtons = jb;
   uint pov = ji.dwPOV;
   bool povUp = (pov <= 4500u || (pov >= 31500u && pov < 36000u));
   bool povRight = (pov >= 4500u && pov <= 13500u);
   bool povDown = (pov >= 13500u && pov <= 22500u);
   bool povLeft = (pov >= 22500u && pov <= 31500u);
   bool prevCentered = (_prevJoyPov >= 36000u);
   _prevJoyPov = pov;

   if (_cachedJoySony)
   {
    // Sony DualSense / DS4 standard DirectInput button map:
    // bit0=Square, bit1=Cross, bit2=Circle, bit3=Triangle, bit4=L1, bit5=R1, bit11=R3
    PadRightStickClick = ((jb & 0x0800u) != 0u);
    if ((jEdge & 0x0002u) != 0u) PulseFluidSplash = true; // Cross
    if ((jEdge & 0x0004u) != 0u) PulseNextFx = true;      // Circle
    if ((jEdge & 0x0001u) != 0u) PulseNextLattice = true; // Square
    if ((jEdge & 0x0008u) != 0u) PulseNextRaster = true;  // Triangle
    if ((jEdge & 0x0020u) != 0u || (prevCentered && povRight)) PulseNextPreset = true; // R1
    if ((jEdge & 0x0010u) != 0u || (prevCentered && povLeft)) PulsePrevPreset = true;  // L1
   }
   else
   {
    PadRightStickClick = ((jb & 0x0200u) != 0u);
    if ((jEdge & 0x0001u) != 0u) PulseFluidSplash = true;
    if ((jEdge & 0x0002u) != 0u) PulseNextFx = true;
    if ((jEdge & 0x0004u) != 0u) PulseNextLattice = true;
    if ((jEdge & 0x0008u) != 0u) PulseNextRaster = true;
    if ((jEdge & 0x0020u) != 0u || (prevCentered && povRight)) PulseNextPreset = true;
    if ((jEdge & 0x0010u) != 0u || (prevCentered && povLeft)) PulsePrevPreset = true;
   }
   if (prevCentered && povUp) PulseNextFluid = true;
   if (prevCentered && povDown) PulsePrevFluid = true;
   }
   else
   {
   _cachedJoySlot = -1;
   PadConnected = false;
   ActivePadName = "No Gamepad";
   PadLx = 0.0; PadLy = 0.0; PadRx = 0.0; PadRy = 0.0; PadLt = 0.0; PadRt = 0.0;
   }
  }
  else
  {
   PadConnected = false;
   ActivePadName = "No Gamepad";
  }
  }

  // 3. Gamepad Axis Learn & Continuous Knob Modulation
  if (PadConnected)
  {
  double[] curAx = new double[] {
   0.5 + 0.5 * PadLx, 0.5 + 0.5 * PadLy,
   0.5 + 0.5 * PadRx, 0.5 + 0.5 * PadRy,
   PadLt, PadRt
  };
  if (LearnTargetSlot >= 0 && LearnTargetSlot < 8)
  {
   for (int a = 0; a < 6; a++)
   {
   double rest = (a < 4) ? 0.5 : 0.0;
   if (Math.Abs(curAx[a] - rest) > 0.42 && Math.Abs(curAx[a] - _prevAxisSample[a]) > 0.04)
   {
    BoundSourceType[LearnTargetSlot] = 1;
    BoundSourceIndex[LearnTargetSlot] = a;
    LearnTargetSlot = -1;
    SaveBindings();
    break;
   }
   }
  }
  for (int a = 0; a < 6; a++) _prevAxisSample[a] = curAx[a];

  for (int s = 0; s < 8; s++)
  {
   if (BoundSourceType[s] == 1)
   {
   int axIdx = Math.Max(0, Math.Min(5, BoundSourceIndex[s]));
   if (axIdx >= 4)
   {
    // Triggers modulate smoothly above baseline when pulled, or return gently when released
    if (curAx[axIdx] > 0.02 || Math.Abs(MacroValues[s] - curAx[axIdx]) > 0.01)
    {
    if (s >= 6) MacroValues[s] = 0.45 + 0.55 * curAx[axIdx];
    else MacroValues[s] = curAx[axIdx];
    TargetTouched[s] = (curAx[axIdx] > 0.02);
    }
   }
   else if (Math.Abs(curAx[axIdx] - 0.5) > 0.03)
   {
    MacroValues[s] = Math.Max(0.0, Math.Min(1.0, MacroValues[s] + (curAx[axIdx] - 0.5) * dt * 0.85));
    TargetTouched[s] = true;
   }
   }
  }
  }
 }

 private static double ApplyDeadzone(double v, double dz)
 {
  double a = Math.Abs(v);
  if (a <= dz) return 0.0;
  double scaled = (a - dz) / (1.0 - dz);
  return (v < 0.0) ? -Math.Min(1.0, scaled) : Math.Min(1.0, scaled);
 }

 private void CloseAllMidi()
 {
  for (int i = 0; i < _midiHandles.Count; i++)
  {
  IntPtr h = _midiHandles[i];
  if (h != IntPtr.Zero)
  {
   try { midiInStop(h); midiInReset(h); midiInClose(h); } catch { }
  }
  }
  _midiHandles.Clear();
  _pinnedCallbacks.Clear();
  _midiNames.Clear();
 }

 public void Dispose()
 {
  CloseAllMidi();
 }
 }

 // Non-blocking Live Hardware MIDI + Xbox / DualSense Controller Learn Window
 public sealed class ControllerLearnForm : Form
 {
 private readonly OmegaInputDeck _deck;
 private readonly Action _onSplash;
 private readonly Label _lblDevices, _lblLiveAxes;
 private readonly Label[] _lblBind = new Label[8];
 private readonly ProgressBar[] _bars = new ProgressBar[8];
 private readonly Button[] _btnLearn = new Button[8];
 private readonly System.Windows.Forms.Timer _uiTimer;

 public ControllerLearnForm(OmegaInputDeck deck, Action onSplash)
 {
  _deck = deck;
  _onSplash = onSplash;
  Text = "OmegaAV v0.7.1 :: Zero-NuGet MIDI + Xbox / Sony DualSense Knob Learn Deck";
  ClientSize = new Size(560, 410);
  FormBorderStyle = FormBorderStyle.FixedToolWindow;
  StartPosition = FormStartPosition.Manual;
  Location = new Point(120, 110);
  TopMost = true;
  BackColor = Color.FromArgb(10, 15, 28);
  ForeColor = Color.White;

  _lblDevices = new Label();
  _lblDevices.SetBounds(14, 10, 532, 38);
  _lblDevices.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
  _lblDevices.ForeColor = Color.FromArgb(56, 189, 248);
  _lblDevices.BackColor = Color.FromArgb(15, 23, 42);
  _lblDevices.Padding = new Padding(6, 4, 6, 4);

  _lblLiveAxes = new Label();
  _lblLiveAxes.SetBounds(14, 52, 532, 22);
  _lblLiveAxes.Font = new Font("Consolas", 8.2f);
  _lblLiveAxes.ForeColor = Color.FromArgb(52, 211, 153);

  for (int i = 0; i < 8; i++)
  {
  int slot = i;
  int y = 82 + i * 33;
  Label ln = new Label();
  ln.SetBounds(14, y + 4, 156, 20);
  ln.Font = new Font("Consolas", 8.3f, FontStyle.Bold);
  ln.ForeColor = (i < 4) ? Color.FromArgb(196, 181, 253) : Color.FromArgb(110, 231, 183);
  ln.Text = OmegaInputDeck.TargetLabels[i];

  ProgressBar pb = new ProgressBar();
  pb.SetBounds(174, y + 4, 146, 18);
  pb.Minimum = 0; pb.Maximum = 1000;
  _bars[i] = pb;

  Label lb = new Label();
  lb.SetBounds(328, y + 4, 132, 20);
  lb.Font = new Font("Consolas", 8.1f);
  lb.ForeColor = Color.FromArgb(226, 232, 240);
  _lblBind[i] = lb;

  Button b = new Button();
  b.SetBounds(464, y + 1, 82, 24);
  b.FlatStyle = FlatStyle.Flat;
  b.Font = new Font("Segoe UI Semibold", 7.8f);
  b.BackColor = Color.FromArgb(67, 56, 202);
  b.ForeColor = Color.White;
  b.Text = "Learn";
  b.Click += delegate
  {
   _deck.LearnTargetSlot = (_deck.LearnTargetSlot == slot) ? -1 : slot;
  };
  _btnLearn[i] = b;

  Controls.Add(ln); Controls.Add(pb); Controls.Add(lb); Controls.Add(b);
  }

  Button bRescan = new Button();
  bRescan.SetBounds(14, 358, 170, 34);
  bRescan.FlatStyle = FlatStyle.Flat; bRescan.BackColor = Color.FromArgb(14, 116, 144); bRescan.ForeColor = Color.White;
  bRescan.Font = new Font("Segoe UI Semibold", 8.2f);
  bRescan.Text = "Rescan MIDI + Gamepads";
  bRescan.Click += delegate { _deck.RescanMidiDevices(); };

  Button bSplash = new Button();
  bSplash.SetBounds(194, 358, 174, 34);
  bSplash.FlatStyle = FlatStyle.Flat; bSplash.BackColor = Color.FromArgb(5, 150, 105); bSplash.ForeColor = Color.White;
  bSplash.Font = new Font("Segoe UI Semibold", 8.2f);
  bSplash.Text = "Splash Golay Fluid Wave (S)";
  bSplash.Click += delegate { if (_onSplash != null) _onSplash(); };

  Button bClose = new Button();
  bClose.SetBounds(378, 358, 168, 34);
  bClose.FlatStyle = FlatStyle.Flat; bClose.BackColor = Color.FromArgb(71, 85, 105); bClose.ForeColor = Color.White;
  bClose.Font = new Font("Segoe UI Semibold", 8.2f);
  bClose.Text = "Hide Deck (K)";
  bClose.Click += delegate { Hide(); };

  Controls.AddRange(new Control[] { _lblDevices, _lblLiveAxes, bRescan, bSplash, bClose });

  _uiTimer = new System.Windows.Forms.Timer();
  _uiTimer.Interval = 50;
  _uiTimer.Tick += delegate { RefreshTelemetry(); };
  _uiTimer.Start();

  FormClosing += delegate(object s, FormClosingEventArgs e)
  {
  if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; _deck.LearnTargetSlot = -1; Hide(); }
  };
 }

 private void RefreshTelemetry()
 {
  if (!Visible) return;
  _lblDevices.Text = _deck.ActiveMidiSummary + "  |  Pad: " + _deck.ActivePadName + "\r\n" + _deck.LastMidiEventText;
  _lblLiveAxes.Text = string.Format(CultureInfo.InvariantCulture,
  "Sticks L:({0:0.00},{1:0.00}) R:({2:0.00},{3:0.00}) | Triggers L2/LT:{4:0.00} R2/RT:{5:0.00}",
  _deck.PadLx, _deck.PadLy, _deck.PadRx, _deck.PadRy, _deck.PadLt, _deck.PadRt);

  for (int i = 0; i < 8; i++)
  {
  int v = Math.Max(0, Math.Min(1000, (int)Math.Round(_deck.MacroValues[i] * 1000.0)));
  _bars[i].Value = v;
  _lblBind[i].Text = _deck.DescribeBinding(i) + " [" + _deck.MacroValues[i].ToString("0.00") + "]";
  bool learning = (_deck.LearnTargetSlot == i);
  _btnLearn[i].Text = learning ? "Move Knob..." : "Learn";
  _btnLearn[i].BackColor = learning ? Color.FromArgb(217, 119, 6) : Color.FromArgb(67, 56, 202);
  }
 }
 }
}
