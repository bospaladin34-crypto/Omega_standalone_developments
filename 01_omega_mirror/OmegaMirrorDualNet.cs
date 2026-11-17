using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

public static class OmegaMirrorDualNet
{
  public static volatile string ActiveTarget = "";
  private static bool _hooked = false, _uiEnhanced = false, _wifiTurbo = false, _autoPausedOnMin = false;
  private static DateTime _lastCacheUtc = DateTime.MinValue;
  private static string _cachedIp = "", _cachedTarget = "";
  private static readonly Dictionary<string, string> _usbSubnetCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

  public static void EnsureHooked()
  {
    if (_hooked) return;
    _hooked = true;
    try { Application.Idle += OnAppIdle; } catch { }
  }

  private static string ResolveVaultDownloadsRoot()
  {
    string cfgRoot = Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT");
      if (!string.IsNullOrEmpty(cfgRoot) && Directory.Exists(cfgRoot)) return Path.Combine(cfgRoot, "downloads");
    string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    DirectoryInfo parent = Directory.GetParent(baseDir);
    return Path.Combine(parent != null ? parent.FullName : baseDir, "downloads");
  }

  private static void OnAppIdle(object sender, EventArgs e)
  {
    try
    {
      if (Application.OpenForms.Count == 0) return;
      Form main = Application.OpenForms[0];
      if (main == null || main.IsDisposed) return;

      ComboBox adbBox = FindFirstComboBox(main);
      if (adbBox != null)
      {
        string cur = CleanSerial(adbBox.Text);
        if (cur.Length > 0 && cur != ActiveTarget)
        {
          ActiveTarget = cur;
          _lastCacheUtc = DateTime.MinValue;
          ThreadPool.QueueUserWorkItem(delegate { ArmAllReverseTunnels(); });
        }
      }
      if (_uiEnhanced) return;
      _uiEnhanced = true;
      main.Text = "ACT-Omega Lab :: OmegaMirror v0.9 (Dual-Pixel 8+10 Smart Tunnel + Drag-Drop ADB Vault)";

      main.AllowDrop = true;
      main.DragEnter += delegate(object ds, DragEventArgs de)
      {
        if (de.Data != null && de.Data.GetDataPresent(DataFormats.FileDrop)) de.Effect = DragDropEffects.Copy;
        else de.Effect = DragDropEffects.None;
      };
      main.DragDrop += delegate(object ds2, DragEventArgs de2)
      {
        string[] dropped = de2.Data.GetData(DataFormats.FileDrop) as string[];
        if (dropped == null || dropped.Length == 0) return;
        PushFilesToActivePixelAsync(main, adbBox, dropped);
      };

      EnsurePcToPhoneServerStarted(main);
      PopulateAllAdbDevices(adbBox);

      Button oldTun = FindButtonContaining(main, "Open Tunnel") ?? FindButtonContaining(main, "Open USB");
      if (oldTun != null) ReplaceActionButton(oldTun, "Open Tunnel", delegate { LaunchOnActivePixel(main, adbBox, false); });

      Button oldLan = FindButtonContaining(main, "Open Wi-Fi") ?? FindButtonContaining(main, "Smart Wi-Fi");
      if (oldLan != null) ReplaceActionButton(oldLan, "Smart Wi-Fi", delegate { LaunchOnActivePixel(main, adbBox, true); });

      Button saveBtn = FindButtonContaining(main, "Save PNG");
      if (saveBtn != null && saveBtn.Parent != null && FindButtonContaining(main, "USB->WiFi") == null)
      {
        int x = saveBtn.Right + 4;
        Button wifiBtn = MakeBtn("USB->WiFi", x, saveBtn.Top, 86, saveBtn.Height, Color.FromArgb(88, 28, 135), saveBtn.Font);
        wifiBtn.Click += delegate
        {
          ThreadPool.QueueUserWorkItem(delegate
          {
            string msg = PromoteUsbToWifiAndTunnel();
            try { main.BeginInvoke((Action)delegate { PopulateAllAdbDevices(adbBox); MessageBox.Show(main, msg, "OmegaMirror v0.9", MessageBoxButtons.OK, MessageBoxIcon.Information); }); } catch { }
          });
        };
        saveBtn.Parent.Controls.Add(wifiBtn); x = wifiBtn.Right + 4;

        Button turboBtn = MakeBtn("Turbo:OFF", x, saveBtn.Top, 84, saveBtn.Height, Color.FromArgb(15, 118, 110), saveBtn.Font);
        turboBtn.Click += delegate
        {
          _wifiTurbo = !_wifiTurbo;
          turboBtn.Text = _wifiTurbo ? "Turbo:ON" : "Turbo:OFF";
          turboBtn.BackColor = _wifiTurbo ? Color.FromArgb(180, 83, 9) : Color.FromArgb(15, 118, 110);
          SetPhoneCapturePaused(main, _wifiTurbo);
        };
        saveBtn.Parent.Controls.Add(turboBtn); x = turboBtn.Right + 4;

        Button clipBtn = MakeBtn("Clip<->", x, saveBtn.Top, 68, saveBtn.Height, Color.FromArgb(3, 105, 161), saveBtn.Font);
        clipBtn.Click += delegate { SyncClipboardWithPixel(main, adbBox); };
        saveBtn.Parent.Controls.Add(clipBtn); x = clipBtn.Right + 4;

        Button pullBtn = MakeBtn("Pull DL", x, saveBtn.Top, 68, saveBtn.Height, Color.FromArgb(5, 150, 105), saveBtn.Font);
        pullBtn.Click += delegate { PullLatestPixelDownloadToVault(main, adbBox); };
        saveBtn.Parent.Controls.Add(pullBtn);
      }
      main.Resize += delegate
      {
        if (main.WindowState == FormWindowState.Minimized) { if (SetPhoneCapturePaused(main, true)) _autoPausedOnMin = true; }
        else if (_autoPausedOnMin && !_wifiTurbo) { _autoPausedOnMin = false; SetPhoneCapturePaused(main, false); }
      };
      ThreadPool.QueueUserWorkItem(delegate { ArmAllReverseTunnels(); });
    }
    catch { }
  }

  private static void PushFilesToActivePixelAsync(Form main, ComboBox adbBox, string[] files)
  {
    string serial = adbBox != null ? CleanSerial(adbBox.Text) : "";
    main.Text = "OmegaMirror v0.9 :: ADB Pushing " + files.Length + " file(s) -> Pixel /sdcard/Download/...";
    ThreadPool.QueueUserWorkItem(delegate
    {
      List<string> targets = new List<string>();
      if (!string.IsNullOrEmpty(serial)) targets.Add(serial); else targets.AddRange(ListConnectedSerials());
      int pushed = 0;
      for (int t = 0; t < targets.Count; t++)
      {
        for (int f = 0; f < files.Length; f++)
        {
          if (!File.Exists(files[f])) continue;
          string fn = Path.GetFileName(files[f]).Replace("\"", "_");
          string rem = "/sdcard/Download/" + fn;
          RunAdb("-s " + targets[t] + " push \"" + files[f] + "\" \"" + rem + "\"", 180000);
          RunAdb("-s " + targets[t] + " shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d \"file://" + rem + "\"", 3500);
          pushed++;
        }
      }
      try
      {
        main.BeginInvoke((Action)delegate
        {
          main.Text = "ACT-Omega Lab :: OmegaMirror v0.9 — Pushed " + pushed + " item(s) to Pixel /sdcard/Download/!";
        });
      }
      catch { }
    });
  }

  private static void PullLatestPixelDownloadToVault(Form main, ComboBox adbBox)
  {
    string serial = adbBox != null ? CleanSerial(adbBox.Text) : "";
    ThreadPool.QueueUserWorkItem(delegate
    {
      if (string.IsNullOrEmpty(serial)) { List<string> d = ListConnectedSerials(); if (d.Count > 0) serial = d[0]; }
      if (string.IsNullOrEmpty(serial)) return;
      string lsOut = RunAdb("-s " + serial + " shell ls -1t /sdcard/Download", 3500);
      string newest = "";
      foreach (string ln in lsOut.Split('\n'))
      {
        string c = ln.Trim();
        if (c.Length > 0 && !c.StartsWith("total ") && !c.Contains("/") && c != "." && c != "..") { newest = c; break; }
      }
      if (string.IsNullOrEmpty(newest)) return;
      string vaultDir = Path.Combine(ResolveVaultDownloadsRoot(), "Files");
      string ext = (Path.GetExtension(newest) ?? "").ToLowerInvariant();
      if (ext == ".pdf") vaultDir = Path.Combine(ResolveVaultDownloadsRoot(), "Papers");
      else if (ext == ".wav" || ext == ".mp3" || ext == ".flac") vaultDir = Path.Combine(ResolveVaultDownloadsRoot(), "Audio");
      Directory.CreateDirectory(vaultDir);
      string localDest = Path.Combine(vaultDir, newest);
      RunAdb("-s " + serial + " pull \"/sdcard/Download/" + newest.Replace("\"", "") + "\" \"" + localDest + "\"", 120000);
      if (File.Exists(localDest))
      {
        try
        {
          Process.Start("explorer.exe", "/select,\"" + localDest + "\"");
          main.BeginInvoke((Action)delegate { main.Text = "ACT-Omega Lab :: OmegaMirror v0.9 — Pulled " + newest + " -> Vault!"; });
        }
        catch { }
      }
    });
  }

  private static Button MakeBtn(string txt, int x, int y, int w, int h, Color bg, Font f)
  {
    Button b = new Button(); b.Text = txt; b.SetBounds(x, y, w, h); b.FlatStyle = FlatStyle.Flat;
    b.BackColor = bg; b.ForeColor = Color.White; b.Font = f; b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
    return b;
  }

  private static bool SetPhoneCapturePaused(Form main, bool pause)
  {
    Button b = FindButtonContaining(main, "Phone -> PC");
    if (b == null) return false;
    bool running = b.Text.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0;
    if (pause && running) { b.PerformClick(); return true; }
    if (!pause && !running) { b.PerformClick(); return true; }
    return false;
  }

  private static void SyncClipboardWithPixel(Form main, ComboBox adbBox)
  {
    string pcClip = "";
    try { if (Clipboard.ContainsText()) pcClip = (Clipboard.GetText() ?? "").Trim(); } catch { }
    string s = adbBox != null ? CleanSerial(adbBox.Text) : "";
    ThreadPool.QueueUserWorkItem(delegate
    {
      if (string.IsNullOrEmpty(s)) { List<string> d = ListConnectedSerials(); if (d.Count > 0) s = d[0]; }
      if (string.IsNullOrEmpty(s)) return;
      string px = RunAdb("-s " + s + " shell cmd clipboard get-text", 1800).Trim();
      if (!string.IsNullOrEmpty(px) && px != pcClip && !px.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
        try { main.BeginInvoke((Action)delegate { Clipboard.SetDataObject(px, true, 4, 40); }); } catch { }
      else if (!string.IsNullOrEmpty(pcClip))
        RunAdb("-s " + s + " shell cmd clipboard set-text \"" + pcClip.Replace("\"", "") + "\"", 1800);
    });
  }

  private static string CleanSerial(string raw)
  {
    string s = (raw ?? "").Trim(); int i = s.IndexOf(" [");
    return i > 0 ? s.Substring(0, i).Trim() : s;
  }

  private static void EnsurePcToPhoneServerStarted(Form main)
  {
    Button b = FindButtonContaining(main, "PC -> Phone");
    if (b != null && b.Text.IndexOf("OFF", StringComparison.OrdinalIgnoreCase) >= 0) b.PerformClick();
  }

  private static void PopulateAllAdbDevices(ComboBox cb)
  {
    if (cb == null) return;
    List<string> devs = ListConnectedSerials();
    for (int i = 0; i < devs.Count; i++)
    {
      bool has = false;
      for (int j = 0; j < cb.Items.Count; j++) if (CleanSerial(Convert.ToString(cb.Items[j])) == devs[i]) { has = true; break; }
      if (!has) cb.Items.Add(devs[i]);
    }
    if (string.IsNullOrWhiteSpace(cb.Text) && devs.Count > 0) cb.Text = devs[0];
  }

  private static void ReplaceActionButton(Button oldB, string lbl, Action act)
  {
    if (oldB == null || oldB.Parent == null) return;
    Button b = new Button(); b.Text = lbl; b.Bounds = oldB.Bounds; b.FlatStyle = oldB.FlatStyle;
    b.BackColor = oldB.BackColor; b.ForeColor = oldB.ForeColor; b.Font = oldB.Font;
    b.FlatAppearance.BorderSize = oldB.FlatAppearance.BorderSize; b.FlatAppearance.BorderColor = oldB.FlatAppearance.BorderColor;
    b.Click += delegate { act(); };
    oldB.Visible = false; oldB.Parent.Controls.Add(b); b.BringToFront();
  }

  private static void LaunchOnActivePixel(Form main, ComboBox cb, bool tryLanFirst)
  {
    EnsurePcToPhoneServerStarted(main);
    string serial = cb != null ? CleanSerial(cb.Text) : "";
    ThreadPool.QueueUserWorkItem(delegate
    {
      List<string> tgts = new List<string>();
      if (!string.IsNullOrEmpty(serial)) tgts.Add(serial); else tgts.AddRange(ListConnectedSerials());
      string stamp = (DateTime.UtcNow.Ticks % 99999).ToString();
      for (int i = 0; i < tgts.Count; i++)
      {
        string s = tgts[i]; ActiveTarget = s; _lastCacheUtc = DateTime.MinValue;
        RunAdb("-s " + s + " reverse tcp:19777 tcp:19777", 2200);
        RunAdb("-s " + s + " reverse tcp:9778 tcp:9778", 1800);
        RunAdb("-s " + s + " reverse tcp:19779 tcp:19779", 1800);
        string url = "http://127.0.0.1:19777/?v=" + stamp;
        if (tryLanFirst)
        {
          string ip = ResolveLanIpForSerial(s);
          string pr = RunAdb("-s " + s + " shell \"toybox nc -z -w 1 " + ip + " 19777 && echo OPEN\"", 2000);
          if (ip != "127.0.0.1" && pr.IndexOf("OPEN", StringComparison.Ordinal) >= 0) url = "http://" + ip + ":19777/?v=" + stamp;
        }
        RunAdb("-s " + s + " shell am start -a android.intent.action.VIEW -d \"" + url + "\"", 2800);
      }
    });
  }

  private static ComboBox FindFirstComboBox(Control r)
  {
    foreach (Control c in r.Controls) { ComboBox cb = c as ComboBox ?? FindFirstComboBox(c); if (cb != null) return cb; }
    return null;
  }

  private static Button FindButtonContaining(Control r, string needle)
  {
    foreach (Control c in r.Controls)
    {
      Button b = c as Button;
      if (b != null && b.Visible && (b.Text ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return b;
      Button sub = FindButtonContaining(c, needle); if (sub != null) return sub;
    }
    return null;
  }

  public static string ResolveAdbExe()
  {
    string[] p = new string[] { Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe"), @"C:\platform-tools\adb.exe", @"E:\platform-tools\adb.exe" };
    for (int i = 0; i < p.Length; i++) if (File.Exists(p[i])) return p[i];
    return "adb.exe";
  }

  private static string RunAdb(string args, int ms)
  {
    try
    {
      ProcessStartInfo psi = new ProcessStartInfo(ResolveAdbExe(), args);
      psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
      using (Process p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(ms); return o ?? ""; }
    }
    catch { return ""; }
  }

  public static List<string> ListConnectedSerials()
  {
    List<string> l = new List<string>();
    foreach (string ln in RunAdb("devices", 2200).Split('\n'))
    {
      string t = ln.Trim(); if (t.EndsWith("\tdevice")) { string s = t.Split('\t')[0].Trim(); if (s.Length > 0) l.Add(s); }
    }
    return l;
  }

  public static void ArmAllReverseTunnels()
  {
    List<string> d = ListConnectedSerials();
    for (int i = 0; i < d.Count; i++)
    {
      RunAdb("-s " + d[i] + " reverse tcp:19777 tcp:19777", 1800);
      RunAdb("-s " + d[i] + " reverse tcp:9778 tcp:9778", 1800);
      RunAdb("-s " + d[i] + " reverse tcp:19779 tcp:19779", 1800);
    }
  }

  public static string PromoteUsbToWifiAndTunnel()
  {
    List<string> all = ListConnectedSerials(), usb = new List<string>(), rep = new List<string>();
    for (int i = 0; i < all.Count; i++) if (!all[i].Contains(":")) usb.Add(all[i]);
    if (usb.Count == 0) { ArmAllReverseTunnels(); return "Pixels (" + string.Join(", ", all.ToArray()) + ") wireless + reverse-tunneled (:19777/:9778/:19779)!"; }
    for (int i = 0; i < usb.Count; i++)
    {
      string s = usb[i], ipOut = RunAdb("-s " + s + " shell ip -f inet addr show wlan0", 2200);
      Match m = Regex.Match(ipOut, @"inet\s+(\d+\.\d+\.\d+\.\d+)");
      RunAdb("-s " + s + " tcpip 5555", 2200); Thread.Sleep(650);
      if (m.Success) { string ip = m.Groups[1].Value; string c = RunAdb("connect " + ip + ":5555", 2800).Trim(); rep.Add(s + " -> " + ip + ":5555 (" + c + ")"); }
      else rep.Add(s + " -> tcpip 5555 armed");
    }
    ArmAllReverseTunnels();
    return "Wireless ADB Armed:\r\n" + string.Join("\r\n", rep.ToArray());
  }

  public static string ResolveLanIp()
  {
    EnsureHooked();
    string tgt = ActiveTarget ?? "";
    if ((DateTime.UtcNow - _lastCacheUtc).TotalSeconds < 3.0 && tgt == _cachedTarget && !string.IsNullOrEmpty(_cachedIp)) return _cachedIp;
    _cachedTarget = tgt; _cachedIp = ResolveLanIpForSerial(tgt); _lastCacheUtc = DateTime.UtcNow;
    return _cachedIp;
  }

  public static string ResolveLanIpForSerial(string tgt)
  {
    string pre = "";
    if (!string.IsNullOrEmpty(tgt) && tgt.Contains(":"))
    {
      string[] o = tgt.Split(':')[0].Split('.'); if (o.Length == 4) pre = o[0] + "." + o[1] + "." + o[2] + ".";
    }
    else if (!string.IsNullOrEmpty(tgt))
    {
      string sub;
      if (!_usbSubnetCache.TryGetValue(tgt, out sub))
      {
        Match m = Regex.Match(RunAdb("-s " + tgt + " shell ip -f inet addr show wlan0", 1400), @"inet\s+(\d+\.\d+\.\d+)\.\d+");
        sub = m.Success ? (m.Groups[1].Value + ".") : ""; _usbSubnetCache[tgt] = sub;
      }
      pre = sub;
    }
    List<string> ips = new List<string>();
    try
    {
      foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
      {
        if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
        foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
          if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
          {
            string ip = ua.Address.ToString(); if (!ip.StartsWith("127.") && !ip.StartsWith("169.254.")) ips.Add(ip);
          }
      }
    }
    catch { }
    if (!string.IsNullOrEmpty(pre)) for (int i = 0; i < ips.Count; i++) if (ips[i].StartsWith(pre)) return ips[i];
    for (int i = 0; i < ips.Count; i++) if (ips[i].StartsWith("192.168.137.")) return ips[i];
    return ips.Count > 0 ? ips[0] : "127.0.0.1";
  }
}
