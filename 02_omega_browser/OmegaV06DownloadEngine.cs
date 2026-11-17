using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace OmegaBrowserV03
{
    public sealed class DownloadTrackItem
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string EngineKind = "HTTP-206";
        public CoreWebView2DownloadOperation Op;
        public HttpRangeDownloadJob RangeJob;
        public string SourceUrl = "";
        public string FilePath = "";
        public string FileName = "";
        public string Category = "Files";
        public string StateText = "Queued";
        public long BytesReceived = 0L;
        public long TotalBytes = 0L;
        public double SpeedBps = 0.0;
        public string Sha256 = "";
        public string CorpusStatus = "";
        public bool AutoDistillPdf = false;
        public bool AutoSendOmegaAv = false;
        public bool Done = false;
        public bool Canceled = false;
        public bool Paused = false;
        public bool PostProcessed = false;
        public bool QueuedForSlot = false;
        public DateTime StartedUtc = DateTime.UtcNow;
        public DateTime LastSampleUtc = DateTime.UtcNow;
        public long LastSampleBytes = 0L;
        public Button Chip;

        public bool IsOmegaAvCompatible
        {
            get
            {
                string ext = (Path.GetExtension(FileName) ?? "").ToLowerInvariant();
                return ext == ".wav" || ext == ".omsh" || ext == ".hlsl" || ext == ".glsl" || ext == ".txt";
            }
        }

        public bool IsPdf
        {
            get
            {
                string ext = (Path.GetExtension(FileName) ?? "").ToLowerInvariant();
                return ext == ".pdf";
            }
        }

        public int ProgressPercent
        {
            get
            {
                if (Done && !Canceled) return 100;
                if (TotalBytes <= 0L || BytesReceived <= 0L) return 0;
                long pct = (BytesReceived * 100L) / TotalBytes;
                if (pct < 0L) return 0;
                if (pct > 100L) return 100;
                return (int)pct;
            }
        }
    }

    public sealed class SniffedLinkCandidate
    {
        public string Url = "";
        public string FileName = "";
        public string Category = "Files";
        public string Label = "";
        public string TagSource = "a";
        public bool IsDownloadableAsset = false;
    }

    public static class OmegaDownloadRouter
    {
        public static readonly string[] VaultCategories = new string[]
        {
            "Papers", "Audio", "Shaders", "Models", "Datasets", "Code", "Archives", "Files"
        };

        public static string ResolveSuiteRoot(string labRoot)
        {
            try
            {
                if (Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')))) return (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
                DirectoryInfo parent = Directory.GetParent(labRoot);
                if (parent != null) return parent.FullName;
            }
            catch { }
            return labRoot;
        }

        public static string ResolveDownloadsRoot(string labRoot)
        {
            try
            {
                if (Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'))) || Directory.Exists(@"E:\"))
                    return Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "downloads");
            }
            catch { }
            return Path.Combine(ResolveSuiteRoot(labRoot), "downloads");
        }

        public static void EnsureVaultDirectories(string labRoot)
        {
            string dlRoot = ResolveDownloadsRoot(labRoot);
            Directory.CreateDirectory(dlRoot);
            for (int i = 0; i < VaultCategories.Length; i++)
                Directory.CreateDirectory(Path.Combine(dlRoot, VaultCategories[i]));
            Directory.CreateDirectory(Path.Combine(labRoot, "vault"));
        }

        public static string ClassifyCategory(string fileName, string sourceUrl)
        {
            string ext = (Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(sourceUrl))
            {
                Uri u;
                if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out u))
                    ext = (Path.GetExtension(u.AbsolutePath ?? "") ?? "").ToLowerInvariant();
            }
            string lowUrl = (sourceUrl ?? "").ToLowerInvariant();
            if (ext == ".pdf" || ext == ".djvu" || ext == ".ps" || ext == ".epub" || ext == ".tex" || ext == ".bib" || lowUrl.Contains("arxiv.org/pdf/"))
                return "Papers";
            if (ext == ".wav" || ext == ".mp3" || ext == ".flac" || ext == ".ogg" || ext == ".m4a" || ext == ".aac" || ext == ".opus" || ext == ".mid" || ext == ".midi")
                return "Audio";
            if (ext == ".omsh" || ext == ".hlsl" || ext == ".glsl" || ext == ".wgsl" || ext == ".fx" || ext == ".frag" || ext == ".vert")
                return "Shaders";
            if (ext == ".gguf" || ext == ".safetensors" || ext == ".onnx" || ext == ".pt" || ext == ".pth" || ext == ".bin" || ext == ".tflite" || ext == ".litertlm")
                return "Models";
            if (ext == ".jsonl" || ext == ".ndjson" || ext == ".csv" || ext == ".tsv" || ext == ".parquet" || ext == ".arrow" || ext == ".sqlite" || ext == ".db" || ext == ".duckdb")
                return "Datasets";
            if (ext == ".rs" || ext == ".cs" || ext == ".py" || ext == ".cpp" || ext == ".c" || ext == ".h" || ext == ".hpp" || ext == ".ts" || ext == ".js" || ext == ".ps1" || ext == ".sh" || ext == ".lean" || ext == ".hs" || ext == ".kt")
                return "Code";
            if (ext == ".zip" || ext == ".7z" || ext == ".tar" || ext == ".gz" || ext == ".tgz" || ext == ".bz2" || ext == ".xz" || ext == ".zst" || ext == ".rar" || ext == ".msi" || ext == ".exe" || ext == ".apk" || ext == ".iso")
                return "Archives";
            return "Files";
        }

        public static bool IsRecognizedAssetUrl(string url, string fileName)
        {
            string cat = ClassifyCategory(fileName, url);
            if (cat != "Files") return true;
            string ext = (Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
            return ext == ".json" || ext == ".xml" || ext == ".yaml" || ext == ".yml" || ext == ".md" || ext == ".txt" || ext == ".mp4" || ext == ".webm" || ext == ".png" || ext == ".jpg";
        }

        public static string InferCleanFileName(string rawName, string url)
        {
            string candidate = (rawName ?? "").Trim();
            if (string.IsNullOrEmpty(candidate) && !string.IsNullOrEmpty(url))
            {
                Match mArx = Regex.Match(url, @"arxiv\.org/pdf/([0-9]{4}\.[0-9]{4,5}(?:v[0-9]+)?)", RegexOptions.IgnoreCase);
                if (mArx.Success) candidate = "arxiv_" + mArx.Groups[1].Value + ".pdf";
                else
                {
                    Uri u;
                    if (Uri.TryCreate(url, UriKind.Absolute, out u))
                    {
                        string seg = Path.GetFileName((u.AbsolutePath ?? "").TrimEnd('/'));
                        try { seg = Uri.UnescapeDataString(seg ?? ""); } catch { }
                        candidate = seg;
                    }
                }
            }
            if (!string.IsNullOrEmpty(url) && url.IndexOf("arxiv.org/pdf/", StringComparison.OrdinalIgnoreCase) >= 0 && !candidate.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                candidate = (string.IsNullOrEmpty(candidate) ? "arxiv_paper" : candidate) + ".pdf";

            if (string.IsNullOrWhiteSpace(candidate)) candidate = "omega_download_" + DateTime.UtcNow.ToString("HHmmss") + ".bin";

            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(candidate.Length);
            for (int i = 0; i < candidate.Length; i++)
            {
                char ch = candidate[i];
                bool invalid = ch < 32;
                if (!invalid)
                {
                    for (int b = 0; b < bad.Length; b++) if (ch == bad[b]) { invalid = true; break; }
                }
                sb.Append(invalid ? '_' : ch);
            }
            string cleaned = sb.ToString().Trim();
            if (cleaned.Length > 140)
            {
                string ext = Path.GetExtension(cleaned);
                string stem = Path.GetFileNameWithoutExtension(cleaned);
                cleaned = stem.Substring(0, Math.Min(110, stem.Length)) + ext;
            }
            return string.IsNullOrEmpty(cleaned) ? "download.bin" : cleaned;
        }

        public static string AllocateDestPath(string labRoot, string rawName, string sourceUrl, out string category)
        {
            string dlRoot = ResolveDownloadsRoot(labRoot);
            string cleanName = InferCleanFileName(rawName, sourceUrl);
            category = ClassifyCategory(cleanName, sourceUrl);
            string catDir = Path.Combine(dlRoot, category);
            Directory.CreateDirectory(catDir);
            string dest = Path.Combine(catDir, cleanName);
            if (File.Exists(dest) || File.Exists(dest + ".part"))
            {
                string ext = Path.GetExtension(cleanName);
                string stem = Path.GetFileNameWithoutExtension(cleanName);
                dest = Path.Combine(catDir, stem + "_" + DateTime.Now.ToString("HHmmss") + ext);
            }
            return dest;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0L) bytes = 0L;
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
        }

        public static string FormatSpeedAndEta(double bps, long received, long total)
        {
            if (bps <= 1.0) return "--";
            string sp = FormatBytes((long)bps) + "/s";
            if (total > received && bps > 128.0)
            {
                long remSec = (long)((total - received) / bps);
                if (remSec < 3600L)
                    sp += " (" + (remSec / 60L).ToString("00") + ":" + (remSec % 60L).ToString("00") + ")";
                else
                    sp += " (" + (remSec / 3600L) + "h " + ((remSec % 3600L) / 60L) + "m)";
            }
            return sp;
        }

        public static string GetVaultDriveSummary(string labRoot)
        {
            string root = ResolveDownloadsRoot(labRoot);
            try
            {
                string drvRoot = Path.GetPathRoot(root);
                if (!string.IsNullOrEmpty(drvRoot))
                {
                    DriveInfo di = new DriveInfo(drvRoot);
                    if (di.IsReady)
                        return "Vault: " + root + "  |  " + drvRoot.TrimEnd('\\') + " Free: " + FormatBytes(di.AvailableFreeSpace) + " / " + FormatBytes(di.TotalSize);
                }
            }
            catch { }
            return "Vault: " + root;
        }
    }

    public sealed class HttpRangeDownloadJob
    {
        static HttpRangeDownloadJob()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                if (ServicePointManager.DefaultConnectionLimit < 24)
                    ServicePointManager.DefaultConnectionLimit = 24;
            }
            catch { }
        }

        public readonly string Url;
        public readonly string DestPath;
        public readonly string PartPath;
        public readonly string MetaPath;

        private long _bytesReceived = 0L;
        private long _totalBytes = 0L;
        private volatile bool _isRunning = false;
        private volatile bool _pauseRequested = false;
        private volatile bool _cancelRequested = false;
        private volatile bool _isCompleted = false;
        private volatile bool _supportsRange = true;
        private volatile string _lastError = "";
        private string _etag = "";
        private bool _autoDistillPdf = false;
        private HttpWebRequest _activeRequest;

        public long BytesReceived { get { return Interlocked.Read(ref _bytesReceived); } }
        public long TotalBytes { get { return Interlocked.Read(ref _totalBytes); } }
        public bool IsRunning { get { return _isRunning; } }
        public bool IsPaused { get { return _pauseRequested && !_isRunning && !_isCompleted && !_cancelRequested; } }
        public bool IsCompleted { get { return _isCompleted; } }
        public bool IsCanceled { get { return _cancelRequested; } }
        public bool SupportsRange { get { return _supportsRange; } }
        public string LastError { get { return _lastError; } }

        public HttpRangeDownloadJob(string url, string destPath, bool autoDistillPdf, long knownTotal, string knownEtag)
        {
            Url = url ?? "";
            DestPath = destPath ?? "";
            PartPath = DestPath + ".part";
            MetaPath = DestPath + ".part.meta";
            _autoDistillPdf = autoDistillPdf;
            _etag = knownEtag ?? "";
            if (knownTotal > 0L) Interlocked.Exchange(ref _totalBytes, knownTotal);
            if (File.Exists(PartPath))
            {
                try { Interlocked.Exchange(ref _bytesReceived, new FileInfo(PartPath).Length); } catch { }
            }
        }

        public void WriteMetaSidecar()
        {
            try
            {
                string[] lines = new string[]
                {
                    Url,
                    Interlocked.Read(ref _totalBytes).ToString(),
                    _etag ?? "",
                    _autoDistillPdf ? "1" : "0"
                };
                File.WriteAllLines(MetaPath, lines, new UTF8Encoding(false));
            }
            catch { }
        }

        public void StartOrResume(Action onStateChanged)
        {
            if (_isRunning || _isCompleted || _cancelRequested) return;
            _pauseRequested = false;
            _lastError = "";
            _isRunning = true;
            WriteMetaSidecar();
            if (onStateChanged != null) onStateChanged();

            ThreadPool.QueueUserWorkItem(delegate
            {
                RunTransferLoop(onStateChanged);
            });
        }

        public void Pause()
        {
            if (_isCompleted || _cancelRequested) return;
            _pauseRequested = true;
            WriteMetaSidecar();
            HttpWebRequest req = _activeRequest;
            if (req != null)
            {
                try { req.Abort(); } catch { }
            }
        }

        public void Cancel()
        {
            _cancelRequested = true;
            _pauseRequested = false;
            HttpWebRequest req = _activeRequest;
            if (req != null)
            {
                try { req.Abort(); } catch { }
            }
            CleanupPartFiles();
        }

        private void CleanupPartFiles()
        {
            try { if (File.Exists(PartPath)) File.Delete(PartPath); } catch { }
            try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch { }
        }

        private void FinalizePartToDest()
        {
            if (File.Exists(PartPath))
            {
                string dir = Path.GetDirectoryName(DestPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(DestPath)) File.Delete(DestPath);
                File.Move(PartPath, DestPath);
            }
            try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch { }
            if (File.Exists(DestPath))
            {
                long finalLen = new FileInfo(DestPath).Length;
                Interlocked.Exchange(ref _bytesReceived, finalLen);
                Interlocked.Exchange(ref _totalBytes, finalLen);
            }
            _isCompleted = true;
        }

        private void RunTransferLoop(Action onStateChanged)
        {
            long existing = 0L;
            try
            {
                string dir = Path.GetDirectoryName(DestPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(PartPath)) existing = new FileInfo(PartPath).Length;
                Interlocked.Exchange(ref _bytesReceived, existing);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url);
                req.Method = "GET";
                req.AllowAutoRedirect = true;
                req.AutomaticDecompression = DecompressionMethods.None;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 OmegaBrowser/0.6 SovereignHTTP206";
                req.Timeout = 25000;
                req.ReadWriteTimeout = 30000;

                if (existing > 0L)
                {
                    req.AddRange(existing);
                    if (!string.IsNullOrEmpty(_etag) && !_etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                        req.Headers["If-Range"] = _etag;
                }

                _activeRequest = req;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int statusCode = (int)resp.StatusCode;
                    string acceptRanges = resp.Headers["Accept-Ranges"] ?? "";
                    string respEtag = resp.Headers["ETag"] ?? "";
                    if (!string.IsNullOrEmpty(respEtag)) _etag = respEtag;

                    bool resumed206 = (existing > 0L && statusCode == 206);
                    if (resumed206)
                    {
                        _supportsRange = true;
                        string contentRange = resp.Headers["Content-Range"] ?? "";
                        Match mCr = Regex.Match(contentRange, @"bytes\s+\d+-\d+/(\d+)", RegexOptions.IgnoreCase);
                        long parsedTotal;
                        if (mCr.Success && long.TryParse(mCr.Groups[1].Value, out parsedTotal) && parsedTotal > 0L)
                            Interlocked.Exchange(ref _totalBytes, parsedTotal);
                        else if (resp.ContentLength > 0L)
                            Interlocked.Exchange(ref _totalBytes, existing + resp.ContentLength);
                    }
                    else
                    {
                        existing = 0L;
                        Interlocked.Exchange(ref _bytesReceived, 0L);
                        _supportsRange = (statusCode == 206) || (acceptRanges.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (resp.ContentLength > 0L)
                            Interlocked.Exchange(ref _totalBytes, resp.ContentLength);
                    }

                    WriteMetaSidecar();

                    FileMode fMode = resumed206 ? FileMode.OpenOrCreate : FileMode.Create;
                    using (Stream net = resp.GetResponseStream())
                    using (FileStream fs = new FileStream(PartPath, fMode, FileAccess.Write, FileShare.Read, 65536))
                    {
                        if (resumed206) fs.Seek(existing, SeekOrigin.Begin);
                        byte[] buf = new byte[65536];
                        while (!_pauseRequested && !_cancelRequested)
                        {
                            int n = net.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            fs.Write(buf, 0, n);
                            existing += n;
                            Interlocked.Exchange(ref _bytesReceived, existing);
                        }
                        fs.Flush(true);
                    }
                }

                if (_cancelRequested)
                {
                    CleanupPartFiles();
                }
                else if (_pauseRequested)
                {
                    WriteMetaSidecar();
                }
                else
                {
                    FinalizePartToDest();
                }
            }
            catch (WebException wex)
            {
                if (_cancelRequested)
                {
                    CleanupPartFiles();
                }
                else if (_pauseRequested)
                {
                    WriteMetaSidecar();
                }
                else
                {
                    HttpWebResponse errResp = wex.Response as HttpWebResponse;
                    if (errResp != null && (int)errResp.StatusCode == 416 && existing > 0L)
                    {
                        try { FinalizePartToDest(); }
                        catch (Exception ex2) { _lastError = ex2.Message; }
                    }
                    else
                    {
                        _lastError = wex.Message;
                        WriteMetaSidecar();
                    }
                }
            }
            catch (Exception ex)
            {
                if (_cancelRequested) CleanupPartFiles();
                else if (!_pauseRequested) { _lastError = ex.Message; WriteMetaSidecar(); }
            }
            finally
            {
                _activeRequest = null;
                _isRunning = false;
                if (onStateChanged != null) onStateChanged();
            }
        }
    }

    public static class OmegaCrossAppBridge
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static string PipeWebStreamToOmegaSynth(string labRoot, string title, string url)
        {
            string suiteRoot = OmegaDownloadRouter.ResolveSuiteRoot(labRoot);
            string syDir = Path.Combine(suiteRoot, "04_omega_synth");
            if (!Directory.Exists(syDir) && Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "04_omega_synth")))
                syDir = Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "04_omega_synth");
            string syExe = Path.Combine(syDir, "OmegaSynth.exe");

            Process[] running = Process.GetProcessesByName("OmegaSynth");
            if ((running == null || running.Length == 0) && File.Exists(syExe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(syExe);
                    psi.WorkingDirectory = syDir;
                    Process.Start(psi);
                    for (int w = 0; w < 16; w++)
                    {
                        Thread.Sleep(200);
                        if (Process.GetProcessesByName("OmegaSynth").Length > 0) break;
                    }
                    Thread.Sleep(350);
                }
                catch { }
            }

            try
            {
                string apiUrl = "http://127.0.0.1:19782/api/browser-pipe?active=1&title=" + Uri.EscapeDataString(title ?? "Web Stream") + "&url=" + Uri.EscapeDataString(url ?? "");
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiUrl);
                req.Method = "GET";
                req.Timeout = 2500;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) { }
                return "[Web-Audio Pipe LIVE] Piping '" + (title ?? "Tab") + "' -> OmegaSynth v1.1.1 Final GM (:19782)!";
            }
            catch (Exception ex)
            {
                return "Launched OmegaSynth (:19782) — click [Web Pipe] if needed (" + ex.Message + ")";
            }
        }

        public static string SendFileToOmegaSynth(string labRoot, string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return "File not found on disk: " + filePath;

            string suiteRoot = OmegaDownloadRouter.ResolveSuiteRoot(labRoot);
            string syDir = Path.Combine(suiteRoot, "04_omega_synth");
            if (!Directory.Exists(syDir) && Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "04_omega_synth")))
                syDir = Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "04_omega_synth");
            Directory.CreateDirectory(syDir);

            File.WriteAllText(Path.Combine(syDir, "synth_inbox.cmd"), filePath, new UTF8Encoding(false));

            Process[] running = Process.GetProcessesByName("OmegaSynth");
            if (running != null && running.Length > 0)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:19782/api/load-file?path=" + Uri.EscapeDataString(filePath));
                    req.Method = "GET"; req.Timeout = 2200;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) { }
                }
                catch { }
                try
                {
                    IntPtr h = running[0].MainWindowHandle;
                    if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                }
                catch { }
                return "Hot-beamed -> OmegaSynth v1.1.1 Final GM (" + Path.GetFileName(filePath) + " Shape + Resynth)!";
            }

            string syExe = Path.Combine(syDir, "OmegaSynth.exe");
            if (File.Exists(syExe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(syExe, "\"" + filePath + "\"");
                    psi.WorkingDirectory = syDir;
                    Process.Start(psi);
                    return "Launched OmegaSynth v1.1.1 Final GM -> " + Path.GetFileName(filePath);
                }
                catch (Exception ex) { return "Staged for OmegaSynth (err: " + ex.Message + ")"; }
            }
            return "Staged -> synth_inbox.cmd (OmegaSynth.exe not found)";
        }

        public static string SendFileToOmegaAv(string labRoot, string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return "File not found on disk: " + filePath;

            string suiteRoot = OmegaDownloadRouter.ResolveSuiteRoot(labRoot);
            string avDir = Path.Combine(suiteRoot, "03_omega_av");
            if (!Directory.Exists(avDir) && Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "03_omega_av")))
                avDir = Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "03_omega_av");
            Directory.CreateDirectory(avDir);

            string ext = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();
            string subFolder = (ext == ".wav" || ext == ".mp3" || ext == ".flac") ? "audio_inbox"
                : ((ext == ".omsh" || ext == ".hlsl" || ext == ".glsl" || ext == ".txt") ? "shaders" : "inbox");
            string stageDir = Path.Combine(avDir, subFolder);
            Directory.CreateDirectory(stageDir);

            string stagedPath = Path.Combine(stageDir, Path.GetFileName(filePath));
            if (!string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(stagedPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(filePath, stagedPath, true);

            string cmdDrop = Path.Combine(avDir, "inbox_open.cmd");
            File.WriteAllText(cmdDrop, stagedPath, new UTF8Encoding(false));

            Process[] running = Process.GetProcessesByName("OmegaAV");
            if (running != null && running.Length > 0)
            {
                try
                {
                    IntPtr h = running[0].MainWindowHandle;
                    if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                }
                catch { }
                return "Hot-beamed -> running OmegaAV v0.7.1 (" + Path.GetFileName(stagedPath) + ")";
            }

            string avExe = Path.Combine(avDir, "OmegaAV.exe");
            if (File.Exists(avExe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(avExe, "\"" + stagedPath + "\"");
                    psi.WorkingDirectory = avDir;
                    Process.Start(psi);
                    return "Launched OmegaAV v0.7.1 -> " + Path.GetFileName(stagedPath);
                }
                catch (Exception ex)
                {
                    return "Staged in " + stagedPath + " (launch err: " + ex.Message + ")";
                }
            }
            return "Staged -> " + stagedPath + " (OmegaAV.exe not compiled yet)";
        }

        public static string PushFileToSinglePixel(string serial, string label, string filePath)
        {
            if (string.IsNullOrEmpty(serial)) return "No Pixel ADB serial selected.";
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return "Local file missing: " + filePath;

            string safeName = Path.GetFileName(filePath).Replace("\"", "_");
            string remotePath = "/sdcard/Download/" + safeName;
            string adbArgs = "-s " + serial + " push \"" + filePath + "\" \"" + remotePath + "\"";
            string outTxt = DualPixelMesh.RunAdb(adbArgs, 180000);
            DualPixelMesh.RunAdb("-s " + serial + " shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d \"file://" + remotePath + "\"", 4000);

            long sz = new FileInfo(filePath).Length;
            if (outTxt.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 && outTxt.IndexOf("pushed", StringComparison.OrdinalIgnoreCase) < 0)
                return "ADB Push Error (" + label + "): " + outTxt.Trim();
            return "Pushed " + safeName + " (" + OmegaDownloadRouter.FormatBytes(sz) + ") -> " + label + " [" + remotePath + "]";
        }

        public static string BroadcastFileToAllPixels(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return "Local file missing: " + filePath;
            List<PixelDeviceInfo> devs = DualPixelMesh.DiscoverPixels();
            if (devs.Count == 0) return "No connected Pixel 8 / Pixel 10 detected via ADB (USB or Wi-Fi :5555).";

            List<string> results = new List<string>();
            for (int i = 0; i < devs.Count; i++)
            {
                string r = PushFileToSinglePixel(devs[i].Serial, devs[i].Model, filePath);
                results.Add(devs[i].Model);
            }
            long sz = new FileInfo(filePath).Length;
            return "Broadcast Pushed " + Path.GetFileName(filePath) + " (" + OmegaDownloadRouter.FormatBytes(sz) + ") -> " + string.Join(" + ", results.ToArray()) + " (/sdcard/Download/)";
        }
    }
}
