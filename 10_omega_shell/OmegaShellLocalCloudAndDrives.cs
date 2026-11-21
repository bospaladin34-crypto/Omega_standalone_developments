using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaShellStudio
{
    public static class MultiDrivePathEngine
    {
        public static string ResolvePath(string currentDir, string rawInput, string shellRoot)
        {
            string suiteRoot = (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            if (!Directory.Exists(suiteRoot) && !string.IsNullOrEmpty(shellRoot))
            {
                try { suiteRoot = Directory.GetParent(shellRoot).FullName; } catch { }
            }

            if (string.IsNullOrWhiteSpace(rawInput) || rawInput.Trim() == ".")
                return string.IsNullOrEmpty(currentDir) ? suiteRoot : currentDir;

            string s = rawInput.Trim().Trim('"');
            if (s == "~" || s.StartsWith("~/", StringComparison.Ordinal) || s.StartsWith("~\\", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return s.Length == 1 ? home : Path.GetFullPath(Path.Combine(home, s.Substring(2).Replace('/', '\\')));
            }

            // Semantic Lab & Cloud Bookmarks (@lab, @vault, @archive / @f -> 2.33 TB F:\, @bridge, @forge, @renders, @cas)
            if (s.StartsWith("@", StringComparison.Ordinal))
            {
                int slash = s.IndexOfAny(new char[] { '/', '\\' });
                string head = (slash > 0 ? s.Substring(0, slash) : s).ToLowerInvariant();
                string tail = slash > 0 ? s.Substring(slash + 1).Replace('/', '\\') : "";
                string baseDir = "";
                if (head == "@lab") baseDir = suiteRoot;
                else if (head == "@vault" || head == "@dl") baseDir = Path.Combine(suiteRoot, "downloads");
                else if (head == "@archive" || head == "@f")
                {
                    baseDir = Directory.Exists(@"F:\") ? @"F:\OMEGA_COLD_CLOUD" : Path.Combine(suiteRoot, "cold_archive");
                    try { Directory.CreateDirectory(baseDir); } catch { baseDir = @"F:\"; }
                }
                else if (head == "@bridge") baseDir = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"))) ? (Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export")) : Path.Combine(suiteRoot, "bridge_staging");
                else if (head == "@forge") baseDir = Path.Combine(suiteRoot, "09_omega_forge", "workspace");
                else if (head == "@renders" || head == "@av") baseDir = Path.Combine(suiteRoot, "03_omega_av", "renders");
                else if (head == "@cas" || head == "@cloud") baseDir = Path.Combine(shellRoot, "cloud_cas");
                else if (head == "@sovereign") baseDir = (Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));

                if (!string.IsNullOrEmpty(baseDir))
                    return string.IsNullOrEmpty(tail) ? baseDir : Path.GetFullPath(Path.Combine(baseDir, tail));
            }

            Match mUnixDrive = Regex.Match(s, @"^/([a-zA-Z])(?:/(.*))?$");
            if (mUnixDrive.Success)
            {
                string drv = mUnixDrive.Groups[1].Value.ToUpperInvariant() + ":\\";
                string rest = (mUnixDrive.Groups[2].Value ?? "").Replace('/', '\\');
                return string.IsNullOrEmpty(rest) ? drv : Path.GetFullPath(Path.Combine(drv, rest));
            }

            if (s.Length == 2 && char.IsLetter(s[0]) && s[1] == ':')
                return char.ToUpperInvariant(s[0]) + ":\\";

            string winNorm = s.Replace('/', '\\');
            if (Path.IsPathRooted(winNorm))
                return Path.GetFullPath(winNorm);

            return Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(currentDir) ? suiteRoot : currentDir, winNorm));
        }

        public static List<OshRow> ListAllDrivesRows()
        {
            List<OshRow> rows = new List<OshRow>();
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    OshRow r = new OshRow();
                    r.Set("drive", d.Name);
                    r.Set("type", d.DriveType.ToString());
                    if (d.IsReady)
                    {
                        double totalGb = Math.Round(d.TotalSize / 1073741824.0, 2);
                        double freeGb = Math.Round(d.AvailableFreeSpace / 1073741824.0, 2);
                        double usedPct = d.TotalSize > 0 ? Math.Round(((d.TotalSize - d.AvailableFreeSpace) * 100.0) / d.TotalSize, 1) : 0.0;
                        string lbl = "";
                        try { lbl = d.VolumeLabel ?? ""; } catch { }
                        string fs = "";
                        try { fs = d.DriveFormat ?? ""; } catch { }
                        r.Set("label", lbl).Set("fs", fs).Set("free_gb", freeGb).Set("total_gb", totalGb).Set("used_pct", usedPct).Set("status", usedPct >= 90.0 ? "HIGH_LOAD" : "READY");
                    }
                    else
                    {
                        r.Set("label", "-").Set("fs", "-").Set("free_gb", 0.0).Set("total_gb", 0.0).Set("used_pct", 0.0).Set("status", "NOT_READY");
                    }
                    rows.Add(r);
                }
            }
            catch { }
            return rows;
        }
    }

    public sealed class LocalCloudVault
    {
        private readonly string _shellRoot;
        private readonly string _casRoot;
        private readonly string _blobsDir;
        private readonly string _indexFile;
        private readonly string _snapsDir;
        private readonly string _kvFile;
        private readonly object _lock = new object();

        public string CasRoot { get { return _casRoot; } }
        public string SnapsDir { get { return _snapsDir; } }

        public LocalCloudVault(string shellRoot)
        {
            _shellRoot = shellRoot;
            _casRoot = Path.Combine(shellRoot, "cloud_cas");
            _blobsDir = Path.Combine(_casRoot, "blobs");
            _indexFile = Path.Combine(_casRoot, "cas_index.tsv");
            _snapsDir = Path.Combine(_casRoot, "snapshots");
            _kvFile = Path.Combine(shellRoot, "config", "kv_store.tsv");
            Directory.CreateDirectory(_blobsDir);
            Directory.CreateDirectory(_snapsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(_kvFile));
        }

        public string PutFileBlob(string filePath, out long sizeBytes, out bool deduplicated)
        {
            sizeBytes = 0L;
            deduplicated = false;
            if (!File.Exists(filePath)) return "";
            FileInfo fi = new FileInfo(filePath);
            sizeBytes = fi.Length;
            string sha = OshFormat.Sha256File(filePath);
            if (string.IsNullOrEmpty(sha)) return "";

            string bucket = Path.Combine(_blobsDir, sha.Substring(0, 2));
            Directory.CreateDirectory(bucket);
            string blobPath = Path.Combine(bucket, sha + ".blob");
            if (File.Exists(blobPath))
                deduplicated = true;
            else
                File.Copy(filePath, blobPath, true);

            lock (_lock)
            {
                string entry = sha + "\t" + sizeBytes + "\t" + fi.Name + "\t" + filePath + "\t" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                File.AppendAllText(_indexFile, entry + "\r\n", new UTF8Encoding(false));
            }
            return sha;
        }

        public List<OshRow> PutPathIntoCas(string targetPath)
        {
            List<OshRow> rows = new List<OshRow>();
            if (File.Exists(targetPath))
            {
                long sz; bool dedup;
                string sha = PutFileBlob(targetPath, out sz, out dedup);
                OshRow r = new OshRow();
                r.Set("name", Path.GetFileName(targetPath)).Set("bytes", sz).Set("dedup", dedup ? "DEDUP_HIT" : "STORED_NEW").Set("uri", "osh://sha256/" + sha.Substring(0, 16)).Set("sha256", sha);
                rows.Add(r);
                return rows;
            }
            if (Directory.Exists(targetPath))
            {
                string[] files = Directory.GetFiles(targetPath, "*.*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length && i < 800; i++)
                {
                    long sz; bool dedup;
                    string sha = PutFileBlob(files[i], out sz, out dedup);
                    if (string.IsNullOrEmpty(sha)) continue;
                    OshRow r = new OshRow();
                    r.Set("name", files[i].Substring(targetPath.TrimEnd('\\').Length + 1)).Set("bytes", sz).Set("dedup", dedup ? "DEDUP_HIT" : "STORED_NEW").Set("uri", "osh://sha256/" + sha.Substring(0, 16)).Set("sha256", sha);
                    rows.Add(r);
                }
            }
            return rows;
        }

        public string GetBlobByPrefix(string hashPrefix, string destPath)
        {
            string clean = (hashPrefix ?? "").Trim().Replace("osh://sha256/", "").ToLowerInvariant();
            if (clean.Length < 4) return "Provide at least 4 hex characters of the SHA-256 hash.";

            string[] buckets = Directory.GetDirectories(_blobsDir);
            for (int b = 0; b < buckets.Length; b++)
            {
                string[] blobs = Directory.GetFiles(buckets[b], "*.blob");
                for (int i = 0; i < blobs.Length; i++)
                {
                    string sha = Path.GetFileNameWithoutExtension(blobs[i]);
                    if (sha.StartsWith(clean, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        File.Copy(blobs[i], destPath, true);
                        return "Restored blob " + sha.Substring(0, 16) + "... -> " + destPath + " (" + new FileInfo(destPath).Length + " bytes)";
                    }
                }
            }
            return "No CAS blob matching prefix '" + clean + "' found in " + _blobsDir;
        }

        public List<OshRow> ListCasIndexRows()
        {
            List<OshRow> rows = new List<OshRow>();
            if (!File.Exists(_indexFile)) return rows;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(_indexFile, Encoding.UTF8);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string[] p = lines[i].Split('\t');
                if (p.Length < 5 || !seen.Add(p[0])) continue;
                long sz; long.TryParse(p[1], out sz);
                OshRow r = new OshRow();
                r.Set("sha16", p[0].Length >= 16 ? p[0].Substring(0, 16) : p[0])
                 .Set("name", p[2])
                 .Set("size_kb", Math.Round(sz / 1024.0, 1))
                 .Set("stored_utc", p[4])
                 .Set("origin", p[3])
                 .Set("sha256", p[0]);
                rows.Add(r);
            }
            return rows;
        }

        public string SyncCasToBridgeAndColdTier()
        {
            List<string> results = new List<string>();
            if (Directory.Exists(@"F:\"))
            {
                string fDst = @"F:\OMEGA_COLD_CLOUD\CAS_MIRROR";
                int fCnt = MirrorCasDirTo(fDst);
                results.Add("F:\\ Cold-Vault (" + fCnt + " new blobs)");
            }
            if (Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"))))
            {
                string gDst = Path.Combine(Environment.GetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bridge_export"), "OMEGA_LOCAL_CLOUD_CAS");
                int gCnt = MirrorCasDirTo(gDst);
                results.Add("G:\\ Drive Bridge (" + gCnt + " new blobs)");
            }
            if (results.Count == 0)
                return "CAS safely stored on E:\\ (" + _casRoot + "); neither F:\\ nor G:\\ Bridge was mounted for mirror.";
            return "Replicated Local Cloud CAS & Snapshots -> " + string.Join(" + ", results.ToArray()) + "!";
        }

        private int MirrorCasDirTo(string dstRoot)
        {
            Directory.CreateDirectory(dstRoot);
            int copied = 0;
            string[] allFiles = Directory.GetFiles(_casRoot, "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < allFiles.Length; i++)
            {
                string rel = allFiles[i].Substring(_casRoot.TrimEnd('\\').Length + 1);
                string dst = Path.Combine(dstRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(allFiles[i]).Length)
                {
                    File.Copy(allFiles[i], dst, true);
                    copied++;
                }
            }
            return copied;
        }

        public string CreateSnapshot(string tag, string sourceDir)
        {
            string safeTag = Regex.Replace((tag ?? "snap").Trim(), @"[^A-Za-z0-9_\-\.]", "_");
            if (!Directory.Exists(sourceDir)) return "Directory not found: " + sourceDir;

            string[] files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
            List<string> lines = new List<string>();
            lines.Add("#root\t" + sourceDir + "\t" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            long totalBytes = 0L;
            int count = 0;

            for (int i = 0; i < files.Length && i < 2000; i++)
            {
                if (files[i].IndexOf(@"\cloud_cas\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                long sz; bool dedup;
                string sha = PutFileBlob(files[i], out sz, out dedup);
                if (string.IsNullOrEmpty(sha)) continue;
                string rel = files[i].Substring(sourceDir.TrimEnd('\\').Length + 1);
                lines.Add(rel + "\t" + sha + "\t" + sz);
                totalBytes += sz;
                count++;
            }

            string snapFile = Path.Combine(_snapsDir, safeTag + ".snap.tsv");
            File.WriteAllLines(snapFile, lines.ToArray(), new UTF8Encoding(false));
            return "Created Snapshot '" + safeTag + "' of " + sourceDir + " (" + count + " files, " + OshFormat.FormatBytes(totalBytes) + " deduplicated in CAS)!";
        }

        public List<OshRow> ListSnapshots()
        {
            List<OshRow> rows = new List<OshRow>();
            if (!Directory.Exists(_snapsDir)) return rows;
            string[] files = Directory.GetFiles(_snapsDir, "*.snap.tsv");
            for (int i = 0; i < files.Length; i++)
            {
                string[] lines = File.ReadAllLines(files[i], Encoding.UTF8);
                string root = "", created = "";
                if (lines.Length > 0 && lines[0].StartsWith("#root\t"))
                {
                    string[] hp = lines[0].Split('\t');
                    if (hp.Length >= 3) { root = hp[1]; created = hp[2]; }
                }
                string nm = Path.GetFileName(files[i]).Replace(".snap.tsv", "");
                OshRow r = new OshRow();
                r.Set("tag", nm).Set("files", Math.Max(0, lines.Length - 1)).Set("created_utc", created).Set("source_dir", root);
                rows.Add(r);
            }
            return rows;
        }

        public List<OshRow> DiffSnapshot(string tag, string overrideDir)
        {
            List<OshRow> rows = new List<OshRow>();
            string safeTag = Regex.Replace((tag ?? "").Trim(), @"[^A-Za-z0-9_\-\.]", "_");
            string snapFile = Path.Combine(_snapsDir, safeTag + ".snap.tsv");
            if (!File.Exists(snapFile)) return rows;

            string[] lines = File.ReadAllLines(snapFile, Encoding.UTF8);
            string root = overrideDir;
            Dictionary<string, string> snapMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#root\t"))
                {
                    if (string.IsNullOrEmpty(root)) root = lines[i].Split('\t')[1];
                    continue;
                }
                string[] p = lines[i].Split('\t');
                if (p.Length >= 2) snapMap[p[0]] = p[1];
            }

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return rows;

            HashSet<string> diskRelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] curFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < curFiles.Length; i++)
            {
                if (curFiles[i].IndexOf(@"\cloud_cas\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string rel = curFiles[i].Substring(root.TrimEnd('\\').Length + 1);
                diskRelSet.Add(rel);
                string curSha = OshFormat.Sha256File(curFiles[i]);
                string oldSha;
                if (!snapMap.TryGetValue(rel, out oldSha))
                    rows.Add(new OshRow().Set("change", "ADDED").Set("file", rel).Set("sha12", curSha.Substring(0, 12)));
                else if (!string.Equals(curSha, oldSha, StringComparison.OrdinalIgnoreCase))
                    rows.Add(new OshRow().Set("change", "MODIFIED").Set("file", rel).Set("sha12", curSha.Substring(0, 12)));
            }

            foreach (KeyValuePair<string, string> kv in snapMap)
            {
                if (!diskRelSet.Contains(kv.Key))
                    rows.Add(new OshRow().Set("change", "DELETED").Set("file", kv.Key).Set("sha12", kv.Value.Substring(0, 12)));
            }

            if (rows.Count == 0)
                rows.Add(new OshRow().Set("change", "IDENTICAL").Set("file", root).Set("sha12", "0 changes vs '" + safeTag + "'"));
            return rows;
        }

        public string RestoreSnapshot(string tag, string overrideDestDir)
        {
            string safeTag = Regex.Replace((tag ?? "").Trim(), @"[^A-Za-z0-9_\-\.]", "_");
            string snapFile = Path.Combine(_snapsDir, safeTag + ".snap.tsv");
            if (!File.Exists(snapFile)) return "Snapshot not found: " + safeTag;

            string[] lines = File.ReadAllLines(snapFile, Encoding.UTF8);
            string destRoot = overrideDestDir;
            int restored = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#root\t"))
                {
                    if (string.IsNullOrEmpty(destRoot)) destRoot = lines[i].Split('\t')[1];
                    continue;
                }
                if (string.IsNullOrEmpty(destRoot)) continue;
                string[] p = lines[i].Split('\t');
                if (p.Length < 2) continue;
                string sha = p[1];
                string blob = Path.Combine(_blobsDir, sha.Substring(0, 2), sha + ".blob");
                if (File.Exists(blob))
                {
                    string outFile = Path.Combine(destRoot, p[0]);
                    Directory.CreateDirectory(Path.GetDirectoryName(outFile));
                    File.Copy(blob, outFile, true);
                    restored++;
                }
            }
            return "Restored Snapshot '" + safeTag + "' -> " + destRoot + " (" + restored + " byte-exact files restored from CAS)!";
        }

        public void KvSet(string key, string val)
        {
            Dictionary<string, string> map = ReadKvMap();
            map[(key ?? "").Trim()] = val ?? "";
            WriteKvMap(map);
        }

        public string KvGet(string key)
        {
            Dictionary<string, string> map = ReadKvMap();
            string v;
            return map.TryGetValue((key ?? "").Trim(), out v) ? v : "";
        }

        public List<OshRow> KvListRows()
        {
            List<OshRow> rows = new List<OshRow>();
            Dictionary<string, string> map = ReadKvMap();
            foreach (KeyValuePair<string, string> kv in map)
                rows.Add(new OshRow().Set("key", kv.Key).Set("value", kv.Value));
            return rows;
        }

        private Dictionary<string, string> ReadKvMap()
        {
            Dictionary<string, string> m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                if (!File.Exists(_kvFile)) return m;
                string[] lines = File.ReadAllLines(_kvFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    int tab = lines[i].IndexOf('\t');
                    if (tab > 0) m[lines[i].Substring(0, tab)] = lines[i].Substring(tab + 1);
                }
            }
            return m;
        }

        private void WriteKvMap(Dictionary<string, string> m)
        {
            lock (_lock)
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in m) lines.Add(kv.Key + "\t" + (kv.Value ?? "").Replace("\r", " ").Replace("\n", " "));
                File.WriteAllLines(_kvFile, lines.ToArray(), new UTF8Encoding(false));
            }
        }
    }

    public static class MeshAndLabBridge
    {
        public static bool ProbePortFast(int port, int timeoutMs)
        {
            System.Net.Sockets.Socket sock = null;
            try
            {
                sock = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                sock.Blocking = false;
                try { sock.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port)); return true; }
                catch (System.Net.Sockets.SocketException se)
                {
                    if (se.SocketErrorCode != System.Net.Sockets.SocketError.WouldBlock &&
                        se.SocketErrorCode != System.Net.Sockets.SocketError.InProgress &&
                        se.SocketErrorCode != System.Net.Sockets.SocketError.AlreadyInProgress)
                        return false;
                }
                if (sock.Poll(Math.Max(1000, timeoutMs * 1000), System.Net.Sockets.SelectMode.SelectWrite))
                    return ((int)sock.GetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.Error)) == 0;
            }
            catch { }
            finally { if (sock != null) { try { sock.Close(); } catch { } } }
            return false;
        }

        public static string ResolveAdbExe()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] cands = new string[]
            {
                Path.Combine(localApp, "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\platform-tools\adb.exe",
                @"E:\platform-tools\adb.exe"
            };
            for (int i = 0; i < cands.Length; i++) if (File.Exists(cands[i])) return cands[i];
            return "adb.exe";
        }

        public static List<string> ListAdbSerials()
        {
            List<string> list = new List<string>();
            string raw = Win32HardwareTuner.RunCmdFast(ResolveAdbExe(), "devices", 1600);
            string[] lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.EndsWith("\tdevice"))
                {
                    string ser = t.Split('\t')[0].Trim();
                    if (ser.Length > 0) list.Add(ser);
                }
            }
            return list;
        }

        public static string RunOnPixelTarget(string sigil, string shellCmd)
        {
            List<string> ser = ListAdbSerials();
            if (ser.Count == 0) return "No ADB Pixel 10 connected (plug USB or promote wireless :5555).";

            string maskPrefix = "";
            string laneTag = "all-cores";
            if (sigil == "@p10-sensor") { maskPrefix = "taskset 03 "; laneTag = "0x03:2xA520"; }
            else if (sigil == "@p10-mid3") { maskPrefix = "taskset 1c "; laneTag = "0x1C:3xA725"; }
            else if (sigil == "@p10-mid2") { maskPrefix = "taskset 60 "; laneTag = "0x60:2xA725"; }
            else if (sigil == "@p10-compute" || sigil == "@p8") { maskPrefix = "taskset 7c "; laneTag = "0x7C:5xA725"; }
            else if (sigil == "@p10-prime") { maskPrefix = "taskset 80 "; laneTag = "0x80:Cortex-X4"; }

            List<string> targets = new List<string>();
            if (sigil == "@pixels") targets.AddRange(ser);
            else targets.Add(ser[0]);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < targets.Count; i++)
            {
                string fullCmd = (maskPrefix + shellCmd).Replace("\"", "\\\"");
                string outTxt = Win32HardwareTuner.RunCmdFast(ResolveAdbExe(), "-s " + targets[i] + " shell \"" + fullCmd + "\"", 12000);
                sb.AppendLine("[" + targets[i] + " :: " + laneTag + "] " + outTxt.Trim());
            }
            return sb.ToString().Trim();
        }

        public static string ExecutePixel10WorkerVerb(string sub, string arg2, string shellRoot)
        {
            string adb = ResolveAdbExe();
            List<string> ser = ListAdbSerials();
            if (ser.Count > 0)
                Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " forward tcp:19788 tcp:19788", 2000);

            if (sub == "host-bench" || sub == "host-tensor")
            {
                string hostExe = Path.Combine(Directory.GetParent(shellRoot).FullName, "11_omega_edge_workers\\rust_p10_worker", "bin", "omega_p10_worker_host.exe");
                if (!File.Exists(hostExe)) return "Host worker binary not built yet: " + hostExe;
                string hArgs = (sub == "host-tensor") ? "--vulkan tensor" : "--bench";
                return Win32HardwareTuner.RunCmdFast(hostExe, hArgs, 25000);
            }
            if (sub == "coopmat" || sub == "tensor" || sub == "intdot")
            {
                string kMode = (sub == "coopmat") ? "coopmat" : ((sub == "intdot") ? "intdot" : "tensor");
                if (sub == "coopmat" && ProbePortFast(19788, 180)) return HttpGetJson("http://127.0.0.1:19788/api/coopmat", 18000);
                if (ProbePortFast(19788, 180)) return HttpGetJson("http://127.0.0.1:19788/api/vulkan?kernel=" + kMode, 18000);
                if (ser.Count == 0) return "Pixel 10 daemon (:19788) offline and no ADB device connected.";
                return Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell \"/data/local/tmp/omega_p10_worker --vulkan " + kMode + "\"", 22000);
            }
            if (sub == "caps")
            {
                if (ProbePortFast(19788, 180)) return HttpGetJson("http://127.0.0.1:19788/api/caps", 4000);
                if (ser.Count == 0) return "Pixel 10 daemon (:19788) offline and no ADB device connected.";
                return Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell /data/local/tmp/omega_p10_worker --caps", 12000);
            }
            if (sub == "vulkan" || sub == "kernels")
            {
                string kName = string.IsNullOrWhiteSpace(arg2) ? "all" : arg2.Trim().ToLowerInvariant();
                if (ProbePortFast(19788, 180)) return HttpGetJson("http://127.0.0.1:19788/api/vulkan?kernel=" + Uri.EscapeDataString(kName), 15000);
                if (ser.Count == 0) return "Pixel 10 daemon (:19788) offline and no ADB device connected.";
                return Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell \"/data/local/tmp/omega_p10_worker --vulkan " + kName.Replace("\"", "") + "\"", 20000);
            }
            if (sub == "lease")
            {
                if (ProbePortFast(19788, 180)) return HttpGetJson("http://127.0.0.1:19788/api/lease", 3000);
                if (ser.Count == 0) return "Pixel 10 daemon (:19788) offline and no ADB device connected.";
                return Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell /data/local/tmp/omega_p10_worker --json", 15000);
            }
            if (sub == "daemon")
            {
                if (ser.Count == 0) return "No ADB Pixel 10 connected.";
                string daemonCmd = "pkill -f omega_p10_worker >/dev/null 2>&1; nohup /data/local/tmp/omega_p10_worker --daemon --port 19788 </dev/null >/data/local/tmp/omega_p10_worker.log 2>&1 &";
                Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell \"" + daemonCmd + "\"", 4000);
                System.Threading.Thread.Sleep(500);
                return ProbePortFast(19788, 250)
                    ? HttpGetJson("http://127.0.0.1:19788/api/status", 5000)
                    : "Started omega_p10_worker v0.3 daemon on " + ser[0] + " (:19788).";
            }
            if (sub == "bench")
            {
                if (ser.Count == 0) return "No ADB Pixel 10 connected.";
                return Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell /data/local/tmp/omega_p10_worker --bench", 22000);
            }
            if (ProbePortFast(19788, 180))
                return HttpGetJson("http://127.0.0.1:19788/api/status", 4000);
            if (ser.Count > 0)
                return Win32HardwareTuner.RunCmdFast(adb, "-s " + ser[0] + " shell /data/local/tmp/omega_p10_worker --bench", 20000);
            return "Pixel 10 Worker (:19788) offline and no ADB device connected.";
        }

        public static string BeamFileToPixels(string localFile)
        {
            if (!File.Exists(localFile)) return "File not found: " + localFile;
            List<string> ser = ListAdbSerials();
            if (ser.Count == 0) return "No ADB Pixel connected.";
            string fn = Path.GetFileName(localFile).Replace("\"", "_");
            string rem = "/sdcard/Download/" + fn;
            for (int i = 0; i < ser.Count; i++)
                Win32HardwareTuner.RunCmdFast(ResolveAdbExe(), "-s " + ser[i] + " push \"" + localFile + "\" \"" + rem + "\"", 90000);
            return "Beamed '" + fn + "' -> " + ser.Count + " Pixel(s) (/sdcard/Download/" + fn + ")!";
        }

        public static string HttpGetJson(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":\"" + OshFormat.JsonEsc(ex.Message) + "\"}"; }
        }

        public static string HttpPostJson(string url, string jsonBody, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                byte[] b = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                req.ContentLength = b.Length;
                using (Stream st = req.GetRequestStream()) st.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":\"" + OshFormat.JsonEsc(ex.Message) + "\"}"; }
        }

        public static string QueryLocalGemma(string endpointUrl, string modelTag, string contextText, string promptText)
        {
            string url = string.IsNullOrWhiteSpace(endpointUrl) ? "http://127.0.0.1:8097/v1/chat/completions" : endpointUrl.Trim();
            bool isOllama = url.IndexOf("/api/chat", StringComparison.OrdinalIgnoreCase) >= 0;
            string combined = string.IsNullOrWhiteSpace(contextText)
                ? promptText
                : (promptText + "\r\n\r\nPiped Shell Input:\r\n" + (contextText.Length > 4000 ? contextText.Substring(0, 4000) : contextText));

            string body = isOllama
                ? "{\"model\":\"" + OshFormat.JsonEsc(modelTag) + "\",\"messages\":[{\"role\":\"user\",\"content\":\"" + OshFormat.JsonEsc(combined) + "\"}],\"stream\":false}"
                : "{\"model\":\"" + OshFormat.JsonEsc(modelTag) + "\",\"messages\":[{\"role\":\"user\",\"content\":\"" + OshFormat.JsonEsc(combined) + "\"}],\"max_tokens\":768,\"temperature\":0.2,\"stream\":false}";

            string raw = HttpPostJson(url, body, 60000);
            Match m = Regex.Match(raw, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (m.Success) return OshFormat.JsonUnesc(m.Groups[1].Value);
            return raw;
        }
    }
}
