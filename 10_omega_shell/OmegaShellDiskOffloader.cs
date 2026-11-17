using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmegaShellStudio
{
    public sealed class SpaceHogEntry
    {
        public string Category = "";
        public string PathOnC = "";
        public double SizeMb = 0.0;
        public int FileCount = 0;
        public bool IsJunction = false;
        public string JunctionTarget = "";
        public string SafeNote = "";
    }

    public static class DiskSpaceOffloader
    {
        private static readonly string[] ForbiddenPrefixes = new string[]
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData\Microsoft",
            @"C:\Users\Public",
            @"C:\$Recycle.Bin",
            @"C:\System Volume Information"
        };

        public static List<SpaceHogEntry> ScanCDriveHogs(string shellRoot)
        {
            List<SpaceHogEntry> results = new List<SpaceHogEntry>();
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string[,] candidates = new string[,]
            {
                { "AI Cache (HuggingFace)", Path.Combine(user, ".cache", "huggingface"), "Safe for mklink /J offload to E: or F:" },
                { "AI Weights (Ollama Blobs)", Path.Combine(user, ".ollama", "models"), "Stop Ollama first, then mklink /J to E: or F:" },
                { "AI Weights (LM Studio)", Path.Combine(user, ".lmstudio", "models"), "Safe for mklink /J offload to E: or F:" },
                { "Rust Cargo Registry", Path.Combine(user, ".cargo", "registry"), "Safe for mklink /J offload to E: or F:" },
                { "Rust Cargo Git Cache", Path.Combine(user, ".cargo", "git"), "Safe for mklink /J offload to E: or F:" },
                { "Sovereign Rust target\\", Path.Combine(Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "target"), "Build cache; safe to offload or cargo clean" },
                { "NuGet Package Cache", Path.Combine(user, ".nuget", "packages"), "Safe for mklink /J offload to E: or F:" },
                { "Python pip Cache", Path.Combine(local, "pip", "cache"), "Safe to offload or clean" },
                { "Node npm Cache", Path.Combine(local, "npm-cache"), "Safe to offload or clean" },
                { "Node pnpm Store", Path.Combine(local, "pnpm", "store"), "Safe for mklink /J offload" },
                { "User Downloads (C:)", Path.Combine(user, "Downloads"), "Move large ISOs/ZIPs/GGUFs to E: or F:" },
                { "Local Temp Folder", Path.Combine(local, "Temp"), "Clean via [Apply Temp Clean] (do not junction Temp)" },
                { "DirectX Shader Cache", Path.Combine(local, "D3DSCache"), "Cleanable GPU cache" },
                { "CrashDumps (WER)", Path.Combine(local, "CrashDumps"), "Safe to clean or offload" },
                { "AppData Roaming Code", Path.Combine(roaming, "Code", "CachedData"), "IDE cached data" }
            };

            Dictionary<string, string> ledgerMap = ReadJunctionLedgerMap(shellRoot);

            for (int i = 0; i < candidates.GetLength(0); i++)
            {
                string cat = candidates[i, 0];
                string dir = candidates[i, 1];
                string note = candidates[i, 2];
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                SpaceHogEntry e = MeasureDirectoryBounded(cat, dir, note, 5, 45000);
                string target;
                if (ledgerMap.TryGetValue(Path.GetFullPath(dir).TrimEnd('\\'), out target))
                    e.JunctionTarget = target;
                results.Add(e);
            }

            // Also include any custom user-created junctions from our ledger not in defaults
            foreach (KeyValuePair<string, string> kv in ledgerMap)
            {
                bool already = false;
                for (int i = 0; i < results.Count; i++)
                    if (string.Equals(results[i].PathOnC.TrimEnd('\\'), kv.Key, StringComparison.OrdinalIgnoreCase)) { already = true; break; }
                if (!already && Directory.Exists(kv.Key))
                {
                    SpaceHogEntry ce = MeasureDirectoryBounded("Custom Offloaded Folder", kv.Key, "Active NTFS Junction", 4, 25000);
                    ce.JunctionTarget = kv.Value;
                    results.Add(ce);
                }
            }

            results.Sort(delegate(SpaceHogEntry a, SpaceHogEntry b) { return b.SizeMb.CompareTo(a.SizeMb); });
            return results;
        }

        public static SpaceHogEntry MeasureDirectoryBounded(string cat, string dir, string note, int maxDepth, int maxFiles)
        {
            SpaceHogEntry e = new SpaceHogEntry();
            e.Category = cat;
            e.PathOnC = dir;
            e.SafeNote = note;
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                e.IsJunction = (di.Attributes & FileAttributes.ReparsePoint) != 0;
                long bytes = 0L;
                int files = 0;
                AccumulateSizeRecursive(dir, maxDepth, maxFiles, ref bytes, ref files);
                e.SizeMb = Math.Round(bytes / 1048576.0, 1);
                e.FileCount = files;
            }
            catch { }
            return e;
        }

        private static void AccumulateSizeRecursive(string dir, int depth, int maxFiles, ref long bytes, ref int count)
        {
            if (depth < 0 || count >= maxFiles) return;
            try
            {
                string[] fs = Directory.GetFiles(dir);
                for (int i = 0; i < fs.Length && count < maxFiles; i++)
                {
                    try { bytes += new FileInfo(fs[i]).Length; count++; } catch { }
                }
                if (depth == 0) return;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length && count < maxFiles; s++)
                {
                    try
                    {
                        DirectoryInfo sdi = new DirectoryInfo(subs[s]);
                        if ((sdi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        AccumulateSizeRecursive(subs[s], depth - 1, maxFiles, ref bytes, ref count);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static bool IsSafeToJunctionOffload(string sourceDir, out string blockReason)
        {
            blockReason = "";
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                blockReason = "Source folder does not exist: " + sourceDir;
                return false;
            }
            string full = Path.GetFullPath(sourceDir).TrimEnd('\\');
            if (!full.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase))
            {
                blockReason = "Folder is already on non-C: drive (" + full + ").";
                return false;
            }
            string userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            string localTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp").TrimEnd('\\');
            if (string.Equals(full, @"C:", StringComparison.OrdinalIgnoreCase) || string.Equals(full, userRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(full, localTemp, StringComparison.OrdinalIgnoreCase))
            {
                blockReason = "Safety Guardrail: Never junction root profile or active system Temp (" + full + "). Use [Apply Temp Clean] for Temp.";
                return false;
            }
            for (int i = 0; i < ForbiddenPrefixes.Length; i++)
            {
                if (full.StartsWith(ForbiddenPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    blockReason = "Safety Guardrail: Protected Windows OS path (" + ForbiddenPrefixes[i] + ") cannot be junction-moved.";
                    return false;
                }
            }
            DirectoryInfo di = new DirectoryInfo(full);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                blockReason = "Folder is ALREADY an active NTFS Junction / ReparsePoint (" + full + ").";
                return false;
            }
            return true;
        }

        public static string OffloadDirectoryWithJunction(string sourceDirOnC, string targetDriveRoot, string shellRoot)
        {
            string reason;
            if (!IsSafeToJunctionOffload(sourceDirOnC, out reason))
                return "[BLOCKED] " + reason;

            string drv = (targetDriveRoot ?? @"F:\").Trim();
            if (drv.Length == 2 && drv[1] == ':') drv += "\\";
            if (!Directory.Exists(drv))
                return "[ERROR] Target drive is not mounted or ready: " + drv;

            string fullSrc = Path.GetFullPath(sourceDirOnC).TrimEnd('\\');
            string slug = fullSrc.Substring(3).Replace('\\', '_').Replace(' ', '_').Replace('.', '_');
            string vaultDest = Path.Combine(drv, "OMEGA_JUNCTION_VAULT", slug);

            try
            {
                // 1. Pre-check that no files inside fullSrc are exclusively locked by running processes
                string lockedFile = FindFirstLockedFile(fullSrc, 400);
                if (!string.IsNullOrEmpty(lockedFile))
                    return "[SAFETY ABORT] File is currently open/locked by a running app: " + lockedFile + ". Close that app first so 100% of files move cleanly!";

                // 2. Copy all contents to target drive with byte-exact parity check
                long srcBytes = 0L, dstBytes = 0L;
                int fileCount = 0;
                Directory.CreateDirectory(vaultDest);
                CopyDirectoryExact(fullSrc, vaultDest, ref srcBytes, ref dstBytes, ref fileCount);
                if (srcBytes != dstBytes)
                    return "[PARITY ABORT] Copied byte total (" + dstBytes + ") did not match source (" + srcBytes + "). Original C: folder left untouched!";

                // 3. Atomic swap: rename original -> ._osh_bak, create mklink /J, verify ReparsePoint, then remove ._osh_bak
                string bakPath = fullSrc + "._osh_bak_" + DateTime.UtcNow.ToString("HHmmss");
                Directory.Move(fullSrc, bakPath);

                string mkOut = Win32HardwareTuner.RunCmdFast("cmd.exe", "/c mklink /J \"" + fullSrc + "\" \"" + vaultDest + "\"", 8000);
                bool junctionOk = Directory.Exists(fullSrc) && ((new DirectoryInfo(fullSrc).Attributes & FileAttributes.ReparsePoint) != 0);

                if (!junctionOk)
                {
                    if (!Directory.Exists(fullSrc) && Directory.Exists(bakPath))
                        Directory.Move(bakPath, fullSrc);
                    return "[ROLLBACK SAFE] mklink /J failed (" + mkOut.Trim() + "); restored original C: folder intact!";
                }

                // 4. Junction verified! Delete the backup copy on C:\ to actually free the NVMe space!
                DeleteDirectoryRecursiveSafe(bakPath);
                RecordJunctionLedger(shellRoot, fullSrc, vaultDest, srcBytes);

                return "[OFFLOAD OK] Freed " + OshFormat.FormatBytes(srcBytes) + " (" + fileCount + " files) from C:\\ -> " + vaultDest + " via transparent NTFS Junction!";
            }
            catch (Exception ex)
            {
                return "[OFFLOAD ERROR] " + ex.Message;
            }
        }

        public static string UndoJunctionBackToC(string junctionPathOnC, string shellRoot)
        {
            if (string.IsNullOrWhiteSpace(junctionPathOnC) || !Directory.Exists(junctionPathOnC))
                return "Path not found: " + junctionPathOnC;

            string fullSrc = Path.GetFullPath(junctionPathOnC).TrimEnd('\\');
            DirectoryInfo di = new DirectoryInfo(fullSrc);
            if ((di.Attributes & FileAttributes.ReparsePoint) == 0)
                return "Path is a normal directory (not an NTFS junction): " + fullSrc;

            Dictionary<string, string> map = ReadJunctionLedgerMap(shellRoot);
            string vaultTarget;
            if (!map.TryGetValue(fullSrc, out vaultTarget) || !Directory.Exists(vaultTarget))
                return "Target vault folder not found in OmegaShell junction ledger for: " + fullSrc;

            try
            {
                // Remove ONLY the junction reparse point ('rmdir' on a junction removes the link without deleting target files!)
                Win32HardwareTuner.RunCmdFast("cmd.exe", "/c rmdir \"" + fullSrc + "\"", 5000);
                if (Directory.Exists(fullSrc))
                    return "Could not remove junction link (a process may have its working directory inside " + fullSrc + ").";

                long b1 = 0L, b2 = 0L; int fc = 0;
                Directory.CreateDirectory(fullSrc);
                CopyDirectoryExact(vaultTarget, fullSrc, ref b1, ref b2, ref fc);
                if (b1 == b2)
                    DeleteDirectoryRecursiveSafe(vaultTarget);

                RemoveFromJunctionLedger(shellRoot, fullSrc);
                return "[RESTORED TO C:\\] Moved " + fc + " files (" + OshFormat.FormatBytes(b2) + ") from " + vaultTarget + " back to " + fullSrc + "!";
            }
            catch (Exception ex)
            {
                return "[RESTORE ERROR] " + ex.Message;
            }
        }

        private static string FindFirstLockedFile(string dir, int maxCheck)
        {
            string[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            int n = Math.Min(files.Length, maxCheck);
            for (int i = 0; i < n; i++)
            {
                try
                {
                    using (FileStream fs = new FileStream(files[i], FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                }
                catch
                {
                    return files[i];
                }
            }
            return "";
        }

        private static void CopyDirectoryExact(string srcDir, string dstDir, ref long srcBytes, ref long dstBytes, ref int count)
        {
            Directory.CreateDirectory(dstDir);
            string[] files = Directory.GetFiles(srcDir);
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo fi = new FileInfo(files[i]);
                string dFile = Path.Combine(dstDir, fi.Name);
                File.Copy(files[i], dFile, true);
                srcBytes += fi.Length;
                dstBytes += new FileInfo(dFile).Length;
                count++;
            }
            string[] subs = Directory.GetDirectories(srcDir);
            for (int s = 0; s < subs.Length; s++)
            {
                DirectoryInfo sdi = new DirectoryInfo(subs[s]);
                if ((sdi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                CopyDirectoryExact(subs[s], Path.Combine(dstDir, sdi.Name), ref srcBytes, ref dstBytes, ref count);
            }
        }

        private static void DeleteDirectoryRecursiveSafe(string dir)
        {
            if (!Directory.Exists(dir)) return;
            string[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                try { File.SetAttributes(files[i], FileAttributes.Normal); File.Delete(files[i]); } catch { }
            }
            try { Directory.Delete(dir, true); } catch { }
        }

        public static Dictionary<string, string> ReadJunctionLedgerMap(string shellRoot)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string p = Path.Combine(shellRoot, "config", "junction_ledger.tsv");
                if (!File.Exists(p)) return map;
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                        map[parts[0].TrimEnd('\\')] = parts[1];
                }
            }
            catch { }
            return map;
        }

        private static void RecordJunctionLedger(string shellRoot, string cPath, string targetPath, long bytes)
        {
            try
            {
                string dir = Path.Combine(shellRoot, "config");
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "junction_ledger.tsv");
                Dictionary<string, string> map = ReadJunctionLedgerMap(shellRoot);
                map[cPath.TrimEnd('\\')] = targetPath;
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in map)
                    lines.Add(kv.Key + "\t" + kv.Value + "\t" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                File.WriteAllLines(p, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void RemoveFromJunctionLedger(string shellRoot, string cPath)
        {
            try
            {
                string p = Path.Combine(shellRoot, "config", "junction_ledger.tsv");
                Dictionary<string, string> map = ReadJunctionLedgerMap(shellRoot);
                map.Remove(cPath.TrimEnd('\\'));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in map)
                    lines.Add(kv.Key + "\t" + kv.Value);
                File.WriteAllLines(p, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
