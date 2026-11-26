using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OmegaCommon;

namespace OmegaTriEngine
{
    public sealed class SkyrimInstallInfo
    {
        public bool Found;
        public string GameDir = "";
        public string SkyrimExePath = "";
        public string SkyrimVersionString = "";
        public uint PackedSkseRuntimeVersion = 0u;
        public bool HasSkseLoader;
        public string SkseLoaderPath = "";
        public string SkseDllName = "";
        public bool HasCreationKit;
        public string CreationKitExePath = "";
        public bool HasPapyrusCompiler;
        public string PapyrusCompilerPath = "";
        public string ScriptsZipPath = "";
    }

    public sealed class SkyrimModBundleResult
    {
        public bool Ok;
        public string VortexZipPath = "";
        public string RootReshadeZipPath = "";
        public string EspPath = "";
        public string WorldSyncPscPath = "";
        public string PlayerAliasPscPath = "";
        public string WorldSyncPexPath = "";
        public string PlayerAliasPexPath = "";
        public bool PexCompiledViaCk;
        public string SkseDllPath = "";
        public string ReshadeFxPath = "";
        public string ReshadeAddonPath = "";
        public int EspBytes;
        public int EspKeywordCount;
        public string Detail = "";
    }

    // Station 15 Task #3: Skyrim AE SKSE64 + Creation Kit + ESL .esp + Papyrus + Vortex Mod Builder
    // 100% MIT/BSD clean, strict C# 5.0, zero external NuGet/DLL dependencies (uses System.IO.Compression.DeflateStream in System.dll).
    public static class OmegaEngineSkseModBuilder
    {
        public const uint EslRecordFlag = 0x00000200u;
        public const ushort SkyrimSeFormVersion = 44;

        public static uint PackSkyrimVersion(int major, int minor, int build, int sub)
        {
            return (uint)(((major & 0xFF) << 24) | ((minor & 0xFF) << 16) | ((build & 0xFFF) << 4) | (sub & 0xF));
        }

        public static SkyrimInstallInfo DiscoverSkyrimAeInstallation(string explicitGameDir)
        {
            SkyrimInstallInfo info = new SkyrimInstallInfo();
            List<string> candidates = new List<string>();

            if (!string.IsNullOrEmpty(explicitGameDir))
            {
                candidates.Add(explicitGameDir.Trim());
            }

            // Priority scan across F:\ (user's primary Skyrim AE + Vortex + CK drive) and C:\..G:\ Steam libraries
            string[] directGuesses = new string[]
            {
                @"F:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                @"F:\Steam\steamapps\common\Skyrim Special Edition",
                @"F:\Games\Steam\steamapps\common\Skyrim Special Edition",
                @"F:\Games\Skyrim Special Edition",
                @"F:\Skyrim Special Edition",
                @"F:\GOG Games\Skyrim Anniversary Edition",
                @"E:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                @"D:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition",
                @"G:\SteamLibrary\steamapps\common\Skyrim Special Edition"
            };
            for (int i = 0; i < directGuesses.Length; i++) candidates.Add(directGuesses[i]);

            // Check Registry (32-bit WOW6432Node & 64-bit Bethesda Softworks key)
            string[] regPaths = new string[]
            {
                @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim Special Edition",
                @"SOFTWARE\Bethesda Softworks\Skyrim Special Edition"
            };
            for (int r = 0; r < regPaths.Length; r++)
            {
                try
                {
                    using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(regPaths[r]))
                    {
                        if (rk != null)
                        {
                            object val = rk.GetValue("Installed Path");
                            if (val != null)
                            {
                                string p = val.ToString().TrimEnd('\\', '/');
                                if (!string.IsNullOrEmpty(p)) candidates.Add(p);
                            }
                        }
                    }
                }
                catch { }
            }

            // Parse Steam libraryfolders.vdf if present
            string[] vdfPaths = new string[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf",
                @"F:\SteamLibrary\steamapps\libraryfolders.vdf",
                @"E:\SteamLibrary\steamapps\libraryfolders.vdf",
                @"D:\SteamLibrary\steamapps\libraryfolders.vdf"
            };
            for (int v = 0; v < vdfPaths.Length; v++)
            {
                try
                {
                    if (File.Exists(vdfPaths[v]))
                    {
                        string vdf = File.ReadAllText(vdfPaths[v], Encoding.UTF8);
                        MatchCollection mc = Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\"");
                        for (int m = 0; m < mc.Count; m++)
                        {
                            string libRoot = mc[m].Groups[1].Value.Replace("\\\\", "\\");
                            candidates.Add(Path.Combine(Path.Combine(Path.Combine(libRoot, "steamapps"), "common"), "Skyrim Special Edition"));
                        }
                    }
                }
                catch { }
            }

            // Shallow 2-level scan on F:\ if SkyrimSE.exe is in a custom folder name on F:\
            try
            {
                if (Directory.Exists(@"F:\"))
                {
                    string[] fTop = Directory.GetDirectories(@"F:\");
                    for (int d = 0; d < fTop.Length; d++)
                    {
                        string fn = Path.GetFileName(fTop[d]) ?? "";
                        if (fn.StartsWith("$", StringComparison.Ordinal) || fn.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (File.Exists(Path.Combine(fTop[d], "SkyrimSE.exe")))
                            candidates.Add(fTop[d]);
                        try
                        {
                            string[] fSub = Directory.GetDirectories(fTop[d]);
                            for (int s = 0; s < fSub.Length; s++)
                            {
                                if (File.Exists(Path.Combine(fSub[s], "SkyrimSE.exe")))
                                    candidates.Add(fSub[s]);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            for (int i = 0; i < candidates.Count; i++)
            {
                string dir = candidates[i];
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                string exe = Path.Combine(dir, "SkyrimSE.exe");
                if (!File.Exists(exe)) continue;

                info.Found = true;
                info.GameDir = dir;
                info.SkyrimExePath = exe;
                try
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exe);
                    int maj = fvi.FileMajorPart;
                    int min = fvi.FileMinorPart;
                    int bld = fvi.FileBuildPart;
                    int prv = fvi.FilePrivatePart;
                    info.SkyrimVersionString = string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", maj, min, bld, prv);
                    info.PackedSkseRuntimeVersion = PackSkyrimVersion(maj, min, bld, prv);
                }
                catch
                {
                    info.SkyrimVersionString = "1.6.1170.0";
                    info.PackedSkseRuntimeVersion = 0x01064920u;
                }

                string skseLdr = Path.Combine(dir, "skse64_loader.exe");
                info.HasSkseLoader = File.Exists(skseLdr);
                if (info.HasSkseLoader) info.SkseLoaderPath = skseLdr;

                try
                {
                    string[] skseDlls = Directory.GetFiles(dir, "skse64_*.dll");
                    for (int k = 0; k < skseDlls.Length; k++)
                    {
                        string dn = Path.GetFileName(skseDlls[k]) ?? "";
                        if (dn.IndexOf("steam_loader", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            info.SkseDllName = dn;
                            break;
                        }
                    }
                }
                catch { }

                string ckExe = Path.Combine(dir, "CreationKit.exe");
                info.HasCreationKit = File.Exists(ckExe);
                if (info.HasCreationKit) info.CreationKitExePath = ckExe;

                string papExe = Path.Combine(Path.Combine(dir, "Papyrus Compiler"), "PapyrusCompiler.exe");
                info.HasPapyrusCompiler = File.Exists(papExe);
                if (info.HasPapyrusCompiler) info.PapyrusCompilerPath = papExe;

                string[] zipCands = new string[]
                {
                    Path.Combine(Path.Combine(dir, "Data"), "Scripts.zip"),
                    Path.Combine(Path.Combine(Path.Combine(dir, "Data"), "Source"), "Scripts.zip"),
                    Path.Combine(Path.Combine(dir, "Source"), "Scripts.zip"),
                    Path.Combine(dir, "Scripts.zip"),
                    Path.Combine(Path.Combine(dir, "Data"), "Scripts.rar")
                };
                for (int z = 0; z < zipCands.Length; z++)
                {
                    if (File.Exists(zipCands[z])) { info.ScriptsZipPath = zipCands[z]; break; }
                }
                break;
            }
            return info;
        }

        // Non-destructively writes CreationKitCustom.ini if absent so Creation Kit loads multiple masters cleanly without audio-init crashes.
        public static string EnsureCreationKitCustomIni(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return "";
            string iniPath = Path.Combine(gameDir, "CreationKitCustom.ini");
            if (!File.Exists(iniPath))
            {
                UTF8Encoding utf8NoBom = new UTF8Encoding(false);
                string body =
                    "[General]\r\n" +
                    "bAllowMultipleMasterLoads=1\r\n" +
                    "bAllowMultipleEditors=1\r\n\r\n" +
                    "[Audio]\r\n" +
                    "bEnableAudio=0\r\n\r\n" +
                    "[Papyrus]\r\n" +
                    "sScriptSourceFolder = \"Data\\Source\\Scripts\"\r\n";
                File.WriteAllText(iniPath, body, utf8NoBom);
            }
            return iniPath;
        }

        private static void WriteSubrecord(BinaryWriter bw, string sig4, byte[] payload)
        {
            byte[] sb = Encoding.ASCII.GetBytes(sig4);
            bw.Write(sb, 0, 4);
            bw.Write((ushort)payload.Length);
            if (payload.Length > 0) bw.Write(payload);
        }

        private static byte[] NullTermAscii(string s)
        {
            byte[] raw = Encoding.ASCII.GetBytes(s ?? "");
            byte[] res = new byte[raw.Length + 1];
            Buffer.BlockCopy(raw, 0, res, 0, raw.Length);
            res[raw.Length] = 0;
            return res;
        }

        private static byte[] BuildMainRecord(string sig4, uint flags, uint formId, ushort formVer, byte[] subrecordsPayload)
        {
            using (MemoryStream ms = new MemoryStream(24 + subrecordsPayload.Length))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes(sig4), 0, 4); // +0x00 signature
                bw.Write((uint)subrecordsPayload.Length);      // +0x04 dataSize
                bw.Write(flags);                               // +0x08 recordFlags
                bw.Write(formId);                              // +0x0C formID
                bw.Write(0u);                                  // +0x10 vcs1
                bw.Write(formVer);                             // +0x14 formVersion (44)
                bw.Write((ushort)0);                           // +0x16 vcs2
                if (subrecordsPayload.Length > 0) bw.Write(subrecordsPayload);
                return ms.ToArray();
            }
        }

        private static byte[] BuildKywdRecord(uint compactFormId, string edid, byte r, byte g, byte b)
        {
            using (MemoryStream sub = new MemoryStream(96))
            using (BinaryWriter bw = new BinaryWriter(sub))
            {
                WriteSubrecord(bw, "EDID", NullTermAscii(edid));
                WriteSubrecord(bw, "CNAM", new byte[] { r, g, b, 0xFF });
                return BuildMainRecord("KYWD", 0u, compactFormId, SkyrimSeFormVersion, sub.ToArray());
            }
        }

        // Synthesizes a 100% standards-compliant ESL-flagged (0x00000200, FE:xxx light slot) TES4 FormVersion 44 plugin:
        // OmegaTriEngine_WorldBridge.esp with HEDR v1.70, Skyrim.esm master, and 4 compact 12-bit KYWD anchors (0x01000800..0x01000803).
        public static int EmitEslLightPluginEsp(string espPath)
        {
            string dir = Path.GetDirectoryName(espPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            byte[][] kywdRecs = new byte[][]
            {
                BuildKywdRecord(0x01000800u, "OmegaTriEngine_Kywd_SovngardeAurora", 0x10, 0xE6, 0xB2),
                BuildKywdRecord(0x01000801u, "OmegaTriEngine_Kywd_ThuumCaustic", 0x38, 0xBD, 0xF8),
                BuildKywdRecord(0x01000802u, "OmegaTriEngine_Kywd_SpectreSheafH1Zero", 0xF5, 0xC2, 0x3D),
                BuildKywdRecord(0x01000803u, "OmegaTriEngine_Kywd_WorldBridgeActive", 0x8B, 0x5C, 0xF6)
            };

            int grupPayloadLen = 0;
            for (int i = 0; i < kywdRecs.Length; i++) grupPayloadLen += kywdRecs[i].Length;
            uint totalGrupSize = (uint)(24 + grupPayloadLen);

            // HEDR: float version = 1.70f (compatible across SE 1.5.97 & AE 1.6.x / 1.7.x), uint32 numRecords = 5 (1 GRUP + 4 KYWD), uint32 nextObjectId = 0x804
            byte[] hedrBytes = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(1.70f), 0, hedrBytes, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(5u), 0, hedrBytes, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0x00000804u), 0, hedrBytes, 8, 4);

            byte[] tes4Rec;
            using (MemoryStream tes4Sub = new MemoryStream(256))
            using (BinaryWriter bw = new BinaryWriter(tes4Sub))
            {
                WriteSubrecord(bw, "HEDR", hedrBytes);
                WriteSubrecord(bw, "CNAM", NullTermAscii("ACT-Omega Unified Labs"));
                WriteSubrecord(bw, "SNAM", NullTermAscii("Omega Tri-Engine v1.7.0 World-Space Shader & H1=0 Spectre Quest Guard (MIT)"));
                WriteSubrecord(bw, "MAST", NullTermAscii("Skyrim.esm"));
                WriteSubrecord(bw, "DATA", new byte[8]);
                tes4Rec = BuildMainRecord("TES4", EslRecordFlag, 0u, SkyrimSeFormVersion, tes4Sub.ToArray());
            }

            using (MemoryStream full = new MemoryStream(tes4Rec.Length + (int)totalGrupSize))
            using (BinaryWriter bw = new BinaryWriter(full))
            {
                bw.Write(tes4Rec);
                // Top-level GRUP header (24 bytes): "GRUP", groupSize, label="KYWD", groupType=0, stamp=0, unknown=0
                bw.Write(Encoding.ASCII.GetBytes("GRUP"), 0, 4);
                bw.Write(totalGrupSize);
                bw.Write(Encoding.ASCII.GetBytes("KYWD"), 0, 4);
                bw.Write(0);
                bw.Write(0u);
                bw.Write(0u);
                for (int i = 0; i < kywdRecs.Length; i++) bw.Write(kywdRecs[i]);
                byte[] allBytes = full.ToArray();
                File.WriteAllBytes(espPath, allBytes);
                return allBytes.Length;
            }
        }

        // Byte-exact structural validator for OmegaTriEngine_WorldBridge.esp (verifies TES4, ESL 0x200, FormVersion 44, HEDR, GRUP, and 12-bit light FormIDs).
        public static bool ValidateEslLightPluginEsp(string espPath, out int kywdCount, out string errorMsg)
        {
            kywdCount = 0;
            errorMsg = "";
            if (string.IsNullOrEmpty(espPath) || !File.Exists(espPath))
            {
                errorMsg = "ESP file missing";
                return false;
            }
            byte[] b = File.ReadAllBytes(espPath);
            if (b.Length < 64)
            {
                errorMsg = "ESP too small";
                return false;
            }
            string sig0 = Encoding.ASCII.GetString(b, 0, 4);
            uint tes4DataSize = BitConverter.ToUInt32(b, 4);
            uint tes4Flags = BitConverter.ToUInt32(b, 8);
            ushort tes4FormVer = BitConverter.ToUInt16(b, 20);
            if (!string.Equals(sig0, "TES4", StringComparison.Ordinal) || (tes4Flags & EslRecordFlag) == 0u || tes4FormVer != SkyrimSeFormVersion)
            {
                errorMsg = "Invalid TES4 ESL header";
                return false;
            }
            int grupOff = 24 + (int)tes4DataSize;
            if (grupOff + 24 > b.Length)
            {
                errorMsg = "Truncated TES4 payload";
                return false;
            }
            string grupSig = Encoding.ASCII.GetString(b, grupOff, 4);
            uint grupSize = BitConverter.ToUInt32(b, grupOff + 4);
            string grupLbl = Encoding.ASCII.GetString(b, grupOff + 8, 4);
            if (!string.Equals(grupSig, "GRUP", StringComparison.Ordinal) ||
                !string.Equals(grupLbl, "KYWD", StringComparison.Ordinal) ||
                grupOff + (int)grupSize != b.Length)
            {
                errorMsg = "Invalid GRUP container bounds";
                return false;
            }

            int pos = grupOff + 24;
            while (pos < b.Length)
            {
                if (pos + 24 > b.Length) { errorMsg = "Truncated KYWD header"; return false; }
                string rSig = Encoding.ASCII.GetString(b, pos, 4);
                uint rSize = BitConverter.ToUInt32(b, pos + 4);
                uint formId = BitConverter.ToUInt32(b, pos + 12);
                ushort fVer = BitConverter.ToUInt16(b, pos + 20);
                if (!string.Equals(rSig, "KYWD", StringComparison.Ordinal) || fVer != SkyrimSeFormVersion)
                {
                    errorMsg = "Unexpected record signature " + rSig;
                    return false;
                }
                // Enforce strict xEdit ESL Light-Slot 12-bit ObjectID rule: (formId & 0x00FFF000) == 0
                if ((formId & 0x00FFF000u) != 0u)
                {
                    errorMsg = "ESL 12-bit ObjectID overflow on 0x" + formId.ToString("X8", CultureInfo.InvariantCulture);
                    return false;
                }
                kywdCount++;
                pos += 24 + (int)rSize;
            }
            return (pos == b.Length) && (kywdCount == 4);
        }

        // Emits UTF-8 No-BOM vanilla-CK-compilable Papyrus scripts (OmegaTriEngine_WorldSync.psc & OmegaTriEngine_PlayerAlias.psc)
        public static void EmitPapyrusScriptsNoBom(string outSourceScriptsDir, out string worldSyncPsc, out string playerAliasPsc)
        {
            Directory.CreateDirectory(outSourceScriptsDir);
            UTF8Encoding utf8NoBom = new UTF8Encoding(false);

            worldSyncPsc = Path.Combine(outSourceScriptsDir, "OmegaTriEngine_WorldSync.psc");
            playerAliasPsc = Path.Combine(outSourceScriptsDir, "OmegaTriEngine_PlayerAlias.psc");

            string qCode =
                "Scriptname OmegaTriEngine_WorldSync extends Quest\r\n" +
                "; SPDX-License-Identifier: MIT OR BSD-3-Clause\r\n" +
                "; Omega Tri-Engine v1.7.0 Creation Kit World-Space Telemetry Bridge\r\n" +
                "; Zero external SKSE .psc dependencies - compiles cleanly with stock PapyrusCompiler.exe!\r\n\r\n" +
                "Int Property StageMask = 39 Auto\r\n" +
                "Int Property InSpectreDungeon = 0 Auto\r\n" +
                "Bool _loopActive = False\r\n" +
                "Int _pendingShoutFormId = 0\r\n\r\n" +
                "Event OnInit()\r\n" +
                "    StartSyncLoop()\r\n" +
                "EndEvent\r\n\r\n" +
                "Function StartSyncLoop()\r\n" +
                "    Debug.OpenUserLog(\"OmegaTriEngine_WorldBridge\")\r\n" +
                "    _loopActive = True\r\n" +
                "    RegisterForSingleUpdate(0.25)\r\n" +
                "EndFunction\r\n\r\n" +
                "Function StopSyncLoop()\r\n" +
                "    _loopActive = False\r\n" +
                "    UnregisterForUpdate()\r\n" +
                "EndFunction\r\n\r\n" +
                "Function NotifyShoutOrSpell(Form akSpell)\r\n" +
                "    If akSpell != None\r\n" +
                "        _pendingShoutFormId = akSpell.GetFormID()\r\n" +
                "        Actor pl = Game.GetPlayer()\r\n" +
                "        If pl != None && pl.Is3DLoaded()\r\n" +
                "            EmitSnapshot(pl)\r\n" +
                "        EndIf\r\n" +
                "    EndIf\r\n" +
                "EndFunction\r\n\r\n" +
                "Function SetSpectreDungeonState(Int aiStageMask, Int aiInSpectre)\r\n" +
                "    StageMask = aiStageMask\r\n" +
                "    InSpectreDungeon = aiInSpectre\r\n" +
                "EndFunction\r\n\r\n" +
                "Event OnUpdate()\r\n" +
                "    If !_loopActive\r\n" +
                "        Return\r\n" +
                "    EndIf\r\n" +
                "    Float nextInterval = 0.25\r\n" +
                "    If Utility.IsInMenuMode()\r\n" +
                "        nextInterval = 0.60\r\n" +
                "    Else\r\n" +
                "        Actor pl = Game.GetPlayer()\r\n" +
                "        If pl != None && pl.Is3DLoaded()\r\n" +
                "            EmitSnapshot(pl)\r\n" +
                "        Else\r\n" +
                "            nextInterval = 1.00\r\n" +
                "        EndIf\r\n" +
                "    EndIf\r\n" +
                "    RegisterForSingleUpdate(nextInterval)\r\n" +
                "EndEvent\r\n\r\n" +
                "Function EmitSnapshot(Actor akPlayer)\r\n" +
                "    Float px = akPlayer.GetPositionX()\r\n" +
                "    Float py = akPlayer.GetPositionY()\r\n" +
                "    Float pz = akPlayer.GetPositionZ() + 120.0\r\n" +
                "    Float yawDeg = akPlayer.GetAngleZ()\r\n" +
                "    Float pitchDeg = akPlayer.GetAngleX()\r\n" +
                "    Weather w = Weather.GetCurrentWeather()\r\n" +
                "    Int wId = 0\r\n" +
                "    Int wClass = 0\r\n" +
                "    If w != None\r\n" +
                "        wId = w.GetFormID()\r\n" +
                "        wClass = w.GetClassification()\r\n" +
                "    EndIf\r\n" +
                "    Float days = Utility.GetCurrentGameTime()\r\n" +
                "    Float hour24 = (days - (days as Int)) * 24.0\r\n" +
                "    Int shoutId = _pendingShoutFormId\r\n" +
                "    _pendingShoutFormId = 0\r\n" +
                "    Debug.OpenUserLog(\"OmegaTriEngine_WorldBridge\")\r\n" +
                "    Debug.TraceUser(\"OmegaTriEngine_WorldBridge\", \"OTW|\" + px + \"|\" + py + \"|\" + pz + \"|\" + yawDeg + \"|\" + pitchDeg + \"|\" + wId + \"|\" + wClass + \"|\" + hour24 + \"|\" + shoutId + \"|\" + StageMask + \"|\" + InSpectreDungeon)\r\n" +
                "EndFunction\r\n";

            string aCode =
                "Scriptname OmegaTriEngine_PlayerAlias extends ReferenceAlias\r\n" +
                "; SPDX-License-Identifier: MIT OR BSD-3-Clause\r\n" +
                "; Forwards event-driven Dragon Shout / Spell casts & Spectre Location transitions to OmegaTriEngine_WorldSync.\r\n\r\n" +
                "OmegaTriEngine_WorldSync Property BridgeQuest Auto\r\n" +
                "Keyword Property SpectreDungeonKeyword Auto\r\n\r\n" +
                "Event OnPlayerLoadGame()\r\n" +
                "    If BridgeQuest != None\r\n" +
                "        BridgeQuest.StartSyncLoop()\r\n" +
                "    EndIf\r\n" +
                "EndEvent\r\n\r\n" +
                "Event OnSpellCast(Form akSpell)\r\n" +
                "    If BridgeQuest != None\r\n" +
                "        BridgeQuest.NotifyShoutOrSpell(akSpell)\r\n" +
                "    EndIf\r\n" +
                "EndEvent\r\n\r\n" +
                "Event OnLocationChange(Location akOldLoc, Location akNewLoc)\r\n" +
                "    If BridgeQuest != None\r\n" +
                "        Int isSpec = 0\r\n" +
                "        If akNewLoc != None && SpectreDungeonKeyword != None && akNewLoc.HasKeyword(SpectreDungeonKeyword)\r\n" +
                "            isSpec = 1\r\n" +
                "        EndIf\r\n" +
                "        BridgeQuest.SetSpectreDungeonState(BridgeQuest.StageMask, isSpec)\r\n" +
                "    EndIf\r\n" +
                "EndEvent\r\n";

            File.WriteAllText(worldSyncPsc, qCode, utf8NoBom);
            File.WriteAllText(playerAliasPsc, aCode, utf8NoBom);
        }

        // Pure System.dll PKZIP extractor for vanilla Data\Scripts.zip (.psc & .flg headers) so PapyrusCompiler.exe works out-of-the-box!
        public static int ExtractVanillaPscHeadersFromZip(string scriptsZipPath, string dstDir)
        {
            if (string.IsNullOrEmpty(scriptsZipPath) || !File.Exists(scriptsZipPath)) return 0;
            Directory.CreateDirectory(dstDir);
            string marker = Path.Combine(dstDir, "Actor.psc");
            if (File.Exists(marker) && File.Exists(Path.Combine(dstDir, "TESV_Papyrus_Flags.flg")))
            {
                return Directory.GetFiles(dstDir, "*.psc").Length;
            }

            int extracted = 0;
            using (FileStream fs = new FileStream(scriptsZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                long fileLen = fs.Length;
                long scanMin = Math.Max(0L, fileLen - 65558L);
                long eocdPos = -1L;
                fs.Position = scanMin;
                byte[] tail = br.ReadBytes((int)(fileLen - scanMin));
                for (int i = tail.Length - 22; i >= 0; i--)
                {
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                    {
                        eocdPos = scanMin + i;
                        break;
                    }
                }
                if (eocdPos < 0) return 0;

                fs.Position = eocdPos + 10;
                ushort totalEntries = br.ReadUInt16();
                uint cdSize = br.ReadUInt32();
                uint cdOffset = br.ReadUInt32();
                if (cdOffset + cdSize > fileLen) return 0;

                long cdPos = cdOffset;
                for (int e = 0; e < totalEntries; e++)
                {
                    fs.Position = cdPos;
                    if (br.ReadUInt32() != 0x02014B50u) break;
                    fs.Position = cdPos + 10;
                    ushort method = br.ReadUInt16();
                    fs.Position = cdPos + 20;
                    uint compSize = br.ReadUInt32();
                    uint uncompSize = br.ReadUInt32();
                    ushort nameLen = br.ReadUInt16();
                    ushort extraLen = br.ReadUInt16();
                    ushort commentLen = br.ReadUInt16();
                    fs.Position = cdPos + 42;
                    uint localHdrOff = br.ReadUInt32();
                    byte[] nameBytes = br.ReadBytes(nameLen);
                    string entryName = Encoding.UTF8.GetString(nameBytes);
                    cdPos = cdPos + 46 + nameLen + extraLen + commentLen;

                    string flatName = Path.GetFileName(entryName.Replace('/', '\\'));
                    if (string.IsNullOrEmpty(flatName)) continue;
                    if (flatName.StartsWith("QF_", StringComparison.OrdinalIgnoreCase) ||
                        flatName.StartsWith("TIF_", StringComparison.OrdinalIgnoreCase) ||
                        flatName.StartsWith("SF_", StringComparison.OrdinalIgnoreCase) ||
                        flatName.StartsWith("PF_", StringComparison.OrdinalIgnoreCase) ||
                        flatName.StartsWith("PRKF_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    bool want = flatName.EndsWith(".psc", StringComparison.OrdinalIgnoreCase) ||
                                flatName.EndsWith(".flg", StringComparison.OrdinalIgnoreCase);
                    if (!want || uncompSize > 8 * 1024 * 1024) continue;

                    fs.Position = localHdrOff;
                    if (br.ReadUInt32() != 0x04034B50u) continue;
                    fs.Position = localHdrOff + 26;
                    ushort lNameLen = br.ReadUInt16();
                    ushort lExtraLen = br.ReadUInt16();
                    fs.Position = localHdrOff + 30 + lNameLen + lExtraLen;

                    byte[] outBytes = null;
                    if (method == 0)
                    {
                        outBytes = br.ReadBytes((int)compSize);
                    }
                    else if (method == 8)
                    {
                        byte[] comp = br.ReadBytes((int)compSize);
                        using (MemoryStream cms = new MemoryStream(comp))
                        using (DeflateStream ds = new DeflateStream(cms, CompressionMode.Decompress))
                        using (MemoryStream oms = new MemoryStream((int)uncompSize))
                        {
                            byte[] buf = new byte[8192];
                            int r;
                            while ((r = ds.Read(buf, 0, buf.Length)) > 0) oms.Write(buf, 0, r);
                            outBytes = oms.ToArray();
                        }
                    }
                    if (outBytes != null)
                    {
                        File.WriteAllBytes(Path.Combine(dstDir, flatName), outBytes);
                        extracted++;
                    }
                }
            }
            return extracted;
        }

        public static bool TryCompilePapyrusScriptsToPex(
            SkyrimInstallInfo skyrim,
            string sourceScriptsDir,
            string outPexDir,
            string workspaceDir,
            out string compilerSummary)
        {
            compilerSummary = "Creation Kit PapyrusCompiler.exe not present; shipped UTF-8 No-BOM .psc sources.";
            if (skyrim == null || !skyrim.Found || !skyrim.HasPapyrusCompiler || !File.Exists(skyrim.PapyrusCompilerPath))
            {
                return false;
            }

            Directory.CreateDirectory(outPexDir);
            string ckHeadersDir = Path.Combine(workspaceDir, "ck_psc_headers");
            Directory.CreateDirectory(ckHeadersDir);

            string dataSourceScripts = Path.Combine(Path.Combine(skyrim.GameDir, "Data"), Path.Combine("Source", "Scripts"));
            string dataScriptsSource = Path.Combine(Path.Combine(skyrim.GameDir, "Data"), Path.Combine("Scripts", "Source"));
            bool haveExtractedVanilla = File.Exists(Path.Combine(dataSourceScripts, "Actor.psc")) ||
                                        File.Exists(Path.Combine(dataScriptsSource, "Actor.psc"));
            if (!haveExtractedVanilla && !string.IsNullOrEmpty(skyrim.ScriptsZipPath) && File.Exists(skyrim.ScriptsZipPath))
            {
                try { ExtractVanillaPscHeadersFromZip(skyrim.ScriptsZipPath, ckHeadersDir); } catch { }
            }

            List<string> incDirs = new List<string>();
            incDirs.Add(sourceScriptsDir);
            if (Directory.Exists(dataSourceScripts)) incDirs.Add(dataSourceScripts);
            if (Directory.Exists(dataScriptsSource)) incDirs.Add(dataScriptsSource);
            if (Directory.Exists(ckHeadersDir)) incDirs.Add(ckHeadersDir);

            string flagsPath = "TESV_Papyrus_Flags.flg";
            for (int i = 0; i < incDirs.Count; i++)
            {
                string candFlg = Path.Combine(incDirs[i], "TESV_Papyrus_Flags.flg");
                if (File.Exists(candFlg)) { flagsPath = candFlg; break; }
            }

            for (int k = 0; k < incDirs.Count; k++) incDirs[k] = incDirs[k].TrimEnd('\\', '/');
            outPexDir = outPexDir.TrimEnd('\\', '/');
            string importArg = string.Join(";", incDirs.ToArray());
            string[] scripts = new string[] { "OmegaTriEngine_WorldSync.psc", "OmegaTriEngine_PlayerAlias.psc" };
            int okCount = 0;
            StringBuilder logSb = new StringBuilder();

            for (int s = 0; s < scripts.Length; s++)
            {
                string pscFull = Path.Combine(sourceScriptsDir, scripts[s]);
                string args = string.Format(CultureInfo.InvariantCulture,
                    "\"{0}\" -f=\"{1}\" -i=\"{2}\" -o=\"{3}\"",
                    pscFull, flagsPath, importArg, outPexDir);
                ProcessStartInfo psi = new ProcessStartInfo(skyrim.PapyrusCompilerPath, args);
                psi.WorkingDirectory = Path.GetDirectoryName(skyrim.PapyrusCompilerPath);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    string pexFile = Path.Combine(outPexDir, Path.GetFileNameWithoutExtension(scripts[s]) + ".pex");
                    if (p.ExitCode == 0 && File.Exists(pexFile))
                    {
                        okCount++;
                    }
                    else
                    {
                        logSb.Append(so).Append(" ").Append(se);
                    }
                }
            }

            if (okCount == scripts.Length)
            {
                compilerSummary = "Compiled 2/2 .pex via Creation Kit PapyrusCompiler.exe (" + skyrim.GameDir + ")";
                return true;
            }
            compilerSummary = "PapyrusCompiler partial (" + okCount + "/2): " + logSb.ToString().Trim();
            return false;
        }

        // Pure System.dll PKZIP writer (Deflate + IEEE CRC-32) so OmegaEngine.exe needs zero extra assembly references!
        public static void CreateZipArchivePure(string zipPath, List<KeyValuePair<string, byte[]>> entries)
        {
            string parent = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            using (FileStream fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                List<byte[]> cdRecords = new List<byte[]>();
                for (int i = 0; i < entries.Count; i++)
                {
                    string relSlash = entries[i].Key.Replace('\\', '/').TrimStart('/');
                    byte[] nameUtf8 = Encoding.UTF8.GetBytes(relSlash);
                    byte[] rawData = entries[i].Value ?? new byte[0];
                    uint crc = OmegaTriEngineSharedBus.ComputeCrc32(rawData, 0, rawData.Length);

                    byte[] compData;
                    ushort method = 8;
                    if (rawData.Length < 64)
                    {
                        method = 0;
                        compData = rawData;
                    }
                    else
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                            {
                                ds.Write(rawData, 0, rawData.Length);
                            }
                            compData = ms.ToArray();
                        }
                        if (compData.Length >= rawData.Length)
                        {
                            method = 0;
                            compData = rawData;
                        }
                    }

                    uint localOff = (uint)fs.Position;
                    ushort dosTime = 0x6000;
                    ushort dosDate = 0x5978;

                    // Local file header (0x04034B50)
                    bw.Write(0x04034B50u);
                    bw.Write((ushort)20);
                    bw.Write((ushort)0x0800); // UTF-8 filename bit
                    bw.Write(method);
                    bw.Write(dosTime);
                    bw.Write(dosDate);
                    bw.Write(crc);
                    bw.Write((uint)compData.Length);
                    bw.Write((uint)rawData.Length);
                    bw.Write((ushort)nameUtf8.Length);
                    bw.Write((ushort)0);
                    bw.Write(nameUtf8);
                    if (compData.Length > 0) bw.Write(compData);

                    using (MemoryStream cdMs = new MemoryStream(64 + nameUtf8.Length))
                    using (BinaryWriter cbw = new BinaryWriter(cdMs))
                    {
                        cbw.Write(0x02014B50u);
                        cbw.Write((ushort)20);
                        cbw.Write((ushort)20);
                        cbw.Write((ushort)0x0800);
                        cbw.Write(method);
                        cbw.Write(dosTime);
                        cbw.Write(dosDate);
                        cbw.Write(crc);
                        cbw.Write((uint)compData.Length);
                        cbw.Write((uint)rawData.Length);
                        cbw.Write((ushort)nameUtf8.Length);
                        cbw.Write((ushort)0);
                        cbw.Write((ushort)0);
                        cbw.Write((ushort)0);
                        cbw.Write((ushort)0);
                        cbw.Write(0u);
                        cbw.Write(localOff);
                        cbw.Write(nameUtf8);
                        cdRecords.Add(cdMs.ToArray());
                    }
                }

                uint cdStart = (uint)fs.Position;
                for (int i = 0; i < cdRecords.Count; i++) bw.Write(cdRecords[i]);
                uint cdLen = (uint)fs.Position - cdStart;

                // End of Central Directory (0x06054B50)
                bw.Write(0x06054B50u);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write((ushort)cdRecords.Count);
                bw.Write((ushort)cdRecords.Count);
                bw.Write(cdLen);
                bw.Write(cdStart);
                bw.Write((ushort)0);
            }
        }

        public static SkyrimModBundleResult BuildCompleteSkyrimModPackage(
            string exportsDir,
            string explicitGameDir,
            string workspaceDir,
            float[] currentU80,
            string customOmsh,
            bool syncDirectlyToGameData)
        {
            SkyrimModBundleResult res = new SkyrimModBundleResult();
            Directory.CreateDirectory(exportsDir);
            Directory.CreateDirectory(workspaceDir);
            UTF8Encoding utf8NoBom = new UTF8Encoding(false);

            SkyrimInstallInfo sky = DiscoverSkyrimAeInstallation(explicitGameDir);
            if (sky.Found)
            {
                EnsureCreationKitCustomIni(sky.GameDir);
            }

            string stageDir = Path.Combine(workspaceDir, "vortex_mod_stage");
            if (Directory.Exists(stageDir))
            {
                try { Directory.Delete(stageDir, true); } catch { }
            }
            Directory.CreateDirectory(stageDir);

            // 1. ESL Light Plugin (.esp)
            res.EspPath = Path.Combine(exportsDir, "OmegaTriEngine_WorldBridge.esp");
            res.EspBytes = EmitEslLightPluginEsp(res.EspPath);
            string espErr;
            bool espValid = ValidateEslLightPluginEsp(res.EspPath, out res.EspKeywordCount, out espErr);

            // 2. UTF-8 No-BOM Papyrus scripts (.psc) + optional Creation Kit PapyrusCompiler (.pex)
            string srcScriptsDir = Path.Combine(Path.Combine(stageDir, "Source"), "Scripts");
            string pexOutDir = Path.Combine(stageDir, "Scripts");
            EmitPapyrusScriptsNoBom(srcScriptsDir, out res.WorldSyncPscPath, out res.PlayerAliasPscPath);
            File.Copy(res.WorldSyncPscPath, Path.Combine(exportsDir, "OmegaTriEngine_WorldSync.psc"), true);
            File.Copy(res.PlayerAliasPscPath, Path.Combine(exportsDir, "OmegaTriEngine_PlayerAlias.psc"), true);

            string papSummary;
            res.PexCompiledViaCk = TryCompilePapyrusScriptsToPex(sky, srcScriptsDir, pexOutDir, workspaceDir, out papSummary);
            string wPex = Path.Combine(pexOutDir, "OmegaTriEngine_WorldSync.pex");
            string aPex = Path.Combine(pexOutDir, "OmegaTriEngine_PlayerAlias.pex");
            if (File.Exists(wPex))
            {
                res.WorldSyncPexPath = Path.Combine(exportsDir, "OmegaTriEngine_WorldSync.pex");
                File.Copy(wPex, res.WorldSyncPexPath, true);
            }
            if (File.Exists(aPex))
            {
                res.PlayerAliasPexPath = Path.Combine(exportsDir, "OmegaTriEngine_PlayerAlias.pex");
                File.Copy(aPex, res.PlayerAliasPexPath, true);
            }

            // 3. Export upgraded World-Space ReShade .fx + .addon64 + SKSE64 DLL
            string addonStub, fxSha;
            res.ReshadeFxPath = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(exportsDir, currentU80, customOmsh, out addonStub, out fxSha);
            res.ReshadeAddonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            res.SkseDllPath = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
            if (!string.IsNullOrEmpty(res.SkseDllPath) && File.Exists(res.SkseDllPath))
            {
                string dstDll = Path.Combine(exportsDir, "OmegaSkyrimWorldBridge.dll");
                if (!string.Equals(Path.GetFullPath(res.SkseDllPath), Path.GetFullPath(dstDll), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(res.SkseDllPath, dstDll, true); } catch { }
                }
            }

            string skseIniBody =
                "; OmegaSkyrimWorldBridge.ini (SKSE64 2.2.x / Skyrim AE 1.6.x & 1.7.x)\r\n" +
                "[WorldBridge]\r\n" +
                "SharedBusName=Local\\OmegaTriEngineUniforms\r\n" +
                "SkyrimUnitToMeter=0.014280\r\n" +
                "EnablePapyrusUserLogTail=true\r\n" +
                "EnableSheafQuestGuardH1=true\r\n";

            string fomodInfoXml =
                "<fomod>\r\n" +
                "  <Name>Omega Tri-Engine World-Space Shader &amp; SKSE64 Bridge</Name>\r\n" +
                "  <Author>ACT-Omega Unified Labs</Author>\r\n" +
                "  <Version>1.9.0</Version>\r\n" +
                "  <Description>Zero-CRT SKSE64 world-space camera/weather/Thu'um uniform streamer + ESL plugin + H^1=0 Spectre dungeon quest guard (MIT).</Description>\r\n" +
                "</fomod>\r\n";

            string skyrimCustomIniHint =
                "; Merge into Documents\\My Games\\Skyrim Special Edition\\SkyrimCustom.ini to enable optional Creation Kit Papyrus UserLog telemetry:\r\n" +
                "[Papyrus]\r\n" +
                "bEnableLogging=1\r\n" +
                "bEnableTrace=1\r\n";
            File.WriteAllText(Path.Combine(exportsDir, "SkyrimCustom_PapyrusUserLog.ini"), skyrimCustomIniHint, utf8NoBom);

            // 4. Assemble Data-rooted Vortex mod .zip (ready to drag-and-drop onto Vortex -> Mods!)
            List<KeyValuePair<string, byte[]>> vortexEntries = new List<KeyValuePair<string, byte[]>>();
            vortexEntries.Add(new KeyValuePair<string, byte[]>("OmegaTriEngine_WorldBridge.esp", File.ReadAllBytes(res.EspPath)));
            if (!string.IsNullOrEmpty(res.SkseDllPath) && File.Exists(res.SkseDllPath))
            {
                vortexEntries.Add(new KeyValuePair<string, byte[]>("SKSE/Plugins/OmegaSkyrimWorldBridge.dll", File.ReadAllBytes(res.SkseDllPath)));
            }
            vortexEntries.Add(new KeyValuePair<string, byte[]>("SKSE/Plugins/OmegaSkyrimWorldBridge.ini", utf8NoBom.GetBytes(skseIniBody)));
            vortexEntries.Add(new KeyValuePair<string, byte[]>("Source/Scripts/OmegaTriEngine_WorldSync.psc", File.ReadAllBytes(res.WorldSyncPscPath)));
            vortexEntries.Add(new KeyValuePair<string, byte[]>("Source/Scripts/OmegaTriEngine_PlayerAlias.psc", File.ReadAllBytes(res.PlayerAliasPscPath)));
            if (!string.IsNullOrEmpty(res.WorldSyncPexPath) && File.Exists(res.WorldSyncPexPath))
            {
                vortexEntries.Add(new KeyValuePair<string, byte[]>("Scripts/OmegaTriEngine_WorldSync.pex", File.ReadAllBytes(res.WorldSyncPexPath)));
            }
            if (!string.IsNullOrEmpty(res.PlayerAliasPexPath) && File.Exists(res.PlayerAliasPexPath))
            {
                vortexEntries.Add(new KeyValuePair<string, byte[]>("Scripts/OmegaTriEngine_PlayerAlias.pex", File.ReadAllBytes(res.PlayerAliasPexPath)));
            }
            vortexEntries.Add(new KeyValuePair<string, byte[]>("fomod/info.xml", utf8NoBom.GetBytes(fomodInfoXml)));

            res.VortexZipPath = Path.Combine(exportsDir, "OmegaTriEngine_SkyrimAE_SKSE64_v1.9.0_Vortex.zip");
            CreateZipArchivePure(res.VortexZipPath, vortexEntries);

            // 5. Companion Game-Root ReShade bundle (ReShade.fxh + .fx + Preset.ini + .addon64)
            List<KeyValuePair<string, byte[]>> rootEntries = new List<KeyValuePair<string, byte[]>>();
            string fxhExportPath = Path.Combine(exportsDir, "ReShade.fxh");
            if (File.Exists(fxhExportPath))
            {
                rootEntries.Add(new KeyValuePair<string, byte[]>("reshade-shaders/Shaders/ReShade.fxh", File.ReadAllBytes(fxhExportPath)));
            }
            if (File.Exists(res.ReshadeFxPath))
            {
                rootEntries.Add(new KeyValuePair<string, byte[]>("reshade-shaders/Shaders/OmegaTriEngine_SkyrimAE.fx", File.ReadAllBytes(res.ReshadeFxPath)));
            }
            string presetIni = Path.Combine(exportsDir, "OmegaTriEngine_Preset.ini");
            if (File.Exists(presetIni))
            {
                rootEntries.Add(new KeyValuePair<string, byte[]>("OmegaTriEngine_Preset.ini", File.ReadAllBytes(presetIni)));
            }
            if (!string.IsNullOrEmpty(res.ReshadeAddonPath) && File.Exists(res.ReshadeAddonPath))
            {
                rootEntries.Add(new KeyValuePair<string, byte[]>("OmegaTriEngine.addon64", File.ReadAllBytes(res.ReshadeAddonPath)));
            }
            res.RootReshadeZipPath = Path.Combine(exportsDir, "OmegaTriEngine_SkyrimAE_RootReShade_v1.9.0.zip");
            CreateZipArchivePure(res.RootReshadeZipPath, rootEntries);

            // Optional direct sync into Skyrim AE root + Data directory
            if (syncDirectlyToGameData && sky.Found && Directory.Exists(sky.GameDir))
            {
                string deployMsg;
                OmegaEngineHlslAndReshade.DeployBridgeToGameDirectory(sky.GameDir, currentU80, customOmsh, out deployMsg);
                string dataDir = Path.Combine(sky.GameDir, "Data");
                string sksePluginsDir = Path.Combine(Path.Combine(dataDir, "SKSE"), "Plugins");
                string dataSrcScripts = Path.Combine(Path.Combine(dataDir, "Source"), "Scripts");
                Directory.CreateDirectory(sksePluginsDir);
                Directory.CreateDirectory(dataSrcScripts);
                File.Copy(res.EspPath, Path.Combine(dataDir, "OmegaTriEngine_WorldBridge.esp"), true);
                if (!string.IsNullOrEmpty(res.SkseDllPath) && File.Exists(res.SkseDllPath))
                {
                    File.Copy(res.SkseDllPath, Path.Combine(sksePluginsDir, "OmegaSkyrimWorldBridge.dll"), true);
                }
                File.WriteAllText(Path.Combine(sksePluginsDir, "OmegaSkyrimWorldBridge.ini"), skseIniBody, utf8NoBom);
                File.Copy(res.WorldSyncPscPath, Path.Combine(dataSrcScripts, "OmegaTriEngine_WorldSync.psc"), true);
                File.Copy(res.PlayerAliasPscPath, Path.Combine(dataSrcScripts, "OmegaTriEngine_PlayerAlias.psc"), true);
                if (!string.IsNullOrEmpty(res.WorldSyncPexPath) && File.Exists(res.WorldSyncPexPath))
                {
                    string dataScripts = Path.Combine(dataDir, "Scripts");
                    Directory.CreateDirectory(dataScripts);
                    File.Copy(res.WorldSyncPexPath, Path.Combine(dataScripts, "OmegaTriEngine_WorldSync.pex"), true);
                }
                if (!string.IsNullOrEmpty(res.PlayerAliasPexPath) && File.Exists(res.PlayerAliasPexPath))
                {
                    string dataScripts = Path.Combine(dataDir, "Scripts");
                    Directory.CreateDirectory(dataScripts);
                    File.Copy(res.PlayerAliasPexPath, Path.Combine(dataScripts, "OmegaTriEngine_PlayerAlias.pex"), true);
                }
            }

            res.Ok = espValid && File.Exists(res.VortexZipPath) && (!string.IsNullOrEmpty(res.SkseDllPath) && File.Exists(res.SkseDllPath));
            res.Detail = string.Format(CultureInfo.InvariantCulture,
                "VortexZip={0} | ESP={1}B (ESL 0x200, {2} KYWDs) | SkyrimFound={3} ({4}) | {5}",
                res.VortexZipPath, res.EspBytes, res.EspKeywordCount,
                sky.Found, sky.Found ? (sky.GameDir + " v" + sky.SkyrimVersionString) : "probe-pending",
                papSummary);
            return res;
        }
    }
}