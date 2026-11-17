using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmegaForgeStudio
{
    public static partial class OmegaForgeScanner
    {
        static OmegaForgeScanner()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }
        }

        public static List<ToolDescriptor> ScanAllToolchains(string forgeRoot)
        {
            List<ToolDescriptor> specs = BuildCatalogSpecs(forgeRoot);
            string[] pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');

            Parallel.For(0, specs.Count, delegate(int idx)
            {
                ToolDescriptor td = specs[idx];
                if (td.IsBuiltInVirtual)
                {
                    if (td.Id == "fs_workspace")
                    {
                        td.Found = true;
                        td.Enabled = true;
                        td.VersionBanner = "Sandboxed R/W (" + Path.Combine(forgeRoot, "workspace") + ")";
                    }
                    else if (td.Id == "omega_browser_api")
                    {
                        td.Found = ProbeTcpPort(9778, 120);
                        td.Enabled = td.Found;
                        td.VersionBanner = td.Found ? "ONLINE (127.0.0.1:9778)" : "Offline (:9778)";
                    }
                    else if (td.Id == "omega_av_ipc")
                    {
                        td.Found = ProbeTcpPort(19779, 120) || Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "03_omega_av"));
                        td.Enabled = td.Found;
                        td.VersionBanner = ProbeTcpPort(19779, 100) ? "ONLINE (:19779 + inbox_open.cmd)" : "Staged IPC Ready";
                    }
                    else if (td.Id == "sovereign_mcp")
                    {
                        td.Found = ProbeTcpPort(8097, 120);
                        td.Enabled = td.Found;
                        td.VersionBanner = td.Found ? "ONLINE (127.0.0.1:8097 UTCP-v31)" : "Standby (:8097)";
                    }
                    return;
                }

                string resolved = ResolveBinaryPath(td.Id, td.ExecutablePath, pathDirs);
                if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                {
                    td.ExecutablePath = resolved;
                    td.Found = true;
                    td.Enabled = true;
                    td.VersionBanner = ProbeVersionBanner(resolved, td.VersionArgs);
                }
                else
                {
                    td.Found = false;
                    td.Enabled = false;
                    td.VersionBanner = "Not found on PATH / SDK roots";
                }
            });

            return specs;
        }

        public static string FindVcVars64ForCl(string clExePath)
        {
            if (string.IsNullOrEmpty(clExePath) || !File.Exists(clExePath)) return "";
            try
            {
                DirectoryInfo dir = Directory.GetParent(clExePath);
                for (int up = 0; up < 8 && dir != null; up++)
                {
                    string cand = Path.Combine(dir.FullName, "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(cand)) return cand;
                    string cand2 = Path.Combine(dir.FullName, "VC", "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(cand2)) return cand2;
                    dir = dir.Parent;
                }
            }
            catch { }
            return "";
        }

        private static List<ToolDescriptor> BuildCatalogSpecs(string forgeRoot)
        {
            List<ToolDescriptor> list = new List<ToolDescriptor>();

            AddSpec(list, "csc", "C# Framework Compiler (csc.exe)", "Compilers", "csc.exe", "/help",
                "using System;\r\nclass Program {\r\n    static void Main() {\r\n        double sum = 0;\r\n        for (int i = 1; i <= 240; i++) sum += Math.Sqrt(i);\r\n        Console.WriteLine(\"C# x64 E8 Norm Checksum = \" + sum.ToString(\"F6\"));\r\n    }\r\n}");

            AddSpec(list, "dotnet", ".NET SDK CLI (dotnet)", "Compilers", "dotnet.exe", "--version", "--info");

            AddSpec(list, "rustc", "Rust Native Compiler (rustc)", "Compilers", "rustc.exe", "--version",
                "fn main() {\r\n    let mut acc: f64 = 0.0;\r\n    for i in 1..=240 { acc += (i as f64).sqrt(); }\r\n    println!(\"Rust L0 E8 Norm Checksum = {:.6}\", acc);\r\n}");

            AddSpec(list, "cargo", "Rust Cargo Package/Build Manager", "Compilers", "cargo.exe", "--version", "--version");

            AddSpec(list, "clangpp", "LLVM Clang++ C/C++23 Compiler", "Compilers", "clang++.exe", "--version",
                "#include <iostream>\r\n#include <cmath>\r\nint main() {\r\n    double s = 0;\r\n    for (int i = 1; i <= 240; ++i) s += std::sqrt((double)i);\r\n    std::cout << \"Clang++ C++23 Checksum = \" << s << std::endl;\r\n    return 0;\r\n}");

            AddSpec(list, "msvc_cl", "MSVC C/C++ Optimizing Compiler (cl.exe)", "Compilers", "cl.exe", "",
                "#include <stdio.h>\r\n#include <math.h>\r\nint main() {\r\n    double s = 0.0;\r\n    for (int i = 1; i <= 240; ++i) s += sqrt((double)i);\r\n    printf(\"MSVC cl.exe x64 E8 Checksum = %.6f\\n\", s);\r\n    return 0;\r\n}");

            AddSpec(list, "gpp", "GNU C++ Compiler (g++)", "Compilers", "g++.exe", "--version", "");

            AddSpec(list, "fxc", "DirectX HLSL Shader Compiler (fxc.exe)", "GPU & Shaders", "fxc.exe", "/?",
                "RWStructuredBuffer<float4> OutBuf : register(u0);\r\n[numthreads(64, 1, 1)]\r\nvoid CSMain(uint3 id : SV_DispatchThreadID) {\r\n    float x = (float)id.x * 0.015625;\r\n    OutBuf[id.x] = float4(sin(x), cos(x), x * 0.5, 1.0);\r\n}");

            AddSpec(list, "dxc", "DirectX Shader Model 6 Compiler (dxc.exe)", "GPU & Shaders", "dxc.exe", "--version",
                "RWStructuredBuffer<float4> OutWave : register(u0);\r\n[numthreads(64, 1, 1)]\r\nvoid CSMain(uint3 id : SV_DispatchThreadID) {\r\n    float laneVal = (float)WaveGetLaneIndex() / 64.0;\r\n    float waveSum = WaveActiveSum(laneVal);\r\n    OutWave[id.x] = float4(laneVal, waveSum, 1.0, 1.0);\r\n}");

            AddSpec(list, "nvidia_smi", "NVIDIA RTX GPU Telemetry (nvidia-smi)", "GPU & Shaders", "nvidia-smi.exe", "--query-gpu=name,driver_version,memory.total,memory.used,temperature.gpu --format=csv,noheader",
                "--query-gpu=name,driver_version,memory.total,memory.used,utilization.gpu,temperature.gpu --format=csv");

            AddSpec(list, "nvcc", "NVIDIA CUDA Compiler (nvcc)", "GPU & Shaders", "nvcc.exe", "--version", "");

            AddSpec(list, "lean4", "Lean 4 Theorem Prover (lean)", "Proof & Algebra", "lean.exe", "--version",
                "theorem omega_comm (a b : Nat) : a + b = b + a := Nat.add_comm a b\r\ntheorem crt_7777_bijection : 7 * 11 * 101 = 7777 := rfl\r\n#check omega_comm\r\n#eval (7 * 11 * 101)");

            AddSpec(list, "lake", "Lean 4 Lake Build System", "Proof & Algebra", "lake.exe", "--version", "--version");
            AddSpec(list, "ghc", "Glasgow Haskell Compiler (ghc)", "Proof & Algebra", "ghc.exe", "--version", "");

            AddSpec(list, "python", "Python 3 Runtime", "Languages & Runtimes", "python.exe", "--version",
                "import math, platform\r\nprint('Python', platform.python_version(), '| 7777D CRT =', 7 * 11 * 101, '| Sum =', round(sum(math.sqrt(i) for i in range(1, 241)), 6))");

            AddSpec(list, "deno", "Deno Secure TypeScript/WASM Runtime", "Languages & Runtimes", "deno.exe", "--version",
                "const crt = 7 * 11 * 101;\r\nconsole.log('Deno TS Runtime :: 7777D CRT = ' + crt);");

            AddSpec(list, "node", "Node.js JavaScript Runtime", "Languages & Runtimes", "node.exe", "--version",
                "console.log('Node.js ' + process.version + ' :: E8 roots = ' + (8 * 30));");

            AddSpec(list, "pwsh", "PowerShell 7 Core (pwsh)", "Languages & Runtimes", "pwsh.exe", "--version", "");

            AddSpec(list, "powershell", "Windows PowerShell 5.1", "Languages & Runtimes", "powershell.exe", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
                "$drives = Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{N='FreeGB';E={[math]::Round($_.Free/1GB,1)}}\r\n$drives | Format-Table -AutoSize");

            AddSpec(list, "adb", "Android Debug Bridge (Dual-Pixel Mesh)", "Edge & Systems", "adb.exe", "version", "devices -l");
            AddSpec(list, "git", "Git Distributed Version Control", "Edge & Systems", "git.exe", "--version", "--version");
            AddSpec(list, "duckdb", "DuckDB Analytical SQL CLI", "Edge & Systems", "duckdb.exe", "--version",
                "-c \"SELECT 7*11*101 AS crt_7777d, 12*44 AS chair44_shards, version() AS duckdb_ver;\"");
            AddSpec(list, "sqlite3", "SQLite 3 Embedded Engine", "Edge & Systems", "sqlite3.exe", "-version", "-version");
            AddSpec(list, "ffmpeg", "FFmpeg Media Muxer/Transcoder", "Edge & Systems", "ffmpeg.exe", "-version", "");
            AddSpec(list, "cmake", "CMake Native Build Generator", "Edge & Systems", "cmake.exe", "--version", "--version");

            AddSpec(list, "ollama_cli", "Ollama Local Model Engine CLI", "AI Runners", "ollama.exe", "--version", "list");
            AddSpec(list, "llama_server", "llama.cpp HTTP Inference Server", "AI Runners", "llama-server.exe", "--version", "");
            AddSpec(list, "llama_cli", "llama.cpp Direct CLI Runner", "AI Runners", "llama-cli.exe", "--version", "");

            ToolDescriptor vFs = new ToolDescriptor();
            vFs.Id = "fs_workspace"; vFs.Name = "Forge Workspace File R/W"; vFs.Category = "Lab Bridges"; vFs.IsBuiltInVirtual = true; vFs.Found = true; vFs.Enabled = true;
            list.Add(vFs);

            ToolDescriptor vBr = new ToolDescriptor();
            vBr.Id = "omega_browser_api"; vBr.Name = "OmegaBrowser v0.6.1 (:9778 API)"; vBr.Category = "Lab Bridges"; vBr.IsBuiltInVirtual = true;
            list.Add(vBr);

            ToolDescriptor vAv = new ToolDescriptor();
            vAv.Id = "omega_av_ipc"; vAv.Name = "OmegaAV v0.7.1 (:19779 Shader/WAV IPC)"; vAv.Category = "Lab Bridges"; vAv.IsBuiltInVirtual = true;
            list.Add(vAv);

            ToolDescriptor vMcp = new ToolDescriptor();
            vMcp.Id = "sovereign_mcp"; vMcp.Name = "Sovereign Manifold UTCP (:8097)"; vMcp.Category = "Lab Bridges"; vMcp.IsBuiltInVirtual = true;
            list.Add(vMcp);

            return list;
        }

        private static void AddSpec(List<ToolDescriptor> list, string id, string name, string cat, string exe, string verArgs, string sample)
        {
            ToolDescriptor t = new ToolDescriptor();
            t.Id = id; t.Name = name; t.Category = cat; t.ExecutablePath = exe; t.VersionArgs = verArgs; t.SampleCode = sample;
            t.Found = false;
            t.Enabled = false;
            list.Add(t);
        }

        private static string ResolveBinaryPath(string id, string exeName, string[] pathDirs)
        {
            List<string> explicitCandidates = GetKnownInstallCandidates(id, exeName);
            for (int i = 0; i < explicitCandidates.Count; i++)
            {
                if (!string.IsNullOrEmpty(explicitCandidates[i]) && File.Exists(explicitCandidates[i]))
                    return explicitCandidates[i];
            }
            for (int p = 0; p < pathDirs.Length; p++)
            {
                string dir = (pathDirs[p] ?? "").Trim().Trim('"');
                if (dir.Length == 0) continue;
                try
                {
                    string cand = Path.Combine(dir, exeName);
                    if (File.Exists(cand)) return cand;
                }
                catch { }
            }
            return "";
        }

        private static List<string> GetKnownInstallCandidates(string id, string exeName)
        {
            List<string> c = new List<string>();
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (id == "csc") c.Add(@"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            else if (id == "dotnet") c.Add(@"C:\Program Files\dotnet\dotnet.exe");
            else if (id == "rustc" || id == "cargo") c.Add(Path.Combine(user, ".cargo", "bin", exeName));
            else if (id == "lean4" || id == "lake") c.Add(Path.Combine(user, ".elan", "bin", exeName));
            else if (id == "ghc") c.Add(@"C:\ghcup\bin\ghc.exe");
            else if (id == "deno") c.Add(Path.Combine(user, ".deno", "bin", "deno.exe"));
            else if (id == "node") c.Add(@"C:\Program Files\nodejs\node.exe");
            else if (id == "pwsh") c.Add(@"C:\Program Files\PowerShell\7\pwsh.exe");
            else if (id == "powershell") c.Add(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
            else if (id == "nvidia_smi") c.Add(@"C:\Windows\System32\nvidia-smi.exe");
            else if (id == "git") c.Add(@"C:\Program Files\Git\cmd\git.exe");
            else if (id == "adb")
            {
                c.Add(Path.Combine(localApp, "Android", "Sdk", "platform-tools", "adb.exe"));
                c.Add(@"C:\platform-tools\adb.exe");
                c.Add(@"E:\platform-tools\adb.exe");
            }
            else if (id == "ollama_cli")
            {
                c.Add(Path.Combine(localApp, "Programs", "Ollama", "ollama.exe"));
            }
            else if (id == "python")
            {
                string pyRoot = Path.Combine(localApp, "Programs", "Python");
                if (Directory.Exists(pyRoot))
                {
                    try
                    {
                        string[] dirs = Directory.GetDirectories(pyRoot, "Python*");
                        for (int i = dirs.Length - 1; i >= 0; i--) c.Add(Path.Combine(dirs[i], "python.exe"));
                    }
                    catch { }
                }
            }
            else if (id == "fxc" || id == "dxc")
            {
                string kits = @"C:\Program Files (x86)\Windows Kits\10\bin";
                if (Directory.Exists(kits))
                {
                    try
                    {
                        string[] vers = Directory.GetDirectories(kits, "10.*");
                        for (int i = vers.Length - 1; i >= 0; i--) c.Add(Path.Combine(vers[i], "x64", exeName));
                    }
                    catch { }
                }
            }
            else if (id == "clangpp")
            {
                c.Add(@"C:\Program Files\LLVM\bin\clang++.exe");
            }
            else if (id == "msvc_cl")
            {
                string[] vsRoots = new string[] { @"C:\Program Files\Microsoft Visual Studio", @"C:\Program Files (x86)\Microsoft Visual Studio" };
                for (int r = 0; r < vsRoots.Length; r++)
                {
                    if (!Directory.Exists(vsRoots[r])) continue;
                    try
                    {
                        string[] found = Directory.GetFiles(vsRoots[r], "cl.exe", SearchOption.AllDirectories);
                        for (int i = 0; i < found.Length; i++)
                            if (found[i].IndexOf(@"Hostx64\x64\cl.exe", StringComparison.OrdinalIgnoreCase) >= 0) c.Add(found[i]);
                    }
                    catch { }
                }
            }
            else if (id == "llama_server" || id == "llama_cli" || id == "duckdb" || id == "ffmpeg")
            {
                string[] searchRoots = new string[] { (Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')), (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')), @"C:\llama.cpp", @"E:\llama.cpp", @"C:\tools", @"E:\tools" };
                for (int r = 0; r < searchRoots.Length; r++)
                {
                    string hit = FindFileBounded(searchRoots[r], exeName, 3);
                    if (!string.IsNullOrEmpty(hit)) c.Add(hit);
                }
            }
            return c;
        }

        private static string FindFileBounded(string dir, string targetFile, int maxDepth)
        {
            if (maxDepth < 0 || string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "";
            try
            {
                string direct = Path.Combine(dir, targetFile);
                if (File.Exists(direct)) return direct;
                if (maxDepth == 0) return "";
                string[] subs = Directory.GetDirectories(dir);
                for (int i = 0; i < subs.Length; i++)
                {
                    string nm = Path.GetFileName(subs[i]);
                    if (nm.StartsWith(".") || nm.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || nm.Equals("target", StringComparison.OrdinalIgnoreCase)) continue;
                    string r = FindFileBounded(subs[i], targetFile, maxDepth - 1);
                    if (!string.IsNullOrEmpty(r)) return r;
                }
            }
            catch { }
            return "";
        }

        private static string ProbeVersionBanner(string exePath, string args)
        {
            try
            {
                if (string.IsNullOrEmpty(args))
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrEmpty(fvi.FileVersion)) return "v" + fvi.FileVersion;
                }
                ProcessStartInfo psi = new ProcessStartInfo(exePath, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && sb.Length < 512) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate(object s2, DataReceivedEventArgs e2) { if (e2.Data != null && sb.Length < 512) sb.AppendLine(e2.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(1400)) { try { p.Kill(); } catch { } }
                    string raw = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        string[] lines = raw.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string cl = lines[i].Trim();
                            if (cl.Length > 2) return cl.Length > 58 ? cl.Substring(0, 58) : cl;
                        }
                    }
                }
                FileVersionInfo fv2 = FileVersionInfo.GetVersionInfo(exePath);
                return !string.IsNullOrEmpty(fv2.ProductVersion) ? fv2.ProductVersion : "Installed";
            }
            catch { return "Installed"; }
        }

        public static bool ProbeTcpPort(int port, int timeoutMs)
        {
            try
            {
                using (TcpClient tc = new TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect("127.0.0.1", port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(timeoutMs) && tc.Connected) { tc.EndConnect(ar); return true; }
                }
            }
            catch { }
            return false;
        }
    }
}
