using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaBrowserV03
{
    public sealed partial class BrowserForm
    {
        public void OpenDownloadStudio()
        {
            if (_dlStudioForm != null && !_dlStudioForm.IsDisposed)
            {
                _dlStudioForm.Show();
                if (_dlStudioForm.WindowState == FormWindowState.Minimized) _dlStudioForm.WindowState = FormWindowState.Normal;
                _dlStudioForm.BringToFront();
                _dlStudioForm.Focus();
                RefreshDownloadStudioGrid();
                return;
            }

            Form f = new Form();
            f.Text = "OmegaBrowser v0.6.4 :: Sovereign Vault Download Studio (Ctrl+J) — HTTP 206 Pause/Resume + OmegaAV & Dual-Pixel Bridge";
            f.Size = new Size(1280, 670);
            f.MinimumSize = new Size(1120, 520);
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Color.FromArgb(9, 13, 22);
            f.ForeColor = Color.White;
            f.KeyPreview = true;
            if (Icon != null) f.Icon = Icon;

            Panel topHdr = new Panel();
            topHdr.Dock = DockStyle.Top;
            topHdr.Height = 82;
            topHdr.BackColor = Color.FromArgb(13, 20, 35);
            topHdr.Padding = new Padding(12, 6, 12, 6);

            Panel row1 = new Panel();
            row1.Dock = DockStyle.Top;
            row1.Height = 32;

            FlowLayoutPanel row1Right = new FlowLayoutPanel();
            row1Right.Dock = DockStyle.Right;
            row1Right.AutoSize = true;
            row1Right.WrapContents = false;

            Button bOpenRoot = AutoBtn("Open Vault Folder", Color.FromArgb(15, 118, 110));
            bOpenRoot.Click += delegate
            {
                string r = OmegaDownloadRouter.ResolveDownloadsRoot(_labRoot);
                Directory.CreateDirectory(r);
                Process.Start("explorer.exe", "\"" + r + "\"");
            };

            Button bOpenSniffer = AutoBtn("Sniff Page Links (Ctrl+Shift+D)", Color.FromArgb(109, 40, 217));
            bOpenSniffer.Click += async delegate { await OpenBatchLinkSnifferAsync(); };
            row1Right.Controls.AddRange(new Control[] { bOpenRoot, bOpenSniffer });

            _dlVaultHdrLabel = new Label();
            _dlVaultHdrLabel.Dock = DockStyle.Fill;
            _dlVaultHdrLabel.TextAlign = ContentAlignment.MiddleLeft;
            _dlVaultHdrLabel.Font = new Font("Consolas", 9.0f, FontStyle.Bold);
            _dlVaultHdrLabel.ForeColor = Color.FromArgb(56, 189, 248);
            _dlVaultHdrLabel.Text = OmegaDownloadRouter.GetVaultDriveSummary(_labRoot);

            row1.Controls.Add(_dlVaultHdrLabel);
            row1.Controls.Add(row1Right);

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Bottom;
            row2.Height = 36;
            row2.WrapContents = false;
            row2.Padding = new Padding(0, 4, 0, 0);

            Label lUrl = new Label();
            lUrl.Text = "+ HTTP 206 URL:";
            lUrl.AutoSize = true;
            lUrl.Padding = new Padding(0, 5, 6, 0);
            lUrl.ForeColor = Color.FromArgb(167, 139, 250);
            lUrl.Font = new Font("Segoe UI Semibold", 8.6f);

            _dlDirectUrlBox = new TextBox();
            _dlDirectUrlBox.Width = 440;
            _dlDirectUrlBox.BackColor = Color.FromArgb(7, 11, 19);
            _dlDirectUrlBox.ForeColor = Color.White;
            _dlDirectUrlBox.Font = new Font("Consolas", 9.0f);
            _dlDirectUrlBox.BorderStyle = BorderStyle.FixedSingle;
            _dlDirectUrlBox.Margin = new Padding(0, 2, 10, 0);

            _dlDirectDistillChk = new CheckBox();
            _dlDirectDistillChk.Text = "Auto-Distill PDF -> +Corpus";
            _dlDirectDistillChk.AutoSize = true;
            _dlDirectDistillChk.Checked = true;
            _dlDirectDistillChk.ForeColor = Color.FromArgb(203, 213, 225);
            _dlDirectDistillChk.Font = new Font("Segoe UI", 8.4f);
            _dlDirectDistillChk.Margin = new Padding(0, 4, 10, 0);

            Button bEnqueue = AutoBtn("Start HTTP 206 Download", Color.FromArgb(2, 132, 199));
            bEnqueue.Margin = new Padding(0, 1, 12, 0);
            bEnqueue.Click += delegate
            {
                string u = _dlDirectUrlBox.Text.Trim();
                if (string.IsNullOrEmpty(u)) return;
                EnqueueHttpRangeDownload(u, "", _dlDirectDistillChk.Checked, false);
                _dlDirectUrlBox.Text = "";
            };
            _dlDirectUrlBox.KeyDown += delegate(object s2, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; bEnqueue.PerformClick(); }
            };

            Label lSlots = new Label();
            lSlots.Text = "Parallel Slots:";
            lSlots.AutoSize = true;
            lSlots.Padding = new Padding(0, 5, 4, 0);
            lSlots.ForeColor = Color.FromArgb(148, 163, 184);
            lSlots.Font = new Font("Segoe UI", 8.4f);

            _dlMaxConcurrencyBox = new NumericUpDown();
            _dlMaxConcurrencyBox.Width = 54;
            _dlMaxConcurrencyBox.Minimum = 1;
            _dlMaxConcurrencyBox.Maximum = 8;
            _dlMaxConcurrencyBox.Value = _maxConcurrentRangeJobs;
            _dlMaxConcurrencyBox.BackColor = Color.FromArgb(15, 23, 42);
            _dlMaxConcurrencyBox.ForeColor = Color.White;
            _dlMaxConcurrencyBox.Margin = new Padding(0, 2, 0, 0);
            _dlMaxConcurrencyBox.ValueChanged += delegate { _maxConcurrentRangeJobs = (int)_dlMaxConcurrencyBox.Value; PumpRangeDownloadQueue(); };

            row2.Controls.AddRange(new Control[] { lUrl, _dlDirectUrlBox, _dlDirectDistillChk, bEnqueue, lSlots, _dlMaxConcurrencyBox });
            topHdr.Controls.Add(row2);
            topHdr.Controls.Add(row1);

            Panel botBar = new Panel();
            botBar.Dock = DockStyle.Bottom;
            botBar.Height = 72;
            botBar.BackColor = Color.FromArgb(12, 18, 31);
            botBar.Padding = new Padding(12, 6, 12, 6);

            FlowLayoutPanel actFlow = new FlowLayoutPanel();
            actFlow.Dock = DockStyle.Top;
            actFlow.Height = 34;
            actFlow.WrapContents = false;

            Button bPause = AutoBtn("Pause", Color.FromArgb(146, 64, 14));
            bPause.Click += delegate { foreach (DownloadTrackItem sel in GetSelectedStudioItems()) PauseDownloadItem(sel); };

            Button bResume = AutoBtn("Resume (HTTP 206)", Color.FromArgb(3, 105, 161));
            bResume.Click += delegate { foreach (DownloadTrackItem sel in GetSelectedStudioItems()) ResumeDownloadItem(sel); };

            Button bCancel = AutoBtn("Cancel", Color.FromArgb(153, 27, 27));
            bCancel.Click += delegate { foreach (DownloadTrackItem sel in GetSelectedStudioItems()) CancelDownloadItem(sel); };

            Button bSendAv = AutoBtn("-> Send to OmegaAV v0.7.1", Color.FromArgb(5, 150, 105));
            bSendAv.Click += delegate
            {
                List<DownloadTrackItem> sels = GetSelectedStudioItems();
                if (sels.Count == 0) { _dlSummaryLabel.Text = "Select a downloaded .wav or .omsh shader first."; return; }
                string msg = OmegaCrossAppBridge.SendFileToOmegaAv(_labRoot, sels[0].FilePath);
                _dlSummaryLabel.Text = msg; _lblStatusL.Text = msg;
            };

            Button bSendSynth = AutoBtn("-> Send to OmegaSynth v1.1.1 GM", Color.FromArgb(190, 24, 93));
            bSendSynth.Click += delegate
            {
                List<DownloadTrackItem> sels = GetSelectedStudioItems();
                if (sels.Count == 0) { _dlSummaryLabel.Text = "Select a downloaded .wav/.mp3/.flac/.osynth file first."; return; }
                string msg = OmegaCrossAppBridge.SendFileToOmegaSynth(_labRoot, sels[0].FilePath);
                _dlSummaryLabel.Text = msg; _lblStatusL.Text = msg;
            };

            Button bPushPx = AutoBtn("-> Push to Pixel 8 / 10", Color.FromArgb(109, 40, 217));
            bPushPx.Click += delegate { ShowStudioPixelPushMenu(bPushPx); };

            Button bDistill = AutoBtn("PDF -> +Corpus", Color.FromArgb(15, 118, 110));
            bDistill.Click += async delegate
            {
                List<DownloadTrackItem> sels = GetSelectedStudioItems();
                for (int i = 0; i < sels.Count; i++) if (sels[i].IsPdf) await DistillDownloadedPdfAsync(sels[i]);
            };

            Button bCopySha = AutoBtn("Copy SHA-256", Color.FromArgb(51, 65, 85));
            bCopySha.Click += delegate
            {
                List<DownloadTrackItem> sels = GetSelectedStudioItems();
                if (sels.Count > 0 && !string.IsNullOrEmpty(sels[0].Sha256))
                {
                    Clipboard.SetDataObject(sels[0].Sha256, true, 4, 40);
                    _dlSummaryLabel.Text = "Copied SHA-256: " + sels[0].Sha256;
                }
            };

            Button bShowExp = AutoBtn("Show in Folder", Color.FromArgb(30, 58, 138));
            bShowExp.Click += delegate
            {
                List<DownloadTrackItem> sels = GetSelectedStudioItems();
                if (sels.Count > 0 && File.Exists(sels[0].FilePath))
                    Process.Start("explorer.exe", "/select,\"" + sels[0].FilePath + "\"");
            };

            Button bClearDone = AutoBtn("Clear Finished", Color.FromArgb(51, 65, 85));
            bClearDone.Click += delegate
            {
                for (int i = _downloads.Count - 1; i >= 0; i--)
                {
                    if (_downloads[i].Done)
                    {
                        if (_downloads[i].Chip != null && _downloads[i].Chip.Parent != null)
                            _downloads[i].Chip.Parent.Controls.Remove(_downloads[i].Chip);
                        _downloads.RemoveAt(i);
                    }
                }
                SaveDownloadsLedger();
                RefreshDownloadStudioGrid();
            };

            actFlow.Controls.AddRange(new Control[] { bPause, bResume, bCancel, bSendSynth, bSendAv, bPushPx, bDistill, bCopySha, bShowExp, bClearDone });

            _dlSummaryLabel = new Label();
            _dlSummaryLabel.Dock = DockStyle.Bottom;
            _dlSummaryLabel.Height = 22;
            _dlSummaryLabel.Font = new Font("Consolas", 8.5f);
            _dlSummaryLabel.ForeColor = Color.FromArgb(148, 163, 184);
            _dlSummaryLabel.Text = "Double-click any completed item to reveal in Vault, or select it and click [-> Send to OmegaAV v0.7.1] / [-> Push to Pixel 8 / 10].";

            botBar.Controls.Add(actFlow);
            botBar.Controls.Add(_dlSummaryLabel);

            _dlListView = new ListView();
            _dlListView.Dock = DockStyle.Fill;
            _dlListView.View = View.Details;
            _dlListView.FullRowSelect = true;
            _dlListView.GridLines = false;
            _dlListView.MultiSelect = true;
            _dlListView.BackColor = Color.FromArgb(7, 11, 19);
            _dlListView.ForeColor = Color.FromArgb(226, 232, 240);
            _dlListView.Font = new Font("Consolas", 9.0f);
            _dlListView.BorderStyle = BorderStyle.None;
            EnableListViewDoubleBuffer(_dlListView);

            _dlListView.Columns.Add("File Name", 285);
            _dlListView.Columns.Add("Category", 90);
            _dlListView.Columns.Add("Engine", 90);
            _dlListView.Columns.Add("Status", 160);
            _dlListView.Columns.Add("Pct", 60);
            _dlListView.Columns.Add("Received / Total", 175);
            _dlListView.Columns.Add("Speed & ETA", 160);
            _dlListView.Columns.Add("SHA-256 & Corpus", 215);

            _dlListView.DoubleClick += delegate
            {
                List<DownloadTrackItem> sels = GetSelectedStudioItems();
                if (sels.Count > 0 && File.Exists(sels[0].FilePath))
                    Process.Start("explorer.exe", "/select,\"" + sels[0].FilePath + "\"");
            };

            f.KeyDown += delegate(object s3, KeyEventArgs ke3)
            {
                if (ke3.KeyCode == Keys.Escape) { ke3.SuppressKeyPress = true; f.Close(); }
            };

            f.Controls.Add(_dlListView);
            f.Controls.Add(topHdr);
            f.Controls.Add(botBar);

            _dlFastUiTimer = new System.Windows.Forms.Timer();
            _dlFastUiTimer.Interval = 400;
            _dlFastUiTimer.Tick += delegate
            {
                for (int i = 0; i < _downloads.Count; i++) if (!_downloads[i].Done) UpdateDownloadItemV06(_downloads[i]);
                RefreshDownloadStudioGrid();
            };
            f.FormClosed += delegate
            {
                if (_dlFastUiTimer != null) { _dlFastUiTimer.Stop(); _dlFastUiTimer.Dispose(); _dlFastUiTimer = null; }
                _dlStudioForm = null;
                _dlListView = null;
            };

            _dlStudioForm = f;
            RefreshDownloadStudioGrid();
            _dlFastUiTimer.Start();
            f.Show(this);
        }

        private List<DownloadTrackItem> GetSelectedStudioItems()
        {
            List<DownloadTrackItem> list = new List<DownloadTrackItem>();
            if (_dlListView == null || _dlListView.IsDisposed) return list;
            for (int i = 0; i < _dlListView.SelectedItems.Count; i++)
            {
                DownloadTrackItem it = _dlListView.SelectedItems[i].Tag as DownloadTrackItem;
                if (it != null) list.Add(it);
            }
            return list;
        }

        private void ShowStudioPixelPushMenu(Control btn)
        {
            List<DownloadTrackItem> sels = GetSelectedStudioItems();
            if (sels.Count == 0 || !File.Exists(sels[0].FilePath))
            {
                if (_dlSummaryLabel != null) _dlSummaryLabel.Text = "Select a completed download first to push over ADB.";
                return;
            }
            DownloadTrackItem targetItem = sels[0];
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Items.Add("Broadcast Push -> ALL Connected Pixels (/sdcard/Download)", null, async delegate
            {
                if (_dlSummaryLabel != null) _dlSummaryLabel.Text = "ADB Broadcasting " + targetItem.FileName + " -> All Pixels...";
                string msg = await Task.Run(delegate { return OmegaCrossAppBridge.BroadcastFileToAllPixels(targetItem.FilePath); });
                if (_dlSummaryLabel != null) _dlSummaryLabel.Text = msg;
                _lblStatusL.Text = msg;
            });
            cms.Items.Add(new ToolStripSeparator());

            List<PixelDeviceInfo> devs = DualPixelMesh.DiscoverPixels();
            if (devs.Count == 0)
            {
                cms.Items.Add("(No ADB Pixel detected — open Dual-Pixel Hub to pair)").Enabled = false;
            }
            else
            {
                for (int i = 0; i < devs.Count; i++)
                {
                    PixelDeviceInfo dev = devs[i];
                    cms.Items.Add("Push -> " + dev.Label, null, async delegate
                    {
                        if (_dlSummaryLabel != null) _dlSummaryLabel.Text = "ADB Pushing " + targetItem.FileName + " -> " + dev.Model + "...";
                        string msg = await Task.Run(delegate { return OmegaCrossAppBridge.PushFileToSinglePixel(dev.Serial, dev.Model, targetItem.FilePath); });
                        if (_dlSummaryLabel != null) _dlSummaryLabel.Text = msg;
                        _lblStatusL.Text = msg;
                    });
                }
            }
            cms.Show(btn, new Point(0, btn.Height));
        }

        private void RefreshDownloadStudioGrid()
        {
            if (_dlStudioForm == null || _dlStudioForm.IsDisposed || _dlListView == null) return;
            if (_dlVaultHdrLabel != null) _dlVaultHdrLabel.Text = OmegaDownloadRouter.GetVaultDriveSummary(_labRoot);

            _dlListView.BeginUpdate();
            try
            {
                while (_dlListView.Items.Count > _downloads.Count)
                    _dlListView.Items.RemoveAt(_dlListView.Items.Count - 1);

                for (int i = 0; i < _downloads.Count; i++)
                {
                    DownloadTrackItem d = _downloads[i];
                    string pctTxt = d.ProgressPercent + "%";
                    string sizeTxt = d.TotalBytes > 0L
                        ? (OmegaDownloadRouter.FormatBytes(d.BytesReceived) + " / " + OmegaDownloadRouter.FormatBytes(d.TotalBytes))
                        : OmegaDownloadRouter.FormatBytes(d.BytesReceived);
                    string speedTxt = (!d.Done && !d.Paused && !d.Canceled)
                        ? OmegaDownloadRouter.FormatSpeedAndEta(d.SpeedBps, d.BytesReceived, d.TotalBytes)
                        : "--";
                    string shaCorpus = !string.IsNullOrEmpty(d.Sha256)
                        ? (d.Sha256.Substring(0, Math.Min(12, d.Sha256.Length)) + (string.IsNullOrEmpty(d.CorpusStatus) ? "" : (" | " + d.CorpusStatus)))
                        : (d.CorpusStatus ?? "");

                    Color rowColor = d.Canceled ? Color.FromArgb(248, 113, 113)
                        : (d.Done ? Color.FromArgb(52, 211, 153)
                        : (d.Paused ? Color.FromArgb(251, 191, 36) : Color.FromArgb(125, 211, 252)));

                    if (i < _dlListView.Items.Count)
                    {
                        ListViewItem lvi = _dlListView.Items[i];
                        lvi.Tag = d;
                        if (lvi.SubItems[0].Text != d.FileName) lvi.SubItems[0].Text = d.FileName;
                        if (lvi.SubItems[1].Text != d.Category) lvi.SubItems[1].Text = d.Category;
                        if (lvi.SubItems[2].Text != d.EngineKind) lvi.SubItems[2].Text = d.EngineKind;
                        if (lvi.SubItems[3].Text != d.StateText) lvi.SubItems[3].Text = d.StateText;
                        if (lvi.SubItems[4].Text != pctTxt) lvi.SubItems[4].Text = pctTxt;
                        if (lvi.SubItems[5].Text != sizeTxt) lvi.SubItems[5].Text = sizeTxt;
                        if (lvi.SubItems[6].Text != speedTxt) lvi.SubItems[6].Text = speedTxt;
                        if (lvi.SubItems[7].Text != shaCorpus) lvi.SubItems[7].Text = shaCorpus;
                        if (lvi.ForeColor != rowColor) lvi.ForeColor = rowColor;
                    }
                    else
                    {
                        ListViewItem lvi = new ListViewItem(new string[]
                        {
                            d.FileName, d.Category, d.EngineKind, d.StateText, pctTxt, sizeTxt, speedTxt, shaCorpus
                        });
                        lvi.Tag = d;
                        lvi.ForeColor = rowColor;
                        _dlListView.Items.Add(lvi);
                    }
                }
            }
            finally
            {
                _dlListView.EndUpdate();
            }
        }

        private static void EnableListViewDoubleBuffer(ListView lv)
        {
            try
            {
                PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(lv, true, null);
            }
            catch { }
        }
    }
}
