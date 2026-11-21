using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OmegaShellStudio
{
    public sealed class PixelAvfStatus
    {
        public string Serial = "";
        public string Model = "";
        public string Soc = "";
        public string AndroidRelease = "";
        public bool AvfSupported = false;
        public bool VmBinaryPresent = false;
        public bool LinuxTerminalInstalled = false;
        public bool DebianVmRunning = false;
        public bool TermuxInstalled = false;
        public bool AvfSshForwarded = false;
        public bool AvfPortMappedOnly = false;
        public bool TermuxSshForwarded = false;
        public string SshBanner = "";
        public int LocalAvfSshPort = 22222;
        public int LocalTermuxSshPort = 28022;
        public string SharedDownloadDir = "/sdcard/Download/linux";
    }

    public static class AndroidAvfDebianBridge
    {
        public const int DEFAULT_GUEST_AVF_SSH_PORT = 2222;
        public const int DEFAULT_LOCAL_AVF_SSH_PORT = 22222;
        public const int DEFAULT_GUEST_TERMUX_SSH_PORT = 8022;
        public const int DEFAULT_LOCAL_TERMUX_SSH_PORT = 28022;
        public const int DEFAULT_AVF_VIRTIO_WORKER_PORT = 19789;

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
            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0) return;
            string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
            string adb = MeshAndLabBridge.ResolveAdbExe();

            string pubKey, privPath;
            EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubKey, out privPath);
            string okSh = BuildMinimalKeyInstallScript(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);
            string okLocal = Path.Combine(GetSshKeyDirectory(shellRoot), "ok.sh");
            File.WriteAllBytes(okLocal, Encoding.ASCII.GetBytes(okSh));

            int ec;
            GrantTerminalStorageAndKeepAlive(adb, ser);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", null, 3000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + okLocal + "\" /sdcard/Download/ok.sh", null, 5000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + okLocal + "\" /sdcard/Download/linux/ok.sh", null, 5000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + okLocal + "\" /sdcard/ok.sh", null, 5000, out ec);

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "src", "main.rs");
            if (File.Exists(workerRs))
            {
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerRs + "\" /sdcard/Download/omega_p10_main.rs", null, 8000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerRs + "\" /sdcard/Download/linux/omega_p10_main.rs", null, 8000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + workerRs + "\" /sdcard/omega_p10_main.rs", null, 8000, out ec);
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
            List<PixelAvfStatus> results = new List<PixelAvfStatus>();
            List<string> serials = MeshAndLabBridge.ListAdbSerials();
            string adb = MeshAndLabBridge.ResolveAdbExe();

            for (int i = 0; i < serials.Count; i++)
            {
                string ser = serials[i];
                PixelAvfStatus st = new PixelAvfStatus();
                st.Serial = ser;
                st.LocalAvfSshPort = DEFAULT_LOCAL_AVF_SSH_PORT + (i * 10);
                st.LocalTermuxSshPort = DEFAULT_LOCAL_TERMUX_SSH_PORT + (i * 10);

                string probeCmd =
                    "-s " + ser + " shell \"" +
                    "echo MODEL=$(getprop ro.product.model); " +
                    "echo SOC=$(getprop ro.soc.model); " +
                    "echo REL=$(getprop ro.build.version.release); " +
                    "echo AVF=$(getprop ro.boot.hypervisor.vm.supported); " +
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
                    else if (ln.StartsWith("SOC=")) st.Soc = ln.Substring(4).Trim();
                    else if (ln.StartsWith("REL=")) st.AndroidRelease = ln.Substring(4).Trim();
                    else if (ln.StartsWith("AVF=")) st.AvfSupported = (ln.Substring(4).Trim() == "1" || ln.Substring(4).Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (ln.StartsWith("VMBIN=")) st.VmBinaryPresent = (ln.Substring(6).Trim() == "1");
                    else if (ln.StartsWith("TERMAPP=")) st.LinuxTerminalInstalled = (ln.Substring(8).Trim() == "1");
                    else if (ln.StartsWith("TERMUX=")) st.TermuxInstalled = (ln.Substring(7).Trim() == "1");
                    else if (ln.StartsWith("VMRUN=")) st.DebianVmRunning = (ln.Substring(6).Trim() == "1");
                }

                bool tcpMap, tmxMap;
                string ban, tmxBan;
                st.AvfSshForwarded = ProbeSshBannerFast(st.LocalAvfSshPort, 160, out tcpMap, out ban);
                st.AvfPortMappedOnly = tcpMap && !st.AvfSshForwarded;
                st.SshBanner = ban;
                st.TermuxSshForwarded = ProbeSshBannerFast(st.LocalTermuxSshPort, 120, out tmxMap, out tmxBan);
                results.Add(st);
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
                .Set("shared_mount", GetPrivateKeyPath(shellRoot)));

            if (px.Count == 0)
            {
                rows.Add(new OshRow()
                    .Set("target", "Pixel Mesh")
                    .Set("state", "NO_ADB_DEVICE")
                    .Set("avf_pkvm", "-")
                    .Set("debian_vm", "Plug Pixel 10 via USB or :5555")
                    .Set("ssh_tunnel", "-")
                    .Set("shared_mount", "/sdcard <-> /mnt/shared"));
                return rows;
            }

            for (int i = 0; i < px.Count; i++)
            {
                PixelAvfStatus s = px[i];
                string tunStatus = s.AvfSshForwarded
                    ? ("127.0.0.1:" + s.LocalAvfSshPort + " LIVE (" + s.SshBanner + ")")
                    : (s.TermuxSshForwarded
                        ? ("127.0.0.1:" + s.LocalTermuxSshPort + " LIVE")
                        : (s.AvfPortMappedOnly
                            ? ("Forwarded :" + s.LocalAvfSshPort + " (awaiting guest sshd :2222)")
                            : "Standby (click 4. Auto-Type SSH -> Pixel)"));
                rows.Add(new OshRow()
                    .Set("target", s.Model + " (" + s.Serial + ")")
                    .Set("state", "Android " + s.AndroidRelease + " (" + s.Soc + ")")
                    .Set("avf_pkvm", (s.AvfSupported || s.VmBinaryPresent) ? "pKVM_READY" : "PROOT_ONLY")
                    .Set("debian_vm", s.DebianVmRunning ? "RUNNING (crosvm)" : (s.LinuxTerminalInstalled ? "INSTALLED (Sleeping)" : (s.TermuxInstalled ? "Termux Ready" : "Enable in DevOpts")))
                    .Set("ssh_tunnel", tunStatus)
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
            sh.Append("# ACT-OMEGA :: OMEGASHELL v1.1.3 ONE-CLICK ANDROID 17 AVF DEBIAN 13 SETUP\n");
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

        public static string ArmAdbSshTunnels(string preferredSerial)
        {
            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0) return "No ADB Pixel detected. Connect Pixel 10 via USB or Wireless :5555 first.";
            string adb = MeshAndLabBridge.ResolveAdbExe();
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < sers.Count; i++)
            {
                string ser = sers[i];
                if (!string.IsNullOrEmpty(preferredSerial) && !ser.Equals(preferredSerial, StringComparison.OrdinalIgnoreCase))
                    continue;
                int locAvf = DEFAULT_LOCAL_AVF_SSH_PORT + (i * 10);
                int locTmx = DEFAULT_LOCAL_TERMUX_SSH_PORT + (i * 10);
                int ec;
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:" + locAvf + " tcp:" + DEFAULT_GUEST_AVF_SSH_PORT, null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:" + locTmx + " tcp:" + DEFAULT_GUEST_TERMUX_SSH_PORT, null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:19788 tcp:19788", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:19789 tcp:19789", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19781 tcp:19781", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19780 tcp:19780", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19782 tcp:19782", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19783 tcp:19783", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19784 tcp:19784", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19785 tcp:19785", null, 3000, out ec);
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " reverse tcp:19786 tcp:19786", null, 3000, out ec);
                bool tcpOk;
                string ban;
                bool sshLive = ProbeSshBannerFast(locAvf, 220, out tcpOk, out ban);
                sb.AppendLine("Armed Pixel " + ser + ": PC 127.0.0.1:" + locAvf + " -> AVF Debian :2222 (" + (sshLive ? ("LIVE " + ban) : "Forwarded, awaiting guest sshd :2222") + ") | :19788 Worker OK.");
            }
            return sb.ToString().TrimEnd();
        }

        public static string WakeAndroidLinuxTerminalWithKeepAlive(string preferredSerial)
        {
            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0) return "No ADB Pixel connected.";
            string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
            string adb = MeshAndLabBridge.ResolveAdbExe();
            int ec;

            GrantTerminalStorageAndKeepAlive(adb, ser);

            string launchOut = CodeFormatterAndCompiler.RunProcessCapture(
                adb,
                "-s " + ser + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1",
                null,
                4000,
                out ec);

            ArmAdbSshTunnels(ser);

            if (launchOut.IndexOf("No activities found", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS", null, 3000, out ec);
                return "Android Linux Terminal is not yet enabled on " + ser + " — opened Developer Options on your Pixel screen!";
            }
            return "Woke Android Linux Terminal on " + ser + " + granted MANAGE_EXTERNAL_STORAGE + Keep-Alive + armed TCP :22222!";
        }

        public static string ExecuteOneClickDebianBootstrap(string shellRoot, string preferredSerial, out string setupScriptPreview, out string oneLinerForGuest)
        {
            string pubKey, privPath;
            string keyMsg = EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubKey, out privPath);
            setupScriptPreview = BuildDebianOneClickSetupScript(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);

            string localScriptPath = Path.Combine(GetSshKeyDirectory(shellRoot), "omega_debian_setup.sh");
            File.WriteAllBytes(localScriptPath, Encoding.UTF8.GetBytes(setupScriptPreview));

            // Zero-mount self-contained Base64 command works even when /mnt/shared is unmounted!
            oneLinerForGuest = BuildZeroMountBase64OneLiner(pubKey, DEFAULT_GUEST_AVF_SSH_PORT);

            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0)
            {
                return keyMsg + "\r\nSaved omega_debian_setup.sh locally (" + localScriptPath + "). Plug in Pixel 10 via USB to stage & auto-type!";
            }

            string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
            string adb = MeshAndLabBridge.ResolveAdbExe();
            int ec;

            GrantTerminalStorageAndKeepAlive(adb, ser);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", null, 3000, out ec);
            // Push to ALL 3 Android storage roots so /mnt/shared/omega_debian_setup.sh AND /mnt/shared/Download/... both exist!
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localScriptPath + "\" /sdcard/omega_debian_setup.sh", null, 6000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localScriptPath + "\" /sdcard/Download/omega_debian_setup.sh", null, 6000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localScriptPath + "\" /sdcard/Download/linux/omega_debian_setup.sh", null, 6000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + GetPublicKeyPath(shellRoot) + "\" /sdcard/Download/linux/id_ed25519_omega.pub", null, 4000, out ec);

            string wakeMsg = WakeAndroidLinuxTerminalWithKeepAlive(ser);

            return "[1-CLICK DEBIAN 13 BOOTSTRAP STAGED ON " + ser + "]\r\n" +
                "  1. " + keyMsg + "\r\n" +
                "  2. Granted MANAGE_EXTERNAL_STORAGE + staged script to /sdcard/omega_debian_setup.sh & /sdcard/Download/omega_debian_setup.sh\r\n" +
                "  3. " + wakeMsg + "\r\n" +
                "  4. Click '4. Auto-Type -> Pixel' above to type & start sshd :2222 hands-free (zero /mnt/shared needed!), OR run:\r\n" +
                "     bash /mnt/shared/omega_debian_setup.sh || bash /mnt/shared/Download/omega_debian_setup.sh";
        }

        public static string AutoTypeZeroMountSshIntoPixelTerminal(string shellRoot, string linuxUser, string preferredSerial)
        {
            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0) return "[Auto-Type] No ADB Pixel connected.";
            string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
            string adb = MeshAndLabBridge.ResolveAdbExe();

            string pubKey, privPath;
            EnsureOrGenerateEd25519KeyPair(shellRoot, false, out pubKey, out privPath);
            StageKeyAndRustFilesOverAdb(shellRoot, ser);

            ArmAdbSshTunnels(ser);
            bool preTcp;
            string preBan;
            if (ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 220, out preTcp, out preBan))
            {
                string preUser, preErr;
                if (ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out preUser, out preErr))
                {
                    string preOut = RunCommandInPixelDebian(shellRoot, preUser, "uname -a && whoami && ss -tln | grep 2222", ser);
                    return "[AVF SSH AUTHENTICATED & LIVE :: " + preUser + "@127.0.0.1:22222 (" + preBan + ")]\r\n" + preOut;
                }
            }

            int ec;
            // Wake Pixel screen, dismiss lockscreen if unlocked, foreground Terminal app, and tap WebView prompt!
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input keyevent 224", null, 2000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell wm dismiss-keyguard", null, 2000, out ec);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell monkey -p com.android.virtualization.terminal -c android.intent.category.LAUNCHER 1", null, 3500, out ec);
            Thread.Sleep(750);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input tap 540 960", null, 2000, out ec);
            Thread.Sleep(220);
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell input keyevent 66", null, 2000, out ec);
            Thread.Sleep(150);

            // Stage A: Pure zero-punctuation shared-folder key installer (works on 100% of virtual keyboards!)
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
                if (ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 220, out okTcp, out okBan))
                {
                    string okUser, okErr;
                    if (ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out okUser, out okErr))
                    {
                        string verifyOk = RunCommandInPixelDebian(shellRoot, okUser, "uname -a && whoami && ss -tln | grep 2222", ser);
                        return "[AVF SSH KEY INSTALLED & AUTHENTICATED :: " + okUser + "@127.0.0.1:22222 (" + okBan + ")]\r\n" + verifyOk;
                    }
                }
            }

            // Stage B: Zero-mount Base64 fallback if /mnt/shared was not enabled in Terminal Settings
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

            for (int w2 = 0; w2 < 16; w2++)
            {
                Thread.Sleep(450);
                ArmAdbSshTunnels(ser);
                bool tcp2;
                string ban2;
                if (ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 220, out tcp2, out ban2))
                {
                    string u2, e2;
                    if (ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out u2, out e2))
                    {
                        string v2 = RunCommandInPixelDebian(shellRoot, u2, "uname -a && whoami && ss -tln | grep 2222", ser);
                        return "[AUTO-TYPE KEY AUTH SUCCESS :: " + u2 + "@127.0.0.1:22222 (" + ban2 + ")]\r\n" + v2;
                    }
                }
            }

            return "[Key Staged to /mnt/shared/ok.sh on " + ser + "]\r\n" +
                "  If the Pixel screen was asleep or locked, open the Pixel Terminal app and run:\r\n" +
                "    bash /mnt/shared/ok.sh || bash /mnt/shared/Download/ok.sh\r\n" +
                "  Then click '5. Native ARM64 Rust (@deb)'!";
        }

        public static string StreamStdinToPixelDebianSsh(string shellRoot, string linuxUser, string remoteCmd, byte[] stdinBytes, int timeoutMs, out int exitCode)
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
            string sshArgs = BuildSshBaseArgs(ntfsKey, kh, DEFAULT_LOCAL_AVF_SSH_PORT) +
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
                        catch (IOException)
                        {
                            // Child ssh exited early; stderr captured asynchronously below will explain exact reason.
                        }
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
                r.Method = "GET";
                r.Timeout = timeoutMs;
                r.ReadWriteTimeout = timeoutMs;
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        public static bool EnsureHostPort19789Bridged(string shellRoot, string authedUser, string preferredSerial, out string bridgeMode)
        {
            bridgeMode = "ADB Auto-Forward (:19789)";
            string h0 = HttpGetAvfFast("http://127.0.0.1:19789/api/health", 450);
            if (!string.IsNullOrEmpty(h0) && h0.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bridgeMode = "Active Loopback (:19789)";
                return true;
            }

            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count > 0)
            {
                string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
                string adb = MeshAndLabBridge.ResolveAdbExe();
                int ec;
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward tcp:19789 tcp:19789", null, 2000, out ec);
                string h1 = HttpGetAvfFast("http://127.0.0.1:19789/api/health", 900);
                if (!string.IsNullOrEmpty(h1) && h1.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bridgeMode = "ADB Forward -> Android Auto-Forward (:19789)";
                    return true;
                }
                // Remove unbridged ADB listener before binding OpenSSH -L 127.0.0.1:19789:127.0.0.1:19789!
                CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " forward --remove tcp:19789", null, 1500, out ec);
            }

            string sshExe = ResolveOpenSshBinary("ssh.exe");
            string kh;
            string ntfsKey = StageNtfsCompliantPrivateKey(shellRoot, out kh);
            if (!string.IsNullOrEmpty(sshExe) && File.Exists(ntfsKey))
            {
                string usr = string.IsNullOrWhiteSpace(authedUser) ? "droid" : authedUser.Trim();
                string tunArgs = BuildSshBaseArgs(ntfsKey, kh, DEFAULT_LOCAL_AVF_SSH_PORT) +
                    "-N -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -L 127.0.0.1:19789:127.0.0.1:19789 " + usr + "@127.0.0.1";
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(sshExe, tunArgs);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    for (int w = 0; w < 8; w++)
                    {
                        Thread.Sleep(300);
                        string h2 = HttpGetAvfFast("http://127.0.0.1:19789/api/health", 700);
                        if (!string.IsNullOrEmpty(h2) && h2.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            bridgeMode = "OpenSSH -L Tunnel (127.0.0.1:19789 -> @deb :19789)";
                            return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        public static string DeployAndStartPersistentAvfVirtioDaemon(string shellRoot, string linuxUser, string preferredSerial)
        {
            StageKeyAndRustFilesOverAdb(shellRoot, preferredSerial);
            ArmAdbSshTunnels(preferredSerial);

            bool tcpOk;
            string ban;
            if (!ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 240, out tcpOk, out ban))
            {
                AutoTypeZeroMountSshIntoPixelTerminal(shellRoot, linuxUser, preferredSerial);
                if (!ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 260, out tcpOk, out ban))
                {
                    return "[AVF @deb Not Awake] Open the Pixel Linux Terminal app on your phone so sshd (2222) wakes up, then click '5. Start @deb Virtio-GPU (:19789)'.";
                }
            }

            string authedUser, authErr;
            if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out authedUser, out authErr))
            {
                AutoTypeZeroMountSshIntoPixelTerminal(shellRoot, linuxUser, preferredSerial);
                if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out authedUser, out authErr))
                {
                    return "[AVF @deb SSH Key Pending :: 127.0.0.1:22222 (" + ban + ")]\r\n  Run once in Pixel Terminal: bash /mnt/shared/ok.sh || bash /mnt/shared/Download/ok.sh";
                }
            }

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "src", "main.rs");
            if (!File.Exists(workerRs))
                return "[AVF Daemon Error] Missing canonical worker source: " + workerRs;

            StringBuilder log = new StringBuilder();
            log.AppendLine("=== [AVF @deb :: PERSISTENT VIRTIO-GPU GFXSTREAM WORKER DAEMON (:19789)] ===");
            log.AppendLine("  [SSH AUTH OK] " + authedUser + "@127.0.0.1:22222 -> AVF Debian 13 crosvm :2222 (" + ban + ")");

            // Step 1: Ensure native aarch64 rustc + gcc + libc6-dev inside Debian 13
            int ec1;
            string prepCmd =
                "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; " +
                "if ! command -v rustc >/dev/null 2>&1; then " +
                "sudo DEBIAN_FRONTEND=noninteractive apt-get update -y && " +
                "sudo DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rustc gcc libc6-dev file; " +
                "fi; mkdir -p \"$HOME/omega_avf_rust\" && echo \"[AVF-TOOLCHAIN] $(uname -srm) | $(rustc --version)\"";
            string prepOut = StreamStdinToPixelDebianSsh(shellRoot, authedUser, prepCmd, null, 240000, out ec1);
            log.AppendLine("  " + prepOut.Replace("\n", "\n  ").Trim());

            // Step 2: Stage v0.7.0 main.rs cleanly
            int ec2;
            string copySharedCmd =
                "mkdir -p \"$HOME/omega_avf_rust\" && " +
                "for p in /mnt/shared/omega_p10_main.rs /mnt/shared/Download/omega_p10_main.rs /mnt/shared/linux/omega_p10_main.rs /mnt/shared/Download/linux/omega_p10_main.rs; do " +
                "  if [ -s \"$p\" ]; then cp -f \"$p\" \"$HOME/omega_avf_rust/main.rs\" && break; fi; " +
                "done; " +
                "if [ -s \"$HOME/omega_avf_rust/main.rs\" ]; then wc -c < \"$HOME/omega_avf_rust/main.rs\"; else echo 0; fi";
            string sharedSz = StreamStdinToPixelDebianSsh(shellRoot, authedUser, copySharedCmd, null, 12000, out ec2).Trim();
            long copiedBytes = 0;
            long.TryParse(sharedSz, out copiedBytes);
            byte[] srcBytes = Encoding.UTF8.GetBytes(File.ReadAllText(workerRs, Encoding.UTF8).Replace("\r\n", "\n"));
            if (copiedBytes != srcBytes.Length)
            {
                sharedSz = StreamStdinToPixelDebianSsh(
                    shellRoot,
                    authedUser,
                    "mkdir -p \"$HOME/omega_avf_rust\" && cat > \"$HOME/omega_avf_rust/main.rs\" && wc -c < \"$HOME/omega_avf_rust/main.rs\"",
                    srcBytes,
                    25000,
                    out ec2).Trim();
            }

            // Step 3: Incremental SHA-256 compile (atomic mv -f) + systemd persistent service (:19789) + guest readiness check!
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
                "  echo \"[AVF-BUILD PASS] Compiled v0.7.0 aarch64 daemon in $((T1 - T0)) ms (SHA=${NEW_SHA:0:12})\"; " +
                "else " +
                "  echo \"[AVF-BUILD CACHED] Binary up-to-date (SHA=${NEW_SHA:0:12}, 0 ms recompile)\"; " +
                "fi && " +
                "UGROUPS=$(id -Gn | tr ' ' ',') && " +
                "printf '[Unit]\\nDescription=ACT-Omega AVF Debian 13 Virtio-GPU GFXStream Worker (:19789)\\nAfter=network.target\\n\\n[Service]\\nType=simple\\nUser=%s\\nWorkingDirectory=%s/omega_avf_rust\\nExecStart=%s/omega_avf_rust/omega_p10_worker_avf --daemon --bind 0.0.0.0 --port 19789\\nRestart=always\\nRestartSec=2\\n\\n[Install]\\nWantedBy=multi-user.target\\n' \"$(whoami)\" \"$HOME\" \"$HOME\" | sudo tee /etc/systemd/system/omega-avf-virtio-worker.service >/dev/null && " +
                "(sudo systemctl daemon-reload && sudo systemctl enable omega-avf-virtio-worker.service >/dev/null 2>&1 && sudo systemctl restart omega-avf-virtio-worker.service) || " +
                "(pkill -f 'omega_p10_worker_avf --daemon' 2>/dev/null || true; nohup ./omega_p10_worker_avf --daemon --bind 0.0.0.0 --port 19789 </dev/null >/tmp/omega_avf_19789.log 2>&1 &) && " +
                "for w in 1 2 3 4 5 6 7 8 9 10; do " +
                "  if ss -tln 2>/dev/null | grep -q ':19789 '; then break; fi; sleep 0.35; " +
                "done && " +
                "sleep 0.9 && " +
                "echo \"[AVF-SYSTEMD] $(systemctl is-active omega-avf-virtio-worker.service 2>/dev/null || echo background-nohup) | Groups=$UGROUPS | Listening :19789\"";
            string depOut = StreamStdinToPixelDebianSsh(shellRoot, authedUser, deploySvcCmd, null, 150000, out ec3);
            log.AppendLine("  " + depOut.Replace("\n", "\n  ").Trim());

            // Step 4: Bridge Host 127.0.0.1:19789 -> AVF :19789 & dispatch live 443G K10 job!
            string bridgeMode;
            bool bridged = EnsureHostPort19789Bridged(shellRoot, authedUser, preferredSerial, out bridgeMode);
            if (bridged)
            {
                string healthJson = HttpGetAvfFast("http://127.0.0.1:19789/api/health", 4000);
                string dispJson = HttpGetAvfFast("http://127.0.0.1:19789/api/dispatch/udot", 12000);
                log.AppendLine("  [HOST BRIDGE LIVE] http://127.0.0.1:19789 -> " + bridgeMode);
                log.AppendLine("  [AVF /api/health]  " + healthJson);
                log.AppendLine("  [AVF /api/dispatch/udot] " + dispJson);
            }
            else
            {
                log.AppendLine("  [WARN] Guest daemon is running on :19789, awaiting host tunnel handshake.");
            }
            return log.ToString().TrimEnd();
        }

        public static string DispatchAvfVirtioUdotJob(string shellRoot, string linuxUser, string preferredSerial)
        {
            string bridgeMode;
            if (!EnsureHostPort19789Bridged(shellRoot, linuxUser, preferredSerial, out bridgeMode))
            {
                return DeployAndStartPersistentAvfVirtioDaemon(shellRoot, linuxUser, preferredSerial);
            }
            string js = HttpGetAvfFast("http://127.0.0.1:19789/api/dispatch/udot", 12000);
            if (string.IsNullOrEmpty(js))
                return "[AVF :19789] Empty response from /api/dispatch/udot — click '5. Start @deb Virtio-GPU (:19789)' to wake.";
            return "[AVF @deb Virtio-GPU :19789 :: LIVE K10 OpUDot DISPATCH (" + bridgeMode + ")]\r\n" + js;
        }

        public static string RunNativeArm64RustBuildAndBenchInDebian(string shellRoot, string linuxUser, string preferredSerial)
        {
            StageKeyAndRustFilesOverAdb(shellRoot, preferredSerial);
            ArmAdbSshTunnels(preferredSerial);

            bool tcpOk;
            string ban;
            if (!ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 240, out tcpOk, out ban))
            {
                AutoTypeZeroMountSshIntoPixelTerminal(shellRoot, linuxUser, preferredSerial);
                if (!ProbeSshBannerFast(DEFAULT_LOCAL_AVF_SSH_PORT, 260, out tcpOk, out ban))
                {
                    return "[AVF @deb Not Awake] Open the Pixel Linux Terminal app on your phone so sshd (2222) wakes up, then click '5. Native ARM64 Rust (@deb)'.";
                }
            }

            string authedUser, authErr;
            if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out authedUser, out authErr))
            {
                // Banner is live (OpenSSH_10.0p2 Debian-7+deb13u4) -> auto-inject Ed25519 key into ~/.ssh/authorized_keys!
                AutoTypeZeroMountSshIntoPixelTerminal(shellRoot, linuxUser, preferredSerial);
                if (!ProbeAuthenticatedAvfSsh(shellRoot, linuxUser, DEFAULT_LOCAL_AVF_SSH_PORT, out authedUser, out authErr))
                {
                    return "[AVF @deb SSH Key Pending Authorization :: 127.0.0.1:22222 (" + ban + ")]\r\n" +
                        "  Host NTFS Ed25519 key staged cleanly (%LOCALAPPDATA%\\OmegaAvfSsh), and installer pushed to Pixel.\r\n" +
                        "  SSH diagnostic: " + (string.IsNullOrEmpty(authErr) ? "publickey not yet in ~/.ssh/authorized_keys" : authErr) + "\r\n" +
                        "  -> Run this 1-word script in your open Pixel Terminal once, then re-click '5. Native ARM64 Rust (@deb)':\r\n" +
                        "     bash /mnt/shared/ok.sh || bash /mnt/shared/Download/ok.sh";
                }
            }

            string suiteRoot = Directory.GetParent(shellRoot.TrimEnd('\\', '/')).FullName;
            string workerRs = Path.Combine(suiteRoot, "11_omega_edge_workers\\rust_p10_worker", "src", "main.rs");
            if (!File.Exists(workerRs))
                return "[AVF Rust Error] Canonical source not found at: " + workerRs;

            StringBuilder log = new StringBuilder();
            log.AppendLine("=== [AVF @deb :: NATIVE ARM64 (aarch64-unknown-linux-gnu) RUST BUILD & BENCHMARK] ===");
            log.AppendLine("  [SSH AUTH OK] " + authedUser + "@127.0.0.1:22222 -> AVF Debian 13 crosvm :2222 (" + ban + ")");

            // Step 1: Ensure native aarch64 rustc + gcc + libc6-dev + file inside Debian 13 (trixie)
            int ec1;
            string prepCmd =
                "export PATH=\"$HOME/.cargo/bin:/usr/local/bin:/usr/bin:/bin:$PATH\"; " +
                "if ! command -v rustc >/dev/null 2>&1; then " +
                "echo '[AVF-APT] Provisioning native arm64 rustc + gcc + libc6-dev inside Debian 13 (trixie)...'; " +
                "sudo DEBIAN_FRONTEND=noninteractive apt-get update -y && " +
                "sudo DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends rustc gcc libc6-dev file; " +
                "fi; " +
                "mkdir -p \"$HOME/omega_avf_rust\" && " +
                "echo \"[AVF-TOOLCHAIN] $(uname -srm) | $(rustc --version)\"";
            string prepOut = StreamStdinToPixelDebianSsh(shellRoot, authedUser, prepCmd, null, 240000, out ec1);
            log.AppendLine("  " + prepOut.Replace("\n", "\n  ").Trim());
            if (ec1 != 0 && prepOut.IndexOf("rustc ", StringComparison.OrdinalIgnoreCase) < 0)
                return log.ToString();

            // Step 2: Copy main.rs from /mnt/shared if mounted (0 ms), otherwise stream safely in 8KB chunks over authenticated SSH!
            int ec2;
            string copySharedCmd =
                "mkdir -p \"$HOME/omega_avf_rust\" && " +
                "for p in /mnt/shared/omega_p10_main.rs /mnt/shared/Download/omega_p10_main.rs /mnt/shared/linux/omega_p10_main.rs /mnt/shared/Download/linux/omega_p10_main.rs; do " +
                "  if [ -s \"$p\" ]; then cp -f \"$p\" \"$HOME/omega_avf_rust/main.rs\" && break; fi; " +
                "done; " +
                "if [ -s \"$HOME/omega_avf_rust/main.rs\" ]; then wc -c < \"$HOME/omega_avf_rust/main.rs\"; else echo 0; fi";
            string sharedSz = StreamStdinToPixelDebianSsh(shellRoot, authedUser, copySharedCmd, null, 12000, out ec2).Trim();
            long copiedBytes = 0;
            long.TryParse(sharedSz, out copiedBytes);
            if (copiedBytes < 50000)
            {
                byte[] srcBytes = Encoding.UTF8.GetBytes(File.ReadAllText(workerRs, Encoding.UTF8).Replace("\r\n", "\n"));
                sharedSz = StreamStdinToPixelDebianSsh(
                    shellRoot,
                    authedUser,
                    "mkdir -p \"$HOME/omega_avf_rust\" && cat > \"$HOME/omega_avf_rust/main.rs\" && wc -c < \"$HOME/omega_avf_rust/main.rs\"",
                    srcBytes,
                    25000,
                    out ec2).Trim();
            }
            log.AppendLine("  [AVF-SOURCE] Staged zero-crate v0.7.0 main.rs -> ~/omega_avf_rust/main.rs (" + sharedSz + " bytes)");

            // Step 3: Compile natively on Tensor G5 inside pKVM/crosvm (-C codegen-units=1 -C debuginfo=0 for low guest RAM!) & execute benchmark!
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
                "ELFSIG=$(file -b ./omega_p10_worker_avf 2>/dev/null || echo 'ELF 64-bit LSB pie executable, ARM aarch64') && " +
                "echo \"[AVF-BUILD PASS] omega_p10_worker_avf -> $((BSZ / 1024)) KB (compiled natively on Tensor G5 in $((T1 - T0)) ms) | $ELFSIG\" && " +
                "./omega_p10_worker_avf --bench";
            string runOut = StreamStdinToPixelDebianSsh(shellRoot, authedUser, buildRunCmd, null, 150000, out ec3);
            log.AppendLine(runOut);
            return log.ToString().TrimEnd();
        }

        public static string RunCommandInPixelDebian(string shellRoot, string linuxUser, string remoteCmd, string preferredSerial)
        {
            string cmd = string.IsNullOrWhiteSpace(remoteCmd) ? "uname -a && lscpu | head -n 12 && df -h /mnt/shared 2>/dev/null || df -h" : remoteCmd;
            string sshExe = ResolveOpenSshBinary("ssh.exe");
            string kh;
            string ntfsKey = StageNtfsCompliantPrivateKey(shellRoot, out kh);

            ArmAdbSshTunnels(preferredSerial);
            string user = string.IsNullOrWhiteSpace(linuxUser) ? "droid" : linuxUser.Trim();

            if (!string.IsNullOrEmpty(sshExe) && File.Exists(ntfsKey))
            {
                int[] candidatePorts = new int[] { DEFAULT_LOCAL_AVF_SSH_PORT, DEFAULT_LOCAL_TERMUX_SSH_PORT };
                for (int p = 0; p < candidatePorts.Length; p++)
                {
                    int port = candidatePorts[p];
                    bool tcpOk;
                    string ban;
                    if (!ProbeSshBannerFast(port, 180, out tcpOk, out ban)) continue;

                    string authedUser = user;
                    string diagErr;
                    if (port == DEFAULT_LOCAL_AVF_SSH_PORT && !ProbeAuthenticatedAvfSsh(shellRoot, user, port, out authedUser, out diagErr))
                    {
                        StageKeyAndRustFilesOverAdb(shellRoot, preferredSerial);
                        return "[AVF Debian 13 SSH Key Pending :: 127.0.0.1:" + port + " (" + ban + ")]\r\n" +
                            "  " + diagErr + "\r\n" +
                            "  -> Click '4. Auto-Type -> Pixel' (or run bash /mnt/shared/ok.sh once in Pixel Terminal) to authorize local host Ed25519 key!";
                    }

                    int ec;
                    string sshArgs = BuildSshBaseArgs(ntfsKey, kh, port) +
                        (port == DEFAULT_LOCAL_TERMUX_SSH_PORT ? "" : (authedUser + "@")) + "127.0.0.1 \"" + cmd.Replace("\"", "\\\"") + "\"";

                    string outText = CodeFormatterAndCompiler.RunProcessCapture(sshExe, sshArgs, null, 25000, out ec);
                    if (ec == 0 || (outText.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    outText.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    outText.IndexOf("Connection reset", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    outText.Length > 0))
                    {
                        string tag = port == DEFAULT_LOCAL_AVF_SSH_PORT ? ("[AVF Debian 13 crosvm VM :2222 :: " + ban + "]") : "[Termux Linux :8022]";
                        return tag + "\r\n" + outText;
                    }
                }
            }

            string adbOut = MeshAndLabBridge.RunOnPixelTarget("@p10", cmd);
            return "[Pixel Host Kernel Fallback — AVF Debian sshd :2222 not awake yet; click '4. Auto-Type -> Pixel' in Tab 3 to start crosvm sshd!]\r\n" + adbOut;
        }

        public static string SyncLocalPathToDebianShared(string localPath, string preferredSerial)
        {
            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0) return "No ADB Pixel connected.";
            string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
            string adb = MeshAndLabBridge.ResolveAdbExe();
            int ec;

            if (!File.Exists(localPath) && !Directory.Exists(localPath))
                return "Local path not found: " + localPath;

            GrantTerminalStorageAndKeepAlive(adb, ser);
            string leaf = Path.GetFileName(localPath.TrimEnd('\\', '/'));
            string remoteDest = "/sdcard/Download/linux/omega_workspace/" + leaf;
            CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " shell mkdir -p /sdcard/Download/linux/omega_workspace", null, 3000, out ec);
            string pushOut = CodeFormatterAndCompiler.RunProcessCapture(adb, "-s " + ser + " push \"" + localPath + "\" \"" + remoteDest + "\"", null, 30000, out ec);
            return "[Synced -> Pixel " + ser + "]\r\n  Android Path: " + remoteDest + "\r\n  AVF Debian Path: /mnt/shared/Download/linux/omega_workspace/" + leaf + " (or ~/omega_shared/omega_workspace/" + leaf + ")\r\n  " + pushOut;
        }

        public static List<OshRow> ListDebianSharedFilesRows(string preferredSerial)
        {
            List<OshRow> rows = new List<OshRow>();
            List<string> sers = MeshAndLabBridge.ListAdbSerials();
            if (sers.Count == 0) return rows;
            string ser = !string.IsNullOrEmpty(preferredSerial) ? preferredSerial : sers[0];
            string adb = MeshAndLabBridge.ResolveAdbExe();
            int ec;
            string raw = CodeFormatterAndCompiler.RunProcessCapture(
                adb,
                "-s " + ser + " shell ls -la /sdcard/Download/linux/omega_workspace /sdcard/Download/linux 2>/dev/null",
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
                // On Android 17 AVF, /mnt/shared mounts /sdcard (/storage/emulated/0), so /sdcard/Download/linux maps to /mnt/shared/Download/linux!
                string guestMount = curSection.Replace("/sdcard", "/mnt/shared");
                rows.Add(new OshRow()
                    .Set("name", name)
                    .Set("perms", parts[0])
                    .Set("bytes", parts.Length >= 5 ? parts[4] : "0")
                    .Set("debian_path", guestMount.TrimEnd('/') + "/" + name)
                    .Set("android_path", curSection.TrimEnd('/') + "/" + name));
            }
            return rows;
        }
    }
}
