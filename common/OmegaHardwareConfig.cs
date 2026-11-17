using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaPortableConfig
{
    public sealed class OmegaHardwareProfile
    {
        public string SuiteVersion = "3.0.0";
        public string LabRoot = "";
        public string HostAlias = "";
        public string PrimaryGpuName = "DirectCompute cs_5_0 Adapter";
        public long PrimaryGpuVramMb = 0;
        public string DownloadsDir = "";
        public string CorpusDir = "";
        public string CloudBridgeDir = "";
        public string ExternalProjectDir = "";
        public string LocalLlmEndpoint = "http://127.0.0.1:11434";
        public string OpenAiCompatEndpoint = "http://127.0.0.1:19791/v1/chat/completions";
        public int LauncherDeckPort = 19800;
        public List<string> DetectedAdbDevices = new List<string>();
    }

    public static class OmegaHardwareConfig
    {
        private static readonly object _sync = new object();
        private static OmegaHardwareProfile _cached = null;

        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Delegate(IntPtr thisPtr, uint index, out IntPtr ppAdapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Delegate(IntPtr thisPtr, out DXGI_ADAPTER_DESC1 pDesc);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public uint AdapterLuidLow;
            public int AdapterLuidHigh;
            public uint Flags;
        }

        public static string ResolveLabRoot()
        {
            string envRoot = Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT");
            if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
                return envRoot.TrimEnd('\\', '/');

            string cur = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(cur); depth++)
            {
                if (File.Exists(Path.Combine(cur, "Build-OmegaLabs.ps1")) ||
                    File.Exists(Path.Combine(cur, "OmegaLabLauncher.exe")) ||
                    File.Exists(Path.Combine(cur, "OmegaLabLauncher.cs")) ||
                    Directory.Exists(Path.Combine(cur, "config")))
                {
                    return cur;
                }
                DirectoryInfo parent = Directory.GetParent(cur);
                cur = (parent != null) ? parent.FullName.TrimEnd('\\', '/') : "";
            }
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        }

        public static string DetectPrimaryDxgiGpu(out long vramMb)
        {
            vramMb = 0;
            IntPtr factory = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
                int hr = CreateDXGIFactory1(ref iid, out factory);
                if (hr < 0 || factory == IntPtr.Zero) return "DirectCompute cs_5_0 Adapter";

                IntPtr vtbl = Marshal.ReadIntPtr(factory);
                IntPtr enumPtr = Marshal.ReadIntPtr(vtbl, 12 * IntPtr.Size); // IDXGIFactory1::EnumAdapters1 = slot 12
                EnumAdapters1Delegate enumFn = (EnumAdapters1Delegate)Marshal.GetDelegateForFunctionPointer(enumPtr, typeof(EnumAdapters1Delegate));

                string bestName = "";
                long bestVram = -1;
                for (uint idx = 0; idx < 8; idx++)
                {
                    IntPtr adapter = IntPtr.Zero;
                    if (enumFn(factory, idx, out adapter) < 0 || adapter == IntPtr.Zero) break;
                    try
                    {
                        IntPtr avtbl = Marshal.ReadIntPtr(adapter);
                        IntPtr descPtr = Marshal.ReadIntPtr(avtbl, 10 * IntPtr.Size); // IDXGIAdapter1::GetDesc1 = slot 10
                        GetDesc1Delegate descFn = (GetDesc1Delegate)Marshal.GetDelegateForFunctionPointer(descPtr, typeof(GetDesc1Delegate));
                        DXGI_ADAPTER_DESC1 desc;
                        if (descFn(adapter, out desc) >= 0)
                        {
                            bool isSoftware = (desc.Flags & 2u) != 0u;
                            long mb = (long)(desc.DedicatedVideoMemory.ToUInt64() / (1024UL * 1024UL));
                            string nm = (desc.Description ?? "").Trim();
                            if (!isSoftware && nm.Length > 0 && mb > bestVram)
                            {
                                bestVram = mb;
                                bestName = nm;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapter);
                    }
                }
                if (bestName.Length > 0)
                {
                    vramMb = bestVram > 0 ? bestVram : 0;
                    return bestName;
                }
            }
            catch { }
            finally
            {
                if (factory != IntPtr.Zero)
                {
                    try { Marshal.Release(factory); } catch { }
                }
            }
            return "DirectCompute cs_5_0 Adapter";
        }

        public static OmegaHardwareProfile LoadAndApplyEnvironment(bool forceReload)
        {
            lock (_sync)
            {
                if (_cached != null && !forceReload) return _cached;

                OmegaHardwareProfile p = new OmegaHardwareProfile();
                p.LabRoot = ResolveLabRoot();
                p.HostAlias = Environment.MachineName;
                long vram;
                p.PrimaryGpuName = DetectPrimaryDxgiGpu(out vram);
                p.PrimaryGpuVramMb = vram;
                p.DownloadsDir = Path.Combine(p.LabRoot, "downloads");
                p.CorpusDir = Path.Combine(p.LabRoot, "corpus");
                p.CloudBridgeDir = Path.Combine(p.LabRoot, "bridge_export");
                p.ExternalProjectDir = Path.Combine(p.LabRoot, "workspace");

                string userCfg = Path.Combine(p.LabRoot, "config", "omega.config.json");
                string exCfg = Path.Combine(p.LabRoot, "config", "omega.config.example.json");
                string cfgPath = File.Exists(userCfg) ? userCfg : (File.Exists(exCfg) ? exCfg : "");
                if (cfgPath.Length > 0)
                {
                    try
                    {
                        string json = File.ReadAllText(cfgPath, Encoding.UTF8);
                        string h = ReadJsonString(json, "host_alias");
                        if (h.Length > 0 && h != "AUTO_DETECT_HOSTNAME") p.HostAlias = h;

                        string g = ReadJsonString(json, "gpu_label");
                        if (g.Length > 0 && g != "AUTO_DETECT_DXGI") p.PrimaryGpuName = g;

                        string dl = ReadJsonString(json, "downloads_dir");
                        if (dl.Length > 0) p.DownloadsDir = ResolveConfigPath(p.LabRoot, dl);

                        string cp = ReadJsonString(json, "corpus_dir");
                        if (cp.Length > 0) p.CorpusDir = ResolveConfigPath(p.LabRoot, cp);

                        string cb = ReadJsonString(json, "cloud_bridge_dir");
                        if (cb.Length > 0) p.CloudBridgeDir = ResolveConfigPath(p.LabRoot, cb);

                        string ep = ReadJsonString(json, "external_project_dir");
                        if (ep.Length > 0) p.ExternalProjectDir = ResolveConfigPath(p.LabRoot, ep);

                        string llm = ReadJsonString(json, "local_llm_endpoint");
                        if (llm.Length > 0) p.LocalLlmEndpoint = llm;
                    }
                    catch { }
                }

                try { Environment.SetEnvironmentVariable("OMEGA_LAB_ROOT", p.LabRoot); } catch { }
                try { Environment.SetEnvironmentVariable("OMEGA_CORPUS_DIR", p.CorpusDir); } catch { }
                try { Environment.SetEnvironmentVariable("OMEGA_CLOUD_BRIDGE_DIR", p.CloudBridgeDir); } catch { }
                try { Environment.SetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR", p.ExternalProjectDir); } catch { }
                try { Environment.SetEnvironmentVariable("OMEGA_GPU_NAME", p.PrimaryGpuName); } catch { }

                p.DetectedAdbDevices = ProbeConnectedAdbDevices();
                _cached = p;
                return p;
            }
        }

        public static void SaveUserConfig(OmegaHardwareProfile p)
        {
            string cfgDir = Path.Combine(p.LabRoot, "config");
            Directory.CreateDirectory(cfgDir);
            StringBuilder sb = new StringBuilder(1024);
            sb.AppendLine("{");
            sb.AppendLine("  \"suite_version\": \"3.0.0\",");
            sb.AppendLine("  \"host_alias\": \"" + EscJson(p.HostAlias) + "\",");
            sb.AppendLine("  \"gpu_label\": \"" + EscJson(p.PrimaryGpuName) + "\",");
            sb.AppendLine("  \"gpu_vram_mb\": " + p.PrimaryGpuVramMb + ",");
            sb.AppendLine("  \"downloads_dir\": \"" + EscJson(p.DownloadsDir) + "\",");
            sb.AppendLine("  \"corpus_dir\": \"" + EscJson(p.CorpusDir) + "\",");
            sb.AppendLine("  \"cloud_bridge_dir\": \"" + EscJson(p.CloudBridgeDir) + "\",");
            sb.AppendLine("  \"external_project_dir\": \"" + EscJson(p.ExternalProjectDir) + "\",");
            sb.AppendLine("  \"local_llm_endpoint\": \"" + EscJson(p.LocalLlmEndpoint) + "\",");
            sb.AppendLine("  \"openai_compat_endpoint\": \"" + EscJson(p.OpenAiCompatEndpoint) + "\",");
            sb.AppendLine("  \"adb_discovery\": \"auto\"");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(cfgDir, "omega.config.json"), sb.ToString(), new UTF8Encoding(false));
            lock (_sync) { _cached = null; }
            LoadAndApplyEnvironment(true);
        }

        public static List<string> ProbeConnectedAdbDevices()
        {
            List<string> res = new List<string>();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("adb", "devices -l");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process pr = Process.Start(psi))
                {
                    if (pr == null) return res;
                    string stdout = pr.StandardOutput.ReadToEnd();
                    pr.WaitForExit(1800);
                    string[] lines = stdout.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string ln = lines[i].Trim();
                        if (ln.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
                        if (ln.IndexOf(" device ", StringComparison.Ordinal) > 0 || ln.EndsWith("\tdevice", StringComparison.Ordinal))
                        {
                            res.Add(ln);
                        }
                    }
                }
            }
            catch { }
            return res;
        }

        private static string ResolveConfigPath(string root, string raw)
        {
            string expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
            if (Path.IsPathRooted(expanded)) return expanded;
            return Path.GetFullPath(Path.Combine(root, expanded));
        }

        private static string ReadJsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success) return "";
            return m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
