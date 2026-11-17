using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmegaBrowserV03
{
    public static class CalcAndUnits
    {
        public static string TryEvaluate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();
            if (s.StartsWith("=")) s = s.Substring(1).Trim();
            else if (!Regex.IsMatch(s, @"^[0-9\.\(\)\+\-\*\/\^\s%]+$") || !Regex.IsMatch(s, @"[\+\-\*\/\^%]"))
                return TryConvertUnits(raw.Trim());
            try
            {
                string expr = Regex.Replace(s, @"(\d+(?:\.\d+)?)\s*\^\s*(\d+(?:\.\d+)?)", delegate(Match m)
                {
                    double a = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double b = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return Math.Pow(a, b).ToString(System.Globalization.CultureInfo.InvariantCulture);
                });
                DataTable dt = new DataTable();
                object val = dt.Compute(expr, "");
                if (val != null && val != DBNull.Value) return Convert.ToDouble(val).ToString("G12");
            }
            catch { }
            return TryConvertUnits(raw.Trim());
        }

        private static string TryConvertUnits(string s)
        {
            Match m = Regex.Match(s, @"^(?:conv\s+)?(-?\d+(?:\.\d+)?)\s*([a-zA-Z]+)\s+(?:to|in)\s+([a-zA-Z]+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            string u1 = m.Groups[2].Value.ToLowerInvariant(), u2 = m.Groups[3].Value.ToLowerInvariant();
            if (u1 == "f" && u2 == "c") return Math.Round((v - 32.0) * 5.0 / 9.0, 3) + " C";
            if (u1 == "c" && u2 == "f") return Math.Round(v * 9.0 / 5.0 + 32.0, 3) + " F";
            if (u1 == "mi" && u2 == "km") return Math.Round(v * 1.609344, 4) + " km";
            if (u1 == "km" && u2 == "mi") return Math.Round(v / 1.609344, 4) + " mi";
            if (u1 == "lb" && u2 == "kg") return Math.Round(v * 0.45359237, 4) + " kg";
            if (u1 == "kg" && u2 == "lb") return Math.Round(v / 0.45359237, 4) + " lb";
            Dictionary<string, double> b = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            b["b"] = 1; b["kb"] = 1e3; b["mb"] = 1e6; b["gb"] = 1e9; b["tb"] = 1e12;
            b["kib"] = 1024.0; b["mib"] = 1048576.0; b["gib"] = 1073741824.0;
            if (b.ContainsKey(u1) && b.ContainsKey(u2)) return Math.Round((v * b[u1]) / b[u2], 4) + " " + u2.ToUpperInvariant();
            return "";
        }
    }

    public static class WinTaskbarProgress
    {
        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
            void SetProgressState(IntPtr hwnd, int tbpFlags);
        }

        [ComImport, Guid("56fdf344-fd6d-11d0-958a-006097c9a090"), ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance { }

        private static ITaskbarList3 _tb;
        private static bool _tried = false;

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            try { _tb = (ITaskbarList3)(new TaskbarInstance()); _tb.HrInit(); } catch { _tb = null; }
        }

        public static void SetProgress(IntPtr hwnd, ulong done, ulong total)
        {
            Ensure();
            if (_tb == null || hwnd == IntPtr.Zero) return;
            try
            {
                if (total == 0) _tb.SetProgressState(hwnd, 1);
                else { _tb.SetProgressState(hwnd, 2); _tb.SetProgressValue(hwnd, done, total); }
            }
            catch { }
        }

        public static void Clear(IntPtr hwnd)
        {
            Ensure();
            if (_tb == null || hwnd == IntPtr.Zero) return;
            try { _tb.SetProgressState(hwnd, 0); } catch { }
        }
    }

    public static class OmegaSecurity
    {
        public static readonly string[] DefaultAllowDomains = new string[]
        {
            "omega://", "omega.home", "127.0.0.1", "localhost",
            "arxiv.org", "ocw.mit.edu", "scholar.google.com", "semanticscholar.org",
            "github.com", "raw.githubusercontent.com", "developer.mozilla.org",
            "doc.rust-lang.org", "crates.io", "huggingface.co", "gutenberg.org",
            "en.wikipedia.org", "act-omega-net.dev"
        };

        public static readonly string[] DefaultTrackerWildcards = new string[]
        {
            "*://*.doubleclick.net/*", "*://*.googlesyndication.com/*", "*://*.google-analytics.com/*",
            "*://*.googletagmanager.com/*", "*://*.facebook.net/*", "*://*.adsystem.com/*",
            "*://*.scorecardresearch.com/*", "*://*.criteo.com/*", "*://*.taboola.com/*",
            "*://*.outbrain.com/*", "*://*.hotjar.com/*", "*://*.clarity.ms/*"
        };

        public static void EnsureFiles(string allowPath, string rulesPath)
        {
            if (!File.Exists(allowPath)) File.WriteAllLines(allowPath, DefaultAllowDomains, new UTF8Encoding(false));
            if (!File.Exists(rulesPath))
            {
                List<string> lines = new List<string>();
                lines.Add("# OmegaBrowser v0.3 Shareable Shield Wildcard Ruleset (Zero Browsing History)");
                lines.AddRange(DefaultTrackerWildcards);
                File.WriteAllLines(rulesPath, lines.ToArray(), new UTF8Encoding(false));
            }
        }

        public static void LoadShieldRules(string rulesPath, out List<string> blockFilters, out HashSet<string> allowedSites)
        {
            blockFilters = new List<string>(); allowedSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(rulesPath)) { blockFilters.AddRange(DefaultTrackerWildcards); return; }
            foreach (string raw in File.ReadAllLines(rulesPath))
            {
                string ln = (raw ?? "").Trim();
                if (ln.Length == 0 || ln.StartsWith("#")) continue;
                if (ln.StartsWith("ALLOW:", StringComparison.OrdinalIgnoreCase))
                {
                    string h = ln.Substring(6).Trim().TrimStart('*', '.').ToLowerInvariant();
                    if (h.Length > 0) allowedSites.Add(h);
                }
                else if (ln.Contains("://")) blockFilters.Add(ln);
            }
            if (blockFilters.Count == 0) blockFilters.AddRange(DefaultTrackerWildcards);
        }

        public static void SaveSiteAllowToggle(string rulesPath, string host, bool allow)
        {
            if (string.IsNullOrEmpty(host)) return;
            string clean = host.Trim().ToLowerInvariant();
            List<string> kept = new List<string>();
            if (File.Exists(rulesPath))
            {
                foreach (string raw in File.ReadAllLines(rulesPath))
                {
                    string ln = (raw ?? "").Trim();
                    if (ln.StartsWith("ALLOW:", StringComparison.OrdinalIgnoreCase) && ln.Substring(6).Trim().TrimStart('*', '.').ToLowerInvariant() == clean) continue;
                    kept.Add(raw);
                }
            }
            if (allow) kept.Add("ALLOW: " + clean);
            File.WriteAllLines(rulesPath, kept.ToArray(), new UTF8Encoding(false));
        }

        public static bool IsSiteAllowed(HashSet<string> allowedSites, string pageHost)
        {
            if (string.IsNullOrEmpty(pageHost) || allowedSites == null) return false;
            string h = pageHost.ToLowerInvariant();
            foreach (string a in allowedSites) if (h == a || h.EndsWith("." + a)) return true;
            return false;
        }

        public static Dictionary<string, DomainPref> LoadDomainPrefs(string path)
        {
            Dictionary<string, DomainPref> map = new Dictionary<string, DomainPref>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                DomainPref yt = new DomainPref(); yt.Host = "www.youtube.com"; yt.Zoom = 0.90; yt.NightDim = true; map[yt.Host] = yt;
                DomainPref mdn = new DomainPref(); mdn.Host = "developer.mozilla.org"; mdn.Zoom = 1.10; map[mdn.Host] = mdn;
                return map;
            }
            foreach (string ln in File.ReadAllLines(path))
            {
                string[] p = ln.Split('\t');
                if (p.Length < 3) continue;
                DomainPref dp = new DomainPref(); dp.Host = p[0];
                double z; if (double.TryParse(p[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out z)) dp.Zoom = z;
                dp.NightDim = (p[2] == "1");
                map[dp.Host] = dp;
            }
            return map;
        }

        public static void SaveDomainPrefs(string path, Dictionary<string, DomainPref> map)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, DomainPref> kv in map)
                lines.Add(kv.Value.Host + "\t" + kv.Value.Zoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "\t" + (kv.Value.NightDim ? "1" : "0"));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }

        public static bool IsAgentUrlAllowed(string url, string allowPath)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string low = url.Trim().ToLowerInvariant();
            if (low.StartsWith("omega://") || low.StartsWith("https://omega.home")) return true;
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != "http" && u.Scheme != "https") return false;
            string host = (u.Host ?? "").ToLowerInvariant();
            if (!File.Exists(allowPath)) File.WriteAllLines(allowPath, DefaultAllowDomains, new UTF8Encoding(false));
            foreach (string raw in File.ReadAllLines(allowPath))
            {
                string r = (raw ?? "").Trim().ToLowerInvariant();
                if (r.Length == 0 || r.StartsWith("#") || r.EndsWith("://")) continue;
                if (host == r || host.EndsWith("." + r)) return true;
            }
            return false;
        }

        public static bool IsStrictLoopbackEndpoint(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url.Trim().StartsWith("offline://", StringComparison.OrdinalIgnoreCase)) return true;
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string h = (u.Host ?? "").ToLowerInvariant();
            return h == "127.0.0.1" || h == "localhost" || h == "::1";
        }
    }

    public static class LocalModelClient
    {
        public static string QueryLocalShXi(string endpoint, string mode, string userPrompt, ReceiptRecord rec)
        {
            if (!OmegaSecurity.IsStrictLoopbackEndpoint(endpoint))
                return "[BLOCKED] Ask SH-XI enforces loopback-only endpoints (127.0.0.1 / localhost / offline://).";

            string sys = mode == "384D Distill" ? "Distill for Shell 0 (384D): definitions, equations/theorems, and signatures."
                : (mode == "Extract Code" ? "Extract code blocks and signatures cleanly."
                : (mode == "Extract Theorems" ? "Extract Definitions, Lemmas, Theorems, and LaTeX equations verbatim."
                : "Summarize for Shell 1 (768D): technical synthesis preserving math invariants."));
            if (!string.IsNullOrWhiteSpace(userPrompt)) sys += " Question: " + userPrompt.Trim();

            string slice = rec.MarkdownBody ?? "";
            if (slice.Length > 18000) slice = slice.Substring(0, 18000);

            if (!endpoint.StartsWith("offline://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endpoint);
                    req.Method = "POST"; req.ContentType = "application/json; charset=utf-8"; req.Timeout = 16000;
                    string payload = endpoint.IndexOf("/api/generate", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "{\"model\":\"gemma4\",\"stream\":false,\"prompt\":\"" + OmegaJson.Esc(sys + "\n\n" + slice) + "\"}"
                        : "{\"model\":\"sh-xi-local\",\"temperature\":0.2,\"messages\":[{\"role\":\"system\",\"content\":\"" + OmegaJson.Esc(sys) + "\"},{\"role\":\"user\",\"content\":\"" + OmegaJson.Esc(slice) + "\"}]}";
                    byte[] b = Encoding.UTF8.GetBytes(payload); req.ContentLength = b.Length;
                    using (Stream rs = req.GetRequestStream()) rs.Write(b, 0, b.Length);
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string raw = sr.ReadToEnd();
                        string c = OmegaJson.ExtractJsonField(raw, "content");
                        if (string.IsNullOrEmpty(c)) c = OmegaJson.ExtractJsonField(raw, "response");
                        return (string.IsNullOrEmpty(c) ? raw : c).Trim() + "\r\n\r\n[Receipt SHA-256: " + rec.Sha256.Substring(0, 16) + "...]";
                    }
                }
                catch { }
            }

            StringBuilder sb = new StringBuilder("[On-Device SH-XI Structural Pass]\r\n\r\n");
            string[] lines = slice.Split('\n'); int kept = 0; bool inCode = false; string fence = new string((char)96, 3);
            for (int i = 0; i < lines.Length && kept < 85; i++)
            {
                string ln = lines[i].TrimEnd('\r'), tr = ln.Trim();
                if (tr.StartsWith(fence)) { inCode = !inCode; sb.AppendLine(ln); kept++; continue; }
                if (inCode) { if (mode != "Extract Theorems") { sb.AppendLine(ln); kept++; } continue; }
                if (tr.StartsWith("#") || tr.Contains("$") || Regex.IsMatch(tr, @"^(Theorem|Lemma|Definition|Proposition|Proof)\b", RegexOptions.IgnoreCase))
                { sb.AppendLine(ln); kept++; }
            }
            sb.AppendLine("\r\n[Receipt SHA-256: " + rec.Sha256.Substring(0, 16) + "...]");
            return sb.ToString();
        }
    }

    public static class PhoneBridge
    {
        public static string PushUrlToPixel(string url)
        {
            if (string.IsNullOrEmpty(url)) return "No URL.";
            string[] paths = new string[] { Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe"), @"C:\platform-tools\adb.exe", @"E:\platform-tools\adb.exe" };
            string adb = "adb.exe";
            for (int i = 0; i < paths.Length; i++) if (File.Exists(paths[i])) { adb = paths[i]; break; }
            try
            {
                string devs = RunCmd(adb, "devices"); string serial = "";
                foreach (string raw in devs.Split('\n'))
                {
                    string ln = raw.Trim();
                    if (ln.EndsWith("\tdevice")) { string s = ln.Split('\t')[0].Trim(); if (serial.Length == 0) serial = s; }
                }
                if (serial.Length == 0) return "No ADB device connected.";
                RunCmd(adb, "-s " + serial + " shell am start -a android.intent.action.VIEW -d \"" + url.Replace("\"", "") + "\"");
                return "Sent to Pixel (" + serial + "): " + url;
            }
            catch (Exception ex) { return "ADB error: " + ex.Message; }
        }

        public static string ProbeHeadSize(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD"; req.Timeout = 2500;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    long len = resp.ContentLength;
                    if (len <= 0) return resp.ContentType ?? "";
                    if (len >= 1048576) return Math.Round(len / 1048576.0, 1) + " MB";
                    return Math.Max(1, len / 1024) + " KB";
                }
            }
            catch { return ""; }
        }

        private static string RunCmd(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
            using (Process p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3500); return o ?? ""; }
        }
    }

    public sealed class AgentApiServer
    {
        private readonly int _port;
        private readonly string _logPath;
        private readonly IAgentBrowserHost _host;
        private TcpListener _listener;
        private volatile bool _running;
        private readonly object _auditLock = new object();

        public AgentApiServer(int port, string labRoot, IAgentBrowserHost host)
        {
            _port = port; _host = host;
            string logDir = Path.Combine(labRoot, "logs"); Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "agent_api_audit.jsonl");
        }

        public void Start()
        {
            if (_running) return; _running = true;
            Thread t = new Thread(ListenLoop); t.IsBackground = true; t.Start();
        }

        public void Stop() { _running = false; try { if (_listener != null) _listener.Stop(); } catch { } }

        private void ListenLoop()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Server.ExclusiveAddressUse = true; _listener.Start();
                while (_running) ThreadPool.QueueUserWorkItem(HandleClient, _listener.AcceptTcpClient());
            }
            catch { }
        }

        private void HandleClient(object state)
        {
            using (TcpClient c = (TcpClient)state)
            {
                c.ReceiveTimeout = 10000; c.SendTimeout = 10000;
                try
                {
                    using (NetworkStream ns = c.GetStream())
                    {
                        string method, rawTarget, body;
                        if (!ReadHttp(ns, out method, out rawTarget, out body)) return;
                        string path = rawTarget;
                        Dictionary<string, string> query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        int qIdx = rawTarget.IndexOf('?');
                        if (qIdx >= 0) { path = rawTarget.Substring(0, qIdx); ParseQuery(rawTarget.Substring(qIdx + 1), query); }
                        ApiResult res = method == "OPTIONS" ? ApiResult.Json(200, "{\"ok\":true}") : _host.HandleAgentRequestAsync(method, path, query, body).GetAwaiter().GetResult();
                        string ln = "{\"utc\":\"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\"method\":\"" + OmegaJson.Esc(method) + "\",\"target\":\"" + OmegaJson.Esc(rawTarget) + "\",\"status\":" + res.Status + "}";
                        lock (_auditLock) { try { File.AppendAllText(_logPath, ln + Environment.NewLine, new UTF8Encoding(false)); } catch { } }
                        string hdr = "HTTP/1.1 " + res.Status + " OK\r\nContent-Type: " + res.ContentType + "\r\nContent-Length: " + res.Body.Length + "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                        byte[] hb = Encoding.ASCII.GetBytes(hdr); ns.Write(hb, 0, hb.Length);
                        if (res.Body.Length > 0) ns.Write(res.Body, 0, res.Body.Length);
                        ns.Flush();
                    }
                }
                catch { }
            }
        }

        private static bool ReadHttp(NetworkStream ns, out string method, out string target, out string body)
        {
            method = ""; target = ""; body = "";
            MemoryStream ms = new MemoryStream(); byte[] buf = new byte[4096]; int hEnd = -1;
            while (hEnd < 0 && ms.Length < 65536)
            {
                int r = ns.Read(buf, 0, buf.Length); if (r <= 0) break;
                ms.Write(buf, 0, r); byte[] cur = ms.ToArray();
                for (int i = 0; i <= cur.Length - 4; i++) if (cur[i] == 13 && cur[i + 1] == 10 && cur[i + 2] == 13 && cur[i + 3] == 10) { hEnd = i + 4; break; }
            }
            if (hEnd < 0) return false;
            byte[] all = ms.ToArray();
            string[] lines = Encoding.ASCII.GetString(all, 0, hEnd).Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;
            string[] p = lines[0].Split(' '); if (p.Length < 2) return false;
            method = p[0].Trim().ToUpperInvariant(); target = p[1].Trim();
            int cLen = 0;
            for (int i = 1; i < lines.Length; i++) if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(lines[i].Substring(15).Trim(), out cLen);
            int rem = all.Length - hEnd; MemoryStream bms = new MemoryStream();
            if (rem > 0) bms.Write(all, hEnd, Math.Min(rem, cLen > 0 ? cLen : rem));
            while (bms.Length < cLen && cLen <= 1048576) { int r = ns.Read(buf, 0, Math.Min(buf.Length, cLen - (int)bms.Length)); if (r <= 0) break; bms.Write(buf, 0, r); }
            if (bms.Length > 0) body = Encoding.UTF8.GetString(bms.ToArray());
            return true;
        }

        private static void ParseQuery(string qs, Dictionary<string, string> dict)
        {
            foreach (string pair in qs.Split('&'))
            {
                if (string.IsNullOrEmpty(pair)) continue;
                int eq = pair.IndexOf('=');
                if (eq > 0) dict[Uri.UnescapeDataString(pair.Substring(0, eq).Replace('+', ' '))] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                else dict[Uri.UnescapeDataString(pair)] = "1";
            }
        }
    }
}
