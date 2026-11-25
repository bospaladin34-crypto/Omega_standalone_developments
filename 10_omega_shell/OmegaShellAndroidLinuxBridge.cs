using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace OmegaShellStudio
{
    public sealed class PixelAvfStatus
    {
        public string Serial = "";
        public string Model = "";
        public string Codename = "";
        public string Soc = "";
        public string AndroidRelease = "";
        public bool IsPixel8Slot = false;
        public bool HasRootUid0 = false;
        public bool AvfSupported = false;
        public bool VmBinaryPresent = false;
        public bool LinuxTerminalInstalled = false;
        public bool DebianVmRunning = false;
        public bool TermuxInstalled = false;
        public bool AvfSshForwarded = false;
        public bool AvfPortMappedOnly = false;
        public bool TermuxSshForwarded = false;
        public bool NativeWorkerLive = false;
        public bool AvfWorkerLive = false;
        public string SshBanner = "";
        public int LocalAvfSshPort = 22222;
        public int LocalTermuxSshPort = 28022;
        public int NativeWorkerPort = 19788;
        public int AvfWorkerPort = 19789;
        public string SharedDownloadDir = "/sdcard/Download/linux";
    }

    public static class AndroidAvfDebianBridge
    {
        public const int DEFAULT_GUEST_AVF_SSH_PORT = 2222;
        public const int DEFAULT_LOCAL_AVF_SSH_PORT = 22222;      // Pixel 10 (frankel, 58170DLCR0004V)
        public const int DEFAULT_P8_LOCAL_AVF_SSH_PORT = 22232;   // Pixel 8  (shiba,   3A121FDJH00BW5)
        public const int DEFAULT_GUEST_TERMUX_SSH_PORT = 8022;
        public const int DEFAULT_LOCAL_TERMUX_SSH_PORT = 28022;   // Pixel 10 Termux
        public const int DEFAULT_P8_LOCAL_TERMUX_SSH_PORT = 28032;// Pixel 8  Termux
        public const int DEFAULT_AVF_VIRTIO_WORKER_PORT = 19789;  // Pixel 10 5.0GB AVF Virtio-GPU
        public const int DEFAULT_P8_AVF_WORKER_PORT = 19793;      // Pixel 8  5.3GB AVF 9-vCPU SMMLA
        public const int DEFAULT_P10_NATIVE_PORT = 19788;
        public const int DEFAULT_P8_NATIVE_PORT = 19792;

        // Deterministic O(1) classifier: defeats alphabetical '3A1...' < '581...' port swap!
        public static bool IsPixel8Identity(string serial, string model, string codename, string soc)
        {
            string sig = ((serial ?? "") + " " + (model ?? "") + " " + (codename ?? "") + " " + (soc ?? "")).ToLowerInvariant();
            if (sig.Contains("3a121") || sig.Contains("shiba") || sig.Contains("pixel 8") || sig.Contains("pixel_8") || sig.Contains("zuma") || sig.Contains("tensor g3"))
                return true;
            if (sig.Contains("58170") || sig.Contains("frankel") || sig.Contains("pixel 10") || sig.Contains("pixel_10") || sig.Contains("laguna") || sig.Contains("tensor g5"))
                return false;
            return false;
        }

        public static bool IsPixel10Identity(string serial, string model, string codename, string soc)
        {
            string sig = ((serial ?? "") + " " + (model ?? "") + " " + (codename ?? "") + " " + (soc ?? "")).ToLowerInvariant();
            return sig.Contains("58170") || sig.Contains("frankel") || sig.Contains("pixel 10") || sig.Contains("pixel_10") || sig.Contains("laguna") || sig.Contains("tensor g5");
        }

        public static void AssignCanonicalPorts(PixelAvfStatus st, int extraOrdinal)
        {
            if (IsPixel8Identity(st.Serial, st.Model, st.Codename, st.Soc))
            {
                st.IsPixel8Slot = true;
                st.LocalAvfSshPort = DEFAULT_P8_LOCAL_AVF_SSH_PORT;
                st.LocalTermuxSshPort = DEFAULT_P8_LOCAL_TERMUX_SSH_PORT;
                st.NativeWorkerPort = DEFAULT_P8_NATIVE_PORT;
                st.AvfWorkerPort = DEFAULT_P8_AVF_WORKER_PORT;
            }
            else if (IsPixel10Identity(st.Serial, st.Model, st.Codename, st.Soc) || extraOrdinal == 0)
            {
                st.IsPixel8Slot = false;
                st.LocalAvfSshPort = DEFAULT_LOCAL_AVF_SSH_PORT;
                st.LocalTermuxSshPort = DEFAULT_LOCAL_TERMUX_SSH_PORT;
                st.NativeWorkerPort = DEFAULT_P10_NATIVE_PORT;
                st.AvfWorkerPort = DEFAULT_AVF_VIRTIO_WORKER_PORT;
            }
            else
            {
                st.IsPixel8Slot = true;
                st.LocalAvfSshPort = 22242 + (extraOrdinal * 10);
                st.LocalTermuxSshPort = 28042 + (extraOrdinal * 10);
                st.NativeWorkerPort = 19796 + (extraOrdinal * 2);
                st.AvfWorkerPort = 19797 + (extraOrdinal * 2);
            }
        }

        public static List<PixelAvfStatus> DiscoverFastPixelTopology()
        {
            List<PixelAvfStatus> list = new List<PixelAvfStatus>();
            string adb = MeshAndLabBridge.ResolveAdbExe();
            int ec;
            string raw = CodeFormatterAndCompiler.RunProcessCapture(adb, "devices -l", null, 1800, out ec);
            string[] lines = (raw ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int extraOrd = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || ln.StartsWith("*", StringComparison.Ordinal))
                    continue;
                string[] toks = ln.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length < 2 || !string.Equals(toks[1], "device", StringComparison.OrdinalIgnoreCase))
                    continue;
                PixelAvfStatus st = new PixelAvfStatus();
                st.Serial = toks[0].Trim();
                for (int k = 2; k < toks.Length; k++)
                {
                    if (toks[k].StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                        st.Model = toks[k].Substring(6).Replace('_', ' ');
                    else if (toks[k].StartsWith("device:", StringComparison.OrdinalIgnoreCase))
                        st.Codename = toks[k].Substring(7);
                }
                if (!IsPixel10Identity(st.Serial, st.Model, st.Codename, "") && !IsPixel8Identity(st.Serial, st.Model, st.Codename, ""))
                {
                    string prop = CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + st.Serial + " shell \"getprop ro.product.device; getprop ro.product.model; getprop ro.soc.model\"", null, 1400, out ec);
                    string[] pl = (prop ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pl.Length >= 1 && string.IsNullOrEmpty(st.Codename)) st.Codename = pl[0].Trim();
                    if (pl.Length >= 2 && string.IsNullOrEmpty(st.Model)) st.Model = pl[1].Trim();
                    if (pl.Length >= 3) st.Soc = pl[2].Trim();
                }
                AssignCanonicalPorts(st, extraOrd++);
                list.Add(st);
            }
            // Canonical order: Pixel 10 (Slot 0 :22222/:19788/:19789) first, Pixel 8 (Slot 1 :22232/:19792/:19793) second!
            list.Sort(delegate(PixelAvfStatus a, PixelAvfStatus b)
            {
                int ra = a.IsPixel8Slot ? 1 : 0;
                int rb = b.IsPixel8Slot ? 1 : 0;
                if (ra != rb) return ra.CompareTo(rb);
                return string.Compare(a.Serial, b.Serial, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static List<PixelAvfStatus> ResolveTargetsForSpec(string preferredSpec)
        {
            List<PixelAvfStatus> all = DiscoverFastPixelTopology();
            if (all.Count == 0) return all;

            string spec = (preferredSpec ?? "").Trim().ToLowerInvariant();
            if (spec == "both" || spec == "all" || spec == "@pixels" || spec == "@deb-all" || spec == "dual")
                return all;

            bool wantP8 = (spec == "p8" || spec == "@p8" || spec == "pixel8" || spec == "pixel_8" || spec == "@deb8" || spec == "shiba" || spec == "22232" || spec == "19793");
            bool wantP10 = (string.IsNullOrEmpty(spec) || spec == "p10" || spec == "@p10" || spec == "pixel10" || spec == "pixel_10" || spec == "@deb" || spec == "@deb10" || spec == "frankel" || spec == "22222" || spec == "19789");

            List<PixelAvfStatus> matched = new List<PixelAvfStatus>();
            for (int i = 0; i < all.Count; i++)
            {
                if (wantP8 && all[i].IsPixel8Slot) { matched.Add(all[i]); return matched; }
                if (wantP10 && !all[i].IsPixel8Slot) { matched.Add(all[i]); return matched; }
                if (!wantP8 && !wantP10 && all[i].Serial.Equals(preferredSpec.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(all[i]);
                    return matched;
                }
            }
            // Fallback only if explicitly unnamed (""): use first online phone
            if (string.IsNullOrEmpty(spec) && all.Count > 0)
            {
                matched.Add(all[0]);
            }
            return matched;
        }

        public static int GetSshPortForTargetSpec(string preferredSpec, out bool isP8)
        {
            List<PixelAvfStatus> t = ResolveTargetsForSpec(preferredSpec);
            if (t.Count > 0)
            {
                isP8 = t[0].IsPixel8Slot;
                return t[0].LocalAvfSshPort;
            }
            string sp = (preferredSpec ?? "").Trim().ToLowerInvariant();
            if (sp == "p8" || sp == "@p8" || sp == "pixel8" || sp == "@deb8" || sp == "shiba" || sp.StartsWith("3a121"))
            {
                isP8 = true;
                return DEFAULT_P8_LOCAL_AVF_SSH_PORT;
            }
            isP8 = false;
            return DEFAULT_LOCAL_AVF_SSH_PORT;
        }

        public static string GetSshKeyDirectory(string shellRoot)
        {
            string dir = Path.Combine(shellRoot, "config", "ssh");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetPrivateKeyPath(string shellRoot)
        {
            return Path.Combine(GetSshKeyDirectory(shellRoot), "id_ed25519_omega");
        }

        public static string GetPublicKeyPath(string shellRoot)
        {
            return GetPrivateKeyPath(shellRoot) + ".pub";
        }

        public static string ResolveOpenSshBinary(string exeName)
        {
            string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", exeName);
            if (File.Exists(sys32)) return sys32;
            string winSys = @"C:\Windows\System32\OpenSSH\" + exeName;
            if (File.Exists(winSys)) return winSys;
            return CodeFormatterAndCompiler.ResolveToolBinary(exeName, null);
        }

        public static bool ProbeSshBannerFast(int port, int timeoutMs, out bool tcpAccepted, out string bannerText)
        {
            tcpAccepted = false;
            bannerText = "";
            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect("127.0.0.1", port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !tc.Connected)
                        return false;
                    tc.EndConnect(ar);
                    tcpAccepted = true;
                    tc.ReceiveTimeout = Math.Max(140, timeoutMs);
                    NetworkStream ns = tc.GetStream();
                    byte[] buf = new byte[64];
                    int n = ns.Read(buf, 0, buf.Length);
                    if (n >= 4)
                    {
                        string s = Encoding.ASCII.GetString(buf, 0, n).Trim();
                        int nl = s.IndexOfAny(new char[] { '\r', '\n' });
                        bannerText = nl > 0 ? s.Substring(0, nl) : s;
                        if (bannerText.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static string EnsureOrGenerateEd25519KeyPair(string shellRoot, bool forceRegenerate, out string pubKeyLine, out string privKeyPath)
        {
            privKeyPath = GetPrivateKeyPath(shellRoot);
            string pubPath = GetPublicKeyPath(shellRoot);
            pubKeyLine = "";

            if (forceRegenerate)
            {
                try { if (File.Exists(privKeyPath)) File.Delete(privKeyPath); } catch { }
                try { if (File.Exists(pubPath)) File.Delete(pubPath); } catch { }
            }

            if (!File.Exists(privKeyPath) || !File.Exists(pubPath))
            {
                string keygen = ResolveOpenSshBinary("ssh-keygen.exe");
                if (string.IsNullOrEmpty(keygen))
                    return "[SSH Keygen Error] Windows OpenSSH ssh-keygen.exe not found in C:\\Windows\\System32\\OpenSSH.";

                int ec;
                string args = "-t ed25519 -f \"" + privKeyPath + "\" -N \"\" -C \"omega-shell@local-host\"";
                string outMsg = CodeFormatterAndCompiler.RunProcessCapture(keygen, args, GetSshKeyDirectory(shellRoot), 10000, out ec);
                if (ec != 0 || !File.Exists(pubPath))
                    return "[SSH Keygen Failed] " + outMsg;

                LockPrivateKeyNtfsAcl(privKeyPath);
            }
            else
            {
                LockPrivateKeyNtfsAcl(privKeyPath);
            }

            pubKeyLine = File.ReadAllText(pubPath, Encoding.UTF8).Trim();
            return "Ed25519 Keypair Ready: " + privKeyPath + " (" + ShortFingerprint(pubKeyLine) + ")";
        }

        private static void LockPrivateKeyNtfsAcl(string privKeyPath)
        {
            try
            {
                WindowsIdentity wid = WindowsIdentity.GetCurrent();
                string curIdent = wid != null && !string.IsNullOrEmpty(wid.Name) ? wid.Name : Environment.UserName;
                int ec;
                CodeFormatterAndCompiler.RunProcessCapture(
                    "icacls.exe",
                    "\"" + privKeyPath + "\" /inheritance:r /grant:r \"*S-1-5-18:F\" \"*S-1-5-32-544:F\" \"" + curIdent + ":F\"",
                    Path.GetDirectoryName(privKeyPath),
                    5000,
                    out ec);
            }
            catch { }
        }

        public static string StageNtfsCompliantPrivateKey(string shellRoot, out string knownHostsPath)
        {
            string srcPriv = GetPrivateKeyPath(shellRoot);
            if (!File.Exists(srcPriv))
            {
                string pubDummy, privDummy;
                EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubDummy, out privDummy);
            }

            string baseLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseLocal) || !Directory.Exists(Path.GetPathRoot(baseLocal)))
                baseLocal = @"C:\Users\Public";
            string safeDir = Path.Combine(baseLocal, "OmegaAvfSsh");
            Directory.CreateDirectory(safeDir);

            bool isAdmin = false;
            try
            {
                WindowsIdentity wid = WindowsIdentity.GetCurrent();
                if (wid != null)
                {
                    WindowsPrincipal wp = new WindowsPrincipal(wid);
                    isAdmin = wp.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { }

            string keyLeaf = isAdmin ? "id_ed25519_omega_adm" : "id_ed25519_omega_usr";
            string dstPriv = Path.Combine(safeDir, keyLeaf);
            knownHostsPath = Path.Combine(safeDir, isAdmin ? "known_hosts_adm" : "known_hosts_usr");

            try
            {
                string rawPem = File.ReadAllText(srcPriv, Encoding.UTF8).Replace("\r\n", "\n").Trim() + "\n";
                if (File.Exists(dstPriv)) File.Delete(dstPriv);
                File.WriteAllBytes(dstPriv, Encoding.ASCII.GetBytes(rawPem));
                LockPrivateKeyNtfsAcl(dstPriv);
                File.WriteAllBytes(knownHostsPath, new byte[0]);
            }
            catch
            {
                return srcPriv;
            }
            return dstPriv;
        }

        public static string BuildSshBaseArgs(string ntfsPrivKey, string knownHostsPath, int port)
        {
            return "-i \"" + ntfsPrivKey + "\" -p " + port +
                " -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=\"" +
                knownHostsPath + "\" -o ConnectTimeout=5 ";
        }

        public static bool ProbeAuthenticatedAvfSsh(string shellRoot, string preferredUser, int port, out string authedUser, out string diagError)
        {
            authedUser = string.IsNullOrWhiteSpace(preferredUser) ? "droid" : preferredUser.Trim();
            diagError = "";
            string sshExe = ResolveOpenSshBinary("ssh.exe");
            if (string.IsNullOrEmpty(sshExe))
            {
                diagError = "ssh.exe not found";
                return false;
            }
            string kh;
            string ntfsKey = StageNtfsCompliantPrivateKey(shellRoot, out kh);
            if (!File.Exists(ntfsKey))
            {
                diagError = "Ed25519 private key missing";
                return false;
            }

            List<string> users = new List<string>();
            if (!string.IsNullOrWhiteSpace(preferredUser)) users.Add(preferredUser.Trim());
            string[] fallbacks = new string[] { "droid", "debian", "root" };
            for (int i = 0; i < fallbacks.Length; i++)
            {
                if (!users.Contains(fallbacks[i])) users.Add(fallbacks[i]);
            }

            string baseArgs = BuildSshBaseArgs(ntfsKey, kh, port);
            for (int u = 0; u < users.Count; u++)
            {
                int ec;
                string outTxt = CodeFormatterAndCompiler.RunProcessCapture(
                    sshExe,
                    baseArgs + users[u] + "@127.0.0.1 \"echo OMEGA_SSH_AUTH_OK:$(whoami)\"",
                    null,
                    6000,
                    out ec);
                int marker = (outTxt ?? "").IndexOf("OMEGA_SSH_AUTH_OK:", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    string rest = outTxt.Substring(marker + "OMEGA_SSH_AUTH_OK:".Length).Trim();
                    int nl = rest.IndexOfAny(new char[] { '\r', '\n', ' ' });
                    authedUser = nl > 0 ? rest.Substring(0, nl) : (rest.Length > 0 ? rest : users[u]);
                    return true;
                }
                if (u == 0) diagError = (outTxt ?? "").Trim();
            }
            return false;
        }

        public static string BuildMinimalKeyInstallScript(string ed25519PubKey, int avfSshPort)
        {
            string cleanKey = (ed25519PubKey ?? "").Replace("'", "").Replace("\r", "").Replace("\n", "").Trim();
            StringBuilder sh = new StringBuilder();
            sh.Append("#!/usr/bin/env bash\n");
            sh.Append("chmod go-w \"$HOME\" 2>/dev/null || true\n");
            sh.Append("mkdir -p \"$HOME/.ssh\" && chmod 700 \"$HOME/.ssh\"\n");
            sh.Append("touch \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("grep -Fq '" + cleanKey + "' \"$HOME/.ssh/authorized_keys\" 2>/dev/null || echo '" + cleanKey + "' >> \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("chmod 600 \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("if command -v sudo >/dev/null 2>&1; then\n");
            sh.Append("  sudo mkdir -p /root/.ssh && sudo chmod 700 /root/.ssh\n");
            sh.Append("  sudo touch /root/.ssh/authorized_keys\n");
            sh.Append("  sudo grep -Fq '" + cleanKey + "' /root/.ssh/authorized_keys 2>/dev/null || echo '" + cleanKey + "' | sudo tee -a /root/.ssh/authorized_keys >/dev/null\n");
            sh.Append("  sudo chmod 600 /root/.ssh/authorized_keys\n");
            sh.Append("  sudo mkdir -p /etc/ssh/sshd_config.d /run/sshd && sudo chmod 0755 /run/sshd\n");
            sh.Append("  printf 'Port " + avfSshPort + "\\nPubkeyAuthentication yes\\nAuthorizedKeysFile .ssh/authorized_keys\\nListenAddress 0.0.0.0\\n' | sudo tee /etc/ssh/sshd_config.d/99-omegashell.conf >/dev/null\n");
            sh.Append("  sudo ssh-keygen -A >/dev/null 2>&1 || true\n");
            sh.Append("  if ss -tln 2>/dev/null | grep -q ':" + avfSshPort + " '; then\n");
            sh.Append("    sudo systemctl reload ssh >/dev/null 2>&1 || sudo pkill -HUP -x sshd >/dev/null 2>&1 || true\n");
            sh.Append("  else\n");
            sh.Append("    sudo systemctl disable --now ssh.socket >/dev/null 2>&1 || true\n");
            sh.Append("    sudo /usr/sbin/sshd -p " + avfSshPort + " >/dev/null 2>&1 || sudo systemctl restart ssh >/dev/null 2>&1 || true\n");
            sh.Append("  fi\n");
            sh.Append("fi\n");
            sh.Append("echo \"[OMEGA_KEY_INSTALLED] user=$(whoami) port=" + avfSshPort + "\"\n");
            return sh.ToString();
        }

        public static void StageKeyAndRustFilesOverAdb(string shellRoot, string preferredSerial)
        {
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(preferredSerial);
            if (targets.Count == 0) return;
            string adb = MeshAndLabBridge.ResolveAdbExe();

            string pubKey, privPath;
            EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubKey, out privPath);
            string okSh = BuildMinimalKeyInstallScript(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);
            string okLocal = Path.Combine(GetSshKeyDirectory(shellRoot), "ok.sh");
            File.WriteAllBytes(okLocal, Encoding.ASCII.GetBytes(okSh));

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "src", "main.rs");
            string workerC = Path.Combine(suiteRoot, "11_omega_edge_workers\\c11_universal_worker", "omega_pocket_worker.c");
            string p8BootSh = Path.Combine(suiteRoot, "11_omega_edge_workers\\c11_universal_worker", "bootstrap_avf_debian.sh");
            string pubPath = GetPublicKeyPath(shellRoot);

            for (int t = 0; t < targets.Count; t++)
            {
                string ser = targets[t].Serial;
                int ec;
                GrantTerminalStorageAndKeepAlive(adb, ser);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + okLocal + "\" /sdcard/Download/ok.sh", null, 5000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + okLocal + "\" /sdcard/Download/linux/ok.sh", null, 5000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + okLocal + "\" /sdcard/ok.sh", null, 5000, out ec);
                if (File.Exists(pubPath))
                {
                    CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + pubPath + "\" /sdcard/Download/omega_rnd_ed25519.pub", null, 4000, out ec);
                    CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + pubPath + "\" /sdcard/Download/linux/id_ed25519_omega.pub", null, 4000, out ec);
                }

                if (targets[t].IsPixel8Slot)
                {
                    if (File.Exists(workerC))
                    {
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerC + "\" /sdcard/Download/omega_pocket_worker.c", null, 6000, out ec);
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerC + "\" /sdcard/Download/linux/omega_pocket_worker.c", null, 6000, out ec);
                    }
                    if (File.Exists(p8BootSh))
                    {
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + p8BootSh + "\" /sdcard/Download/omega_p8_avf_boot.sh", null, 5000, out ec);
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + p8BootSh + "\" /sdcard/Download/linux/omega_p8_avf_boot.sh", null, 5000, out ec);
                    }
                }
                else
                {
                    if (File.Exists(workerRs))
                    {
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerRs + "\" /sdcard/Download/omega_p10_main.rs", null, 8000, out ec);
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerRs + "\" /sdcard/Download/linux/omega_p10_main.rs", null, 8000, out ec);
                        CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerRs + "\" /sdcard/omega_p10_main.rs", null, 8000, out ec);
                    }
                }
            }
        }

        private static string ShortFingerprint(string pubLine)
        {
            if (string.IsNullOrEmpty(pubLine)) return "empty";
            string sha = OshFormat.Sha256Hex(pubLine);
            return "SHA256:" + (sha.Length >= 16 ? sha.Substring(0, 16) : sha);
        }

        public static void GrantTerminalStorageAndKeepAlive(string adb, string ser)
        {
            int ec;
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell appops set com.android.virtualization.terminal MANAGE_EXTERNAL_STORAGE allow", null, 2500, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell cmd appops set com.android.virtualization.terminal RUN_IN_BACKGROUND allow", null, 2500, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell cmd appops set com.android.virtualization.terminal RUN_ANY_IN_BACKGROUND allow", null, 2500, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell dumpsys deviceidle whitelist +com.android.virtualization.terminal", null, 2500, out ec);
        }

        public static List<PixelAvfStatus> ProbeAllPixels()
        {
            List<PixelAvfStatus> results = DiscoverFastPixelTopology();
            string adb = MeshAndLabBridge.ResolveAdbExe();

            // Always ensure canonical non-swapped ADB port forwards when probing!
            if (results.Count > 0)
            {
                ArmAdbSshTunnels("both");
            }

            for (int i = 0; i < results.Count; i++)
            {
                PixelAvfStatus st = results[i];
                string ser = st.Serial;

                string probeCmd =
                    "-s " + ser + " shell \"" +
                    "echo MODEL=$(getprop ro.product.model); " +
                    "echo CODE=$(getprop ro.product.device); " +
                    "echo SOC=$(getprop ro.soc.model); " +
                    "echo REL=$(getprop ro.build.version.release); " +
                    "echo AVF=$(getprop ro.boot.hypervisor.vm.supported); " +
                    "su -c id 2>/dev/null | grep -q 'uid=0' && echo ROOT0=1 || echo ROOT0=0; " +
                    "test -x /apex/com.android.virt/bin/vm && echo VMBIN=1 || echo VMBIN=0; " +
                    "pm path com.android.virtualization.terminal >/dev/null 2>&1 && echo TERMAPP=1 || echo TERMAPP=0; " +
                    "pm path com.termux >/dev/null 2>&1 && echo TERMUX=1 || echo TERMUX=0; " +
                    "ps -A 2>/dev/null | grep -E 'crosvm|virtualization\\.terminal' >/dev/null 2>&1 && echo VMRUN=1 || echo VMRUN=0" +
                    "\"";

                int ec;
                string raw = CodeFormatterAndCompiler.RunProcessCapture(adb, probeCmd, null, 4000, out ec);
                string[] lines = (raw ?? "").Split('\n');
                for (int k = 0; k < lines.Length; k++)
                {
                    string ln = lines[k].Trim();
                    if (ln.StartsWith("MODEL=")) st.Model = ln.Substring(6).Trim();
                    else if (ln.StartsWith("CODE=")) st.Codename = ln.Substring(5).Trim();
                    else if (ln.StartsWith("SOC=")) st.Soc = ln.Substring(4).Trim();
                    else if (ln.StartsWith("REL=")) st.AndroidRelease = ln.Substring(4).Trim();
                    else if (ln.StartsWith("ROOT0=")) st.HasRootUid0 = (ln.Substring(6).Trim() == "1");
                    else if (ln.StartsWith("AVF=")) st.AvfSupported = (ln.Substring(4).Trim() == "1" || ln.Substring(4).Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (ln.StartsWith("VMBIN=")) st.VmBinaryPresent = (ln.Substring(6).Trim() == "1");
                    else if (ln.StartsWith("TERMAPP=")) st.LinuxTerminalInstalled = (ln.Substring(8).Trim() == "1");
                    else if (ln.StartsWith("TERMUX=")) st.TermuxInstalled = (ln.Substring(7).Trim() == "1");
                    else if (ln.StartsWith("VMRUN=")) st.DebianVmRunning = (ln.Substring(6).Trim() == "1");
                }
                AssignCanonicalPorts(st, i);

                bool tcpMap, tmxMap;
                string ban, tmxBan;
                st.AvfSshForwarded = ProbeSshBannerFast(st.LocalAvfSshPort, 180, out tcpMap, out ban);
                st.AvfPortMappedOnly = tcpMap && !st.AvfSshForwarded;
                st.SshBanner = ban;
                st.TermuxSshForwarded = ProbeSshBannerFast(st.LocalTermuxSshPort, 120, out tmxMap, out tmxBan);

                string natH = HttpGetAvfFast("http://127.0.0.1:" + st.NativeWorkerPort + "/api/health", 450);
                st.NativeWorkerLive = !string.IsNullOrEmpty(natH) && natH.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                string avfH = HttpGetAvfFast("http://127.0.0.1:" + st.AvfWorkerPort + "/api/health", 450);
                st.AvfWorkerLive = !string.IsNullOrEmpty(avfH) && avfH.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return results;
        }

        public static List<OshRow> BuildAvfStatusRows(string shellRoot)
        {
            List<PixelAvfStatus> px = ProbeAllPixels();
            List<OshRow> rows = new List<OshRow>();

            string pubPath = GetPublicKeyPath(shellRoot);
            bool hasKey = File.Exists(pubPath);
            rows.Add(new OshRow()
                .Set("target", "Host OpenSSH Key")
                .Set("state", hasKey ? "ED25519_READY" : "NOT_GENERATED")
                .Set("avf_pkvm", "Windows 11 OpenSSH")
                .Set("debian_vm", ResolveOpenSshBinary("ssh.exe") != "" ? "ssh.exe OK" : "Missing")
                .Set("ssh_tunnel", hasKey ? ShortFingerprint(File.ReadAllText(pubPath, Encoding.UTF8).Trim()) : "Run 'avf keygen'")
                .Set("workers", "Host DirectCompute + Dual-Pixel")
                .Set("shared_mount", GetPrivateKeyPath(shellRoot)));

            if (px.Count == 0)
            {
                rows.Add(new OshRow()
                    .Set("target", "Dual-Pixel Mesh")
                    .Set("state", "NO_ADB_DEVICE")
                    .Set("avf_pkvm", "-")
                    .Set("debian_vm", "Plug Pixel 10 / Pixel 8 via USB or :5555")
                    .Set("ssh_tunnel", "-")
                    .Set("workers", "-")
                    .Set("shared_mount", "/sdcard <-> /mnt/shared"));
                return rows;
            }

            for (int i = 0; i < px.Count; i++)
            {
                PixelAvfStatus s = px[i];
                string roleBadge = s.IsPixel8Slot ? "[@p8/@deb8]" : "[@p10/@deb10]";
                string tunStatus = s.AvfSshForwarded
                    ? ("127.0.0.1:" + s.LocalAvfSshPort + " LIVE (" + s.SshBanner + ")")
                    : (s.TermuxSshForwarded
                        ? ("127.0.0.1:" + s.LocalTermuxSshPort + " LIVE")
                        : (s.AvfPortMappedOnly
                            ? ("Forwarded :" + s.LocalAvfSshPort + " (awaiting guest sshd :2222)")
                            : ("Standby :" + s.LocalAvfSshPort)));
                string wkStatus = ":" + s.NativeWorkerPort + "=" + (s.NativeWorkerLive ? "LIVE" : "Idle") +
                    " | AVF :" + s.AvfWorkerPort + "=" + (s.AvfWorkerLive ? "LIVE" : "Idle");
                rows.Add(new OshRow()
                    .Set("target", roleBadge + " " + s.Model + " (" + s.Serial + ")")
                    .Set("state", "Android " + s.AndroidRelease + " (" + s.Soc + (s.HasRootUid0 ? ", ksud" : "") + ")")
                    .Set("avf_pkvm", (s.AvfSupported || s.VmBinaryPresent) ? (s.IsPixel8Slot ? "pKVM_5.3GB" : "pKVM_5.0GB") : "PROOT_ONLY")
                    .Set("debian_vm", s.DebianVmRunning ? "RUNNING (crosvm)" : (s.LinuxTerminalInstalled ? "INSTALLED (Sleeping)" : (s.TermuxInstalled ? "Termux Ready" : "Enable in DevOpts")))
                    .Set("ssh_tunnel", tunStatus)
                    .Set("workers", wkStatus)
                    .Set("shared_mount", "/sdcard -> /mnt/shared (Download/linux)"));
            }
            return rows;
        }

        public static string BuildCompactZeroMountPayload(string ed25519PubKey, int avfSshPort)
        {
            string cleanKey = (ed25519PubKey ?? "").Replace("'", "").Trim();
            StringBuilder sh = new StringBuilder();
            sh.Append("mkdir -p \"$HOME/.ssh\" && chmod 700 \"$HOME/.ssh\"\n");
            sh.Append("touch \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("grep -Fq '" + cleanKey + "' \"$HOME/.ssh/authorized_keys\" 2>/dev/null || echo '" + cleanKey + "' >> \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("chmod 600 \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("if [ ! -x /usr/sbin/sshd ]; then sudo apt-get update -y && sudo apt-get install -y openssh-server; fi\n");
            sh.Append("sudo mkdir -p /etc/ssh/sshd_config.d\n");
            sh.Append("printf 'Port " + avfSshPort + "\\nPubkeyAuthentication yes\\nPasswordAuthentication no\\nListenAddress 0.0.0.0\\n' | sudo tee /etc/ssh/sshd_config.d/99-omegashell.conf >/dev/null\n");
            sh.Append("sudo ssh-keygen -A >/dev/null 2>&1 || true\n");
            sh.Append("sudo systemctl disable --now ssh.socket >/dev/null 2>&1 || true\n");
            sh.Append("sudo systemctl stop ssh >/dev/null 2>&1 || true\n");
            sh.Append("sudo pkill -f '/usr/sbin/sshd' 2>/dev/null || true\n");
            sh.Append("sudo mkdir -p -m 0755 /run/sshd\n");
            sh.Append("sudo systemctl enable --now ssh >/dev/null 2>&1 || true\n");
            sh.Append("ss -tln 2>/dev/null | grep -q ':" + avfSshPort + " ' || (sudo mkdir -p -m 0755 /run/sshd && sudo /usr/sbin/sshd -p " + avfSshPort + ")\n");
            sh.Append("if [ -d /mnt/shared/Download/linux ]; then ln -sfn /mnt/shared/Download/linux \"$HOME/omega_shared\" 2>/dev/null || true; fi\n");
            sh.Append("echo \"[OMEGASHELL AVF LIVE] User=$(whoami) | Port=" + avfSshPort + " | Kernel=$(uname -srm)\"\n");
            return sh.ToString();
        }

        public static string BuildZeroMountBase64OneLiner(string ed25519PubKey, int avfSshPort)
        {
            string payload = BuildCompactZeroMountPayload(ed25519PubKey, avfSshPort);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            return "echo " + b64 + " | base64 -d | bash";
        }

        public static string BuildDebianOneClickSetupScript(string ed25519PubKey, int avfSshPort)
        {
            StringBuilder sh = new StringBuilder();
            sh.Append("#!/usr/bin/env bash\n");
            sh.Append("# ==========================================================================\n");
            sh.Append("# ACT-OMEGA :: OMEGASHELL v1.2.0 DUAL-PIXEL ANDROID 17 AVF DEBIAN 13 SETUP\n");
            sh.Append("# Disables Debian 13 ssh.socket (:22) and binds sshd cleanly to 0.0.0.0:2222\n");
            sh.Append("# ==========================================================================\n");
            sh.Append("set -e\n\n");
            sh.Append("PUB_KEY='" + (ed25519PubKey ?? "").Replace("'", "").Trim() + "'\n");
            sh.Append("SSH_PORT=" + avfSshPort + "\n\n");
            sh.Append("echo '>>> [1/4] Installing local host Ed25519 key into ~/.ssh/authorized_keys...'\n");
            sh.Append("mkdir -p \"$HOME/.ssh\"\n");
            sh.Append("chmod 700 \"$HOME/.ssh\"\n");
            sh.Append("touch \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("if ! grep -Fq \"$PUB_KEY\" \"$HOME/.ssh/authorized_keys\"; then\n");
            sh.Append("    echo \"$PUB_KEY\" >> \"$HOME/.ssh/authorized_keys\"\n");
            sh.Append("fi\n");
            sh.Append("chmod 600 \"$HOME/.ssh/authorized_keys\"\n\n");

            sh.Append("if [ -n \"${PREFIX:-}\" ] && [ -x \"$PREFIX/bin/pkg\" ]; then\n");
            sh.Append("    echo '>>> [2/4] Detected Termux -> Starting sshd on :8022...'\n");
            sh.Append("    pkg update -y && pkg install -y openssh clang git rust\n");
            sh.Append("    pkill sshd 2>/dev/null || true\n");
            sh.Append("    sshd\n");
            sh.Append("    exit 0\n");
            sh.Append("fi\n\n");

            sh.Append("echo '>>> [2/4] Checking Debian 13 (trixie) OpenSSH daemon...'\n");
            sh.Append("SUDO_CMD=''\n");
            sh.Append("if [ \"$(id -u)\" -ne 0 ] && command -v sudo >/dev/null 2>&1; then SUDO_CMD='sudo'; fi\n");
            sh.Append("if [ ! -x /usr/sbin/sshd ]; then\n");
            sh.Append("    $SUDO_CMD apt-get update -y\n");
            sh.Append("    $SUDO_CMD apt-get install -y openssh-server build-essential git curl pkg-config\n");
            sh.Append("fi\n\n");

            sh.Append("echo \"=== [3/4] Disabling ssh.socket (:22) & binding sshd to 0.0.0.0:$SSH_PORT ===\"\n");
            sh.Append("$SUDO_CMD mkdir -p /etc/ssh/sshd_config.d\n");
            sh.Append("printf 'Port %s\\nPubkeyAuthentication yes\\nPasswordAuthentication no\\nListenAddress 0.0.0.0\\n' \"$SSH_PORT\" | $SUDO_CMD tee /etc/ssh/sshd_config.d/99-omegashell.conf >/dev/null\n");
            sh.Append("$SUDO_CMD ssh-keygen -A >/dev/null 2>&1 || true\n");
            sh.Append("$SUDO_CMD systemctl disable --now ssh.socket >/dev/null 2>&1 || true\n");
            sh.Append("$SUDO_CMD systemctl stop ssh >/dev/null 2>&1 || true\n");
            sh.Append("$SUDO_CMD pkill -f '/usr/sbin/sshd' 2>/dev/null || true\n");
            sh.Append("$SUDO_CMD mkdir -p -m 0755 /run/sshd\n");
            sh.Append("$SUDO_CMD systemctl enable --now ssh >/dev/null 2>&1 || true\n");
            sh.Append("ss -tln 2>/dev/null | grep -q \":$SSH_PORT \" || ($SUDO_CMD mkdir -p -m 0755 /run/sshd && $SUDO_CMD /usr/sbin/sshd -p \"$SSH_PORT\")\n\n");

            sh.Append("echo '>>> [4/4] Linking /mnt/shared/Download/linux/omega_workspace...'\n");
            sh.Append("if [ -d /mnt/shared/Download/linux ]; then\n");
            sh.Append("    mkdir -p /mnt/shared/Download/linux/omega_workspace\n");
            sh.Append("    ln -sfn /mnt/shared/Download/linux \"$HOME/omega_shared\" 2>/dev/null || true\n");
            sh.Append("elif [ -d /mnt/shared ]; then\n");
            sh.Append("    mkdir -p /mnt/shared/omega_workspace 2>/dev/null || true\n");
            sh.Append("fi\n");
            sh.Append("echo \"[OMEGASHELL AVF READY] User=$(whoami) | Port=$SSH_PORT | Kernel=$(uname -srm)\"\n");
            return sh.ToString();
        }

        // Deterministically arms NON-OVERLAPPING ports for Pixel 10 (:22222/:19788/:19789) & Pixel 8 (:22232/:19792/:19793)!
        public static string ArmAdbSshTunnels(string preferredSerial)
        {
            List<PixelAvfStatus> all = DiscoverFastPixelTopology();
            if (all.Count == 0) return "No ADB Pixel detected. Connect Pixel 10 or Pixel 8 via USB or Wireless :5555 first.";
            string adb = MeshAndLabBridge.ResolveAdbExe();
            StringBuilder sb = new StringBuilder();

            string spec = (preferredSerial ?? "").Trim().ToLowerInvariant();
            bool filterSpecific = !string.IsNullOrEmpty(spec) && spec != "both" && spec != "all" && spec != "@pixels" && spec != "@deb-all" && spec != "dual";
            List<PixelAvfStatus> chosen = filterSpecific ? ResolveTargetsForSpec(preferredSerial) : all;
            if (chosen.Count == 0) chosen = all;

            int[] revPorts = new int[] { 19777, 9778, 19779, 19780, 19781, 19782, 19783, 19784, 19785, 19786, 19790, 19791, 19794, 19795 };

            for (int i = 0; i < chosen.Count; i++)
            {
                PixelAvfStatus p = chosen[i];
                string ser = p.Serial;
                int ec;
                if (p.IsPixel8Slot)
                {
                    CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward --remove tcp:19790", null, 1200, out ec);
                    CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward --remove tcp:19791", null, 1200, out ec);
                }
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:" + p.LocalAvfSshPort + " tcp:" + DEFAULT_GUEST_AVF_SSH_PORT, null, 2500, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:" + p.LocalTermuxSshPort + " tcp:" + DEFAULT_GUEST_TERMUX_SSH_PORT, null, 2500, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:" + p.NativeWorkerPort + " tcp:" + p.NativeWorkerPort, null, 2500, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:" + p.AvfWorkerPort + " tcp:" + p.AvfWorkerPort, null, 2500, out ec);
                for (int r = 0; r < revPorts.Length; r++)
                {
                    CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:" + revPorts[r] + " tcp:" + revPorts[r], null, 1500, out ec);
                }

                bool tcpOk;
                string ban;
                bool sshLive = ProbeSshBannerFast(p.LocalAvfSshPort, 220, out tcpOk, out ban);
                string tag = p.IsPixel8Slot ? "Pixel 8 (@p8/@deb8)" : "Pixel 10 (@p10/@deb10)";
                sb.AppendLine("Armed " + tag + " [" + ser + "]: SSH 127.0.0.1:" + p.LocalAvfSshPort + " -> :2222 (" +
                    (sshLive ? ("LIVE " + ban) : "Forwarded") + ") | Native :" + p.NativeWorkerPort + " | AVF :" + p.AvfWorkerPort);
            }
            return sb.ToString().TrimEnd();
        }

        public static string WakeAndroidLinuxTerminalWithKeepAlive(string preferredSerial)
        {
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "both" : preferredSerial);
            if (targets.Count == 0) return "No ADB Pixel connected.";
            string adb = MeshAndLabBridge.ResolveAdbExe();
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < targets.Count; i++)
            {
                PixelAvfStatus p = targets[i];
                int ec;
                GrantTerminalStorageAndKeepAlive(adb, p.Serial);
                string launchOut = CodeFormatterAndCompiler.RunProcessCapture(
                    adb,
                    "-s " + p.Serial + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1",
                    null,
                    4000,
                    out ec);
                ArmAdbSshTunnels(p.Serial);

                // Also wake native worker if present on device!
                if (p.IsPixel8Slot)
                {
                    CodeFormatterAndCompiler.RunProcessCapture(
                        adb,
                        "-s " + p.Serial + " shell \"if [ -x /data/local/tmp/omega_pocket_worker ]; then su -c 'killall -q omega_pocket_worker 2>/dev/null; /data/local/tmp/omega_pocket_worker --daemonize --stationary-boost --port 19792' 2>/dev/null || (/data/local/tmp/omega_pocket_worker --daemonize --port 19792); fi\"",
                        null,
                        2500,
                        out ec);
                }
                else
                {
                    CodeFormatterAndCompiler.RunProcessCapture(
                        adb,
                        "-s " + p.Serial + " shell \"test -x /data/local/tmp/omega_p10_worker && (/data/local/tmp/omega_p10_worker --daemonize --port 19788)\"",
                        null,
                        2500,
                        out ec);
                }

                if (launchOut.IndexOf("No activities found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + p.Serial + " shell am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS", null, 3000, out ec);
                    sb.AppendLine("[" + p.Serial + "] Linux Terminal not enabled yet -- opened Developer Options.");
                }
                else
                {
                    sb.AppendLine("Woke AVF Linux Terminal + Native Worker on " + (p.IsPixel8Slot ? "Pixel 8" : "Pixel 10") + " (" + p.Serial + " -> SSH :" + p.LocalAvfSshPort + ", Native :" + p.NativeWorkerPort + ", AVF :" + p.AvfWorkerPort + ")!");
                }
            }
            return sb.ToString().TrimEnd();
        }

        public static string ExecuteOneClickDebianBootstrap(string shellRoot, string preferredSerial, out string setupScriptPreview, out string oneLinerForGuest)
        {
            string pubKey, privPath;
            string keyMsg = EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubKey, out privPath);
            setupScriptPreview = BuildDebianOneClickSetupScript(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);

            string localScriptPath = Path.Combine(GetSshKeyDirectory(shellRoot), "omega_debian_setup.sh");
            File.WriteAllBytes(localScriptPath, Encoding.UTF8.GetBytes(setupScriptPreview));

            oneLinerForGuest = BuildZeroMountBase64OneLiner(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);

            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "both" : preferredSerial);
            if (targets.Count == 0)
            {
                return keyMsg + "\r\nSaved omega_debian_setup.sh locally (" + localScriptPath + "). Plug in Pixel 10 or Pixel 8 via USB to stage & auto-type!";
            }

            string adb = MeshAndLabBridge.ResolveAdbExe();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[1-CLICK DUAL-PIXEL DEBIAN 13 BOOTSTRAP]");
            sb.AppendLine("  Key: " + keyMsg);
            for (int i = 0; i < targets.Count; i++)
            {
                PixelAvfStatus p = targets[i];
                string ser = p.Serial;
                int ec;
                StageKeyAndRustFilesOverAdb(shellRoot, ser);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localScriptPath + "\" /sdcard/omega_debian_setup.sh", null, 6000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localScriptPath + "\" /sdcard/Download/omega_debian_setup.sh", null, 6000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localScriptPath + "\" /sdcard/Download/linux/omega_debian_setup.sh", null, 6000, out ec);
                string wakeMsg = WakeAndroidLinuxTerminalWithKeepAlive(ser);
                sb.AppendLine("  -> " + wakeMsg);
            }
            sb.AppendLine("  Next: Click '4. Auto-Type SSH' or '5. Start @deb Daemon' above!");
            return sb.ToString().TrimEnd();
        }

        public static string AutoTypeZeroMountSshIntoPixelTerminal(string shellRoot, string linuxUser, string preferredSerial)
        {
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(preferredSerial);
            if (targets.Count == 0) return "[Auto-Type] No matching ADB Pixel connected.";
            if (targets.Count > 1)
            {
                StringBuilder allSb = new StringBuilder();
                for (int i = 0; i < targets.Count; i++)
                {
                    if (i > 0) allSb.AppendLine();
                    allSb.AppendLine(AutoTypeSinglePixel(shellRoot, linuxUser, targets[i]));
                }
                return allSb.ToString().TrimEnd();
            }
            return AutoTypeSinglePixel(shellRoot, linuxUser, targets[0]);
        }

        private static string AutoTypeSinglePixel(string shellRoot, string linuxUser, PixelAvfStatus target)
        {
            string ser = target.Serial;
            int sshPort = target.LocalAvfSshPort;
            string label = target.IsPixel8Slot ? "Pixel 8 (@deb8)" : "Pixel 10 (@deb10)";
            string adb = MeshAndLabBridge.ResolveAdbExe();

            string pubKey, privPath;
            EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubKey, out privPath);
            StageKeyAndRustFilesOverAdb(shellRoot, ser);
            ArmAdbSshTunnels(ser);

            bool preTcp;
            string preBan;
            if (ProbeSshBannerFast(sshPort, 220, out preTcp, out preBan))
            {
                string preUser, preErr;
                if (ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, sshPort, out preUser, out preErr))
                {
                    string preOut = RunCommandInPixelDebian(shellRoot, preUser, "uname -a && whoami && ss -tln | grep 2222", ser);
                    return "[" + label + " SSH AUTHENTICATED & LIVE :: " + preUser + "@127.0.0.1:" + sshPort + " (" + preBan + ")]\r\n" + preOut;
                }
            }

            int ec;
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input keyevent 224", null, 2000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell wm dismiss-keyguard", null, 2000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1", null, 3500, out ec);
            Thread.Sleep(750);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input tap 540 960", null, 2000, out ec);
            Thread.Sleep(220);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input keyevent 66", null, 2000, out ec);
            Thread.Sleep(150);

            string[] zeroPunctCmds = new string[]
            {
                "bash%s/mnt/shared/ok.sh",
                "bash%s/mnt/shared/Download/ok.sh",
                "bash%s/mnt/shared/linux/ok.sh"
            };
            for (int c = 0; c < zeroPunctCmds.Length; c++)
            {
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input text " + zeroPunctCmds[c], null, 2500, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input keyevent 66", null, 2000, out ec);
                Thread.Sleep(320);
            }

            for (int w = 0; w < 8; w++)
            {
                Thread.Sleep(420);
                ArmAdbSshTunnels(ser);
                bool okTcp;
                string okBan;
                if (ProbeSshBannerFast(sshPort, 220, out okTcp, out okBan))
                {
                    string okUser, okErr;
                    if (ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, sshPort, out okUser, out okErr))
                    {
                        string verifyOk = RunCommandInPixelDebian(shellRoot, okUser, "uname -a && whoami && ss -tln | grep 2222", ser);
                        return "[" + label + " SSH KEY INSTALLED & AUTHENTICATED :: " + okUser + "@127.0.0.1:" + sshPort + " (" + okBan + ")]\r\n" + verifyOk;
                    }
                }
            }

            string payload = BuildMinimalKeyInstallScript(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input tap 540 960", null, 2000, out ec);
            Thread.Sleep(150);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell \"input text 'echo%s'\"", null, 2500, out ec);
            int chunk = 72;
            for (int off = 0; off < b64.Length; off += chunk)
            {
                string part = b64.Substring(off, Math.Min(chunk, b64.Length - off));
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell \"input text '" + part + "'\"", null, 2500, out ec);
                Thread.Sleep(60);
            }
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell \"input text '%s|base64%s-d|bash'\"", null, 2500, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input keyevent 66", null, 2000, out ec);

            for (int w2 = 0; w2 < 14; w2++)
            {
                Thread.Sleep(450);
                ArmAdbSshTunnels(ser);
                bool tcp2;
                string ban2;
                if (ProbeSshBannerFast(sshPort, 220, out tcp2, out ban2))
                {
                    string u2, e2;
                    if (ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, sshPort, out u2, out e2))
                    {
                        string v2 = RunCommandInPixelDebian(shellRoot, u2, "uname -a && whoami && ss -tln | grep 2222", ser);
                        return "[" + label + " AUTO-TYPE KEY AUTH SUCCESS :: " + u2 + "@127.0.0.1:" + sshPort + " (" + ban2 + ")]\r\n" + v2;
                    }
                }
            }

            return "[Key Staged to /mnt/shared/ok.sh on " + label + " (" + ser + ")]\r\n" +
                "  Open the Pixel Terminal app and run: bash /mnt/shared/ok.sh || bash /mnt/shared/Download/ok.sh";
        }

        public static string StreamStdinToPixelDebianSsh(string shellRoot, string linuxUser, string remoteCmd, byte[] stdinBytes, int timeoutMs, out int exitCode)
        {
            return StreamStdinToPixelDebianSshOnPort(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, remoteCmd, stdinBytes, timeoutMs, out exitCode);
        }

        public static string StreamStdinToPixelDebianSshOnPort(string shellRoot, string linuxUser, int sshPort, string remoteCmd, byte[] stdinBytes, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            string sshExe = ResolveOpenSshBinary("ssh.exe");
            if (string.IsNullOrEmpty(sshExe))
                return "[AVF SSH Error] Missing Windows OpenSSH ssh.exe.";

            string kh;
            string ntfsKey = StageNtfsCompliantPrivateKey(shellRoot, out kh);
            if (!File.Exists(ntfsKey))
                return "[AVF SSH Error] Missing Ed25519 private key (" + ntfsKey + ").";

            string user = string.IsNullOrWhiteSpace(linuxUser) ? "droid" : linuxUser.Trim();
            string sshArgs = BuildSshBaseArgs(ntfsKey, kh, sshPort) +
                user + "@127.0.0.1 \"" + remoteCmd.Replace("\"", "\\\"") + "\"";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(sshExe, sshArgs);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    StringBuilder outSb = new StringBuilder();
                    StringBuilder errSb = new StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) outSb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) errSb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (stdinBytes != null && stdinBytes.Length > 0)
                    {
                        try
                        {
                            int off = 0;
                            while (off < stdinBytes.Length && !p.HasExited)
                            {
                                int n = Math.Min(8192, stdinBytes.Length - off);
                                p.StandardInput.BaseStream.Write(stdinBytes, off, n);
                                off += n;
                            }
                            p.StandardInput.BaseStream.Flush();
                        }
                        catch (IOException) { }
                    }
                    try { p.StandardInput.Close(); } catch { }

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return "[AVF SSH Timeout after " + (timeoutMs / 1000) + "s]\r\n" + outSb.ToString();
                    }
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                    string comb = outSb.ToString().TrimEnd();
                    string err = errSb.ToString().Trim();
                    if (err.Length > 0 && comb.IndexOf(err, StringComparison.Ordinal) < 0)
                        comb = (comb.Length > 0 ? (comb + "\r\n") : "") + err;
                    return comb;
                }
            }
            catch (Exception ex)
            {
                return "[AVF SSH Exception] " + ex.Message;
            }
        }

        public static string HttpGetAvfFast(string url, int timeoutMs)
        {
            try
            {
                System.Net.HttpWebRequest r = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                r.Proxy = null;
                r.Method = "GET";
                r.Timeout = timeoutMs;
                r.ReadWriteTimeout = timeoutMs;
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        public static bool EnsureHostPortBridgedForNode(string shellRoot, string authedUser, string serial, int sshPort, int workerPort, out string bridgeMode)
        {
            bridgeMode = "ADB Auto-Forward (:" + workerPort + ")";
            string healthUrl = "http://127.0.0.1:" + workerPort + "/api/health";
            string h0 = HttpGetAvfFast(healthUrl, 450);
            if (!string.IsNullOrEmpty(h0) && h0.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bridgeMode = "Active Loopback (:" + workerPort + ")";
                return true;
            }

            string adb = MeshAndLabBridge.ResolveAdbExe();
            if (!string.IsNullOrEmpty(serial))
            {
                int ec;
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + serial + " forward tcp:" + workerPort + " tcp:" + workerPort, null, 2000, out ec);
                string h1 = HttpGetAvfFast(healthUrl, 900);
                if (!string.IsNullOrEmpty(h1) && h1.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bridgeMode = "ADB Forward -> Android Relay (:" + workerPort + ")";
                    return true;
                }
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + serial + " forward --remove tcp:" + workerPort, null, 1500, out ec);
            }

            string sshExe = ResolveOpenSshBinary("ssh.exe");
            string kh;
            string ntfsKey = StageNtfsCompliantPrivateKey(shellRoot, out kh);
            if (!string.IsNullOrEmpty(sshExe) && File.Exists(ntfsKey))
            {
                string usr = string.IsNullOrWhiteSpace(authedUser) ? "droid" : authedUser.Trim();
                string tunArgs = BuildSshBaseArgs(ntfsKey, kh, sshPort) +
                    "-N -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -L 127.0.0.1:" + workerPort + ":127.0.0.1:" + workerPort + " " + usr + "@127.0.0.1";
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(sshExe, tunArgs);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    for (int w = 0; w < 8; w++)
                    {
                        Thread.Sleep(300);
                        string h2 = HttpGetAvfFast(healthUrl, 700);
                        if (!string.IsNullOrEmpty(h2) && h2.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            bridgeMode = "OpenSSH -L Tunnel (127.0.0.1:" + workerPort + " -> SSH :" + sshPort + ")";
                            return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        public static bool EnsureHostPort19789Bridged(string shellRoot, string authedUser, string preferredSerial, out string bridgeMode)
        {
            List<PixelAvfStatus> p10 = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "p10" : preferredSerial);
            string ser = p10.Count > 0 ? p10[0].Serial : "";
            int sshPort = p10.Count > 0 ? p10[0].LocalAvfSshPort : DEFAULT_LOCAL_AVF_SSH_PORT;
            int wkPort = p10.Count > 0 ? p10[0].AvfWorkerPort : DEFAULT_AVF_VIRTIO_WORKER_PORT;
            return EnsureHostPortBridgedForNode(shellRoot, authedUser, ser, sshPort, wkPort, out bridgeMode);
        }

        public static bool EnsureHostPort19793Bridged(string shellRoot, string authedUser, out string bridgeMode)
        {
            List<PixelAvfStatus> p8 = ResolveTargetsForSpec("p8");
            string ser = p8.Count > 0 ? p8[0].Serial : "";
            return EnsureHostPortBridgedForNode(shellRoot, authedUser, ser, DEFAULT_P8_LOCAL_AVF_SSH_PORT, DEFAULT_P8_AVF_WORKER_PORT, out bridgeMode);
        }

        public static string DeployAndStartPersistentAvfVirtioDaemon(string shellRoot, string linuxUser, string preferredSerial)
        {
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "p10" : preferredSerial);
            if (targets.Count == 0)
                return "[AVF Daemon] No matching Pixel connected over ADB for target '" + (preferredSerial ?? "p10") + "'.";

            StringBuilder combined = new StringBuilder();
            for (int i = 0; i < targets.Count; i++)
            {
                if (i > 0) combined.AppendLine();
                if (targets[i].IsPixel8Slot)
                    combined.AppendLine(DeploySinglePixel8AvfDaemon(shellRoot, linuxUser, targets[i]));
                else
                    combined.AppendLine(DeploySinglePixel10AvfDaemon(shellRoot, linuxUser, targets[i]));
            }
            return combined.ToString().TrimEnd();
        }

        private static string DeploySinglePixel10AvfDaemon(string shellRoot, string linuxUser, PixelAvfStatus p10)
        {
            StageKeyAndRustFilesOverAdb(shellRoot, p10.Serial);
            ArmAdbSshTunnels(p10.Serial);

            bool tcpOk;
            string ban;
            if (!ProbeSshBannerFast(p10.LocalAvfSshPort, 240, out tcpOk, out ban))
            {
                AutoTypeSinglePixel(shellRoot, linuxUser, p10);
                if (!ProbeSshBannerFast(p10.LocalAvfSshPort, 260, out tcpOk, out ban))
                {
                    return "[Pixel 10 @deb10 Not Awake] Open Pixel 10 Linux Terminal so sshd (:2222) wakes on :" + p10.LocalAvfSshPort + ", then click '5. Start @deb Daemon'.";
                }
            }

            string authedUser, authErr;
            if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, p10.LocalAvfSshPort, out authedUser, out authErr))
            {
                AutoTypeSinglePixel(shellRoot, linuxUser, p10);
                if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, p10.LocalAvfSshPort, out authedUser, out authErr))
                {
                    return "[Pixel 10 @deb10 SSH Key Pending :: 127.0.0.1:" + p10.LocalAvfSshPort + " (" + ban + ")]\r\n  Run once in Pixel 10 Terminal: bash /mnt/shared/ok.sh || bash /mnt/shared/Download/ok.sh";
                }
            }

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "src", "main.rs");
            if (!File.Exists(workerRs))
                return "[AVF Daemon Error] Missing canonical worker source: " + workerRs;

            StringBuilder log = new StringBuilder();
            log.AppendLine("=== [PIXEL 10 @deb10 :: PERSISTENT 5.0GB AVF VIRTIO-GPU WORKER (:19789)] ===");
            log.AppendLine("  [SSH AUTH OK] " + authedUser + "@127.0.0.1:" + p10.LocalAvfSshPort + " -> Pixel 10 (" + p10.Serial + ") crosvm :2222 (" + ban + ")");

            int ec1;
            string prepCmd =
                "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; " +
                "if ! command -v rustc >/dev/null 2>&1; then " +
                "sudo DEBIAN_FRONTEND=noninteractive apt-get update -y && " +
                "sudo DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rustc gcc libc6-dev file; " +
                "fi; mkdir -p \"$HOME/omega_avf_rust\" && echo \"[AVF-TOOLCHAIN] $(uname -srm) | $(rustc --version)\"";
            string prepOut = StreamStdinToPixelDebianSshOnPort(shellRoot, authedUser, p10.LocalAvfSshPort, prepCmd, null, 240000, out ec1);
            log.AppendLine("  " + prepOut.Replace("\n", "\n  ").Trim());

            int ec2;
            string copySharedCmd =
                "mkdir -p \"$HOME/omega_avf_rust\" && " +
                "for p in /mnt/shared/omega_p10_main.rs /mnt/shared/Download/omega_p10_main.rs /mnt/shared/linux/omega_p10_main.rs /mnt/shared/Download/linux/omega_p10_main.rs; do " +
                "  if [ -s \"$p\" ]; then cp -f \"$p\" \"$HOME/omega_avf_rust/main.rs\" && break; fi; " +
                "done; " +
                "if [ -s \"$HOME/omega_avf_rust/main.rs\" ]; then wc -c < \"$HOME/omega_avf_rust/main.rs\"; else echo 0; fi";
            string sharedSz = StreamStdinToPixelDebianSshOnPort(shellRoot, authedUser, p10.LocalAvfSshPort, copySharedCmd, null, 12000, out ec2).Trim();
            long copiedBytes = 0;
            long.TryParse(sharedSz, out copiedBytes);
            byte[] srcBytes = Encoding.UTF8.GetBytes(File.ReadAllText(workerRs, Encoding.UTF8).Replace("\r\n", "\n"));
            if (copiedBytes != srcBytes.Length)
            {
                StreamStdinToPixelDebianSshOnPort(
                    shellRoot,
                    authedUser,
                    p10.LocalAvfSshPort,
                    "mkdir -p \"$HOME/omega_avf_rust\" && cat > \"$HOME/omega_avf_rust/main.rs\" && wc -c < \"$HOME/omega_avf_rust/main.rs\"",
                    srcBytes,
                    25000,
                    out ec2);
            }

            int ec3;
            string deploySvcCmd =
                "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; " +
                "cd \"$HOME/omega_avf_rust\" && " +
                "NEW_SHA=$(sha256sum main.rs | awk '{print $1}') && " +
                "OLD_SHA=$(cat main.rs.sha256 2>/dev/null || echo none) && " +
                "if [ ! -x ./omega_p10_worker_avf ] || [ \"$NEW_SHA\" != \"$OLD_SHA\" ]; then " +
                "  T0=$(date +%s%3N 2>/dev/null || echo 0) && " +
                "  (rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 -C 'target-feature=+neon,+dotprod,+lse' main.rs -o omega_p10_worker_avf.new -l dl 2>&1 " +
                "   || rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 main.rs -o omega_p10_worker_avf.new -l dl 2>&1 " +
                "   || rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 main.rs -o omega_p10_worker_avf.new 2>&1) && " +
                "  chmod 755 ./omega_p10_worker_avf.new && mv -f ./omega_p10_worker_avf.new ./omega_p10_worker_avf && " +
                "  echo \"$NEW_SHA\" > main.rs.sha256 && " +
                "  T1=$(date +%s%3N 2>/dev/null || echo 0) && " +
                "  echo \"[AVF-BUILD PASS] Compiled v0.7.5 aarch64 daemon in $((T1 - T0)) ms (SHA=${NEW_SHA:0:12})\"; " +
                "else " +
                "  echo \"[AVF-BUILD CACHED] Binary up-to-date (SHA=${NEW_SHA:0:12}, 0 ms recompile)\"; " +
                "fi && " +
                "printf '[Unit]\\nDescription=ACT-Omega Pixel 10 AVF Debian 13 Virtio-GPU Worker (:19789)\\nAfter=network.target\\n\\n[Service]\\nType=simple\\nUser=%s\\nWorkingDirectory=%s/omega_avf_rust\\nExecStart=%s/omega_avf_rust/omega_p10_worker_avf --daemon --bind 0.0.0.0 --port 19789\\nRestart=always\\nRestartSec=2\\n\\n[Install]\\nWantedBy=multi-user.target\\n' \"$(whoami)\" \"$HOME\" \"$HOME\" | sudo tee /etc/systemd/system/omega-avf-virtio-worker.service >/dev/null && " +
                "(sudo systemctl daemon-reload && sudo systemctl enable omega-avf-virtio-worker.service >/dev/null 2>&1 && sudo systemctl restart omega-avf-virtio-worker.service) || " +
                "(pkill -f 'omega_p10_worker_avf --daemon' 2>/dev/null || true; nohup ./omega_p10_worker_avf --daemon --bind 0.0.0.0 --port 19789 </dev/null >/tmp/omega_avf_19789.log 2>&1 &) && " +
                "for w in 1 2 3 4 5 6 7 8 9 10; do if ss -tln 2>/dev/null | grep -q ':19789 '; then break; fi; sleep 0.35; done && " +
                "echo \"[AVF-SYSTEMD] Listening :19789\"";
            string depOut = StreamStdinToPixelDebianSshOnPort(shellRoot, authedUser, p10.LocalAvfSshPort, deploySvcCmd, null, 150000, out ec3);
            log.AppendLine("  " + depOut.Replace("\n", "\n  ").Trim());

            string bridgeMode;
            bool bridged = EnsureHostPortBridgedForNode(shellRoot, authedUser, p10.Serial, p10.LocalAvfSshPort, p10.AvfWorkerPort, out bridgeMode);
            if (bridged)
            {
                string healthJson = HttpGetAvfFast("http://127.0.0.1:19789/api/health", 4000);
                string dispJson = HttpGetAvfFast("http://127.0.0.1:19789/api/dispatch/udot", 12000);
                log.AppendLine("  [HOST BRIDGE LIVE] http://127.0.0.1:19789 -> " + bridgeMode);
                log.AppendLine("  [AVF /api/health]  " + healthJson);
                log.AppendLine("  [AVF /api/dispatch/udot] " + dispJson);
            }
            return log.ToString().TrimEnd();
        }

        private static string DeploySinglePixel8AvfDaemon(string shellRoot, string linuxUser, PixelAvfStatus p8)
        {
            StageKeyAndRustFilesOverAdb(shellRoot, p8.Serial);
            ArmAdbSshTunnels(p8.Serial);

            // Also ensure Pixel 8 Native ksud worker (:19792) is awake!
            string adb = MeshAndLabBridge.ResolveAdbExe();
            int ecW;
            CodeFormatterAndCompiler.RunProcessCapture(
                adb,
                "-s " + p8.Serial + " shell \"if [ -x /data/local/tmp/omega_pocket_worker ]; then su -c 'killall -q omega_pocket_worker 2>/dev/null; /data/local/tmp/omega_pocket_worker --daemonize --stationary-boost --port 19792' 2>/dev/null || (/data/local/tmp/omega_pocket_worker --daemonize --port 19792); fi\"",
                null,
                3000,
                out ecW);

            bool tcpOk;
            string ban;
            if (!ProbeSshBannerFast(p8.LocalAvfSshPort, 240, out tcpOk, out ban))
            {
                AutoTypeSinglePixel(shellRoot, linuxUser, p8);
                if (!ProbeSshBannerFast(p8.LocalAvfSshPort, 260, out tcpOk, out ban))
                {
                    return "[Pixel 8 @deb8 Not Awake] Open Pixel 8 Linux Terminal so sshd (:2222) wakes on :" + p8.LocalAvfSshPort + ", then click '5. Start @deb Daemon'.";
                }
            }

            string authedUser, authErr;
            if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, p8.LocalAvfSshPort, out authedUser, out authErr))
            {
                AutoTypeSinglePixel(shellRoot, linuxUser, p8);
                if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, p8.LocalAvfSshPort, out authedUser, out authErr))
                {
                    return "[Pixel 8 @deb8 SSH Key Pending :: 127.0.0.1:" + p8.LocalAvfSshPort + " (" + ban + ")]\r\n  Run once in Pixel 8 Terminal: bash /mnt/shared/ok.sh || bash /mnt/shared/Download/ok.sh";
                }
            }

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerC = Path.Combine(suiteRoot, "11_omega_edge_workers\\c11_universal_worker", "omega_pocket_worker.c");
            if (!File.Exists(workerC))
                return "[Pixel 8 AVF Error] Missing C11 worker source: " + workerC;

            StringBuilder log = new StringBuilder();
            log.AppendLine("=== [PIXEL 8 @deb8 :: PERSISTENT 5.3GB AVF 9-vCPU SMMLA WORKER (:19793)] ===");
            log.AppendLine("  [SSH AUTH OK] " + authedUser + "@127.0.0.1:" + p8.LocalAvfSshPort + " -> Pixel 8 (" + p8.Serial + ") crosvm :2222 (" + ban + ")");

            byte[] cBytes = Encoding.UTF8.GetBytes(File.ReadAllText(workerC, Encoding.UTF8).Replace("\r\n", "\n"));
            int ecS;
            StreamStdinToPixelDebianSshOnPort(
                shellRoot,
                authedUser,
                p8.LocalAvfSshPort,
                "mkdir -p \"$HOME/omega_p8_avf\" && cat > \"$HOME/omega_p8_avf/omega_pocket_worker.c\"",
                cBytes,
                25000,
                out ecS);

            int ecB;
            string p8DeployCmd =
                "if ! command -v gcc >/dev/null 2>&1; then " +
                "  sudo DEBIAN_FRONTEND=noninteractive apt-get update -qq && " +
                "  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq gcc libc6-dev; " +
                "fi && " +
                "cd \"$HOME/omega_p8_avf\" && " +
                "NEW_SHA=$(sha256sum omega_pocket_worker.c | awk '{print $1}') && " +
                "OLD_SHA=$(cat omega_pocket_worker.sha256 2>/dev/null || echo none) && " +
                "if [ ! -x ./omega_p8_avf_worker ] || [ \"$NEW_SHA\" != \"$OLD_SHA\" ]; then " +
                "  (gcc -std=c11 -O3 -march=armv8.6-a+dotprod+i8mm+fp16 -pthread omega_pocket_worker.c -ldl -lm -o omega_p8_avf_worker.new 2>/dev/null " +
                "   || gcc -std=c11 -O3 -march=armv8.2-a+dotprod -pthread omega_pocket_worker.c -ldl -lm -o omega_p8_avf_worker.new 2>/dev/null " +
                "   || gcc -std=c11 -O3 -pthread omega_pocket_worker.c -ldl -lm -o omega_p8_avf_worker.new) && " +
                "  chmod 755 ./omega_p8_avf_worker.new && mv -f ./omega_p8_avf_worker.new ./omega_p8_avf_worker && " +
                "  echo \"$NEW_SHA\" > omega_pocket_worker.sha256 && " +
                "  echo \"[AVF-BUILD PASS] Compiled Pixel 8 9-vCPU SMMLA worker v2.0.0 (SHA=${NEW_SHA:0:12})\"; " +
                "else " +
                "  echo \"[AVF-BUILD CACHED] Pixel 8 5.3GB AVF binary up-to-date (SHA=${NEW_SHA:0:12})\"; " +
                "fi && " +
                "printf '[Unit]\\nDescription=Omega Pixel 8 5.3GB AVF 9-vCPU SMMLA Worker (:19793)\\nAfter=network.target\\n\\n[Service]\\nType=simple\\nUser=%s\\nWorkingDirectory=%s/omega_p8_avf\\nExecStart=%s/omega_p8_avf/omega_p8_avf_worker --daemon --bind 0.0.0.0 --port 19793\\nRestart=always\\nRestartSec=2\\n\\n[Install]\\nWantedBy=multi-user.target\\n' \"$(whoami)\" \"$HOME\" \"$HOME\" | sudo tee /etc/systemd/system/omega-p8-avf-worker.service >/dev/null && " +
                "(sudo systemctl daemon-reload && sudo systemctl enable omega-p8-avf-worker.service >/dev/null 2>&1 && sudo systemctl restart omega-p8-avf-worker.service) || " +
                "(killall -q omega_p8_avf_worker 2>/dev/null || true; ./omega_p8_avf_worker --daemonize --bind 0.0.0.0 --port 19793) && " +
                "for w in 1 2 3 4 5 6 7 8 9 10; do if ss -tln 2>/dev/null | grep -q ':19793 '; then break; fi; sleep 0.35; done && " +
                "echo \"[AVF-SYSTEMD] Pixel 8 5.3GB Debian VM Listening :19793\"";
            string depOut = StreamStdinToPixelDebianSshOnPort(shellRoot, authedUser, p8.LocalAvfSshPort, p8DeployCmd, null, 120000, out ecB);
            log.AppendLine("  " + depOut.Replace("\n", "\n  ").Trim());

            string bridgeMode;
            bool bridged = EnsureHostPortBridgedForNode(shellRoot, authedUser, p8.Serial, p8.LocalAvfSshPort, p8.AvfWorkerPort, out bridgeMode);
            if (bridged)
            {
                string stJson = HttpGetAvfFast("http://127.0.0.1:19793/api/status", 5000);
                string dispJson = HttpGetAvfFast("http://127.0.0.1:19793/api/dispatch/gpu_cpu", 8000);
                log.AppendLine("  [HOST BRIDGE LIVE] http://127.0.0.1:19793 -> " + bridgeMode);
                log.AppendLine("  [AVF /api/status]  " + stJson);
                log.AppendLine("  [AVF /api/dispatch/gpu_cpu] " + dispJson);
            }
            return log.ToString().TrimEnd();
        }

        public static string DispatchAvfVirtioUdotJob(string shellRoot, string linuxUser, string preferredSerial)
        {
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "p10" : preferredSerial);
            if (targets.Count == 0) return "[AVF Dispatch] No matching Pixel connected.";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < targets.Count; i++)
            {
                PixelAvfStatus t = targets[i];
                string bridgeMode;
                if (!EnsureHostPortBridgedForNode(shellRoot, linuxUser, t.Serial, t.LocalAvfSshPort, t.AvfWorkerPort, out bridgeMode))
                {
                    sb.AppendLine(DeployAndStartPersistentAvfVirtioDaemon(shellRoot, linuxUser, t.Serial));
                    continue;
                }
                string ep = t.IsPixel8Slot ? "/api/dispatch/gpu_cpu" : "/api/dispatch/udot";
                string js = HttpGetAvfFast("http://127.0.0.1:" + t.AvfWorkerPort + ep, 12000);
                sb.AppendLine("[" + (t.IsPixel8Slot ? "Pixel 8 5.3GB AVF :19793 9c SMMLA" : "Pixel 10 5.0GB AVF :19789 Virtio-GPU") + " (" + bridgeMode + ")]");
                sb.AppendLine(string.IsNullOrEmpty(js) ? "  Empty response -- click '5. Start @deb Daemon'." : ("  " + js));
            }
            return sb.ToString().TrimEnd();
        }

        public static string RunNativeArm64RustBuildAndBenchInDebian(string shellRoot, string linuxUser, string preferredSerial)
        {
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "p10" : preferredSerial);
            if (targets.Count > 0 && targets[0].IsPixel8Slot && targets.Count == 1)
            {
                return DeploySinglePixel8AvfDaemon(shellRoot, linuxUser, targets[0]);
            }
            PixelAvfStatus p10 = targets.Count > 0 ? targets[0] : new PixelAvfStatus();
            int sshPort = p10.LocalAvfSshPort;
            StageKeyAndRustFilesOverAdb(shellRoot, p10.Serial);
            ArmAdbSshTunnels(p10.Serial);

            bool tcpOk;
            string ban;
            if (!ProbeSshBannerFast(sshPort, 240, out tcpOk, out ban))
            {
                if (!string.IsNullOrEmpty(p10.Serial)) AutoTypeSinglePixel(shellRoot, linuxUser, p10);
                if (!ProbeSshBannerFast(sshPort, 260, out tcpOk, out ban))
                    return "[AVF @deb Not Awake] Open Pixel Linux Terminal so sshd (:2222) wakes on :" + sshPort + ".";
            }

            string authedUser, authErr;
            if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, sshPort, out authedUser, out authErr))
            {
                if (!string.IsNullOrEmpty(p10.Serial)) AutoTypeSinglePixel(shellRoot, linuxUser, p10);
                if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, sshPort, out authedUser, out authErr))
                    return "[AVF @deb SSH Key Pending :: 127.0.0.1:" + sshPort + " (" + ban + ")] Run bash /mnt/shared/ok.sh once in Pixel Terminal.";
            }

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "src", "main.rs");
            if (!File.Exists(workerRs))
                return "[AVF Rust Error] Canonical source not found at: " + workerRs;

            StringBuilder log = new StringBuilder();
            log.AppendLine("=== [AVF @deb10 :: NATIVE ARM64 (aarch64-unknown-linux-gnu) RUST BUILD & BENCHMARK] ===");
            log.AppendLine("  [SSH AUTH OK] " + authedUser + "@127.0.0.1:" + sshPort + " -> AVF Debian 13 crosvm :2222 (" + ban + ")");

            byte[] srcBytes = Encoding.UTF8.GetBytes(File.ReadAllText(workerRs, Encoding.UTF8).Replace("\r\n", "\n"));
            int ec2;
            StreamStdinToPixelDebianSshOnPort(
                shellRoot,
                authedUser,
                sshPort,
                "mkdir -p \"$HOME/omega_avf_rust\" && cat > \"$HOME/omega_avf_rust/main.rs\"",
                srcBytes,
                25000,
                out ec2);

            int ec3;
            string buildRunCmd =
                "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; " +
                "cd \"$HOME/omega_avf_rust\" && " +
                "T0=$(date +%s%3N 2>/dev/null || echo 0); " +
                "(rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 -C 'target-feature=+neon,+dotprod,+lse' main.rs -o omega_p10_worker_avf -l dl 2>&1 " +
                "|| rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 main.rs -o omega_p10_worker_avf -l dl 2>&1 " +
                "|| rustc --edition 2021 -C opt-level=2 -C codegen-units=4 -C debuginfo=0 main.rs -o omega_p10_worker_avf 2>&1) && " +
                "T1=$(date +%s%3N 2>/dev/null || echo 0) && " +
                "chmod 755 ./omega_p10_worker_avf && " +
                "BSZ=$(wc -c < ./omega_p10_worker_avf | tr -d ' ') && " +
                "echo \"[AVF-BUILD PASS] omega_p10_worker_avf -> $((BSZ / 1024)) KB (compiled in $((T1 - T0)) ms)\" && " +
                "./omega_p10_worker_avf --bench";
            string runOut = StreamStdinToPixelDebianSshOnPort(shellRoot, authedUser, sshPort, buildRunCmd, null, 150000, out ec3);
            log.AppendLine(runOut);
            return log.ToString().TrimEnd();
        }

        public static string RunCommandInPixelDebian(string shellRoot, string linuxUser, string remoteCmd, string preferredSerial)
        {
            string cmd = string.IsNullOrWhiteSpace(remoteCmd) ? "uname -a && lscpu | head -n 12 && free -m" : remoteCmd;
            string spec = (preferredSerial ?? "").Trim().ToLowerInvariant();
            if (spec == "both" || spec == "all" || spec == "@pixels" || spec == "@deb-all" || spec == "dual")
            {
                List<PixelAvfStatus> both = DiscoverFastPixelTopology();
                if (both.Count == 0) return "No ADB Pixel connected.";
                StringBuilder bsb = new StringBuilder();
                for (int i = 0; i < both.Count; i++)
                {
                    if (i > 0) bsb.AppendLine();
                    bsb.AppendLine(RunSingleDebianCmd(shellRoot, linuxUser, cmd, both[i]));
                }
                return bsb.ToString().TrimEnd();
            }

            List<PixelAvfStatus> resolved = ResolveTargetsForSpec(preferredSerial);
            if (resolved.Count > 0)
            {
                return RunSingleDebianCmd(shellRoot, linuxUser, cmd, resolved[0]);
            }

            bool isP8;
            int fallbackSshPort = GetSshPortForTargetSpec(preferredSerial, out isP8);
            PixelAvfStatus synth = new PixelAvfStatus();
            synth.IsPixel8Slot = isP8;
            synth.LocalAvfSshPort = fallbackSshPort;
            synth.LocalTermuxSshPort = isP8 ? DEFAULT_P8_LOCAL_TERMUX_SSH_PORT : DEFAULT_LOCAL_TERMUX_SSH_PORT;
            return RunSingleDebianCmd(shellRoot, linuxUser, cmd, synth);
        }

        private static string RunSingleDebianCmd(string shellRoot, string linuxUser, string cmd, PixelAvfStatus target)
        {
            string sshExe = ResolveOpenSshBinary("ssh.exe");
            string kh;
            string ntfsKey = StageNtfsCompliantPrivateKey(shellRoot, out kh);

            if (!string.IsNullOrEmpty(target.Serial)) ArmAdbSshTunnels(target.Serial);
            string user = string.IsNullOrWhiteSpace(linuxUser) ? "droid" : linuxUser.Trim();
            string role = target.IsPixel8Slot ? "Pixel 8 @deb8" : "Pixel 10 @deb10";

            if (!string.IsNullOrEmpty(sshExe) && File.Exists(ntfsKey))
            {
                int[] candidatePorts = new int[] { target.LocalAvfSshPort, target.LocalTermuxSshPort };
                for (int p = 0; p < candidatePorts.Length; p++)
                {
                    int port = candidatePorts[p];
                    bool tcpOk;
                    string ban;
                    if (!ProbeSshBannerFast(port, 200, out tcpOk, out ban)) continue;

                    string authedUser = user;
                    string diagErr;
                    if (port == target.LocalAvfSshPort && !ProbeAuthenticatedAvfSsh(shellRoot, user, port, out authedUser, out diagErr))
                    {
                        if (!string.IsNullOrEmpty(target.Serial)) StageKeyAndRustFilesOverAdb(shellRoot, target.Serial);
                        return "[" + role + " SSH Key Pending :: 127.0.0.1:" + port + " (" + ban + ")]\r\n  " + diagErr +
                            "\r\n  -> Click '4. Auto-Type SSH' (or run bash /mnt/shared/ok.sh once in Pixel Terminal)!";
                    }

                    int ec;
                    string sshArgs = BuildSshBaseArgs(ntfsKey, kh, port) +
                        (port == target.LocalTermuxSshPort ? "" : (authedUser + "@")) + "127.0.0.1 \"" + cmd.Replace("\"", "\\\"") + "\"";

                    string outText = CodeFormatterAndCompiler.RunProcessCapture(sshExe, sshArgs, null, 25000, out ec);
                    if (ec == 0 || (outText.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    outText.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    outText.IndexOf("Connection reset", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    outText.Length > 0))
                    {
                        string tag = port == target.LocalAvfSshPort
                            ? ("[" + role + " crosvm :" + port + " -> :2222 :: " + ban + "]")
                            : ("[" + role + " Termux :" + port + "]");
                        return tag + "\r\n" + outText;
                    }
                }
            }

            string sigil = target.IsPixel8Slot ? "@p8" : "@p10";
            string adbOut = MeshAndLabBridge.RunOnPixelTarget(sigil, cmd);
            return "[" + role + " Android Kernel Fallback -- AVF sshd :" + target.LocalAvfSshPort + " sleeping; click '4. Auto-Type SSH' to wake crosvm!]\r\n" + adbOut;
        }

        public static string SyncLocalPathToDebianShared(string localPath, string preferredSerial)
        {
            if (!File.Exists(localPath) && !Directory.Exists(localPath))
                return "Local path not found: " + localPath;

            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "both" : preferredSerial);
            if (targets.Count == 0) return "No ADB Pixel connected.";
            string adb = MeshAndLabBridge.ResolveAdbExe();
            string leaf = Path.GetFileName(localPath.TrimEnd('\\', '/'));
            string remoteDest = "/sdcard/Download/linux/omega_workspace/" + leaf;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < targets.Count; i++)
            {
                string ser = targets[i].Serial;
                int ec;
                GrantTerminalStorageAndKeepAlive(adb, ser);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", null, 3000, out ec);
                string pushOut = CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localPath + "\" \"" + remoteDest + "\"", null, 30000, out ec);
                sb.AppendLine("[Synced -> " + (targets[i].IsPixel8Slot ? "Pixel 8" : "Pixel 10") + " (" + ser + ")] " + remoteDest + " <-> /mnt/shared/Download/linux/omega_workspace/" + leaf + " " + pushOut.Trim());
            }
            return sb.ToString().TrimEnd();
        }

        public static List<OshRow> ListDebianSharedFilesRows(string preferredSerial)
        {
            List<OshRow> rows = new List<OshRow>();
            List<PixelAvfStatus> targets = ResolveTargetsForSpec(string.IsNullOrEmpty(preferredSerial) ? "both" : preferredSerial);
            if (targets.Count == 0) return rows;
            string adb = MeshAndLabBridge.ResolveAdbExe();

            for (int t = 0; t < targets.Count; t++)
            {
                PixelAvfStatus px = targets[t];
                string devLabel = px.IsPixel8Slot ? "@p8" : "@p10";
                int ec;
                string raw = CodeFormatterAndCompiler.RunProcessCapture(
                    adb,
                    "-s " + px.Serial + " shell ls -la /sdcard/Download/linux/omega_workspace /sdcard/Download/linux 2>/dev/null",
                    null,
                    4000,
                    out ec);

                string curSection = "/sdcard/Download/linux";
                string[] lines = (raw ?? "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (ln.Length == 0 || ln.StartsWith("total ")) continue;
                    if (ln.EndsWith(":")) { curSection = ln.TrimEnd(':'); continue; }
                    string[] parts = ln.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 7) continue;
                    string name = parts[parts.Length - 1];
                    if (name == "." || name == "..") continue;
                    string guestMount = curSection.Replace("/sdcard", "/mnt/shared");
                    rows.Add(new OshRow()
                        .Set("device", devLabel)
                        .Set("name", name)
                        .Set("perms", parts[0])
                        .Set("bytes", parts.Length >= 5 ? parts[4] : "0")
                        .Set("debian_path", guestMount.TrimEnd('/') + "/" + name)
                        .Set("android_path", curSection.TrimEnd('/') + "/" + name));
                }
            }
            return rows;
        }
    }
}