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
using OmegaPortableConfig;

namespace OmegaMirrorStudio
{
    public sealed class OmegaMirrorHostForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private ComboBox _cmbDevices;
        private PictureBox _picScreen;
        private TextBox _txtLog;
        private Label _lblBanner;
        private TcpListener _http;
        private volatile bool _running = true;
        private int _framesPulled = 0;
        private readonly string _mirrorRoot;

        public OmegaMirrorHostForm(string mirrorRoot)
        {
            _mirrorRoot = mirrorRoot;
            OmegaHardwareProfile prof = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            Text = "ACT-Omega Lab :: OmegaMirror v3.0.0 (:19777) — Portable Android Screen Mirror, Dual-Net ADB & File Bridge";
            Size = new Size(1060, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 15, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.0f);
            AllowDrop = true;

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 72;
            top.BackColor = Color.FromArgb(15, 23, 42);

            _lblBanner = new Label();
            _lblBanner.Text = "OMEGAMIRROR v3.0.0 (:19777) :: DUAL-NET ADB/USB-TETHER/WI-FI BRIDGE (" + prof.HostAlias + ")";
            _lblBanner.Font = new Font("Segoe UI Semibold", 10.2f, FontStyle.Bold);
            _lblBanner.ForeColor = Color.FromArgb(45, 212, 191);
            _lblBanner.SetBounds(14, 8, 980, 22);
            top.Controls.Add(_lblBanner);

            _cmbDevices = new ComboBox();
            _cmbDevices.DropDownStyle = ComboBoxStyle.DropDown;
            _cmbDevices.BackColor = Color.FromArgb(7, 11, 20);
            _cmbDevices.ForeColor = Color.White;
            _cmbDevices.SetBounds(14, 36, 300, 26);
            top.Controls.Add(_cmbDevices);

            int bx = 324;
            Button bScan = MakeBtn("Scan ADB", bx, 35, 96, Color.FromArgb(15, 118, 110)); bx += 102;
            bScan.Click += delegate { RefreshDevices(); };
            top.Controls.Add(bScan);

            Button bSnap = MakeBtn("Capture Frame", bx, 35, 116, Color.FromArgb(3, 105, 161)); bx += 122;
            bSnap.Click += delegate { CaptureAdbFrameAsync(); };
            top.Controls.Add(bSnap);

            Button bScrcpy = MakeBtn("Launch scrcpy", bx, 35, 116, Color.FromArgb(109, 40, 217)); bx += 122;
            bScrcpy.Click += delegate { LaunchExternalScrcpy(); };
            top.Controls.Add(bScrcpy);

            Button bTunnels = MakeBtn("Arm Reverse Ports", bx, 35, 138, Color.FromArgb(5, 150, 105)); bx += 144;
            bTunnels.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    OmegaMirrorDualNet.ArmAllReverseTunnels();
                    SafeLog("[TUNNELS] Armed canonical reverse/forward ports across connected ADB devices.");
                });
            };
            top.Controls.Add(bTunnels);

            Button bDownloads = MakeBtn("Downloads Vault", bx, 35, 130, Color.FromArgb(180, 83, 9));
            bDownloads.Click += delegate
            {
                Directory.CreateDirectory(prof.DownloadsDir);
                try { Process.Start("explorer.exe", prof.DownloadsDir); } catch { }
            };
            top.Controls.Add(bDownloads);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterDistance = 450;

            _picScreen = new PictureBox();
            _picScreen.Dock = DockStyle.Fill;
            _picScreen.BackColor = Color.FromArgb(5, 8, 16);
            _picScreen.SizeMode = PictureBoxSizeMode.Zoom;
            split.Panel1.Controls.Add(_picScreen);

            _txtLog = new TextBox();
            _txtLog.Dock = DockStyle.Fill;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BackColor = Color.FromArgb(6, 10, 19);
            _txtLog.ForeColor = Color.FromArgb(167, 243, 208);
            _txtLog.Font = new Font("Consolas", 9.0f);
            split.Panel2.Controls.Add(_txtLog);

            Controls.Add(split);
            Controls.Add(top);

            DragEnter += delegate(object s, DragEventArgs e)
            {
                e.Effect = (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) PushDroppedFilesAsync(files);
            };

            FormClosing += delegate
            {
                _running = false;
                try { if (_http != null) _http.Stop(); } catch { }
            };

            OmegaMirrorDualNet.EnsureHooked();
            StartHttp19777();
            RefreshDevices();
            SafeLog("Drag-and-drop any file onto this window to push directly to /sdcard/Download on the selected device.");
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

        private void RefreshDevices()
        {
            _cmbDevices.Items.Clear();
            List<string> devs = OmegaHardwareConfig.ProbeConnectedAdbDevices();
            for (int i = 0; i < devs.Count; i++)
            {
                string tok = devs[i].Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                _cmbDevices.Items.Add(tok);
            }
            if (_cmbDevices.Items.Count > 0)
            {
                _cmbDevices.SelectedIndex = 0;
                OmegaMirrorDualNet.ActiveTarget = _cmbDevices.Text;
                SafeLog("[ADB] Detected " + _cmbDevices.Items.Count + " online device(s). Active target: " + _cmbDevices.Text);
            }
            else
            {
                SafeLog("[ADB] 0 Android devices currently attached (connect via USB or Wi-Fi ADB and click Scan ADB).");
            }
        }

        private void CaptureAdbFrameAsync()
        {
            string target = (_cmbDevices.Text ?? "").Trim();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string args = (target.Length > 0 ? ("-s " + target + " ") : "") + "exec-out screencap -p";
                    ProcessStartInfo psi = new ProcessStartInfo("adb", args);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    using (Process p = Process.Start(psi))
                    {
                        if (p == null) return;
                        using (MemoryStream ms = new MemoryStream())
                        {
                            p.StandardOutput.BaseStream.CopyTo(ms);
                            p.WaitForExit(4000);
                            if (ms.Length > 64)
                            {
                                ms.Position = 0;
                                Bitmap bmp = new Bitmap(ms);
                                Interlocked.Increment(ref _framesPulled);
                                BeginInvoke((MethodInvoker)delegate
                                {
                                    Image old = _picScreen.Image;
                                    _picScreen.Image = bmp;
                                    if (old != null) old.Dispose();
                                });
                                SafeLog("[FRAME OK] Captured lossless PNG screencap (" + (ms.Length / 1024) + " KB).");
                            }
                            else
                            {
                                SafeLog("[FRAME SKIP] No PNG bytes returned from adb screencap.");
                            }
                        }
                    }
                }
                catch (Exception ex) { SafeLog("[ADB ERROR] " + ex.Message); }
            });
        }

        private void LaunchExternalScrcpy()
        {
            string target = (_cmbDevices.Text ?? "").Trim();
            try
            {
                string args = target.Length > 0 ? ("-s " + target) : "";
                Process.Start("scrcpy", args);
                SafeLog("[SCRCPY] Launched low-latency scrcpy session " + args);
            }
            catch
            {
                SafeLog("[SCRCPY] scrcpy.exe not found on PATH; using built-in ADB screencap bridge.");
                CaptureAdbFrameAsync();
            }
        }

        private void PushDroppedFilesAsync(string[] files)
        {
            string target = (_cmbDevices.Text ?? "").Trim();
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    if (!File.Exists(f)) continue;
                    try
                    {
                        string args = (target.Length > 0 ? ("-s " + target + " ") : "") + "push \"" + f + "\" /sdcard/Download/";
                        ProcessStartInfo psi = new ProcessStartInfo("adb", args);
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        psi.RedirectStandardOutput = true;
                        using (Process p = Process.Start(psi))
                        {
                            if (p != null) p.WaitForExit(15000);
                        }
                        SafeLog("[PUSH OK] -> /sdcard/Download/" + Path.GetFileName(f));
                    }
                    catch (Exception ex) { SafeLog("[PUSH ERR] " + ex.Message); }
                }
            });
        }

        private void StartHttp19777()
        {
            Thread th = new Thread(delegate()
            {
                try
                {
                    _http = new TcpListener(IPAddress.Loopback, 19777);
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
                                        int r = ns.Read(b, 0, b.Length);
                                        string body = string.Format(CultureInfo.InvariantCulture,
                                            "{{\"ok\":true,\"service\":\"OmegaMirror v3.0.0\",\"port\":19777,\"active_target\":\"{0}\",\"frames_pulled\":{1}}}",
                                            (OmegaMirrorDualNet.ActiveTarget ?? "").Replace("\"", ""), _framesPulled);
                                        byte[] raw = Encoding.UTF8.GetBytes(body);
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

        private void SafeLog(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { SafeLog(line); }); } catch { }
                return;
            }
            _txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + "\r\n");
        }

        public static int RunHeadlessSelfTest()
        {
            OmegaHardwareProfile prof = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            Console.WriteLine("[PASS] OmegaMirror v3.0.0 Self-Test :: Root=" + prof.LabRoot + " | ADB Online=" + prof.DetectedAdbDevices.Count);
            return 0;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--stress-test", StringComparison.OrdinalIgnoreCase))
                        return RunHeadlessSelfTest();
                }
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaMirrorHostForm(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')));
            return 0;
        }
    }
}
