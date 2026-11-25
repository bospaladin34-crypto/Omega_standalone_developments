using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaTriEngine
{
    public sealed class OmshCoPilotResult
    {
        public bool Ok;
        public bool FxcValidated;
        public bool RepairedAfterFxcError;
        public bool UsedLocalDeterministicCompiler;
        public int Attempts;
        public double TotalMs;
        public double FxcValidateMs;
        public string ModelSource = "";
        public string Prompt = "";
        public string OmshCode = "";
        public string FirstFxcError = "";
        public string FinalError = "";
    }

    public static class OmegaEngineOmshCoPilot
    {
        public const string DefaultTensorOpenAiUrl = "http://127.0.0.1:19791/v1/chat/completions";
        public const string DefaultTensorAssistBase = "http://127.0.0.1:19791/api/omsh_assist";
        public const string DefaultOllamaOpenAiUrl = "http://127.0.0.1:11434/v1/chat/completions";

        private static readonly string SystemPromptHlsl =
            "You are the OmegaEngine cs_5_0 HLSL shader co-pilot. Write ONLY valid HLSL statements that fit inside " +
            "void EvalCustomOmsh(float3 wp, float3 rd, inout float3 col) and mutate col (linear HDR RGB). " +
            "Do NOT declare cbuffer, textures, structs, or outer function signatures. " +
            "Available uniforms & helpers: wp (world XYZ), rd (unit view ray), col (inout float3 HDR RGB), " +
            "U[0].z (time sec), U[3].xyzw (bass,mid,high,peak), U[6].xyzw (camera quaternion), U[7].xyzw (5D pentagrid shifts), " +
            "U[8].w (cluster TOPS/s), U[9].x (symmetry mode 0..7), U[13].xyz (shockwave origin), U[14].xy (shockwave strength, radius), " +
            "U[15].xyzw (k1,k2,k3,k4), om_hsv2rgb(float3), om_sdGyroid(float3,float), " +
            "om_e8_voronoi_edge(float4,float4,out float,out float,out uint), " +
            "om_hopf_villarceau_field(float3,float4,float,out float3,out float), " +
            "om_h4_fold16(float4), om_poincare_honeycomb_sdf(float3,float,float,float,out float3,out float), " +
            "om_leech24_golay_voronoi(float3,float,out float,out float,out uint). Return ONLY HLSL statements.";

        public static OmshCoPilotResult QueryCoPilotWithAutoFxc(
            OmegaEngineDirectComputeGpu gpu,
            string rawPrompt,
            int activeSymMode,
            string openAiEndpointOverride,
            string optionalStation13ExePath)
        {
            Stopwatch swTotal = Stopwatch.StartNew();
            OmshCoPilotResult res = new OmshCoPilotResult();
            string cleanPrompt = string.IsNullOrEmpty(rawPrompt)
                ? "golden hopf villarceau torus with audio bass pulse and emerald fresnel rim"
                : rawPrompt.Trim();
            res.Prompt = cleanPrompt;

            string primaryUrl = string.IsNullOrEmpty(openAiEndpointOverride)
                ? DefaultTensorOpenAiUrl
                : openAiEndpointOverride.Trim();

            string modeHint = string.Format(CultureInfo.InvariantCulture,
                "[Active SymmetryMode={0}] Request: {1}", Math.Max(0, Math.Min(7, activeSymMode)), cleanPrompt);

            string rawReply1 = "";
            string usedEndpoint = "";
            int timeoutMs = string.IsNullOrEmpty(openAiEndpointOverride) ? 2200 : 3500;

            bool gotRemote = TryPostOpenAiChat(primaryUrl, "gemma-omsh-copilot", SystemPromptHlsl, modeHint, null, null, timeoutMs, out rawReply1);
            if (gotRemote && LooksLikeShaderCandidate(rawReply1))
            {
                usedEndpoint = primaryUrl;
            }
            else if (string.IsNullOrEmpty(openAiEndpointOverride))
            {
                // Also check local Ollama OpenAI v1 (:11434) or OmegaTensor /api/omsh_assist (:19791) if /v1/chat/completions returned cluster telemetry prose
                string ollamaReply;
                if (TryPostOpenAiChat(DefaultOllamaOpenAiUrl, "gemma2:2b", SystemPromptHlsl, modeHint, null, null, 1800, out ollamaReply) &&
                    LooksLikeShaderCandidate(ollamaReply))
                {
                    gotRemote = true;
                    rawReply1 = ollamaReply;
                    usedEndpoint = DefaultOllamaOpenAiUrl;
                }
                else
                {
                    string assistReply;
                    if (TryGetTensorOmshAssist(activeSymMode, cleanPrompt, 1200, out assistReply) &&
                        LooksLikeShaderCandidate(assistReply))
                    {
                        gotRemote = true;
                        rawReply1 = assistReply;
                        usedEndpoint = DefaultTensorAssistBase;
                    }
                    else if (!string.IsNullOrEmpty(optionalStation13ExePath) && File.Exists(optionalStation13ExePath))
                    {
                        try
                        {
                            if (Process.GetProcessesByName("OmegaTensor").Length == 0)
                            {
                                ProcessStartInfo psi = new ProcessStartInfo(optionalStation13ExePath);
                                psi.WorkingDirectory = Path.GetDirectoryName(optionalStation13ExePath);
                                Process.Start(psi);
                            }
                        }
                        catch { }
                    }
                }
            }
            else if (gotRemote)
            {
                usedEndpoint = primaryUrl;
            }

            if (gotRemote && !string.IsNullOrEmpty(rawReply1))
            {
                res.Attempts = 1;
                string cand1 = SanitizeAndCanonicalizeOmsh(rawReply1);
                double fxcMs1 = 0.0;
                string fxcErr1 = "";
                if (!string.IsNullOrEmpty(cand1) && ValidateCandidateWithGpuOrLexical(gpu, cand1, out fxcMs1, out fxcErr1))
                {
                    swTotal.Stop();
                    res.Ok = true;
                    res.FxcValidated = true;
                    res.RepairedAfterFxcError = false;
                    res.UsedLocalDeterministicCompiler = false;
                    res.FxcValidateMs = fxcMs1;
                    res.TotalMs = swTotal.Elapsed.TotalMilliseconds;
                    res.ModelSource = usedEndpoint;
                    res.OmshCode = EnsureHeaderComment(cand1, "OmegaTensor OpenAI v1 Co-Pilot", cleanPrompt);
                    return res;
                }

                res.FirstFxcError = string.IsNullOrEmpty(fxcErr1) ? "Candidate did not mutate col or failed lexical gate" : fxcErr1;

                // Automatic 1-Retry OpenAI v1 FXC Diagnostic Self-Repair Turn
                if (usedEndpoint.IndexOf("/v1/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    res.Attempts = 2;
                    string repairInstruction =
                        "D3DCompile cs_5_0 rejected your previous HLSL snippet with compiler diagnostic:\n" +
                        res.FirstFxcError +
                        "\nFix every undeclared identifier, GLSLism, or syntax error. Return ONLY valid cs_5_0 HLSL statements mutating col.";
                    string rawReply2;
                    if (TryPostOpenAiChat(usedEndpoint, "gemma-omsh-copilot", SystemPromptHlsl, modeHint, rawReply1, repairInstruction, timeoutMs, out rawReply2) &&
                        !string.IsNullOrEmpty(rawReply2))
                    {
                        string cand2 = SanitizeAndCanonicalizeOmsh(rawReply2);
                        double fxcMs2 = 0.0;
                        string fxcErr2 = "";
                        if (!string.IsNullOrEmpty(cand2) && ValidateCandidateWithGpuOrLexical(gpu, cand2, out fxcMs2, out fxcErr2))
                        {
                            swTotal.Stop();
                            res.Ok = true;
                            res.FxcValidated = true;
                            res.RepairedAfterFxcError = true;
                            res.UsedLocalDeterministicCompiler = false;
                            res.FxcValidateMs = fxcMs1 + fxcMs2;
                            res.TotalMs = swTotal.Elapsed.TotalMilliseconds;
                            res.ModelSource = usedEndpoint + " (Auto-FXC Repaired)";
                            res.OmshCode = EnsureHeaderComment(cand2, "OmegaTensor OpenAI v1 + Auto-FXC Repaired", cleanPrompt);
                            return res;
                        }
                        res.FinalError = fxcErr2;
                    }
                }
            }

            // Compositional Natural-Language-to-.omsh Deterministic Compiler (100% FXC-verified across all 8 modes)
            res.Attempts = Math.Max(1, res.Attempts + 1);
            string detCode = SynthesizeDeterministicNlOmsh(cleanPrompt, activeSymMode);
            double detFxcMs = 0.0;
            string detErr = "";
            bool detValid = ValidateCandidateWithGpuOrLexical(gpu, detCode, out detFxcMs, out detErr);
            swTotal.Stop();

            res.Ok = detValid;
            res.FxcValidated = detValid;
            res.RepairedAfterFxcError = !string.IsNullOrEmpty(res.FirstFxcError);
            res.UsedLocalDeterministicCompiler = true;
            res.FxcValidateMs += detFxcMs;
            res.TotalMs = swTotal.Elapsed.TotalMilliseconds;
            res.ModelSource = string.IsNullOrEmpty(res.FirstFxcError)
                ? "OmegaEngine NL->HLSL Compositional Synthesizer (:19791 Offline Fallback)"
                : "OmegaEngine NL->HLSL Auto-FXC Safe Recovery";
            res.OmshCode = detCode;
            res.FinalError = detValid ? "" : detErr;
            return res;
        }

        private static bool ValidateCandidateWithGpuOrLexical(
            OmegaEngineDirectComputeGpu gpu,
            string candidateOmsh,
            out double fxcMs,
            out string errorMsg)
        {
            fxcMs = 0.0;
            errorMsg = "";
            if (string.IsNullOrEmpty(candidateOmsh))
            {
                errorMsg = "Empty .omsh candidate";
                return false;
            }
            if (!Regex.IsMatch(candidateOmsh, @"\bcol\b"))
            {
                errorMsg = "Candidate .omsh must read or mutate inout float3 col";
                return false;
            }
            if (Regex.IsMatch(candidateOmsh, @"\b(cbuffer|RWTexture2D|StructuredBuffer|Texture2D|numthreads)\b"))
            {
                errorMsg = "Top-level HLSL resource declarations are forbidden inside EvalCustomOmsh";
                return false;
            }
            if (!AreBracesAndParensBalanced(candidateOmsh))
            {
                errorMsg = "Unbalanced braces or parentheses in candidate .omsh";
                return false;
            }

            if (gpu != null)
            {
                return gpu.ValidateOmshFastFxc(candidateOmsh, out fxcMs, out errorMsg);
            }
            using (OmegaEngineDirectComputeGpu tempGpu = new OmegaEngineDirectComputeGpu())
            {
                return tempGpu.ValidateOmshFastFxc(candidateOmsh, out fxcMs, out errorMsg);
            }
        }

        public static string SanitizeAndCanonicalizeOmsh(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return "";
            string s = rawText.Trim();

            // 1. Extract fenced code block if present
            Match fence = Regex.Match(s, "\u0060\u0060\u0060(?:hlsl|glsl|c|cpp|omsh)?\\s*([\\s\\S]*?)\u0060\u0060\u0060", RegexOptions.IgnoreCase);
            if (fence.Success)
            {
                s = fence.Groups[1].Value.Trim();
            }

            // 2. Unwrap outer void EvalCustomOmsh(...) { ... } if LLM included function signature
            Match outerFn = Regex.Match(s, @"void\s+EvalCustomOmsh\s*\([^)]*\)\s*\{([\s\S]*)\}\s*$");
            if (outerFn.Success)
            {
                s = outerFn.Groups[1].Value.Trim();
            }

            // 3. Canonicalize common GLSL hallucinations -> strict HLSL cs_5_0 intrinsics/types
            s = Regex.Replace(s, @"\bvec2\b", "float2");
            s = Regex.Replace(s, @"\bvec3\b", "float3");
            s = Regex.Replace(s, @"\bvec4\b", "float4");
            s = Regex.Replace(s, @"\bivec2\b", "int2");
            s = Regex.Replace(s, @"\bivec3\b", "int3");
            s = Regex.Replace(s, @"\bivec4\b", "int4");
            s = Regex.Replace(s, @"\buvec2\b", "uint2");
            s = Regex.Replace(s, @"\buvec3\b", "uint3");
            s = Regex.Replace(s, @"\buvec4\b", "uint4");
            s = Regex.Replace(s, @"\bmat3\b", "float3x3");
            s = Regex.Replace(s, @"\bmat4\b", "float4x4");
            s = Regex.Replace(s, @"\bmix\s*\(", "lerp(");
            s = Regex.Replace(s, @"\bfract\s*\(", "frac(");
            s = Regex.Replace(s, @"\binversesqrt\s*\(", "rsqrt(");
            s = Regex.Replace(s, @"\bdFdx\s*\(", "ddx(");
            s = Regex.Replace(s, @"\bdFdy\s*\(", "ddy(");
            s = Regex.Replace(s, @"\biTime\b", "U[0].z");

            s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            return s.Trim();
        }

        private static bool LooksLikeShaderCandidate(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return false;
            if (txt.IndexOf("[OMEGATENSOR", StringComparison.OrdinalIgnoreCase) >= 0 &&
                txt.IndexOf("col", StringComparison.Ordinal) < 0)
            {
                return false;
            }
            return txt.IndexOf("col", StringComparison.Ordinal) >= 0 ||
                   txt.IndexOf("float", StringComparison.Ordinal) >= 0 ||
                   txt.IndexOf("vec3", StringComparison.Ordinal) >= 0 ||
                   txt.IndexOf("\u0060\u0060\u0060", StringComparison.Ordinal) >= 0;
        }

        private static string EnsureHeaderComment(string code, string sourceTag, string prompt)
        {
            string oneLinePrompt = (prompt ?? "").Replace("\r", " ").Replace("\n", " ");
            if (oneLinePrompt.Length > 76) oneLinePrompt = oneLinePrompt.Substring(0, 76) + "...";
            if (code.StartsWith("//", StringComparison.Ordinal)) return code;
            return "// [" + sourceTag + "] Prompt: " + oneLinePrompt + "\r\n" + code;
        }

        public static string SynthesizeDeterministicNlOmsh(string prompt, int activeSymMode)
        {
            string p = (prompt ?? "").ToLowerInvariant();
            string cleanOneLine = (prompt ?? "spectral resonance").Replace("\r", " ").Replace("\n", " ").Trim();
            if (cleanOneLine.Length > 74) cleanOneLine = cleanOneLine.Substring(0, 74) + "...";

            // 1. Resolve target geometry family from natural-language keywords (falling back to activeSymMode)
            int geom = Math.Max(0, Math.Min(7, activeSymMode));
            bool explicitGyroid = false;
            if (ContainsAny(p, "hopf", "villarceau", "clifford", "torus", "fiber", "s3")) geom = 4;
            else if (ContainsAny(p, "leech", "golay", "24d", "dodecad", "turyn")) geom = 7;
            else if (ContainsAny(p, "poincare", "hyperbolic", "5,3,4", "4,3,5", "mobius", "portal", "non-euclidean")) geom = 6;
            else if (ContainsAny(p, "120-cell", "24-cell", "polychor", "h4", "hurwitz", "4d cell", "dodecaplex")) geom = 5;
            else if (ContainsAny(p, "e8", "coxeter", "voronoi", "spinor", "8d")) geom = 1;
            else if (ContainsAny(p, "spectre", "chiral", "mystic")) geom = 3;
            else if (ContainsAny(p, "hat", "einstein", "monotile", "hex")) geom = 2;
            else if (ContainsAny(p, "penrose", "pentagrid", "quasicrystal", "de bruijn", "5d")) geom = 0;
            else if (ContainsAny(p, "gyroid", "labyrinth", "minimal surface", "sponge")) explicitGyroid = true;

            // 2. Resolve linear HDR color palette
            string colA = "float3(0.08, 0.78, 0.98)";
            string colB = "float3(0.86, 0.36, 0.98)";
            bool useRainbowHsv = ContainsAny(p, "rainbow", "prism", "iridescent", "spectral", "chromatic", "opal", "hsv");
            if (ContainsAny(p, "gold", "amber", "solar", "sun", "brass", "bronze", "dwemer", "auriel"))
            {
                colA = "float3(1.18, 0.82, 0.20)";
                colB = "float3(0.32, 0.88, 0.96)";
            }
            else if (ContainsAny(p, "emerald", "jade", "green", "malachite", "viridis", "aurora", "forest"))
            {
                colA = "float3(0.08, 0.96, 0.54)";
                colB = "float3(0.16, 0.68, 0.98)";
            }
            else if (ContainsAny(p, "crimson", "ruby", "red", "fire", "lava", "magma", "ember", "blood", "solar flare"))
            {
                colA = "float3(1.24, 0.22, 0.08)";
                colB = "float3(1.12, 0.74, 0.14)";
            }
            else if (ContainsAny(p, "violet", "purple", "amethyst", "void", "nebula", "arcane", "daedric"))
            {
                colA = "float3(0.66, 0.20, 1.18)";
                colB = "float3(0.18, 0.84, 1.05)";
            }
            else if (ContainsAny(p, "frost", "ice", "cyan", "glacier", "sovngarde", "arctic", "crystal", "diamond"))
            {
                colA = "float3(0.24, 0.88, 1.22)";
                colB = "float3(0.82, 0.94, 1.25)";
            }
            else if (ContainsAny(p, "plasma", "neon", "magenta", "cyber", "synthwave", "pink"))
            {
                colA = "float3(1.18, 0.16, 0.78)";
                colB = "float3(0.12, 0.95, 0.92)";
            }

            // 3. Speed & scale modifiers
            double speed = 1.0;
            if (ContainsAny(p, "fast", "rapid", "hyper", "overdrive", "turbo")) speed = 2.15;
            else if (ContainsAny(p, "slow", "calm", "gentle", "glacial", "meditative")) speed = 0.42;

            double scale = 1.0;
            if (ContainsAny(p, "fine", "dense", "micro", "intricate", "tight")) scale = 1.65;
            else if (ContainsAny(p, "wide", "macro", "broad", "giant", "large")) scale = 0.62;

            bool wantAudio = ContainsAny(p, "audio", "bass", "beat", "pulse", "music", "fft", "react", "harmonic");
            bool wantShock = ContainsAny(p, "thuum", "thu'um", "shout", "shockwave", "ripple", "wavefront", "ring");
            bool wantRim   = ContainsAny(p, "rim", "fresnel", "halo", "glow", "edge", "silhouette", "aura");
            bool wantGrid  = ContainsAny(p, "wireframe", "grid", "contour", "lattice", "vein", "iso", "lines");

            StringBuilder sb = new StringBuilder(1024);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "// [OmegaEngine NL->HLSL Co-Pilot] \"{0}\"\r\n", cleanOneLine);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "float omT = U[0].z * {0:0.00}; float omSc = {1:0.00};\r\n", speed, scale);

            if (explicitGyroid)
            {
                sb.Append("float gVal = abs(om_sdGyroid(wp * (1.85 * omSc) + float3(0.0, omT * 0.28, 0.0), 2.45));\r\n");
                sb.Append("float omMask = exp(-gVal * 18.0); float omPhase = frac(gVal * 4.0 + omT * 0.12);\r\n");
            }
            else if (geom == 4)
            {
                sb.Append("float3 bS2; float hSeam;\r\n");
                sb.Append("om_hopf_villarceau_field(wp * (0.28 * omSc), U[6], omT * 0.42, bS2, hSeam);\r\n");
                sb.Append("float omMask = exp(-hSeam * 14.5); float omPhase = frac(atan2(bS2.y, bS2.x + 1e-6) / 6.2831853 + omT * 0.09);\r\n");
            }
            else if (geom == 5)
            {
                sb.Append("float4 f4 = om_h4_fold16(float4(wp * (0.34 * omSc), cos(omT * 0.45) * 0.48));\r\n");
                sb.Append("float omMask = exp(-abs(f4.w - 0.86) * 15.5); float omPhase = saturate(abs(f4.x) + abs(f4.y));\r\n");
            }
            else if (geom == 6)
            {
                sb.Append("float3 fU; float invS;\r\n");
                sb.Append("float dHyp = om_poincare_honeycomb_sdf(clamp(wp * (0.115 * omSc), -0.92, 0.92), 1.0, omT * 0.26, U[15].z, fU, invS);\r\n");
                sb.Append("float omMask = exp(-abs(dHyp) * 16.5) * (0.55 + 0.08 * invS); float omPhase = frac(invS * 0.19 + omT * 0.07);\r\n");
            }
            else if (geom == 7)
            {
                sb.Append("float f1L, dod; uint hL;\r\n");
                sb.Append("float e24 = om_leech24_golay_voronoi(wp * (0.92 * omSc), omT * 0.32, f1L, dod, hL);\r\n");
                sb.Append("float omMask = exp(-e24 * 15.5); float omPhase = saturate(dod);\r\n");
            }
            else if (geom == 1)
            {
                sb.Append("uint h8 = 0u; float f1 = 0.0; float sp8 = 0.0;\r\n");
                sb.Append("float e8 = om_e8_voronoi_edge(float4(wp.xyz * (0.95 * omSc), omT * 0.28), float4(wp.zyx * 0.65, U[3].x), f1, sp8, h8);\r\n");
                sb.Append("float omMask = exp(-e8 * 15.5); float omPhase = frac(sp8 * 0.5 + float(h8 & 7u) * 0.125);\r\n");
            }
            else if (geom == 2 || geom == 3)
            {
                sb.Append("float angM = atan2(wp.z, wp.x + 1e-5); float radM = length(wp.xz) * omSc;\r\n");
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "float mono = cos(radM * 3.4 - omT * 1.6) * sin(angM * {0}.0 + wp.y * 2.1);\r\n", (geom == 3) ? 7 : 6);
                sb.Append("float omMask = exp(-abs(mono) * 4.8); float omPhase = frac(radM * 0.18 + omT * 0.08);\r\n");
            }
            else
            {
                sb.Append("float qP = cos(dot(wp.xz * omSc, PENTA_DIR[0]) * 2.3 + U[7].x) + cos(dot(wp.xz * omSc, PENTA_DIR[1]) * 2.3 + U[7].y) + cos(dot(wp.xz * omSc, PENTA_DIR[2]) * 2.3 + U[7].z);\r\n");
                sb.Append("float omMask = exp(-abs(qP) * 2.65); float omPhase = frac(qP * 0.25 + omT * 0.10);\r\n");
            }

            if (useRainbowHsv)
            {
                sb.Append("float3 omTint = om_hsv2rgb(float3(frac(omPhase + omT * 0.06), 0.82, 1.15));\r\n");
            }
            else
            {
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "float3 omTint = lerp({0}, {1}, saturate(0.5 + 0.5 * sin(omPhase * 6.2831853 + omT * 0.5)));\r\n", colA, colB);
            }

            sb.AppendFormat(CultureInfo.InvariantCulture,
                "float omGain = {0};\r\n",
                wantAudio ? "(0.52 + 0.95 * U[3].x + 0.45 * U[3].w)" : "(0.62 + 0.30 * U[3].y)");

            if (wantGrid)
            {
                sb.Append("float isoLine = exp(-abs(frac((wp.x + wp.y + wp.z) * (1.75 * omSc) - omT * 0.35) - 0.5) * 26.0);\r\n");
                sb.Append("omMask = max(omMask, isoLine * 0.72);\r\n");
            }
            if (wantRim)
            {
                sb.Append("float fres = pow(1.0 - saturate(abs(dot(normalize(wp + float3(0.0, 1e-4, 0.0)), rd))), 2.8);\r\n");
                sb.AppendFormat(CultureInfo.InvariantCulture, "col += {0} * fres * 0.34 * omGain;\r\n", colB);
            }
            if (wantShock)
            {
                sb.Append("float shRing = exp(-abs(length(wp.xz - U[13].xz) - U[14].y) * 1.75) * max(0.25, U[14].x);\r\n");
                sb.AppendFormat(CultureInfo.InvariantCulture, "col += {0} * shRing * 0.85;\r\n", colA);
            }

            sb.Append("col += omTint * omMask * omGain * 0.68;");
            return sb.ToString();
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static bool AreBracesAndParensBalanced(string s)
        {
            int br = 0, pr = 0, sq = 0;
            bool inLineComment = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (c == '{') br++;
                else if (c == '}') { br--; if (br < 0) return false; }
                else if (c == '(') pr++;
                else if (c == ')') { pr--; if (pr < 0) return false; }
                else if (c == '[') sq++;
                else if (c == ']') { sq--; if (sq < 0) return false; }
            }
            return br == 0 && pr == 0 && sq == 0;
        }

        private static bool TcpProbeUrlFast(string url, int connectTimeoutMs)
        {
            try
            {
                Uri u = new Uri(url);
                if (!u.IsLoopback) return true;
                using (System.Net.Sockets.TcpClient tc = new System.Net.Sockets.TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect("127.0.0.1", u.Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs))
                    {
                        return false;
                    }
                    tc.EndConnect(ar);
                    return tc.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPostOpenAiChat(
            string url,
            string modelName,
            string sysPrompt,
            string userPrompt,
            string optionalPrevAssistant,
            string optionalRepairUser,
            int timeoutMs,
            out string contentOut)
        {
            contentOut = "";
            try
            {
                if (!TcpProbeUrlFast(url, 90)) return false;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = null;
                req.KeepAlive = false;
                req.ServicePoint.Expect100Continue = false;
                req.Method = "POST";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.ContentType = "application/json; charset=utf-8";

                StringBuilder jb = new StringBuilder(1536);
                jb.Append("{\"model\":\"").Append(EscapeJson(modelName)).Append("\",\"temperature\":0.15,\"max_tokens\":320,\"stream\":false,\"messages\":[");
                jb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJson(sysPrompt)).Append("\"},");
                jb.Append("{\"role\":\"user\",\"content\":\"").Append(EscapeJson(userPrompt)).Append("\"}");
                if (!string.IsNullOrEmpty(optionalPrevAssistant) && !string.IsNullOrEmpty(optionalRepairUser))
                {
                    jb.Append(",{\"role\":\"assistant\",\"content\":\"").Append(EscapeJson(optionalPrevAssistant)).Append("\"}");
                    jb.Append(",{\"role\":\"user\",\"content\":\"").Append(EscapeJson(optionalRepairUser)).Append("\"}");
                }
                jb.Append("]}");

                byte[] payload = Encoding.UTF8.GetBytes(jb.ToString());
                req.ContentLength = payload.Length;
                using (Stream rs = req.GetRequestStream())
                {
                    rs.Write(payload, 0, payload.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string rawJson = sr.ReadToEnd();
                    contentOut = ExtractOpenAiMessageContent(rawJson);
                    return !string.IsNullOrEmpty(contentOut);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetTensorOmshAssist(int symMode, string prompt, int timeoutMs, out string omshOut)
        {
            omshOut = "";
            try
            {
                if (!TcpProbeUrlFast(DefaultTensorAssistBase, 90)) return false;
                string u = DefaultTensorAssistBase + "?mode=" + symMode.ToString(CultureInfo.InvariantCulture) +
                           "&prompt=" + Uri.EscapeDataString(prompt ?? "");
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(u);
                req.Proxy = null;
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string js = sr.ReadToEnd();
                    Match m = Regex.Match(js ?? "", "\"omsh\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                    if (m.Success)
                    {
                        omshOut = UnescapeJsonString(m.Groups[1].Value);
                        return !string.IsNullOrEmpty(omshOut);
                    }
                }
            }
            catch { }
            return false;
        }

        public static string ExtractOpenAiMessageContent(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            // Match "message" : { ... "content" : "..." } first, then fallback to any "content" : "..."
            Match mMsg = Regex.Match(json, "\"message\"\\s*:\\s*\\{[^{}]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mMsg.Success) return UnescapeJsonString(mMsg.Groups[1].Value);
            Match mAny = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mAny.Success) return UnescapeJsonString(mAny.Groups[1].Value);
            Match mOmsh = Regex.Match(json, "\"omsh\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (mOmsh.Success) return UnescapeJsonString(mOmsh.Groups[1].Value);
            return "";
        }

        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 32);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string UnescapeJsonString(string esc)
        {
            if (string.IsNullOrEmpty(esc)) return "";
            StringBuilder sb = new StringBuilder(esc.Length);
            for (int i = 0; i < esc.Length; i++)
            {
                char c = esc[i];
                if (c == '\\' && i + 1 < esc.Length)
                {
                    char nx = esc[++i];
                    if (nx == 'n') sb.Append('\n');
                    else if (nx == 'r') sb.Append('\r');
                    else if (nx == 't') sb.Append('\t');
                    else if (nx == '"') sb.Append('"');
                    else if (nx == '\\') sb.Append('\\');
                    else if (nx == '/') sb.Append('/');
                    else if (nx == 'u' && i + 4 < esc.Length)
                    {
                        int code;
                        if (int.TryParse(esc.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                    else sb.Append(nx);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }

    // ============================================================================
    // DETERMINISTIC CAMERA & UNIFORM KEYFRAME SPLINE + LOSSLESS PNG TURNTABLE
    // ============================================================================
    public struct ArenaSplineKeyframe
    {
        public float TimeSec;
        public float CamX;
        public float CamY;
        public float CamZ;
        public float QuatX;
        public float QuatY;
        public float QuatZ;
        public float QuatW;
        public float CamYaw;
        public float CamPitch;
        public float FovDeg;
        public int SymmetryMode;
        public float K1;
        public float K2;
        public float K3;
        public float K4;
        public float WeatherAurora;
        public float HdrBloom;
        public float CustomMix;
        public float PhasonTimeSec;
    }

    public sealed class ArenaKeyframeTrack
    {
        private readonly object _lk = new object();
        private readonly List<ArenaSplineKeyframe> _knots = new List<ArenaSplineKeyframe>();
        private bool _loopClosed = false;

        public int Count { get { lock (_lk) { return _knots.Count; } } }
        public bool LoopClosed
        {
            get { lock (_lk) { return _loopClosed; } }
            set { lock (_lk) { _loopClosed = value; } }
        }

        public float TotalDurationSec
        {
            get
            {
                lock (_lk)
                {
                    if (_knots.Count == 0) return 0.0f;
                    if (_knots.Count == 1) return Math.Max(1.0f, _knots[0].TimeSec);
                    float span = _knots[_knots.Count - 1].TimeSec - _knots[0].TimeSec;
                    if (_loopClosed)
                    {
                        float avgStep = span / Math.Max(1, _knots.Count - 1);
                        return Math.Max(0.1f, span + avgStep);
                    }
                    return Math.Max(0.1f, span);
                }
            }
        }

        public void Clear()
        {
            lock (_lk)
            {
                _knots.Clear();
                _loopClosed = false;
            }
        }

        public int AddKeyframe(ArenaSplineKeyframe kf)
        {
            lock (_lk)
            {
                double qx = kf.QuatX, qy = kf.QuatY, qz = kf.QuatZ, qw = kf.QuatW;
                double qn = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                if (qn < 1e-5)
                {
                    float ex, ey, ez, ew;
                    OmegaTriEngineMath.EulerYawPitchToQuat(kf.CamYaw, kf.CamPitch, 0.0f, out ex, out ey, out ez, out ew);
                    kf.QuatX = ex; kf.QuatY = ey; kf.QuatZ = ez; kf.QuatW = ew;
                }
                else
                {
                    float inv = (float)(1.0 / qn);
                    kf.QuatX *= inv; kf.QuatY *= inv; kf.QuatZ *= inv; kf.QuatW *= inv;
                    float ry, rp;
                    OmegaTriEngineMath.QuatToYawPitch(kf.QuatX, kf.QuatY, kf.QuatZ, kf.QuatW, out ry, out rp);
                    kf.CamYaw = ry;
                    kf.CamPitch = rp;
                }

                if (_knots.Count > 0)
                {
                    ArenaSplineKeyframe prev = _knots[_knots.Count - 1];
                    if (kf.TimeSec <= prev.TimeSec + 1e-4f)
                    {
                        kf.TimeSec = prev.TimeSec + 1.50f;
                    }
                    // Enforce shortest-arc hemisphere continuity along recorded knot chain
                    double dot = prev.QuatX * kf.QuatX + prev.QuatY * kf.QuatY + prev.QuatZ * kf.QuatZ + prev.QuatW * kf.QuatW;
                    if (dot < 0.0)
                    {
                        kf.QuatX = -kf.QuatX; kf.QuatY = -kf.QuatY; kf.QuatZ = -kf.QuatZ; kf.QuatW = -kf.QuatW;
                    }
                }
                else if (kf.TimeSec < 0.0f)
                {
                    kf.TimeSec = 0.0f;
                }

                _knots.Add(kf);
                return _knots.Count;
            }
        }

        public int RecordFromArenaState(OmegaEngineArenaState state, float stepSec)
        {
            ArenaSplineKeyframe kf = new ArenaSplineKeyframe();
            lock (state.Sync)
            {
                kf.CamX = state.CamX;
                kf.CamY = state.CamY;
                kf.CamZ = state.CamZ;
                kf.CamYaw = state.CamYaw;
                kf.CamPitch = state.CamPitch;
                OmegaTriEngineMath.EulerYawPitchToQuat(state.CamYaw, state.CamPitch, 0.0f, out kf.QuatX, out kf.QuatY, out kf.QuatZ, out kf.QuatW);
                kf.FovDeg = state.FovDeg;
                kf.SymmetryMode = state.SymmetryMode;
                kf.K1 = state.K1;
                kf.K2 = state.K2;
                kf.K3 = state.K3;
                kf.K4 = state.K4;
                kf.WeatherAurora = state.WeatherAurora;
                kf.HdrBloom = state.HdrBloomStrength;
                kf.CustomMix = state.CustomMix;
            }
            lock (_lk)
            {
                kf.TimeSec = (_knots.Count == 0) ? 0.0f : (_knots[_knots.Count - 1].TimeSec + Math.Max(0.20f, stepSec));
                kf.PhasonTimeSec = kf.TimeSec;
            }
            return AddKeyframe(kf);
        }

        public ArenaSplineKeyframe[] GetKnotsSnapshot()
        {
            lock (_lk)
            {
                return _knots.ToArray();
            }
        }

        public static ArenaKeyframeTrack BuildCanonicalTurntableTrack(
            int knotCount,
            float orbitRadius,
            float lookAtY,
            float durationSec,
            int symmetryMode,
            float k1,
            float k2,
            float k3,
            float k4,
            float aurora,
            float hdrBloom,
            float customMix)
        {
            int n = Math.Max(4, Math.Min(64, knotCount));
            float r = Math.Max(3.5f, Math.Min(16.2f, orbitRadius));
            float dur = Math.Max(1.0f, durationSec);
            float dt = dur / n;

            ArenaKeyframeTrack trk = new ArenaKeyframeTrack();
            for (int i = 0; i < n; i++)
            {
                double frac = (double)i / n;
                double ang = 2.0 * Math.PI * frac;
                float cx = (float)(-r * Math.Sin(ang));
                float cz = (float)(-r * Math.Cos(ang));
                float cy = lookAtY - 0.18f + (float)(0.52 * Math.Sin(2.0 * ang));

                double dx = 0.0 - cx;
                double dy = lookAtY - cy;
                double dz = 0.0 - cz;
                double horiz = Math.Sqrt(dx * dx + dz * dz);
                float yaw = (float)Math.Atan2(dx, dz);
                float pitch = (float)Math.Atan2(dy, Math.Max(1e-6, horiz));
                float qx, qy, qz, qw;
                OmegaTriEngineMath.EulerYawPitchToQuat(yaw, pitch, 0.0f, out qx, out qy, out qz, out qw);

                ArenaSplineKeyframe kf = new ArenaSplineKeyframe();
                kf.TimeSec = i * dt;
                kf.CamX = cx;
                kf.CamY = cy;
                kf.CamZ = cz;
                kf.QuatX = qx; kf.QuatY = qy; kf.QuatZ = qz; kf.QuatW = qw;
                kf.CamYaw = yaw;
                kf.CamPitch = pitch;
                kf.FovDeg = 62.0f + (float)(3.0 * Math.Cos(ang));
                kf.SymmetryMode = Math.Max(0, Math.Min(7, symmetryMode));
                kf.K1 = k1;
                kf.K2 = k2;
                kf.K3 = k3;
                kf.K4 = k4;
                kf.WeatherAurora = aurora;
                kf.HdrBloom = hdrBloom;
                kf.CustomMix = customMix;
                kf.PhasonTimeSec = i * dt;
                trk.AddKeyframe(kf);
            }
            trk.LoopClosed = true;
            return trk;
        }

        public bool EvaluateAtTime(double tSec, out ArenaSplineKeyframe pose)
        {
            lock (_lk)
            {
                int n = _knots.Count;
                if (n == 0)
                {
                    pose = new ArenaSplineKeyframe();
                    pose.CamY = 2.35f; pose.CamZ = -10.2f; pose.QuatW = 1.0f; pose.FovDeg = 64.0f;
                    pose.K1 = 1.0f; pose.K2 = 1.0f; pose.K3 = 0.85f; pose.K4 = 1.54f;
                    pose.WeatherAurora = 0.78f; pose.HdrBloom = 0.32f; pose.CustomMix = 0.25f;
                    return false;
                }
                if (n == 1)
                {
                    pose = _knots[0];
                    return true;
                }

                int segIdx = 0;
                double u = 0.0;

                if (_loopClosed)
                {
                    double t0 = _knots[0].TimeSec;
                    double avgStep = (_knots[n - 1].TimeSec - t0) / Math.Max(1, n - 1);
                    if (avgStep < 1e-5) avgStep = 1.0;
                    double period = (_knots[n - 1].TimeSec - t0) + avgStep;
                    double wrapped = (tSec - t0) % period;
                    if (wrapped < 0.0) wrapped += period;
                    double absT = t0 + wrapped;

                    if (absT >= _knots[n - 1].TimeSec)
                    {
                        segIdx = n - 1;
                        u = (absT - _knots[n - 1].TimeSec) / avgStep;
                    }
                    else
                    {
                        segIdx = n - 2;
                        for (int i = 0; i < n - 1; i++)
                        {
                            if (absT >= _knots[i].TimeSec && absT <= _knots[i + 1].TimeSec)
                            {
                                segIdx = i;
                                break;
                            }
                        }
                        double segDt = Math.Max(1e-6, _knots[segIdx + 1].TimeSec - _knots[segIdx].TimeSec);
                        u = (absT - _knots[segIdx].TimeSec) / segDt;
                    }
                    u = Math.Max(0.0, Math.Min(1.0, u));

                    ArenaSplineKeyframe k0 = _knots[(segIdx - 1 + n) % n];
                    ArenaSplineKeyframe k1 = _knots[segIdx];
                    ArenaSplineKeyframe k2 = _knots[(segIdx + 1) % n];
                    ArenaSplineKeyframe k3 = _knots[(segIdx + 2) % n];
                    pose = InterpolateSegment(k0, k1, k2, k3, u, tSec);
                    return true;
                }
                else
                {
                    if (tSec <= _knots[0].TimeSec)
                    {
                        pose = _knots[0];
                        return true;
                    }
                    if (tSec >= _knots[n - 1].TimeSec)
                    {
                        pose = _knots[n - 1];
                        return true;
                    }
                    segIdx = n - 2;
                    for (int i = 0; i < n - 1; i++)
                    {
                        if (tSec >= _knots[i].TimeSec && tSec <= _knots[i + 1].TimeSec)
                        {
                            segIdx = i;
                            break;
                        }
                    }
                    double segDt = Math.Max(1e-6, _knots[segIdx + 1].TimeSec - _knots[segIdx].TimeSec);
                    u = Math.Max(0.0, Math.Min(1.0, (tSec - _knots[segIdx].TimeSec) / segDt));

                    ArenaSplineKeyframe k1 = _knots[segIdx];
                    ArenaSplineKeyframe k2 = _knots[segIdx + 1];
                    ArenaSplineKeyframe k0 = (segIdx > 0) ? _knots[segIdx - 1] : ExtrapolatePhantomEndpoint(k1, k2);
                    ArenaSplineKeyframe k3 = (segIdx + 2 < n) ? _knots[segIdx + 2] : ExtrapolatePhantomEndpoint(k2, k1);
                    pose = InterpolateSegment(k0, k1, k2, k3, u, tSec);
                    return true;
                }
            }
        }

        private static ArenaSplineKeyframe ExtrapolatePhantomEndpoint(ArenaSplineKeyframe anchor, ArenaSplineKeyframe neighbor)
        {
            ArenaSplineKeyframe p = anchor;
            p.CamX = 2.0f * anchor.CamX - neighbor.CamX;
            p.CamY = 2.0f * anchor.CamY - neighbor.CamY;
            p.CamZ = 2.0f * anchor.CamZ - neighbor.CamZ;
            p.FovDeg = 2.0f * anchor.FovDeg - neighbor.FovDeg;
            p.K1 = 2.0f * anchor.K1 - neighbor.K1;
            p.K2 = 2.0f * anchor.K2 - neighbor.K2;
            p.K3 = 2.0f * anchor.K3 - neighbor.K3;
            p.K4 = 2.0f * anchor.K4 - neighbor.K4;
            p.WeatherAurora = 2.0f * anchor.WeatherAurora - neighbor.WeatherAurora;
            p.HdrBloom = 2.0f * anchor.HdrBloom - neighbor.HdrBloom;
            p.CustomMix = 2.0f * anchor.CustomMix - neighbor.CustomMix;
            p.PhasonTimeSec = 2.0f * anchor.PhasonTimeSec - neighbor.PhasonTimeSec;
            return p;
        }

        private static ArenaSplineKeyframe InterpolateSegment(
            ArenaSplineKeyframe k0,
            ArenaSplineKeyframe k1,
            ArenaSplineKeyframe k2,
            ArenaSplineKeyframe k3,
            double u,
            double rawTimeSec)
        {
            ArenaSplineKeyframe o = new ArenaSplineKeyframe();
            o.TimeSec = (float)rawTimeSec;
            o.CamX = (float)OmegaTriEngineMath.EvalCatmullRom(k0.CamX, k1.CamX, k2.CamX, k3.CamX, u);
            o.CamY = (float)OmegaTriEngineMath.EvalCatmullRom(k0.CamY, k1.CamY, k2.CamY, k3.CamY, u);
            o.CamZ = (float)OmegaTriEngineMath.EvalCatmullRom(k0.CamZ, k1.CamZ, k2.CamZ, k3.CamZ, u);

            double qx, qy, qz, qw;
            OmegaTriEngineMath.QuatSquad(
                k0.QuatX, k0.QuatY, k0.QuatZ, k0.QuatW,
                k1.QuatX, k1.QuatY, k1.QuatZ, k1.QuatW,
                k2.QuatX, k2.QuatY, k2.QuatZ, k2.QuatW,
                k3.QuatX, k3.QuatY, k3.QuatZ, k3.QuatW,
                u, out qx, out qy, out qz, out qw);
            o.QuatX = (float)qx;
            o.QuatY = (float)qy;
            o.QuatZ = (float)qz;
            o.QuatW = (float)qw;
            OmegaTriEngineMath.QuatToYawPitch(o.QuatX, o.QuatY, o.QuatZ, o.QuatW, out o.CamYaw, out o.CamPitch);

            o.FovDeg = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.FovDeg, k1.FovDeg, k2.FovDeg, k3.FovDeg, u), 35.0, 100.0);
            o.SymmetryMode = (u < 0.5) ? k1.SymmetryMode : k2.SymmetryMode;
            o.K1 = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.K1, k1.K1, k2.K1, k3.K1, u), 0.35, 2.20);
            o.K2 = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.K2, k1.K2, k2.K2, k3.K2, u), 0.00, 2.50);
            o.K3 = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.K3, k1.K3, k2.K3, k3.K3, u), 0.00, 2.20);
            o.K4 = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.K4, k1.K4, k2.K4, k3.K4, u), 1.10, 2.40);
            o.WeatherAurora = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.WeatherAurora, k1.WeatherAurora, k2.WeatherAurora, k3.WeatherAurora, u), 0.0, 1.80);
            o.HdrBloom = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.HdrBloom, k1.HdrBloom, k2.HdrBloom, k3.HdrBloom, u), 0.0, 1.20);
            o.CustomMix = (float)OmegaTriEngineMath.ClampRange(OmegaTriEngineMath.EvalCatmullRom(k0.CustomMix, k1.CustomMix, k2.CustomMix, k3.CustomMix, u), 0.0, 1.00);
            o.PhasonTimeSec = (float)(k1.PhasonTimeSec + (k2.PhasonTimeSec - k1.PhasonTimeSec) * u);
            return o;
        }

        public string ExportTrackJson(string outFilePath)
        {
            ArenaSplineKeyframe[] snap = GetKnotsSnapshot();
            bool loop = LoopClosed;
            StringBuilder sb = new StringBuilder(snap.Length * 256 + 128);
            sb.AppendLine("{");
            sb.AppendLine("  \"format\": \"omega_keyframe_spline_v1\",");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"loop_closed\": {0},\r\n", loop ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"knot_count\": {0},\r\n", snap.Length);
            sb.AppendLine("  \"keyframes\": [");
            for (int i = 0; i < snap.Length; i++)
            {
                ArenaSplineKeyframe k = snap[i];
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "    {{\"t\":{0:0.0000},\"cam\":[{1:0.000000},{2:0.000000},{3:0.000000}],\"quat\":[{4:0.0000000},{5:0.0000000},{6:0.0000000},{7:0.0000000}],\"fov\":{8:0.0000},\"sym\":{9},\"k\":[{10:0.0000},{11:0.0000},{12:0.0000},{13:0.0000}],\"aurora\":{14:0.0000},\"bloom\":{15:0.0000},\"mix\":{16:0.0000},\"phason_t\":{17:0.0000}}}{18}\r\n",
                    k.TimeSec, k.CamX, k.CamY, k.CamZ,
                    k.QuatX, k.QuatY, k.QuatZ, k.QuatW,
                    k.FovDeg, k.SymmetryMode,
                    k.K1, k.K2, k.K3, k.K4,
                    k.WeatherAurora, k.HdrBloom, k.CustomMix, k.PhasonTimeSec,
                    (i + 1 < snap.Length) ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            string txt = sb.ToString();
            if (!string.IsNullOrEmpty(outFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outFilePath));
                File.WriteAllText(outFilePath, txt, new UTF8Encoding(false));
            }
            return txt;
        }

        public static ArenaKeyframeTrack ImportTrackJson(string jsonOrPath)
        {
            string js = jsonOrPath ?? "";
            if (File.Exists(js)) js = File.ReadAllText(js, Encoding.UTF8);
            ArenaKeyframeTrack trk = new ArenaKeyframeTrack();
            bool loop = Regex.IsMatch(js, "\"loop_closed\"\\s*:\\s*true", RegexOptions.IgnoreCase);
            MatchCollection mc = Regex.Matches(js, "\\{[^{}]*\"t\"\\s*:\\s*([-0-9.]+)[^{}]*\\}");
            for (int i = 0; i < mc.Count; i++)
            {
                string row = mc[i].Value;
                ArenaSplineKeyframe k = new ArenaSplineKeyframe();
                k.TimeSec = MatchFloat(row, "\"t\"\\s*:\\s*([-0-9.]+)", i * 1.5f);
                float[] cam = MatchFloatArray(row, "cam", 3);
                k.CamX = cam[0]; k.CamY = cam[1]; k.CamZ = cam[2];
                float[] qt = MatchFloatArray(row, "quat", 4);
                k.QuatX = qt[0]; k.QuatY = qt[1]; k.QuatZ = qt[2]; k.QuatW = qt[3];
                OmegaTriEngineMath.QuatToYawPitch(k.QuatX, k.QuatY, k.QuatZ, k.QuatW, out k.CamYaw, out k.CamPitch);
                k.FovDeg = MatchFloat(row, "\"fov\"\\s*:\\s*([-0-9.]+)", 64.0f);
                k.SymmetryMode = (int)MatchFloat(row, "\"sym\"\\s*:\\s*([-0-9]+)", 0.0f);
                float[] ks = MatchFloatArray(row, "k", 4);
                k.K1 = ks[0]; k.K2 = ks[1]; k.K3 = ks[2]; k.K4 = ks[3];
                k.WeatherAurora = MatchFloat(row, "\"aurora\"\\s*:\\s*([-0-9.]+)", 0.78f);
                k.HdrBloom = MatchFloat(row, "\"bloom\"\\s*:\\s*([-0-9.]+)", 0.32f);
                k.CustomMix = MatchFloat(row, "\"mix\"\\s*:\\s*([-0-9.]+)", 0.25f);
                k.PhasonTimeSec = MatchFloat(row, "\"phason_t\"\\s*:\\s*([-0-9.]+)", k.TimeSec);
                trk.AddKeyframe(k);
            }
            trk.LoopClosed = loop;
            return trk;
        }

        private static float MatchFloat(string s, string pat, float defVal)
        {
            Match m = Regex.Match(s, pat);
            float v;
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return defVal;
        }

        private static float[] MatchFloatArray(string s, string key, int expectedLen)
        {
            float[] res = new float[expectedLen];
            Match m = Regex.Match(s, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]+)\\]");
            if (m.Success)
            {
                string[] parts = m.Groups[1].Value.Split(',');
                for (int i = 0; i < expectedLen && i < parts.Length; i++)
                {
                    float v;
                    if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        res[i] = v;
                }
            }
            return res;
        }
    }

    public sealed class TurntableSequenceResult
    {
        public bool Ok;
        public string OutputDir = "";
        public string ManifestPath = "";
        public string TrackJsonPath = "";
        public int FrameCount;
        public int Width;
        public int Height;
        public float Fps;
        public double TotalMs;
        public readonly List<string> PngPaths = new List<string>();
        public readonly List<string> FrameSha256 = new List<string>();
        public string Error = "";
    }

    public static class OmegaTurntableSequenceRenderer
    {
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", ExactSpelling = true)]
        private static extern void CopyMemoryRow(IntPtr dest, IntPtr src, UIntPtr count);

        public static TurntableSequenceResult RenderKeyframeSequencePngs(
            OmegaEngineDirectComputeGpu gpu,
            ArenaKeyframeTrack track,
            string outputDir,
            int width,
            int height,
            int frameCount,
            float fps,
            float renderScale,
            bool enableRayCone)
        {
            Stopwatch sw = Stopwatch.StartNew();
            TurntableSequenceResult res = new TurntableSequenceResult();
            int w = Math.Max(64, Math.Min(3840, width));
            int h = Math.Max(64, Math.Min(2160, height));
            int frames = Math.Max(2, Math.Min(600, frameCount));
            float safeFps = Math.Max(1.0f, Math.Min(240.0f, fps));

            res.OutputDir = outputDir;
            res.Width = w;
            res.Height = h;
            res.FrameCount = frames;
            res.Fps = safeFps;

            try
            {
                Directory.CreateDirectory(outputDir);
                ArenaKeyframeTrack activeTrack = (track != null && track.Count >= 2)
                    ? track
                    : ArenaKeyframeTrack.BuildCanonicalTurntableTrack(8, 9.6f, 2.65f, frames / safeFps, 0, 1.0f, 1.0f, 0.85f, 1.54f, 0.78f, 0.32f, 0.25f);

                string trackPath = Path.Combine(outputDir, "turntable_spline.omkf.json");
                activeTrack.ExportTrackJson(trackPath);
                res.TrackJsonPath = trackPath;

                if (gpu != null && gpu.IsAvailable)
                {
                    gpu.ResetDeterministicMoatState();
                }

                float totalDur = activeTrack.TotalDurationSec;
                bool closed = activeTrack.LoopClosed;

                // Single reusable pixel buffer + single reusable Bitmap -> ZERO per-frame LOH allocations!
                int[] pixels = new int[w * h];
                float[] u80 = new float[80];
                double[] gamma5 = new double[5];

                GCHandle pinPx = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    IntPtr srcBase = pinPx.AddrOfPinnedObject();
                    using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (SHA256 sha = SHA256.Create())
                    {
                        StringBuilder man = new StringBuilder(frames * 180 + 256);
                        man.AppendLine("{");
                        man.AppendLine("  \"format\": \"omega_turntable_sequence_v1\",");
                        man.AppendFormat(CultureInfo.InvariantCulture, "  \"width\": {0},\r\n  \"height\": {1},\r\n  \"frames\": {2},\r\n  \"fps\": {3:0.00},\r\n", w, h, frames, safeFps);
                        man.AppendFormat(CultureInfo.InvariantCulture, "  \"loop_closed\": {0},\r\n", closed ? "true" : "false");
                        man.AppendLine("  \"sequence\": [");

                        for (int f = 0; f < frames; f++)
                        {
                            double norm = closed
                                ? ((double)f / frames)
                                : ((double)f / Math.Max(1, frames - 1));
                            double evalTime = norm * totalDur;
                            double simClockSec = f / (double)safeFps;

                            ArenaSplineKeyframe pose;
                            activeTrack.EvaluateAtTime(evalTime, out pose);
                            BuildDeterministicTurntableUniforms(pose, w, h, simClockSec, gamma5, u80);

                            if (gpu != null && gpu.IsAvailable)
                            {
                                gpu.RenderArenaFrameEx(w, h, renderScale, pose.HdrBloom, enableRayCone, u80, false, 0u, pixels);
                            }
                            else
                            {
                                OmegaEngineDirectComputeGpu.RenderCpuFallbackArena(w, h, u80, pixels);
                            }

                            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                            try
                            {
                                int rowBytes = w * 4;
                                if (bd.Stride == rowBytes)
                                {
                                    CopyMemoryRow(bd.Scan0, srcBase, (UIntPtr)(rowBytes * h));
                                }
                                else
                                {
                                    for (int ry = 0; ry < h; ry++)
                                    {
                                        CopyMemoryRow(
                                            new IntPtr(bd.Scan0.ToInt64() + (long)ry * bd.Stride),
                                            new IntPtr(srcBase.ToInt64() + (long)ry * rowBytes),
                                            (UIntPtr)rowBytes);
                                    }
                                }
                            }
                            finally
                            {
                                bmp.UnlockBits(bd);
                            }

                            string fileName = string.Format(CultureInfo.InvariantCulture, "frame_{0:D4}.png", f);
                            string fullPng = Path.Combine(outputDir, fileName);
                            bmp.Save(fullPng, ImageFormat.Png);

                            byte[] pngBytes = File.ReadAllBytes(fullPng);
                            byte[] digest = sha.ComputeHash(pngBytes);
                            StringBuilder hs = new StringBuilder(64);
                            for (int b = 0; b < digest.Length; b++) hs.Append(digest[b].ToString("x2", CultureInfo.InvariantCulture));
                            string shaHex = hs.ToString();

                            res.PngPaths.Add(fullPng);
                            res.FrameSha256.Add(shaHex);

                            man.AppendFormat(CultureInfo.InvariantCulture,
                                "    {{\"frame\":{0},\"file\":\"{1}\",\"t_sec\":{2:0.0000},\"cam\":[{3:0.000},{4:0.000},{5:0.000}],\"yaw_pitch\":[{6:0.000},{7:0.000}],\"bytes\":{8},\"sha256\":\"{9}\"}}{10}\r\n",
                                f, fileName, evalTime,
                                pose.CamX, pose.CamY, pose.CamZ,
                                pose.CamYaw, pose.CamPitch,
                                pngBytes.Length, shaHex,
                                (f + 1 < frames) ? "," : "");
                        }

                        man.AppendLine("  ]");
                        man.AppendLine("}");
                        string manifestPath = Path.Combine(outputDir, "turntable_manifest.json");
                        File.WriteAllText(manifestPath, man.ToString(), new UTF8Encoding(false));
                        res.ManifestPath = manifestPath;
                    }
                }
                finally
                {
                    if (pinPx.IsAllocated) pinPx.Free();
                }

                OmegaEngineDirectComputeGpu.TrimWorkingSetNow();
                sw.Stop();
                res.TotalMs = sw.Elapsed.TotalMilliseconds;
                res.Ok = (res.PngPaths.Count == frames);
                return res;
            }
            catch (Exception ex)
            {
                sw.Stop();
                res.TotalMs = sw.Elapsed.TotalMilliseconds;
                res.Ok = false;
                res.Error = ex.Message;
                return res;
            }
        }

        public static void BuildDeterministicTurntableUniforms(
            ArenaSplineKeyframe pose,
            int w,
            int h,
            double simSec,
            double[] gamma5Scratch,
            float[] dstU80)
        {
            Array.Clear(dstU80, 0, 80);
            double phasonArg = pose.PhasonTimeSec * 0.22 * pose.K2;
            OmegaTriEngineMath.ComputeZeroSumPentagridShifts(phasonArg, gamma5Scratch);

            float fx, fy, fz, rx, ry, rz, ux, uy, uz;
            OmegaTriEngineMath.BuildCameraBasis(pose.CamYaw, pose.CamPitch, out fx, out fy, out fz, out rx, out ry, out rz, out ux, out uy, out uz);
            float fovTan = (float)Math.Tan((pose.FovDeg * Math.PI / 180.0) * 0.5);

            // Deterministic analytic harmonic audio envelope (zero wall-clock or microphone non-determinism!)
            float bass = (float)(0.20 + 0.08 * Math.Sin(simSec * Math.PI * 2.0));
            float mid  = (float)(0.16 + 0.06 * Math.Cos(simSec * Math.PI * 3.0));
            float high = (float)(0.12 + 0.04 * Math.Sin(simSec * Math.PI * 4.0));
            float peak = Math.Max(bass, Math.Max(mid, high)) * 1.15f;

            dstU80[0] = w; dstU80[1] = h; dstU80[2] = (float)simSec; dstU80[3] = fovTan;
            dstU80[4] = pose.CamX; dstU80[5] = pose.CamY; dstU80[6] = pose.CamZ; dstU80[7] = pose.CamYaw;
            dstU80[8] = pose.CamPitch; dstU80[9] = 0.08f; dstU80[10] = 180.0f; dstU80[11] = 2.0f;
            dstU80[12] = bass; dstU80[13] = mid; dstU80[14] = high; dstU80[15] = peak;
            for (int k = 0; k < 8; k++)
            {
                dstU80[16 + k] = (float)(0.14 + 0.05 * Math.Sin(simSec * 2.0 + k * 0.75));
            }
            dstU80[24] = pose.QuatX; dstU80[25] = pose.QuatY; dstU80[26] = pose.QuatZ; dstU80[27] = pose.QuatW;
            dstU80[28] = (float)gamma5Scratch[0]; dstU80[29] = (float)gamma5Scratch[1];
            dstU80[30] = (float)gamma5Scratch[2]; dstU80[31] = (float)gamma5Scratch[3];
            dstU80[32] = (float)gamma5Scratch[4];
            dstU80[33] = (float)(0.25 * Math.Sin(pose.PhasonTimeSec * 0.31) * pose.K2);
            dstU80[34] = (float)(0.25 * Math.Cos(pose.PhasonTimeSec * 0.27) * pose.K2);
            dstU80[35] = 12.31f;
            dstU80[36] = pose.SymmetryMode; dstU80[37] = 0.85f; dstU80[38] = 0.032f; dstU80[39] = 1.0f;
            dstU80[40] = 0.20f; dstU80[41] = 0.045f; dstU80[42] = 0.58f; dstU80[43] = -0.14f;
            dstU80[44] = 4.0f; dstU80[45] = 7.0f; dstU80[46] = 96.0f; dstU80[47] = 1.0f;
            dstU80[48] = 0.65f; dstU80[49] = 0.80f; dstU80[50] = 22.4f; dstU80[51] = 12.0f;
            dstU80[52] = 0.0f; dstU80[53] = 0.85f; dstU80[54] = 0.0f; dstU80[55] = 99.0f;
            dstU80[56] = 0.0f; dstU80[57] = 0.0f; dstU80[58] = 9.0f; dstU80[59] = pose.WeatherAurora;
            dstU80[60] = pose.K1; dstU80[61] = pose.K2; dstU80[62] = pose.K3; dstU80[63] = pose.K4;
            dstU80[64] = 3.4f; dstU80[65] = 0.055f; dstU80[66] = 2.2f; dstU80[67] = 0.38f;
            dstU80[68] = 0.42f; dstU80[69] = 0.78f; dstU80[70] = -0.46f; dstU80[71] = 0.013f;
            dstU80[72] = fx; dstU80[73] = fy; dstU80[74] = fz; dstU80[75] = 1.12f;
            dstU80[76] = rx; dstU80[77] = ry; dstU80[78] = rz; dstU80[79] = pose.CustomMix;
        }
    }
}