using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaShellStudio
{
    public sealed partial class OmegaShellForm
    {
        private FlowLayoutPanel _flpDrivesBar;
        private TextBox _txtExplorerPath, _txtFilePreview, _txtSnapTag;
        private DataGridView _gridFiles, _gridCasAndSnaps;
        private string _explorerDir = "";

        private void BuildTab2ExplorerAndCloudUi(TabPage page)
        {
            page.BackColor = Color.FromArgb(8, 12, 21);

            _flpDrivesBar = new FlowLayoutPanel();
            _flpDrivesBar.Dock = DockStyle.Top;
            _flpDrivesBar.AutoSize = true;
            _flpDrivesBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _flpDrivesBar.MinimumSize = new Size(0, 36);
            _flpDrivesBar.Padding = new Padding(8, 5, 8, 4);
            _flpDrivesBar.WrapContents = true;
            _flpDrivesBar.BackColor = Color.FromArgb(14, 21, 38);

            Panel navBar = new Panel();
            navBar.Dock = DockStyle.Top;
            navBar.Height = 38;
            navBar.Padding = new Padding(8, 5, 8, 5);
            navBar.BackColor = Color.FromArgb(11, 17, 30);

            Button bUp = MakeBtn("Up ..", Color.FromArgb(51, 65, 85));
            bUp.Dock = DockStyle.Left;
            bUp.Click += delegate
            {
                if (!string.IsNullOrEmpty(_explorerDir))
                {
                    DirectoryInfo p = Directory.GetParent(_explorerDir.TrimEnd('\\'));
                    if (p != null) NavigateExplorerDirectory(p.FullName);
                }
            };

            FlowLayoutPanel navRight = new FlowLayoutPanel();
            navRight.Dock = DockStyle.Right;
            navRight.AutoSize = true;
            navRight.WrapContents = false;

            Button bGo = MakeBtn("Open", Color.FromArgb(2, 132, 199));
            bGo.Click += delegate { NavigateExplorerDirectory(MultiDrivePathEngine.ResolvePath(_explorerDir, _txtExplorerPath.Text, _shellRoot)); };

            Button bEditSel = MakeBtn("Open in Code Studio", Color.FromArgb(180, 83, 9));
            bEditSel.Click += delegate
            {
                if (_gridFiles.SelectedRows.Count > 0 && _gridFiles.Columns.Contains("path"))
                {
                    string fp = (_gridFiles.SelectedRows[0].Cells["path"].Value ?? "").ToString();
                    if (File.Exists(fp)) OpenFileInCodeStudio(fp);
                    else SetSingleLineStatus("Select a file row first to open in Tab 2 Code Studio.");
                }
            };

            Button bFmtSel = MakeBtn("Auto-Fmt", Color.FromArgb(109, 40, 217));
            bFmtSel.Click += async delegate
            {
                string target = _explorerDir;
                if (_gridFiles.SelectedRows.Count > 0 && _gridFiles.Columns.Contains("path"))
                    target = (_gridFiles.SelectedRows[0].Cells["path"].Value ?? _explorerDir).ToString();
                bool zbt = _settings.EnforceZeroBacktickPs1;
                List<FormatResultItem> items = await Task.Run(delegate { return CodeFormatterAndCompiler.FormatPath(target, false, zbt); });
                if (File.Exists(target)) PreviewExplorerFile(target);
                SetSingleLineStatus("Auto-formatted " + items.Count + " item(s) at " + target);
            };

            Button bCdShell = MakeBtn("cd in OSH", Color.FromArgb(5, 150, 105));
            bCdShell.Click += async delegate
            {
                await RunCommandFromUiAsync("cd \"" + _explorerDir + "\"", false);
                _mainTabs.SelectedIndex = 0;
            };

            Button bPinCas = MakeBtn("Pin -> CAS", Color.FromArgb(88, 28, 135));
            bPinCas.Click += async delegate { await PinSelectedExplorerItemToCasAsync(); };

            Button bBeamDeb = MakeBtn("Sync -> @deb", Color.FromArgb(15, 118, 110));
            bBeamDeb.Click += async delegate
            {
                string target = _explorerDir;
                if (_gridFiles.SelectedRows.Count > 0 && _gridFiles.Columns.Contains("path"))
                    target = (_gridFiles.SelectedRows[0].Cells["path"].Value ?? _explorerDir).ToString();
                string msg = await Task.Run(delegate { return AndroidAvfDebianBridge.SyncLocalPathToDebianShared(target, ""); });
                SetSingleLineStatus(msg);
            };

            Button bWinExp = MakeBtn("Explorer", Color.FromArgb(51, 65, 85));
            bWinExp.Click += delegate { if (Directory.Exists(_explorerDir)) Process.Start("explorer.exe", "\"" + _explorerDir + "\""); };

            navRight.Controls.AddRange(new Control[] { bGo, bEditSel, bFmtSel, bCdShell, bPinCas, bBeamDeb, bWinExp });

            _txtExplorerPath = new TextBox();
            _txtExplorerPath.Dock = DockStyle.Fill;
            _txtExplorerPath.BackColor = Color.FromArgb(6, 9, 17);
            _txtExplorerPath.ForeColor = Color.White;
            _txtExplorerPath.Font = new Font("Consolas", 9.4f);
            _txtExplorerPath.BorderStyle = BorderStyle.FixedSingle;
            _txtExplorerPath.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    NavigateExplorerDirectory(MultiDrivePathEngine.ResolvePath(_explorerDir, _txtExplorerPath.Text, _shellRoot));
                }
            };

            navBar.Controls.Add(_txtExplorerPath);
            navBar.Controls.Add(bUp);
            navBar.Controls.Add(navRight);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Color.FromArgb(30, 41, 59);
            split.SplitterWidth = 5;

            _gridFiles = MakeStyledGrid();
            AttachPipeContextMenu(_gridFiles, delegate { return "ls \"" + _explorerDir + "\""; });
            _gridFiles.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs ce)
            {
                if (ce.RowIndex < 0 || ce.RowIndex >= _gridFiles.Rows.Count) return;
                string kind = (_gridFiles.Rows[ce.RowIndex].Cells["kind"].Value ?? "").ToString();
                string fullPath = (_gridFiles.Rows[ce.RowIndex].Cells["path"].Value ?? "").ToString();
                if ((kind == "DIR" || kind == "JUNCTION") && Directory.Exists(fullPath))
                    NavigateExplorerDirectory(fullPath);
                else if (File.Exists(fullPath))
                    OpenFileInCodeStudio(fullPath);
            };
            _gridFiles.SelectionChanged += delegate
            {
                if (_gridFiles.SelectedRows.Count > 0 && _gridFiles.Columns.Contains("path"))
                {
                    string fp = (_gridFiles.SelectedRows[0].Cells["path"].Value ?? "").ToString();
                    if (File.Exists(fp)) PreviewExplorerFile(fp);
                    else if (Directory.Exists(fp)) PreviewExplorerDirectorySummary(fp);
                }
            };

            SplitContainer rightVert = new SplitContainer();
            rightVert.Dock = DockStyle.Fill;
            rightVert.Orientation = Orientation.Horizontal;
            rightVert.BackColor = Color.FromArgb(30, 41, 59);
            rightVert.SplitterWidth = 5;

            Panel prevPanel = new Panel();
            prevPanel.Dock = DockStyle.Fill;
            prevPanel.BackColor = Color.FromArgb(7, 11, 19);

            Label lPrev = MakeLbl(
                "LIVE FILE & DIRECTORY SHA-256 INSPECTOR (Double-Click File -> Open in Code Studio):",
                new Font("Segoe UI", 8.4f, FontStyle.Bold),
                Color.FromArgb(52, 211, 153));
            lPrev.AutoSize = false;
            lPrev.Dock = DockStyle.Top;
            lPrev.Height = 24;
            lPrev.Padding = new Padding(6, 4, 0, 0);
            lPrev.BackColor = Color.FromArgb(14, 21, 38);

            _txtFilePreview = new TextBox();
            _txtFilePreview.Dock = DockStyle.Fill;
            _txtFilePreview.Multiline = true;
            _txtFilePreview.ReadOnly = true;
            _txtFilePreview.ScrollBars = ScrollBars.Vertical;
            _txtFilePreview.BackColor = Color.FromArgb(6, 9, 17);
            _txtFilePreview.ForeColor = Color.FromArgb(226, 232, 240);
            _txtFilePreview.Font = new Font("Consolas", 8.8f);
            _txtFilePreview.BorderStyle = BorderStyle.None;

            Panel prevPad = new Panel();
            prevPad.Dock = DockStyle.Fill;
            prevPad.Padding = new Padding(8, 6, 2, 4);
            prevPad.BackColor = Color.FromArgb(6, 9, 17);
            prevPad.Controls.Add(_txtFilePreview);

            prevPanel.Controls.Add(prevPad);
            prevPanel.Controls.Add(lPrev);

            Panel cloudPanel = new Panel();
            cloudPanel.Dock = DockStyle.Fill;
            cloudPanel.BackColor = Color.FromArgb(9, 13, 22);

            Label lCasHdr = MakeLbl(
                "LOCAL CLOUD SHA-256 CAS VAULT & TIME-TRAVEL FOLDER SNAPSHOTS (Mirrors -> F:\\ & G:\\):",
                new Font("Segoe UI", 8.3f, FontStyle.Bold),
                Color.FromArgb(56, 189, 248));
            lCasHdr.AutoSize = false;
            lCasHdr.Dock = DockStyle.Top;
            lCasHdr.Height = 23;
            lCasHdr.Padding = new Padding(6, 3, 0, 0);
            lCasHdr.BackColor = Color.FromArgb(12, 19, 34);

            FlowLayoutPanel cloudBar = new FlowLayoutPanel();
            cloudBar.Dock = DockStyle.Top;
            cloudBar.AutoSize = true;
            cloudBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            cloudBar.MinimumSize = new Size(0, 35);
            cloudBar.Padding = new Padding(6, 4, 6, 4);
            cloudBar.WrapContents = true;
            cloudBar.BackColor = Color.FromArgb(14, 21, 38);

            _txtSnapTag = new TextBox();
            _txtSnapTag.Width = 105;
            _txtSnapTag.Text = "snap_" + DateTime.Now.ToString("HHmm");
            _txtSnapTag.BackColor = Color.FromArgb(6, 9, 17);
            _txtSnapTag.ForeColor = Color.White;
            _txtSnapTag.Font = new Font("Consolas", 8.6f);

            Button bSnapSave = MakeBtn("Snapshot Folder", Color.FromArgb(5, 150, 105));
            bSnapSave.Click += async delegate
            {
                string tag = _txtSnapTag.Text.Trim();
                string dir = _explorerDir;
                string msg = await Task.Run(delegate { return _runtime.Cloud.CreateSnapshot(tag, dir); });
                SetSingleLineStatus(msg);
                RefreshSnapshotGrid();
            };

            Button bViewSnaps = MakeBtn("Snapshots", Color.FromArgb(30, 58, 138));
            bViewSnaps.Click += delegate { RefreshSnapshotGrid(); };

            Button bViewCas = MakeBtn("CAS Blobs", Color.FromArgb(109, 40, 217));
            bViewCas.Click += delegate { RefreshCasAndSnapshotLists(); };

            Button bRestoreSnap = MakeBtn("Restore Snap", Color.FromArgb(180, 83, 9));
            bRestoreSnap.Click += async delegate
            {
                if (_gridCasAndSnaps.SelectedRows.Count > 0 && _gridCasAndSnaps.Columns.Contains("tag"))
                {
                    string tag = (_gridCasAndSnaps.SelectedRows[0].Cells["tag"].Value ?? "").ToString();
                    if (tag.StartsWith("(")) return;
                    string msg = await Task.Run(delegate { return _runtime.Cloud.RestoreSnapshot(tag, ""); });
                    SetSingleLineStatus(msg);
                    NavigateExplorerDirectory(_explorerDir);
                }
            };

            Button bSyncBridge = MakeBtn("Sync -> F:\\ & G:\\", Color.FromArgb(15, 118, 110));
            bSyncBridge.Click += async delegate
            {
                string msg = await Task.Run(delegate { return _runtime.Cloud.SyncCasToBridgeAndColdTier(); });
                SetSingleLineStatus(msg);
            };

            cloudBar.Controls.AddRange(new Control[] { _txtSnapTag, bSnapSave, bViewSnaps, bViewCas, bRestoreSnap, bSyncBridge });

            _gridCasAndSnaps = MakeStyledGrid();
            AttachPipeContextMenu(_gridCasAndSnaps, delegate { return _gridCasAndSnaps.Columns.Contains("tag") ? "snap ls" : "cas ls"; });
            cloudPanel.Controls.Add(_gridCasAndSnaps);
            cloudPanel.Controls.Add(cloudBar);
            cloudPanel.Controls.Add(lCasHdr);

            rightVert.Panel1.Controls.Add(prevPanel);
            rightVert.Panel2.Controls.Add(cloudPanel);

            split.Panel1.Controls.Add(_gridFiles);
            split.Panel2.Controls.Add(rightVert);

            page.Controls.Add(split);
            page.Controls.Add(navBar);
            page.Controls.Add(_flpDrivesBar);

            ApplySplitterOnTabVisible(page, delegate
            {
                split.SplitterDistance = Math.Max(640, (split.Width * 54) / 100);
                rightVert.SplitterDistance = Math.Max(260, (rightVert.Height * 50) / 100);
            });
        }

        private void RefreshDriveExplorerButtons()
        {
            _flpDrivesBar.Controls.Clear();
            List<OshRow> drives = MultiDrivePathEngine.ListAllDrivesRows();
            for (int i = 0; i < drives.Count; i++)
            {
                string drv = drives[i].GetString("drive");
                string free = drives[i].GetString("free_gb");
                string status = drives[i].GetString("status");
                bool isReady = (status == "READY" || status == "HIGH_LOAD");
                string label = status == "HIGH_LOAD"
                    ? (drv + " (" + free + " GB free !)")
                    : (isReady ? (drv + " (" + free + " GB free)") : (drv + " (Offline)"));
                Color bg = status == "HIGH_LOAD"
                    ? Color.FromArgb(180, 83, 9)
                    : (isReady ? Color.FromArgb(2, 132, 199) : Color.FromArgb(51, 65, 85));
                Button b = MakeBtn(label, bg);
                b.Click += delegate { if (Directory.Exists(drv)) NavigateExplorerDirectory(drv); };
                _flpDrivesBar.Controls.Add(b);
            }

            string[,] bms = new string[,]
            {
                { "@lab (E:)", "@lab" },
                { "@archive (F: 2.3TB)", "@archive" },
                { "@vault (DL)", "@vault" },
                { "@forge", "@forge" },
                { "@bridge (G:)", "@bridge" },
                { "Home (~)", "~" }
            };
            for (int k = 0; k < bms.GetLength(0); k++)
            {
                string lbl = bms[k, 0];
                string bm = bms[k, 1];
                Color bc = bm == "@archive" ? Color.FromArgb(5, 150, 105) : Color.FromArgb(67, 56, 202);
                Button bb = MakeBtn(lbl, bc);
                bb.Click += delegate
                {
                    string resolved = MultiDrivePathEngine.ResolvePath(_explorerDir, bm, _shellRoot);
                    try { if (!Directory.Exists(resolved) && bm == "@archive") Directory.CreateDirectory(resolved); } catch { }
                    if (Directory.Exists(resolved)) NavigateExplorerDirectory(resolved);
                };
                _flpDrivesBar.Controls.Add(bb);
            }

            Button bRefDrv = MakeBtn("Re-Probe", Color.FromArgb(51, 65, 85));
            bRefDrv.Click += delegate { RefreshDriveExplorerButtons(); };
            _flpDrivesBar.Controls.Add(bRefDrv);
        }

        private void SyncExplorerToDirectory(string dir)
        {
            if (Directory.Exists(dir)) NavigateExplorerDirectory(dir);
        }

        private void NavigateExplorerDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            _explorerDir = dir;
            _txtExplorerPath.Text = dir;

            List<OshRow> rows = new List<OshRow>();
            long immediateBytes = 0L;
            int dirCount = 0;
            int fileCount = 0;
            try
            {
                string[] subDirs = Directory.GetDirectories(dir);
                dirCount = subDirs.Length;
                for (int i = 0; i < subDirs.Length && rows.Count < 500; i++)
                {
                    DirectoryInfo di = new DirectoryInfo(subDirs[i]);
                    bool isJunc = (di.Attributes & FileAttributes.ReparsePoint) != 0;
                    rows.Add(new OshRow()
                        .Set("name", "[" + di.Name + "]")
                        .Set("kind", isJunc ? "JUNCTION" : "DIR")
                        .Set("size_kb", 0.0)
                        .Set("modified", di.LastWriteTime.ToString("yyyy-MM-dd HH:mm"))
                        .Set("path", di.FullName));
                }
                string[] files = Directory.GetFiles(dir);
                fileCount = files.Length;
                for (int i = 0; i < files.Length && rows.Count < 800; i++)
                {
                    FileInfo fi = new FileInfo(files[i]);
                    immediateBytes += fi.Length;
                    rows.Add(new OshRow()
                        .Set("name", fi.Name)
                        .Set("kind", (fi.Extension ?? "").TrimStart('.').ToUpperInvariant())
                        .Set("size_kb", Math.Round(fi.Length / 1024.0, 1))
                        .Set("modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"))
                        .Set("path", fi.FullName));
                }
            }
            catch (Exception ex)
            {
                SetSingleLineStatus("Drive access notice: " + ex.Message);
            }
            PopulateGridWithOshRows(_gridFiles, rows);
            RenderDirectoryInspectorBanner(dir, dirCount, fileCount, immediateBytes);
        }

        private void PreviewExplorerDirectorySummary(string dirPath)
        {
            try
            {
                int dCnt = Directory.GetDirectories(dirPath).Length;
                string[] fs = Directory.GetFiles(dirPath);
                long sz = 0L;
                for (int i = 0; i < fs.Length && i < 500; i++) { try { sz += new FileInfo(fs[i]).Length; } catch { } }
                RenderDirectoryInspectorBanner(dirPath, dCnt, fs.Length, sz);
            }
            catch { }
        }

        private void RenderDirectoryInspectorBanner(string dirPath, int dirCount, int fileCount, long immediateBytes)
        {
            if (_txtFilePreview == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== DIRECTORY & VOLUME INSPECTOR ===");
            sb.AppendLine("Folder:     " + dirPath);
            try
            {
                DriveInfo drv = new DriveInfo(Path.GetPathRoot(dirPath));
                if (drv.IsReady)
                {
                    sb.AppendLine("Volume:     " + drv.Name + " (" + drv.DriveFormat + ") — " +
                        (drv.AvailableFreeSpace / 1073741824.0).ToString("0.00") + " GB free of " +
                        (drv.TotalSize / 1073741824.0).ToString("0.00") + " GB");
                }
            }
            catch { }
            sb.AppendLine("Contents:   " + dirCount + " subfolder(s), " + fileCount + " file(s) (" + OshFormat.FormatBytes(immediateBytes) + " top-level)");
            sb.AppendLine("================================================================");
            sb.AppendLine("Quick Actions:");
            sb.AppendLine("  * Click any file row on the left to inspect its SHA-256 hash + UTF-8/Hex preview.");
            sb.AppendLine("  * Double-click a subfolder to enter it, or double-click any code file to open in Tab 2 Code Studio.");
            sb.AppendLine("  * Click 'Pin -> CAS' or 'Snapshot Folder' below to store deduplicated SHA-256 backups!");
            _txtFilePreview.Text = sb.ToString();
        }

        private void PreviewExplorerFile(string filePath)
        {
            try
            {
                FileInfo fi = new FileInfo(filePath);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("File:     " + fi.Name);
                sb.AppendLine("Path:     " + fi.FullName);
                sb.AppendLine("Size:     " + OshFormat.FormatBytes(fi.Length) + " (" + fi.Length + " bytes)");
                sb.AppendLine("Modified: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                if (fi.Length <= 64L * 1048576L)
                    sb.AppendLine("SHA-256:  " + OshFormat.Sha256File(filePath));
                sb.AppendLine("================================================================");

                byte[] head = new byte[(int)Math.Min(4096L, fi.Length)];
                using (FileStream fs = File.OpenRead(filePath))
                    fs.Read(head, 0, head.Length);

                bool binary = false;
                for (int i = 0; i < head.Length; i++) if (head[i] == 0) { binary = true; break; }

                if (!binary)
                {
                    sb.AppendLine(Encoding.UTF8.GetString(head));
                }
                else
                {
                    sb.AppendLine("[Binary Header Hex Peek — first 256 bytes]");
                    int n = Math.Min(256, head.Length);
                    for (int i = 0; i < n; i += 16)
                    {
                        sb.Append(i.ToString("X4") + ": ");
                        for (int j = 0; j < 16 && (i + j) < n; j++) sb.Append(head[i + j].ToString("X2") + " ");
                        sb.AppendLine();
                    }
                }
                _txtFilePreview.Text = sb.ToString();
            }
            catch (Exception ex) { _txtFilePreview.Text = "Preview error: " + ex.Message; }
        }

        private async Task PinSelectedExplorerItemToCasAsync()
        {
            string target = _explorerDir;
            if (_gridFiles.SelectedRows.Count > 0 && _gridFiles.Columns.Contains("path"))
                target = (_gridFiles.SelectedRows[0].Cells["path"].Value ?? _explorerDir).ToString();

            List<OshRow> putRows = await Task.Run(delegate { return _runtime.Cloud.PutPathIntoCas(target); });
            PopulateGridWithOshRows(_gridCasAndSnaps, putRows);
            SetSingleLineStatus("Pinned '" + Path.GetFileName(target) + "' (" + putRows.Count + " item(s)) into deduplicated Local Cloud CAS!");
        }

        private void RefreshCasAndSnapshotLists()
        {
            List<OshRow> rows = _runtime.Cloud.ListCasIndexRows();
            if (rows.Count == 0)
            {
                rows.Add(new OshRow()
                    .Set("sha16", "(vault empty)")
                    .Set("name", "Select any file above and click 'Pin -> CAS' or 'Snapshot Folder'")
                    .Set("size_kb", 0.0)
                    .Set("origin", _runtime.Cloud.CasRoot));
            }
            PopulateGridWithOshRows(_gridCasAndSnaps, rows);
        }

        private void RefreshSnapshotGrid()
        {
            List<OshRow> rows = _runtime.Cloud.ListSnapshots();
            if (rows.Count == 0)
            {
                rows.Add(new OshRow()
                    .Set("tag", "(no snapshots)")
                    .Set("files", 0)
                    .Set("source_dir", "Click 'Snapshot Folder' to save an instant SHA-256 time-travel backup"));
            }
            PopulateGridWithOshRows(_gridCasAndSnaps, rows);
        }
    }
}
