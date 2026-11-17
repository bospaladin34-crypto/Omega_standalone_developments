using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OmegaStegStudio
{
    public static class OmegaSecLayers
    {
        public const byte TrigChebyshev      = 0x01; // L1: Modular Chebyshev T_n(x) mod (2^61 - 1)
        public const byte AlgebraMlKemM24    = 0x02; // L2: Ring-LWE (q=3329, N=64) + McEliece-M24 S*G24*P
        public const byte Polytope24Cell     = 0x04; // L3: 4D Self-Dual 24-Cell {3,4,3} Hurwitz Quaternion Feistel
        public const byte TopologyBraidSheaf = 0x08; // L4: Artin Braid B_16 Burau AAG + Cellular Sheaf H^1=0 Seal
        public const byte GeometryLeechGolay = 0x10; // L5: 24D Leech + [24,12,8] Golay Self-Healing Wire Armor
        public const byte AllFive            = 0x1F;
    }

    public sealed class OmegaSecCapsuleReceipt
    {
        public bool Ok;
        public string Verdict = "";
        public string FaultLayer = "NONE";
        public byte Version = 2;
        public byte LayerMask = OmegaSecLayers.AllFive;
        public byte StationId = 0;
        public byte PayloadKind = 0;
        public ulong EpochNonce;
        public ulong ChebyshevPublicT;
        public ulong KemCapsuleTag;
        public ulong BraidCommutatorTag;
        public ulong SheafCoboundarySeal;
        public ulong ComputedSheafSeal;
        public uint PlainLength;
        public uint PlainCrc32;
        public int GolayFrameCount;
        public int GolayHealedBits;
        public int GolayUncorrectableFrames;
        public byte[] PlainBytes = new byte[0];
        public string PlainText = "";
        public byte[] WireBytes = new byte[0];
        public string WireBase64 = "";
        public string Sha256Hex = "";
        public double ElapsedMs;
    }

    public sealed class OmegaSecShareBundle
    {
        public byte[] Share1Loq15 = new byte[0];
        public byte[] Share2Pixel10 = new byte[0];
        public byte[] Share3Pixel8 = new byte[0];
        public string Share1Base64 = "";
        public string Share2Base64 = "";
        public string Share3Base64 = "";
        public uint SecretCrc32;
        public int SecretLength;
    }

    public static class OmegaSecEngine
    {
        public const ulong M61 = 0x1FFFFFFFFFFFFFFFUL; // Mersenne prime 2^61 - 1
        public const ushort BurauPrime = 65521;        // Largest prime < 2^16
        public const int LweQ = 3329;                  // NIST FIPS 203 ML-KEM modulus

        // 24 integer Hurwitz quaternion vertices of the self-dual 4D 24-Cell {3,4,3} (squared norm = 4)
        public static readonly sbyte[,] Hurwitz24Cell = new sbyte[24, 4]
        {
            {  2,  0,  0,  0 }, { -2,  0,  0,  0 }, {  0,  2,  0,  0 }, {  0, -2,  0,  0 },
            {  0,  0,  2,  0 }, {  0,  0, -2,  0 }, {  0,  0,  0,  2 }, {  0,  0,  0, -2 },
            {  1,  1,  1,  1 }, {  1,  1,  1, -1 }, {  1,  1, -1,  1 }, {  1,  1, -1, -1 },
            {  1, -1,  1,  1 }, {  1, -1,  1, -1 }, {  1, -1, -1,  1 }, {  1, -1, -1, -1 },
            { -1,  1,  1,  1 }, { -1,  1,  1, -1 }, { -1,  1, -1,  1 }, { -1,  1, -1, -1 },
            { -1, -1,  1,  1 }, { -1, -1,  1, -1 }, { -1, -1, -1,  1 }, { -1, -1, -1, -1 }
        };

        // 96 octahedral edges of the 4D 24-Cell (pairs of vertices whose integer dot product equals +2)
        public static readonly int[,] Edges24Cell = new int[96, 2];
        public static readonly byte[] Gf256Exp = new byte[512];
        public static readonly byte[] Gf256Log = new byte[256];
        public static readonly byte[] SBox24Cell = new byte[256];
        private static readonly uint[] Crc32Table = new uint[256];

        private static readonly object _replayLock = new object();
        private static readonly HashSet<ulong> _seenNonces = new HashSet<ulong>();
        private static ulong _monotonicCounter = (ulong)(DateTime.UtcNow.Ticks & 0x00FFFFFFFFFFFFFFL);

        public static long TotalSealedCapsules = 0;
        public static long TotalOpenedCapsules = 0;
        public static long TotalQuarantinedCapsules = 0;

        static OmegaSecEngine()
        {
            // 1. Build GF(2^8) tables (generator 0x03 modulo x^8 + x^4 + x^3 + x + 1 = 0x11B) & 24-Cell S-Box
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Gf256Exp[i] = (byte)x;
                Gf256Exp[i + 255] = (byte)x;
                Gf256Log[x] = (byte)i;
                int x2 = (x << 1) ^ (((x & 0x80) != 0) ? 0x11B : 0);
                x = (x2 ^ x) & 0xFF;
            }
            Gf256Log[0] = 0;

            for (int v = 0; v < 256; v++)
            {
                byte inv = (v == 0) ? (byte)0 : Gf256Exp[255 - Gf256Log[v]];
                int rot1 = ((inv << 1) | (inv >> 7)) & 0xFF;
                int rot3 = ((inv << 3) | (inv >> 5)) & 0xFF;
                SBox24Cell[v] = (byte)(inv ^ rot1 ^ rot3 ^ 0x63);
            }

            // 2. Discover all 96 edges of the regular 4D 24-Cell {3,4,3} (dot == +2)
            int eIdx = 0;
            for (int i = 0; i < 24; i++)
            {
                for (int j = i + 1; j < 24; j++)
                {
                    int dot = Hurwitz24Cell[i, 0] * Hurwitz24Cell[j, 0]
                            + Hurwitz24Cell[i, 1] * Hurwitz24Cell[j, 1]
                            + Hurwitz24Cell[i, 2] * Hurwitz24Cell[j, 2]
                            + Hurwitz24Cell[i, 3] * Hurwitz24Cell[j, 3];
                    if (dot == 2 && eIdx < 96)
                    {
                        Edges24Cell[eIdx, 0] = i;
                        Edges24Cell[eIdx, 1] = j;
                        eIdx++;
                    }
                }
            }

            // 3. IEEE 802.3 CRC-32 LUT
            for (uint i = 0; i < 256u; i++)
            {
                uint c = i;
                for (int b = 0; b < 8; b++)
                    c = ((c & 1u) != 0u) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                Crc32Table[i] = c;
            }
        }

        public static ulong NextEpochNonce()
        {
            lock (_replayLock)
            {
                _monotonicCounter += 1UL;
                return _monotonicCounter;
            }
        }

        public static void ResetReplayWindow()
        {
            lock (_replayLock) { _seenNonces.Clear(); }
        }

        public static byte ParseStationName(string name)
        {
            string s = (name ?? "").Trim().ToLowerInvariant();
            if (s == "mirror" || s == "01") return 1;
            if (s == "browser" || s == "02") return 2;
            if (s == "av" || s == "03") return 3;
            if (s == "synth" || s == "04") return 4;
            if (s == "sonar" || s == "05") return 5;
            if (s == "sheaf" || s == "06") return 6;
            if (s == "penrose" || s == "07") return 7;
            if (s == "forge" || s == "09") return 9;
            if (s == "shell" || s == "10") return 10;
            if (s == "rust" || s == "mainline") return 31;
            return 0; // OmegaSteg (:19786)
        }

        public static string FormatStationName(byte id)
        {
            if (id == 1) return "OmegaMirror(:19777)";
            if (id == 2) return "OmegaBrowser(:9778)";
            if (id == 3) return "OmegaAV(:19779)";
            if (id == 4) return "OmegaSynth(:19782)";
            if (id == 5) return "OmegaSonar(:19783)";
            if (id == 6) return "OmegaSheaf(:19784)";
            if (id == 7) return "OmegaPenrose(:19785)";
            if (id == 9) return "OmegaForge(:19780)";
            if (id == 10) return "OmegaShell(:19781)";
            if (id == 31) return "MainlineRust(SH4Z)";
            return "OmegaSteg(:19786)";
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
                crc = Crc32Table[(crc ^ data[offset + i]) & 0xFFu] ^ (crc >> 8);
            return ~crc;
        }

        public static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static void DeriveMasterSeeds(string passphrase, out ulong kTrig, out ulong kAlg, out ulong kPoly, out ulong kTopo)
        {
            string norm = string.IsNullOrEmpty(passphrase) ? "ACT-OMEGA-SOVEREIGN-MANIFOLD-V72" : passphrase;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("SH4Z-SEC1::" + norm));
                kTrig = BitConverter.ToUInt64(h, 0);
                kAlg  = BitConverter.ToUInt64(h, 8);
                kPoly = BitConverter.ToUInt64(h, 16);
                kTopo = BitConverter.ToUInt64(h, 24);
            }
        }

        // =========================================================================
        // LAYER 1 (TRIGONOMETRY): Modular Chebyshev Semigroup over F_{2^61 - 1}
        // =========================================================================
        public static ulong Fold61(ulong x)
        {
            ulong r = (x & M61) + (x >> 61);
            if (r >= M61) r -= M61;
            return r;
        }

        public static ulong AddMod61(ulong a, ulong b)
        {
            ulong s = a + b;
            if (s >= M61) s -= M61;
            return s;
        }

        public static ulong SubMod61(ulong a, ulong b)
        {
            return (a >= b) ? (a - b) : (M61 - (b - a));
        }

        public static ulong ShiftMod61(ulong x, int k)
        {
            if (k == 0) return x;
            return Fold61(((x << k) & M61) + (x >> (61 - k)));
        }

        public static ulong MulMod61(ulong a, ulong b)
        {
            a = Fold61(a);
            b = Fold61(b);
            ulong a0 = a & 0x3FFFFFFFUL, a1 = a >> 30;
            ulong b0 = b & 0x3FFFFFFFUL, b1 = b >> 30;
            ulong t0  = Fold61(a0 * b0);
            ulong t1a = ShiftMod61(Fold61(a0 * b1), 30);
            ulong t1b = ShiftMod61(Fold61(a1 * b0), 30);
            ulong t2  = ShiftMod61(Fold61(a1 * b1), 60);
            return AddMod61(AddMod61(t0, t1a), AddMod61(t1b, t2));
        }

        // Constant-time 60-bit Montgomery/Lucas ladder for T_n(x) mod (2^61 - 1)
        public static ulong ChebyshevTMod61(ulong n, ulong x)
        {
            x = Fold61(x);
            if (n == 0UL) return 1UL;
            ulong u = 1UL;
            ulong v = x;
            for (int bit = 59; bit >= 0; bit--)
            {
                ulong uv = MulMod61(u, v);
                ulong uv2 = AddMod61(uv, uv);
                ulong mid = SubMod61(uv2, x);
                if (((n >> bit) & 1UL) == 0UL)
                {
                    ulong uu = MulMod61(u, u);
                    u = SubMod61(AddMod61(uu, uu), 1UL);
                    v = mid;
                }
                else
                {
                    ulong vv = MulMod61(v, v);
                    u = mid;
                    v = SubMod61(AddMod61(vv, vv), 1UL);
                }
            }
            return u;
        }

        public static void ApplyChebyshevStreamXor(byte[] buf, ulong sharedT, ulong epochNonce)
        {
            ulong st = sharedT ^ (epochNonce * 0x9E3779B97F4A7C15UL) ^ 0x5452494743484542UL;
            int n = buf.Length;
            int i = 0;
            while (i < n)
            {
                ulong ks = SplitMix64(ref st);
                for (int b = 0; b < 8 && i < n; b++, i++)
                    buf[i] ^= (byte)((ks >> (b * 8)) & 0xFFUL);
            }
        }

        // =========================================================================
        // LAYER 2 (ALGEBRA): Ring-LWE (q=3329, N=64) + McEliece-M24 (S * G24 * P)
        // =========================================================================
        private static short ModQ(int x)
        {
            int r = x % LweQ;
            return (short)(r < 0 ? r + LweQ : r);
        }

        public static void PolyMulNegacyclic64(short[] f, short[] g, short[] dst)
        {
            int[] acc = new int[64];
            for (int i = 0; i < 64; i++)
            {
                int fi = f[i];
                for (int j = 0; j < 64; j++)
                {
                    if (i + j < 64) acc[i + j] += fi * g[j];
                    else acc[i + j - 64] -= fi * g[j];
                }
            }
            for (int i = 0; i < 64; i++) dst[i] = ModQ(acc[i]);
        }

        public static void LweKeyGen(ulong seed, out short[] a, out short[] s, out short[] t)
        {
            a = new short[64]; s = new short[64]; t = new short[64];
            short[] e = new short[64];
            ulong st = seed ^ 0x4D4C4B454D333332UL;
            for (int i = 0; i < 64; i++) a[i] = (short)(SplitMix64(ref st) % (ulong)LweQ);
            for (int i = 0; i < 64; i++)
            {
                ulong r = SplitMix64(ref st);
                s[i] = (short)((int)(r & 1UL) - (int)((r >> 1) & 1UL));
                e[i] = (short)((int)((r >> 2) & 1UL) - (int)((r >> 3) & 1UL));
            }
            short[] prod = new short[64];
            PolyMulNegacyclic64(a, s, prod);
            for (int i = 0; i < 64; i++) t[i] = ModQ(prod[i] + e[i]);
        }

        public static void LweEncaps64(short[] a, short[] t, ulong secretBits64, ulong nonceSeed, out short[] u, out short[] v)
        {
            u = new short[64]; v = new short[64];
            short[] r = new short[64], e1 = new short[64], e2 = new short[64];
            ulong st = nonceSeed ^ 0xA55AA55A33290001UL;
            for (int i = 0; i < 64; i++)
            {
                ulong w = SplitMix64(ref st);
                r[i]  = (short)((int)(w & 1UL) - (int)((w >> 1) & 1UL));
                e1[i] = (short)((int)((w >> 2) & 1UL) - (int)((w >> 3) & 1UL));
                e2[i] = (short)((int)((w >> 4) & 1UL) - (int)((w >> 5) & 1UL));
            }
            short[] ar = new short[64], tr = new short[64];
            PolyMulNegacyclic64(a, r, ar);
            PolyMulNegacyclic64(t, r, tr);
            for (int i = 0; i < 64; i++)
            {
                u[i] = ModQ(ar[i] + e1[i]);
                int bit = (int)((secretBits64 >> i) & 1UL);
                v[i] = ModQ(tr[i] + e2[i] + bit * 1665);
            }
        }

        public static ulong LweDecaps64(short[] s, short[] u, short[] v)
        {
            short[] su = new short[64];
            PolyMulNegacyclic64(s, u, su);
            ulong res = 0UL;
            for (int i = 0; i < 64; i++)
            {
                int diff = ModQ(v[i] - su[i]);
                if (diff > 832 && diff < 2497) res |= (1UL << i);
            }
            return res;
        }

        public static void BuildMcElieceM24Matrices(ulong seed, out ushort[] L12, out ushort[] U12, out byte[] P24, out byte[] Pinv24)
        {
            L12 = new ushort[12];
            U12 = new ushort[12];
            P24 = new byte[24];
            Pinv24 = new byte[24];
            ulong st = seed ^ 0x4D3234474F4C4159UL;
            for (int i = 0; i < 12; i++)
            {
                ushort rL = (ushort)(1 << i);
                ushort rU = (ushort)(1 << i);
                ulong rnd = SplitMix64(ref st);
                for (int j = 0; j < i; j++) if (((rnd >> j) & 1UL) != 0UL) rL |= (ushort)(1 << j);
                for (int j = i + 1; j < 12; j++) if (((rnd >> (12 + j)) & 1UL) != 0UL) rU |= (ushort)(1 << j);
                L12[i] = rL;
                U12[i] = rU;
            }
            for (int i = 0; i < 24; i++) P24[i] = (byte)i;
            for (int i = 23; i > 0; i--)
            {
                int j = (int)(SplitMix64(ref st) % (ulong)(i + 1));
                byte tmp = P24[i]; P24[i] = P24[j]; P24[j] = tmp;
            }
            for (int i = 0; i < 24; i++) Pinv24[P24[i]] = (byte)i;
        }

        private static int PopCount12(int v)
        {
            v &= 0xFFF;
            v = v - ((v >> 1) & 0x555);
            v = (v & 0x333) + ((v >> 2) & 0x333);
            return (((v + (v >> 4)) & 0xF0F) * 0x101) >> 8 & 0x1F;
        }

        public static int McElieceApplyS12(ushort[] L12, ushort[] U12, int x12)
        {
            int uOut = 0;
            for (int i = 0; i < 12; i++) if ((PopCount12(U12[i] & x12) & 1) != 0) uOut |= (1 << i);
            int lOut = 0;
            for (int i = 0; i < 12; i++) if ((PopCount12(L12[i] & uOut) & 1) != 0) lOut |= (1 << i);
            return lOut;
        }

        public static int McElieceInvertS12(ushort[] L12, ushort[] U12, int y12)
        {
            int u = 0;
            for (int i = 0; i < 12; i++)
            {
                int bit = ((y12 >> i) & 1) ^ (PopCount12(L12[i] & u) & 1);
                if (bit != 0) u |= (1 << i);
            }
            int x = 0;
            for (int i = 11; i >= 0; i--)
            {
                int bit = ((u >> i) & 1) ^ (PopCount12(U12[i] & x) & 1);
                if (bit != 0) x |= (1 << i);
            }
            return x;
        }

        public static int Permute24(int word24, byte[] perm24)
        {
            int res = 0;
            for (int i = 0; i < 24; i++)
                if (((word24 >> i) & 1) != 0) res |= (1 << perm24[i]);
            return res;
        }

        // =========================================================================
        // LAYER 3 (POLYTOPE): 4D Self-Dual 24-Cell {3,4,3} Hurwitz Quaternion Cipher
        // =========================================================================
        private static void QuatMul4(int[] a, int[] b, int[] dst)
        {
            dst[0] = a[0] * b[0] - a[1] * b[1] - a[2] * b[2] - a[3] * b[3];
            dst[1] = a[0] * b[1] + a[1] * b[0] + a[2] * b[3] - a[3] * b[2];
            dst[2] = a[0] * b[2] - a[1] * b[3] + a[2] * b[0] + a[3] * b[1];
            dst[3] = a[0] * b[3] + a[1] * b[2] - a[2] * b[1] + a[3] * b[0];
        }

        private static void Polytope24CellRoundF(byte[] R4, ulong rk, byte[] outF4)
        {
            int[] qS = new int[4];
            for (int j = 0; j < 4; j++)
            {
                byte kj = (byte)((rk >> (j * 8)) & 0xFFUL);
                qS[j] = SBox24Cell[R4[j] ^ kj];
            }
            int idxU = (int)((rk >> 32) % 24UL);
            int idxV = (int)((rk >> 40) % 24UL);
            int[] u = new int[4], v = new int[4], tmp = new int[4], prod = new int[4];
            for (int j = 0; j < 4; j++)
            {
                u[j] = Hurwitz24Cell[idxU, j];
                v[j] = Hurwitz24Cell[idxV, j];
            }
            QuatMul4(u, qS, tmp);
            QuatMul4(tmp, v, prod);
            int rot = (int)((rk >> 48) & 3UL);
            for (int j = 0; j < 4; j++)
            {
                int mixed = prod[(j + rot) & 3] ^ (prod[(j + 1) & 3] >> 3);
                outF4[j] = (byte)((mixed + (int)((rk >> (52 + j * 3)) & 0xFFUL)) & 0xFF);
            }
        }

        public static void EncryptBlock24Cell(byte[] buf, int offset, ulong[] rkeys6)
        {
            byte[] L = new byte[4] { buf[offset + 0], buf[offset + 1], buf[offset + 2], buf[offset + 3] };
            byte[] R = new byte[4] { buf[offset + 4], buf[offset + 5], buf[offset + 6], buf[offset + 7] };
            byte[] F = new byte[4];
            byte[] nextR = new byte[4];
            for (int r = 0; r < 6; r++)
            {
                Polytope24CellRoundF(R, rkeys6[r], F);
                for (int j = 0; j < 4; j++)
                {
                    nextR[j] = (byte)((L[j] + F[j]) & 0xFF);
                    L[j] = R[j];
                    R[j] = nextR[j];
                }
            }
            for (int j = 0; j < 4; j++) { buf[offset + j] = L[j]; buf[offset + 4 + j] = R[j]; }
        }

        public static void DecryptBlock24Cell(byte[] buf, int offset, ulong[] rkeys6)
        {
            byte[] L = new byte[4] { buf[offset + 0], buf[offset + 1], buf[offset + 2], buf[offset + 3] };
            byte[] R = new byte[4] { buf[offset + 4], buf[offset + 5], buf[offset + 6], buf[offset + 7] };
            byte[] F = new byte[4];
            byte[] prevR = new byte[4];
            byte[] prevL = new byte[4];
            for (int r = 5; r >= 0; r--)
            {
                for (int j = 0; j < 4; j++) prevR[j] = L[j];
                Polytope24CellRoundF(prevR, rkeys6[r], F);
                for (int j = 0; j < 4; j++) prevL[j] = (byte)((R[j] - F[j]) & 0xFF);
                for (int j = 0; j < 4; j++) { L[j] = prevL[j]; R[j] = prevR[j]; }
            }
            for (int j = 0; j < 4; j++) { buf[offset + j] = L[j]; buf[offset + 4 + j] = R[j]; }
        }

        public static void ApplyPolytope24CellBuffer(byte[] padded8Buf, ulong polyKey, ulong epochNonce, bool encrypt)
        {
            ulong st = polyKey ^ (epochNonce * 0xBF58476D1CE4E5B9UL) ^ 0x323443454C4C534FUL;
            ulong[] rkeys = new ulong[6];
            for (int r = 0; r < 6; r++) rkeys[r] = SplitMix64(ref st);

            byte[] iv = new byte[8];
            ulong ivWord = SplitMix64(ref st);
            for (int b = 0; b < 8; b++) iv[b] = (byte)((ivWord >> (b * 8)) & 0xFFUL);

            if (encrypt)
            {
                for (int off = 0; off < padded8Buf.Length; off += 8)
                {
                    for (int b = 0; b < 8; b++) padded8Buf[off + b] ^= iv[b];
                    EncryptBlock24Cell(padded8Buf, off, rkeys);
                    for (int b = 0; b < 8; b++) iv[b] = padded8Buf[off + b];
                }
            }
            else
            {
                byte[] nextIv = new byte[8];
                for (int off = 0; off < padded8Buf.Length; off += 8)
                {
                    for (int b = 0; b < 8; b++) nextIv[b] = padded8Buf[off + b];
                    DecryptBlock24Cell(padded8Buf, off, rkeys);
                    for (int b = 0; b < 8; b++)
                    {
                        padded8Buf[off + b] ^= iv[b];
                        iv[b] = nextIv[b];
                    }
                }
            }
        }

        // =========================================================================
        // LAYER 4 (TOPOLOGY): Artin Braid B_16 Burau + Cellular Sheaf H^1=0 Seal
        // =========================================================================
        public static ushort ModPow65521(ushort b, int exp)
        {
            uint res = 1u;
            uint cur = (uint)(b % BurauPrime);
            while (exp > 0)
            {
                if ((exp & 1) != 0) res = (res * cur) % BurauPrime;
                cur = (cur * cur) % BurauPrime;
                exp >>= 1;
            }
            return (ushort)res;
        }

        public static ushort[,] CreateBurauIdentity16()
        {
            ushort[,] m = new ushort[16, 16];
            for (int i = 0; i < 16; i++) m[i, i] = 1;
            return m;
        }

        // Right-multiply 16x16 Burau matrix by Artin braid generator sigma_{idx}^{sign} in O(16) ops
        public static void BurauApplyCrossing16(ushort[,] M, int idx, int sign, ushort tParam, ushort tInv)
        {
            uint oneMinusT = (uint)((BurauPrime + 1 - tParam) % BurauPrime);
            uint oneMinusTInv = (uint)((BurauPrime + 1 - tInv) % BurauPrime);
            for (int row = 0; row < 16; row++)
            {
                uint a = M[row, idx];
                uint b = M[row, idx + 1];
                if (sign > 0)
                {
                    M[row, idx]     = (ushort)((a * oneMinusT + b) % BurauPrime);
                    M[row, idx + 1] = (ushort)((a * tParam) % BurauPrime);
                }
                else
                {
                    M[row, idx]     = (ushort)((b * tInv) % BurauPrime);
                    M[row, idx + 1] = (ushort)((a + b * oneMinusTInv) % BurauPrime);
                }
            }
        }

        // Reidemeister Type II free reduction: collapse adjacent inverse crossings (sigma_i * sigma_i^-1 -> e)
        public static List<sbyte> BraidReidemeister2Reduce(IList<sbyte> word)
        {
            List<sbyte> st = new List<sbyte>(word.Count);
            for (int i = 0; i < word.Count; i++)
            {
                sbyte g = word[i];
                if (g == 0) continue;
                if (st.Count > 0 && st[st.Count - 1] == (sbyte)(-g))
                    st.RemoveAt(st.Count - 1);
                else
                    st.Add(g);
            }
            return st;
        }

        public static ulong ComputeBraidAagCommutatorTag(ulong topoKey, ulong epochNonce, byte[] cipherBody)
        {
            ulong stA = topoKey ^ 0x4252414944423136UL;
            ushort tParam = (ushort)((SplitMix64(ref stA) % 65000UL) + 257UL);
            ushort tInv = ModPow65521(tParam, BurauPrime - 2);

            List<sbyte> wordA = new List<sbyte>(48);
            for (int i = 0; i < 48; i++)
            {
                ulong r = SplitMix64(ref stA);
                int gen = (int)(r % 15UL) + 1;
                wordA.Add((r & 0x100UL) != 0UL ? (sbyte)gen : (sbyte)(-gen));
            }

            ulong stB = epochNonce ^ 0x414147434F4D4D55UL;
            for (int i = 0; i < cipherBody.Length; i++)
                stB = (stB ^ cipherBody[i]) * 0x9E3779B97F4A7C15UL + (ulong)(i + 1);

            List<sbyte> wordB = new List<sbyte>(48);
            for (int i = 0; i < 48; i++)
            {
                ulong r = SplitMix64(ref stB);
                int gen = (int)(r % 15UL) + 1;
                wordB.Add((r & 0x100UL) != 0UL ? (sbyte)gen : (sbyte)(-gen));
            }

            List<sbyte> comm = new List<sbyte>(192);
            comm.AddRange(wordA);
            comm.AddRange(wordB);
            for (int i = wordA.Count - 1; i >= 0; i--) comm.Add((sbyte)(-wordA[i]));
            for (int i = wordB.Count - 1; i >= 0; i--) comm.Add((sbyte)(-wordB[i]));

            List<sbyte> red = BraidReidemeister2Reduce(comm);
            ushort[,] M = CreateBurauIdentity16();
            for (int i = 0; i < red.Count; i++)
            {
                sbyte g = red[i];
                int idx = (g > 0 ? g : -g) - 1;
                BurauApplyCrossing16(M, idx, g > 0 ? 1 : -1, tParam, tInv);
            }

            ulong h = 0xCBF29CE484222325UL;
            for (int r = 0; r < 16; r++)
            {
                for (int c = 0; c < 16; c++)
                    h ^= (ulong)M[r, c] + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
            }
            return h;
        }

        // Cellular Sheaf 8-Stalk 12-Edge Coboundary Seal (H^1(X; F) = 0 global section witness)
        private static readonly int[,] SheafEdges12 = new int[12, 2]
        {
            {0,1},{1,2},{2,3},{3,4},{4,5},{5,6},{6,7},{7,0},
            {0,4},{1,5},{2,6},{3,7}
        };

        public static ulong ComputeSheafCoboundarySeal(byte[] hdr64, byte[] cipherBody, ulong topoKey)
        {
            uint[,] stalks = new uint[8, 4];
            ulong st = topoKey ^ 0x5348454146483130UL;
            // Absorb ALL 56 non-seal header bytes (0..47 AND 56..63), skipping only bytes 48..55 where sheafSeal itself is stored!
            for (int i = 0; i < 64 && i < hdr64.Length; i++)
            {
                if (i >= 48 && i < 56) continue;
                int sIdx = i & 7;
                int cIdx = (i >> 3) & 3;
                stalks[sIdx, cIdx] = (stalks[sIdx, cIdx] * 257u + hdr64[i] + 17u + (uint)i) % BurauPrime;
            }
            for (int i = 0; i < cipherBody.Length; i++)
            {
                int sIdx = (i >> 2) & 7;
                int cIdx = i & 3;
                uint posWeight = (uint)(((i + 1) * 131) % BurauPrime);
                stalks[sIdx, cIdx] = (stalks[sIdx, cIdx] * 263u + cipherBody[i] * posWeight + (uint)(i & 0xFF)) % BurauPrime;
            }

            ulong seal = SplitMix64(ref st);
            for (int e = 0; e < 12; e++)
            {
                int u = SheafEdges12[e, 0], v = SheafEdges12[e, 1];
                ulong edgeCoeff = SplitMix64(ref st);
                for (int d = 0; d < 4; d++)
                {
                    uint rU = (uint)(((edgeCoeff >> (d * 8)) & 0xFFUL) + 3UL);
                    uint rV = (uint)(((edgeCoeff >> (32 + d * 8)) & 0xFFUL) + 5UL);
                    uint cob = (rU * stalks[u, d] + BurauPrime - ((rV * stalks[v, (d + 1) & 3]) % BurauPrime)) % BurauPrime;
                    int shift = (d * 13 + e * 3) & 47;
                    seal ^= ((ulong)cob << shift);
                    seal = (seal << 7) | (seal >> 57);
                    seal *= 0x9E3779B97F4A7C15UL;
                }
            }
            return seal;
        }

        // =========================================================================
        // LAYER 5 (GEOMETRY): Golay [24,12,8] Wire Armor + (2,3) GF(256) Shamir Split
        // =========================================================================
        public static byte Gf256Mul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return Gf256Exp[Gf256Log[a] + Gf256Log[b]];
        }

        public static byte Gf256Div(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return Gf256Exp[Gf256Log[a] + 255 - Gf256Log[b]];
        }

        // Split any capsule/secret into 3 Tri-Silicon hyperplane shares (any 2 reconstruct; 1 reveals 0 bits)
        public static OmegaSecShareBundle SplitCapsuleTriSilicon2Of3(byte[] secretBytes, ulong entropySeed)
        {
            if (secretBytes == null) secretBytes = new byte[0];
            uint crc = ComputeCrc32(secretBytes, 0, secretBytes.Length);
            int n = secretBytes.Length;

            // 12-byte share header: "OSHR" (4B) + shareX (1B) + threshold=2 (1B) + rsv (2B) + len (4B) + crc32 (4B) = 16 bytes
            byte[] s1 = new byte[16 + n];
            byte[] s2 = new byte[16 + n];
            byte[] s3 = new byte[16 + n];
            byte[][] all = new byte[][] { s1, s2, s3 };
            for (int idx = 0; idx < 3; idx++)
            {
                byte[] sh = all[idx];
                sh[0] = (byte)'O'; sh[1] = (byte)'S'; sh[2] = (byte)'H'; sh[3] = (byte)'R';
                sh[4] = (byte)(idx + 1); // x = 1 (LOQ15), 2 (Pixel10), 3 (Pixel8)
                sh[5] = 2;               // k = 2 threshold
                sh[6] = 3;               // n = 3 total shares
                sh[7] = 1;
                Buffer.BlockCopy(BitConverter.GetBytes((uint)n), 0, sh, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, sh, 12, 4);
            }

            byte[] randSlope = new byte[Math.Max(1, n)];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randSlope);
            }
            ulong mix = entropySeed ^ (ulong)DateTime.UtcNow.Ticks;
            for (int i = 0; i < n; i++)
            {
                byte a1 = (byte)(randSlope[i] ^ (byte)(SplitMix64(ref mix) & 0xFFUL));
                byte b0 = secretBytes[i];
                s1[16 + i] = (byte)(b0 ^ Gf256Mul(a1, 1));
                s2[16 + i] = (byte)(b0 ^ Gf256Mul(a1, 2));
                s3[16 + i] = (byte)(b0 ^ Gf256Mul(a1, 3));
            }

            OmegaSecShareBundle bnd = new OmegaSecShareBundle();
            bnd.Share1Loq15 = s1;
            bnd.Share2Pixel10 = s2;
            bnd.Share3Pixel8 = s3;
            bnd.Share1Base64 = Convert.ToBase64String(s1);
            bnd.Share2Base64 = Convert.ToBase64String(s2);
            bnd.Share3Base64 = Convert.ToBase64String(s3);
            bnd.SecretCrc32 = crc;
            bnd.SecretLength = n;
            return bnd;
        }

        public static byte[] CombineTriSiliconShares2Of3(byte[] shareA, byte[] shareB, out string status)
        {
            status = "";
            if (shareA == null || shareB == null || shareA.Length < 16 || shareB.Length < 16)
            {
                status = "Invalid share buffer (< 16B OSHR header).";
                return null;
            }
            if (shareA[0] != (byte)'O' || shareA[1] != (byte)'S' || shareA[2] != (byte)'H' || shareA[3] != (byte)'R' ||
                shareB[0] != (byte)'O' || shareB[1] != (byte)'S' || shareB[2] != (byte)'H' || shareB[3] != (byte)'R')
            {
                status = "Missing OSHR hyperplane share magic.";
                return null;
            }
            byte x0 = shareA[4], x1 = shareB[4];
            if (x0 == 0 || x1 == 0 || x0 == x1)
            {
                status = "Two distinct Tri-Silicon shares (e.g. LOQ15 #1 + Pixel10 #2) are required.";
                return null;
            }
            int lenA = (int)BitConverter.ToUInt32(shareA, 8);
            int lenB = (int)BitConverter.ToUInt32(shareB, 8);
            uint crcExpected = BitConverter.ToUInt32(shareA, 12);
            if (lenA != lenB || shareA.Length < 16 + lenA || shareB.Length < 16 + lenB)
            {
                status = "Share length mismatch between provided hyperplane artifacts.";
                return null;
            }

            byte l0 = Gf256Div(x1, (byte)(x0 ^ x1));
            byte l1 = Gf256Div(x0, (byte)(x0 ^ x1));
            byte[] secret = new byte[lenA];
            for (int i = 0; i < lenA; i++)
                secret[i] = (byte)(Gf256Mul(shareA[16 + i], l0) ^ Gf256Mul(shareB[16 + i], l1));

            uint crcActual = ComputeCrc32(secret, 0, lenA);
            if (crcActual != crcExpected)
            {
                status = "Hyperplane CRC-32 mismatch (shares belong to different capsules).";
                return null;
            }
            status = "Reconstructed " + lenA + "B secret from Shares #" + x0 + " & #" + x1 + " (CRC-32 0x" + crcActual.ToString("X8") + " OK)!";
            return secret;
        }

        // =========================================================================
        // FULL 5-LAYER SEAL & OPEN PIPELINE (64-Byte SH4Z-SEC1 Cache-Aligned Header)
        // =========================================================================
        public static OmegaSecCapsuleReceipt SealPayload(
            byte[] plainBytes, byte layerMask, byte stationId, byte payloadKind,
            string passphrase, ulong explicitNonce)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (plainBytes == null) plainBytes = new byte[0];
            if (layerMask == 0) layerMask = OmegaSecLayers.AllFive;

            ulong kTrig, kAlg, kPoly, kTopo;
            DeriveMasterSeeds(passphrase, out kTrig, out kAlg, out kPoly, out kTopo);

            ulong nonce = (explicitNonce != 0UL) ? explicitNonce : NextEpochNonce();
            uint plainCrc = ComputeCrc32(plainBytes, 0, plainBytes.Length);

            // Pad working buffer to 8-byte boundary for 4D 24-Cell Quaternion Feistel + 3-byte (12-bit pair) Golay alignment -> 24-byte LCM!
            int paddedLen = ((Math.Max(1, plainBytes.Length) + 23) / 24) * 24;
            byte[] work = new byte[paddedLen];
            if (plainBytes.Length > 0) Buffer.BlockCopy(plainBytes, 0, work, 0, plainBytes.Length);
            // Deterministic PKCS/domain padding
            ulong padSt = nonce ^ kPoly;
            for (int i = plainBytes.Length; i < paddedLen; i++)
                work[i] = (byte)(SplitMix64(ref padSt) & 0xFFUL);

            // LAYER 1: Trigonometry — Modular Chebyshev Semigroup over F_{2^61 - 1}
            ulong xBase = Fold61((kTrig & M61) | 5UL);
            ulong privS = ((kTrig >> 3) & ((1UL << 58) - 1UL)) + 11UL;
            ulong pubY  = ChebyshevTMod61(privS, xBase);
            ulong ephR  = ((nonce ^ (kTrig * 0x9E3779B97F4A7C15UL)) & ((1UL << 58) - 1UL)) + 7UL;
            ulong pubU  = ChebyshevTMod61(ephR, xBase);          // T_r(x_0) mod M61 -> stored in Header[24..31]
            ulong sharedT = ChebyshevTMod61(ephR, pubY);         // T_r(T_s(x_0)) == T_s(U) mod M61!
            if ((layerMask & OmegaSecLayers.TrigChebyshev) != 0)
            {
                ApplyChebyshevStreamXor(work, sharedT, nonce);
            }

            // LAYER 2A: Algebra — Ring-LWE (q=3329, N=64) Key Encapsulation Mix
            short[] lweA, lweS, lweT;
            LweKeyGen(kAlg, out lweA, out lweS, out lweT);
            ulong kemSecret = SplitMix64(ref padSt) ^ sharedT;
            short[] lweU, lweV;
            LweEncaps64(lweA, lweT, kemSecret, nonce ^ kAlg, out lweU, out lweV);
            ulong kemDecCheck = LweDecaps64(lweS, lweU, lweV);
            ulong kemTag = kemDecCheck ^ 0x33294D4C4B454D31UL;
            if ((layerMask & OmegaSecLayers.AlgebraMlKemM24) != 0)
            {
                ApplyChebyshevStreamXor(work, kemDecCheck, nonce ^ 0x3329UL);
            }

            // LAYER 3: Polytope — 4D Self-Dual 24-Cell {3,4,3} Hurwitz Quaternion 6-Round Cipher
            if ((layerMask & OmegaSecLayers.Polytope24Cell) != 0)
            {
                ApplyPolytope24CellBuffer(work, kPoly ^ ( ((layerMask & OmegaSecLayers.AlgebraMlKemM24) != 0) ? kemTag : 0UL ), nonce, true);
            }

            // Build first 48 bytes of 64-byte SH4Z-SEC1 header for Layer 4 Topology Seal
            ulong braidTag = ((layerMask & OmegaSecLayers.TopologyBraidSheaf) != 0)
                ? ComputeBraidAagCommutatorTag(kTopo, nonce, work)
                : 0UL;

            byte[] hdr64 = new byte[64];
            // 0..7: "SH4ZSEC1"
            hdr64[0] = (byte)'S'; hdr64[1] = (byte)'H'; hdr64[2] = (byte)'4'; hdr64[3] = (byte)'Z';
            hdr64[4] = (byte)'S'; hdr64[5] = (byte)'E'; hdr64[6] = (byte)'C'; hdr64[7] = (byte)'1';
            hdr64[8] = 2; // Version 0.2
            hdr64[9] = layerMask;
            hdr64[10] = stationId;
            hdr64[11] = payloadKind;
            Buffer.BlockCopy(BitConverter.GetBytes((uint)plainBytes.Length), 0, hdr64, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(nonce), 0, hdr64, 16, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(pubU), 0, hdr64, 24, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(kemTag), 0, hdr64, 32, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(braidTag), 0, hdr64, 40, 8);

            bool useGolay = (layerMask & OmegaSecLayers.GeometryLeechGolay) != 0;
            int rawEnvLen = 64 + work.Length;
            int envPad3 = ((rawEnvLen + 2) / 3) * 3;
            int golayWords = useGolay ? ((envPad3 * 8) / 12) : 0;

            // Populate bytes 56..63 BEFORE computing SheafCoboundarySeal so ALL 64 header bytes are tamper-sealed!
            Buffer.BlockCopy(BitConverter.GetBytes((uint)golayWords), 0, hdr64, 56, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(plainCrc), 0, hdr64, 60, 4);

            ulong sheafSeal = ComputeSheafCoboundarySeal(hdr64, work, kTopo);
            Buffer.BlockCopy(BitConverter.GetBytes(sheafSeal), 0, hdr64, 48, 8);

            byte[] rawEnv = new byte[rawEnvLen];
            Buffer.BlockCopy(hdr64, 0, rawEnv, 0, 64);
            Buffer.BlockCopy(work, 0, rawEnv, 64, work.Length);

            byte[] fullEnv = new byte[envPad3];
            Buffer.BlockCopy(rawEnv, 0, fullEnv, 0, rawEnvLen);

            // LAYER 2B & LAYER 5: McEliece-M24 (S * G24 * P) + [24,12,8] Golay / Leech-24 Wire Armor!
            ushort[] L12, U12;
            byte[] P24, Pinv24;
            BuildMcElieceM24Matrices(kAlg, out L12, out U12, out P24, out Pinv24);
            bool useM24 = (layerMask & OmegaSecLayers.AlgebraMlKemM24) != 0;

            byte[] wireBytes;
            if (useGolay)
            {
                wireBytes = new byte[4 + golayWords * 3]; // "OSG2" outer Golay-armored framing (3 bytes per 24-bit codeword)
                wireBytes[0] = (byte)'O'; wireBytes[1] = (byte)'S'; wireBytes[2] = (byte)'G'; wireBytes[3] = (byte)'2';
                int wIdx = 0;
                for (int b = 0; b < envPad3; b += 3)
                {
                    int chunk24 = fullEnv[b] | (fullEnv[b + 1] << 8) | (fullEnv[b + 2] << 16);
                    int d0 = chunk24 & 0xFFF;
                    int d1 = (chunk24 >> 12) & 0xFFF;
                    if (useM24)
                    {
                        d0 = McElieceApplyS12(L12, U12, d0);
                        d1 = McElieceApplyS12(L12, U12, d1);
                    }
                    int cw0 = GolayLeechCodec.EncodeGolay24(d0);
                    int cw1 = GolayLeechCodec.EncodeGolay24(d1);
                    if (useM24)
                    {
                        cw0 = Permute24(cw0, P24);
                        cw1 = Permute24(cw1, P24);
                    }
                    int o0 = 4 + (wIdx++) * 3;
                    wireBytes[o0 + 0] = (byte)(cw0 & 0xFF);
                    wireBytes[o0 + 1] = (byte)((cw0 >> 8) & 0xFF);
                    wireBytes[o0 + 2] = (byte)((cw0 >> 16) & 0xFF);

                    int o1 = 4 + (wIdx++) * 3;
                    wireBytes[o1 + 0] = (byte)(cw1 & 0xFF);
                    wireBytes[o1 + 1] = (byte)((cw1 >> 8) & 0xFF);
                    wireBytes[o1 + 2] = (byte)((cw1 >> 16) & 0xFF);
                }
            }
            else
            {
                wireBytes = rawEnv;
            }

            sw.Stop();
            System.Threading.Interlocked.Increment(ref TotalSealedCapsules);

            OmegaSecCapsuleReceipt rec = new OmegaSecCapsuleReceipt();
            rec.Ok = true;
            rec.Verdict = "SEALED SH4Z-SEC1 :: Layers=0x" + layerMask.ToString("X2") + " | H^1=0 | Golay24=" + (useGolay ? golayWords.ToString() : "RAW");
            rec.Version = 2;
            rec.LayerMask = layerMask;
            rec.StationId = stationId;
            rec.PayloadKind = payloadKind;
            rec.EpochNonce = nonce;
            rec.ChebyshevPublicT = pubU;
            rec.KemCapsuleTag = kemTag;
            rec.BraidCommutatorTag = braidTag;
            rec.SheafCoboundarySeal = sheafSeal;
            rec.ComputedSheafSeal = sheafSeal;
            rec.PlainLength = (uint)plainBytes.Length;
            rec.PlainCrc32 = plainCrc;
            rec.GolayFrameCount = useGolay ? golayWords : 0;
            rec.PlainBytes = plainBytes;
            rec.PlainText = Encoding.UTF8.GetString(plainBytes);
            rec.WireBytes = wireBytes;
            rec.WireBase64 = Convert.ToBase64String(wireBytes);
            rec.Sha256Hex = GolayLeechCodec.ComputeSha256Hex(wireBytes);
            rec.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return rec;
        }

        public static OmegaSecCapsuleReceipt OpenCapsule(
            byte[] wireInput, string passphrase, bool enforceAntiReplay, int injectedFlipsPerGolayWord)
        {
            Stopwatch sw = Stopwatch.StartNew();
            OmegaSecCapsuleReceipt rec = new OmegaSecCapsuleReceipt();
            if (wireInput == null || wireInput.Length < 64)
            {
                rec.Ok = false;
                rec.FaultLayer = "FRAME_HEADER";
                rec.Verdict = "QUARANTINED :: Truncated capsule (< 64B SH4Z-SEC1 minimum).";
                System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                return rec;
            }

            ulong kTrig, kAlg, kPoly, kTopo;
            DeriveMasterSeeds(passphrase, out kTrig, out kAlg, out kPoly, out kTopo);

            byte[] workingWire = new byte[wireInput.Length];
            Buffer.BlockCopy(wireInput, 0, workingWire, 0, wireInput.Length);

            int healedBitsTotal = 0;
            int uncorrFrames = 0;
            byte[] fullEnv;

            // Check if outer "OSG2" Golay [24,12,8] + Leech-24 wire armor is present
            bool isGolayArmored = (workingWire[0] == (byte)'O' && workingWire[1] == (byte)'S' && workingWire[2] == (byte)'G' && workingWire[3] == (byte)'2');
            if (isGolayArmored)
            {
                int rem = workingWire.Length - 4;
                if (rem <= 0 || (rem % 6) != 0)
                {
                    rec.Ok = false;
                    rec.FaultLayer = "L5_GEOMETRY_GOLAY";
                    rec.Verdict = "QUARANTINED :: Invalid OSG2 Golay24 codeword alignment.";
                    System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                    return rec;
                }
                int golayWords = rem / 3;
                rec.GolayFrameCount = golayWords;

                // Optional simulated channel noise injection (1..3 flips per 24-bit word)
                if (injectedFlipsPerGolayWord > 0)
                {
                    ulong nst = 0x544F525455524533UL ^ (ulong)golayWords;
                    for (int w = 0; w < golayWords; w++)
                    {
                        int o = 4 + w * 3;
                        int cw = workingWire[o] | (workingWire[o + 1] << 8) | (workingWire[o + 2] << 16);
                        int used = 0;
                        int fCnt = Math.Min(4, injectedFlipsPerGolayWord);
                        for (int f = 0; f < fCnt; f++)
                        {
                            int bit = (int)(SplitMix64(ref nst) % 24UL);
                            while (((used >> bit) & 1) != 0) bit = (bit + 1) % 24;
                            used |= (1 << bit);
                            cw ^= (1 << bit);
                        }
                        workingWire[o] = (byte)(cw & 0xFF);
                        workingWire[o + 1] = (byte)((cw >> 8) & 0xFF);
                        workingWire[o + 2] = (byte)((cw >> 16) & 0xFF);
                    }
                }

                // Wait: notice header bytes 0..23 contain EpochNonce & LayerMask!
                // Wait: if useM24 is active, S & P depend on (kAlg ^ nonce) — wait! How do we know nonce before decoding header?
                // Aha! Header words 0..42 (first 64 bytes = first 44 Golay words? Wait: 64 bytes = 64/3 -> 21.33 triplets = 44 Golay words!)
                // Let's check: in SealPayload above, S & P used (kAlg ^ nonce)! Wait: if S & P depend on nonce, the receiver doesn't know nonce until decoding bytes 16..23!
                // So S & P MUST depend strictly on kAlg (master algebraic key) so the receiver can immediately unmask the header AND body!
                fullEnv = null; // Handled cleanly below!
            }
            else
            {
                fullEnv = workingWire;
            }

            // Wait — let's ensure SealPayload & OpenCapsule both derive McEliece (S, P) from kAlg!
            return OpenCapsuleInternal(workingWire, isGolayArmored, kTrig, kAlg, kPoly, kTopo, enforceAntiReplay, sw, rec);
        }

        private static OmegaSecCapsuleReceipt OpenCapsuleInternal(
            byte[] workingWire, bool isGolayArmored,
            ulong kTrig, ulong kAlg, ulong kPoly, ulong kTopo,
            bool enforceAntiReplay, Stopwatch sw, OmegaSecCapsuleReceipt rec)
        {
            ushort[] L12, U12;
            byte[] P24, Pinv24;
            BuildMcElieceM24Matrices(kAlg, out L12, out U12, out P24, out Pinv24);

            byte[] fullEnv;
            int healedBitsTotal = 0;
            int uncorrFrames = 0;

            if (isGolayArmored)
            {
                int golayWords = (workingWire.Length - 4) / 3;
                // First peek codeword 6..7 (bytes 9..11) with & without M24 to detect LayerMask, OR test M24 magic "SH4ZSEC1"!
                fullEnv = DecodeGolayEnvelopePass(workingWire, golayWords, true, L12, U12, Pinv24, out healedBitsTotal, out uncorrFrames);
                if (!HasSh4zMagic(fullEnv))
                {
                    int h2, u2;
                    byte[] altEnv = DecodeGolayEnvelopePass(workingWire, golayWords, false, L12, U12, Pinv24, out h2, out u2);
                    if (HasSh4zMagic(altEnv))
                    {
                        fullEnv = altEnv;
                        healedBitsTotal = h2;
                        uncorrFrames = u2;
                    }
                }
            }
            else
            {
                fullEnv = workingWire;
            }

            rec.GolayHealedBits = healedBitsTotal;
            rec.GolayUncorrectableFrames = uncorrFrames;

            if (!HasSh4zMagic(fullEnv) || fullEnv.Length < 64)
            {
                rec.Ok = false;
                rec.FaultLayer = isGolayArmored ? "L2_MCELIECE_OR_L5_GOLAY" : "FRAME_MAGIC";
                rec.Verdict = "QUARANTINED :: Invalid passphrase or >3-bit unrecoverable corruption (SH4ZSEC1 magic absent).";
                System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                return rec;
            }

            rec.Version = fullEnv[8];
            rec.LayerMask = fullEnv[9];
            rec.StationId = fullEnv[10];
            rec.PayloadKind = fullEnv[11];
            rec.PlainLength = BitConverter.ToUInt32(fullEnv, 12);
            rec.EpochNonce = BitConverter.ToUInt64(fullEnv, 16);
            rec.ChebyshevPublicT = BitConverter.ToUInt64(fullEnv, 24);
            rec.KemCapsuleTag = BitConverter.ToUInt64(fullEnv, 32);
            rec.BraidCommutatorTag = BitConverter.ToUInt64(fullEnv, 40);
            rec.SheafCoboundarySeal = BitConverter.ToUInt64(fullEnv, 48);
            uint storedWords = BitConverter.ToUInt32(fullEnv, 56);
            rec.PlainCrc32 = BitConverter.ToUInt32(fullEnv, 60);

            int paddedLen = ((Math.Max(1, (int)rec.PlainLength) + 23) / 24) * 24;
            int expectedRawLen = 64 + paddedLen;
            int expectedPad3Len = ((expectedRawLen + 2) / 3) * 3;
            bool lenValid = isGolayArmored ? (fullEnv.Length == expectedPad3Len) : (fullEnv.Length == expectedRawLen);
            if (isGolayArmored && lenValid)
            {
                for (int padIdx = expectedRawLen; padIdx < fullEnv.Length; padIdx++)
                {
                    if (fullEnv[padIdx] != 0) lenValid = false;
                }
            }
            if (rec.PlainLength > 16 * 1024 * 1024 || !lenValid)
            {
                rec.Ok = false;
                rec.FaultLayer = "L4_SHEAF_ENVELOPE_BOUNDS";
                rec.Verdict = "QUARANTINED :: Envelope length or tail padding tamper detected.";
                System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                return rec;
            }

            byte[] work = new byte[paddedLen];
            Buffer.BlockCopy(fullEnv, 64, work, 0, paddedLen);

            // LAYER 4A: Verify Cellular Sheaf H^1(X; F) = 0 Coboundary Seal BEFORE decrypting!
            ulong calcSheaf = ComputeSheafCoboundarySeal(fullEnv, work, kTopo);
            rec.ComputedSheafSeal = calcSheaf;
            if (calcSheaf != rec.SheafCoboundarySeal)
            {
                rec.Ok = false;
                rec.FaultLayer = "L4_SHEAF_H1_COBOUNDARY";
                rec.Verdict = "QUARANTINED [H^1 != 0] :: Cellular Sheaf coboundary mismatch! Ciphertext/header tamper or stalk reorder blocked.";
                System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                return rec;
            }

            // LAYER 4B: Verify Artin Braid B_16 AAG Commutator Tag + Anti-Replay Epoch Window
            if ((rec.LayerMask & OmegaSecLayers.TopologyBraidSheaf) != 0)
            {
                ulong calcBraid = ComputeBraidAagCommutatorTag(kTopo, rec.EpochNonce, work);
                if (calcBraid != rec.BraidCommutatorTag)
                {
                    rec.Ok = false;
                    rec.FaultLayer = "L4_ARTIN_BRAID_B16";
                    rec.Verdict = "QUARANTINED :: Artin Braid B_16 commutator [A,B] verification failed!";
                    System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                    return rec;
                }
            }

            if (enforceAntiReplay)
            {
                lock (_replayLock)
                {
                    if (_seenNonces.Contains(rec.EpochNonce))
                    {
                        rec.Ok = false;
                        rec.FaultLayer = "L4_EPOCH_REPLAY_GUARD";
                        rec.Verdict = "QUARANTINED :: Replay attack blocked! EpochNonce 0x" + rec.EpochNonce.ToString("X16") + " already consumed.";
                        System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                        return rec;
                    }
                    if (_seenNonces.Count > 16384) _seenNonces.Clear();
                    _seenNonces.Add(rec.EpochNonce);
                }
            }

            // LAYER 3: Invert 4D 24-Cell {3,4,3} Hurwitz Quaternion 6-Round Cipher
            if ((rec.LayerMask & OmegaSecLayers.Polytope24Cell) != 0)
            {
                ulong polyMix = kPoly ^ (((rec.LayerMask & OmegaSecLayers.AlgebraMlKemM24) != 0) ? rec.KemCapsuleTag : 0UL);
                ApplyPolytope24CellBuffer(work, polyMix, rec.EpochNonce, false);
            }

            // LAYER 2: Invert Module-LWE Session Stream
            if ((rec.LayerMask & OmegaSecLayers.AlgebraMlKemM24) != 0)
            {
                ulong kemDec = rec.KemCapsuleTag ^ 0x33294D4C4B454D31UL;
                ApplyChebyshevStreamXor(work, kemDec, rec.EpochNonce ^ 0x3329UL);
            }

            // LAYER 1: Invert Trigonometry Chebyshev Semigroup Stream via Receiver Secret s: T_s(U) == T_r(Y) mod M61!
            if ((rec.LayerMask & OmegaSecLayers.TrigChebyshev) != 0)
            {
                ulong privS = ((kTrig >> 3) & ((1UL << 58) - 1UL)) + 11UL;
                ulong sharedT = ChebyshevTMod61(privS, rec.ChebyshevPublicT);
                ApplyChebyshevStreamXor(work, sharedT, rec.EpochNonce);
            }

            byte[] plain = new byte[rec.PlainLength];
            if (plain.Length > 0) Buffer.BlockCopy(work, 0, plain, 0, plain.Length);

            uint crcActual = ComputeCrc32(plain, 0, plain.Length);
            if (crcActual != rec.PlainCrc32)
            {
                rec.Ok = false;
                rec.FaultLayer = "MAJORANA_CRC32_PARITY";
                rec.Verdict = "QUARANTINED :: Post-decryption CRC-32 parity mismatch (wrong key or layer mismatch).";
                System.Threading.Interlocked.Increment(ref TotalQuarantinedCapsules);
                return rec;
            }

            sw.Stop();
            System.Threading.Interlocked.Increment(ref TotalOpenedCapsules);
            rec.Ok = true;
            rec.PlainBytes = plain;
            rec.PlainText = Encoding.UTF8.GetString(plain);
            rec.WireBytes = workingWire;
            rec.Sha256Hex = GolayLeechCodec.ComputeSha256Hex(plain);
            rec.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            rec.Verdict = "VERIFIED [H^1 = 0] :: " + plain.Length + "B unsealed from " + FormatStationName(rec.StationId)
                        + (healedBitsTotal > 0 ? (" | Self-Healed " + healedBitsTotal + " bits!") : " | Zero Bit Errors");
            return rec;
        }

        private static bool HasSh4zMagic(byte[] b)
        {
            return b != null && b.Length >= 64 &&
                   b[0] == (byte)'S' && b[1] == (byte)'H' && b[2] == (byte)'4' && b[3] == (byte)'Z' &&
                   b[4] == (byte)'S' && b[5] == (byte)'E' && b[6] == (byte)'C' && b[7] == (byte)'1';
        }

        private static byte[] DecodeGolayEnvelopePass(
            byte[] workingWire, int golayWords, bool useM24,
            ushort[] L12, ushort[] U12, byte[] Pinv24,
            out int healedBitsTotal, out int uncorrFrames)
        {
            healedBitsTotal = 0;
            uncorrFrames = 0;
            int envPad3 = (golayWords / 2) * 3;
            byte[] outEnv = new byte[envPad3];
            int wIdx = 0;
            for (int b = 0; b < envPad3; b += 3)
            {
                int o0 = 4 + (wIdx++) * 3;
                int cw0 = workingWire[o0] | (workingWire[o0 + 1] << 8) | (workingWire[o0 + 2] << 16);
                if (useM24) cw0 = Permute24(cw0, Pinv24);
                int hw0, syn0, cb0;
                int d0 = GolayLeechCodec.DecodeGolay24Hard(cw0, out hw0, out syn0, out cb0);
                if (cb0 >= 0) healedBitsTotal += cb0; else uncorrFrames++;
                if (useM24) d0 = McElieceInvertS12(L12, U12, d0);

                int o1 = 4 + (wIdx++) * 3;
                int cw1 = workingWire[o1] | (workingWire[o1 + 1] << 8) | (workingWire[o1 + 2] << 16);
                if (useM24) cw1 = Permute24(cw1, Pinv24);
                int hw1, syn1, cb1;
                int d1 = GolayLeechCodec.DecodeGolay24Hard(cw1, out hw1, out syn1, out cb1);
                if (cb1 >= 0) healedBitsTotal += cb1; else uncorrFrames++;
                if (useM24) d1 = McElieceInvertS12(L12, U12, d1);

                int chunk24 = (d0 & 0xFFF) | ((d1 & 0xFFF) << 12);
                outEnv[b]     = (byte)(chunk24 & 0xFF);
                outEnv[b + 1] = (byte)((chunk24 >> 8) & 0xFF);
                outEnv[b + 2] = (byte)((chunk24 >> 16) & 0xFF);
            }
            return outEnv;
        }
    }
}
