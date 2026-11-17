using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace OmegaStegStudio
{
    public static class OmegaStegVisualVault
    {
        public static readonly string[] ThemeNames = new string[]
        {
            "0: Sovereign Emerald & Gold Mandala",
            "1: Cyber Violet & Leech Cyan Star",
            "2: Crimson Cohomology & Obsidian",
            "3: Blueprint Sapphire & Silver"
        };

        // Render a lossless 768x768 24-fold Coxeter Leech Lattice Mandala AND embed
        // the Golay [24,12,8] frames across 2 LSBs/channel (6 bits/pixel = 4 pixels/Golay24 word)!
        public static Bitmap BuildVisualVaultBitmap(int[] golayFrames, int themeIdx)
        {
            const int S = 768;
            Bitmap bmp = new Bitmap(S, S, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            int stride = bd.Stride;
            byte[] rgb = new byte[stride * S];

            float half = S * 0.5f;
            for (int y = 0; y < S; y++)
            {
                float dy = (y - half) / half;
                int rowOff = y * stride;
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - half) / half;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);
                    double ang = Math.Atan2(dy, dx);

                    // 24-fold Coxeter-plane Leech minimal shell interference pattern!
                    double c24 = Math.Cos(24.0 * ang) * Math.Exp(-Math.Abs(r - 0.68) * 7.5);
                    double c12 = Math.Cos(12.0 * ang + r * 9.0) * Math.Exp(-Math.Abs(r - 0.42) * 8.0);
                    double octadWave = Math.Cos(8.0 * ang - r * 14.0) * Math.Exp(-r * 1.6);
                    double rings = Math.Exp(-Math.Abs(Math.Sin(r * 18.8495)) * 12.0) * (r < 0.92f ? 0.55 : 0.12);

                    double v = Math.Max(0.0, Math.Min(1.0, 0.12 + 0.48 * Math.Abs(c24) + 0.36 * Math.Abs(c12) + 0.28 * Math.Abs(octadWave) + rings));

                    int cr, cg, cb;
                    if (themeIdx == 1)
                    {
                        cr = ClampByte(24 + v * 175 + c24 * 45);
                        cg = ClampByte(18 + v * 185 + c12 * 55);
                        cb = ClampByte(48 + v * 205);
                    }
                    else if (themeIdx == 2)
                    {
                        cr = ClampByte(32 + v * 215);
                        cg = ClampByte(14 + v * 75 + rings * 80);
                        cb = ClampByte(28 + v * 125);
                    }
                    else if (themeIdx == 3)
                    {
                        cr = ClampByte(14 + v * 95 + rings * 90);
                        cg = ClampByte(28 + v * 165);
                        cb = ClampByte(46 + v * 215);
                    }
                    else
                    {
                        cr = ClampByte(12 + v * 145 + Math.Max(0.0, c12) * 95);
                        cg = ClampByte(26 + v * 205);
                        cb = ClampByte(22 + v * 150 + Math.Max(0.0, c24) * 65);
                    }

                    int p = rowOff + x * 3;
                    rgb[p + 0] = (byte)cb; // BGR in Format24bppRgb
                    rgb[p + 1] = (byte)cg;
                    rgb[p + 2] = (byte)cr;
                }
            }

            // Prepend 2 Golay24 header words storing [Magic 0x5A7, FrameCount (up to 4095)]
            int nData = Math.Min(4095, golayFrames.Length);
            int[] stream = new int[2 + nData];
            stream[0] = GolayLeechCodec.EncodeGolay24(0x5A7);
            stream[1] = GolayLeechCodec.EncodeGolay24(nData);
            Array.Copy(golayFrames, 0, stream, 2, nData);

            // Embed each 24-bit Golay codeword across 4 consecutive pixels (6 bits/pixel = 2 LSBs in B, G, R)!
            int totalPix = S * S;
            for (int wIdx = 0; wIdx < stream.Length; wIdx++)
            {
                int word24 = stream[wIdx];
                for (int sub = 0; sub < 4; sub++)
                {
                    int pixIdx = wIdx * 4 + sub;
                    if (pixIdx >= totalPix) break;
                    int py = pixIdx / S;
                    int px = pixIdx % S;
                    int off = py * stride + px * 3;

                    int sixBits = (word24 >> (sub * 6)) & 0x3F;
                    int b2 = sixBits & 0x03;
                    int g2 = (sixBits >> 2) & 0x03;
                    int r2 = (sixBits >> 4) & 0x03;

                    rgb[off + 0] = (byte)((rgb[off + 0] & 0xFC) | b2);
                    rgb[off + 1] = (byte)((rgb[off + 1] & 0xFC) | g2);
                    rgb[off + 2] = (byte)((rgb[off + 2] & 0xFC) | r2);
                }
            }

            Marshal.Copy(rgb, 0, bd.Scan0, rgb.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }

        public static GolayDecodeReceipt ExtractPayloadFromVaultBitmap(Bitmap srcBmp)
        {
            GolayDecodeReceipt fail = new GolayDecodeReceipt();
            if (srcBmp == null || srcBmp.Width < 32 || srcBmp.Height < 32)
            {
                fail.StatusSummary = "Invalid image dimensions for Leech-24 Visual Vault.";
                return fail;
            }

            using (Bitmap bmp = new Bitmap(srcBmp.Width, srcBmp.Height, PixelFormat.Format24bppRgb))
            {
                // Copy pixel-exact without color-space scaling
                for (int y = 0; y < srcBmp.Height; y++)
                {
                    for (int x = 0; x < srcBmp.Width; x++)
                        bmp.SetPixel(x, y, srcBmp.GetPixel(x, y));
                }

                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int stride = bd.Stride;
                byte[] rgb = new byte[stride * bmp.Height];
                Marshal.Copy(bd.Scan0, rgb, 0, rgb.Length);
                bmp.UnlockBits(bd);

                int w = bmp.Width, h = bmp.Height;
                int maxWords = (w * h) / 4;
                if (maxWords < 10)
                {
                    fail.StatusSummary = "Image too small to hold Golay [24,12,8] Vault header.";
                    return fail;
                }

                int w0 = ReadGolayWordAt(rgb, stride, w, 0);
                int w1 = ReadGolayWordAt(rgb, stride, w, 1);
                int h0, s0, c0, h1, s1, c1;
                int magic12 = GolayLeechCodec.DecodeGolay24Hard(w0, out h0, out s0, out c0);
                int nData = GolayLeechCodec.DecodeGolay24Hard(w1, out h1, out s1, out c1);

                if (magic12 != 0x5A7 || nData < 8 || 2 + nData > maxWords)
                {
                    fail.StatusSummary = "No OmegaSteg Leech-24 Visual Vault signature (0x5A7) found in PNG.";
                    return fail;
                }

                int[] frames = new int[nData];
                for (int i = 0; i < nData; i++)
                    frames[i] = ReadGolayWordAt(rgb, stride, w, 2 + i);

                return GolayLeechCodec.DecodeGolayFramesToPayload(frames, null, null);
            }
        }

        // Torture-test an embedded Vault Bitmap by flipping up to 3 LSB bits inside EVERY 4-pixel block!
        public static Bitmap InjectStegoBitRot(Bitmap cleanVaultBmp, int bitFlipsPerWord, out int totalFlippedBits)
        {
            totalFlippedBits = 0;
            Bitmap copy = new Bitmap(cleanVaultBmp.Width, cleanVaultBmp.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(copy)) g.DrawImageUnscaled(cleanVaultBmp, 0, 0);

            BitmapData bd = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int stride = bd.Stride;
            byte[] rgb = new byte[stride * copy.Height];
            Marshal.Copy(bd.Scan0, rgb, 0, rgb.Length);

            int w = copy.Width;
            int totalWords = Math.Min(1024, (copy.Width * copy.Height) / 4);
            Random rng = new Random(24128);
            for (int wIdx = 0; wIdx < totalWords; wIdx++)
            {
                int mask = 0, done = 0;
                while (done < bitFlipsPerWord)
                {
                    int b = rng.Next(0, 24);
                    if (((mask >> b) & 1) == 0) { mask |= (1 << b); done++; }
                }
                for (int b = 0; b < 24; b++)
                {
                    if (((mask >> b) & 1) == 0) continue;
                    int sub = b / 6;
                    int rem = b % 6;
                    int chan = rem / 2;     // 0=B, 1=G, 2=R
                    int bitInChan = rem % 2; // bit 0 or bit 1
                    int pixIdx = wIdx * 4 + sub;
                    int py = pixIdx / w, px = pixIdx % w;
                    rgb[py * stride + px * 3 + chan] ^= (byte)(1 << bitInChan);
                    totalFlippedBits++;
                }
            }

            Marshal.Copy(rgb, 0, bd.Scan0, rgb.Length);
            copy.UnlockBits(bd);
            return copy;
        }

        private static int ReadGolayWordAt(byte[] rgb, int stride, int width, int wordIdx)
        {
            int word24 = 0;
            for (int sub = 0; sub < 4; sub++)
            {
                int pixIdx = wordIdx * 4 + sub;
                int py = pixIdx / width;
                int px = pixIdx % width;
                int off = py * stride + px * 3;
                int b2 = rgb[off + 0] & 0x03;
                int g2 = rgb[off + 1] & 0x03;
                int r2 = rgb[off + 2] & 0x03;
                int sixBits = b2 | (g2 << 2) | (r2 << 4);
                word24 |= (sixBits << (sub * 6));
            }
            return word24 & 0xFFFFFF;
        }

        private static int ClampByte(double v)
        {
            int iv = (int)Math.Round(v);
            if (iv < 4) return 4;
            if (iv > 251) return 251;
            return iv;
        }
    }
}
