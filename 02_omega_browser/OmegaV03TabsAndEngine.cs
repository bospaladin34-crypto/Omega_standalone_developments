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
    private async Task ConfigureCoreAsync(CoreWebView2 core, CoreWebView2Environment env)
    {
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping("omega.home", _homeDir, CoreWebView2HostResourceAccessKind.Allow);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(OmegaInjectedJs.BuildDocumentGuardScript());
        await core.AddScriptToExecuteOnDocumentCreatedAsync(OmegaZapperAndClipper.BuildInteractiveDomToolsScript());
        for (int i = 0; i < _blockFilters.Count; i++)
        {
            try { core.AddWebResourceRequestedFilter(_blockFilters[i], CoreWebView2WebResourceContext.All); } catch { }
        }
        core.WebResourceRequested += delegate(object s, CoreWebView2WebResourceRequestedEventArgs e)
        {
            bool wsOn = true; _wsShield.TryGetValue(_workspace, out wsOn);
            if (!wsOn) return;
            CoreWebView2 cw = s as CoreWebView2;
            string pHost = GetHostSafe(cw != null ? cw.Source : "");
            if (OmegaSecurity.IsSiteAllowed(_allowedSites, pHost)) return;
            Interlocked.Increment(ref _totalBlocked);
            if (!string.IsNullOrEmpty(pHost))
            {
                lock (_siteStats)
                {
                    SiteShieldStat st;
                    if (!_siteStats.TryGetValue(pHost, out st)) { st = new SiteShieldStat(); st.Host = pHost; _siteStats[pHost] = st; }
                    st.Blocked++;
                }
            }
            e.Response = env.CreateWebResourceResponse(Stream.Null, 204, "Blocked", "Access-Control-Allow-Origin: *");
        };
        core.WebResourceResponseReceived += delegate(object s, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                CoreWebView2 cw = s as CoreWebView2;
                string pHost = GetHostSafe(cw != null ? cw.Source : ""), rHost = GetHostSafe(e.Request.Uri);
                if (string.IsNullOrEmpty(pHost) || string.IsNullOrEmpty(rHost)) return;
                bool is1P = rHost == pHost || rHost.EndsWith("." + pHost) || pHost.EndsWith("." + rHost);
                if (is1P) Interlocked.Increment(ref _total1P); else Interlocked.Increment(ref _total3P);
                lock (_siteStats)
                {
                    SiteShieldStat st;
                    if (!_siteStats.TryGetValue(pHost, out st)) { st = new SiteShieldStat(); st.Host = pHost; _siteStats[pHost] = st; }
                    if (is1P) st.FirstParty++; else st.ThirdParty++;
                }
            }
            catch { }
        };
        core.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications || e.PermissionKind == CoreWebView2PermissionKind.Geolocation)
            {
                e.State = CoreWebView2PermissionState.Deny;
                Interlocked.Increment(ref _cookiesRejected);
            }
        };
        core.ContextMenuRequested += delegate(object s, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                CoreWebView2ContextMenuItem pipeItem = env.CreateContextMenuItem("Pipe Tab Audio -> OmegaSynth v1.1.1 Final GM (:19782)", null, CoreWebView2ContextMenuItemKind.Command);
                pipeItem.CustomItemSelected += delegate
                {
                    BeginInvoke((Action)(async delegate { await PipeActiveTabAudioToOmegaSynthAsync(); }));
                };
                e.MenuItems.Insert(0, pipeItem);

                if (e.ContextMenuTarget.HasLinkUri)
                {
                    string link = e.ContextMenuTarget.LinkUri;
                    CoreWebView2ContextMenuItem item = env.CreateContextMenuItem("Copy Clean Link (Strip Trackers)", null, CoreWebView2ContextMenuItemKind.Command);
                    item.CustomItemSelected += delegate
                    {
                        string clean = OmegaUrlCleaner.CleanUrl(link);
                        BeginInvoke((Action)delegate { Clipboard.SetDataObject(clean, true, 4, 40); _lblStatusL.Text = "Copied clean link: " + clean; });
                    };
                    e.MenuItems.Insert(0, item);
                    CoreWebView2ContextMenuItem dlItem = env.CreateContextMenuItem("Download Link via HTTP 206 Vault Studio", null, CoreWebView2ContextMenuItemKind.Command);
                    dlItem.CustomItemSelected += delegate
                    {
                        string cleanDl = OmegaUrlCleaner.CleanUrl(link);
                        BeginInvoke((Action)delegate { EnqueueHttpRangeDownload(cleanDl, "", cleanDl.ToLowerInvariant().Contains(".pdf") || cleanDl.ToLowerInvariant().Contains("arxiv.org/pdf/"), false); OpenDownloadStudio(); });
                    };
                    e.MenuItems.Insert(0, dlItem);
                }
                if (e.ContextMenuTarget.HasSelection)
                {
                    string sel = e.ContextMenuTarget.SelectionText;
                    CoreWebView2ContextMenuItem runItem = env.CreateContextMenuItem("Send Selection to Paper-to-Code Runner (4s Sandbox)", null, CoreWebView2ContextMenuItemKind.Command);
                    runItem.CustomItemSelected += delegate
                    {
                        BeginInvoke((Action)(async delegate { await OpenPaperToCodeAsync(sel); }));
                    };
                    e.MenuItems.Insert(0, runItem);

                    CoreWebView2ContextMenuItem hi = env.CreateContextMenuItem("Highlight & Save Selection to Sovereign Vault", null, CoreWebView2ContextMenuItemKind.Command);
                    hi.CustomItemSelected += delegate
                    {
                        BeginInvoke((Action)(async delegate { await OpenStickyAndHighlightAsync(sel); }));
                    };
                    e.MenuItems.Insert(0, hi);
                }
            }
            catch { }
        };
        core.DownloadStarting += OnDownloadStarting;
        SuppressDefaultWebViewDownloadPopup(core);
    }

    private async Task EnsureTabWebViewAsync(BrowserTab tab, Control targetPanel)
    {
        if (tab.View != null && tab.View.CoreWebView2 != null)
        {
            if (targetPanel != null && tab.View.Parent != targetPanel) targetPanel.Controls.Add(tab.View);
            return;
        }
        WebView2 w;
        string cnt = string.IsNullOrWhiteSpace(tab.Container) ? "Default" : tab.Container;
        if (cnt.Equals("Default", StringComparison.OrdinalIgnoreCase) && _prewarmedView != null && _prewarmedView.CoreWebView2 != null)
        {
            w = _prewarmedView; _prewarmedView = null;
            if (targetPanel != null && w.Parent != targetPanel) targetPanel.Controls.Add(w);
            BindTabEvents(w, tab);
            Task ignored = PrewarmDefaultAsync();
        }
        else
        {
            CoreWebView2Environment env = await GetContainerEnvAsync(cnt);
            w = new WebView2(); w.Visible = false;
            (targetPanel ?? _splitStage.Panel1).Controls.Add(w);
            await w.EnsureCoreWebView2Async(env);
            await ConfigureCoreAsync(w.CoreWebView2, env);
            BindTabEvents(w, tab);
        }
        tab.View = w;
        LayoutViewports();
        if (string.IsNullOrEmpty(_desktopUa)) _desktopUa = w.CoreWebView2.Settings.UserAgent;
        ApplyViewportUa(w.CoreWebView2);
        w.CoreWebView2.Navigate(ResolveNavUrl(tab.Url, tab.Workspace));
    }

    private void BindTabEvents(WebView2 wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        wv.KeyDown += OnAnyKeyDown;
        core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri != null && e.Uri.StartsWith("http://127.0.0.1:19777", StringComparison.OrdinalIgnoreCase) && Process.GetProcessesByName("OmegaMirror").Length == 0)
            {
                e.Cancel = true;
                BeginInvoke((Action)delegate { NavigateActive(e.Uri); });
                return;
            }
            if (_zenMode && (_workspace == "Research" || _workspace == "Training"))
            {
                string h = GetHostSafe(e.Uri);
                if (h.Contains("youtube.com") || h.Contains("reddit.com") || h.Contains("tiktok.com") || h == "x.com")
                {
                    if (MessageBox.Show(this, "Focus Mode is active in " + _workspace + " Workspace.\r\n\r\nStay focused and cancel opening " + h + "?", "Omega Focus Guard", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        e.Cancel = true; return;
                    }
                }
            }
            tab.NavWatch.Restart(); tab.ReaderOn = false;
        };
        core.NavigationCompleted += async delegate
        {
            tab.NavWatch.Stop(); tab.LastLoadMs = tab.NavWatch.ElapsedMilliseconds;
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Tab" : core.DocumentTitle;
            tab.Url = DisplayUrl(core.Source);
            if (tab.History.Count == 0 || tab.History[tab.History.Count - 1] != tab.Url) tab.History.Add(tab.Url);
            OmegaVault.LogHistoryVisit(_labRoot, tab.Title, tab.Url);

            await ApplyDomainPrefsAndStateAsync(tab);
            RefreshAllTabBars();
            if (tab == _active)
            {
                SyncActiveChrome();
                await RefreshCompanionCacheAsync(tab);
            }
            SaveSessionState();
        };
        core.DocumentTitleChanged += delegate { tab.Title = core.DocumentTitle; RefreshAllTabBars(); if (tab == _active) SyncActiveChrome(); };
        core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            BeginInvoke((Action)(async delegate { await CreateTabAsync(_workspace, tab.Container, e.Uri, true, false); }));
        };
        core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = ""; try { msg = e.TryGetWebMessageAsString(); } catch { }
            BeginInvoke((Action)(async delegate { await HandleWebMessageAsync(tab, msg); }));
        };
        core.ProcessFailed += delegate(object s, CoreWebView2ProcessFailedEventArgs e)
        {
            BeginInvoke((Action)(async delegate
            {
                _lblStatusL.Text = "[Crash Recovery] " + e.ProcessFailedKind + " isolated -> restoring tab frozen-safe...";
                SaveSessionState();
                try
                {
                    if (tab.View != null) { if (tab.View.Parent != null) tab.View.Parent.Controls.Remove(tab.View); tab.View.Dispose(); tab.View = null; }
                    if (tab == _active) await EnsureTabWebViewAsync(tab, _splitStage.Panel1);
                    else tab.Frozen = true;
                    RefreshAllTabBars();
                }
                catch { }
            }));
        };
    }

    private async Task HandleWebMessageAsync(BrowserTab tab, string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        if (msg == "PANE_ACTIVE" && tab != _active) { _active = tab; SyncActiveChrome(); RefreshAllTabBars(); await RefreshCompanionCacheAsync(tab); return; }
        if (msg == "CMD_PALETTE") { OpenCommandPalette(); return; }
        if (msg == "OPEN_FIND") { OpenFindBar(); return; }
        if (msg == "TAB_SEARCH") { OpenTabSearchModal(); return; }
        if (msg == "REOPEN_TAB") { await ReopenLastClosedTabAsync(); return; }
        if (msg == "VAULT_SEARCH") { OpenVaultSearchModal(); return; }
        if (msg == "OPEN_DOWNLOADS") { OpenDownloadStudio(); return; }
        if (msg == "OPEN_SNIFFER") { await OpenBatchLinkSnifferAsync(); return; }
        if (msg == "SHIELD_COOKIE") { Interlocked.Increment(ref _cookiesRejected); UpdateTelemetryBar(); return; }
        if (msg == "SHIELD_AUTOPLAY") { Interlocked.Increment(ref _autoplayBlocked); UpdateTelemetryBar(); return; }
        if (msg.StartsWith("OMNI_GO|")) { NavigateActive(msg.Substring(8)); return; }
        if (msg.StartsWith("OPEN_BG|")) { await CreateTabAsync(_workspace, _activeContainer, msg.Substring(8), false, false); return; }
        if (msg.StartsWith("DOM_ZAP|"))
        {
            string sel = msg.Substring(8).Trim();
            string h = GetHostSafe(tab.View != null && tab.View.CoreWebView2 != null ? tab.View.CoreWebView2.Source : tab.Url);
            OmegaZapperAndClipper.SaveDomainZap(_labRoot, h, sel);
            _lblStatusL.Text = "[DOM Zapper] Erased '" + sel + "' permanently on " + h + "!";
            return;
        }
        if (msg.StartsWith("DOM_CLIP|"))
        {
            string md = msg.Substring(9).Trim();
            ReceiptRecord rec = OmegaVault.MintReceiptAndSaveHtml(_labRoot, tab.Title + " [Subtree Clip]", tab.Url, md, md, 0, Interlocked.Read(ref _totalBlocked));
            Clipboard.SetDataObject(OmegaVault.FormatReceiptMarkdown(rec), true, 4, 40);
            CorpusSaveResult cs = OmegaVault.SaveToCorpus(_labRoot, rec, "subtree_clip,sovereign_v04");
            _lblStatusL.Text = "[Subtree Clipped] Copied MD + SHA-256 (" + rec.Sha256.Substring(0, 10) + "...) & " + (cs.Duplicate ? "already in Corpus" : "saved to Corpus!");
            return;
        }
        if (msg.StartsWith("PERF_JUMP|"))
        {
            string id = msg.Substring(10);
            for (int i = 0; i < _tabs.Count; i++) if (_tabs[i].Id == id) { await ActivateTabAsync(_tabs[i]); break; }
            return;
        }
        if (msg.StartsWith("PERF_FREEZE|"))
        {
            string id = msg.Substring(12);
            for (int i = 0; i < _tabs.Count; i++) if (_tabs[i].Id == id) { await FreezeSingleTabAsync(_tabs[i]); break; }
            return;
        }
        if (msg.StartsWith("FORM_DRAFT|"))
        {
            string rest = msg.Substring(11); int sep = rest.IndexOf('|');
            if (sep > 0) SaveFormDraft(tab.Url, rest.Substring(0, sep), rest.Substring(sep + 1));
            return;
        }
        if (msg.StartsWith("PROBE_HEAD|"))
        {
            string u = msg.Substring(11);
            string sz = await Task.Run(delegate { return PhoneBridge.ProbeHeadSize(u); });
            if (!string.IsNullOrEmpty(sz) && tab.View != null && tab.View.CoreWebView2 != null)
                await tab.View.CoreWebView2.ExecuteScriptAsync("if(window.__omegaHeadReply) window.__omegaHeadReply(\"" + OmegaJson.Esc(u) + "\",\"" + OmegaJson.Esc(sz) + "\");");
        }
    }

    private void SuppressDefaultWebViewDownloadPopup(CoreWebView2 core)
    {
        try
        {
            System.Reflection.EventInfo ev = core.GetType().GetEvent("IsDefaultDownloadDialogOpenChanged");
            System.Reflection.MethodInfo closeM = core.GetType().GetMethod("CloseDefaultDownloadDialog");
            System.Reflection.PropertyInfo openP = core.GetType().GetProperty("IsDefaultDownloadDialogOpen");
            if (ev != null && closeM != null && openP != null)
            {
                EventHandler<object> h = delegate(object s, object a)
                {
                    try
                    {
                        bool isOpen = (bool)openP.GetValue(core, null);
                        if (isOpen)
                        {
                            closeM.Invoke(core, null);
                            BeginInvoke((Action)OpenDownloadStudio);
                        }
                    }
                    catch { }
                };
                ev.AddEventHandler(core, h);
            }
        }
        catch { }
    }

    private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
    {
        HandleWebViewDownloadStartingV06(sender, e);
    }

    private async Task<BrowserTab> CreateTabAsync(string ws, string container, string url, bool activate, bool lazyFrozen)
    {
        BrowserTab t = new BrowserTab();
        t.Workspace = string.IsNullOrEmpty(ws) ? _workspace : ws;
        t.Container = string.IsNullOrEmpty(container) ? AutoRouteContainer(url, _activeContainer) : container;
        t.Url = string.IsNullOrWhiteSpace(url) ? "omega://home" : url.Trim();
        t.Title = t.Url == "omega://home" ? (t.Workspace + " Home") : t.Url;
        t.Frozen = lazyFrozen && !activate;
        t.History.Add(t.Url);
        _tabs.Add(t);
        if (activate) await ActivateTabAsync(t);
        else if (!t.Frozen) await EnsureTabWebViewAsync(t, _splitStage.Panel1);
        RefreshAllTabBars(); SaveSessionState();
        return t;
    }

    private static string AutoRouteContainer(string url, string fallback)
    {
        if (!string.Equals(fallback, "Default", StringComparison.OrdinalIgnoreCase)) return fallback;
        string h = GetHostSafe(url);
        if (h.Contains("arxiv.org") || h.Contains("ocw.mit.edu") || h.Contains("scholar.google")) return "arXiv-Lab";
        if (h.Contains("github.com")) return "GitHub-1";
        if (h.Contains("youtube.com") || h.Contains("google.com")) return "Google";
        return "Default";
    }

    private async Task ActivateTabAsync(BrowserTab tab)
    {
        if (tab == null) return;
        if (!string.Equals(_workspace, tab.Workspace, StringComparison.OrdinalIgnoreCase)) _workspace = tab.Workspace;
        _active = tab; _activeContainer = tab.Container;
        tab.Frozen = false; tab.LastActiveUtc = DateTime.UtcNow;

        Control targetPane = (_splitMode && tab == _splitRight) ? (Control)_splitStage.Panel2 : (Control)_splitStage.Panel1;
        await EnsureTabWebViewAsync(tab, targetPane);

        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab ti = _tabs[i];
            if (ti.View == null) continue;
            if (_splitMode) ti.View.Visible = (ti == _active || ti == _splitRight);
            else ti.View.Visible = (ti == _active);
        }
        if (tab.View != null) tab.View.BringToFront();
        LayoutViewports();
        SyncActiveChrome();
        RefreshAllTabBars();
        UpdateTelemetryBar();
        await RefreshCompanionCacheAsync(tab);
    }

    private void SyncActiveChrome()
    {
        if (_active == null) return;
        _omnibox.Text = _active.Url;
        _btnWs.Text = "WS: " + _workspace;
        _btnWs.BackColor = _workspace == "Training" ? Color.FromArgb(5, 150, 105) : (_workspace == "Dev" ? Color.FromArgb(217, 119, 6) : Color.FromArgb(2, 132, 199));
        _btnContainer.Text = "Cnt: " + _active.Container;
        string note = OmegaVault.GetStickyNote(_labRoot, _active.Url);
        _btnNote.Text = string.IsNullOrEmpty(note) ? "Note" : "Note *";
        _btnNote.BackColor = string.IsNullOrEmpty(note) ? Color.FromArgb(113, 63, 18) : Color.FromArgb(202, 138, 4);
        string h = GetHostSafe(_active.View != null && _active.View.CoreWebView2 != null ? _active.View.CoreWebView2.Source : _active.Url);
        DomainPref dp;
        double z = (!string.IsNullOrEmpty(h) && _domainPrefs.TryGetValue(h, out dp)) ? dp.Zoom : 1.0;
        _btnZoom.Text = Math.Round(z * 100) + "%";
        Text = _active.Title + " :: OmegaBrowser v0.6.4 [" + _workspace + " | " + _active.Container + "]";
    }

    private async void CloseTab(BrowserTab tab)
    {
        if (tab == null) return;
        ClosedTabRecord rec = new ClosedTabRecord();
        rec.Workspace = tab.Workspace; rec.Container = tab.Container; rec.Title = tab.Title;
        rec.History.AddRange(tab.History);
        _closedTabs.Push(rec);

        if (tab == _splitRight) { _splitRight = null; _splitMode = false; _splitStage.Panel2Collapsed = true; }
        _tabs.Remove(tab);
        if (tab.View != null) { if (tab.View.Parent != null) tab.View.Parent.Controls.Remove(tab.View); tab.View.Dispose(); tab.View = null; }

        BrowserTab next = null;
        for (int i = _tabs.Count - 1; i >= 0; i--) if (_tabs[i].Workspace == _workspace) { next = _tabs[i]; break; }
        if (next == null) await CreateTabAsync(_workspace, "Default", "omega://home", true, false);
        else if (_active == tab) await ActivateTabAsync(next);
        else RefreshAllTabBars();
        SaveSessionState();
    }

    private async Task ReopenLastClosedTabAsync()
    {
        if (_closedTabs.Count == 0) { _lblStatusL.Text = "No closed tabs to reopen."; return; }
        ClosedTabRecord rec = _closedTabs.Pop();
        string firstUrl = rec.History.Count > 0 ? rec.History[0] : "omega://home";
        BrowserTab t = await CreateTabAsync(rec.Workspace, rec.Container, firstUrl, true, false);
        for (int i = 1; i < rec.History.Count; i++)
        {
            t.Url = rec.History[i];
            if (t.View != null && t.View.CoreWebView2 != null)
                t.View.CoreWebView2.Navigate(ResolveNavUrl(t.Url, t.Workspace));
        }
        _lblStatusL.Text = "Reopened tab with " + rec.History.Count + " history step(s): " + rec.Title;
    }

    private void CloseDuplicateTabs()
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<BrowserTab> dupes = new List<BrowserTab>();
        if (_active != null) seen.Add(_active.Workspace + "|" + _active.Container + "|" + OmegaUrlCleaner.CleanUrl(_active.Url));
        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab t = _tabs[i];
            if (t == _active) continue;
            string key = t.Workspace + "|" + t.Container + "|" + OmegaUrlCleaner.CleanUrl(t.Url);
            if (!seen.Add(key)) dupes.Add(t);
        }
        for (int i = 0; i < dupes.Count; i++) CloseTab(dupes[i]);
        _lblStatusL.Text = "Closed " + dupes.Count + " duplicate tab(s).";
    }

    private async Task ToggleSplitViewAsync()
    {
        _splitMode = !_splitMode;
        if (!_splitMode)
        {
            _splitStage.Panel2Collapsed = true;
            if (_splitRight != null && _splitRight.View != null)
            {
                _splitStage.Panel1.Controls.Add(_splitRight.View);
                _splitRight.View.Visible = (_splitRight == _active);
            }
            _splitRight = null;
            _lblStatusL.Text = "Split View OFF.";
        }
        else
        {
            _splitStage.Panel2Collapsed = false;
            _splitStage.SplitterDistance = Math.Max(300, _splitStage.Width / 2);
            BrowserTab candidate = null;
            for (int i = 0; i < _tabs.Count; i++) if (_tabs[i] != _active && _tabs[i].Workspace == _workspace) { candidate = _tabs[i]; break; }
            if (candidate == null) candidate = await CreateTabAsync(_workspace, _activeContainer, "omega://home", false, false);
            _splitRight = candidate;
            _splitRight.Frozen = false;
            await EnsureTabWebViewAsync(_splitRight, _splitStage.Panel2);
            _splitRight.View.Dock = DockStyle.Fill;
            _splitRight.View.Visible = true;
            _splitRight.View.BringToFront();
            _lblStatusL.Text = "Split View ON :: Left [" + _active.Title + "]  ||  Right [" + _splitRight.Title + "]";
        }
        RefreshAllTabBars();
    }

    private void RefreshAllTabBars()
    {
        _tabFlow.SuspendLayout(); _tabFlow.Controls.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab t = _tabs[i];
            if (!string.Equals(t.Workspace, _workspace, StringComparison.OrdinalIgnoreCase)) continue;
            string lbl = (t.Frozen ? "[Zz] " : "") + "[" + Shorten(t.Container, 6) + "] " + Shorten(t.Title, 18) + "  x";
            Button b = AutoBtn(lbl, t == _active ? Color.FromArgb(3, 105, 161) : (t == _splitRight ? Color.FromArgb(15, 118, 110) : Color.FromArgb(20, 30, 48)));
            BrowserTab cap = t;
            b.MouseUp += async delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Middle || e.X > b.Width - 22) CloseTab(cap);
                else await ActivateTabAsync(cap);
            };
            _tabFlow.Controls.Add(b);
        }
        Button bPlus = AutoBtn("+ Tab", Color.FromArgb(15, 118, 110));
        bPlus.Click += async delegate { await CreateTabAsync(_workspace, _activeContainer, "omega://home", true, false); };
        Button bPerf = AutoBtn("Perf", Color.FromArgb(30, 41, 59));
        bPerf.Click += delegate { NavigateActive("omega://performance"); };
        Button bDev = AutoBtn("DevTools", Color.FromArgb(30, 41, 59));
        bDev.Click += delegate { if (_active != null && _active.View != null && _active.View.CoreWebView2 != null) _active.View.CoreWebView2.OpenDevToolsWindow(); };
        _tabFlow.Controls.AddRange(new Control[] { bPlus, bPerf, bDev });
        _tabFlow.ResumeLayout();

        Color wsAccent = _workspace == "Training" ? Color.FromArgb(16, 185, 129) : (_workspace == "Dev" ? Color.FromArgb(245, 158, 11) : Color.FromArgb(56, 189, 248));
        _lblVTabHeader.ForeColor = wsAccent;
        _lblVTabHeader.Text = _workspace.ToUpperInvariant() + " GROUPS (" + (_workspace == "Training" ? "GREEN" : (_workspace == "Dev" ? "ORANGE" : "BLUE")) + ")";

        _vTabFlow.SuspendLayout(); _vTabFlow.Controls.Clear();
        Dictionary<string, List<BrowserTab>> groups = new Dictionary<string, List<BrowserTab>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (!string.Equals(_tabs[i].Workspace, _workspace, StringComparison.OrdinalIgnoreCase)) continue;
            string g = GetHostSafe(_tabs[i].Url); if (string.IsNullOrEmpty(g)) g = "omega.local";
            List<BrowserTab> list;
            if (!groups.TryGetValue(g, out list)) { list = new List<BrowserTab>(); groups[g] = list; }
            list.Add(_tabs[i]);
        }
        foreach (KeyValuePair<string, List<BrowserTab>> kv in groups)
        {
            string dom = kv.Key; bool collapsed = _collapsedDomains.Contains(dom);
            Button gh = new Button(); gh.Width = 194; gh.Height = 24; gh.FlatStyle = FlatStyle.Flat;
            gh.TextAlign = ContentAlignment.MiddleLeft; gh.BackColor = Color.FromArgb(17, 24, 39); gh.ForeColor = wsAccent;
            gh.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            gh.Text = (collapsed ? "[+] " : "[-] ") + Shorten(dom, 20) + " (" + kv.Value.Count + ")";
            gh.Click += delegate { if (_collapsedDomains.Contains(dom)) _collapsedDomains.Remove(dom); else _collapsedDomains.Add(dom); RefreshAllTabBars(); };
            _vTabFlow.Controls.Add(gh);
            if (collapsed) continue;
            for (int j = 0; j < kv.Value.Count; j++)
            {
                BrowserTab t = kv.Value[j];
                Button tb = new Button(); tb.Width = 194; tb.Height = 26; tb.FlatStyle = FlatStyle.Flat;
                tb.TextAlign = ContentAlignment.MiddleLeft;
                tb.BackColor = t == _active ? Color.FromArgb(3, 105, 161) : Color.FromArgb(13, 20, 34);
                tb.ForeColor = Color.White; tb.Font = new Font("Segoe UI", 8f);
                tb.Text = (t.Frozen ? "[Zz] " : "") + Shorten(t.Title, 24);
                BrowserTab cap = t;
                tb.MouseUp += async delegate(object s, MouseEventArgs me) { if (me.Button == MouseButtons.Middle) CloseTab(cap); else await ActivateTabAsync(cap); };
                _vTabFlow.Controls.Add(tb);
            }
        }
        _vTabFlow.ResumeLayout();
    }
}
}
