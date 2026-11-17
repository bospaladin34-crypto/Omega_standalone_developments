using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmegaPenroseStudio
{
    public sealed class OmegaPenrosePadServer : IDisposable
    {
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public Action<float, float, int> OnPadInteraction; // kind: 0=phason, 1=stamp/quantum, 2=3D orbit, 3=Miller Bragg inspect
        public Action<string> OnRemoteCommand;
        public Func<string> OnBuildStatusJson;
        public string ConnectedPixelHint = "Pixel OLED Pad Ready (:19785/pad)";

        public void Start(int port)
        {
            if (_running) return;
            _running = true;
            _acceptThread = new Thread(delegate()
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Start();
                    while (_running)
                    {
                        TcpClient cli = _listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleClient, cli);
                    }
                }
                catch { }
            });
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
        }

        private void HandleClient(object state)
        {
            using (TcpClient cli = (TcpClient)state)
            {
                try
                {
                    cli.ReceiveTimeout = 2500;
                    cli.SendTimeout = 2500;
                    using (NetworkStream ns = cli.GetStream())
                    {
                        byte[] buf = new byte[4096];
                        int r = ns.Read(buf, 0, buf.Length);
                        if (r <= 0) return;
                        string req = Encoding.UTF8.GetString(buf, 0, r);
                        int sp1 = req.IndexOf(' ');
                        int sp2 = (sp1 >= 0) ? req.IndexOf(' ', sp1 + 1) : -1;
                        string rawUrl = (sp1 >= 0 && sp2 > sp1) ? req.Substring(sp1 + 1, sp2 - sp1 - 1) : "/";
                        string path = rawUrl;
                        string query = "";
                        int qIdx = rawUrl.IndexOf('?');
                        if (qIdx >= 0) { path = rawUrl.Substring(0, qIdx); query = rawUrl.Substring(qIdx + 1); }

                        if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
                        {
                            string js = (OnBuildStatusJson != null) ? OnBuildStatusJson() : "{\"ok\":true}";
                            WriteHttp(ns, "application/json; charset=utf-8", js);
                            return;
                        }
                        if (path.Equals("/api/touch", StringComparison.OrdinalIgnoreCase))
                        {
                            ConnectedPixelHint = "Pixel OLED Touch LIVE (:19785)";
                            float nx = ParseFloatQ(query, "x", 0.5f);
                            float ny = ParseFloatQ(query, "y", 0.5f);
                            int kind = (int)ParseFloatQ(query, "k", 0.0f);
                            if (OnPadInteraction != null) OnPadInteraction(nx, ny, kind);
                            WriteHttp(ns, "application/json; charset=utf-8", "{\"ok\":true}");
                            return;
                        }
                        if (path.Equals("/api/cmd", StringComparison.OrdinalIgnoreCase))
                        {
                            string act = ParseStrQ(query, "a");
                            if (OnRemoteCommand != null && act.Length > 0) OnRemoteCommand(act);
                            WriteHttp(ns, "application/json; charset=utf-8", "{\"ok\":true}");
                            return;
                        }
                        WriteHttp(ns, "text/html; charset=utf-8", BuildPixelPadHtml());
                    }
                }
                catch { }
            }
        }

        private static float ParseFloatQ(string q, string key, float defVal)
        {
            Match m = Regex.Match(q ?? "", "(?:^|&)" + key + "=([-0-9.]+)");
            float v;
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return defVal;
        }

        private static string ParseStrQ(string q, string key)
        {
            Match m = Regex.Match(q ?? "", "(?:^|&)" + key + "=([A-Za-z0-9_\\-]+)");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static void WriteHttp(NetworkStream ns, string mime, string body)
        {
            byte[] b = Encoding.UTF8.GetBytes(body);
            string hdr = "HTTP/1.1 200 OK\r\nContent-Type: " + mime + "\r\nContent-Length: " + b.Length +
                         "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            ns.Write(b, 0, b.Length);
        }

        private static string BuildPixelPadHtml()
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'/>" +
                   "<meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no'/>" +
                   "<title>OmegaPenrose v1.0.2 GM OLED Pad</title>" +
                   "<style>body{margin:0;background:#070b14;color:#e2e8f0;font-family:sans-serif;overflow:hidden;touch-action:none;}" +
                   "#top{padding:8px 10px;background:#0f172a;display:flex;justify-content:space-between;align-items:center;font-size:12px;border-bottom:1px solid #1e293b;}" +
                   "#pad{width:100vw;height:66vh;background:radial-gradient(circle,#11243e 0%,#060911 78%);position:relative;}" +
                   ".bar{display:flex;gap:5px;padding:5px;background:#0b1120;flex-wrap:wrap;}" +
                   "button{flex:1;padding:8px 5px;border:0;border-radius:6px;background:#0284c7;color:#fff;font-weight:bold;font-size:11px;}" +
                   "</style></head><body>" +
                   "<div id='top'><b>OMEGAPENROSE v1.0.2 GM PAD</b><span id='st'>Mode: 5D/8D Phason</span></div>" +
                   "<div id='pad'></div>" +
                   "<div class='bar'>" +
                   "<button onclick='mode=0;upd()'>Phason</button>" +
                   "<button onclick='mode=1;upd()' style='background:#059669'>Stamp/Psi</button>" +
                   "<button onclick='mode=2;upd()' style='background:#7c3aed'>3D Orbit</button>" +
                   "<button onclick='mode=3;upd()' style='background:#0891b2'>5D Miller</button>" +
                   "</div><div class='bar'>" +
                   "<button onclick=\"cmd('sym')\" style='background:#4338ca'>Symmetry</button>" +
                   "<button onclick=\"cmd('quantum')\" style='background:#be185d'>Quantum</button>" +
                   "<button onclick=\"cmd('holo')\" style='background:#0f766e'>Hologram</button>" +
                   "<button onclick=\"cmd('audio')\" style='background:#b45309'>Audio</button>" +
                   "</div><div class='bar'>" +
                   "<button onclick=\"cmd('view')\" style='background:#0d9488'>View</button>" +
                   "<button onclick=\"cmd('zin')\" style='background:#334155'>Zoom +</button>" +
                   "<button onclick=\"cmd('zout')\" style='background:#334155'>Zoom -</button>" +
                   "<button onclick=\"cmd('orbium')\" style='background:#9333ea'>5-Ring</button>" +
                   "</div>" +
                   "<script>var mode=0,pad=document.getElementById('pad'),busy=false;" +
                   "function upd(){var n=['5D/8D Phason','Stamp Life / Psi','3D Jewel Orbit','5D Miller Peak'];document.getElementById('st').textContent='Mode: '+n[mode];}" +
                   "function send(e){if(busy)return;var r=pad.getBoundingClientRect();" +
                   "var t=e.touches?e.touches[0]:e;var x=Math.max(0,Math.min(1,(t.clientX-r.left)/r.width));" +
                   "var y=Math.max(0,Math.min(1,(t.clientY-r.top)/r.height));busy=true;" +
                   "fetch('/api/touch?x='+x.toFixed(3)+'&y='+y.toFixed(3)+'&k='+mode).finally(function(){busy=false;});}" +
                   "function cmd(a){fetch('/api/cmd?a='+a);}" +
                   "pad.addEventListener('pointerdown',send);pad.addEventListener('pointermove',function(e){if(e.buttons)send(e);});" +
                   "</script></body></html>";
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }
    }

    public static class OmegaPenroseGemmaSynthesizer
    {
        public delegate void GemmaSynthCallback(int sym, int lifeMode, int sp, float phason, float coup, string note);
        public static void SynthesizeAsync(string userPrompt, GemmaSynthCallback onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string p = (userPrompt ?? "").ToLowerInvariant();
                int sym = 0;
                if (p.Contains("spectre") || p.Contains("chiral")) sym = 8;
                else if (p.Contains("hat") || p.Contains("einstein") || p.Contains("monotile")) sym = 7;
                else if (p.Contains("e8") || p.Contains("gosset") || p.Contains("elser") || p.Contains("30")) sym = 6;
                else if (p.Contains("12") || p.Contains("dodec") || p.Contains("stampfli")) sym = 5;
                else if (p.Contains("8") || p.Contains("ammann") || p.Contains("octa")) sym = 4;
                else if (p.Contains("10") || p.Contains("deca")) sym = 3;
                else if (p.Contains("cartwheel") || p.Contains("worm")) sym = 1;

                int lifeMode = 0;
                if (p.Contains("quantum") || p.Contains("kohmoto") || p.Contains("wavepacket") || p.Contains("schrodinger")) lifeMode = 2;
                else if (p.Contains("darwin") || p.Contains("mutat") || p.Contains("evolv") || p.Contains("speciat")) lifeMode = 1;

                int sp = (lifeMode == 2) ? 4 : ((lifeMode == 1) ? 1 : 0);
                if (p.Contains("predator") || p.Contains("chase")) sp = 2;
                else if (p.Contains("surfer") || p.Contains("waveguide")) sp = 3;
                else if (p.Contains("orbium")) sp = 5;
                else if (p.Contains("star") || p.Contains("penta")) sp = 7;

                float phason = 0.37f;
                float coup = 0.38f;
                string note = "Synthesized Sovereign GM Preset";

                try
                {
                    string sys = "Reply ONLY with compact JSON: {\"symmetry\":0..8,\"life\":0..2,\"species\":0..7,\"phason\":0.0..6.28,\"coupling\":0.1..0.7}";
                    string raw = QueryLocalEndpoint("http://127.0.0.1:8097/v1/chat/completions", sys, userPrompt);
                    if (string.IsNullOrEmpty(raw))
                        raw = QueryLocalOllama("http://127.0.0.1:11434/api/generate", sys + "\nUser: " + userPrompt);

                    if (!string.IsNullOrEmpty(raw))
                    {
                        Match ms = Regex.Match(raw, "\"symmetry\"\\s*:\\s*(\\d)");
                        Match ml = Regex.Match(raw, "\"life\"\\s*:\\s*(\\d)");
                        Match mp = Regex.Match(raw, "\"species\"\\s*:\\s*(\\d)");
                        Match mf = Regex.Match(raw, "\"phason\"\\s*:\\s*([0-9.]+)");
                        Match mc = Regex.Match(raw, "\"coupling\"\\s*:\\s*([0-9.]+)");
                        if (ms.Success) sym = Math.Max(0, Math.Min(8, int.Parse(ms.Groups[1].Value)));
                        if (ml.Success) lifeMode = Math.Max(0, Math.Min(2, int.Parse(ml.Groups[1].Value)));
                        if (mp.Success) sp = Math.Max(0, Math.Min(7, int.Parse(mp.Groups[1].Value)));
                        if (mf.Success) float.TryParse(mf.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out phason);
                        if (mc.Success) float.TryParse(mc.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out coup);
                        note = "Local Gemma GM Patch";
                    }
                }
                catch { }

                if (onComplete != null) onComplete(sym, lifeMode, sp, phason, coup, note);
            });
        }

        private static string QueryLocalEndpoint(string url, string sys, string usr)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "POST"; r.ContentType = "application/json"; r.Timeout = 2600; r.ReadWriteTimeout = 2600;
                string body = "{\"model\":\"gemma\",\"temperature\":0.3,\"max_tokens\":120,\"messages\":[{\"role\":\"system\",\"content\":\"" +
                              EscapeJson(sys) + "\"},{\"role\":\"user\",\"content\":\"" + EscapeJson(usr) + "\"}]}";
                byte[] b = Encoding.UTF8.GetBytes(body);
                using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string QueryLocalOllama(string url, string prompt)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "POST"; r.ContentType = "application/json"; r.Timeout = 2600; r.ReadWriteTimeout = 2600;
                string body = "{\"model\":\"gemma3:4b\",\"stream\":false,\"prompt\":\"" + EscapeJson(prompt) + "\"}";
                byte[] b = Encoding.UTF8.GetBytes(body);
                using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
