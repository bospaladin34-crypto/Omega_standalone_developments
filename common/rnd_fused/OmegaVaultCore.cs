using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaZeroCrateVault
{
    public sealed class VaultEntryRecord
    {
        public string Key = "";
        public string ValueText = "";
        public string CasSha256 = "";
        public long WallMs;
        public uint LogicalSeq;
        public string NodeId = "LOQ15-HOST";
        public bool Tombstone;
        public int ByteSize;
    }

    public sealed class WalRecoveryReport
    {
        public bool Ok;
        public string ChosenSlot = "NONE";
        public ulong CommittedGen;
        public bool SlotAValid;
        public ulong SlotAGen;
        public string SlotANote = "";
        public bool SlotBValid;
        public ulong SlotBGen;
        public string SlotBNote = "";
        public string RootSha256 = "";
        public List<VaultEntryRecord> Entries = new List<VaultEntryRecord>();
    }

    public sealed class SupplyChainFinding
    {
        public string Severity = "HIGH";
        public string RuleId = "";
        public int LineNumber;
        public string Snippet = "";
        public string Reason = "";
    }

    public sealed class SupplyChainScanReport
    {
        public bool Clean;
        public int LinesScanned;
        public bool HadUtf8Bom;
        public int BidiTrojanCount;
        public List<SupplyChainFinding> Findings = new List<SupplyChainFinding>();
    }

    public sealed class AirGapDecodeReport
    {
        public bool Ok;
        public byte[] Payload = new byte[0];
        public string DecodedText = "";
        public int CodewordsTotal;
        public int BitFlipsHealed;
        public int UncorrectableWords;
        public uint EnvelopeCrc32;
        public string Diagnostic = "";
    }

    public static class OmegaVaultCore
    {
        public const uint VaultWalMagic = 0x4F564C54u; // "OVLT"
        private static readonly object _kdfCacheLock = new object();
        private static string _cachedPass = null;
        private static byte[] _cachedSalt = null;
        private static byte[] _cachedEncKey = null;
        private static byte[] _cachedMacKey = null;

        private static void DeriveAeadKeysCached(string pass, byte[] salt16, out byte[] encKey, out byte[] macKey)
        {
            lock (_kdfCacheLock)
            {
                if (_cachedPass != null && _cachedSalt != null &&
                    string.Equals(_cachedPass, pass, StringComparison.Ordinal) &&
                    ConstantTimeEquals(_cachedSalt, salt16))
                {
                    encKey = new byte[32];
                    macKey = new byte[32];
                    Buffer.BlockCopy(_cachedEncKey, 0, encKey, 0, 32);
                    Buffer.BlockCopy(_cachedMacKey, 0, macKey, 0, 32);
                    return;
                }
            }
            byte[] ek, mk;
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(pass, salt16, 4096))
            {
                ek = kdf.GetBytes(32);
                mk = kdf.GetBytes(32);
            }
            lock (_kdfCacheLock)
            {
                _cachedPass = pass;
                _cachedSalt = new byte[16];
                _cachedEncKey = new byte[32];
                _cachedMacKey = new byte[32];
                Buffer.BlockCopy(salt16, 0, _cachedSalt, 0, 16);
                Buffer.BlockCopy(ek, 0, _cachedEncKey, 0, 32);
                Buffer.BlockCopy(mk, 0, _cachedMacKey, 0, 32);
            }
            encKey = ek;
            macKey = mk;
        }
        public const uint AirGapMagic   = 0x4F414750u; // "OAGP"

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // Self-orthogonal Extended Binary Golay [24, 12, 8] matrices (B * B^T == I_12 mod 2, d_min = 8)
        public static readonly ushort[] GolayB = new ushort[12]
        {
            0x0DC5, 0x06E3, 0x0B71, 0x05B9, 0x02DD, 0x016F,
            0x08B7, 0x0C5B, 0x0E2D, 0x0717, 0x0B8B, 0x0FFE
        };

        public static readonly ushort[] GolayBT = new ushort[12]
        {
            0x0A3B, 0x0D1D, 0x068F, 0x0B47, 0x0DA3, 0x0ED1,
            0x0769, 0x03B5, 0x01DB, 0x08ED, 0x0477, 0x0FFE
        };

        private static readonly uint[] CrcTable = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int b = 0; b < 8; b++)
                    c = ((c & 1u) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return 0u;
            uint crc = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFFu] ^ (crc >> 8);
            return ~crc;
        }

        public static string ComputeSha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data ?? new byte[0]);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++)
                    sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= (a[i] ^ b[i]);
            return diff == 0;
        }

        // =====================================================================
        // 1. EXTENDED BINARY GOLAY [24, 12, 8] ENCODER & 3-BIT ALGEBRAIC DECODER
        // =====================================================================
        public static int PopCount12(int v)
        {
            int x = v & 0x0FFF;
            x = x - ((x >> 1) & 0x5555);
            x = (x & 0x3333) + ((x >> 2) & 0x3333);
            return ((((x + (x >> 4)) & 0x0F0F) * 0x0101) >> 8) & 0x1F;
        }

        private static ushort MulMat12(ushort v12, ushort[] mat)
        {
            ushort res = 0;
            for (int r = 0; r < 12; r++)
            {
                if ((PopCount12(v12 & mat[r]) & 1) != 0)
                    res |= (ushort)(1 << (11 - r));
            }
            return res;
        }

        public static uint Golay24Encode(ushort data12)
        {
            ushort d = (ushort)(data12 & 0x0FFF);
            ushort p = (ushort)(MulMat12(d, GolayBT) & 0x0FFF);
            return ((uint)d << 12) | p;
        }

        public static bool Golay24Decode(uint cw24, out ushort data12, out int correctedBits)
        {
            ushort dRx = (ushort)((cw24 >> 12) & 0x0FFF);
            ushort pRx = (ushort)(cw24 & 0x0FFF);
            ushort s = (ushort)(pRx ^ MulMat12(dRx, GolayBT));
            int ws = PopCount12(s);
            if (ws <= 3)
            {
                data12 = dRx;
                correctedBits = ws;
                return true;
            }
            for (int i = 0; i < 12; i++)
            {
                int w = PopCount12(s ^ GolayB[i]);
                if (w <= 2)
                {
                    ushort eD = (ushort)(1 << (11 - i));
                    data12 = (ushort)((dRx ^ eD) & 0x0FFF);
                    correctedBits = 1 + w;
                    return true;
                }
            }
            ushort sB = MulMat12(s, GolayB);
            int wsB = PopCount12(sB);
            if (wsB <= 3)
            {
                data12 = (ushort)((dRx ^ sB) & 0x0FFF);
                correctedBits = wsB;
                return true;
            }
            for (int j = 0; j < 12; j++)
            {
                int w = PopCount12(sB ^ GolayBT[j]);
                if (w <= 2)
                {
                    ushort eD = (ushort)(sB ^ GolayBT[j]);
                    data12 = (ushort)((dRx ^ eD) & 0x0FFF);
                    correctedBits = 1 + w;
                    return true;
                }
            }
            data12 = dRx;
            correctedBits = 4;
            return false;
        }

        public static uint[] EncodeBytesToGolay24Words(byte[] payload)
        {
            if (payload == null) payload = new byte[0];
            uint crc = ComputeCrc32(payload, 0, payload.Length);
            // Frame header (12 bytes): Magic(4) + ByteLen(4) + CRC32(4) + Payload
            int rawLen = 12 + payload.Length;
            int paddedLen = (rawLen % 3 == 0) ? rawLen : (rawLen + (3 - (rawLen % 3)));
            byte[] frame = new byte[paddedLen];
            Buffer.BlockCopy(BitConverter.GetBytes(AirGapMagic), 0, frame, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, frame, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, frame, 8, 4);
            if (payload.Length > 0) Buffer.BlockCopy(payload, 0, frame, 12, payload.Length);

            int wordPairs = paddedLen / 3;
            uint[] words = new uint[wordPairs * 2];
            int wi = 0;
            for (int i = 0; i < paddedLen; i += 3)
            {
                int b0 = frame[i];
                int b1 = frame[i + 1];
                int b2 = frame[i + 2];
                ushort hi12 = (ushort)((b0 << 4) | (b1 >> 4));
                ushort lo12 = (ushort)(((b1 & 0x0F) << 8) | b2);
                words[wi++] = Golay24Encode(hi12);
                words[wi++] = Golay24Encode(lo12);
            }
            return words;
        }

        public static AirGapDecodeReport DecodeGolay24WordsToBytes(uint[] words)
        {
            AirGapDecodeReport rep = new AirGapDecodeReport();
            if (words == null || words.Length < 8 || (words.Length & 1) != 0)
            {
                rep.Diagnostic = "Invalid codeword count";
                return rep;
            }
            rep.CodewordsTotal = words.Length;
            int byteCap = (words.Length / 2) * 3;
            byte[] decoded = new byte[byteCap];
            int bi = 0;

            for (int i = 0; i < words.Length; i += 2)
            {
                ushort hi12, lo12;
                int c0, c1;
                bool ok0 = Golay24Decode(words[i], out hi12, out c0);
                bool ok1 = Golay24Decode(words[i + 1], out lo12, out c1);
                if (!ok0) rep.UncorrectableWords++;
                else rep.BitFlipsHealed += c0;
                if (!ok1) rep.UncorrectableWords++;
                else rep.BitFlipsHealed += c1;

                decoded[bi++] = (byte)((hi12 >> 4) & 0xFF);
                decoded[bi++] = (byte)(((hi12 & 0x0F) << 4) | ((lo12 >> 8) & 0x0F));
                decoded[bi++] = (byte)(lo12 & 0xFF);
            }

            if (rep.UncorrectableWords > 0)
            {
                rep.Diagnostic = "Fail-closed: >=4-bit uncorrectable codeword detected";
                return rep;
            }

            uint magic = BitConverter.ToUInt32(decoded, 0);
            int payLen = BitConverter.ToInt32(decoded, 4);
            uint wantCrc = BitConverter.ToUInt32(decoded, 8);
            if (magic != AirGapMagic || payLen < 0 || 12 + payLen > decoded.Length)
            {
                rep.Diagnostic = "Envelope header magic/length mismatch";
                return rep;
            }

            byte[] payload = new byte[payLen];
            if (payLen > 0) Buffer.BlockCopy(decoded, 12, payload, 0, payLen);
            uint gotCrc = ComputeCrc32(payload, 0, payLen);
            if (gotCrc != wantCrc)
            {
                rep.Diagnostic = "Outer IEEE CRC-32 mismatch";
                return rep;
            }

            rep.Ok = true;
            rep.Payload = payload;
            rep.DecodedText = Encoding.UTF8.GetString(payload);
            rep.EnvelopeCrc32 = gotCrc;
            rep.Diagnostic = string.Format(CultureInfo.InvariantCulture,
                "OK ({0} bytes, {1} Golay words, {2} bit-flips healed, CRC=0x{3:X8})",
                payLen, words.Length, rep.BitFlipsHealed, gotCrc);
            return rep;
        }

        // =====================================================================
        // 2. CONTENT-ADDRESSED STORAGE (CAS) + ENCRYPT-THEN-MAC AEAD ENVELOPE
        // =====================================================================
        public static byte[] SealAuthenticatedBlob(byte[] plaintext, string passphrase)
        {
            if (plaintext == null) plaintext = new byte[0];
            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            byte[] encKey, macKey;
            DeriveAeadKeysCached(passphrase ?? "omega-local-vault", salt, out encKey, out macKey);

            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    cipher = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }

            // Format: Magic(4="OCAS") + Salt(16) + IV(16) + CipherLen(4) + Cipher + HMAC-SHA256(32)
            int bodyLen = 4 + 16 + 16 + 4 + cipher.Length;
            byte[] packet = new byte[bodyLen + 32];
            packet[0] = (byte)'O'; packet[1] = (byte)'C'; packet[2] = (byte)'A'; packet[3] = (byte)'S';
            Buffer.BlockCopy(salt, 0, packet, 4, 16);
            Buffer.BlockCopy(iv, 0, packet, 20, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(cipher.Length), 0, packet, 36, 4);
            Buffer.BlockCopy(cipher, 0, packet, 40, cipher.Length);

            using (HMACSHA256 hmac = new HMACSHA256(macKey))
            {
                byte[] tag = hmac.ComputeHash(packet, 0, bodyLen);
                Buffer.BlockCopy(tag, 0, packet, bodyLen, 32);
            }
            return packet;
        }

        public static bool UnsealAuthenticatedBlob(byte[] packet, string passphrase, out byte[] plaintext)
        {
            plaintext = null;
            if (packet == null || packet.Length < 72) return false;
            if (packet[0] != (byte)'O' || packet[1] != (byte)'C' || packet[2] != (byte)'A' || packet[3] != (byte)'S')
                return false;

            int cipherLen = BitConverter.ToInt32(packet, 36);
            int bodyLen = 40 + cipherLen;
            if (cipherLen < 0 || bodyLen + 32 != packet.Length) return false;

            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            Buffer.BlockCopy(packet, 4, salt, 0, 16);
            Buffer.BlockCopy(packet, 20, iv, 0, 16);

            byte[] encKey, macKey;
            DeriveAeadKeysCached(passphrase ?? "omega-local-vault", salt, out encKey, out macKey);

            byte[] gotTag = new byte[32];
            Buffer.BlockCopy(packet, bodyLen, gotTag, 0, 32);
            using (HMACSHA256 hmac = new HMACSHA256(macKey))
            {
                byte[] expectedTag = hmac.ComputeHash(packet, 0, bodyLen);
                if (!ConstantTimeEquals(gotTag, expectedTag)) return false;
            }

            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey;
                    aes.IV = iv;
                    using (ICryptoTransform dec = aes.CreateDecryptor())
                    {
                        plaintext = dec.TransformFinalBlock(packet, 40, cipherLen);
                        return true;
                    }
                }
            }
            catch { return false; }
        }

        public static string StoreCasChunk(string casDir, byte[] rawBytes, string passphrase)
        {
            if (rawBytes == null) rawBytes = new byte[0];
            string sha = ComputeSha256Hex(rawBytes);
            Directory.CreateDirectory(casDir);
            string finalPath = Path.Combine(casDir, sha + ".ocas");
            if (!File.Exists(finalPath))
            {
                string tmpPath = finalPath + ".tmp";
                byte[] sealedBytes = SealAuthenticatedBlob(rawBytes, passphrase);
                using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(sealedBytes, 0, sealedBytes.Length);
                    fs.Flush(true);
                }
                if (File.Exists(finalPath)) File.Delete(tmpPath);
                else File.Move(tmpPath, finalPath);
            }
            return sha;
        }

        // =====================================================================
        // 3. DUAL-SLOT (A/B) SEQLOCK + IEEE CRC-32 POWER-LOSS ATOMIC WAL
        // =====================================================================
        public static byte[] SerializeVaultSnapshot(List<VaultEntryRecord> entries)
        {
            StringBuilder sb = new StringBuilder();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    VaultEntryRecord e = entries[i];
                    string b64Key = Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Key ?? ""));
                    string b64Val = Convert.ToBase64String(Encoding.UTF8.GetBytes(e.ValueText ?? ""));
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "{0}|{1}|{2}|{3}|{4}|{5}|{6}\n",
                        b64Key, b64Val, e.CasSha256 ?? "", e.WallMs, e.LogicalSeq, e.NodeId ?? "LOCAL", e.Tombstone ? 1 : 0);
                }
            }
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        public static List<VaultEntryRecord> DeserializeVaultSnapshot(byte[] bytes)
        {
            List<VaultEntryRecord> list = new List<VaultEntryRecord>();
            if (bytes == null || bytes.Length == 0) return list;
            string txt = Encoding.UTF8.GetString(bytes);
            string[] lines = txt.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] p = lines[i].Split('|');
                if (p.Length < 7) continue;
                try
                {
                    VaultEntryRecord r = new VaultEntryRecord();
                    r.Key = Encoding.UTF8.GetString(Convert.FromBase64String(p[0]));
                    r.ValueText = Encoding.UTF8.GetString(Convert.FromBase64String(p[1]));
                    r.CasSha256 = p[2];
                    long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.WallMs);
                    uint.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.LogicalSeq);
                    r.NodeId = p[5];
                    r.Tombstone = (p[6] == "1");
                    r.ByteSize = Encoding.UTF8.GetByteCount(r.ValueText);
                    list.Add(r);
                }
                catch { }
            }
            return list;
        }

        // Binary WAL Slot Layout (24-byte header + payload):
        //   [0..3]   uint32 Magic = 0x4F564C54 ("OVLT")
        //   [4..11]  uint64 SeqlockGen (ODD while writing, EVEN >= 2 when committed!)
        //   [12..15] int32  PayloadLen
        //   [16..19] uint32 PayloadCrc32
        //   [20..23] uint32 HeaderCrc32 (CRC-32 over bytes [0..19])
        //   [24..]   Payload bytes
        public static void CommitDualSlotWal(string walDir, ref ulong currentEvenGen, List<VaultEntryRecord> entries)
        {
            CommitDualSlotWalEx(walDir, ref currentEvenGen, entries, true);
        }

        public static void CommitDualSlotWalEx(string walDir, ref ulong currentEvenGen, List<VaultEntryRecord> entries, bool flushHardwareDisk)
        {
            Directory.CreateDirectory(walDir);
            string slotA = Path.Combine(walDir, "vault_slot_a.wal");
            string slotB = Path.Combine(walDir, "vault_slot_b.wal");

            ulong nextEvenGen = currentEvenGen + 2UL;
            ulong oddInFlightGen = nextEvenGen - 1UL;
            string targetPath = (((nextEvenGen / 2UL) & 1UL) == 1UL) ? slotA : slotB;

            byte[] payload = SerializeVaultSnapshot(entries);
            uint payCrc = ComputeCrc32(payload, 0, payload.Length);

            using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // Phase 1: Write ODD Seqlock generation (marks write in progress if power fails!)
                byte[] hdrOdd = BuildWalHeader(oddInFlightGen, payload.Length, payCrc);
                fs.Write(hdrOdd, 0, 24);
                if (payload.Length > 0) fs.Write(payload, 0, payload.Length);
                fs.Flush(flushHardwareDisk);

                // Phase 2: Hardware barrier complete -> stamp EVEN committed Seqlock generation!
                byte[] hdrEven = BuildWalHeader(nextEvenGen, payload.Length, payCrc);
                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(hdrEven, 0, 24);
                fs.Flush(flushHardwareDisk);
            }
            currentEvenGen = nextEvenGen;
        }

        private static byte[] BuildWalHeader(ulong seqGen, int payLen, uint payCrc)
        {
            byte[] h = new byte[24];
            Buffer.BlockCopy(BitConverter.GetBytes(VaultWalMagic), 0, h, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(seqGen), 0, h, 4, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(payLen), 0, h, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(payCrc), 0, h, 16, 4);
            uint hCrc = ComputeCrc32(h, 0, 20);
            Buffer.BlockCopy(BitConverter.GetBytes(hCrc), 0, h, 20, 4);
            return h;
        }

        public static string InjectSimulatedTornSlot(string walDir, ulong currentEvenGen)
        {
            return InjectSimulatedTornSlotEx(walDir, currentEvenGen, true);
        }

        public static string InjectSimulatedTornSlotEx(string walDir, ulong currentEvenGen, bool flushHardwareDisk)
        {
            Directory.CreateDirectory(walDir);
            ulong tornOddGen = currentEvenGen + 1UL;
            bool tearB = (((currentEvenGen / 2UL) & 1UL) == 1UL);
            string victimPath = Path.Combine(walDir, tearB ? "vault_slot_b.wal" : "vault_slot_a.wal");
            byte[] halfPayload = Encoding.UTF8.GetBytes("TORN_PARTIAL_FLASH_PAGE_CORRUPT_MID_SAVE");
            byte[] hdrOdd = BuildWalHeader(tornOddGen, halfPayload.Length + 64, 0xDEADBEEFu);
            using (FileStream fs = new FileStream(victimPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(hdrOdd, 0, 24);
                fs.Write(halfPayload, 0, halfPayload.Length);
                fs.Flush(flushHardwareDisk);
            }
            return tearB ? "Slot B" : "Slot A";
        }

        private static bool TryVerifyWalSlot(string path, out ulong seqGen, out byte[] payload, out string note)
        {
            seqGen = 0UL;
            payload = null;
            note = "missing";
            if (!File.Exists(path)) return false;
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length < 24) { note = "truncated_header"; return false; }
                uint magic = BitConverter.ToUInt32(raw, 0);
                ulong gen = BitConverter.ToUInt64(raw, 4);
                int payLen = BitConverter.ToInt32(raw, 12);
                uint wantPayCrc = BitConverter.ToUInt32(raw, 16);
                uint wantHdrCrc = BitConverter.ToUInt32(raw, 20);
                uint gotHdrCrc = ComputeCrc32(raw, 0, 20);

                seqGen = gen;
                if (magic != VaultWalMagic || gotHdrCrc != wantHdrCrc) { note = "corrupt_header_crc"; return false; }
                if (gen == 0UL || (gen & 1UL) != 0UL) { note = "torn_odd_seqlock_gen_" + gen.ToString(CultureInfo.InvariantCulture); return false; }
                if (payLen < 0 || 24 + payLen != raw.Length) { note = "torn_truncated_payload"; return false; }

                byte[] p = new byte[payLen];
                if (payLen > 0) Buffer.BlockCopy(raw, 24, p, 0, payLen);
                uint gotPayCrc = ComputeCrc32(p, 0, payLen);
                if (gotPayCrc != wantPayCrc) { note = "corrupt_payload_crc32"; return false; }

                payload = p;
                note = "COMMITTED_EVEN_GEN_" + gen.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                note = "io_err:" + ex.Message;
                return false;
            }
        }

        public static WalRecoveryReport RecoverDualSlotWal(string walDir)
        {
            WalRecoveryReport rep = new WalRecoveryReport();
            string slotA = Path.Combine(walDir, "vault_slot_a.wal");
            string slotB = Path.Combine(walDir, "vault_slot_b.wal");

            byte[] payA, payB;
            rep.SlotAValid = TryVerifyWalSlot(slotA, out rep.SlotAGen, out payA, out rep.SlotANote);
            rep.SlotBValid = TryVerifyWalSlot(slotB, out rep.SlotBGen, out payB, out rep.SlotBNote);

            byte[] chosenBytes = null;
            if (rep.SlotAValid && rep.SlotBValid)
            {
                if (rep.SlotBGen >= rep.SlotAGen)
                {
                    rep.ChosenSlot = "SLOT-B";
                    rep.CommittedGen = rep.SlotBGen;
                    chosenBytes = payB;
                }
                else
                {
                    rep.ChosenSlot = "SLOT-A";
                    rep.CommittedGen = rep.SlotAGen;
                    chosenBytes = payA;
                }
                rep.Ok = true;
            }
            else if (rep.SlotAValid)
            {
                rep.ChosenSlot = "SLOT-A (Healed Torn B)";
                rep.CommittedGen = rep.SlotAGen;
                chosenBytes = payA;
                rep.Ok = true;
            }
            else if (rep.SlotBValid)
            {
                rep.ChosenSlot = "SLOT-B (Healed Torn A)";
                rep.CommittedGen = rep.SlotBGen;
                chosenBytes = payB;
                rep.Ok = true;
            }

            if (rep.Ok && chosenBytes != null)
            {
                rep.RootSha256 = ComputeSha256Hex(chosenBytes);
                rep.Entries = DeserializeVaultSnapshot(chosenBytes);
            }
            return rep;
        }

        // =====================================================================
        // 4. COMMUTATIVE / ASSOCIATIVE / IDEMPOTENT 3-NODE HLC CRDT MERGE
        // =====================================================================
        public static VaultEntryRecord ChooseCrdtWinner(VaultEntryRecord a, VaultEntryRecord b)
        {
            if (a == null) return CloneEntry(b);
            if (b == null) return CloneEntry(a);

            if (a.WallMs != b.WallMs) return (b.WallMs > a.WallMs) ? CloneEntry(b) : CloneEntry(a);
            if (a.LogicalSeq != b.LogicalSeq) return (b.LogicalSeq > a.LogicalSeq) ? CloneEntry(b) : CloneEntry(a);
            int cmpNode = string.CompareOrdinal(b.NodeId ?? "", a.NodeId ?? "");
            if (cmpNode != 0) return (cmpNode > 0) ? CloneEntry(b) : CloneEntry(a);
            if (a.Tombstone != b.Tombstone) return b.Tombstone ? CloneEntry(b) : CloneEntry(a);
            int cmpSha = string.CompareOrdinal(b.CasSha256 ?? "", a.CasSha256 ?? "");
            if (cmpSha != 0) return (cmpSha > 0) ? CloneEntry(b) : CloneEntry(a);
            int cmpVal = string.CompareOrdinal(b.ValueText ?? "", a.ValueText ?? "");
            return (cmpVal > 0) ? CloneEntry(b) : CloneEntry(a);
        }

        public static List<VaultEntryRecord> MergeCrdtReplicas(List<VaultEntryRecord> left, List<VaultEntryRecord> right)
        {
            Dictionary<string, VaultEntryRecord> map = new Dictionary<string, VaultEntryRecord>(StringComparer.Ordinal);
            if (left != null)
            {
                for (int i = 0; i < left.Count; i++)
                {
                    VaultEntryRecord e = left[i];
                    VaultEntryRecord cur;
                    if (map.TryGetValue(e.Key, out cur)) map[e.Key] = ChooseCrdtWinner(cur, e);
                    else map[e.Key] = CloneEntry(e);
                }
            }
            if (right != null)
            {
                for (int i = 0; i < right.Count; i++)
                {
                    VaultEntryRecord e = right[i];
                    VaultEntryRecord cur;
                    if (map.TryGetValue(e.Key, out cur)) map[e.Key] = ChooseCrdtWinner(cur, e);
                    else map[e.Key] = CloneEntry(e);
                }
            }

            List<string> keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            List<VaultEntryRecord> merged = new List<VaultEntryRecord>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                merged.Add(map[keys[i]]);
            return merged;
        }

        private static VaultEntryRecord CloneEntry(VaultEntryRecord s)
        {
            if (s == null) return null;
            VaultEntryRecord c = new VaultEntryRecord();
            c.Key = s.Key;
            c.ValueText = s.ValueText;
            c.CasSha256 = s.CasSha256;
            c.WallMs = s.WallMs;
            c.LogicalSeq = s.LogicalSeq;
            c.NodeId = s.NodeId;
            c.Tombstone = s.Tombstone;
            c.ByteSize = s.ByteSize;
            return c;
        }

        // =====================================================================
        // 5A. 18.5 kHz / 19.3 kHz ULTRASONIC BFSK .WAV MODEM + GOERTZEL DECODER
        // =====================================================================
        public const int AudioSampleRate = 48000;
        public const int SamplesPerBit   = 96; // 2.0 ms per bit -> 500 bps ultrasonic air-gap burst
        public const double FreqBit0Hz   = 18500.0;
        public const double FreqBit1Hz   = 19300.0;

        public static byte[] EncodeUltrasonicBfskWav(byte[] payload)
        {
            uint[] words = EncodeBytesToGolay24Words(payload);
            int totalBits = words.Length * 24;
            int totalSamples = totalBits * SamplesPerBit;
            short[] pcm = new short[totalSamples];

            int sIdx = 0;
            for (int w = 0; w < words.Length; w++)
            {
                uint cw = words[w];
                for (int b = 23; b >= 0; b--)
                {
                    bool one = ((cw >> b) & 1u) != 0u;
                    double freq = one ? FreqBit1Hz : FreqBit0Hz;
                    double omega = 2.0 * Math.PI * freq / AudioSampleRate;
                    for (int n = 0; n < SamplesPerBit; n++)
                    {
                        // Raised-cosine edge taper eliminates audible speaker clicks!
                        double env = 1.0;
                        if (n < 8) env = 0.5 * (1.0 - Math.Cos(Math.PI * n / 8.0));
                        else if (n >= SamplesPerBit - 8) env = 0.5 * (1.0 - Math.Cos(Math.PI * (SamplesPerBit - 1 - n) / 8.0));
                        double val = Math.Sin(omega * n) * env * 22000.0;
                        pcm[sIdx++] = (short)Math.Max(-32760, Math.Min(32760, (int)Math.Round(val)));
                    }
                }
            }

            int dataBytes = totalSamples * 2;
            byte[] wav = new byte[44 + dataBytes];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            Buffer.BlockCopy(BitConverter.GetBytes(36 + dataBytes), 0, wav, 4, 4);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(16), 0, wav, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)1), 0, wav, 20, 2); // PCM
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)1), 0, wav, 22, 2); // Mono
            Buffer.BlockCopy(BitConverter.GetBytes(AudioSampleRate), 0, wav, 24, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(AudioSampleRate * 2), 0, wav, 28, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)2), 0, wav, 32, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)16), 0, wav, 34, 2);
            Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            Buffer.BlockCopy(BitConverter.GetBytes(dataBytes), 0, wav, 40, 4);
            Buffer.BlockCopy(pcm, 0, wav, 44, dataBytes);
            return wav;
        }

        private static double GoertzelMagSq(short[] pcm, int offset, int count, double targetHz)
        {
            double omega = 2.0 * Math.PI * targetHz / AudioSampleRate;
            double coeff = 2.0 * Math.Cos(omega);
            double s0 = 0.0, s1 = 0.0, s2 = 0.0;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                s0 = (pcm[i] / 32768.0) + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            return s1 * s1 + s2 * s2 - coeff * s1 * s2;
        }

        public static AirGapDecodeReport DecodeUltrasonicBfskWav(byte[] wavBytes, int injectBitFlipsPerWord)
        {
            AirGapDecodeReport rep = new AirGapDecodeReport();
            if (wavBytes == null || wavBytes.Length < 44)
            {
                rep.Diagnostic = "Invalid WAV buffer";
                return rep;
            }
            int dataBytes = wavBytes.Length - 44;
            int totalSamples = dataBytes / 2;
            int totalBits = totalSamples / SamplesPerBit;
            int wordCount = totalBits / 24;
            if (wordCount < 8)
            {
                rep.Diagnostic = "WAV too short for Golay frame";
                return rep;
            }

            short[] pcm = new short[totalSamples];
            Buffer.BlockCopy(wavBytes, 44, pcm, 0, totalSamples * 2);

            uint[] words = new uint[wordCount];
            int bitCursor = 0;
            for (int w = 0; w < wordCount; w++)
            {
                uint cw = 0u;
                for (int b = 23; b >= 0; b--)
                {
                    int off = bitCursor * SamplesPerBit;
                    double e0 = GoertzelMagSq(pcm, off, SamplesPerBit, FreqBit0Hz);
                    double e1 = GoertzelMagSq(pcm, off, SamplesPerBit, FreqBit1Hz);
                    if (e1 > e0) cw |= (1u << b);
                    bitCursor++;
                }
                for (int f = 0; f < injectBitFlipsPerWord && f < 3; f++)
                {
                    int flipBit = (w * 5 + f * 7) % 24;
                    cw ^= (1u << flipBit);
                }
                words[w] = cw;
            }
            return DecodeGolay24WordsToBytes(words);
        }

        // =====================================================================
        // 5B. LOSSLESS 24D LATTICE VISUAL VAULT .PNG CARRIER + DECODER
        // =====================================================================
        public static byte[] EncodeLatticeVaultPng(byte[] payload)
        {
            uint[] words = EncodeBytesToGolay24Words(payload);
            // Prepend 2 Golay words storing total word count so decoder knows exact frame boundary!
            int totalWords = words.Length + 2;
            uint[] full = new uint[totalWords];
            full[0] = Golay24Encode((ushort)((words.Length >> 12) & 0x0FFF));
            full[1] = Golay24Encode((ushort)(words.Length & 0x0FFF));
            Array.Copy(words, 0, full, 2, words.Length);

            using (Bitmap bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        double dx = (x - 128.0) / 128.0;
                        double dy = (y - 128.0) / 128.0;
                        double r = Math.Sqrt(dx * dx + dy * dy);
                        double ang = Math.Atan2(dy, dx);
                        int cr = (int)(36 + 70 * (0.5 + 0.5 * Math.Cos(12.0 * r - 4.0 * ang))) & 0xFC;
                        int cg = (int)(64 + 120 * (0.5 + 0.5 * Math.Sin(8.0 * ang + 10.0 * r))) & 0xFC;
                        int cb = (int)(92 + 120 * (0.5 + 0.5 * Math.Cos(16.0 * r))) & 0xFC;
                        bmp.SetPixel(x, y, Color.FromArgb(255, cr, cg, cb));
                    }
                }

                // Embed 24-bit Golay codeword across 4 consecutive pixels (6 bits/pixel: 2 bits R, 2 bits G, 2 bits B)
                int pixIdx = 0;
                for (int w = 0; w < full.Length && pixIdx + 3 < 65536; w++)
                {
                    uint cw = full[w];
                    for (int p = 0; p < 4; p++)
                    {
                        int shift = (3 - p) * 6;
                        int six = (int)((cw >> shift) & 0x3Fu);
                        int px = pixIdx & 255;
                        int py = pixIdx >> 8;
                        Color c = bmp.GetPixel(px, py);
                        int nr = (c.R & 0xFC) | ((six >> 4) & 0x03);
                        int ng = (c.G & 0xFC) | ((six >> 2) & 0x03);
                        int nb = (c.B & 0xFC) | (six & 0x03);
                        bmp.SetPixel(px, py, Color.FromArgb(255, nr, ng, nb));
                        pixIdx++;
                    }
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        public static AirGapDecodeReport DecodeLatticeVaultPng(byte[] pngBytes, int injectBitFlipsPerWord)
        {
            AirGapDecodeReport rep = new AirGapDecodeReport();
            if (pngBytes == null || pngBytes.Length < 64)
            {
                rep.Diagnostic = "Invalid PNG bytes";
                return rep;
            }
            using (MemoryStream ms = new MemoryStream(pngBytes))
            using (Bitmap bmp = new Bitmap(ms))
            {
                if (bmp.Width < 256 || bmp.Height < 256)
                {
                    rep.Diagnostic = "Unexpected PNG dimensions";
                    return rep;
                }

                uint lenHiCw = ReadGolayWordFromBitmap(bmp, 0);
                uint lenLoCw = ReadGolayWordFromBitmap(bmp, 4);
                ushort lenHi, lenLo;
                int c0, c1;
                if (!Golay24Decode(lenHiCw, out lenHi, out c0) || !Golay24Decode(lenLoCw, out lenLo, out c1))
                {
                    rep.Diagnostic = "PNG length header uncorrectable";
                    return rep;
                }
                int wordCount = ((int)lenHi << 12) | lenLo;
                if (wordCount < 8 || (wordCount + 2) * 4 > 65536)
                {
                    rep.Diagnostic = "Invalid embedded Golay word count";
                    return rep;
                }

                uint[] words = new uint[wordCount];
                for (int w = 0; w < wordCount; w++)
                {
                    uint cw = ReadGolayWordFromBitmap(bmp, (w + 2) * 4);
                    for (int f = 0; f < injectBitFlipsPerWord && f < 3; f++)
                    {
                        int flipBit = (w * 3 + f * 9) % 24;
                        cw ^= (1u << flipBit);
                    }
                    words[w] = cw;
                }
                return DecodeGolay24WordsToBytes(words);
            }
        }

        private static uint ReadGolayWordFromBitmap(Bitmap bmp, int startPixel)
        {
            uint cw = 0u;
            for (int p = 0; p < 4; p++)
            {
                int idx = startPixel + p;
                Color c = bmp.GetPixel(idx & 255, idx >> 8);
                uint six = (uint)(((c.R & 3) << 4) | ((c.G & 3) << 2) | (c.B & 3));
                cw = (cw << 6) | six;
            }
            return cw;
        }

        // =====================================================================
        // 6. SUPPLY-CHAIN DEPENDENCY, LOCKFILE & TROJAN-SOURCE FIREWALL
        // =====================================================================
        public static SupplyChainScanReport ScanManifestForSupplyChainRisks(string rawText)
        {
            SupplyChainScanReport rep = new SupplyChainScanReport();
            if (string.IsNullOrEmpty(rawText))
            {
                rep.Clean = true;
                return rep;
            }
            if (rawText[0] == '\uFEFF') rep.HadUtf8Bom = true;

            for (int i = 0; i < rawText.Length; i++)
            {
                char ch = rawText[i];
                if ((ch >= '\u202A' && ch <= '\u202E') || (ch >= '\u2066' && ch <= '\u2069') || ch == '\u200B')
                    rep.BidiTrojanCount++;
            }
            if (rep.HadUtf8Bom)
            {
                AddFinding(rep, "MEDIUM", "SC-BOM-01", 1, "UTF-8 BOM (U+FEFF)", "Leading BOM breaks POSIX shebangs & deterministic lockfile hashes");
            }
            if (rep.BidiTrojanCount > 0)
            {
                AddFinding(rep, "CRITICAL", "SC-BIDI-02", 1, "Trojan-Source Bidi/ZWSP", "Invisible Unicode directional override detected (CVE-2021-42574)");
            }

            string[] lines = rawText.Split(new char[] { '\n' });
            rep.LinesScanned = lines.Length;

            string[] typosquats = new string[] { "reqeusts", "urllib4", "colros", "lodahs", "crossenv", "electorn", "pyhton", "react-domm" };

            for (int idx = 0; idx < lines.Length; idx++)
            {
                string ln = lines[idx].Trim('\r', ' ', '\t');
                string low = ln.ToLowerInvariant();
                int lineNo = idx + 1;

                if (Regex.IsMatch(low, "\"(preinstall|install|postinstall|prepare)\"\\s*:"))
                {
                    if (low.Contains("curl ") || low.Contains("wget ") || low.Contains("powershell") ||
                        low.Contains("invoke-webrequest") || low.Contains("iex ") || low.Contains("eval(") ||
                        low.Contains("base64") || low.Contains("/bin/sh") || low.Contains("nc "))
                    {
                        AddFinding(rep, "CRITICAL", "SC-HOOK-RCE", lineNo, ln, "Lifecycle install hook executes remote shell / encoded payload");
                    }
                    else
                    {
                        AddFinding(rep, "HIGH", "SC-HOOK-EXEC", lineNo, ln, "Lifecycle install script runs arbitrary code during package install");
                    }
                }

                if (Regex.IsMatch(low, "\"[^\"]+\"\\s*:\\s*\"(\\*|latest|next)\"") ||
                    Regex.IsMatch(low, "^[a-z0-9_\\-]+\\s*$"))
                {
                    if (low.Contains("\"*\"") || low.Contains("\"latest\"") || low.Contains("\"next\""))
                        AddFinding(rep, "HIGH", "SC-UNPINNED", lineNo, ln, "Unpinned wildcard/floating version exposes build to registry takeover");
                }

                if (low.Contains("git+http://") || low.Contains("http://") ||
                    (low.Contains(".tgz") && low.Contains("https://")) || low.Contains("git+https://"))
                {
                    AddFinding(rep, "HIGH", "SC-URL-DEP", lineNo, ln, "Mutable remote tarball/git URL bypasses immutable registry checksums");
                }

                for (int t = 0; t < typosquats.Length; t++)
                {
                    if (low.Contains("\"" + typosquats[t] + "\"") || low.StartsWith(typosquats[t], StringComparison.Ordinal))
                    {
                        AddFinding(rep, "CRITICAL", "SC-TYPOSQUAT", lineNo, ln, "Known typosquat / malicious lookalike package name: " + typosquats[t]);
                    }
                }
            }

            rep.Clean = (rep.Findings.Count == 0);
            return rep;
        }

        private static void AddFinding(SupplyChainScanReport r, string sev, string rule, int ln, string snip, string why)
        {
            SupplyChainFinding f = new SupplyChainFinding();
            f.Severity = sev;
            f.RuleId = rule;
            f.LineNumber = ln;
            f.Snippet = (snip != null && snip.Length > 96) ? (snip.Substring(0, 96) + "...") : (snip ?? "");
            f.Reason = why;
            r.Findings.Add(f);
        }

        // =====================================================================
        // 7. ONE-CLICK USB ADB VAULT CAPSULE SYNC TO PIXEL 10 & PIXEL 8
        // =====================================================================
        public static string SyncVaultSnapshotToConnectedPixels(string vaultDir, List<VaultEntryRecord> entries, string passphrase)
        {
            string adbExe = ResolveAdbExe();
            if (string.IsNullOrEmpty(adbExe))
                return "adb.exe not found on PATH/SDK (local vault & air-gapWAV/PNG ready)";

            byte[] rawSnap = SerializeVaultSnapshot(entries);
            byte[] sealedSnap = SealAuthenticatedBlob(rawSnap, passphrase);
            Directory.CreateDirectory(vaultDir);
            string localCap = Path.Combine(vaultDir, "omega_vault_capsule.ocas");
            File.WriteAllBytes(localCap, sealedSnap);

            string devsOut = RunQuickProc(adbExe, "devices", 1500);
            string[] lines = devsOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int synced = 0;
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.StartsWith("List of", StringComparison.Ordinal) || ln.StartsWith("*", StringComparison.Ordinal)) continue;
                string[] tok = ln.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length >= 2 && tok[1] == "device")
                {
                    string ser = tok[0];
                    RunQuickProc(adbExe, "-s " + ser + " shell mkdir -p /sdcard/Download/omega_vault_sync", 1500);
                    RunQuickProc(adbExe, "-s " + ser + " push \"" + localCap + "\" /sdcard/Download/omega_vault_sync/omega_vault_capsule.ocas", 4000);
                    synced++;
                    sb.Append(ser).Append(' ');
                }
            }

            if (synced == 0) return "0 online ADB devices connected (sealed capsule staged locally at " + localCap + ")";
            return string.Format(CultureInfo.InvariantCulture,
                "Synced AES-256+HMAC vault capsule ({0} bytes) to {1} USB Pixel(s): {2}",
                sealedSnap.Length, synced, sb.ToString().Trim());
        }

        private static string ResolveAdbExe()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string p1 = Path.Combine(localApp, "Android", "Sdk", "platform-tools", "adb.exe");
            if (File.Exists(p1)) return p1;
            string[] dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            for (int i = 0; i < dirs.Length; i++)
            {
                if (string.IsNullOrEmpty(dirs[i])) continue;
                try
                {
                    string cand = Path.Combine(dirs[i].Trim(), "adb.exe");
                    if (File.Exists(cand)) return cand;
                }
                catch { }
            }
            return "";
        }

        private static string RunQuickProc(string exe, string args, int timeoutMs)
        {
            Process p = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p = Process.Start(psi);
                if (p == null) return "";
                System.Threading.Tasks.Task<string> oTask = p.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> eTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                p.WaitForExit();
                string ign = eTask.Result;
                if (ign == null) ign = "";
                return oTask.Result ?? "";
            }
            catch { return ""; }
            finally { if (p != null) { try { p.Dispose(); } catch { } } }
        }

        public static double TrimWorkingSetMb()
        {
            Process self = Process.GetCurrentProcess();
            try { EmptyWorkingSet(self.Handle); self.Refresh(); } catch { }
            return self.WorkingSet64 / (1024.0 * 1024.0);
        }
    }
}
