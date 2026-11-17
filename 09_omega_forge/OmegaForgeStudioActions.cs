using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaForgeStudio
{
    public sealed partial class OmegaForgeForm
    {
        private async Task RunFullSystemProbeAsync()
        {
            _lblTopStats.Text = "Scanning laptop PATH, Visual Studio, Windows Kits, Cargo, Ollama, and Multi-Drive AI Weights...";
            string root = _forgeRoot;
            List<ToolDescriptor> scannedTools = await Task.Run(delegate { return OmegaForgeScanner.ScanAllToolchains(root); });
            LoadSavedToolProfile(scannedTools);
            _tools = scannedTools;

            List<AiModelDescriptor> scannedModels = await Task.Run(delegate { return OmegaForgeScanner.ScanAllModelsAndEndpoints(scannedTools); });
            _models = scannedModels;

            BindModelsToUi();
            RefreshToolChecklist();
            PopulateDefaultScratchForLang();
            UpdateHeaderCounts();
            _lblStatus.Text = "OmegaForge v0.2.1 Probe Complete :: Ready on 127.0.0.1:19780 | Workspace: " + _workspaceDir;
        }

        private void BindModelsToUi()
        {
            _cmbModel.Items.Clear();
            _lstDiskModels.Items.Clear();
            for (int i = 0; i < _models.Count; i++)
            {
                _cmbModel.Items.Add(_models[i]);
                _lstDiskModels.Items.Add(_models[i].ToString());
            }
            if (_cmbModel.Items.Count > 0) _cmbModel.SelectedIndex = 0;
        }

        private async Task WakeOllamaAndRescanAsync()
        {
            _lblStatus.Text = "Waking Ollama (:11434) and refreshing live models...";
            List<ToolDescriptor> snap = new List<ToolDescriptor>(_tools);
            string msg = await Task.Run(delegate { return OmegaForgeScanner.WakeOllamaServer(snap); });
            List<AiModelDescriptor> refreshed = await Task.Run(delegate { return OmegaForgeScanner.ScanAllModelsAndEndpoints(snap); });
            _models = refreshed;
            BindModelsToUi();
            UpdateHeaderCounts();
            _txtExecLog.AppendText("[WAKE OLLAMA] " + msg + "\r\n");
            _lblStatus.Text = msg;
        }

        private void RefreshToolChecklist()
        {
            _suppressItemCheck = true;
            _clbTools.BeginUpdate();
            _clbTools.Items.Clear();
            _visibleTools.Clear();

            string cat = _cmbCategoryFilter.SelectedItem != null ? _cmbCategoryFilter.SelectedItem.ToString() : "All Categories";
            string q = (_txtToolSearch.Text ?? "").Trim().ToLowerInvariant();

            for (int i = 0; i < _tools.Count; i++)
            {
                ToolDescriptor t = _tools[i];
                if (!t.Found && !t.IsBuiltInVirtual) t.Enabled = false;
                if (cat == "Found Only" && !t.Found && !t.IsBuiltInVirtual) continue;
                else if (cat != "All Categories" && cat != "Found Only" && t.Category != cat) continue;

                if (q.Length > 0 && t.Name.ToLowerInvariant().IndexOf(q) < 0 && t.Id.ToLowerInvariant().IndexOf(q) < 0 && t.VersionBanner.ToLowerInvariant().IndexOf(q) < 0)
                    continue;

                _visibleTools.Add(t);
                _clbTools.Items.Add(t.DisplayLabel, t.Enabled && (t.Found || t.IsBuiltInVirtual));
            }

            _clbTools.EndUpdate();
            _suppressItemCheck = false;
        }

        private void UpdateHeaderCounts()
        {
            int foundTools = 0, enabledTools = 0, gemmaModels = 0, liveModels = 0;
            for (int i = 0; i < _tools.Count; i++)
            {
                if (_tools[i].Found || _tools[i].IsBuiltInVirtual) foundTools++;
                if (_tools[i].Enabled && (_tools[i].Found || _tools[i].IsBuiltInVirtual)) enabledTools++;
            }
            for (int m = 0; m < _models.Count; m++)
            {
                if (_models[m].IsGemma) gemmaModels++;
                if (_models[m].IsLiveServer) liveModels++;
            }

            _lblTopStats.Text = "OMEGAFORGE v0.2.1 (:19780) | Found Tools: " + foundTools + "/" + _tools.Count + " (" + enabledTools + " Armed) | AI Models: " + _models.Count + " (" + liveModels + " Live, " + gemmaModels + " Gemma)";
        }

        private void OnSelectedModelChanged()
        {
            AiModelDescriptor m = _cmbModel.SelectedItem as AiModelDescriptor;
            if (m == null) return;
            _txtEndpoint.Text = m.EndpointUrl;
            _txtModelTag.Text = m.ModelTag;
            _lblStatus.Text = "Assigned AI -> " + m.DisplayName + " [" + m.EndpointUrl + "]";
        }

        private void BootOrPingSelectedModel()
        {
            AiModelDescriptor m = _cmbModel.SelectedItem as AiModelDescriptor;
            string newEp;
            string msg = LocalGemmaAgentEngine.TryBootLocalModelServer(m, _tools, out newEp);
            if (!string.IsNullOrEmpty(newEp)) _txtEndpoint.Text = newEp;
            _lblStatus.Text = msg;
            _txtExecLog.AppendText("[MODEL BOOT/PING] " + msg + "\r\n");
        }

        private void SetToolPreset(string mode)
        {
            for (int i = 0; i < _tools.Count; i++)
            {
                ToolDescriptor t = _tools[i];
                if (!t.Found && !t.IsBuiltInVirtual) { t.Enabled = false; continue; }
                if (mode == "all") t.Enabled = true;
                else if (mode == "gpu") t.Enabled = (t.Category == "GPU & Shaders" || t.Id == "omega_av_ipc");
                else if (mode == "lean") t.Enabled = (t.Id == "lean4" || t.Id == "lake" || t.Id == "fs_workspace");
                else if (mode == "systems") t.Enabled = (t.Id == "csc" || t.Id == "rustc" || t.Id == "cargo" || t.Id == "clangpp" || t.Id == "msvc_cl" || t.Id == "python" || t.Id == "fs_workspace");
                else if (mode == "edge") t.Enabled = (t.Id == "adb" || t.Id == "duckdb" || t.Id == "sqlite3" || t.Id == "powershell" || t.Id == "git");
            }
            RefreshToolChecklist();
            UpdateHeaderCounts();
            _lblStatus.Text = "Applied Tool Preset -> [" + mode.ToUpperInvariant() + "]";
        }

        private void LoadSelectedToolSampleIntoScratchpad()
        {
            int idx = _clbTools.SelectedIndex;
            if (idx < 0 || idx >= _visibleTools.Count) return;
            ToolDescriptor td = _visibleTools[idx];
            string mapLang = td.Id;
            if (td.Id == "csc") mapLang = "csharp";
            else if (td.Id == "rustc") mapLang = "rust";
            else if (td.Id == "clangpp") mapLang = "cpp";
            else if (td.Id == "msvc_cl") mapLang = "msvc";
            else if (td.Id == "fxc") mapLang = "hlsl";

            for (int i = 0; i < _cmbScratchLang.Items.Count; i++)
            {
                if (string.Equals(_cmbScratchLang.Items[i].ToString(), mapLang, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbScratchLang.SelectedIndex = i;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(td.SampleCode)) _txtScratchCode.Text = td.SampleCode;
        }

        private void PopulateDefaultScratchForLang()
        {
            string k = _cmbScratchLang.Text;
            string mapId = k == "csharp" ? "csc" : (k == "rust" ? "rustc" : (k == "cpp" ? "clangpp" : (k == "msvc" ? "msvc_cl" : (k == "hlsl" ? "fxc" : k))));
            for (int i = 0; i < _tools.Count; i++)
            {
                if (_tools[i].Id == mapId && !string.IsNullOrEmpty(_tools[i].SampleCode))
                {
                    _txtScratchCode.Text = _tools[i].SampleCode;
                    return;
                }
            }
        }

        private async Task RunDirectScratchpadAsync()
        {
            string lang = _cmbScratchLang.Text;
            string code = _txtScratchCode.Text;
            _lblStatus.Text = "Compiling & running [" + lang + "] in Forge sandbox...";
            List<ToolDescriptor> snap = new List<ToolDescriptor>(_tools);
            string ws = _workspaceDir;
            ToolExecResult res = await Task.Run(delegate { return PolyglotSandboxRunner.CompileAndRunCode(ws, snap, lang, code, "", 20000); });
            _txtExecLog.Text = res.ToCompactReport() + "\r\n";
            string shaShort = (res.Sha256Receipt != null && res.Sha256Receipt.Length >= 12) ? res.Sha256Receipt.Substring(0, 12) : "n/a";
            _lblStatus.Text = "Scratchpad [" + lang + "] -> Exit " + res.ExitCode + " in " + res.ElapsedMs + " ms | SHA-256: " + shaShort;
        }

        private void PullLastHarvestedBlockToScratchpad()
        {
            List<HarvestedBlock> blocks = OmegaForgeHarvester.HarvestAllBlocks(_lastAssistantReply);
            if (blocks.Count == 0)
            {
                _lblStatus.Text = "No fenced code or Lean 4 theorems found in the last AI reply yet.";
                return;
            }
            HarvestedBlock b = blocks[0];
            for (int i = 0; i < _cmbScratchLang.Items.Count; i++)
            {
                if (string.Equals(_cmbScratchLang.Items[i].ToString(), b.Lang, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbScratchLang.SelectedIndex = i;
                    break;
                }
            }
            _txtScratchCode.Text = b.Code;
            _lblStatus.Text = "Loaded " + b.SummaryLabel + " into Right Scratchpad (press F5 to compile/verify).";
        }

        private async Task<int> HarvestAndVerifyTextAsync(string textToInspect, bool loadFirstIntoScratch)
        {
            List<HarvestedBlock> blocks = OmegaForgeHarvester.HarvestAllBlocks(textToInspect);
            if (blocks.Count == 0) return 0;

            if (loadFirstIntoScratch)
            {
                HarvestedBlock first = blocks[0];
                for (int i = 0; i < _cmbScratchLang.Items.Count; i++)
                {
                    if (string.Equals(_cmbScratchLang.Items[i].ToString(), first.Lang, StringComparison.OrdinalIgnoreCase))
                    {
                        _cmbScratchLang.SelectedIndex = i;
                        break;
                    }
                }
                _txtScratchCode.Text = first.Code;
            }

            List<ToolDescriptor> snapTools = new List<ToolDescriptor>(_tools);
            string ws = _workspaceDir;
            int verifiedOk = 0;
            StringBuilder logSb = new StringBuilder();

            for (int i = 0; i < blocks.Count; i++)
            {
                HarvestedBlock hb = blocks[i];
                _lblStatus.Text = "Auto-Verifying harvested block " + (i + 1) + "/" + blocks.Count + ": " + hb.SummaryLabel + "...";
                ToolExecResult vr = await Task.Run(delegate
                {
                    return PolyglotSandboxRunner.CompileAndRunCode(ws, snapTools, hb.Lang, hb.Code, "", 35000);
                });
                if (loadFirstIntoScratch && i == 0 && hb.Lang == "lean4" && !string.IsNullOrEmpty(vr.ArtifactPath) && File.Exists(vr.ArtifactPath))
                {
                    try { _txtScratchCode.Text = File.ReadAllText(vr.ArtifactPath, Encoding.UTF8); } catch { }
                }
                if (vr.Ok && !hb.HasSorry) verifiedOk++;
                string rep = "[HARVEST VERIFIER :: " + hb.SummaryLabel + "]\r\n" + vr.ToCompactReport();
                logSb.AppendLine(rep);
                logSb.AppendLine();
                _txtAgentChat.AppendText("   " + rep.Replace("\r\n", "\r\n   ") + "\r\n\r\n");
            }

            _txtExecLog.Text = logSb.ToString();
            return verifiedOk;
        }

        private void SendQuickPrompt(string promptText)
        {
            _txtUserPrompt.Text = promptText;
            Task ignored = RunGemmaTurnAsync();
        }

        private async Task RunGemmaTurnAsync()
        {
            if (_agentBusy) return;
            string userMsg = (_txtUserPrompt.Text ?? "").Trim();
            if (string.IsNullOrEmpty(userMsg)) return;

            _txtUserPrompt.Clear();
            _agentBusy = true;
            _cancelAgent = false;
            _btnSendGemma.Enabled = false;
            _btnStopGemma.Enabled = true;

            _txtAgentChat.AppendText(">>> [ARCHITECT] " + userMsg + "\r\n\r\n");
            _chatHistory.Add(new ChatTurnMessage("user", userMsg));

            string endpoint = _txtEndpoint.Text.Trim();
            string modelTag = _txtModelTag.Text.Trim();
            string ctxMode = _cmbContextMode.Text;
            int maxTok = (int)_nudMaxTokens.Value;
            double temp = (double)_nudTemp.Value;
            bool allowTools = _chkAutoReAct.Checked;
            bool autoHarvest = _chkAutoHarvestVerify.Checked;
            bool dualBrain = _chkDualBrain.Checked;
            List<ToolDescriptor> snapTools = new List<ToolDescriptor>(_tools);
            string ws = _workspaceDir;

            try
            {
                // Dual-Brain Pre-Flight: If Dual-Brain Tandem is ON and the prompt explicitly asks for live hardware CLI telemetry
                // (like nvidia_smi or adb), gather live ground-truth telemetry FIRST and feed it to the model!
                if (dualBrain)
                {
                    string qLow = userMsg.ToLowerInvariant();
                    if (qLow.Contains("nvidia") || qLow.Contains("rtx") || qLow.Contains("vram") || qLow.Contains("gpu"))
                    {
                        ToolExecResult gpuPre = await Task.Run(delegate { return PolyglotSandboxRunner.CompileAndRunCode(ws, snapTools, "nvidia_smi", "", "", 8000); });
                        if (gpuPre.Ok)
                        {
                            _txtExecLog.Text = gpuPre.ToCompactReport() + "\r\n";
                            _txtAgentChat.AppendText("   [DUAL-BRAIN PRE-PROBE :: nvidia_smi]\r\n   " + gpuPre.Stdout.Trim().Replace("\r\n", "\r\n   ") + "\r\n\r\n");
                            _chatHistory.Add(new ChatTurnMessage("user", "[Live nvidia-smi hardware telemetry captured on local workstation]:\r\n" + gpuPre.Stdout.Trim()));
                        }
                    }
                    else if (qLow.Contains("adb") || qLow.Contains("pixel"))
                    {
                        ToolExecResult adbPre = await Task.Run(delegate { return PolyglotSandboxRunner.CompileAndRunCode(ws, snapTools, "adb", "devices -l", "", 6000); });
                        if (adbPre.Ok)
                        {
                            _txtExecLog.Text = adbPre.ToCompactReport() + "\r\n";
                            _txtAgentChat.AppendText("   [DUAL-BRAIN PRE-PROBE :: adb]\r\n   " + adbPre.Stdout.Trim().Replace("\r\n", "\r\n   ") + "\r\n\r\n");
                            _chatHistory.Add(new ChatTurnMessage("user", "[Live Dual-Pixel ADB mesh telemetry]:\r\n" + adbPre.Stdout.Trim()));
                        }
                    }

                    // If an Ollama model is live on :11434 while primary is :8097 (or vice versa), let :11434 handle general chat/summaries if :8097 is selected for non-proof prompts
                    bool isProofOrKernel = qLow.Contains("lean") || qLow.Contains("theorem") || qLow.Contains("rust") || qLow.Contains("hlsl") || qLow.Contains("e8") || qLow.Contains("sh4z");
                    if (!isProofOrKernel && endpoint.Contains(":8097") && OmegaForgeScanner.ProbeTcpPort(11434, 100))
                    {
                        for (int m = 0; m < _models.Count; m++)
                        {
                            if (_models[m].Kind == "Ollama-Live")
                            {
                                endpoint = _models[m].EndpointUrl;
                                modelTag = _models[m].ModelTag;
                                _txtAgentChat.AppendText("   [DUAL-BRAIN ROUTER] Routed conversational/telemetry query -> Ollama (:11434 :: " + modelTag + ")\r\n\r\n");
                                break;
                            }
                        }
                    }
                }

                int maxSteps = allowTools ? 4 : 1;
                int totalVerified = 0;

                for (int step = 0; step < maxSteps && !_cancelAgent; step++)
                {
                    string routeInfo = ctxMode;
                    List<ToolDescriptor> promptTools = snapTools;
                    if (ctxMode.StartsWith("Surgical", StringComparison.OrdinalIgnoreCase))
                        promptTools = SurgicalIntentRouter.SelectSurgicalToolsForPrompt(snapTools, userMsg, out routeInfo);

                    _lblStatus.Text = "Streaming from " + modelTag + " [" + routeInfo + " | Step " + (step + 1) + "/" + maxSteps + "]...";

                    List<ChatTurnMessage> reqMsgs = new List<ChatTurnMessage>();
                    string sysPrompt = LocalGemmaAgentEngine.BuildScopedSystemPrompt(promptTools, ws, ctxMode);
                    if (!string.IsNullOrEmpty(sysPrompt))
                        reqMsgs.Add(new ChatTurnMessage("system", sysPrompt));

                    // Keep context window crisp: in Surgical mode on :8097, avoid dragging giant 2,000-token past theorem dumps into unrelated new turns!
                    int maxHist = endpoint.Contains(":8097") ? 4 : 10;
                    int startHist = Math.Max(0, _chatHistory.Count - maxHist);
                    for (int h = startHist; h < _chatHistory.Count; h++)
                    {
                        string hc = _chatHistory[h].Content ?? "";
                        if (endpoint.Contains(":8097") && hc.Length > 1400 && h < _chatHistory.Count - 1)
                            hc = hc.Substring(0, 1400) + "\r\n...[prior long output condensed]";
                        reqMsgs.Add(new ChatTurnMessage(_chatHistory[h].Role, hc));
                    }

                    _txtAgentChat.AppendText("[GEMMA :: " + modelTag + " | " + routeInfo + "]\r\n");
                    int streamStartLen = _txtAgentChat.TextLength;
                    StringBuilder uiChunkBuffer = new StringBuilder();
                    object chunkLock = new object();

                    StreamTelemetry turnStats = new StreamTelemetry();
                    string reply = await Task.Run(delegate
                    {
                        StreamTelemetry stOut;
                        string full = LocalGemmaAgentEngine.SendChatRequestStreaming(
                            endpoint,
                            modelTag,
                            reqMsgs,
                            maxTok,
                            temp,
                            120000,
                            delegate { return _cancelAgent; },
                            delegate(string deltaPiece, StreamTelemetry liveSt)
                            {
                                lock (chunkLock) { uiChunkBuffer.Append(deltaPiece); }
                                if (!IsDisposed)
                                {
                                    try
                                    {
                                        BeginInvoke((Action)delegate
                                        {
                                            string drain;
                                            lock (chunkLock) { drain = uiChunkBuffer.ToString(); uiChunkBuffer.Clear(); }
                                            if (drain.Length > 0) _txtAgentChat.AppendText(drain.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                                            _lblStreamHud.Text = "Stream: LIVE  |  Speed: " + (liveSt.TokensPerSec >= 1000.0 ? (liveSt.TokensPerSec / 1000.0).ToString("0.0") + "k" : liveSt.TokensPerSec.ToString("0.0")) + " tok/s | ~" + liveSt.ApproxTokens + " tok | 4-Gram: " + liveSt.Unique4GramPct.ToString("0.0") + "% | Verified: " + totalVerified;
                                        });
                                    }
                                    catch { }
                                }
                            },
                            out stOut);
                        turnStats = stOut;
                        return full;
                    });

                    // Flush any final buffered characters or non-streamed full reply
                    string leftover;
                    lock (chunkLock) { leftover = uiChunkBuffer.ToString(); uiChunkBuffer.Clear(); }
                    if (turnStats.WasStreaming)
                    {
                        if (leftover.Length > 0) _txtAgentChat.AppendText(leftover.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                        if (turnStats.Aborted) _txtAgentChat.AppendText("\r\n[STREAM STOPPED BY USER]");
                        _txtAgentChat.AppendText("\r\n\r\n");
                    }
                    else if (_txtAgentChat.TextLength == streamStartLen)
                    {
                        _txtAgentChat.AppendText((reply ?? "").Trim() + "\r\n\r\n");
                    }

                    _lastAssistantReply = reply ?? "";
                    _chatHistory.Add(new ChatTurnMessage("assistant", _lastAssistantReply));

                    string loopTag = turnStats.Unique4GramPct >= 92.0 ? "Zero-Loop Coherent" : "Repetitive";
                    _lblStreamHud.Text = "Stream: " + (turnStats.Aborted ? "STOPPED" : "DONE") +
                        " | " + (turnStats.TokensPerSec >= 1000.0 ? (turnStats.TokensPerSec / 1000.0).ToString("0.0") + "k" : turnStats.TokensPerSec.ToString("0.0")) + " tok/s | ~" + turnStats.ApproxTokens + " tok | 4-Gram: " + turnStats.Unique4GramPct.ToString("0.0") + "% (" + loopTag + ") | Verified: " + totalVerified;

                    if (_cancelAgent || turnStats.Aborted) break;

                    // 1. Check explicit <tool_call> JSON first
                    string act, langOrTool, codeOrContent, argsOrPath;
                    if (allowTools && LocalGemmaAgentEngine.TryExtractToolCall(_lastAssistantReply, out act, out langOrTool, out codeOrContent, out argsOrPath))
                    {
                        _lblStatus.Text = "Executing Gemma tool call -> " + act + " (" + langOrTool + argsOrPath + ")...";
                        ToolExecResult execRes = await Task.Run(delegate
                        {
                            if (act == "compile_run")
                                return PolyglotSandboxRunner.CompileAndRunCode(ws, snapTools, langOrTool, codeOrContent, argsOrPath, 20000);
                            if (act == "exec")
                                return PolyglotSandboxRunner.CompileAndRunCode(ws, snapTools, langOrTool, "", argsOrPath, 15000);
                            return PolyglotSandboxRunner.ExecuteVirtualTool(ws, snapTools, act, argsOrPath, codeOrContent);
                        });

                        if (execRes.Ok) totalVerified++;
                        string report = execRes.ToCompactReport();
                        _txtExecLog.Text = report + "\r\n";
                        _txtAgentChat.AppendText("   " + report.Replace("\r\n", "\r\n   ") + "\r\n\r\n");
                        _chatHistory.Add(new ChatTurnMessage("user", "<tool_result>\r\n" + report + "\r\n</tool_result>"));
                        continue;
                    }

                    // 2. Otherwise, if Auto-Verify Fences & Lean 4 is enabled, harvest fenced code or continuous Lean 4 proofs and compile/verify live!
                    if (autoHarvest)
                    {
                        int newlyVerified = await HarvestAndVerifyTextAsync(_lastAssistantReply, true);
                        totalVerified += newlyVerified;
                        _lblStreamHud.Text = "Stream: DONE  |  Speed: " + turnStats.TokensPerSec.ToString("0.0") + " tok/s  |  Tokens: ~" + turnStats.ApproxTokens +
                            "  |  Unique 4-Gram: " + turnStats.Unique4GramPct.ToString("0.0") + "% (" + loopTag + ")  |  Verified: " + totalVerified;
                    }
                    break;
                }

                _lblStatus.Text = "Turn complete (Verified blocks: " + totalVerified + ").";
            }
            catch (Exception ex)
            {
                _txtAgentChat.AppendText("\r\n[LOCAL ENDPOINT NOTICE] Could not reach " + endpoint + " (" + ex.Message + ").\r\n" +
                    "Quick Fix: Pick a [Live] model in the AI Model dropdown above (such as gemma-4-12B-it-sh4z on :8097), or click [Wake Ollama (:11434)]!\r\n\r\n");
                _lblStatus.Text = "Endpoint offline (" + endpoint + ") — select a Live :8097 model or click [Wake Ollama (:11434)].";
            }
            finally
            {
                _agentBusy = false;
                _btnSendGemma.Enabled = true;
                _btnStopGemma.Enabled = false;
            }
        }

        private void ShowForgeQuickGuideDialog()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "OmegaForge v0.2.1 — Human Quick-Start Guide";
                dlg.Size = new Size(760, 560);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = Color.FromArgb(10, 15, 28);
                dlg.ForeColor = Color.White;

                TextBox tb = new TextBox();
                tb.Dock = DockStyle.Fill;
                tb.Multiline = true;
                tb.ReadOnly = true;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.BackColor = Color.FromArgb(10, 15, 28);
                tb.ForeColor = Color.FromArgb(226, 232, 240);
                tb.Font = new Font("Segoe UI", 9.6f);
                tb.BorderStyle = BorderStyle.None;
                tb.Text =
                    "WELCOME TO OMEGAFORGE v0.2.1 (PLAIN-ENGLISH GUIDE)\r\n" +
                    "====================================================================\r\n\r\n" +
                    "1. PICKING YOUR AI MODEL (TOP ROW 2)\r\n" +
                    "   - Live ready models appear at the very top of the AI Model dropdown.\r\n" +
                    "   - Sovereign :8097 (gemma-4-12B-it-sh4z / E4B): Specialized in formal Lean 4 theorems, Rust L0 kernels, and mathematical specs.\r\n" +
                    "   - Ollama (:11434): Click [Wake Ollama (:11434)] anytime to start your local Ollama engine for general chat & CLI tool commands.\r\n\r\n" +
                    "2. CONTEXT GATE MODES (TOP ROW 3)\r\n" +
                    "   - Surgical Intent (1-3 Tools) [Recommended]: Sends ONLY the 1 to 3 tools matching your question so :8097 stays laser-focused.\r\n" +
                    "   - Raw Model (Zero System): Sends your exact text with zero system wrapper (great for pure raw model testing).\r\n" +
                    "   - Full Armed Matrix: Injects all checked tools on the left into the system prompt.\r\n\r\n" +
                    "3. LIVE STREAMING, STOP STREAM & 4-GRAM METER\r\n" +
                    "   - Replies stream live token-by-token. Click [Stop Stream] at any moment to halt immediately.\r\n" +
                    "   - Unique 4-Gram % measures whether the model is generating fresh non-repeating content (95-100% = zero repetition).\r\n\r\n" +
                    "4. AUTO-VERIFY FENCES & LEAN 4 PROOFS\r\n" +
                    "   - Whenever the AI outputs fenced code (Rust, C#, C++, HLSL, DXC, Python) OR a continuous Lean 4 theorem stream, OmegaForge automatically reflows it, loads it into the Right Scratchpad, and runs your real compiler (lean.exe, rustc.exe, csc.exe, dxc.exe) to prove it compiles with zero errors!\r\n" +
                    "   - You can also edit any code in the Right Scratchpad and press F5 to compile & run directly (even with no AI running!).\r\n\r\n" +
                    "Tip: Open the main ACT-Omega Lab Launcher and click [? Help & FAQ Manual (F6)] for the complete step-by-step handbook covering Browser, Mirror, OmegaAV, and OmegaForge!";
                tb.SelectionStart = 0;
                tb.SelectionLength = 0;

                Panel pad = new Panel();
                pad.Dock = DockStyle.Fill;
                pad.Padding = new Padding(16);
                pad.Controls.Add(tb);
                dlg.Controls.Add(pad);
                dlg.ShowDialog(this);
            }
        }

        private void OpenRegisterCustomToolModal()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "Register Custom Compiler, SDK, or CLI Tool";
                dlg.ClientSize = new Size(520, 220);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.BackColor = Color.FromArgb(12, 18, 32); dlg.ForeColor = Color.White;

                Label l1 = new Label(); l1.Text = "Tool ID (short slug):"; l1.SetBounds(16, 18, 150, 20);
                TextBox tId = new TextBox(); tId.SetBounds(176, 16, 320, 24); tId.Text = "custom_tool";

                Label l2 = new Label(); l2.Text = "Display Name:"; l2.SetBounds(16, 54, 150, 20);
                TextBox tNm = new TextBox(); tNm.SetBounds(176, 52, 320, 24); tNm.Text = "Custom Sovereign Binary";

                Label l3 = new Label(); l3.Text = "Executable Path:"; l3.SetBounds(16, 90, 150, 20);
                TextBox tEx = new TextBox(); tEx.SetBounds(176, 88, 248, 24);
                Button bBrowse = MakeBtn("Browse", Color.FromArgb(30, 58, 138)); bBrowse.SetBounds(430, 86, 66, 26);
                bBrowse.Click += delegate
                {
                    using (OpenFileDialog ofd = new OpenFileDialog())
                    {
                        if (ofd.ShowDialog(dlg) == DialogResult.OK) tEx.Text = ofd.FileName;
                    }
                };

                Label l4 = new Label(); l4.Text = "Version Flag:"; l4.SetBounds(16, 126, 150, 20);
                TextBox tVf = new TextBox(); tVf.SetBounds(176, 124, 320, 24); tVf.Text = "--version";

                Button bAdd = MakeBtn("Register & Enable for Gemma", Color.FromArgb(5, 150, 105)); bAdd.SetBounds(176, 168, 210, 32);
                bAdd.Click += delegate
                {
                    if (string.IsNullOrWhiteSpace(tId.Text) || !File.Exists(tEx.Text.Trim())) return;
                    ToolDescriptor nt = new ToolDescriptor();
                    nt.Id = tId.Text.Trim(); nt.Name = tNm.Text.Trim(); nt.Category = "Edge & Systems";
                    nt.ExecutablePath = tEx.Text.Trim(); nt.VersionArgs = tVf.Text.Trim();
                    nt.Found = true; nt.Enabled = true; nt.VersionBanner = "Custom Registered";
                    _tools.Add(nt);
                    SaveToolSelectionProfile();
                    RefreshToolChecklist();
                    UpdateHeaderCounts();
                    dlg.Close();
                };

                dlg.Controls.AddRange(new Control[] { l1, tId, l2, tNm, l3, tEx, bBrowse, l4, tVf, bAdd });
                dlg.ShowDialog(this);
            }
        }

        private void SaveToolSelectionProfile()
        {
            try
            {
                List<string> lines = new List<string>();
                for (int i = 0; i < _tools.Count; i++)
                {
                    ToolDescriptor t = _tools[i];
                    bool en = t.Enabled && (t.Found || t.IsBuiltInVirtual);
                    lines.Add(t.Id + "\t" + (en ? "1" : "0") + "\t" + t.ExecutablePath + "\t" + t.Name + "\t" + t.Category);
                }
                File.WriteAllLines(_profilePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        private void LoadSavedToolProfile(List<ToolDescriptor> scanned)
        {
            if (!File.Exists(_profilePath)) return;
            try
            {
                Dictionary<string, bool> enMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                string[] lines = File.ReadAllLines(_profilePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 2) continue;
                    enMap[p[0]] = (p[1] == "1");
                    bool existsInScan = false;
                    for (int j = 0; j < scanned.Count; j++) if (scanned[j].Id == p[0]) { existsInScan = true; break; }
                    if (!existsInScan && p.Length >= 5 && File.Exists(p[2]))
                    {
                        ToolDescriptor custom = new ToolDescriptor();
                        custom.Id = p[0]; custom.Enabled = (p[1] == "1"); custom.ExecutablePath = p[2];
                        custom.Name = p[3]; custom.Category = p[4]; custom.Found = true; custom.VersionBanner = "Custom Saved";
                        scanned.Add(custom);
                    }
                }
                for (int i = 0; i < scanned.Count; i++)
                {
                    if (!scanned[i].Found && !scanned[i].IsBuiltInVirtual)
                    {
                        scanned[i].Enabled = false;
                        continue;
                    }
                    bool savedEn;
                    if (enMap.TryGetValue(scanned[i].Id, out savedEn))
                        scanned[i].Enabled = savedEn;
                }
            }
            catch { }
        }

        public string BuildStatusJson()
        {
            int found = 0, en = 0;
            for (int i = 0; i < _tools.Count; i++)
            {
                if (_tools[i].Found || _tools[i].IsBuiltInVirtual) found++;
                if (_tools[i].Enabled && (_tools[i].Found || _tools[i].IsBuiltInVirtual)) en++;
            }
            return "{\"ok\":true,\"service\":\"OmegaForge\",\"version\":\"0.2\",\"port\":19780,\"tools_found\":" + found + ",\"tools_enabled\":" + en + ",\"models_detected\":" + _models.Count + "}";
        }

        public string BuildToolsJson()
        {
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"tools\":[");
            for (int i = 0; i < _tools.Count; i++)
            {
                ToolDescriptor t = _tools[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"id\":\"" + ForgeJson.Esc(t.Id) + "\",\"name\":\"" + ForgeJson.Esc(t.Name) + "\",\"category\":\"" + ForgeJson.Esc(t.Category) + "\",\"found\":" + ((t.Found || t.IsBuiltInVirtual) ? "true" : "false") + ",\"enabled\":" + ((t.Enabled && (t.Found || t.IsBuiltInVirtual)) ? "true" : "false") + ",\"path\":\"" + ForgeJson.Esc(t.ExecutablePath) + "\",\"version\":\"" + ForgeJson.Esc(t.VersionBanner) + "\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public string BuildModelsJson()
        {
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"models\":[");
            for (int i = 0; i < _models.Count; i++)
            {
                AiModelDescriptor m = _models[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"id\":\"" + ForgeJson.Esc(m.Id) + "\",\"name\":\"" + ForgeJson.Esc(m.DisplayName) + "\",\"kind\":\"" + ForgeJson.Esc(m.Kind) + "\",\"gemma\":" + (m.IsGemma ? "true" : "false") + ",\"live\":" + (m.IsLiveServer ? "true" : "false") + ",\"endpoint\":\"" + ForgeJson.Esc(m.EndpointUrl) + "\",\"path\":\"" + ForgeJson.Esc(m.FilePath) + "\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public string ExecuteApiRequestJson(string bodyJson)
        {
            string lang = ForgeJson.ExtractStringField(bodyJson, "lang");
            string code = ForgeJson.ExtractStringField(bodyJson, "code");
            string args = ForgeJson.ExtractStringField(bodyJson, "args");
            List<ToolDescriptor> snap = new List<ToolDescriptor>(_tools);
            ToolExecResult r = PolyglotSandboxRunner.CompileAndRunCode(_workspaceDir, snap, lang, code, args, 20000);
            return "{\"ok\":" + (r.Ok ? "true" : "false") + ",\"exit_code\":" + r.ExitCode + ",\"elapsed_ms\":" + r.ElapsedMs + ",\"sha256\":\"" + ForgeJson.Esc(r.Sha256Receipt) + "\",\"stdout\":\"" + ForgeJson.Esc(r.Stdout) + "\",\"stderr\":\"" + ForgeJson.Esc(r.Stderr) + "\"}";
        }

        public string VerifyOutputApiJson(string bodyJson)
        {
            string text = ForgeJson.ExtractStringField(bodyJson, "text");
            List<HarvestedBlock> blocks = OmegaForgeHarvester.HarvestAllBlocks(text);
            List<ToolDescriptor> snap = new List<ToolDescriptor>(_tools);
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"blocks_found\":" + blocks.Count + ",\"results\":[");
            for (int i = 0; i < blocks.Count; i++)
            {
                HarvestedBlock hb = blocks[i];
                ToolExecResult r = PolyglotSandboxRunner.CompileAndRunCode(_workspaceDir, snap, hb.Lang, hb.Code, "", 22000);
                if (i > 0) sb.Append(",");
                sb.Append("{\"lang\":\"" + ForgeJson.Esc(hb.Lang) + "\",\"kind\":\"" + ForgeJson.Esc(hb.Kind) + "\",\"theorems\":" + hb.TheoremCount + ",\"has_sorry\":" + (hb.HasSorry ? "true" : "false") + ",\"ok\":" + (r.Ok ? "true" : "false") + ",\"exit_code\":" + r.ExitCode + ",\"elapsed_ms\":" + r.ElapsedMs + ",\"sha256\":\"" + ForgeJson.Esc(r.Sha256Receipt) + "\",\"stderr\":\"" + ForgeJson.Esc(r.Stderr) + "\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
 
        private void SealScratchpadViaOmegaSec()
        {
            string code = _txtScratchCode.Text;
            ThreadPoolShimSeal(code);
        }

        private void ThreadPoolShimSeal(string code)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string resMsg;
                try
                {
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        string json = wc.UploadString("http://127.0.0.1:19786/api/sec/seal?station=forge&layers=31", "POST", code ?? "");
                        string wsDir = Path.Combine(_forgeRoot, "workspace");
                        Directory.CreateDirectory(wsDir);
                        string outP = Path.Combine(wsDir, "forge_sealed_sh4z_" + DateTime.Now.ToString("HHmmss") + ".json");
                        File.WriteAllText(outP, json, new UTF8Encoding(false));
                        resMsg = "[OMEGASEC :19786] Sealed verified code/proof into SH4Z-SEC1 capsule -> " + outP;
                    }
                }
                catch
                {
                    resMsg = "[OMEGASEC :19786] Offline — launch OmegaSteg v0.2 (Shift+F8) to seal Forge artifacts!";
                }
                if (!IsDisposed) BeginInvoke((Action)delegate
                {
                    _txtExecLog.AppendText("\r\n" + resMsg + "\r\n");
                    _lblStatus.Text = resMsg;
                });
            });
        }
    }
}
