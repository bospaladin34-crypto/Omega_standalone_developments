using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmegaBraidRE
{
    public sealed class PeSectionDesc
    {
        public string Name = "";
        public uint VirtualAddress;
        public uint VirtualSize;
        public uint RawOffset;
        public uint RawSize;
        public uint Characteristics;
    }

    public sealed class PdataFunctionEntry
    {
        public uint BeginRva;
        public uint EndRva;
        public uint UnwindInfoRva;
        public byte UnwindVersion;
        public byte UnwindFlags;
        public byte PrologSize;
        public byte CodeCount;
    }

    public sealed class PeExportEntry
    {
        public uint Ordinal;
        public uint FunctionRva;
        public string Name = "";
    }

    public sealed class PeImportEntry
    {
        public string DllName = "";
        public string FunctionName = "";
        public ushort HintOrOrdinal;
        public bool IsByOrdinal;
        public uint IatSlotRva;
    }

    public sealed class ClrStreamDesc
    {
        public string Name = "";
        public uint Offset;
        public uint Size;
    }

    public sealed class ClrMethodDefEntry
    {
        public uint Token;
        public uint Rva;
        public ushort ImplFlags;
        public ushort Flags;
        public string TypeNamespace = "";
        public string TypeName = "";
        public string MethodName = "";
        public string FullName = "";
        public bool IsEntryPoint;
        public bool IsCompilerGenerated;
    }

    public sealed class ClrAssemblyReport
    {
        public bool IsClrAssembly = false;
        public bool IlOnly = false;
        public bool Prefer32Bit = false;
        public bool StrongNameSigned = false;
        public ushort MajorRuntime = 0;
        public ushort MinorRuntime = 0;
        public uint MetaDataRva = 0u;
        public uint MetaDataSize = 0u;
        public uint Flags = 0u;
        public uint EntryPointToken = 0u;
        public bool BsjbVerified = false;
        public bool TildeTablesDecoded = false;
        public string MetadataVersion = "";
        public string ModuleName = "";
        public string EntryPointFullName = "";
        public int TypeDefCount = 0;
        public int FieldDefCount = 0;
        public int MethodDefCount = 0;
        public int UsHeapAbsOffset = -1;
        public int UsHeapLength = 0;
        public List<ClrStreamDesc> Streams = new List<ClrStreamDesc>();
        public List<string> IdentifierSamples = new List<string>();
        public List<string> UserStringSamples = new List<string>();
        public List<ClrMethodDefEntry> Methods = new List<ClrMethodDefEntry>();
        public Dictionary<uint, string> MethodTokenToName = new Dictionary<uint, string>();
        public Dictionary<uint, string> FieldTokenToName = new Dictionary<uint, string>();
    }

    public static class OmegaBraidRePeAndClr
    {
        public const uint EcmaBsjbMagic = 0x424A5342u; // "BSJB"

        public static bool TryRvaToFileOffset(byte[] buf, List<PeSectionDesc> sections, uint rva, out int fileOffset)
        {
            fileOffset = -1;
            if (buf == null || rva == 0u) return false;

            if (sections != null && sections.Count > 0)
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    PeSectionDesc s = sections[i];
                    uint span = (s.VirtualSize > s.RawSize) ? s.VirtualSize : s.RawSize;
                    if (span > 0u && rva >= s.VirtualAddress && (rva - s.VirtualAddress) < span)
                    {
                        uint delta = rva - s.VirtualAddress;
                        if (delta < s.RawSize)
                        {
                            ulong cand = (ulong)s.RawOffset + (ulong)delta;
                            if (cand < (ulong)buf.Length)
                            {
                                fileOffset = (int)cand;
                                return true;
                            }
                        }
                    }
                }
                return false;
            }

            if ((ulong)rva < (ulong)buf.Length)
            {
                fileOffset = (int)rva;
                return true;
            }
            return false;
        }

        public static string ReadNullTermAscii(byte[] buf, int off, int maxLen)
        {
            if (buf == null || off < 0 || off >= buf.Length || maxLen <= 0) return "";
            int end = Math.Min(buf.Length, off + maxLen);
            StringBuilder sb = new StringBuilder(Math.Min(64, maxLen));
            for (int i = off; i < end; i++)
            {
                byte b = buf[i];
                if (b == 0) break;
                if (b < 0x20 || b > 0x7E) return "";
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        public static bool TryExtractStringAtRva(byte[] buf, List<PeSectionDesc> sections, uint targetRva, out string literal, out bool isUtf16)
        {
            literal = "";
            isUtf16 = false;
            int off;
            if (!TryRvaToFileOffset(buf, sections, targetRva, out off)) return false;
            if (off < 0 || off >= buf.Length) return false;

            if (off + 6 <= buf.Length &&
                buf[off] >= 0x20 && buf[off] <= 0x7E && buf[off + 1] == 0 &&
                buf[off + 2] >= 0x20 && buf[off + 2] <= 0x7E && buf[off + 3] == 0 &&
                buf[off + 4] >= 0x20 && buf[off + 4] <= 0x7E && buf[off + 5] == 0)
            {
                StringBuilder u16 = new StringBuilder(64);
                int p = off;
                while (p + 1 < buf.Length && u16.Length < 96)
                {
                    byte lo = buf[p];
                    byte hi = buf[p + 1];
                    if (lo == 0 && hi == 0) break;
                    if (hi != 0 || lo < 0x20 || lo > 0x7E) break;
                    u16.Append((char)lo);
                    p += 2;
                }
                if (u16.Length >= 3)
                {
                    literal = u16.ToString();
                    isUtf16 = true;
                    return true;
                }
            }

            if (off + 4 <= buf.Length && buf[off] >= 0x20 && buf[off] <= 0x7E)
            {
                StringBuilder asc = new StringBuilder(64);
                int p = off;
                bool terminated = false;
                while (p < buf.Length && asc.Length < 96)
                {
                    byte ch = buf[p];
                    if (ch == 0) { terminated = true; break; }
                    if (ch < 0x20 || ch > 0x7E) return false;
                    asc.Append((char)ch);
                    p++;
                }
                if ((terminated && asc.Length >= 3) || asc.Length >= 6)
                {
                    literal = asc.ToString();
                    isUtf16 = false;
                    return true;
                }
            }
            return false;
        }

        public static void ParsePeDirectories(byte[] buf, BinaryContainerReport rep)
        {
            if (buf == null || rep == null || !rep.Valid) return;
            if (rep.Format != BraidContainerKind.Pe32Plus && rep.Format != BraidContainerKind.Pe32) return;

            bool isPe64 = (rep.Format == BraidContainerKind.Pe32Plus);

            // 1. Parse .pdata RUNTIME_FUNCTION + UNWIND_INFO (DataDirectory[3])
            if (rep.PdataDirRva != 0u && rep.PdataDirSize >= 12u)
            {
                int pdOff;
                if (TryRvaToFileOffset(buf, rep.Sections, rep.PdataDirRva, out pdOff))
                {
                    int maxEntries = (int)Math.Min(4096u, rep.PdataDirSize / 12u);
                    for (int i = 0; i < maxEntries && pdOff + (i + 1) * 12 <= buf.Length; i++)
                    {
                        int e = pdOff + i * 12;
                        uint bRva = OmegaBraidReCore.ReadU32Le(buf, e);
                        uint eRva = OmegaBraidReCore.ReadU32Le(buf, e + 4);
                        uint uRva = OmegaBraidReCore.ReadU32Le(buf, e + 8);
                        if (bRva == 0u && eRva == 0u) break;
                        if (eRva > bRva && (eRva - bRva) <= 0x40000u)
                        {
                            PdataFunctionEntry pf = new PdataFunctionEntry();
                            pf.BeginRva = bRva;
                            pf.EndRva = eRva;
                            pf.UnwindInfoRva = uRva;

                            int uOff;
                            if (TryRvaToFileOffset(buf, rep.Sections, uRva & ~1u, out uOff) && uOff + 4 <= buf.Length)
                            {
                                pf.UnwindVersion = (byte)(buf[uOff] & 0x07);
                                pf.UnwindFlags = (byte)((buf[uOff] >> 3) & 0x1F);
                                pf.PrologSize = buf[uOff + 1];
                                pf.CodeCount = buf[uOff + 2];
                            }
                            rep.PdataFunctions.Add(pf);
                        }
                    }
                }
            }

            // 2. Parse Export Directory EAT (DataDirectory[0])
            if (rep.ExportDirRva != 0u && rep.ExportDirSize >= 40u)
            {
                int expOff;
                if (TryRvaToFileOffset(buf, rep.Sections, rep.ExportDirRva, out expOff) && expOff + 40 <= buf.Length)
                {
                    uint dllNameRva = OmegaBraidReCore.ReadU32Le(buf, expOff + 12);
                    uint ordBase = OmegaBraidReCore.ReadU32Le(buf, expOff + 16);
                    uint numFuncs = Math.Min(4096u, OmegaBraidReCore.ReadU32Le(buf, expOff + 20));
                    uint numNames = Math.Min(4096u, OmegaBraidReCore.ReadU32Le(buf, expOff + 24));
                    uint addrFuncsRva = OmegaBraidReCore.ReadU32Le(buf, expOff + 28);
                    uint addrNamesRva = OmegaBraidReCore.ReadU32Le(buf, expOff + 32);
                    uint addrOrdsRva = OmegaBraidReCore.ReadU32Le(buf, expOff + 36);

                    int dnOff;
                    if (TryRvaToFileOffset(buf, rep.Sections, dllNameRva, out dnOff))
                    {
                        rep.ExportModuleName = ReadNullTermAscii(buf, dnOff, 96);
                    }

                    int fnTbl, nmTbl, ordTbl;
                    if (TryRvaToFileOffset(buf, rep.Sections, addrFuncsRva, out fnTbl) &&
                        TryRvaToFileOffset(buf, rep.Sections, addrNamesRva, out nmTbl) &&
                        TryRvaToFileOffset(buf, rep.Sections, addrOrdsRva, out ordTbl))
                    {
                        for (uint i = 0; i < numNames; i++)
                        {
                            int nPos = nmTbl + (int)(i * 4u);
                            int oPos = ordTbl + (int)(i * 2u);
                            if (nPos + 4 > buf.Length || oPos + 2 > buf.Length) break;

                            uint symRva = OmegaBraidReCore.ReadU32Le(buf, nPos);
                            ushort funcIdx = OmegaBraidReCore.ReadU16Le(buf, oPos);
                            if (funcIdx >= numFuncs) continue;

                            int fPos = fnTbl + (int)funcIdx * 4;
                            if (fPos + 4 > buf.Length) continue;
                            uint funcRva = OmegaBraidReCore.ReadU32Le(buf, fPos);

                            int symOff;
                            string symName = "";
                            if (TryRvaToFileOffset(buf, rep.Sections, symRva, out symOff))
                            {
                                symName = ReadNullTermAscii(buf, symOff, 128);
                            }
                            if (symName.Length > 0 && funcRva != 0u)
                            {
                                PeExportEntry ex = new PeExportEntry();
                                ex.Ordinal = ordBase + funcIdx;
                                ex.FunctionRva = funcRva;
                                ex.Name = symName;
                                rep.Exports.Add(ex);
                                if (!rep.ExportRvaToName.ContainsKey(funcRva))
                                {
                                    rep.ExportRvaToName[funcRva] = symName;
                                }
                            }
                        }
                    }
                }
            }

            // 3. Parse Import Directory IAT (DataDirectory[1])
            if (rep.ImportDirRva != 0u && rep.ImportDirSize >= 20u)
            {
                int impOff;
                if (TryRvaToFileOffset(buf, rep.Sections, rep.ImportDirRva, out impOff))
                {
                    int ptrSz = isPe64 ? 8 : 4;
                    for (int d = 0; d < 256 && impOff + (d + 1) * 20 <= buf.Length; d++)
                    {
                        int desc = impOff + d * 20;
                        uint origFirstThunk = OmegaBraidReCore.ReadU32Le(buf, desc);
                        uint nameRva = OmegaBraidReCore.ReadU32Le(buf, desc + 12);
                        uint firstThunk = OmegaBraidReCore.ReadU32Le(buf, desc + 16);
                        if (origFirstThunk == 0u && nameRva == 0u && firstThunk == 0u) break;

                        int dllNameOff;
                        string dllName = "UNKNOWN.dll";
                        if (TryRvaToFileOffset(buf, rep.Sections, nameRva, out dllNameOff))
                        {
                            string parsedDll = ReadNullTermAscii(buf, dllNameOff, 96);
                            if (parsedDll.Length > 0) dllName = parsedDll;
                        }

                        uint thunkLookupRva = (origFirstThunk != 0u) ? origFirstThunk : firstThunk;
                        int thunkOff;
                        if (!TryRvaToFileOffset(buf, rep.Sections, thunkLookupRva, out thunkOff)) continue;

                        for (int k = 0; k < 1024 && thunkOff + (k + 1) * ptrSz <= buf.Length; k++)
                        {
                            ulong thunkVal = isPe64
                                ? OmegaBraidReCore.ReadU64Le(buf, thunkOff + k * 8)
                                : (ulong)OmegaBraidReCore.ReadU32Le(buf, thunkOff + k * 4);
                            if (thunkVal == 0UL) break;

                            bool isOrd = isPe64 ? ((thunkVal & 0x8000000000000000UL) != 0UL) : ((thunkVal & 0x80000000UL) != 0UL);
                            uint slotRva = firstThunk + (uint)(k * ptrSz);

                            PeImportEntry ie = new PeImportEntry();
                            ie.DllName = dllName;
                            ie.IatSlotRva = slotRva;
                            ie.IsByOrdinal = isOrd;

                            if (isOrd)
                            {
                                ie.HintOrOrdinal = (ushort)(thunkVal & 0xFFFFUL);
                                ie.FunctionName = "Ordinal#" + ie.HintOrOrdinal.ToString(CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                uint ibnRva = (uint)(thunkVal & 0x7FFFFFFFUL);
                                int ibnOff;
                                if (TryRvaToFileOffset(buf, rep.Sections, ibnRva, out ibnOff) && ibnOff + 3 <= buf.Length)
                                {
                                    ie.HintOrOrdinal = OmegaBraidReCore.ReadU16Le(buf, ibnOff);
                                    ie.FunctionName = ReadNullTermAscii(buf, ibnOff + 2, 128);
                                }
                                if (string.IsNullOrEmpty(ie.FunctionName))
                                {
                                    ie.FunctionName = "import_0x" + ibnRva.ToString("X", CultureInfo.InvariantCulture);
                                }
                            }

                            rep.Imports.Add(ie);
                            if (slotRva != 0u && !rep.IatSlotRvaToSymbol.ContainsKey(slotRva))
                            {
                                rep.IatSlotRvaToSymbol[slotRva] = ie.DllName + "!" + ie.FunctionName;
                            }
                        }
                    }
                }
            }

            // 4. Parse .NET CLR Runtime Header (DataDirectory[14] IMAGE_COR20_HEADER + ECMA-335 BSJB + #~ Tables)
            if (rep.ClrDirRva != 0u && rep.ClrDirSize >= 72u)
            {
                int corOff;
                if (TryRvaToFileOffset(buf, rep.Sections, rep.ClrDirRva, out corOff) && corOff + 72 <= buf.Length)
                {
                    uint cb = OmegaBraidReCore.ReadU32Le(buf, corOff);
                    if (cb >= 72u)
                    {
                        ClrAssemblyReport clr = rep.ClrInfo;
                        clr.IsClrAssembly = true;
                        clr.MajorRuntime = OmegaBraidReCore.ReadU16Le(buf, corOff + 4);
                        clr.MinorRuntime = OmegaBraidReCore.ReadU16Le(buf, corOff + 6);
                        clr.MetaDataRva = OmegaBraidReCore.ReadU32Le(buf, corOff + 8);
                        clr.MetaDataSize = OmegaBraidReCore.ReadU32Le(buf, corOff + 12);
                        clr.Flags = OmegaBraidReCore.ReadU32Le(buf, corOff + 16);
                        clr.EntryPointToken = OmegaBraidReCore.ReadU32Le(buf, corOff + 20);
                        clr.IlOnly = (clr.Flags & 0x01u) != 0u;
                        clr.Prefer32Bit = (clr.Flags & 0x02u) != 0u;
                        clr.StrongNameSigned = (clr.Flags & 0x08u) != 0u;

                        int mdOff;
                        if (TryRvaToFileOffset(buf, rep.Sections, clr.MetaDataRva, out mdOff) && mdOff + 20 <= buf.Length)
                        {
                            uint sig = OmegaBraidReCore.ReadU32Le(buf, mdOff);
                            if (sig == EcmaBsjbMagic)
                            {
                                clr.BsjbVerified = true;
                                int verLen = (int)OmegaBraidReCore.ReadU32Le(buf, mdOff + 12);
                                int alignedVerLen = (verLen + 3) & ~3;
                                if (verLen > 0 && verLen <= 64 && mdOff + 16 + alignedVerLen + 4 <= buf.Length)
                                {
                                    clr.MetadataVersion = ReadNullTermAscii(buf, mdOff + 16, verLen);
                                    int stHdrPos = mdOff + 16 + alignedVerLen + 2;
                                    ushort streamCount = OmegaBraidReCore.ReadU16Le(buf, stHdrPos);
                                    int cur = stHdrPos + 2;

                                    int tildeAbsOff = -1, tildeLen = 0;
                                    int strAbsOff = -1, strLen = 0;

                                    for (int s = 0; s < streamCount && s < 16 && cur + 8 < buf.Length; s++)
                                    {
                                        uint sOff = OmegaBraidReCore.ReadU32Le(buf, cur);
                                        uint sSz = OmegaBraidReCore.ReadU32Le(buf, cur + 4);
                                        cur += 8;
                                        StringBuilder sn = new StringBuilder(16);
                                        while (cur < buf.Length && sn.Length < 32)
                                        {
                                            byte b = buf[cur++];
                                            if (b == 0) break;
                                            if (b >= 0x20 && b <= 0x7E) sn.Append((char)b);
                                        }
                                        int rel = cur - mdOff;
                                        int rem = rel & 3;
                                        if (rem != 0) cur += (4 - rem);

                                        ClrStreamDesc sd = new ClrStreamDesc();
                                        sd.Name = sn.ToString();
                                        sd.Offset = sOff;
                                        sd.Size = sSz;
                                        clr.Streams.Add(sd);

                                        ulong absStreamCand = (ulong)mdOff + (ulong)sOff;
                                        if (absStreamCand < (ulong)buf.Length)
                                        {
                                            int absStreamOff = (int)absStreamCand;
                                            int clampedSz = (int)Math.Min((ulong)sSz, (ulong)Math.Max(0, buf.Length - absStreamOff));
                                            if (sd.Name == "#~" || sd.Name == "#-")
                                            {
                                                tildeAbsOff = absStreamOff;
                                                tildeLen = clampedSz;
                                            }
                                            else if (sd.Name == "#Strings")
                                            {
                                                strAbsOff = absStreamOff;
                                                strLen = clampedSz;
                                                HarvestClrIdentifierHeap(buf, absStreamOff, clampedSz, clr.IdentifierSamples, 36);
                                            }
                                            else if (sd.Name == "#US")
                                            {
                                                clr.UsHeapAbsOffset = absStreamOff;
                                                clr.UsHeapLength = clampedSz;
                                                HarvestClrUserStringsHeap(buf, absStreamOff, clampedSz, clr.UserStringSamples, 24);
                                            }
                                        }
                                    }

                                    if (tildeAbsOff > 0 && tildeLen >= 24 && strAbsOff > 0 && strLen > 0)
                                    {
                                        ParseEcma335TildeTables(buf, tildeAbsOff, tildeLen, strAbsOff, strLen, clr);
                                    }
                                }
                            }
                        }

                        if (clr.IlOnly || (clr.BsjbVerified && rep.PdataFunctions.Count == 0))
                        {
                            rep.Isa = BraidArchIsa.DotNetCil;
                        }
                    }
                }
            }
        }

        private static uint ReadTableIndex(byte[] buf, int off, int sz)
        {
            return (sz == 4) ? OmegaBraidReCore.ReadU32Le(buf, off) : (uint)OmegaBraidReCore.ReadU16Le(buf, off);
        }

        private static string ReadStringsHeapEntry(byte[] buf, int strHeapOff, int strHeapLen, uint idx)
        {
            if (strHeapOff <= 0 || idx >= (uint)strHeapLen) return "";
            int abs = strHeapOff + (int)idx;
            int maxLen = Math.Min(160, strHeapOff + strHeapLen - abs);
            return ReadNullTermAscii(buf, abs, maxLen);
        }

        /// Parses ECMA-335 Partition II 24.2.6 `#~` Metadata Tables 0x00 (Module), 0x01 (TypeRef),
        /// 0x02 (TypeDef), 0x04 (Field), and 0x06 (MethodDef) for exact `Type::Method` names & RVAs!
        public static void ParseEcma335TildeTables(
            byte[] buf, int tildeOff, int tildeLen, int strHeapOff, int strHeapLen, ClrAssemblyReport clr)
        {
            if (buf == null || clr == null || tildeLen < 24 || tildeOff < 0 || tildeOff + tildeLen > buf.Length) return;

            byte heapSizes = buf[tildeOff + 6];
            int strIdxSz = ((heapSizes & 0x01) != 0) ? 4 : 2;
            int guidIdxSz = ((heapSizes & 0x02) != 0) ? 4 : 2;
            int blobIdxSz = ((heapSizes & 0x04) != 0) ? 4 : 2;

            ulong validMask = OmegaBraidReCore.ReadU64Le(buf, tildeOff + 8);
            uint[] rows = new uint[64];
            int tildeEnd = tildeOff + tildeLen;
            int cur = tildeOff + 24;

            for (int t = 0; t < 64; t++)
            {
                if (((validMask >> t) & 1UL) != 0UL)
                {
                    if (cur + 4 > tildeEnd) return;
                    rows[t] = OmegaBraidReCore.ReadU32Le(buf, cur);
                    cur += 4;
                }
            }

            uint maxResScope = Math.Max(Math.Max(rows[0x00], rows[0x01]), Math.Max(rows[0x1A], rows[0x23]));
            int resScopeSz = (maxResScope >= 16384u) ? 4 : 2;

            uint maxTdOrRef = Math.Max(rows[0x02], Math.Max(rows[0x01], rows[0x1B]));
            int tdOrRefSz = (maxTdOrRef >= 16384u) ? 4 : 2;

            int fieldIdxSz = (rows[0x04] >= 65536u) ? 4 : 2;
            int methodIdxSz = (rows[0x06] >= 65536u) ? 4 : 2;
            int paramIdxSz = (rows[0x08] >= 65536u) ? 4 : 2;

            // Table 0x00: Module [Generation:2, Name:str, Mvid:guid, EncId:guid, EncBaseId:guid]
            if (rows[0x00] > 0u)
            {
                int rowSz00 = 2 + strIdxSz + 3 * guidIdxSz;
                ulong bytes00 = (ulong)rows[0x00] * (ulong)rowSz00;
                if ((ulong)cur + bytes00 > (ulong)tildeEnd) return;
                uint modNameIdx = ReadTableIndex(buf, cur + 2, strIdxSz);
                clr.ModuleName = ReadStringsHeapEntry(buf, strHeapOff, strHeapLen, modNameIdx);
                cur += (int)bytes00;
            }

            // Table 0x01: TypeRef [ResolutionScope, TypeName:str, TypeNamespace:str]
            if (rows[0x01] > 0u)
            {
                int rowSz01 = resScopeSz + 2 * strIdxSz;
                ulong bytes01 = (ulong)rows[0x01] * (ulong)rowSz01;
                if ((ulong)cur + bytes01 > (ulong)tildeEnd) return;
                cur += (int)bytes01;
            }

            // Table 0x02: TypeDef [Flags:4, TypeName:str, TypeNamespace:str, Extends:tdOrRef, FieldList:fld, MethodList:mtd]
            int nTd = 0;
            string[] tdNames = null;
            string[] tdNamespaces = null;
            uint[] tdMethodStart = null;
            if (rows[0x02] > 0u)
            {
                int rowSz02 = 4 + 2 * strIdxSz + tdOrRefSz + fieldIdxSz + methodIdxSz;
                ulong bytes02 = (ulong)rows[0x02] * (ulong)rowSz02;
                if ((ulong)cur + bytes02 > (ulong)tildeEnd) return;

                clr.TypeDefCount = (int)rows[0x02];
                nTd = (int)Math.Min(rows[0x02], 8192u);
                tdNames = new string[nTd];
                tdNamespaces = new string[nTd];
                tdMethodStart = new uint[nTd];

                int mListOffsetInRow = 4 + 2 * strIdxSz + tdOrRefSz + fieldIdxSz;
                for (int i = 0; i < nTd; i++)
                {
                    int rOff = cur + i * rowSz02;
                    uint tnIdx = ReadTableIndex(buf, rOff + 4, strIdxSz);
                    uint tnsIdx = ReadTableIndex(buf, rOff + 4 + strIdxSz, strIdxSz);
                    uint mList = ReadTableIndex(buf, rOff + mListOffsetInRow, methodIdxSz);

                    string tn = ReadStringsHeapEntry(buf, strHeapOff, strHeapLen, tnIdx);
                    tdNames[i] = (tn.Length > 0) ? tn : ("TypeDef_" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    tdNamespaces[i] = ReadStringsHeapEntry(buf, strHeapOff, strHeapLen, tnsIdx);
                    tdMethodStart[i] = mList;
                }
                cur += (int)bytes02;
            }

            // Table 0x03: FieldPtr (optional in uncompressed #- streams)
            if (rows[0x03] > 0u)
            {
                ulong bytes03 = (ulong)rows[0x03] * (ulong)fieldIdxSz;
                if ((ulong)cur + bytes03 > (ulong)tildeEnd) return;
                cur += (int)bytes03;
            }

            // Table 0x04: Field [Flags:2, Name:str, Signature:blob]
            if (rows[0x04] > 0u)
            {
                int rowSz04 = 2 + strIdxSz + blobIdxSz;
                ulong bytes04 = (ulong)rows[0x04] * (ulong)rowSz04;
                if ((ulong)cur + bytes04 > (ulong)tildeEnd) return;

                clr.FieldDefCount = (int)rows[0x04];
                int nFld = (int)Math.Min(rows[0x04], 8192u);
                for (int i = 0; i < nFld; i++)
                {
                    int rOff = cur + i * rowSz04;
                    uint fnIdx = ReadTableIndex(buf, rOff + 2, strIdxSz);
                    string fn = ReadStringsHeapEntry(buf, strHeapOff, strHeapLen, fnIdx);
                    if (fn.Length > 0)
                    {
                        uint fTok = 0x04000000u | (uint)(i + 1);
                        clr.FieldTokenToName[fTok] = fn;
                    }
                }
                cur += (int)bytes04;
            }

            // Table 0x05: MethodPtr (optional in uncompressed #- streams)
            if (rows[0x05] > 0u)
            {
                ulong bytes05 = (ulong)rows[0x05] * (ulong)methodIdxSz;
                if ((ulong)cur + bytes05 > (ulong)tildeEnd) return;
                cur += (int)bytes05;
            }

            // Table 0x06: MethodDef [RVA:4, ImplFlags:2, Flags:2, Name:str, Signature:blob, ParamList:param]
            if (rows[0x06] > 0u)
            {
                int rowSz06 = 8 + strIdxSz + blobIdxSz + paramIdxSz;
                ulong bytes06 = (ulong)rows[0x06] * (ulong)rowSz06;
                if ((ulong)cur + bytes06 > (ulong)tildeEnd) return;

                clr.MethodDefCount = (int)rows[0x06];
                int nMd = (int)Math.Min(rows[0x06], 8192u);
                int tdCursor = 0;

                for (int i = 0; i < nMd; i++)
                {
                    uint mRow1Based = (uint)(i + 1);
                    while (tdCursor + 1 < nTd && tdMethodStart[tdCursor + 1] > 0u && tdMethodStart[tdCursor + 1] <= mRow1Based)
                    {
                        tdCursor++;
                    }

                    int rOff = cur + i * rowSz06;
                    uint mRva = OmegaBraidReCore.ReadU32Le(buf, rOff);
                    ushort impFlags = OmegaBraidReCore.ReadU16Le(buf, rOff + 4);
                    ushort mFlags = OmegaBraidReCore.ReadU16Le(buf, rOff + 6);
                    uint mnIdx = ReadTableIndex(buf, rOff + 8, strIdxSz);
                    string mn = ReadStringsHeapEntry(buf, strHeapOff, strHeapLen, mnIdx);
                    if (mn.Length == 0) mn = "Method_" + mRow1Based.ToString(CultureInfo.InvariantCulture);

                    string ownerType = (nTd > 0) ? tdNames[tdCursor] : "Module";
                    string ownerNs = (nTd > 0) ? tdNamespaces[tdCursor] : "";
                    bool isCompGen = ownerType.StartsWith("<", StringComparison.Ordinal) || mn.StartsWith("<", StringComparison.Ordinal);
                    string cleanOwner = (ownerType == "<Module>") ? "GlobalModule" : ownerType;

                    uint tok = 0x06000000u | mRow1Based;
                    ClrMethodDefEntry md = new ClrMethodDefEntry();
                    md.Token = tok;
                    md.Rva = mRva;
                    md.ImplFlags = impFlags;
                    md.Flags = mFlags;
                    md.TypeNamespace = ownerNs;
                    md.TypeName = cleanOwner;
                    md.MethodName = mn;
                    md.FullName = cleanOwner + "::" + mn;
                    md.IsEntryPoint = (tok == clr.EntryPointToken);
                    md.IsCompilerGenerated = isCompGen;

                    clr.Methods.Add(md);
                    clr.MethodTokenToName[tok] = md.FullName;
                    if (md.IsEntryPoint)
                    {
                        clr.EntryPointFullName = md.FullName;
                    }
                }
                clr.TildeTablesDecoded = true;
            }
        }

        public static bool TryReadUsStringByToken(byte[] buf, ClrAssemblyReport clr, uint usToken, out string literal)
        {
            literal = "";
            if (buf == null || clr == null || clr.UsHeapAbsOffset <= 0 || (usToken >> 24) != 0x70u) return false;
            uint rel = usToken & 0x00FFFFFFu;
            if (rel == 0u || rel >= (uint)clr.UsHeapLength) return false;

            int p = clr.UsHeapAbsOffset + (int)rel;
            int end = Math.Min(buf.Length, clr.UsHeapAbsOffset + clr.UsHeapLength);
            if (p >= end) return false;

            byte b0 = buf[p++];
            int blobLen = 0;
            if ((b0 & 0x80) == 0) blobLen = b0;
            else if ((b0 & 0xC0) == 0x80 && p < end) blobLen = ((b0 & 0x3F) << 8) | buf[p++];
            else if ((b0 & 0xE0) == 0xC0 && p + 2 < end)
            {
                blobLen = ((b0 & 0x1F) << 24) | (buf[p] << 16) | (buf[p + 1] << 8) | buf[p + 2];
                p += 3;
            }
            else return false;

            if (blobLen <= 1 || p + blobLen > end) return false;
            int charBytes = blobLen - 1;
            StringBuilder sb = new StringBuilder(Math.Min(64, charBytes / 2));
            for (int k = 0; k + 1 < charBytes && sb.Length < 64; k += 2)
            {
                byte lo = buf[p + k];
                byte hi = buf[p + k + 1];
                if (hi == 0 && lo >= 0x20 && lo <= 0x7E) sb.Append((char)lo);
            }
            if (sb.Length == 0) return false;
            literal = sb.ToString();
            return true;
        }

        private static void HarvestClrIdentifierHeap(byte[] buf, int start, int len, List<string> outList, int maxItems)
        {
            int end = Math.Min(buf.Length, start + len);
            int p = start + 1;
            while (p < end && outList.Count < maxItems)
            {
                int s0 = p;
                while (p < end && buf[p] != 0) p++;
                int slen = p - s0;
                if (slen >= 4 && slen <= 64)
                {
                    bool printable = true;
                    for (int k = s0; k < p; k++)
                    {
                        if (buf[k] < 0x21 || buf[k] > 0x7E) { printable = false; break; }
                    }
                    if (printable)
                    {
                        string id = Encoding.ASCII.GetString(buf, s0, slen);
                        if (!id.StartsWith("<", StringComparison.Ordinal))
                        {
                            outList.Add(id);
                        }
                    }
                }
                p++;
            }
        }

        private static void HarvestClrUserStringsHeap(byte[] buf, int start, int len, List<string> outList, int maxItems)
        {
            int end = Math.Min(buf.Length, start + len);
            int p = start + 1;
            while (p < end && outList.Count < maxItems)
            {
                byte b0 = buf[p++];
                int blobLen = 0;
                if ((b0 & 0x80) == 0) blobLen = b0;
                else if ((b0 & 0xC0) == 0x80 && p < end) blobLen = ((b0 & 0x3F) << 8) | buf[p++];
                else if ((b0 & 0xE0) == 0xC0 && p + 2 < end)
                {
                    blobLen = ((b0 & 0x1F) << 24) | (buf[p] << 16) | (buf[p + 1] << 8) | buf[p + 2];
                    p += 3;
                }
                else break;

                if (blobLen <= 0 || p + blobLen > end) break;
                if (blobLen >= 7)
                {
                    int charBytes = blobLen - 1;
                    StringBuilder sb = new StringBuilder(Math.Min(64, charBytes / 2));
                    for (int k = 0; k + 1 < charBytes && sb.Length < 72; k += 2)
                    {
                        byte lo = buf[p + k];
                        byte hi = buf[p + k + 1];
                        if (hi == 0 && lo >= 0x20 && lo <= 0x7E) sb.Append((char)lo);
                    }
                    if (sb.Length >= 4) outList.Add(sb.ToString());
                }
                p += blobLen;
            }
        }

        public static bool TryDecodeCilMethodHeader(byte[] buf, int bodyOff, out int codeStart, out int codeSize)
        {
            codeStart = -1;
            codeSize = 0;
            if (buf == null || bodyOff < 0 || bodyOff + 2 > buf.Length) return false;

            byte hdr = buf[bodyOff];
            if ((hdr & 0x03) == 0x02) // Tiny IL Header
            {
                int sz = hdr >> 2;
                if (sz > 0 && bodyOff + 1 + sz <= buf.Length)
                {
                    codeStart = bodyOff + 1;
                    codeSize = sz;
                    return true;
                }
                return false;
            }
            if ((hdr & 0x03) == 0x03 && bodyOff + 12 <= buf.Length) // Fat IL Header
            {
                ushort flagsAndSize = OmegaBraidReCore.ReadU16Le(buf, bodyOff);
                int hdrBytes = ((flagsAndSize >> 12) & 0x0F) * 4;
                uint fatCodeSz = OmegaBraidReCore.ReadU32Le(buf, bodyOff + 4);
                if (hdrBytes >= 12 && fatCodeSz > 0u && fatCodeSz <= 65536u &&
                    (ulong)bodyOff + (ulong)hdrBytes + (ulong)fatCodeSz <= (ulong)buf.Length)
                {
                    codeStart = bodyOff + hdrBytes;
                    codeSize = (int)fatCodeSz;
                    return true;
                }
            }
            return false;
        }

        /// Lifts ECMA-335 CIL / MSIL method bodies using exact `#~` MethodDef RVAs & Type::Method names (with fallback scan if stripped).
        public static List<LiftedBraidFunction> DiscoverAndLiftClrMethods(byte[] buf, BinaryContainerReport rep, int maxMethods)
        {
            List<LiftedBraidFunction> list = new List<LiftedBraidFunction>();
            if (buf == null || rep == null || !rep.ClrInfo.IsClrAssembly) return list;

            ClrAssemblyReport clr = rep.ClrInfo;

            // Primary Path: Exact ECMA-335 #~ MethodDef RVA + TypeDef::MethodDef resolution!
            if (clr.TildeTablesDecoded && clr.Methods.Count > 0)
            {
                // Pass 0: Lift EntryPoint method first so it appears right at the top of the inspector!
                for (int pass = 0; pass < 3 && list.Count < maxMethods; pass++)
                {
                    for (int i = 0; i < clr.Methods.Count && list.Count < maxMethods; i++)
                    {
                        ClrMethodDefEntry md = clr.Methods[i];
                        if (md.Rva == 0u || (md.ImplFlags & 0x03) != 0) continue; // skip abstract / P/Invoke / runtime methods

                        bool matchPass = (pass == 0 && md.IsEntryPoint)
                            || (pass == 1 && !md.IsEntryPoint && !md.IsCompilerGenerated && md.MethodName != ".ctor" && md.MethodName != ".cctor")
                            || (pass == 2 && !md.IsEntryPoint && (md.MethodName == ".ctor" || md.IsCompilerGenerated));
                        if (!matchPass) continue;

                        int bodyOff;
                        int codeStart, codeSize;
                        if (TryRvaToFileOffset(buf, rep.Sections, md.Rva, out bodyOff) &&
                            TryDecodeCilMethodHeader(buf, bodyOff, out codeStart, out codeSize))
                        {
                            string dispName = md.IsEntryPoint ? (md.FullName + " [EntryPoint]") : md.FullName;
                            LiftedBraidFunction fn = LiftSingleCilBodyWithClr(dispName, rep.ImageBase + md.Rva, buf, codeStart, codeSize, clr);
                            if (fn.Instructions.Count >= 1)
                            {
                                list.Add(fn);
                            }
                        }
                    }
                }
                if (list.Count > 0) return list;
            }

            // Fallback Path (only if #~ stream was stripped/corrupted): scan .text for Tiny/Fat CIL bodies
            int textOff = (int)Math.Min((ulong)buf.Length, rep.TextRawOffset);
            int textEnd = (int)Math.Min((ulong)buf.Length, rep.TextRawOffset + rep.TextSize);
            uint mdStartRva = clr.MetaDataRva;
            uint mdEndRva = clr.MetaDataRva + clr.MetaDataSize;

            int pos = textOff + 8;
            int methodIdx = 0;
            while (pos + 4 < textEnd && list.Count < maxMethods)
            {
                uint curRva = (uint)rep.TextRva + (uint)Math.Max(0, pos - textOff);
                if (curRva >= rep.ClrDirRva && curRva < rep.ClrDirRva + rep.ClrDirSize)
                {
                    pos = textOff + (int)(rep.ClrDirRva + rep.ClrDirSize - (uint)rep.TextRva);
                    continue;
                }
                if (curRva >= mdStartRva && curRva < mdEndRva)
                {
                    pos = textOff + (int)(mdEndRva - (uint)rep.TextRva);
                    continue;
                }

                int codeStart, codeSize;
                if (TryDecodeCilMethodHeader(buf, pos, out codeStart, out codeSize) &&
                    codeSize >= 4 && codeSize <= 512 && codeStart + codeSize <= textEnd &&
                    buf[codeStart + codeSize - 1] == 0x2A)
                {
                    string mName = string.Format(CultureInfo.InvariantCulture, "CIL::Method_0x{0:X}", curRva);
                    LiftedBraidFunction fn = LiftSingleCilBodyWithClr(mName, rep.ImageBase + curRva, buf, codeStart, codeSize, clr);
                    if (fn.Instructions.Count >= 2)
                    {
                        list.Add(fn);
                        methodIdx++;
                        pos = codeStart + codeSize;
                        continue;
                    }
                }
                pos++;
            }
            return list;
        }

        public static LiftedBraidFunction LiftSingleCilBody(string name, ulong baseRva, byte[] buf, int start, int length)
        {
            return LiftSingleCilBodyWithClr(name, baseRva, buf, start, length, null);
        }

        public static LiftedBraidFunction LiftSingleCilBodyWithClr(string name, ulong baseRva, byte[] buf, int start, int length, ClrAssemblyReport clr)
        {
            LiftedBraidFunction fn = new LiftedBraidFunction();
            fn.Name = name;
            fn.Isa = BraidArchIsa.DotNetCil;
            fn.BaseRva = baseRva;
            fn.ByteLength = length;

            int end = Math.Min(buf.Length, start + length);
            int p = start;
            int ssaVer = 1;
            int loads = 0, stores = 0, calls = 0, vcalls = 0, alus = 0;

            while (p < end && fn.Instructions.Count < 192)
            {
                BraidSsaInstruction ins = new BraidSsaInstruction();
                ins.Offset = p - start;
                ins.TargetOffset = -1;
                byte op = buf[p++];

                if (op == 0x00) { ins.Op = BraidMicroOp.Nop; ins.Length = 1; ins.MnemonicText = "il.nop"; }
                else if (op >= 0x02 && op <= 0x05)
                {
                    int argIdx = op - 0x02;
                    ins.Op = BraidMicroOp.MovReg; ins.DstReg = argIdx; ins.SsaDstVer = ssaVer++;
                    ins.Length = 1; ins.MnemonicText = "il.ldarg." + argIdx.ToString(CultureInfo.InvariantCulture);
                }
                else if (op == 0x0E && p < end)
                {
                    int argIdx = buf[p++];
                    ins.Op = BraidMicroOp.MovReg; ins.DstReg = argIdx & 31; ins.SsaDstVer = ssaVer++;
                    ins.Length = 2; ins.MnemonicText = "il.ldarg.s " + argIdx.ToString(CultureInfo.InvariantCulture);
                }
                else if (op >= 0x06 && op <= 0x09)
                {
                    int locIdx = op - 0x06;
                    ins.Op = BraidMicroOp.LoadMem; ins.DstReg = 8 + locIdx; ins.SsaDstVer = ssaVer++;
                    ins.Length = 1; ins.MnemonicText = "il.ldloc." + locIdx.ToString(CultureInfo.InvariantCulture); loads++;
                }
                else if ((op == 0x11 || op == 0x12) && p < end)
                {
                    int locIdx = buf[p++];
                    ins.Op = BraidMicroOp.LoadMem; ins.DstReg = 8 + (locIdx & 15); ins.SsaDstVer = ssaVer++;
                    ins.Length = 2; ins.MnemonicText = ((op == 0x11) ? "il.ldloc.s V_" : "il.ldloca.s V_") + locIdx.ToString(CultureInfo.InvariantCulture); loads++;
                }
                else if (op >= 0x0A && op <= 0x0D)
                {
                    int locIdx = op - 0x0A;
                    ins.Op = BraidMicroOp.StoreMem; ins.SrcA = 8 + locIdx;
                    ins.Length = 1; ins.MnemonicText = "il.stloc." + locIdx.ToString(CultureInfo.InvariantCulture); stores++;
                }
                else if (op == 0x13 && p < end)
                {
                    int locIdx = buf[p++];
                    ins.Op = BraidMicroOp.StoreMem; ins.SrcA = 8 + (locIdx & 15);
                    ins.Length = 2; ins.MnemonicText = "il.stloc.s V_" + locIdx.ToString(CultureInfo.InvariantCulture); stores++;
                }
                else if (op >= 0x15 && op <= 0x1E)
                {
                    int cval = op - 0x16;
                    ins.Op = BraidMicroOp.MovImm; ins.DispOrImm = cval; ins.SsaDstVer = ssaVer++;
                    ins.Length = 1; ins.MnemonicText = "il.ldc.i4." + cval.ToString(CultureInfo.InvariantCulture);
                }
                else if (op == 0x1F && p < end)
                {
                    int imm = (sbyte)buf[p++];
                    ins.Op = BraidMicroOp.MovImm; ins.DispOrImm = imm; ins.SsaDstVer = ssaVer++;
                    ins.Length = 2; ins.MnemonicText = "il.ldc.i4.s " + imm.ToString(CultureInfo.InvariantCulture);
                }
                else if (op == 0x20 && p + 4 <= end)
                {
                    int imm = (int)OmegaBraidReCore.ReadU32Le(buf, p); p += 4;
                    ins.Op = BraidMicroOp.MovImm; ins.DispOrImm = imm; ins.SsaDstVer = ssaVer++;
                    ins.Length = 5; ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.ldc.i4 0x{0:X}", imm);
                }
                else if (op == 0x28 && p + 4 <= end) // call token
                {
                    uint tok = OmegaBraidReCore.ReadU32Le(buf, p); p += 4;
                    ins.Op = BraidMicroOp.CallDirect; ins.DispOrImm = tok; ins.SsaDstVer = ssaVer++;
                    ins.Length = 5; ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.call token(0x{0:X8})", tok); calls++;
                    string mSym;
                    if (clr != null && clr.MethodTokenToName.TryGetValue(tok, out mSym))
                    {
                        ins.ResolvedAnnotation = "MethodDef -> " + mSym;
                        if (!fn.ImportedCalls.Contains(mSym)) fn.ImportedCalls.Add(mSym);
                    }
                }
                else if ((op == 0x6F || op == 0x73) && p + 4 <= end) // callvirt / newobj
                {
                    uint tok = OmegaBraidReCore.ReadU32Le(buf, p); p += 4;
                    ins.Op = BraidMicroOp.CallVTable; ins.DispOrImm = tok; ins.SsaDstVer = ssaVer++;
                    ins.Length = 5; ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.{0} token(0x{1:X8})", (op == 0x6F) ? "callvirt" : "newobj", tok); vcalls++;
                    string mSym;
                    if (clr != null && clr.MethodTokenToName.TryGetValue(tok, out mSym))
                    {
                        ins.ResolvedAnnotation = "MethodDef -> " + mSym;
                        if (!fn.ImportedCalls.Contains(mSym)) fn.ImportedCalls.Add(mSym);
                    }
                }
                else if ((op == 0x7B || op == 0x7D || op == 0x7E || op == 0x80) && p + 4 <= end) // ldfld / stfld / ldsfld / stsfld
                {
                    uint tok = OmegaBraidReCore.ReadU32Le(buf, p); p += 4;
                    uint fldSlot = (tok & 0xFFFFu) * 8u;
                    bool isLoad = (op == 0x7B || op == 0x7E);
                    ins.Op = isLoad ? BraidMicroOp.LoadMem : BraidMicroOp.StoreMem;
                    ins.DispOrImm = fldSlot; ins.MemWidth = 8;
                    if (isLoad) { ins.SsaDstVer = ssaVer++; loads++; } else stores++;
                    ins.Length = 5; ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.{0} fld(0x{1:X8})", isLoad ? "ldfld" : "stfld", tok);

                    string fSym;
                    if (clr != null && clr.FieldTokenToName.TryGetValue(tok, out fSym))
                    {
                        ins.ResolvedAnnotation = "Field -> " + fSym;
                    }

                    if (fn.Fields.Count < 32 && fldSlot <= 0x400u)
                    {
                        SheafFieldSlot sf = new SheafFieldSlot();
                        sf.Offset = fldSlot; sf.Width = 8; sf.FieldType = BraidFieldType.U64; sf.AccessMask = isLoad ? 1 : 2;
                        bool exists = false;
                        for (int k = 0; k < fn.Fields.Count; k++) { if (fn.Fields[k].Offset == fldSlot) { exists = true; break; } }
                        if (!exists) fn.Fields.Add(sf);
                    }
                }
                else if (op == 0x72 && p + 4 <= end) // ldstr #US token
                {
                    uint tok = OmegaBraidReCore.ReadU32Le(buf, p); p += 4;
                    ins.Op = BraidMicroOp.LeaAddr; ins.DispOrImm = tok; ins.SsaDstVer = ssaVer++;
                    ins.Length = 5; ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.ldstr #US(0x{0:X8})", tok);
                    string usLit;
                    if (clr != null && TryReadUsStringByToken(buf, clr, tok, out usLit))
                    {
                        string q = "\"" + usLit + "\"";
                        ins.ResolvedAnnotation = "#US -> " + q;
                        if (!fn.ReferencedStrings.Contains(q)) fn.ReferencedStrings.Add(q);
                    }
                }
                else if ((op >= 0x2B && op <= 0x37) && p + 1 <= end) // short branches (.s)
                {
                    int rel = (sbyte)buf[p++];
                    ins.Op = (op == 0x2B) ? BraidMicroOp.Jmp : BraidMicroOp.BrCond;
                    ins.UsesFlags = (op != 0x2B);
                    ins.Length = 2;
                    ins.TargetOffset = (p - start) + rel;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.{0} 0x{1:X2}", (op == 0x2B) ? "br.s" : "brcond.s", ins.TargetOffset);
                }
                else if ((op >= 0x38 && op <= 0x44) && p + 4 <= end) // 4-byte branches
                {
                    int rel = (int)OmegaBraidReCore.ReadU32Le(buf, p); p += 4;
                    ins.Op = (op == 0x38) ? BraidMicroOp.Jmp : BraidMicroOp.BrCond;
                    ins.UsesFlags = (op != 0x38);
                    ins.Length = 5;
                    ins.TargetOffset = (p - start) + rel;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.{0} 0x{1:X}", (op == 0x38) ? "br" : "brcond", ins.TargetOffset);
                }
                else if (op == 0x58 || op == 0x59 || op == 0x5A) // add / sub / mul
                {
                    ins.Op = (op == 0x58) ? BraidMicroOp.Add : BraidMicroOp.Sub;
                    ins.SsaDstVer = ssaVer++; ins.Length = 1;
                    ins.MnemonicText = (op == 0x58) ? "il.add" : ((op == 0x59) ? "il.sub" : "il.mul"); alus++;
                }
                else if (op == 0x2A)
                {
                    ins.Op = BraidMicroOp.Ret; ins.Length = 1; ins.MnemonicText = "il.ret";
                }
                else if (op == 0xFE && p < end)
                {
                    byte op2 = buf[p++];
                    ins.Op = BraidMicroOp.CmpFlags; ins.SetsFlags = true; ins.Length = 2;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.fe_{0:X2}", op2);
                }
                else
                {
                    ins.Op = BraidMicroOp.Nop; ins.Length = 1;
                    ins.MnemonicText = string.Format(CultureInfo.InvariantCulture, "il.op_0x{0:X2}", op);
                }
                fn.Instructions.Add(ins);
            }

            BraidBasicBlockNode bb = new BraidBasicBlockNode();
            bb.Id = 0; bb.StartOffset = 0; bb.EndOffset = length; bb.InsnStart = 0; bb.InsnCount = fn.Instructions.Count;
            fn.Blocks.Add(bb);

            fn.RawFeatures16[0] = -2.0f;
            fn.RawFeatures16[4] = (calls > 0) ? calls * 4.0f : -2.0f;
            fn.RawFeatures16[5] = (vcalls > 0) ? vcalls * 6.0f : -4.0f;
            fn.RawFeatures16[8] = (loads - stores) * 2.5f;
            fn.RawFeatures16[10] = (fn.Fields.Count >= 2) ? fn.Fields.Count * 3.0f : -4.0f;
            fn.RawFeatures16[11] = alus * 2.0f;
            OmegaBraidReCore.SnapToE8MinimalRoot2x(fn.RawFeatures16, 0, fn.E8TopoRoot2x);
            OmegaBraidReCore.SnapToE8MinimalRoot2x(fn.RawFeatures16, 8, fn.E8DataRoot2x);
            return fn;
        }
    }
}
