using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace OmegaBrowserV03
{
    public sealed partial class BrowserForm
    {
        private Form _dlStudioForm;
        private ListView _dlListView;
        private Label _dlVaultHdrLabel;
        private Label _dlSummaryLabel;
        private TextBox _dlDirectUrlBox;
        private CheckBox _dlDirectDistillChk;
        private NumericUpDown _dlMaxConcurrencyBox;
        private System.Windows.Forms.Timer _dlFastUiTimer;
        private int _maxConcurrentRangeJobs = 3;
        private string _dlHistoryLedgerPath = "";

        private void InitDownloadStudioSubsystem()
        {
            OmegaDownloadRouter.EnsureVaultDirectories(_labRoot);
            _dlHistoryLedgerPath = Path.Combine(_labRoot, "vault", "downloads_v06.tsv");
            LoadDownloadsHistoryAndPartials();
        }

        private void LoadDownloadsHistoryAndPartials()
        {
            HashSet<string> knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_dlHistoryLedgerPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(_dlHistoryLedgerPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] p = lines[i].Split('\t');
                        if (p.Length < 8) continue;
                        string fPath = p[2];
                        if (string.IsNullOrEmpty(fPath) || !File.Exists(fPath)) continue;
                        if (!knownPaths.Add(fPath)) continue;

                        DownloadTrackItem it = new DownloadTrackItem();
                        it.Id = p[0];
                        it.EngineKind = p[1];
                        it.FilePath = fPath;
                        it.FileName = Path.GetFileName(fPath);
                        it.Category = p[3];
                        long sz;
                        if (long.TryParse(p[4], out sz)) { it.BytesReceived = sz; it.TotalBytes = sz; }
                        it.Sha256 = p[5];
                        it.SourceUrl = p[6];
                        it.CorpusStatus = p[7];
                        it.StateText = "Completed";
                        it.Done = true;
                        it.PostProcessed = true;
                        _downloads.Add(it);
                    }
                }
                catch { }
            }

            try
            {
                string dlRoot = OmegaDownloadRouter.ResolveDownloadsRoot(_labRoot);
                if (Directory.Exists(dlRoot))
                {
                    string[] metas = Directory.GetFiles(dlRoot, "*.part.meta", SearchOption.AllDirectories);
                    for (int m = 0; m < metas.Length; m++)
                    {
                        string metaFile = metas[m];
                        string destPath = metaFile.Substring(0, metaFile.Length - ".part.meta".Length);
                        if (!knownPaths.Add(destPath)) continue;
                        string[] ml = File.ReadAllLines(metaFile, Encoding.UTF8);
                        if (ml.Length < 1 || string.IsNullOrWhiteSpace(ml[0])) continue;

                        long tot = 0L;
                        if (ml.Length >= 2) long.TryParse(ml[1], out tot);
                        string etag = ml.Length >= 3 ? ml[2] : "";
                        bool autoDistill = ml.Length >= 4 && ml[3] == "1";

                        HttpRangeDownloadJob job = new HttpRangeDownloadJob(ml[0].Trim(), destPath, autoDistill, tot, etag);
                        DownloadTrackItem partItem = new DownloadTrackItem();
                        partItem.EngineKind = "HTTP-206";
                        partItem.RangeJob = job;
                        partItem.SourceUrl = job.Url;
                        partItem.FilePath = destPath;
                        partItem.FileName = Path.GetFileName(destPath);
                        partItem.Category = OmegaDownloadRouter.ClassifyCategory(partItem.FileName, partItem.SourceUrl);
                        partItem.BytesReceived = job.BytesReceived;
                        partItem.TotalBytes = job.TotalBytes;
                        partItem.AutoDistillPdf = autoDistill;
                        partItem.Paused = true;
                        partItem.StateText = "Paused (.part Ready)";
                        AttachShelfChip(partItem, false);
                        _downloads.Insert(0, partItem);
                    }
                }
            }
            catch { }
        }

        private void SaveDownloadsLedger()
        {
            try
            {
                List<string> lines = new List<string>();
                int saved = 0;
                for (int i = 0; i < _downloads.Count && saved < 250; i++)
                {
                    DownloadTrackItem it = _downloads[i];
                    if (!it.Done || it.Canceled || !File.Exists(it.FilePath)) continue;
                    lines.Add(
                        (it.Id ?? "") + "\t" +
                        (it.EngineKind ?? "HTTP-206") + "\t" +
                        (it.FilePath ?? "").Replace("\t", " ") + "\t" +
                        (it.Category ?? "Files") + "\t" +
                        it.BytesReceived + "\t" +
                        (it.Sha256 ?? "") + "\t" +
                        (it.SourceUrl ?? "").Replace("\t", " ") + "\t" +
                        (it.CorpusStatus ?? "").Replace("\t", " ")
                    );
                    saved++;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_dlHistoryLedgerPath));
                File.WriteAllLines(_dlHistoryLedgerPath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        private void AttachShelfChip(DownloadTrackItem item, bool showShelfNow)
        {
            Button chip = AutoBtn("[" + item.Category + "] " + Shorten(item.FileName, 20), Color.FromArgb(14, 116, 144));
            item.Chip = chip;
            chip.MouseUp += delegate(object s1, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Right)
                {
                    ShowDownloadItemContextMenu(item, chip, me.Location);
                }
                else
                {
                    if (item.Done && File.Exists(item.FilePath))
                        Process.Start("explorer.exe", "/select,\"" + item.FilePath + "\"");
                    else
                        OpenDownloadStudio();
                }
            };
            if (showShelfNow)
            {
                _dlShelf.Visible = !_zenMode;
                _dlFlow.Controls.Add(chip);
                _dlFlow.ScrollControlIntoView(chip);
            }
        }

        private void ShowDownloadItemContextMenu(DownloadTrackItem it, Control anchor, Point pt)
        {
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Items.Add("Open Download Studio (Ctrl+J)", null, delegate { OpenDownloadStudio(); });
            if (!it.Done && !it.Canceled)
            {
                cms.Items.Add("Pause Transfer", null, delegate { PauseDownloadItem(it); });
                cms.Items.Add("Resume Transfer (HTTP 206 / WebView2)", null, delegate { ResumeDownloadItem(it); });
                cms.Items.Add("Cancel Transfer", null, delegate { CancelDownloadItem(it); });
            }
            if (it.Done && File.Exists(it.FilePath))
            {
                cms.Items.Add("Show in Explorer (" + it.Category + ")", null, delegate { Process.Start("explorer.exe", "/select,\"" + it.FilePath + "\""); });
                cms.Items.Add("Send to OmegaSynth v1.1.1 Final GM (Geometric Shape & Resynth)", null, delegate
                {
                    string msg = OmegaCrossAppBridge.SendFileToOmegaSynth(_labRoot, it.FilePath);
                    _lblStatusL.Text = msg;
                });
                cms.Items.Add("Send to OmegaAV v0.7.1 (.wav / .omsh)", null, delegate
                {
                    string msg = OmegaCrossAppBridge.SendFileToOmegaAv(_labRoot, it.FilePath);
                    _lblStatusL.Text = msg;
                });
                cms.Items.Add("Broadcast Push -> Connected Pixels (/sdcard/Download)", null, async delegate
                {
                    _lblStatusL.Text = "ADB Pushing " + it.FileName + " -> Pixels...";
                    string res = await Task.Run(delegate { return OmegaCrossAppBridge.BroadcastFileToAllPixels(it.FilePath); });
                    _lblStatusL.Text = res;
                });
                if (it.IsPdf)
                {
                    cms.Items.Add("Distill PDF -> Sovereign +Corpus", null, async delegate { await DistillDownloadedPdfAsync(it); });
                }
                if (!string.IsNullOrEmpty(it.Sha256))
                {
                    cms.Items.Add("Copy SHA-256 (" + it.Sha256.Substring(0, 12) + "...)", null, delegate
                    {
                        Clipboard.SetDataObject(it.Sha256, true, 4, 40);
                        _lblStatusL.Text = "Copied SHA-256: " + it.Sha256;
                    });
                }
            }
            cms.Show(anchor, pt);
        }

        private void HandleWebViewDownloadStartingV06(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            e.Handled = true;
            CoreWebView2DownloadOperation op = e.DownloadOperation;
            string origName = Path.GetFileName(e.ResultFilePath);
            string srcUrl = "";
            try { srcUrl = op.Uri ?? ""; } catch { }

            string cat;
            string dest = OmegaDownloadRouter.AllocateDestPath(_labRoot, origName, srcUrl, out cat);
            e.ResultFilePath = dest;

            DownloadTrackItem item = new DownloadTrackItem();
            item.EngineKind = "WebView2";
            item.Op = op;
            item.SourceUrl = srcUrl;
            item.FilePath = dest;
            item.FileName = Path.GetFileName(dest);
            item.Category = cat;
            item.StateText = "Downloading";

            BeginInvoke((Action)delegate
            {
                AttachShelfChip(item, true);
                _downloads.Insert(0, item);
                UpdateDownloadItemV06(item);
                RefreshDownloadStudioGrid();
            });

            op.BytesReceivedChanged += delegate { BeginInvoke((Action)delegate { UpdateDownloadItemV06(item); }); };
            op.StateChanged += delegate { BeginInvoke((Action)delegate { UpdateDownloadItemV06(item); }); };
        }

        public DownloadTrackItem EnqueueHttpRangeDownload(string rawUrl, string suggestedName, bool autoDistillPdf, bool autoSendAv)
        {
            string cleanUrl = (rawUrl ?? "").Trim();
            if (string.IsNullOrEmpty(cleanUrl)) return null;

            string cat;
            string dest = OmegaDownloadRouter.AllocateDestPath(_labRoot, suggestedName, cleanUrl, out cat);
            HttpRangeDownloadJob job = new HttpRangeDownloadJob(cleanUrl, dest, autoDistillPdf, 0L, "");

            DownloadTrackItem item = new DownloadTrackItem();
            item.EngineKind = "HTTP-206";
            item.RangeJob = job;
            item.SourceUrl = cleanUrl;
            item.FilePath = dest;
            item.FileName = Path.GetFileName(dest);
            item.Category = cat;
            item.AutoDistillPdf = autoDistillPdf;
            item.AutoSendOmegaAv = autoSendAv;
            item.QueuedForSlot = true;
            item.StateText = "Queued";
            job.WriteMetaSidecar();

            AttachShelfChip(item, true);
            _downloads.Insert(0, item);
            PumpRangeDownloadQueue();
            RefreshDownloadStudioGrid();
            return item;
        }

        private void PumpRangeDownloadQueue()
        {
            int activeCount = 0;
            for (int i = 0; i < _downloads.Count; i++)
            {
                DownloadTrackItem d = _downloads[i];
                if (d.RangeJob != null && d.RangeJob.IsRunning) activeCount++;
            }

            for (int i = _downloads.Count - 1; i >= 0 && activeCount < _maxConcurrentRangeJobs; i--)
            {
                DownloadTrackItem cand = _downloads[i];
                if (cand.RangeJob != null && cand.QueuedForSlot && !cand.Done && !cand.Canceled && !cand.Paused && !cand.RangeJob.IsRunning)
                {
                    cand.QueuedForSlot = false;
                    cand.StateText = "Connecting (206)";
                    cand.LastSampleUtc = DateTime.UtcNow;
                    cand.LastSampleBytes = cand.RangeJob.BytesReceived;
                    activeCount++;
                    DownloadTrackItem cap = cand;
                    cand.RangeJob.StartOrResume(delegate
                    {
                        try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate { UpdateDownloadItemV06(cap); PumpRangeDownloadQueue(); }); }
                        catch { }
                    });
                }
            }
        }

        private void PauseDownloadItem(DownloadTrackItem it)
        {
            if (it == null || it.Done || it.Canceled) return;
            it.Paused = true;
            it.QueuedForSlot = false;
            it.SpeedBps = 0.0;
            if (it.RangeJob != null)
            {
                it.RangeJob.Pause();
                it.StateText = "Paused (206 Ready)";
            }
            else if (it.Op != null)
            {
                try { it.Op.Pause(); it.StateText = "Paused (WebView2)"; }
                catch (Exception ex) { it.StateText = "Pause Err: " + ex.Message; }
            }
            UpdateDownloadItemV06(it);
            PumpRangeDownloadQueue();
            RefreshDownloadStudioGrid();
        }

        private void ResumeDownloadItem(DownloadTrackItem it)
        {
            if (it == null || it.Done || it.Canceled) return;
            it.Paused = false;
            it.LastSampleUtc = DateTime.UtcNow;
            if (it.RangeJob != null)
            {
                it.LastSampleBytes = it.RangeJob.BytesReceived;
                it.QueuedForSlot = true;
                it.StateText = "Resuming (HTTP 206)";
                PumpRangeDownloadQueue();
            }
            else if (it.Op != null)
            {
                try
                {
                    if (it.Op.CanResume)
                    {
                        it.Op.Resume();
                        it.StateText = "Downloading";
                    }
                    else if (!string.IsNullOrEmpty(it.SourceUrl))
                    {
                        try { it.Op.Cancel(); } catch { }
                        it.Op = null;
                        it.EngineKind = "HTTP-206";
                        it.RangeJob = new HttpRangeDownloadJob(it.SourceUrl, it.FilePath, it.AutoDistillPdf, it.TotalBytes, "");
                        it.QueuedForSlot = true;
                        it.StateText = "Fallback -> HTTP 206";
                        PumpRangeDownloadQueue();
                    }
                }
                catch { }
            }
            UpdateDownloadItemV06(it);
            RefreshDownloadStudioGrid();
        }

        private void CancelDownloadItem(DownloadTrackItem it)
        {
            if (it == null || it.Done) return;
            it.Canceled = true;
            it.Paused = false;
            it.QueuedForSlot = false;
            it.Done = true;
            it.SpeedBps = 0.0;
            it.StateText = "Canceled";
            if (it.RangeJob != null) it.RangeJob.Cancel();
            if (it.Op != null) { try { it.Op.Cancel(); } catch { } }
            if (it.Chip != null)
            {
                it.Chip.Text = "[Canceled] " + Shorten(it.FileName, 18);
                it.Chip.BackColor = Color.FromArgb(127, 29, 29);
            }
            PumpRangeDownloadQueue();
            RefreshDownloadStudioGrid();
        }

        private void TickDownloadsHousekeeping()
        {
            ulong aggRecv = 0UL;
            ulong aggTot = 0UL;
            int activeRunning = 0;

            for (int i = 0; i < _downloads.Count; i++)
            {
                DownloadTrackItem it = _downloads[i];
                if (!it.Done) UpdateDownloadItemV06(it);
                if (!it.Done && !it.Paused && !it.Canceled && !it.QueuedForSlot)
                {
                    activeRunning++;
                    if (it.TotalBytes > 0L)
                    {
                        aggRecv += (ulong)Math.Max(0L, it.BytesReceived);
                        aggTot += (ulong)Math.Max(1L, it.TotalBytes);
                    }
                }
            }

            if (activeRunning > 0 && aggTot > 0UL) WinTaskbarProgress.SetProgress(Handle, aggRecv, aggTot);
            else WinTaskbarProgress.Clear(Handle);

            PumpRangeDownloadQueue();
            RefreshDownloadStudioGrid();
        }

        private void UpdateDownloadItemV06(DownloadTrackItem it)
        {
            if (it == null) return;

            if (it.RangeJob != null)
            {
                it.BytesReceived = it.RangeJob.BytesReceived;
                it.TotalBytes = it.RangeJob.TotalBytes;

                if (it.RangeJob.IsCanceled)
                {
                    it.Canceled = true; it.Done = true; it.SpeedBps = 0.0; it.StateText = "Canceled";
                }
                else if (it.RangeJob.IsCompleted || (!it.RangeJob.IsRunning && File.Exists(it.FilePath) && !File.Exists(it.RangeJob.PartPath) && it.BytesReceived > 0L))
                {
                    it.Done = true; it.Paused = false; it.SpeedBps = 0.0; it.StateText = "Completed";
                }
                else if (it.RangeJob.IsRunning)
                {
                    UpdateSlidingSpeed(it);
                    it.StateText = it.RangeJob.SupportsRange ? "HTTP 206 Active" : "HTTP 200 Stream";
                }
                else if (it.Paused || it.RangeJob.IsPaused)
                {
                    it.SpeedBps = 0.0;
                    it.StateText = "Paused (.part)";
                }
                else if (!string.IsNullOrEmpty(it.RangeJob.LastError))
                {
                    it.SpeedBps = 0.0;
                    it.Paused = true;
                    it.StateText = "Retryable: " + Shorten(it.RangeJob.LastError, 26);
                }
            }
            else if (it.Op != null && !it.Done)
            {
                long recv = Math.Max(0L, Convert.ToInt64(it.Op.BytesReceived));
                long tot = it.Op.TotalBytesToReceive.HasValue ? (long)it.Op.TotalBytesToReceive.Value : 0L;
                it.BytesReceived = recv;
                it.TotalBytes = tot;

                if (it.Op.State == CoreWebView2DownloadState.Completed || (tot > 0L && recv >= tot && File.Exists(it.FilePath)))
                {
                    it.Done = true; it.Paused = false; it.SpeedBps = 0.0; it.StateText = "Completed";
                }
                else if (it.Op.State == CoreWebView2DownloadState.Interrupted)
                {
                    it.SpeedBps = 0.0;
                    if (it.Paused || it.Op.CanResume)
                    {
                        it.Paused = true;
                        it.StateText = "Paused (WebView2)";
                    }
                    else
                    {
                        it.StateText = "Interrupted (" + it.Op.InterruptReason + ")";
                    }
                }
                else
                {
                    UpdateSlidingSpeed(it);
                    it.StateText = "Downloading";
                }
            }

            if (it.Chip != null)
            {
                if (it.Canceled)
                {
                    it.Chip.Text = "[Canceled] " + Shorten(it.FileName, 18);
                    it.Chip.BackColor = Color.FromArgb(127, 29, 29);
                }
                else if (it.Done)
                {
                    string sh8 = !string.IsNullOrEmpty(it.Sha256) && it.Sha256.Length >= 8 ? it.Sha256.Substring(0, 8) : "hashing";
                    it.Chip.Text = "[" + it.Category + "] " + Shorten(it.FileName, 18) + " | SHA:" + sh8;
                    it.Chip.BackColor = Color.FromArgb(6, 95, 70);
                }
                else if (it.Paused)
                {
                    it.Chip.Text = "[PAUSED " + it.ProgressPercent + "%] " + Shorten(it.FileName, 18);
                    it.Chip.BackColor = Color.FromArgb(146, 64, 14);
                }
                else
                {
                    string sp = it.SpeedBps > 1024.0 ? (" " + OmegaDownloadRouter.FormatBytes((long)it.SpeedBps) + "/s") : "";
                    it.Chip.Text = "[DL " + it.ProgressPercent + "%" + sp + "] " + Shorten(it.FileName, 18);
                    it.Chip.BackColor = Color.FromArgb(14, 116, 144);
                }
            }

            if (it.Done && !it.Canceled && !it.PostProcessed && File.Exists(it.FilePath))
            {
                it.PostProcessed = true;
                Task ignored = FinalizeCompletedDownloadAsync(it);
            }
        }

        private static void UpdateSlidingSpeed(DownloadTrackItem it)
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - it.LastSampleUtc).TotalSeconds;
            if (dt >= 0.35)
            {
                long delta = it.BytesReceived - it.LastSampleBytes;
                if (delta >= 0L)
                {
                    double inst = delta / dt;
                    it.SpeedBps = it.SpeedBps <= 1.0 ? inst : (it.SpeedBps * 0.62 + inst * 0.38);
                }
                it.LastSampleUtc = now;
                it.LastSampleBytes = it.BytesReceived;
            }
        }

        private async Task FinalizeCompletedDownloadAsync(DownloadTrackItem it)
        {
            string path = it.FilePath;
            string sha = await Task.Run(delegate { return OmegaVault.ComputeSha256File(path); });
            it.Sha256 = sha ?? "";
            if (File.Exists(path))
            {
                long len = new FileInfo(path).Length;
                it.BytesReceived = len;
                it.TotalBytes = len;
            }
            string sh8 = it.Sha256.Length >= 8 ? it.Sha256.Substring(0, 8) : "done";
            if (it.Chip != null)
                it.Chip.Text = "[" + it.Category + "] " + Shorten(it.FileName, 18) + " | SHA:" + sh8;

            _lblStatusL.Text = "Vault Saved -> " + it.FilePath + " | SHA-256: " + sh8 + "...";

            if (it.AutoDistillPdf && it.IsPdf)
                await DistillDownloadedPdfAsync(it);

            if (it.AutoSendOmegaAv && it.IsOmegaAvCompatible)
            {
                string avMsg = OmegaCrossAppBridge.SendFileToOmegaAv(_labRoot, it.FilePath);
                _lblStatusL.Text = avMsg;
            }

            SaveDownloadsLedger();
            RefreshDownloadStudioGrid();
        }

        private async Task DistillDownloadedPdfAsync(DownloadTrackItem it)
        {
            if (it == null || !File.Exists(it.FilePath)) return;
            it.CorpusStatus = "Distilling PDF...";
            RefreshDownloadStudioGrid();

            string fileUri = new Uri(it.FilePath).AbsoluteUri;
            string srcUrl = string.IsNullOrEmpty(it.SourceUrl) ? fileUri : it.SourceUrl;
            string fName = it.FileName;

            CorpusSaveResult cs = await Task.Run(delegate
            {
                string pdfSha;
                string md = NativePdfExtractor.ExtractPdfMarkdown(fileUri, "full", out pdfSha);
                ReceiptRecord rec = OmegaVault.MintReceiptAndSaveHtml(_labRoot, fName + " [Vault PDF]", srcUrl, md, md, 0, Interlocked.Read(ref _totalBlocked));
                return OmegaVault.SaveToCorpus(_labRoot, rec, "vault_pdf_v06,auto_distill");
            });

            it.CorpusStatus = cs.Duplicate ? "+Corpus (Dedup)" : (cs.Saved ? "+Corpus Saved" : "PDF Scanned");
            _lblStatusL.Text = "[Vault PDF -> Corpus] " + it.FileName + " :: " + it.CorpusStatus;
            SaveDownloadsLedger();
            RefreshDownloadStudioGrid();
        }
    }
}
