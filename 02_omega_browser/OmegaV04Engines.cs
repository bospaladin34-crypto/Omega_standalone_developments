using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaBrowserV03
{
    public sealed class PixelDeviceInfo
    {
        public string Serial = "";
        public string Model = "Pixel";
        public string Resolution = "1080x2400";
        public bool IsWireless = false;
        public string MatchedPcIp = "127.0.0.1";
        public string Label
        {
            get { return Model + " [" + (IsWireless ? ("Wi-Fi " + Serial) : ("USB " + Serial)) + " | PC:" + MatchedPcIp + "]"; }
        }
    }

    public static class DualPixelMesh
    {
        public static string ResolveAdbExe()
        {
            string[] paths = new string[]
            {
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe"),
                @"C:\platform-tools\adb.exe",
                @"E:\platform-tools\adb.exe"
            };
            for (int i = 0; i < paths.Length; i++) if (File.Exists(paths[i])) return paths[i];
            return "adb.exe";
        }

        public static string RunAdb(string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ResolveAdbExe(), args);
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(timeoutMs);
                    return o ?? "";
                }
            }
            catch { return ""; }
        }

        public static List<PixelDeviceInfo> DiscoverPixels()
        {
            List<PixelDeviceInfo> list = new List<PixelDeviceInfo>();
            List<string> pcIps = GetLocalIpv4List();
            string raw = RunAdb("devices", 2500);
            foreach (string ln in raw.Split('\n'))
            {
                string tr = ln.Trim();
                if (!tr.EndsWith("\tdevice")) continue;
                string s = tr.Split('\t')[0].Trim();
                if (s.Length == 0) continue;

                PixelDeviceInfo d = new PixelDeviceInfo();
                d.Serial = s;
                d.IsWireless = s.Contains(":");
                string model = RunAdb("-s " + s + " shell getprop ro.product.model", 1500).Trim();
                if (!string.IsNullOrEmpty(model)) d.Model = model;
                else d.Model = (d.IsWireless ? "Android (Wi-Fi ADB)" : "Android (USB ADB)");

                string wm = RunAdb("-s " + s + " shell wm size", 1500);
                Match mWm = Regex.Match(wm, @"(\d{3,4}x\d{3,4})");
                if (mWm.Success) d.Resolution = mWm.Groups[1].Value;
                else if (d.Model.IndexOf("10", StringComparison.OrdinalIgnoreCase) >= 0) d.Resolution = "1080x2424";

                string prefix = "";
                if (d.IsWireless)
                {
                    string[] oct = s.Split(':')[0].Split('.');
                    if (oct.Length == 4) prefix = oct[0] + "." + oct[1] + "." + oct[2] + ".";
                }
                else
                {
                    string wlan = RunAdb("-s " + s + " shell ip -f inet addr show wlan0", 1500);
                    Match mIp = Regex.Match(wlan, @"inet\s+(\d+\.\d+\.\d+)\.\d+");
                    if (mIp.Success) prefix = mIp.Groups[1].Value + ".";
                }
                d.MatchedPcIp = MatchPcIp(pcIps, prefix);
                list.Add(d);
            }
            return list;
        }

        private static List<string> GetLocalIpv4List()
        {
            List<string> ips = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ua.Address.ToString();
                        if (!s.StartsWith("127.") && !s.StartsWith("169.254.")) ips.Add(s);
                    }
                }
            }
            catch { }
            return ips;
        }

        private static string MatchPcIp(List<string> pcIps, string prefix)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                for (int i = 0; i < pcIps.Count; i++) if (pcIps[i].StartsWith(prefix)) return pcIps[i];
            }
            for (int i = 0; i < pcIps.Count; i++) if (pcIps[i].StartsWith("192.168.137.")) return pcIps[i];
            return pcIps.Count > 0 ? pcIps[0] : "127.0.0.1";
        }

        public static string PushUrlToTarget(string serial, string rawUrl)
        {
            string clean = OmegaUrlCleaner.CleanUrl(rawUrl);
            if (string.IsNullOrEmpty(clean) || clean.StartsWith("omega://")) clean = "http://127.0.0.1:9778/companion";
            if (clean.StartsWith("http://127.0.0.1:9778")) RunAdb("-s " + serial + " reverse tcp:9778 tcp:9778", 2000);
            if (clean.StartsWith("http://127.0.0.1:19777")) RunAdb("-s " + serial + " reverse tcp:19777 tcp:19777", 2000);
            RunAdb("-s " + serial + " shell am start -a android.intent.action.VIEW -d \"" + clean.Replace("\"", "") + "\"", 2800);
            return "Pushed to " + serial + " -> " + clean;
        }

        public static string BroadcastToAllPixels(string rawUrl, bool companionDeck)
        {
            List<PixelDeviceInfo> devs = DiscoverPixels();
            if (devs.Count == 0) return "No Pixel ADB devices detected (check USB or Wireless :5555).";
            string url = companionDeck ? "http://127.0.0.1:9778/companion" : OmegaUrlCleaner.CleanUrl(rawUrl);
            List<string> names = new List<string>();
            for (int i = 0; i < devs.Count; i++)
            {
                RunAdb("-s " + devs[i].Serial + " reverse tcp:9778 tcp:9778", 2000);
                RunAdb("-s " + devs[i].Serial + " reverse tcp:19777 tcp:19777", 2000);
                RunAdb("-s " + devs[i].Serial + " shell am start -a android.intent.action.VIEW -d \"" + url.Replace("\"", "") + "\"", 2800);
                names.Add(devs[i].Model);
            }
            return "Beamed " + (companionDeck ? "OLED Companion Deck" : url) + " -> " + string.Join(" + ", names.ToArray());
        }

        public static string PromoteUsbPixelToWireless(string usbSerial)
        {
            if (string.IsNullOrEmpty(usbSerial) || usbSerial.Contains(":")) return "Device is already on Wireless ADB: " + usbSerial;
            string wlan = RunAdb("-s " + usbSerial + " shell ip -f inet addr show wlan0", 2000);
            Match m = Regex.Match(wlan, @"inet\s+(\d+\.\d+\.\d+\.\d+)");
            RunAdb("-s " + usbSerial + " tcpip 5555", 2500);
            System.Threading.Thread.Sleep(650);
            if (!m.Success) return "Armed tcpip 5555 on " + usbSerial + " (connect phone to Wi-Fi or PC Hotspot to auto-pair).";
            string ip = m.Groups[1].Value;
            string res = RunAdb("connect " + ip + ":5555", 3000).Trim();
            RunAdb("-s " + ip + ":5555 reverse tcp:9778 tcp:9778", 2000);
            RunAdb("-s " + ip + ":5555 reverse tcp:19777 tcp:19777", 2000);
            return "Promoted " + usbSerial + " -> Wireless ADB " + ip + ":5555 (" + res + ") + Tunnels Armed!";
        }

        public static string PullUrlOrClipFromPixel(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return "";
            string clip = RunAdb("-s " + serial + " shell cmd clipboard get-text", 2000).Trim();
            Match mClip = Regex.Match(clip, @"https?://[^\s""'<>]+");
            if (mClip.Success) return OmegaUrlCleaner.CleanUrl(mClip.Value);

            string act = RunAdb("-s " + serial + " shell dumpsys activity activities", 3000);
            MatchCollection ms = Regex.Matches(act, @"https?://[^\s""'<>\}]+");
            for (int i = 0; i < ms.Count; i++)
            {
                string u = ms[i].Value;
                if (u.Contains("127.0.0.1:19777") || u.Contains("127.0.0.1:9778") || u.Contains("schemas.android.com")) continue;
                return OmegaUrlCleaner.CleanUrl(u);
            }
            return "";
        }
    }

    public static class NativePdfExtractor
    {
        public static bool LooksLikePdfUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string low = url.ToLowerInvariant();
            if (low.EndsWith(".pdf") || low.Contains(".pdf?") || low.Contains(".pdf#")) return true;
            if (low.Contains("arxiv.org/pdf/")) return true;
            return false;
        }

        public static string ExtractPdfMarkdown(string url, string mode, out string pdfSha256)
        {
            pdfSha256 = "";
            byte[] raw = FetchPdfBytes(url);
            if (raw == null || raw.Length < 64) return "";
            pdfSha256 = OmegaVault.ComputeSha256Bytes(raw);

            string ascii = Encoding.GetEncoding("ISO-8859-1").GetString(raw);
            List<string> pages = new List<string>();
            int pos = 0;
            int streamCount = 0;

            while (pos < raw.Length && streamCount < 240)
            {
                int sIdx = ascii.IndexOf("stream", pos, StringComparison.Ordinal);
                if (sIdx < 0) break;
                if (sIdx > 0 && char.IsLetterOrDigit(ascii[sIdx - 1])) { pos = sIdx + 6; continue; }

                int dataStart = sIdx + 6;
                if (dataStart < raw.Length && raw[dataStart] == 13 && dataStart + 1 < raw.Length && raw[dataStart + 1] == 10) dataStart += 2;
                else if (dataStart < raw.Length && raw[dataStart] == 10) dataStart += 1;
                else { pos = sIdx + 6; continue; }

                int eIdx = ascii.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                if (eIdx < 0) break;
                pos = eIdx + 9;
                int len = eIdx - dataStart;
                while (len > 0 && (raw[dataStart + len - 1] == 10 || raw[dataStart + len - 1] == 13)) len--;
                if (len <= 4 || len > 12 * 1048576) continue;

                int hdrStart = Math.Max(0, sIdx - 260);
                string dictHdr = ascii.Substring(hdrStart, sIdx - hdrStart);
                if (dictHdr.Contains("/Subtype/Image") || dictHdr.Contains("/Subtype /Image") || dictHdr.Contains("/FontFile")) continue;

                string content = "";
                if (dictHdr.Contains("FlateDecode"))
                {
                    content = TryInflateZlib(raw, dataStart, len);
                }
                else if (dictHdr.Contains("/Length") && !dictHdr.Contains("DCTDecode"))
                {
                    content = ascii.Substring(dataStart, len);
                }
                if (string.IsNullOrEmpty(content) || content.IndexOf("BT", StringComparison.Ordinal) < 0) continue;

                streamCount++;
                string extracted = ParsePdfTextOperators(content);
                if (extracted.Trim().Length >= 24)
                    pages.Add("### [PDF Stream " + (pages.Count + 1) + "]\n" + extracted.Trim());
            }

            if (pages.Count == 0)
                return "[Zero-Download PDF Inspector :: Binary SHA-256 " + pdfSha256.Substring(0, 16) + "... verified (" + (raw.Length / 1024) + " KB), no uncompressed/Flate glyph streams found (scanned or custom Type3 font PDF).]";

            string fullMd = "> **Zero-Download Native PDF Extraction** | Size: " + (raw.Length / 1024) + " KB | PDF Binary SHA-256: " + pdfSha256 + "\n\n" + string.Join("\n\n", pages.ToArray());
            if (string.Equals(mode, "distilled", StringComparison.OrdinalIgnoreCase))
                return DistillPdf384D(fullMd, pdfSha256);
            return fullMd;
        }

        private static byte[] FetchPdfBytes(string url)
        {
            try
            {
                Uri u;
                if (Uri.TryCreate(url, UriKind.Absolute, out u) && u.IsFile && File.Exists(u.LocalPath))
                    return File.ReadAllBytes(u.LocalPath);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) OmegaBrowser/0.4";
                req.Timeout = 14000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[16384];
                    int r;
                    while ((r = st.Read(buf, 0, buf.Length)) > 0 && ms.Length < 32 * 1048576) ms.Write(buf, 0, r);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        private static string TryInflateZlib(byte[] buf, int offset, int length)
        {
            int skip = (length > 2 && buf[offset] == 0x78) ? 2 : 0;
            try
            {
                using (MemoryStream ms = new MemoryStream(buf, offset + skip, length - skip))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    byte[] tmp = new byte[16384];
                    int r;
                    while ((r = ds.Read(tmp, 0, tmp.Length)) > 0 && outMs.Length < 4 * 1048576) outMs.Write(tmp, 0, r);
                    return Encoding.GetEncoding("ISO-8859-1").GetString(outMs.ToArray());
                }
            }
            catch { return ""; }
        }

        private static string ParsePdfTextOperators(string s)
        {
            StringBuilder outSb = new StringBuilder();
            int i = 0; int n = s.Length;
            while (i < n)
            {
                int bt = s.IndexOf("BT", i, StringComparison.Ordinal);
                if (bt < 0) break;
                int et = s.IndexOf("ET", bt + 2, StringComparison.Ordinal);
                if (et < 0) et = n;
                ParseBtBlock(s, bt + 2, et, outSb);
                outSb.Append('\n');
                i = et + 2;
            }
            string raw = Regex.Replace(outSb.ToString(), @"[ \t]{2,}", " ");
            raw = Regex.Replace(raw, @"-\n([a-z])", "$1");
            raw = Regex.Replace(raw, @"\n{3,}", "\n\n");
            return raw.Trim();
        }

        private static void ParseBtBlock(string s, int pos, int end, StringBuilder sb)
        {
            int i = pos;
            double prevNum = 0;
            while (i < end)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(')
                {
                    sb.Append(ReadPdfLiteral(s, ref i, end));
                }
                else if (c == '[')
                {
                    i++;
                    while (i < end && s[i] != ']')
                    {
                        if (s[i] == '(') sb.Append(ReadPdfLiteral(s, ref i, end));
                        else if (s[i] == '<') sb.Append(ReadPdfHex(s, ref i, end));
                        else if (s[i] == '-' || s[i] == '+' || char.IsDigit(s[i]) || s[i] == '.')
                        {
                            double kern = ReadNumber(s, ref i, end);
                            if (kern <= -130.0 && sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n') sb.Append(' ');
                        }
                        else i++;
                    }
                    if (i < end && s[i] == ']') i++;
                }
                else if (c == '<' && i + 1 < end && s[i + 1] != '<')
                {
                    sb.Append(ReadPdfHex(s, ref i, end));
                }
                else if (c == '-' || c == '+' || char.IsDigit(c) || c == '.')
                {
                    prevNum = ReadNumber(s, ref i, end);
                }
                else if (char.IsLetter(c) || c == '*' || c == '\'' || c == '"')
                {
                    int st = i;
                    while (i < end && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != '[' && s[i] != '<' && s[i] != '/') i++;
                    string op = s.Substring(st, i - st);
                    if (op == "T*" || op == "'" || op == "\"") sb.Append('\n');
                    else if ((op == "Td" || op == "TD") && Math.Abs(prevNum) > 1.5)
                    {
                        sb.Append(Math.Abs(prevNum) > 16.0 ? "\n\n" : "\n");
                        prevNum = 0;
                    }
                }
                else i++;
            }
        }

        private static string ReadPdfLiteral(string s, ref int i, int end)
        {
            StringBuilder b = new StringBuilder();
            i++; int depth = 1;
            while (i < end && depth > 0)
            {
                char c = s[i++];
                if (c == '\\' && i < end)
                {
                    char nx = s[i++];
                    if (nx == 'n') b.Append('\n');
                    else if (nx == 'r') b.Append('\r');
                    else if (nx == 't') b.Append('\t');
                    else if (nx >= '0' && nx <= '7')
                    {
                        int oct = nx - '0';
                        for (int k = 0; k < 2 && i < end && s[i] >= '0' && s[i] <= '7'; k++) oct = (oct * 8) + (s[i++] - '0');
                        if (oct >= 32 && oct < 127) b.Append((char)oct);
                    }
                    else b.Append(nx);
                }
                else if (c == '(') { depth++; b.Append(c); }
                else if (c == ')') { depth--; if (depth > 0) b.Append(c); }
                else if (c >= 32 && c <= 126) b.Append(c);
                else if (c == 10 || c == 13) b.Append(' ');
            }
            return b.ToString();
        }

        private static string ReadPdfHex(string s, ref int i, int end)
        {
            StringBuilder b = new StringBuilder();
            i++;
            StringBuilder hex = new StringBuilder();
            while (i < end && s[i] != '>')
            {
                if (!char.IsWhiteSpace(s[i])) hex.Append(s[i]);
                i++;
            }
            if (i < end && s[i] == '>') i++;
            string h = hex.ToString();
            for (int k = 0; k + 1 < h.Length; k += 2)
            {
                int v;
                if (int.TryParse(h.Substring(k, 2), System.Globalization.NumberStyles.HexNumber, null, out v) && v >= 32 && v <= 126)
                    b.Append((char)v);
            }
            return b.ToString();
        }

        private static double ReadNumber(string s, ref int i, int end)
        {
            int st = i;
            if (i < end && (s[i] == '-' || s[i] == '+')) i++;
            while (i < end && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            double v;
            if (double.TryParse(s.Substring(st, i - st), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        public static string DistillPdf384D(string md, string sha)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("> **384D Shell-0 PDF Distillation** | SHA-256: " + sha);
            sb.AppendLine();
            string[] lines = (md ?? "").Split('\n');
            Regex rx = new Regex(@"\b(Theorem|Lemma|Definition|Proposition|Corollary|Proof|Algorithm|Equation|Invariant|Abstract|Conclusion)\b|[=\+\-\*/\^<>]{2,}|\bO\(", RegexOptions.IgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.StartsWith("#") || rx.IsMatch(ln)) sb.AppendLine(ln);
            }
            return sb.ToString().Trim();
        }
    }

    public static class OmegaZapperAndClipper
    {
        public static List<string> GetDomainZaps(string labRoot, string host)
        {
            List<string> res = new List<string>();
            if (string.IsNullOrEmpty(host)) return res;
            string p = Path.Combine(labRoot, "vault", "dom_zaps.tsv");
            if (!File.Exists(p)) return res;
            foreach (string ln in File.ReadAllLines(p))
            {
                int tab = ln.IndexOf('\t');
                if (tab > 0 && string.Equals(ln.Substring(0, tab), host, StringComparison.OrdinalIgnoreCase))
                    res.Add(ln.Substring(tab + 1));
            }
            return res;
        }

        public static void SaveDomainZap(string labRoot, string host, string selector)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(selector)) return;
            string p = Path.Combine(labRoot, "vault", "dom_zaps.tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            string entry = host.ToLowerInvariant() + "\t" + selector.Trim();
            if (File.Exists(p))
            {
                foreach (string ln in File.ReadAllLines(p))
                    if (string.Equals(ln, entry, StringComparison.OrdinalIgnoreCase)) return;
            }
            File.AppendAllText(p, entry + Environment.NewLine, new UTF8Encoding(false));
        }

        public static int ClearDomainZaps(string labRoot, string host)
        {
            string p = Path.Combine(labRoot, "vault", "dom_zaps.tsv");
            if (!File.Exists(p) || string.IsNullOrEmpty(host)) return 0;
            List<string> kept = new List<string>();
            int removed = 0;
            foreach (string ln in File.ReadAllLines(p))
            {
                if (ln.StartsWith(host + "\t", StringComparison.OrdinalIgnoreCase)) removed++;
                else kept.Add(ln);
            }
            File.WriteAllLines(p, kept.ToArray(), new UTF8Encoding(false));
            return removed;
        }

        public static string BuildInteractiveDomToolsScript()
        {
            return "(function(){ if(window.__omegaDomV04) return; window.__omegaDomV04=true; window.__omegaToolMode='';" +
                "var bt=String.fromCharCode(96), fence=bt+bt+bt, lastEl=null, prevOut='', badge=null;" +
                "function post(m){ try{ if(window.chrome&&window.chrome.webview) window.chrome.webview.postMessage(m); }catch(e){} }" +
                "function ensureBadge(){ if(badge) return badge; badge=document.createElement('div'); badge.id='omega-v04-tool-hud'; badge.style.cssText='position:fixed;top:10px;left:50%;transform:translateX(-50%);z-index:2147483647;padding:7px 14px;border-radius:99px;font:bold 12px Consolas,monospace;display:none;pointer-events:none;box-shadow:0 4px 16px rgba(0,0,0,.65)'; document.documentElement.appendChild(badge); return badge; }" +
                "function clearHi(){ if(lastEl){ try{ lastEl.style.outline=prevOut; }catch(e){} lastEl=null; } }" +
                "window.__omegaSetToolMode=function(m){ clearHi(); window.__omegaToolMode=(window.__omegaToolMode===m)?'':m; var b=ensureBadge(); if(!window.__omegaToolMode){ b.style.display='none'; return; } b.style.display='block'; if(window.__omegaToolMode==='zap'){ b.style.background='#7f1d1d'; b.style.color='#fecaca'; b.style.border='1px solid #ef4444'; b.textContent='ALT+X DOM ZAPPER :: Click clutter to erase forever on site (Esc exit)'; } else { b.style.background='#064e3b'; b.style.color='#a7f3d0'; b.style.border='1px solid #10b981'; b.textContent='ALT+C SUBTREE CLIPPER :: Click theorem/table/code to harvest to Corpus (Esc exit)'; } };" +
                "function selFor(el){ if(el.id&&/^[a-zA-Z][a-zA-Z0-9_-]{1,48}$/.test(el.id)) return '#'+el.id; var tg=el.tagName.toLowerCase(); var cls=(el.className&&typeof el.className==='string')?el.className.trim().split(/\\s+/).filter(function(c){return /^[a-zA-Z][a-zA-Z0-9_-]{1,36}$/.test(c);}).slice(0,2):[]; if(cls.length) return tg+'.'+cls.join('.'); return tg; }" +
                "function clipMd(el){ var tg=(el.tagName||'').toUpperCase(); if(tg==='PRE') return fence+'\\n'+(el.innerText||'').trim()+'\\n'+fence; var cl=el.cloneNode(true); var ks=cl.querySelectorAll?cl.querySelectorAll('annotation[encoding*=\"tex\"]'):[]; for(var i=0;i<ks.length;i++){ var p=ks[i].closest('mjx-container,.katex,math'); if(p) p.textContent=' $'+ks[i].textContent.trim()+'$ '; } var ps=cl.querySelectorAll?cl.querySelectorAll('pre'):[]; for(var j=0;j<ps.length;j++) ps[j].textContent='\\n'+fence+'\\n'+(ps[j].innerText||'').trim()+'\\n'+fence+'\\n'; return (cl.innerText||cl.textContent||'').replace(/\\n{3,}/g,'\\n\\n').trim(); }" +
                "document.addEventListener('mouseover',function(e){ if(!window.__omegaToolMode) return; var t=e.target; if(!t||t.id==='omega-v04-tool-hud'||t===document.body||t===document.documentElement) return; clearHi(); lastEl=t; prevOut=t.style.outline||''; t.style.outline=(window.__omegaToolMode==='zap')?'2px solid #ef4444':'2px solid #10b981'; },true);" +
                "document.addEventListener('click',function(e){ if(!window.__omegaToolMode) return; var t=e.target; if(!t||t.id==='omega-v04-tool-hud') return; e.preventDefault(); e.stopPropagation(); var m=window.__omegaToolMode; clearHi(); if(m==='zap'){ var s=selFor(t); t.style.setProperty('display','none','important'); post('DOM_ZAP|'+s); } else if(m==='clip'){ var md=clipMd(t); window.__omegaSetToolMode(''); if(md) post('DOM_CLIP|'+md); } },true);" +
                "document.addEventListener('keydown',function(e){ if(e.altKey&&(e.key==='x'||e.key==='X')){ e.preventDefault(); window.__omegaSetToolMode('zap'); } else if(e.altKey&&(e.key==='c'||e.key==='C')){ e.preventDefault(); window.__omegaSetToolMode('clip'); } else if(e.key==='Escape'&&window.__omegaToolMode){ e.preventDefault(); window.__omegaSetToolMode(''); } },true);" +
                "})();";
        }
    }
}
