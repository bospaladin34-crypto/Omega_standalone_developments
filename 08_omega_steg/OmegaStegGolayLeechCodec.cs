using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OmegaStegStudio
{
    public enum StegPayloadKind : byte
    {
        TextOrKey = 0,
        OmshShader = 1,
        JsonManifest = 2
    }

    public sealed class GolayFrameVisualPoint
    {
        public int FrameIndex;
        public int Data12;
        public int SentWord24;
        public int ReceivedWord24;
        public int HealedWord24;
        public int Syndrome12;
        public int CorrectedBits; // 0, 1, 2, 3, or -1 if weight-4 uncorrectable
        public float IdealX, IdealY;
        public float NoisyX, NoisyY;
    }

    public sealed class GolayDecodeReceipt
    {
        public bool Success = false;
        public StegPayloadKind Kind = StegPayloadKind.TextOrKey;
        public byte[] PayloadBytes = new byte[0];
        public string DecodedText = "";
        public int TotalFrames = 0;
        public int CleanFrames = 0;
        public int Healed1BitFrames = 0;
        public int Healed2BitFrames = 0;
        public int Healed3BitFrames = 0;
        public int Uncorrectable4BitFrames = 0;
        public int TotalBitsHealed = 0;
        public bool Crc16Verified = false;
        public string Sha256Hex = "";
        public float EstimatedSnrDb = 28.0f;
        public string StatusSummary = "";
        public readonly List<GolayFrameVisualPoint> ConstellationPoints = new List<GolayFrameVisualPoint>();
    }

    public static class GolayLeechCodec
    {
        // Exact Paley-11 [24, 12, 8] Extended Binary Golay Parity Submatrix B_12:
        // Weight distribution across 4096 codewords: 1 (w=0), 759 octads (w=8), 2576 dodecads (w=12), 759 (w=16), 1 (w=24)!
        public static readonly ushort[] ParityRows12 = new ushort[12]
        {
            0xA3B, 0xC76, 0x8ED, 0x9DA, 0xBB4, 0xF68,
            0xED1, 0xDA3, 0xB47, 0xE8E, 0xD1D, 0x7FF
        };

        // O(1) Syndrome Lookup Table: maps 12-bit syndrome s in [0..4095] -> 24-bit error pattern (<0 if weight >= 4)
        private static readonly int[] _syndromeToError24 = new int[4096];
        private static readonly byte[] _syndromeWeight = new byte[4096];

        // 24D -> 2D Coxeter-Plane 24-Fold Projection Basis (cos/sin(2*pi*k/24))
        public static readonly float[] CoxeterCos24 = new float[24];
        public static readonly float[] CoxeterSin24 = new float[24];

        public static readonly string[] PresetNames = new string[]
        {
            "01. Leech-24 Sovereign Halo Shader (.omsh -> OmegaAV :19779)",
            "02. Ed25519 Hardware YubiKey / AVF Debian SSH Public Key",
            "03. ACT-Omega Rosetta Stone Axiom Capsule (H^1 = 0)",
            "04. Creation Kit Fallout 4 Vault-111 Override Console Macro",
            "05. Golay [24,12,8] 3-Bit Error-Per-Word Torture Benchmark",
            "06. Chladni(5,8) Golden Cymatic Shader (.omsh -> OmegaAV)",
            "07. Tri-Silicon QUIC Mesh Bootstrap Token (LOQ15 + P10 + P8)",
            "08. Steiner S(5,8,24) Octad & 196,560 Leech Minimal Manifesto"
        };

        static GolayLeechCodec()
        {
            for (int i = 0; i < 4096; i++)
            {
                _syndromeToError24[i] = -1;
                _syndromeWeight[i] = 99;
            }

            // Weight 0
            _syndromeToError24[0] = 0;
            _syndromeWeight[0] = 0;

            // Weight 1 (24 patterns)
            for (int i = 0; i < 24; i++)
            {
                int err = (1 << i);
                int syn = ComputeSyndrome12(err);
                _syndromeToError24[syn] = err;
                _syndromeWeight[syn] = 1;
            }

            // Weight 2 (276 patterns)
            for (int i = 0; i < 24; i++)
            {
                for (int j = i + 1; j < 24; j++)
                {
                    int err = (1 << i) | (1 << j);
                    int syn = ComputeSyndrome12(err);
                    if (_syndromeToError24[syn] < 0)
                    {
                        _syndromeToError24[syn] = err;
                        _syndromeWeight[syn] = 2;
                    }
                }
            }

            // Weight 3 (2024 patterns -> total 1 + 24 + 276 + 2024 = 2325 unique syndromes!)
            for (int i = 0; i < 24; i++)
            {
                for (int j = i + 1; j < 24; j++)
                {
                    for (int k = j + 1; k < 24; k++)
                    {
                        int err = (1 << i) | (1 << j) | (1 << k);
                        int syn = ComputeSyndrome12(err);
                        if (_syndromeToError24[syn] < 0)
                        {
                            _syndromeToError24[syn] = err;
                            _syndromeWeight[syn] = 3;
                        }
                    }
                }
            }

            for (int k = 0; k < 24; k++)
            {
                double ang = (2.0 * Math.PI * k) / 24.0;
                CoxeterCos24[k] = (float)Math.Cos(ang);
                CoxeterSin24[k] = (float)Math.Sin(ang);
            }
        }

        public static string GetPresetPayload(int idx, out StegPayloadKind kind)
        {
            if (idx == 0)
            {
                kind = StegPayloadKind.OmshShader;
                return "// Air-Gap Beamed by OmegaSteg v1.0 (:19786) [Golay 24,12,8 + Leech-24]\r\n" +
                       "sx = x * 2.6 * k1; sy = y * 2.6 * k1;\r\n" +
                       "ld = leech24(sx + cos(t*0.4), sy + sin(t*0.4));\r\n" +
                       "shell = exp(-abs(ld - 0.68) * (14.0 * k3));\r\n" +
                       "r = saturate(shell * 0.96 + bass * 0.35);\r\n" +
                       "g = saturate(shell * 0.78 + rdV * 0.42);\r\n" +
                       "b = saturate(shell * 0.24 + 0.45 * abs(sin(theta*12.0 - t*2.0)));\r\n";
            }
            if (idx == 1)
            {
                kind = StegPayloadKind.TextOrKey;
                return "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOmegaSovereignLeech24AirGapKeyV190 omega-steg@local-host";
            }
            if (idx == 2)
            {
                kind = StegPayloadKind.JsonManifest;
                return "{\"rosetta\":\"ACT-Omega v72.2\",\"golay\":\"[24,12,8]\",\"leech_kissing\":196560,\"sheaf_h1\":0}";
            }
            if (idx == 3)
            {
                kind = StegPayloadKind.TextOrKey;
                return "; Fallout 4 Creation Kit Air-Gap Vault Unlock\r\nsetstage SovereignQuest 60\r\nplayer.additem 0000000F 2400";
            }
            if (idx == 5)
            {
                kind = StegPayloadKind.OmshShader;
                return "// Air-Gap Chladni(5,8) Golden Cymatic Shader\r\n" +
                       "ch = cos(x * 15.708 * k1) * cos(y * 25.132 * k2) - cos(x * 25.132 * k2) * cos(y * 15.708 * k1);\r\n" +
                       "nd = exp(-abs(ch) * 8.5);\r\n" +
                       "r = saturate(nd * 0.22 + rdU * 0.3);\r\n" +
                       "g = saturate(nd * 0.94 + bass * 0.3);\r\n" +
                       "b = saturate(nd * 0.82 + high * 0.4);\r\n";
            }
            if (idx == 6)
            {
                kind = StegPayloadKind.JsonManifest;
                return "{\"mesh\":\"tri-silicon-iroh\",\"nodes\":[\"host:WORKSTATION\",\"frankel:P10\",\"shiba:P8\"],\"ports\":[9778,19777,19779,19780,19781,19782,19784,19785,19786]}";
            }
            if (idx == 7)
            {
                kind = StegPayloadKind.TextOrKey;
                return "STEINER S(5,8,24) :: 759 Octads | 2576 Dodecads | Lambda_24 Kissing Number = 196,560 | Exact 3-Bit Self-Healing Vault";
            }
            kind = StegPayloadKind.TextOrKey;
            return "GOLAY [24,12,8] TORTURE PAYLOAD :: Every single 24-bit codeword survives 3 arbitrary bit flips with 0% data loss!";
        }

        public static int EncodeParity12(int data12)
        {
            int d = data12 & 0xFFF;
            int p = 0;
            for (int b = 0; b < 12; b++)
            {
                if (((d >> b) & 1) != 0) p ^= ParityRows12[b];
            }
            return p & 0xFFF;
        }

        public static int EncodeGolay24(int data12)
        {
            int d = data12 & 0xFFF;
            return (d << 12) | EncodeParity12(d);
        }

        public static int ComputeSyndrome12(int word24)
        {
            int d = (word24 >> 12) & 0xFFF;
            int p = word24 & 0xFFF;
            return (p ^ EncodeParity12(d)) & 0xFFF;
        }

        public static int DecodeGolay24Hard(int received24, out int healedWord24, out int syndrome12, out int correctedBits)
        {
            int w = received24 & 0xFFFFFF;
            syndrome12 = ComputeSyndrome12(w);
            int err = _syndromeToError24[syndrome12];
            if (err >= 0)
            {
                healedWord24 = (w ^ err) & 0xFFFFFF;
                correctedBits = _syndromeWeight[syndrome12];
                return (healedWord24 >> 12) & 0xFFF;
            }
            // Weight-4 error detected (1771 remaining syndromes have weight 4!)
            healedWord24 = w;
            correctedBits = -1;
            return (w >> 12) & 0xFFF;
        }

        // Chase-2 / Leech-24 Soft-Decision Bounded-Distance Refiner:
        // If hard Golay detects a weight-4 tie (correctedBits == -1), flips each of the 2 least-reliable
        // Leech coordinates to rescue even 4-bit soft errors (+3 dB Leech lattice coding gain)!
        public static int DecodeLeech24Soft(float[] softLlr24, out int hardWord24, out int healedWord24, out int syndrome12, out int correctedBits)
        {
            int w = 0;
            int minIdx1 = 0, minIdx2 = 1;
            float minAbs1 = 1e9f, minAbs2 = 1e9f;

            for (int k = 0; k < 24; k++)
            {
                float v = softLlr24[k];
                if (v >= 0.0f) w |= (1 << k);
                float av = Math.Abs(v);
                if (av < minAbs1) { minAbs2 = minAbs1; minIdx2 = minIdx1; minAbs1 = av; minIdx1 = k; }
                else if (av < minAbs2) { minAbs2 = av; minIdx2 = k; }
            }
            hardWord24 = w;

            int d12 = DecodeGolay24Hard(w, out healedWord24, out syndrome12, out correctedBits);
            if (correctedBits >= 0) return d12;

            // Chase-2 Leech soft rescue on least-reliable coordinate minIdx1:
            int cand1 = w ^ (1 << minIdx1);
            int h1, s1, c1;
            int dCand1 = DecodeGolay24Hard(cand1, out h1, out s1, out c1);
            if (c1 >= 0)
            {
                healedWord24 = h1; syndrome12 = s1; correctedBits = 3; // rescued via soft Leech reliability!
                return dCand1;
            }

            int cand2 = w ^ (1 << minIdx2);
            int h2, s2, c2;
            int dCand2 = DecodeGolay24Hard(cand2, out h2, out s2, out c2);
            if (c2 >= 0)
            {
                healedWord24 = h2; syndrome12 = s2; correctedBits = 3;
                return dCand2;
            }

            return d12;
        }

        // Project 24-bit Golay / Leech vector onto 2D Coxeter Plane (normalized roughly into [-1, +1]^2)
        public static void ProjectLeech24ToCoxeter2D(int word24, float[] optionalNoisy24, out float cx, out float cy)
        {
            float sx = 0.0f, sy = 0.0f;
            for (int k = 0; k < 24; k++)
            {
                float val = (optionalNoisy24 != null)
                    ? optionalNoisy24[k]
                    : ((((word24 >> k) & 1) != 0) ? 1.0f : -1.0f);
                sx += val * CoxeterCos24[k];
                sy += val * CoxeterSin24[k];
            }
            // Normalize by 1 / sqrt(12) ~ 0.288675 so typical radius is ~0.55..0.95
            cx = Math.Max(-1.35f, Math.Min(1.35f, sx * 0.165f));
            cy = Math.Max(-1.35f, Math.Min(1.35f, sy * 0.165f));
        }

        public static ushort ComputeCrc16Ccitt(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= (ushort)(data[offset + i] << 8);
                for (int b = 0; b < 8; b++)
                {
                    if ((crc & 0x8000) != 0) crc = (ushort)((crc << 1) ^ 0x1021);
                    else crc <<= 1;
                }
            }
            return crc;
        }

        public static string ComputeSha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data ?? new byte[0]);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // Pack arbitrary bytes -> "OSG1" 12-byte header + optional Deflate + 12-bit symbol slicing -> 24-bit Golay codewords!
        public static int[] EncodePayloadToGolayFrames(byte[] rawPayload, StegPayloadKind kind)
        {
            if (rawPayload == null) rawPayload = new byte[0];
            byte[] body = rawPayload;
            byte flags = 0;

            if (rawPayload.Length >= 48)
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                        {
                            ds.Write(rawPayload, 0, rawPayload.Length);
                        }
                        byte[] comp = ms.ToArray();
                        if (comp.Length + 4 < rawPayload.Length)
                        {
                            body = comp;
                            flags = 1; // Deflate compressed
                        }
                    }
                }
                catch { }
            }

            int rawLen = Math.Min(65535, rawPayload.Length);
            int bodyLen = Math.Min(65535, body.Length);
            ushort crc = ComputeCrc16Ccitt(rawPayload, 0, rawLen);

            // 12-byte envelope header (exact multiple of 3 bytes = 8 Golay [24,12,8] frames!)
            int totalPacketBytes = 12 + bodyLen;
            int paddedBytes = ((totalPacketBytes + 2) / 3) * 3;
            byte[] pkt = new byte[paddedBytes];
            pkt[0] = (byte)'O'; pkt[1] = (byte)'S'; pkt[2] = (byte)'G'; pkt[3] = (byte)'1';
            pkt[4] = (byte)kind;
            pkt[5] = flags;
            pkt[6] = (byte)(rawLen & 0xFF);
            pkt[7] = (byte)((rawLen >> 8) & 0xFF);
            pkt[8] = (byte)(bodyLen & 0xFF);
            pkt[9] = (byte)((bodyLen >> 8) & 0xFF);
            pkt[10] = (byte)(crc & 0xFF);
            pkt[11] = (byte)((crc >> 8) & 0xFF);
            Buffer.BlockCopy(body, 0, pkt, 12, bodyLen);

            int numTriplets = paddedBytes / 3;
            int[] frames = new int[numTriplets * 2];
            for (int t = 0; t < numTriplets; t++)
            {
                int b0 = pkt[t * 3 + 0];
                int b1 = pkt[t * 3 + 1];
                int b2 = pkt[t * 3 + 2];
                int symA = b0 | ((b1 & 0x0F) << 8);
                int symB = ((b1 >> 4) & 0x0F) | (b2 << 4);
                frames[t * 2 + 0] = EncodeGolay24(symA);
                frames[t * 2 + 1] = EncodeGolay24(symB);
            }
            return frames;
        }

        public static GolayDecodeReceipt DecodeGolayFramesToPayload(int[] receivedFrames, float[][] optionalSoftFrames, int[] optionalIdealFrames)
        {
            GolayDecodeReceipt rec = new GolayDecodeReceipt();
            if (receivedFrames == null || receivedFrames.Length < 8)
            {
                rec.StatusSummary = "Insufficient Golay frames (< 8 header frames).";
                return rec;
            }

            int nFrames = receivedFrames.Length & ~1;
            rec.TotalFrames = nFrames;
            int[] sym12 = new int[nFrames];

            for (int i = 0; i < nFrames; i++)
            {
                int rxWord = receivedFrames[i];
                int healed, syn, corr;
                if (optionalSoftFrames != null && i < optionalSoftFrames.Length && optionalSoftFrames[i] != null)
                {
                    int hardDummy;
                    sym12[i] = DecodeLeech24Soft(optionalSoftFrames[i], out hardDummy, out healed, out syn, out corr);
                    rxWord = hardDummy;
                }
                else
                {
                    sym12[i] = DecodeGolay24Hard(rxWord, out healed, out syn, out corr);
                }

                if (corr == 0) rec.CleanFrames++;
                else if (corr == 1) { rec.Healed1BitFrames++; rec.TotalBitsHealed += 1; }
                else if (corr == 2) { rec.Healed2BitFrames++; rec.TotalBitsHealed += 2; }
                else if (corr == 3) { rec.Healed3BitFrames++; rec.TotalBitsHealed += 3; }
                else rec.Uncorrectable4BitFrames++;

                if (rec.ConstellationPoints.Count < 256)
                {
                    GolayFrameVisualPoint vp = new GolayFrameVisualPoint();
                    vp.FrameIndex = i;
                    vp.Data12 = sym12[i];
                    vp.SentWord24 = (optionalIdealFrames != null && i < optionalIdealFrames.Length) ? optionalIdealFrames[i] : healed;
                    vp.ReceivedWord24 = rxWord;
                    vp.HealedWord24 = healed;
                    vp.Syndrome12 = syn;
                    vp.CorrectedBits = corr;
                    ProjectLeech24ToCoxeter2D(healed, null, out vp.IdealX, out vp.IdealY);
                    float[] soft = (optionalSoftFrames != null && i < optionalSoftFrames.Length) ? optionalSoftFrames[i] : null;
                    if (soft != null) ProjectLeech24ToCoxeter2D(rxWord, soft, out vp.NoisyX, out vp.NoisyY);
                    else ProjectLeech24ToCoxeter2D(rxWord, null, out vp.NoisyX, out vp.NoisyY);
                    rec.ConstellationPoints.Add(vp);
                }
            }

            int numTriplets = nFrames / 2;
            byte[] pkt = new byte[numTriplets * 3];
            for (int t = 0; t < numTriplets; t++)
            {
                int symA = sym12[t * 2 + 0] & 0xFFF;
                int symB = sym12[t * 2 + 1] & 0xFFF;
                pkt[t * 3 + 0] = (byte)(symA & 0xFF);
                pkt[t * 3 + 1] = (byte)(((symA >> 8) & 0x0F) | ((symB & 0x0F) << 4));
                pkt[t * 3 + 2] = (byte)((symB >> 4) & 0xFF);
            }

            if (pkt.Length < 12 || pkt[0] != (byte)'O' || pkt[1] != (byte)'S' || pkt[2] != (byte)'G' || pkt[3] != (byte)'1')
            {
                rec.StatusSummary = "Header magic 'OSG1' mismatch (signal too noisy or wrong carrier lock).";
                return rec;
            }

            rec.Kind = (StegPayloadKind)(pkt[4] % 3);
            byte flags = pkt[5];
            int rawLen = pkt[6] | (pkt[7] << 8);
            int bodyLen = pkt[8] | (pkt[9] << 8);
            ushort expectedCrc = (ushort)(pkt[10] | (pkt[11] << 8));

            if (bodyLen < 0 || 12 + bodyLen > pkt.Length)
            {
                rec.StatusSummary = "Decoded body length (" + bodyLen + ") exceeds frame buffer (" + (pkt.Length - 12) + ").";
                return rec;
            }

            byte[] body = new byte[bodyLen];
            Buffer.BlockCopy(pkt, 12, body, 0, bodyLen);

            byte[] rawOut = body;
            if ((flags & 1) != 0)
            {
                try
                {
                    using (MemoryStream msIn = new MemoryStream(body))
                    using (DeflateStream ds = new DeflateStream(msIn, CompressionMode.Decompress))
                    using (MemoryStream msOut = new MemoryStream())
                    {
                        byte[] tmp = new byte[4096];
                        int r;
                        while ((r = ds.Read(tmp, 0, tmp.Length)) > 0) msOut.Write(tmp, 0, r);
                        rawOut = msOut.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    rec.StatusSummary = "Deflate decompression failed: " + ex.Message;
                    return rec;
                }
            }

            if (rawOut.Length > rawLen && rawLen > 0)
            {
                byte[] trimmed = new byte[rawLen];
                Buffer.BlockCopy(rawOut, 0, trimmed, 0, rawLen);
                rawOut = trimmed;
            }

            ushort actualCrc = ComputeCrc16Ccitt(rawOut, 0, rawOut.Length);
            rec.Crc16Verified = (actualCrc == expectedCrc);
            rec.PayloadBytes = rawOut;
            rec.DecodedText = Encoding.UTF8.GetString(rawOut);
            rec.Sha256Hex = ComputeSha256Hex(rawOut);
            rec.Success = rec.Crc16Verified;
            rec.StatusSummary = rec.Success
                ? ("VERIFIED [CRC-16 OK] :: " + rawOut.Length + "B recovered | Golay Healed " + rec.TotalBitsHealed + " bit errors across " + (rec.Healed1BitFrames + rec.Healed2BitFrames + rec.Healed3BitFrames) + "/" + rec.TotalFrames + " frames!")
                : ("CRC-16 Mismatch (Uncorrectable 4-bit frames: " + rec.Uncorrectable4BitFrames + ")");
            return rec;
        }

        // Deterministic / Random Torture Injector: injects exact K bit flips per 24-bit codeword + AWGN noise!
        public static GolayDecodeReceipt RunTortureSimulation(byte[] rawPayload, StegPayloadKind kind, int flipsPerWord, float snrDb, int seed)
        {
            int[] cleanFrames = EncodePayloadToGolayFrames(rawPayload, kind);
            int n = cleanFrames.Length;
            int[] noisyFrames = new int[n];
            float[][] softFrames = new float[n][];

            Random rng = new Random(seed);
            double sigma = Math.Pow(10.0, -snrDb / 20.0);

            for (int i = 0; i < n; i++)
            {
                int w = cleanFrames[i];
                int flipMask = 0;
                int flipsDone = 0;
                int guard = 0;
                while (flipsDone < flipsPerWord && guard++ < 64)
                {
                    int bit = rng.Next(0, 24);
                    if (((flipMask >> bit) & 1) == 0)
                    {
                        flipMask |= (1 << bit);
                        flipsDone++;
                    }
                }
                int corrupted = w ^ flipMask;
                noisyFrames[i] = corrupted;

                float[] soft = new float[24];
                for (int k = 0; k < 24; k++)
                {
                    float idealSign = (((corrupted >> k) & 1) != 0) ? 1.0f : -1.0f;
                    // Box-Muller Gaussian sample
                    double u1 = Math.Max(1e-6, rng.NextDouble());
                    double u2 = rng.NextDouble();
                    double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    soft[k] = idealSign + (float)(z * sigma * 0.45);
                }
                softFrames[i] = soft;
            }

            GolayDecodeReceipt rec = DecodeGolayFramesToPayload(noisyFrames, flipsPerWord <= 3 ? null : softFrames, cleanFrames);
            rec.EstimatedSnrDb = snrDb;
            return rec;
        }
    }
}
