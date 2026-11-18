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
    public async Task PipeActiveTabAudioToOmegaSynthAsync()
    {
        if (_active == null) return;
        string title = _active.Title ?? "Web Audio Tab";
        string url = _active.Url ?? "";
        try
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                BrowserTab bt = _tabs[i];
                if (bt != _active && bt.View != null && bt.View.CoreWebView2 != null)
                {
                    try { await bt.View.CoreWebView2.ExecuteScriptAsync("(function(){var ms=document.querySelectorAll('video,audio');for(var k=0;k<ms.length;k++){ms[k].pause();}})();"); } catch { }
                }
            }
            if (_active.View != null && _active.View.CoreWebView2 != null)
            {
                await _active.View.CoreWebView2.ExecuteScriptAsync("(function(){var m=document.querySelector('video,audio');if(m){m.muted=false;if(m.volume<0.2)m.volume=0.85;if(m.paused)m.play().catch(function(){});}})();");
                if (!string.IsNullOrWhiteSpace(_active.View.CoreWebView2.DocumentTitle)) title = _active.View.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(_active.View.CoreWebView2.Source)) url = _active.View.CoreWebView2.Source;
            }
        }
        catch { }

        string msg = await Task.Run(delegate { return OmegaCrossAppBridge.PipeWebStreamToOmegaSynth(_labRoot, title, url); });
        _lblStatusL.Text = msg;
    }

    private async Task<string> SyncToGeminiBridgeAsync()
    {
        _lblStatusL.Text = "Exporting JSONL Corpus Shard + Syncing Suite to OMEGA_BRIDGE_EXPORT...";
        string summary = await Task.Run(delegate { return RunBridgeExportJob(_labRoot); });
        _lblStatusL.Text = summary;
        return summary;
    }

    private static string RunBridgeExportJob(string labRoot)
    {
        try
        {
            string suiteRoot = Directory.GetParent(labRoot).FullName;
            string corpusDir = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus"))) ? (Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")) : Path.Combine(labRoot, "corpus");
            Directory.CreateDirectory(corpusDir);
            string jsonlPath = Path.Combine(corpusDir, "omega_corpus_bundle_v06.jsonl");
            string[] mdFiles = Directory.GetFiles(corpusDir, "*.md");
            List<string> jsonLines = new List<string>();
            long totalChars = 0;
            for (int i = 0; i < mdFiles.Length; i++)
            {
                string raw = File.ReadAllText(mdFiles[i], Encoding.UTF8);
                totalChars += raw.Length;
                string fn = Path.GetFileName(mdFiles[i]);
                int estTok = Math.Max(1, raw.Length / 4);
                jsonLines.Add("{\"file\":\"" + OmegaJson.Esc(fn) + "\",\"est_tokens\":" + estTok + ",\"sha256\":\"" + OmegaVault.ComputeSha256Hex(raw) + "\",\"markdown\":\"" + OmegaJson.Esc(raw) + "\"}");
            }
            File.WriteAllLines(jsonlPath, jsonLines.ToArray(), new UTF8Encoding(false));

            string bridgeBase = (Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"));
            if (!Directory.Exists(bridgeBase)) bridgeBase = Path.Combine(suiteRoot, "bridge_staging");
            string destRoot = Path.Combine(bridgeBase, "35_OMEGA_LAB_v1_7_2_FULL_60FPS_GPU_GM");
            string destMir = Path.Combine(destRoot, "01_omega_mirror");
            string destBr = Path.Combine(destRoot, "02_omega_browser");
            string destHome = Path.Combine(destBr, "home");
            string destIco = Path.Combine(destRoot, "icons");
            Directory.CreateDirectory(destMir); Directory.CreateDirectory(destHome); Directory.CreateDirectory(destIco);

            CopyMatching(suiteRoot, destRoot, new string[] { "*.cs", "*.ps1", "*.exe" });
            CopyMatching(Path.Combine(suiteRoot, "icons"), destIco, new string[] { "*.ico" });
            CopyMatching(Path.Combine(suiteRoot, "01_omega_mirror"), destMir, new string[] { "*.cs", "*.html", "*.exe" });
            CopyMatching(labRoot, destBr, new string[] { "*.cs", "*.ps1", "*.txt", "*.exe" });
            CopyMatching(Path.Combine(labRoot, "home"), destHome, new string[] { "*.html" });
            File.Copy(jsonlPath, Path.Combine(destRoot, "omega_corpus_bundle_v06.jsonl"), true);

            return "Bridge Synced -> " + destRoot + " (" + mdFiles.Length + " corpus docs, ~" + (totalChars / 4) + " tok)";
        }
        catch (Exception ex) { return "Bridge Sync Error: " + ex.Message; }
    }

    private static void CopyMatching(string srcDir, string dstDir, string[] patterns)
    {
        if (!Directory.Exists(srcDir)) return;
        Directory.CreateDirectory(dstDir);
        for (int p = 0; p < patterns.Length; p++)
        {
            foreach (string f in Directory.GetFiles(srcDir, patterns[p]))
            {
                try { File.Copy(f, Path.Combine(dstDir, Path.GetFileName(f)), true); } catch { }
            }
        }
    }

    private void ShowFreezeMemoryMenu()
    {
        ContextMenuStrip cms = new ContextMenuStrip();
        cms.Items.Add("Freeze All Background Tabs", null, async delegate { int n = await FreezeBackgroundTabsAsync(); _lblStatusL.Text = "Hibernated " + n + " tab(s)."; });
        cms.Items.Add("Open omega://performance Live Dashboard", null, delegate { NavigateActive("omega://performance"); });
        cms.Items.Add(new ToolStripSeparator());
        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab t = _tabs[i]; BrowserTab cap = t;
            int idle = (int)Math.Max(0, (DateTime.UtcNow - t.LastActiveUtc).TotalMinutes);
            cms.Items.Add("[" + t.Workspace + "|" + t.Container + "] " + Shorten(t.Title, 22) + " | " + (t.Frozen ? "FROZEN" : (GetTabMemoryMb(t) + " MB")) + " | " + idle + "m", null, async delegate
            {
                if (cap == _active) return;
                if (!cap.Frozen) await FreezeSingleTabAsync(cap); else await ActivateTabAsync(cap);
            });
        }
        cms.Show(_btnFreeze, new Point(0, _btnFreeze.Height));
    }

    private async Task<int> FreezeBackgroundTabsAsync()
    {
        int c = 0;
        for (int i = 0; i < _tabs.Count; i++) if (_tabs[i] != _active && _tabs[i] != _splitRight && !_tabs[i].Frozen) { await FreezeSingleTabAsync(_tabs[i]); c++; }
        RefreshAllTabBars(); return c;
    }

    private async Task FreezeSingleTabAsync(BrowserTab t)
    {
        if (t == null || t == _active || t == _splitRight) return;
        t.Frozen = true;
        if (t.View != null && t.View.CoreWebView2 != null) { try { t.View.Visible = false; await t.View.CoreWebView2.TrySuspendAsync(); } catch { } }
        RefreshAllTabBars();
    }

    private static long GetTabMemoryMb(BrowserTab t)
    {
        try { if (t.View == null || t.View.CoreWebView2 == null) return 0; int pid = (int)t.View.CoreWebView2.BrowserProcessId; if (pid <= 0) return 0; using (Process p = Process.GetProcessById(pid)) return p.WorkingSet64 / 1048576L; }
        catch { return 0; }
    }

    private void SetViewportPreset(string preset)
    {
        _viewportPreset = (_viewportPreset == preset) ? "" : (preset ?? "");
        if (_active != null && _active.View != null && _active.View.CoreWebView2 != null)
        {
            ApplyViewportUa(_active.View.CoreWebView2);
            LayoutViewports();
            _active.View.Reload();
        }
        _lblStatusL.Text = string.IsNullOrEmpty(_viewportPreset)
            ? "Viewport reset to Full Desktop."
            : ("Viewport set to " + (_viewportPreset == "P8" ? "Pixel 8 (1080x2400)" : "Pixel 10 (1080x2424)") + ".");
    }

    private void ApplyViewportUa(CoreWebView2 core)
    {
        if (core == null) return;
        if (_viewportPreset == "P10") core.Settings.UserAgent = "Mozilla/5.0 (Linux; Android 16; Pixel 10) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Mobile Safari/537.36";
        else if (_viewportPreset == "P8") core.Settings.UserAgent = "Mozilla/5.0 (Linux; Android 16; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Mobile Safari/537.36";
        else if (!string.IsNullOrEmpty(_desktopUa)) core.Settings.UserAgent = _desktopUa;
    }

    private void LayoutViewports()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            WebView2 v = _tabs[i].View; if (v == null || v.Parent == null) continue;
            if (!string.IsNullOrEmpty(_viewportPreset) && _tabs[i] == _active)
            {
                int ph = _viewportPreset == "P8" ? 2400 : 2424;
                int h = Math.Max(320, v.Parent.ClientSize.Height - 16), w = Math.Max(220, (h * 1080) / ph);
                v.Dock = DockStyle.None; v.SetBounds(Math.Max(0, (v.Parent.ClientSize.Width - w) / 2), 8, w, h);
            }
            else v.Dock = DockStyle.Fill;
        }
    }

    private void OpenShieldModal()
    {
        Dictionary<string, SiteShieldStat> snap;
        lock (_siteStats) snap = new Dictionary<string, SiteShieldStat>(_siteStats, StringComparer.OrdinalIgnoreCase);
        OmegaDialogs.ShowShieldDashboard(this, GetActiveHost(), Interlocked.Read(ref _totalBlocked), Interlocked.Read(ref _cookiesRejected), Interlocked.Read(ref _autoplayBlocked), Interlocked.Read(ref _total1P), Interlocked.Read(ref _total3P), snap, _rulesPath, delegate
        {
            OmegaSecurity.LoadShieldRules(_rulesPath, out _blockFilters, out _allowedSites); UpdateTelemetryBar();
        });
    }

    private void OpenCommandPalette()
    {
        List<PaletteItem> items = new List<PaletteItem>();
        AddCmd(items, "Open Vault Download Studio (HTTP 206 Pause/Resume + OmegaAV/Pixel)", "Ctrl+J", OpenDownloadStudio);
        AddCmd(items, "Pipe Active Tab Audio (YouTube / Web) -> OmegaSynth v1.1.1 Final GM", "-> Synth (:19782)", async delegate { await PipeActiveTabAudioToOmegaSynthAsync(); });
        AddCmd(items, "Batch Sniff Page Links, Papers (.pdf) & Audio (.wav)", "Ctrl+Shift+D", async delegate { await OpenBatchLinkSnifferAsync(); });
        AddCmd(items, "Send Latest Downloaded .WAV / .OMSH -> OmegaAV v0.7.1", "1-Click Bridge", delegate { for (int i = 0; i < _downloads.Count; i++) if (_downloads[i].Done && File.Exists(_downloads[i].FilePath)) { _lblStatusL.Text = OmegaCrossAppBridge.SendFileToOmegaAv(_labRoot, _downloads[i].FilePath); break; } });
        AddCmd(items, "Broadcast Latest Downloaded File -> Connected Pixels", "ADB /sdcard/Download", async delegate { for (int i = 0; i < _downloads.Count; i++) if (_downloads[i].Done && File.Exists(_downloads[i].FilePath)) { string fp = _downloads[i].FilePath; _lblStatusL.Text = await Task.Run(delegate { return OmegaCrossAppBridge.BroadcastFileToAllPixels(fp); }); break; } });
        AddCmd(items, "Export JSONL Corpus Shard + Sync Suite -> Gemini Bridge", "Bridge Sync", async delegate { await SyncToGeminiBridgeAsync(); });
        AddCmd(items, "Dual-Pixel Tri-Silicon Mesh Hub (Pixel 8 + Pixel 10)", "USB/Wi-Fi :5555", OpenDualPixelHub);
        AddCmd(items, "Beam OLED Equation & Spec Deck -> All Pixels", ":9778/companion", async delegate { await BeamOledCompanionAsync(); });
        AddCmd(items, "Pull Foreground URL / Clipboard from Pixel -> PC Tab", "Reverse Bridge", async delegate { await QuickPullPixelUrlAsync(); });
        AddCmd(items, "Multi-Tab Concept Braid Synthesizer (2-6 Tabs)", "Ctrl+Shift+B", OpenConceptBraidModal);
        AddCmd(items, "Paper-to-Code Scratchpad Runner (C# / PS / Rust 4s)", "Ctrl+Shift+R", async delegate { await OpenPaperToCodeAsync(""); });
        AddCmd(items, "Surgical DOM Zapper Mode (Click Clutter to Erase)", "Alt+X", async delegate { await ToggleDomToolAsync("zap"); });
        AddCmd(items, "Surgical Subtree Clipper -> Corpus + Receipt", "Alt+C", async delegate { await ToggleDomToolAsync("clip"); });
        AddCmd(items, "Tab Search Across All Open Tabs", "Ctrl+Shift+A", OpenTabSearchModal);
        AddCmd(items, "Close All Duplicate Tabs", "1-Click Dedupe", CloseDuplicateTabs);
        AddCmd(items, "Reopen Last Closed Tab (With History)", "Ctrl+Shift+T", async delegate { await ReopenLastClosedTabAsync(); });
        AddCmd(items, "Toggle Split View (Side-by-Side Tabs)", "Ctrl+\\", async delegate { await ToggleSplitViewAsync(); });
        AddCmd(items, "Find in Page (Highlight All + Case)", "Ctrl+F", OpenFindBar);
        AddCmd(items, "Full-Text History & Markdown Vault Search", "Ctrl+H", OpenVaultSearchModal);
        AddCmd(items, "Copy Clean URL (Strip Tracking Params)", "Brave-Style", delegate { if (_active != null) { string c = OmegaUrlCleaner.CleanUrl(_active.Url); Clipboard.SetDataObject(c, true, 4, 40); _lblStatusL.Text = "Copied clean URL: " + c; } });
        AddCmd(items, "Screenshot Plus: Full Scrollable Page -> Studio", "Annotate/Blur", async delegate { await OpenScreenshotStudioAsync(true); });
        AddCmd(items, "QR Code for Current Page", "Pixel Scan", OpenQrModal);
        AddCmd(items, "Sticky Note & Paper/PDF Vault Highlights", "Per-URL Note", async delegate { await OpenStickyAndHighlightAsync(""); });
        AddCmd(items, "Time Machine: Archive Snapshot & Version Diff", "Provenance", async delegate { await OpenTimeMachineAsync(); });
        AddCmd(items, "Reader TTS: Read Page or PDF Aloud", "Offline Voice", async delegate { await StartReaderTtsAsync(); });
        AddCmd(items, "Toggle Vim Mode (j/k/gg/G/f Link Hints)", "Alt+V", async delegate { await ToggleVimModeAsync(); });
        AddCmd(items, "Toggle Focus / Zen Mode (25m + Guard)", "Alt+Z", ToggleZenMode);
        AddCmd(items, "Open omega://performance Live Dashboard", "RAM/CPU/Corpus", delegate { NavigateActive("omega://performance"); });
        AddCmd(items, "Open omega://companion OLED Second-Screen Preview", "384D Deck", delegate { NavigateActive("omega://companion"); });
        OmegaDialogs.ShowCommandPalette(this, items, delegate(string bang) { NavigateActive(bang); }, delegate(int secs) { _focusSecondsLeft = secs; });
    }

    private static void AddCmd(List<PaletteItem> list, string t, string h, Action run)
    {
        PaletteItem p = new PaletteItem(); p.Title = t; p.Hint = h; p.Run = run; list.Add(p);
    }

    private async Task<byte[]> CaptureTabPngAsync()
    {
        if (_active == null || _active.View == null || _active.View.CoreWebView2 == null) return new byte[0];
        using (MemoryStream ms = new MemoryStream())
        {
            await _active.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            byte[] b = ms.ToArray();
            File.WriteAllBytes(Path.Combine(_labRoot, "vault", "screenshots", "shot_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".png"), b);
            return b;
        }
    }

    public Task<ApiResult> HandleAgentRequestAsync(string method, string path, Dictionary<string, string> query, string body)
    {
        TaskCompletionSource<ApiResult> tcs = new TaskCompletionSource<ApiResult>();
        BeginInvoke((Action)(async delegate
        {
            try { tcs.TrySetResult(await HandleAgentOnUiAsync(path, query, body)); }
            catch (Exception ex) { tcs.TrySetResult(ApiResult.Json(500, "{\"ok\":false,\"error\":\"" + OmegaJson.Esc(ex.Message) + "\"}")); }
        }));
        return tcs.Task;
    }

    private async Task<ApiResult> HandleAgentOnUiAsync(string path, Dictionary<string, string> query, string body)
    {
        string p = (path ?? "/").ToLowerInvariant();
        if (p == "/companion") return CompanionDeckRenderer.RenderHtmlPage();
        if (p == "/api/companion/state")
        {
            return ApiResult.Json(200, "{\"ok\":true,\"workspace\":\"" + OmegaJson.Esc(_workspace) + "\",\"title\":\"" + OmegaJson.Esc(_lastCompanionTitle) + "\",\"url\":\"" + OmegaJson.Esc(_lastCompanionUrl) + "\",\"sha256\":\"" + OmegaJson.Esc(_lastCompanionSha) + "\",\"distilled\":\"" + OmegaJson.Esc(_lastCompanionDistilled) + "\"}");
        }
        if (p == "/api/pixels")
        {
            List<PixelDeviceInfo> devs = await Task.Run(delegate { return DualPixelMesh.DiscoverPixels(); });
            StringBuilder pb = new StringBuilder("[");
            for (int i = 0; i < devs.Count; i++)
            {
                if (i > 0) pb.Append(",");
                pb.Append("{\"serial\":\"" + OmegaJson.Esc(devs[i].Serial) + "\",\"model\":\"" + OmegaJson.Esc(devs[i].Model) + "\",\"wireless\":" + (devs[i].IsWireless ? "true" : "false") + ",\"resolution\":\"" + OmegaJson.Esc(devs[i].Resolution) + "\",\"matched_pc_ip\":\"" + OmegaJson.Esc(devs[i].MatchedPcIp) + "\"}");
            }
            pb.Append("]");
            return ApiResult.Json(200, "{\"ok\":true,\"count\":" + devs.Count + ",\"devices\":" + pb.ToString() + "}");
        }
        if (p == "/api/status")
        {
            long ram = Process.GetCurrentProcess().WorkingSet64 / 1048576L;
            return ApiResult.Json(200, "{\"ok\":true,\"version\":\"0.4\",\"workspace\":\"" + OmegaJson.Esc(_workspace) + "\",\"tabs\":" + _tabs.Count + ",\"shield_blocked\":" + Interlocked.Read(ref _totalBlocked) + ",\"host_ram_mb\":" + ram + ",\"agent_port\":9778}");
        }
        if (p == "/api/performance")
        {
            long hostMb = Process.GetCurrentProcess().WorkingSet64 / 1048576L;
            HashSet<int> pids = new HashSet<int>(); long wvMb = 0; int frz = 0;
            StringBuilder tb = new StringBuilder("[");
            for (int i = 0; i < _tabs.Count; i++)
            {
                BrowserTab t = _tabs[i]; if (t.Frozen) frz++;
                long tmb = GetTabMemoryMb(t);
                if (t.View != null && t.View.CoreWebView2 != null)
                {
                    int pid = (int)t.View.CoreWebView2.BrowserProcessId;
                    if (pid > 0 && pids.Add(pid)) wvMb += tmb;
                }
                if (i > 0) tb.Append(",");
                tb.Append("{\"id\":\"" + t.Id + "\",\"workspace\":\"" + OmegaJson.Esc(t.Workspace) + "\",\"container\":\"" + OmegaJson.Esc(t.Container) + "\",\"title\":\"" + OmegaJson.Esc(t.Title) + "\",\"frozen\":" + (t.Frozen ? "true" : "false") + ",\"active\":" + (t == _active ? "true" : "false") + ",\"ram_mb\":" + tmb + ",\"load_ms\":" + t.LastLoadMs + "}");
            }
            tb.Append("]");
            int cFiles = 0; long cBytes = 0;
            string cDir = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus"))) ? (Environment.GetEnvironmentVariable("OMEGA_CORPUS_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "corpus")) : Path.Combine(_labRoot, "corpus");
            if (Directory.Exists(cDir)) { foreach (string fn in Directory.GetFiles(cDir, "*.md")) { cFiles++; cBytes += new FileInfo(fn).Length; } }
            int tmCount = Directory.Exists(Path.Combine(_labRoot, "vault", "timemachine")) ? Directory.GetFiles(Path.Combine(_labRoot, "vault", "timemachine"), "*.md", SearchOption.AllDirectories).Length : 0;
            return ApiResult.Json(200, "{\"ok\":true,\"host_ram_mb\":" + hostMb + ",\"webview_ram_mb\":" + wvMb + ",\"cpu_pct\":" + _cpuPct.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ",\"tabs_total\":" + _tabs.Count + ",\"tabs_frozen\":" + frz + ",\"shield_blocked\":" + Interlocked.Read(ref _totalBlocked) + ",\"cookies_rejected\":" + Interlocked.Read(ref _cookiesRejected) + ",\"autoplay_blocked\":" + Interlocked.Read(ref _autoplayBlocked) + ",\"corpus_files\":" + cFiles + ",\"corpus_mb\":" + Math.Round(cBytes / 1048576.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"timemachine_snaps\":" + tmCount + ",\"shxi_status\":\"ONLINE (:9778)\",\"tabs\":" + tb.ToString() + "}");
        }
        if (p == "/api/media")
        {
            string act = query.ContainsKey("action") ? query["action"] : OmegaJson.ExtractJsonField(body, "action");
            string targetUrl = query.ContainsKey("url") ? query["url"] : OmegaJson.ExtractJsonField(body, "url");
            if (act == "open" && !string.IsNullOrEmpty(targetUrl))
            {
                await CreateTabAsync(_workspace, "Google", targetUrl, true, false);
                return ApiResult.Json(200, "{\"ok\":true,\"action\":\"open\",\"title\":\"" + OmegaJson.Esc(_active != null ? _active.Title : targetUrl) + "\",\"url\":\"" + OmegaJson.Esc(targetUrl) + "\"}");
            }
            if (_active != null && _active.View != null && _active.View.CoreWebView2 != null)
            {
                string js = "(function(){var m=document.querySelector('video,audio');if(!m)return 'none';m.muted=false;if('" + OmegaJson.Esc(act) + "'==='pause')m.pause();else if('" + OmegaJson.Esc(act) + "'==='play')m.play();else{if(m.paused)m.play();else m.pause();}return m.paused?'paused':'playing';})();";
                try { await _active.View.CoreWebView2.ExecuteScriptAsync(js); } catch { }
            }
            return ApiResult.Json(200, "{\"ok\":true,\"title\":\"" + OmegaJson.Esc(_active != null ? _active.Title : "") + "\",\"url\":\"" + OmegaJson.Esc(_active != null ? _active.Url : "") + "\"}");
        }
        if (p == "/api/page") return ApiResult.Json(200, "{\"ok\":true,\"workspace\":\"" + OmegaJson.Esc(_workspace) + "\",\"title\":\"" + OmegaJson.Esc(_active != null ? _active.Title : "") + "\",\"url\":\"" + OmegaJson.Esc(_active != null ? _active.Url : "") + "\"}");
        if (p == "/api/tabs")
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < _tabs.Count; i++) { BrowserTab t = _tabs[i]; if (i > 0) sb.Append(","); sb.Append("{\"id\":\"" + t.Id + "\",\"workspace\":\"" + OmegaJson.Esc(t.Workspace) + "\",\"container\":\"" + OmegaJson.Esc(t.Container) + "\",\"title\":\"" + OmegaJson.Esc(t.Title) + "\",\"url\":\"" + OmegaJson.Esc(t.Url) + "\",\"frozen\":" + (t.Frozen ? "true" : "false") + ",\"ram_mb\":" + GetTabMemoryMb(t) + ",\"active\":" + (t == _active ? "true" : "false") + "}"); }
            sb.Append("]"); return ApiResult.Json(200, "{\"ok\":true,\"tabs\":" + sb.ToString() + "}");
        }
        if (p == "/api/navigate")
        {
            string url = query.ContainsKey("url") ? query["url"] : OmegaJson.ExtractJsonField(body, "url");
            if (!OmegaSecurity.IsAgentUrlAllowed(url, _allowPath)) return ApiResult.Json(403, "{\"ok\":false,\"error\":\"Not in agent_allowlist.txt\"}");
            if (query.ContainsKey("new") && query["new"] == "1") await CreateTabAsync(_workspace, _activeContainer, url, true, false);
            else NavigateActive(url);
            return ApiResult.Json(200, "{\"ok\":true,\"navigating\":\"" + OmegaJson.Esc(url) + "\"}");
        }
        if (p == "/api/markdown")
        {
            if (_active == null) return ApiResult.Json(400, "{\"ok\":false}");
            string mode = query.ContainsKey("mode") ? query["mode"] : "full";
            ReceiptRecord r = await ExtractPageReceiptAsync(_active, mode);
            return ApiResult.Json(200, "{\"ok\":true,\"mode\":\"" + OmegaJson.Esc(mode) + "\",\"title\":\"" + OmegaJson.Esc(r.Title) + "\",\"final_url\":\"" + OmegaJson.Esc(r.FinalUrl) + "\",\"sha256\":\"" + r.Sha256 + "\",\"fetched_utc\":\"" + r.FetchedUtc + "\",\"site_blocked\":" + r.SiteBlocked + ",\"total_blocked\":" + r.TotalBlocked + ",\"vault_path\":\"" + OmegaJson.Esc(r.VaultPath) + "\",\"markdown\":\"" + OmegaJson.Esc(r.MarkdownBody) + "\"}");
        }
        if (p == "/api/screenshot") return ApiResult.Png(await CaptureTabPngAsync());
        if (p == "/api/freeze") { int n = await FreezeBackgroundTabsAsync(); return ApiResult.Json(200, "{\"ok\":true,\"frozen_count\":" + n + "}"); }
        if (p == "/api/bridge/sync") { string msg = await SyncToGeminiBridgeAsync(); return ApiResult.Json(200, "{\"ok\":true,\"summary\":\"" + OmegaJson.Esc(msg) + "\"}"); }
        if (p == "/api/downloads")
        {
            StringBuilder db = new StringBuilder("[");
            for (int i = 0; i < _downloads.Count; i++)
            {
                DownloadTrackItem d = _downloads[i];
                if (i > 0) db.Append(",");
                db.Append("{\"id\":\"" + OmegaJson.Esc(d.Id) + "\",\"file\":\"" + OmegaJson.Esc(d.FileName) + "\",\"category\":\"" + OmegaJson.Esc(d.Category) + "\",\"engine\":\"" + OmegaJson.Esc(d.EngineKind) + "\",\"state\":\"" + OmegaJson.Esc(d.StateText) + "\",\"bytes_received\":" + d.BytesReceived + ",\"total_bytes\":" + d.TotalBytes + ",\"speed_bps\":" + ((long)d.SpeedBps) + ",\"sha256\":\"" + OmegaJson.Esc(d.Sha256) + "\",\"path\":\"" + OmegaJson.Esc(d.FilePath) + "\"}");
            }
            db.Append("]");
            return ApiResult.Json(200, "{\"ok\":true,\"vault_root\":\"" + OmegaJson.Esc(OmegaDownloadRouter.ResolveDownloadsRoot(_labRoot)) + "\",\"count\":" + _downloads.Count + ",\"items\":" + db.ToString() + "}");
        }
        if (p == "/api/downloads/enqueue")
        {
            string dlUrl = query.ContainsKey("url") ? query["url"] : OmegaJson.ExtractJsonField(body, "url");
            bool distill = (query.ContainsKey("distill") && query["distill"] == "1");
            DownloadTrackItem enq = EnqueueHttpRangeDownload(dlUrl, "", distill, false);
            if (enq == null) return ApiResult.Json(400, "{\"ok\":false,\"error\":\"Missing url\"}");
            return ApiResult.Json(200, "{\"ok\":true,\"id\":\"" + OmegaJson.Esc(enq.Id) + "\",\"dest\":\"" + OmegaJson.Esc(enq.FilePath) + "\"}");
        }
        if (p == "/api/corpus/save")
        {
            if (_active == null) return ApiResult.Json(400, "{\"ok\":false}");
            string tags = query.ContainsKey("tags") ? query["tags"] : OmegaJson.ExtractJsonField(body, "tags");
            ReceiptRecord r = await ExtractPageReceiptAsync(_active, "full");
            CorpusSaveResult cs = OmegaVault.SaveToCorpus(_labRoot, r, tags);
            return ApiResult.Json(200, "{\"ok\":true,\"saved\":" + (cs.Saved ? "true" : "false") + ",\"duplicate\":" + (cs.Duplicate ? "true" : "false") + ",\"sha256\":\"" + cs.Sha256 + "\",\"path\":\"" + OmegaJson.Esc(string.IsNullOrEmpty(cs.ManifoldPath) ? cs.PrimaryPath : cs.ManifoldPath) + "\"}");
        }
        return ApiResult.Json(404, "{\"ok\":false}");
    }

    private async Task RestoreOrSeedSessionAsync()
    {
        BrowserTab toActivate = null;
        string srcPath = File.Exists(_sessionPath) ? _sessionPath : Path.Combine(_labRoot, "session_v02.tsv");
        if (File.Exists(srcPath))
        {
            foreach (string ln in File.ReadAllLines(srcPath))
            {
                string[] p = ln.Split('\t'); if (p.Length < 4) continue;
                BrowserTab t = new BrowserTab();
                if (p.Length >= 6) { t.Workspace = p[0]; t.Container = p[1]; t.Url = p[3]; t.Title = p[4]; foreach (string h in p[5].Split('|')) if (!string.IsNullOrEmpty(h)) t.History.Add(h); if (p[2] == "1") { toActivate = t; _workspace = t.Workspace; } }
                else { t.Workspace = p[0]; t.Url = p[2]; t.Title = p[3]; t.History.Add(t.Url); if (p[1] == "1") { toActivate = t; _workspace = t.Workspace; } }
                t.Frozen = true; _tabs.Add(t);
            }
        }
        if (_tabs.Count == 0)
        {
            toActivate = await CreateTabAsync("Research", "arXiv-Lab", "omega://home", true, false);
            await CreateTabAsync("Training", "arXiv-Lab", "https://ocw.mit.edu/", false, true);
            await CreateTabAsync("Dev", "GitHub-1", "omega://home", false, true);
        }
        else { if (toActivate == null) toActivate = _tabs[0]; await ActivateTabAsync(toActivate); }
    }

    private void SaveSessionState()
    {
        try
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < _tabs.Count; i++)
            {
                BrowserTab t = _tabs[i];
                string hist = string.Join("|", t.History.ToArray()).Replace("\t", " ");
                lines.Add(t.Workspace + "\t" + t.Container + "\t" + (t == _active ? "1" : "0") + "\t" + t.Url.Replace("\t", " ") + "\t" + (t.Title ?? "Tab").Replace("\t", " ") + "\t" + hist);
            }
            File.WriteAllLines(_sessionPath, lines.ToArray(), new UTF8Encoding(false));
        }
        catch { }
    }

    private async Task HousekeepingTickAsync()
    {
        DateTime now = DateTime.UtcNow;
        double dt = (now - _lastCpuCheck).TotalSeconds;
        if (dt >= 1.8)
        {
            TimeSpan curCpu = Process.GetCurrentProcess().TotalProcessorTime;
            _cpuPct = Math.Min(100.0, Math.Round(((curCpu - _lastCpuTime).TotalSeconds / (dt * Environment.ProcessorCount)) * 100.0, 1));
            _lastCpuTime = curCpu; _lastCpuCheck = now;
        }
        if (_focusSecondsLeft > 0) _focusSecondsLeft--;
        _pixelPollTick++;
        if (_pixelPollTick >= 12) { _pixelPollTick = 0; await RefreshPixelBadgeAsync(); }
        TickDownloadsHousekeeping();
        for (int i = 0; i < _tabs.Count; i++)
            if (_tabs[i] != _active && _tabs[i] != _splitRight && !_tabs[i].Frozen && (now - _tabs[i].LastActiveUtc).TotalMinutes >= 10.0)
                await FreezeSingleTabAsync(_tabs[i]);
        UpdateTelemetryBar();
    }

    private void UpdateTelemetryBar()
    {
        bool shOn = true; _wsShield.TryGetValue(_workspace, out shOn);
        long blk = Interlocked.Read(ref _totalBlocked), ck = Interlocked.Read(ref _cookiesRejected);
        _btnShield.Text = "Shield:" + (shOn ? "ON" : "OFF") + " (" + (blk + ck) + ")";
        _btnShield.BackColor = shOn ? Color.FromArgb(6, 95, 70) : Color.FromArgb(127, 29, 29);
        long ram = Process.GetCurrentProcess().WorkingSet64 / 1048576L;
        string timerTxt = _focusSecondsLeft > 0 ? (" | Timer " + (_focusSecondsLeft / 60).ToString("00") + ":" + (_focusSecondsLeft % 60).ToString("00")) : "";
        int activeDl = 0; double sumBps = 0.0;
        for (int i = 0; i < _downloads.Count; i++) if (!_downloads[i].Done && !_downloads[i].Paused && !_downloads[i].Canceled) { activeDl++; sumBps += _downloads[i].SpeedBps; }
        string dlBadge = activeDl > 0 ? (" | DL:" + activeDl + "@" + OmegaDownloadRouter.FormatBytes((long)sumBps) + "/s") : "";
        _btnDlStudio.Text = activeDl > 0 ? ("DL:" + activeDl + " (" + OmegaDownloadRouter.FormatBytes((long)sumBps) + "/s)") : "DL (Ctrl+J)";
        _lblStatusR.Text = "v0.6.4 | omega://performance | WS:" + _workspace + " | Tabs:" + _tabs.Count + dlBadge + " | Blk:" + (blk + ck) + " | Shell:" + ram + " MB" + timerTxt;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.J))
        {
            BeginInvoke((Action)OpenDownloadStudio);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D))
        {
            BeginInvoke((Action)(async delegate { await OpenBatchLinkSnifferAsync(); }));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnAnyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && !e.Shift && e.KeyCode == Keys.J) { e.Handled = true; e.SuppressKeyPress = true; BeginInvoke((Action)OpenDownloadStudio); }
        else if (e.Control && e.Shift && e.KeyCode == Keys.D) { e.Handled = true; e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await OpenBatchLinkSnifferAsync(); })); }
        else if (e.Control && e.Shift && e.KeyCode == Keys.A) { e.SuppressKeyPress = true; BeginInvoke((Action)OpenTabSearchModal); }
        else if (e.Control && e.Shift && e.KeyCode == Keys.B) { e.SuppressKeyPress = true; BeginInvoke((Action)OpenConceptBraidModal); }
        else if (e.Control && e.Shift && e.KeyCode == Keys.R) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await OpenPaperToCodeAsync(""); })); }
        else if (e.Control && e.Shift && e.KeyCode == Keys.T) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await ReopenLastClosedTabAsync(); })); }
        else if (e.Control && e.KeyCode == Keys.K) { e.SuppressKeyPress = true; BeginInvoke((Action)OpenCommandPalette); }
        else if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; BeginInvoke((Action)OpenFindBar); }
        else if (e.Control && e.KeyCode == Keys.H) { e.SuppressKeyPress = true; BeginInvoke((Action)OpenVaultSearchModal); }
        else if (e.Control && e.KeyCode == Keys.OemPipe) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await ToggleSplitViewAsync(); })); }
        else if (e.Control && e.KeyCode == Keys.T) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await CreateTabAsync(_workspace, _activeContainer, "omega://home", true, false); })); }
        else if (e.Control && e.KeyCode == Keys.W) { e.SuppressKeyPress = true; BeginInvoke((Action)(delegate { CloseTab(_active); })); }
        else if (e.Control && e.KeyCode == Keys.L) { e.SuppressKeyPress = true; BeginInvoke((Action)(delegate { _omnibox.Focus(); _omnibox.SelectAll(); })); }
        else if (e.Control && e.KeyCode == Keys.Oemplus) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { double z = (_active != null && _active.View != null) ? _active.View.ZoomFactor + 0.1 : 1.1; await SetActiveDomainZoomAsync(z, null); })); }
        else if (e.Control && e.KeyCode == Keys.OemMinus) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { double z = (_active != null && _active.View != null) ? _active.View.ZoomFactor - 0.1 : 0.9; await SetActiveDomainZoomAsync(z, null); })); }
        else if (e.Control && e.KeyCode == Keys.D0) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await SetActiveDomainZoomAsync(1.0, null); })); }
        else if (e.Alt && e.KeyCode == Keys.X) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await ToggleDomToolAsync("zap"); })); }
        else if (e.Alt && e.KeyCode == Keys.C) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await ToggleDomToolAsync("clip"); })); }
        else if (e.Alt && e.KeyCode == Keys.V) { e.SuppressKeyPress = true; BeginInvoke((Action)(async delegate { await ToggleVimModeAsync(); })); }
        else if ((e.Alt && e.KeyCode == Keys.Z) || (e.KeyCode == Keys.Escape && _zenMode)) { e.SuppressKeyPress = true; BeginInvoke((Action)ToggleZenMode); }
    }

    private void OnBrowserClosing(object sender, FormClosingEventArgs e)
    {
        for (int i = 0; i < _downloads.Count; i++)
        {
            if (_downloads[i].RangeJob != null && _downloads[i].RangeJob.IsRunning)
                _downloads[i].RangeJob.Pause();
        }
        SaveDownloadsLedger();
        SaveSessionState(); StopTts();
        if (_houseTmr != null) _houseTmr.Stop();
        if (_api != null) _api.Stop();
    }

    private string GetActiveHost()
    {
        if (_active != null && _active.View != null && _active.View.CoreWebView2 != null) return GetHostSafe(_active.View.CoreWebView2.Source);
        return _active != null ? GetHostSafe(_active.Url) : "";
    }

    private static string DisplayUrl(string src)
    {
        if (string.IsNullOrEmpty(src)) return "omega://home";
        if (src.StartsWith("https://omega.home/performance", StringComparison.OrdinalIgnoreCase)) return "omega://performance";
        if (src.StartsWith("https://omega.home", StringComparison.OrdinalIgnoreCase)) return "omega://home";
        if (src.StartsWith("http://127.0.0.1:9778/companion", StringComparison.OrdinalIgnoreCase)) return "omega://companion";
        return src;
    }

    private static string GetHostSafe(string u)
    {
        Uri parsed;
        if (!string.IsNullOrEmpty(u) && Uri.TryCreate(u, UriKind.Absolute, out parsed)) return (parsed.Host ?? "").ToLowerInvariant();
        return "";
    }

    private static string Shorten(string s, int max)
    {
        string t = string.IsNullOrWhiteSpace(s) ? "Tab" : s.Trim();
        return t.Length <= max ? t : t.Substring(0, max - 2) + "..";
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args != null && args.Length > 0 && string.Equals(args[0], "--stress-test", StringComparison.OrdinalIgnoreCase))
        {
            OmegaNativeWorkbench.Track01StressSummary sr = OmegaNativeWorkbench.OmegaWorkbenchStressSuite.RunAll8Vectors();
            Console.WriteLine("[OMEGABROWSER+WORKBENCH 8-VECTOR AUDIT] " + sr.PassedCount + "/" + sr.TotalCount + " PASS (" + sr.TotalTrials + " trials | RAM=" + sr.TrimmedWorkingSetMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB)");
            for (int vi = 0; vi < sr.Vectors.Count; vi++) { if (!sr.Vectors[vi].Passed) Console.WriteLine("  [FAIL] " + sr.Vectors[vi].Code + " -> " + sr.Vectors[vi].Detail); }
            Environment.Exit(sr.AllPassed ? 0 : 1);
            return;
        }
        OmegaPortableConfig.OmegaHardwareConfig.LoadAndApplyEnvironment(false);
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        Application.Run(new BrowserForm(root));
    }
}
}
