using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaShellStudio
{
    public sealed class OshChainSegment
    {
        public string GateOp = "";
        public string PipelineText = "";
    }

    public sealed class OshShellRuntime
    {
        public string CurrentDir;
        public readonly string ShellRoot;
        public readonly string HistoryFilePath;
        public readonly LocalCloudVault Cloud;
        public readonly ShellSettingsProfile Settings;
        public readonly List<string> History = new List<string>();
        public readonly Dictionary<string, string> Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public event Action<string> OnWorkingDirectoryChanged;

        public OshShellRuntime(string shellRoot, ShellSettingsProfile settings)
        {
            ShellRoot = shellRoot;
            Settings = settings;
            Cloud = new LocalCloudVault(shellRoot);
            CurrentDir = Directory.Exists((Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'))) ? (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')) : shellRoot;
            HistoryFilePath = Path.Combine(shellRoot, "config", "history.log");
            Variables["lab"] = (Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            Variables["arch"] = "x86_64-pc-windows-msvc";
            Variables["debian_user"] = settings.DebianLinuxUser;
            LoadPersistentHistory();
        }

        private void LoadPersistentHistory()
        {
            try
            {
                if (!File.Exists(HistoryFilePath)) return;
                string[] lines = File.ReadAllLines(HistoryFilePath, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - 300);
                for (int i = start; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (ln.Length > 0) History.Add(ln);
                }
            }
            catch { }
        }

        private void AppendPersistentHistory(string cmd)
        {
            try
            {
                if (History.Count > 0 && string.Equals(History[History.Count - 1], cmd, StringComparison.Ordinal))
                    return;
                History.Add(cmd);
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath));
                File.AppendAllText(HistoryFilePath, cmd + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        public OshExecOutput ExecutePipeline(string commandLine)
        {
            return ExecutePipelineInDir(commandLine, ref CurrentDir, true);
        }

        public OshExecOutput ExecutePipelineInDir(string commandLine, ref string sessionDir, bool notifyGlobalCwd)
        {
            Stopwatch sw = Stopwatch.StartNew();
            OshExecOutput finalOut = new OshExecOutput();
            finalOut.CommandText = commandLine ?? "";

            string trimmed = (commandLine ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#"))
            {
                sw.Stop();
                return finalOut;
            }

            AppendPersistentHistory(trimmed);

            string prevDir = CurrentDir;
            CurrentDir = sessionDir;

            List<OshChainSegment> chain = SplitCompoundChain(trimmed);
            StringBuilder combinedText = new StringBuilder();
            List<OshRow> lastRows = null;
            bool lastOk = true;

            try
            {
                for (int c = 0; c < chain.Count; c++)
                {
                    OshChainSegment seg = chain[c];
                    if (seg.GateOp == "&&" && !lastOk) continue;
                    if (seg.GateOp == "||" && lastOk) continue;

                    string segText = seg.PipelineText.Trim();
                    if (segText.Length == 0) continue;

                    string segOutText;
                    List<OshRow> segRows;
                    bool segOk = ExecuteSinglePipelineWithRedirects(segText, finalOut, out segRows, out segOutText);
                    lastOk = segOk;
                    if (segRows != null && segRows.Count > 0) lastRows = segRows;
                    if (!string.IsNullOrWhiteSpace(segOutText))
                    {
                        if (combinedText.Length > 0) combinedText.AppendLine();
                        combinedText.Append(segOutText.TrimEnd());
                    }
                }

                finalOut.Ok = lastOk;
                sessionDir = CurrentDir;
                if (notifyGlobalCwd && sessionDir != prevDir && OnWorkingDirectoryChanged != null)
                    OnWorkingDirectoryChanged(sessionDir);
            }
            catch (Exception ex)
            {
                finalOut.Ok = false;
                if (combinedText.Length > 0) combinedText.AppendLine();
                combinedText.Append("[OSH v1.0 GM ERROR] " + ex.Message);
            }

            sw.Stop();
            finalOut.Rows = lastRows ?? new List<OshRow>();
            finalOut.TextOutput = combinedText.ToString();
            finalOut.ElapsedMs = sw.ElapsedMilliseconds;
            finalOut.Sha256Receipt = OshFormat.Sha256Hex(finalOut.RenderConsoleText() + "|" + finalOut.ElapsedMs);
            return finalOut;
        }

        private bool ExecuteSinglePipelineWithRedirects(string rawPipe, OshExecOutput outCtx, out List<OshRow> curRows, out string curText)
        {
            curRows = null;
            curText = "";

            if (rawPipe.StartsWith("let ", StringComparison.OrdinalIgnoreCase) || rawPipe.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            {
                int eq = rawPipe.IndexOf('=');
                if (eq > 4)
                {
                    string vName = rawPipe.Substring(4, eq - 4).Trim().TrimStart('$');
                    string rhs = rawPipe.Substring(eq + 1).Trim();
                    string val = EvaluateAssignmentRhs(rhs);
                    Variables[vName] = val;
                    curRows = new List<OshRow> { new OshRow().Set("variable", "$" + vName).Set("value", val) };
                    return true;
                }
            }

            string redirectFile;
            bool redirectAppend;
            string pipeCore = ExtractTrailingRedirect(rawPipe, out redirectFile, out redirectAppend);

            string expanded = ExpandVariables(ExpandAliasIfMatch(pipeCore));
            List<string> stages = SplitUnquotedPipes(expanded);

            bool ok = true;
            try
            {
                for (int s = 0; s < stages.Count; s++)
                {
                    string stageCmd = stages[s].Trim();
                    if (stageCmd.Length == 0) continue;
                    ExecuteSingleStage(stageCmd, ref curRows, ref curText, outCtx, out ok);
                    if (!ok) break;
                }

                if (ok && !string.IsNullOrEmpty(redirectFile))
                {
                    string dstPath = MultiDrivePathEngine.ResolvePath(CurrentDir, ExpandVariables(redirectFile), ShellRoot);
                    string pDir = Path.GetDirectoryName(dstPath);
                    if (!string.IsNullOrEmpty(pDir)) Directory.CreateDirectory(pDir);

                    string ext = (Path.GetExtension(dstPath) ?? "").ToLowerInvariant();
                    string payload;
                    if (curRows != null && curRows.Count > 0 && ext == ".json")
                        payload = CodeFormatterAndCompiler.FormatJsonPretty(OshTableFormatter.FormatJson(curRows));
                    else if (curRows != null && curRows.Count > 0 && ext == ".csv")
                        payload = OshTableFormatter.FormatCsv(curRows, ',');
                    else if (curRows != null && curRows.Count > 0 && ext == ".tsv")
                        payload = OshTableFormatter.FormatCsv(curRows, '\t');
                    else if (curRows != null && curRows.Count > 0)
                        payload = OshTableFormatter.FormatRows(curRows, 5000) + "\r\n";
                    else
                        payload = (curText ?? "") + "\r\n";

                    if (redirectAppend) File.AppendAllText(dstPath, payload, new UTF8Encoding(false));
                    else File.WriteAllText(dstPath, payload, new UTF8Encoding(false));

                    curText = (string.IsNullOrEmpty(curText) ? "" : (curText + "\r\n")) +
                        "[Redirected " + payload.Length + " chars -> " + dstPath + "]";
                }
            }
            catch (Exception ex)
            {
                ok = false;
                curText = "[Pipeline Error] " + ex.Message;
            }
            return ok;
        }

        private string EvaluateAssignmentRhs(string rhs)
        {
            string r = ExpandVariables(rhs.Trim());
            if (r.StartsWith("(") && r.EndsWith(")") && r.Length > 2)
            {
                string inner = r.Substring(1, r.Length - 2).Trim();
                double mathVal;
                if (OshMathEvaluator.TryEval(inner, out mathVal))
                    return mathVal.ToString("0.######", CultureInfo.InvariantCulture);

                string tmpDir = CurrentDir;
                OshExecOutput sub = ExecutePipelineInDir(inner, ref tmpDir, false);
                if (sub.Rows != null && sub.Rows.Count == 1 && sub.Rows[0].Columns.Count == 1)
                    return sub.Rows[0].GetString(sub.Rows[0].Columns[0]);
                return sub.RenderConsoleText().Trim();
            }
            double mv;
            if (OshMathEvaluator.TryEval(r, out mv))
                return mv.ToString("0.######", CultureInfo.InvariantCulture);
            return r.Trim('"', '\'');
        }

        private string ExpandVariables(string input)
        {
            if (string.IsNullOrEmpty(input) || input.IndexOf('$') < 0) return input;
            return Regex.Replace(input, @"\$\{?([A-Za-z_][A-Za-z0-9_:\.]*)\}?", delegate(Match m)
            {
                string key = m.Groups[1].Value;
                if (key.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                    return Environment.GetEnvironmentVariable(key.Substring(4)) ?? "";
                if (key.StartsWith("kv:", StringComparison.OrdinalIgnoreCase))
                    return Cloud.KvGet(key.Substring(3));
                string v;
                if (Variables.TryGetValue(key, out v)) return v;
                string envFallback = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrEmpty(envFallback)) return envFallback;
                return m.Value;
            });
        }

        private static List<OshChainSegment> SplitCompoundChain(string line)
        {
            List<OshChainSegment> list = new List<OshChainSegment>();
            StringBuilder cur = new StringBuilder();
            string pendingGate = "";
            bool inDbl = false, inSgl = false;
            int parenDepth = 0;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                char nx = i + 1 < line.Length ? line[i + 1] : '\0';
                if (c == '"' && !inSgl) { inDbl = !inDbl; cur.Append(c); continue; }
                if (c == '\'' && !inDbl) { inSgl = !inSgl; cur.Append(c); continue; }
                if (!inDbl && !inSgl)
                {
                    if (c == '(') parenDepth++;
                    else if (c == ')' && parenDepth > 0) parenDepth--;
                    else if (parenDepth == 0)
                    {
                        if (c == ';')
                        {
                            OshChainSegment s = new OshChainSegment(); s.GateOp = pendingGate; s.PipelineText = cur.ToString();
                            list.Add(s); cur.Clear(); pendingGate = ";"; continue;
                        }
                        if (c == '&' && nx == '&')
                        {
                            OshChainSegment s = new OshChainSegment(); s.GateOp = pendingGate; s.PipelineText = cur.ToString();
                            list.Add(s); cur.Clear(); pendingGate = "&&"; i++; continue;
                        }
                        if (c == '|' && nx == '|')
                        {
                            OshChainSegment s = new OshChainSegment(); s.GateOp = pendingGate; s.PipelineText = cur.ToString();
                            list.Add(s); cur.Clear(); pendingGate = "||"; i++; continue;
                        }
                    }
                }
                cur.Append(c);
            }
            if (cur.Length > 0)
            {
                OshChainSegment last = new OshChainSegment(); last.GateOp = pendingGate; last.PipelineText = cur.ToString();
                list.Add(last);
            }
            return list;
        }

        private static string ExtractTrailingRedirect(string line, out string redirectTarget, out bool isAppend)
        {
            redirectTarget = "";
            isAppend = false;
            bool inDbl = false, inSgl = false;
            int lastRedirPos = -1;
            bool appendFlag = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' && !inSgl) inDbl = !inDbl;
                else if (c == '\'' && !inDbl) inSgl = !inSgl;
                else if (!inDbl && !inSgl && c == '>')
                {
                    char prev = i > 0 ? line[i - 1] : '\0';
                    char next = i + 1 < line.Length ? line[i + 1] : '\0';
                    if (prev == '|' || prev == '-' || next == '=') continue;
                    string before = line.Substring(0, i);
                    int lastPipe = before.LastIndexOf('|');
                    string activeStage = (lastPipe >= 0 ? before.Substring(lastPipe + 1) : before).TrimStart();
                    if (activeStage.StartsWith("where ", StringComparison.OrdinalIgnoreCase) || activeStage.StartsWith("calc ", StringComparison.OrdinalIgnoreCase) || activeStage.StartsWith("=", StringComparison.Ordinal))
                        continue;

                    lastRedirPos = i;
                    if (next == '>') { appendFlag = true; i++; }
                    else appendFlag = false;
                }
            }

            if (lastRedirPos < 0) return line;
            isAppend = appendFlag;
            int skip = appendFlag ? 2 : 1;
            redirectTarget = line.Substring(lastRedirPos + skip).Trim().Trim('"', '\'');
            return line.Substring(0, lastRedirPos).TrimEnd();
        }

        private string ExpandAliasIfMatch(string line)
        {
            int sp = line.IndexOf(' ');
            int pipe = line.IndexOf('|');
            int cut = -1;
            if (sp > 0 && pipe > 0) cut = Math.Min(sp, pipe);
            else if (sp > 0) cut = sp;
            else if (pipe > 0) cut = pipe;

            string first = (cut > 0 ? line.Substring(0, cut) : line).Trim();
            string rest = cut > 0 ? line.Substring(cut) : "";
            string repl;
            if (Settings.Aliases.TryGetValue(first, out repl))
                return repl + rest;
            return line;
        }

        private static List<string> SplitUnquotedPipes(string s)
        {
            List<string> list = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool inDbl = false, inSgl = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && !inSgl) inDbl = !inDbl;
                else if (c == '\'' && !inDbl) inSgl = !inSgl;
                else if (c == '|' && !inDbl && !inSgl)
                {
                    if (i + 1 < s.Length && s[i + 1] == '>') i++;
                    list.Add(cur.ToString());
                    cur.Clear();
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        private static List<string> TokenizeArgs(string stage)
        {
            List<string> args = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool inDbl = false, inSgl = false;
            for (int i = 0; i < stage.Length; i++)
            {
                char c = stage[i];
                if (c == '"' && !inSgl) { inDbl = !inDbl; continue; }
                if (c == '\'' && !inDbl) { inSgl = !inSgl; continue; }
                if (char.IsWhiteSpace(c) && !inDbl && !inSgl)
                {
                    if (cur.Length > 0) { args.Add(cur.ToString()); cur.Clear(); }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) args.Add(cur.ToString());
            return args;
        }

        private void ExecuteSingleStage(string stage, ref List<OshRow> rows, ref string text, OshExecOutput outCtx, out bool stageOk)
        {
            stageOk = true;
            List<string> toks = TokenizeArgs(stage);
            if (toks.Count == 0) return;

            string verb = toks[0].ToLowerInvariant();
            string rawRest = stage.Length > toks[0].Length ? stage.Substring(toks[0].Length).Trim() : "";

            if (verb == "@deb" || verb == "@avf" || verb == "@debian")
            {
                rows = null;
                text = AndroidAvfDebianBridge.RunCommandInPixelDebian(ShellRoot, Settings.DebianLinuxUser, rawRest, "");
                return;
            }
            if (verb == "@p10" || verb == "@p10-sensor" || verb == "@p10-mid3" || verb == "@p10-mid2" || verb == "@p10-compute" || verb == "@p10-prime" || verb == "@p8" || verb == "@pixels")
            {
                rows = null;
                string defCmd = "uname -a && cat /proc/self/status | grep Cpus_allowed_list";
                text = MeshAndLabBridge.RunOnPixelTarget(verb, string.IsNullOrWhiteSpace(rawRest) ? defCmd : rawRest);
                return;
            }
            if (verb == "@loq")
            {
                if (!string.IsNullOrWhiteSpace(rawRest)) ExecuteSingleStage(rawRest, ref rows, ref text, outCtx, out stageOk);
                return;
            }
            if (verb == "@gemma" || verb == "gemma" || verb == "agent")
            {
                if (string.IsNullOrWhiteSpace(rawRest))
                {
                    rows = null;
                    text = "Usage: gemma <natural-language mission or codegen goal>  (e.g., gemma check listen ports and write a Rust benchmark)";
                    stageOk = false;
                    return;
                }
                string pipedCtx = (rows != null && rows.Count > 0) ? ("\r\n\r\nPiped Input:\r\n" + OshTableFormatter.FormatRows(rows, 40)) : (!string.IsNullOrWhiteSpace(text) ? ("\r\n\r\nPiped Text:\r\n" + text) : "");
                GemmaMissionResult gm = OmegaShellGemmaCommander.ExecuteAutonomousMission(this, rawRest + pipedCtx, null);
                stageOk = gm.Ok;
                rows = gm.TraceRows;
                text = "[Gemma Commander :: " + gm.ModelUsed + " | " + gm.TokensPerSec + " tok/s | " + gm.TotalMs + " ms]\r\n" + gm.FinalNarrative;
                if (!string.IsNullOrEmpty(gm.GeneratedFilePath))
                    outCtx.EditTargetFile = gm.GeneratedFilePath;
                return;
            }
            if (verb == "codegen")
            {
                if (toks.Count < 3)
                {
                    rows = null;
                    text = "Usage: codegen <rust|csharp|cpp|lean4|typescript|powershell|hlsl|osh> \"<instruction>\" [optional_file]";
                    stageOk = false;
                    return;
                }
                string cgLang = toks[1];
                string cgPrompt = toks[2];
                string cgOut = "";
                if (toks.Count >= 4)
                {
                    string lastTok = toks[toks.Count - 1];
                    if (lastTok.Contains(".") || lastTok.Contains("\\") || lastTok.Contains("/"))
                    {
                        cgOut = MultiDrivePathEngine.ResolvePath(CurrentDir, lastTok, ShellRoot);
                        cgPrompt = string.Join(" ", toks.GetRange(2, toks.Count - 3).ToArray());
                    }
                    else
                    {
                        cgPrompt = string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray());
                    }
                }
                GemmaCodegenReceipt rec = OmegaShellGemmaCommander.GenerateFormatAndVerifyCode(
                    Settings, ShellRoot, cgLang, cgPrompt, "", cgOut, true, Settings.AutoHealCodegen, null);
                stageOk = rec.Ok;
                outCtx.EditTargetFile = rec.FilePath;
                rows = new List<OshRow>
                {
                    new OshRow()
                        .Set("lang", rec.Language)
                        .Set("formatter", rec.FormatterEngine)
                        .Set("exit_code", rec.ExitCode)
                        .Set("repairs", rec.RepairIterations)
                        .Set("tok_s", rec.TokensPerSec)
                        .Set("ms", rec.ElapsedMs)
                        .Set("file", rec.FilePath)
                };
                text = rec.BuildOutput;
                return;
            }
            if (verb == "models" || verb == "gemma-models")
            {
                List<GemmaLiveModelItem> mlist = OmegaShellGemmaCommander.DiscoverLiveModels(Settings.DefaultAiEndpoint, Settings.DefaultAiModel);
                rows = new List<OshRow>();
                for (int i = 0; i < mlist.Count; i++)
                {
                    GemmaLiveModelItem mi = mlist[i];
                    bool active = string.Equals(mi.ModelTag, Settings.DefaultAiModel, StringComparison.OrdinalIgnoreCase);
                    rows.Add(new OshRow()
                        .Set("active", active ? "* ACTIVE" : "")
                        .Set("provider", mi.Provider)
                        .Set("model", mi.ModelTag)
                        .Set("status", mi.Online ? "ONLINE" : "OFFLINE")
                        .Set("endpoint", mi.EndpointUrl));
                }
                text = "";
                return;
            }
            if (verb == "use-model" && toks.Count >= 2)
            {
                string want = toks[1].Trim();
                List<GemmaLiveModelItem> mlist = OmegaShellGemmaCommander.DiscoverLiveModels(Settings.DefaultAiEndpoint, Settings.DefaultAiModel);
                for (int i = 0; i < mlist.Count; i++)
                {
                    if (mlist[i].ModelTag.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Settings.DefaultAiModel = mlist[i].ModelTag;
                        Settings.DefaultAiEndpoint = mlist[i].EndpointUrl;
                        Settings.SaveToDisk(Path.Combine(ShellRoot, "config"));
                        rows = null;
                        text = "Bound active Gemma Commander model -> " + Settings.DefaultAiModel + " (" + Settings.DefaultAiEndpoint + ")";
                        return;
                    }
                }
                Settings.DefaultAiModel = want;
                Settings.SaveToDisk(Path.Combine(ShellRoot, "config"));
                rows = null;
                text = "Set active model tag -> " + want + " (" + Settings.DefaultAiEndpoint + ")";
                return;
            }
            if (verb == "@ai")
            {
                string pipedCtx = (rows != null && rows.Count > 0) ? OshTableFormatter.FormatRows(rows, 50) : text;
                rows = null;
                text = MeshAndLabBridge.QueryLocalGemma(Settings.DefaultAiEndpoint, Settings.DefaultAiModel, pipedCtx, string.IsNullOrWhiteSpace(rawRest) ? "Summarize this output concisely." : rawRest);
                return;
            }

            if (verb == "code" || verb == "edit")
            {
                if (toks.Count < 2) { rows = null; text = "Usage: code <file_path>"; stageOk = false; return; }
                string fp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                outCtx.EditTargetFile = fp;
                rows = null;
                text = "Opened in Tab 2 Polyglot Code Studio -> " + fp;
                return;
            }
            if (verb == "fmt" || verb == "format")
            {
                bool checkOnly = toks.Contains("-check") || toks.Contains("--check");
                string targetArg = ".";
                for (int i = 1; i < toks.Count; i++)
                    if (!toks[i].StartsWith("-")) { targetArg = toks[i]; break; }

                string resolved = MultiDrivePathEngine.ResolvePath(CurrentDir, targetArg, ShellRoot);
                List<FormatResultItem> fmtItems = CodeFormatterAndCompiler.FormatPath(resolved, checkOnly, Settings.EnforceZeroBacktickPs1);
                rows = new List<OshRow>();
                for (int i = 0; i < fmtItems.Count; i++)
                {
                    FormatResultItem fi = fmtItems[i];
                    if (checkOnly && fi.Status == "NEEDS_FORMAT") stageOk = false;
                    rows.Add(new OshRow()
                        .Set("file", Path.GetFileName(fi.FilePath))
                        .Set("lang", fi.Language)
                        .Set("engine", fi.EngineUsed)
                        .Set("status", fi.Status)
                        .Set("changed_lines", fi.ChangedLines)
                        .Set("bytes", fi.ByteCount)
                        .Set("path", fi.FilePath));
                }
                text = fmtItems.Count == 0 ? ("No supported code files found at: " + resolved) : "";
                return;
            }
            if (verb == "toolchains" || verb == "formatters")
            {
                rows = CodeFormatterAndCompiler.ListFormatterAndCompilerMatrixRows();
                text = "";
                return;
            }
            if (verb == "build" || verb == "check" || verb == "run-code")
            {
                if (toks.Count < 2) { rows = null; text = "Usage: " + verb + " <source_file>"; stageOk = false; return; }
                string fp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                CodeBuildExecResult br = CodeFormatterAndCompiler.CompileAndRunFile(fp, verb == "check", ShellRoot);
                stageOk = br.Ok;
                rows = new List<OshRow>();
                for (int i = 0; i < br.Diagnostics.Count; i++)
                {
                    CompilerDiagnosticItem d = br.Diagnostics[i];
                    rows.Add(new OshRow()
                        .Set("severity", d.Severity)
                        .Set("line", d.Line)
                        .Set("col", d.Column)
                        .Set("code", d.Code)
                        .Set("message", d.Message)
                        .Set("file", d.FilePath));
                }
                text = "[" + Path.GetFileName(br.CompilerUsed) + " | Exit " + br.ExitCode + " | " + br.ElapsedMs + " ms]\r\n" + br.OutputText;
                return;
            }
            if (verb == "diff")
            {
                if (toks.Count < 3) { rows = null; text = "Usage: diff <fileA> <fileB>"; stageOk = false; return; }
                string pA = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                string pB = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[2], ShellRoot);
                string tA = File.ReadAllText(pA, Encoding.UTF8);
                string tB = File.ReadAllText(pB, Encoding.UTF8);
                string sum;
                rows = CodeFormatterAndCompiler.ComputeUnifiedDiffRows(Path.GetFileName(pA), tA, Path.GetFileName(pB), tB, out sum);
                text = sum;
                return;
            }
            if (verb == "calc" || verb == "=")
            {
                string expr = verb == "=" ? stage.Substring(1).Trim() : rawRest;
                double val;
                if (!OshMathEvaluator.TryEval(expr, out val))
                {
                    stageOk = false; rows = null; text = "Could not evaluate math expression: " + expr; return;
                }
                rows = new List<OshRow> { new OshRow().Set("expression", expr).Set("result", val).Set("hex", "0x" + ((long)val).ToString("X")) };
                text = "";
                return;
            }
            if (verb == "vars")
            {
                rows = new List<OshRow>();
                foreach (KeyValuePair<string, string> kv in Variables)
                    rows.Add(new OshRow().Set("variable", "$" + kv.Key).Set("value", kv.Value));
                text = "";
                return;
            }
            if (verb == "bench" && toks.Count >= 3)
            {
                int runs = 5;
                int.TryParse(toks[1], out runs);
                runs = Math.Max(1, Math.Min(30, runs));
                string benchCmd = string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray());
                List<double> samples = new List<double>();
                for (int r = 0; r < runs; r++)
                {
                    Stopwatch bsw = Stopwatch.StartNew();
                    string tmpD = CurrentDir;
                    ExecutePipelineInDir(benchCmd, ref tmpD, false);
                    bsw.Stop();
                    samples.Add(Math.Round(bsw.Elapsed.TotalMilliseconds, 3));
                }
                samples.Sort();
                double sumMs = 0.0;
                for (int i = 0; i < samples.Count; i++) sumMs += samples[i];
                rows = new List<OshRow>
                {
                    new OshRow()
                        .Set("command", benchCmd)
                        .Set("runs", runs)
                        .Set("min_ms", samples[0])
                        .Set("median_ms", samples[samples.Count / 2])
                        .Set("mean_ms", Math.Round(sumMs / runs, 3))
                        .Set("max_ms", samples[samples.Count - 1])
                };
                text = "";
                return;
            }
            if (verb == "git-stat")
            {
                ExecuteGitStat(out rows, out text);
                return;
            }
            if (verb == "avf")
            {
                ExecuteAvfVerb(toks, ref rows, ref text);
                return;
            }
            if (verb == "tpu" || verb == "npu")
            {
                rows = null;
                bool runBench = toks.Count >= 2 && (toks[1].Equals("bench", StringComparison.OrdinalIgnoreCase) || toks[1].Equals("dispatch", StringComparison.OrdinalIgnoreCase));
                string ep = runBench ? "http://127.0.0.1:19788/api/dispatch/tpu" : "http://127.0.0.1:19788/api/tpu";
                try
                {
                    System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(ep);
                    req.Method = "GET";
                    req.Timeout = 30000;
                    using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
                    using (System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                    {
                        text = sr.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    text = "[TPU Probe :19788] " + ex.Message;
                }
                return;
            }

            if (verb == "where") { ApplyWhereFilter(toks, ref rows); return; }
            if (verb == "sort") { ApplySort(toks, ref rows); return; }
            if (verb == "group" || verb == "agg")
            {
                if (toks.Count >= 2 && rows != null) ApplyGroupBy(toks[1], ref rows);
                return;
            }
            if (verb == "top" || verb == "head")
            {
                int n = 10;
                if (toks.Count >= 2) int.TryParse(toks[1], out n);
                if (rows != null && rows.Count > n) rows = rows.GetRange(0, Math.Max(0, n));
                else if (toks.Count >= 2 && rows == null && string.IsNullOrEmpty(text))
                {
                    string fp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                    if (File.Exists(fp))
                    {
                        string[] ls = File.ReadAllLines(fp, Encoding.UTF8);
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < ls.Length && i < 25; i++) sb.AppendLine(ls[i]);
                        text = sb.ToString().TrimEnd();
                    }
                }
                return;
            }
            if (verb == "select")
            {
                if (rows != null && toks.Count >= 2)
                {
                    string[] keep = string.Join(",", toks.GetRange(1, toks.Count - 1).ToArray()).Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    List<OshRow> proj = new List<OshRow>();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        OshRow nr = new OshRow();
                        for (int c = 0; c < keep.Length; c++)
                        {
                            object v;
                            if (rows[r].Values.TryGetValue(keep[c], out v)) nr.Set(keep[c], v);
                        }
                        proj.Add(nr);
                    }
                    rows = proj;
                }
                return;
            }
            if (verb == "count")
            {
                int cnt = rows != null ? rows.Count : (string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length);
                rows = new List<OshRow> { new OshRow().Set("count", cnt) };
                text = "";
                return;
            }
            if (verb == "sum" && toks.Count >= 2 && rows != null)
            {
                double acc = 0.0;
                for (int r = 0; r < rows.Count; r++) { bool isN; acc += rows[r].GetDouble(toks[1], out isN); }
                rows = new List<OshRow> { new OshRow().Set("column", toks[1]).Set("sum", Math.Round(acc, 3)) };
                text = "";
                return;
            }
            if (verb == "json")
            {
                text = rows != null ? CodeFormatterAndCompiler.FormatJsonPretty(OshTableFormatter.FormatJson(rows)) : ("{\"text\":\"" + OshFormat.JsonEsc(text) + "\"}");
                rows = null;
                return;
            }
            if (verb == "csv")
            {
                text = rows != null ? OshTableFormatter.FormatCsv(rows, ',') : (text ?? "");
                rows = null;
                return;
            }
            if (verb == "grep")
            {
                string pat = toks.Count >= 2 ? toks[1] : "";
                if (rows != null)
                {
                    List<OshRow> matched = new List<OshRow>();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        foreach (string col in rows[r].Columns)
                            if (rows[r].GetString(col).IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0) { matched.Add(rows[r]); break; }
                    }
                    rows = matched;
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    string[] lines = (text ?? "").Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                        if (lines[i].IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0) sb.AppendLine(lines[i].TrimEnd('\r'));
                    text = sb.ToString().TrimEnd();
                }
                return;
            }

            if (verb == "pwd") { rows = null; text = CurrentDir; return; }
            if (verb == "drives" || verb == "df") { rows = MultiDrivePathEngine.ListAllDrivesRows(); text = ""; return; }
            if (verb == "history")
            {
                int lim = 40;
                if (toks.Count >= 2) int.TryParse(toks[1], out lim);
                rows = new List<OshRow>();
                int st = Math.Max(0, History.Count - Math.Max(1, lim));
                for (int i = st; i < History.Count; i++) rows.Add(new OshRow().Set("idx", i + 1).Set("command", History[i]));
                text = ""; return;
            }
            if (verb == "disk-hogs")
            {
                List<SpaceHogEntry> hogs = DiskSpaceOffloader.ScanCDriveHogs(ShellRoot);
                rows = new List<OshRow>();
                for (int i = 0; i < hogs.Count; i++)
                {
                    SpaceHogEntry h = hogs[i];
                    rows.Add(new OshRow()
                        .Set("category", h.Category)
                        .Set("size_mb", h.SizeMb)
                        .Set("files", h.FileCount)
                        .Set("state", h.IsJunction ? ("JUNCTION -> " + h.JunctionTarget) : "ON C: NVME")
                        .Set("path", h.PathOnC));
                }
                text = "";
                return;
            }
            if (verb == "offload" && toks.Count >= 2)
            {
                string srcDir = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                string dstDrv = toks.Count >= 3 ? toks[2] : (Directory.Exists(@"F:\") ? @"F:\" : @"E:\");
                rows = null;
                text = DiskSpaceOffloader.OffloadDirectoryWithJunction(srcDir, dstDrv, ShellRoot);
                return;
            }
            if (verb == "undo-junction" && toks.Count >= 2)
            {
                string srcDir = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                rows = null;
                text = DiskSpaceOffloader.UndoJunctionBackToC(srcDir, ShellRoot);
                return;
            }
            if (verb == "junctions")
            {
                Dictionary<string, string> jmap = DiskSpaceOffloader.ReadJunctionLedgerMap(ShellRoot);
                rows = new List<OshRow>();
                foreach (KeyValuePair<string, string> kv in jmap)
                    rows.Add(new OshRow().Set("c_junction", kv.Key).Set("vault_target", kv.Value));
                text = rows.Count == 0 ? "No active OmegaShell junctions created yet (use Tab 5 Sub-Tab E or 'disk-hogs' / 'offload')." : "";
                return;
            }

            if (verb == "cd")
            {
                string target = MultiDrivePathEngine.ResolvePath(CurrentDir, toks.Count >= 2 ? toks[1] : "@lab", ShellRoot);
                if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Directory not found: " + target);
                CurrentDir = target;
                rows = null;
                text = "CWD -> " + CurrentDir;
                return;
            }
            if (verb == "ls" || verb == "dir" || verb == "find")
            {
                ExecuteLsOrFind(verb, toks, out rows, out text);
                return;
            }
            if (verb == "cat" || verb == "type")
            {
                if (toks.Count < 2) throw new ArgumentException("Usage: cat <file>");
                string fp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                rows = null;
                text = File.ReadAllText(fp, Encoding.UTF8);
                return;
            }
            if (verb == "sha256")
            {
                if (toks.Count >= 2)
                {
                    string fp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                    rows = new List<OshRow> { new OshRow().Set("file", Path.GetFileName(fp)).Set("sha256", OshFormat.Sha256File(fp)).Set("path", fp) };
                    text = "";
                }
                else
                {
                    string src = rows != null ? OshTableFormatter.FormatJson(rows) : text;
                    rows = new List<OshRow> { new OshRow().Set("sha256", OshFormat.Sha256Hex(src)) };
                    text = "";
                }
                return;
            }
            if (verb == "mkdir" && toks.Count >= 2)
            {
                string dp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                Directory.CreateDirectory(dp);
                rows = null; text = "Created directory: " + dp;
                return;
            }
            if (verb == "touch" && toks.Count >= 2)
            {
                string fp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(fp));
                if (!File.Exists(fp)) File.WriteAllText(fp, "", new UTF8Encoding(false));
                else File.SetLastWriteTimeUtc(fp, DateTime.UtcNow);
                rows = null; text = "Touched: " + fp;
                return;
            }
            if (verb == "cp" && toks.Count >= 3)
            {
                string sPath = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                string dPath = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[2], ShellRoot);
                if (Directory.Exists(dPath)) dPath = Path.Combine(dPath, Path.GetFileName(sPath));
                File.Copy(sPath, dPath, true);
                rows = null; text = "Copied " + sPath + " -> " + dPath;
                return;
            }
            if (verb == "mv" && toks.Count >= 3)
            {
                string sPath = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                string dPath = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[2], ShellRoot);
                if (Directory.Exists(dPath)) dPath = Path.Combine(dPath, Path.GetFileName(sPath));
                if (File.Exists(dPath)) File.Delete(dPath);
                File.Move(sPath, dPath);
                rows = null; text = "Moved " + sPath + " -> " + dPath;
                return;
            }
            if (verb == "rm" && toks.Count >= 2)
            {
                string tPath = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[1], ShellRoot);
                if (File.Exists(tPath)) { File.Delete(tPath); rows = null; text = "Deleted file: " + tPath; return; }
                throw new IOException("Refusing recursive directory delete via bare 'rm' for safety (file not found: " + tPath + ").");
            }

            if (verb == "ps") { rows = BuildProcessRows(Settings.ProcessCoreRules); text = ""; return; }
            if (verb == "kill" && toks.Count >= 2) { rows = null; text = KillProcessTarget(toks[1]); return; }
            if (verb == "ports")
            {
                bool listenOnly = toks.Contains("-listen") || toks.Contains("--listen") || toks.Contains("-l");
                bool groupMode = toks.Contains("-group") || toks.Contains("-g");
                List<TcpSocketItem> socks = Win32HardwareTuner.GetLiveTcpSockets();
                socks.Sort(delegate(TcpSocketItem a, TcpSocketItem b)
                {
                    int ra = a.StateName == "LISTEN" ? 0 : (a.StateName == "ESTABLISHED" ? 1 : 2);
                    int rb = b.StateName == "LISTEN" ? 0 : (b.StateName == "ESTABLISHED" ? 1 : 2);
                    if (ra != rb) return ra.CompareTo(rb);
                    return a.LocalPort.CompareTo(b.LocalPort);
                });

                rows = new List<OshRow>();
                for (int i = 0; i < socks.Count; i++)
                {
                    TcpSocketItem sk = socks[i];
                    if (listenOnly && sk.StateName != "LISTEN") continue;
                    rows.Add(new OshRow()
                        .Set("port", sk.LocalPort)
                        .Set("state", sk.StateName)
                        .Set("pid", sk.Pid)
                        .Set("process", sk.ProcessName)
                        .Set("ram_mb", sk.RamMb)
                        .Set("local_ip", sk.LocalIp)
                        .Set("remote", sk.RemoteIp + ":" + sk.RemotePort));
                }
                if (groupMode) ApplyGroupBy("process", ref rows);
                text = "";
                return;
            }
            if (verb == "kill-port" && toks.Count >= 2)
            {
                int prt; int.TryParse(toks[1].TrimStart(':'), out prt);
                rows = null;
                text = prt > 0 ? Win32HardwareTuner.KillProcessOnPort(prt, Settings.ProtectedProcesses) : "Usage: kill-port <port_number>";
                return;
            }
            if (verb == "services")
            {
                string q = toks.Count >= 2 ? toks[1] : "";
                List<WindowsServiceItem> svcs = Win32HardwareTuner.GetWindowsServices(q, false);
                rows = new List<OshRow>();
                for (int i = 0; i < svcs.Count; i++)
                    rows.Add(new OshRow().Set("service", svcs[i].ServiceName).Set("status", svcs[i].Status).Set("start_mode", svcs[i].StartMode).Set("can_stop", svcs[i].CanStop).Set("display_name", svcs[i].DisplayName));
                text = "";
                return;
            }
            if (verb == "svc" && toks.Count >= 3)
            {
                rows = null;
                text = Win32HardwareTuner.ControlWindowsService(toks[2], toks[1]);
                return;
            }
            if (verb == "startup")
            {
                List<StartupAppItem> apps = Win32HardwareTuner.GetStartupApps(ShellRoot);
                if (toks.Count >= 3 && (toks[1] == "enable" || toks[1] == "disable"))
                {
                    bool en = (toks[1] == "enable");
                    for (int i = 0; i < apps.Count; i++)
                    {
                        if (apps[i].Name.IndexOf(toks[2], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            rows = null;
                            text = Win32HardwareTuner.SetStartupAppEnabled(apps[i], en, ShellRoot);
                            return;
                        }
                    }
                    rows = null; text = "No startup item matching '" + toks[2] + "' found.";
                    return;
                }
                rows = new List<OshRow>();
                for (int i = 0; i < apps.Count; i++)
                    rows.Add(new OshRow().Set("name", apps[i].Name).Set("enabled", apps[i].Enabled ? "ENABLED" : "DISABLED").Set("location", apps[i].LocationTag).Set("command", apps[i].Command));
                text = "";
                return;
            }
            if (verb == "gpu")
            {
                rows = Win32HardwareTuner.QueryNvidiaVramProcessRows();
                text = Win32HardwareTuner.QueryNvidiaGpuLine();
                return;
            }
            if (verb == "ram")
            {
                MemorySnapshot ms = Win32HardwareTuner.QueryMemoryStatus();
                rows = new List<OshRow> { new OshRow().Set("used_mb", ms.UsedPhysMb).Set("avail_mb", ms.AvailPhysMb).Set("total_mb", ms.TotalPhysMb).Set("load_pct", (int)ms.MemoryLoadPct).Set("commit_total_mb", ms.TotalPageMb) };
                text = "";
                return;
            }
            if (verb == "opt" || verb == "trim-ram")
            {
                ExecuteOptVerb(verb == "trim-ram" ? "trim-ram" : (toks.Count >= 2 ? toks[1].ToLowerInvariant() : "status"), toks, ref rows, ref text);
                return;
            }

            if (verb == "cloud") { rows = BuildCloudNodesStatusRows(); text = ""; return; }
            if (verb == "cas")
            {
                string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "ls";
                if (sub == "put" && toks.Count >= 3) { rows = Cloud.PutPathIntoCas(MultiDrivePathEngine.ResolvePath(CurrentDir, toks[2], ShellRoot)); text = ""; return; }
                if (sub == "get" && toks.Count >= 4) { rows = null; text = Cloud.GetBlobByPrefix(toks[2], MultiDrivePathEngine.ResolvePath(CurrentDir, toks[3], ShellRoot)); return; }
                if (sub == "sync") { rows = null; text = Cloud.SyncCasToBridgeAndColdTier(); return; }
                rows = Cloud.ListCasIndexRows(); text = "";
                return;
            }
            if (verb == "snap")
            {
                string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "ls";
                if (sub == "create" || sub == "save")
                {
                    string tag = toks.Count >= 3 ? toks[2] : ("snap_" + DateTime.UtcNow.ToString("HHmmss"));
                    string dir = toks.Count >= 4 ? MultiDrivePathEngine.ResolvePath(CurrentDir, toks[3], ShellRoot) : CurrentDir;
                    rows = null; text = Cloud.CreateSnapshot(tag, dir); return;
                }
                if (sub == "diff" && toks.Count >= 3)
                {
                    rows = Cloud.DiffSnapshot(toks[2], toks.Count >= 4 ? MultiDrivePathEngine.ResolvePath(CurrentDir, toks[3], ShellRoot) : "");
                    text = ""; return;
                }
                if (sub == "restore" && toks.Count >= 3)
                {
                    rows = null;
                    text = Cloud.RestoreSnapshot(toks[2], toks.Count >= 4 ? MultiDrivePathEngine.ResolvePath(CurrentDir, toks[3], ShellRoot) : "");
                    return;
                }
                rows = Cloud.ListSnapshots(); text = "";
                return;
            }
            if (verb == "kv")
            {
                string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "ls";
                if (sub == "set" && toks.Count >= 4) { string v = string.Join(" ", toks.GetRange(3, toks.Count - 3).ToArray()); Cloud.KvSet(toks[2], v); rows = null; text = "KV[" + toks[2] + "] = " + v; return; }
                if (sub == "get" && toks.Count >= 3) { rows = null; text = Cloud.KvGet(toks[2]); return; }
                rows = Cloud.KvListRows(); text = "";
                return;
            }
            if (verb == "mesh")
            {
                string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "pixels";
                if (sub == "beam" && toks.Count >= 3) { rows = null; text = MeshAndLabBridge.BeamFileToPixels(MultiDrivePathEngine.ResolvePath(CurrentDir, toks[2], ShellRoot)); return; }
                List<string> sers = MeshAndLabBridge.ListAdbSerials();
                rows = new List<OshRow>();
                for (int i = 0; i < sers.Count; i++) rows.Add(new OshRow().Set("slot", i == 0 ? "@p10 (03|7c|80)" : ("@px" + i)).Set("serial", sers[i]).Set("mode", (sers[i].Contains(":") ? "Wi-Fi :5555" : "USB-C Direct") + (MeshAndLabBridge.ProbePortFast(19788, 90) ? " + :19788 LIVE" : "")));
                text = sers.Count == 0 ? "No ADB Pixel currently connected." : "";
                return;
            }
            if (verb == "forge") { ExecuteForgeVerb(toks, ref rows, ref text); return; }
            if (verb == "browser")
            {
                string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "status";
                rows = null;
                if (sub == "freeze") text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:9778/api/freeze", 2500);
                else if (sub == "dl" && toks.Count >= 3) text = MeshAndLabBridge.HttpPostJson("http://127.0.0.1:9778/api/downloads/enqueue", "{\"url\":\"" + OshFormat.JsonEsc(toks[2]) + "\"}", 3500);
                else text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:9778/api/status", 2000);
                return;
            }
            if (verb == "av")
            {
                string avDir = Path.Combine(Environment.GetEnvironmentVariable("OMEGA_LAB_ROOT") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "03_omega_av");
                if (toks.Count >= 3 && toks[1].ToLowerInvariant() == "shader")
                {
                    Directory.CreateDirectory(Path.Combine(avDir, "shaders"));
                    string omsh = Path.Combine(avDir, "shaders", "osh_live.omsh");
                    string expr = string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray());
                    File.WriteAllText(omsh, expr, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(avDir, "inbox_open.cmd"), omsh, new UTF8Encoding(false));
                    rows = null; text = "Hot-beamed shader expression -> OmegaAV (:19779): " + omsh;
                    return;
                }
                rows = null; text = MeshAndLabBridge.ProbePortFast(19779, 140) ? "OmegaAV v0.7.1 (:19779) is ONLINE." : "OmegaAV v0.7.1 (:19779) is offline.";
                return;
            }
            if (verb == "sonar")
            {
                rows = null;
                string sub = (toks.Count >= 2 ? toks[1] : "status").ToLowerInvariant();
                if (!MeshAndLabBridge.ProbePortFast(19783, 140))
                    text = "OmegaSonar v1.1.0 GM (:19783) is offline — press Shift+F5 in Launcher to start.";
                else if (sub == "ping")
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19783/api/ping", 3000);
                else if (sub == "gestures")
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19783/api/gestures", 3000);
                else
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19783/api/status", 3000);
                return;
            }
            if (verb == "conductor" || verb == "omegaconductor")
            {
                rows = null;
                string sub = (toks.Count >= 2 ? toks[1] : "status").ToLowerInvariant();
                if (!MeshAndLabBridge.ProbePortFast(19790, 160))
                    text = "OmegaConductor v1.0.0 GM (:19790) is offline -- press Shift+F11 in Launcher to start.";
                else if (sub == "shm")
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19790/api/shm", 3000);
                else if (sub == "pulse" || sub == "orbit" || sub == "hover")
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19790/api/gesture?g=" + sub.ToUpperInvariant(), 3000);
                else
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19790/api/status", 3000);
                return;
            }
            if (verb == "tensor" || verb == "omegatensor")
            {
                rows = null;
                string sub = (toks.Count >= 2 ? toks[1] : "status").ToLowerInvariant();
                if (!MeshAndLabBridge.ProbePortFast(19791, 160))
                    text = "OmegaTensor v1.0.2 GM (:19791) is offline -- press Shift+F10 in Launcher to start.";
                else if (sub == "burst" || sub == "dispatch" || sub == "infer")
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19791/api/dispatch", 15000);
                else
                    text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19791/api/status", 4000);
                return;
            }
            if (verb == "synth")
            {
                rows = null;
                text = MeshAndLabBridge.ProbePortFast(19782, 140)
                    ? MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19782/api/status", 2000)
                    : "OmegaSynth v1.1.1 GM (:19782) is offline.";
                return;
            }
            if (verb == "steg")
            {
                rows = null;
                text = MeshAndLabBridge.ProbePortFast(19786, 140)
                    ? MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19786/api/status", 2000)
                    : "OmegaSteg v0.2 (:19786) is offline.";
                return;
            }
            if (verb == "sec" || verb == "omegasec")
            {
                rows = null;
                if (!MeshAndLabBridge.ProbePortFast(19786, 160))
                {
                    text = "OmegaSec Bus (:19786) is offline. Launch OmegaSteg v0.2 (Shift+F8) first.";
                    return;
                }
                string sub = (toks.Count >= 2 ? toks[1] : "status").ToLowerInvariant();
                string rest = toks.Count >= 3 ? string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray()) : "ACT-Omega Sovereign Shell Capsule";
                try
                {
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        if (sub == "seal")
                            text = wc.UploadString("http://127.0.0.1:19786/api/sec/seal?station=shell", "POST", rest);
                        else if (sub == "open")
                            text = wc.UploadString("http://127.0.0.1:19786/api/sec/open", "POST", rest.Trim());
                        else if (sub == "split")
                            text = wc.UploadString("http://127.0.0.1:19786/api/sec/split?station=shell", "POST", rest);
                        else if (sub == "stress")
                            text = wc.UploadString("http://127.0.0.1:19786/api/sec/stress", "POST", "");
                        else
                            text = wc.DownloadString("http://127.0.0.1:19786/api/sec/status");
                    }
                }
                catch (Exception ex) { text = "OmegaSec (:19786) RPC error: " + ex.Message; }
                return;
            }
            if (verb == "p10" || verb == "pixel10")
            {
                rows = null;
                string sub = (toks.Count >= 2 ? toks[1] : "status").ToLowerInvariant();
                string arg2 = toks.Count >= 3 ? toks[2] : "all";
                text = MeshAndLabBridge.ExecutePixel10WorkerVerb(sub, arg2, ShellRoot);
                return;
            }
            if (verb == "sheaf")
            {
                rows = null;
                text = MeshAndLabBridge.ProbePortFast(19784, 140)
                    ? MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19784/api/status", 2000)
                    : "OmegaSheaf v1.0 GM (:19784) is offline.";
                return;
            }
            if (verb == "penrose")
            {
                rows = null;
                text = MeshAndLabBridge.ProbePortFast(19785, 140)
                    ? MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19785/api/status", 2000)
                    : "OmegaPenrose v1.0.2 GM (:19785) is offline.";
                return;
            }
            if (verb == "alias")
            {
                if (toks.Count >= 3)
                {
                    string expansion = string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray());
                    Settings.Aliases[toks[1]] = expansion;
                    Settings.SaveToDisk(Path.Combine(ShellRoot, "config"));
                    rows = null; text = "Saved alias '" + toks[1] + "' -> " + expansion;
                    return;
                }
                rows = new List<OshRow>();
                foreach (KeyValuePair<string, string> kv in Settings.Aliases) rows.Add(new OshRow().Set("alias", kv.Key).Set("command", kv.Value));
                text = ""; return;
            }
            if (verb == "help" || verb == "?")
            {
                string q = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "";
                rows = BuildHelpCatalogRows(q);
                text = "OSH v1.0 GM Structured Command Reference (Tip: filter via 'help avf' or 'help fmt', or inspect in Grid!)";
                return;
            }

            rows = null;
            int extExit;
            text = RunExternalFallbackCommand(stage, out extExit);
            stageOk = (extExit == 0);
        }

        private void ExecuteAvfVerb(List<string> toks, ref List<OshRow> rows, ref string text)
        {
            string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "status";
            if (sub == "status")
            {
                rows = AndroidAvfDebianBridge.BuildAvfStatusRows(ShellRoot);
                text = "";
            }
            else if (sub == "keygen")
            {
                bool regen = toks.Contains("-force") || toks.Contains("--force");
                string pub, priv;
                string msg = AndroidAvfDebianBridge.EnsureOrGenerateEd25519KeyPair(ShellRoot, regen, out pub, out priv);
                rows = null;
                text = msg + "\r\nPublic Key:\r\n" + pub;
            }
            else if (sub == "setup" || sub == "bootstrap")
            {
                string script, oneLiner;
                text = AndroidAvfDebianBridge.ExecuteOneClickDebianBootstrap(ShellRoot, "", out script, out oneLiner);
                rows = null;
            }
            else if (sub == "wake" || sub == "keepalive")
            {
                rows = null;
                text = AndroidAvfDebianBridge.WakeAndroidLinuxTerminalWithKeepAlive("");
            }
            else if (sub == "tunnel" || sub == "forward")
            {
                rows = null;
                text = AndroidAvfDebianBridge.ArmAdbSshTunnels("");
            }
            else if (sub == "sync" && toks.Count >= 3)
            {
                string lp = MultiDrivePathEngine.ResolvePath(CurrentDir, toks[2], ShellRoot);
                rows = null;
                text = AndroidAvfDebianBridge.SyncLocalPathToDebianShared(lp, "");
            }
            else if (sub == "ls")
            {
                rows = AndroidAvfDebianBridge.ListDebianSharedFilesRows("");
                text = rows.Count == 0 ? "No files in /sdcard/Download/linux yet (use 'avf setup' or 'avf sync <path>')." : "";
            }
            else if (sub == "daemon" || sub == "virtio" || sub == "start-daemon")
            {
                rows = null;
                text = AndroidAvfDebianBridge.DeployAndStartPersistentAvfVirtioDaemon(ShellRoot, Settings.DebianLinuxUser, "");
            }
            else if (sub == "udot" || sub == "dispatch")
            {
                rows = null;
                text = AndroidAvfDebianBridge.DispatchAvfVirtioUdotJob(ShellRoot, Settings.DebianLinuxUser, "");
            }
            else if (sub == "health")
            {
                rows = null;
                string bm;
                AndroidAvfDebianBridge.EnsureHostPort19789Bridged(ShellRoot, Settings.DebianLinuxUser, "", out bm);
                text = AndroidAvfDebianBridge.HttpGetAvfFast("http://127.0.0.1:19789/api/health", 3000);
                if (string.IsNullOrEmpty(text)) text = "AVF Virtio-GPU daemon (:19789) offline — run 'avf daemon' to launch.";
            }
            else if (sub == "rust" || sub == "rust-bench" || sub == "build-rust")
            {
                rows = null;
                text = AndroidAvfDebianBridge.RunNativeArm64RustBuildAndBenchInDebian(ShellRoot, Settings.DebianLinuxUser, "");
            }
            else if (sub == "exec" && toks.Count >= 3)
            {
                string rcmd = string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray());
                rows = null;
                text = AndroidAvfDebianBridge.RunCommandInPixelDebian(ShellRoot, Settings.DebianLinuxUser, rcmd, "");
            }
            else
            {
                rows = AndroidAvfDebianBridge.BuildAvfStatusRows(ShellRoot);
                text = "AVF subcommands: avf status | avf daemon | avf udot | avf health | avf rust | avf keygen | avf setup | avf wake | avf tunnel | avf sync <path> | avf ls | avf exec <cmd>";
            }
        }

        private void ExecuteGitStat(out List<OshRow> rows, out string text)
        {
            rows = new List<OshRow>();
            int ec;
            string raw = CodeFormatterAndCompiler.RunProcessCapture("git.exe", "status --porcelain -b", CurrentDir, 6000, out ec);
            if (ec != 0)
            {
                text = raw.Length > 0 ? raw : "Not a git repository (or git.exe not found): " + CurrentDir;
                return;
            }
            string branch = "HEAD";
            string[] lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].TrimEnd('\r');
                if (ln.StartsWith("## ")) { branch = ln.Substring(3).Trim(); continue; }
                if (ln.Length < 4) continue;
                string xy = ln.Substring(0, 2).Trim();
                string fp = ln.Substring(3).Trim();
                rows.Add(new OshRow().Set("state", xy.Length > 0 ? xy : "MOD").Set("file", fp).Set("branch", branch));
            }
            text = rows.Count == 0 ? ("Git working tree CLEAN on branch: " + branch) : ("Branch: " + branch + " (" + rows.Count + " changed entry/entries)");
        }

        private static void ApplyGroupBy(string groupCol, ref List<OshRow> rows)
        {
            if (rows == null || rows.Count == 0) return;

            List<string> numCols = new List<string>();
            for (int c = 0; c < rows[0].Columns.Count; c++)
            {
                string cn = rows[0].Columns[c];
                if (string.Equals(cn, groupCol, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cn, "pid", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cn, "port", StringComparison.OrdinalIgnoreCase))
                    continue;
                bool isN;
                rows[0].GetDouble(cn, out isN);
                if (isN) numCols.Add(cn);
            }

            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, double[]> sums = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            List<string> order = new List<string>();

            for (int r = 0; r < rows.Count; r++)
            {
                string key = rows[r].GetString(groupCol);
                if (string.IsNullOrEmpty(key)) key = "(empty)";
                if (!counts.ContainsKey(key))
                {
                    counts[key] = 0;
                    sums[key] = new double[numCols.Count];
                    order.Add(key);
                }
                counts[key]++;
                double[] arr = sums[key];
                for (int n = 0; n < numCols.Count; n++)
                {
                    bool okN;
                    arr[n] += rows[r].GetDouble(numCols[n], out okN);
                }
            }

            List<OshRow> grouped = new List<OshRow>();
            for (int k = 0; k < order.Count; k++)
            {
                string key = order[k];
                OshRow gr = new OshRow().Set(groupCol, key).Set("count", counts[key]);
                double[] arr = sums[key];
                for (int n = 0; n < numCols.Count; n++)
                    gr.Set("sum_" + numCols[n], Math.Round(arr[n], 2));
                grouped.Add(gr);
            }
            grouped.Sort(delegate(OshRow a, OshRow b) { bool na, nb; return b.GetDouble("count", out nb).CompareTo(a.GetDouble("count", out na)); });
            rows = grouped;
        }

        private void ExecuteLsOrFind(string verb, List<string> toks, out List<OshRow> rows, out string text)
        {
            rows = new List<OshRow>();
            text = "";
            bool recursive = (verb == "find");
            string pattern = "*";
            string dirArg = "";

            for (int i = 1; i < toks.Count; i++)
            {
                string tk = toks[i];
                if (tk == "-r" || tk == "-R" || tk == "--recursive") recursive = true;
                else if (tk.Contains("*") || tk.Contains("?")) pattern = tk;
                else if (string.IsNullOrEmpty(dirArg)) dirArg = tk;
                else pattern = "*" + tk + "*";
            }

            string targetDir = MultiDrivePathEngine.ResolvePath(CurrentDir, dirArg, ShellRoot);
            if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException("Directory not found: " + targetDir);

            if (!recursive && pattern == "*")
            {
                string[] subDirs = Directory.GetDirectories(targetDir);
                for (int i = 0; i < subDirs.Length && rows.Count < 600; i++)
                {
                    DirectoryInfo di = new DirectoryInfo(subDirs[i]);
                    bool isJ = (di.Attributes & FileAttributes.ReparsePoint) != 0;
                    rows.Add(new OshRow().Set("name", di.Name + "\\").Set("kind", isJ ? "JUNCTION" : "DIR").Set("size_mb", 0.0).Set("modified", di.LastWriteTime.ToString("yyyy-MM-dd HH:mm")).Set("path", di.FullName));
                }
            }

            EnumerateFilesSafeBounded(targetDir, pattern, recursive ? 4 : 0, rows, 600);
        }

        private static void EnumerateFilesSafeBounded(string dir, string pattern, int depth, List<OshRow> outRows, int cap)
        {
            if (depth < 0 || outRows.Count >= cap) return;
            try
            {
                string[] files = Directory.GetFiles(dir, pattern);
                for (int i = 0; i < files.Length && outRows.Count < cap; i++)
                {
                    FileInfo fi = new FileInfo(files[i]);
                    outRows.Add(new OshRow()
                        .Set("name", fi.Name)
                        .Set("kind", (fi.Extension ?? "").TrimStart('.').ToUpperInvariant())
                        .Set("size_mb", Math.Round(fi.Length / 1048576.0, 2))
                        .Set("bytes", fi.Length)
                        .Set("modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"))
                        .Set("path", fi.FullName));
                }
                if (depth == 0) return;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length && outRows.Count < cap; s++)
                {
                    string nm = Path.GetFileName(subs[s]);
                    if (nm.Equals("Windows", StringComparison.OrdinalIgnoreCase) || nm.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || nm.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                        continue;
                    EnumerateFilesSafeBounded(subs[s], pattern, depth - 1, outRows, cap);
                }
            }
            catch { }
        }

        private static void ApplyWhereFilter(List<string> toks, ref List<OshRow> rows)
        {
            if (rows == null || toks.Count < 4) return;
            string col = toks[1];
            string op = toks[2].ToLowerInvariant();
            string rhsRaw = string.Join(" ", toks.GetRange(3, toks.Count - 3).ToArray());

            double rhsNum;
            bool rhsIsNum = TryParseNumberWithUnits(rhsRaw, out rhsNum);

            List<OshRow> kept = new List<OshRow>();
            for (int r = 0; r < rows.Count; r++)
            {
                bool isCellNum;
                double cellNum = rows[r].GetDouble(col, out isCellNum);
                string cellStr = rows[r].GetString(col);

                bool pass;
                if (rhsIsNum && isCellNum && (op == ">" || op == "gt" || op == ">=" || op == "ge" || op == "<" || op == "lt" || op == "<=" || op == "le" || op == "==" || op == "eq" || op == "!=" || op == "ne"))
                {
                    if (op == ">" || op == "gt") pass = cellNum > rhsNum;
                    else if (op == ">=" || op == "ge") pass = cellNum >= rhsNum;
                    else if (op == "<" || op == "lt") pass = cellNum < rhsNum;
                    else if (op == "<=" || op == "le") pass = cellNum <= rhsNum;
                    else if (op == "==" || op == "eq") pass = Math.Abs(cellNum - rhsNum) < 0.0001;
                    else pass = Math.Abs(cellNum - rhsNum) >= 0.0001;
                }
                else
                {
                    if (op == "contains" || op == "~") pass = cellStr.IndexOf(rhsRaw, StringComparison.OrdinalIgnoreCase) >= 0;
                    else if (op == "!=" || op == "ne") pass = !string.Equals(cellStr, rhsRaw, StringComparison.OrdinalIgnoreCase);
                    else pass = string.Equals(cellStr, rhsRaw, StringComparison.OrdinalIgnoreCase);
                }
                if (pass) kept.Add(rows[r]);
            }
            rows = kept;
        }

        private static bool TryParseNumberWithUnits(string s, out double val)
        {
            val = 0.0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string clean = s.Trim().ToLowerInvariant();
            double mult = 1.0;
            if (clean.EndsWith("gb")) { mult = 1024.0; clean = clean.Substring(0, clean.Length - 2).Trim(); }
            else if (clean.EndsWith("mb")) { mult = 1.0; clean = clean.Substring(0, clean.Length - 2).Trim(); }
            else if (clean.EndsWith("kb")) { mult = 1.0 / 1024.0; clean = clean.Substring(0, clean.Length - 2).Trim(); }
            double baseVal;
            if (!double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out baseVal)) return false;
            val = baseVal * mult;
            return true;
        }

        private static void ApplySort(List<string> toks, ref List<OshRow> rows)
        {
            if (rows == null || rows.Count <= 1 || toks.Count < 2) return;
            bool desc = false;
            string col = toks[1];
            if ((col == "-desc" || col == "-r") && toks.Count >= 3) { desc = true; col = toks[2]; }

            rows.Sort(delegate(OshRow a, OshRow b)
            {
                bool na, nb;
                double da = a.GetDouble(col, out na);
                double db = b.GetDouble(col, out nb);
                int cmp = (na && nb) ? da.CompareTo(db) : string.Compare(a.GetString(col), b.GetString(col), StringComparison.OrdinalIgnoreCase);
                return desc ? -cmp : cmp;
            });
        }

        public static List<OshRow> BuildProcessRows(Dictionary<string, string> customRules)
        {
            List<OshRow> rows = new List<OshRow>();
            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    long mb = p.WorkingSet64 / 1048576L;
                    string prio = "Normal";
                    try { prio = p.PriorityClass.ToString(); } catch { }
                    long affVal = -1L;
                    bool hasAcc = false;
                    try { affVal = p.ProcessorAffinity.ToInt64(); hasAcc = true; } catch { }
                    string affHex = hasAcc ? ("0x" + affVal.ToString("X")) : "-";
                    string tier = Win32HardwareTuner.ClassifyCoreTier(affVal, hasAcc, prio);
                    string rule = "-";
                    if (customRules != null)
                    {
                        string rm;
                        if (customRules.TryGetValue(p.ProcessName, out rm)) rule = rm.ToUpperInvariant();
                    }
                    rows.Add(new OshRow()
                        .Set("pid", p.Id)
                        .Set("name", p.ProcessName)
                        .Set("ram_mb", mb)
                        .Set("core_tier", tier)
                        .Set("rule", rule)
                        .Set("threads", p.Threads.Count)
                        .Set("priority", prio)
                        .Set("affinity", affHex));
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            rows.Sort(delegate(OshRow a, OshRow b) { bool na, nb; return b.GetDouble("ram_mb", out nb).CompareTo(a.GetDouble("ram_mb", out na)); });
            return rows;
        }

        private string KillProcessTarget(string target)
        {
            int pid;
            if (int.TryParse(target, out pid))
            {
                if (pid <= 4 || pid == Process.GetCurrentProcess().Id) return "Refusing to terminate protected PID " + pid;
                using (Process p = Process.GetProcessById(pid))
                {
                    string nm = p.ProcessName;
                    p.Kill();
                    return "Terminated " + nm + " (PID " + pid + ").";
                }
            }
            if (Settings.ProtectedProcesses.Contains(target)) return "Blocked: '" + target + "' is in your Protected Whitelist.";
            Process[] ps = Process.GetProcessesByName(target);
            int cnt = 0;
            for (int i = 0; i < ps.Length; i++) { try { ps[i].Kill(); cnt++; } catch { } finally { ps[i].Dispose(); } }
            return "Terminated " + cnt + " instance(s) of '" + target + "'.";
        }

        private void ExecuteOptVerb(string sub, List<string> toks, ref List<OshRow> rows, ref string text)
        {
            rows = null;
            if (sub == "trim-ram")
            {
                int cnt; long freed;
                text = Win32HardwareTuner.TrimWorkingSetMemory(Settings.ProtectedProcesses, out cnt, out freed);
            }
            else if (sub == "boost")
            {
                text = Win32HardwareTuner.ApplySovereignComputeBoost(Settings.ProtectedProcesses, Settings.ProcessCoreRules);
            }
            else if (sub == "restore")
            {
                text = Win32HardwareTuner.RestoreAllProcessDefaults();
            }
            else if (sub == "rules")
            {
                rows = new List<OshRow>();
                foreach (KeyValuePair<string, string> kv in Settings.ProcessCoreRules)
                    rows.Add(new OshRow().Set("process", kv.Key).Set("auto_tier", kv.Value.ToUpperInvariant()));
                text = rows.Count == 0 ? "No custom process core rules saved yet." : "";
            }
            else if (sub == "rule" && toks.Count >= 4)
            {
                string procNm = toks[2];
                string mode = toks[3].ToLowerInvariant();
                if (mode == "clear" || mode == "none")
                {
                    Settings.ProcessCoreRules.Remove(procNm);
                    Settings.SaveToDisk(Path.Combine(ShellRoot, "config"));
                    text = "Cleared custom core rule for '" + procNm + "'.";
                }
                else if (mode == "pcores" || mode == "eco")
                {
                    Settings.ProcessCoreRules[procNm] = mode;
                    Settings.SaveToDisk(Path.Combine(ShellRoot, "config"));
                    text = "Saved persistent rule: '" + procNm + "' -> " + mode.ToUpperInvariant() + " (enforced on P-Core Boost).";
                }
                else text = "Usage: opt rule <process_name> <pcores|eco|clear>";
            }
            else if (sub == "power" && toks.Count >= 3)
            {
                text = Win32HardwareTuner.SwitchPowerPlan(toks[2]);
            }
            else if ((sub == "pcores" || sub == "eco") && toks.Count >= 3)
            {
                long pMask, eMask, allMask;
                Win32HardwareTuner.GetHybridCoreMasks(out pMask, out eMask, out allMask);
                int pid;
                if (!int.TryParse(toks[2], out pid))
                {
                    Process[] byNm = Process.GetProcessesByName(toks[2]);
                    if (byNm.Length > 0) pid = byNm[0].Id;
                }
                string detail;
                if (sub == "pcores") Win32HardwareTuner.SetProcessEcoQoSAndAffinity(pid, false, pMask, ProcessPriorityClass.AboveNormal, out detail);
                else Win32HardwareTuner.SetProcessEcoQoSAndAffinity(pid, true, eMask, ProcessPriorityClass.BelowNormal, out detail);
                text = detail;
            }
            else if (sub == "clean-temp")
            {
                bool apply = toks.Contains("--apply") || toks.Contains("-y");
                int fc; long fb;
                text = Win32HardwareTuner.CleanSafeTempAndCaches(!apply, out fc, out fb);
            }
            else
            {
                MemorySnapshot ms = Win32HardwareTuner.QueryMemoryStatus();
                double cpu = Win32HardwareTuner.SampleCpuUsagePct();
                long pMask, eMask, allMask;
                Win32HardwareTuner.GetHybridCoreMasks(out pMask, out eMask, out allMask);
                text = "=== OMEGASHELL v0.3 HARDWARE TUNER STATUS ===\r\n" +
                    "CPU Usage:    " + cpu.ToString("0.0") + "% (" + Environment.ProcessorCount + " logical cores | P-Mask=0x" + pMask.ToString("X") + ", E-Mask=0x" + eMask.ToString("X") + ")\r\n" +
                    "Physical RAM: " + ms.UsedPhysMb + " / " + ms.TotalPhysMb + " MB (" + ms.AvailPhysMb + " MB Free, Load " + ms.MemoryLoadPct + "%)\r\n" +
                    "GPU Status:   " + Win32HardwareTuner.QueryNvidiaGpuLine() + "\r\n" +
                    "Power Scheme: " + Win32HardwareTuner.GetActivePowerPlanBanner();
            }
        }

        private List<OshRow> BuildCloudNodesStatusRows()
        {
            List<OshRow> rows = new List<OshRow>();
            try
            {
                DriveInfo[] drvs = DriveInfo.GetDrives();
                int dIdx = 1;
                for (int i = 0; i < drvs.Length; i++)
                {
                    if (!drvs[i].IsReady) continue;
                    string lbl = "";
                    try { lbl = drvs[i].VolumeLabel ?? ""; } catch { }
                    double freeGb = Math.Round(drvs[i].AvailableFreeSpace / 1073741824.0, 2);
                    double usedPct = drvs[i].TotalSize > 0 ? Math.Round(((drvs[i].TotalSize - drvs[i].AvailableFreeSpace) * 100.0) / drvs[i].TotalSize, 1) : 0.0;
                    string role = drvs[i].Name.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "Boot NVMe" :
                        (drvs[i].Name.StartsWith("E", StringComparison.OrdinalIgnoreCase) ? "Hot Lab Vault" :
                        (drvs[i].Name.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "2.3TB Cold Archive" :
                        (drvs[i].Name.StartsWith("G", StringComparison.OrdinalIgnoreCase) ? "Drive Bridge" : "Mounted Volume")));

                    rows.Add(new OshRow()
                        .Set("node", "Drive-" + dIdx + ": " + role + " (" + lbl + ")")
                        .Set("status", usedPct >= 90.0 ? ("WARN " + usedPct + "%") : "ONLINE")
                        .Set("free_gb", freeGb)
                        .Set("endpoint", drvs[i].Name));
                    dIdx++;
                }
            }
            catch { }

            List<string> px = MeshAndLabBridge.ListAdbSerials();
            bool dummyTcp; string dummyBan; bool avfTun = AndroidAvfDebianBridge.ProbeSshBannerFast(AndroidAvfDebianBridge.DEFAULT_LOCAL_AVF_SSH_PORT, 140, out dummyTcp, out dummyBan) || AndroidAvfDebianBridge.ProbeSshBannerFast(AndroidAvfDebianBridge.DEFAULT_LOCAL_TERMUX_SSH_PORT, 110, out dummyTcp, out dummyBan);
            rows.Add(new OshRow().Set("node", "Edge: Pixel 10 Tensor G5 (@p10)").Set("status", px.Count > 0 ? ("ONLINE (" + px[0] + ")") : "STANDBY").Set("free_gb", 0.0).Set("endpoint", "0x03=A520 | 0x7C=5xA725 | 0x80=X4"));
            rows.Add(new OshRow().Set("node", "Edge: Pixel 10 Rust+Vulkan (:19788)").Set("status", MeshAndLabBridge.ProbePortFast(19788, 80) ? "DAEMON LIVE" : (px.Count > 0 ? "ADB READY" : "STANDBY")).Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19788 -> /data/local/tmp"));
            rows.Add(new OshRow().Set("node", "Edge: AVF Virtio-GPU (:19789)").Set("status", MeshAndLabBridge.ProbePortFast(19789, 80) ? "DAEMON LIVE" : (avfTun ? "SSH READY" : "STANDBY")).Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19789 -> Virtio-GPU GFXStream"));
            string tpuSt = "STANDBY";
            if (MeshAndLabBridge.ProbePortFast(19788, 80)) {
                try {
                    System.Net.HttpWebRequest rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:19788/api/health");
                    rq.Timeout = 1200;
                    using (System.Net.HttpWebResponse rp = (System.Net.HttpWebResponse)rq.GetResponse())
                    using (System.IO.StreamReader sr = new System.IO.StreamReader(rp.GetResponseStream(), System.Text.Encoding.UTF8)) {
                        string hj = sr.ReadToEnd();
                        System.Text.RegularExpressions.Match mg = System.Text.RegularExpressions.Regex.Match(hj ?? "", "\"tpu_peak_gops\"\\s*:\\s*([0-9.]+)");
                        if ((hj ?? "").IndexOf("\"tpu_hw_unlocked\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                            tpuSt = "TPU UNLOCKED (" + (mg.Success ? mg.Groups[1].Value : "LIVE") + " GOPS/s)";
                        else tpuSt = "DAEMON LIVE";
                    }
                } catch { tpuSt = "DAEMON LIVE"; }
            }
            rows.Add(new OshRow().Set("node", "Edge: Tensor G5 4th-Gen TPU (:19788)").Set("status", tpuSt).Set("free_gb", 0.0).Set("endpoint", "36500000.buenos -> google-edgetpu (16L DMA)"));
            rows.Add(new OshRow().Set("node", "Edge: Android AVF Debian (@deb)").Set("status", avfTun ? "SSH TUNNEL LIVE" : (px.Count > 0 ? "ADB READY" : "STANDBY")).Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:22222 -> :2222"));
            rows.Add(new OshRow().Set("node", "Service: OmegaMirror v0.9").Set("status", MeshAndLabBridge.ProbePortFast(19777, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19777"));
            rows.Add(new OshRow().Set("node", "Service: OmegaBrowser v0.6.6").Set("status", MeshAndLabBridge.ProbePortFast(9778, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:9778"));
            rows.Add(new OshRow().Set("node", "Service: OmegaAV v0.7.1").Set("status", MeshAndLabBridge.ProbePortFast(19779, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19779"));
            rows.Add(new OshRow().Set("node", "Service: OmegaForge v0.2.2").Set("status", MeshAndLabBridge.ProbePortFast(19780, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19780"));
            rows.Add(new OshRow().Set("node", "Service: OmegaShell v1.1.6 GM").Set("status", MeshAndLabBridge.ProbePortFast(19781, 80) ? "ONLINE" : "ONLINE (Self)").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19781"));
            rows.Add(new OshRow().Set("node", "Service: OmegaSynth v1.1.1 GM").Set("status", MeshAndLabBridge.ProbePortFast(19782, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19782"));
            rows.Add(new OshRow().Set("node", "Service: OmegaSonar v1.1.0 GM").Set("status", MeshAndLabBridge.ProbePortFast(19783, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19783 (Golay 19kHz)"));
            rows.Add(new OshRow().Set("node", "Service: OmegaSheaf v0.2.2 GM").Set("status", MeshAndLabBridge.ProbePortFast(19784, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19784"));
            rows.Add(new OshRow().Set("node", "Service: OmegaPenrose v1.0.2 GM").Set("status", MeshAndLabBridge.ProbePortFast(19785, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19785"));
            rows.Add(new OshRow().Set("node", "Service: OmegaSteg v0.2.1 OmegaSec").Set("status", MeshAndLabBridge.ProbePortFast(19786, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19786"));
            rows.Add(new OshRow().Set("node", "Service: OmegaConductor v1.0.0 GM").Set("status", MeshAndLabBridge.ProbePortFast(19790, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19790 (6-DoF Hopf S^3 Wand Bus)"));
            rows.Add(new OshRow().Set("node", "Service: OmegaTensor v1.0.2 GM").Set("status", MeshAndLabBridge.ProbePortFast(19791, 80) ? "ONLINE" : "OFFLINE").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:19791 (5-Engine 9.0 TOPS Split)"));
            rows.Add(new OshRow().Set("node", "Service: Sovereign Manifold").Set("status", MeshAndLabBridge.ProbePortFast(8097, 80) ? "ONLINE" : "STANDBY").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:8097"));
            rows.Add(new OshRow().Set("node", "Service: Ollama Engine").Set("status", MeshAndLabBridge.ProbePortFast(11434, 80) ? "ONLINE" : "STANDBY").Set("free_gb", 0.0).Set("endpoint", "127.0.0.1:11434"));
            return rows;
        }

        private static void ExecuteForgeVerb(List<string> toks, ref List<OshRow> rows, ref string text)
        {
            string sub = toks.Count >= 2 ? toks[1].ToLowerInvariant() : "status";
            if (!MeshAndLabBridge.ProbePortFast(19780, 140))
            {
                rows = null; text = "OmegaForge (:19780) is currently offline. Launch it from Tab 6 or press F4 in OmegaLabLauncher!";
                return;
            }
            if (sub == "tools") { rows = null; text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19780/api/tools", 3000); return; }
            if (sub == "run" && toks.Count >= 3)
            {
                string lang = toks[2];
                string code = toks.Count >= 4 ? string.Join(" ", toks.GetRange(3, toks.Count - 3).ToArray()) : text;
                rows = null;
                text = MeshAndLabBridge.HttpPostJson("http://127.0.0.1:19780/api/exec", "{\"lang\":\"" + OshFormat.JsonEsc(lang) + "\",\"code\":\"" + OshFormat.JsonEsc(code) + "\"}", 25000);
                return;
            }
            if (sub == "verify")
            {
                string src = toks.Count >= 3 ? string.Join(" ", toks.GetRange(2, toks.Count - 2).ToArray()) : text;
                rows = null;
                text = MeshAndLabBridge.HttpPostJson("http://127.0.0.1:19780/api/verify", "{\"text\":\"" + OshFormat.JsonEsc(src) + "\"}", 35000);
                return;
            }
            rows = null;
            text = MeshAndLabBridge.HttpGetJson("http://127.0.0.1:19780/api/status", 2500);
        }

        private string RunExternalFallbackCommand(string rawCmd, out int exitCode)
        {
            exitCode = 0;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + rawCmd.Replace("\"", "\\\"") + "\"");
                psi.WorkingDirectory = CurrentDir;
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
                    p.ErrorDataReceived += delegate(object s2, DataReceivedEventArgs e2) { if (e2.Data != null && sb.Length < 65536) sb.AppendLine(e2.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } sb.AppendLine("[Command timed out after 15s]"); exitCode = 124; }
                    else exitCode = p.ExitCode;
                    return sb.ToString().TrimEnd();
                }
            }
            catch (Exception ex) { exitCode = -1; return "[External Exec Error] " + ex.Message; }
        }

        public static List<OshRow> BuildHelpCatalogRows(string filter)
        {
            string[,] items = new string[,]
            {
                { "Gemma Command", "gemma <goal> | @gemma <goal>", "Autonomous Gemma ReAct agent across OSH, Codegen, @deb & Lab Suite" },
                { "Gemma Codegen", "codegen rust \"<spec>\"", "Generate, Auto-Format, Compile & Self-Heal polyglot code -> Tab 2" },
                { "Gemma Models", "models | use-model <tag>", "Discover live :8097 Sovereign & :11434 Ollama models or switch" },
                { "Code & Auto-Fmt", "code <file>", "Open file in Tab 2 Syntax-Highlighted Code Studio" },
                { "Code & Auto-Fmt", "fmt <path> [-check]", "Format Rust/C#/C++/TS/PS1/Lean4/JSON via CLI or AST" },
                { "Code & Auto-Fmt", "build <file> | check <file>", "Compile/run or syntax-check code with line diagnostics" },
                { "Code & Auto-Fmt", "diff <fileA> <fileB>", "Compute line-by-line unified diff table" },
                { "Code & Auto-Fmt", "toolchains | git-stat", "List formatters/compilers or structured git status" },
                { "AVF & Debian VM", "avf status | avf keygen", "Probe Pixel AVF/pKVM & generate Ed25519 SSH key" },
                { "AVF & Debian VM", "avf setup | avf wake", "1-Click Debian SSH bootstrap + wake Linux Terminal" },
                { "AVF & Debian VM", "@deb <cmd> | avf sync <p>", "Run command in Pixel Debian VM or sync to /mnt/shared" },
                { "OSH Programming", "let x = 2^10; calc $x * 3", "Variables ($x, $env:PATH), chains (&&, ||, ;) & math" },
                { "OSH Programming", "ps | group name | top 10", "Group rows + sum RAM; redirect > out.json / .csv" },
                { "OSH Programming", "bench 5 \"drives\"", "Micro-benchmark command (min / median / mean / max ms)" },
                { "Drives & Relief", "drives | disk-hogs", "Inspect C:/E:/F:/G: drives & scan C: cache space hogs" },
                { "Drives & Relief", "offload <C:\\dir> F:", "Atomic mklink /J junction move to F: (2.33 TB) or E:" },
                { "Win32 & Ports", "opt trim-ram | opt boost", "Reclaim DDR5 via EmptyWorkingSet & P-Core/EcoQoS tune" },
                { "Win32 & Ports", "ports -listen | kill-port", "List active LISTEN sockets first or free a stuck port" },
                { "Cloud & Mesh", "cloud status | cas sync", "Replicate SHA-256 CAS blobs to F: Cold Vault + G:" }
            };

            List<OshRow> rows = new List<OshRow>();
            for (int i = 0; i < items.GetLength(0); i++)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    items[i, 0].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    items[i, 1].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    items[i, 2].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                rows.Add(new OshRow().Set("domain", items[i, 0]).Set("syntax", items[i, 1]).Set("summary", items[i, 2]));
            }
            return rows;
        }
    }

    public static class OshMathEvaluator
    {
        public static bool TryEval(string expr, out double result)
        {
            result = 0.0;
            if (string.IsNullOrWhiteSpace(expr)) return false;
            try
            {
                Parser p = new Parser(expr.Trim());
                double v = p.ParseExpr();
                if (!p.AtEnd) return false;
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
                result = Math.Round(v, 8);
                return true;
            }
            catch { return false; }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s) { _s = s; _pos = 0; }
            public bool AtEnd { get { SkipWs(); return _pos >= _s.Length; } }

            private void SkipWs() { while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++; }

            public double ParseExpr()
            {
                double v = ParseTerm();
                while (true)
                {
                    SkipWs();
                    if (_pos < _s.Length && _s[_pos] == '+') { _pos++; v += ParseTerm(); }
                    else if (_pos < _s.Length && _s[_pos] == '-') { _pos++; v -= ParseTerm(); }
                    else break;
                }
                return v;
            }

            private double ParseTerm()
            {
                double v = ParsePower();
                while (true)
                {
                    SkipWs();
                    if (_pos < _s.Length && _s[_pos] == '*') { _pos++; v *= ParsePower(); }
                    else if (_pos < _s.Length && _s[_pos] == '/') { _pos++; double d = ParsePower(); v = d == 0.0 ? 0.0 : (v / d); }
                    else if (_pos < _s.Length && _s[_pos] == '%') { _pos++; double d = ParsePower(); v = d == 0.0 ? 0.0 : (v % d); }
                    else break;
                }
                return v;
            }

            private double ParsePower()
            {
                double b = ParseUnary();
                SkipWs();
                if (_pos < _s.Length && _s[_pos] == '^') { _pos++; double e = ParsePower(); return Math.Pow(b, e); }
                return b;
            }

            private double ParseUnary()
            {
                SkipWs();
                if (_pos < _s.Length && _s[_pos] == '-') { _pos++; return -ParseUnary(); }
                if (_pos < _s.Length && _s[_pos] == '+') { _pos++; return ParseUnary(); }
                return ParsePrimary();
            }

            private double ParsePrimary()
            {
                SkipWs();
                if (_pos >= _s.Length) throw new FormatException();
                if (_s[_pos] == '(')
                {
                    _pos++;
                    double v = ParseExpr();
                    SkipWs();
                    if (_pos >= _s.Length || _s[_pos] != ')') throw new FormatException();
                    _pos++;
                    return v;
                }

                if (char.IsLetter(_s[_pos]))
                {
                    int st = _pos;
                    while (_pos < _s.Length && (char.IsLetterOrDigit(_s[_pos]) || _s[_pos] == '_')) _pos++;
                    string ident = _s.Substring(st, _pos - st).ToLowerInvariant();
                    if (ident == "pi") return Math.PI;
                    if (ident == "e") return Math.E;
                    if (ident == "phi") return 1.6180339887498948;
                    SkipWs();
                    if (_pos < _s.Length && _s[_pos] == '(')
                    {
                        _pos++;
                        double arg = ParseExpr();
                        SkipWs();
                        if (_pos >= _s.Length || _s[_pos] != ')') throw new FormatException();
                        _pos++;
                        if (ident == "sqrt") return Math.Sqrt(arg);
                        if (ident == "abs") return Math.Abs(arg);
                        if (ident == "sin") return Math.Sin(arg);
                        if (ident == "cos") return Math.Cos(arg);
                        if (ident == "log") return Math.Log(arg);
                    }
                    throw new FormatException();
                }

                int numStart = _pos;
                while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
                if (_pos == numStart) throw new FormatException();
                double parsed;
                if (!double.TryParse(_s.Substring(numStart, _pos - numStart), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    throw new FormatException();
                return parsed;
            }
        }
    }
}
