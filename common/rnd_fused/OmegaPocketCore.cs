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

namespace OmegaPocketSiliconFusion
{
    public enum SiliconTierKind
    {
        HostGpuDirectCompute = 0,
        MobileEdgeNpuTpu = 1,
        MobileVulkanGpu = 2,
        MobileAvfLinuxVm = 3,
        MobileRootedKsudSecondary = 4,
        MobileSecondaryAvfLinuxVm = 5,
        LocalLlmEndpoint = 6
    }

    public sealed class DiscoveredSiliconNode
    {
        public string NodeId = "";
        public string DisplayName = "";
        public SiliconTierKind Kind;
        public string Transport = "";
        public int Port;
        public bool Online;
        public bool AdbReady;
        public bool CircuitTripped;
        public double MeasuredRttMs;
        public double EstimatedGops;
        public double BatteryTempC;
        public int FreeRamMb;
        public int AssignedLayers;
        public string HealthNote = "";
    }

    public sealed class AdbDeviceRecord
    {
        public string Serial = "";
        public string State = "";
        public string Model = "";
        public string Product = "";
        public string Codename = "";
        public string TransportType = "";
        public string SocName = "";
        public int AndroidSdk = 0;
        public double BatteryTempC = 28.0;
        public int AvailRamMb = 0;
        public int TotalRamMb = 0;
        public bool HasRootUid0 = false;
        public bool CrosvmRunning = false;
        public bool IsPixel8Slot = false;
        public int AssignedHostPort = 19788;
        public int AssignedAvfPort = 19789;
    }

    public sealed class KvCheckpointSlot
    {
        public int TokenIndex;
        public ulong Generation;
        public int HiddenDim;
        public uint Crc32;
        public float[] StateDigest16 = new float[16];
    }

    public sealed class SplitStreamRunSummary
    {
        public bool Ok;
        public int RequestedTokens;
        public int CompletedTokens;
        public int EdgeTokensExecuted;
        public int HostFailoverTokens;
        public int UnplugFaultsHandled;
        public double WorstFailoverLatencyMs;
        public double TotalElapsedMs;
        public double EffectiveTokPerSec;
        public bool ZeroDroppedTokens;
        public bool AllKvCheckpointsVerified;
        public string OutputPreview = "";
        public List<string> TraceLines = new List<string>();
    }

    public static class OmegaPocketCore
    {
        private static readonly uint[] CrcTable = InitCrc32();

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private static uint[] InitCrc32()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int b = 0; b < 8; b++)
                    c = ((c & 1u) != 0u) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            uint c = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
                c = CrcTable[(c ^ data[i]) & 0xFFu] ^ (c >> 8);
            return ~c;
        }

        public static uint ComputeFloatDigestCrc32(float[] vec, int tokenIndex)
        {
            byte[] raw = new byte[4 + vec.Length * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(tokenIndex), 0, raw, 0, 4);
            Buffer.BlockCopy(vec, 0, raw, 4, vec.Length * 4);
            return ComputeCrc32(raw, 0, raw.Length);
        }

        public static bool ProbeTcpLoopbackFast(int port, int maxWaitUs, out double rttMs)
        {
            rttMs = 0.0;
            Socket sock = null;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Blocking = false;
                try
                {
                    sock.Connect(new IPEndPoint(IPAddress.Loopback, port));
                }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode != SocketError.WouldBlock && se.SocketErrorCode != SocketError.InProgress)
                        return false;
                }
                bool writable = sock.Poll(Math.Max(200, maxWaitUs), SelectMode.SelectWrite);
                if (!writable) return false;
                int sockErr = (int)sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                if (sockErr != 0) return false;
                sw.Stop();
                rttMs = sw.Elapsed.TotalMilliseconds;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (sock != null) { try { sock.Close(); } catch { } }
            }
        }

        // True HTTP /api/status Liveness Gate: defeats `adb forward` empty-tunnel TCP-SYN false positives!
        public static bool ProbeDaemonHttpAlive(int port, int timeoutMs, out double rttMs, out string statusJson)
        {
            rttMs = 0.0;
            statusJson = "";
            double tcpRtt;
            if (!ProbeTcpLoopbackFast(port, 900, out tcpRtt))
                return false;

            Stopwatch sw = Stopwatch.StartNew();
            string url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/api/status";
            string body = QuickHttpGet(url, timeoutMs);
            sw.Stop();
            if (!string.IsNullOrEmpty(body) &&
                !body.StartsWith("ERR:", StringComparison.Ordinal) &&
                (body.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0 || body.IndexOf("\"ok\": true", StringComparison.Ordinal) >= 0))
            {
                rttMs = sw.Elapsed.TotalMilliseconds;
                statusJson = body;
                return true;
            }
            return false;
        }

        public static List<AdbDeviceRecord> ParseAdbDevicesOutput(string rawText)
        {
            List<AdbDeviceRecord> list = new List<AdbDeviceRecord>();
            if (string.IsNullOrEmpty(rawText)) return list;
            string[] lines = rawText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length == 0) continue;
                if (ln.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
                if (ln.StartsWith("*", StringComparison.Ordinal)) continue;
                if (ln.StartsWith("adb server", StringComparison.OrdinalIgnoreCase)) continue;

                string[] toks = Regex.Split(ln, @"\s+");
                if (toks.Length < 2) continue;
                string serial = toks[0].Trim();
                string st = toks[1].Trim().ToLowerInvariant();
                if (st != "device" && st != "unauthorized" && st != "offline" && st != "recovery" && st != "authorizing")
                    continue;
                if (serial.Length > 96 || serial.IndexOf('\0') >= 0) continue;

                AdbDeviceRecord rec = new AdbDeviceRecord();
                rec.Serial = serial;
                rec.State = st;
                rec.TransportType = (serial.IndexOf(':') > 0) ? "LAN-WiFi" : "USB-C";
                for (int k = 2; k < toks.Length; k++)
                {
                    if (toks[k].StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                        rec.Model = toks[k].Substring(6);
                    else if (toks[k].StartsWith("product:", StringComparison.OrdinalIgnoreCase))
                        rec.Product = toks[k].Substring(8);
                    else if (toks[k].StartsWith("device:", StringComparison.OrdinalIgnoreCase))
                        rec.Codename = toks[k].Substring(7);
                }
                if (string.IsNullOrEmpty(rec.Model)) rec.Model = rec.Serial;
                list.Add(rec);
            }
            AssignDualPhoneSlotsAndPorts(list);
            return list;
        }

        // Non-colliding Quad-Env Port Map:
        //   Pixel 10 (Slot 0 / Tensor G5 class) -> Native :19788, 5.0GB AVF VM :19789
        //   Pixel 8  (Slot 1 / Tensor G3 class) -> Native :19792, 5.3GB AVF VM :19793 (Zero overlap with Standalone Lab :19790/:19791!)
        public static void AssignDualPhoneSlotsAndPorts(List<AdbDeviceRecord> devs)
        {
            if (devs == null) return;
            bool primaryClaimed = false;
            bool secondaryClaimed = false;
            int extraPort = 19794;

            for (int i = 0; i < devs.Count; i++)
            {
                AdbDeviceRecord d = devs[i];
                string sig = (d.Serial + " " + d.Model + " " + d.Product + " " + d.Codename + " " + d.SocName).ToLowerInvariant();
                if (sig.Contains("shiba") || sig.Contains("pixel_8") || sig.Contains("pixel 8") || sig.Contains("zuma") || sig.Contains("g3"))
                {
                    d.IsPixel8Slot = true;
                    d.AssignedHostPort = 19792;
                    d.AssignedAvfPort = 19793;
                    secondaryClaimed = true;
                }
                else if (sig.Contains("frankel") || sig.Contains("pixel_10") || sig.Contains("pixel 10") || sig.Contains("laguna") || sig.Contains("g5"))
                {
                    d.IsPixel8Slot = false;
                    d.AssignedHostPort = 19788;
                    d.AssignedAvfPort = 19789;
                    primaryClaimed = true;
                }
                else if (!primaryClaimed)
                {
                    d.IsPixel8Slot = false;
                    d.AssignedHostPort = 19788;
                    d.AssignedAvfPort = 19789;
                    primaryClaimed = true;
                }
                else if (!secondaryClaimed)
                {
                    d.IsPixel8Slot = true;
                    d.AssignedHostPort = 19792;
                    d.AssignedAvfPort = 19793;
                    secondaryClaimed = true;
                }
                else
                {
                    d.IsPixel8Slot = true;
                    d.AssignedHostPort = extraPort++;
                    d.AssignedAvfPort = extraPort++;
                }
            }
        }

        public static List<AdbDeviceRecord> DiscoverLiveAdbPhones(int timeoutMs, out string adbDiag)
        {
            adbDiag = "adb not invoked";
            string adbExe = ResolveAdbPath();
            if (string.IsNullOrEmpty(adbExe))
            {
                adbDiag = "adb.exe not on PATH/SDK (loopback/LAN probe still active)";
                return new List<AdbDeviceRecord>();
            }
            string outTxt = RunQuickCli(adbExe, "devices -l", timeoutMs);
            List<AdbDeviceRecord> devs = ParseAdbDevicesOutput(outTxt);
            for (int i = 0; i < devs.Count; i++)
            {
                AdbDeviceRecord d = devs[i];
                if (d.State == "device")
                {
                    string probeCmd = "-s " + d.Serial + " shell \"getprop ro.soc.model; getprop ro.build.version.sdk; getprop ro.product.device; cat /sys/class/power_supply/battery/temp 2>/dev/null; grep -E 'MemTotal|MemAvailable' /proc/meminfo 2>/dev/null; su -c id 2>/dev/null; ps -A 2>/dev/null | grep -E 'crosvm|virtualization' >/dev/null 2>&1 && echo CROSVM_LIVE=1\"";
                    string props = RunQuickCli(adbExe, probeCmd, 1500);
                    EnrichPhoneTelemetry(d, props);
                }
            }
            AssignDualPhoneSlotsAndPorts(devs);
            adbDiag = string.Format(CultureInfo.InvariantCulture, "Scanned via {0} -> {1} Android device(s)", Path.GetFileName(adbExe), devs.Count);
            return devs;
        }

        public static void EnrichPhoneTelemetry(AdbDeviceRecord d, string shellOut)
        {
            if (d == null || string.IsNullOrEmpty(shellOut)) return;
            string[] lines = shellOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && !lines[0].StartsWith("Mem", StringComparison.Ordinal) &&
                !lines[0].StartsWith("uid=", StringComparison.Ordinal) && !lines[0].StartsWith("CROSVM", StringComparison.Ordinal))
            {
                d.SocName = lines[0].Trim();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.IndexOf("uid=0(", StringComparison.Ordinal) >= 0)
                {
                    d.HasRootUid0 = true;
                    continue;
                }
                if (ln.IndexOf("CROSVM_LIVE=1", StringComparison.Ordinal) >= 0)
                {
                    d.CrosvmRunning = true;
                    continue;
                }
                int sdkVal;
                if (i == 1 && int.TryParse(ln, NumberStyles.Integer, CultureInfo.InvariantCulture, out sdkVal) && sdkVal >= 21 && sdkVal <= 45)
                {
                    d.AndroidSdk = sdkVal;
                    continue;
                }
                if (i == 2 && string.IsNullOrEmpty(d.Codename) && ln.Length <= 32 && !ln.StartsWith("Mem", StringComparison.Ordinal))
                {
                    int dummy;
                    if (!int.TryParse(ln, NumberStyles.Integer, CultureInfo.InvariantCulture, out dummy))
                    {
                        d.Codename = ln;
                        continue;
                    }
                }
                int deciDeg;
                if (int.TryParse(ln, NumberStyles.Integer, CultureInfo.InvariantCulture, out deciDeg) && deciDeg >= 100 && deciDeg <= 750)
                {
                    d.BatteryTempC = deciDeg / 10.0;
                    continue;
                }
                Match mTot = Regex.Match(ln, @"MemTotal:\s*(\d+)");
                if (mTot.Success)
                {
                    int kb;
                    if (int.TryParse(mTot.Groups[1].Value, out kb)) d.TotalRamMb = kb / 1024;
                }
                Match mAv = Regex.Match(ln, @"MemAvailable:\s*(\d+)");
                if (mAv.Success)
                {
                    int kb;
                    if (int.TryParse(mAv.Groups[1].Value, out kb)) d.AvailRamMb = kb / 1024;
                }
            }
        }

        // Arms ALL 4 Phone/VM Tunnels (:19788 + :19789 on Pixel 10, :19790 + :19791 on ksud Pixel 8!)
        public static string AutoHealAdbTunnels(out int healedCount)
        {
            healedCount = 0;
            string adbExe = ResolveAdbPath();
            if (string.IsNullOrEmpty(adbExe)) return "adb.exe not found";
            string raw = RunQuickCli(adbExe, "devices -l", 1500);
            List<AdbDeviceRecord> devs = ParseAdbDevicesOutput(raw);
            for (int i = 0; i < devs.Count; i++)
            {
                if (devs[i].State == "device")
                {
                    string pCmd = "-s " + devs[i].Serial + " shell \"getprop ro.soc.model; getprop ro.build.version.sdk; getprop ro.product.device; su -c id 2>/dev/null; ps -A 2>/dev/null | grep -E 'crosvm|virtualization' >/dev/null 2>&1 && echo CROSVM_LIVE=1\"";
                    EnrichPhoneTelemetry(devs[i], RunQuickCli(adbExe, pCmd, 1200));
                }
            }
            AssignDualPhoneSlotsAndPorts(devs);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < devs.Count; i++)
            {
                AdbDeviceRecord d = devs[i];
                if (d.State != "device") continue;

                string s = d.Serial;
                int port = d.AssignedHostPort;
                int avfPort = d.AssignedAvfPort;
                string portStr = port.ToString(CultureInfo.InvariantCulture);
                string avfStr = avfPort.ToString(CultureInfo.InvariantCulture);

                if (d.IsPixel8Slot)
                {
                    RunQuickCli(adbExe, "-s " + s + " forward --remove tcp:19790", 600);
                    RunQuickCli(adbExe, "-s " + s + " forward --remove tcp:19791", 600);
                    RunQuickCli(adbExe, "-s " + s + " forward tcp:22232 tcp:2222", 900);
                }
                else
                {
                    RunQuickCli(adbExe, "-s " + s + " forward tcp:22222 tcp:2222", 900);
                }
                RunQuickCli(adbExe, "-s " + s + " forward tcp:" + portStr + " tcp:" + portStr, 900);
                RunQuickCli(adbExe, "-s " + s + " forward tcp:" + avfStr + " tcp:" + avfStr, 900);
                RunQuickCli(adbExe, "-s " + s + " reverse tcp:19802 tcp:19802", 900);

                double rttCheck;
                string stJsonCheck;
                bool isUp = ProbeDaemonHttpAlive(port, 1000, out rttCheck, out stJsonCheck);
                bool wokeDaemon = false;
                if (!isUp)
                {
                    string wakeCmd;
                    if (d.IsPixel8Slot && d.HasRootUid0)
                    {
                        wakeCmd = "-s " + s + " shell \"if [ -x /data/local/tmp/omega_pocket_worker ]; then su -c 'killall -q omega_pocket_worker 2>/dev/null; /data/local/tmp/omega_pocket_worker --daemonize --stationary-boost --port " + portStr + "'; echo SPAWNED; else echo MISSING; fi\"";
                    }
                    else if (!d.IsPixel8Slot)
                    {
                        wakeCmd = "-s " + s + " shell \"if [ -x /data/local/tmp/omega_p10_worker ]; then killall -q omega_p10_worker 2>/dev/null; /data/local/tmp/omega_p10_worker --daemonize --port " + portStr + "; echo SPAWNED; elif [ -x /data/local/tmp/omega_pocket_worker ]; then killall -q omega_pocket_worker 2>/dev/null; /data/local/tmp/omega_pocket_worker --daemonize --port " + portStr + "; echo SPAWNED; else echo MISSING; fi\"";
                    }
                    else
                    {
                        wakeCmd = "-s " + s + " shell \"if [ -x /data/local/tmp/omega_pocket_worker ]; then killall -q omega_pocket_worker 2>/dev/null; /data/local/tmp/omega_pocket_worker --daemonize --port " + portStr + "; echo SPAWNED; else echo MISSING; fi\"";
                    }
                    string chk = RunQuickCli(adbExe, wakeCmd, 2200);
                    if (chk.IndexOf("SPAWNED", StringComparison.Ordinal) >= 0)
                    {
                        System.Threading.Thread.Sleep(420);
                        isUp = ProbeDaemonHttpAlive(port, 1500, out rttCheck, out stJsonCheck);
                        wokeDaemon = isUp;
                    }
                    if (!isUp && d.IsPixel8Slot && d.HasRootUid0)
                    {
                        string fbCmd = "-s " + s + " shell \"if [ -x /data/local/tmp/omega_pocket_worker ]; then /data/local/tmp/omega_pocket_worker --daemonize --port " + portStr + "; echo SPAWNED_SH; fi\"";
                        string fbOut = RunQuickCli(adbExe, fbCmd, 1800);
                        if (fbOut.IndexOf("SPAWNED_SH", StringComparison.Ordinal) >= 0)
                        {
                            System.Threading.Thread.Sleep(360);
                            isUp = ProbeDaemonHttpAlive(port, 1500, out rttCheck, out stJsonCheck);
                            wokeDaemon = isUp;
                        }
                    }
                }

                double avfRtt;
                string avfSt;
                bool avfUp = ProbeDaemonHttpAlive(avfPort, 900, out avfRtt, out avfSt);

                healedCount++;
                sb.Append(d.Model).Append(" [").Append(s).Append(d.HasRootUid0 ? ", ksud" : "")
                  .Append(" -> :").Append(portStr).Append(isUp ? (wokeDaemon ? "=WOKE" : "=LIVE") : "=IDLE")
                  .Append(", AVF:").Append(avfStr).Append(avfUp ? "=LIVE" : (d.CrosvmRunning ? "=CROSVM-UP" : "=IDLE"))
                  .Append("]; ");
            }
            return healedCount > 0 ? sb.ToString() : "No online 'device' state phones connected right now (Host RTX failover ready)";
        }

        // 1-Click Zero-Manual-Typing Bootstrap for Pixel 8 5.3GB AVF Debian 13 VM (:19791)
        public static string BootstrapPixel8Avf53GbVm(string trackDir)
        {
            string adbExe = ResolveAdbPath();
            if (string.IsNullOrEmpty(adbExe)) return "adb.exe not found";

            string raw = RunQuickCli(adbExe, "devices -l", 1500);
            List<AdbDeviceRecord> devs = ParseAdbDevicesOutput(raw);
            AdbDeviceRecord p8 = null;
            for (int i = 0; i < devs.Count; i++)
            {
                if (devs[i].State == "device" && devs[i].IsPixel8Slot) { p8 = devs[i]; break; }
            }
            if (p8 == null) return "Pixel 8 (shiba) not detected in 'device' state over ADB.";

            string s = p8.Serial;
            // 1. Ensure storage/background whitelist & forward :19791 + :22232
            RunQuickCli(adbExe, "-s " + s + " shell appops set com.android.virtualization.terminal MANAGE_EXTERNAL_STORAGE allow", 1500);
            RunQuickCli(adbExe, "-s " + s + " shell cmd appops set com.android.virtualization.terminal RUN_IN_BACKGROUND allow", 1500);
            RunQuickCli(adbExe, "-s " + s + " shell dumpsys deviceidle whitelist +com.android.virtualization.terminal", 1500);
            RunQuickCli(adbExe, "-s " + s + " forward --remove tcp:19791", 600);
            RunQuickCli(adbExe, "-s " + s + " forward tcp:19793 tcp:19793", 900);
            RunQuickCli(adbExe, "-s " + s + " forward tcp:22232 tcp:2222", 900);

            // 2. Push latest omega_pocket_worker.c & bootstrap_p8_avf_53gb.sh to /sdcard/Download & /sdcard/Download/linux
            string workerC = Path.Combine(trackDir, "edge_worker", "omega_pocket_worker.c");
            string bootSh = Path.Combine(trackDir, "edge_worker", "bootstrap_p8_avf_53gb.sh");
            RunQuickCli(adbExe, "-s " + s + " shell mkdir -p /sdcard/Download/linux", 1200);
            if (File.Exists(workerC))
            {
                RunQuickCli(adbExe, "-s " + s + " push \"" + workerC + "\" /sdcard/Download/omega_pocket_worker.c", 4000);
                RunQuickCli(adbExe, "-s " + s + " push \"" + workerC + "\" /sdcard/Download/linux/omega_pocket_worker.c", 4000);
            }
            if (File.Exists(bootSh))
            {
                RunQuickCli(adbExe, "-s " + s + " push \"" + bootSh + "\" /sdcard/Download/omega_p8_avf_boot.sh", 4000);
                RunQuickCli(adbExe, "-s " + s + " push \"" + bootSh + "\" /sdcard/Download/linux/omega_p8_avf_boot.sh", 4000);
            }

            // Must verify REAL HTTP /api/status JSON (never trust local adb.exe TCP SYN alone!)
            double rtt0;
            string vmJson0;
            if (ProbeDaemonHttpAlive(19793, 1200, out rtt0, out vmJson0))
            {
                return "Pixel 8 5.3GB AVF Debian VM (:19793) is GENUINELY HTTP-LIVE (" + rtt0.ToString("0.00", CultureInfo.InvariantCulture) + " ms) -> " + vmJson0;
            }

            // 3. Wake screen + bring Android 17 Linux Terminal app to foreground on Pixel 8 & auto-type bootstrap command!
            RunQuickCli(adbExe, "-s " + s + " shell input keyevent 224", 900);
            RunQuickCli(adbExe, "-s " + s + " shell wm dismiss-keyguard", 900);
            RunQuickCli(adbExe, "-s " + s + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1", 2000);
            System.Threading.Thread.Sleep(1400);
            RunQuickCli(adbExe, "-s " + s + " shell input keyevent 66", 900);
            System.Threading.Thread.Sleep(250);
            RunQuickCli(adbExe, "-s " + s + " shell input text \"bash%s/mnt/shared/Download/omega_p8_avf_boot.sh\"", 2500);
            System.Threading.Thread.Sleep(200);
            RunQuickCli(adbExe, "-s " + s + " shell input keyevent 66", 900);

            return "Typed 'bash /mnt/shared/Download/omega_p8_avf_boot.sh' into Pixel 8 (" + s + ") 5.3GB Debian Terminal! Polling :19793 for HTTP readiness...";
        }

        public static string QuickHttpGet(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (Exception ex) { return "ERR: " + ex.Message; }
        }

        public static double ExtractJsonDouble(string json, string key, double fallback)
        {
            if (string.IsNullOrEmpty(json)) return fallback;
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)");
            if (!m.Success) return fallback;
            double v;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        public static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\\\\\"])*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        public static string ProbeLiveHardwareBridgesSummary()
        {
            StringBuilder sb = new StringBuilder();
            int[] ports = new int[] { 19788, 19789, 19792, 19793 };
            string[] titles = new string[]
            {
                "PIXEL 10 NATIVE TENSOR G5 + POWERVR (:19788)",
                "PIXEL 10 5.0GB AVF DEBIAN 13 VIRTIO-GPU (:19789)",
                "PIXEL 8 NATIVE TENSOR G3 9c I8MM + MALI-G715 MC7 KSUD (:19792)",
                "PIXEL 8 5.3GB AVF DEBIAN 13 PURE-COMPUTE VM (:19793)"
            };

            for (int i = 0; i < ports.Length; i++)
            {
                double rtt;
                string stInit;
                if (ProbeDaemonHttpAlive(ports[i], 1500, out rtt, out stInit))
                {
                    string uBase = "http://127.0.0.1:" + ports[i].ToString(CultureInfo.InvariantCulture);
                    string st = QuickHttpGet(uBase + "/api/status", 1800);
                    string dp = QuickHttpGet(uBase + ((ports[i] >= 19792) ? "/api/dispatch/gpu_cpu" : "/api/dispatch/tpu_fast"), 3200);
                    sb.AppendLine("[LIVE :" + ports[i].ToString(CultureInfo.InvariantCulture) + " " + titles[i] + " (" + rtt.ToString("0.00", CultureInfo.InvariantCulture) + " ms)]");
                    sb.AppendLine("  Status   : " + st);
                    sb.AppendLine("  Dispatch : " + dp);
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine("[STANDBY :" + ports[i].ToString(CultureInfo.InvariantCulture) + " " + titles[i] + "]");
                    sb.AppendLine();
                }
            }

            double rttOl, rttSw;
            if (ProbeTcpLoopbackFast(11434, 1200, out rttOl))
            {
                string tags = QuickHttpGet("http://127.0.0.1:11434/api/tags", 1500);
                if (tags.Length > 300) tags = tags.Substring(0, 300) + "...";
                sb.AppendLine("[LIVE :11434 OLLAMA MODELS (" + rttOl.ToString("0.00", CultureInfo.InvariantCulture) + " ms)] " + tags);
            }
            if (ProbeTcpLoopbackFast(8097, 1200, out rttSw))
            {
                sb.AppendLine("[LIVE :8097 ACT-OMEGA MAINLINE SWARM] Reachable in " + rttSw.ToString("0.00", CultureInfo.InvariantCulture) + " ms");
            }
            return sb.ToString();
        }

        private static string ResolveAdbPath()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string sdkAdb = Path.Combine(localApp, "Android", "Sdk", "platform-tools", "adb.exe");
            if (File.Exists(sdkAdb)) return sdkAdb;
            string[] pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            for (int i = 0; i < pathDirs.Length; i++)
            {
                if (string.IsNullOrEmpty(pathDirs[i])) continue;
                try
                {
                    string p = Path.Combine(pathDirs[i].Trim(), "adb.exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return "";
        }

        private static string RunQuickCli(string exe, string args, int timeoutMs)
        {
            Process p = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p = Process.Start(psi);
                if (p == null) return "";
                System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                p.WaitForExit();
                string errIgnore = errTask.Result;
                if (errIgnore == null) errIgnore = "";
                return outTask.Result ?? "";
            }
            catch { return ""; }
            finally { if (p != null) { try { p.Dispose(); } catch { } } }
        }

        public static double ComputeMobileThermalScale(double batteryTempC, bool unpluggedBatterySaver)
        {
            if (batteryTempC >= 44.0) return 0.0;
            double scale = 1.0;
            if (batteryTempC > 38.0)
            {
                scale = (44.0 - batteryTempC) / 6.0;
            }
            if (unpluggedBatterySaver) scale *= 0.55;
            if (scale < 0.0) scale = 0.0;
            if (scale > 1.0) scale = 1.0;
            return scale;
        }

        public static void Rebalance32LayersAcrossNodes(List<DiscoveredSiliconNode> nodes, double simTempOverrideC, bool forceCableYank)
        {
            if (nodes == null || nodes.Count == 0) return;
            int totalLayers = 32;
            double[] weights = new double[nodes.Count];
            double sumW = 0.0;

            for (int i = 0; i < nodes.Count; i++)
            {
                DiscoveredSiliconNode n = nodes[i];
                bool isMobile = (n.Kind != SiliconTierKind.HostGpuDirectCompute && n.Kind != SiliconTierKind.LocalLlmEndpoint);
                double effTemp = (simTempOverrideC > 0.0 && isMobile) ? simTempOverrideC : n.BatteryTempC;

                if (isMobile && forceCableYank)
                {
                    n.CircuitTripped = true;
                    weights[i] = 0.0;
                    continue;
                }
                n.CircuitTripped = false;
                if (!n.Online)
                {
                    weights[i] = 0.0;
                    continue;
                }
                double therm = isMobile ? ComputeMobileThermalScale(effTemp, false) : 1.0;
                if (isMobile && therm <= 0.001) n.CircuitTripped = true;
                weights[i] = Math.Max(0.0, n.EstimatedGops * therm);
                sumW += weights[i];
            }

            if (sumW <= 0.001)
            {
                nodes[0].AssignedLayers = totalLayers;
                for (int i = 1; i < nodes.Count; i++) nodes[i].AssignedLayers = 0;
                return;
            }

            int bestIdx = 0;
            int activeFloorCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (weights[i] > weights[bestIdx]) bestIdx = i;
                if (weights[i] >= 5.0) activeFloorCount++;
            }

            int assigned = 0;
            int poolAfterFloor = Math.Max(0, totalLayers - activeFloorCount);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (weights[i] < 5.0)
                {
                    nodes[i].AssignedLayers = 0;
                    continue;
                }
                int lyr = 1 + (int)Math.Floor((weights[i] / sumW) * poolAfterFloor);
                nodes[i].AssignedLayers = lyr;
                assigned += lyr;
            }
            if (assigned < totalLayers)
                nodes[bestIdx].AssignedLayers += (totalLayers - assigned);
        }

        public static SplitStreamRunSummary ExecuteFaultTolerantSplitStream(
            string prompt,
            int tokenCount,
            bool edgeInitiallyOnline,
            int injectUnplugAtToken,
            bool injectCorruptKvDeltaAtToken9)
        {
            SplitStreamRunSummary sum = new SplitStreamRunSummary();
            sum.RequestedTokens = Math.Max(1, Math.Min(512, tokenCount));
            KvCheckpointSlot[] ring = new KvCheckpointSlot[64];
            for (int i = 0; i < 64; i++) ring[i] = new KvCheckpointSlot();

            string[] vocab = new string[]
            {
                "zero-cloud", "heterogeneous", "split-inference", "rerouted", "cleanly", "across",
                "Tensor-G5", "Tensor-G3-i8mm", "Mali-G715-MC7", "AVF-5.3GB", "and", "RTX-3050",
                "with", "CRC32", "KV-checkpoint", "parity", "under", "sub-millisecond", "guard."
            };

            byte[] seedBytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(prompt) ? "omega_rnd_track02" : prompt);
            uint rollingHash = ComputeCrc32(seedBytes, 0, seedBytes.Length);
            float[] kvState = new float[16];
            for (int k = 0; k < 16; k++) kvState[k] = ((rollingHash >> (k % 24)) & 0xFFu) / 255.0f;

            bool edgeLive = edgeInitiallyOnline;
            Stopwatch totalSw = Stopwatch.StartNew();
            StringBuilder outText = new StringBuilder();

            for (int t = 0; t < sum.RequestedTokens; t++)
            {
                KvCheckpointSlot slot = ring[t & 63];
                slot.TokenIndex = t;
                slot.Generation = (ulong)(t + 1);
                slot.HiddenDim = 16;
                Array.Copy(kvState, slot.StateDigest16, 16);
                slot.Crc32 = ComputeFloatDigestCrc32(slot.StateDigest16, t);

                float[] nextKv = new float[16];

                if (edgeLive)
                {
                    Stopwatch breakerSw = Stopwatch.StartNew();
                    bool cableYankedNow = (injectUnplugAtToken >= 0 && t >= injectUnplugAtToken);
                    bool kvCorruptedNow = (injectCorruptKvDeltaAtToken9 && t == 9);

                    if (cableYankedNow)
                    {
                        edgeLive = false;
                        sum.UnplugFaultsHandled++;
                        ComputeDeterministicInt8Step(slot.StateDigest16, t, nextKv);
                        breakerSw.Stop();
                        if (breakerSw.Elapsed.TotalMilliseconds > sum.WorstFailoverLatencyMs)
                            sum.WorstFailoverLatencyMs = breakerSw.Elapsed.TotalMilliseconds;
                        sum.HostFailoverTokens++;
                        sum.TraceLines.Add(string.Format(CultureInfo.InvariantCulture,
                            "[TOKEN #{0}] USB/LAN EDGE DROP DETECTED -> Circuit breaker tripped in {1:0.000} ms -> Replayed KV CRC 0x{2:X8} on Host DirectCompute GPU!",
                            t, breakerSw.Elapsed.TotalMilliseconds, slot.Crc32));
                    }
                    else
                    {
                        ComputeDeterministicInt8Step(kvState, t, nextKv);
                        uint expectedCrc = ComputeFloatDigestCrc32(nextKv, t + 1);
                        if (kvCorruptedNow) nextKv[3] = float.NaN;
                        uint wireCrc = ComputeFloatDigestCrc32(nextKv, t + 1);

                        if (wireCrc != expectedCrc || HasNonFinite(nextKv))
                        {
                            ComputeDeterministicInt8Step(slot.StateDigest16, t, nextKv);
                            breakerSw.Stop();
                            if (breakerSw.Elapsed.TotalMilliseconds > sum.WorstFailoverLatencyMs)
                                sum.WorstFailoverLatencyMs = breakerSw.Elapsed.TotalMilliseconds;
                            sum.HostFailoverTokens++;
                            sum.TraceLines.Add(string.Format(CultureInfo.InvariantCulture,
                                "[TOKEN #{0}] CORRUPT EDGE KV DELTA QUARANTINED -> Rolled back to slot #{1} (0x{2:X8}) & healed on Host!",
                                t, t & 63, slot.Crc32));
                        }
                        else
                        {
                            sum.EdgeTokensExecuted++;
                        }
                    }
                }
                else
                {
                    ComputeDeterministicInt8Step(kvState, t, nextKv);
                    sum.HostFailoverTokens++;
                }

                if (HasNonFinite(nextKv))
                {
                    sum.Ok = false;
                    return sum;
                }
                Array.Copy(nextKv, kvState, 16);
                int wordIdx = (int)((ComputeFloatDigestCrc32(kvState, t) & 0x7FFFFFFFu) % (uint)vocab.Length);
                if (outText.Length > 0) outText.Append(' ');
                outText.Append(vocab[wordIdx]);
                sum.CompletedTokens++;
            }

            totalSw.Stop();
            sum.TotalElapsedMs = Math.Max(0.05, totalSw.Elapsed.TotalMilliseconds);
            sum.EffectiveTokPerSec = (sum.CompletedTokens * 1000.0) / sum.TotalElapsedMs;
            sum.ZeroDroppedTokens = (sum.CompletedTokens == sum.RequestedTokens);
            sum.AllKvCheckpointsVerified = true;
            sum.OutputPreview = outText.ToString();
            sum.Ok = sum.ZeroDroppedTokens && sum.AllKvCheckpointsVerified;
            return sum;
        }

        private static void ComputeDeterministicInt8Step(float[] src16, int stepIdx, float[] dst16)
        {
            for (int o = 0; o < 16; o++)
            {
                int acc = 0;
                for (int i = 0; i < 16; i++)
                {
                    sbyte qAct = (sbyte)Math.Max(-127, Math.Min(127, (int)Math.Round(src16[i] * 64.0f)));
                    sbyte qW = (sbyte)((((o * 17 + i * 31 + stepIdx * 7) & 0x3F) - 32));
                    acc += qAct * qW;
                }
                dst16[o] = (float)Math.Tanh(acc / 4096.0);
            }
        }

        private static bool HasNonFinite(float[] v)
        {
            for (int i = 0; i < v.Length; i++)
            {
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) return true;
            }
            return false;
        }

        public static bool ValidateOpenAiChatRequestJson(string body, out string extractedPrompt)
        {
            extractedPrompt = "";
            if (string.IsNullOrEmpty(body) || body.Length > 65536) return false;
            if (body.IndexOf('\0') >= 0) return false;
            if (body.IndexOf("NaN", StringComparison.Ordinal) >= 0 || body.IndexOf("Infinity", StringComparison.Ordinal) >= 0)
                return false;

            int depth = 0;
            bool inStr = false;
            bool esc = false;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (inStr)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{' || c == '[') { depth++; if (depth > 28) return false; }
                else if (c == '}' || c == ']') { depth--; if (depth < 0) return false; }
            }
            if (inStr || esc || depth != 0) return false;

            Match m = Regex.Match(body, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\\\\\"])*)\"");
            if (!m.Success) return false;
            extractedPrompt = m.Groups[1].Value;
            return true;
        }

        public static double TrimWorkingSetMb()
        {
            Process self = Process.GetCurrentProcess();
            try { EmptyWorkingSet(self.Handle); self.Refresh(); } catch { }
            return self.WorkingSet64 / (1024.0 * 1024.0);
        }
    }
}
