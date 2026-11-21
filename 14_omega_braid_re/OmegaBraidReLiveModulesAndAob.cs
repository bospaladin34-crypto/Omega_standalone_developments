using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaBraidRE
{
    public sealed class LiveModuleItem
    {
        public int Pid = 0;
        public string ModuleName = "";
        public ulong BaseAddress = 0UL;
        public uint ModuleSize = 0u;
        public string FullPath = "";
        public bool IsMainExe = false;
        public bool IsSystemDll = false;

        public override string ToString()
        {
            string tag = IsMainExe ? "[EXE]" : (IsSystemDll ? "[SYS]" : "[DLL]");
            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1,-30} | Base=0x{2:X12} | {3,6} KB",
                tag, ModuleName, BaseAddress, ModuleSize / 1024u);
        }
    }

    public struct CompiledAobByte
    {
        public byte Value;
        public byte Mask; // 0xFF = exact byte, 0x00 = full wildcard, 0xF0 / 0x0F = nibble wildcard
    }

    public sealed class CompiledAobPattern
    {
        public bool Valid = false;
        public string NormalizedPattern = "";
        public string ErrorMessage = "";
        public CompiledAobByte[] Tokens = new CompiledAobByte[0];
        public int AnchorIndex = -1;
        public byte AnchorValue = 0;
    }

    public sealed class AobMatchHit
    {
        public int FileOffset = 0;
        public uint Rva = 0u;
        public ulong VirtualAddress = 0UL;
        public string SectionName = ".raw";
        public string OwningFunction = "";
        public string MatchedBytesHex = "";
        public bool HasRipTarget = false;
        public ulong RipTargetRva = 0UL;
    }

    public sealed class AobScanReport
    {
        public string Pattern = "";
        public int ScannedBytes = 0;
        public double ElapsedMs = 0.0;
        public List<AobMatchHit> Hits = new List<AobMatchHit>();
    }

    public static class OmegaBraidReLiveModulesAndAob
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
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static ushort RdU16(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        private static uint RdU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        private static int RdI32(byte[] b, int o)
        {
            return (int)RdU32(b, o);
        }

        // =========================================================================
        // 1. LOADED-DLL MODULE ENUMERATION & READ-ONLY MODULE MEMORY SNAPSHOT
        // =========================================================================

        public static List<LiveModuleItem> ListProcessModules(int pid)
        {
            List<LiveModuleItem> modules = new List<LiveModuleItem>();
            HashSet<ulong> seenBases = new HashSet<ulong>();

            IntPtr hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
            if (hSnap != INVALID_HANDLE_VALUE && hSnap != IntPtr.Zero)
            {
                try
                {
                    MODULEENTRY32W me = new MODULEENTRY32W();
                    me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                    bool ok = Module32FirstW(hSnap, ref me);
                    int idx = 0;
                    while (ok && idx < 1024)
                    {
                        ulong baseVa = (ulong)me.modBaseAddr.ToInt64();
                        if (baseVa != 0UL && !seenBases.Contains(baseVa))
                        {
                            seenBases.Add(baseVa);
                            LiveModuleItem item = new LiveModuleItem();
                            item.Pid = pid;
                            item.ModuleName = me.szModule ?? ("mod_" + idx);
                            item.BaseAddress = baseVa;
                            item.ModuleSize = me.modBaseSize;
                            item.FullPath = me.szExePath ?? "";
                            item.IsMainExe = (idx == 0) || item.ModuleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                            item.IsSystemDll = IsWindowsSystemModulePath(item.FullPath);
                            modules.Add(item);
                        }
                        idx++;
                        me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                        ok = Module32NextW(hSnap, ref me);
                    }
                }
                catch { }
                finally
                {
                    CloseHandle(hSnap);
                }
            }

            if (modules.Count == 0)
            {
                try
                {
                    using (Process proc = Process.GetProcessById(pid))
                    {
                        ProcessModuleCollection pmc = proc.Modules;
                        for (int i = 0; i < pmc.Count; i++)
                        {
                            ProcessModule pm = pmc[i];
                            ulong baseVa = (ulong)pm.BaseAddress.ToInt64();
                            if (baseVa == 0UL || seenBases.Contains(baseVa)) continue;
                            seenBases.Add(baseVa);
                            LiveModuleItem item = new LiveModuleItem();
                            item.Pid = pid;
                            item.ModuleName = pm.ModuleName ?? ("mod_" + i);
                            item.BaseAddress = baseVa;
                            item.ModuleSize = (uint)Math.Max(0, pm.ModuleMemorySize);
                            item.FullPath = pm.FileName ?? "";
                            item.IsMainExe = (i == 0) || item.ModuleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                            item.IsSystemDll = IsWindowsSystemModulePath(item.FullPath);
                            modules.Add(item);
                        }
                    }
                }
                catch { }
            }

            // Sort order: [EXE] first -> Non-system plugin/user [DLL]s -> [SYS] Windows DLLs alphabetical
            modules.Sort(delegate(LiveModuleItem a, LiveModuleItem b)
            {
                if (a.IsMainExe != b.IsMainExe) return a.IsMainExe ? -1 : 1;
                if (a.IsSystemDll != b.IsSystemDll) return a.IsSystemDll ? 1 : -1;
                return string.Compare(a.ModuleName, b.ModuleName, StringComparison.OrdinalIgnoreCase);
            });
            return modules;
        }

        private static bool IsWindowsSystemModulePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('/', '\\').ToLowerInvariant();
            return p.Contains("\\windows\\system32\\") ||
                   p.Contains("\\windows\\syswow64\\") ||
                   p.Contains("\\windows\\winsxs\\") ||
                   p.Contains("\\microsoft.net\\framework");
        }

        public static LiveMemorySnapshot ReadLiveModulePeImage(int pid, ulong moduleBase, uint moduleSize, string moduleName, int maxTotalBytes)
        {
            LiveMemorySnapshot snapOut = new LiveMemorySnapshot();
            snapOut.Pid = pid;
            snapOut.ProcessName = string.IsNullOrEmpty(moduleName) ? ("PID_" + pid) : moduleName;
            snapOut.BaseAddress = moduleBase;
            snapOut.ImageSize = moduleSize;

            if (moduleBase == 0UL)
            {
                snapOut.ErrorMessage = "Module base address is zero.";
                return snapOut;
            }

            int capBytes = Math.Max(65536, Math.Min(maxTotalBytes, 12 * 1024 * 1024));
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
                IntPtr modPtr = new IntPtr( unchecked((long)moduleBase) );
                byte[] hdrPage = new byte[4096];
                IntPtr got;
                if (!ReadProcessMemory(hProc, modPtr, hdrPage, new IntPtr(hdrPage.Length), out got) || got.ToInt64() < 256)
                {
                    snapOut.ErrorMessage = "ReadProcessMemory failed on module PE header at 0x" + moduleBase.ToString("X", CultureInfo.InvariantCulture);
                    return snapOut;
                }

                if (hdrPage[0] != (byte)'M' || hdrPage[1] != (byte)'Z')
                {
                    snapOut.ErrorMessage = "Module at 0x" + moduleBase.ToString("X", CultureInfo.InvariantCulture) + " does not begin with MZ.";
                    return snapOut;
                }

                uint peOff = RdU32(hdrPage, 0x3C);
                if (peOff + 0xF8u >= (uint)hdrPage.Length || hdrPage[peOff] != (byte)'P' || hdrPage[peOff + 1] != (byte)'E')
                {
                    snapOut.ErrorMessage = "Invalid PE signature in module " + snapOut.ProcessName;
                    return snapOut;
                }

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
                    else if (endOff > capBytes && maxNeeded < capBytes) maxNeeded = capBytes;
                }

                byte[] img = new byte[maxNeeded];
                int hdrCopy = (int)Math.Min((uint)hdrPage.Length, Math.Min(firstSectionRaw, (uint)img.Length));
                Buffer.BlockCopy(hdrPage, 0, img, 0, hdrCopy);
                int totalRead = hdrCopy;

                // Patch live ImageBase into optional header so displayed VAs reflect actual ASLR base in memory!
                ushort optMagic = RdU16(hdrPage, (int)peOff + 24);
                int imgBaseField = (optMagic == 0x020B) ? ((int)peOff + 24 + 24) : ((int)peOff + 24 + 28);
                if (optMagic == 0x020B && imgBaseField + 8 <= hdrCopy)
                {
                    for (int b = 0; b < 8; b++) img[imgBaseField + b] = (byte)((moduleBase >> (b * 8)) & 0xFFUL);
                }

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
                    IntPtr remoteAddr = new IntPtr( unchecked((long)(moduleBase + virtAddr)) );
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
                            IntPtr pgAddr = new IntPtr( unchecked((long)(moduleBase + virtAddr + pg)) );
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
        // 2. WILDCARD & NIBBLE-MASKED AOB SIGNATURE COMPILER, SCANNER & SYNTHESIZER
        // =========================================================================

        private static int ParseHexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
            if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
            return -1;
        }

        public static CompiledAobPattern CompileAobPattern(string rawPattern)
        {
            CompiledAobPattern res = new CompiledAobPattern();
            if (string.IsNullOrEmpty(rawPattern))
            {
                res.ErrorMessage = "Empty AOB pattern.";
                return res;
            }

            string cleaned = rawPattern.Replace(",", " ").Replace("0x", "").Replace("0X", "").Replace("\\x", " ").Trim();
            string[] parts = cleaned.Split(new char[] { ' ', '\t', '\r', '\n', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                res.ErrorMessage = "No byte tokens found in AOB pattern.";
                return res;
            }

            List<CompiledAobByte> toks = new List<CompiledAobByte>(parts.Length);
            StringBuilder norm = new StringBuilder(parts.Length * 3);

            for (int i = 0; i < parts.Length; i++)
            {
                string tk = parts[i].Trim();
                if (tk == "?" || tk == "??" || tk == "*" || tk == "**")
                {
                    CompiledAobByte cb = new CompiledAobByte();
                    cb.Value = 0;
                    cb.Mask = 0x00;
                    toks.Add(cb);
                    if (norm.Length > 0) norm.Append(' ');
                    norm.Append("??");
                    continue;
                }

                if (tk.Length == 2)
                {
                    bool hiWild = (tk[0] == '?' || tk[0] == '*');
                    bool loWild = (tk[1] == '?' || tk[1] == '*');
                    int hi = hiWild ? 0 : ParseHexNibble(tk[0]);
                    int lo = loWild ? 0 : ParseHexNibble(tk[1]);
                    if (hi < 0 || lo < 0)
                    {
                        res.ErrorMessage = "Invalid hex byte token '" + tk + "' at index " + i + ".";
                        return res;
                    }
                    CompiledAobByte cb = new CompiledAobByte();
                    cb.Value = (byte)((hi << 4) | lo);
                    cb.Mask = (byte)((hiWild ? 0x00 : 0xF0) | (loWild ? 0x00 : 0x0F));
                    toks.Add(cb);
                    if (norm.Length > 0) norm.Append(' ');
                    norm.Append(hiWild ? '?' : char.ToUpperInvariant(tk[0]));
                    norm.Append(loWild ? '?' : char.ToUpperInvariant(tk[1]));
                    continue;
                }

                // Support contiguous hex pairs without spaces (e.g. "48895C24")
                if ((tk.Length % 2) == 0 && tk.Length > 2)
                {
                    for (int j = 0; j < tk.Length; j += 2)
                    {
                        char c0 = tk[j];
                        char c1 = tk[j + 1];
                        bool hw = (c0 == '?' || c0 == '*');
                        bool lw = (c1 == '?' || c1 == '*');
                        int h = hw ? 0 : ParseHexNibble(c0);
                        int l = lw ? 0 : ParseHexNibble(c1);
                        if (h < 0 || l < 0)
                        {
                            res.ErrorMessage = "Invalid contiguous hex pair '" + c0 + c1 + "'.";
                            return res;
                        }
                        CompiledAobByte cb = new CompiledAobByte();
                        cb.Value = (byte)((h << 4) | l);
                        cb.Mask = (byte)((hw ? 0x00 : 0xF0) | (lw ? 0x00 : 0x0F));
                        toks.Add(cb);
                        if (norm.Length > 0) norm.Append(' ');
                        norm.Append(hw ? '?' : char.ToUpperInvariant(c0));
                        norm.Append(lw ? '?' : char.ToUpperInvariant(c1));
                    }
                    continue;
                }

                res.ErrorMessage = "Malformed AOB token '" + tk + "'.";
                return res;
            }

            res.Tokens = toks.ToArray();
            res.NormalizedPattern = norm.ToString();
            for (int i = 0; i < res.Tokens.Length; i++)
            {
                if (res.Tokens[i].Mask == 0xFF)
                {
                    res.AnchorIndex = i;
                    res.AnchorValue = res.Tokens[i].Value;
                    break;
                }
            }
            res.Valid = (res.Tokens.Length > 0);
            return res;
        }

        public static AobScanReport ScanBufferForAob(
            byte[] imageBytes,
            BinaryContainerReport container,
            List<LiftedBraidFunction> liftedFunctions,
            string patternText,
            int maxHits)
        {
            Stopwatch sw = Stopwatch.StartNew();
            AobScanReport rep = new AobScanReport();
            rep.Pattern = patternText ?? "";
            if (imageBytes == null || imageBytes.Length == 0) return rep;

            CompiledAobPattern pat = CompileAobPattern(patternText);
            if (!pat.Valid || pat.Tokens.Length == 0 || pat.Tokens.Length > imageBytes.Length)
            {
                rep.Pattern = pat.Valid ? pat.NormalizedPattern : (patternText + " [ERR: " + pat.ErrorMessage + "]");
                return rep;
            }

            rep.Pattern = pat.NormalizedPattern;
            rep.ScannedBytes = imageBytes.Length;
            int plen = pat.Tokens.Length;
            int limit = imageBytes.Length - plen;
            int cap = Math.Max(1, Math.Min(maxHits, 512));
            CompiledAobByte[] toks = pat.Tokens;
            int anchorIdx = pat.AnchorIndex;
            byte anchorVal = pat.AnchorValue;

            int pos = 0;
            while (pos <= limit && rep.Hits.Count < cap)
            {
                if (anchorIdx >= 0)
                {
                    int foundAnchor = Array.IndexOf(imageBytes, anchorVal, pos + anchorIdx, limit - pos + 1);
                    if (foundAnchor < 0) break;
                    pos = foundAnchor - anchorIdx;
                }

                bool match = true;
                for (int k = 0; k < plen; k++)
                {
                    byte m = toks[k].Mask;
                    if (m != 0 && (byte)(imageBytes[pos + k] & m) != toks[k].Value)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    AobMatchHit hit = BuildHitMetadata(imageBytes, pos, plen, container, liftedFunctions);
                    rep.Hits.Add(hit);
                }
                pos++;
            }

            sw.Stop();
            rep.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return rep;
        }

        private static AobMatchHit BuildHitMetadata(
            byte[] buf,
            int fileOff,
            int patLen,
            BinaryContainerReport container,
            List<LiftedBraidFunction> funcs)
        {
            AobMatchHit h = new AobMatchHit();
            h.FileOffset = fileOff;
            h.Rva = (uint)fileOff;
            ulong imgBase = (container != null && container.ImageBase != 0UL) ? container.ImageBase : 0x140000000UL;

            if (container != null && container.Sections != null && container.Sections.Count > 0)
            {
                for (int i = 0; i < container.Sections.Count; i++)
                {
                    PeSectionDesc s = container.Sections[i];
                    if (s.RawSize > 0u && (uint)fileOff >= s.RawOffset && (uint)fileOff < s.RawOffset + s.RawSize)
                    {
                        h.SectionName = s.Name;
                        h.Rva = s.VirtualAddress + ((uint)fileOff - s.RawOffset);
                        break;
                    }
                }
            }
            h.VirtualAddress = imgBase + h.Rva;

            // Match against .pdata RUNTIME_FUNCTION or lifted functions
            if (container != null && container.PdataFunctions != null)
            {
                for (int i = 0; i < container.PdataFunctions.Count; i++)
                {
                    PdataFunctionEntry pf = container.PdataFunctions[i];
                    if (h.Rva >= pf.BeginRva && h.Rva < pf.EndRva)
                    {
                        h.OwningFunction = string.Format(CultureInfo.InvariantCulture, "pdata_0x{0:X}+0x{1:X}", pf.BeginRva, h.Rva - pf.BeginRva);
                        break;
                    }
                }
            }

            if (funcs != null)
            {
                for (int i = 0; i < funcs.Count; i++)
                {
                    LiftedBraidFunction fn = funcs[i];
                    ulong fnRva = (fn.BaseRva >= imgBase) ? (fn.BaseRva - imgBase) : fn.BaseRva;
                    uint fnLen = (uint)Math.Max(16, fn.ByteLength);
                    if (h.Rva >= fnRva && h.Rva < fnRva + fnLen)
                    {
                        h.OwningFunction = string.Format(CultureInfo.InvariantCulture, "{0}+0x{1:X}", fn.Name, h.Rva - fnRva);
                        break;
                    }
                }
            }

            int showLen = Math.Min(Math.Max(patLen, 12), Math.Min(16, buf.Length - fileOff));
            StringBuilder sb = new StringBuilder(showLen * 3);
            for (int i = 0; i < showLen; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buf[fileOff + i]);
            }
            h.MatchedBytesHex = sb.ToString();

            // Check if instruction at hit is RIP-relative (E8/E9 rel32, FF 15/25 [RIP+disp32], REX 8B/8D [RIP+disp32])
            if (fileOff + 5 <= buf.Length && (buf[fileOff] == 0xE8 || buf[fileOff] == 0xE9))
            {
                int disp = RdI32(buf, fileOff + 1);
                h.HasRipTarget = true;
                h.RipTargetRva = (ulong)((long)h.Rva + 5L + disp);
            }
            else if (fileOff + 6 <= buf.Length && buf[fileOff] == 0xFF && (buf[fileOff + 1] == 0x15 || buf[fileOff + 1] == 0x25))
            {
                int disp = RdI32(buf, fileOff + 2);
                h.HasRipTarget = true;
                h.RipTargetRva = (ulong)((long)h.Rva + 6L + disp);
            }
            else if (fileOff + 7 <= buf.Length && buf[fileOff] >= 0x40 && buf[fileOff] <= 0x4F &&
                     (buf[fileOff + 1] == 0x8D || buf[fileOff + 1] == 0x8B || buf[fileOff + 1] == 0x89) &&
                     (buf[fileOff + 2] & 0xC7) == 0x05)
            {
                int disp = RdI32(buf, fileOff + 3);
                h.HasRipTarget = true;
                h.RipTargetRva = (ulong)((long)h.Rva + 7L + disp);
            }

            return h;
        }

        public static string SynthesizeUniqueFunctionAob(byte[] imageBytes, BinaryContainerReport container, byte[] funcBytes, out int matchCountInImage)
        {
            matchCountInImage = 0;
            if (funcBytes == null || funcBytes.Length == 0) return "";

            int maxTake = Math.Min(32, funcBytes.Length);
            bool[] wildcardMask = new bool[maxTake];

            // Walk x64 instructions and mask out rel32 / RIP-disp32 bytes so AOB survives ASLR & patch rebasing!
            int off = 0;
            while (off < maxTake)
            {
                bool isRip;
                string lbl;
                int len = OmegaBraidReSymbolsAndHooks.DecodeX64InstructionLength(funcBytes, off, out isRip, out lbl);
                if (len <= 0) break;
                if (isRip && len >= 5)
                {
                    int dispStart = off + len - 4;
                    for (int w = dispStart; w < off + len && w < maxTake; w++)
                    {
                        if (w >= 0) wildcardMask[w] = true;
                    }
                }
                off += len;
            }

            // Find shortest prefix (8..maxTake bytes) that uniquely matches once in imageBytes
            int[] candidateLengths = new int[] { 10, 14, 18, 24, maxTake };
            string lastPattern = "";
            for (int c = 0; c < candidateLengths.Length; c++)
            {
                int take = Math.Min(maxTake, candidateLengths[c]);
                if (take < 4) take = maxTake;
                StringBuilder sb = new StringBuilder(take * 3);
                for (int i = 0; i < take; i++)
                {
                    if (i > 0) sb.Append(' ');
                    if (wildcardMask[i]) sb.Append("??");
                    else sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", funcBytes[i]);
                }
                lastPattern = sb.ToString();
                if (imageBytes != null && imageBytes.Length >= take)
                {
                    AobScanReport scan = ScanBufferForAob(imageBytes, container, null, lastPattern, 8);
                    matchCountInImage = scan.Hits.Count;
                    if (matchCountInImage == 1) return lastPattern;
                }
            }
            return lastPattern;
        }

        public static string EmitCpp23AndRustAobScannerSnippets(string pattern, string symbolLabel)
        {
            CompiledAobPattern cp = CompileAobPattern(pattern);
            string norm = cp.Valid ? cp.NormalizedPattern : pattern;
            StringBuilder sb = new StringBuilder(1024);
            sb.AppendLine("// Auto-generated by OmegaBraidRE v1.4.0 :: CommonLibSSE/F4SE C++23 & #![no_std] Rust AOB Scanner");
            sb.AppendFormat(CultureInfo.InvariantCulture, "// Target: {0} | Pattern ({1} bytes): \"{2}\"\r\n",
                symbolLabel, cp.Tokens.Length, norm);
            sb.AppendLine("// [1] C++23 CommonLibSSE-NG / F4SE Pattern Matcher:");
            sb.AppendFormat(CultureInfo.InvariantCulture, "//     constexpr auto kPat = REL::make_pattern<\"{0}\">();\r\n\r\n", norm);

            sb.AppendLine("// [2] Zero-Crate #![no_std] Rust Masked Slice Scanner:");
            StringBuilder vals = new StringBuilder();
            StringBuilder masks = new StringBuilder();
            for (int i = 0; i < cp.Tokens.Length; i++)
            {
                if (i > 0) { vals.Append(", "); masks.Append(", "); }
                vals.AppendFormat(CultureInfo.InvariantCulture, "0x{0:X2}", cp.Tokens[i].Value);
                masks.AppendFormat(CultureInfo.InvariantCulture, "0x{0:X2}", cp.Tokens[i].Mask);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, "pub const AOB_VALS: [u8; {0}] = [{1}];\r\n", cp.Tokens.Length, vals.ToString());
            sb.AppendFormat(CultureInfo.InvariantCulture, "pub const AOB_MASK: [u8; {0}] = [{1}];\r\n", cp.Tokens.Length, masks.ToString());
            sb.AppendLine("pub fn scan_aob_first(haystack: &[u8]) -> Option<usize> {");
            sb.AppendLine("    if haystack.len() < AOB_VALS.len() { return None; }");
            sb.AppendLine("    let end = haystack.len() - AOB_VALS.len();");
            sb.AppendLine("    let mut i = 0usize;");
            sb.AppendLine("    while i <= end {");
            sb.AppendLine("        let mut ok = true;");
            sb.AppendLine("        let mut k = 0usize;");
            sb.AppendLine("        while k < AOB_VALS.len() {");
            sb.AppendLine("            if (haystack[i + k] & AOB_MASK[k]) != AOB_VALS[k] { ok = false; break; }");
            sb.AppendLine("            k += 1;");
            sb.AppendLine("        }");
            sb.AppendLine("        if ok { return Some(i); }");
            sb.AppendLine("        i += 1;");
            sb.AppendLine("    }");
            sb.AppendLine("    None");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
