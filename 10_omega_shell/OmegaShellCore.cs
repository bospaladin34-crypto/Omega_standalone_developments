using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OmegaShellStudio
{
    public sealed class OshRow
    {
        public readonly List<string> Columns = new List<string>();
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public OshRow Set(string col, object val)
        {
            if (!Values.ContainsKey(col)) Columns.Add(col);
            Values[col] = val;
            return this;
        }

        public string GetString(string col)
        {
            object v;
            if (!Values.TryGetValue(col, out v) || v == null) return "";
            if (v is double) return ((double)v).ToString("0.##", CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public double GetDouble(string col, out bool isNum)
        {
            isNum = false;
            object v;
            if (!Values.TryGetValue(col, out v) || v == null) return 0.0;
            if (v is int) { isNum = true; return (int)v; }
            if (v is long) { isNum = true; return (long)v; }
            if (v is double) { isNum = true; return (double)v; }
            if (v is float) { isNum = true; return (float)v; }
            double parsed;
            string s = v.ToString().Trim();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                isNum = true;
                return parsed;
            }
            return 0.0;
        }
    }

    public sealed class OshExecOutput
    {
        public bool Ok = true;
        public string CommandText = "";
        public List<OshRow> Rows = new List<OshRow>();
        public string TextOutput = "";
        public long ElapsedMs = 0L;
        public string Sha256Receipt = "";
        public string EditTargetFile = "";

        public string RenderConsoleText()
        {
            if (Rows != null && Rows.Count > 0)
            {
                string tbl = OshTableFormatter.FormatRows(Rows, 140);
                if (!string.IsNullOrWhiteSpace(TextOutput))
                    return tbl + "\r\n" + TextOutput.Trim();
                return tbl;
            }
            return TextOutput ?? "";
        }
    }

    public static class OshTableFormatter
    {
        public static string FormatRows(List<OshRow> rows, int maxRows)
        {
            if (rows == null || rows.Count == 0) return "(0 rows)";
            List<string> cols = CollectColumns(rows);
            if (cols.Count == 0) return "(empty schema)";

            int limit = Math.Min(rows.Count, maxRows);
            int[] widths = new int[cols.Count];
            for (int c = 0; c < cols.Count; c++) widths[c] = Math.Max(3, cols[c].Length);

            for (int r = 0; r < limit; r++)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    string cell = rows[r].GetString(cols[c]).Replace("\r", " ").Replace("\n", " ");
                    if (cell.Length > 44) cell = cell.Substring(0, 41) + "...";
                    if (cell.Length > widths[c]) widths[c] = cell.Length;
                }
            }

            StringBuilder sb = new StringBuilder();
            for (int c = 0; c < cols.Count; c++)
            {
                if (c > 0) sb.Append("  ");
                sb.Append(cols[c].PadRight(widths[c]));
            }
            sb.AppendLine();
            for (int c = 0; c < cols.Count; c++)
            {
                if (c > 0) sb.Append("  ");
                sb.Append(new string('-', widths[c]));
            }
            sb.AppendLine();

            for (int r = 0; r < limit; r++)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    if (c > 0) sb.Append("  ");
                    string cell = rows[r].GetString(cols[c]).Replace("\r", " ").Replace("\n", " ");
                    if (cell.Length > 44) cell = cell.Substring(0, 41) + "...";
                    sb.Append(cell.PadRight(widths[c]));
                }
                sb.AppendLine();
            }
            if (rows.Count > limit)
                sb.AppendLine("... (" + (rows.Count - limit) + " more rows; inspect full sortable table in Grid view)");
            else
                sb.AppendLine("(" + rows.Count + " row" + (rows.Count == 1 ? "" : "s") + ")");
            return sb.ToString().TrimEnd();
        }

        public static List<string> CollectColumns(List<OshRow> rows)
        {
            List<string> cols = new List<string>();
            if (rows == null) return cols;
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < rows[r].Columns.Count; c++)
                {
                    string cn = rows[r].Columns[c];
                    bool seen = false;
                    for (int k = 0; k < cols.Count; k++)
                        if (string.Equals(cols[k], cn, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
                    if (!seen) cols.Add(cn);
                }
            }
            return cols;
        }

        public static string FormatCsv(List<OshRow> rows, char sep)
        {
            List<string> cols = CollectColumns(rows);
            if (cols.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int c = 0; c < cols.Count; c++)
            {
                if (c > 0) sb.Append(sep);
                sb.Append(EscapeCsvCell(cols[c], sep));
            }
            sb.AppendLine();
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    if (c > 0) sb.Append(sep);
                    sb.Append(EscapeCsvCell(rows[r].GetString(cols[c]), sep));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string EscapeCsvCell(string val, char sep)
        {
            string s = (val ?? "").Replace("\r", " ").Replace("\n", " ");
            if (s.IndexOf(sep) >= 0 || s.IndexOf('"') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static string FormatJson(List<OshRow> rows)
        {
            StringBuilder sb = new StringBuilder("[");
            if (rows != null)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    if (r > 0) sb.Append(",");
                    sb.Append("{");
                    for (int c = 0; c < rows[r].Columns.Count; c++)
                    {
                        if (c > 0) sb.Append(",");
                        string col = rows[r].Columns[c];
                        object v = rows[r].Values[col];
                        sb.Append("\"" + OshFormat.JsonEsc(col) + "\":");
                        if (v is bool) sb.Append(((bool)v) ? "true" : "false");
                        else if (v is int || v is long) sb.Append(v.ToString());
                        else if (v is double) sb.Append(((double)v).ToString("0.####", CultureInfo.InvariantCulture));
                        else sb.Append("\"" + OshFormat.JsonEsc(v != null ? v.ToString() : "") + "\"");
                    }
                    sb.Append("}");
                }
            }
            sb.Append("]");
            return sb.ToString();
        }
    }

    public sealed class ShellSettingsProfile
    {
        public float FontSize = 9.5f;
        public string DefaultAiEndpoint = "tandem://auto";
        public string DefaultAiModel = "auto-gemma-router";
        public string DebianLinuxUser = "droid";
        public bool FormatOnSave = true;
        public bool EnforceZeroBacktickPs1 = true;
        public bool AutoHealCodegen = true;
        public HashSet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ProcessCoreRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static ShellSettingsProfile CreateDefault(string configDir)
        {
            ShellSettingsProfile p = new ShellSettingsProfile();
            string[] coreSafe = new string[]
            {
                "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass", "svchost",
                "winlogon", "dwm", "explorer", "fontdrvhost", "sihost", "taskhostw", "audiodg",
                "OmegaShell", "OmegaLabLauncher", "OmegaBrowser", "OmegaMirror", "OmegaAV", "OmegaForge",
                "v31_swarm", "ollama", "llama-server", "rustc", "lean", "csc", "clang++", "dxc", "fxc", "powershell", "sshd"
            };
            for (int i = 0; i < coreSafe.Length; i++) p.ProtectedProcesses.Add(coreSafe[i]);

            p.Aliases["ll"] = "ls";
            p.Aliases["top-ram"] = "ps | group name | sort -desc sum_ram_mb | top 15";
            p.Aliases["listen"] = "ports -listen";
            p.Aliases["lab"] = "cloud status";
            p.Aliases["free-ram"] = "opt trim-ram";
            p.Aliases["boost"] = "opt boost";
            p.Aliases["hogs"] = "disk-hogs";
            p.Aliases["deb"] = "avf status";
            p.LoadFromDisk(configDir);
            return p;
        }

        public void LoadFromDisk(string configDir)
        {
            try
            {
                string pFile = Path.Combine(configDir, "osh_settings.tsv");
                if (!File.Exists(pFile)) return;
                string[] lines = File.ReadAllLines(pFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length < 2) continue;
                    if (parts[0] == "font_size") { float fs; if (float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out fs) && fs >= 7f && fs <= 18f) FontSize = fs; }
                    else if (parts[0] == "ai_endpoint") DefaultAiEndpoint = parts[1];
                    else if (parts[0] == "ai_model") DefaultAiModel = parts[1];
                    else if (parts[0] == "debian_user" && !string.IsNullOrWhiteSpace(parts[1])) DebianLinuxUser = parts[1].Trim();
                    else if (parts[0] == "fmt_on_save") FormatOnSave = (parts[1].Trim() == "1" || parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (parts[0] == "zero_bt_ps1") EnforceZeroBacktickPs1 = (parts[1].Trim() == "1" || parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (parts[0] == "auto_heal_codegen") AutoHealCodegen = (parts[1].Trim() == "1" || parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (parts[0] == "whitelist" && !string.IsNullOrWhiteSpace(parts[1])) ProtectedProcesses.Add(parts[1].Trim());
                    else if (parts[0] == "alias" && parts.Length >= 3) Aliases[parts[1].Trim()] = parts[2];
                    else if (parts[0] == "proc_rule" && parts.Length >= 3) ProcessCoreRules[parts[1].Trim()] = parts[2].Trim().ToLowerInvariant();
                }
            }
            catch { }
        }

        public void SaveToDisk(string configDir)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                List<string> lines = new List<string>();
                lines.Add("font_size\t" + FontSize.ToString("0.0", CultureInfo.InvariantCulture));
                lines.Add("ai_endpoint\t" + DefaultAiEndpoint);
                lines.Add("ai_model\t" + DefaultAiModel);
                lines.Add("debian_user\t" + DebianLinuxUser);
                lines.Add("fmt_on_save\t" + (FormatOnSave ? "1" : "0"));
                lines.Add("zero_bt_ps1\t" + (EnforceZeroBacktickPs1 ? "1" : "0"));
                lines.Add("auto_heal_codegen\t" + (AutoHealCodegen ? "1" : "0"));
                foreach (string w in ProtectedProcesses) lines.Add("whitelist\t" + w);
                foreach (KeyValuePair<string, string> kv in Aliases) lines.Add("alias\t" + kv.Key + "\t" + kv.Value);
                foreach (KeyValuePair<string, string> pr in ProcessCoreRules) lines.Add("proc_rule\t" + pr.Key + "\t" + pr.Value);
                File.WriteAllLines(Path.Combine(configDir, "osh_settings.tsv"), lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    public static class OshFormat
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0L) bytes = 0L;
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
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

        public static string JsonUnesc(string raw)
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
                        int hv;
                        if (int.TryParse(raw.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hv))
                        {
                            sb.Append((char)hv);
                            i += 4;
                        }
                    }
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string JsonEsc(string s)
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
    }

    public interface IOshApiHost
    {
        string HandleStatusJson();
        string HandleExecJson(string bodyJson);
        string HandleTunerJson();
        string HandleFormatJson(string bodyJson);
        string HandleAvfStatusJson();
        string HandleModelsJson();
        string HandleGemmaMissionJson(string bodyJson);
        string HandleCodegenJson(string bodyJson);
    }

    public sealed class OshLocalApiServer
    {
        private TcpListener _listener;
        private volatile bool _running;
        private readonly int _port;
        private readonly IOshApiHost _host;

        public OshLocalApiServer(int port, IOshApiHost host)
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
                            ThreadPool.QueueUserWorkItem(ServeClient, cl);
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

        private void ServeClient(object state)
        {
            using (TcpClient cl = (TcpClient)state)
            {
                try
                {
                    cl.ReceiveTimeout = 10000;
                    cl.SendTimeout = 120000;
                    NetworkStream ns = cl.GetStream();
                    byte[] buf = new byte[262144];
                    int rd = ReadFullHttpRequest(ns, buf);
                    if (rd <= 0) return;
                    string raw = Encoding.UTF8.GetString(buf, 0, rd);
                    int hdrEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string firstLine = hdrEnd > 0 ? raw.Substring(0, raw.IndexOf("\r\n", StringComparison.Ordinal)) : raw.Split('\n')[0];
                    string body = hdrEnd > 0 ? raw.Substring(hdrEnd + 4) : "";
                    string[] parts = firstLine.Trim().Split(' ');
                    string path = parts.Length >= 2 ? parts[1].Split('?')[0].ToLowerInvariant() : "/";

                    string json;
                    if (path == "/api/exec") json = _host.HandleExecJson(body);
                    else if (path == "/api/tuner") json = _host.HandleTunerJson();
                    else if (path == "/api/format") json = _host.HandleFormatJson(body);
                    else if (path == "/api/avf") json = _host.HandleAvfStatusJson();
                    else if (path == "/api/models") json = _host.HandleModelsJson();
                    else if (path == "/api/gemma") json = _host.HandleGemmaMissionJson(body);
                    else if (path == "/api/codegen") json = _host.HandleCodegenJson(body);
                    else json = _host.HandleStatusJson();

                    byte[] payload = Encoding.UTF8.GetBytes(json ?? "{\"ok\":true}");
                    string hdr = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] hb = Encoding.ASCII.GetBytes(hdr);
                    ns.Write(hb, 0, hb.Length);
                    ns.Write(payload, 0, payload.Length);
                }
                catch { }
            }
        }

        private static int ReadFullHttpRequest(NetworkStream ns, byte[] buf)
        {
            int total = 0;
            int contentLen = -1;
            int headerEndPos = -1;

            while (total < buf.Length)
            {
                int rd = ns.Read(buf, total, buf.Length - total);
                if (rd <= 0) break;
                total += rd;

                if (headerEndPos < 0)
                {
                    string head = Encoding.ASCII.GetString(buf, 0, total);
                    headerEndPos = head.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headerEndPos >= 0)
                    {
                        contentLen = 0;
                        string[] lines = head.Substring(0, headerEndPos).Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string ln = lines[i].Trim();
                            if (ln.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(ln.Substring(15).Trim(), out contentLen);
                                break;
                            }
                        }
                    }
                }

                if (headerEndPos >= 0 && total >= headerEndPos + 4 + contentLen)
                    break;
            }
            return total;
        }
    }
}
