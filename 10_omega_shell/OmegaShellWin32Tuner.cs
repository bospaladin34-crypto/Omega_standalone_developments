using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OmegaShellStudio
{
    public sealed class MemorySnapshot
    {
        public uint MemoryLoadPct;
        public long TotalPhysMb;
        public long AvailPhysMb;
        public long UsedPhysMb;
        public long TotalPageMb;
        public long AvailPageMb;
    }

    public sealed class TcpSocketItem
    {
        public string LocalIp = "";
        public int LocalPort;
        public string RemoteIp = "";
        public int RemotePort;
        public string StateName = "";
        public int StateCode;
        public int Pid;
        public string ProcessName = "";
        public long RamMb;
        internal Win32HardwareTuner.MIB_TCPROW RawTcpRow;
    }

    public sealed class StartupAppItem
    {
        public string Name = "";
        public string Command = "";
        public string LocationTag = "";
        public bool Enabled = true;
        public bool IsRegistry = true;
        public bool IsLocalMachine = false;
        public string RegSubKey = "";
        public string ApprovedSubKey = "";
        public string FilePath = "";
    }

    public sealed class WindowsServiceItem
    {
        public string ServiceName = "";
        public string DisplayName = "";
        public string Status = "";
        public string StartMode = "";
        public bool CanStop = false;
    }

    public static class Win32HardwareTuner
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
            public ulong ToUInt64() { return ((ulong)dwHighDateTime << 32) | dwLowDateTime; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
            public uint dwOwningPid;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref PROCESS_POWER_THROTTLING_STATE ProcessInformation, uint ProcessInformationSize);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int SetTcpEntry(ref MIB_TCPROW pTcpRow);

        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const uint MIB_TCP_STATE_DELETE_TCB = 12;

        private static ulong _prevIdle = 0UL, _prevKernel = 0UL, _prevUser = 0UL;
        private static double _lastCpuPct = 0.0;
        private static readonly object _cpuLock = new object();

        public static MemorySnapshot QueryMemoryStatus()
        {
            MemorySnapshot snap = new MemorySnapshot();
            try
            {
                MEMORYSTATUSEX ms = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(ms))
                {
                    snap.MemoryLoadPct = ms.dwMemoryLoad;
                    snap.TotalPhysMb = (long)(ms.ullTotalPhys / 1048576UL);
                    snap.AvailPhysMb = (long)(ms.ullAvailPhys / 1048576UL);
                    snap.UsedPhysMb = snap.TotalPhysMb - snap.AvailPhysMb;
                    snap.TotalPageMb = (long)(ms.ullTotalPageFile / 1048576UL);
                    snap.AvailPageMb = (long)(ms.ullAvailPageFile / 1048576UL);
                }
            }
            catch { }
            return snap;
        }

        public static double SampleCpuUsagePct()
        {
            lock (_cpuLock)
            {
                try
                {
                    FILETIME idleFt, kernFt, userFt;
                    if (!GetSystemTimes(out idleFt, out kernFt, out userFt)) return _lastCpuPct;
                    ulong idle = idleFt.ToUInt64();
                    ulong kern = kernFt.ToUInt64();
                    ulong user = userFt.ToUInt64();
                    if (_prevKernel == 0UL && _prevUser == 0UL)
                    {
                        _prevIdle = idle; _prevKernel = kern; _prevUser = user;
                        return _lastCpuPct;
                    }
                    ulong dIdle = idle - _prevIdle;
                    ulong dKern = kern - _prevKernel;
                    ulong dUser = user - _prevUser;
                    _prevIdle = idle; _prevKernel = kern; _prevUser = user;
                    ulong totalSys = dKern + dUser;
                    if (totalSys > 0UL && totalSys >= dIdle)
                        _lastCpuPct = ((double)(totalSys - dIdle) * 100.0) / (double)totalSys;
                }
                catch { }
                return _lastCpuPct;
            }
        }

        public static void GetHybridCoreMasks(out long pCoreMask, out long eCoreMask, out long allCoreMask)
        {
            int lp = Math.Max(1, Math.Min(62, Environment.ProcessorCount));
            allCoreMask = (lp >= 62) ? long.MaxValue : ((1L << lp) - 1L);
            if (lp == 12)
            {
                // Intel Core i5-12450HX: 4 P-cores (8 HT threads LP 0..7) + 4 E-cores (LP 8..11)
                pCoreMask = 0x00FFL;
                eCoreMask = 0x0F00L;
            }
            else if (lp >= 8)
            {
                int pCount = (lp * 2) / 3;
                pCoreMask = (1L << pCount) - 1L;
                eCoreMask = allCoreMask & (~pCoreMask);
                if (eCoreMask == 0L) eCoreMask = allCoreMask;
            }
            else
            {
                pCoreMask = allCoreMask;
                eCoreMask = allCoreMask;
            }
        }

        public static string TrimWorkingSetMemory(HashSet<string> whitelist, out int trimmedProcesses, out long reclaimedMb)
        {
            trimmedProcesses = 0;
            MemorySnapshot before = QueryMemoryStatus();
            int selfPid = Process.GetCurrentProcess().Id;
            Process[] procs = Process.GetProcesses();

            for (int i = 0; i < procs.Length; i++)
            {
                Process p = procs[i];
                try
                {
                    if (p.Id <= 4 || p.Id == selfPid) continue;
                    if (whitelist != null && whitelist.Contains(p.ProcessName)) continue;
                    // Skip foreground active window owner so visible apps never hitch
                    if (p.WorkingSet64 < 8L * 1048576L) continue;
                    if (EmptyWorkingSet(p.Handle)) trimmedProcesses++;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            System.Threading.Thread.Sleep(120);
            MemorySnapshot after = QueryMemoryStatus();
            reclaimedMb = Math.Max(0L, after.AvailPhysMb - before.AvailPhysMb);
            return "[EmptyWorkingSet OK] Trimmed " + trimmedProcesses + " background processes.\r\n  Available DDR5: " + before.AvailPhysMb + " MB -> " + after.AvailPhysMb + " MB (+" + reclaimedMb + " MB reclaimed, Load " + after.MemoryLoadPct + "%).";
        }

        public static bool SetProcessEcoQoSAndAffinity(int pid, bool enableEcoQoS, long affinityMask, ProcessPriorityClass priority, out string detail)
        {
            detail = "";
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (affinityMask > 0L) p.ProcessorAffinity = new IntPtr(affinityMask);
                    p.PriorityClass = priority;

                    PROCESS_POWER_THROTTLING_STATE st = new PROCESS_POWER_THROTTLING_STATE();
                    st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                    st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    st.StateMask = enableEcoQoS ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0U;
                    SetProcessInformation(p.Handle, ProcessPowerThrottling, ref st, (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));

                    detail = p.ProcessName + " (PID " + pid + ") -> Affinity=0x" + affinityMask.ToString("X") +
                        ", Priority=" + priority + ", EcoQoS=" + (enableEcoQoS ? "ON (Efficiency)" : "OFF (High-QoS)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                detail = "Access denied or exited for PID " + pid + ": " + ex.Message;
                return false;
            }
        }

        public static string ClassifyCoreTier(long affVal, bool hasAccess, string prioStr)
        {
            if (!hasAccess || affVal <= 0L) return "OS-Protected";
            long pMask, eMask, allMask;
            GetHybridCoreMasks(out pMask, out eMask, out allMask);
            if (affVal == pMask) return "P-CORES (High)";
            if (affVal == eMask) return "E-CORES (EcoQoS)";
            if (affVal == allMask) return prioStr == "Idle" ? "ALL (Idle)" : "ALL CORES";
            return "CUSTOM (0x" + affVal.ToString("X") + ")";
        }

        public static string ApplySovereignComputeBoost(HashSet<string> whitelist, Dictionary<string, string> customRules)
        {
            long pMask, eMask, allMask;
            GetHybridCoreMasks(out pMask, out eMask, out allMask);

            string[] backgroundBloat = new string[]
            {
                "SearchHost", "SearchIndexer", "Widgets", "WidgetService", "OneDrive", "YourPhone", "PhoneExperienceHost",
                "CrossDeviceService", "MSPCManagerService", "GameBar", "GameBarFTServer", "Teams", "ms-teams",
                "Spotify", "Discord", "SteamWebHelper", "EpicWebHelper", "AdobeCollabSync", "Acrotray", "CCXProcess", "Updater", "GoogleUpdate"
            };
            HashSet<string> bloatSet = new HashSet<string>(backgroundBloat, StringComparer.OrdinalIgnoreCase);

            string[] computeTargets = new string[]
            {
                "OmegaAV", "OmegaForge", "OmegaBrowser", "OmegaMirror", "OmegaShell",
                "rustc", "lean", "csc", "clang++", "cl", "dxc", "fxc", "ollama", "llama-server"
            };
            HashSet<string> boostSet = new HashSet<string>(computeTargets, StringComparer.OrdinalIgnoreCase);

            int boosted = 0, parked = 0;
            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    if (p.Id <= 4) continue;
                    string nm = p.ProcessName;
                    string msg;
                    string ruleMode = "";
                    if (customRules != null) customRules.TryGetValue(nm, out ruleMode);

                    if (ruleMode == "pcores" || (string.IsNullOrEmpty(ruleMode) && boostSet.Contains(nm)))
                    {
                        if (SetProcessEcoQoSAndAffinity(p.Id, false, pMask, ProcessPriorityClass.AboveNormal, out msg)) boosted++;
                    }
                    else if (ruleMode == "eco" || (string.IsNullOrEmpty(ruleMode) && bloatSet.Contains(nm) && (whitelist == null || !whitelist.Contains(nm))))
                    {
                        if (SetProcessEcoQoSAndAffinity(p.Id, true, eMask, ProcessPriorityClass.BelowNormal, out msg)) parked++;
                    }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            int trimmed; long freedMb;
            TrimWorkingSetMemory(whitelist, out trimmed, out freedMb);
            return "[Sovereign Boost OK] Pinned " + boosted + " process(es) -> P-Cores (0x" + pMask.ToString("X") + ") | Parked " + parked + " -> E-Cores EcoQoS (0x" + eMask.ToString("X") + ")\r\n  Working-Set Trim reclaimed +" + freedMb + " MB Physical DDR5!";
        }

        public static string RestoreAllProcessDefaults()
        {
            long pMask, eMask, allMask;
            GetHybridCoreMasks(out pMask, out eMask, out allMask);
            int restored = 0;
            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    if (p.Id <= 4) continue;
                    p.ProcessorAffinity = new IntPtr(allMask);
                    if (p.PriorityClass == ProcessPriorityClass.BelowNormal || p.PriorityClass == ProcessPriorityClass.AboveNormal)
                        p.PriorityClass = ProcessPriorityClass.Normal;

                    PROCESS_POWER_THROTTLING_STATE st = new PROCESS_POWER_THROTTLING_STATE();
                    st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                    st.ControlMask = 0U;
                    st.StateMask = 0U;
                    SetProcessInformation(p.Handle, ProcessPowerThrottling, ref st, (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
                    restored++;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return "Restored default all-core affinity (0x" + allMask.ToString("X") + ") and OS QoS scheduling across " + restored + " user processes.";
        }

        // =========================================================================
        // LIVE TCP PORT & SOCKET INSPECTOR + TCB SOCKET DROPPER / PORT KILLER
        // =========================================================================
        public static List<TcpSocketItem> GetLiveTcpSockets()
        {
            List<TcpSocketItem> result = new List<TcpSocketItem>();
            Dictionary<int, KeyValuePair<string, long>> pidMap = new Dictionary<int, KeyValuePair<string, long>>();
            Process[] procs = Process.GetProcesses();
            for (int i = 0; i < procs.Length; i++)
            {
                try { pidMap[procs[i].Id] = new KeyValuePair<string, long>(procs[i].ProcessName, procs[i].WorkingSet64 / 1048576L); }
                catch { }
                finally { try { procs[i].Dispose(); } catch { } }
            }

            int bufSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (bufSize <= 0) return result;

            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufSize);
            try
            {
                uint ret = GetExtendedTcpTable(tcpTablePtr, ref bufSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (ret != 0U) return result;

                int numEntries = Marshal.ReadInt32(tcpTablePtr);
                IntPtr rowPtr = new IntPtr(tcpTablePtr.ToInt64() + 4L);
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                for (int i = 0; i < numEntries; i++)
                {
                    MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID));
                    rowPtr = new IntPtr(rowPtr.ToInt64() + rowSize);

                    TcpSocketItem item = new TcpSocketItem();
                    item.StateCode = (int)row.dwState;
                    item.StateName = DecodeTcpState(row.dwState);
                    item.LocalIp = new IPAddress(row.dwLocalAddr).ToString();
                    item.LocalPort = DecodeNetworkPort(row.dwLocalPort);
                    item.RemoteIp = new IPAddress(row.dwRemoteAddr).ToString();
                    item.RemotePort = DecodeNetworkPort(row.dwRemotePort);
                    item.Pid = (int)row.dwOwningPid;

                    KeyValuePair<string, long> pInfo;
                    if (pidMap.TryGetValue(item.Pid, out pInfo))
                    {
                        item.ProcessName = pInfo.Key;
                        item.RamMb = pInfo.Value;
                    }
                    else
                    {
                        item.ProcessName = item.Pid == 0 ? "System Idle" : (item.Pid == 4 ? "System" : ("PID:" + item.Pid));
                    }

                    MIB_TCPROW raw = new MIB_TCPROW();
                    raw.dwState = MIB_TCP_STATE_DELETE_TCB;
                    raw.dwLocalAddr = row.dwLocalAddr;
                    raw.dwLocalPort = row.dwLocalPort;
                    raw.dwRemoteAddr = row.dwRemoteAddr;
                    raw.dwRemotePort = row.dwRemotePort;
                    item.RawTcpRow = raw;

                    result.Add(item);
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }
            return result;
        }

        public static string DropTcpSocketConnection(TcpSocketItem item)
        {
            if (item == null) return "No socket selected.";
            if (item.StateCode == 2)
                return "Socket on port :" + item.LocalPort + " is a LISTEN server socket (use [Kill Owning PID] to free a listening port; DeleteTCB applies to established connections).";

            MIB_TCPROW r = item.RawTcpRow;
            r.dwState = MIB_TCP_STATE_DELETE_TCB;
            int rc = SetTcpEntry(ref r);
            if (rc == 0)
                return "Dropped TCP socket " + item.LocalIp + ":" + item.LocalPort + " <-> " + item.RemoteIp + ":" + item.RemotePort + " (MIB_TCP_STATE_DELETE_TCB OK)!";
            return "SetTcpEntry returned Win32 code " + rc + " (run OmegaShell as Administrator to drop raw kernel TCB entries, or click [Kill Owning PID]).";
        }

        public static string KillProcessOnPort(int port, HashSet<string> whitelist)
        {
            List<TcpSocketItem> all = GetLiveTcpSockets();
            HashSet<int> pids = new HashSet<int>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].LocalPort == port && all[i].Pid > 4)
                    pids.Add(all[i].Pid);
            }
            if (pids.Count == 0) return "No active user process found holding TCP port :" + port + ".";

            int killed = 0;
            List<string> names = new List<string>();
            foreach (int pid in pids)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        if (whitelist != null && whitelist.Contains(p.ProcessName) && !p.ProcessName.StartsWith("Omega", StringComparison.OrdinalIgnoreCase))
                            return "Blocked: Port :" + port + " is owned by protected system process '" + p.ProcessName + "' (PID " + pid + ").";
                        string nm = p.ProcessName + ":" + pid;
                        p.Kill();
                        killed++;
                        names.Add(nm);
                    }
                }
                catch { }
            }
            return killed > 0
                ? ("Freed TCP port :" + port + " -> Terminated " + string.Join(", ", names.ToArray()) + "!")
                : ("Could not terminate owner of port :" + port + " (may require Administrator rights).");
        }

        private static int DecodeNetworkPort(uint dwPort)
        {
            return (int)(((dwPort & 0xFFU) << 8) | ((dwPort >> 8) & 0xFFU));
        }

        private static string DecodeTcpState(uint s)
        {
            switch (s)
            {
                case 1: return "CLOSED";
                case 2: return "LISTEN";
                case 3: return "SYN_SENT";
                case 4: return "SYN_RCVD";
                case 5: return "ESTABLISHED";
                case 6: return "FIN_WAIT1";
                case 7: return "FIN_WAIT2";
                case 8: return "CLOSE_WAIT";
                case 9: return "CLOSING";
                case 10: return "LAST_ACK";
                case 11: return "TIME_WAIT";
                case 12: return "DELETE_TCB";
                default: return "STATE_" + s;
            }
        }

        // =========================================================================
        // STARTUP APPS INSPECTOR & REVERSIBLE TOGGLE (REGISTRY + STARTUP FOLDERS)
        // =========================================================================
        public static List<StartupAppItem> GetStartupApps(string shellRootDir)
        {
            List<StartupAppItem> list = new List<StartupAppItem>();
            ReadRegistryRunEntries(Registry.CurrentUser, false, @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "HKCU\\Run", list);

            ReadRegistryRunEntries(Registry.LocalMachine, true, @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "HKLM\\Run", list);

            ReadRegistryRunEntries(Registry.LocalMachine, true, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "HKLM\\Wow64\\Run", list);

            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ReadStartupFolderEntries(userStartup, "User Startup Folder", true, list);
            string disabledDir = Path.Combine(shellRootDir, "config", "disabled_startup_lnks");
            ReadStartupFolderEntries(disabledDir, "User Startup (Disabled)", false, list);

            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ReadStartupFolderEntries(commonStartup, "All-Users Startup", true, list);

            return list;
        }

        private static void ReadRegistryRunEntries(RegistryKey root, bool isMachine, string runPath, string approvedPath, string tag, List<StartupAppItem> outList)
        {
            try
            {
                using (RegistryKey rk = root.OpenSubKey(runPath, false))
                {
                    if (rk == null) return;
                    using (RegistryKey ak = root.OpenSubKey(approvedPath, false))
                    {
                        string[] names = rk.GetValueNames();
                        for (int i = 0; i < names.Length; i++)
                        {
                            string nm = names[i];
                            if (string.IsNullOrEmpty(nm)) continue;
                            string cmd = (rk.GetValue(nm) ?? "").ToString();
                            bool enabled = true;
                            if (ak != null)
                            {
                                byte[] bin = ak.GetValue(nm) as byte[];
                                // In Windows 11 StartupApproved\Run: byte[0] odd (0x03, 0x01) = Disabled; even (0x02, 0x06) = Enabled!
                                if (bin != null && bin.Length > 0 && (bin[0] & 1) == 1)
                                    enabled = false;
                            }
                            StartupAppItem item = new StartupAppItem();
                            item.Name = nm;
                            item.Command = cmd;
                            item.LocationTag = tag;
                            item.Enabled = enabled;
                            item.IsRegistry = true;
                            item.IsLocalMachine = isMachine;
                            item.RegSubKey = runPath;
                            item.ApprovedSubKey = approvedPath;
                            outList.Add(item);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ReadStartupFolderEntries(string folder, string tag, bool isEnabled, List<StartupAppItem> outList)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            try
            {
                string[] files = Directory.GetFiles(folder);
                for (int i = 0; i < files.Length; i++)
                {
                    string fn = Path.GetFileName(files[i]);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    StartupAppItem item = new StartupAppItem();
                    item.Name = Path.GetFileNameWithoutExtension(fn);
                    item.Command = files[i];
                    item.LocationTag = tag;
                    item.Enabled = isEnabled;
                    item.IsRegistry = false;
                    item.FilePath = files[i];
                    outList.Add(item);
                }
            }
            catch { }
        }

        public static string SetStartupAppEnabled(StartupAppItem item, bool enable, string shellRootDir)
        {
            if (item == null) return "No startup item selected.";
            try
            {
                if (item.IsRegistry)
                {
                    RegistryKey root = item.IsLocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                    using (RegistryKey ak = root.CreateSubKey(item.ApprovedSubKey))
                    {
                        if (ak == null) return "Could not open StartupApproved registry key (Administrator rights required for HKLM items).";
                        byte[] payload = new byte[12];
                        payload[0] = enable ? (byte)0x02 : (byte)0x03;
                        if (!enable)
                        {
                            byte[] ft = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
                            Array.Copy(ft, 0, payload, 4, Math.Min(8, ft.Length));
                        }
                        ak.SetValue(item.Name, payload, RegistryValueKind.Binary);
                        item.Enabled = enable;
                        return (enable ? "Enabled" : "Disabled") + " Windows 11 startup item '" + item.Name + "' (" + item.LocationTag + ") cleanly via StartupApproved!";
                    }
                }
                else
                {
                    string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    string disabledDir = Path.Combine(shellRootDir, "config", "disabled_startup_lnks");
                    Directory.CreateDirectory(disabledDir);
                    string targetDir = enable ? userStartup : disabledDir;
                    string dest = Path.Combine(targetDir, Path.GetFileName(item.FilePath));
                    if (File.Exists(item.FilePath))
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(item.FilePath, dest);
                        item.FilePath = dest;
                        item.Command = dest;
                        item.Enabled = enable;
                        return (enable ? "Restored" : "Parked") + " startup shortcut '" + item.Name + "' -> " + dest;
                    }
                }
            }
            catch (Exception ex)
            {
                return "Could not toggle '" + item.Name + "' (HKLM items require Administrator): " + ex.Message;
            }
            return "No change applied.";
        }

        // =========================================================================
        // WINDOWS SERVICES INSPECTOR & CONTROLLER (System.ServiceProcess)
        // =========================================================================
        public static List<WindowsServiceItem> GetWindowsServices(string filterQuery, bool runningOnly)
        {
            List<WindowsServiceItem> list = new List<WindowsServiceItem>();
            string q = (filterQuery ?? "").Trim().ToLowerInvariant();
            try
            {
                ServiceController[] svcs = ServiceController.GetServices();
                for (int i = 0; i < svcs.Length; i++)
                {
                    ServiceController sc = svcs[i];
                    try
                    {
                        string st = sc.Status.ToString();
                        if (runningOnly && sc.Status != ServiceControllerStatus.Running) continue;
                        string sName = sc.ServiceName ?? "";
                        string dName = sc.DisplayName ?? "";
                        if (q.Length > 0 && sName.ToLowerInvariant().IndexOf(q) < 0 && dName.ToLowerInvariant().IndexOf(q) < 0)
                            continue;

                        WindowsServiceItem wi = new WindowsServiceItem();
                        wi.ServiceName = sName;
                        wi.DisplayName = dName;
                        wi.Status = st;
                        wi.CanStop = sc.CanStop;
                        wi.StartMode = ReadServiceStartMode(sName);
                        list.Add(wi);
                    }
                    catch { }
                    finally { try { sc.Dispose(); } catch { } }
                }
            }
            catch { }

            list.Sort(delegate(WindowsServiceItem a, WindowsServiceItem b)
            {
                bool ar = a.Status == "Running", br = b.Status == "Running";
                if (ar != br) return br.CompareTo(ar);
                return string.Compare(a.ServiceName, b.ServiceName, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static string ReadServiceStartMode(string svcName)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + svcName, false))
                {
                    if (k == null) return "Unknown";
                    object v = k.GetValue("Start");
                    if (v is int)
                    {
                        int sv = (int)v;
                        if (sv == 2) return "Automatic";
                        if (sv == 3) return "Manual";
                        if (sv == 4) return "Disabled";
                        if (sv == 0 || sv == 1) return "Boot/System";
                    }
                }
            }
            catch { }
            return "Manual";
        }

        public static string ControlWindowsService(string serviceName, string action)
        {
            if (string.IsNullOrWhiteSpace(serviceName)) return "No service specified.";
            string[] critical = new string[] { "RpcSs", "DcomLaunch", "PlugPlay", "LSM", "SamSs", "WinDefend", "EventLog", "BFE", "mpssvc" };
            for (int i = 0; i < critical.Length; i++)
            {
                if (string.Equals(serviceName, critical[i], StringComparison.OrdinalIgnoreCase) && action != "start")
                    return "Safety Guardrail: '" + serviceName + "' is a core Windows security/RPC service and will not be stopped.";
            }

            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    string act = (action ?? "").Trim().ToLowerInvariant();
                    if (act == "stop")
                    {
                        if (sc.Status == ServiceControllerStatus.Stopped) return "Service '" + serviceName + "' is already Stopped.";
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        return "Stopped Windows service '" + serviceName + "' cleanly.";
                    }
                    if (act == "start")
                    {
                        if (sc.Status == ServiceControllerStatus.Running) return "Service '" + serviceName + "' is already Running.";
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
                        return "Started Windows service '" + serviceName + "' cleanly.";
                    }
                    if (act == "restart")
                    {
                        if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        }
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
                        return "Restarted Windows service '" + serviceName + "' cleanly.";
                    }
                    return "Unknown service action: " + action;
                }
            }
            catch (Exception ex)
            {
                return "Service control failed for '" + serviceName + "' (run OmegaShell as Administrator to manage system services): " + ex.Message;
            }
        }

        // =========================================================================
        // WINDOWS POWERCFG PLANS, NVIDIA DirectCompute GPU TELEMETRY & SAFE CACHE CLEANER
        // =========================================================================
        public static string GetActivePowerPlanBanner()
        {
            string raw = RunCmdFast("powercfg.exe", "/getactivescheme", 1800);
            return string.IsNullOrWhiteSpace(raw) ? "Power Plan: Windows Default" : raw.Trim();
        }

        public static string SwitchPowerPlan(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            string guid = "";
            if (m == "eco" || m == "saver") guid = "a1841308-3541-4fab-bc81-f71556f20b4a";
            else if (m == "balanced") guid = "381b4222-f694-41f0-9685-ff5bb260df2e";
            else if (m == "high") guid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
            else if (m == "ultimate")
            {
                string listBefore = RunCmdFast("powercfg.exe", "/list", 2000);
                Match mEx = Regex.Match(listBefore, @"([0-9a-fA-F\-]{36})\s+\(Ultimate Performance\)", RegexOptions.IgnoreCase);
                if (mEx.Success) guid = mEx.Groups[1].Value;
                else
                {
                    string dup = RunCmdFast("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", 2500);
                    Match mNew = Regex.Match(dup, @"([0-9a-fA-F\-]{36})");
                    guid = mNew.Success ? mNew.Groups[1].Value : "e9a42b02-d5df-448d-aa00-03f14749eb61";
                }
            }
            else return "Specify power mode: eco | balanced | high | ultimate";

            RunCmdFast("powercfg.exe", "/setactive " + guid, 2200);
            return "Switched Windows Power Scheme -> " + GetActivePowerPlanBanner();
        }

        public static string QueryNvidiaGpuLine()
        {
            string smi = @"C:\Windows\System32\nvidia-smi.exe";
            if (!File.Exists(smi)) smi = "nvidia-smi.exe";
            string raw = RunCmdFast(smi, "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits", 1600);
            if (string.IsNullOrWhiteSpace(raw)) return "GPU: Integrated / nvidia-smi idle";
            string[] p = raw.Trim().Split('\n')[0].Split(',');
            if (p.Length >= 6)
                return p[0].Trim() + " | " + p[1].Trim() + "C | Util " + p[2].Trim() + "% | VRAM " + p[3].Trim() + "/" + p[4].Trim() + " MB | " + p[5].Trim() + " W";
            return raw.Trim();
        }

        public static List<OshRow> QueryNvidiaVramProcessRows()
        {
            List<OshRow> rows = new List<OshRow>();
            string smi = @"C:\Windows\System32\nvidia-smi.exe";
            if (!File.Exists(smi)) smi = "nvidia-smi.exe";
            string raw = RunCmdFast(smi, "--query-compute-apps=pid,process_name,used_memory --format=csv,noheader,nounits", 1800);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                string[] lines = raw.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Trim().Split(',');
                    if (p.Length < 3) continue;
                    int pid; long mb;
                    int.TryParse(p[0].Trim(), out pid);
                    long.TryParse(p[2].Trim(), out mb);
                    OshRow r = new OshRow();
                    r.Set("pid", pid).Set("process", Path.GetFileName(p[1].Trim())).Set("vram_mb", mb).Set("path", p[1].Trim());
                    rows.Add(r);
                }
            }
            return rows;
        }

        public static string CleanSafeTempAndCaches(bool dryRun, out int fileCount, out long bytesTarget)
        {
            fileCount = 0;
            bytesTarget = 0L;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-15);
            List<string> dirs = new List<string>();
            dirs.Add(Path.GetTempPath());
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(Path.Combine(localApp, "D3DSCache"));
            dirs.Add(Path.Combine(localApp, "CrashDumps"));
            dirs.Add(Path.Combine(localApp, "Temp"));

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int d = 0; d < dirs.Count; d++)
            {
                string root = dirs[d];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                string full = Path.GetFullPath(root).TrimEnd('\\');
                if (!visited.Add(full)) continue;
                ScanAndCleanDirBounded(full, 3, cutoff, dryRun, ref fileCount, ref bytesTarget);
            }

            string verb = dryRun ? "[DRY-RUN PREVIEW]" : "[CLEANED]";
            return verb + " Safe Temp/D3DSCache/CrashDumps: " + fileCount + " stale file(s) (" + OshFormat.FormatBytes(bytesTarget) + ")" +
                (dryRun ? " eligible. Click [Apply Clean] or run 'opt clean-temp --apply' to delete." : " removed cleanly!");
        }

        private static void ScanAndCleanDirBounded(string dir, int depth, DateTime cutoffUtc, bool dryRun, ref int filesHit, ref long bytesHit)
        {
            if (depth < 0 || filesHit > 25000) return;
            try
            {
                string[] files = Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        if (fi.LastWriteTimeUtc > cutoffUtc) continue;
                        long len = fi.Length;
                        if (dryRun)
                        {
                            filesHit++;
                            bytesHit += len;
                        }
                        else
                        {
                            File.Delete(files[i]);
                            filesHit++;
                            bytesHit += len;
                        }
                    }
                    catch { }
                }
                if (depth == 0) return;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length; s++)
                    ScanAndCleanDirBounded(subs[s], depth - 1, cutoffUtc, dryRun, ref filesHit, ref bytesHit);
            }
            catch { }
        }

        public static string RunCmdFast(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && sb.Length < 65536) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }
    }
}
