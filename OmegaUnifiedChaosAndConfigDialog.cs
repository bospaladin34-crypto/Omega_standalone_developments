using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using OmegaPortableConfig;
using OmegaRnDLab;

namespace OmegaStandaloneLab
{
    public static class OmegaUnifiedDeckHttpServer
    {
        private static TcpListener _listener;
        private static volatile bool _running;
        public static volatile string LastChaosSummary = "READY (Click Run 8-Vector Chaos Suite or POST /api/chaos)";
        public static Action ShowMainDeckAction;

        public static void EnsureStarted(int port)
        {
            if (_running) return;
            _running = true;
            Thread th = new Thread(delegate()
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, port);
                    _listener.Start();
                    while (_running)
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                }
                catch { _running = false; }
            });
            th.IsBackground = true;
            th.Name = "OmegaUnifiedDeck19800";
            th.Start();
        }

        public static void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }

        private static void HandleClient(object state)
        {
            using (TcpClient c = (TcpClient)state)
            {
                try
                {
                    c.ReceiveTimeout = 2000;
                    c.SendTimeout = 2000;
                    using (NetworkStream ns = c.GetStream())
                    {
                        byte[] buf = new byte[4096];
                        int n = ns.Read(buf, 0, buf.Length);
                        if (n <= 0) return;
                        string req = Encoding.UTF8.GetString(buf, 0, n);
                        string first = req.Split('\n')[0].Trim();
                        string[] parts = first.Split(' ');
                        string path = parts.Length > 1 ? parts[1].Split('?')[0].ToLowerInvariant() : "/";

                        if (path == "/api/show")
                        {
                            Action showAct = ShowMainDeckAction;
                            if (showAct != null) { try { showAct(); } catch { } }
                            byte[] sraw = Encoding.UTF8.GetBytes("{\"ok\":true,\"shown\":true,\"port\":19800}");
                            string shdr = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + sraw.Length + "\r\nConnection: close\r\n\r\n";
                            byte[] shb = Encoding.ASCII.GetBytes(shdr);
                            ns.Write(shb, 0, shb.Length);
                            ns.Write(sraw, 0, sraw.Length);
                            return;
                        }
                        OmegaHardwareProfile prof = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
                        if (path == "/api/chaos")
                        {
                            ChaosSuiteSummary cs = OmegaRnDEdgeCaseTortureSuite.RunAll8ChaosVectors();
                            LastChaosSummary = string.Format(CultureInfo.InvariantCulture,
                                "{0}/{1} PASS ({2} trials in {3:0.0} ms | RAM={4:0.0} MB)",
                                cs.PassedVectors, cs.TotalVectors, cs.TotalTrials, cs.TotalElapsedMs, cs.WorkingSetMb);
                        }

                        string body = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"service\":\"OmegaUnifiedLabDeck v3.0.9\",\"port\":19800,\"host\":\"{0}\",\"gpu\":\"{1}\",\"gpu_vram_mb\":{2},\"adb_devices\":{3},\"last_chaos\":\"{4}\"}}",
                            Esc(prof.HostAlias), Esc(prof.PrimaryGpuName), prof.PrimaryGpuVramMb, prof.DetectedAdbDevices.Count, Esc(LastChaosSummary));
                        byte[] raw = Encoding.UTF8.GetBytes(body);
                        string hdr = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + raw.Length + "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                        byte[] hb = Encoding.ASCII.GetBytes(hdr);
                        ns.Write(hb, 0, hb.Length);
                        ns.Write(raw, 0, raw.Length);
                    }
                }
                catch { }
            }
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    public sealed class OmegaUnifiedChaosAndConfigDialog : Form
    {
        private TextBox _txtHost;
        private TextBox _txtGpu;
        private TextBox _txtDownloads;
        private TextBox _txtCorpus;
        private TextBox _txtBridge;
        private TextBox _txtLlm;
        private TextBox _txtLog;

        public OmegaUnifiedChaosAndConfigDialog()
        {
            OmegaHardwareProfile p = OmegaHardwareConfig.LoadAndApplyEnvironment(true, true);
            Text = "Omega Unified Lab v3.0.9 :: Hardware Profile (omega.config.json) & 8-Vector R&D Chaos Harness (:19800)";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(920, 660);
            BackColor = Color.FromArgb(10, 15, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.0f);

            int y = 14;
            Label hdr = new Label();
            hdr.Text = "PORTABLE HARDWARE PROFILE (config/omega.config.json) + R&D CHAOS TORTURE HARNESS (:19800)";
            hdr.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
            hdr.ForeColor = Color.FromArgb(56, 189, 248);
            hdr.SetBounds(16, y, 860, 24);
            Controls.Add(hdr);
            y += 30;

            _txtHost = AddRow("Host Alias:", p.HostAlias, ref y);
            _txtGpu = AddRow("Detected GPU (DXGI):", p.PrimaryGpuName, ref y);
            _txtDownloads = AddRow("Downloads Vault Dir:", p.DownloadsDir, ref y);
            _txtCorpus = AddRow("Markdown/JSONL Corpus:", p.CorpusDir, ref y);
            _txtBridge = AddRow("Cloud/Export Bridge Dir:", p.CloudBridgeDir, ref y);
            _txtLlm = AddRow("Local LLM Endpoint:", p.LocalLlmEndpoint, ref y);

            y += 6;
            Button bSave = MakeBtn("Save config/omega.config.json", 16, y, 220, Color.FromArgb(5, 150, 105));
            bSave.Click += delegate
            {
                p.HostAlias = _txtHost.Text.Trim();
                p.PrimaryGpuName = _txtGpu.Text.Trim();
                p.DownloadsDir = _txtDownloads.Text.Trim();
                p.CorpusDir = _txtCorpus.Text.Trim();
                p.CloudBridgeDir = _txtBridge.Text.Trim();
                p.LocalLlmEndpoint = _txtLlm.Text.Trim();
                OmegaHardwareConfig.SaveUserConfig(p);
                Append("[CONFIG SAVED] Wrote local gitignored config/omega.config.json and updated environment.");
            };
            Controls.Add(bSave);

            Button bChaos = MakeBtn("Run 8-Vector R&D Chaos Torture Suite", 246, y, 260, Color.FromArgb(159, 18, 57));
            bChaos.Click += delegate { RunChaosAudit(); };
            Controls.Add(bChaos);

            Button bAdb = MakeBtn("Refresh ADB Silicon Nodes", 516, y, 200, Color.FromArgb(3, 105, 161));
            bAdb.Click += delegate
            {
                List<string> devs = OmegaHardwareConfig.ProbeConnectedAdbDevices();
                Append("[ADB DISCOVERY] Connected Android devices: " + devs.Count);
                for (int i = 0; i < devs.Count; i++) Append("  Slot #" + i + ": " + devs[i]);
            };
            Controls.Add(bAdb);

            Button bApi = MakeBtn("Open :19800 JSON", 726, y, 155, Color.FromArgb(109, 40, 217));
            bApi.Click += delegate { try { Process.Start("http://127.0.0.1:19800/api/status"); } catch { } };
            Controls.Add(bApi);
            y += 40;

            _txtLog = new TextBox();
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BackColor = Color.FromArgb(5, 10, 20);
            _txtLog.ForeColor = Color.FromArgb(167, 243, 208);
            _txtLog.Font = new Font("Consolas", 9.0f);
            _txtLog.SetBounds(16, y, 868, 300);
            Controls.Add(_txtLog);

            Append("Detected DXGI GPU : " + p.PrimaryGpuName + (p.PrimaryGpuVramMb > 0 ? (" (" + p.PrimaryGpuVramMb + " MB VRAM)") : ""));
            Append("Resolved Lab Root : " + p.LabRoot);
            Append("Connected ADB     : " + p.DetectedAdbDevices.Count + " device(s)");
            for (int i = 0; i < p.DetectedAdbDevices.Count; i++) Append("  -> " + p.DetectedAdbDevices[i]);
        }

        private TextBox AddRow(string label, string val, ref int y)
        {
            Label l = new Label();
            l.Text = label;
            l.ForeColor = Color.FromArgb(203, 213, 225);
            l.SetBounds(16, y + 3, 180, 22);
            Controls.Add(l);

            TextBox tb = new TextBox();
            tb.Text = val ?? "";
            tb.BackColor = Color.FromArgb(15, 23, 42);
            tb.ForeColor = Color.White;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.SetBounds(202, y, 680, 24);
            Controls.Add(tb);
            y += 32;
            return tb;
        }

        private static Button MakeBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private void RunChaosAudit()
        {
            ChaosSuiteSummary cs = OmegaRnDEdgeCaseTortureSuite.RunAll8ChaosVectors();
            OmegaUnifiedDeckHttpServer.LastChaosSummary = string.Format(CultureInfo.InvariantCulture,
                "{0}/{1} PASS ({2} trials in {3:0.0} ms | RAM={4:0.0} MB)",
                cs.PassedVectors, cs.TotalVectors, cs.TotalTrials, cs.TotalElapsedMs, cs.WorkingSetMb);
            Append("=== R&D CHAOS TORTURE HARNESS: " + OmegaUnifiedDeckHttpServer.LastChaosSummary + " ===");
            for (int i = 0; i < cs.Vectors.Count; i++)
            {
                ChaosVectorResult v = cs.Vectors[i];
                Append("  [" + (v.Passed ? "PASS" : "FAIL") + "] " + v.Code + " [" + v.Title + "] (" + v.Matched + "/" + v.Trials + " trials, " + v.ElapsedMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms) -> " + v.Summary);
            }
        }

        private void Append(string msg)
        {
            _txtLog.AppendText(msg + "\r\n");
        }
    }
}
