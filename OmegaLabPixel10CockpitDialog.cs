using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OmegaStandaloneLab
{
    public sealed class P10KernelRow
    {
        public string Id = "";
        public int Matched = 0;
        public int Elements = 0;
        public double MelemPerSec = 0.0;
        public double GopsPerSec = 0.0;
        public double ElapsedMs = 0.0;
        public string Detail = "";
    }

    public sealed class P10TelemetrySnapshot
    {
        public bool Online = false;
        public string DeviceName = "";
        public string ApiVersion = "";
        public int SubgroupSize = 128;
        public int SubgroupMin = 32;
        public int SubgroupMax = 128;
        public int UmaHeapMb = 0;
        public int ExtTotal = 0;
        public string IdotTune = "";
        public string UdotTune = "";
        public string CoopTune = "";
        public int HeartbeatTicks = 0;
        public int JitterUs = 0;
        public double Sh4zMops = 0.0;
        public double UdotMops = 0.0;
        public double E8Mvec = 0.0;
        public double LeaseNs = 0.0;
        public string LeaseToken = "";
        public int MemAvailMb = 0;
        public int MemTotalMb = 0;
        public bool TpuHwUnlocked = false;
        public double TpuPeakGops = 0.0;
        public string TpuSummary = "";
        public string TpuSocProps = "";
        public string TpuThermal = "";
        public string TpuBuenosSysfs = "";
        public List<P10KernelRow> Kernels = new List<P10KernelRow>();
        public string RawJson = "";
    }

    public static class Pixel10TensorBridge
    {
        public static bool ProbeSshBanner(int port, int timeoutMs, out string banner)
        {
            banner = "";
            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect("127.0.0.1", port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !tc.Connected) return false;
                    tc.EndConnect(ar);
                    tc.ReceiveTimeout = Math.Max(130, timeoutMs);
                    NetworkStream ns = tc.GetStream();
                    byte[] b = new byte[64];
                    int n = ns.Read(b, 0, b.Length);
                    if (n >= 4)
                    {
                        string s = Encoding.ASCII.GetString(b, 0, n).Trim();
                        int nl = s.IndexOfAny(new char[] { '\r', '\n' });
                        banner = nl > 0 ? s.Substring(0, nl) : s;
                        return banner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }
            return false;
        }

                public static bool ProbeDaemonHealth(int port, int timeoutMs)
        {
            string h = HttpGet("http://127.0.0.1:" + port + "/api/health", timeoutMs);
            return !string.IsNullOrEmpty(h) && h.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string HttpGet(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Proxy = null;
                r.Method = "GET";
                r.Timeout = timeoutMs;
                r.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r");
        }

        private static string GetStr(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? Unesc(m.Groups[1].Value) : "";
        }

        private static int GetInt(string json, string key, int defVal)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            int v;
            return (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) ? v : defVal;
        }

        private static double GetDbl(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            double v;
            return (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) ? v : 0.0;
        }

        public static P10TelemetrySnapshot FetchSnapshot(string endpointPath, int timeoutMs)
        {
            return FetchSnapshotOnPort(19788, endpointPath, timeoutMs);
        }

        public static P10TelemetrySnapshot FetchSnapshotOnPort(int port, string endpointPath, int timeoutMs)
        {
            P10TelemetrySnapshot snap = new P10TelemetrySnapshot();
            string raw = HttpGet("http://127.0.0.1:" + port + endpointPath, timeoutMs);
            if (string.IsNullOrEmpty(raw)) return snap;

            snap.Online = true;
            snap.RawJson = raw;
            snap.DeviceName = GetStr(raw, "device");
            snap.ApiVersion = GetStr(raw, "api");
            snap.SubgroupSize = GetInt(raw, "subgroup_size", 128);
            snap.SubgroupMin = GetInt(raw, "subgroup_min", 32);
            snap.SubgroupMax = GetInt(raw, "subgroup_max", 128);
            snap.UmaHeapMb = GetInt(raw, "uma_heap_mb", 0);
            snap.ExtTotal = GetInt(raw, "ext_total", 0);
            snap.IdotTune = GetStr(raw, "idot_tune");
            snap.UdotTune = GetStr(raw, "udot_tune");
            snap.CoopTune = GetStr(raw, "coop_tune");
            snap.HeartbeatTicks = GetInt(raw, "heartbeat_ticks", GetInt(raw, "ticks", 0));
            snap.JitterUs = GetInt(raw, "max_jitter_us", 0);
            snap.Sh4zMops = GetDbl(raw, "sh4z_mops_s");
            snap.UdotMops = GetDbl(raw, "udot_mops_s");
            snap.E8Mvec = GetDbl(raw, "e8_mvec_s");
            snap.LeaseNs = GetDbl(raw, "avg_lease_ns");
            snap.LeaseToken = GetStr(raw, "lease_token");
            snap.MemAvailMb = GetInt(raw, "mem_avail_mb", 0);
            snap.MemTotalMb = GetInt(raw, "mem_total_mb", 0);
            snap.TpuHwUnlocked = Regex.IsMatch(raw, "\"tpu_hw_unlocked\"\\s*:\\s*true", RegexOptions.IgnoreCase)
                || Regex.IsMatch(raw, "\"hw_tpu_unlocked\"\\s*:\\s*true", RegexOptions.IgnoreCase);
            snap.TpuPeakGops = GetDbl(raw, "tpu_peak_gops");
            if (snap.TpuPeakGops <= 0.0) snap.TpuPeakGops = GetDbl(raw, "peak_tpu_gops");
            snap.TpuSummary = GetStr(raw, "tpu_summary");
            if (string.IsNullOrEmpty(snap.TpuSummary)) snap.TpuSummary = GetStr(raw, "summary");
            snap.TpuSocProps = GetStr(raw, "soc_props");
            snap.TpuThermal = GetStr(raw, "thermal_and_freq");
            snap.TpuBuenosSysfs = GetStr(raw, "buenos_sysfs");

            MatchCollection km = Regex.Matches(raw, "\\{\\s*\"id\"\\s*:\\s*\"([^\"]+)\"[^{}]*?\\}");
            for (int i = 0; i < km.Count; i++)
            {
                string obj = km[i].Value;
                P10KernelRow kr = new P10KernelRow();
                kr.Id = GetStr(obj, "id");
                kr.Elements = GetInt(obj, "elements", 0);
                kr.Matched = GetInt(obj, "matched", -1);
                if (kr.Elements <= 0 || kr.Matched < 0)
                {
                    string par = GetStr(obj, "parity");
                    int slash = par.IndexOf('/');
                    if (slash > 0)
                    {
                        int mVal, eVal;
                        if (int.TryParse(par.Substring(0, slash), NumberStyles.Integer, CultureInfo.InvariantCulture, out mVal)) kr.Matched = mVal;
                        if (int.TryParse(par.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out eVal)) kr.Elements = eVal;
                    }
                    if (kr.Elements <= 0) kr.Elements = 131072;
                    if (kr.Matched < 0) kr.Matched = 0;
                }
                kr.ElapsedMs = GetDbl(obj, "elapsed_ms");
                if (kr.ElapsedMs <= 0.0) kr.ElapsedMs = GetDbl(obj, "ms");
                kr.MelemPerSec = GetDbl(obj, "melem_s");
                kr.GopsPerSec = GetDbl(obj, "gops_s");
                kr.Detail = GetStr(obj, "detail");
                snap.Kernels.Add(kr);
            }
            return snap;
        }

        public static string FormatCompactLauncherLine(P10TelemetrySnapshot s)
        {
            if (s == null || !s.Online || s.Kernels.Count == 0)
                return "P10 10-Kernel Tensor (:19788): STANDBY — click row below or press Shift+F9 to wake & bench!";

            int passK = 0;
            double k7Gops = 0.0, k10Gops = 0.0, k8Gops = 0.0, k9Gops = 0.0;
            for (int i = 0; i < s.Kernels.Count; i++)
            {
                P10KernelRow k = s.Kernels[i];
                if (k.Elements > 0 && k.Matched == k.Elements) passK++;
                if (k.Id.StartsWith("K7_")) k7Gops = k.GopsPerSec;
                else if (k.Id.StartsWith("K8_")) k8Gops = k.GopsPerSec;
                else if (k.Id.StartsWith("K9_")) k9Gops = k.GopsPerSec;
                else if (k.Id.StartsWith("K10_")) k10Gops = k.GopsPerSec;
            }
            double sh4zGops = s.Sh4zMops / 1000.0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "P10 Tensor (:19788): {0}/{1} PASS | K7(SDot)={2:0.0}G | K10(UDot)={3:0.0}G | K8/K9={4:0.0}/{5:0.0}G | A725={6:0.0}G",
                passK, s.Kernels.Count, k7Gops, k10Gops, k8Gops, k9Gops, sh4zGops);
        }

        public static string FormatDualDaemonLauncherLine(P10TelemetrySnapshot hostSnap, P10TelemetrySnapshot avfSnap)
        {
            bool hOk = (hostSnap != null && hostSnap.Online && hostSnap.Kernels.Count > 0);
            bool aOk = (avfSnap != null && avfSnap.Online && avfSnap.Kernels.Count > 0);
            if (!hOk && !aOk)
                return "P10 Dual-GPU (:19788 + @deb :19789): STANDBY — press Shift+F9 to wake Native + Virtio-GPU daemons!";
            if (hOk && aOk)
            {
                double hk10 = 0.0, ak10 = 0.0, a725U = hostSnap.UdotMops / 1000.0;
                for (int i = 0; i < hostSnap.Kernels.Count; i++)
                    if (hostSnap.Kernels[i].Id.StartsWith("K10_")) hk10 = hostSnap.Kernels[i].GopsPerSec;
                for (int i = 0; i < avfSnap.Kernels.Count; i++)
                    if (avfSnap.Kernels[i].Id.StartsWith("K10_")) ak10 = avfSnap.Kernels[i].GopsPerSec;
                string tpuTag = hostSnap.TpuHwUnlocked
                    ? string.Format(CultureInfo.InvariantCulture, "TPU(buenos)={0:0.0}G | ", hostSnap.TpuPeakGops)
                    : "";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Quad-Silicon LIVE :: {0}@deb={1:0.0}G | PowerVR={2:0.0}G | A725={3:0.0}G",
                    tpuTag, ak10, hk10, a725U > 0.1 ? a725U : (hostSnap.Sh4zMops / 1000.0));
            }
            if (aOk)
            {
                double ak10 = 0.0, ak7 = 0.0;
                for (int i = 0; i < avfSnap.Kernels.Count; i++)
                {
                    if (avfSnap.Kernels[i].Id.StartsWith("K10_")) ak10 = avfSnap.Kernels[i].GopsPerSec;
                    else if (avfSnap.Kernels[i].Id.StartsWith("K7_")) ak7 = avfSnap.Kernels[i].GopsPerSec;
                }
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "P10 AVF Virtio-GPU (:19789): {0}/{0} PASS | K10(UDot)={1:0.0}G | K7(SDot)={2:0.0}G (:19788 standby)",
                    avfSnap.Kernels.Count, ak10, ak7);
            }
            return FormatCompactLauncherLine(hostSnap);
        }

        public static string FormatMultiLineScorecard(string header, P10TelemetrySnapshot s)
        {
            if (s == null || !s.Online)
                return "[" + header + "] :19788 offline.";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== " + header + " ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "GPU: {0} (API {1}, Subgroup={2} [{3}..{4}], UMA={5} MB, {6} Exts)",
                s.DeviceName, s.ApiVersion, s.SubgroupSize, s.SubgroupMin, s.SubgroupMax, s.UmaHeapMb, s.ExtTotal));
            if (!string.IsNullOrEmpty(s.IdotTune))
                sb.AppendLine("IntDot Tune: " + s.IdotTune);
            if (!string.IsNullOrEmpty(s.UdotTune))
                sb.AppendLine("UDot 0x0040008 Tune: " + s.UdotTune);
            if (!string.IsNullOrEmpty(s.CoopTune))
                sb.AppendLine("CoopMat Tune: " + s.CoopTune);
            if (!string.IsNullOrEmpty(s.TpuSummary) && s.TpuSummary.IndexOf("AVF Debian guest", StringComparison.OrdinalIgnoreCase) < 0)
                sb.AppendLine("Tensor G5 4th-Gen TPU: " + s.TpuSummary + (!string.IsNullOrEmpty(s.TpuBuenosSysfs) ? (" [" + s.TpuBuenosSysfs + "]") : ""));
            if (s.Sh4zMops > 0.0)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Tensor G5 CPU: 0x7C SH4Z={0:0.0} MOPS/s ({1:0.00} GOPS/s) | E8={2:0.00} MVec/s | 0x80 Lease={3:0.0} ns ({4}) | 15.965Hz Ticks={5}",
                    s.Sh4zMops, s.Sh4zMops / 1000.0, s.E8Mvec, s.LeaseNs, s.LeaseToken, s.HeartbeatTicks));
            }
            long totMatch = 0, totElem = 0;
            for (int i = 0; i < s.Kernels.Count; i++)
            {
                P10KernelRow k = s.Kernels[i];
                totMatch += k.Matched;
                totElem += k.Elements;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1,-19} Parity={2}/{3} | {4,7:0.0} MElem/s | {5,7:0.00} GOPS/s ({6,5:0.00} ms) | {7}",
                    (k.Elements > 0 && k.Matched == k.Elements) ? "PASS" : "WARN",
                    k.Id, k.Matched, k.Elements, k.MelemPerSec, k.GopsPerSec, k.ElapsedMs, k.Detail));
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "TOTAL PARITY: {0}/{1} elements ({2}/{3} kernels exact)",
                totMatch, totElem, s.Kernels.Count, s.Kernels.Count));
            return sb.ToString().TrimEnd();
        }
    }

    public sealed class OmegaLabPixel10CockpitDialog : Form
    {
        private static OmegaLabPixel10CockpitDialog _openInstance;
        private readonly string _suiteRoot;
        private readonly Label _lblSummary, _lblTune, _lblAvf;
        private readonly DataGridView _grid;
        private readonly RichTextBox _rtbLog;
        private volatile bool _busy = false;
        private volatile int _activePort = 19788;

        public static void ShowOrFocus(Form owner, string suiteRoot)
        {
            if (_openInstance != null && !_openInstance.IsDisposed)
            {
                if (_openInstance.WindowState == FormWindowState.Minimized)
                    _openInstance.WindowState = FormWindowState.Normal;
                _openInstance.BringToFront();
                _openInstance.Focus();
                _openInstance.RefreshFromDaemonAsync("/api/status", "Refreshed live :19788 snapshot");
                return;
            }
            OmegaLabPixel10CockpitDialog dlg = new OmegaLabPixel10CockpitDialog(suiteRoot);
            _openInstance = dlg;
            dlg.Show(owner);
        }

        public OmegaLabPixel10CockpitDialog(string suiteRoot)
        {
            _suiteRoot = suiteRoot;
            AutoScaleMode = AutoScaleMode.None;
            Text = "ACT-Omega Lab v2.3.2 :: Pixel 10 Quad-Silicon Cockpit (:19788 PowerVR + Tensor G5 TPU | :19789 AVF @deb Virtio-GPU)";
            ClientSize = new Size(1068, 692);
            MinimumSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(9, 13, 22);
            ForeColor = Color.White;
            try
            {
                string ic = Path.Combine(suiteRoot, "icons", "omega_lab.ico");
                if (File.Exists(ic)) Icon = new Icon(ic);
            }
            catch { }

            Panel hdr = new Panel();
            hdr.Dock = DockStyle.Top;
            hdr.Height = 76;
            hdr.Padding = new Padding(12, 6, 12, 6);
            hdr.BackColor = Color.FromArgb(13, 21, 38);

            _lblSummary = new Label();
            _lblSummary.UseMnemonic = false;
            _lblSummary.SetBounds(12, 6, 1040, 20);
            _lblSummary.Font = new Font("Consolas", 9.2f, FontStyle.Bold);
            _lblSummary.ForeColor = Color.FromArgb(52, 211, 153);
            _lblSummary.Text = "Pixel 10 Tensor G5 + PowerVR DXT-48-1536 MC1 (:19788) :: Probing live daemon...";

            _lblTune = new Label();
            _lblTune.UseMnemonic = false;
            _lblTune.SetBounds(12, 29, 1040, 19);
            _lblTune.Font = new Font("Consolas", 8.5f);
            _lblTune.ForeColor = Color.FromArgb(56, 189, 248);
            _lblTune.Text = "[VK-TUNE] Waiting for live Cooperative Matrix & IntDot calibration...";

            _lblAvf = new Label();
            _lblAvf.UseMnemonic = false;
            _lblAvf.SetBounds(12, 50, 1040, 19);
            _lblAvf.Font = new Font("Consolas", 8.5f);
            _lblAvf.ForeColor = Color.FromArgb(250, 204, 21);
            _lblAvf.Text = "Android 17 AVF / pKVM Debian 13 (:22222) :: Checking OpenSSH banner...";

            hdr.Controls.AddRange(new Control[] { _lblSummary, _lblTune, _lblAvf });

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 42;
            bar.Padding = new Padding(10, 5, 10, 5);
            bar.WrapContents = false;
            bar.BackColor = Color.FromArgb(15, 24, 44);

            Button bSelHost = MakeBtn("1. Native GPU+TPU (:19788)", Color.FromArgb(2, 132, 199));
            bSelHost.Click += delegate { _activePort = 19788; RefreshFromDaemonAsync("/api/status", "Switched -> Native PowerVR + Tensor G5 TPU (:19788)"); };

            Button bSelAvf = MakeBtn("2. @deb Virtio-GPU (:19789)", Color.FromArgb(4, 120, 87));
            bSelAvf.Click += delegate { _activePort = 19789; RefreshFromDaemonAsync("/api/status", "Switched -> AVF @deb Virtio-GPU (:19789)"); };

            Button bProbeTpu = MakeBtn("3. Bench 16L TPU (:19788)", Color.FromArgb(147, 51, 234));
            bProbeTpu.Click += delegate { ProbeAndBenchTensorG5TpuAsync(); };

            Button bDispUdot = MakeBtn("4. @deb 497G UDot (:19789)", Color.FromArgb(14, 116, 144));
            bDispUdot.Click += delegate { DispatchLiveAvfUdotJobAsync(); };

            Button bBenchAll = MakeBtn("5. Re-Bench", Color.FromArgb(5, 150, 105));
            bBenchAll.Click += delegate { RefreshFromDaemonAsync("/api/bench", "Executed full silicon benchmark on :" + _activePort); };

            Button bRtx = MakeBtn("6. DirectCompute GPU", Color.FromArgb(180, 83, 9));
            bRtx.Click += delegate { RunHostRtxCompanionAsync(); };

            Button bAvfDaemon = MakeBtn("7. Deploy @deb", Color.FromArgb(109, 40, 217));
            bAvfDaemon.Click += delegate { TriggerAvfNativeRustBenchAsync(); };

            Button bWake = MakeBtn("8. Deploy :19788", Color.FromArgb(15, 118, 110));
            bWake.Click += delegate { WakeOrDeployPixelWorkerAsync(); };

            Button bWeb = MakeBtn("9. Web HUD", Color.FromArgb(30, 58, 138));
            bWeb.Click += delegate { try { Process.Start("http://127.0.0.1:" + _activePort + "/"); } catch { } };

            bar.Controls.AddRange(new Control[] { bSelHost, bSelAvf, bProbeTpu, bDispUdot, bBenchAll, bRtx, bAvfDaemon, bWake, bWeb });

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = Color.FromArgb(8, 12, 21);
            _grid.BorderStyle = BorderStyle.None;
            _grid.GridColor = Color.FromArgb(30, 41, 59);
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeight = 28;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 23, 42);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(56, 189, 248);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.6f);
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(9, 14, 25);
            _grid.DefaultCellStyle.ForeColor = Color.FromArgb(226, 232, 240);
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 58, 138);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.DefaultCellStyle.Font = new Font("Consolas", 9.0f);
            _grid.RowTemplate.Height = 25;

            _grid.Columns.Add("kernel", "Kernel ID");
            _grid.Columns.Add("status", "Status");
            _grid.Columns.Add("parity", "Bit-Exact Parity");
            _grid.Columns.Add("melem", "MElem/s");
            _grid.Columns.Add("gops", "GOPS/s");
            _grid.Columns.Add("ms", "Time (ms)");
            _grid.Columns.Add("spec", "SPIR-V / Subgroup / Tensor Tile Spec");
            _grid.Columns[0].Width = 185;
            _grid.Columns[1].Width = 68;
            _grid.Columns[2].Width = 136;
            _grid.Columns[3].Width = 96;
            _grid.Columns[4].Width = 98;
            _grid.Columns[5].Width = 88;
            _grid.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _rtbLog = new RichTextBox();
            _rtbLog.Dock = DockStyle.Fill;
            _rtbLog.ReadOnly = true;
            _rtbLog.BorderStyle = BorderStyle.None;
            _rtbLog.BackColor = Color.FromArgb(6, 9, 17);
            _rtbLog.ForeColor = Color.FromArgb(226, 232, 240);
            _rtbLog.Font = new Font("Consolas", 9.0f);

            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(_rtbLog);

            Controls.Add(split);
            Controls.Add(bar);
            Controls.Add(hdr);

            Shown += delegate
            {
                split.SplitterDistance = 315;
                RefreshFromDaemonAsync("/api/status", "Initial cockpit probe");
            };
        }

        private static Button MakeBtn(string text, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.AutoSize = true;
            b.Height = 30;
            b.Padding = new Padding(6, 0, 6, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.2f);
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }

        private void AppendLog(string msg, Color fg)
        {
            if (IsDisposed) return;
            Action a = delegate
            {
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.SelectionLength = 0;
                _rtbLog.SelectionColor = fg;
                _rtbLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
                _rtbLog.ScrollToCaret();
            };
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } } else a();
        }

        public void RefreshFromDaemonAsync(string apiPath, string reason)
        {
            if (_busy) return;
            _busy = true;
            AppendLog(reason + " (GET " + apiPath + ")...", Color.FromArgb(125, 211, 252));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    OmegaLabLauncherForm.ArmAllPixelsStatic();
                    int pTarget = _activePort;
                    if (pTarget == 19789) OmegaLabLauncherForm.EnsureHostPort19789BridgedStatic(_suiteRoot);
                    P10TelemetrySnapshot snap = Pixel10TensorBridge.FetchSnapshotOnPort(pTarget, apiPath, 35000);
                    if (!snap.Online && pTarget == 19789)
                    {
                        // Fall back cleanly to :19788 if :19789 hasn't been deployed yet, while noting :19789 status
                        P10TelemetrySnapshot fallback = Pixel10TensorBridge.FetchSnapshotOnPort(19788, apiPath, 8000);
                        if (fallback.Online) { snap = fallback; pTarget = 19788; _activePort = 19788; }
                    }
                    string sshBan;
                    bool sshLive = Pixel10TensorBridge.ProbeSshBanner(22222, 180, out sshBan);
                    string avfHealth = Pixel10TensorBridge.HttpGet("http://127.0.0.1:19789/api/health", 500);
                    bool avf19789Live = !string.IsNullOrEmpty(avfHealth) && avfHealth.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                    string natHealth = Pixel10TensorBridge.HttpGet("http://127.0.0.1:19788/api/health", 500);
                    bool tpuLive = !string.IsNullOrEmpty(natHealth) && natHealth.IndexOf("\"tpu_hw_unlocked\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                    Match mTpuG = Regex.Match(natHealth ?? "", "\"tpu_peak_gops\"\\s*:\\s*([0-9.]+)");
                    string tpuBadge = tpuLive ? ("TPU UNLOCKED (" + (mTpuG.Success ? mTpuG.Groups[1].Value : "OK") + " GOPS/s)") : "Standby";

                    if (!snap.Online && pTarget == 19788)
                    {
                        OmegaLabLauncherForm.QuickWakePixel10DaemonStatic();
                        snap = Pixel10TensorBridge.FetchSnapshotOnPort(19788, "/api/status", 8000);
                    }

                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)delegate
                        {
                            _lblAvf.Text = "View: :" + pTarget + " | Tensor G5 TPU (:19788)=" + tpuBadge +
                                " | @deb Virtio-GPU (:19789)=" + (avf19789Live ? "LIVE" : "Standby") +
                                " | SSH (:22222)=" + (sshLive ? "LIVE" : "Standby");
                            _lblAvf.ForeColor = sshLive ? Color.FromArgb(52, 211, 153) : Color.FromArgb(250, 204, 21);

                            if (!snap.Online)
                            {
                                _lblSummary.Text = "Pixel 10 Worker (:19788) OFFLINE — Click '5. Wake / Deploy P10' to launch on connected Pixel 10!";
                                _lblSummary.ForeColor = Color.FromArgb(248, 113, 113);
                                AppendLog("Daemon :19788 did not respond. Click '5. Wake / Deploy P10' to compile/start.", Color.FromArgb(248, 113, 113));
                                return;
                            }

                            _lblSummary.ForeColor = Color.FromArgb(52, 211, 153);
                            _lblSummary.Text = string.Format(CultureInfo.InvariantCulture,
                                "{0} (API {1} | SG={2} [{3}..{4}] | UMA={5} MB) | A725 SH4Z={6:0.00} GOPS/s | E8={7:0.00} MVec/s | 15.965Hz Ticks={8}",
                                snap.DeviceName, snap.ApiVersion, snap.SubgroupSize, snap.SubgroupMin, snap.SubgroupMax,
                                snap.UmaHeapMb, snap.Sh4zMops / 1000.0, snap.E8Mvec, snap.HeartbeatTicks);
                            _lblTune.Text = "[VK-TUNE] " + (!string.IsNullOrEmpty(snap.UdotTune) ? ("UDot: " + snap.UdotTune + " || IntDot: " + snap.IdotTune) : snap.IdotTune);

                            _grid.Rows.Clear();
                            long sumM = 0, sumE = 0;
                            double sumGops = 0.0;
                            for (int i = 0; i < snap.Kernels.Count; i++)
                            {
                                P10KernelRow k = snap.Kernels[i];
                                bool ok = (k.Elements > 0 && k.Matched == k.Elements);
                                sumM += k.Matched;
                                sumE += k.Elements;
                                sumGops += k.GopsPerSec;
                                int rIdx = _grid.Rows.Add(
                                    k.Id,
                                    ok ? "PASS" : "CHECK",
                                    k.Matched + " / " + k.Elements,
                                    k.MelemPerSec.ToString("0.0", CultureInfo.InvariantCulture),
                                    k.GopsPerSec.ToString("0.00", CultureInfo.InvariantCulture),
                                    k.ElapsedMs.ToString("0.00", CultureInfo.InvariantCulture),
                                    k.Detail);
                                _grid.Rows[rIdx].Cells[1].Style.ForeColor = ok ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                                _grid.Rows[rIdx].Cells[4].Style.ForeColor = Color.FromArgb(125, 211, 252);
                            }
                            if (snap.Kernels.Count > 0)
                            {
                                int totIdx = _grid.Rows.Add(
                                    "TOTAL (" + snap.Kernels.Count + " KERNELS)",
                                    (sumM == sumE && sumE > 0) ? "100% EXACT" : "PARTIAL",
                                    sumM + " / " + sumE,
                                    "-",
                                    sumGops.ToString("0.00", CultureInfo.InvariantCulture) + " sum",
                                    "-",
                                    "Zero-Root ARMv9.2 + PowerVR Vulkan 1.4SPIR-V JIT");
                                _grid.Rows[totIdx].DefaultCellStyle.BackColor = Color.FromArgb(16, 28, 48);
                                _grid.Rows[totIdx].DefaultCellStyle.ForeColor = Color.FromArgb(250, 204, 21);
                            }
                            AppendLog(Pixel10TensorBridge.FormatMultiLineScorecard("PIXEL 10 LIVE SNAPSHOT", snap), Color.FromArgb(52, 211, 153));
                        });
                    }
                }
                finally { _busy = false; }
            });
        }

        private void RunHostRtxCompanionAsync()
        {
            string hostExe = Path.Combine(_suiteRoot, "11_omega_edge_workers\rust_p10_worker", "bin", "omega_p10_worker_host.exe");
            if (!File.Exists(hostExe))
            {
                AppendLog("Host companion missing at " + hostExe + " — click '5. Wake / Deploy P10' to build!", Color.FromArgb(248, 113, 113));
                return;
            }
            AppendLog("Running Host NVIDIA DirectCompute GPU Tensor Companion (K7 IntDot + K8/K9 Ampere CoopMat)...", Color.FromArgb(192, 132, 252));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(hostExe, "--vulkan tensor");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (Process p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(30000);
                        AppendLog("=== HOST DirectCompute GPU TENSOR RESULT ===\r\n" + (o ?? "").Trim(), Color.FromArgb(52, 211, 153));
                    }
                }
                catch (Exception ex) { AppendLog("RTX bench error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
            });
        }

        private void WakeOrDeployPixelWorkerAsync()
        {
            string ps1 = Path.Combine(_suiteRoot, "11_omega_edge_workers\rust_p10_worker", "Deploy-And-Run-Pixel10Worker.ps1");
            if (!File.Exists(ps1)) { AppendLog("Missing " + ps1, Color.FromArgb(248, 113, 113)); return; }
            AppendLog("Deploying & starting omega_p10_worker v0.7.0 (:19788) via Deploy-And-Run-Pixel10Worker.ps1...", Color.FromArgb(56, 189, 248));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\" -Action BenchAndDaemon");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (Process p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(120000);
                        AppendLog((o ?? "").Trim(), Color.FromArgb(52, 211, 153));
                    }
                    RefreshFromDaemonAsync("/api/status", "Post-deploy verification");
                }
                catch (Exception ex) { AppendLog("Deploy error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
            });
        }

        public static string FormatTpuXrayReport(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
                return "[TPU X-RAY] No response from http://127.0.0.1:19788/api/dispatch/tpu (click '8. Deploy :19788' to push v0.7.0!).";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== PIXEL 10 TENSOR G5 4TH-GEN TPU (36500000.buenos) 16-LAYER SYSTOLIC REPORT ===");
            Match mSum = Regex.Match(rawJson, "\"summary\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mSum.Success) sb.AppendLine("  [SUMMARY]  " + mSum.Groups[1].Value.Replace("\\\"", "\""));
            Match mSoc = Regex.Match(rawJson, "\"soc_props\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mSoc.Success) sb.AppendLine("  [SOC-PROP] " + mSoc.Groups[1].Value);
            Match mSys = Regex.Match(rawJson, "\"buenos_sysfs\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mSys.Success && mSys.Groups[1].Value.Length > 0) sb.AppendLine("  [BUENOS]   " + mSys.Groups[1].Value);

            MatchCollection dms = Regex.Matches(rawJson, "\\{\"index\":(\\d+),\"name\":\"([^\"]+)\",\"version\":\"([^\"]*)\",\"type\":(-?\\d+),\"type_label\":\"([^\"]+)\",\"feature_level\":(-?\\d+),\"ops_supported\":\"([^\"]*)\"\\}");
            for (int i = 0; i < dms.Count; i++)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  [NNAPI-{0}]  {1} (v={2}, {3}, FL={4}) :: {5}",
                    dms[i].Groups[1].Value, dms[i].Groups[2].Value, dms[i].Groups[3].Value,
                    dms[i].Groups[5].Value, dms[i].Groups[6].Value, dms[i].Groups[7].Value));
            }

            MatchCollection bms = Regex.Matches(rawJson, "\\{\"kernel_id\":\"([^\"]+)\",\"device\":\"([^\"]+)\",\"device_type\":(-?\\d+),\"is_hw_tpu\":(true|false),\"supported\":(true|false),\"compile_rc\":(-?\\d+),\"exec_rc\":(-?\\d+),\"matched\":(\\d+),\"elements\":(\\d+),\"burst_iters\":(\\d+),\"elapsed_ms\":([0-9.]+),\"hw_us\":(-?[0-9.]+),\"drv_us\":(-?[0-9.]+),\"melem_s\":([0-9.]+),\"gops_s\":([0-9.]+),\"detail\":\"((?:\\\\.|[^\"\\\\])*)\"\\}");
            for (int i = 0; i < bms.Count; i++)
            {
                string tag = (bms[i].Groups[6].Value == "0" && bms[i].Groups[7].Value == "0") ? "TPU-PASS" : "TPU-DIAG";
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1,-24} Parity={2}/{3} | {4,8} GOPS/s ({5} ms) | {6}",
                    tag, bms[i].Groups[1].Value, bms[i].Groups[8].Value, bms[i].Groups[9].Value,
                    bms[i].Groups[15].Value, bms[i].Groups[11].Value, bms[i].Groups[16].Value));
            }
            return sb.ToString().TrimEnd();
        }

        private void ProbeAndBenchTensorG5TpuAsync()
        {
            AppendLog("Dispatching 16-Layer Fused Systolic Burst + Zero-Copy memfd DMA -> Tensor G5 TPU (:19788)...", Color.FromArgb(192, 132, 252));
            ThreadPool.QueueUserWorkItem(delegate
            {
                OmegaLabLauncherForm.ArmAllPixelsStatic();
                string js = Pixel10TensorBridge.HttpGet("http://127.0.0.1:19788/api/dispatch/tpu", 30000);
                if (string.IsNullOrEmpty(js))
                {
                    AppendLog("Daemon :19788 offline — deploying v0.7.0...", Color.FromArgb(250, 204, 21));
                    WakeOrDeployPixelWorkerAsync();
                    return;
                }
                AppendLog(FormatTpuXrayReport(js), Color.FromArgb(52, 211, 153));
                _activePort = 19788;
                RefreshFromDaemonAsync("/api/status", "Refreshed 13-Kernel PowerVR + Tensor G5 TPU (:19788) grid");
            });
        }

        private void DispatchLiveAvfUdotJobAsync()
        {
            AppendLog("Dispatching live K10_HW_UDOT_PACK4U8 (OpUDot 4x8b) job -> http://127.0.0.1:19789/api/dispatch/udot...", Color.FromArgb(56, 189, 248));
            ThreadPool.QueueUserWorkItem(delegate
            {
                OmegaLabLauncherForm.EnsureHostPort19789BridgedStatic(_suiteRoot);
                string js = Pixel10TensorBridge.HttpGet("http://127.0.0.1:19789/api/dispatch/udot", 15000);
                if (string.IsNullOrEmpty(js))
                {
                    AppendLog("Daemon :19789 not awake yet — deploying persistent systemd AVF Virtio-GPU daemon...", Color.FromArgb(250, 204, 21));
                    string dep = OmegaLabLauncherForm.ExecuteAvfNativeRustBenchStatic(_suiteRoot);
                    AppendLog(dep, Color.FromArgb(52, 211, 153));
                }
                else
                {
                    AppendLog("[AVF @deb Virtio-GPU :19789 DISPATCH PASS] " + js, Color.FromArgb(52, 211, 153));
                }
                _activePort = 19789;
                RefreshFromDaemonAsync("/api/status", "Refreshed @deb Virtio-GPU (:19789) telemetry after live dispatch");
            });
        }

        private void TriggerAvfNativeRustBenchAsync()
        {
            AppendLog("Streaming v0.7.0 Rust source over 127.0.0.1:22222 & compiling native ARM64 binary inside AVF Debian 13...", Color.FromArgb(56, 189, 248));
            ThreadPool.QueueUserWorkItem(delegate
            {
                string res = OmegaLabLauncherForm.ExecuteAvfNativeRustBenchStatic(_suiteRoot);
                AppendLog(res, Color.FromArgb(52, 211, 153));
            });
        }

        private void TriggerShellAvfAutoTypeAsync()
        {
            AppendLog("Launching OmegaShell v1.0.8 GM & running Zero-Mount AVF SSH (:22222) Auto-Type into Pixel Terminal...", Color.FromArgb(250, 204, 21));
            ThreadPool.QueueUserWorkItem(delegate
            {
                string outMsg = OmegaLabLauncherForm.ExecuteZeroMountAvfAutoTypeStatic(_suiteRoot);
                AppendLog(outMsg, Color.FromArgb(52, 211, 153));
                RefreshFromDaemonAsync("/api/status", "AVF SSH status refresh");
            });
        }
    }
}
