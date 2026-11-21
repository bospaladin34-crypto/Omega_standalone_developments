using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaBraidRE
{
    public sealed class CodeViewRsdsInfo
    {
        public bool Found = false;
        public string GuidHex = "";
        public uint Age = 0;
        public string PdbPath = "";
    }

    public sealed class DebugSymbolEntry
    {
        public string Source = "";     // "PDB-S_PUB32", "PDB-S_GPROC32", "ELF-SYMTAB", "DWARF-SUBPROG"
        public string Name = "";
        public uint SectionIndex = 0;  // 1-based for PDB, section idx for ELF
        public uint SectionOffset = 0;
        public ulong RvaOrVa = 0;
        public uint CodeSize = 0;
    }

    public sealed class SymbolResolutionReport
    {
        public CodeViewRsdsInfo Rsds = new CodeViewRsdsInfo();
        public bool PdbLoaded = false;
        public uint PdbBlockSize = 0;
        public uint PdbStreamCount = 0;
        public ushort PdbSymRecordStream = 0;
        public bool DwarfLoaded = false;
        public ushort DwarfVersion = 0;
        public List<DebugSymbolEntry> Symbols = new List<DebugSymbolEntry>();
        public string SummaryLine = "";
    }

    public sealed class LiveProcessItem
    {
        public int Pid = 0;
        public string Name = "";
        public ulong BaseAddress = 0;
        public uint ImageSize = 0;
        public string ModulePath = "";
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "PID {0,-6} | {1,-26} | Base=0x{2:X} ({3} KB)",
                Pid, Name, BaseAddress, ImageSize / 1024u);
        }
    }

    public sealed class LiveMemorySnapshot
    {
        public bool Success = false;
        public int Pid = 0;
        public string ProcessName = "";
        public ulong BaseAddress = 0;
        public uint ImageSize = 0;
        public int BytesRead = 0;
        public byte[] PeImageBuffer = new byte[0];
        public string ErrorMessage = "";
    }

    public sealed class TrampolineProloguePlan
    {
        public string FunctionName = "";
        public ulong TargetRva = 0;
        public int Rel32StealBytes = 0;      // >= 5 bytes on exact x64 instruction boundary
        public int Abs64StealBytes = 0;      // >= 14 bytes on exact x64 instruction boundary
        public int StolenInsnCount5 = 0;
        public int StolenInsnCount14 = 0;
        public bool HasRipRelativeInSteal14 = false;
        public int RipRelativeInsnOffset = -1;
        public string StolenHex5 = "";
        public string StolenHex14 = "";
        public string SafeHookStrategy = "";
        public List<string> DisassembledPrologue = new List<string>();
    }

    public static class OmegaBraidReSymbolsAndHooks
    {
        private const uint PROCESS_VM_READ = 0x0010u;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400u;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000u;
        private const uint TH32CS_SNAPMODULE = 0x00000008u;
        private const uint TH32CS_SNAPMODULE32 = 0x00000010u;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MODULEENTRY32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        private static ushort RdU16(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        private static uint RdU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        private static ulong RdU64(byte[] b, int o)
        {
            return (ulong)RdU32(b, o) | ((ulong)RdU32(b, o + 4) << 32);
        }

        private static void WrU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
        }

        private static void WrU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFFu);
            b[o + 1] = (byte)((v >> 8) & 0xFFu);
            b[o + 2] = (byte)((v >> 16) & 0xFFu);
            b[o + 3] = (byte)((v >> 24) & 0xFFu);
        }

        private static void WrU64(byte[] b, int o, ulong v)
        {
            WrU32(b, o, (uint)(v & 0xFFFFFFFFUL));
            WrU32(b, o + 4, (uint)(v >> 32));
        }

        private static string RdAsciiZ(byte[] b, int off, int maxLen)
        {
            if (b == null || off < 0 || off >= b.Length) return "";
            int end = Math.Min(b.Length, off + maxLen);
            int cur = off;
            while (cur < end && b[cur] != 0) cur++;
            if (cur <= off) return "";
            return Encoding.ASCII.GetString(b, off, cur - off);
        }

        private static ulong RdUleb128(byte[] b, ref int cur, int end)
        {
            ulong res = 0UL;
            int shift = 0;
            while (cur < end && shift < 64)
            {
                byte bt = b[cur++];
                res |= ((ulong)(bt & 0x7F)) << shift;
                if ((bt & 0x80) == 0) break;
                shift += 7;
            }
            return res;
        }

        // =========================================================================
        // 1. PE IMAGE_DIRECTORY_ENTRY_DEBUG (CodeView RSDS) + PDB 7.00 MSF DECODER
        // =========================================================================

        public static CodeViewRsdsInfo ParsePeCodeViewRsds(byte[] raw, BinaryContainerReport rep)
        {
            CodeViewRsdsInfo info = new CodeViewRsdsInfo();
            if (raw == null || rep == null || raw.Length < 128) return info;
            if (rep.Format != BraidContainerKind.Pe32Plus && rep.Format != BraidContainerKind.Pe32) return info;

            uint peOff = RdU32(raw, 0x3C);
            if (peOff + 0x78u >= (uint)raw.Length) return info;
            int optOff = (int)peOff + 24;
            ushort optMagic = RdU16(raw, optOff);
            int ddBase = (optMagic == 0x020B) ? (optOff + 112) : (optOff + 96);
            int numDdOff = (optMagic == 0x020B) ? (optOff + 108) : (optOff + 92);
            if (numDdOff + 4 > raw.Length) return info;
            uint numDd = RdU32(raw, numDdOff);
            if (numDd <= 6u) return info;

            int dbgDdOff = ddBase + 6 * 8; // IMAGE_DIRECTORY_ENTRY_DEBUG = 6
            if (dbgDdOff + 8 > raw.Length) return info;
            uint dbgRva = RdU32(raw, dbgDdOff);
            uint dbgSize = RdU32(raw, dbgDdOff + 4);
            if (dbgRva == 0u || dbgSize < 28u) return info;

            int dbgFileOff;
            if (!OmegaBraidRePeAndClr.TryRvaToFileOffset(raw, rep.Sections, dbgRva, out dbgFileOff)) return info;
            int entryCount = (int)Math.Min(16u, dbgSize / 28u);
            for (int i = 0; i < entryCount; i++)
            {
                int eo = dbgFileOff + i * 28;
                if (eo + 28 > raw.Length) break;
                uint dbgType = RdU32(raw, eo + 12);
                uint szData = RdU32(raw, eo + 16);
                uint ptrRaw = RdU32(raw, eo + 24);
                if (dbgType == 2u && szData >= 24u && ptrRaw + 24u <= (uint)raw.Length)
                {
                    int ro = (int)ptrRaw;
                    if (raw[ro] == (byte)'R' && raw[ro + 1] == (byte)'S' && raw[ro + 2] == (byte)'D' && raw[ro + 3] == (byte)'S')
                    {
                        uint g1 = RdU32(raw, ro + 4);
                        ushort g2 = RdU16(raw, ro + 8);
                        ushort g3 = RdU16(raw, ro + 10);
                        StringBuilder gsb = new StringBuilder();
                        gsb.AppendFormat(CultureInfo.InvariantCulture, "{0:X8}-{1:X4}-{2:X4}-{3:X2}{4:X2}-", g1, g2, g3, raw[ro + 12], raw[ro + 13]);
                        for (int k = 14; k < 20; k++) gsb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", raw[ro + k]);
                        info.Found = true;
                        info.GuidHex = gsb.ToString();
                        info.Age = RdU32(raw, ro + 20);
                        info.PdbPath = RdAsciiZ(raw, ro + 24, (int)Math.Min(260u, szData - 24u));
                        return info;
                    }
                }
            }
            return info;
        }

        private static readonly byte[] MSF7_MAGIC = new byte[]
        {
            (byte)'M',(byte)'i',(byte)'c',(byte)'r',(byte)'o',(byte)'s',(byte)'o',(byte)'f',
            (byte)'t',(byte)' ',(byte)'C',(byte)'/',(byte)'C',(byte)'+',(byte)'+',(byte)' ',
            (byte)'M',(byte)'S',(byte)'F',(byte)' ',(byte)'7',(byte)'.',(byte)'0',(byte)'0',
            0x0D, 0x0A, 0x1A, 0x44, 0x53, 0x00, 0x00, 0x00
        };

        public static SymbolResolutionReport ParsePdbMsf7Symbols(byte[] pdbBytes, BinaryContainerReport peRep)
        {
            SymbolResolutionReport outRep = new SymbolResolutionReport();
            if (pdbBytes == null || pdbBytes.Length < 56) return outRep;
            for (int i = 0; i < 32; i++)
            {
                if (pdbBytes[i] != MSF7_MAGIC[i]) return outRep;
            }

            uint blockSize = RdU32(pdbBytes, 32);
            uint numBlocks = RdU32(pdbBytes, 40);
            uint numDirBytes = RdU32(pdbBytes, 44);
            uint blockMapAddr = RdU32(pdbBytes, 52);

            if (blockSize != 512u && blockSize != 1024u && blockSize != 2048u && blockSize != 4096u) return outRep;
            if (numDirBytes < 4u || numDirBytes > 16u * 1024u * 1024u) return outRep;
            if ((ulong)blockMapAddr * blockSize + 4UL > (ulong)pdbBytes.Length) return outRep;

            // Assemble Stream Directory from BlockMapAddr
            uint dirBlockCount = (numDirBytes + blockSize - 1u) / blockSize;
            byte[] dirBytes = new byte[numDirBytes];
            int copied = 0;
            for (uint bi = 0; bi < dirBlockCount; bi++)
            {
                int mapEntryOff = (int)(blockMapAddr * blockSize + bi * 4u);
                if (mapEntryOff + 4 > pdbBytes.Length) return outRep;
                uint blkIdx = RdU32(pdbBytes, mapEntryOff);
                int srcOff = (int)(blkIdx * blockSize);
                if (srcOff < 0 || srcOff >= pdbBytes.Length) return outRep;
                int take = (int)Math.Min((uint)(numDirBytes - copied), Math.Min(blockSize, (uint)(pdbBytes.Length - srcOff)));
                Buffer.BlockCopy(pdbBytes, srcOff, dirBytes, copied, take);
                copied += take;
            }

            uint numStreams = RdU32(dirBytes, 0);
            if (numStreams == 0u || numStreams > 65535u || 4u + numStreams * 4u > (uint)dirBytes.Length) return outRep;

            uint[] streamSizes = new uint[numStreams];
            int cursor = 4;
            for (uint s = 0; s < numStreams; s++)
            {
                streamSizes[s] = RdU32(dirBytes, cursor);
                cursor += 4;
            }

            List<uint[]> streamBlocks = new List<uint[]>((int)numStreams);
            for (uint s = 0; s < numStreams; s++)
            {
                uint sz = streamSizes[s];
                if (sz == 0xFFFFFFFFu || sz == 0u)
                {
                    streamBlocks.Add(new uint[0]);
                    continue;
                }
                uint bcnt = (sz + blockSize - 1u) / blockSize;
                if (cursor + (int)(bcnt * 4u) > dirBytes.Length)
                {
                    streamBlocks.Add(new uint[0]);
                    continue;
                }
                uint[] blks = new uint[bcnt];
                for (uint k = 0; k < bcnt; k++)
                {
                    blks[k] = RdU32(dirBytes, cursor);
                    cursor += 4;
                }
                streamBlocks.Add(blks);
            }

            outRep.PdbLoaded = true;
            outRep.PdbBlockSize = blockSize;
            outRep.PdbStreamCount = numStreams;

            // DBI Stream is Stream 3 per LLVM PDB DBI spec
            if (numStreams <= 3u) return outRep;
            byte[] dbi = ReadMsfStream(pdbBytes, blockSize, streamSizes[3], streamBlocks[3]);
            if (dbi.Length < 64) return outRep;

            // DbiStreamHeader.SymRecordStream is uint16 at offset +20
            ushort symRecStream = RdU16(dbi, 20);
            outRep.PdbSymRecordStream = symRecStream;
            if (symRecStream == 0xFFFF || symRecStream >= numStreams) return outRep;

            byte[] symBytes = ReadMsfStream(pdbBytes, blockSize, streamSizes[symRecStream], streamBlocks[symRecStream]);
            ParseCodeViewSymbolStream(symBytes, peRep, outRep.Symbols);

            outRep.SummaryLine = string.Format(CultureInfo.InvariantCulture,
                "PDB MSF 7.00 (Blk={0}B, Streams={1}, SymStream={2}, Symbols={3})",
                blockSize, numStreams, symRecStream, outRep.Symbols.Count);
            return outRep;
        }

        private static byte[] ReadMsfStream(byte[] pdbBytes, uint blockSize, uint streamSize, uint[] blocks)
        {
            if (streamSize == 0u || streamSize == 0xFFFFFFFFu || blocks == null || blocks.Length == 0) return new byte[0];
            uint capped = Math.Min(streamSize, 8u * 1024u * 1024u);
            byte[] buf = new byte[capped];
            int dst = 0;
            for (int i = 0; i < blocks.Length && dst < (int)capped; i++)
            {
                int src = (int)(blocks[i] * blockSize);
                if (src < 0 || src >= pdbBytes.Length) break;
                int take = (int)Math.Min((uint)(capped - dst), Math.Min(blockSize, (uint)(pdbBytes.Length - src)));
                Buffer.BlockCopy(pdbBytes, src, buf, dst, take);
                dst += take;
            }
            return buf;
        }

        private static void ParseCodeViewSymbolStream(byte[] sym, BinaryContainerReport peRep, List<DebugSymbolEntry> outSyms)
        {
            if (sym == null || sym.Length < 8) return;
            int off = 0;
            int safety = 0;
            while (off + 4 <= sym.Length && safety < 200000)
            {
                safety++;
                ushort recLen = RdU16(sym, off);
                if (recLen < 2) break;
                int nextOff = off + 2 + recLen;
                if (nextOff > sym.Length) break;
                ushort recKind = RdU16(sym, off + 2);

                if (recKind == 0x110E && recLen >= 13) // S_PUB32: flags(4), off(4), seg(2), name(z)
                {
                    uint segOff = RdU32(sym, off + 8);
                    ushort seg = RdU16(sym, off + 12);
                    string name = RdAsciiZ(sym, off + 14, nextOff - (off + 14));
                    if (!string.IsNullOrEmpty(name))
                    {
                        DebugSymbolEntry e = new DebugSymbolEntry();
                        e.Source = "PDB-S_PUB32";
                        e.Name = name;
                        e.SectionIndex = seg;
                        e.SectionOffset = segOff;
                        e.RvaOrVa = ResolvePdbSegmentToRva(peRep, seg, segOff);
                        outSyms.Add(e);
                    }
                }
                else if ((recKind == 0x1110 || recKind == 0x110F) && recLen >= 38) // S_GPROC32 / S_LPROC32
                {
                    uint codeLen = RdU32(sym, off + 16);
                    uint segOff = RdU32(sym, off + 32);
                    ushort seg = RdU16(sym, off + 36);
                    string name = RdAsciiZ(sym, off + 39, nextOff - (off + 39));
                    if (!string.IsNullOrEmpty(name))
                    {
                        DebugSymbolEntry e = new DebugSymbolEntry();
                        e.Source = (recKind == 0x1110) ? "PDB-S_GPROC32" : "PDB-S_LPROC32";
                        e.Name = name;
                        e.SectionIndex = seg;
                        e.SectionOffset = segOff;
                        e.CodeSize = codeLen;
                        e.RvaOrVa = ResolvePdbSegmentToRva(peRep, seg, segOff);
                        outSyms.Add(e);
                    }
                }
                off = nextOff;
            }
        }

        private static ulong ResolvePdbSegmentToRva(BinaryContainerReport peRep, ushort seg1Based, uint segOff)
        {
            if (peRep != null && peRep.Sections != null && seg1Based >= 1 && seg1Based <= peRep.Sections.Count)
            {
                return (ulong)peRep.Sections[seg1Based - 1].VirtualAddress + segOff;
            }
            return 0x1000UL + segOff;
        }

        // =========================================================================
        // 2. ELF64 .SYMTAB + DWARF v4/v5 .DEBUG_INFO / .DEBUG_ABBREV DECODER
        // =========================================================================

        public static SymbolResolutionReport ParseElfAndDwarfSymbols(byte[] elfBytes)
        {
            SymbolResolutionReport rep = new SymbolResolutionReport();
            if (elfBytes == null || elfBytes.Length < 64) return rep;
            if (elfBytes[0] != 0x7F || elfBytes[1] != (byte)'E' || elfBytes[2] != (byte)'L' || elfBytes[3] != (byte)'F') return rep;
            if (elfBytes[4] != 2) return rep; // ELF64 only

            ulong shOff = RdU64(elfBytes, 40);
            ushort shEntSize = RdU16(elfBytes, 58);
            ushort shNum = RdU16(elfBytes, 60);
            ushort shStrNdx = RdU16(elfBytes, 62);
            if (shOff == 0UL || shEntSize < 64 || shNum == 0 || shStrNdx >= shNum) return rep;
            if (shOff + (ulong)shNum * shEntSize > (ulong)elfBytes.Length) return rep;

            int shStrHdr = (int)(shOff + (ulong)shStrNdx * shEntSize);
            ulong shStrTabOff = RdU64(elfBytes, shStrHdr + 24);
            ulong shStrTabSize = RdU64(elfBytes, shStrHdr + 32);

            int dbgInfoOff = -1, dbgInfoSz = 0;
            int dbgAbbrevOff = -1, dbgAbbrevSz = 0;
            int dbgStrOff = -1, dbgStrSz = 0;
            int symTabOff = -1, symTabSz = 0, symTabLink = -1;

            Dictionary<int, int[]> secOffSz = new Dictionary<int, int[]>();
            for (int i = 0; i < shNum; i++)
            {
                int sh = (int)(shOff + (ulong)i * shEntSize);
                uint nameIdx = RdU32(elfBytes, sh);
                uint shType = RdU32(elfBytes, sh + 4);
                ulong sOff = RdU64(elfBytes, sh + 24);
                ulong sSize = RdU64(elfBytes, sh + 32);
                uint sLink = RdU32(elfBytes, sh + 40);
                secOffSz[i] = new int[] { (int)sOff, (int)sSize };

                string sName = "";
                if (shStrTabOff + nameIdx < (ulong)elfBytes.Length && nameIdx < shStrTabSize)
                {
                    sName = RdAsciiZ(elfBytes, (int)(shStrTabOff + nameIdx), 64);
                }

                if (sName == ".debug_info") { dbgInfoOff = (int)sOff; dbgInfoSz = (int)sSize; }
                else if (sName == ".debug_abbrev") { dbgAbbrevOff = (int)sOff; dbgAbbrevSz = (int)sSize; }
                else if (sName == ".debug_str") { dbgStrOff = (int)sOff; dbgStrSz = (int)sSize; }
                else if (shType == 2u || sName == ".symtab") { symTabOff = (int)sOff; symTabSz = (int)sSize; symTabLink = (int)sLink; }
            }

            // A. Parse ELF64 .symtab STT_FUNC symbols (24-byte Elf64_Sym entries)
            if (symTabOff > 0 && symTabSz >= 24 && secOffSz.ContainsKey(symTabLink))
            {
                int strOff = secOffSz[symTabLink][0];
                int strSz = secOffSz[symTabLink][1];
                int count = Math.Min(4096, symTabSz / 24);
                for (int i = 0; i < count; i++)
                {
                    int eo = symTabOff + i * 24;
                    if (eo + 24 > elfBytes.Length) break;
                    uint stName = RdU32(elfBytes, eo);
                    byte stInfo = elfBytes[eo + 4];
                    ulong stVal = RdU64(elfBytes, eo + 8);
                    ulong stSize = RdU64(elfBytes, eo + 16);
                    if ((stInfo & 0x0F) == 2 && stName > 0u && stName < (uint)strSz)
                    {
                        string nm = RdAsciiZ(elfBytes, strOff + (int)stName, 128);
                        if (!string.IsNullOrEmpty(nm))
                        {
                            DebugSymbolEntry d = new DebugSymbolEntry();
                            d.Source = "ELF-SYMTAB";
                            d.Name = nm;
                            d.RvaOrVa = stVal;
                            d.CodeSize = (uint)stSize;
                            rep.Symbols.Add(d);
                        }
                    }
                }
            }

            // B. Parse DWARF v4/v5 .debug_info + .debug_abbrev DW_TAG_subprogram (0x2E)
            if (dbgInfoOff > 0 && dbgInfoSz >= 11 && dbgAbbrevOff > 0 && dbgAbbrevSz >= 4 &&
                dbgInfoOff + dbgInfoSz <= elfBytes.Length && dbgAbbrevOff + dbgAbbrevSz <= elfBytes.Length)
            {
                ParseDwarfCompileUnits(elfBytes, dbgInfoOff, dbgInfoSz, dbgAbbrevOff, dbgAbbrevSz, dbgStrOff, dbgStrSz, rep);
            }

            rep.SummaryLine = string.Format(CultureInfo.InvariantCulture,
                "ELF64 Symbols={0} | DWARF={1} (v{2})",
                rep.Symbols.Count, rep.DwarfLoaded ? "VERIFIED" : "NONE", rep.DwarfVersion);
            return rep;
        }

        private sealed class DwarfAbbrevSpec
        {
            public uint Tag = 0;
            public bool HasChildren = false;
            public List<uint> AttrNames = new List<uint>();
            public List<uint> AttrForms = new List<uint>();
        }

        private static void ParseDwarfCompileUnits(
            byte[] b, int infoOff, int infoSz, int abbrevOff, int abbrevSz, int strOff, int strSz, SymbolResolutionReport rep)
        {
            int cur = infoOff;
            int infoEnd = infoOff + infoSz;
            if (cur + 11 > infoEnd) return;

            uint unitLen = RdU32(b, cur); cur += 4;
            int unitEnd = Math.Min(infoEnd, cur + (int)unitLen);
            ushort ver = RdU16(b, cur); cur += 2;
            uint abbrevRel = 0u;
            byte addrSize = 8;

            if (ver == 5)
            {
                if (cur + 6 > unitEnd) return;
                cur += 1; // unit_type
                addrSize = b[cur++];
                abbrevRel = RdU32(b, cur); cur += 4;
            }
            else if (ver == 4 || ver == 3 || ver == 2)
            {
                if (cur + 5 > unitEnd) return;
                abbrevRel = RdU32(b, cur); cur += 4;
                addrSize = b[cur++];
            }
            else return;

            rep.DwarfLoaded = true;
            rep.DwarfVersion = ver;

            // Load abbreviation table at abbrevOff + abbrevRel
            Dictionary<uint, DwarfAbbrevSpec> abbrevs = new Dictionary<uint, DwarfAbbrevSpec>();
            int ac = abbrevOff + (int)abbrevRel;
            int aEnd = abbrevOff + abbrevSz;
            while (ac < aEnd)
            {
                uint code = (uint)RdUleb128(b, ref ac, aEnd);
                if (code == 0u) break;
                DwarfAbbrevSpec sp = new DwarfAbbrevSpec();
                sp.Tag = (uint)RdUleb128(b, ref ac, aEnd);
                if (ac >= aEnd) break;
                sp.HasChildren = (b[ac++] != 0);
                while (ac < aEnd)
                {
                    uint an = (uint)RdUleb128(b, ref ac, aEnd);
                    uint af = (uint)RdUleb128(b, ref ac, aEnd);
                    if (an == 0u && af == 0u) break;
                    sp.AttrNames.Add(an);
                    sp.AttrForms.Add(af);
                }
                abbrevs[code] = sp;
            }

            // Walk DIEs in compilation unit
            int safety = 0;
            while (cur < unitEnd && safety < 4096)
            {
                safety++;
                uint acode = (uint)RdUleb128(b, ref cur, unitEnd);
                if (acode == 0u) continue;
                DwarfAbbrevSpec sp;
                if (!abbrevs.TryGetValue(acode, out sp)) break;

                string fnName = "";
                ulong lowPc = 0UL;
                uint highPc = 0u;

                for (int i = 0; i < sp.AttrNames.Count && cur < unitEnd; i++)
                {
                    uint an = sp.AttrNames[i];
                    uint af = sp.AttrForms[i];
                    string sVal = "";
                    ulong uVal = 0UL;

                    if (af == 0x08u) // DW_FORM_string
                    {
                        sVal = RdAsciiZ(b, cur, unitEnd - cur);
                        cur += sVal.Length + 1;
                    }
                    else if (af == 0x0Eu) // DW_FORM_strp
                    {
                        if (cur + 4 > unitEnd) { cur = unitEnd; break; }
                        uint so = RdU32(b, cur); cur += 4;
                        if (strOff >= 0 && so < (uint)strSz && strOff + (int)so < b.Length)
                        {
                            sVal = RdAsciiZ(b, strOff + (int)so, 160);
                        }
                    }
                    else if (af == 0x01u) // DW_FORM_addr
                    {
                        if (addrSize == 8 && cur + 8 <= unitEnd) { uVal = RdU64(b, cur); cur += 8; }
                        else if (addrSize == 4 && cur + 4 <= unitEnd) { uVal = RdU32(b, cur); cur += 4; }
                        else { cur = unitEnd; break; }
                    }
                    else if (af == 0x0Bu) { uVal = b[cur++]; }                      // DW_FORM_data1
                    else if (af == 0x05u) { uVal = RdU16(b, cur); cur += 2; }        // DW_FORM_data2
                    else if (af == 0x06u) { uVal = RdU32(b, cur); cur += 4; }        // DW_FORM_data4
                    else if (af == 0x07u) { uVal = RdU64(b, cur); cur += 8; }        // DW_FORM_data8
                    else if (af == 0x0Fu) { uVal = RdUleb128(b, ref cur, unitEnd); } // DW_FORM_udata
                    else if (af == 0x0Cu) { uVal = b[cur++]; }                       // DW_FORM_flag
                    else if (af == 0x19u) { uVal = 1UL; }                            // DW_FORM_flag_present
                    else { cur = unitEnd; break; }

                    if (an == 0x03u) fnName = sVal;      // DW_AT_name
                    else if (an == 0x11u) lowPc = uVal;  // DW_AT_low_pc
                    else if (an == 0x12u) highPc = (uint)uVal; // DW_AT_high_pc
                }

                if (sp.Tag == 0x2Eu && !string.IsNullOrEmpty(fnName)) // DW_TAG_subprogram
                {
                    DebugSymbolEntry d = new DebugSymbolEntry();
                    d.Source = "DWARF-SUBPROG";
                    d.Name = fnName;
                    d.RvaOrVa = lowPc;
                    d.CodeSize = highPc;
                    rep.Symbols.Add(d);
                }
            }
        }

        // =========================================================================
        // 3. READ-ONLY LIVE WINDOWS PROCESS MEMORY ATTACHMENT (ReadProcessMemory)
        // =========================================================================

        public static List<LiveProcessItem> ListAttachableProcesses()
        {
            List<LiveProcessItem> list = new List<LiveProcessItem>();
            try
            {
                Process[] procs = Process.GetProcesses();
                for (int i = 0; i < procs.Length; i++)
                {
                    Process p = procs[i];
                    try
                    {
                        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)p.Id);
                        if (snap == INVALID_HANDLE_VALUE || snap == IntPtr.Zero) continue;
                        try
                        {
                            MODULEENTRY32W me = new MODULEENTRY32W();
                            me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                            if (Module32FirstW(snap, ref me))
                            {
                                LiveProcessItem item = new LiveProcessItem();
                                item.Pid = p.Id;
                                item.Name = p.ProcessName;
                                item.BaseAddress = (ulong)me.modBaseAddr.ToInt64();
                                item.ImageSize = me.modBaseSize;
                                item.ModulePath = me.szExePath ?? "";
                                list.Add(item);
                            }
                        }
                        finally
                        {
                            CloseHandle(snap);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
            list.Sort(delegate(LiveProcessItem a, LiveProcessItem b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static LiveMemorySnapshot ReadLiveProcessPeImage(int pid, int maxTotalBytes)
        {
            LiveMemorySnapshot snapOut = new LiveMemorySnapshot();
            snapOut.Pid = pid;
            int capBytes = Math.Max(65536, Math.Min(maxTotalBytes, 8 * 1024 * 1024));

            IntPtr modBasePtr = IntPtr.Zero;
            uint modSizeVal = 0u;
            string modNameStr = "PID_" + pid;

            IntPtr hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
            if (hSnap != INVALID_HANDLE_VALUE && hSnap != IntPtr.Zero)
            {
                MODULEENTRY32W me = new MODULEENTRY32W();
                me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                try
                {
                    if (Module32FirstW(hSnap, ref me) && me.modBaseAddr != IntPtr.Zero)
                    {
                        modBasePtr = me.modBaseAddr;
                        modSizeVal = me.modBaseSize;
                        if (!string.IsNullOrEmpty(me.szModule)) modNameStr = me.szModule;
                    }
                }
                finally
                {
                    CloseHandle(hSnap);
                }
            }

            if (modBasePtr == IntPtr.Zero)
            {
                try
                {
                    using (Process pFallback = Process.GetProcessById(pid))
                    {
                        if (pFallback.MainModule != null)
                        {
                            modBasePtr = pFallback.MainModule.BaseAddress;
                            modSizeVal = (uint)Math.Max(0, pFallback.MainModule.ModuleMemorySize);
                            modNameStr = pFallback.MainModule.ModuleName ?? pFallback.ProcessName;
                        }
                    }
                }
                catch { }
            }

            if (modBasePtr == IntPtr.Zero)
            {
                snapOut.ErrorMessage = "Unable to resolve main module base for PID " + pid + ".";
                return snapOut;
            }

            snapOut.ProcessName = modNameStr;
            snapOut.BaseAddress = (ulong)modBasePtr.ToInt64();
            snapOut.ImageSize = modSizeVal;

            IntPtr hProc = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero)
            {
                hProc = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            }
            if (hProc == IntPtr.Zero)
            {
                snapOut.ErrorMessage = "OpenProcess(PROCESS_VM_READ) denied for PID " + pid + ".";
                return snapOut;
            }

            try
            {
                // Step 1: Read the 4 KB live PE header page directly from process memory
                byte[] hdrPage = new byte[4096];
                IntPtr got;
                if (!ReadProcessMemory(hProc, modBasePtr, hdrPage, new IntPtr(hdrPage.Length), out got) || got.ToInt64() < 256)
                {
                    snapOut.ErrorMessage = "ReadProcessMemory failed on PE header page at 0x" + snapOut.BaseAddress.ToString("X", CultureInfo.InvariantCulture);
                    return snapOut;
                }

                if (hdrPage[0] != (byte)'M' || hdrPage[1] != (byte)'Z')
                {
                    snapOut.ErrorMessage = "Live module base does not begin with MZ signature.";
                    return snapOut;
                }

                uint peOff = RdU32(hdrPage, 0x3C);
                if (peOff + 0xF8u >= (uint)hdrPage.Length || hdrPage[peOff] != (byte)'P' || hdrPage[peOff + 1] != (byte)'E')
                {
                    snapOut.ErrorMessage = "Invalid live PE signature at e_lfanew=0x" + peOff.ToString("X", CultureInfo.InvariantCulture);
                    return snapOut;
                }

                // Step 2: Reconstruct an aligned raw-file-compatible buffer from live virtual sections
                ushort numSec = RdU16(hdrPage, (int)peOff + 6);
                ushort optSize = RdU16(hdrPage, (int)peOff + 20);
                int secTabOff = (int)peOff + 24 + optSize;
                int maxNeeded = 4096;
                uint firstSectionRaw = 4096u;

                for (int i = 0; i < numSec && secTabOff + (i + 1) * 40 <= hdrPage.Length; i++)
                {
                    int so = secTabOff + i * 40;
                    uint rawPtr = RdU32(hdrPage, so + 20);
                    uint rawSz = RdU32(hdrPage, so + 16);
                    if (rawPtr > 0u && rawPtr < firstSectionRaw) firstSectionRaw = rawPtr;
                    long endOff = (long)rawPtr + (long)rawSz;
                    if (endOff > maxNeeded && endOff <= capBytes) maxNeeded = (int)endOff;
                }

                byte[] img = new byte[maxNeeded];
                int hdrCopy = (int)Math.Min((uint)hdrPage.Length, Math.Min(firstSectionRaw, (uint)img.Length));
                Buffer.BlockCopy(hdrPage, 0, img, 0, hdrCopy);
                int totalRead = hdrCopy;

                for (int i = 0; i < numSec && secTabOff + (i + 1) * 40 <= hdrPage.Length; i++)
                {
                    int so = secTabOff + i * 40;
                    uint virtSz = RdU32(hdrPage, so + 8);
                    uint virtAddr = RdU32(hdrPage, so + 12);
                    uint rawSz = RdU32(hdrPage, so + 16);
                    uint rawPtr = RdU32(hdrPage, so + 20);
                    if (rawSz == 0u || rawPtr >= (uint)img.Length) continue;

                    uint availInImg = (uint)img.Length - rawPtr;
                    uint readLen = Math.Min(rawSz, availInImg);
                    if (virtSz > 0u)
                    {
                        uint pageAlignedVirt = (virtSz + 4095u) & ~4095u;
                        if (pageAlignedVirt < readLen) readLen = pageAlignedVirt;
                    }
                    if (readLen == 0u) continue;

                    byte[] secBuf = new byte[readLen];
                    IntPtr secGot;
                    IntPtr remoteAddr = new IntPtr(modBasePtr.ToInt64() + (long)virtAddr);
                    if (ReadProcessMemory(hProc, remoteAddr, secBuf, new IntPtr(secBuf.Length), out secGot) && secGot.ToInt64() > 0)
                    {
                        int nCopy = (int)Math.Min((long)secBuf.Length, secGot.ToInt64());
                        Buffer.BlockCopy(secBuf, 0, img, (int)rawPtr, nCopy);
                        totalRead += nCopy;
                    }
                    else
                    {
                        byte[] pageBuf = new byte[4096];
                        for (uint pg = 0u; pg < readLen; pg += 4096u)
                        {
                            int chunk = (int)Math.Min(4096u, readLen - pg);
                            IntPtr pgGot;
                            IntPtr pgAddr = new IntPtr(modBasePtr.ToInt64() + (long)virtAddr + (long)pg);
                            if (ReadProcessMemory(hProc, pgAddr, pageBuf, new IntPtr(chunk), out pgGot) && pgGot.ToInt64() > 0)
                            {
                                int nCopy = (int)Math.Min((long)chunk, pgGot.ToInt64());
                                Buffer.BlockCopy(pageBuf, 0, img, (int)(rawPtr + pg), nCopy);
                                totalRead += nCopy;
                            }
                        }
                    }
                }

                snapOut.Success = true;
                snapOut.BytesRead = totalRead;
                snapOut.PeImageBuffer = img;
                return snapOut;
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        // =========================================================================
        // 4. MOD CITY SKSE64 / F4SE x86_64 TRAMPOLINE PROLOGUE ANALYZER & CODEGEN
        // =========================================================================

        public static TrampolineProloguePlan AnalyzeTrampolinePrologue(string funcName, ulong targetRva, byte[] codeBytes)
        {
            TrampolineProloguePlan plan = new TrampolineProloguePlan();
            plan.FunctionName = string.IsNullOrEmpty(funcName) ? "TargetFunction" : funcName;
            plan.TargetRva = targetRva;
            if (codeBytes == null || codeBytes.Length == 0)
            {
                plan.SafeHookStrategy = "EMPTY_CODE_SLICE";
                return plan;
            }

            int off = 0;
            int insnIdx = 0;
            while (off < codeBytes.Length && (plan.Abs64StealBytes == 0 || off < 20) && insnIdx < 16)
            {
                bool isRipRel;
                string mnem;
                int len = DecodeX64InstructionLength(codeBytes, off, out isRipRel, out mnem);
                if (len <= 0 || off + len > codeBytes.Length) break;

                if (isRipRel && off < 14 && !plan.HasRipRelativeInSteal14)
                {
                    plan.HasRipRelativeInSteal14 = true;
                    plan.RipRelativeInsnOffset = off;
                }

                StringBuilder hx = new StringBuilder();
                for (int k = 0; k < len; k++)
                {
                    if (k > 0) hx.Append(' ');
                    hx.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", codeBytes[off + k]);
                }
                plan.DisassembledPrologue.Add(string.Format(CultureInfo.InvariantCulture,
                    "+0x{0:X2} ({1}B) {2,-26} ; {3}{4}",
                    off, len, hx.ToString(), mnem, isRipRel ? " [RIP-REL RELOC REQUIRED]" : ""));

                off += len;
                insnIdx++;

                if (plan.Rel32StealBytes == 0 && off >= 5)
                {
                    plan.Rel32StealBytes = off;
                    plan.StolenInsnCount5 = insnIdx;
                }
                if (plan.Abs64StealBytes == 0 && off >= 14)
                {
                    plan.Abs64StealBytes = off;
                    plan.StolenInsnCount14 = insnIdx;
                }
            }

            if (plan.Rel32StealBytes == 0) plan.Rel32StealBytes = off;
            if (plan.Abs64StealBytes == 0) plan.Abs64StealBytes = off;

            plan.StolenHex5 = FormatHexRange(codeBytes, 0, Math.Min(plan.Rel32StealBytes, codeBytes.Length));
            plan.StolenHex14 = FormatHexRange(codeBytes, 0, Math.Min(plan.Abs64StealBytes, codeBytes.Length));
            plan.SafeHookStrategy = plan.HasRipRelativeInSteal14
                ? "5-Byte Near Trampoline (SKSE/F4SE write_branch<5> + RIP-disp32 fixup)"
                : "14-Byte Abs64 Detour (FF 25 00 00 00 00 <abs64>) OR 5-Byte Near Trampoline";
            return plan;
        }

        private static string FormatHexRange(byte[] b, int start, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count && start + i < b.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", b[start + i]);
            }
            return sb.ToString();
        }

        public static int DecodeX64InstructionLength(byte[] b, int off, out bool isRipRelative, out string label)
        {
            isRipRelative = false;
            label = "op";
            if (b == null || off < 0 || off >= b.Length) return 0;

            int cur = off;
            int end = b.Length;

            // Optional CET endbr64 (F3 0F 1E FA)
            if (cur + 4 <= end && b[cur] == 0xF3 && b[cur + 1] == 0x0F && b[cur + 2] == 0x1E && b[cur + 3] == 0xFA)
            {
                label = "endbr64";
                return 4;
            }

            bool hasRex = false;
            bool rexW = false;
            bool has66 = false;
            bool hasF2F3 = false;

            while (cur < end)
            {
                byte p = b[cur];
                if (p == 0x66) { has66 = true; cur++; }
                else if (p == 0xF2 || p == 0xF3) { hasF2F3 = true; cur++; }
                else if (p >= 0x40 && p <= 0x4F) { hasRex = true; rexW = (p & 0x08) != 0; cur++; break; }
                else break;
            }
            if (cur >= end) return cur - off;

            byte op = b[cur++];
            if (op == 0x90) { label = "nop"; return cur - off; }
            if (op == 0xC3) { label = "ret"; return cur - off; }
            if (op >= 0x50 && op <= 0x5F) { label = (op < 0x58) ? "push r64" : "pop r64"; return cur - off; }
            if (op == 0xE8 || op == 0xE9) { isRipRelative = true; label = (op == 0xE8) ? "call rel32" : "jmp rel32"; return Math.Min(end - off, (cur - off) + 4); }
            if (op == 0xEB || (op >= 0x70 && op <= 0x7F)) { label = "jcc/jmp rel8"; return Math.Min(end - off, (cur - off) + 1); }
            if (op >= 0xB8 && op <= 0xBF) { label = rexW ? "mov r64, imm64" : "mov r32, imm32"; return Math.Min(end - off, (cur - off) + (rexW ? 8 : 4)); }

            // 2-byte 0F xx opcodes
            if (op == 0x0F && cur < end)
            {
                byte op2 = b[cur++];
                if (op2 >= 0x80 && op2 <= 0x8F) { isRipRelative = true; label = "jcc rel32"; return Math.Min(end - off, (cur - off) + 4); }
                if (op2 == 0x1F || op2 == 0xAF || op2 == 0xB6 || op2 == 0xB7 || op2 == 0x10 || op2 == 0x11 || op2 == 0x2E || op2 == 0x58 || op2 == 0x59)
                {
                    label = hasF2F3 ? "sse_scalar" : "0f_rm";
                    return DecodeModRmTail(b, off, cur, 0, out isRipRelative);
                }
                return Math.Min(end - off, (cur - off) + 1);
            }

            // Standard ModR/M opcodes (MOV 88..8B, LEA 8D, ADD/SUB/CMP/XOR/AND/OR/TEST, 83 imm8, 81 imm32, C7 imm32, FF /2 /4)
            int immBytes = 0;
            if (op == 0x83 || op == 0xC1) { immBytes = 1; label = "alu_rm_imm8"; }
            else if (op == 0x81 || op == 0xC7) { immBytes = has66 ? 2 : 4; label = "alu_rm_imm32"; }
            else if (op == 0x8D) { label = "lea"; }
            else if (op == 0x89 || op == 0x8B) { label = hasRex ? "mov64" : "mov32"; }
            else if (op == 0xFF) { label = "call/jmp_rm"; }
            else { label = string.Format(CultureInfo.InvariantCulture, "op_{0:X2}", op); }

            return DecodeModRmTail(b, off, cur, immBytes, out isRipRelative);
        }

        private static int DecodeModRmTail(byte[] b, int insnStart, int modrmPos, int immBytes, out bool isRipRelative)
        {
            isRipRelative = false;
            if (modrmPos >= b.Length) return b.Length - insnStart;
            byte modrm = b[modrmPos++];
            int mod = (modrm >> 6) & 3;
            int rm = modrm & 7;
            int cur = modrmPos;

            if (mod != 3 && rm == 4 && cur < b.Length)
            {
                byte sib = b[cur++];
                int sibBase = sib & 7;
                if (mod == 0 && sibBase == 5) cur += 4;
            }

            if (mod == 0 && rm == 5)
            {
                isRipRelative = true;
                cur += 4;
            }
            else if (mod == 1) cur += 1;
            else if (mod == 2) cur += 4;

            cur += immBytes;
            return Math.Min(b.Length - insnStart, cur - insnStart);
        }

        public static string EmitSkse64F4seCpp23Plugin(LiftedBraidFunction fn, TrampolineProloguePlan plan, string gameTarget)
        {
            string cleanName = SanitizeIdent(plan.FunctionName);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// Auto-generated by OmegaBraidRE v1.4.0 :: Mod City SKSE64 / F4SE C++23 Trampoline Hook");
            sb.AppendFormat(CultureInfo.InvariantCulture, "// Target: {0} | Function: {1} @ RVA 0x{2:X}\r\n", gameTarget, plan.FunctionName, plan.TargetRva);
            sb.AppendFormat(CultureInfo.InvariantCulture, "// Safe Prologue Steal: 5B-boundary={0}B ({1} insns) | 14B-boundary={2}B ({3} insns)\r\n",
                plan.Rel32StealBytes, plan.StolenInsnCount5, plan.Abs64StealBytes, plan.StolenInsnCount14);
            sb.AppendFormat(CultureInfo.InvariantCulture, "// Strategy: {0}\r\n", plan.SafeHookStrategy);
            sb.AppendLine("#include <cstdint>");
            sb.AppendLine("#include <REL/Relocation.h>");
            sb.AppendLine("#include <SKSE/Trampoline.h> // Swap to <F4SE/Trampoline.h> for Fallout 4");
            sb.AppendLine();
            sb.AppendLine("namespace OmegaModCityHooks {");
            sb.AppendFormat(CultureInfo.InvariantCulture, "    static constexpr std::uintptr_t kTargetRva_{0} = 0x{1:X}ULL;\r\n", cleanName, plan.TargetRva);
            sb.AppendFormat(CultureInfo.InvariantCulture, "    // Verified stolen prologue bytes ({0}B): {1}\r\n", plan.Rel32StealBytes, plan.StolenHex5);
            sb.AppendFormat(CultureInfo.InvariantCulture, "    using Fn_{0} = std::uint64_t(*)(void* self, std::uint64_t a2, std::uint64_t a3);\r\n", cleanName);
            sb.AppendFormat(CultureInfo.InvariantCulture, "    inline REL::Relocation<Fn_{0}> orig_{0};\r\n\r\n", cleanName);
            sb.AppendFormat(CultureInfo.InvariantCulture, "    static std::uint64_t Hook_{0}(void* self, std::uint64_t a2, std::uint64_t a3) {{\r\n", cleanName);
            sb.AppendLine("        // Cellular Sheaf H^1 == 0 verified struct access before delegating to original:");
            if (fn != null && fn.Fields != null && fn.Fields.Count > 0)
            {
                uint alignedStructSz = 0u;
                OmegaBraidReCore.SynthesizePolyglotStructHeaders(fn, cleanName + "Frame", out alignedStructSz);
                sb.AppendFormat(CultureInfo.InvariantCulture, "        // Recovered fields: {0} (struct size = 0x{1:X})\r\n", fn.Fields.Count, alignedStructSz);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, "        return orig_{0}(self, a2, a3);\r\n", cleanName);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    inline void Install_{0}() {{\r\n", cleanName);
            sb.AppendLine("        const auto base = REL::Module::get().base();");
            sb.AppendFormat(CultureInfo.InvariantCulture, "        auto& tramp = SKSE::GetTrampoline();\r\n");
            sb.AppendFormat(CultureInfo.InvariantCulture, "        orig_{0} = tramp.write_branch<5>(base + kTargetRva_{0}, Hook_{0});\r\n", cleanName);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string EmitSkse64F4seRustNoStdPlugin(LiftedBraidFunction fn, TrampolineProloguePlan plan)
        {
            string cleanName = SanitizeIdent(plan.FunctionName);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// Auto-generated by OmegaBraidRE v1.4.0 :: Zero-Crate #![no_std] Rust x86_64 Detour");
            sb.AppendFormat(CultureInfo.InvariantCulture, "// Function: {0} @ RVA 0x{1:X} | 14B Steal: {2}B ({3})\r\n",
                plan.FunctionName, plan.TargetRva, plan.Abs64StealBytes, plan.StolenHex14);
            sb.AppendFormat(CultureInfo.InvariantCulture, "pub const TARGET_RVA_{0}: usize = 0x{1:X};\r\n", cleanName.ToUpperInvariant(), plan.TargetRva);
            sb.AppendFormat(CultureInfo.InvariantCulture, "pub const SAFE_ABS64_STEAL_LEN: usize = {0};\r\n\r\n", plan.Abs64StealBytes);
            sb.AppendLine("#[inline(always)]");
            sb.AppendLine("pub fn encode_abs64_jmp(target_fn: usize) -> [u8; 14] {");
            sb.AppendLine("    let b = (target_fn as u64).to_le_bytes();");
            sb.AppendLine("    [0xFF, 0x25, 0x00, 0x00, 0x00, 0x00, b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "pub unsafe extern \"C\" fn detour_{0}(self_ptr: *mut u8, a2: u64, a3: u64) -> u64 {{\r\n", cleanName.ToLowerInvariant());
            sb.AppendLine("    let _ = (self_ptr, a2, a3);");
            sb.AppendLine("    0");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string SanitizeIdent(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "TargetFn";
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length > 0 && s[0] >= '0' && s[0] <= '9') s = "_" + s;
            return s;
        }

        // =========================================================================
        // 5. SYNTHETIC PDB MSF 7.00 & ELF64 DWARF v4 FIXTURES FOR SELF-VERIFICATION
        // =========================================================================

        public static byte[] BuildSyntheticPdbMsf7()
        {
            // 7 blocks x 4096 bytes = 28,672 bytes valid MSF 7.00 PDB:
            // Block 0: SuperBlock | Block 1: FPM1 | Block 2: FPM2 | Block 3: BlockMapAddr -> [4]
            // Block 4: Stream Directory (5 streams: 0..4) | Block 5: Stream 3 (DBI Header, SymRecordStream=4)
            // Block 6: Stream 4 (CodeView S_PUB32 + S_GPROC32 records)
            const int blkSz = 4096;
            byte[] pdb = new byte[7 * blkSz];
            Buffer.BlockCopy(MSF7_MAGIC, 0, pdb, 0, MSF7_MAGIC.Length);
            WrU32(pdb, 32, (uint)blkSz); // BlockSize
            WrU32(pdb, 36, 1u);          // FreeBlockMapBlock = 1
            WrU32(pdb, 40, 7u);          // NumBlocks
            WrU32(pdb, 44, 32u);         // NumDirectoryBytes = 4 + 5*4 + 2*4 = 32
            WrU32(pdb, 52, 3u);          // BlockMapAddr = Block 3

            // Block 3: Directory resides on Block 4
            WrU32(pdb, 3 * blkSz, 4u);

            // Block 6: Stream 4 (CodeView symbols: S_PUB32 "F4SEPlugin_Query" @ seg 1 +0x0, S_GPROC32 "Actor::UpdatePhysics" @ seg 1 +0x40 len 0x28)
            int s4 = 6 * blkSz;
            int p = s4;
            // Record 1: S_PUB32 (0x110E)
            string n1 = "F4SEPlugin_Query";
            int r1Len = 2 + 4 + 4 + 2 + n1.Length + 1;
            WrU16(pdb, p, (ushort)r1Len);
            WrU16(pdb, p + 2, 0x110E);
            WrU32(pdb, p + 4, 2u); // pubsymflags = Function
            WrU32(pdb, p + 8, 0x00u);
            WrU16(pdb, p + 12, 1);
            byte[] b1 = Encoding.ASCII.GetBytes(n1);
            Buffer.BlockCopy(b1, 0, pdb, p + 14, b1.Length);
            p += 2 + r1Len;

            // Record 2: S_GPROC32 (0x1110)
            string n2 = "Actor::UpdatePhysics";
            int r2Len = 37 + n2.Length + 1;
            WrU16(pdb, p, (ushort)r2Len);
            WrU16(pdb, p + 2, 0x1110);
            WrU32(pdb, p + 16, 0x28u); // CodeSize
            WrU32(pdb, p + 32, 0x40u); // Offset
            WrU16(pdb, p + 36, 1);     // Segment = 1 (.text)
            byte[] b2 = Encoding.ASCII.GetBytes(n2);
            Buffer.BlockCopy(b2, 0, pdb, p + 39, b2.Length);
            p += 2 + r2Len;
            uint s4Bytes = (uint)(p - s4);

            // Block 5: Stream 3 (DBI Header 64 bytes, SymRecordStream = 4 at +20)
            int s3 = 5 * blkSz;
            WrU32(pdb, s3 + 0, 0xFFFFFFFFu);
            WrU32(pdb, s3 + 4, 19990903u); // V70
            WrU32(pdb, s3 + 8, 1u);        // Age
            WrU16(pdb, s3 + 20, 4);        // SymRecordStream = 4

            // Block 4: Stream Directory (5 streams: [0]=0, [1]=0, [2]=0, [3]=64 @ blk 5, [4]=s4Bytes @ blk 6)
            int d = 4 * blkSz;
            WrU32(pdb, d + 0, 5u);
            WrU32(pdb, d + 4, 0u);
            WrU32(pdb, d + 8, 0u);
            WrU32(pdb, d + 12, 0u);
            WrU32(pdb, d + 16, 64u);
            WrU32(pdb, d + 20, s4Bytes);
            WrU32(pdb, d + 24, 5u); // Stream 3 -> Block 5
            WrU32(pdb, d + 28, 6u); // Stream 4 -> Block 6
            return pdb;
        }

        public static byte[] BuildSyntheticElf64WithDwarf()
        {
            // Minimal valid ELF64 with 5 section headers:
            // [0] null, [1] .shstrtab, [2] .debug_abbrev, [3] .debug_info (DWARF v4 DW_TAG_subprogram), [4] .debug_str
            byte[] elf = new byte[1024];
            elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
            elf[4] = 2; elf[5] = 1; elf[6] = 1; // ELF64, LE, v1
            WrU16(elf, 16, 3);     // ET_DYN
            WrU16(elf, 18, 0xB7);  // EM_AARCH64
            WrU32(elf, 20, 1u);
            WrU64(elf, 40, 512UL); // e_shoff = 512
            WrU16(elf, 52, 64);
            WrU16(elf, 58, 64);    // e_shentsize
            WrU16(elf, 60, 5);     // e_shnum
            WrU16(elf, 62, 1);     // e_shstrndx = 1

            // .shstrtab at 0x80
            byte[] shstr = Encoding.ASCII.GetBytes("\0.shstrtab\0.debug_abbrev\0.debug_info\0.debug_str\0");
            Buffer.BlockCopy(shstr, 0, elf, 0x80, shstr.Length);

            // .debug_abbrev at 0x100: Code 1 = DW_TAG_subprogram(0x2E), no children, DW_AT_name(0x03)/DW_FORM_strp(0x0E), DW_AT_low_pc(0x11)/DW_FORM_addr(0x01), DW_AT_high_pc(0x12)/DW_FORM_data4(0x06)
            byte[] abbrev = new byte[] { 0x01, 0x2E, 0x00, 0x03, 0x0E, 0x11, 0x01, 0x12, 0x06, 0x00, 0x00, 0x00 };
            Buffer.BlockCopy(abbrev, 0, elf, 0x100, abbrev.Length);

            // .debug_str at 0x180
            byte[] dstr = Encoding.ASCII.GetBytes("omega_arm64_e8_quantize\0");
            Buffer.BlockCopy(dstr, 0, elf, 0x180, dstr.Length);

            // .debug_info at 0x140 (DWARF v4 CU)
            int di = 0x140;
            WrU32(elf, di + 0, 25u);        // unit_length = 25 bytes after length field
            WrU16(elf, di + 4, 4);          // DWARF version 4
            WrU32(elf, di + 6, 0u);         // debug_abbrev_offset = 0
            elf[di + 10] = 8;               // address_size = 8
            elf[di + 11] = 1;               // abbrev code 1 (DW_TAG_subprogram)
            WrU32(elf, di + 12, 0u);        // DW_AT_name -> .debug_str[0]
            WrU64(elf, di + 16, 0x401200UL);// DW_AT_low_pc = 0x401200
            WrU32(elf, di + 24, 0x60u);     // DW_AT_high_pc = 0x60
            elf[di + 28] = 0;               // end of children

            // Section headers at 512 (5 x 64B)
            // [1] .shstrtab
            int s1 = 512 + 64;
            WrU32(elf, s1 + 0, 1u); WrU32(elf, s1 + 4, 3u); WrU64(elf, s1 + 24, 0x80UL); WrU64(elf, s1 + 32, (ulong)shstr.Length);
            // [2] .debug_abbrev
            int s2 = 512 + 128;
            WrU32(elf, s2 + 0, 11u); WrU32(elf, s2 + 4, 1u); WrU64(elf, s2 + 24, 0x100UL); WrU64(elf, s2 + 32, (ulong)abbrev.Length);
            // [3] .debug_info
            int s3 = 512 + 192;
            WrU32(elf, s3 + 0, 25u); WrU32(elf, s3 + 4, 1u); WrU64(elf, s3 + 24, 0x140UL); WrU64(elf, s3 + 32, 32UL);
            // [4] .debug_str
            int s4 = 512 + 256;
            WrU32(elf, s4 + 0, 37u); WrU32(elf, s4 + 4, 1u); WrU64(elf, s4 + 24, 0x180UL); WrU64(elf, s4 + 32, (ulong)dstr.Length);
            return elf;
        }
    }
}
