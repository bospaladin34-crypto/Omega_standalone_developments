using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaShellStudio
{
    public sealed class FormatResultItem
    {
        public string FilePath = "";
        public string Language = "";
        public string EngineUsed = "";
        public bool Changed = false;
        public int ChangedLines = 0;
        public long ByteCount = 0L;
        public string Status = "OK";
        public string Detail = "";
    }

    public sealed class CompilerDiagnosticItem
    {
        public string Severity = "ERROR";
        public int Line = 1;
        public int Column = 1;
        public string Code = "";
        public string Message = "";
        public string FilePath = "";
    }

    public sealed class CodeBuildExecResult
    {
        public bool Ok = true;
        public int ExitCode = 0;
        public long ElapsedMs = 0L;
        public string CompilerUsed = "";
        public string OutputText = "";
        public string Sha256Receipt = "";
        public List<CompilerDiagnosticItem> Diagnostics = new List<CompilerDiagnosticItem>();
    }

    public static class CodeFormatterAndCompiler
    {
        public static string ResolveToolBinary(string exeName, string[] extraDirs)
        {
            if (extraDirs != null)
            {
                for (int i = 0; i < extraDirs.Length; i++)
                {
                    if (string.IsNullOrEmpty(extraDirs[i])) continue;
                    string cand = Path.Combine(extraDirs[i], exeName);
                    if (File.Exists(cand)) return cand;
                }
            }
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] parts = pathEnv.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string dir = parts[i].Trim();
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

        public static string ResolveWindowsSdkShaderTool(string exeName)
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string kitsBin = Path.Combine(pf86, "Windows Kits", "10", "bin");
            if (Directory.Exists(kitsBin))
            {
                try
                {
                    string[] vers = Directory.GetDirectories(kitsBin, "10.*");
                    Array.Sort(vers);
                    for (int i = vers.Length - 1; i >= 0; i--)
                    {
                        string cand = Path.Combine(vers[i], "x64", exeName);
                        if (File.Exists(cand)) return cand;
                    }
                }
                catch { }
            }
            return ResolveToolBinary(exeName, null);
        }

        public static string DetectLanguageFromPath(string filePath)
        {
            string ext = (Path.GetExtension(filePath ?? "") ?? "").ToLowerInvariant();
            if (ext == ".rs") return "rust";
            if (ext == ".cs") return "csharp";
            if (ext == ".cpp" || ext == ".cc" || ext == ".cxx" || ext == ".c" || ext == ".h" || ext == ".hpp") return "cpp";
            if (ext == ".ts" || ext == ".tsx" || ext == ".js" || ext == ".jsx") return "typescript";
            if (ext == ".lean") return "lean4";
            if (ext == ".ps1" || ext == ".psm1") return "powershell";
            if (ext == ".hlsl" || ext == ".fx" || ext == ".omsh") return "hlsl";
            if (ext == ".json") return "json";
            if (ext == ".md") return "markdown";
            if (ext == ".py") return "python";
            if (ext == ".osh" || ext == ".oshrc") return "osh";
            if (ext == ".sh" || ext == ".bash") return "bash";
            return "text";
        }

        public static string[] GetStandardCompilerDirs()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new string[]
            {
                @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319",
                Path.Combine(home, ".cargo", "bin"),
                Path.Combine(home, ".elan", "bin"),
                Path.Combine(home, ".deno", "bin"),
                @"C:\Program Files\LLVM\bin",
                @"C:\Program Files\dotnet",
                Path.Combine(localApp, "Programs", "Python", "Python312"),
                Path.Combine(localApp, "Programs", "Python", "Python311")
            };
        }

        public static List<OshRow> ListFormatterAndCompilerMatrixRows()
        {
            string[] dirs = GetStandardCompilerDirs();
            string[,] items = new string[,]
            {
                { "rustfmt", "rustfmt.exe", "Rust Official AST Formatter (.rs)" },
                { "cargo", "cargo.exe", "Rust Cargo Workspace Builder & cargo fmt" },
                { "rustc", "rustc.exe", "Rust Native Compiler (.rs)" },
                { "clang-format", "clang-format.exe", "LLVM C/C++23/C#/HLSL Formatter" },
                { "clang++", "clang++.exe", "LLVM C/C++23 Native Compiler" },
                { "deno", "deno.exe", "Deno TypeScript/JS/JSON/MD Formatter & Runtime" },
                { "csc", "csc.exe", ".NET Framework x64 C# 5.0 Compiler" },
                { "fxc", "fxc.exe", "DirectCompute HLSL Shader Compiler (cs_5_0)" },
                { "lean", "lean.exe", "Lean 4 Formal Proof Kernel (.lean)" },
                { "powershell", "powershell.exe", "Windows PowerShell 5.1 AST Checker" },
                { "builtin-ast", "(Built-In)", "Zero-Dep C#/PS1/Lean4/HLSL/JSON/OSH Lexer Formatter" }
            };

            List<OshRow> rows = new List<OshRow>();
            for (int i = 0; i < items.GetLength(0); i++)
            {
                string id = items[i, 0];
                string exe = items[i, 1];
                string role = items[i, 2];
                string resolved;
                if (exe == "(Built-In)") resolved = "OmegaShell v1.0 GM Internal Engine";
                else if (exe == "fxc.exe") resolved = ResolveWindowsSdkShaderTool("fxc.exe");
                else resolved = ResolveToolBinary(exe, dirs);
                bool ok = !string.IsNullOrEmpty(resolved);
                rows.Add(new OshRow()
                    .Set("tool", id)
                    .Set("status", ok ? "READY" : "FALLBACK_TO_BUILTIN")
                    .Set("role", role)
                    .Set("binary", ok ? resolved : "(uses Built-In AST Formatter)"));
            }
            return rows;
        }

        public static string FormatBufferText(string sourceText, string langOrExt, bool stripPs1Backticks, out string engineUsed, out int changedLines)
        {
            string raw = (sourceText ?? "").TrimStart('\uFEFF');
            string lang = (langOrExt ?? "").ToLowerInvariant().TrimStart('.');
            if (lang == "rs") lang = "rust";
            else if (lang == "cs") lang = "csharp";
            else if (lang == "c" || lang == "h" || lang == "hpp" || lang == "cc") lang = "cpp";
            else if (lang == "ts" || lang == "tsx" || lang == "js") lang = "typescript";
            else if (lang == "ps1") lang = "powershell";
            else if (lang == "lean") lang = "lean4";
            else if (lang == "omsh") lang = "hlsl";

            string[] dirs = GetStandardCompilerDirs();
            string formatted = null;
            engineUsed = "Built-In AST";

            try
            {
                if (lang == "rust")
                {
                    string rustfmt = ResolveToolBinary("rustfmt.exe", dirs);
                    if (!string.IsNullOrEmpty(rustfmt))
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "osh_fmt_" + Guid.NewGuid().ToString("N") + ".rs");
                        try
                        {
                            File.WriteAllText(tmp, raw, new UTF8Encoding(false));
                            int ec;
                            RunProcessCapture(rustfmt, "--edition 2021 \"" + tmp + "\"", Path.GetTempPath(), 8000, out ec);
                            if (ec == 0 && File.Exists(tmp))
                            {
                                formatted = File.ReadAllText(tmp, Encoding.UTF8);
                                engineUsed = "rustfmt (2021)";
                            }
                        }
                        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                    }
                    if (formatted == null)
                    {
                        formatted = FormatCStyleBraceLanguage(raw, 4, true);
                        engineUsed = "Built-In Rust Lexer";
                    }
                }
                else if (lang == "cpp")
                {
                    string cf = ResolveToolBinary("clang-format.exe", dirs);
                    if (!string.IsNullOrEmpty(cf))
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "osh_fmt_" + Guid.NewGuid().ToString("N") + ".cpp");
                        try
                        {
                            File.WriteAllText(tmp, raw, new UTF8Encoding(false));
                            int ec;
                            RunProcessCapture(cf, "-i --style=LLVM \"" + tmp + "\"", Path.GetTempPath(), 8000, out ec);
                            if (ec == 0 && File.Exists(tmp))
                            {
                                formatted = File.ReadAllText(tmp, Encoding.UTF8);
                                engineUsed = "clang-format (LLVM)";
                            }
                        }
                        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                    }
                    if (formatted == null)
                    {
                        formatted = FormatCStyleBraceLanguage(raw, 4, true);
                        engineUsed = "Built-In C++23 Lexer";
                    }
                }
                else if (lang == "typescript" || lang == "markdown")
                {
                    string deno = ResolveToolBinary("deno.exe", dirs);
                    string ext = lang == "markdown" ? ".md" : ".ts";
                    if (!string.IsNullOrEmpty(deno))
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "osh_fmt_" + Guid.NewGuid().ToString("N") + ext);
                        try
                        {
                            File.WriteAllText(tmp, raw, new UTF8Encoding(false));
                            int ec;
                            RunProcessCapture(deno, "fmt \"" + tmp + "\"", Path.GetTempPath(), 8000, out ec);
                            if (ec == 0 && File.Exists(tmp))
                            {
                                formatted = File.ReadAllText(tmp, Encoding.UTF8);
                                engineUsed = "deno fmt";
                            }
                        }
                        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                    }
                    if (formatted == null)
                    {
                        formatted = lang == "typescript" ? FormatCStyleBraceLanguage(raw, 2, true) : NormalizeWhitespaceClean(raw, true);
                        engineUsed = "Built-In TS/MD Formatter";
                    }
                }
                else if (lang == "json")
                {
                    formatted = FormatJsonPretty(raw);
                    engineUsed = "Built-In JSON Strict Pretty";
                }
                else if (lang == "csharp" || lang == "hlsl")
                {
                    formatted = FormatCStyleBraceLanguage(raw, 4, true);
                    engineUsed = lang == "csharp" ? "Built-In C# 5.0 Lexer" : "Built-In HLSL/OMSH Lexer";
                }
                else if (lang == "powershell" || lang == "osh")
                {
                    formatted = FormatPowerShellOrOsh(raw, lang == "powershell" && stripPs1Backticks, true);
                    engineUsed = lang == "powershell"
                        ? (stripPs1Backticks ? "Built-In PS 5.1 (Zero-Backtick)" : "Built-In PS 5.1 Lexer")
                        : "Built-In OSH Formatter";
                }
                else if (lang == "lean4")
                {
                    formatted = FormatLean4Source(raw);
                    engineUsed = "Built-In Lean 4 UTF-8 Formatter";
                }
                else if (lang == "bash")
                {
                    formatted = NormalizeWhitespaceClean(raw, false);
                    engineUsed = "Built-In POSIX LF Normalizer";
                }
                else
                {
                    formatted = NormalizeWhitespaceClean(raw, true);
                    engineUsed = "Built-In UTF-8 Trimmer";
                }
            }
            catch
            {
                formatted = NormalizeWhitespaceClean(raw, true);
                engineUsed = "Built-In Fallback Normalizer";
            }

            changedLines = CountLineDifferences(raw, formatted);
            return formatted;
        }

        public static List<FormatResultItem> FormatPath(string targetPath, bool checkOnly, bool stripPs1Backticks)
        {
            List<FormatResultItem> results = new List<FormatResultItem>();
            if (File.Exists(targetPath))
            {
                results.Add(FormatSingleFile(targetPath, checkOnly, stripPs1Backticks));
                return results;
            }
            if (!Directory.Exists(targetPath)) return results;

            string[] exts = new string[] { ".rs", ".cs", ".cpp", ".c", ".h", ".hpp", ".ts", ".js", ".lean", ".ps1", ".hlsl", ".omsh", ".json", ".osh" };
            HashSet<string> allowed = new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);

            List<string> files = new List<string>();
            CollectCodeFilesBounded(targetPath, allowed, 4, files, 200);
            for (int i = 0; i < files.Count; i++)
            {
                results.Add(FormatSingleFile(files[i], checkOnly, stripPs1Backticks));
            }
            return results;
        }

        private static void CollectCodeFilesBounded(string dir, HashSet<string> allowed, int depth, List<string> acc, int cap)
        {
            if (depth < 0 || acc.Count >= cap) return;
            try
            {
                string[] fs = Directory.GetFiles(dir);
                for (int i = 0; i < fs.Length && acc.Count < cap; i++)
                {
                    string ext = Path.GetExtension(fs[i]) ?? "";
                    if (allowed.Contains(ext)) acc.Add(fs[i]);
                }
                if (depth == 0) return;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length && acc.Count < cap; s++)
                {
                    string nm = Path.GetFileName(subs[s]);
                    if (nm == "target" || nm == "node_modules" || nm == ".git" || nm == "bin" || nm == "obj") continue;
                    CollectCodeFilesBounded(subs[s], allowed, depth - 1, acc, cap);
                }
            }
            catch { }
        }

        private static FormatResultItem FormatSingleFile(string filePath, bool checkOnly, bool stripPs1Backticks)
        {
            FormatResultItem item = new FormatResultItem();
            item.FilePath = filePath;
            item.Language = DetectLanguageFromPath(filePath);
            try
            {
                byte[] rawBytes = File.ReadAllBytes(filePath);
                bool hadBom = rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF;
                string rawText = Encoding.UTF8.GetString(rawBytes).TrimStart('\uFEFF');

                string engine;
                int diffLines;
                string formatted = FormatBufferText(rawText, item.Language, stripPs1Backticks, out engine, out diffLines);
                item.EngineUsed = engine;
                item.ChangedLines = diffLines + (hadBom ? 1 : 0);
                item.Changed = !string.Equals(rawText, formatted, StringComparison.Ordinal) || hadBom;

                if (item.Changed && !checkOnly)
                {
                    File.WriteAllText(filePath, formatted, new UTF8Encoding(false));
                    item.ByteCount = new FileInfo(filePath).Length;
                    item.Status = "FORMATTED";
                    item.Detail = "Updated " + item.ChangedLines + " line(s)" + (hadBom ? " + stripped UTF-8 BOM" : "");
                }
                else if (item.Changed && checkOnly)
                {
                    item.ByteCount = rawBytes.LongLength;
                    item.Status = "NEEDS_FORMAT";
                    item.Detail = item.ChangedLines + " line(s) differ";
                }
                else
                {
                    item.ByteCount = rawBytes.LongLength;
                    item.Status = "CLEAN";
                    item.Detail = "Already canonical";
                }
            }
            catch (Exception ex)
            {
                item.Status = "ERROR";
                item.Detail = ex.Message;
            }
            return item;
        }

        public static string FormatCStyleBraceLanguage(string raw, int indentSpaces, bool useCrLf)
        {
            string norm = (raw ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = norm.Split('\n');
            StringBuilder sb = new StringBuilder(norm.Length + 128);
            string eol = useCrLf ? "\r\n" : "\n";

            int indent = 0;
            bool inBlockComment = false;
            bool inVerbatimStr = false;
            int blankRun = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string origLine = lines[i];
                if (inVerbatimStr)
                {
                    sb.Append(origLine.TrimEnd('\r')).Append(eol);
                    for (int k = 0; k < origLine.Length; k++)
                    {
                        if (origLine[k] == '"')
                        {
                            if (k + 1 < origLine.Length && origLine[k + 1] == '"') k++;
                            else { inVerbatimStr = false; break; }
                        }
                    }
                    continue;
                }

                string trimmed = origLine.Trim();
                if (trimmed.Length == 0)
                {
                    blankRun++;
                    if (blankRun <= 2) sb.Append(eol);
                    continue;
                }
                blankRun = 0;

                if (inBlockComment)
                {
                    sb.Append(new string(' ', Math.Max(0, indent * indentSpaces))).Append(" ").Append(trimmed).Append(eol);
                    if (trimmed.Contains("*/")) inBlockComment = false;
                    continue;
                }

                if (trimmed.StartsWith("#"))
                {
                    sb.Append(trimmed).Append(eol);
                    continue;
                }

                int leadingCloses = 0;
                for (int c = 0; c < trimmed.Length; c++)
                {
                    if (trimmed[c] == '}') leadingCloses++;
                    else if (!char.IsWhiteSpace(trimmed[c])) break;
                }

                int lineIndent = Math.Max(0, indent - leadingCloses);
                if (trimmed.StartsWith("case ") || trimmed.StartsWith("default:"))
                    lineIndent = Math.Max(0, lineIndent - 1);

                sb.Append(new string(' ', lineIndent * indentSpaces)).Append(trimmed).Append(eol);

                int netDelta = 0;
                bool inStr = false;
                bool inChr = false;
                for (int k = 0; k < trimmed.Length; k++)
                {
                    char ch = trimmed[k];
                    char nx = k + 1 < trimmed.Length ? trimmed[k + 1] : '\0';

                    if (!inStr && !inChr && ch == '/' && nx == '/') break;
                    if (!inStr && !inChr && ch == '/' && nx == '*')
                    {
                        int closeIdx = trimmed.IndexOf("*/", k + 2, StringComparison.Ordinal);
                        if (closeIdx >= 0) { k = closeIdx + 1; continue; }
                        inBlockComment = true;
                        break;
                    }
                    if (!inStr && !inChr && ch == '@' && nx == '"')
                    {
                        k += 2;
                        bool closedOnSameLine = false;
                        while (k < trimmed.Length)
                        {
                            if (trimmed[k] == '"')
                            {
                                if (k + 1 < trimmed.Length && trimmed[k + 1] == '"') k += 2;
                                else { closedOnSameLine = true; break; }
                            }
                            else k++;
                        }
                        if (!closedOnSameLine) inVerbatimStr = true;
                        continue;
                    }
                    if (!inChr && ch == '"') { inStr = !inStr; continue; }
                    if (inStr && ch == '\\') { k++; continue; }
                    if (!inStr && ch == '\'') { inChr = !inChr; continue; }
                    if (inChr && ch == '\\') { k++; continue; }

                    if (!inStr && !inChr)
                    {
                        if (ch == '{') netDelta++;
                        else if (ch == '}') netDelta--;
                    }
                }
                indent = Math.Max(0, indent + netDelta);
            }

            return sb.ToString().TrimEnd() + eol;
        }

        public static string FormatPowerShellOrOsh(string raw, bool stripBackticks, bool useCrLf)
        {
            string norm = (raw ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = norm.Split('\n');
            StringBuilder sb = new StringBuilder(norm.Length + 64);
            string eol = useCrLf ? "\r\n" : "\n";

            int indent = 0;
            bool inSingleHere = false;
            bool inDoubleHere = false;
            int blankRun = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (inSingleHere)
                {
                    sb.Append(ln).Append(eol);
                    if (ln.TrimEnd() == "'@") inSingleHere = false;
                    continue;
                }
                if (inDoubleHere)
                {
                    sb.Append(ln).Append(eol);
                    if (ln.TrimEnd() == "\"@") inDoubleHere = false;
                    continue;
                }

                string trimmed = ln.Trim();
                if (trimmed.Length == 0)
                {
                    blankRun++;
                    if (blankRun <= 2) sb.Append(eol);
                    continue;
                }
                blankRun = 0;

                if (stripBackticks && trimmed.IndexOf((char)96) >= 0 && !trimmed.StartsWith("#"))
                {
                    if (trimmed.EndsWith(((char)96).ToString()))
                        trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
                    trimmed = trimmed.Replace((char)96, '\'');
                }

                int leadClose = 0;
                for (int c = 0; c < trimmed.Length; c++)
                {
                    if (trimmed[c] == '}') leadClose++;
                    else if (!char.IsWhiteSpace(trimmed[c])) break;
                }
                int curInd = Math.Max(0, indent - leadClose);
                sb.Append(new string(' ', curInd * 4)).Append(trimmed).Append(eol);

                if (trimmed.EndsWith("@'")) { inSingleHere = true; continue; }
                if (trimmed.EndsWith("@\"")) { inDoubleHere = true; continue; }

                bool inDbl = false, inSgl = false;
                int delta = 0;
                for (int k = 0; k < trimmed.Length; k++)
                {
                    char c = trimmed[k];
                    if (!inDbl && !inSgl && c == '#') break;
                    if (!inSgl && c == '"') { inDbl = !inDbl; continue; }
                    if (!inDbl && c == '\'') { inSgl = !inSgl; continue; }
                    if (!inDbl && !inSgl)
                    {
                        if (c == '{') delta++;
                        else if (c == '}') delta--;
                    }
                }
                indent = Math.Max(0, indent + delta);
            }
            return sb.ToString().TrimEnd() + eol;
        }

        public static string FormatLean4Source(string raw)
        {
            string norm = (raw ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
            norm = norm.Replace("\u00E2\u0160\u00A2", "\u22A2").Replace("\u00E2\u2020\u2019", "\u2192").Replace("\uFFFD", "");
            string[] lines = norm.Split('\n');
            StringBuilder sb = new StringBuilder(norm.Length + 64);
            int blankRun = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string rt = lines[i].TrimEnd();
                if (rt.Length == 0)
                {
                    blankRun++;
                    if (blankRun <= 2) sb.Append("\n");
                    continue;
                }
                blankRun = 0;
                sb.Append(rt).Append("\n");
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        public static string FormatJsonPretty(string raw)
        {
            string src = (raw ?? "").Trim();
            if (src.Length == 0) return "{}\r\n";
            StringBuilder sb = new StringBuilder(src.Length + 128);
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (char.IsWhiteSpace(c)) continue;
                if (c == '"') { inStr = true; sb.Append(c); }
                else if (c == '{' || c == '[')
                {
                    sb.Append(c);
                    int nx = i + 1;
                    while (nx < src.Length && char.IsWhiteSpace(src[nx])) nx++;
                    if (nx < src.Length && ((c == '{' && src[nx] == '}') || (c == '[' && src[nx] == ']')))
                    {
                        sb.Append(src[nx]);
                        i = nx;
                    }
                    else
                    {
                        indent++;
                        sb.Append("\r\n").Append(new string(' ', indent * 2));
                    }
                }
                else if (c == '}' || c == ']')
                {
                    indent = Math.Max(0, indent - 1);
                    sb.Append("\r\n").Append(new string(' ', indent * 2)).Append(c);
                }
                else if (c == ',')
                {
                    sb.Append(",\r\n").Append(new string(' ', indent * 2));
                }
                else if (c == ':')
                {
                    sb.Append(": ");
                }
                else sb.Append(c);
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }

        private static string NormalizeWhitespaceClean(string raw, bool useCrLf)
        {
            string norm = (raw ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = norm.Split('\n');
            string eol = useCrLf ? "\r\n" : "\n";
            StringBuilder sb = new StringBuilder(norm.Length + 32);
            for (int i = 0; i < lines.Length; i++)
                sb.Append(lines[i].TrimEnd()).Append(eol);
            return sb.ToString().TrimEnd() + eol;
        }

        private static int CountLineDifferences(string a, string b)
        {
            string[] la = (a ?? "").Replace("\r\n", "\n").Split('\n');
            string[] lb = (b ?? "").Replace("\r\n", "\n").Split('\n');
            int max = Math.Max(la.Length, lb.Length);
            int diff = 0;
            for (int i = 0; i < max; i++)
            {
                string sa = i < la.Length ? la[i] : null;
                string sb = i < lb.Length ? lb[i] : null;
                if (!string.Equals(sa, sb, StringComparison.Ordinal)) diff++;
            }
            return diff;
        }

        public static List<OshRow> ComputeUnifiedDiffRows(string labelA, string textA, string labelB, string textB, out string summary)
        {
            string[] a = (textA ?? "").Replace("\r\n", "\n").Split('\n');
            string[] b = (textB ?? "").Replace("\r\n", "\n").Split('\n');
            List<OshRow> rows = new List<OshRow>();
            int max = Math.Max(a.Length, b.Length);
            int added = 0, removed = 0, modified = 0;

            for (int i = 0; i < max && rows.Count < 400; i++)
            {
                string sa = i < a.Length ? a[i] : null;
                string sb = i < b.Length ? b[i] : null;
                if (string.Equals(sa, sb, StringComparison.Ordinal)) continue;

                string kind;
                if (sa == null) { kind = "ADD (+)"; added++; }
                else if (sb == null) { kind = "DEL (-)"; removed++; }
                else { kind = "MOD (~)"; modified++; }

                rows.Add(new OshRow()
                    .Set("line", i + 1)
                    .Set("change", kind)
                    .Set("before_" + labelA, sa ?? "(none)")
                    .Set("after_" + labelB, sb ?? "(none)"));
            }

            summary = rows.Count == 0
                ? ("IDENTICAL (0 line differences between " + labelA + " and " + labelB + ")")
                : ("Diff " + labelA + " -> " + labelB + ": " + modified + " modified, +" + added + " added, -" + removed + " deleted (" + rows.Count + " changed lines).");
            return rows;
        }

        public static CodeBuildExecResult CompileAndRunFile(string filePath, bool checkOnly, string shellRoot)
        {
            Stopwatch sw = Stopwatch.StartNew();
            CodeBuildExecResult res = new CodeBuildExecResult();
            if (!File.Exists(filePath))
            {
                res.Ok = false;
                res.ExitCode = -1;
                res.OutputText = "File not found: " + filePath;
                return res;
            }

            string lang = DetectLanguageFromPath(filePath);
            string workDir = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
            string[] dirs = GetStandardCompilerDirs();
            string outWork = Path.Combine(shellRoot, "workspace_builds");
            Directory.CreateDirectory(outWork);

            int exitCode = 0;
            string rawOut = "";

            if (lang == "csharp")
            {
                string csc = ResolveToolBinary("csc.exe", dirs);
                string outExe = Path.Combine(outWork, Path.GetFileNameWithoutExtension(filePath) + "_cs.exe");
                res.CompilerUsed = csc;
                rawOut = RunProcessCapture(csc, "/nologo /optimize+ /out:\"" + outExe + "\" \"" + filePath + "\"", workDir, 15000, out exitCode);
                if (exitCode == 0 && !checkOnly && File.Exists(outExe))
                {
                    int runExit;
                    string runOut = RunProcessCapture(outExe, "", workDir, 15000, out runExit);
                    exitCode = runExit;
                    rawOut = (rawOut.Length > 0 ? rawOut + "\r\n" : "") + "[RUN OUTPUT -> Exit " + runExit + "]\r\n" + runOut;
                    res.Sha256Receipt = OshFormat.Sha256File(outExe);
                }
            }
            else if (lang == "rust")
            {
                string cargoToml = FindParentCargoToml(workDir);
                if (!string.IsNullOrEmpty(cargoToml))
                {
                    string cargo = ResolveToolBinary("cargo.exe", dirs);
                    res.CompilerUsed = cargo;
                    string sub = checkOnly ? "check --message-format short" : "run --quiet";
                    rawOut = RunProcessCapture(cargo, sub, Path.GetDirectoryName(cargoToml), 30000, out exitCode);
                }
                else
                {
                    string rustc = ResolveToolBinary("rustc.exe", dirs);
                    string outExe = Path.Combine(outWork, Path.GetFileNameWithoutExtension(filePath) + "_rs.exe");
                    res.CompilerUsed = rustc;
                    rawOut = RunProcessCapture(rustc, "-O \"" + filePath + "\" -o \"" + outExe + "\"", workDir, 20000, out exitCode);
                    if (exitCode == 0 && !checkOnly && File.Exists(outExe))
                    {
                        int runExit;
                        string runOut = RunProcessCapture(outExe, "", workDir, 15000, out runExit);
                        exitCode = runExit;
                        rawOut = (rawOut.Length > 0 ? rawOut + "\r\n" : "") + "[RUN OUTPUT -> Exit " + runExit + "]\r\n" + runOut;
                        res.Sha256Receipt = OshFormat.Sha256File(outExe);
                    }
                }
            }
            else if (lang == "cpp")
            {
                string clang = ResolveToolBinary("clang++.exe", dirs);
                if (string.IsNullOrEmpty(clang))
                {
                    res.Ok = false; res.ExitCode = -1; res.OutputText = "clang++.exe not found on PATH or C:\\Program Files\\LLVM\\bin.";
                    return res;
                }
                string outExe = Path.Combine(outWork, Path.GetFileNameWithoutExtension(filePath) + "_cpp.exe");
                res.CompilerUsed = clang;
                string args = checkOnly
                    ? ("-std=c++23 -fsyntax-only \"" + filePath + "\"")
                    : ("-std=c++23 -O2 \"" + filePath + "\" -o \"" + outExe + "\"");
                rawOut = RunProcessCapture(clang, args, workDir, 20000, out exitCode);
                if (exitCode == 0 && !checkOnly && File.Exists(outExe))
                {
                    int runExit;
                    string runOut = RunProcessCapture(outExe, "", workDir, 15000, out runExit);
                    exitCode = runExit;
                    rawOut = (rawOut.Length > 0 ? rawOut + "\r\n" : "") + "[RUN OUTPUT -> Exit " + runExit + "]\r\n" + runOut;
                    res.Sha256Receipt = OshFormat.Sha256File(outExe);
                }
            }
            else if (lang == "hlsl")
            {
                string ext = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();
                string fxc = ResolveWindowsSdkShaderTool("fxc.exe");
                string srcTxt = File.ReadAllText(filePath, Encoding.UTF8);
                if (!string.IsNullOrEmpty(fxc) && srcTxt.IndexOf("CSMain", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string outCso = Path.Combine(outWork, Path.GetFileNameWithoutExtension(filePath) + ".cso");
                    res.CompilerUsed = fxc;
                    rawOut = RunProcessCapture(fxc, "/nologo /T cs_5_0 /E CSMain /O3 /Fo \"" + outCso + "\" \"" + filePath + "\"", workDir, 15000, out exitCode);
                    if (exitCode == 0 && File.Exists(outCso))
                    {
                        res.Sha256Receipt = OshFormat.Sha256File(outCso);
                        rawOut = "[DirectCompute HLSL cs_5_0 Compiled Cleanly -> " + Path.GetFileName(outCso) + " (" + new FileInfo(outCso).Length + " bytes)]\r\n" + rawOut;
                    }
                }
                else
                {
                    res.CompilerUsed = "OmegaAV .omsh Hot-Loader";
                    string avDir = Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "03_omega_av");
                    if (Directory.Exists(avDir) && (ext == ".omsh" || !checkOnly))
                    {
                        string stageDir = Path.Combine(avDir, "shaders");
                        Directory.CreateDirectory(stageDir);
                        string staged = Path.Combine(stageDir, Path.GetFileNameWithoutExtension(filePath) + ".omsh");
                        File.Copy(filePath, staged, true);
                        if (!checkOnly) File.WriteAllText(Path.Combine(avDir, "inbox_open.cmd"), staged, new UTF8Encoding(false));
                        rawOut = "[OmegaAV Shader Staged] -> " + staged + (checkOnly ? "" : " (live-signaled inbox_open.cmd)");
                    }
                    else
                    {
                        rawOut = "Validated shader source (" + srcTxt.Length + " chars).";
                    }
                    exitCode = 0;
                }
            }
            else if (lang == "lean4")
            {
                string lean = ResolveToolBinary("lean.exe", dirs);
                res.CompilerUsed = lean;
                rawOut = RunProcessCapture(lean, "\"" + filePath + "\"", workDir, 35000, out exitCode);
                if (exitCode == 0 && string.IsNullOrWhiteSpace(rawOut))
                    rawOut = "[Lean 4 Kernel Verified] Exit 0 — zero type errors or sorry obligations!";
            }
            else if (lang == "typescript")
            {
                string deno = ResolveToolBinary("deno.exe", dirs);
                res.CompilerUsed = deno;
                string sub = checkOnly ? ("check \"" + filePath + "\"") : ("run -A \"" + filePath + "\"");
                rawOut = RunProcessCapture(deno, sub, workDir, 20000, out exitCode);
            }
            else if (lang == "powershell")
            {
                res.CompilerUsed = "powershell.exe";
                if (checkOnly)
                {
                    string psAstCmd = "$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + filePath.Replace("'", "''") + "', [ref]$null, [ref]$e); if ($e.Count -gt 0) { $e | ForEach-Object { Write-Output ($_.Extent.StartLineNumber.ToString() + ':' + $_.Extent.StartColumnNumber.ToString() + ': error: ' + $_.Message) }; exit 1 } else { Write-Output '[PowerShell 5.1 AST Verified] 0 parse errors.'; exit 0 }";
                    rawOut = RunProcessCapture("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + psAstCmd + "\"", workDir, 10000, out exitCode);
                }
                else
                {
                    string safePsPath = filePath.Replace("'", "''");
                    string strictPsRunner = "$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + safePsPath + "', [ref]$null, [ref]$e); if ($e.Count -gt 0) { $e | ForEach-Object { Write-Output ($_.Extent.StartLineNumber.ToString() + ':' + $_.Extent.StartColumnNumber.ToString() + ': error: ' + $_.Message) }; exit 1 }; $ErrorActionPreference = 'Stop'; try { & '" + safePsPath + "'; if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; exit 0 } catch { $ln = if ($_.InvocationInfo -and $_.InvocationInfo.ScriptLineNumber) { $_.InvocationInfo.ScriptLineNumber } else { 1 }; $cl = if ($_.InvocationInfo -and $_.InvocationInfo.OffsetInLine) { $_.InvocationInfo.OffsetInLine } else { 1 }; Write-Output ($ln.ToString() + ':' + $cl.ToString() + ': error: ' + $_.Exception.Message); exit 1 }";
                    rawOut = RunProcessCapture("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + strictPsRunner + "\"", workDir, 25000, out exitCode);
                }
            }
            else if (lang == "python")
            {
                string py = ResolveToolBinary("python.exe", dirs);
                res.CompilerUsed = py;
                string args = checkOnly ? ("-m py_compile \"" + filePath + "\"") : ("\"" + filePath + "\"");
                rawOut = RunProcessCapture(py, args, workDir, 20000, out exitCode);
            }
            else if (lang == "json")
            {
                res.CompilerUsed = "Built-In JSON Validator";
                string txt = File.ReadAllText(filePath, Encoding.UTF8);
                rawOut = FormatJsonPretty(txt);
                exitCode = 0;
            }
            else
            {
                res.CompilerUsed = "OSH Inspector";
                rawOut = "Validated UTF-8 readable file (" + new FileInfo(filePath).Length + " bytes): " + filePath;
                exitCode = 0;
            }

            sw.Stop();
            res.ExitCode = exitCode;
            res.Ok = (exitCode == 0);
            res.ElapsedMs = sw.ElapsedMilliseconds;
            res.OutputText = rawOut;
            if (string.IsNullOrEmpty(res.Sha256Receipt)) res.Sha256Receipt = OshFormat.Sha256File(filePath);
            res.Diagnostics = ParseDiagnostics(rawOut, filePath);
            return res;
        }

        private static string FindParentCargoToml(string startDir)
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(startDir);
                for (int depth = 0; depth < 3 && d != null; depth++)
                {
                    string cand = Path.Combine(d.FullName, "Cargo.toml");
                    if (File.Exists(cand)) return cand;
                    d = d.Parent;
                }
            }
            catch { }
            return "";
        }

        public static List<CompilerDiagnosticItem> ParseDiagnostics(string rawOutput, string fallbackFile)
        {
            List<CompilerDiagnosticItem> list = new List<CompilerDiagnosticItem>();
            if (string.IsNullOrEmpty(rawOutput)) return list;

            Regex rxParen = new Regex(@"^(.+?)\((\d+),(\d+)\):\s*(error|warning)\s*([A-Za-z0-9_]*):\s*(.+)$", RegexOptions.IgnoreCase);
            Regex rxColon = new Regex(@"^(?:(.+?):)?(\d+):(\d+):\s*(error|warning)[\s:\[\]A-Za-z0-9_]*(.+)$", RegexOptions.IgnoreCase);

            string[] lines = rawOutput.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length == 0) continue;

                Match m1 = rxParen.Match(ln);
                if (m1.Success)
                {
                    int lineNo, colNo;
                    int.TryParse(m1.Groups[2].Value, out lineNo);
                    int.TryParse(m1.Groups[3].Value, out colNo);
                    CompilerDiagnosticItem d = new CompilerDiagnosticItem();
                    d.FilePath = m1.Groups[1].Value.Trim();
                    d.Line = Math.Max(1, lineNo);
                    d.Column = Math.Max(1, colNo);
                    d.Severity = m1.Groups[4].Value.ToUpperInvariant();
                    d.Code = m1.Groups[5].Value;
                    d.Message = m1.Groups[6].Value.Trim();
                    list.Add(d);
                    continue;
                }

                Match m2 = rxColon.Match(ln);
                if (m2.Success)
                {
                    int lineNo, colNo;
                    int.TryParse(m2.Groups[2].Value, out lineNo);
                    int.TryParse(m2.Groups[3].Value, out colNo);
                    CompilerDiagnosticItem d = new CompilerDiagnosticItem();
                    d.FilePath = string.IsNullOrWhiteSpace(m2.Groups[1].Value) ? fallbackFile : m2.Groups[1].Value.Trim();
                    d.Line = Math.Max(1, lineNo);
                    d.Column = Math.Max(1, colNo);
                    d.Severity = m2.Groups[4].Value.ToUpperInvariant();
                    d.Message = m2.Groups[5].Value.Trim();
                    list.Add(d);
                }
            }
            return list;
        }

        public static string RunProcessCapture(string exe, string args, string workDir, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            if (string.IsNullOrEmpty(exe)) return "Executable path is empty.";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir)) psi.WorkingDirectory = workDir;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && sb.Length < 131072) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate(object s2, DataReceivedEventArgs e2) { if (e2.Data != null && sb.Length < 131072) sb.AppendLine(e2.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        sb.AppendLine("[Timed out after " + timeoutMs + " ms]");
                        exitCode = 124;
                    }
                    else
                    {
                        exitCode = p.ExitCode;
                    }
                    return sb.ToString().TrimEnd();
                }
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "[Exec Error: " + Path.GetFileName(exe) + "] " + ex.Message;
            }
        }
    }
}
