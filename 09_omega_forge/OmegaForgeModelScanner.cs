using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace OmegaForgeStudio
{
    public static partial class OmegaForgeScanner
    {
        public static List<AiModelDescriptor> ScanAllModelsAndEndpoints(List<ToolDescriptor> tools)
        {
            List<AiModelDescriptor> models = new List<AiModelDescriptor>();
            HashSet<string> dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool sovereign8097Live = ProbeTcpPort(8097, 150);

            // 1. Probe Ollama live HTTP server (:11434/api/tags)
            if (ProbeTcpPort(11434, 160))
            {
                string tagsJson = HttpGetFast("http://127.0.0.1:11434/api/tags", 1600);
                List<string> names = ForgeJson.ExtractAllStringFields(tagsJson, "name");
                for (int i = 0; i < names.Count; i++)
                {
                    string nm = names[i];
                    if (string.IsNullOrEmpty(nm) || !dedupe.Add("ollama:" + nm)) continue;
                    AiModelDescriptor m = new AiModelDescriptor();
                    m.Id = "ollama_" + nm;
                    m.DisplayName = nm + " (Ollama :11434)";
                    m.Kind = "Ollama-Live";
                    m.EndpointUrl = "http://127.0.0.1:11434/api/chat";
                    m.ModelTag = nm;
                    m.IsLiveServer = true;
                    m.IsGemma = nm.IndexOf("gemma", StringComparison.OrdinalIgnoreCase) >= 0;
                    m.Family = m.IsGemma ? "Gemma" : "Ollama";
                    m.SortPriority = m.IsGemma ? 420 : 280;
                    models.Add(m);
                }
            }
            else
            {
                string ollamaExe = FindToolPath(tools, "ollama_cli");
                if (!string.IsNullOrEmpty(ollamaExe))
                {
                    string listOut = RunQuickStdout(ollamaExe, "list", 1800);
                    string[] lines = listOut.Split('\n');
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string ln = lines[i].Trim();
                        if (ln.Length == 0) continue;
                        string tag = ln.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (!dedupe.Add("ollama:" + tag)) continue;
                        AiModelDescriptor m = new AiModelDescriptor();
                        m.Id = "ollama_cold_" + tag;
                        m.DisplayName = tag + " (Ollama Installed — Click Wake Ollama)";
                        m.Kind = "Ollama-Installed";
                        m.EndpointUrl = "http://127.0.0.1:11434/api/chat";
                        m.ModelTag = tag;
                        m.FilePath = ollamaExe;
                        m.IsLiveServer = false;
                        m.IsGemma = tag.IndexOf("gemma", StringComparison.OrdinalIgnoreCase) >= 0;
                        m.Family = m.IsGemma ? "Gemma" : "Ollama";
                        m.SortPriority = m.IsGemma ? 260 : 180;
                        models.Add(m);
                    }
                }
            }

            // 2. Probe OpenAI-compatible local servers (Sovereign :8097, llama-server :8080, LM Studio :1234, Local :8000)
            int[] oaiPorts = new int[] { 8097, 8080, 1234, 8000 };
            string[] oaiLabels = new string[] { "Sovereign Manifold :8097", "llama-server :8080", "LM Studio :1234", "Local OpenAI :8000" };
            for (int p = 0; p < oaiPorts.Length; p++)
            {
                if (!ProbeTcpPort(oaiPorts[p], 140)) continue;
                string mJson = HttpGetFast("http://127.0.0.1:" + oaiPorts[p] + "/v1/models", 1400);
                List<string> ids = ForgeJson.ExtractAllStringFields(mJson, "id");
                if (ids.Count == 0) ids.Add(oaiPorts[p] == 8097 ? "gemma-4-12B-it-sh4z" : "gemma-local");
                for (int k = 0; k < ids.Count; k++)
                {
                    string mid = ids[k];
                    if (!dedupe.Add("oai:" + oaiPorts[p] + ":" + mid)) continue;
                    AiModelDescriptor m = new AiModelDescriptor();
                    m.Id = "oai_" + oaiPorts[p] + "_" + mid;
                    m.DisplayName = mid + " (" + oaiLabels[p] + ")";
                    m.Kind = "OpenAI-Live";
                    m.EndpointUrl = "http://127.0.0.1:" + oaiPorts[p] + "/v1/chat/completions";
                    m.ModelTag = mid;
                    m.IsLiveServer = true;
                    bool namedGemma = mid.IndexOf("gemma", StringComparison.OrdinalIgnoreCase) >= 0;
                    m.IsGemma = namedGemma || oaiPorts[p] == 8097;
                    m.Family = m.IsGemma ? "Gemma" : "OpenAI-Local";
                    m.SortPriority = namedGemma ? 400 : (oaiPorts[p] == 8097 ? 320 : 250);
                    models.Add(m);
                }
            }

            // 3. Multi-drive model weight scan (.gguf, .safetensors, .litertlm, .onnx, .sh4z)
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] roots = new string[]
            {
                (Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')),
                (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')),
                @"E:\Models",
                @"C:\Models",
                Path.Combine(user, ".lmstudio", "models"),
                Path.Combine(user, ".cache", "huggingface", "hub"),
                Path.Combine(user, "Downloads")
            };
            for (int r = 0; r < roots.Length; r++)
                ScanModelFilesBounded(roots[r], 4, models, dedupe, sovereign8097Live);

            // Strictly rank Live Gemma servers first, then Installed Ollama Gemma, then other Live servers, then Disk weights!
            models.Sort(delegate(AiModelDescriptor a, AiModelDescriptor b)
            {
                if (a.SortPriority != b.SortPriority) return b.SortPriority.CompareTo(a.SortPriority);
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            if (models.Count == 0)
            {
                AiModelDescriptor def = new AiModelDescriptor();
                def.Id = "default_gemma_endpoint";
                def.DisplayName = "gemma3:4b / gemma-4-12B-it-sh4z (Configure :11434 or :8097)";
                def.Kind = "Ollama-Endpoint";
                def.EndpointUrl = "http://127.0.0.1:11434/api/chat";
                def.ModelTag = "gemma3:4b";
                def.IsGemma = true;
                def.SortPriority = 100;
                models.Add(def);
            }

            return models;
        }

        public static string WakeOllamaServer(List<ToolDescriptor> tools)
        {
            if (ProbeTcpPort(11434, 150))
                return "Ollama server is already ONLINE at http://127.0.0.1:11434/api/chat.";

            string ollamaExe = FindToolPath(tools, "ollama_cli");
            if (string.IsNullOrEmpty(ollamaExe))
                return "ollama.exe was not found on this laptop.";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ollamaExe, "serve");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
                for (int w = 0; w < 12; w++)
                {
                    Thread.Sleep(220);
                    if (ProbeTcpPort(11434, 140))
                        return "Woke Ollama server -> ONLINE at http://127.0.0.1:11434/api/chat!";
                }
                return "Started ollama serve in background; waiting for :11434 to finish binding...";
            }
            catch (Exception ex)
            {
                return "Could not start ollama serve: " + ex.Message;
            }
        }

        private static void ScanModelFilesBounded(string dir, int depth, List<AiModelDescriptor> outList, HashSet<string> dedupe, bool sovereign8097Live)
        {
            if (depth < 0 || string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || outList.Count >= 120) return;
            try
            {
                string[] files = Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant();
                    if (ext != ".gguf" && ext != ".safetensors" && ext != ".litertlm" && ext != ".onnx" && ext != ".sh4z" && ext != ".tflite")
                        continue;

                    FileInfo fi = new FileInfo(f);
                    long minBytes = (ext == ".sh4z") ? 8192L : (4L * 1048576L);
                    if (fi.Length < minBytes) continue;
                    if (!dedupe.Add("file:" + f)) continue;

                    string fn = fi.Name;
                    bool isGem = f.IndexOf("gemma", StringComparison.OrdinalIgnoreCase) >= 0 || ext == ".sh4z";
                    AiModelDescriptor m = new AiModelDescriptor();
                    m.Id = "disk_" + fn;
                    m.DisplayName = fn;
                    m.Kind = ext == ".gguf" ? "Disk-GGUF" : (ext == ".sh4z" ? "Sovereign-SH4Z" : ("Disk-" + ext.TrimStart('.').ToUpperInvariant()));
                    if (ext == ".sh4z")
                    {
                        m.EndpointUrl = "http://127.0.0.1:8097/v1/chat/completions";
                        m.ModelTag = "gemma-4-12B-it-sh4z";
                        m.IsLiveServer = sovereign8097Live;
                    }
                    else
                    {
                        m.EndpointUrl = "http://127.0.0.1:8080/v1/chat/completions";
                        m.ModelTag = Path.GetFileNameWithoutExtension(fn);
                        m.IsLiveServer = false;
                    }
                    m.FilePath = f;
                    m.SizeBytes = fi.Length;
                    m.IsGemma = isGem;
                    m.Family = isGem ? "Gemma" : "Weights";
                    m.SortPriority = ext == ".sh4z" ? 95 : (isGem ? 90 : 50);
                    outList.Add(m);
                }
                if (depth == 0) return;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length; s++)
                {
                    string nm = Path.GetFileName(subs[s]);
                    if (nm.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || nm.Equals(".git", StringComparison.OrdinalIgnoreCase) || nm.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                        continue;
                    ScanModelFilesBounded(subs[s], depth - 1, outList, dedupe, sovereign8097Live);
                }
            }
            catch { }
        }

        private static string FindToolPath(List<ToolDescriptor> tools, string id)
        {
            if (tools == null) return "";
            for (int i = 0; i < tools.Count; i++)
                if (tools[i].Id == id && tools[i].Found) return tools[i].ExecutablePath;
            return "";
        }

        private static string HttpGetFast(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch { return ""; }
        }

        private static string RunQuickStdout(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(timeoutMs);
                    return o ?? "";
                }
            }
            catch { return ""; }
        }
    }
}
