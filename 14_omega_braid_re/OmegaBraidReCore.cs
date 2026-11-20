using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmegaBraidRE
{
    public enum BraidArchIsa
    {
        Unknown = 0,
        X86_64 = 1,
        Arm64 = 2,
        DotNetCil = 3
    }

    public enum BraidContainerKind
    {
        RawSlice = 0,
        Pe32Plus = 1,
        Pe32 = 2,
        Elf64 = 3
    }

    public enum BraidMicroOp
    {
        Nop = 0,
        MovReg = 1,
        MovImm = 2,
        LeaAddr = 3,
        LoadMem = 4,
        StoreMem = 5,
        Add = 6,
        Sub = 7,
        Xor = 8,
        And = 9,
        Orr = 10,
        CmpFlags = 11,
        FloatOp = 12,
        Push = 13,
        Pop = 14,
        CallDirect = 15,
        CallVTable = 16,
        BrCond = 17,
        Jmp = 18,
        Ret = 19,
        Phi = 20
    }

    public enum BraidFieldType
    {
        Unknown = 0,
        U8 = 1,
        U16 = 2,
        U32 = 3,
        U64 = 4,
        F32 = 5,
        F64 = 6,
        VTablePtr64 = 7
    }

    public sealed class BinaryContainerReport
    {
        public BraidContainerKind Format = BraidContainerKind.RawSlice;
        public BraidArchIsa Isa = BraidArchIsa.Unknown;
        public bool Valid = false;
        public ulong ImageBase = 0UL;
        public ulong EntryRva = 0UL;
        public ulong TextRva = 0UL;
        public ulong TextRawOffset = 0UL;
        public ulong TextSize = 0UL;
        public int SectionCount = 0;
        public uint ExportDirRva = 0u;
        public uint ExportDirSize = 0u;
        public uint ImportDirRva = 0u;
        public uint ImportDirSize = 0u;
        public uint PdataDirRva = 0u;
        public uint PdataDirSize = 0u;
        public uint ClrDirRva = 0u;
        public uint ClrDirSize = 0u;
        public string ExportModuleName = "";
        public double TextEntropyBits = 0.0;
        public float[] HistogramNorm256 = new float[256];
        public List<PeSectionDesc> Sections = new List<PeSectionDesc>();
        public List<PdataFunctionEntry> PdataFunctions = new List<PdataFunctionEntry>();
        public List<PeExportEntry> Exports = new List<PeExportEntry>();
        public List<PeImportEntry> Imports = new List<PeImportEntry>();
        public Dictionary<uint, string> ExportRvaToName = new Dictionary<uint, string>();
        public Dictionary<uint, string> IatSlotRvaToSymbol = new Dictionary<uint, string>();
        public ClrAssemblyReport ClrInfo = new ClrAssemblyReport();
    }

    public sealed class BraidSsaInstruction
    {
        public int Offset;
        public int Length;
        public BraidMicroOp Op;
        public int DstReg;
        public int SrcA;
        public int SrcB;
        public int SsaDstVer;
        public int SsaSrcAVer;
        public int SsaSrcBVer;
        public long DispOrImm;
        public int MemWidth;
        public bool SetsFlags;
        public bool UsesFlags;
        public bool DeadFlagsEliminated;
        public bool IsFloat;
        public int TargetOffset;
        public string MnemonicText = "";
        public string ResolvedAnnotation = "";
    }

    public sealed class BraidBasicBlockNode
    {
        public int Id;
        public int StartOffset;
        public int EndOffset;
        public int InsnStart;
        public int InsnCount;
        public int Succ0 = -1;
        public int Succ1 = -1;
        public int PredCount = 0;
        public bool HasPhiMerge = false;
    }

    public sealed class SheafFieldSlot
    {
        public uint Offset;
        public int Width;
        public BraidFieldType FieldType;
        public int AccessMask; // 1=Read, 2=Write, 4=VCall
        public bool ConflictH1;
    }

    public sealed class LiftedBraidFunction
    {
        public string Name = "sub_unknown";
        public BraidArchIsa Isa = BraidArchIsa.X86_64;
        public ulong BaseRva = 0UL;
        public int ByteLength = 0;
        public int LoopBackedges = 0;
        public int EdgeCount = 0;
        public int TotalFlagsSet = 0;
        public int DeadFlagsKilled = 0;
        public int PhiInserted = 0;
        public int H1DefectCount = 0;
        public bool DiscoveredViaPdata = false;
        public byte PdataPrologSize = 0;
        public sbyte[] E8TopoRoot2x = new sbyte[8];
        public sbyte[] E8DataRoot2x = new sbyte[8];
        public float[] RawFeatures16 = new float[16];
        public string WildcardAobSignature = "";
        public List<BraidSsaInstruction> Instructions = new List<BraidSsaInstruction>();
        public List<BraidBasicBlockNode> Blocks = new List<BraidBasicBlockNode>();
        public List<SheafFieldSlot> Fields = new List<SheafFieldSlot>();
        public List<uint> VTableSlots = new List<uint>();
        public List<string> ReferencedStrings = new List<string>();
        public List<string> ImportedCalls = new List<string>();
    }

    public sealed class CrossPatchMatchEntry
    {
        public string OldName = "";
        public ulong OldRva = 0UL;
        public string NewName = "";
        public ulong NewRva = 0UL;
        public long RvaDelta = 0L;
        public float CombinedScore = 0.0f;
        public int DualE8Dot = 0;
        public float Cosine16 = 0.0f;
        public string MigratedAob = "";
    }

    public static class OmegaBraidReCore
    {
        private static readonly string[] X64RegNames = new string[]
        {
            "rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi",
            "r8","r9","r10","r11","r12","r13","r14","r15","rip"
        };

        public static ushort ReadU16Le(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        public static uint ReadU32Le(byte[] b, int o)
        {
            return (uint)b[o]
                | ((uint)b[o + 1] << 8)
                | ((uint)b[o + 2] << 16)
                | ((uint)b[o + 3] << 24);
        }

        public static ulong ReadU64Le(byte[] b, int o)
        {
            return (ulong)ReadU32Le(b, o) | ((ulong)ReadU32Le(b, o + 4) << 32);
        }

        public static BinaryContainerReport InspectContainer(byte[] buf)
        {
            BinaryContainerReport r = new BinaryContainerReport();
            if (buf == null || buf.Length == 0) return r;

            ComputeHistogramAndEntropy(buf, 0, Math.Min(buf.Length, 262144), r.HistogramNorm256, out r.TextEntropyBits);
            if (buf.Length < 64) return r;

            if (buf[0] == (byte)'M' && buf[1] == (byte)'Z')
            {
                uint lfanew = ReadU32Le(buf, 0x3C);
                if (buf.Length >= 136 && (ulong)lfanew + 136UL <= (ulong)buf.Length &&
                    buf[lfanew] == (byte)'P' && buf[lfanew + 1] == (byte)'E' &&
                    buf[lfanew + 2] == 0 && buf[lfanew + 3] == 0)
                {
                    int pe = (int)lfanew;
                    ushort machine = ReadU16Le(buf, pe + 4);
                    ushort nsects = ReadU16Le(buf, pe + 6);
                    ushort optSz = ReadU16Le(buf, pe + 20);
                    int optOff = pe + 24;
                    if (optSz >= 96 && optOff + optSz <= buf.Length)
                    {
                        ushort magic = ReadU16Le(buf, optOff);
                        if (magic == 0x020B || magic == 0x010B)
                        {
                            bool isPe64 = (magic == 0x020B);
                            r.Format = isPe64 ? BraidContainerKind.Pe32Plus : BraidContainerKind.Pe32;
                            r.Isa = (machine == 0xAA64) ? BraidArchIsa.Arm64 : BraidArchIsa.X86_64;
                            r.EntryRva = ReadU32Le(buf, optOff + 16);
                            r.ImageBase = isPe64 ? ReadU64Le(buf, optOff + 24) : (ulong)ReadU32Le(buf, optOff + 28);
                            r.SectionCount = nsects;
                            r.Valid = true;

                            int ddCountOff = isPe64 ? (optOff + 108) : (optOff + 92);
                            int ddBase = isPe64 ? (optOff + 112) : (optOff + 96);
                            int numDd = 0;
                            if (ddCountOff + 4 <= optOff + optSz)
                            {
                                uint rawDd = ReadU32Le(buf, ddCountOff);
                                numDd = (rawDd > 16u) ? 16 : (int)rawDd;
                            }

                            if (0 < numDd && ddBase + 8 <= optOff + optSz)
                            {
                                r.ExportDirRva = ReadU32Le(buf, ddBase);
                                r.ExportDirSize = ReadU32Le(buf, ddBase + 4);
                            }
                            if (1 < numDd && ddBase + 16 <= optOff + optSz)
                            {
                                r.ImportDirRva = ReadU32Le(buf, ddBase + 8);
                                r.ImportDirSize = ReadU32Le(buf, ddBase + 12);
                            }
                            if (3 < numDd && ddBase + 32 <= optOff + optSz)
                            {
                                r.PdataDirRva = ReadU32Le(buf, ddBase + 24);
                                r.PdataDirSize = ReadU32Le(buf, ddBase + 28);
                            }
                            if (14 < numDd && ddBase + 120 <= optOff + optSz)
                            {
                                r.ClrDirRva = ReadU32Le(buf, ddBase + 112);
                                r.ClrDirSize = ReadU32Le(buf, ddBase + 116);
                            }

                            int secTable = optOff + optSz;
                            for (int i = 0; i < nsects && i < 64 && secTable + (i + 1) * 40 <= buf.Length; i++)
                            {
                                int sh = secTable + i * 40;
                                StringBuilder sn = new StringBuilder(8);
                                for (int c = 0; c < 8; c++)
                                {
                                    byte chb = buf[sh + c];
                                    if (chb == 0) break;
                                    if (chb >= 0x20 && chb <= 0x7E) sn.Append((char)chb);
                                }

                                PeSectionDesc sd = new PeSectionDesc();
                                sd.Name = sn.ToString();
                                sd.VirtualSize = ReadU32Le(buf, sh + 8);
                                sd.VirtualAddress = ReadU32Le(buf, sh + 12);
                                sd.RawSize = ReadU32Le(buf, sh + 16);
                                sd.RawOffset = ReadU32Le(buf, sh + 20);
                                sd.Characteristics = ReadU32Le(buf, sh + 36);
                                r.Sections.Add(sd);

                                bool isText = (sd.Name == ".text") || ((sd.Characteristics & 0x20000000u) != 0u);
                                if (r.TextRva == 0UL && isText)
                                {
                                    r.TextRva = sd.VirtualAddress;
                                    r.TextSize = (sd.VirtualSize == 0u || (sd.RawSize > 0u && sd.RawSize < sd.VirtualSize))
                                        ? sd.RawSize
                                        : sd.VirtualSize;
                                    r.TextRawOffset = sd.RawOffset;
                                    if (r.TextRawOffset < (ulong)buf.Length)
                                    {
                                        int slen = (int)Math.Min((ulong)buf.Length - r.TextRawOffset, Math.Min(r.TextSize, 262144UL));
                                        ComputeHistogramAndEntropy(buf, (int)r.TextRawOffset, slen, r.HistogramNorm256, out r.TextEntropyBits);
                                    }
                                }
                            }

                            OmegaBraidRePeAndClr.ParsePeDirectories(buf, r);
                        }
                    }
                }
                return r;
            }

            if (buf[0] == 0x7F && buf[1] == (byte)'E' && buf[2] == (byte)'L' && buf[3] == (byte)'F' && buf[4] == 2 && buf[5] == 1)
            {
                ushort machine = ReadU16Le(buf, 18);
                r.Format = BraidContainerKind.Elf64;
                r.Isa = (machine == 0x00B7) ? BraidArchIsa.Arm64 : BraidArchIsa.X86_64;
                r.EntryRva = ReadU64Le(buf, 24);
                r.ImageBase = 0x400000UL;
                ulong phoff = ReadU64Le(buf, 32);
                ushort phent = ReadU16Le(buf, 54);
                ushort phnum = ReadU16Le(buf, 56);
                r.SectionCount = phnum;
                r.Valid = true;
                if (phent >= 56 && phoff < (ulong)buf.Length)
                {
                    for (int i = 0; i < phnum && (ulong)((i + 1) * phent) <= (ulong)buf.Length - phoff; i++)
                    {
                        int ph = (int)phoff + i * phent;
                        uint pType = ReadU32Le(buf, ph);
                        uint pFlags = ReadU32Le(buf, ph + 4);
                        if (pType == 1u && (pFlags & 1u) != 0u)
                        {
                            r.TextRawOffset = ReadU64Le(buf, ph + 8);
                            r.TextRva = ReadU64Le(buf, ph + 16);
                            r.TextSize = ReadU64Le(buf, ph + 32);
                            break;
                        }
                    }
                }
            }
            return r;
        }

        public static void ComputeHistogramAndEntropy(byte[] buf, int start, int length, float[] histNorm256, out double entropyBits)
        {
            entropyBits = 0.0;
            for (int i = 0; i < 256; i++) histNorm256[i] = 0.0f;
            if (buf == null || length <= 0 || start < 0 || start >= buf.Length) return;

            int end = Math.Min(buf.Length, start + length);
            int actual = end - start;
            if (actual <= 0) return;

            int[] counts = new int[256];
            for (int i = start; i < end; i++) counts[buf[i]]++;

            int maxCnt = 1;
            double invN = 1.0 / (double)actual;
            double ent = 0.0;
            for (int i = 0; i < 256; i++)
            {
                if (counts[i] > maxCnt) maxCnt = counts[i];
                if (counts[i] > 0)
                {
                    double p = counts[i] * invN;
                    ent -= p * (Math.Log(p) / Math.Log(2.0));
                }
            }
            for (int i = 0; i < 256; i++) histNorm256[i] = (float)counts[i] / (float)maxCnt;
            entropyBits = ent;
        }

        public static void SnapToE8MinimalRoot2x(float[] in8, int offset, sbyte[] out2x)
        {
            double normSq = 0.0;
            for (int i = 0; i < 8; i++)
            {
                double x = in8[offset + i];
                normSq += x * x;
            }
            if (normSq < 1e-12)
            {
                for (int i = 0; i < 8; i++) out2x[i] = 0;
                out2x[0] = 2;
                out2x[1] = 2;
                return;
            }

            double scale = Math.Sqrt(2.0 / normSq);
            double[] v = new double[8];
            for (int i = 0; i < 8; i++) v[i] = in8[offset + i] * scale;

            double bestD8 = -1e30;
            int bestI = 0, bestJ = 1, bestSi = 2, bestSj = 2;
            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    for (int si = -1; si <= 1; si += 2)
                    {
                        for (int sj = -1; sj <= 1; sj += 2)
                        {
                            double dot = si * v[i] + sj * v[j];
                            if (dot > bestD8)
                            {
                                bestD8 = dot;
                                bestI = i;
                                bestJ = j;
                                bestSi = si * 2;
                                bestSj = sj * 2;
                            }
                        }
                    }
                }
            }

            sbyte[] half2x = new sbyte[8];
            int negCnt = 0;
            int weakestIdx = 0;
            double weakestAbs = 1e30;
            for (int i = 0; i < 8; i++)
            {
                if (v[i] < 0.0)
                {
                    half2x[i] = -1;
                    negCnt++;
                }
                else
                {
                    half2x[i] = 1;
                }
                double av = Math.Abs(v[i]);
                if (av < weakestAbs)
                {
                    weakestAbs = av;
                    weakestIdx = i;
                }
            }
            if ((negCnt & 1) != 0) half2x[weakestIdx] = (sbyte)(-half2x[weakestIdx]);

            double dotHalf = 0.0;
            for (int i = 0; i < 8; i++) dotHalf += 0.5 * half2x[i] * v[i];

            if (bestD8 >= dotHalf)
            {
                for (int i = 0; i < 8; i++) out2x[i] = 0;
                out2x[bestI] = (sbyte)bestSi;
                out2x[bestJ] = (sbyte)bestSj;
            }
            else
            {
                for (int i = 0; i < 8; i++) out2x[i] = half2x[i];
            }
        }

        public static bool VerifyE8Root2xNorm(sbyte[] r2)
        {
            if (r2 == null || r2.Length != 8) return false;
            int sq = 0;
            for (int i = 0; i < 8; i++) sq += (int)r2[i] * (int)r2[i];
            return sq == 8;
        }

        private static int DecodeX64ModRm(byte[] code, int p, int end, byte rex, out int reg, out int baseReg, out long disp, out bool hasMem)
        {
            reg = 0;
            baseReg = 0;
            disp = 0L;
            hasMem = false;
            if (p >= end) return 0;

            byte modrm = code[p];
            int mod = (modrm >> 6) & 3;
            reg = ((modrm >> 3) & 7) | (((rex & 4) != 0) ? 8 : 0);
            int rm = (modrm & 7) | (((rex & 1) != 0) ? 8 : 0);

            if (mod == 3)
            {
                baseReg = rm;
                hasMem = false;
                return 1;
            }

            hasMem = true;
            int idx = p + 1;
            int b = rm;
            if ((modrm & 7) == 4)
            {
                if (idx >= end) return 0;
                byte sib = code[idx++];
                b = (sib & 7) | (((rex & 1) != 0) ? 8 : 0);
            }

            if (mod == 0 && (b & 7) == 5)
            {
                if (idx + 4 > end) return 0;
                disp = (int)ReadU32Le(code, idx);
                baseReg = 16; // RIP
                return (idx + 4) - p;
            }
            if (mod == 1)
            {
                if (idx + 1 > end) return 0;
                disp = (sbyte)code[idx];
                baseReg = b;
                return (idx + 1) - p;
            }
            if (mod == 2)
            {
                if (idx + 4 > end) return 0;
                disp = (int)ReadU32Le(code, idx);
                baseReg = b;
                return (idx + 4) - p;
            }
            baseReg = b;
            return idx - p;
        }

        private static string RegName(BraidArchIsa isa, int r)
        {
            if (isa == BraidArchIsa.Arm64)
            {
                if (r == 31) return "sp/xzr";
                return "x" + r.ToString(CultureInfo.InvariantCulture);
            }
            if (r >= 0 && r < X64RegNames.Length) return X64RegNames[r];
            return "r" + r.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatRipOrDisp(int baseReg, long disp)
        {
            string bName = RegName(BraidArchIsa.X86_64, baseReg);
            if (disp < 0) return string.Format(CultureInfo.InvariantCulture, "[{0}-0x{1:X}]", bName, -disp);
            return string.Format(CultureInfo.InvariantCulture, "[{0}+0x{1:X}]", bName, disp);
        }

        private static BraidSsaInstruction DecodeOneX64(byte[] code, int start, int end, int relOff)
        {
            BraidSsaInstruction ins = new BraidSsaInstruction();
            ins.Offset = relOff;
            ins.TargetOffset = -1;
            int p = start;
            bool op66 = false;
            byte scalarPfx = 0;

            if (p < end && code[p] == 0x66) { op66 = true; p++; }
            if (p < end && (code[p] == 0xF3 || code[p] == 0xF2)) { scalarPfx = code[p]; p++; }

            if (scalarPfx == 0xF3 && p + 3 <= end && code[p] == 0x0F && code[p + 1] == 0x1E && code[p + 2] == 0xFA)
            {
                ins.Op = BraidMicroOp.Nop;
                ins.Length = (p + 3) - start;
                ins.MnemonicText = "endbr64";
                return ins;
            }

            byte rex = 0;
            if (p < end && code[p] >= 0x40 && code[p] <= 0x4F) { rex = code[p++]; }
            if (p >= end) { ins.Op = BraidMicroOp.Nop; ins.Length = 1; ins.MnemonicText = "db"; return ins; }

            int width = ((rex & 8) != 0) ? 8 : (op66 ? 2 : 4);
            byte opc = code[p++];

            if (opc == 0x90) { ins.Op = BraidMicroOp.Nop; ins.Length = p - start; ins.MnemonicText = "nop"; return ins; }
            if (opc == 0xC3) { ins.Op = BraidMicroOp.Ret; ins.Length = p - start; ins.MnemonicText = "ret"; return ins; }

            if (opc >= 0x50 && opc <= 0x57)
            {
                ins.Op = BraidMicroOp.Push;
                ins.SrcA = (opc - 0x50) | (((rex & 1) != 0) ? 8 : 0);
                ins.DstReg = 4;
                ins.Length = p - start;
                ins.MnemonicText = "push " + RegName(BraidArchIsa.X86_64, ins.SrcA);
                return ins;
            }
            if (opc >= 0x58 && opc <= 0x5F)
            {
                ins.Op = BraidMicroOp.Pop;
                ins.DstReg = (opc - 0x58) | (((rex & 1) != 0) ? 8 : 0);
                ins.SrcA = 4;
                ins.Length = p - start;
                ins.MnemonicText = "pop " + RegName(BraidArchIsa.X86_64, ins.DstReg);
                return ins;
            }
            if (opc == 0xE8 && p + 4 <= end)
            {
                int rel = (int)ReadU32Le(code, p);
                ins.Op = BraidMicroOp.CallDirect;
                ins.DstReg = 0;
                ins.DispOrImm = rel;
                ins.Length = (p + 4) - start;
                ins.TargetOffset = relOff + ins.Length + rel;
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "call rel32 (0x{0:X})", ins.TargetOffset);
                return ins;
            }
            if (opc == 0xE9 && p + 4 <= end)
            {
                int rel = (int)ReadU32Le(code, p);
                ins.Op = BraidMicroOp.Jmp;
                ins.Length = (p + 4) - start;
                ins.TargetOffset = relOff + ins.Length + rel;
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "jmp 0x{0:X}", ins.TargetOffset);
                return ins;
            }
            if (opc == 0xEB && p + 1 <= end)
            {
                int rel = (sbyte)code[p];
                ins.Op = BraidMicroOp.Jmp;
                ins.Length = (p + 1) - start;
                ins.TargetOffset = relOff + ins.Length + rel;
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "jmp short 0x{0:X}", ins.TargetOffset);
                return ins;
            }
            if (opc >= 0x70 && opc <= 0x7F && p + 1 <= end)
            {
                int rel = (sbyte)code[p];
                ins.Op = BraidMicroOp.BrCond;
                ins.UsesFlags = true;
                ins.Length = (p + 1) - start;
                ins.TargetOffset = relOff + ins.Length + rel;
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "jcc 0x{0:X}", ins.TargetOffset);
                return ins;
            }
            if (opc >= 0xB8 && opc <= 0xBF)
            {
                int immLen = ((rex & 8) != 0) ? 8 : 4;
                if (p + immLen <= end)
                {
                    ins.Op = BraidMicroOp.MovImm;
                    ins.DstReg = (opc - 0xB8) | (((rex & 1) != 0) ? 8 : 0);
                    ins.DispOrImm = (immLen == 8) ? (long)ReadU64Le(code, p) : (long)(int)ReadU32Le(code, p);
                    ins.Length = (p + immLen) - start;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "mov {0}, 0x{1:X}", RegName(BraidArchIsa.X86_64, ins.DstReg), ins.DispOrImm);
                    return ins;
                }
            }

            if (opc == 0x0F && p < end)
            {
                byte op2 = code[p++];
                if (op2 >= 0x80 && op2 <= 0x8F && p + 4 <= end)
                {
                    int rel = (int)ReadU32Le(code, p);
                    ins.Op = BraidMicroOp.BrCond;
                    ins.UsesFlags = true;
                    ins.Length = (p + 4) - start;
                    ins.TargetOffset = relOff + ins.Length + rel;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "jcc near 0x{0:X}", ins.TargetOffset);
                    return ins;
                }
                int r, b;
                long d;
                bool m;
                int ml = DecodeX64ModRm(code, p, end, rex, out r, out b, out d, out m);
                if (ml > 0)
                {
                    if (op2 == 0x1F)
                    {
                        ins.Op = BraidMicroOp.Nop;
                        ins.Length = (p + ml) - start;
                        ins.MnemonicText = "nop [multibyte]";
                        return ins;
                    }
                    if (op2 == 0xB6 || op2 == 0xB7 || op2 == 0x10 || op2 == 0x11 || op2 == 0x58 || op2 == 0x59 || op2 == 0x5C)
                    {
                        int mw = (op2 == 0xB6) ? 1 : ((op2 == 0xB7) ? 2 : ((scalarPfx == 0xF2) ? 8 : 4));
                        ins.IsFloat = (op2 == 0x10 || op2 == 0x11 || op2 >= 0x58);
                        if (op2 == 0x11 && m)
                        {
                            ins.Op = BraidMicroOp.StoreMem; ins.SrcA = b; ins.SrcB = r; ins.DispOrImm = d; ins.MemWidth = mw;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "movss {0}, xmm{1}", FormatRipOrDisp(b, d), r);
                        }
                        else if (m && (op2 == 0x10 || op2 == 0xB6 || op2 == 0xB7))
                        {
                            ins.Op = BraidMicroOp.LoadMem; ins.DstReg = r; ins.SrcA = b; ins.DispOrImm = d; ins.MemWidth = mw;
                            ins.MnemonicText = ins.IsFloat
                                ? string.Format(CultureInfo.InvariantCulture, "movss xmm{0}, {1}", r, FormatRipOrDisp(b, d))
                                : string.Format(CultureInfo.InvariantCulture, "movzx {0}, {1}", RegName(BraidArchIsa.X86_64, r), FormatRipOrDisp(b, d));
                        }
                        else
                        {
                            ins.Op = ins.IsFloat ? BraidMicroOp.FloatOp : BraidMicroOp.MovReg;
                            ins.DstReg = r; ins.SrcA = r; ins.SrcB = b; ins.DispOrImm = d; ins.MemWidth = m ? mw : 0;
                            ins.MnemonicText = ins.IsFloat ? string.Format(CultureInfo.InvariantCulture, "addss xmm{0}, xmm{1}", r, b) : "movzx";
                        }
                        ins.Length = (p + ml) - start;
                        return ins;
                    }
                }
            }

            if (p < end)
            {
                int r, b;
                long d;
                bool m;
                int ml = DecodeX64ModRm(code, p, end, rex, out r, out b, out d, out m);
                if (ml > 0)
                {
                    int cur = p + ml;
                    if (opc == 0x89 || opc == 0x88)
                    {
                        int mw = (opc == 0x88) ? 1 : width;
                        if (m)
                        {
                            ins.Op = BraidMicroOp.StoreMem; ins.SrcA = b; ins.SrcB = r; ins.DispOrImm = d; ins.MemWidth = mw;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "mov {0}, {1}", FormatRipOrDisp(b, d), RegName(BraidArchIsa.X86_64, r));
                        }
                        else
                        {
                            ins.Op = BraidMicroOp.MovReg; ins.DstReg = b; ins.SrcA = r;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "mov {0}, {1}", RegName(BraidArchIsa.X86_64, b), RegName(BraidArchIsa.X86_64, r));
                        }
                        ins.Length = cur - start;
                        return ins;
                    }
                    if (opc == 0x8B || opc == 0x8A || opc == 0x63)
                    {
                        int mw = (opc == 0x8A) ? 1 : ((opc == 0x63) ? 4 : width);
                        if (m)
                        {
                            ins.Op = BraidMicroOp.LoadMem; ins.DstReg = r; ins.SrcA = b; ins.DispOrImm = d; ins.MemWidth = mw;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "mov {0}, {1}", RegName(BraidArchIsa.X86_64, r), FormatRipOrDisp(b, d));
                        }
                        else
                        {
                            ins.Op = BraidMicroOp.MovReg; ins.DstReg = r; ins.SrcA = b;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "mov {0}, {1}", RegName(BraidArchIsa.X86_64, r), RegName(BraidArchIsa.X86_64, b));
                        }
                        ins.Length = cur - start;
                        return ins;
                    }
                    if (opc == 0x8D)
                    {
                        ins.Op = BraidMicroOp.LeaAddr; ins.DstReg = r; ins.SrcA = b; ins.DispOrImm = d;
                        ins.Length = cur - start;
                        ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "lea {0}, {1}", RegName(BraidArchIsa.X86_64, r), FormatRipOrDisp(b, d));
                        return ins;
                    }
                    if (opc == 0x01 || opc == 0x03 || opc == 0x29 || opc == 0x2B || opc == 0x31 || opc == 0x33 ||
                        opc == 0x21 || opc == 0x23 || opc == 0x09 || opc == 0x0B || opc == 0x39 || opc == 0x3B || opc == 0x85)
                    {
                        bool dirRmDst = (opc == 0x01 || opc == 0x29 || opc == 0x31 || opc == 0x21 || opc == 0x09 || opc == 0x39);
                        int dst = dirRmDst ? b : r;
                        int sa = dirRmDst ? b : r;
                        int sb = dirRmDst ? r : b;
                        ins.SetsFlags = true;
                        if (opc == 0x39 || opc == 0x3B || opc == 0x85)
                        {
                            ins.Op = BraidMicroOp.CmpFlags; ins.SrcA = sa; ins.SrcB = sb;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "{0} {1}, {2}", (opc == 0x85) ? "test" : "cmp", RegName(BraidArchIsa.X86_64, sa), RegName(BraidArchIsa.X86_64, sb));
                        }
                        else
                        {
                            ins.Op = (opc <= 0x03) ? BraidMicroOp.Add : ((opc <= 0x2B) ? BraidMicroOp.Sub : ((opc >= 0x31) ? BraidMicroOp.Xor : ((opc >= 0x21) ? BraidMicroOp.And : BraidMicroOp.Orr)));
                            ins.DstReg = dst; ins.SrcA = sa; ins.SrcB = sb;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "{0} {1}, {2}", ins.Op.ToString().ToLowerInvariant(), RegName(BraidArchIsa.X86_64, dst), RegName(BraidArchIsa.X86_64, sb));
                        }
                        if (m) { ins.DispOrImm = d; ins.MemWidth = width; }
                        ins.Length = cur - start;
                        return ins;
                    }
                    if ((opc == 0x83 && cur + 1 <= end) || (opc == 0x81 && cur + 4 <= end))
                    {
                        long imm = (opc == 0x83) ? (long)(sbyte)code[cur] : (long)(int)ReadU32Le(code, cur);
                        int ilen = (opc == 0x83) ? 1 : 4;
                        int subop = r & 7;
                        ins.SetsFlags = true;
                        ins.DispOrImm = m ? d : imm;
                        if (subop == 7)
                        {
                            ins.Op = BraidMicroOp.CmpFlags; ins.SrcA = b;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "cmp {0}, 0x{1:X}", RegName(BraidArchIsa.X86_64, b), imm);
                        }
                        else
                        {
                            ins.Op = (subop == 0) ? BraidMicroOp.Add : ((subop == 5) ? BraidMicroOp.Sub : ((subop == 4) ? BraidMicroOp.And : BraidMicroOp.Xor));
                            ins.DstReg = b; ins.SrcA = b;
                            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "{0} {1}, 0x{2:X}", ins.Op.ToString().ToLowerInvariant(), RegName(BraidArchIsa.X86_64, b), imm);
                        }
                        if (m) ins.MemWidth = width;
                        ins.Length = (cur + ilen) - start;
                        return ins;
                    }
                    if (opc == 0xFF)
                    {
                        int subop = r & 7;
                        if (subop == 2)
                        {
                            ins.Op = (b == 16 && m) ? BraidMicroOp.CallDirect : BraidMicroOp.CallVTable;
                            ins.SrcA = b; ins.DispOrImm = d; ins.DstReg = 0; ins.MemWidth = m ? 8 : 0;
                            ins.Length = cur - start;
                            ins.MnemonicText = m
                                ? string.Format(CultureInfo.InvariantCulture, "call qword ptr {0}", FormatRipOrDisp(b, d))
                                : string.Format(CultureInfo.InvariantCulture, "call {0}", RegName(BraidArchIsa.X86_64, b));
                            return ins;
                        }
                        if (subop == 4)
                        {
                            ins.Op = BraidMicroOp.Jmp; ins.SrcA = b; ins.DispOrImm = d; ins.MemWidth = m ? 8 : 0;
                            ins.Length = cur - start;
                            ins.MnemonicText = m
                                ? string.Format(CultureInfo.InvariantCulture, "jmp qword ptr {0}", FormatRipOrDisp(b, d))
                                : string.Format(CultureInfo.InvariantCulture, "jmp {0}", RegName(BraidArchIsa.X86_64, b));
                            return ins;
                        }
                    }
                }
            }

            ins.Op = BraidMicroOp.Nop;
            ins.Length = 1;
            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "db 0x{0:X2}", opc);
            return ins;
        }

        private static BraidSsaInstruction DecodeOneArm64(byte[] code, int start, int relOff)
        {
            BraidSsaInstruction ins = new BraidSsaInstruction();
            ins.Offset = relOff;
            ins.Length = 4;
            ins.TargetOffset = -1;
            uint w = ReadU32Le(code, start);

            if ((w & 0xFFFFFC1Fu) == 0xD65F0000u)
            {
                ins.Op = BraidMicroOp.Ret;
                ins.MnemonicText = "ret";
                return ins;
            }
            if ((w & 0xFFFFFC1Fu) == 0xD63F0000u)
            {
                ins.Op = BraidMicroOp.CallVTable;
                ins.SrcA = (int)((w >> 5) & 31u);
                ins.DstReg = 0;
                ins.MnemonicText = "blr x" + ins.SrcA.ToString(CultureInfo.InvariantCulture);
                return ins;
            }
            if ((w & 0x7C000000u) == 0x14000000u)
            {
                int imm26 = (int)(w & 0x03FFFFFFu);
                if ((imm26 & 0x02000000) != 0) imm26 |= unchecked((int)0xFC000000u);
                bool isBl = (w >> 31) != 0u;
                ins.Op = isBl ? BraidMicroOp.CallDirect : BraidMicroOp.Jmp;
                if (isBl) ins.DstReg = 0;
                ins.TargetOffset = relOff + (imm26 * 4);
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "{0} 0x{1:X}", isBl ? "bl" : "b", ins.TargetOffset);
                return ins;
            }
            if ((w & 0xFF000010u) == 0x54000000u || (w & 0x7E000000u) == 0x34000000u)
            {
                int imm19 = (int)((w >> 5) & 0x7FFFFu);
                if ((imm19 & 0x40000) != 0) imm19 |= unchecked((int)0xFFF80000u);
                ins.Op = BraidMicroOp.BrCond;
                ins.UsesFlags = ((w & 0xFF000010u) == 0x54000000u);
                ins.TargetOffset = relOff + (imm19 * 4);
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "b.cond 0x{0:X}", ins.TargetOffset);
                return ins;
            }
            if ((w & 0x7C000000u) == 0x28000000u)
            {
                bool isLoad = ((w >> 22) & 1u) != 0u;
                ins.Op = isLoad ? BraidMicroOp.Pop : BraidMicroOp.Push;
                ins.DstReg = isLoad ? (int)(w & 31u) : (int)((w >> 5) & 31u);
                ins.SrcA = (int)((w >> 5) & 31u);
                ins.MnemonicText = isLoad ? "ldp x29, x30, [sp]" : "stp x29, x30, [sp]";
                return ins;
            }
            if ((w & 0x3B000000u) == 0x39000000u)
            {
                int szBits = (int)((w >> 30) & 3u);
                bool isVec = ((w >> 26) & 1u) != 0u;
                uint opc = (w >> 22) & 3u;
                uint imm12 = (w >> 10) & 0xFFFu;
                int rn = (int)((w >> 5) & 31u);
                int rt = (int)(w & 31u);
                int width = 1 << szBits;
                long byteOff = (long)imm12 * (long)width;
                ins.IsFloat = isVec;
                ins.MemWidth = width;
                ins.DispOrImm = byteOff;
                if (opc == 0u)
                {
                    ins.Op = BraidMicroOp.StoreMem; ins.SrcA = rn; ins.SrcB = rt;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "str {0}{1}, [x{2}, #0x{3:X}]", isVec ? "s" : "x", rt, rn, byteOff);
                }
                else
                {
                    ins.Op = BraidMicroOp.LoadMem; ins.DstReg = rt; ins.SrcA = rn;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "ldr {0}{1}, [x{2}, #0x{3:X}]", isVec ? "s" : "x", rt, rn, byteOff);
                }
                return ins;
            }
            if ((w & 0x1F000000u) == 0x11000000u)
            {
                bool isSub = ((w >> 30) & 1u) != 0u;
                bool setsS = ((w >> 29) & 1u) != 0u;
                uint imm12 = (w >> 10) & 0xFFFu;
                int rn = (int)((w >> 5) & 31u);
                int rd = (int)(w & 31u);
                ins.SetsFlags = setsS;
                ins.DispOrImm = (long)imm12;
                if (setsS && rd == 31)
                {
                    ins.Op = BraidMicroOp.CmpFlags; ins.SrcA = rn;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "cmp x{0}, #0x{1:X}", rn, imm12);
                }
                else
                {
                    ins.Op = isSub ? BraidMicroOp.Sub : BraidMicroOp.Add; ins.DstReg = rd; ins.SrcA = rn;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "{0} x{1}, x{2}, #0x{3:X}", isSub ? "sub" : "add", rd, rn, imm12);
                }
                return ins;
            }
            if ((w & 0x5F200C00u) == 0x1E200800u)
            {
                ins.Op = BraidMicroOp.FloatOp;
                ins.IsFloat = true;
                ins.DstReg = (int)(w & 31u);
                ins.SrcA = (int)((w >> 5) & 31u);
                ins.SrcB = (int)((w >> 16) & 31u);
                ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "fadd s{0}, s{1}, s{2}", ins.DstReg, ins.SrcA, ins.SrcB);
                return ins;
            }

            ins.Op = BraidMicroOp.Nop;
            ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "inst 0x{0:X8}", w);
            return ins;
        }

        private static void RecordSheafField(LiftedBraidFunction fn, uint offset, int width, BraidFieldType ftype, int access)
        {
            if (offset > 0x4000u || width <= 0) return;
            for (int i = 0; i < fn.Fields.Count; i++)
            {
                SheafFieldSlot e = fn.Fields[i];
                if (e.Offset == offset)
                {
                    e.AccessMask |= access;
                    bool floatMismatch = (e.FieldType == BraidFieldType.F32) != (ftype == BraidFieldType.F32);
                    if (e.Width != width || (floatMismatch && e.FieldType != BraidFieldType.VTablePtr64 && ftype != BraidFieldType.VTablePtr64))
                    {
                        if (!e.ConflictH1) { e.ConflictH1 = true; fn.H1DefectCount++; }
                    }
                    else if (ftype == BraidFieldType.VTablePtr64)
                    {
                        e.FieldType = BraidFieldType.VTablePtr64;
                    }
                    return;
                }
                uint endNew = offset + (uint)width;
                uint endOld = e.Offset + (uint)e.Width;
                if (offset < endOld && e.Offset < endNew)
                {
                    if (!e.ConflictH1) { e.ConflictH1 = true; fn.H1DefectCount++; }
                    return;
                }
            }
            if (fn.Fields.Count < 64)
            {
                SheafFieldSlot nf = new SheafFieldSlot();
                nf.Offset = offset;
                nf.Width = width;
                nf.FieldType = ftype;
                nf.AccessMask = access;
                nf.ConflictH1 = false;
                fn.Fields.Add(nf);
            }
        }

        public static LiftedBraidFunction LiftFunctionToSsaBraidIr(string name, BraidArchIsa isa, ulong baseRva, byte[] code, int start, int length)
        {
            return LiftFunctionWithContext(name, isa, baseRva, 0u, code, start, length, null);
        }

        public static LiftedBraidFunction LiftFunctionWithContext(
            string name, BraidArchIsa isa, ulong baseVa, uint funcRva,
            byte[] code, int start, int length, BinaryContainerReport container)
        {
            if (isa == BraidArchIsa.DotNetCil)
            {
                return OmegaBraidRePeAndClr.LiftSingleCilBody(name, baseVa, code, start, length);
            }

            LiftedBraidFunction fn = new LiftedBraidFunction();
            fn.Name = string.IsNullOrEmpty(name) ? "sub_" + baseVa.ToString("X", CultureInfo.InvariantCulture) : name;
            fn.Isa = isa;
            fn.BaseRva = baseVa;
            int end = Math.Min(code.Length, start + length);
            fn.ByteLength = Math.Max(0, end - start);

            int pos = start;
            while (pos < end && fn.Instructions.Count < 256)
            {
                int relOff = pos - start;
                BraidSsaInstruction ins;
                if (isa == BraidArchIsa.Arm64)
                {
                    if (pos + 4 > end) break;
                    ins = DecodeOneArm64(code, pos, relOff);
                }
                else
                {
                    ins = DecodeOneX64(code, pos, end, relOff);
                }
                if (ins.Length <= 0) break;
                fn.Instructions.Add(ins);
                pos += ins.Length;
            }

            int ic = fn.Instructions.Count;
            if (ic == 0) return fn;

            // Resolve RIP-relative IAT calls, indirect E8 -> FF 25 IAT jump thunks, EAT targets, and ASCII/UTF-16 strings!
            if (container != null && container.Valid && isa == BraidArchIsa.X86_64 && funcRva != 0u)
            {
                for (int i = 0; i < ic; i++)
                {
                    BraidSsaInstruction ins = fn.Instructions[i];
                    if (ins.SrcA == 16) // RIP-relative ModR/M!
                    {
                        long targetRvaSigned = (long)funcRva + (long)ins.Offset + (long)ins.Length + ins.DispOrImm;
                        if (targetRvaSigned > 0L && targetRvaSigned < 0x7FFFFFFFL)
                        {
                            uint targetRva = (uint)targetRvaSigned;
                            string iatSym;
                            if (container.IatSlotRvaToSymbol.TryGetValue(targetRva, out iatSym))
                            {
                                ins.ResolvedAnnotation = "IAT -> " + iatSym;
                                if (!fn.ImportedCalls.Contains(iatSym)) fn.ImportedCalls.Add(iatSym);
                            }
                            else
                            {
                                string lit;
                                bool isU16;
                                if (OmegaBraidRePeAndClr.TryExtractStringAtRva(code, container.Sections, targetRva, out lit, out isU16))
                                {
                                    string fmt = (isU16 ? "L\"" : "\"") + lit + "\"";
                                    ins.ResolvedAnnotation = string.Format(CultureInfo.InvariantCulture, "RIP 0x{0:X} -> {1}", targetRva, fmt);
                                    if (!fn.ReferencedStrings.Contains(fmt)) fn.ReferencedStrings.Add(fmt);
                                }
                            }
                        }
                    }
                    else if (ins.Op == BraidMicroOp.CallDirect && ins.TargetOffset != -1)
                    {
                        long calleeRvaSigned = (long)funcRva + (long)ins.TargetOffset;
                        if (calleeRvaSigned > 0L && calleeRvaSigned < 0x7FFFFFFFL)
                        {
                            uint calleeRva = (uint)calleeRvaSigned;
                            string expSym;
                            if (container.ExportRvaToName.TryGetValue(calleeRva, out expSym))
                            {
                                ins.ResolvedAnnotation = "EAT -> " + expSym;
                            }
                            else
                            {
                                // Check if calleeRva is an MSVC/LLD 6-byte `FF 25 disp32` IAT jump thunk!
                                int thunkFileOff;
                                if (OmegaBraidRePeAndClr.TryRvaToFileOffset(code, container.Sections, calleeRva, out thunkFileOff) &&
                                    thunkFileOff + 6 <= code.Length &&
                                    code[thunkFileOff] == 0xFF && code[thunkFileOff + 1] == 0x25)
                                {
                                    int jmpDisp = (int)ReadU32Le(code, thunkFileOff + 2);
                                    long slotSigned = (long)calleeRva + 6L + (long)jmpDisp;
                                    if (slotSigned > 0L && slotSigned < 0x7FFFFFFFL)
                                    {
                                        string thunkSym;
                                        if (container.IatSlotRvaToSymbol.TryGetValue((uint)slotSigned, out thunkSym))
                                        {
                                            ins.ResolvedAnnotation = "THUNK IAT -> " + thunkSym;
                                            if (!fn.ImportedCalls.Contains(thunkSym)) fn.ImportedCalls.Add(thunkSym);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Build Wildcard AOB Signature (masking relative call/branch/RIP displacements)
            StringBuilder aob = new StringBuilder(96);
            int maxAobInsns = Math.Min(ic, 8);
            for (int i = 0; i < maxAobInsns; i++)
            {
                BraidSsaInstruction ins = fn.Instructions[i];
                for (int b = 0; b < ins.Length; b++)
                {
                    if (aob.Length > 0) aob.Append(' ');
                    bool wildcard = ((ins.Op == BraidMicroOp.CallDirect || ins.Op == BraidMicroOp.Jmp || ins.Op == BraidMicroOp.BrCond) && b >= 1)
                        || (ins.SrcA == 16 && b >= ins.Length - 4);
                    if (wildcard) aob.Append("??");
                    else aob.Append(code[start + ins.Offset + b].ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            fn.WildcardAobSignature = aob.ToString();

            bool[] leaders = new bool[ic];
            leaders[0] = true;
            for (int i = 0; i < ic; i++)
            {
                BraidSsaInstruction ins = fn.Instructions[i];
                if (ins.Op == BraidMicroOp.BrCond || ins.Op == BraidMicroOp.Jmp || ins.Op == BraidMicroOp.Ret)
                {
                    if (i + 1 < ic) leaders[i + 1] = true;
                    if (ins.TargetOffset >= 0)
                    {
                        for (int j = 0; j < ic; j++)
                        {
                            if (fn.Instructions[j].Offset == ins.TargetOffset) { leaders[j] = true; break; }
                        }
                    }
                }
            }

            for (int i = 0; i < ic; i++)
            {
                if (leaders[i])
                {
                    BraidBasicBlockNode bb = new BraidBasicBlockNode();
                    bb.Id = fn.Blocks.Count;
                    bb.StartOffset = fn.Instructions[i].Offset;
                    bb.InsnStart = i;
                    fn.Blocks.Add(bb);
                }
            }

            int bc = fn.Blocks.Count;
            for (int b = 0; b < bc; b++)
            {
                int nextI = (b + 1 < bc) ? fn.Blocks[b + 1].InsnStart : ic;
                fn.Blocks[b].InsnCount = nextI - fn.Blocks[b].InsnStart;
                BraidSsaInstruction last = fn.Instructions[nextI - 1];
                fn.Blocks[b].EndOffset = last.Offset + last.Length;

                if (last.Op == BraidMicroOp.Jmp || last.Op == BraidMicroOp.BrCond)
                {
                    for (int tb = 0; tb < bc; tb++)
                    {
                        if (fn.Blocks[tb].StartOffset == last.TargetOffset)
                        {
                            fn.Blocks[b].Succ0 = tb;
                            fn.Blocks[tb].PredCount++;
                            fn.EdgeCount++;
                            if (tb <= b) fn.LoopBackedges++;
                            break;
                        }
                    }
                    if (last.Op == BraidMicroOp.BrCond && b + 1 < bc)
                    {
                        fn.Blocks[b].Succ1 = b + 1;
                        fn.Blocks[b + 1].PredCount++;
                        fn.EdgeCount++;
                    }
                }
                else if (last.Op != BraidMicroOp.Ret && b + 1 < bc)
                {
                    fn.Blocks[b].Succ0 = b + 1;
                    fn.Blocks[b + 1].PredCount++;
                    fn.EdgeCount++;
                }
            }

            int[] regVer = new int[32];
            int nextVer = 1;
            for (int b = 0; b < bc; b++)
            {
                if (fn.Blocks[b].PredCount >= 2)
                {
                    fn.Blocks[b].HasPhiMerge = true;
                    fn.PhiInserted++;
                }
                int endI = fn.Blocks[b].InsnStart + fn.Blocks[b].InsnCount;
                for (int i = fn.Blocks[b].InsnStart; i < endI; i++)
                {
                    BraidSsaInstruction ins = fn.Instructions[i];
                    if (ins.SrcA >= 0 && ins.SrcA < 32) ins.SsaSrcAVer = regVer[ins.SrcA];
                    if (ins.SrcB >= 0 && ins.SrcB < 32) ins.SsaSrcBVer = regVer[ins.SrcB];
                    if (ins.Op != BraidMicroOp.Nop && ins.Op != BraidMicroOp.StoreMem && ins.Op != BraidMicroOp.CmpFlags &&
                        ins.Op != BraidMicroOp.BrCond && ins.Op != BraidMicroOp.Jmp && ins.Op != BraidMicroOp.Ret &&
                        ins.DstReg >= 0 && ins.DstReg < 32)
                    {
                        regVer[ins.DstReg] = nextVer++;
                        ins.SsaDstVer = regVer[ins.DstReg];
                    }
                }
            }

            for (int b = 0; b < bc; b++)
            {
                bool flagsLive = false;
                int[] succs = new int[] { fn.Blocks[b].Succ0, fn.Blocks[b].Succ1 };
                for (int k = 0; k < 2; k++)
                {
                    int sk = succs[k];
                    if (sk >= 0 && sk < bc)
                    {
                        BraidBasicBlockNode sb = fn.Blocks[sk];
                        for (int j = sb.InsnStart; j < sb.InsnStart + sb.InsnCount; j++)
                        {
                            if (fn.Instructions[j].UsesFlags) { flagsLive = true; break; }
                            if (fn.Instructions[j].SetsFlags) { break; }
                        }
                    }
                }
                for (int i = fn.Blocks[b].InsnStart + fn.Blocks[b].InsnCount - 1; i >= fn.Blocks[b].InsnStart; i--)
                {
                    BraidSsaInstruction ins = fn.Instructions[i];
                    if (ins.SetsFlags)
                    {
                        fn.TotalFlagsSet++;
                        if (!flagsLive && ins.Op != BraidMicroOp.CmpFlags)
                        {
                            ins.DeadFlagsEliminated = true;
                            fn.DeadFlagsKilled++;
                        }
                        flagsLive = false;
                    }
                    if (ins.UsesFlags) flagsLive = true;
                }
            }

            bool[] thisAlias = new bool[32];
            bool[] vtableAlias = new bool[32];
            if (isa == BraidArchIsa.Arm64) thisAlias[0] = true;
            else { thisAlias[1] = true; thisAlias[7] = true; } // RCX & RDI

            int loads = 0, stores = 0, alus = 0, floats = 0, calls = 0, vcalls = 0, rets = 0, stackOps = 0;
            ulong immFold = 0UL;

            for (int i = 0; i < ic; i++)
            {
                BraidSsaInstruction ins = fn.Instructions[i];
                if (ins.Op == BraidMicroOp.MovReg && ins.DstReg < 32 && ins.SrcA < 32)
                {
                    thisAlias[ins.DstReg] = thisAlias[ins.SrcA];
                    vtableAlias[ins.DstReg] = vtableAlias[ins.SrcA];
                }
                else if (ins.Op == BraidMicroOp.LoadMem)
                {
                    loads++;
                    if (ins.IsFloat) floats++;
                    if (ins.SrcA < 32 && thisAlias[ins.SrcA] && ins.DispOrImm >= 0)
                    {
                        BraidFieldType ft = ins.IsFloat
                            ? ((ins.MemWidth == 8) ? BraidFieldType.F64 : BraidFieldType.F32)
                            : ((ins.MemWidth == 1) ? BraidFieldType.U8 : ((ins.MemWidth == 2) ? BraidFieldType.U16 : ((ins.MemWidth == 8) ? BraidFieldType.U64 : BraidFieldType.U32)));
                        if (ins.DispOrImm == 0 && ins.MemWidth == 8 && ins.DstReg < 32) vtableAlias[ins.DstReg] = true;
                        RecordSheafField(fn, (uint)ins.DispOrImm, ins.MemWidth, ft, 1);
                        if (ins.DstReg < 32 && ins.DstReg != ins.SrcA) thisAlias[ins.DstReg] = false;
                    }
                    else if (ins.SrcA < 32 && vtableAlias[ins.SrcA] && ins.DispOrImm >= 0)
                    {
                        RecordSheafField(fn, 0u, 8, BraidFieldType.VTablePtr64, 4);
                        uint slot = (uint)ins.DispOrImm;
                        if (!fn.VTableSlots.Contains(slot)) fn.VTableSlots.Add(slot);
                    }
                }
                else if (ins.Op == BraidMicroOp.StoreMem)
                {
                    stores++;
                    if (ins.IsFloat) floats++;
                    if (ins.SrcA < 32 && thisAlias[ins.SrcA] && ins.DispOrImm >= 0)
                    {
                        BraidFieldType ft = ins.IsFloat
                            ? ((ins.MemWidth == 8) ? BraidFieldType.F64 : BraidFieldType.F32)
                            : ((ins.MemWidth == 1) ? BraidFieldType.U8 : ((ins.MemWidth == 2) ? BraidFieldType.U16 : ((ins.MemWidth == 8) ? BraidFieldType.U64 : BraidFieldType.U32)));
                        RecordSheafField(fn, (uint)ins.DispOrImm, ins.MemWidth, ft, 2);
                    }
                }
                else if (ins.Op == BraidMicroOp.CallVTable)
                {
                    vcalls++;
                    if (ins.SrcA < 32 && vtableAlias[ins.SrcA] && ins.DispOrImm >= 0 && ins.MemWidth == 8)
                    {
                        RecordSheafField(fn, 0u, 8, BraidFieldType.VTablePtr64, 4);
                        uint slot = (uint)ins.DispOrImm;
                        if (!fn.VTableSlots.Contains(slot)) fn.VTableSlots.Add(slot);
                    }
                }
                else if (ins.Op == BraidMicroOp.CallDirect) calls++;
                else if (ins.Op == BraidMicroOp.FloatOp) { floats++; alus++; }
                else if (ins.Op == BraidMicroOp.Add || ins.Op == BraidMicroOp.Sub || ins.Op == BraidMicroOp.Xor || ins.Op == BraidMicroOp.And || ins.Op == BraidMicroOp.Orr)
                {
                    alus++;
                    if (ins.DispOrImm != 0 && ins.SrcA != 4 && ins.SrcA != 31) immFold ^= (ulong)(ins.DispOrImm & 0xFFFFL);
                }
                else if (ins.Op == BraidMicroOp.Push || ins.Op == BraidMicroOp.Pop) stackOps++;
                else if (ins.Op == BraidMicroOp.Ret) rets++;
            }

            float cyclomatic = (float)(fn.EdgeCount - bc + 2);
            if (cyclomatic < 1.0f) cyclomatic = 1.0f;

            fn.RawFeatures16[0] = (float)(bc - 2) * 2.0f;
            fn.RawFeatures16[1] = (float)(fn.EdgeCount - bc) * 3.0f;
            fn.RawFeatures16[2] = (cyclomatic - 2.0f) * 2.5f;
            fn.RawFeatures16[3] = (fn.LoopBackedges > 0) ? (fn.LoopBackedges * 6.0f) : -4.0f;
            fn.RawFeatures16[4] = (calls > 0) ? (calls * 4.0f) : -2.0f;
            fn.RawFeatures16[5] = (vcalls > 0) ? (vcalls * 6.0f) : -4.0f;
            fn.RawFeatures16[6] = (float)(rets - 1) * 2.0f + 1.0f;
            fn.RawFeatures16[7] = (fn.PhiInserted > 0) ? (fn.PhiInserted * 4.0f) : -3.0f;

            fn.RawFeatures16[8] = (float)(loads - stores) * 2.5f;
            fn.RawFeatures16[9] = (stores > 0) ? (stores * 3.0f) : -3.0f;
            fn.RawFeatures16[10] = (fn.Fields.Count >= 3) ? (fn.Fields.Count * 3.0f) : -4.0f;
            fn.RawFeatures16[11] = (float)(alus - (loads + stores)) * 2.0f;
            fn.RawFeatures16[12] = (floats > 0) ? (floats * 5.0f) : -5.0f;
            fn.RawFeatures16[13] = (fn.VTableSlots.Count > 0) ? (fn.VTableSlots.Count * 6.0f) : -4.0f;
            fn.RawFeatures16[14] = (float)((int)(immFold & 0x0FUL) - 4);
            fn.RawFeatures16[15] = (stackOps > 0) ? (stackOps * 2.0f) : -2.0f;

            SnapToE8MinimalRoot2x(fn.RawFeatures16, 0, fn.E8TopoRoot2x);
            SnapToE8MinimalRoot2x(fn.RawFeatures16, 8, fn.E8DataRoot2x);
            return fn;
        }

        public static float ScoreCrossPatchSimilarity(LiftedBraidFunction a, LiftedBraidFunction b, out int dualE8Dot, out float cos16)
        {
            dualE8Dot = 0;
            for (int i = 0; i < 8; i++)
            {
                dualE8Dot += (int)a.E8TopoRoot2x[i] * (int)b.E8TopoRoot2x[i];
                dualE8Dot += (int)a.E8DataRoot2x[i] * (int)b.E8DataRoot2x[i];
            }
            float e8Sim = (float)dualE8Dot / 16.0f;

            double dot = 0.0, na = 0.0, nb = 0.0;
            for (int i = 0; i < 16; i++)
            {
                dot += (double)a.RawFeatures16[i] * (double)b.RawFeatures16[i];
                na += (double)a.RawFeatures16[i] * (double)a.RawFeatures16[i];
                nb += (double)b.RawFeatures16[i] * (double)b.RawFeatures16[i];
            }
            cos16 = (na > 1e-9 && nb > 1e-9) ? (float)(dot / Math.Sqrt(na * nb)) : 0.0f;
            return 0.45f * e8Sim + 0.55f * cos16;
        }

        public static List<CrossPatchMatchEntry> DiffFunctionTables(List<LiftedBraidFunction> oldFuncs, List<LiftedBraidFunction> newFuncs)
        {
            List<CrossPatchMatchEntry> results = new List<CrossPatchMatchEntry>();
            for (int i = 0; i < oldFuncs.Count; i++)
            {
                LiftedBraidFunction fa = oldFuncs[i];
                float bestScore = -999.0f;
                int bestDot = 0;
                float bestCos = 0.0f;
                LiftedBraidFunction bestFn = null;

                for (int j = 0; j < newFuncs.Count; j++)
                {
                    int dDot;
                    float c16;
                    float sc = ScoreCrossPatchSimilarity(fa, newFuncs[j], out dDot, out c16);
                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        bestDot = dDot;
                        bestCos = c16;
                        bestFn = newFuncs[j];
                    }
                }

                if (bestFn != null)
                {
                    CrossPatchMatchEntry m = new CrossPatchMatchEntry();
                    m.OldName = fa.Name;
                    m.OldRva = fa.BaseRva;
                    m.NewName = bestFn.Name;
                    m.NewRva = bestFn.BaseRva;
                    m.RvaDelta = (long)bestFn.BaseRva - (long)fa.BaseRva;
                    m.CombinedScore = bestScore;
                    m.DualE8Dot = bestDot;
                    m.Cosine16 = bestCos;
                    m.MigratedAob = bestFn.WildcardAobSignature;
                    results.Add(m);
                }
            }
            return results;
        }

        public static List<LiftedBraidFunction> AutoDiscoverFunctionsInBinary(byte[] fileBytes, int maxFunctions)
        {
            BinaryContainerReport hdr = InspectContainer(fileBytes);
            return AutoDiscoverFunctionsWithReport(fileBytes, hdr, maxFunctions);
        }

        public static List<LiftedBraidFunction> AutoDiscoverFunctionsWithReport(byte[] fileBytes, BinaryContainerReport hdr, int maxFunctions)
        {
            List<LiftedBraidFunction> list = new List<LiftedBraidFunction>();
            if (fileBytes == null || fileBytes.Length == 0) return list;

            // 1. Managed .NET CLR Assembly -> Lift ECMA-335 CIL Methods cleanly!
            if (hdr.Valid && hdr.ClrInfo.IsClrAssembly && (hdr.ClrInfo.IlOnly || (hdr.ClrInfo.BsjbVerified && hdr.PdataFunctions.Count == 0)))
            {
                List<LiftedBraidFunction> cilList = OmegaBraidRePeAndClr.DiscoverAndLiftClrMethods(fileBytes, hdr, maxFunctions);
                if (cilList.Count > 0) return cilList;
            }

            // 2. Native PE32+ with .pdata RUNTIME_FUNCTION Unwind Directory -> Exact Function Boundaries!
            if (hdr.Valid && hdr.PdataFunctions.Count > 0)
            {
                for (int i = 0; i < hdr.PdataFunctions.Count && list.Count < maxFunctions; i++)
                {
                    PdataFunctionEntry pf = hdr.PdataFunctions[i];
                    if ((pf.UnwindFlags & 0x04) != 0) continue; // skip secondary UNW_FLAG_CHAININFO funclets
                    uint fnLen = pf.EndRva - pf.BeginRva;
                    string expName;
                    bool isExported = hdr.ExportRvaToName.TryGetValue(pf.BeginRva, out expName);
                    if (fnLen < 6u && !isExported) continue;

                    int rawOff;
                    if (!OmegaBraidRePeAndClr.TryRvaToFileOffset(fileBytes, hdr.Sections, pf.BeginRva, out rawOff)) continue;
                    int safeLen = Math.Min((int)Math.Min(fnLen, 512u), fileBytes.Length - rawOff);
                    if (safeLen <= 0) continue;

                    ulong va = hdr.ImageBase + pf.BeginRva;
                    string fnName = isExported ? expName : ("pdata_" + va.ToString("X", CultureInfo.InvariantCulture));
                    LiftedBraidFunction fn = LiftFunctionWithContext(fnName, hdr.Isa, va, pf.BeginRva, fileBytes, rawOff, safeLen, hdr);
                    fn.DiscoveredViaPdata = true;
                    fn.PdataPrologSize = pf.PrologSize;
                    list.Add(fn);
                }
                if (list.Count > 0) return list;
            }

            // 3. Exported Functions (EAT) when .pdata is absent
            if (hdr.Valid && hdr.Exports.Count > 0)
            {
                for (int i = 0; i < hdr.Exports.Count && list.Count < maxFunctions; i++)
                {
                    PeExportEntry ex = hdr.Exports[i];
                    int rawOff;
                    if (!OmegaBraidRePeAndClr.TryRvaToFileOffset(fileBytes, hdr.Sections, ex.FunctionRva, out rawOff)) continue;
                    int sliceLen = Math.Min(96, fileBytes.Length - rawOff);
                    if (sliceLen <= 0) continue;
                    ulong va = hdr.ImageBase + ex.FunctionRva;
                    list.Add(LiftFunctionWithContext(ex.Name, hdr.Isa, va, ex.FunctionRva, fileBytes, rawOff, sliceLen, hdr));
                }
                if (list.Count > 0) return list;
            }

            // 4. Fallback Prologue Scan (Stripped PE / ELF64 / Raw Blob)
            int start = (hdr.Valid && hdr.TextRawOffset < (ulong)fileBytes.Length) ? (int)hdr.TextRawOffset : 0;
            int len = (hdr.Valid && hdr.TextSize > 0UL) ? (int)Math.Min((ulong)Math.Max(0, fileBytes.Length - start), hdr.TextSize) : Math.Min(fileBytes.Length, 65536);
            BraidArchIsa isa = (hdr.Isa == BraidArchIsa.Arm64) ? BraidArchIsa.Arm64 : BraidArchIsa.X86_64;
            ulong textRva = hdr.Valid ? hdr.TextRva : 0x1000UL;

            if (isa == BraidArchIsa.Arm64)
            {
                for (int off = 0; off + 32 <= len && list.Count < maxFunctions; off += 4)
                {
                    uint w = ReadU32Le(fileBytes, start + off);
                    if ((w & 0xFFC07FFFu) == 0xA9807BFDu)
                    {
                        int sliceLen = Math.Min(96, len - off);
                        uint fRva = (uint)(textRva + (ulong)off);
                        ulong va = hdr.ImageBase + fRva;
                        list.Add(LiftFunctionWithContext("sub_" + va.ToString("X", CultureInfo.InvariantCulture), isa, va, fRva, fileBytes, start + off, sliceLen, hdr));
                        off += 48;
                    }
                }
            }
            else
            {
                for (int off = 0; off + 24 <= len && list.Count < maxFunctions; off++)
                {
                    byte b0 = fileBytes[start + off];
                    byte b1 = fileBytes[start + off + 1];
                    byte b2 = fileBytes[start + off + 2];
                    bool isPrologue = (b0 == 0x48 && b1 == 0x83 && b2 == 0xEC)
                        || (b0 == 0x48 && b1 == 0x89 && b2 == 0x5C && fileBytes[start + off + 3] == 0x24)
                        || (b0 == 0x55 && b1 == 0x48 && b2 == 0x89 && fileBytes[start + off + 3] == 0xE5);
                    if (isPrologue)
                    {
                        int sliceLen = 16;
                        while (off + sliceLen < len && sliceLen < 112)
                        {
                            if (fileBytes[start + off + sliceLen - 1] == 0xC3) break;
                            sliceLen++;
                        }
                        uint fRva = (uint)(textRva + (ulong)off);
                        ulong va = hdr.ImageBase + fRva;
                        list.Add(LiftFunctionWithContext("sub_" + va.ToString("X", CultureInfo.InvariantCulture), isa, va, fRva, fileBytes, start + off, sliceLen, hdr));
                        off += Math.Max(16, sliceLen - 1);
                    }
                }
            }

            if (list.Count == 0 && len >= 16)
            {
                ulong va = hdr.ImageBase + textRva;
                list.Add(LiftFunctionWithContext("entry_" + va.ToString("X", CultureInfo.InvariantCulture), isa, va, (uint)textRva, fileBytes, start, Math.Min(96, len), hdr));
            }
            return list;
        }

        private static string RustFieldType(BraidFieldType t)
        {
            switch (t)
            {
                case BraidFieldType.U8: return "u8";
                case BraidFieldType.U16: return "u16";
                case BraidFieldType.U32: return "u32";
                case BraidFieldType.U64: return "u64";
                case BraidFieldType.F32: return "f32";
                case BraidFieldType.F64: return "f64";
                case BraidFieldType.VTablePtr64: return "*const RecoveredVTable";
                default: return "u32";
            }
        }

        private static string CppFieldType(BraidFieldType t)
        {
            switch (t)
            {
                case BraidFieldType.U8: return "uint8_t";
                case BraidFieldType.U16: return "uint16_t";
                case BraidFieldType.U32: return "uint32_t";
                case BraidFieldType.U64: return "uint64_t";
                case BraidFieldType.F32: return "float";
                case BraidFieldType.F64: return "double";
                case BraidFieldType.VTablePtr64: return "const RecoveredVTable*";
                default: return "uint32_t";
            }
        }

        private static string CSharpFieldType(BraidFieldType t)
        {
            switch (t)
            {
                case BraidFieldType.U8: return "byte";
                case BraidFieldType.U16: return "ushort";
                case BraidFieldType.U32: return "uint";
                case BraidFieldType.U64: return "ulong";
                case BraidFieldType.F32: return "float";
                case BraidFieldType.F64: return "double";
                case BraidFieldType.VTablePtr64: return "IntPtr";
                default: return "uint";
            }
        }

        public static string SynthesizePolyglotStructHeaders(LiftedBraidFunction fn, string structName, out uint alignedSizeBytes)
        {
            List<SheafFieldSlot> sorted = new List<SheafFieldSlot>(fn.Fields);
            sorted.Sort(delegate(SheafFieldSlot a, SheafFieldSlot b) { return a.Offset.CompareTo(b.Offset); });

            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "// Auto-Synthesized by OmegaBraidRE v1.2.1 | Function: {0} @ 0x{1:X} ({2})",
                fn.Name, fn.BaseRva, fn.Isa));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "// Cellular Sheaf H^1 Defects = {0} ({1})",
                fn.H1DefectCount, fn.H1DefectCount == 0 ? "EXACT_ZERO_CONFLICT" : "POLYMORPHIC_UNION_OVERLAP_QUARANTINED"));

            if (fn.VTableSlots.Count > 0)
            {
                sb.AppendLine("#[repr(C)]");
                sb.AppendLine("pub struct RecoveredVTable {");
                uint vcur = 0;
                List<uint> vs = new List<uint>(fn.VTableSlots);
                vs.Sort();
                for (int i = 0; i < vs.Count; i++)
                {
                    if (vs[i] > vcur)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    pub _vpad_0x{0:X2}: [usize; {1}],", vcur, (vs[i] - vcur) / 8u));
                        vcur = vs[i];
                    }
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    pub vfunc_0x{0:X2}: unsafe extern \"C\" fn(*mut {1}),", vs[i], structName));
                    vcur += 8u;
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("#[repr(C)]");
            sb.AppendLine("pub struct " + structName + " {");
            uint cursor = 0u;
            for (int i = 0; i < sorted.Count; i++)
            {
                SheafFieldSlot f = sorted[i];
                if (f.Offset > cursor)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    pub _pad_0x{0:X2}: [u8; 0x{1:X}],", cursor, f.Offset - cursor));
                    cursor = f.Offset;
                }
                if (f.Offset == cursor)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    pub field_0x{0:X2}: {1}, // +0x{0:X2} ({2}B{3})",
                        f.Offset, RustFieldType(f.FieldType), f.Width, f.ConflictH1 ? ", H^1!=0 OVERLAP!" : ""));
                    cursor += (uint)f.Width;
                }
            }
            alignedSizeBytes = (cursor + 7u) & ~7u;
            if (alignedSizeBytes > cursor)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    pub _tail_pad: [u8; 0x{0:X}],", alignedSizeBytes - cursor));
            }
            sb.AppendLine("}");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "const _: () = assert!(core::mem::size_of::<{0}>() == 0x{1:X});", structName, alignedSizeBytes));
            sb.AppendLine();

            sb.AppendLine("// C++23 Lockstep Struct");
            sb.AppendLine("struct " + structName + " {");
            cursor = 0u;
            for (int i = 0; i < sorted.Count; i++)
            {
                SheafFieldSlot f = sorted[i];
                if (f.Offset > cursor)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    uint8_t _pad_0x{0:X2}[0x{1:X}];", cursor, f.Offset - cursor));
                    cursor = f.Offset;
                }
                if (f.Offset == cursor)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    {0} field_0x{1:X2};", CppFieldType(f.FieldType), f.Offset));
                    cursor += (uint)f.Width;
                }
            }
            if (alignedSizeBytes > cursor)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    uint8_t _tail_pad[0x{0:X}];", alignedSizeBytes - cursor));
            }
            sb.AppendLine("};");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "static_assert(sizeof({0}) == 0x{1:X}, \"Sheaf H^1=0 ABI lock\");", structName, alignedSizeBytes));
            sb.AppendLine();

            sb.AppendLine("// C# Explicit-Offset Interop Struct");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "[StructLayout(LayoutKind.Explicit, Size = 0x{0:X})]", alignedSizeBytes));
            sb.AppendLine("public struct " + structName + "Interop {");
            for (int i = 0; i < sorted.Count; i++)
            {
                SheafFieldSlot f = sorted[i];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    [FieldOffset(0x{0:X2})] public {1} Field_0x{0:X2};", f.Offset, CSharpFieldType(f.FieldType)));
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
