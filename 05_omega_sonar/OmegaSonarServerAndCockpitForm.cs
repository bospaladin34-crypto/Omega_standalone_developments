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
using System.Threading;
using System.Windows.Forms;

namespace OmegaSonarStudio
{
    public sealed class OmegaSonarHttpServer : IDisposable
    {
        public const int DefaultPort = 19783;
        private readonly OmegaSonarTransceiverEngine _sonar;
        private readonly OmegaSonarWandFusionEngine _wand;
        private TcpListener _listener;
        private volatile bool _running;

        public OmegaSonarHttpServer(OmegaSonarTransceiverEngine sonar, OmegaSonarWandFusionEngine wand)
        {
            _sonar = sonar;
            _wand = wand;
        }

        public void Start()
        {
            if (_running) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, DefaultPort);
                _listener.Start();
                _running = true;
                Thread th = new Thread(AcceptLoop);
                th.IsBackground = true;
                th.Start();
            }
            catch { }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient tc = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, tc);
                }
                catch { if (!_running) break; }
            }
        }

        private void HandleClient(object state)
        {
            using (TcpClient tc = (TcpClient)state)
            {
                try
                {
                    tc.NoDelay = true;
                    tc.ReceiveTimeout = 2000;
                    tc.SendTimeout = 2000;
                    NetworkStream ns = tc.GetStream();
                    byte[] buf = new byte[8192];
                    int n = ns.Read(buf, 0, buf.Length);
                    if (n <= 0) return;
                    string req = Encoding.UTF8.GetString(buf, 0, n);
                    string first = req.Split('\r', '\n')[0];
                    string[] parts = first.Split(' ');
                    string method = parts.Length > 0 ? parts[0] : "GET";
                    string path = parts.Length > 1 ? parts[1] : "/";
                    int qIdx = path.IndexOf('?');
                    if (qIdx >= 0) path = path.Substring(0, qIdx);
                    int bodyIdx = req.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string body = (bodyIdx >= 0) ? req.Substring(bodyIdx + 4) : "";

                    if (path == "/api/imu" && method == "POST")
                    {
                        _wand.IngestEulerOrGyroJson(body, "Pixel 10 OLED Wand (:19783)");
                        WriteHttp(ns, "200 OK", "application/json", "{\"ok\":true}");
                    }
                    else if (path == "/api/ping")
                    {
                        string msg = _sonar.EmitOneShot19kHzGolayPing();
                        WriteHttp(ns, "200 OK", "application/json", "{\"ok\":true,\"ping\":\"" + JsonEsc(msg) + "\"}");
                    }
                    else if (path == "/api/gestures")
                    {
                        List<string> gs = _sonar.GetRecentGestures();
                        StringBuilder sb = new StringBuilder("[");
                        for (int i = 0; i < gs.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append("\"").Append(JsonEsc(gs[i])).Append("\"");
                        }
                        sb.Append("]");
                        WriteHttp(ns, "200 OK", "application/json", "{\"ok\":true,\"gestures\":" + sb.ToString() + "}");
                    }
                    else if (path == "/wand")
                    {
                        WriteHttp(ns, "200 OK", "text/html; charset=utf-8", BuildMobileWandHtml());
                    }
                    else
                    {
                        SonarRadarSnapshot s = _sonar.GetSnapshotCopy();
                        float[] trDummy;
                        Wand6DofState w = _wand.StepAndFuseWithSonar(0.0, s.TargetRangeM, out trDummy);
                        string js = string.Format(
                            CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"station\":\"05_omega_sonar_v1.1.0_GM\",\"port\":19783,\"golay_chips\":{0},\"sidelobe_max\":{1},\"range_m\":{2:0.000},\"velocity_mps\":{3:0.000},\"azimuth_deg\":{4:0.00},\"snr_db\":{5:0.0},\"gesture\":\"{6}\",\"gesture_count\":{7},\"hw_audio\":{8},\"wand\":{{\"source\":\"{9}\",\"qw\":{10:0.0000},\"qx\":{11:0.0000},\"qy\":{12:0.0000},\"qz\":{13:0.0000},\"tip_m\":[{14:0.000},{15:0.000},{16:0.000}],\"virtio_gpu\":\"{17}\"}}}}",
                            s.GolayLength, s.OutOfPhaseSidelobeMax, s.TargetRangeM, s.RadialVelocityMps, s.AzimuthDeg,
                            s.PeakSnrDb, JsonEsc(s.ActiveGesture), s.GestureCount, s.HardwareAudioLive ? "true" : "false",
                            JsonEsc(w.SourceLabel), w.Q.W, w.Q.X, w.Q.Y, w.Q.Z, w.TipX, w.TipY, w.TipZ, JsonEsc(w.LastVirtioGpuReceipt));
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", js);
                    }
                }
                catch { }
            }
        }

        private static string BuildMobileWandHtml()
        {
            return "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no'>" +
                "<title>OmegaSonar v1.1.0 :: Pixel 10 6-DoF Wand</title>" +
                "<style>body{background:#060a14;color:#e2e8f0;font-family:Consolas,monospace;margin:0;padding:16px;text-align:center}" +
                ".card{background:#0f172a;border:1px solid #34d399;border-radius:12px;padding:16px;margin-bottom:14px}" +
                "button{background:#059669;color:#fff;border:1px solid #34d399;border-radius:10px;padding:14px 18px;font-size:16px;font-weight:bold;margin:6px;width:92%}" +
                "#pad{height:180px;background:#111c33;border:2px dashed #38bdf8;border-radius:12px;display:flex;align-items:center;justify-content:center;font-size:18px;color:#38bdf8;user-select:none}</style></head><body>" +
                "<div class='card'><h3 style='color:#34d399;margin:0 0 8px 0'>OMEGASONAR v1.1.0 :: PIXEL 10 6-DoF WAND</h3>" +
                "<div id='st'>Tap Arm IMU + 19kHz Golay Chirp below</div></div>" +
                "<button onclick='armWand()'>1. Arm Smooth 30Hz 6-DoF IMU Stream</button>" +
                "<button style='background:#0284c7' onclick='chirp19k()'>2. Emit Tapered 19.000 kHz Golay Pair</button>" +
                "<div id='pad' ontouchstart='trig(true)' ontouchend='trig(false)'>HOLD THUMB = WAND BEAM TRIGGER</div>" +
                "<script>var tr=false,ac=null,busy=false,lastMs=0,armed=false;function trig(v){tr=v;}" +
                "function armWand(){if(armed)return;armed=true;if(typeof DeviceOrientationEvent!=='undefined'&&typeof DeviceOrientationEvent.requestPermission==='function'){DeviceOrientationEvent.requestPermission();}" +
                "window.addEventListener('deviceorientation',function(e){var now=performance.now();if(busy||(now-lastMs)<30)return;lastMs=now;busy=true;" +
                "var a=e.alpha||0,b=e.beta||0,g=e.gamma||0;document.getElementById('st').innerText='Yaw='+a.toFixed(1)+' Pitch='+b.toFixed(1)+' Roll='+g.toFixed(1);" +
                "fetch('/api/imu',{method:'POST',body:JSON.stringify({alpha:a,beta:b,gamma:g,trig:tr})}).then(function(){busy=false;}).catch(function(){busy=false;});});}" +
                "function chirp19k(){if(!ac)ac=new(window.AudioContext||window.webkitAudioContext)({sampleRate:48000});" +
                "var N=2400,b=ac.createBuffer(1,N,48000),d=b.getChannelData(0);" +
                "for(var i=0;i<N;i++){var env=(i<64)?(i/64.0):((i>N-64)?((N-i)/64.0):1.0);var s=(Math.floor(i/12)&1)?1:-1;d[i]=0.32*env*s*Math.cos(2*Math.PI*19000*i/48000);}" +
                "var src=ac.createBufferSource();src.buffer=b;src.connect(ac.destination);src.start();fetch('/api/ping');}</script></body></html>";
        }

        private static string JsonEsc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private static void WriteHttp(NetworkStream ns, string status, string ctype, string body)
        {
            byte[] b = Encoding.UTF8.GetBytes(body);
            string hdr = "HTTP/1.1 " + status + "\r\nContent-Type: " + ctype + "\r\nContent-Length: " + b.Length + "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            ns.Write(b, 0, b.Length);
            ns.Flush();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }
    }

    public sealed class ViewportPanel : Panel
    {
        public ViewportPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        }
    }

    public sealed class OmegaSonarCockpitForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        private readonly string _baseDir;
        private readonly OmegaSonarTransceiverEngine _sonar = new OmegaSonarTransceiverEngine();
        private readonly OmegaSonarWandFusionEngine _wand = new OmegaSonarWandFusionEngine();
        private readonly OmegaSonarDirectComputeGpu _gpu = new OmegaSonarDirectComputeGpu();
        private readonly OmegaSonarHttpServer _http;

        private readonly Label _lblHeader, _lblMetrics, _lblWand;
        private readonly ViewportPanel _vp;
        private readonly RichTextBox _rtbLog;
        private readonly ComboBox _cmbMode, _cmbGolayN, _cmbView;
        private readonly System.Windows.Forms.Timer _tmr;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private SonarRadarSnapshot _lastSnap = new SonarRadarSnapshot();
        private Wand6DofState _lastWand = new Wand6DofState();
        private float[] _lastTrail = new float[96 * 3];
        private string _prevMetText = "", _prevWandText = "";

        public OmegaSonarCockpitForm(string baseDir)
        {
            _baseDir = baseDir;
            _http = new OmegaSonarHttpServer(_sonar, _wand);
            _http.Start();

            AutoScaleMode = AutoScaleMode.None;
            Text = "ACT-Omega Lab :: OmegaSonar v1.1.0 GM (:19783) -- 19 kHz Ultrasonic Golay Complementary Radar & Pixel 10 6-DoF Spatial Wand";
            ClientSize = new Size(1188, 748);
            MinimumSize = new Size(1040, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(8, 12, 21);
            ForeColor = Color.White;

            try
            {
                string ico = Path.Combine(Directory.GetParent(baseDir.TrimEnd('\\', '/')).FullName, "icons", "omega_sonar.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            Panel topHud = new Panel();
            topHud.Dock = DockStyle.Top;
            topHud.Height = 72;
            topHud.BackColor = Color.FromArgb(12, 19, 35);

            _lblHeader = new Label();
            _lblHeader.UseMnemonic = false;
            _lblHeader.SetBounds(12, 6, 1160, 19);
            _lblHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblHeader.Font = new Font("Consolas", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblHeader.ForeColor = Color.FromArgb(52, 211, 153);
            _lblHeader.Text = "OMEGASONAR v1.1.0 GM (:19783) :: 19.000 kHz Golay Complementary Sequence Array (R_A+R_B = 2N*delta[0], Zero Sidelobes)";

            _lblMetrics = new Label();
            _lblMetrics.UseMnemonic = false;
            _lblMetrics.SetBounds(12, 27, 1160, 19);
            _lblMetrics.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblMetrics.Font = new Font("Consolas", 11.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            _lblMetrics.ForeColor = Color.FromArgb(56, 189, 248);
            _lblMetrics.Text = "Probing 19 kHz Golay Range-Doppler & 3-Node Phased Array...";

            _lblWand = new Label();
            _lblWand.UseMnemonic = false;
            _lblWand.SetBounds(12, 48, 1160, 19);
            _lblWand.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblWand.Font = new Font("Consolas", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            _lblWand.ForeColor = Color.FromArgb(250, 204, 21);
            _lblWand.Text = "6-DoF Wand S^3 Quaternion :: Waiting for fusion tick...";

            topHud.Controls.AddRange(new Control[] { _lblHeader, _lblMetrics, _lblWand });

            Panel ctrlBar = new Panel();
            ctrlBar.Dock = DockStyle.Top;
            ctrlBar.Height = 40;
            ctrlBar.BackColor = Color.FromArgb(15, 24, 44);

            int cx = 10;
            _cmbMode = MakeCombo(new string[] { "Sim: 3-Node Phased Array", "Live 48kHz WinMM 19kHz", "Hybrid: Audio + Sim" }, 0, cx, 172);
            cx += 178;
            _cmbMode.SelectedIndexChanged += delegate
            {
                _sonar.OperatingMode = _cmbMode.SelectedIndex;
                _sonar.SetLiveAudioEnabled(_cmbMode.SelectedIndex >= 1);
                LogLine("Switched Sonar Mode -> " + _cmbMode.SelectedItem + " (" + _sonar.AudioStatus + ")", Color.FromArgb(56, 189, 248));
            };

            _cmbGolayN = MakeCombo(new string[] { "Golay N=32", "Golay N=64", "Golay N=128" }, 1, cx, 102);
            cx += 108;
            _cmbGolayN.SelectedIndexChanged += delegate
            {
                _sonar.GolayStages = 5 + _cmbGolayN.SelectedIndex;
                LogLine("Selected Complementary Pair Length N=" + (1 << _sonar.GolayStages) + " chips (0 sidelobes).", Color.FromArgb(52, 211, 153));
            };

            _cmbView = MakeCombo(new string[] { "Split: R-D | 3D Wand", "Full Range-Doppler", "Full 3D Wand + Fan" }, 0, cx, 146);
            cx += 152;
            _cmbView.SelectedIndexChanged += delegate { _gpu.ViewMode = _cmbView.SelectedIndex; _vp.Invalidate(); };

            Button bPing = MakeBtn("1. Ping 19kHz", cx, 104, Color.FromArgb(5, 150, 105));
            cx += 110;
            bPing.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string res = _sonar.EmitOneShot19kHzGolayPing();
                    LogLine(res, Color.FromArgb(52, 211, 153));
                });
            };

            Button bBeamWand = MakeBtn("2. Beam Pixel Wand", cx, 132, Color.FromArgb(2, 132, 199));
            cx += 138;
            bBeamWand.Click += delegate { BeamWandToPixel10Async(); };

            Button bAdbImu = MakeBtn("3. Poll Pixel IMU", cx, 118, Color.FromArgb(109, 40, 217));
            cx += 124;
            bAdbImu.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string r = _wand.PollPixel10AdbImuOnce();
                    LogLine(r, Color.FromArgb(192, 132, 252));
                });
            };

            Button bVirtio = MakeBtn("4. Offload @deb :19789", cx, 148, Color.FromArgb(4, 120, 87));
            cx += 154;
            bVirtio.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string r = _wand.OffloadBatchToAvfVirtioGpu19789();
                    LogLine(r, Color.FromArgb(52, 211, 153));
                });
            };

            Button bAudit = MakeBtn("5. 8-Vec Audit", cx, 104, Color.FromArgb(180, 83, 9));
            cx += 110;
            bAudit.Click += delegate { RunInlineAuditAsync(); };

            Button bExport = MakeBtn("6. Save PNG", cx, 94, Color.FromArgb(30, 58, 138));
            bExport.Click += delegate { ExportSnapshotPng(); };

            ctrlBar.Controls.AddRange(new Control[] { _cmbMode, _cmbGolayN, _cmbView, bPing, bBeamWand, bAdbImu, bVirtio, bAudit, bExport });

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            _vp = new ViewportPanel();
            _vp.Dock = DockStyle.Fill;
            _vp.BackColor = Color.FromArgb(6, 9, 17);
            _vp.Paint += delegate(object s, PaintEventArgs pe)
            {
                if (!_gpu.IsGpuActive)
                    OmegaSonarDirectComputeGpu.RenderSoftwareFallback(pe.Graphics, _vp.Width, _vp.Height, _lastSnap, _lastWand);
            };
            _vp.Resize += delegate
            {
                if (_gpu.IsGpuActive && _vp.ClientSize.Width >= 32 && _vp.ClientSize.Height >= 32)
                    _gpu.ResizeSwapChain(_vp.ClientSize.Width, _vp.ClientSize.Height);
                else
                    _vp.Invalidate();
            };

            MouseEventHandler steerMouse = delegate(object s, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Left && _vp.Width > 32 && _vp.Height > 32)
                {
                    _sonar.ManualTargetOverride = true;
                    float nx = Math.Max(0f, Math.Min(1f, me.X / (float)_vp.Width));
                    float ny = Math.Max(0f, Math.Min(1f, me.Y / (float)_vp.Height));
                    int vm = _gpu.ViewMode;
                    bool onLeft = (vm == 1) || (vm == 0 && nx < 0.495f);
                    if (onLeft)
                    {
                        float lx = (vm == 0) ? (nx / 0.495f) : nx;
                        float ry = 1.0f - Math.Min(1.0f, ny / 0.64f);
                        _sonar.ManualRangeM = 0.08f + ry * 1.50f;
                        _sonar.ManualVelocityMps = (lx - 0.5f) * 2.40f;
                    }
                    else
                    {
                        float rx = (vm == 0) ? ((nx - 0.50f) / 0.50f) : nx;
                        float px = (rx - 0.5f) * 2.0f;
                        float py = (0.5f - ny) * 2.0f - (-0.80f);
                        if (py < 0.05f) py = 0.05f;
                        float aspect = (vm == 0) ? (_vp.Width * 0.5f / Math.Max(1f, _vp.Height)) : (_vp.Width / Math.Max(1f, _vp.Height));
                        float dx = px * aspect;
                        float rPol = (float)Math.Sqrt(dx * dx + py * py);
                        float newR = Math.Max(0.08f, Math.Min(1.60f, (rPol / 1.50f) * 1.65f));
                        float angDeg = (float)(Math.Atan2(dx, py) * 180.0 / Math.PI);
                        _sonar.ManualVelocityMps = Math.Max(-1.2f, Math.Min(1.2f, (newR - _sonar.ManualRangeM) * 14.0f));
                        _sonar.ManualRangeM = newR;
                        _sonar.ManualAzimuthDeg = Math.Max(-58.0f, Math.Min(58.0f, angDeg));
                    }
                }
            };
            _vp.MouseDown += steerMouse;
            _vp.MouseMove += steerMouse;
            _vp.MouseUp += delegate { _sonar.ManualTargetOverride = false; };

            _rtbLog = new RichTextBox();
            _rtbLog.Dock = DockStyle.Fill;
            _rtbLog.ReadOnly = true;
            _rtbLog.BorderStyle = BorderStyle.None;
            _rtbLog.BackColor = Color.FromArgb(6, 9, 17);
            _rtbLog.ForeColor = Color.FromArgb(226, 232, 240);
            _rtbLog.Font = new Font("Consolas", 11.8f, FontStyle.Regular, GraphicsUnit.Pixel);

            split.Panel1.Controls.Add(_vp);
            split.Panel2.Controls.Add(_rtbLog);

            Controls.Add(split);
            Controls.Add(ctrlBar);
            Controls.Add(topHud);

            _tmr = new System.Windows.Forms.Timer();
            _tmr.Interval = 16;
            _tmr.Tick += delegate { StepFrame(); };

            Shown += delegate
            {
                try { split.SplitterDistance = Math.Max(320, ClientSize.Height - 256); } catch { }
                bool gpuOk = _gpu.Initialize(_vp.Handle, _vp.ClientSize.Width, _vp.ClientSize.Height);
                LogLine("OmegaSonar v1.1.0 GM LIVE on http://127.0.0.1:19783 (Wand UI: http://127.0.0.1:19783/wand) | Renderer: " + _gpu.BackendLabel,
                    gpuOk ? Color.FromArgb(52, 211, 153) : Color.FromArgb(250, 204, 21));
                LogLine("Upgrades Active: Integrated Phase-Continuous Range-Doppler + Gapless 3-Buffer 48kHz WinMM + Resizable SwapChain + Pane-Aware Drag!",
                    Color.FromArgb(125, 211, 252));
                _tmr.Start();
            };

            FormClosing += delegate
            {
                _tmr.Stop();
                _http.Dispose();
                _sonar.Dispose();
                _gpu.Dispose();
            };
        }

        private void StepFrame()
        {
            if (WindowState == FormWindowState.Minimized) return;
            _sonar.StepSimulationTick(0.016, _lastWand.YawDeg);
            _lastSnap = _sonar.GetSnapshotCopy();
            _lastWand = _wand.StepAndFuseWithSonar(0.016, _lastSnap.TargetRangeM, out _lastTrail);

            string mTxt = string.Format(
                CultureInfo.InvariantCulture,
                "Golay N={0} (Sidelobe={1}, PSLR=INF dB) | Range={2:0.000} m | Doppler={3:+0.00;-0.00} m/s | Azimuth={4:+0.0;-0.0} deg | Gesture={5} (#{6})",
                _lastSnap.GolayLength, _lastSnap.OutOfPhaseSidelobeMax, _lastSnap.TargetRangeM,
                _lastSnap.RadialVelocityMps, _lastSnap.AzimuthDeg, _lastSnap.ActiveGesture, _lastSnap.GestureCount);
            if (mTxt != _prevMetText) { _prevMetText = mTxt; _lblMetrics.Text = mTxt; }

            string wTxt = string.Format(
                CultureInfo.InvariantCulture,
                "6-DoF Wand [{0}] :: q=({1:0.00},{2:+0.00;-0.00},{3:+0.00;-0.00},{4:+0.00;-0.00}) | YPR=({5:+0.0;-0.0},{6:+0.0;-0.0},{7:+0.0;-0.0}) | Tip=({8:+0.00;-0.00},{9:+0.00;-0.00},{10:+0.00;-0.00})m | {11}",
                _lastWand.SourceLabel, _lastWand.Q.W, _lastWand.Q.X, _lastWand.Q.Y, _lastWand.Q.Z,
                _lastWand.YawDeg, _lastWand.PitchDeg, _lastWand.RollDeg,
                _lastWand.TipX, _lastWand.TipY, _lastWand.TipZ, _lastWand.LastVirtioGpuReceipt);
            if (wTxt != _prevWandText) { _prevWandText = wTxt; _lblWand.Text = wTxt; }

            if (_gpu.IsGpuActive)
                _gpu.RenderFrame((float)_clock.Elapsed.TotalSeconds, _lastSnap, _lastWand, _lastTrail, null, _vp.ClientSize.Width, _vp.ClientSize.Height);
            else
                _vp.Invalidate();
        }

        private void BeamWandToPixel10Async()
        {
            LogLine("Arming ADB reverse tcp:19783 tcp:19783 & opening 6-DoF OLED Spatial Wand on Pixel 10...", Color.FromArgb(56, 189, 248));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string adb = OmegaSonarWandFusionEngine.ResolveAdbPath();
                    RunSimpleProc(adb, "reverse tcp:19783 tcp:19783", 2500);
                    string outLaunch = RunSimpleProc(adb, "shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19783/wand\"", 3500);
                    LogLine("[Pixel 10 Wand Beamed] http://127.0.0.1:19783/wand -> " + outLaunch.Trim(), Color.FromArgb(52, 211, 153));
                }
                catch (Exception ex) { LogLine("Beam error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
            });
        }

        private void RunInlineAuditAsync()
        {
            LogLine("Running live 8-Vector Ultrasonic Golay, Phased-Array TDOA & S^3 Madgwick Stress Audit...", Color.FromArgb(250, 204, 21));
            ThreadPool.QueueUserWorkItem(delegate
            {
                int rc = OmegaSonarRedTeamStressSuite.RunAllVectors(_baseDir);
                string txtFile = Path.Combine(_baseDir, "exports", "omega_sonar_stress_console.txt");
                string txt = File.Exists(txtFile) ? File.ReadAllText(txtFile, Encoding.UTF8).Trim() : ("Exit=" + rc);
                LogLine(txt, rc == 0 ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113));
            });
        }

        private void ExportSnapshotPng()
        {
            try
            {
                string dir = Path.Combine(_baseDir, "exports");
                Directory.CreateDirectory(dir);
                string fn = Path.Combine(dir, "omega_sonar_frame_" + DateTime.Now.ToString("HHmmss") + ".png");
                OmegaSonarDirectComputeGpu.SaveDiagnosticSnapshotPng(fn, _lastSnap, _lastWand);
                LogLine("Saved radar & 6-DoF wand snapshot -> " + fn, Color.FromArgb(52, 211, 153));
            }
            catch (Exception ex) { LogLine("Export error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
        }

        private static string RunSimpleProc(string exe, string args, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(timeoutMs);
                return o ?? "";
            }
        }

        private static ComboBox MakeCombo(string[] items, int sel, int x, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(x, 8, w, 24);
            c.BackColor = Color.FromArgb(6, 9, 17);
            c.ForeColor = Color.FromArgb(56, 189, 248);
            c.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel);
            c.Items.AddRange(items);
            c.SelectedIndex = sel;
            return c;
        }

        private static Button MakeBtn(string txt, int x, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = txt;
            b.SetBounds(x, 5, w, 29);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }

        private void LogLine(string msg, Color fg)
        {
            if (IsDisposed) return;
            Action a = delegate
            {
                if (_rtbLog.Lines.Length > 200)
                    _rtbLog.Clear();
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.SelectionLength = 0;
                _rtbLog.SelectionColor = fg;
                _rtbLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
                _rtbLog.ScrollToCaret();
            };
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } } else a();
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--stress-test")
                    return OmegaSonarRedTeamStressSuite.RunAllVectors(baseDir);
            }
            try { SetProcessDPIAware(); } catch { }
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); Application.ThreadException += delegate { }; AppDomain.CurrentDomain.UnhandledException += delegate { }; } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaSonarCockpitForm(baseDir));
            return 0;
        }
    }
}
