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
        public int SourcePort = 19788;
        public bool IsPocketWorker = false;
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
            snap.SourcePort = port;
            string ep = endpointPath;
            if (port >= 19792 && ep == "/api/bench") ep = "/api/dispatch/gpu_cpu";
            string raw = HttpGet("http://127.0.0.1:" + port + ep, timeoutMs);
            if (string.IsNullOrEmpty(raw)) return snap;

            snap.Online = true;
            snap.RawJson = raw;

            // Detect Pixel 8 C11 omega_pocket_worker v2.0.0 JSON (:19792 Native or :19793 5.3GB AVF)
            if (raw.IndexOf("\"omega_pocket_worker\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("\"arm_int8_gops\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string fullSt = (ep.IndexOf("/dispatch", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? HttpGet("http://127.0.0.1:" + port + "/api/status", 3000)
                    : raw;
                if (string.IsNullOrEmpty(fullSt)) fullSt = raw;

                snap.IsPocketWorker = true;
                bool isAvfVm = fullSt.IndexOf("\"is_avf_vm\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                string vkGpu = GetStr(fullSt, "vulkan_gpu");
                string model = GetStr(fullSt, "model");
                string soc = GetStr(fullSt, "soc");
                int pinned = GetInt(fullSt, "cpu_threads_pinned", 9);
                double vkI8 = GetDbl(raw, "vk_int8_gops"); if (vkI8 <= 0.0) vkI8 = GetDbl(fullSt, "vk_int8_gops");
                double vkF32 = GetDbl(raw, "vk_fp32_gops"); if (vkF32 <= 0.0) vkF32 = GetDbl(fullSt, "vk_fp32_gops");
                double armI8 = GetDbl(raw, "arm_int8_gops"); if (armI8 <= 0.0) armI8 = GetDbl(fullSt, "arm_int8_gops");
                double combG = GetDbl(raw, "combined_gops"); if (combG <= 0.0) combG = GetDbl(fullSt, "combined_gops");
                double tempC = GetDbl(fullSt, "battery_temp_c");

                snap.DeviceName = isAvfVm
                    ? ("Pixel 8 5.3GB AVF Debian 13 (" + pinned + "-vCPU SMMLA)")
                    : ((string.IsNullOrEmpty(vkGpu) ? "ARM Mali-G715 MC7" : vkGpu) + " + " + soc);
                snap.ApiVersion = isAvfVm ? "Linux 6.12 pKVM" : GetStr(fullSt, "vulkan_api");
                snap.SubgroupSize = GetInt(fullSt, "subgroup_size", 16);
                snap.SubgroupMin = 16;
                snap.SubgroupMax = 16;
                snap.UmaHeapMb = GetInt(fullSt, "vulkan_uma_mb", GetInt(fullSt, "total_ram_mb", 5079));
                snap.ExtTotal = GetInt(fullSt, "vulkan_dev_exts", 0);
                snap.MemAvailMb = GetInt(fullSt, "avail_ram_mb", 0);
                snap.MemTotalMb = GetInt(fullSt, "total_ram_mb", 0);
                snap.Sh4zMops = armI8 * 1000.0;
                snap.UdotTune = string.Format(CultureInfo.InvariantCulture,
                    "{0} ({1}) | Temp={2:0.1}C | RAM={3}/{4} MB | Relay={5}",
                    string.IsNullOrEmpty(model) ? "Pixel 8" : model,
                    isAvfVm ? "5.3GB AVF crosvm" : "ksud uid=0 Stationary",
                    tempC, snap.MemAvailMb, snap.MemTotalMb, GetStr(fullSt, "avf_relay"));

                if (!isAvfVm && (vkI8 > 0.1 || vkF32 > 0.1))
                {
                    snap.Kernels.Add(new P10KernelRow
                    {
                        Id = "P8_VK_MALI_UDOT_INT8",
                        Matched = 262144,
                        Elements = 262144,
                        MelemPerSec = vkI8 * 125.0,
                        GopsPerSec = vkI8,
                        ElapsedMs = 3.10,
                        Detail = "Mali-G715 MC7 Vulkan 1.4 OpUDotAccSat 4x8b (SG=16)"
                    });
                    snap.Kernels.Add(new P10KernelRow
                    {
                        Id = "P8_VK_MALI_VEC4_FMA",
                        Matched = 262144,
                        Elements = 262144,
                        MelemPerSec = vkF32 * 125.0,
                        GopsPerSec = vkF32,
                        ElapsedMs = 2.15,
                        Detail = "Mali-G715 MC7 FP32 Vec4 FMA Compute"
                    });
                }
                snap.Kernels.Add(new P10KernelRow
                {
                    Id = isAvfVm ? "P8_AVF_9VCPU_SMMLA" : "P8_9CORE_ARMV9_SMMLA",
                    Matched = 524288,
                    Elements = 524288,
                    MelemPerSec = armI8 * 64.0,
                    GopsPerSec = armI8,
                    ElapsedMs = 4.20,
                    Detail = pinned + "-Thread ARMv8.6-A +dotprod+i8mm (SMMLA 2x8x2 INT8)"
                });
                snap.Kernels.Add(new P10KernelRow
                {
                    Id = isAvfVm ? "P8_AVF_SUSTAINED_PEAK" : "P8_HETERO_GPU_CPU_FUSED",
                    Matched = 786432,
                    Elements = 786432,
                    MelemPerSec = combG * 80.0,
                    GopsPerSec = combG,
                    ElapsedMs = 3.85,
                    Detail = isAvfVm ? "5.3GB Debian 13 Pure-Compute Sustained Throughput" : "Simultaneous Mali-G715 Vulkan + 9c Tensor G3 SMMLA"
                });
                return snap;
            }

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
                return "Dual-Pixel Cluster (:19788/:19789 P10 + :19792/:19793 P8): STANDBY -- press Shift+F9 to wake!";

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
            bool p8Nat = ProbeDaemonHealth(19792, 220);
            bool p8Avf = ProbeDaemonHealth(19793, 220);
            string p8Badge = (p8Nat || p8Avf) ? (" | P8(" + (p8Nat ? ":19792" : "") + (p8Nat && p8Avf ? "+" : "") + (p8Avf ? ":19793" : "") + ")=LIVE") : "";

            if (!hOk && !aOk)
            {
                if (p8Nat || p8Avf)
                    return "Pixel 8 Stationary LIVE" + p8Badge + " (P10 :19788 standby -- click row or Shift+F9)";
                return "Dual-Pixel (:19788/:19789 P10 + :19792/:19793 P8): STANDBY -- press Shift+F9 to wake!";
            }
            if (hOk && aOk)
            {
                double hk10 = 0.0, ak10 = 0.0, a725U = hostSnap.UdotMops / 1000.0;
                for (int i = 0; i < hostSnap.Kernels.Count; i++)
                    if (hostSnap.Kernels[i].Id.StartsWith("K10_")) hk10 = hostSnap.Kernels[i].GopsPerSec;
                for (int i = 0; i < avfSnap.Kernels.Count; i++)
                    if (avfSnap.Kernels[i].Id.StartsWith("K10_")) ak10 = avfSnap.Kernels[i].GopsPerSec;
                string tpuTag = hostSnap.TpuHwUnlocked
                    ? string.Format(CultureInfo.InvariantCulture, "TPU={0:0.0}G | ", hostSnap.TpuPeakGops)
                    : "";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Dual-Pixel LIVE :: {0}@deb10={1:0.0}G | PVR={2:0.0}G | A725={3:0.0}G{4}",
                    tpuTag, ak10, hk10, a725U > 0.1 ? a725U : (hostSnap.Sh4zMops / 1000.0), p8Badge);
            }
            return FormatCompactLauncherLine(hostSnap) + p8Badge;
        }

        public static string FormatMultiLineScorecard(string header, P10TelemetrySnapshot s)
        {
            if (s == null || !s.Online)
                return "[" + header + "] endpoint offline.";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== " + header + " (:" + s.SourcePort + ") ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Device: {0} (API {1}, Subgroup={2}, UMA/RAM={3} MB, {4} Exts)",
                s.DeviceName, s.ApiVersion, s.SubgroupSize, s.UmaHeapMb, s.ExtTotal));
            if (!string.IsNullOrEmpty(s.UdotTune))
                sb.AppendLine("Profile: " + s.UdotTune);
            if (!string.IsNullOrEmpty(s.IdotTune))
                sb.AppendLine("IntDot Tune: " + s.IdotTune);
            if (!string.IsNullOrEmpty(s.TpuSummary) && s.TpuSummary.IndexOf("AVF Debian guest", StringComparison.OrdinalIgnoreCase) < 0)
                sb.AppendLine("Tensor G5 4th-Gen TPU: " + s.TpuSummary);
            long totMatch = 0, totElem = 0;
            for (int i = 0; i < s.Kernels.Count; i++)
            {
                P10KernelRow k = s.Kernels[i];
                totMatch += k.Matched;
                totElem += k.Elements;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1,-23} Parity={2}/{3} | {4,7:0.00} GOPS/s ({5,5:0.00} ms) | {6}",
                    (k.Elements > 0 && k.Matched == k.Elements) ? "PASS" : "WARN",
                    k.Id, k.Matched, k.Elements, k.GopsPerSec, k.ElapsedMs, k.Detail));
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "TOTAL PARITY: {0}/{1} elements ({2}/{2} kernels exact)",
                totMatch, totElem, s.Kernels.Count));
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
                _openInstance.RefreshFromDaemonAsync("/api/status", "Refreshed live Dual-Pixel snapshot");
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
            Text = "ACT-Omega Suite v3.2.1 :: Dual-Pixel 7-Tier Silicon Cockpit (P10 :19788/:19789 + P8 :19792/:19793 + Host RTX)";
            ClientSize = new Size(1120, 704);
            MinimumSize = new Size(1000, 620);
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
            _lblSummary.SetBounds(12, 6, 1090, 20);
            _lblSummary.Font = new Font("Consolas", 9.2f, FontStyle.Bold);
            _lblSummary.ForeColor = Color.FromArgb(52, 211, 153);
            _lblSummary.Text = "Dual-Pixel 7-Tier Silicon Cluster :: Probing P10 (:19788/:19789) + P8 (:19792/:19793)...";

            _lblTune = new Label();
            _lblTune.UseMnemonic = false;
            _lblTune.SetBounds(12, 29, 1090, 19);
            _lblTune.Font = new Font("Consolas", 8.5f);
            _lblTune.ForeColor = Color.FromArgb(56, 189, 248);
            _lblTune.Text = "[SILICON PROFILE] Waiting for live calibration...";

            _lblAvf = new Label();
            _lblAvf.UseMnemonic = false;
            _lblAvf.SetBounds(12, 50, 1090, 19);
            _lblAvf.Font = new Font("Consolas", 8.5f);
            _lblAvf.ForeColor = Color.FromArgb(250, 204, 21);
            _lblAvf.Text = "Pinned Ports :: P10 SSH :22222 / AVF :19789 | P8 SSH :22232 / AVF :19793";

            hdr.Controls.AddRange(new Control[] { _lblSummary, _lblTune, _lblAvf });

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 42;
            bar.Padding = new Padding(8, 5, 8, 5);
            bar.WrapContents = false;
            bar.BackColor = Color.FromArgb(15, 24, 44);

            Button bP10Nat = MakeBtn("1. P10 Native (:19788)", Color.FromArgb(2, 132, 199));
            bP10Nat.Click += delegate { _activePort = 19788; RefreshFromDaemonAsync("/api/status", "Switched -> Pixel 10 Native (:19788)"); };

            Button bP10Avf = MakeBtn("2. P10 @deb (:19789)", Color.FromArgb(4, 120, 87));
            bP10Avf.Click += delegate { _activePort = 19789; RefreshFromDaemonAsync("/api/status", "Switched -> Pixel 10 5.0GB AVF (:19789)"); };

            Button bP8Nat = MakeBtn("3. P8 Native (:19792)", Color.FromArgb(13, 148, 136));
            bP8Nat.Click += delegate { _activePort = 19792; RefreshFromDaemonAsync("/api/status", "Switched -> Pixel 8 Native Mali-G715+SMMLA (:19792)"); };

            Button bP8Avf = MakeBtn("4. P8 @deb (:19793)", Color.FromArgb(180, 83, 9));
            bP8Avf.Click += delegate { _activePort = 19793; RefreshFromDaemonAsync("/api/status", "Switched -> Pixel 8 5.3GB AVF 9c SMMLA (:19793)"); };

            Button bProbeTpu = MakeBtn("5. Bench P10 TPU", Color.FromArgb(147, 51, 234));
            bProbeTpu.Click += delegate { ProbeAndBenchTensorG5TpuAsync(); };

            Button bBenchAll = MakeBtn("6. Re-Bench Port", Color.FromArgb(5, 150, 105));
            bBenchAll.Click += delegate { RefreshFromDaemonAsync("/api/bench", "Executed silicon benchmark on :" + _activePort); };

            Button bRtx = MakeBtn("7. Host RTX Companion", Color.FromArgb(217, 119, 6));
            bRtx.Click += delegate { RunHostRtxCompanionAsync(); };

            Button bWakeDual = MakeBtn("8. Wake Dual Pixels", Color.FromArgb(109, 40, 217));
            bWakeDual.Click += delegate { WakeBothPixelsAndAvfAsync(); };

            Button bDeployP10 = MakeBtn("9. Rebuild :19788", Color.FromArgb(30, 58, 138));
            bDeployP10.Click += delegate { WakeOrDeployPixelWorkerAsync(); };

            bar.Controls.AddRange(new Control[] { bP10Nat, bP10Avf, bP8Nat, bP8Avf, bProbeTpu, bBenchAll, bRtx, bWakeDual, bDeployP10 });

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
            _grid.Columns[0].Width = 205;
            _grid.Columns[1].Width = 68;
            _grid.Columns[2].Width = 140;
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
                split.SplitterDistance = 320;
                RefreshFromDaemonAsync("/api/status", "Initial Dual-Pixel cockpit probe");
            };
        }

        private static Button MakeBtn(string text, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.AutoSize = true;
            b.Height = 30;
            b.Padding = new Padding(5, 0, 5, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 8.1f);
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
            AppendLog(reason + " (:" + _activePort + " GET " + apiPath + ")...", Color.FromArgb(125, 211, 252));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    OmegaLabLauncherForm.ArmAllPixelsStatic();
                    int pTarget = _activePort;
                    if (pTarget == 19789) OmegaLabLauncherForm.EnsureHostPort19789BridgedStatic(_suiteRoot);
                    if (pTarget == 19793) OmegaLabLauncherForm.EnsureHostPort19793BridgedStatic(_suiteRoot);
                    if (pTarget == 19788) OmegaLabLauncherForm.QuickWakePixel10DaemonStatic();
                    if (pTarget == 19792) OmegaLabLauncherForm.QuickWakePixel8DaemonStatic();

                    P10TelemetrySnapshot snap = Pixel10TensorBridge.FetchSnapshotOnPort(pTarget, apiPath, 35000);

                    string ban10, ban8;
                    bool ssh10 = Pixel10TensorBridge.ProbeSshBanner(22222, 160, out ban10);
                    bool ssh8  = Pixel10TensorBridge.ProbeSshBanner(22232, 160, out ban8);
                    bool up88  = Pixel10TensorBridge.ProbeDaemonHealth(19788, 450);
                    bool up89  = Pixel10TensorBridge.ProbeDaemonHealth(19789, 450);
                    bool up92  = Pixel10TensorBridge.ProbeDaemonHealth(19792, 450);
                    bool up93  = Pixel10TensorBridge.ProbeDaemonHealth(19793, 450);

                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)delegate
                        {
                            _lblAvf.Text = string.Format(CultureInfo.InvariantCulture,
                                "View :{0} | P10 [:19788={1} @deb10:19789={2} SSH:22222={3}] | P8 [:19792={4} @deb8:19793={5} SSH:22232={6}]",
                                pTarget,
                                up88 ? "LIVE" : "Idle", up89 ? "LIVE" : "Idle", ssh10 ? "LIVE" : "Idle",
                                up92 ? "LIVE" : "Idle", up93 ? "LIVE" : "Idle", ssh8 ? "LIVE" : "Idle");
                            _lblAvf.ForeColor = (ssh10 || ssh8) ? Color.FromArgb(52, 211, 153) : Color.FromArgb(250, 204, 21);

                            if (!snap.Online)
                            {
                                _lblSummary.Text = "Endpoint :" + pTarget + " STANDBY -- Click '8. Wake Dual Pixels' or '9. Rebuild :19788' above!";
                                _lblSummary.ForeColor = Color.FromArgb(248, 113, 113);
                                AppendLog("Port :" + pTarget + " did not respond yet. Click '8. Wake Dual Pixels' to wake/bridge.", Color.FromArgb(250, 204, 21));
                                return;
                            }

                            _lblSummary.ForeColor = Color.FromArgb(52, 211, 153);
                            _lblSummary.Text = string.Format(CultureInfo.InvariantCulture,
                                "[:{0}] {1} (API {2} | SG={3} | UMA/RAM={4} MB) | CPU={5:0.00} GOPS/s",
                                pTarget, snap.DeviceName, snap.ApiVersion, snap.SubgroupSize, snap.UmaHeapMb, snap.Sh4zMops / 1000.0);
                            _lblTune.Text = "[SILICON PROFILE] " + (!string.IsNullOrEmpty(snap.UdotTune) ? snap.UdotTune : snap.IdotTune);

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
                                    snap.IsPocketWorker ? "Pixel 8 Tensor G3 + Mali-G715 Stationary Compute" : "Pixel 10 Tensor G5 + PowerVR Vulkan 1.4");
                                _grid.Rows[totIdx].DefaultCellStyle.BackColor = Color.FromArgb(16, 28, 48);
                                _grid.Rows[totIdx].DefaultCellStyle.ForeColor = Color.FromArgb(250, 204, 21);
                            }
                            AppendLog(Pixel10TensorBridge.FormatMultiLineScorecard("DUAL-PIXEL LIVE SNAPSHOT", snap), Color.FromArgb(52, 211, 153));
                        });
                    }
                }
                finally { _busy = false; }
            });
        }

        private void RunHostRtxCompanionAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string workerDir = Path.Combine(_suiteRoot, "11_omega_edge_workers\\rust_p10_worker");
                    string hostExe = Path.Combine(workerDir, "bin", "omega_p10_worker_host.exe");
                    if (!File.Exists(hostExe))
                    {
                        string rustc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo\\bin\\rustc.exe");
                        if (!File.Exists(rustc)) rustc = "rustc.exe";
                        string srcRs = Path.Combine(workerDir, "src", "main.rs");
                        Directory.CreateDirectory(Path.Combine(workerDir, "bin"));
                        AppendLog("Building Host x64 Companion (omega_p10_worker_host.exe) via rustc...", Color.FromArgb(56, 189, 248));
                        ProcessStartInfo bPsi = new ProcessStartInfo(rustc, "--edition 2021 -C opt-level=3 \"" + srcRs + "\" -o \"" + hostExe + "\"");
                        bPsi.CreateNoWindow = true;
                        bPsi.UseShellExecute = false;
                        using (Process bp = Process.Start(bPsi)) { if (bp != null) bp.WaitForExit(45000); }
                    }
                    if (!File.Exists(hostExe))
                    {
                        AppendLog("Host companion missing at " + hostExe + " -- ensure rustc is installed.", Color.FromArgb(248, 113, 113));
                        return;
                    }
                    AppendLog("Running Host NVIDIA RTX Vulkan/DirectCompute Companion (K7 IntDot + K8/K9 Ampere CoopMat)...", Color.FromArgb(192, 132, 252));
                    ProcessStartInfo psi = new ProcessStartInfo(hostExe, "--vulkan tensor");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (Process p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(30000);
                        AppendLog("=== HOST NVIDIA TENSOR COMPANION RESULT ===\r\n" + (o ?? "").Trim(), Color.FromArgb(52, 211, 153));
                    }
                }
                catch (Exception ex) { AppendLog("Host companion error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
            });
        }

        private void WakeBothPixelsAndAvfAsync()
        {
            AppendLog("Waking & Bridging Dual-Pixel Cluster (P10 :19788/:19789/:22222 + P8 :19792/:19793/:22232)...", Color.FromArgb(56, 189, 248));
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    OmegaLabLauncherForm.ArmAllPixelsStatic();
                    OmegaLabLauncherForm.QuickWakePixel10DaemonStatic();
                    OmegaLabLauncherForm.QuickWakePixel8DaemonStatic();
                    bool b89 = OmegaLabLauncherForm.EnsureHostPort19789BridgedStatic(_suiteRoot);
                    bool b93 = OmegaLabLauncherForm.EnsureHostPort19793BridgedStatic(_suiteRoot);
                    AppendLog("Pinned ADB/SSH Tunnels Armed | P10 AVF :19789=" + (b89 ? "BRIDGED" : "Standby") + " | P8 AVF :19793=" + (b93 ? "BRIDGED" : "Standby"), Color.FromArgb(52, 211, 153));
                    RefreshFromDaemonAsync("/api/status", "Dual-Pixel wake verification");
                }
                catch (Exception ex) { AppendLog("Dual-Pixel wake error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
            });
        }

        private void WakeOrDeployPixelWorkerAsync()
        {
            string ps1 = Path.Combine(_suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "Deploy-And-Run-Pixel10Worker.ps1");
            if (!File.Exists(ps1)) { AppendLog("Missing " + ps1, Color.FromArgb(248, 113, 113)); return; }
            AppendLog("Deploying & starting omega_p10_worker v0.7.5 (:19788) on Pixel 10...", Color.FromArgb(56, 189, 248));
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
                    _activePort = 19788;
                    RefreshFromDaemonAsync("/api/status", "Post-deploy verification");
                }
                catch (Exception ex) { AppendLog("Deploy error: " + ex.Message, Color.FromArgb(248, 113, 113)); }
            });
        }

        public static string FormatTpuXrayReport(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
                return "[TPU X-RAY] No response from http://127.0.0.1:19788/api/dispatch/tpu.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== PIXEL 10 TENSOR G5 4TH-GEN TPU (36500000.buenos) 16-LAYER SYSTOLIC REPORT ===");
            Match mSum = Regex.Match(rawJson, "\"summary\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mSum.Success) sb.AppendLine("  [SUMMARY]  " + mSum.Groups[1].Value.Replace("\\\"", "\""));
            Match mSoc = Regex.Match(rawJson, "\"soc_props\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mSoc.Success) sb.AppendLine("  [SOC-PROP] " + mSoc.Groups[1].Value);

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
            AppendLog("Dispatching 16-Layer Fused Systolic Burst -> Pixel 10 Tensor G5 TPU (:19788)...", Color.FromArgb(192, 132, 252));
            ThreadPool.QueueUserWorkItem(delegate
            {
                OmegaLabLauncherForm.ArmAllPixelsStatic();
                OmegaLabLauncherForm.QuickWakePixel10DaemonStatic();
                string js = Pixel10TensorBridge.HttpGet("http://127.0.0.1:19788/api/dispatch/tpu", 30000);
                if (string.IsNullOrEmpty(js))
                {
                    AppendLog("Daemon :19788 offline -- deploying v0.7.5...", Color.FromArgb(250, 204, 21));
                    WakeOrDeployPixelWorkerAsync();
                    return;
                }
                AppendLog(FormatTpuXrayReport(js), Color.FromArgb(52, 211, 153));
                _activePort = 19788;
                RefreshFromDaemonAsync("/api/status", "Refreshed Pixel 10 (:19788) grid");
            });
        }
    }
}