using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmegaSheafModSurgeon
{
    public sealed class PolyglotFieldDecl
    {
        public string Name = "";
        public int ByteSize;
        public int NaturalAlign;
        public string RustType = "";
        public string CppType = "";
        public string CSharpType = "";
    }

    public sealed class ComputedFieldLayout
    {
        public string Name = "";
        public int Offset;
        public int Size;
        public int AlignUsed;
        public int ImplicitPadBefore;
    }

    public sealed class StalkStructLayout
    {
        public string Language = "";
        public int PackRule;
        public int StructAlign;
        public int TotalSize;
        public int TailPadding;
        public List<ComputedFieldLayout> Fields = new List<ComputedFieldLayout>();
    }

    public sealed class SheafAbiAuditReport
    {
        public string StructName = "";
        public bool H1ZeroExact;
        public int TotalCoboundaryDefectBytes;
        public StalkStructLayout RustStalk;
        public StalkStructLayout CppStalk;
        public StalkStructLayout CSharpStalk;
        public string Diagnostic = "";
    }

    public sealed class QuestStageNode
    {
        public int StageId;
        public string SourcePlugin = "";
        public string Description = "";
        public List<int> PrereqStages = new List<int>();
        public bool IsCompletionStage;
    }

    public sealed class ModPluginDescriptor
    {
        public string FileName = "";
        public bool IsLightEsl;
        public int NewRecordCount;
        public int DeletedRefCount;
        public List<string> RequiredMasters = new List<string>();
    }

    public sealed class LoadOrderSheafReport
    {
        public bool H1ZeroExact;
        public int CycleStageCount;
        public int UnreachableCompletionCount;
        public int EslOverflowCount;
        public int DeletedRefHazards;
        public int MissingMasterOrderViolations;
        public List<string> Findings = new List<string>();
    }

    public enum HookGateVerdict
    {
        ArmedUniqueMatch = 0,
        QuarantinedZeroMatch = 1,
        QuarantinedAmbiguousMultiMatch = 2,
        QuarantinedMalformedPattern = 3
    }

    public sealed class AobScanReport
    {
        public string HookName = "";
        public string SignatureText = "";
        public HookGateVerdict Verdict;
        public int MatchCount;
        public int UniqueOffsetRva;
        public string Detail = "";
    }

    public sealed class CrashTriageReport
    {
        public uint ExceptionCode;
        public string ExceptionName = "";
        public ulong RipAddress;
        public string FaultingModule = "";
        public ulong ModuleRvaOffset;
        public string RootCauseCategory = "";
        public string SurgicalPrescription = "";
    }

    public sealed class IdempotentHookLatch
    {
        private int _state = 0; // 0 = Unarmed, 1 = Patching, 2 = ArmedOnce
        private int _installCount = 0;

        public int InstallCount { get { return _installCount; } }

        public bool TryInstallOnce()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                return false;
            Interlocked.Increment(ref _installCount);
            Interlocked.Exchange(ref _state, 2);
            return true;
        }
    }

    public static class OmegaSheafModCore
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static double TrimWorkingSetMb()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Process self = Process.GetCurrentProcess();
                EmptyWorkingSet(self.Handle);
                self.Refresh();
                return self.WorkingSet64 / (1024.0 * 1024.0);
            }
            catch { }
            return Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        }

        // =====================================================================
        // 1. POLYGLOT #[repr(C)] CELLULAR SHEAF ABI VERIFIER (H^1 = 0)
        // =====================================================================
        public static StalkStructLayout ComputeAbiStalkLayout(string language, List<PolyglotFieldDecl> fields, int packOverride, int explicitStructAlign)
        {
            StalkStructLayout st = new StalkStructLayout();
            st.Language = language;
            st.PackRule = (packOverride <= 0) ? 16 : packOverride;

            int offset = 0;
            int maxFieldAlign = Math.Max(1, explicitStructAlign);

            for (int i = 0; i < fields.Count; i++)
            {
                PolyglotFieldDecl f = fields[i];
                int effAlign = Math.Max(1, Math.Min(f.NaturalAlign, st.PackRule));
                if (effAlign > maxFieldAlign) maxFieldAlign = effAlign;

                int rem = offset % effAlign;
                int pad = (rem == 0) ? 0 : (effAlign - rem);
                offset += pad;

                ComputedFieldLayout cf = new ComputedFieldLayout();
                cf.Name = f.Name;
                cf.Offset = offset;
                cf.Size = f.ByteSize;
                cf.AlignUsed = effAlign;
                cf.ImplicitPadBefore = pad;
                st.Fields.Add(cf);

                offset += f.ByteSize;
            }

            int structAlign = Math.Max(1, Math.Min(maxFieldAlign, (packOverride > 0) ? Math.Max(packOverride, explicitStructAlign) : maxFieldAlign));
            int tailRem = offset % structAlign;
            int tailPad = (tailRem == 0) ? 0 : (structAlign - tailRem);

            st.StructAlign = structAlign;
            st.TailPadding = tailPad;
            st.TotalSize = offset + tailPad;
            return st;
        }

        private static int PairwiseLayoutDefect(StalkStructLayout a, StalkStructLayout b)
        {
            if (a.Fields.Count != b.Fields.Count) return 9999;
            int defect = Math.Abs(a.TotalSize - b.TotalSize) + Math.Abs(a.StructAlign - b.StructAlign);
            for (int i = 0; i < a.Fields.Count; i++)
            {
                defect += Math.Abs(a.Fields[i].Offset - b.Fields[i].Offset);
                defect += Math.Abs(a.Fields[i].Size - b.Fields[i].Size);
            }
            return defect;
        }

        public static SheafAbiAuditReport AuditPolyglotStructTriple(
            string structName,
            List<PolyglotFieldDecl> fields,
            int rustPack, int rustExplicitAlign,
            int cppPack, int cppExplicitAlign,
            int csPack, int csExplicitAlign)
        {
            SheafAbiAuditReport rep = new SheafAbiAuditReport();
            rep.StructName = structName;
            rep.RustStalk = ComputeAbiStalkLayout("Rust #[repr(C)]", fields, rustPack, rustExplicitAlign);
            rep.CppStalk = ComputeAbiStalkLayout("C++23 F4SE/SKSE64", fields, cppPack, cppExplicitAlign);
            rep.CSharpStalk = ComputeAbiStalkLayout("C# P/Invoke x64", fields, csPack, csExplicitAlign);

            int dRC = PairwiseLayoutDefect(rep.RustStalk, rep.CppStalk);
            int dCS = PairwiseLayoutDefect(rep.CppStalk, rep.CSharpStalk);
            int dRS = PairwiseLayoutDefect(rep.RustStalk, rep.CSharpStalk);
            rep.TotalCoboundaryDefectBytes = dRC + dCS + dRS;
            rep.H1ZeroExact = (rep.TotalCoboundaryDefectBytes == 0);

            if (rep.H1ZeroExact)
            {
                rep.Diagnostic = string.Format(CultureInfo.InvariantCulture,
                    "H^1 = 0 EXACT ABI CONSENSUS ({0} bytes, align={1}, 0 byte drift across Rust/C++23/C#)",
                    rep.RustStalk.TotalSize, rep.RustStalk.StructAlign);
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "H^1 != 0 ABI OBSTRUCTION (defect={0}B | Rust={1}B, C++23={2}B, C#={3}B): ",
                    rep.TotalCoboundaryDefectBytes, rep.RustStalk.TotalSize, rep.CppStalk.TotalSize, rep.CSharpStalk.TotalSize);
                for (int i = 0; i < fields.Count; i++)
                {
                    int oR = rep.RustStalk.Fields[i].Offset;
                    int oC = rep.CppStalk.Fields[i].Offset;
                    int oS = rep.CSharpStalk.Fields[i].Offset;
                    if (oR != oC || oC != oS)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "field '{0}' offsets [Rust={1}, C++={2}, C#={3}]; ",
                            fields[i].Name, oR, oC, oS);
                    }
                }
                rep.Diagnostic = sb.ToString();
            }
            return rep;
        }

        public static string GenerateLockstepPolyglotHeaders(string structName, List<PolyglotFieldDecl> fields, out StalkStructLayout canonicalLayout)
        {
            canonicalLayout = ComputeAbiStalkLayout("Canonical x86_64/ARM64 C ABI", fields, 0, 8);
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("// ============================================================================");
            sb.AppendLine("// 1. RUST #[repr(C)] LOCKSTEP STALK (H^1 = 0 VERIFIED)");
            sb.AppendLine("// ============================================================================");
            sb.AppendLine("#[repr(C, align(" + canonicalLayout.StructAlign.ToString(CultureInfo.InvariantCulture) + "))]");
            sb.AppendLine("#[derive(Clone, Copy, Debug)]");
            sb.AppendLine("pub struct " + structName + " {");
            for (int i = 0; i < fields.Count; i++)
            {
                ComputedFieldLayout cf = canonicalLayout.Fields[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "    pub {0}: {1}, // offset={2}, size={3}\r\n",
                    cf.Name, fields[i].RustType, cf.Offset, cf.Size);
            }
            sb.AppendLine("}");
            sb.AppendFormat(CultureInfo.InvariantCulture, "const _: [(); {0}] = [(); core::mem::size_of::<{1}>()];\r\n\r\n",
                canonicalLayout.TotalSize, structName);

            sb.AppendLine("// ============================================================================");
            sb.AppendLine("// 2. C++23 SKSE64 / F4SE NATIVE PLUGIN STALK (STATIC_ASSERT LOCKED)");
            sb.AppendLine("// ============================================================================");
            sb.AppendLine("#include <cstddef>");
            sb.AppendLine("#include <cstdint>");
            sb.AppendLine("struct alignas(" + canonicalLayout.StructAlign.ToString(CultureInfo.InvariantCulture) + ") " + structName + " {");
            for (int i = 0; i < fields.Count; i++)
            {
                ComputedFieldLayout cf = canonicalLayout.Fields[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "    {0} {1}; // offset={2}\r\n",
                    fields[i].CppType, cf.Name, cf.Offset);
            }
            sb.AppendLine("};");
            sb.AppendFormat(CultureInfo.InvariantCulture, "static_assert(sizeof({0}) == {1}, \"Sheaf FFI size mismatch\");\r\n",
                structName, canonicalLayout.TotalSize);
            for (int i = 0; i < fields.Count; i++)
            {
                ComputedFieldLayout cf = canonicalLayout.Fields[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "static_assert(offsetof({0}, {1}) == {2}, \"Sheaf FFI offset mismatch\");\r\n",
                    structName, cf.Name, cf.Offset);
            }
            sb.AppendLine();

            sb.AppendLine("// ============================================================================");
            sb.AppendLine("// 3. C# 5.0 EXPLICIT-OFFSET P/INVOKE STALK (ZERO PACK DRIFT)");
            sb.AppendLine("// ============================================================================");
            sb.AppendFormat(CultureInfo.InvariantCulture, "[StructLayout(LayoutKind.Explicit, Size = {0})]\r\n", canonicalLayout.TotalSize);
            sb.AppendLine("public struct " + structName + " {");
            for (int i = 0; i < fields.Count; i++)
            {
                ComputedFieldLayout cf = canonicalLayout.Fields[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "    [FieldOffset({0})] public {1} {2};\r\n",
                    cf.Offset, fields[i].CSharpType, cf.Name);
            }
            sb.AppendLine("}");

            return sb.ToString();
        }

        // =====================================================================
        // 2. CREATION ENGINE LOAD-ORDER, ESL 12-BIT FORMID & QUEST DAG AUDITOR
        // =====================================================================
        public static LoadOrderSheafReport AuditModLoadOrderAndQuestGraph(List<ModPluginDescriptor> loadOrder, List<QuestStageNode> questStages)
        {
            LoadOrderSheafReport rep = new LoadOrderSheafReport();
            HashSet<string> loadedSoFar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (loadOrder != null)
            {
                for (int i = 0; i < loadOrder.Count; i++)
                {
                    ModPluginDescriptor p = loadOrder[i];
                    for (int m = 0; m < p.RequiredMasters.Count; m++)
                    {
                        string req = p.RequiredMasters[m];
                        if (!loadedSoFar.Contains(req))
                        {
                            rep.MissingMasterOrderViolations++;
                            rep.Findings.Add("[MASTER ORDER PARADOX] '" + p.FileName + "' loads BEFORE required master '" + req + "'!");
                        }
                    }
                    if (p.IsLightEsl && p.NewRecordCount > 4096)
                    {
                        rep.EslOverflowCount++;
                        rep.Findings.Add(string.Format(CultureInfo.InvariantCulture,
                            "[ESL FE-XXX OVERFLOW] '{0}' defines {1} new records (> 4,096 12-bit 0x000..0xFFF light-slot ceiling -> save corruption!)",
                            p.FileName, p.NewRecordCount));
                    }
                    if (p.DeletedRefCount > 0)
                    {
                        rep.DeletedRefHazards += p.DeletedRefCount;
                        rep.Findings.Add(string.Format(CultureInfo.InvariantCulture,
                            "[UDR CTD HAZARD] '{0}' contains {1} hard-deleted worldspace reference(s) -> convert to Initially Disabled + Z=-30000!",
                            p.FileName, p.DeletedRefCount));
                    }
                    loadedSoFar.Add(p.FileName);
                }
            }

            if (questStages != null && questStages.Count > 0)
            {
                Dictionary<int, QuestStageNode> map = new Dictionary<int, QuestStageNode>();
                HashSet<int> resolved = new HashSet<int>();
                for (int i = 0; i < questStages.Count; i++)
                    map[questStages[i].StageId] = questStages[i];

                bool progress = true;
                while (progress)
                {
                    progress = false;
                    for (int i = 0; i < questStages.Count; i++)
                    {
                        QuestStageNode n = questStages[i];
                        if (resolved.Contains(n.StageId)) continue;
                        bool allMet = true;
                        for (int k = 0; k < n.PrereqStages.Count; k++)
                        {
                            int pre = n.PrereqStages[k];
                            if (!resolved.Contains(pre)) { allMet = false; break; }
                        }
                        if (allMet)
                        {
                            resolved.Add(n.StageId);
                            progress = true;
                        }
                    }
                }

                for (int i = 0; i < questStages.Count; i++)
                {
                    QuestStageNode n = questStages[i];
                    if (!resolved.Contains(n.StageId))
                    {
                        rep.CycleStageCount++;
                        if (n.IsCompletionStage) rep.UnreachableCompletionCount++;
                        rep.Findings.Add(string.Format(CultureInfo.InvariantCulture,
                            "[QUEST SOFTLOCK H^1 != 0] Stage {0} ('{1}' from {2}) is trapped in a prerequisite cycle/missing stage!",
                            n.StageId, n.Description, n.SourcePlugin));
                    }
                }
            }

            rep.H1ZeroExact = (rep.CycleStageCount == 0 &&
                               rep.UnreachableCompletionCount == 0 &&
                               rep.EslOverflowCount == 0 &&
                               rep.DeletedRefHazards == 0 &&
                               rep.MissingMasterOrderViolations == 0);
            if (rep.H1ZeroExact)
                rep.Findings.Add("[H^1 = 0 CERTIFIED] Load order, FE-xxx ESL bounds, UDR hygiene, and Quest DAG reachability verified paradox-free!");
            return rep;
        }

        // =====================================================================
        // 3. FAIL-CLOSED SKSE64 / F4SE WILDCARD AOB BINARY SIGNATURE SCANNER
        // =====================================================================
        public static bool TryParseAobPattern(string sigText, out ushort[] pattern)
        {
            pattern = null;
            if (string.IsNullOrEmpty(sigText)) return false;
            string[] toks = sigText.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) return false;
            ushort[] p = new ushort[toks.Length];
            for (int i = 0; i < toks.Length; i++)
            {
                string t = toks[i];
                if (t == "?" || t == "??")
                {
                    p[i] = 0xFFFF;
                }
                else
                {
                    byte b;
                    if (!byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                        return false;
                    p[i] = b;
                }
            }
            pattern = p;
            return true;
        }

        public static AobScanReport ScanPeSectionForUniqueSignature(string hookName, byte[] peTextBytes, string sigText)
        {
            AobScanReport rep = new AobScanReport();
            rep.HookName = hookName;
            rep.SignatureText = sigText;
            rep.UniqueOffsetRva = -1;

            ushort[] pat;
            if (!TryParseAobPattern(sigText, out pat))
            {
                rep.Verdict = HookGateVerdict.QuarantinedMalformedPattern;
                rep.Detail = "Malformed hex/wildcard AOB token -> trampoline installation blocked";
                return rep;
            }
            if (peTextBytes == null || peTextBytes.Length < pat.Length)
            {
                rep.Verdict = HookGateVerdict.QuarantinedZeroMatch;
                rep.Detail = "Target PE .text buffer shorter than signature -> trampoline blocked";
                return rep;
            }

            int limit = peTextBytes.Length - pat.Length;
            int count = 0;
            int first = -1;
            for (int pos = 0; pos <= limit; pos++)
            {
                bool ok = true;
                for (int k = 0; k < pat.Length; k++)
                {
                    ushort want = pat[k];
                    if (want <= 0x00FF && peTextBytes[pos + k] != (byte)want)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    if (count == 0) first = pos;
                    count++;
                }
            }

            rep.MatchCount = count;
            if (count == 1)
            {
                rep.Verdict = HookGateVerdict.ArmedUniqueMatch;
                rep.UniqueOffsetRva = 0x1000 + first;
                rep.Detail = string.Format(CultureInfo.InvariantCulture,
                    "ARMED SAFE: Exact unique match (1/1) at .text RVA +0x{0:X6}", rep.UniqueOffsetRva);
            }
            else if (count == 0)
            {
                rep.Verdict = HookGateVerdict.QuarantinedZeroMatch;
                rep.Detail = "QUARANTINED (0 matches): Game executable updated -> clean skip without CTD";
            }
            else
            {
                rep.Verdict = HookGateVerdict.QuarantinedAmbiguousMultiMatch;
                rep.Detail = string.Format(CultureInfo.InvariantCulture,
                    "QUARANTINED ({0} ambiguous matches): Signature too short -> blocked to prevent random vtable corruption", count);
            }
            return rep;
        }

        // =====================================================================
        // 4. SEH MINIDUMP & CRASH-RING EXCEPTION ROOT-CAUSE SURGEON
        // =====================================================================
        public static CrashTriageReport DiagnoseSehCrashRecord(uint exceptionCode, ulong ripAddr, ulong faultPtrReg)
        {
            CrashTriageReport r = new CrashTriageReport();
            r.ExceptionCode = exceptionCode;
            r.RipAddress = ripAddr;

            // Canonical module map for Fallout 4 / Skyrim SE + F4SE/SKSE64 + Sovereign native plugins
            string[] modNames = new string[] { "Fallout4.exe", "f4se_1_10_163.dll", "SovereignPerfF4SE.dll", "ntdll.dll" };
            ulong[] modBases = new ulong[] { 0x140000000UL, 0x7FF810000000UL, 0x7FF820000000UL, 0x7FF900000000UL };
            ulong[] modSizes = new ulong[] { 0x006800000UL, 0x0000200000UL, 0x0000180000UL, 0x0000240000UL };

            r.FaultingModule = "UnknownJITOrUnloadedDLL";
            r.ModuleRvaOffset = ripAddr;
            for (int i = 0; i < modNames.Length; i++)
            {
                if (ripAddr >= modBases[i] && ripAddr < modBases[i] + modSizes[i])
                {
                    r.FaultingModule = modNames[i];
                    r.ModuleRvaOffset = ripAddr - modBases[i];
                    break;
                }
            }

            if (exceptionCode == 0xC0000005u)
            {
                r.ExceptionName = "EXCEPTION_ACCESS_VIOLATION (0xC0000005)";
                if (faultPtrReg < 0x10000UL)
                {
                    r.RootCauseCategory = "Null TESForm* / Deleted Object Reference Dereference";
                    r.SurgicalPrescription = "Guard native pointer with null/IsDeleted() check & clean UDR records in overriding .esp";
                }
                else
                {
                    r.RootCauseCategory = "Dangling VTable / Unaligned #[repr(C)] FFI Pointer Read";
                    r.SurgicalPrescription = "Run Tab 1 Polyglot Sheaf Verifier (H^1=0) to fix 4-byte struct alignment drift";
                }
            }
            else if (exceptionCode == 0xC00000FDu)
            {
                r.ExceptionName = "EXCEPTION_STACK_OVERFLOW (0xC00000FD)";
                r.RootCauseCategory = "Infinite OnHit / Papyrus Event Hook Recursion";
                r.SurgicalPrescription = "Add thread-local re-entrancy guard (tls_in_hook) around F4SE/SKSE64 event sink";
            }
            else if (exceptionCode == 0xC0000374u)
            {
                r.ExceptionName = "STATUS_HEAP_CORRUPTION (0xC0000374)";
                r.RootCauseCategory = "Cross-CRT Heap Free or Undersized P/Invoke Buffer";
                r.SurgicalPrescription = "Allocate/free via game Scaleform/MemoryManager heap & match sizeof(T) across FFI";
            }
            else if (exceptionCode == 0xC000001Du)
            {
                r.ExceptionName = "EXCEPTION_ILLEGAL_INSTRUCTION (0xC000001D)";
                r.RootCauseCategory = "Ungated AVX-512 Opcode on AVX2-Only Alder Lake / Zen CPU";
                r.SurgicalPrescription = "Gate SIMD dispatch behind CPUID AVX2 check (LOQ 15 i5-12450HX supports AVX2+FMA)";
            }
            else
            {
                r.ExceptionName = string.Format(CultureInfo.InvariantCulture, "SEH_CODE_0x{0:X8}", exceptionCode);
                r.RootCauseCategory = "Unhandled Native Plugin Fault";
                r.SurgicalPrescription = "Inspect crash-ring breadcrumb slot prior to RIP " + r.FaultingModule;
            }
            return r;
        }
    }
}
