using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OmegaForgeStudio
{
    public sealed class ToolDescriptor
    {
        public string Id = "";
        public string Name = "";
        public string Category = "Compilers";
        public string ExecutablePath = "";
        public string VersionArgs = "--version";
        public string VersionBanner = "Not probed";
        public string SampleCode = "";
        public bool Found = false;
        public bool Enabled = false;
        public bool IsBuiltInVirtual = false;

        public string DisplayLabel
        {
            get
            {
                string st = (Found || IsBuiltInVirtual) ? "OK" : "--";
                string ver = string.IsNullOrEmpty(VersionBanner) ? "" : (" :: " + VersionBanner);
                return "[" + st + "] " + Name + " (" + Category + ")" + ver;
            }
        }
    }

    public sealed class AiModelDescriptor
    {
        public string Id = "";
        public string DisplayName = "";
        public string Family = "Local";
        public string Kind = "Ollama-Live";
        public string EndpointUrl = "";
        public string ModelTag = "";
        public string FilePath = "";
        public long SizeBytes = 0L;
        public string QuantTag = "";
        public bool IsLiveServer = false;
        public bool IsGemma = false;
        public int SortPriority = 0;

        public override string ToString()
        {
            string gem = IsGemma ? "[GEMMA] " : "";
            string sz = SizeBytes > 0L ? (" (" + ForgeFormat.FormatBytes(SizeBytes) + ")") : "";
            return gem + "[" + Kind + "] " + DisplayName + sz;
        }
    }

    public sealed class ToolExecResult
    {
        public bool Ok = false;
        public int ExitCode = -1;
        public long ElapsedMs = 0L;
        public string ToolId = "";
        public string CommandLine = "";
        public string Stdout = "";
        public string Stderr = "";
        public string ArtifactPath = "";
        public string Sha256Receipt = "";

        public string ToCompactReport()
        {
            StringBuilder sb = new StringBuilder();
            string shaShort = (Sha256Receipt != null && Sha256Receipt.Length >= 12) ? Sha256Receipt.Substring(0, 12) : "n/a";
            sb.AppendLine("[Tool: " + ToolId + " | Exit: " + ExitCode + " | Time: " + ElapsedMs + " ms | SHA-256: " + shaShort + "]");
            if (!string.IsNullOrEmpty(CommandLine)) sb.AppendLine("Cmd: " + CommandLine);
            if (!string.IsNullOrEmpty(ArtifactPath)) sb.AppendLine("Artifact: " + ArtifactPath);
            if (!string.IsNullOrWhiteSpace(Stdout)) sb.AppendLine("STDOUT:\r\n" + Truncate(Stdout.Trim(), 3600));
            if (!string.IsNullOrWhiteSpace(Stderr)) sb.AppendLine("STDERR:\r\n" + Truncate(Stderr.Trim(), 2600));
            return sb.ToString().Trim();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s ?? "";
            return s.Substring(0, maxLen) + "\r\n...[truncated " + (s.Length - maxLen) + " chars]";
        }
    }

    public sealed class HarvestedBlock
    {
        public string Kind = "fenced_code";
        public string Lang = "";
        public string Code = "";
        public int TheoremCount = 0;
        public int DefCount = 0;
        public bool HasSorry = false;
        public string SummaryLabel = "";
    }

    public sealed class StreamTelemetry
    {
        public int ApproxTokens = 0;
        public double TokensPerSec = 0.0;
        public double Unique4GramPct = 100.0;
        public int Total4Grams = 0;
        public long ElapsedMs = 0L;
        public bool WasStreaming = false;
        public bool Aborted = false;
    }

    public sealed class ChatTurnMessage
    {
        public string Role = "user";
        public string Content = "";
        public ChatTurnMessage() { }
        public ChatTurnMessage(string role, string content) { Role = role; Content = content; }
    }

    public static class ForgeFormat
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0L) bytes = 0L;
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
        }

        public static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Sha256File(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
                using (FileStream fs = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(64);
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        public static double ComputeUnique4GramPct(string text, out int total4Grams, out int wordTokens)
        {
            total4Grams = 0;
            wordTokens = 0;
            if (string.IsNullOrWhiteSpace(text)) return 100.0;
            string[] raw = text.Split(new char[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            wordTokens = raw.Length;
            if (raw.Length < 4) return 100.0;
            HashSet<string> uniq = new HashSet<string>(StringComparer.Ordinal);
            int n = raw.Length - 3;
            total4Grams = n;
            for (int i = 0; i < n; i++)
            {
                string gram = raw[i] + "\u001f" + raw[i + 1] + "\u001f" + raw[i + 2] + "\u001f" + raw[i + 3];
                uniq.Add(gram);
            }
            return (uniq.Count * 100.0) / (double)n;
        }
    }

    public static class ForgeJson
    {
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string UnescapeJsonString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\\' && i + 1 < raw.Length)
                {
                    char n = raw[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else if (n == '"') sb.Append('"');
                    else if (n == '\\') sb.Append('\\');
                    else if (n == '/') sb.Append('/');
                    else if (n == 'u' && i + 4 < raw.Length)
                    {
                        int hexVal;
                        if (int.TryParse(raw.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out hexVal))
                        {
                            sb.Append((char)hexVal);
                            i += 4;
                        }
                    }
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string ExtractStringField(string json, string fieldName)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName)) return "";
            string pat = "\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            Match m = Regex.Match(json, pat, RegexOptions.Singleline);
            if (!m.Success) return "";
            return UnescapeJsonString(m.Groups[1].Value);
        }

        public static List<string> ExtractAllStringFields(string json, string fieldName)
        {
            List<string> res = new List<string>();
            if (string.IsNullOrEmpty(json)) return res;
            string pat = "\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            MatchCollection mc = Regex.Matches(json, pat, RegexOptions.Singleline);
            for (int i = 0; i < mc.Count; i++)
                res.Add(UnescapeJsonString(mc[i].Groups[1].Value));
            return res;
        }

        public static string ExtractStreamDeltaPiece(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            string clean = line.Trim();
            if (clean.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(5).Trim();
            if (clean == "[DONE]" || clean.Length == 0 || clean[0] != '{') return "";

            Match mDelta = Regex.Match(clean, "\"delta\"\\s*:\\s*\\{[^}]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (mDelta.Success) return UnescapeJsonString(mDelta.Groups[1].Value);

            Match mMsg = Regex.Match(clean, "\"message\"\\s*:\\s*\\{[^}]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (mMsg.Success) return UnescapeJsonString(mMsg.Groups[1].Value);

            string resp = ExtractStringField(clean, "response");
            if (!string.IsNullOrEmpty(resp)) return resp;

            return "";
        }

        public static string ExtractChatReplyContent(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return "";
            Match mMsg = Regex.Match(rawJson, "\"message\"\\s*:\\s*\\{.*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (mMsg.Success) return UnescapeJsonString(mMsg.Groups[1].Value);
            string c = ExtractStringField(rawJson, "content");
            if (!string.IsNullOrEmpty(c)) return c;
            string r = ExtractStringField(rawJson, "response");
            if (!string.IsNullOrEmpty(r)) return r;
            return rawJson;
        }
    }

    public interface IForgeHostApi
    {
        string BuildStatusJson();
        string BuildToolsJson();
        string BuildModelsJson();
        string ExecuteApiRequestJson(string bodyJson);
        string VerifyOutputApiJson(string bodyJson);
    }

    public sealed class ForgeLocalApiServer
    {
        private TcpListener _listener;
        private volatile bool _running;
        private readonly int _port;
        private readonly IForgeHostApi _host;

        public ForgeLocalApiServer(int port, IForgeHostApi host)
        {
            _port = port;
            _host = host;
        }

        public void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _running = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    while (_running)
                    {
                        try
                        {
                            TcpClient cl = _listener.AcceptTcpClient();
                            ThreadPool.QueueUserWorkItem(HandleClient, cl);
                        }
                        catch { if (!_running) break; }
                    }
                });
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }

        private void HandleClient(object state)
        {
            using (TcpClient cl = (TcpClient)state)
            {
                try
                {
                    cl.ReceiveTimeout = 8000;
                    cl.SendTimeout = 20000;
                    NetworkStream ns = cl.GetStream();
                    byte[] buf = new byte[131072];
                    int rd = ns.Read(buf, 0, buf.Length);
                    if (rd <= 0) return;

                    string rawReq = Encoding.UTF8.GetString(buf, 0, rd);
                    int hdrEnd = rawReq.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string firstLine = hdrEnd > 0 ? rawReq.Substring(0, rawReq.IndexOf("\r\n", StringComparison.Ordinal)) : rawReq.Split('\n')[0];
                    string body = hdrEnd > 0 ? rawReq.Substring(hdrEnd + 4) : "";

                    string[] parts = firstLine.Trim().Split(' ');
                    string path = parts.Length >= 2 ? parts[1].Split('?')[0].ToLowerInvariant() : "/";

                    string respJson;
                    if (path == "/api/tools") respJson = _host.BuildToolsJson();
                    else if (path == "/api/models") respJson = _host.BuildModelsJson();
                    else if (path == "/api/exec") respJson = _host.ExecuteApiRequestJson(body);
                    else if (path == "/api/verify") respJson = _host.VerifyOutputApiJson(body);
                    else respJson = _host.BuildStatusJson();

                    byte[] payload = Encoding.UTF8.GetBytes(respJson ?? "{\"ok\":true}");
                    string hdr = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] hdrBytes = Encoding.ASCII.GetBytes(hdr);
                    ns.Write(hdrBytes, 0, hdrBytes.Length);
                    ns.Write(payload, 0, payload.Length);
                }
                catch { }
            }
        }
    }
}
