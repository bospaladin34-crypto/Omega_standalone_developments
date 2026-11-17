using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OmegaBrowserV03
{
    public static class OmegaDialogs
    {
        public static void ShowCommandPalette(Form owner, List<PaletteItem> items, Action<string> onNavigateBang, Action<int> onStartTimer)
        {
            Form dlg = MakeModal("OmegaBrowser v0.3 :: Command Palette (Ctrl+K  |  = math  |  conv 64 gb to mib  |  timer 25m  |  !mit !arxiv !gh)", 640, 410);
            TextBox box = new TextBox(); box.SetBounds(12, 12, 616, 28);
            box.BackColor = Color.FromArgb(20, 30, 48); box.ForeColor = Color.White; box.Font = new Font("Consolas", 10.5f); box.BorderStyle = BorderStyle.FixedSingle;

            ListBox list = new ListBox(); list.SetBounds(12, 48, 616, 348);
            list.BackColor = Color.FromArgb(15, 23, 42); list.ForeColor = Color.FromArgb(226, 232, 240); list.Font = new Font("Consolas", 9.5f); list.BorderStyle = BorderStyle.FixedSingle;

            Action refresh = delegate()
            {
                string raw = (box.Text ?? "").Trim();
                string q = raw.ToLowerInvariant();
                list.BeginUpdate(); list.Items.Clear();

                string calc = CalcAndUnits.TryEvaluate(raw);
                if (!string.IsNullOrEmpty(calc))
                {
                    PaletteItem ci = new PaletteItem(); ci.Title = "Result: " + calc; ci.Hint = "Enter to Copy";
                    ci.Run = delegate { Clipboard.SetDataObject(calc, true, 4, 40); };
                    list.Items.Add(ci);
                }
                if (raw.StartsWith("!"))
                {
                    PaletteItem bi = new PaletteItem(); bi.Title = "Bang Jump -> " + raw; bi.Hint = "!mit !arxiv !gh !rs !mdn !scholar";
                    bi.Run = delegate { if (onNavigateBang != null) onNavigateBang(raw); };
                    list.Items.Add(bi);
                }
                Match tm = Regex.Match(raw, @"^timer\s+(\d+)\s*(m|s)?$", RegexOptions.IgnoreCase);
                if (tm.Success)
                {
                    int val = int.Parse(tm.Groups[1].Value);
                    int secs = (tm.Groups[2].Success && tm.Groups[2].Value.ToLowerInvariant() == "s") ? val : (val * 60);
                    PaletteItem ti = new PaletteItem(); ti.Title = "Start Focus Countdown Timer (" + val + (tm.Groups[2].Success ? tm.Groups[2].Value : "m") + ")"; ti.Hint = "Status Timer";
                    ti.Run = delegate { if (onStartTimer != null) onStartTimer(secs); };
                    list.Items.Add(ti);
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (q.Length == 0 || (items[i].Title + " " + items[i].Hint).ToLowerInvariant().Contains(q))
                        list.Items.Add(items[i]);
                }
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                list.EndUpdate();
            };

            WireListModal(dlg, box, list, refresh);
            dlg.ShowDialog(owner);
        }

        public static void ShowTabSearch(Form owner, List<PaletteItem> tabItems, Action onDedupe, Action onReopen)
        {
            Form dlg = MakeModal("Tab Search (Ctrl+Shift+A) :: Jump Across 40+ Tabs, Dedupe & Reopen History", 650, 420);
            TextBox box = new TextBox(); box.SetBounds(12, 12, 370, 28);
            box.BackColor = Color.FromArgb(20, 30, 48); box.ForeColor = Color.White; box.Font = new Font("Segoe UI", 10.5f); box.BorderStyle = BorderStyle.FixedSingle;

            Button bDedup = Btn("Close Duplicates", 390, 10, 122, Color.FromArgb(153, 27, 27));
            bDedup.Click += delegate { dlg.Close(); if (onDedupe != null) onDedupe(); };

            Button bReopen = Btn("Reopen Closed", 518, 10, 120, Color.FromArgb(14, 116, 144));
            bReopen.Click += delegate { dlg.Close(); if (onReopen != null) onReopen(); };

            ListBox list = new ListBox(); list.SetBounds(12, 48, 626, 358);
            list.BackColor = Color.FromArgb(15, 23, 42); list.ForeColor = Color.White; list.Font = new Font("Consolas", 9.5f);

            Action refresh = delegate()
            {
                string q = (box.Text ?? "").Trim().ToLowerInvariant();
                list.BeginUpdate(); list.Items.Clear();
                for (int i = 0; i < tabItems.Count; i++)
                    if (q.Length == 0 || (tabItems[i].Title + " " + tabItems[i].Hint).ToLowerInvariant().Contains(q)) list.Items.Add(tabItems[i]);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                list.EndUpdate();
            };

            dlg.Controls.Add(bDedup); dlg.Controls.Add(bReopen);
            WireListModal(dlg, box, list, refresh);
            dlg.ShowDialog(owner);
        }

        public static void ShowHistoryVaultSearch(Form owner, string labRoot, Action<string> onNavigateUrl)
        {
            Form dlg = MakeModal("Full-Text History & Sovereign Markdown Vault Search (Ctrl+H)", 720, 440);
            TextBox box = new TextBox(); box.SetBounds(12, 12, 696, 28);
            box.BackColor = Color.FromArgb(20, 30, 48); box.ForeColor = Color.White; box.Font = new Font("Segoe UI", 10.5f); box.BorderStyle = BorderStyle.FixedSingle;

            ListBox list = new ListBox(); list.SetBounds(12, 48, 696, 378);
            list.BackColor = Color.FromArgb(15, 23, 42); list.ForeColor = Color.FromArgb(226, 232, 240); list.Font = new Font("Consolas", 9f);

            Action refresh = delegate()
            {
                List<SearchHit> hits = OmegaVault.SearchHistoryAndVault(labRoot, box.Text);
                list.BeginUpdate(); list.Items.Clear();
                for (int i = 0; i < hits.Count; i++) list.Items.Add(hits[i]);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                list.EndUpdate();
            };

            Action openSel = delegate()
            {
                SearchHit h = list.SelectedItem as SearchHit;
                if (h == null) return;
                dlg.Close();
                if (h.Kind == "History" && onNavigateUrl != null) onNavigateUrl(h.UrlOrPath);
                else if (File.Exists(h.UrlOrPath)) Process.Start("explorer.exe", "/select,\"" + h.UrlOrPath + "\"");
            };

            box.TextChanged += delegate { refresh(); };
            list.DoubleClick += delegate { openSel(); };
            dlg.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) dlg.Close();
                else if (e.KeyCode == Keys.Enter) { e.Handled = true; openSel(); }
                else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { e.Handled = true; list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); }
                else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { e.Handled = true; list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); }
            };
            dlg.Controls.Add(box); dlg.Controls.Add(list);
            refresh(); dlg.Shown += delegate { box.Focus(); };
            dlg.ShowDialog(owner);
        }

        public static void ShowQrDialog(Form owner, string url, Action onPushAdb)
        {
            string clean = OmegaUrlCleaner.CleanUrl(url);
            Form dlg = MakeModal("QR Bridge :: Scan with Pixel Camera", 380, 440);
            PictureBox pb = new PictureBox(); pb.SetBounds(30, 16, 320, 320); pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.Image = QrCodeEncoder.RenderBitmap(clean, 8);

            TextBox tb = new TextBox(); tb.SetBounds(16, 346, 348, 24); tb.ReadOnly = true; tb.Text = clean;
            tb.BackColor = Color.FromArgb(15, 23, 42); tb.ForeColor = Color.FromArgb(56, 189, 248);

            Button bCopy = Btn("Copy Clean URL", 16, 382, 165, Color.FromArgb(3, 105, 161));
            bCopy.Click += delegate { Clipboard.SetDataObject(clean, true, 4, 40); };
            Button bAdb = Btn("Push via ADB", 195, 382, 169, Color.FromArgb(15, 118, 110));
            bAdb.Click += delegate { if (onPushAdb != null) onPushAdb(); dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { pb, tb, bCopy, bAdb });
            dlg.ShowDialog(owner);
        }

        public static void ShowStickyAndHighlightDialog(Form owner, string labRoot, string title, string url, string selectedQuote, Action onUpdated)
        {
            Form dlg = MakeModal("Lab Notebook :: Sticky Note & Paper/PDF Vault Highlights", 620, 450);
            Label l1 = Lbl("Sticky Note Anchored to URL (" + OmegaUrlCleaner.CleanUrl(url) + "):", 14, 10, 590);
            TextBox sticky = new TextBox(); sticky.SetBounds(14, 32, 592, 145); sticky.Multiline = true; sticky.ScrollBars = ScrollBars.Vertical;
            sticky.BackColor = Color.FromArgb(20, 28, 44); sticky.ForeColor = Color.FromArgb(253, 224, 71); sticky.Font = new Font("Consolas", 9.5f);
            sticky.Text = OmegaVault.GetStickyNote(labRoot, url);

            Label l2 = Lbl("Paper / PDF Quote or Highlight to Append to Markdown Vault:", 14, 188, 590);
            TextBox quote = new TextBox(); quote.SetBounds(14, 210, 592, 110); quote.Multiline = true; quote.ScrollBars = ScrollBars.Vertical;
            quote.BackColor = Color.FromArgb(15, 23, 42); quote.ForeColor = Color.White; quote.Font = new Font("Consolas", 9.5f);
            quote.Text = selectedQuote ?? "";

            Label l3 = Lbl("Optional Margin Commentary / Page #:", 14, 328, 590);
            TextBox comm = new TextBox(); comm.SetBounds(14, 350, 592, 26);
            comm.BackColor = Color.FromArgb(15, 23, 42); comm.ForeColor = Color.White;

            Button bSave = Btn("Save Sticky Note + Vault Highlight", 14, 392, 260, Color.FromArgb(15, 118, 110));
            bSave.Click += delegate
            {
                OmegaVault.SaveStickyNote(labRoot, url, sticky.Text);
                if (!string.IsNullOrWhiteSpace(quote.Text)) OmegaVault.AppendHighlightToVault(labRoot, title, url, quote.Text, comm.Text);
                if (onUpdated != null) onUpdated();
                dlg.Close();
            };
            Button bOpenVault = Btn("Open highlights_vault.md", 284, 392, 190, Color.FromArgb(30, 64, 110));
            bOpenVault.Click += delegate
            {
                string vp = Path.Combine(labRoot, "vault", "highlights_vault.md");
                if (File.Exists(vp)) Process.Start("notepad.exe", "\"" + vp + "\"");
            };
            Button bClose = Btn("Close", 484, 392, 122, Color.FromArgb(51, 65, 85));
            bClose.Click += delegate { dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { l1, sticky, l2, quote, l3, comm, bSave, bOpenVault, bClose });
            dlg.ShowDialog(owner);
        }

        public static void ShowTimeMachineDialog(Form owner, string labRoot, ReceiptRecord liveRec)
        {
            Form dlg = MakeModal("Time Machine :: Local Provenance Archiver & Version Diff", 740, 480);
            ListBox snaps = new ListBox(); snaps.SetBounds(14, 14, 250, 395);
            snaps.BackColor = Color.FromArgb(15, 23, 42); snaps.ForeColor = Color.White; snaps.Font = new Font("Consolas", 9f);

            TextBox viewer = new TextBox(); viewer.SetBounds(274, 14, 452, 395); viewer.Multiline = true; viewer.ReadOnly = true; viewer.ScrollBars = ScrollBars.Both;
            viewer.BackColor = Color.FromArgb(9, 13, 22); viewer.ForeColor = Color.FromArgb(226, 232, 240); viewer.Font = new Font("Consolas", 9f);

            List<string> paths = new List<string>();
            Action reload = delegate()
            {
                paths = OmegaVault.ListTimeMachineSnapshots(labRoot, liveRec.FinalUrl);
                snaps.Items.Clear();
                for (int i = 0; i < paths.Count; i++) snaps.Items.Add(Path.GetFileNameWithoutExtension(paths[i]));
                if (snaps.Items.Count > 0) snaps.SelectedIndex = 0;
                else viewer.Text = "No archived snapshots yet for:\r\n" + liveRec.FinalUrl + "\r\n\r\nClick 'Archive Snapshot Now' below to capture provenance.";
            };

            Button bSnap = Btn("Archive Snapshot Now", 14, 422, 180, Color.FromArgb(15, 118, 110));
            bSnap.Click += delegate
            {
                string f = OmegaVault.SaveTimeMachineSnapshot(labRoot, liveRec);
                reload();
                viewer.Text = "Saved Time Machine snapshot:\r\n" + f + "\r\nSHA-256: " + liveRec.Sha256;
            };

            Button bDiff = Btn("Diff Selected vs Live Page", 204, 422, 195, Color.FromArgb(109, 40, 217));
            bDiff.Click += delegate
            {
                if (snaps.SelectedIndex < 0 || snaps.SelectedIndex >= paths.Count) return;
                string oldMd = File.ReadAllText(paths[snaps.SelectedIndex]);
                string curMd = OmegaVault.FormatReceiptMarkdown(liveRec);
                viewer.Text = OmegaVault.DiffTexts(oldMd, curMd);
            };

            Button bView = Btn("View Archived Copy", 409, 422, 165, Color.FromArgb(3, 105, 161));
            bView.Click += delegate
            {
                if (snaps.SelectedIndex >= 0 && snaps.SelectedIndex < paths.Count)
                    viewer.Text = File.ReadAllText(paths[snaps.SelectedIndex]).Replace("\r\n", "\n").Replace("\n", "\r\n");
            };

            Button bClose = Btn("Close", 584, 422, 142, Color.FromArgb(51, 65, 85));
            bClose.Click += delegate { dlg.Close(); };

            reload();
            dlg.Controls.AddRange(new Control[] { snaps, viewer, bSnap, bDiff, bView, bClose });
            dlg.ShowDialog(owner);
        }

        public static void ShowScreenshotStudio(Form owner, Bitmap sourceBmp, string labRoot, string title, string url)
        {
            Bitmap working = new Bitmap(sourceBmp);
            Form dlg = MakeModal("Screenshot Plus Studio :: Drag to Annotate, Privacy-Blur, or Crop Evidence", 900, 620);

            RadioButton rBox = new RadioButton(); rBox.Text = "Draw Red Box"; rBox.SetBounds(14, 10, 120, 26); rBox.Checked = true;
            RadioButton rBlur = new RadioButton(); rBlur.Text = "Privacy Pixelate"; rBlur.SetBounds(140, 10, 135, 26);
            RadioButton rCrop = new RadioButton(); rCrop.Text = "Crop Region"; rCrop.SetBounds(282, 10, 115, 26);

            PictureBox canvas = new PictureBox(); canvas.SetBounds(14, 44, 872, 515);
            canvas.BackColor = Color.Black; canvas.SizeMode = PictureBoxSizeMode.Zoom; canvas.Image = working;

            Point ptStart = Point.Empty; bool dragging = false;
            Func<Point, Point> mapPt = delegate(Point p)
            {
                if (working.Width <= 0 || working.Height <= 0) return Point.Empty;
                float ratio = Math.Min((float)canvas.Width / working.Width, (float)canvas.Height / working.Height);
                float ox = (canvas.Width - working.Width * ratio) / 2f;
                float oy = (canvas.Height - working.Height * ratio) / 2f;
                int ix = (int)((p.X - ox) / ratio), iy = (int)((p.Y - oy) / ratio);
                return new Point(Math.Max(0, Math.Min(working.Width - 1, ix)), Math.Max(0, Math.Min(working.Height - 1, iy)));
            };

            canvas.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; ptStart = mapPt(e.Location); } };
            canvas.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return; dragging = false;
                Point ptEnd = mapPt(e.Location);
                Rectangle rc = new Rectangle(Math.Min(ptStart.X, ptEnd.X), Math.Min(ptStart.Y, ptEnd.Y), Math.Abs(ptEnd.X - ptStart.X), Math.Abs(ptEnd.Y - ptStart.Y));
                if (rc.Width < 6 || rc.Height < 6) return;
                if (rCrop.Checked)
                {
                    Bitmap cropped = working.Clone(rc, working.PixelFormat);
                    working.Dispose(); working = cropped; canvas.Image = working;
                }
                else if (rBox.Checked)
                {
                    using (Graphics g = Graphics.FromImage(working))
                    using (Pen pen = new Pen(Color.FromArgb(239, 68, 68), Math.Max(3f, working.Width / 380f)))
                        g.DrawRectangle(pen, rc);
                    canvas.Invalidate();
                }
                else if (rBlur.Checked)
                {
                    int block = 12;
                    for (int y = rc.Top; y < rc.Bottom; y += block)
                        for (int x = rc.Left; x < rc.Right; x += block)
                        {
                            Color c = working.GetPixel(x, y);
                            using (Graphics g = Graphics.FromImage(working))
                            using (Brush br = new SolidBrush(c))
                                g.FillRectangle(br, x, y, Math.Min(block, rc.Right - x), Math.Min(block, rc.Bottom - y));
                        }
                    canvas.Invalidate();
                }
            };

            Button bCopy = Btn("Copy PNG to Clipboard", 410, 8, 175, Color.FromArgb(3, 105, 161));
            bCopy.Click += delegate { Clipboard.SetImage(working); };

            Button bSave = Btn("Save Vault PNG + MD Evidence", 595, 8, 210, Color.FromArgb(15, 118, 110));
            bSave.Click += delegate
            {
                string dir = Path.Combine(labRoot, "vault", "screenshots"); Directory.CreateDirectory(dir);
                string baseName = "evidence_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string pngPath = Path.Combine(dir, baseName + ".png");
                working.Save(pngPath, ImageFormat.Png);
                string sha = OmegaVault.ComputeSha256File(pngPath);
                string mdPath = Path.Combine(dir, baseName + ".md");
                File.WriteAllText(mdPath, "# Screenshot Evidence: " + title + "\r\n- **URL:** " + url + "\r\n- **UTC:** " + DateTime.UtcNow.ToString("o") + "\r\n- **PNG SHA-256:** " + sha + "\r\n- **File:** " + pngPath + "\r\n", new UTF8Encoding(false));
                dlg.Close();
            };

            Button bClose = Btn("Close", 812, 8, 74, Color.FromArgb(51, 65, 85));
            bClose.Click += delegate { dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { rBox, rBlur, rCrop, canvas, bCopy, bSave, bClose });
            dlg.ShowDialog(owner);
        }

        public static void ShowShieldDashboard(Form owner, string currentHost, long totalBlocked, long cookiesRej, long autoBlk, long total1P, long total3P, Dictionary<string, SiteShieldStat> stats, string rulesPath, Action onRulesChanged)
        {
            Form dlg = MakeModal("OmegaBrowser v0.3 :: Sovereign Shield, Cookie Guard & Wildcard Ruleset", 700, 450);
            Label summary = Lbl("Trackers Blocked: " + totalBlocked + "  |  Cookie Banners Rejected: " + cookiesRej + "  |  Autoplay Blocked: " + autoBlk + "\r\n" +
                "Est. Saved: " + (totalBlocked * 28L) + " KB  |  1st-Party: " + total1P + " vs 3rd-Party: " + total3P + "  |  Host: " + (string.IsNullOrEmpty(currentHost) ? "(home)" : currentHost), 14, 12, 672);
            summary.Height = 42; summary.ForeColor = Color.FromArgb(56, 189, 248);

            ListView lv = new ListView(); lv.SetBounds(14, 60, 672, 322);
            lv.View = View.Details; lv.FullRowSelect = true; lv.GridLines = true;
            lv.BackColor = Color.FromArgb(15, 23, 42); lv.ForeColor = Color.White;
            lv.Columns.Add("Domain", 260); lv.Columns.Add("Blocked", 90); lv.Columns.Add("1st-Party", 100); lv.Columns.Add("3rd-Party", 100); lv.Columns.Add("Saved", 100);

            List<string> bf; HashSet<string> allowed;
            OmegaSecurity.LoadShieldRules(rulesPath, out bf, out allowed);
            foreach (KeyValuePair<string, SiteShieldStat> kv in stats)
            {
                SiteShieldStat st = kv.Value;
                lv.Items.Add(new ListViewItem(new string[] { st.Host + (OmegaSecurity.IsSiteAllowed(allowed, st.Host) ? " [ALLOWED]" : ""), st.Blocked.ToString(), st.FirstParty.ToString(), st.ThirdParty.ToString(), (st.Blocked * 28) + " KB" }));
            }

            bool curAllowed = OmegaSecurity.IsSiteAllowed(allowed, currentHost);
            Button bSite = Btn(curAllowed ? "Re-Enable Shield on Site" : "Allow Current Site", 14, 395, 210, Color.FromArgb(14, 116, 144));
            bSite.Enabled = !string.IsNullOrEmpty(currentHost);
            bSite.Click += delegate { OmegaSecurity.SaveSiteAllowToggle(rulesPath, currentHost, !curAllowed); if (onRulesChanged != null) onRulesChanged(); dlg.Close(); };

            Button bExp = Btn("Export Ruleset (Zero History)", 234, 395, 205, Color.FromArgb(30, 64, 110));
            bExp.Click += delegate { SaveFileDialog sfd = new SaveFileDialog(); sfd.FileName = "omega_shield_rules.txt"; if (sfd.ShowDialog(dlg) == DialogResult.OK) File.Copy(rulesPath, sfd.FileName, true); };

            Button bImp = Btn("Import Ruleset", 449, 395, 135, Color.FromArgb(30, 64, 110));
            bImp.Click += delegate { OpenFileDialog ofd = new OpenFileDialog(); if (ofd.ShowDialog(dlg) == DialogResult.OK) { File.Copy(ofd.FileName, rulesPath, true); if (onRulesChanged != null) onRulesChanged(); dlg.Close(); } };

            Button bClose = Btn("Close", 594, 395, 92, Color.FromArgb(51, 65, 85));
            bClose.Click += delegate { dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { summary, lv, bSite, bExp, bImp, bClose });
            dlg.ShowDialog(owner);
        }

        private static Form MakeModal(string title, int w, int h)
        {
            Form f = new Form(); f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(w, h);
            f.BackColor = Color.FromArgb(12, 18, 30); f.ForeColor = Color.White;
            f.MaximizeBox = false; f.MinimizeBox = false; f.KeyPreview = true;
            return f;
        }

        private static void WireListModal(Form dlg, TextBox box, ListBox list, Action refresh)
        {
            Action exec = delegate()
            {
                PaletteItem sel = list.SelectedItem as PaletteItem;
                if (sel != null && sel.Run != null) { dlg.Close(); sel.Run(); }
            };
            box.TextChanged += delegate { refresh(); };
            list.DoubleClick += delegate { exec(); };
            dlg.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; dlg.Close(); }
                else if (e.KeyCode == Keys.Enter) { e.Handled = true; exec(); }
                else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { e.Handled = true; list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); }
                else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { e.Handled = true; list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); }
            };
            dlg.Controls.Add(box); dlg.Controls.Add(list);
            refresh(); dlg.Shown += delegate { box.Focus(); };
        }

        private static Label Lbl(string t, int x, int y, int w)
        {
            Label l = new Label(); l.Text = t; l.SetBounds(x, y, w, 20); l.ForeColor = Color.FromArgb(148, 163, 184); l.Font = new Font("Segoe UI Semibold", 9f); return l;
        }

        private static Button Btn(string txt, int x, int y, int w, Color bg)
        {
            Button b = new Button(); b.Text = txt; b.SetBounds(x, y, w, 34);
            b.FlatStyle = FlatStyle.Flat; b.BackColor = bg; b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            return b;
        }
    }
}
