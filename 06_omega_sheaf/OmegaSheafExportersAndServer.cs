using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OmegaSheafStudio
{
    public static class OmegaSheafExporters
    {
        public static string ExportLean4CohomologyWitness(string exportsDir, SheafGraphModel g)
        {
            Directory.CreateDirectory(exportsDir);
            string path = Path.Combine(exportsDir, "sheaf_cohomology_witness_" + DateTime.Now.ToString("HHmmss") + ".lean");

            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("-- Formal Cellular Sheaf Cohomology Witness exported by OmegaSheaf v0.2 (:19784)");
            sb.AppendLine("-- Graph: " + g.GraphTitle);
            sb.AppendLine("");
            sb.AppendLine("def numStalks : Nat := " + g.Nodes.Count);
            sb.AppendLine("def numRestrictions : Nat := " + g.Edges.Count);
            sb.AppendLine("def consistentEdges : Nat := " + g.ConsistentEdgeCount);
            sb.AppendLine("def obstructedEdges : Nat := " + g.ObstructedEdgeCount);
            sb.AppendLine("def bettiH0 : Nat := " + g.BettiH0);
            sb.AppendLine("def bettiH1 : Nat := " + g.BettiH1);
            sb.AppendLine("");
            sb.AppendLine("theorem edge_partition_conservation : consistentEdges + obstructedEdges = numRestrictions := by rfl");
            if (g.IsGlobalConsensusH1Zero)
            {
                sb.AppendLine("theorem global_section_obstruction_free : bettiH1 = 0 := by rfl");
                sb.AppendLine("theorem all_restrictions_commute : consistentEdges = numRestrictions := by rfl");
            }
            else
            {
                sb.AppendLine("theorem nontrivial_cohomology_detected : bettiH1 > 0 := by decide");
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string ExportVectorSvg(string exportsDir, SheafGraphModel g, float[] sx, float[] sy)
        {
            Directory.CreateDirectory(exportsDir);
            string path = Path.Combine(exportsDir, "sheaf_truth_blueprint_" + DateTime.Now.ToString("HHmmss") + ".svg");
            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1100 760\" width=\"1100\" height=\"760\">");
            sb.AppendLine("<rect width=\"1100\" height=\"760\" fill=\"#070b14\"/>");
            sb.AppendFormat("<text x=\"24\" y=\"36\" fill=\"#34d399\" font-family=\"Consolas,monospace\" font-size=\"16\">{0} | H^1={1}</text>\r\n",
                XmlEsc(g.GraphTitle), g.BettiH1);

            int n = Math.Min(g.Nodes.Count, sx.Length);
            for (int e = 0; e < g.Edges.Count; e++)
            {
                SheafRestrictionEdge ed = g.Edges[e];
                if (ed.SrcIdx < 0 || ed.SrcIdx >= n || ed.DstIdx < 0 || ed.DstIdx >= n) continue;
                string col = ed.InContradictionLoop ? "#f43f5e" : (ed.IsObstructed ? "#fbbf24" : "#10b981");
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<line x1=\"{0:0.0}\" y1=\"{1:0.0}\" x2=\"{2:0.0}\" y2=\"{3:0.0}\" stroke=\"{4}\" stroke-width=\"{5:0.0}\"/>\r\n",
                    sx[ed.SrcIdx], sy[ed.SrcIdx], sx[ed.DstIdx], sy[ed.DstIdx], col, ed.InContradictionLoop ? 3.2f : 1.9f);
            }
            for (int i = 0; i < n; i++)
            {
                SheafStalkNode nd = g.Nodes[i];
                string col = nd.InContradictionLoop ? "#fb7185" : "#34d399";
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<circle cx=\"{0:0.0}\" cy=\"{1:0.0}\" r=\"15\" fill=\"#0f172a\" stroke=\"{2}\" stroke-width=\"2.5\"/>\r\n",
                    sx[i], sy[i], col);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<text x=\"{0:0.0}\" y=\"{1:0.0}\" fill=\"#e2e8f0\" font-family=\"Consolas,monospace\" font-size=\"11\" text-anchor=\"middle\">{2} [E{3}]</text>\r\n",
                    sx[i], sy[i] + 29.0f, XmlEsc(nd.Title), nd.E8RootIndex);
            }
            sb.AppendLine("</svg>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string ExportJsonlAuditLedger(string exportsDir, SheafGraphModel g)
        {
            Directory.CreateDirectory(exportsDir);
            string path = Path.Combine(exportsDir, "sheaf_audit_ledger_" + DateTime.Now.ToString("HHmmss") + ".jsonl");
            StringBuilder sb = new StringBuilder(4096);
            string rawSummary = g.GraphTitle + "|" + g.Nodes.Count + "|" + g.Edges.Count + "|" + g.BettiH1 + "|" + g.TotalDirichletEnergy.ToString("0.0000", CultureInfo.InvariantCulture);
            string sha = ComputeSha256Hex(rawSummary);

            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{{\"type\":\"sheaf_receipt\",\"title\":\"{0}\",\"nodes\":{1},\"edges\":{2},\"h1_zero\":{3},\"betti_h0\":{4},\"betti_h1\":{5},\"dirichlet_energy\":{6:0.0000},\"exact_pct\":{7:0.0},\"curl_pct\":{8:0.0},\"harmonic_pct\":{9:0.0},\"sha256\":\"{10}\"}}\r\n",
                JsonEsc(g.GraphTitle), g.Nodes.Count, g.Edges.Count, g.IsGlobalConsensusH1Zero ? "true" : "false",
                g.BettiH0, g.BettiH1, g.TotalDirichletEnergy, g.ExactGradEnergyPct, g.CurlCycleEnergyPct, g.HarmonicH1EnergyPct, sha);

            for (int i = 0; i < g.Nodes.Count; i++)
            {
                SheafStalkNode nd = g.Nodes[i];
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"type\":\"stalk\",\"id\":{0},\"title\":\"{1}\",\"source\":\"{2}\",\"e8_root\":{3},\"sqnr_db\":{4:0.0},\"in_h1_loop\":{5}}}\r\n",
                    nd.Id, JsonEsc(nd.Title), JsonEsc(nd.SourceDoc), nd.E8RootIndex, nd.SqnrDb, nd.InContradictionLoop ? "true" : "false");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string ExportOmshToOmegaAv(string exportsDir, string avDir, SheafGraphModel g)
        {
            Directory.CreateDirectory(exportsDir);
            string fn = "sheaf_cohomology_" + DateTime.Now.ToString("HHmmss") + ".omsh";
            string expPath = Path.Combine(exportsDir, fn);

            double obsFactor = g.IsGlobalConsensusH1Zero ? 0.0 : Math.Min(2.5, 0.65 + g.ContradictionLoops.Count * 0.35);
            StringBuilder sb = new StringBuilder(1024);
            sb.AppendLine("// Exported by OmegaSheaf v0.2 (:19784) -> OmegaAV v0.7.1 (:19779)");
            sb.AppendFormat(CultureInfo.InvariantCulture, "obs = {0:0.000} + m1 * 0.35;\r\n", obsFactor);
            sb.AppendLine("sx = x * 2.4 * k1; sy = y * 2.4 * k1;");
            sb.AppendLine("ed = e8voronoi(sx + cos(t*0.35), sy + sin(t*0.31), sin(t*0.22), cos(t*0.19));");
            sb.AppendLine("curl = sin(6.0 * theta - t * (1.8 + obs * 2.5) + rad * 5.0 * k2);");
            sb.AppendLine("ridge = exp(-abs(ed - 0.70) * (12.5 * k3));");
            if (g.IsGlobalConsensusH1Zero)
            {
                sb.AppendLine("r = saturate(ridge * 0.16 + rdV * 0.25);");
                sb.AppendLine("g = saturate(ridge * 0.96 + 0.30 * abs(curl) + bass * 0.28);");
                sb.AppendLine("b = saturate(ridge * 0.72 + 0.24 * rdU);");
            }
            else
            {
                sb.AppendLine("r = saturate(ridge * 0.98 + 0.35 * max(0.0, curl) + bass * 0.32);");
                sb.AppendLine("g = saturate(ridge * 0.20 + rdV * 0.36);");
                sb.AppendLine("b = saturate(ridge * 0.48 + 0.22 * abs(curl));");
            }

            UTF8Encoding u8 = new UTF8Encoding(false);
            File.WriteAllText(expPath, sb.ToString(), u8);
            if (!string.IsNullOrEmpty(avDir) && Directory.Exists(avDir))
            {
                string shDir = Path.Combine(avDir, "shaders");
                Directory.CreateDirectory(shDir);
                string staged = Path.Combine(shDir, fn);
                File.WriteAllText(staged, sb.ToString(), u8);
                try { File.WriteAllText(Path.Combine(avDir, "inbox_open.cmd"), staged, u8); } catch { }
            }
            return expPath;
        }

        public static string SynthesizeSheafViaGemma(SheafGraphModel g, string prompt)
        {
            g.Nodes.Clear();
            g.Edges.Clear();
            g.ContradictionLoops.Clear();

            string p = (prompt ?? "").ToLowerInvariant();
            bool wantParadox = p.Contains("paradox") || p.Contains("drift") || p.Contains("contradict") || p.Contains("bug") || p.Contains("deadlock") || p.Contains("softlock") || p.Contains("56b");
            int nNodes = 6;
            for (int k = 3; k <= 12; k++)
            {
                if (p.Contains(k.ToString() + "-node") || p.Contains(k.ToString() + " node")) { nNodes = k; break; }
            }

            g.GraphTitle = "Gemma Synthesized Sheaf :: " + (prompt.Length > 48 ? prompt.Substring(0, 48) : prompt);
            g.ProvenanceNote = "Synthesized " + nNodes + " E8 stalks (" + (wantParadox ? "with H^1 != 0 obstruction loop" : "H^1 = 0 consensus") + ")";

            float[] b8 = new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
            string[] roles = p.Contains("quest")
                ? new string[] { "Stage 10 Wake", "Stage 20 Item", "Stage 30 NPC", "Stage 40 Gate", "Stage 50 Vault", "Stage 60 End", "Stage 70 Epilogue", "Stage 80 L0" }
                : new string[] { "Rust #[repr(C)] L0", "C++23 ZeroCopy Ring", "Deno FFI Surface", "C# DirectCompute Host", "HLSL cs_5_0 SRV", "Pixel 10 Edge NDK", "Lean 4 Proof", "Iroh QUIC Frame" };

            for (int i = 0; i < nNodes; i++)
            {
                double ang = (2.0 * Math.PI * i) / nNodes;
                float px = (float)(Math.Cos(ang) * 1.95);
                float py = (float)(Math.Sin(ang) * 1.45);
                OmegaSheafIngestor.AddNode(g, roles[i % roles.Length], "GemmaSheaf#" + i, wantParadox && i == nNodes - 1 ? "56B Misaligned Struct" : "64B Canonical E8 Stalk", StalkCategory.RustL0Abi, px, py, 0.0f, i == 0, b8);
            }

            for (int i = 0; i < nNodes; i++)
            {
                int nxt = (i + 1) % nNodes;
                bool flip = wantParadox && (i == nNodes - 1);
                OmegaSheafIngestor.AddEdge(g, i, nxt, flip ? "PARADOX: 64B != 56B ABI Drift!" : "Commuting Contract", flip ? -1.0f : 1.0f, 0.0f, flip ? 0.42f : 0.0f);
            }
            if (nNodes >= 5)
                OmegaSheafIngestor.AddEdge(g, 0, nNodes / 2, "Cross-Chord Verification", 1.0f, 0.0f, 0.0f);

            return "Synthesized " + nNodes + "-stalk Cellular Sheaf (" + (wantParadox ? "H^1 != 0 Paradox Ready for Surgery" : "H^1 = 0 Global Consensus") + ")";
        }

        public static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static string XmlEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string ComputeSha256Hex(string s)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hb = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < hb.Length; i++) sb.Append(hb[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    // =========================================================================
    // PILLAR 4A: BIDIRECTIONAL PIXEL OLED MULTI-TOUCH SURGERY PAD (:19784/sheaf)
    // =========================================================================
    public sealed class OmegaSheafHttpServer : IDisposable
    {
        private TcpListener _listener;
        private volatile bool _running;
        public volatile byte[] LatestFrameJpeg = new byte[0];
        public volatile string LatestStatusJson = "{\"ok\":true,\"port\":19784}";
        public volatile string LatestGraphJson = "{\"nodes\":[],\"edges\":[]}";
        public Action<string> OnRemoteCommand;
        public int TouchPacketsCount = 0;

        public void Start(int port)
        {
            if (_running) return;
            _running = true;
            Thread t = new Thread(delegate()
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, port);
                    _listener.Server.ExclusiveAddressUse = false;
                    _listener.Start();
                    while (_running)
                    {
                        TcpClient c = _listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleClient, c);
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void HandleClient(object obj)
        {
            using (TcpClient c = (TcpClient)obj)
            {
                c.NoDelay = true;
                c.ReceiveTimeout = 4000; c.SendTimeout = 4000;
                try
                {
                    using (NetworkStream ns = c.GetStream())
                    {
                        byte[] buf = new byte[4096];
                        int r = ns.Read(buf, 0, buf.Length);
                        if (r <= 0) return;
                        string req = Encoding.UTF8.GetString(buf, 0, r);
                        string first = req.Split('\n')[0].Trim();
                        string[] parts = first.Split(' ');
                        string target = parts.Length > 1 ? parts[1] : "/";

                        if (target.StartsWith("/frame.jpg", StringComparison.OrdinalIgnoreCase))
                        {
                            WriteResp(ns, "200 OK", "image/jpeg", LatestFrameJpeg ?? new byte[0]);
                            return;
                        }
                        if (target.StartsWith("/api/surgeon/audit", StringComparison.OrdinalIgnoreCase) || target.StartsWith("/surgeon", StringComparison.OrdinalIgnoreCase))
                        {
                            OmegaSheafModSurgeon.Track05StressSummary s5 = OmegaSheafModSurgeon.OmegaSheafModStressSuite.RunAll8Vectors();
                            string js5 = "{\"ok\":" + (s5.AllPassed ? "true" : "false") + ",\"module\":\"OmegaSheaf Pillar 6 FFI/Quest/AOB Surgeon\",\"passed\":" + s5.PassedCount + ",\"total\":" + s5.TotalCount + ",\"trials\":" + s5.TotalTrials + "}";
                            WriteResp(ns, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(js5));
                            return;
                        }
                        if (target.StartsWith("/api/status", StringComparison.OrdinalIgnoreCase))
                        {
                            WriteResp(ns, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(LatestStatusJson));
                            return;
                        }
                        if (target.StartsWith("/api/graph", StringComparison.OrdinalIgnoreCase))
                        {
                            WriteResp(ns, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(LatestGraphJson));
                            return;
                        }
                        if (target.StartsWith("/api/touch", StringComparison.OrdinalIgnoreCase) || target.StartsWith("/api/cmd", StringComparison.OrdinalIgnoreCase))
                        {
                            Interlocked.Increment(ref TouchPacketsCount);
                            int q = target.IndexOf('?');
                            string qs = (q >= 0 && q + 1 < target.Length) ? target.Substring(q + 1) : "";
                            if (!string.IsNullOrEmpty(qs) && OnRemoteCommand != null) OnRemoteCommand(qs);
                            WriteResp(ns, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                            return;
                        }

                        string html = BuildInteractiveOledTouchHtml();
                        WriteResp(ns, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                    }
                }
                catch { }
            }
        }

        private static void WriteResp(NetworkStream ns, string code, string ct, byte[] body)
        {
            string hdr = "HTTP/1.1 " + code + "\r\nContent-Type: " + ct + "\r\nContent-Length: " + body.Length + "\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            if (body.Length > 0) ns.Write(body, 0, body.Length);
        }

        private static string BuildInteractiveOledTouchHtml()
        {
            return "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no'>" +
                "<title>OmegaSheaf v0.2 :: 2-Way OLED Touch Surgery Pad</title>" +
                "<style>body{margin:0;background:#040711;color:#e2e8f0;font-family:Consolas,monospace;padding:8px;touch-action:none;user-select:none}" +
                ".ban{padding:9px;border-radius:8px;font-weight:bold;text-align:center;margin-bottom:6px;border:1px solid #38bdf8;font-size:13px}" +
                "canvas{width:100%;height:340px;border-radius:10px;border:1px solid #1e293b;background:#070b14;display:block}" +
                ".sub{font-size:11px;color:#94a3b8;margin:5px 2px}" +
                ".row{display:flex;gap:6px;flex-wrap:wrap;margin-top:6px}" +
                "button{flex:1;min-width:96px;padding:10px 6px;border-radius:7px;border:1px solid #38bdf8;background:#0f172a;color:#fff;font-weight:bold;font-family:Consolas,monospace;font-size:12px}" +
                "input[type=range]{width:100%;margin:4px 0}</style></head><body>" +
                "<div id='ban' class='ban'>OMEGASHEAF v0.2 (:19784) :: 2-WAY TOUCH PAD</div>" +
                "<canvas id='cv' width='680' height='420'></canvas>" +
                "<div id='inf' class='sub'>Tap/drag any stalk on OLED to move on PC | Double-tap crimson edge to heal!</div>" +
                "<div class='sub'>Filtration Scrubber (eps): <span id='epv'>0.14</span><input id='eps' type='range' min='3' max='60' value='14' oninput='setEps(this.value)'/></div>" +
                "<div class='row'>" +
                "<button style='background:#065f46' onclick=\"act('act=rectify')\">Rectify H^1=0</button>" +
                "<button style='background:#9f1239' onclick=\"act('act=paradox')\">Paradox</button>" +
                "<button style='background:#1e3a8a' onclick=\"act('act=suite')\">Suite FFI</button>" +
                "<button style='background:#6d28d9' onclick=\"act('act=ck')\">CK Quest</button>" +
                "</div>" +
                "<script>" +
                "var cv=document.getElementById('cv'),ctx=cv.getContext('2d'),G={nodes:[],edges:[]},dragId=-1,lastTap=0;" +
                "function act(q){fetch('/api/touch?'+q).catch(()=>{});}" +
                "function setEps(v){document.getElementById('epv').textContent=(v/100).toFixed(2);act('act=eps&val='+(v/100));}" +
                "function draw(){ctx.fillStyle='#070b14';ctx.fillRect(0,0,cv.width,cv.height);" +
                "for(var e=0;e<G.edges.length;e++){var ed=G.edges[e],u=G.nodes[ed.u],v=G.nodes[ed.v];if(!u||!v)continue;" +
                "ctx.strokeStyle=ed.loop?'#f43f5e':(ed.obs?'#fbbf24':'#10b981');ctx.lineWidth=ed.loop?3.5:2.0;" +
                "ctx.beginPath();ctx.moveTo(u.nx*cv.width,u.ny*cv.height);ctx.lineTo(v.nx*cv.width,v.ny*cv.height);ctx.stroke();}" +
                "for(var i=0;i<G.nodes.length;i++){var n=G.nodes[i],x=n.nx*cv.width,y=n.ny*cv.height;" +
                "ctx.fillStyle=n.loop?'rgba(244,63,94,0.22)':'rgba(16,185,129,0.20)';ctx.beginPath();ctx.arc(x,y,21,0,6.283);ctx.fill();" +
                "ctx.strokeStyle=n.pin?'#facc15':(n.loop?'#fb7185':'#34d399');ctx.lineWidth=2.6;ctx.beginPath();ctx.arc(x,y,14,0,6.283);ctx.stroke();" +
                "ctx.fillStyle='#f8fafc';ctx.font='bold 12px Consolas,monospace';ctx.textAlign='center';ctx.fillText(n.t,x,y+28);}}" +
                "function pt(ev){var r=cv.getBoundingClientRect(),t=ev.touches?ev.touches[0]:ev;return {x:(t.clientX-r.left)*cv.width/r.width,y:(t.clientY-r.top)*cv.height/r.height};}" +
                "cv.addEventListener('pointerdown',function(ev){var p=pt(ev),best=-1,bd=999;" +
                "for(var i=0;i<G.nodes.length;i++){var dx=p.x-G.nodes[i].nx*cv.width,dy=p.y-G.nodes[i].ny*cv.height,d=Math.hypot(dx,dy);if(d<30&&d<bd){bd=d;best=i;}}" +
                "if(best>=0){dragId=best;document.getElementById('inf').textContent='Selected Stalk ['+best+']: '+G.nodes[best].t+' (E8#'+G.nodes[best].r+')';act('act=sel_node&id='+best);return;}" +
                "for(var e=0;e<G.edges.length;e++){var ed=G.edges[e],u=G.nodes[ed.u],v=G.nodes[ed.v];if(!u||!v)continue;" +
                "var ax=u.nx*cv.width,ay=u.ny*cv.height,bx=v.nx*cv.width,by=v.ny*cv.height,abx=bx-ax,aby=by-ay,h=Math.max(0,Math.min(1,((p.x-ax)*abx+(p.y-ay)*aby)/(abx*abx+aby*aby+1)));" +
                "if(Math.hypot(p.x-(ax+abx*h),p.y-(ay+aby*h))<18){act('act=toggle_edge&id='+e);document.getElementById('inf').textContent='Toggled/Healed Edge #'+e;break;}}});" +
                "cv.addEventListener('pointermove',function(ev){if(dragId<0)return;var p=pt(ev);var nx=Math.max(0.06,Math.min(0.94,p.x/cv.width)),ny=Math.max(0.08,Math.min(0.92,p.y/cv.height));" +
                "if(G.nodes[dragId]){G.nodes[dragId].nx=nx;G.nodes[dragId].ny=ny;draw();}act('act=drag&id='+dragId+'&nx='+nx.toFixed(3)+'&ny='+ny.toFixed(3));});" +
                "window.addEventListener('pointerup',function(){dragId=-1;});" +
                "setInterval(function(){fetch('/api/graph').then(r=>r.json()).then(g=>{if(dragId<0){G=g;draw();}" +
                "var b=document.getElementById('ban');b.style.background=g.h1_zero?'#064e3b':'#7f1d1d';" +
                "b.textContent=(g.h1_zero?'CONSENSUS H^1 = 0':'PARADOX H^1 != 0 ('+g.loops+' loops)')+' | E_F='+g.energy+' | '+g.fps+' FPS';}).catch(()=>{});},180);" +
                "</script></body></html>";
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }
    }
}
