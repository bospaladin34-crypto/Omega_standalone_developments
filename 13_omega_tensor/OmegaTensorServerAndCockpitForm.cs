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
using OmegaPocketSiliconFusion;

namespace OmegaTensorStudio
{
    public sealed class OmegaTensorHttpServer : IDisposable
    {
        private readonly int _port;
        private readonly OmegaTensorClusterCoordinator _coord;
        private readonly OmegaTensorDirectComputeGpu _gpu;
        private readonly Action<string> _logCb;
        private TcpListener _listener;
        private volatile bool _running;

        public OmegaTensorHttpServer(int port, OmegaTensorClusterCoordinator coord, OmegaTensorDirectComputeGpu gpu, Action<string> logCb)
        {
            _port = port;
            _coord = coord;
            _gpu = gpu;
            _logCb = logCb;
        }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _running = true;
                Thread th = new Thread(AcceptLoop);
                th.IsBackground = true;
                th.Name = "OmegaTensorHttp19791";
                th.Start();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleClient(object state)
        {
            TcpClient client = state as TcpClient;
            if (client == null) return;
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 4000;
                    client.SendTimeout = 4000;
                    NetworkStream ns = client.GetStream();
                    byte[] buf = new byte[8192];
                    int n = ns.Read(buf, 0, buf.Length);
                    if (n <= 0) return;

                    string raw = Encoding.UTF8.GetString(buf, 0, n);
                    int eol = raw.IndexOf('\n');
                    string first = (eol > 0 ? raw.Substring(0, eol) : raw).Trim();
                    string[] parts = first.Split(' ');
                    string method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
                    string fullUrl = parts.Length > 1 ? parts[1] : "/";
                    int qPos = fullUrl.IndexOf('?');
                    string path = qPos >= 0 ? fullUrl.Substring(0, qPos) : fullUrl;
                    string query = qPos >= 0 ? fullUrl.Substring(qPos + 1) : "";

                    int bodyIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string reqBody = bodyIdx >= 0 ? raw.Substring(bodyIdx + 4).Trim() : "";

                    if (path == "/v1/models")
                    {
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", "{\"object\":\"list\",\"data\":[{\"id\":\"omega-dual-pixel-tensor-v321\",\"object\":\"model\",\"owned_by\":\"omega-standalone-labs\"}]}");
                        return;
                    }
                    if (path == "/v1/chat/completions")
                    {
                        string promptV1 = string.IsNullOrEmpty(reqBody) ? "Hello OmegaTensor 7-Engine Cluster" : reqBody;
                        bool isOmshReq = (promptV1.IndexOf("EvalCustomOmsh", StringComparison.OrdinalIgnoreCase) >= 0) || (promptV1.IndexOf("omsh", StringComparison.OrdinalIgnoreCase) >= 0); string summaryV1 = isOmshReq ? _coord.SynthesizeOmshSnippet((promptV1.GetHashCode() & 0x7FFFFFFF) % 8) : _coord.ExecuteLiveHeterogeneousSplitBurst(_gpu, promptV1, true);
                        string oaJson = "{\"id\":\"chatcmpl-omega\",\"object\":\"chat.completion\",\"model\":\"omega-dual-pixel-tensor-v321\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"" + JsonEsc(summaryV1) + "\"},\"finish_reason\":\"stop\"}]}";
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", oaJson);
                        return;
                    }
                    if (path == "/api/health" || path == "/api/status")
                    {
                        ClusterSplitSnapshot s = _coord.GetSnapshotCopy();
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", FormatStatusJson(s));
                        return;
                    }
                    if (path == "/api/dual_pixel")
                    {
                        string heal = _coord.TryAutoRecoverDualPixelDaemons(true, true);
                        string hw = OmegaPocketCore.ProbeLiveHardwareBridgesSummary();
                        ClusterSplitSnapshot s = _coord.GetSnapshotCopy();
                        string js = "{\"ok\":true,\"heal\":\"" + JsonEsc(heal) + "\",\"xray\":\"" + JsonEsc(hw) + "\",\"status\":" + FormatStatusJson(s) + "}";
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", js);
                        return;
                    }
                    if (path == "/api/omsh_assist")
                    {
                        string mStr = ExtractQueryParam(query, "mode");
                        int modeVal = 0;
                        int.TryParse(mStr, out modeVal);
                        string code = _coord.SynthesizeOmshSnippet(modeVal);
                        ClusterSplitSnapshot s = _coord.GetSnapshotCopy();
                        string js = "{\"ok\":true,\"omsh\":\"" + JsonEsc(code) + "\",\"cluster_tops\":" + s.TotalOnlineTops.ToString("0.000", CultureInfo.InvariantCulture) + "}";
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", js);
                        return;
                    }
                    if (path == "/api/dispatch" || path == "/api/infer")
                    {
                        string prompt = ExtractQueryParam(query, "prompt");
                        if (string.IsNullOrEmpty(prompt) && !string.IsNullOrEmpty(reqBody)) prompt = reqBody;
                        if (string.IsNullOrEmpty(prompt)) prompt = "7-Engine Dual-Pixel Split-Inference Burst";
                        bool useAi = query.IndexOf("ai=1", StringComparison.OrdinalIgnoreCase) >= 0 || method == "POST";
                        string summary = _coord.ExecuteLiveHeterogeneousSplitBurst(_gpu, prompt, useAi);
                        if (_logCb != null) _logCb(summary);
                        ClusterSplitSnapshot s = _coord.GetSnapshotCopy();
                        string js = "{\"ok\":true,\"report\":\"" + JsonEsc(summary) + "\",\"status\":" + FormatStatusJson(s) + "}";
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", js);
                        return;
                    }
                    if (path == "/api/layers")
                    {
                        string lvStr = ExtractQueryParam(query, "n");
                        int lv;
                        if (int.TryParse(lvStr, out lv)) _coord.SetTransformerLayerBudget(lv);
                        ClusterSplitSnapshot s = _coord.GetSnapshotCopy();
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", FormatStatusJson(s));
                        return;
                    }

                    WriteHttp(ns, "200 OK", "text/html; charset=utf-8", BuildMobileDeckHtml());
                }
                catch { }
            }
        }

        private static string ExtractQueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            string[] kvs = query.Split('&');
            string prefix = key + "=";
            for (int i = 0; i < kvs.Length; i++)
            {
                if (kvs[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kvs[i].Substring(prefix.Length).Replace('+', ' '));
            }
            return "";
        }

        private static string FormatStatusJson(ClusterSplitSnapshot s)
        {
            StringBuilder sb = new StringBuilder(1536);
            sb.Append("{\"ok\":true,\"service\":\"OmegaTensor v1.2.0 GM\",\"port\":19791,");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "\"total_online_gops\":{0:0.00},\"total_online_tops\":{1:0.000},\"layers\":{2},\"speedup_vs_rtx\":{3:0.00},\"last_step_ms\":{4:0.0},\"spec_tok_s\":{5:0.0},\"spec_accept_pct\":{6:0.0},\"e8_sqnr_db\":{7:0.00},\"golay_frames\":{8},\"golay_healed\":{9},\"sheaf_h1_zero\":{10},\"sheaf_summary\":\"{11}\",\"active_model\":\"{12}\",\"android_build\":\"{13}\",\"pixel10_live\":{14},\"pixel8_live\":{15},\"dispatches\":{16},\"engines\":[",
                s.TotalOnlineGops, s.TotalOnlineTops, s.TotalTransformerLayers, s.SpeedupVsRtxOnly,
                s.LastSplitStepMs, s.SpeculativeTokPerSec, s.SpeculativeAcceptRate * 100.0,
                s.E8MeanSqnrDb, s.GolayFramesVerified, s.GolayBitsHealed,
                s.SheafH1Zero ? "true" : "false", JsonEsc(s.SheafSummary), JsonEsc(s.ActiveLocalModel),
                JsonEsc(s.AndroidBuildBanner), s.Pixel10Live ? "true" : "false", s.Pixel8Live ? "true" : "false",
                s.DispatchesCompleted);
            for (int i = 0; i < s.Engines.Length; i++)
            {
                SiliconEngineTelemetry e = s.Engines[i];
                if (i > 0) sb.Append(",");
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"index\":{0},\"name\":\"{1}\",\"online\":{2},\"gops\":{3:0.00},\"layers\":{4},\"share_pct\":{5:0.0},\"detail\":\"{6}\"}}",
                    e.Index, JsonEsc(e.Name), e.Online ? "true" : "false", e.MeasuredGops, e.AssignedLayers, e.SharePercent, JsonEsc(e.LastDetail));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string BuildMobileDeckHtml()
        {
            return "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
                "<title>OmegaTensor :19791 7-Engine Dual-Pixel OLED Deck</title>" +
                "<style>body{margin:0;padding:14px;background:#070b14;color:#e2e8f0;font-family:Consolas,monospace}" +
                "h2{margin:0 0 8px;color:#38bdf8;font-size:17px}.kpi{background:#0f172a;border:1px solid #1e293b;border-radius:8px;padding:10px;margin-bottom:10px}" +
                ".bar{height:16px;background:#1e293b;border-radius:4px;overflow:hidden;margin:3px 0 7px}.fill{height:100%;background:linear-gradient(90deg,#10b981,#38bdf8)}" +
                "button{background:#0284c7;color:#fff;border:1px solid #38bdf8;border-radius:6px;padding:9px 11px;font-weight:700;margin:3px;cursor:pointer}" +
                "input{width:96%;padding:9px;background:#090d16;color:#fff;border:1px solid #334155;border-radius:6px;margin-bottom:8px}</style></head><body>" +
                "<h2>OMEGATENSOR v1.2.0 GM (:19791) :: 7-ENGINE DUAL-PIXEL SPLIT</h2>" +
                "<div class='kpi' id='top'>Connecting to 7-engine cluster...</div>" +
                "<input id='pr' value='Split 32 SH4Z+E8 layers across Host RTX + Pixel 10 (:19788/:19789) + Pixel 8 (:19792/:19793)'/>" +
                "<div><button onclick='fire(0)'>1. Fire 7-Engine Split</button><button onclick='fire(1)' style='background:#7c3aed'>2. Split + Local AI</button>" +
                "<button onclick='setL(32)'>32L</button><button onclick='setL(48)'>48L</button><button onclick='setL(64)'>64L</button></div>" +
                "<div class='kpi' id='lanes' style='margin-top:10px'></div>" +
                "<pre class='kpi' id='log' style='white-space:pre-wrap;font-size:12px;color:#a7f3d0'>Ready.</pre>" +
                "<script>async function poll(){try{let r=await fetch('/api/status');let j=await r.json();" +
                "document.getElementById('top').innerHTML='<b>Cluster: '+j.total_online_tops.toFixed(3)+' TOPS/s</b> | Speedup: '+j.speedup_vs_rtx.toFixed(2)+'x | P10:'+j.pixel10_live+' P8:'+j.pixel8_live+'<br>'+j.sheaf_summary+' | '+j.android_build;" +
                "let h='';for(let e of j.engines){let w=Math.min(100,(e.gops/52));h+='<div>'+(e.online?'[LIVE] ':'[IDLE] ')+e.name+' -> '+e.layers+'L ('+e.gops.toFixed(1)+' GOPS/s)</div><div class=\"bar\"><div class=\"fill\" style=\"width:'+w+'%\"></div></div>';}" +
                "document.getElementById('lanes').innerHTML=h;}catch(x){}}" +
                "async function fire(ai){let p=encodeURIComponent(document.getElementById('pr').value);let r=await fetch('/api/dispatch?ai='+ai+'&prompt='+p);let j=await r.json();document.getElementById('log').textContent=j.report;poll();}" +
                "async function setL(n){await fetch('/api/layers?n='+n);poll();}setInterval(poll,1200);poll();</script></body></html>";
        }

        private static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static void WriteHttp(NetworkStream ns, string code, string ctype, string body)
        {
            byte[] b = Encoding.UTF8.GetBytes(body ?? "");
            string hdr = "HTTP/1.1 " + code + "\r\nContent-Type: " + ctype + "\r\nContent-Length: " + b.Length +
                "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
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

    public sealed class BufferedViewportPanel : Panel
    {
        public BufferedViewportPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = false;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    public sealed class OmegaTensorCockpitForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        private readonly string _baseDir;
        private readonly OmegaTensorClusterCoordinator _coord;
        private readonly OmegaTensorDirectComputeGpu _gpu;
        private readonly OmegaTensorHttpServer _http;
        private readonly Stopwatch _animClock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer _renderTimer;
        private readonly System.Windows.Forms.Timer _pollTimer;

        private readonly Label _lblKpiTop;
        private readonly Label _lblKpiSub;
        private readonly ComboBox _cmbLayers;
        private readonly ComboBox _cmbView;
        private readonly Button _btnAuto;
        private readonly TextBox _txtPrompt;
        private readonly BufferedViewportPanel _vpPanel;
        private readonly DataGridView _gridEngines;
        private readonly RichTextBox _rtbLog;

        private volatile bool _autoStream = false;
        private volatile bool _busyBurst = false;
        private int _autoTick = 0;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaTensorCockpitForm(string baseDir)
        {
            _baseDir = baseDir;
            _coord = new OmegaTensorClusterCoordinator(baseDir);
            _gpu = new OmegaTensorDirectComputeGpu();

            AutoScaleMode = AutoScaleMode.None;
            Text = "OmegaTensor v1.2.0 GM (:19791) :: 7-Engine Dual-Pixel + Dual-AVF Split-Inference & Phase-2 Tri-Engine Bus";
            ClientSize = new Size(1260, 840);
            MinimumSize = new Size(1080, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(8, 12, 22);
            ForeColor = Color.White;
            KeyPreview = true;

            try
            {
                DirectoryInfo parent = Directory.GetParent(baseDir.TrimEnd('\\', '/'));
                string ico = Path.Combine(parent != null ? parent.FullName : baseDir, "icons", "omega_tensor.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 96;
            topBar.BackColor = C(12, 18, 34);

            _lblKpiTop = new Label();
            _lblKpiTop.UseMnemonic = false;
            _lblKpiTop.SetBounds(14, 6, 1230, 22);
            _lblKpiTop.Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblKpiTop.ForeColor = C(56, 189, 248);
            _lblKpiTop.Text = "OMEGATENSOR v1.2.0 GM (:19791) :: 7-TIER DUAL-PIXEL (P10 + P8 + DUAL AVF) + PHASE-2 384B SEQLOCK MESH";

            _lblKpiSub = new Label();
            _lblKpiSub.UseMnemonic = false;
            _lblKpiSub.SetBounds(14, 29, 1230, 20);
            _lblKpiSub.Font = new Font("Consolas", 11.2f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblKpiSub.ForeColor = C(52, 211, 153);
            _lblKpiSub.Text = "Probing 7 Silicon Tiers (Host RTX + P10 :19788/:19789 + P8 :19792/:19793)...";

            Label lLay = new Label();
            lLay.UseMnemonic = false;
            lLay.Text = "Stack:";
            lLay.SetBounds(14, 60, 44, 20);
            lLay.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            lLay.ForeColor = C(148, 163, 184);

            _cmbLayers = new ComboBox();
            _cmbLayers.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLayers.SetBounds(60, 56, 126, 26);
            _cmbLayers.BackColor = C(6, 9, 17);
            _cmbLayers.ForeColor = Color.White;
            _cmbLayers.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cmbLayers.Items.AddRange(new object[] { "32 Layers (8B)", "48 Layers (14B)", "64 Layers (32B)", "80 Layers (70B)" });
            _cmbLayers.SelectedIndex = 0;
            _cmbLayers.SelectedIndexChanged += delegate
            {
                int[] map = new int[] { 32, 48, 64, 80 };
                int idx = Math.Max(0, Math.Min(3, _cmbLayers.SelectedIndex));
                _coord.SetTransformerLayerBudget(map[idx]);
                RefreshUiFromSnapshot();
            };

            _cmbView = new ComboBox();
            _cmbView.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbView.SetBounds(192, 56, 166, 26);
            _cmbView.BackColor = C(6, 9, 17);
            _cmbView.ForeColor = C(56, 189, 248);
            _cmbView.Font = new Font("Consolas", 10.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cmbView.Items.AddRange(new object[] { "Split: 7-Lane + E8/Sheaf", "Left: 7-Tier + SpecRibbon", "Right: 240-Root E8 + H^1=0" });
            _cmbView.SelectedIndex = 0;
            _cmbView.SelectedIndexChanged += delegate { _gpu.ViewMode = _cmbView.SelectedIndex; };

            Button bBurst = MakeBtn("1. 7-Tier Burst [Space]", 364, 54, 152, C(5, 150, 105));
            bBurst.Click += delegate { TriggerAsyncSplitBurst(false); };

            Button bCalib = MakeBtn("2. Calibrate 7x Silicon", 522, 54, 154, C(124, 58, 237));
            bCalib.Click += delegate { TriggerFullCalibrationAsync(); };

            _btnAuto = MakeBtn("3. Auto-Stream: OFF", 682, 54, 142, C(30, 58, 138));
            _btnAuto.Click += delegate
            {
                _autoStream = !_autoStream;
                _btnAuto.Text = _autoStream ? "3. Auto-Stream: LIVE" : "3. Auto-Stream: OFF";
                _btnAuto.BackColor = _autoStream ? C(16, 185, 129) : C(30, 58, 138);
            };

            Button bWakeP8P10 = MakeBtn("4. Wake P10+P8 AVF", 830, 54, 146, C(13, 148, 136));
            bWakeP8P10.Click += delegate { WakeDualPixelsAndAvfAsync(); };

            Button bBeam = MakeBtn("5. Beam Dual OLED", 982, 54, 136, C(67, 56, 202));
            bBeam.Click += delegate { BeamDeckToDualPixelsAsync(); };

            Button bAudit = MakeBtn("6. 10-Vec Audit", 1124, 54, 118, C(180, 83, 9));
            bAudit.Click += delegate { RunStressSuiteToLogAsync(); };

            topBar.Controls.AddRange(new Control[]
            {
                _lblKpiTop, _lblKpiSub, lLay, _cmbLayers, _cmbView,
                bBurst, bCalib, _btnAuto, bWakeP8P10, bBeam, bAudit
            });

            Panel promptBar = new Panel();
            promptBar.Dock = DockStyle.Top;
            promptBar.Height = 38;
            promptBar.BackColor = C(15, 23, 42);

            Label lPr = new Label();
            lPr.UseMnemonic = false;
            lPr.Text = "Split Prompt:";
            lPr.SetBounds(14, 9, 88, 20);
            lPr.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Bold, GraphicsUnit.Pixel);
            lPr.ForeColor = C(192, 132, 252);

            _txtPrompt = new TextBox();
            _txtPrompt.SetBounds(104, 6, 940, 26);
            _txtPrompt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtPrompt.BackColor = C(6, 9, 17);
            _txtPrompt.ForeColor = Color.White;
            _txtPrompt.BorderStyle = BorderStyle.FixedSingle;
            _txtPrompt.Font = new Font("Consolas", 11.6f, FontStyle.Regular, GraphicsUnit.Pixel);
            _txtPrompt.Text = "Partition 32 SH4Z/E8 layers across Host RTX, Pixel 10 (:19788 TPU+PowerVR / :19789 AVF), and Pixel 8 (:19792 Mali-G715+SMMLA / :19793 5.3GB AVF).";
            _txtPrompt.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    TriggerAsyncSplitBurst(true);
                }
            };

            Button bInferAi = MakeBtn("Run Split + Local AI [Enter]", 1054, 4, 192, C(147, 51, 234));
            bInferAi.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bInferAi.Click += delegate { TriggerAsyncSplitBurst(true); };

            promptBar.Controls.AddRange(new Control[] { lPr, _txtPrompt, bInferAi });

            SplitContainer mainSplit = new SplitContainer();
            mainSplit.Dock = DockStyle.Fill;
            mainSplit.Orientation = Orientation.Horizontal;
            mainSplit.BackColor = C(30, 41, 59);
            mainSplit.SplitterWidth = 5;

            _vpPanel = new BufferedViewportPanel();
            _vpPanel.Dock = DockStyle.Fill;
            _vpPanel.BackColor = C(7, 11, 20);
            _vpPanel.Paint += delegate(object s, PaintEventArgs pe)
            {
                if (!_gpu.IsGpuActive)
                    _gpu.RenderSoftwareFallback(pe.Graphics, _vpPanel.ClientSize.Width, _vpPanel.ClientSize.Height, (float)_animClock.Elapsed.TotalSeconds, _coord.GetSnapshotCopy());
            };
            _vpPanel.Resize += delegate
            {
                if (_gpu.IsGpuActive)
                    _gpu.ResizeSwapChain(_vpPanel.ClientSize.Width, _vpPanel.ClientSize.Height);
            };
            mainSplit.Panel1.Controls.Add(_vpPanel);

            SplitContainer botSplit = new SplitContainer();
            botSplit.Dock = DockStyle.Fill;
            botSplit.Orientation = Orientation.Vertical;
            botSplit.BackColor = C(30, 41, 59);
            botSplit.SplitterWidth = 5;

            _gridEngines = BuildEngineGrid();
            botSplit.Panel1.Controls.Add(_gridEngines);

            _rtbLog = new RichTextBox();
            _rtbLog.Dock = DockStyle.Fill;
            _rtbLog.ReadOnly = true;
            _rtbLog.BorderStyle = BorderStyle.None;
            _rtbLog.BackColor = C(6, 9, 17);
            _rtbLog.ForeColor = C(226, 232, 240);
            _rtbLog.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            botSplit.Panel2.Controls.Add(_rtbLog);

            mainSplit.Panel2.Controls.Add(botSplit);

            Controls.Add(mainSplit);
            Controls.Add(promptBar);
            Controls.Add(topBar);

            _http = new OmegaTensorHttpServer(19791, _coord, _gpu, delegate(string msg) { AppendLog(msg, C(52, 211, 153)); });

            _renderTimer = new System.Windows.Forms.Timer();
            _renderTimer.Interval = 16;
            _renderTimer.Tick += delegate
            {
                float t = (float)_animClock.Elapsed.TotalSeconds;
                if (_gpu.IsGpuActive)
                    _gpu.RenderFrame(t, _coord.GetSnapshotCopy(), _vpPanel.ClientSize.Width, _vpPanel.ClientSize.Height);
                else
                    _vpPanel.Invalidate();
            };

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 2400;
            _pollTimer.Tick += delegate
            {
                if (!_busyBurst)
                {
                    _coord.RefreshClusterHealthAsync(delegate(string note)
                    {
                        if (!string.IsNullOrEmpty(note) && note.StartsWith("[AUTO-", StringComparison.Ordinal))
                            AppendLog(note, C(250, 204, 21));
                        RefreshUiFromSnapshot();
                    });
                }
                if (_autoStream && !_busyBurst && (++_autoTick % 2) == 0)
                    TriggerAsyncSplitBurst(false);
            };

            KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ActiveControl != _txtPrompt && ke.KeyCode == Keys.Space)
                {
                    ke.SuppressKeyPress = true;
                    TriggerAsyncSplitBurst(false);
                }
            };

            Shown += delegate
            {
                try
                {
                    mainSplit.SplitterDistance = Math.Max(330, (mainSplit.Height * 58) / 100);
                    botSplit.SplitterDistance = Math.Max(600, (botSplit.Width * 58) / 100);
                }
                catch { }

                bool gpuOk = _gpu.Initialize(_vpPanel.Handle, _vpPanel.ClientSize.Width, _vpPanel.ClientSize.Height);
                bool httpOk = _http.Start();
                AppendLog("=== OMEGATENSOR v1.2.0 GM (:19791) LIVE [7-ENGINE DUAL-PIXEL + PHASE-2 384B SEQLOCK MESH] ===", C(56, 189, 248));
                AppendLog("  GPU Backend : " + _gpu.BackendLabel + " (active=" + gpuOk + ")", C(52, 211, 153));
                AppendLog("  HTTP + Bus  : " + (httpOk ? "http://127.0.0.1:19791 (/api/status, /api/dual_pixel, /api/omsh_assist) + Local\\OmegaTriEngineUniforms" : "Port 19791 busy"), C(192, 132, 252));

                RefreshUiFromSnapshot();
                _renderTimer.Start();
                _pollTimer.Start();
                _coord.RefreshClusterHealthAsync(delegate(string msg)
                {
                    ClusterSplitSnapshot snap0 = _coord.GetSnapshotCopy();
                    AppendLog("  " + msg, C(52, 211, 153));
                    AppendLog("  " + snap0.AndroidBuildBanner, C(250, 204, 21));
                    RefreshUiFromSnapshot();
                });
            };

            FormClosing += delegate
            {
                _renderTimer.Stop();
                _pollTimer.Stop();
                _http.Dispose();
                _coord.Dispose();
                _gpu.Dispose();
            };
        }

        private static Button MakeBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 10.6f, FontStyle.Regular, GraphicsUnit.Pixel);
            b.FlatAppearance.BorderColor = C(56, 189, 248);
            return b;
        }

        private static DataGridView BuildEngineGrid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.BackgroundColor = C(8, 12, 22);
            g.GridColor = C(30, 41, 59);
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = C(15, 23, 42);
            g.ColumnHeadersDefaultCellStyle.ForeColor = C(56, 189, 248);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DefaultCellStyle.BackColor = C(9, 14, 26);
            g.DefaultCellStyle.ForeColor = Color.White;
            g.DefaultCellStyle.SelectionBackColor = C(30, 58, 138);
            g.DefaultCellStyle.Font = new Font("Consolas", 10.6f, FontStyle.Regular, GraphicsUnit.Pixel);
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.RowTemplate.Height = 23;

            g.Columns.Add("eng", "Silicon Engine (7 Tiers)");
            g.Columns.Add("st", "State");
            g.Columns.Add("lay", "Layers");
            g.Columns.Add("gops", "GOPS/s");
            g.Columns.Add("rtt", "RTT");
            g.Columns.Add("role", "Assigned Split Stage & Live Telemetry");

            g.Columns[0].FillWeight = 26f;
            g.Columns[1].FillWeight = 9f;
            g.Columns[2].FillWeight = 12f;
            g.Columns[3].FillWeight = 11f;
            g.Columns[4].FillWeight = 9f;
            g.Columns[5].FillWeight = 33f;
            for (int i = 0; i < ClusterSplitSnapshot.EngineCount; i++)
                g.Rows.Add("", "STANDBY", "0L (0.0%)", "0.0", "0.0ms", "");
            return g;
        }

        private void TriggerAsyncSplitBurst(bool withLocalAi)
        {
            if (_busyBurst) return;
            _busyBurst = true;
            string prompt = _txtPrompt.Text;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string res = _coord.ExecuteLiveHeterogeneousSplitBurst(_gpu, prompt, withLocalAi);
                    AppendLog(res, C(52, 211, 153));
                    RefreshUiFromSnapshot();
                }
                catch (Exception ex)
                {
                    AppendLog("[BURST GUARD] " + ex.Message, C(250, 204, 21));
                }
                finally
                {
                    _busyBurst = false;
                }
            });
        }

        private void TriggerFullCalibrationAsync()
        {
            if (_busyBurst) return;
            _busyBurst = true;
            AppendLog("[CALIBRATE] Probing Host Vulkan Companion + Pixel 10 (:19788/:19789) + Pixel 8 (:19792/:19793)...", C(250, 204, 21));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string res = _coord.CalibrateHostVulkanAndEdgeCluster(_gpu);
                    AppendLog(res, C(52, 211, 153));
                    RefreshUiFromSnapshot();
                }
                catch (Exception ex)
                {
                    AppendLog("[CALIBRATE GUARD] " + ex.Message, C(250, 204, 21));
                }
                finally
                {
                    _busyBurst = false;
                }
            });
        }

        private void WakeDualPixelsAndAvfAsync()
        {
            AppendLog("[WAKE DUAL-PIXEL] Arming pinned ADB/SSH tunnels (:19788/:19789/:22222 P10 + :19792/:19793/:22232 P8)...", C(56, 189, 248));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string heal = _coord.TryAutoRecoverDualPixelDaemons(true, true);
                    if (!string.IsNullOrEmpty(heal)) AppendLog(heal, C(52, 211, 153));
                    string p8Msg = OmegaPocketCore.BootstrapPixel8Avf53GbVm(_baseDir);
                    AppendLog("[P8 5.3GB AVF] " + p8Msg, C(250, 204, 21));
                    _coord.RefreshClusterHealthAsync(delegate(string m)
                    {
                        AppendLog(m, C(52, 211, 153));
                        RefreshUiFromSnapshot();
                    });
                }
                catch (Exception ex)
                {
                    AppendLog("[WAKE NOTE] " + ex.Message, C(248, 113, 113));
                }
            });
        }

        private void RunStressSuiteToLogAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    OmegaTensorRedTeamStressSuite.RunAllVectors(_baseDir);
                    string txt = Path.Combine(_baseDir, "exports", "omega_tensor_stress_console.txt");
                    if (File.Exists(txt))
                        AppendLog(File.ReadAllText(txt, Encoding.UTF8).TrimEnd(), C(125, 211, 252));
                }
                catch { }
            });
        }

        private void BeamDeckToDualPixelsAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string adb = ResolveAdbExe();
                    string diag;
                    List<AdbDeviceRecord> devs = OmegaPocketCore.DiscoverLiveAdbPhones(1500, out diag);
                    int beamed = 0;
                    for (int i = 0; i < devs.Count; i++)
                    {
                        if (devs[i].State != "device") continue;
                        RunCmd(adb, "-s " + devs[i].Serial + " reverse tcp:19791 tcp:19791", 2000);
                        RunCmd(adb, "-s " + devs[i].Serial + " shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19791/deck\"", 2500);
                        beamed++;
                    }
                    AppendLog("[BEAM OK] Launched 7-Engine OLED Split Deck on " + beamed + " connected Pixel(s)!", C(192, 132, 252));
                }
                catch (Exception ex)
                {
                    AppendLog("[BEAM WARN] " + ex.Message, C(248, 113, 113));
                }
            });
        }

        private static string ResolveAdbExe()
        {
            string p1 = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe");
            if (File.Exists(p1)) return p1;
            if (File.Exists(@"C:\platform-tools\adb.exe")) return @"C:\platform-tools\adb.exe";
            return "adb.exe";
        }

        private static void RunCmd(string exe, string args, int ms)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi))
            {
                if (p != null) p.WaitForExit(ms);
            }
        }

        private void RefreshUiFromSnapshot()
        {
            if (IsDisposed) return;
            Action act = delegate
            {
                ClusterSplitSnapshot s = _coord.GetSnapshotCopy();
                _lblKpiSub.Text = string.Format(CultureInfo.InvariantCulture,
                    "Cluster: {0:0.000} TOPS/s ({1:0.0} GOPS/s) | Speedup: {2:0.00}x | P10:{3} P8:{4} | Spec: {5:0.0} tok/s ({6:0.0}%) | E8={7:0.0}dB | {8}",
                    s.TotalOnlineTops, s.TotalOnlineGops, s.SpeedupVsRtxOnly,
                    s.Pixel10Live ? "ON" : "--", s.Pixel8Live ? "ON" : "--",
                    s.SpeculativeTokPerSec, s.SpeculativeAcceptRate * 100.0,
                    s.E8MeanSqnrDb, s.SheafSummary);

                if (IsDisposed || _gridEngines == null || _gridEngines.Rows.Count < s.Engines.Length) return;
                for (int i = 0; i < s.Engines.Length; i++)
                {
                    SiliconEngineTelemetry e = s.Engines[i];
                    string rttStr = e.LastLatencyMs < 100.0
                        ? (e.LastLatencyMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms")
                        : (((int)Math.Round(e.LastLatencyMs)).ToString(CultureInfo.InvariantCulture) + "ms");
                    DataGridViewRow row = _gridEngines.Rows[i];
                    row.Cells[0].Value = e.Name;
                    row.Cells[1].Value = e.Online ? "LIVE" : "STANDBY";
                    row.Cells[1].Style.ForeColor = e.Online ? C(52, 211, 153) : C(248, 113, 113);
                    row.Cells[2].Value = string.Format(CultureInfo.InvariantCulture, "{0}L ({1:0.0}%)", e.AssignedLayers, e.SharePercent);
                    row.Cells[3].Value = e.MeasuredGops.ToString("0.0", CultureInfo.InvariantCulture);
                    row.Cells[4].Value = rttStr;
                    row.Cells[5].Value = e.Role + " :: " + e.LastDetail;
                }
            };
            if (InvokeRequired) { try { BeginInvoke(act); } catch { } }
            else act();
        }

        private void AppendLog(string text, Color fg)
        {
            if (IsDisposed || _rtbLog == null) return;
            Action act = delegate
            {
                if (IsDisposed || _rtbLog == null) return;
                if (_rtbLog.TextLength > 24000)
                {
                    _rtbLog.Select(0, 8000);
                    _rtbLog.SelectedText = "";
                }
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.SelectionLength = 0;
                _rtbLog.SelectionColor = fg;
                _rtbLog.AppendText((text ?? "") + "\r\n");
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.ScrollToCaret();
            };
            if (InvokeRequired) { try { BeginInvoke(act); } catch { } }
            else act();
        }

        private static void WriteCrashGuardLog(string baseDir, string tag, Exception ex)
        {
            try
            {
                string expDir = Path.Combine(baseDir, "exports");
                Directory.CreateDirectory(expDir);
                string ln = "[" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "] [" + tag + "] " + (ex != null ? ex.ToString() : "unknown") + "\r\n";
                File.AppendAllText(Path.Combine(expDir, "omega_tensor_crash_guard.log"), ln, new UTF8Encoding(false));
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--stress-test", StringComparison.OrdinalIgnoreCase))
                    {
                        int rc = OmegaTensorRedTeamStressSuite.RunAllVectors(baseDir);
                        Track02StressSummary p2 = OmegaPocketStressSuite.RunAll8Vectors();
                        Console.WriteLine("[FUSED POCKET+PC SILICON FUSION 8-VEC] " + p2.PassedCount + "/" + p2.TotalCount + " PASS (" + p2.TotalTrials + " trials)");
                        Environment.Exit((rc == 0 && p2.AllPassed) ? 0 : 1);
                        return;
                    }
                }
            }
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs te)
                {
                    WriteCrashGuardLog(baseDir, "UI_THREAD_GUARD", te.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ue)
                {
                    WriteCrashGuardLog(baseDir, "BG_THREAD_GUARD", ue.ExceptionObject as Exception);
                };
            }
            catch { }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaTensorCockpitForm(baseDir));
        }
    }
}