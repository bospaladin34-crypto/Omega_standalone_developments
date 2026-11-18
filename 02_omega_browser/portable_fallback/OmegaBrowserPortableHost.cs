using System;
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
using OmegaNativeWorkbench;
using OmegaPortableConfig;

namespace OmegaBrowserPortable
{
    public sealed class OmegaBrowserPortableForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private TextBox _txtUrl;
        private TextBox _txtOut;
        private WebBrowser _tridentView;
        private TcpListener _http;
        private volatile bool _running = true;

        public OmegaBrowserPortableForm(string browserRoot)
        {
            OmegaHardwareProfile prof = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            Text = "ACT-Omega Lab :: OmegaBrowser v3.0.0 (:9778 Zero-DLL Portable Workbench Mode — SimHash-64 + PDF Harvester)";
            Size = new Size(1220, 780);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 15, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.0f);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 46;
            top.BackColor = Color.FromArgb(15, 23, 42);

            _txtUrl = new TextBox();
            _txtUrl.Text = "https://arxiv.org";
            _txtUrl.BackColor = Color.FromArgb(7, 11, 20);
            _txtUrl.ForeColor = Color.White;
            _txtUrl.SetBounds(12, 10, 560, 26);
            top.Controls.Add(_txtUrl);

            Button bGo = MakeBtn("Fetch & Harvest", 580, 9, 136, Color.FromArgb(2, 132, 199));
            bGo.Click += delegate { ProbeUrl(); };
            top.Controls.Add(bGo);

            Button bHome = MakeBtn("Local Home", 722, 9, 110, Color.FromArgb(15, 118, 110));
            bHome.Click += delegate
            {
                string hp = Path.Combine(browserRoot, "home", "index.html");
                if (File.Exists(hp)) _tridentView.Navigate(new Uri(hp));
            };
            top.Controls.Add(bHome);

            Button bAudit = MakeBtn("Run 8-Vector Workbench Audit", 838, 9, 220, Color.FromArgb(159, 18, 57));
            bAudit.Click += delegate
            {
                Track01StressSummary s = OmegaWorkbenchStressSuite.RunAll8Vectors();
                _txtOut.AppendText(string.Format(CultureInfo.InvariantCulture,
                    "[WORKBENCH 8-VECTOR AUDIT] {0}/{1} PASS | {2} trials | Native RAM={3:0.0} MB\r\n",
                    s.PassedCount, s.TotalCount, s.TotalTrials, s.TrimmedWorkingSetMb));
            };
            top.Controls.Add(bAudit);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterDistance = 720;

            _tridentView = new WebBrowser();
            _tridentView.Dock = DockStyle.Fill;
            _tridentView.ScriptErrorsSuppressed = true;
            split.Panel1.Controls.Add(_tridentView);

            _txtOut = new TextBox();
            _txtOut.Dock = DockStyle.Fill;
            _txtOut.Multiline = true;
            _txtOut.ReadOnly = true;
            _txtOut.ScrollBars = ScrollBars.Vertical;
            _txtOut.BackColor = Color.FromArgb(6, 10, 19);
            _txtOut.ForeColor = Color.FromArgb(167, 243, 208);
            _txtOut.Font = new Font("Consolas", 8.8f);
            split.Panel2.Controls.Add(_txtOut);

            Controls.Add(split);
            Controls.Add(top);

            FormClosing += delegate
            {
                _running = false;
                try { if (_http != null) _http.Stop(); } catch { }
            };

            string homeHtml = Path.Combine(browserRoot, "home", "index.html");
            if (File.Exists(homeHtml))
            {
                try { _tridentView.Navigate(new Uri(homeHtml)); } catch { }
            }
            StartApi9778();
            _txtOut.AppendText("[PORTABLE MODE] Built with zero external WebView2 DLLs. Drop Microsoft.Web.WebView2.*.dll into 02_omega_browser/ and re-run Build-OmegaLabs.ps1 anytime to enable full Chromium WebView2 mode.\r\n");
        }

        private static Button MakeBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            return b;
        }

        private void ProbeUrl()
        {
            string url = (_txtUrl.Text ?? "").Trim();
            if (url.Length == 0) return;
            try { _tridentView.Navigate(url); } catch { }
            HttpProbeResult res = OmegaWorkbenchCore.ExecuteBoundedHttpProbe("GET", url, "", "", 262144, 8000);
            ulong sim = OmegaWorkbenchCore.ComputeSimHash64(Encoding.UTF8.GetBytes(res.BodyPreview ?? ""));
            _txtOut.AppendText(string.Format(CultureInfo.InvariantCulture,
                "[HTTP {0}] {1} ({2:0.0} ms, {3} B) | SimHash64=0x{4:X16}\r\n",
                res.StatusCode, url, res.TotalMs, res.PayloadBytes, sim));
        }

        private void StartApi9778()
        {
            Thread th = new Thread(delegate()
            {
                try
                {
                    _http = new TcpListener(IPAddress.Loopback, 9778);
                    _http.Start();
                    while (_running)
                    {
                        TcpClient c = _http.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(delegate(object st)
                        {
                            using (TcpClient cl = (TcpClient)st)
                            {
                                try
                                {
                                    using (NetworkStream ns = cl.GetStream())
                                    {
                                        byte[] b = new byte[1024];
                                        ns.Read(b, 0, b.Length);
                                        byte[] raw = Encoding.UTF8.GetBytes("{\"ok\":true,\"service\":\"OmegaBrowser v3.0.0 (Portable Native Workbench)\",\"port\":9778}");
                                        byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + raw.Length + "\r\nConnection: close\r\n\r\n");
                                        ns.Write(hdr, 0, hdr.Length);
                                        ns.Write(raw, 0, raw.Length);
                                    }
                                }
                                catch { }
                            }
                        }, c);
                    }
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--stress-test", StringComparison.OrdinalIgnoreCase))
                    {
                        Track01StressSummary sum = OmegaWorkbenchStressSuite.RunAll8Vectors();
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "[OMEGABROWSER+WORKBENCH 8-VECTOR AUDIT] {0}/{1} PASS | {2} trials | RAM={3:0.0} MB",
                            sum.PassedCount, sum.TotalCount, sum.TotalTrials, sum.TrimmedWorkingSetMb));
                        return sum.AllPassed ? 0 : 1;
                    }
                }
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaBrowserPortableForm(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')));
            return 0;
        }
    }
}
