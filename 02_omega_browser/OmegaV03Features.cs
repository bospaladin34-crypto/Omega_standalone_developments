using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OmegaBrowserV03
{
public sealed partial class BrowserForm
{
    private async Task ApplyDomainPrefsAndStateAsync(BrowserTab tab)
    {
        if (tab == null || tab.View == null || tab.View.CoreWebView2 == null) return;
        CoreWebView2 core = tab.View.CoreWebView2;
        string host = GetHostSafe(core.Source);
        DomainPref dp = null;
        if (!string.IsNullOrEmpty(host)) _domainPrefs.TryGetValue(host, out dp);
        tab.View.ZoomFactor = dp != null ? dp.Zoom : 1.0;

        bool dim = dp != null && dp.NightDim;
        string scaleJs = "window.__omegaVim=" + (_vimMode ? "true" : "false") + ";" +
            "document.documentElement.style.fontSize='" + Math.Round(_globalTextScale * 100) + "%';" +
            "document.documentElement.style.filter='" + (dim ? "brightness(0.84) contrast(0.96)" : "") + "';";
        await core.ExecuteScriptAsync(scaleJs);

        List<string> zaps = OmegaZapperAndClipper.GetDomainZaps(_labRoot, host);
        if (zaps.Count > 0)
        {
            string css = OmegaJson.Esc(string.Join(", ", zaps.ToArray()) + " { display: none !important; }");
            await core.ExecuteScriptAsync("(function(){ var st=document.getElementById('omega-zaps'); if(!st){ st=document.createElement('style'); st.id='omega-zaps'; document.head.appendChild(st); } st.textContent=\"" + css + "\"; })();");
        }

        string note = OmegaVault.GetStickyNote(_labRoot, tab.Url);
        if (!string.IsNullOrEmpty(note))
        {
            string nEsc = OmegaJson.Esc(note);
            await core.ExecuteScriptAsync("(function(){ var old=document.getElementById('omega-sticky-badge'); if(old) old.remove(); var d=document.createElement('div'); d.id='omega-sticky-badge'; d.style.cssText='position:fixed;right:12px;top:12px;z-index:2147483646;max-width:260px;background:#422006;color:#fef08a;border:1px solid #eab308;border-radius:8px;padding:8px 10px;font:12px Segoe UI,sans-serif;box-shadow:0 4px 12px rgba(0,0,0,.5)'; d.textContent='Lab Note: " + nEsc + "'; d.onclick=function(){d.remove();}; document.documentElement.appendChild(d); })();");
        }
        await RestoreFormDraftIfAnyAsync(tab);
    }

    private void SaveFormDraft(string url, string fieldKey, string val)
    {
        try
        {
            string clean = OmegaUrlCleaner.CleanUrl(url);
            if (string.IsNullOrEmpty(clean)) return;
            List<string> kept = new List<string>();
            if (File.Exists(_draftsPath))
            {
                foreach (string ln in File.ReadAllLines(_draftsPath))
                    if (!ln.StartsWith(clean + "\t" + fieldKey + "\t", StringComparison.OrdinalIgnoreCase)) kept.Add(ln);
            }
            kept.Add(clean + "\t" + fieldKey + "\t" + val.Replace("\r", "").Replace("\n", "\\n"));
            File.WriteAllLines(_draftsPath, kept.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    private async Task RestoreFormDraftIfAnyAsync(BrowserTab tab)
    {
        if (!File.Exists(_draftsPath) || tab.Url.StartsWith("omega://")) return;
        string clean = OmegaUrlCleaner.CleanUrl(tab.Url);
        foreach (string ln in File.ReadAllLines(_draftsPath))
        {
            string[] p = ln.Split('\t');
            if (p.Length >= 3 && string.Equals(p[0], clean, StringComparison.OrdinalIgnoreCase))
            {
                string k = OmegaJson.Esc(p[1]), v = OmegaJson.Esc(p[2].Replace("\\n", "\n"));
                await tab.View.CoreWebView2.ExecuteScriptAsync("(function(){ var el=document.getElementById('" + k + "')||document.querySelector('[name=\"" + k + "\"]'); if(el&&!el.value) el.value=\"" + v + "\"; })();");
            }
        }
    }

    private static string ResolveNavUrl(string raw, string ws)
    {
        string s = (raw ?? "").Trim();
        if (s.Equals("omega://companion", StringComparison.OrdinalIgnoreCase))
            return "http://127.0.0.1:9778/companion";
        return OmegaUrlCleaner.ResolveOmniboxOrBang(s, ws);
    }

    private async void CycleWorkspace()
    {
        string[] order = new string[] { "Research", "Training", "Dev" };
        int idx = Array.IndexOf(order, _workspace);
        await SwitchWorkspaceAsync(order[(idx + 1) % order.Length]);
    }

    private async Task SwitchWorkspaceAsync(string ws)
    {
        _workspace = ws;
        BrowserTab found = null;
        for (int i = 0; i < _tabs.Count; i++) if (string.Equals(_tabs[i].Workspace, ws, StringComparison.OrdinalIgnoreCase)) { found = _tabs[i]; break; }
        if (found == null) await CreateTabAsync(ws, "Default", "omega://home", true, false);
        else await ActivateTabAsync(found);
        UpdateTelemetryBar();
    }

    private void ShowContainerMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        for (int i = 0; i < ContainerNames.Length; i++)
        {
            string c = ContainerNames[i];
            cms.Items.Add("New Tab in [" + c + "] Sovereign Container", null, async delegate
            {
                _activeContainer = c;
                await CreateTabAsync(_workspace, c, "omega://home", true, false);
            });
        }
        cms.Show(_btnContainer, new Point(0, _btnContainer.Height));
    }

    private void ShowZoomAndScaleMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        double[] zooms = new double[] { 0.80, 0.90, 1.00, 1.10, 1.25, 1.50 };
        for (int i = 0; i < zooms.Length; i++)
        {
            double z = zooms[i];
            cms.Items.Add("Site Zoom: " + Math.Round(z * 100) + "% (Remember for Domain)", null, async delegate { await SetActiveDomainZoomAsync(z, null); });
        }
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add("Toggle 2 AM Night Dimmer for Current Domain", null, async delegate
        {
            string h = GetActiveHost();
            DomainPref dp; bool cur = (!string.IsNullOrEmpty(h) && _domainPrefs.TryGetValue(h, out dp)) && dp.NightDim;
            await SetActiveDomainZoomAsync(null, !cur);
        });
        cms.Items.Add(new ToolStripSeparator());
        double[] scales = new double[] { 1.0, 1.10, 1.20 };
        for (int j = 0; j < scales.Length; j++)
        {
            double sc = scales[j];
            cms.Items.Add("Global Text Scale: " + Math.Round(sc * 100) + "%", null, async delegate { _globalTextScale = sc; if (_active != null) await ApplyDomainPrefsAndStateAsync(_active); });
        }
        cms.Show(_btnZoom, new Point(0, _btnZoom.Height));
    }

    private async Task SetActiveDomainZoomAsync(double? newZoom, bool? newDim)
    {
        if (_active == null) return;
        string h = GetActiveHost();
        if (string.IsNullOrEmpty(h)) return;
        DomainPref dp;
        if (!_domainPrefs.TryGetValue(h, out dp)) { dp = new DomainPref(); dp.Host = h; _domainPrefs[h] = dp; }
        if (newZoom.HasValue) dp.Zoom = Math.Max(0.5, Math.Min(2.5, newZoom.Value));
        if (newDim.HasValue) dp.NightDim = newDim.Value;
        OmegaSecurity.SaveDomainPrefs(_prefsPath, _domainPrefs);
        await ApplyDomainPrefsAndStateAsync(_active);
        SyncActiveChrome();
    }

    private async void NavigateActive(string input)
    {
        if (_active == null || _active.View == null || _active.View.CoreWebView2 == null) return;
        string raw = (input ?? "").Trim();
        if (raw.Length == 0) return;
        _active.Url = raw;
        string resolved = ResolveNavUrl(raw, _workspace);
        if (resolved.StartsWith("http://127.0.0.1:19777", StringComparison.OrdinalIgnoreCase) && Process.GetProcessesByName("OmegaMirror").Length == 0)
        {
            try
            {
                string mExe = Path.Combine(Directory.GetParent(_labRoot).FullName, "01_omega_mirror", "OmegaMirror.exe");
                if (File.Exists(mExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(mExe);
                    psi.WorkingDirectory = Path.GetDirectoryName(mExe);
                    Process.Start(psi);
                    _lblStatusL.Text = "Auto-launching OmegaMirror v0.9 (:19777)...";
                    for (int w = 0; w < 20; w++)
                    {
                        await Task.Delay(150);
                        try { using (System.Net.Sockets.TcpClient tc = new System.Net.Sockets.TcpClient()) { tc.Connect("127.0.0.1", 19777); break; } } catch { }
                    }
                }
            }
            catch { }
        }
        _active.View.CoreWebView2.Navigate(resolved);
    }

    private void OpenFindBar()
    {
        _findBar.Visible = true;
        _txtFind.Focus(); _txtFind.SelectAll();
    }

    private async void CloseFindBar()
    {
        _findBar.Visible = false;
        if (_active != null && _active.View != null && _active.View.CoreWebView2 != null)
            await _active.View.CoreWebView2.ExecuteScriptAsync(OmegaInjectedJs.BuildFindInPageScript("", false, 0));
    }

    private async Task RunFindInPageAsync(int step)
    {
        if (_active == null || _active.View == null || _active.View.CoreWebView2 == null) return;
        string raw = await _active.View.CoreWebView2.ExecuteScriptAsync(OmegaInjectedJs.BuildFindInPageScript(_txtFind.Text, _chkFindCase.Checked, step));
        _lblFindHits.Text = OmegaJson.UnwrapJsString(raw);
    }

    private void OpenDualPixelHub()
    {
        string url = _active != null ? _active.Url : "omega://home";
        OmegaV04Studios.ShowDualPixelHubDialog(this, url,
            async delegate(string pulled) { await CreateTabAsync(_workspace, _activeContainer, pulled, true, false); },
            delegate(string vp) { SetViewportPreset(vp); },
            delegate(string msg) { _lblStatusL.Text = msg; });
    }

    private async Task RefreshPixelBadgeAsync()
    {
        List<PixelDeviceInfo> devs = await Task.Run(delegate { return DualPixelMesh.DiscoverPixels(); });
        if (devs.Count == 0)
        {
            _btnPixels.Text = "Pixels: 0";
            _btnPixels.BackColor = Color.FromArgb(51, 65, 85);
        }
        else
        {
            List<string> tags = new List<string>();
            for (int i = 0; i < devs.Count; i++)
                tags.Add((devs[i].Model.Contains("10") ? "P10" : (devs[i].Model.Contains("8") ? "P8" : "Px")) + (devs[i].IsWireless ? ":WiFi" : ":USB"));
            _btnPixels.Text = "Pixels: " + string.Join("+", tags.ToArray());
            _btnPixels.BackColor = Color.FromArgb(6, 95, 70);
        }
    }

    private async Task BeamOledCompanionAsync()
    {
        string url = _active != null ? _active.Url : "omega://home";
        if (_active != null) await RefreshCompanionCacheAsync(_active);
        string msg = await Task.Run(delegate { return DualPixelMesh.BroadcastToAllPixels(url, true); });
        _lblStatusL.Text = msg;
    }

    private async Task QuickPullPixelUrlAsync()
    {
        List<PixelDeviceInfo> devs = await Task.Run(delegate { return DualPixelMesh.DiscoverPixels(); });
        if (devs.Count == 0) { _lblStatusL.Text = "No Pixel ADB devices connected to pull from."; return; }
        for (int i = 0; i < devs.Count; i++)
        {
            string ser = devs[i].Serial;
            string u = await Task.Run(delegate { return DualPixelMesh.PullUrlOrClipFromPixel(ser); });
            if (!string.IsNullOrEmpty(u))
            {
                await CreateTabAsync(_workspace, _activeContainer, u, true, false);
                _lblStatusL.Text = "Pulled URL from " + devs[i].Model + " -> " + u;
                return;
            }
        }
        _lblStatusL.Text = "No foreground http(s) URL or URL clipboard found on connected Pixel(s).";
    }

    private void OpenConceptBraidModal()
    {
        OmegaV04Studios.ShowConceptBraidDialog(this, _tabs, _labRoot, _shEndpoint.Text.Trim(),
            async delegate(BrowserTab t) { return await ExtractPageReceiptAsync(t, "distilled"); },
            delegate(string msg) { _lblStatusL.Text = msg; });
    }

    private void ShowZapMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        cms.Items.Add("Toggle Surgical DOM Zapper (Alt+X — Click Element to Erase Forever)", null, async delegate { await ToggleDomToolAsync("zap"); });
        cms.Items.Add("Toggle Surgical Subtree Clipper (Alt+C — Click Theorem/Code to Harvest)", null, async delegate { await ToggleDomToolAsync("clip"); });
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add("Reset Saved DOM Zaps for Current Site", null, delegate
        {
            string h = GetActiveHost();
            int n = OmegaZapperAndClipper.ClearDomainZaps(_labRoot, h);
            _lblStatusL.Text = "Cleared " + n + " saved DOM zap rule(s) on " + h + " — reloading.";
            if (_active != null && _active.View != null) _active.View.Reload();
        });
        cms.Show(_btnZap, new Point(0, _btnZap.Height));
    }

    private async Task ToggleDomToolAsync(string mode)
    {
        if (_active == null || _active.View == null || _active.View.CoreWebView2 == null) return;
        await _active.View.CoreWebView2.ExecuteScriptAsync("if(window.__omegaSetToolMode) window.__omegaSetToolMode('" + mode + "');");
        _lblStatusL.Text = mode == "zap"
            ? "DOM Zapper armed (Alt+X): hover & click any clutter element to erase permanently on this site (Esc to exit)."
            : "Subtree Clipper armed (Alt+C): hover & click any theorem, table, or code block to save MD + SHA-256 to Corpus (Esc to exit).";
    }

    private async Task OpenPaperToCodeAsync(string selectedCode)
    {
        if (_active == null) return;
        string sel = selectedCode;
        if (string.IsNullOrEmpty(sel) && _active.View != null && _active.View.CoreWebView2 != null)
            sel = OmegaJson.UnwrapJsString(await _active.View.CoreWebView2.ExecuteScriptAsync("window.getSelection?window.getSelection().toString():''"));
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, "full");
        OmegaV04Studios.ShowPaperToCodeRunner(this, _labRoot, rec.Title, rec.FinalUrl, rec.MarkdownBody, sel, delegate(string msg) { _lblStatusL.Text = msg; });
    }

    private async Task RefreshCompanionCacheAsync(BrowserTab tab)
    {
        try
        {
            if (tab == null || tab.View == null || tab.View.CoreWebView2 == null) return;
            if (tab.Url.StartsWith("omega://", StringComparison.OrdinalIgnoreCase) || tab.Url.Contains("127.0.0.1:9778/companion")) return;
            ReceiptRecord r = await ExtractPageReceiptAsync(tab, "distilled");
            _lastCompanionTitle = r.Title;
            _lastCompanionUrl = r.FinalUrl;
            _lastCompanionSha = r.Sha256;
            _lastCompanionDistilled = string.IsNullOrWhiteSpace(r.MarkdownBody) ? "(No distilled blocks on page)" : r.MarkdownBody;
        }
        catch { }
    }

    private void ShowTtsMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        cms.Items.Add("Read Current Page / PDF Aloud (With Live Highlight)", null, async delegate { await StartReaderTtsAsync(); });
        cms.Items.Add(_ttsPaused ? "Resume Speech" : "Pause Speech", null, delegate
        {
            if (_tts == null) return;
            if (!_ttsPaused) { _tts.Pause(); _ttsPaused = true; _lblStatusL.Text = "TTS Paused."; }
            else { _tts.Resume(); _ttsPaused = false; _lblStatusL.Text = "TTS Resumed."; }
        });
        cms.Items.Add("Skip to Next Paragraph", null, delegate { if (_tts != null) { _tts.SpeakAsyncCancelAll(); SpeakNextParagraph(); } });
        cms.Items.Add("Stop Reading", null, delegate { StopTts(); });
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add("Speed: Normal (0)", null, delegate { if (_tts != null) _tts.Rate = 0; });
        cms.Items.Add("Speed: Brisk (+2)", null, delegate { if (_tts != null) _tts.Rate = 2; });
        cms.Items.Add("Speed: Fast Research (+4)", null, delegate { if (_tts != null) _tts.Rate = 4; });
        cms.Show(_btnTts, new Point(0, _btnTts.Height));
    }

    private async Task StartReaderTtsAsync()
    {
        if (_tts == null || _active == null) return;
        StopTts();
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, "full");
        string[] blocks = rec.MarkdownBody.Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        _ttsParagraphs.Clear();
        string bt = new string((char)96, 1);
        for (int i = 0; i < blocks.Length; i++)
        {
            string clean = blocks[i].Replace("#", "").Replace("*", "").Replace(bt, "").Trim();
            if (clean.Length > 8) _ttsParagraphs.Add(clean);
        }
        _ttsIndex = -1; _ttsPaused = false;
        SpeakNextParagraph();
    }

    private async void SpeakNextParagraph()
    {
        if (_tts == null || _ttsParagraphs.Count == 0) return;
        _ttsIndex++;
        if (_ttsIndex >= _ttsParagraphs.Count) { StopTts(); _lblStatusL.Text = "TTS finished reading page."; return; }
        string line = _ttsParagraphs[_ttsIndex];
        _lblStatusL.Text = "[TTS " + (_ttsIndex + 1) + "/" + _ttsParagraphs.Count + "] " + Shorten(line, 68);
        if (_active != null && _active.View != null && _active.View.CoreWebView2 != null)
        {
            string probe = OmegaJson.Esc(line.Length > 42 ? line.Substring(0, 42) : line);
            await _active.View.CoreWebView2.ExecuteScriptAsync("(function(){ var ps=document.querySelectorAll('p,li,h1,h2,h3,pre'); for(var i=0;i<ps.length;i++){ ps[i].style.outline=''; if((ps[i].innerText||'').indexOf(\"" + probe + "\")>=0){ ps[i].style.outline='2px solid #38bdf8'; ps[i].scrollIntoView({behavior:'smooth',block:'center'}); break; } } })();");
        }
        _tts.SpeakAsync(line);
    }

    private void StopTts()
    {
        _ttsPaused = false; _ttsIndex = -1; _ttsParagraphs.Clear();
        if (_tts != null) { try { _tts.SpeakAsyncCancelAll(); } catch { } }
    }

    private void ShowScreenshotMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        cms.Items.Add("Screenshot Plus :: Visible Area -> Annotate / Blur / Crop", null, async delegate { await OpenScreenshotStudioAsync(false); });
        cms.Items.Add("Screenshot Plus :: Full Scrollable Page (CDP) -> Studio", null, async delegate { await OpenScreenshotStudioAsync(true); });
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add("Desktop Full-Width Viewport", null, delegate { SetViewportPreset(""); });
        cms.Items.Add("Pixel 10 Viewport (1080x2424 Tensor G5)", null, delegate { SetViewportPreset("P10"); });
        cms.Items.Add("Pixel 8 Viewport (1080x2400 Tensor G3)", null, delegate { SetViewportPreset("P8"); });
        cms.Show(_btnShot, new Point(0, _btnShot.Height));
    }

    private async Task OpenScreenshotStudioAsync(bool fullPage)
    {
        if (_active == null || _active.View == null || _active.View.CoreWebView2 == null) return;
        byte[] png = null;
        if (fullPage)
        {
            try
            {
                string json = await _active.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":true,\"fromSurface\":true}");
                string b64 = OmegaJson.ExtractJsonField(json, "data");
                if (!string.IsNullOrEmpty(b64)) png = Convert.FromBase64String(b64);
            }
            catch { }
        }
        if (png == null || png.Length == 0) png = await CaptureTabPngAsync();
        if (png.Length == 0) return;
        using (MemoryStream ms = new MemoryStream(png))
        using (Bitmap bmp = new Bitmap(ms))
        {
            OmegaDialogs.ShowScreenshotStudio(this, bmp, _labRoot, _active.Title, _active.Url);
        }
    }

    private void OpenQrModal()
    {
        if (_active == null) return;
        OmegaDialogs.ShowQrDialog(this, _active.Url, delegate { _lblStatusL.Text = DualPixelMesh.BroadcastToAllPixels(_active.Url, false); });
    }

    private async Task OpenStickyAndHighlightAsync(string selectedText)
    {
        if (_active == null) return;
        string sel = selectedText;
        if (string.IsNullOrEmpty(sel) && _active.View != null && _active.View.CoreWebView2 != null)
            sel = OmegaJson.UnwrapJsString(await _active.View.CoreWebView2.ExecuteScriptAsync("window.getSelection?window.getSelection().toString():''"));
        OmegaDialogs.ShowStickyAndHighlightDialog(this, _labRoot, _active.Title, _active.Url, sel, async delegate
        {
            SyncActiveChrome();
            await ApplyDomainPrefsAndStateAsync(_active);
        });
    }

    private async Task OpenTimeMachineAsync()
    {
        if (_active == null) return;
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, "full");
        OmegaDialogs.ShowTimeMachineDialog(this, _labRoot, rec);
    }

    private async Task ToggleVimModeAsync()
    {
        _vimMode = !_vimMode;
        _btnVim.BackColor = _vimMode ? Color.FromArgb(202, 138, 4) : Color.FromArgb(30, 41, 59);
        if (_active != null) await ApplyDomainPrefsAndStateAsync(_active);
        _lblStatusL.Text = _vimMode ? "Vim Mode ON :: j/k scroll, d/u half-page, gg/G top/bottom, f link hints!" : "Vim Mode OFF.";
    }

    private void ToggleZenMode()
    {
        _zenMode = !_zenMode;
        _tabStrip.Visible = !_zenMode;
        _navBar.Visible = !_zenMode;
        _labBar.Visible = !_zenMode;
        _vTabPanel.Visible = !_zenMode && _verticalTabsOn;
        if (_zenMode)
        {
            _dlShelf.Visible = false; _shSidebar.Visible = false;
            if (_focusSecondsLeft <= 0) _focusSecondsLeft = 25 * 60;
            _lblStatusL.Text = "Focus / Zen Mode ON (25m Session + Research Distraction Guard) — Press Alt+Z or Esc to restore chrome.";
        }
        else _lblStatusL.Text = "Focus / Zen Mode OFF.";
    }

    private void OpenTabSearchModal()
    {
        List<PaletteItem> list = new List<PaletteItem>();
        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab t = _tabs[i]; BrowserTab cap = t;
            PaletteItem p = new PaletteItem();
            p.Title = (t.Frozen ? "[Zz] " : "") + t.Title;
            p.Hint = t.Workspace + " | " + t.Container + " | " + Shorten(t.Url, 38);
            p.Run = async delegate { await ActivateTabAsync(cap); };
            list.Add(p);
        }
        OmegaDialogs.ShowTabSearch(this, list, CloseDuplicateTabs, async delegate { await ReopenLastClosedTabAsync(); });
    }

    private void OpenVaultSearchModal()
    {
        OmegaDialogs.ShowHistoryVaultSearch(this, _labRoot, delegate(string u) { NavigateActive(u); });
    }

    private async Task<ReceiptRecord> ExtractPageReceiptAsync(BrowserTab tab, string mode)
    {
        await EnsureTabWebViewAsync(tab, null);
        CoreWebView2 core = tab.View.CoreWebView2;
        if ((string.IsNullOrEmpty(core.Source) || core.Source == "about:blank") && !string.IsNullOrEmpty(tab.Url) && !tab.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            TaskCompletionSource<bool> navTcs = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> eh = null;
            eh = delegate { navTcs.TrySetResult(true); };
            core.NavigationCompleted += eh;
            try
            {
                if (core.Source == "about:blank") core.Navigate(ResolveNavUrl(tab.Url, tab.Workspace));
                await Task.WhenAny(navTcs.Task, Task.Delay(5500));
                await Task.Delay(180);
            }
            finally { core.NavigationCompleted -= eh; }
        }
        string liveSrc = DisplayUrl(core.Source);
        string md = OmegaJson.UnwrapJsString(await core.ExecuteScriptAsync(OmegaInjectedJs.BuildExtractionScript(mode)));
        string html = OmegaJson.UnwrapJsString(await core.ExecuteScriptAsync("document.documentElement.outerHTML"));

        bool isPdfEmbed = html.IndexOf("application/pdf", StringComparison.OrdinalIgnoreCase) >= 0;
        if (NativePdfExtractor.LooksLikePdfUrl(liveSrc) || (isPdfEmbed && (md == null || md.Trim().Length < 80)))
        {
            string pdfSha = "";
            string pdfMd = await Task.Run(delegate { return NativePdfExtractor.ExtractPdfMarkdown(liveSrc, mode, out pdfSha); });
            if (!string.IsNullOrEmpty(pdfMd)) md = pdfMd;
        }

        string host = GetHostSafe(core.Source); int siteBlk = 0;
        lock (_siteStats) { SiteShieldStat st; if (!string.IsNullOrEmpty(host) && _siteStats.TryGetValue(host, out st)) siteBlk = st.Blocked; }
        string title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? Path.GetFileName(liveSrc) : core.DocumentTitle;
        ReceiptRecord rec = OmegaVault.MintReceiptAndSaveHtml(_labRoot, title, liveSrc, html, md, siteBlk, Interlocked.Read(ref _totalBlocked));
        int estTok = Math.Max(1, rec.MarkdownBody.Length / 4);
        _lblCtxMeter.Text = "Context: ~" + estTok + " / 8,192 tok | Loopback";
        return rec;
    }

    private async Task CopyMarkdownWithReceiptAsync()
    {
        if (_active == null) return;
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, _active.ReaderOn ? _active.ReaderMode : "full");
        Clipboard.SetDataObject(OmegaVault.FormatReceiptMarkdown(rec), true, 4, 40);
        _lblStatusL.Text = "Copied MD + SHA-256 Receipt (" + rec.Sha256.Substring(0, 12) + "...) -> Vault snapshot saved!";
    }

    private async Task SaveActiveToCorpusAsync(string tags)
    {
        if (_active == null) return;
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, "full");
        CorpusSaveResult res = OmegaVault.SaveToCorpus(_labRoot, rec, tags);
        _lblStatusL.Text = res.Duplicate ? ("[Corpus Dedupe] Already indexed " + res.Sha256.Substring(0, 12)) : ("[Saved to Corpus] " + res.Source + " -> " + (string.IsNullOrEmpty(res.ManifoldPath) ? res.PrimaryPath : res.ManifoldPath));
    }

    private void ShowReaderMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        cms.Items.Add("Reader 2.0 :: Full 768D (HTML + Zero-Download PDF + Math + Code)", null, async delegate { await ApplyReaderOverlayAsync("full"); });
        cms.Items.Add("Reader 2.0 :: Distilled 384D (Theorems + Equations + Signatures)", null, async delegate { await ApplyReaderOverlayAsync("distilled"); });
        cms.Items.Add("Exit Reader (Reload Original DOM / PDF)", null, delegate { if (_active != null && _active.View != null) { _active.ReaderOn = false; _active.View.Reload(); } });
        cms.Show(_btnReader, new Point(0, _btnReader.Height));
    }

    private async Task ApplyReaderOverlayAsync(string mode)
    {
        if (_active == null) return;
        _active.ReaderMode = mode; _active.ReaderOn = true;
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, mode);
        string esc = OmegaJson.Esc(rec.MarkdownBody), tEsc = OmegaJson.Esc(rec.Title + " [" + mode.ToUpperInvariant() + "]");
        string js = "(function(){ document.body.innerHTML='<div style=\"max-width:860px;margin:28px auto;padding:28px;background:#0b111e;color:#e2e8f0;border:1px solid #1e293b;border-radius:10px;font-family:Segoe UI,sans-serif\"><div style=\"color:#38bdf8;font-size:12px;font-weight:700;margin-bottom:6px\">OMEGA READER 2.0 (" + mode.ToUpperInvariant() + ") &bull; SHA256: " + rec.Sha256.Substring(0, 16) + "...</div><h1 style=\"font-size:23px;margin-bottom:16px;color:#fff\">" + tEsc + "</h1><pre style=\"white-space:pre-wrap;word-break:break-word;font-family:Consolas,monospace;font-size:13px;line-height:1.65;color:#cbd5e1\">" + esc + "</pre></div>'; document.body.style.background='#06090f'; window.scrollTo(0,0); })();";
        await _active.View.CoreWebView2.ExecuteScriptAsync(js);
    }

    private async Task RunAskShXiAsync()
    {
        if (_active == null) return;
        string ep = _shEndpoint.Text.Trim(), mode = _shMode.Text, q = _shPrompt.Text.Trim();
        _shOutput.Text = "Extracting Reader 2.0 / PDF Markdown + SHA-256 receipt...";
        ReceiptRecord rec = await ExtractPageReceiptAsync(_active, mode == "384D Distill" ? "distilled" : "full");
        string ans = await Task.Run(delegate { return LocalModelClient.QueryLocalShXi(ep, mode, q, rec); });
        _shOutput.Text = ans.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
}
