using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OmegaPenroseStudio
{
    // 1. 256x256 Gerchberg-Saxton Phase-Only Computer-Generated Laser Hologram Synthesizer!
    public static class OmegaPenroseGerchbergSaxtonCgh
    {
        public const int N = 256;

        public static readonly string[] BuiltInTargets = new string[]
        {
            "0: E8 30-Fold Sacred Star Hologram",
            "1: ACT-Omega Sovereign Crown Glyph",
            "2: Golden Phi Nautilus Spiral",
            "3: 5-Fold Penrose Rose Mandala",
            "4: Custom Image File (.png/.jpg/.bmp)"
        };

        public static float[] SynthesizeFromImageFile(string imagePath, out string labelOut)
        {
            using (Image rawImg = Image.FromFile(imagePath))
            using (Bitmap bmp = new Bitmap(N, N, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    // Fit image inside central 68% of Bragg viewport so it sits cleanly inside the laser field!
                    int box = (int)(N * 0.68f);
                    float aspect = (float)rawImg.Width / Math.Max(1, rawImg.Height);
                    int dw = (aspect >= 1.0f) ? box : (int)Math.Round(box * aspect);
                    int dh = (aspect >= 1.0f) ? (int)Math.Round(box / aspect) : box;
                    g.DrawImage(rawImg, (N - dw) / 2, (N - dh) / 2, dw, dh);
                }
                labelOut = Path.GetFileName(imagePath);
                return RunGerchbergSaxton256(ExtractTargetAmplitude(bmp), 20);
            }
        }

        public static float[] SynthesizeProceduralGlyph(int kind, out string labelOut)
        {
            using (Bitmap bmp = new Bitmap(N, N, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float cx = N * 0.5f, cy = N * 0.5f;

                    if (kind == 0)
                    {
                        labelOut = "E8 30-Fold Sacred Star";
                        using (Pen p1 = new Pen(Color.White, 2.2f))
                        using (Pen p2 = new Pen(Color.FromArgb(180, 255, 255, 255), 1.2f))
                        {
                            PointF[] pts = new PointF[30];
                            for (int i = 0; i < 30; i++)
                            {
                                double a = 2.0 * Math.PI * i / 30.0;
                                pts[i] = new PointF(cx + 84f * (float)Math.Cos(a), cy + 84f * (float)Math.Sin(a));
                            }
                            for (int i = 0; i < 30; i++)
                            {
                                g.DrawLine(p1, pts[i], pts[(i + 11) % 30]);
                                g.DrawLine(p2, pts[i], pts[(i + 7) % 30]);
                            }
                            g.DrawEllipse(p1, cx - 34f, cy - 34f, 68f, 68f);
                        }
                    }
                    else if (kind == 1)
                    {
                        labelOut = "ACT-Omega Sovereign Crown";
                        using (Pen p = new Pen(Color.White, 3.2f))
                        using (Font f = new Font("Segoe UI", 38f, FontStyle.Bold))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawEllipse(p, cx - 80f, cy - 80f, 160f, 160f);
                            PointF[] deca = new PointF[10];
                            for (int i = 0; i < 10; i++)
                            {
                                double a = 2.0 * Math.PI * i / 10.0 - Math.PI / 2.0;
                                float rr = (i % 2 == 0) ? 78f : 48f;
                                deca[i] = new PointF(cx + rr * (float)Math.Cos(a), cy + rr * (float)Math.Sin(a));
                            }
                            g.DrawPolygon(p, deca);
                            g.DrawString("OMEGA", f, Brushes.White, new RectangleF(0, cy - 28f, N, 56f), sf);
                        }
                    }
                    else if (kind == 2)
                    {
                        labelOut = "Golden Phi Nautilus Spiral";
                        using (Pen p = new Pen(Color.White, 2.8f))
                        {
                            PointF prev = new PointF(cx, cy);
                            for (int s = 1; s <= 220; s++)
                            {
                                double t = s * 0.11;
                                double r = 2.4 * Math.Exp(0.145 * t);
                                if (r > 88.0) break;
                                PointF cur = new PointF(cx + (float)(r * Math.Cos(t)), cy + (float)(r * Math.Sin(t)));
                                g.DrawLine(p, prev, cur);
                                prev = cur;
                            }
                        }
                    }
                    else
                    {
                        labelOut = "5-Fold Penrose Rose";
                        using (Pen p = new Pen(Color.White, 2.5f))
                        {
                            PointF prev = PointF.Empty;
                            for (int s = 0; s <= 360; s++)
                            {
                                double a = s * Math.PI / 180.0;
                                double r = 82.0 * Math.Cos(2.5 * a);
                                PointF cur = new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a)));
                                if (s > 0) g.DrawLine(p, prev, cur);
                                prev = cur;
                            }
                            PointF prev2 = PointF.Empty;
                            for (int s = 0; s <= 360; s++)
                            {
                                double a = s * Math.PI / 180.0;
                                double r = 82.0 * Math.Sin(5.0 * a);
                                PointF cur = new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a)));
                                if (s > 0) g.DrawLine(p, prev2, cur);
                                prev2 = cur;
                            }
                        }
                    }
                }
                return RunGerchbergSaxton256(ExtractTargetAmplitude(bmp), 20);
            }
        }

        private static float[] ExtractTargetAmplitude(Bitmap bmp)
        {
            float[] amp = new float[N * N];
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, N, N), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = bd.Stride;
                byte[] raw = new byte[stride * N];
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
                double sum = 0.0;
                for (int y = 0; y < N; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < N; x++)
                    {
                        int o = row + x * 3;
                        float lum = (0.114f * raw[o + 0] + 0.587f * raw[o + 1] + 0.299f * raw[o + 2]) / 255.0f;
                        // Suppress tiny central DC beam-stop zone so laser origin mask doesn't clip image center
                        float dx = x - 128.0f, dy = y - 128.0f;
                        if (dx * dx + dy * dy < 9.0f) lum *= 0.25f;
                        float a = (float)Math.Sqrt(Math.Max(0.0f, lum));
                        amp[y * N + x] = a;
                        sum += a * a;
                    }
                }
                float norm = (sum > 1e-6) ? (float)(128.0 / Math.Sqrt(sum)) : 1.0f;
                for (int i = 0; i < amp.Length; i++) amp[i] *= norm;
            }
            finally { bmp.UnlockBits(bd); }
            return amp;
        }

        // Exact 256x256 Iterative Fourier Gerchberg-Saxton Phase Retrieval!
        private static float[] RunGerchbergSaxton256(float[] targetAmp, int iterations)
        {
            float[] win = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                double ty = y / 255.0;
                double wy = 0.35875 - 0.48829 * Math.Cos(2.0 * Math.PI * ty) + 0.14128 * Math.Cos(4.0 * Math.PI * ty) - 0.01168 * Math.Cos(6.0 * Math.PI * ty);
                for (int x = 0; x < N; x++)
                {
                    double tx = x / 255.0;
                    double wx = 0.35875 - 0.48829 * Math.Cos(2.0 * Math.PI * tx) + 0.14128 * Math.Cos(4.0 * Math.PI * tx) - 0.01168 * Math.Cos(6.0 * Math.PI * tx);
                    win[y * N + x] = (float)Math.Max(0.02, wx * wy);
                }
            }

            double[] re = new double[N * N];
            double[] im = new double[N * N];
            float[] phaseOut = new float[N * N];
            Random rng = new Random(161803);
            for (int i = 0; i < N * N; i++)
                phaseOut[i] = (float)((rng.NextDouble() * 2.0 - 1.0) * Math.PI);

            for (int it = 0; it < iterations; it++)
            {
                // 1. Construct optically centered real-space field A(x,y) * exp(i*phase) * (-1)^(x+y)
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        double sgn = (((x + y) & 1) == 0) ? 1.0 : -1.0;
                        double w = win[i] * sgn;
                        re[i] = w * Math.Cos(phaseOut[i]);
                        im[i] = w * Math.Sin(phaseOut[i]);
                    }
                }

                // 2. Forward 2D FFT -> Bragg diffraction plane
                Fft2D(re, im, false);

                // 3. Enforce target Fourier amplitude while preserving retrieved Fourier phase
                for (int i = 0; i < N * N; i++)
                {
                    double mag = Math.Sqrt(re[i] * re[i] + im[i] * im[i]) + 1e-12;
                    double scale = targetAmp[i] / mag;
                    re[i] *= scale;
                    im[i] *= scale;
                }

                // 4. Inverse 2D FFT -> hologram aperture plane & extract phase!
                Fft2D(re, im, true);
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        double sgn = (((x + y) & 1) == 0) ? 1.0 : -1.0;
                        phaseOut[i] = (float)Math.Atan2(im[i] * sgn, re[i] * sgn);
                    }
                }
            }
            return phaseOut;
        }

        private static void Fft2D(double[] re, double[] im, bool inverse)
        {
            double[] rRow = new double[N], iRow = new double[N];
            for (int y = 0; y < N; y++)
            {
                int off = y * N;
                for (int x = 0; x < N; x++) { rRow[x] = re[off + x]; iRow[x] = im[off + x]; }
                Fft1D256(rRow, iRow, inverse);
                for (int x = 0; x < N; x++) { re[off + x] = rRow[x]; im[off + x] = iRow[x]; }
            }
            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++) { rRow[y] = re[y * N + x]; iRow[y] = im[y * N + x]; }
                Fft1D256(rRow, iRow, inverse);
                for (int y = 0; y < N; y++) { re[y * N + x] = rRow[y]; im[y * N + x] = iRow[y]; }
            }
        }

        private static void Fft1D256(double[] re, double[] im, bool inverse)
        {
            int j = 0;
            for (int i = 0; i < N; i++)
            {
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
                int m = N >> 1;
                while (m >= 1 && j >= m) { j -= m; m >>= 1; }
                j += m;
            }
            double sign = inverse ? 1.0 : -1.0;
            for (int s = 1; s <= 8; s++)
            {
                int m = 1 << s;
                int half = m >> 1;
                double theta = sign * 2.0 * Math.PI / m;
                double wpr = Math.Cos(theta), wpi = Math.Sin(theta);
                for (int k = 0; k < N; k += m)
                {
                    double wr = 1.0, wi = 0.0;
                    for (int p = 0; p < half; p++)
                    {
                        int a = k + p, b = a + half;
                        double tr = wr * re[b] - wi * im[b];
                        double ti = wr * im[b] + wi * re[b];
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr;        im[a] += ti;
                        double nwr = wr * wpr - wi * wpi;
                        wi = wr * wpi + wi * wpr;
                        wr = nwr;
                    }
                }
            }
            if (inverse)
            {
                const double inv = 1.0 / 256.0;
                for (int i = 0; i < N; i++) { re[i] *= inv; im[i] *= inv; }
            }
        }
    }

    // 2. 10-Preset .openrose Factory Patch Bank!
    public sealed class OmegaPenrosePatch
    {
        public string Name = "01. Sovereign Penrose P3 Jewel + Laser Bragg";
        public int SymMode = 0;
        public int ViewMode = 0;
        public int ThemeIdx = 0;
        public int LifeMode = 0;
        public int SpeciesIdx = 0;
        public int FftSource = 0;
        public int FftWin = 0;
        public int LaserMode = 0;
        public int Crystal3dKind = 0;
        public int Shader3dSolid = 0;
        public float ZoomUnits = 14.0f;
        public float Coupling = 0.34f;
        public float MutationRate = 0.18f;
        public int HologramGlyph = -1;

        public override string ToString() { return Name; }

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.AppendLine("# OmegaPenrose v1.0.2 Final GM Patch (.openrose)");
            sb.AppendLine("name=" + Name);
            sb.AppendLine("sym=" + SymMode);
            sb.AppendLine("view=" + ViewMode);
            sb.AppendLine("theme=" + ThemeIdx);
            sb.AppendLine("life_mode=" + LifeMode);
            sb.AppendLine("species=" + SpeciesIdx);
            sb.AppendLine("fft_src=" + FftSource);
            sb.AppendLine("fft_win=" + FftWin);
            sb.AppendLine("laser=" + LaserMode);
            sb.AppendLine("c3d_kind=" + Crystal3dKind);
            sb.AppendLine("c3d_solid=" + Shader3dSolid);
            sb.AppendLine("zoom=" + ZoomUnits.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("coupling=" + Coupling.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("mutation=" + MutationRate.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("holo_glyph=" + HologramGlyph);
            return sb.ToString();
        }

        public static OmegaPenrosePatch LoadFromFile(string path)
        {
            OmegaPenrosePatch p = new OmegaPenrosePatch();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length == 0 || ln.StartsWith("#")) continue;
                int eq = ln.IndexOf('=');
                if (eq <= 0) continue;
                string k = ln.Substring(0, eq).Trim();
                string v = ln.Substring(eq + 1).Trim();
                int iv; float fv;
                if (k == "name") p.Name = v;
                else if (k == "sym" && int.TryParse(v, out iv)) p.SymMode = iv;
                else if (k == "view" && int.TryParse(v, out iv)) p.ViewMode = iv;
                else if (k == "theme" && int.TryParse(v, out iv)) p.ThemeIdx = iv;
                else if (k == "life_mode" && int.TryParse(v, out iv)) p.LifeMode = iv;
                else if (k == "species" && int.TryParse(v, out iv)) p.SpeciesIdx = iv;
                else if (k == "fft_src" && int.TryParse(v, out iv)) p.FftSource = iv;
                else if (k == "fft_win" && int.TryParse(v, out iv)) p.FftWin = iv;
                else if (k == "laser" && int.TryParse(v, out iv)) p.LaserMode = iv;
                else if (k == "c3d_kind" && int.TryParse(v, out iv)) p.Crystal3dKind = iv;
                else if (k == "c3d_solid" && int.TryParse(v, out iv)) p.Shader3dSolid = iv;
                else if (k == "zoom" && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) p.ZoomUnits = fv;
                else if (k == "coupling" && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) p.Coupling = fv;
                else if (k == "mutation" && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) p.MutationRate = fv;
                else if (k == "holo_glyph" && int.TryParse(v, out iv)) p.HologramGlyph = iv;
            }
            return p;
        }

        public static OmegaPenrosePatch[] BuildFactoryBank(string patchesDir)
        {
            Directory.CreateDirectory(patchesDir);
            OmegaPenrosePatch[] bank = new OmegaPenrosePatch[]
            {
                Make("01. Sovereign Penrose P3 Jewel + Laser Bragg",  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 12.5f, 0.34f, 0.18f, -1),
                Make("02. 8D E8 Elser-Sloane H4 Star + Darwinian",    6, 0, 1, 1, 1, 0, 0, 2, 2, 0, 16.0f, 0.42f, 0.32f, -1),
                Make("03. Einstein Hat Monotile (Chiral Anti-Hats)",  7, 0, 0, 0, 0, 0, 0, 0, 1, 0, 13.0f, 0.30f, 0.15f, -1),
                Make("04. Chiral Spectre Tile(1,1) + Worm Surfers",   8, 0, 2, 0, 3, 0, 0, 2, 1, 0, 13.0f, 0.38f, 0.15f, -1),
                Make("05. Kohmoto-Sutherland Quantum Ring States",    0, 0, 3, 2, 4, 0, 0, 2, 0, 0, 15.0f, 0.55f, 0.00f, -1),
                Make("06. 6D Icosahedral Solid Sapphire Jewel 3D",    0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 14.0f, 0.34f, 0.15f, -1),
                Make("07. 8D E8 Gosset 240-Root Solid Star 3D",       6, 5, 1, 1, 1, 0, 0, 1, 2, 0, 15.0f, 0.40f, 0.25f, -1),
                Make("08. Inverse Laser Hologram (E8 Sacred Star)",   0, 0, 2, 0, 0, 4, 0, 0, 0, 0, 14.0f, 0.30f, 0.15f,  0),
                Make("09. 3D Stepped Penrose Citadel + A* Sanctum",   1, 5, 1, 0, 3, 0, 0, 1, 1, 0, 14.5f, 0.40f, 0.15f, -1),
                Make("10. 12-Fold Stampfli + Ruby HeNe Laser Bench",  5, 0, 3, 1, 2, 0, 0, 1, 0, 0, 18.0f, 0.44f, 0.28f, -1)
            };

            UTF8Encoding u8 = new UTF8Encoding(false);
            for (int i = 0; i < bank.Length; i++)
            {
                string fn = string.Format(CultureInfo.InvariantCulture, "patch_{0:00}.openrose", i + 1);
                string fp = Path.Combine(patchesDir, fn);
                if (!File.Exists(fp)) File.WriteAllText(fp, bank[i].Serialize(), u8);
            }
            return bank;
        }

        private static OmegaPenrosePatch Make(
            string name, int sym, int view, int thm, int lm, int sp, int fs, int fw, int las,
            int c3k, int c3s, float zm, float cp, float mut, int holo)
        {
            OmegaPenrosePatch p = new OmegaPenrosePatch();
            p.Name = name; p.SymMode = sym; p.ViewMode = view; p.ThemeIdx = thm;
            p.LifeMode = lm; p.SpeciesIdx = sp; p.FftSource = fs; p.FftWin = fw; p.LaserMode = las;
            p.Crystal3dKind = c3k; p.Shader3dSolid = c3s; p.ZoomUnits = zm;
            p.Coupling = cp; p.MutationRate = mut; p.HologramGlyph = holo;
            return p;
        }
    }

    // 3. Zero-NuGet Motion-JPEG RIFF .AVI Video Recorder!
    public sealed class OmegaPenroseAviRecorder : IDisposable
    {
        private readonly List<byte[]> _jpegFrames = new List<byte[]>();
        private int _width, _height, _fps = 24;
        private long _lastCaptureMs;
        private string _targetPath;
        public bool IsRecording { get; private set; }
        public int FrameCount { get { return _jpegFrames.Count; } }

        public void Start(string exportsDir, int w, int h, int fps)
        {
            Directory.CreateDirectory(exportsDir);
            _jpegFrames.Clear();
            _width = Math.Max(320, w & ~1);
            _height = Math.Max(240, h & ~1);
            _fps = Math.Max(12, Math.Min(30, fps));
            _lastCaptureMs = 0;
            _targetPath = Path.Combine(exportsDir, "omega_penrose_gm_loop_" + DateTime.Now.ToString("HHmmss") + ".avi");
            IsRecording = true;
        }

        public void CaptureFrameIfDue(Control stageCtrl, long elapsedMs)
        {
            if (!IsRecording || stageCtrl == null || !stageCtrl.IsHandleCreated) return;
            long minStep = 1000 / _fps;
            if (elapsedMs - _lastCaptureMs < minStep) return;
            _lastCaptureMs = elapsedMs;

            if (_jpegFrames.Count >= 600) return; // Cap at 25 seconds per clip for lean RAM
            try
            {
                Point org = stageCtrl.PointToScreen(Point.Empty);
                using (Bitmap bmp = new Bitmap(_width, _height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(org, Point.Empty, new Size(_width, _height));
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Jpeg);
                        _jpegFrames.Add(ms.ToArray());
                    }
                }
            }
            catch { }
        }

        public string StopAndSave()
        {
            if (!IsRecording) return "";
            IsRecording = false;
            if (_jpegFrames.Count == 0) return "";

            using (FileStream fs = new FileStream(_targetPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int numFrames = _jpegFrames.Count;
                int maxBytes = 0;
                int moviPayloadBytes = 4; // "movi"
                for (int i = 0; i < numFrames; i++)
                {
                    int sz = _jpegFrames[i].Length;
                    if (sz > maxBytes) maxBytes = sz;
                    moviPayloadBytes += 8 + sz + (sz & 1);
                }
                int idx1PayloadBytes = numFrames * 16;
                int hdrlPayloadBytes = 4 + (8 + 56) + (12 + (8 + 56) + (8 + 40));
                int riffSize = 4 + (8 + hdrlPayloadBytes) + (8 + moviPayloadBytes) + (8 + idx1PayloadBytes);

                WriteFourCC(bw, "RIFF");
                bw.Write(riffSize);
                WriteFourCC(bw, "AVI ");

                // LIST hdrl
                WriteFourCC(bw, "LIST");
                bw.Write(hdrlPayloadBytes);
                WriteFourCC(bw, "hdrl");

                // avih (56 bytes)
                WriteFourCC(bw, "avih");
                bw.Write(56);
                bw.Write((int)(1000000 / _fps)); // dwMicroSecPerFrame
                bw.Write(maxBytes * _fps);       // dwMaxBytesPerSec
                bw.Write(0);                     // dwPaddingGranularity
                bw.Write(0x10);                  // AVIF_HASINDEX
                bw.Write(numFrames);             // dwTotalFrames
                bw.Write(0);                     // dwInitialFrames
                bw.Write(1);                     // dwStreams
                bw.Write(maxBytes);              // dwSuggestedBufferSize
                bw.Write(_width);
                bw.Write(_height);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                // LIST strl
                int strlSize = 4 + (8 + 56) + (8 + 40);
                WriteFourCC(bw, "LIST");
                bw.Write(strlSize);
                WriteFourCC(bw, "strl");

                // strh (56 bytes)
                WriteFourCC(bw, "strh");
                bw.Write(56);
                WriteFourCC(bw, "vids");
                WriteFourCC(bw, "MJPG");
                bw.Write(0);                     // dwFlags
                bw.Write((ushort)0); bw.Write((ushort)0);
                bw.Write(0);                     // dwInitialFrames
                bw.Write(1);                     // dwScale
                bw.Write(_fps);                  // dwRate
                bw.Write(0);                     // dwStart
                bw.Write(numFrames);             // dwLength
                bw.Write(maxBytes);              // dwSuggestedBufferSize
                bw.Write(-1);                    // dwQuality
                bw.Write(0);                     // dwSampleSize
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)_width); bw.Write((short)_height);

                // strf (BITMAPINFOHEADER = 40 bytes)
                WriteFourCC(bw, "strf");
                bw.Write(40);
                bw.Write(40);                    // biSize
                bw.Write(_width);
                bw.Write(_height);
                bw.Write((ushort)1);             // biPlanes
                bw.Write((ushort)24);            // biBitCount
                WriteFourCC(bw, "MJPG");         // biCompression
                bw.Write(_width * _height * 3);  // biSizeImage
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                // LIST movi
                WriteFourCC(bw, "LIST");
                bw.Write(moviPayloadBytes);
                WriteFourCC(bw, "movi");

                int[] offsets = new int[numFrames];
                int curOffset = 4; // offset from 'movi' tag
                for (int i = 0; i < numFrames; i++)
                {
                    offsets[i] = curOffset;
                    byte[] jpg = _jpegFrames[i];
                    WriteFourCC(bw, "00dc");
                    bw.Write(jpg.Length);
                    bw.Write(jpg);
                    if ((jpg.Length & 1) != 0) bw.Write((byte)0);
                    curOffset += 8 + jpg.Length + (jpg.Length & 1);
                }

                // idx1
                WriteFourCC(bw, "idx1");
                bw.Write(idx1PayloadBytes);
                for (int i = 0; i < numFrames; i++)
                {
                    WriteFourCC(bw, "00dc");
                    bw.Write(0x10); // AVIIF_KEYFRAME
                    bw.Write(offsets[i]);
                    bw.Write(_jpegFrames[i].Length);
                }
            }
            _jpegFrames.Clear();
            return _targetPath;
        }

        private static void WriteFourCC(BinaryWriter bw, string fourcc)
        {
            bw.Write(Encoding.ASCII.GetBytes(fourcc));
        }

        public void Dispose()
        {
            IsRecording = false;
            _jpegFrames.Clear();
        }
    }
}
