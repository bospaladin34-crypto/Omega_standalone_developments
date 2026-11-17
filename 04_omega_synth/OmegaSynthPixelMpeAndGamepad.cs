using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmegaSynthStudio
{
    public sealed class OmegaPixelMpeAndGamepadServer : IDisposable
    {
        private readonly OmegaWavefieldAudioEngine _audio;
        private readonly OmegaCymaticsGpuEngine _gpu;
        private readonly string _rendersDir;
        private TcpListener _listener;
        private volatile bool _running;

        public int MpePacketsReceived = 0;
        public volatile string ConnectedPixelHint = "Ready on :19782/mpe";
        public Action OnUiStateMutated;
        public Action<string> OnExternalFileLoadRequested;
        public Action<string, string, bool> OnBrowserPipeRequested;

        private const uint CALLBACK_FUNCTION = 0x00030000u;
        private const uint MIM_DATA = 0x03C3u;
        private const uint JOY_RETURNALL = 0x00FFu;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint XInputGetStateDel(uint dwUserIndex, out XINPUT_STATE pState);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger, bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
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
            public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint midiInOpen(out IntPtr phmi, uint uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint midiInStart(IntPtr hmi);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint midiInStop(IntPtr hmi);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint midiInReset(IntPtr hmi);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint midiInClose(IntPtr hmi);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint joyGetNumDevs();
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)] private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        private readonly List<IntPtr> _midiHandles = new List<IntPtr>();
        private readonly List<MidiInProc> _midiCallbacks = new List<MidiInProc>();
        private XInputGetStateDel _xinputGetState;
        private ushort _prevXBtn = 0;
        private bool _padVoiceGate = false;
        public string ControllerStatus = "MIDI:0";

        public OmegaPixelMpeAndGamepadServer(OmegaWavefieldAudioEngine audio, OmegaCymaticsGpuEngine gpu, string rendersDir)
        {
            _audio = audio;
            _gpu = gpu;
            _rendersDir = rendersDir;
            InitXInput();
            RescanControllers();
        }

        private void InitXInput()
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
                            _xinputGetState = (XInputGetStateDel)Marshal.GetDelegateForFunctionPointer(fn, typeof(XInputGetStateDel));
                            return;
                        }
                    }
                }
                catch { }
            }
        }

        public void RescanControllers()
        {
            CloseMidi();
            try
            {
                uint n = midiInGetNumDevs();
                for (uint i = 0; i < n && i < 6u; i++)
                {
                    MidiInProc cb = OnMidiMsg;
                    IntPtr h;
                    if (midiInOpen(out h, i, cb, (IntPtr)i, CALLBACK_FUNCTION) == 0u && h != IntPtr.Zero)
                    {
                        _midiCallbacks.Add(cb);
                        _midiHandles.Add(h);
                        midiInStart(h);
                    }
                }
            }
            catch { }
            ControllerStatus = "MIDI:" + _midiHandles.Count;
        }

        public void PollGamepadExpression()
        {
            bool connected = false;
            double lx = 0, ly = 0, rx = 0, ry = 0, lt = 0, rt = 0;
            bool trigPluck = false, trigLoop = false, trigSand = false;

            if (_xinputGetState != null)
            {
                XINPUT_STATE st;
                if (_xinputGetState(0u, out st) == 0u)
                {
                    connected = true;
                    lx = Deadzone(st.Gamepad.sThumbLX / 32767.0);
                    ly = Deadzone(st.Gamepad.sThumbLY / 32767.0);
                    rx = Deadzone(st.Gamepad.sThumbRX / 32767.0);
                    ry = Deadzone(st.Gamepad.sThumbRY / 32767.0);
                    lt = st.Gamepad.bLeftTrigger > 12 ? (st.Gamepad.bLeftTrigger - 12) / 243.0 : 0.0;
                    rt = st.Gamepad.bRightTrigger > 12 ? (st.Gamepad.bRightTrigger - 12) / 243.0 : 0.0;
                    ushort edge = (ushort)(st.Gamepad.wButtons & ~_prevXBtn);
                    _prevXBtn = st.Gamepad.wButtons;
                    trigPluck = (edge & 0x1000) != 0;
                    trigLoop  = (edge & 0x2000) != 0;
                    trigSand  = (edge & 0x8000) != 0;
                    ControllerStatus = "MIDI:" + _midiHandles.Count + " | Xbox LIVE";
                }
            }

            if (!connected)
            {
                try
                {
                    JOYINFOEX ji = new JOYINFOEX();
                    ji.dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
                    ji.dwFlags = JOY_RETURNALL;
                    if (joyGetNumDevs() > 0 && joyGetPosEx(0u, ref ji) == 0u)
                    {
                        connected = true;
                        lx = Deadzone((ji.dwXpos - 32767.5) / 32767.5);
                        ly = Deadzone((32767.5 - ji.dwYpos) / 32767.5);
                        rx = Deadzone((ji.dwZpos - 32767.5) / 32767.5);
                        ry = Deadzone((32767.5 - ji.dwRpos) / 32767.5);
                        rt = ((ji.dwButtons & 0x0080u) != 0u) ? 0.9 : 0.0;
                        lt = ((ji.dwButtons & 0x0040u) != 0u) ? 0.8 : 0.0;
                        ControllerStatus = "MIDI:" + _midiHandles.Count + " | HID LIVE";
                    }
                }
                catch { }
            }

            if (!connected) return;

            if (lt > 0.05) _audio.E8CouplingOverride = (float)Math.Min(1.0, 0.25 + lt * 0.75);
            if (Math.Abs(rx) > 0.15) _audio.FormantVowelMorph = (float)Math.Max(0.0, Math.Min(1.0, 0.5 + rx * 0.5));
            if (Math.Abs(ry) > 0.25)
            {
                _audio.AutoChladniFromPitch = false;
                _audio.ChladniN = Math.Max(1, Math.Min(12, 5 + (int)Math.Round(ry * 5.0)));
            }

            if (rt > 0.06 || trigPluck)
            {
                double nx = 0.5 + 0.48 * lx;
                double ny = 0.5 + 0.48 * ly;
                _audio.SetOrTriggerVoice(650, nx, ny, Math.Max(0.35, rt > 0.06 ? rt : 0.85), false);
                _padVoiceGate = true;
                if (trigPluck && rt <= 0.06) { _audio.ReleaseVoice(650); _padVoiceGate = false; }
            }
            else if (_padVoiceGate)
            {
                _audio.ReleaseVoice(650);
                _padVoiceGate = false;
            }

            if (trigLoop) { _audio.SeqLooper.ToggleRecOrOverdub(_audio.ArpBpm); if (OnUiStateMutated != null) OnUiStateMutated(); }
            if (trigSand) _gpu.ResetSandAndWavefield();
        }

        private static double Deadzone(double v)
        {
            double a = Math.Abs(v);
            if (a < 0.15) return 0.0;
            return (v < 0 ? -1.0 : 1.0) * Math.Min(1.0, (a - 0.15) / 0.85);
        }

        private void OnMidiMsg(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            if (wMsg != MIM_DATA) return;
            uint raw = (uint)(dwParam1.ToInt64() & 0xFFFFFFFFL);
            int status = (int)(raw & 0xFFu);
            int cmd = status & 0xF0;
            int d1 = (int)((raw >> 8) & 0x7Fu);
            int d2 = (int)((raw >> 16) & 0x7Fu);

            if (cmd == 0x90 && d2 > 0)
            {
                double x = Math.Max(0.0, Math.Min(1.0, (d1 - _audio.BaseMidiNote) / (12.0 * _audio.OctaveSpan)));
                _audio.SetOrTriggerVoice(500 + d1, x, 0.35 + 0.55 * (d2 / 127.0), d2 / 127.0, false);
            }
            else if (cmd == 0x80 || (cmd == 0x90 && d2 == 0)) _audio.ReleaseVoice(500 + d1);
            else if (cmd == 0xB0 && d1 == 1) _audio.FormantVowelMorph = d2 / 127.0f;
            else if (cmd == 0xE0)
            {
                int bend14 = (d2 << 7) | d1;
                _audio.UpdatePixelGyro(_audio.GyroBetaDeg, ((bend14 - 8192) / 8192.0) * 35.0);
            }
        }

        public void Start(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _running = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    while (_running)
                    {
                        try
                        {
                            TcpClient c = _listener.AcceptTcpClient();
                            ThreadPool.QueueUserWorkItem(HandleClient, c);
                        }
                        catch { if (!_running) break; }
                    }
                });
            }
            catch { }
        }

        private void HandleClient(object state)
        {
            using (TcpClient c = (TcpClient)state)
            {
                try
                {
                    c.NoDelay = true; c.ReceiveTimeout = 3500; c.SendTimeout = 3500;
                    NetworkStream ns = c.GetStream();
                    byte[] buf = new byte[16384];
                    int rd = ns.Read(buf, 0, buf.Length);
                    if (rd <= 0) return;

                    string raw = Encoding.UTF8.GetString(buf, 0, rd);
                    int firstNl = raw.IndexOf('\r');
                    if (firstNl < 0) firstNl = raw.IndexOf('\n');
                    string reqLine = (firstNl > 0) ? raw.Substring(0, firstNl) : raw;
                    string[] parts = reqLine.Split(' ');
                    string fullPath = parts.Length > 1 ? parts[1] : "/";
                    int bodyIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string body = (bodyIdx >= 0 && bodyIdx + 4 < raw.Length) ? raw.Substring(bodyIdx + 4) : "";

                    string pathOnly = fullPath, query = "";
                    int qIdx = fullPath.IndexOf('?');
                    if (qIdx >= 0) { pathOnly = fullPath.Substring(0, qIdx); query = fullPath.Substring(qIdx + 1); }

                    if (pathOnly.Equals("/api/mpe", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleMpePacket(query, body);
                        string noteLbl = "--"; int cents = 0;
                        if (_audio.LeadVoiceHz > 20.0) E8WaveLatticeMath.FormatNoteAndCents(_audio.LeadVoiceHz, out noteLbl, out cents);
                        string resp = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"voices\":{0},\"hz\":{1:0.0},\"note\":\"{2}\",\"cents\":{3},\"m\":{4},\"n\":{5},\"loop\":{6},\"root\":{7}}}",
                            _audio.ActiveVoiceCount, _audio.LeadVoiceHz, noteLbl, cents, _audio.ChladniM, _audio.ChladniN, _audio.SeqLooper.LoopState, _audio.LeadVoiceRoot);
                        WriteJson(ns, resp);
                        return;
                    }

                    if (pathOnly.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
                    {
                        string st = string.Format(CultureInfo.InvariantCulture,
                            "{{\"app\":\"OmegaSynth v1.1.1 Final GM\",\"port\":19782,\"gpu\":{0},\"vsync\":{1},\"preset\":{2},\"scale\":{3},\"m\":{4},\"n\":{5},\"loop_state\":{6},\"arp\":{7},\"bpm\":{8},\"voices\":{9},\"web_pipe\":{10},\"mic\":{11},\"evolve\":{12}}}",
                            _gpu.IsGpuActive ? "true" : "false", _gpu.VSyncEnabled ? "true" : "false",
                            _audio.PresetIndex, _audio.ScaleIndex, _audio.ChladniM, _audio.ChladniN,
                            _audio.SeqLooper.LoopState, _audio.ArpEnabled ? "true" : "false", _audio.ArpBpm,
                            _audio.ActiveVoiceCount, _audio.WebPipeActive ? "true" : "false",
                            _audio.MicInputActive ? "true" : "false", _audio.AutoEvolveEnabled ? "true" : "false");
                        WriteJson(ns, st);
                        return;
                    }

                    if (pathOnly.Equals("/api/browser-pipe", StringComparison.OrdinalIgnoreCase))
                    {
                        Dictionary<string, string> kv = ParseQueryMap(!string.IsNullOrEmpty(query) ? query : "");
                        string title = kv.ContainsKey("title") ? kv["title"] : ExtractJsonStr(body, "title");
                        string url   = kv.ContainsKey("url") ? kv["url"] : ExtractJsonStr(body, "url");
                        string act   = kv.ContainsKey("active") ? kv["active"] : ExtractJsonStr(body, "active");
                        bool enable = string.IsNullOrEmpty(act) || act == "1" || act.Equals("true", StringComparison.OrdinalIgnoreCase);
                        if (act == "toggle") enable = !_audio.WebPipeActive;
                        if (OnBrowserPipeRequested != null) OnBrowserPipeRequested(title, url, enable);
                        else _audio.SetWebPipeEnabled(enable, title, url);
                        WriteJson(ns, "{\"ok\":true,\"web_pipe\":" + (_audio.WebPipeActive ? "true" : "false") + ",\"title\":\"" + JsonEsc(title) + "\"}");
                        return;
                    }

                    if (pathOnly.Equals("/api/load-file", StringComparison.OrdinalIgnoreCase))
                    {
                        Dictionary<string, string> kv = ParseQueryMap(!string.IsNullOrEmpty(query) ? query : "");
                        string fp = kv.ContainsKey("path") ? kv["path"] : ExtractJsonStr(body, "path");
                        if (!string.IsNullOrEmpty(fp) && OnExternalFileLoadRequested != null)
                        {
                            OnExternalFileLoadRequested(fp);
                            WriteJson(ns, "{\"ok\":true,\"loaded\":\"" + JsonEsc(fp) + "\"}");
                        }
                        else
                        {
                            WriteJson(ns, "{\"ok\":false,\"error\":\"Missing path\"}");
                        }
                        return;
                    }

                    if (pathOnly.Equals("/api/control", StringComparison.OrdinalIgnoreCase) || pathOnly.Equals("/api/preset", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleControlPacket(query, body);
                        if (OnUiStateMutated != null) { try { OnUiStateMutated(); } catch { } }
                        WriteJson(ns, "{\"ok\":true,\"loop\":" + _audio.SeqLooper.LoopState + "}");
                        return;
                    }

                    byte[] html = Encoding.UTF8.GetBytes(BuildPixelSplitInstrumentHtml());
                    byte[] hdr = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: "
                        + html.Length + "\r\nConnection: close\r\n\r\n");
                    ns.Write(hdr, 0, hdr.Length);
                    ns.Write(html, 0, html.Length);
                }
                catch { }
            }
        }

        private void HandleMpePacket(string query, string body)
        {
            int cnt = Interlocked.Increment(ref MpePacketsReceived);
            ConnectedPixelHint = "Pixel MPE LIVE (" + cnt + " pkts)";
            Dictionary<string, string> kv = ParseQueryMap(!string.IsNullOrEmpty(query) ? query : body);

            string gben;
            if (kv.TryGetValue("gben", out gben))
                _audio.GyroPitchBendEnabled = (gben == "1" || gben.Equals("true", StringComparison.OrdinalIgnoreCase));

            string gbStr, ggStr;
            if (kv.TryGetValue("gb", out gbStr) && kv.TryGetValue("gg", out ggStr))
            {
                double gb, gg;
                if (double.TryParse(gbStr, NumberStyles.Float, CultureInfo.InvariantCulture, out gb) &&
                    double.TryParse(ggStr, NumberStyles.Float, CultureInfo.InvariantCulture, out gg))
                    _audio.UpdatePixelGyro(gb, gg);
            }

            string kxStr, kyStr;
            if (kv.TryGetValue("kx", out kxStr) && kv.TryGetValue("ky", out kyStr))
            {
                double kx, ky;
                if (double.TryParse(kxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out kx) &&
                    double.TryParse(kyStr, NumberStyles.Float, CultureInfo.InvariantCulture, out ky))
                {
                    _audio.FormantFilterEnabled = true;
                    _audio.FormantVowelMorph = (float)Math.Max(0.0, Math.Min(1.0, kx));
                    _audio.E8CouplingOverride = (float)Math.Max(0.1, Math.Min(1.0, ky));
                    _audio.AutoChladniFromPitch = false;
                    _audio.ChladniM = Math.Max(1, Math.Min(10, 1 + (int)(kx * 8.9)));
                    _audio.ChladniN = Math.Max(2, Math.Min(12, 2 + (int)(ky * 9.9)));
                }
            }

            string upStr;
            if (kv.TryGetValue("up", out upStr) && !string.IsNullOrEmpty(upStr))
            {
                if (upStr == "all") _audio.ReleaseAllVoices(true);
                else
                {
                    string[] ids = upStr.Split(',');
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int id; if (int.TryParse(ids[i], out id)) _audio.ReleaseVoice(1000 + (Math.Abs(id) % 10000));
                    }
                }
            }

            string tStr;
            if (kv.TryGetValue("t", out tStr) && !string.IsNullOrEmpty(tStr))
            {
                string[] touches = tStr.Split(',');
                for (int i = 0; i < touches.Length; i++)
                {
                    string[] f = touches[i].Split(':');
                    if (f.Length >= 4)
                    {
                        int tid; double x, y, p;
                        if (int.TryParse(f[0], out tid) &&
                            double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                            double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                            double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out p))
                        {
                            _audio.SetOrTriggerVoice(1000 + (Math.Abs(tid) % 10000), x, y, p, true);
                        }
                    }
                }
            }
        }

        private void HandleControlPacket(string query, string body)
        {
            Dictionary<string, string> kv = ParseQueryMap(!string.IsNullOrEmpty(query) ? query : body);
            string v;
            if (kv.TryGetValue("preset", out v))
            {
                int p; if (int.TryParse(v, out p)) { _audio.PresetIndex = Math.Max(0, Math.Min(E8WaveLatticeMath.Presets.Length - 1, p)); _gpu.ResetSandAndWavefield(); }
            }
            if (kv.TryGetValue("scale", out v))
            {
                int sc; if (int.TryParse(v, out sc)) _audio.ScaleIndex = Math.Max(0, Math.Min(E8WaveLatticeMath.ScaleNames.Length - 1, sc));
            }
            if (kv.TryGetValue("arp", out v))
            {
                if (v == "toggle") _audio.ArpEnabled = !_audio.ArpEnabled;
                else _audio.ArpEnabled = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            if (kv.TryGetValue("drone", out v))
            {
                if (v == "toggle") _audio.DroneLatchMode = !_audio.DroneLatchMode;
                if (!_audio.DroneLatchMode) _audio.ReleaseAllVoices(true);
            }
            if (kv.TryGetValue("loop", out v))
            {
                if (v == "rec") _audio.SeqLooper.ToggleRecOrOverdub(_audio.ArpBpm);
                else if (v == "clear") { _audio.SeqLooper.ClearLoop(); _audio.ReleaseLooperVoicesOnly(); }
            }
            if (kv.TryGetValue("rec", out v) && v == "toggle")
            {
                if (_audio.IsRecordingWav) _audio.StopAndSaveWavRecording(_rendersDir);
                else _audio.BeginWavRecording();
            }
            if (kv.TryGetValue("mic", out v))
            {
                bool en = (v == "toggle") ? !_audio.MicInputActive : (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                _audio.SetMicInputEnabled(en, _audio.MicCapture.SelectedDeviceIndex);
            }
            if (kv.TryGetValue("evolve", out v))
            {
                _audio.AutoEvolveEnabled = (v == "toggle") ? !_audio.AutoEvolveEnabled : (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(body) && body.Contains("{"))
            {
                Match mp = Regex.Match(body, "\"preset\"\\s*:\\s*(\\d+)");
                if (mp.Success) _audio.PresetIndex = Math.Max(0, Math.Min(E8WaveLatticeMath.Presets.Length - 1, int.Parse(mp.Groups[1].Value)));
                Match ms = Regex.Match(body, "\"scale\"\\s*:\\s*(\\d+)");
                if (ms.Success) _audio.ScaleIndex = Math.Max(0, Math.Min(5, int.Parse(ms.Groups[1].Value)));
                Match mb = Regex.Match(body, "\"bpm\"\\s*:\\s*(\\d+)");
                if (mb.Success) _audio.ArpBpm = Math.Max(40, Math.Min(240, int.Parse(mb.Groups[1].Value)));
            }
        }

        private static string ExtractJsonStr(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : "";
        }

        private static string JsonEsc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private static Dictionary<string, string> ParseQueryMap(string q)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(q)) return d;
            string[] pairs = q.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq > 0)
                    d[Uri.UnescapeDataString(pairs[i].Substring(0, eq).Trim())] = Uri.UnescapeDataString(pairs[i].Substring(eq + 1).Trim());
            }
            return d;
        }

        private static void WriteJson(NetworkStream ns, string json)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            byte[] h = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: "
                + b.Length + "\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\n");
            ns.Write(h, 0, h.Length); ns.Write(b, 0, b.Length);
        }

        private static string BuildPixelSplitInstrumentHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no'>
<title>OmegaSynth v1.1.1 Final GM :: Pixel 10 Split Kaoss + MPE Continuum</title>
<style>
*{box-sizing:border-box;margin:0;padding:0;user-select:none;-webkit-user-select:none;touch-action:none}
body{background:#000;color:#e2e8f0;font-family:Consolas,monospace;height:100vh;width:100vw;overflow:hidden;display:flex;flex-direction:column}
.top{display:flex;gap:4px;padding:5px 7px;background:#050811;border-bottom:1px solid #1e293b;align-items:center;flex-wrap:wrap}
.badge{font-size:11px;font-weight:700;color:#f472b6;padding:4px 7px;border:1px solid #db2777;border-radius:6px;background:#190718}
select,button{background:#0f172a;color:#f8fafc;border:1px solid #334155;border-radius:6px;padding:5px 7px;font-size:11px;font-weight:700;font-family:Consolas,monospace}
button.on{background:#059669;border-color:#34d399}
button.loop{background:#d97706;border-color:#fde047}
button.rec{background:#dc2626;border-color:#fca5a5}
#pad{flex:1;width:100%;display:block;background:#000}
.bot{display:flex;justify-content:space-between;padding:4px 8px;background:#050811;border-top:1px solid #1e293b;font-size:11px;color:#38bdf8}
</style>
</head>
<body>
<div class='top'>
  <span class='badge'>SYNTH v1.0 GM</span>
  <select onchange='ctrl(""preset=""+this.value)'>
    <option value='0'>01 E8 Continuum</option>
    <option value='1'>02 Chladni Bronze</option>
    <option value='2'>03 Quartz Bowl</option>
    <option value='3'>04 Handpan</option>
    <option value='4'>05 Bowed Glass</option>
    <option value='5'>06 Sub Gong</option>
    <option value='6'>07 KS Stiff Harp</option>
    <option value='7'>08 Formant Choir</option>
    <option value='8'>09 Cloned Resynth</option>
  </select>
  <select onchange='ctrl(""scale=""+this.value)'>
    <option value='0'>12-TET Glide</option>
    <option value='1'>Just 5-Limit</option>
    <option value='2'>31-EDO</option>
    <option value='3'>Bohlen-Pierce</option>
    <option value='4'>Golden Phi</option>
    <option value='5'>E8 Coxeter</option>
  </select>
  <select id='selLayout' onchange='splitMode=parseInt(this.value)'>
    <option value='0'>Split: Kaoss + Ribbon</option>
    <option value='1'>Full: Fretless Ribbon</option>
  </select>
  <button id='btnLoop' onclick='ctrlLoop()'>Loop REC</button>
  <button onclick='ctrl(""loop=clear"")'>Clr</button>
  <button id='btnArp' onclick='toggleArp()'>4-Lane</button>
  <button id='btnGyro' class='on' onclick='toggleGyro()'>Gyro</button>
  <button onclick='zeroGyro()'>Tare</button>
  <button id='btnRec' onclick='toggleRec()'>.WAV</button>
</div>
<canvas id='pad'></canvas>
<div class='bot'>
  <span id='stPc'>PC 48kHz :: Touch Ribbon, Cloned Preset #09, or Chladni Kaoss Pad</span>
  <span id='stGyro'>Tilt 0.0 / 0.0</span>
</div>
<script>
var cv = document.getElementById('pad'), ctx = cv.getContext('2d');
var touches = {}, kaossPt = null, upQueue = [], splitMode = 0;
var gyroOn = true, rawB = 0, rawG = 0, baseB = 0, baseG = 0;
var arpOn = false, recOn = false, inflight = false;
var pcM = 3, pcN = 5, pcLoop = 0;

function resize(){ cv.width = cv.clientWidth; cv.height = cv.clientHeight; }
window.addEventListener('resize', resize); resize();

function ctrl(q){ fetch('/api/control?' + q).catch(function(){}); }
function ctrlLoop(){ fetch('/api/control?loop=rec').then(function(r){return r.json();}).then(function(d){ updLoopBtn(d.loop); }).catch(function(){}); }
function updLoopBtn(st){
  pcLoop = st;
  var b = document.getElementById('btnLoop');
  b.textContent = st===0 ? 'Loop REC' : (st===1 ? 'LOCK LOOP' : (st===2 ? 'OVERDUB' : 'PLAYING'));
  b.className = st===1 ? 'rec' : (st>=2 ? 'loop' : '');
}
function zeroGyro(){ baseB = rawB; baseG = rawG; }
function toggleGyro(){
  if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function')
    DeviceOrientationEvent.requestPermission().catch(function(){});
  gyroOn = !gyroOn; document.getElementById('btnGyro').className = gyroOn ? 'on' : ''; ctrl('gben=' + (gyroOn?'1':'0'));
}
function toggleArp(){ arpOn = !arpOn; document.getElementById('btnArp').className = arpOn ? 'on' : ''; ctrl('arp=toggle'); }
function toggleRec(){ recOn = !recOn; document.getElementById('btnRec').className = recOn ? 'rec' : ''; ctrl('rec=toggle'); }

window.addEventListener('deviceorientation', function(e){
  if (e.beta !== null) rawB = e.beta;
  if (e.gamma !== null) rawG = e.gamma;
}, true);

function handlePtr(id, cx, cy, force, isEnd){
  var r = cv.getBoundingClientRect();
  var x = Math.max(0, Math.min(1, (cx - r.left) / Math.max(1, r.width)));
  var yRaw = Math.max(0, Math.min(1, (cy - r.top) / Math.max(1, r.height)));
  if (isEnd){
    if (kaossPt && kaossPt.id === id) kaossPt = null;
    if (touches[id]){ delete touches[id]; upQueue.push(id); }
    return;
  }
  if (splitMode === 0 && yRaw < 0.42){
    var ky = 1.0 - (yRaw / 0.42);
    kaossPt = { id:id, x:x, y:ky, sx:cx-r.left, sy:cy-r.top };
  } else {
    var ribY = (splitMode === 0) ? (1.0 - (yRaw - 0.42) / 0.58) : (1.0 - yRaw);
    var p = (force && force > 0.05) ? Math.min(1, force) : Math.min(1, 0.74 + ribY * 0.24);
    touches[id] = { id:id, x:x, y:Math.max(0,Math.min(1,ribY)), p:p, sx:cx-r.left, sy:cy-r.top };
  }
}

cv.addEventListener('touchstart', function(ev){ ev.preventDefault(); for(var i=0;i<ev.changedTouches.length;i++){ var t=ev.changedTouches[i]; handlePtr(t.identifier, t.clientX, t.clientY, t.force, false); } flushMpe(); }, {passive:false});
cv.addEventListener('touchmove', function(ev){ ev.preventDefault(); for(var i=0;i<ev.changedTouches.length;i++){ var t=ev.changedTouches[i]; handlePtr(t.identifier, t.clientX, t.clientY, t.force, false); } }, {passive:false});
cv.addEventListener('touchend', function(ev){ ev.preventDefault(); for(var i=0;i<ev.changedTouches.length;i++){ var t=ev.changedTouches[i]; handlePtr(t.identifier, 0, 0, 0, true); } flushMpe(); }, {passive:false});
cv.addEventListener('touchcancel', function(ev){ ev.preventDefault(); for(var i=0;i<ev.changedTouches.length;i++){ var t=ev.changedTouches[i]; handlePtr(t.identifier, 0, 0, 0, true); } flushMpe(); }, {passive:false});

var md=false;
cv.addEventListener('mousedown', function(e){ md=true; handlePtr(99, e.clientX, e.clientY, 0.85, false); flushMpe(); });
window.addEventListener('mousemove', function(e){ if(md) handlePtr(99, e.clientX, e.clientY, 0.85, false); });
window.addEventListener('mouseup', function(){ if(md){ md=false; handlePtr(99,0,0,0,true); flushMpe(); }});

function flushMpe(){
  if (inflight) return;
  var list = [];
  for (var k in touches){ var t = touches[k]; list.push(t.id+':'+t.x.toFixed(4)+':'+t.y.toFixed(4)+':'+t.p.toFixed(3)); }
  var db = rawB - baseB, dg = rawG - baseG;
  document.getElementById('stGyro').textContent = 'Tilt ' + db.toFixed(1) + '/' + dg.toFixed(1) + ' deg';
  if (list.length === 0 && upQueue.length === 0 && !kaossPt && !gyroOn) return;
  var ups = upQueue.splice(0, upQueue.length).join(',');
  var url = '/api/mpe?gb=' + db.toFixed(2) + '&gg=' + dg.toFixed(2) + '&gben=' + (gyroOn?'1':'0');
  if (kaossPt) url += '&kx=' + kaossPt.x.toFixed(3) + '&ky=' + kaossPt.y.toFixed(3);
  if (list.length > 0) url += '&t=' + encodeURIComponent(list.join(','));
  if (ups.length > 0) url += '&up=' + encodeURIComponent(ups);
  inflight = true;
  fetch(url).then(function(r){return r.json();}).then(function(d){
    inflight = false;
    if (d && d.m){
      pcM = d.m; pcN = d.n; updLoopBtn(d.loop);
      if (d.hz > 20) document.getElementById('stPc').textContent = d.hz.toFixed(1)+' Hz ('+d.note+' '+(d.cents>=0?'+':'')+d.cents+'c) | Chladni('+d.m+','+d.n+') | E8#'+d.root;
    }
  }).catch(function(){ inflight = false; });
}
setInterval(flushMpe, 22);

function draw(){
  var w = cv.width, h = cv.height;
  ctx.fillStyle = 'rgba(0,0,0,0.38)'; ctx.fillRect(0, 0, w, h);
  var splitY = (splitMode === 0) ? Math.floor(h * 0.42) : 0;

  if (splitMode === 0){
    ctx.strokeStyle = '#db2777'; ctx.lineWidth = 1.5;
    ctx.strokeRect(2, 2, w - 4, splitY - 4);
    ctx.fillStyle = 'rgba(250,204,21,0.55)';
    var step = 8;
    for (var py = 6; py < splitY - 6; py += step){
      var uy = py / splitY;
      for (var px = 6; px < w - 6; px += step){
        var ux = px / w;
        var eig = Math.cos(pcM*Math.PI*ux)*Math.cos(pcN*Math.PI*uy) - Math.cos(pcN*Math.PI*ux)*Math.cos(pcM*Math.PI*uy);
        if (Math.abs(eig) < 0.14) ctx.fillRect(px, py, 3, 3);
      }
    }
    ctx.fillStyle = '#94a3b8'; ctx.font = '11px Consolas';
    ctx.fillText('2D CHLADNI ('+pcM+','+pcN+') + VOWEL FORMANT KAOSS PAD (X=A-E-I-O-U | Y=E8 SYMPATHY)', 10, 18);
    if (kaossPt){
      ctx.strokeStyle = '#f472b6'; ctx.lineWidth = 2.5;
      ctx.beginPath(); ctx.arc(kaossPt.sx, kaossPt.sy, 22, 0, Math.PI*2); ctx.stroke();
    }
  }

  for (var s = 0; s <= 36; s++){
    var x = (s / 36.0) * w;
    var isOct = (s % 12 === 0), isFifth = (s % 12 === 7);
    ctx.strokeStyle = isOct ? '#facc15' : (isFifth ? '#0284c7' : '#1e293b');
    ctx.lineWidth = isOct ? 2.0 : (isFifth ? 1.3 : 0.7);
    ctx.beginPath(); ctx.moveTo(x, splitY); ctx.lineTo(x, h); ctx.stroke();
  }
  for (var k in touches){
    var t = touches[k];
    ctx.strokeStyle = '#34d399'; ctx.lineWidth = 2.6;
    ctx.beginPath(); ctx.arc(t.sx, t.sy, 28 * t.p + 10, 0, Math.PI * 2); ctx.stroke();
    ctx.fillStyle = 'rgba(56,189,248,0.35)';
    ctx.beginPath(); ctx.arc(t.sx, t.sy, 13, 0, Math.PI * 2); ctx.fill();
  }
  requestAnimationFrame(draw);
}
draw();
</script>
</body>
</html>";
        }

        public static string BeamMpePadToPixels(int port)
        {
            string adb = ResolveAdb();
            List<string> serials = new List<string>();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(adb, "devices");
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1800);
                    string[] lines = (o ?? "").Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = lines[i].Trim();
                        if (t.EndsWith("\tdevice"))
                        {
                            string s = t.Split('\t')[0].Trim();
                            if (s.Length > 0) serials.Add(s);
                        }
                    }
                }
            }
            catch { }

            if (serials.Count == 0)
                return "No ADB Pixel detected — open http://127.0.0.1:" + port + "/mpe or connect Pixel USB/Wi-Fi.";

            string url = "http://127.0.0.1:" + port + "/mpe?v=" + (DateTime.UtcNow.Ticks % 99999);
            for (int i = 0; i < serials.Count; i++)
            {
                RunFast(adb, "-s " + serials[i] + " reverse tcp:" + port + " tcp:" + port, 1600);
                RunFast(adb, "-s " + serials[i] + " shell am start -a android.intent.action.VIEW -d \"" + url + "\"", 2200);
            }
            return "Beamed Split Kaoss + MPE Pad (" + url + ") -> " + serials.Count + " Pixel(s)!";
        }

        private static string ResolveAdb()
        {
            string[] c = new string[]
            {
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe"),
                @"C:\platform-tools\adb.exe",
                @"E:\platform-tools\adb.exe"
            };
            for (int i = 0; i < c.Length; i++) if (File.Exists(c[i])) return c[i];
            return "adb.exe";
        }

        private static void RunFast(string exe, string args, int ms)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                using (Process p = Process.Start(psi)) p.WaitForExit(ms);
            }
            catch { }
        }

        private void CloseMidi()
        {
            for (int i = 0; i < _midiHandles.Count; i++)
            {
                try { midiInStop(_midiHandles[i]); midiInReset(_midiHandles[i]); midiInClose(_midiHandles[i]); } catch { }
            }
            _midiHandles.Clear();
            _midiCallbacks.Clear();
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            CloseMidi();
        }

        public void Dispose() { Stop(); }
    }
}
