using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaAVStudio
{
 // Exact 24D Leech Lattice (Lambda_24) Minimal Norm-4 Shell via Extended Binary Golay Code G_24 [24,12,8]
 public static class GolayLeech24Math
 {
 public static readonly int[] GolayCodewords = new int[4096];
 public static readonly int[] GolayOctads;
 public static readonly double[][] Vectors = new double[720][];
 public static readonly byte[] VecShape = new byte[720];
 public static readonly int[] EdgeA;
 public static readonly int[] EdgeB;

 static GolayLeech24Math()
 {
 int[] nonQr0 = new int[] { 0, 2, 6, 7, 8, 10 };
 int[] gRows = new int[12];
 gRows[0] = 1 | (0xFFE << 12);
 for (int r = 0; r < 11; r++)
 {
 int bMask = 1;
 for (int k = 0; k < nonQr0.Length; k++) bMask |= (1 << (((r + nonQr0[k]) % 11) + 1));
 gRows[r + 1] = (1 << (r + 1)) | (bMask << 12);
 }

 List<int> octs = new List<int>(759);
 for (int m = 0; m < 4096; m++)
 {
 int cw = 0;
 for (int k = 0; k < 12; k++) if (((m >> k) & 1) != 0) cw ^= gRows[k];
 GolayCodewords[m] = cw;
 if (PopCount24(cw) == 8) octs.Add(cw);
 }
 GolayOctads = octs.ToArray();

 double invS8 = 1.0 / Math.Sqrt(8.0);
 int idx = 0;
 int[] steps = new int[] { 1, 3, 8 };
 List<int> pairA = new List<int>(60), pairB = new List<int>(60);
 for (int si = 0; si < steps.Length && pairA.Count < 60; si++)
 {
 int st = steps[si];
 for (int i = 0; i < 24 && pairA.Count < 60; i++)
 {
 int a = Math.Min(i, (i + st) % 24), b = Math.Max(i, (i + st) % 24);
 bool dup = false;
 for (int p = 0; p < pairA.Count; p++) if (pairA[p] == a && pairB[p] == b) { dup = true; break; }
 if (!dup) { pairA.Add(a); pairB.Add(b); }
 }
 }
 for (int p = 0; p < 60; p++)
 for (int s1 = -1; s1 <= 1; s1 += 2)
 for (int s2 = -1; s2 <= 1; s2 += 2)
 {
 double[] v = new double[24];
 v[pairA[p]] = 4.0 * s1 * invS8; v[pairB[p]] = 4.0 * s2 * invS8;
 VecShape[idx] = 0; Vectors[idx++] = v;
 }

 for (int o = 0; o < 120; o++)
 {
 int cw = GolayOctads[(o * 6) % GolayOctads.Length];
 double[] vp = new double[24], vn = new double[24];
 int k = 0;
 for (int bit = 0; bit < 24; bit++)
 {
 if (((cw >> bit) & 1) != 0) { double val = ((k & 1) != 0 ? -2.0 : 2.0) * invS8; vp[bit] = val; vn[bit] = -val; k++; }
 }
 VecShape[idx] = 1; Vectors[idx++] = vp;
 VecShape[idx] = 1; Vectors[idx++] = vn;
 }

 for (int o = 0; o < 120; o++)
 {
 int cw = GolayOctads[(o * 6 + 3) % GolayOctads.Length];
 double[] vp = new double[24], vn = new double[24];
 int j0 = o % 24;
 for (int bit = 0; bit < 24; bit++)
 {
 double z = (bit == j0) ? -3.0 : 1.0;
 if (((cw >> bit) & 1) != 0) z = -z;
 vp[bit] = z * invS8; vn[bit] = -z * invS8;
 }
 VecShape[idx] = 2; Vectors[idx++] = vp;
 VecShape[idx] = 2; Vectors[idx++] = vn;
 }

 List<int> ea = new List<int>(8000), eb = new List<int>(8000);
 for (int i = 0; i < 720; i++)
 {
 double[] vi = Vectors[i];
 for (int j = i + 1; j < 720; j++)
 {
 double[] vj = Vectors[j]; double dot = 0.0;
 for (int k = 0; k < 24; k++) dot += vi[k] * vj[k];
 if (dot > 1.998 && dot < 2.002) { ea.Add(i); eb.Add(j); }
 }
 }
 EdgeA = ea.ToArray(); EdgeB = eb.ToArray();
 }

 public static int PopCount24(int x) { int c = 0, v = x & 0xFFFFFF; while (v != 0) { v &= (v - 1); c++; } return c; }

 public static double Leech24VoronoiDist(double x, double y, double z, double t)
 {
 bool sA, sB, sC; int hA, hB, hC;
 double c120 = -0.5, s120 = 0.86602540378;
 double xB = x * c120 - y * s120, yB = x * s120 + y * c120, xC = x * c120 + y * s120, yC = -x * s120 + y * c120;
 double dA = E8LatticeMath.FastVoronoiCellDist(x, y, z, t * 0.35, x + y, x - y, z * 0.5, 0.0, out sA, out hA);
 double dB = E8LatticeMath.FastVoronoiCellDist(xB, yB, z * c120, -t * 0.28, xB - z, yB + z, 0.25, 0.0, out sB, out hB);
 double dC = E8LatticeMath.FastVoronoiCellDist(xC, yC, -z * c120, t * 0.21, yC - xC, xC + z, -0.25, 0.0, out sC, out hC);
 double parityPenalty = ((sA ? 1 : 0) ^ (sB ? 1 : 0) ^ (sC ? 1 : 0)) != 0 ? 0.14 : 0.0;
 return Math.Sqrt((dA * dA + dB * dB + dC * dC) / 3.0) + parityPenalty;
 }

 public static double NearestGolayNormWeight(double u) { int idx = Math.Abs((int)Math.Floor(u * 64.0)) & 4095; return PopCount24(GolayCodewords[idx]) / 24.0; }

 public static double QuantizeTuryn24Sqnr(double[] bands8, double t, out int activeLeechIdx)
 {
 double sig = 1e-12, err = 1e-12;
 double[] v24 = new double[24];
 for (int blk = 0; blk < 3; blk++)
 {
 double[] x8 = new double[8]; double ph = blk * 2.0943951;
 for (int i = 0; i < 8; i++)
 {
 double val = (bands8[i] - 0.42) * 2.4 + 0.45 * Math.Sin(t * (0.6 + 0.15 * blk) + i + ph);
 x8[i] = val; v24[blk * 8 + i] = val; sig += val * val;
 }
 double sq; int rIdx;
 double[] q8 = E8LatticeMath.QuantizeE8(x8, out sq, out rIdx);
 for (int i = 0; i < 8; i++) { double d = x8[i] - q8[i]; err += d * d; }
 }
 int best = 0; double bestDot = -999.0;
 for (int i = 0; i < 720; i++)
 {
 double[] lv = Vectors[i]; double dot = 0.0;
 for (int k = 0; k < 24; k++) dot += v24[k] * lv[k];
 if (dot > bestDot) { bestDot = dot; best = i; }
 }
 activeLeechIdx = best;
 return 10.0 * Math.Log10(sig / Math.Max(1e-12, err));
 }
 }

 // CPU Fallback Xiaolin Wu (SIGGRAPH 1991) Sub-Pixel AA Line & Vertex Rasterizer
 public static class WuSubpixelRaster
 {
 public static void DrawLineUnified(int[] fb, int w, int h, double x0, double y0, double x1, double y1, int cr, int cg, int cb, int rasterMode)
 {
 if (rasterMode == 1 || (rasterMode == 2 && ((x0 + x1) * 0.5) >= (w >> 1)))
 {
 BresenhamRaster.DrawLineAdditive(fb, w, h, (int)Math.Round(x0), (int)Math.Round(y0), (int)Math.Round(x1), (int)Math.Round(y1), cr, cg, cb);
 return;
 }
 if ((x0 < -64.0 && x1 < -64.0) || (x0 > w + 64.0 && x1 > w + 64.0) || (y0 < -64.0 && y1 < -64.0) || (y0 > h + 64.0 && y1 > h + 64.0)) return;
 bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
 if (steep) { double tx = x0; x0 = y0; y0 = tx; tx = x1; x1 = y1; y1 = tx; }
 if (x0 > x1) { double tx = x0; x0 = x1; x1 = tx; double ty = y0; y0 = y1; y1 = ty; }
 double dx = x1 - x0, dy = y1 - y0;
 if (dx > 4096.0) return;
 double gradient = (dx < 1e-6) ? 1.0 : (dy / dx);

 double xend = Math.Round(x0), yend = y0 + gradient * (xend - x0), xgap = 1.0 - FPart(x0 + 0.5);
 int xpxl1 = (int)xend, ypxl1 = (int)Math.Floor(yend); double fy1 = FPart(yend);
 if (steep) { PlotW(fb, w, h, ypxl1, xpxl1, cr, cg, cb, (int)((1.0 - fy1) * xgap * 255.0)); PlotW(fb, w, h, ypxl1 + 1, xpxl1, cr, cg, cb, (int)(fy1 * xgap * 255.0)); }
 else       { PlotW(fb, w, h, xpxl1, ypxl1, cr, cg, cb, (int)((1.0 - fy1) * xgap * 255.0)); PlotW(fb, w, h, xpxl1, ypxl1 + 1, cr, cg, cb, (int)(fy1 * xgap * 255.0)); }
 double intery = yend + gradient;

 xend = Math.Round(x1); yend = y1 + gradient * (xend - x1); xgap = FPart(x1 + 0.5);
 int xpxl2 = (int)xend, ypxl2 = (int)Math.Floor(yend); double fy2 = FPart(yend);
 if (steep) { PlotW(fb, w, h, ypxl2, xpxl2, cr, cg, cb, (int)((1.0 - fy2) * xgap * 255.0)); PlotW(fb, w, h, ypxl2 + 1, xpxl2, cr, cg, cb, (int)(fy2 * xgap * 255.0)); }
 else       { PlotW(fb, w, h, xpxl2, ypxl2, cr, cg, cb, (int)((1.0 - fy2) * xgap * 255.0)); PlotW(fb, w, h, xpxl2, ypxl2 + 1, cr, cg, cb, (int)(fy2 * xgap * 255.0)); }

 int interFix = (int)(intery * 65536.0), gradFix = (int)(gradient * 65536.0);
 int startX = Math.Max(-2, xpxl1 + 1);
 if (startX > xpxl1 + 1) interFix += (startX - (xpxl1 + 1)) * gradFix;
 int endX = Math.Min(steep ? (h + 2) : (w + 2), xpxl2);
 for (int x = startX; x < endX; x++)
 {
 int yInt = interFix >> 16, frac8 = (interFix >> 8) & 255;
 if (steep) { PlotW(fb, w, h, yInt, x, cr, cg, cb, 255 - frac8); PlotW(fb, w, h, yInt + 1, x, cr, cg, cb, frac8); }
 else       { PlotW(fb, w, h, x, yInt, cr, cg, cb, 255 - frac8); PlotW(fb, w, h, x, yInt + 1, cr, cg, cb, frac8); }
 interFix += gradFix;
 }
 }

 public static void DrawVertexUnified(int[] fb, int w, int h, double cx, double cy, double rad, int cr, int cg, int cb, int rasterMode)
 {
 if (rasterMode == 1 || (rasterMode == 2 && cx >= (w >> 1)))
 {
 BresenhamRaster.DrawDiscAdditive(fb, w, h, (int)Math.Round(cx), (int)Math.Round(cy), Math.Max(1, (int)Math.Round(rad)), cr, cg, cb); return;
 }
 int xMin = Math.Max(0, (int)Math.Floor(cx - rad - 1.0)), xMax = Math.Min(w - 1, (int)Math.Ceiling(cx + rad + 1.0));
 int yMin = Math.Max(0, (int)Math.Floor(cy - rad - 1.0)), yMax = Math.Min(h - 1, (int)Math.Ceiling(cy + rad + 1.0));
 for (int py = yMin; py <= yMax; py++)
 {
 double dy = py - cy;
 for (int px = xMin; px <= xMax; px++)
 {
 double dx = px - cx, cov = rad + 0.55 - Math.Sqrt(dx * dx + dy * dy);
 if (cov <= 0.0) continue;
 PlotW(fb, w, h, px, py, cr, cg, cb, (int)(Math.Min(1.0, cov) * 255.0));
 }
 }
 }

 private static double FPart(double v) { return v - Math.Floor(v); }
 private static void PlotW(int[] fb, int w, int h, int x, int y, int cr, int cg, int cb, int a255)
 {
 if ((uint)x >= (uint)w || (uint)y >= (uint)h || a255 <= 0) return;
 int idx = y * w + x, cur = fb[idx];
 int r = Math.Min(255, ((cur >> 16) & 255) + ((cr * a255) >> 8));
 int g = Math.Min(255, ((cur >> 8) & 255) + ((cg * a255) >> 8));
 int b = Math.Min(255, (cur & 255) + ((cb * a255) >> 8));
 fb[idx] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
 }
 }

 public sealed partial class OmegaAvForm
 {
 private sealed class RecFrameItem { public int Index, W, H; public int[] Pixels; }
 private ConcurrentQueue<RecFrameItem> _recQueue;
 private volatile bool _liveRecActive = false;
 private int _liveRecFrames = 0;
 private double _liveRecStartSec = 0.0, _liveRecNextSec = 0.0;
 private string _liveRecDir = null;
 private Task _liveRecWriterTask = null;

 private void ToggleLivePerformanceRecord()
 {
 if (!_liveRecActive)
 {
 _liveRecDir = Path.Combine(_rendersDir, "live_rec_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
 Directory.CreateDirectory(_liveRecDir);
 File.WriteAllText(Path.Combine(_liveRecDir, "shader_live.omsh"), _txtPixelCode.Text + "\r\n===OMEGA_PROJ8D===\r\n" + _txtProjCode.Text, new UTF8Encoding(false));
 _recQueue = new ConcurrentQueue<RecFrameItem>();
 _liveRecFrames = 0;
 _liveRecStartSec = _simClock.Elapsed.TotalSeconds;
 _liveRecNextSec = _liveRecStartSec;
 _wasapi.BeginLiveAudioRecord();
 _liveRecActive = true;
 _btnRecLive.Text = "STOP REC (0f)";
 _btnRecLive.BackColor = Color.FromArgb(220, 38, 38);

 string targetDir = _liveRecDir;
 ConcurrentQueue<RecFrameItem> qRef = _recQueue;
 _liveRecWriterTask = Task.Run(delegate
 {
 while (_liveRecActive || !qRef.IsEmpty)
 {
 RecFrameItem item;
 if (qRef.TryDequeue(out item))
 {
  try
  {
  using (Bitmap bmp = new Bitmap(item.W, item.H, PixelFormat.Format32bppPArgb))
  {
  BitmapData bd = bmp.LockBits(new Rectangle(0, 0, item.W, item.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
  try { Marshal.Copy(item.Pixels, 0, bd.Scan0, item.Pixels.Length); } finally { bmp.UnlockBits(bd); }
  bmp.Save(Path.Combine(targetDir, "frame_" + item.Index.ToString("0000") + ".png"), ImageFormat.Png);
  }
  }
  catch { }
 }
 else Thread.Sleep(6);
 }
 });
 return;
 }

 // Stop and finalize live WASAPI + GPU recording asynchronously
 _liveRecActive = false;
 _btnRecLive.Enabled = false;
 _btnRecLive.Text = "Saving MP4...";
 double elapsedSec = Math.Max(0.2, _simClock.Elapsed.TotalSeconds - _liveRecStartSec);
 int totalCap = Math.Max(1, _liveRecFrames);
 int effFps = Math.Max(10, Math.Min(60, (int)Math.Round(totalCap / elapsedSec)));
 short[] recL, recR; int recRate;
 _wasapi.EndLiveAudioRecord(out recL, out recR, out recRate);
 if (recL.Length < 512)
 {
 recRate = CymaticAudioEngine.SampleRate;
 int nSmp = Math.Min(_audio.TotalSamples, (int)(elapsedSec * recRate));
 recL = new short[nSmp]; recR = new short[nSmp];
 Array.Copy(_audio.PcmLeft, recL, nSmp); Array.Copy(_audio.PcmRight, recR, nSmp);
 }
 string outDir = _liveRecDir;
 Task writerTask = _liveRecWriterTask;
 Task.Run(delegate
 {
 try { if (writerTask != null) writerTask.Wait(20000); } catch { }
 byte[] wavBytes = CymaticAudioEngine.EncodeRiffWav(recL, recR, recRate);
 File.WriteAllBytes(Path.Combine(outDir, "live_audio.wav"), wavBytes);
 string ffCmd = "ffmpeg -y -framerate " + effFps + " -i frame_%04d.png -i live_audio.wav -c:v libx264 -pix_fmt yuv420p -crf 17 -c:a aac -b:a 256k -shortest omega_live_capture.mp4\r\n";
 File.WriteAllText(Path.Combine(outDir, "Stitch-Live-MP4.ps1"), ffCmd, new UTF8Encoding(false));
 bool mp4Ok = false;
 try
 {
 ProcessStartInfo psi = new ProcessStartInfo("ffmpeg.exe", "-y -framerate " + effFps + " -i \"" + Path.Combine(outDir, "frame_%04d.png") + "\" -i \"" + Path.Combine(outDir, "live_audio.wav") + "\" -c:v libx264 -pix_fmt yuv420p -crf 17 -c:a aac -shortest \"" + Path.Combine(outDir, "omega_live_capture.mp4") + "\"");
 psi.CreateNoWindow = true; psi.UseShellExecute = false;
 using (Process p = Process.Start(psi)) { p.WaitForExit(35000); mp4Ok = File.Exists(Path.Combine(outDir, "omega_live_capture.mp4")); }
 }
 catch { }
 try { Process.Start("explorer.exe", "\"" + outDir + "\""); } catch { }
 try
 {
 BeginInvoke((Action)delegate
 {
 _btnRecLive.Enabled = true; _btnRecLive.Text = "Rec Live"; _btnRecLive.BackColor = Color.FromArgb(153, 27, 27);
 _lblHud.Text = "Saved Live Session (" + totalCap + " frames @ " + effFps + "fps + WAV" + (mp4Ok ? " + MP4" : "") + ") -> " + outDir;
 });
 }
 catch { }
 });
 }

 private void OpenOffline1080pExportDialog()
 {
 _modalPaused = true;
 using (Form dlg = new Form())
 {
 dlg.Text = "OmegaAV v0.7.1 :: Deterministic 1080p / 1440p / 4K UHD Full-VRAM GPU Exporter";
 dlg.ClientSize = new Size(510, 250);
 dlg.FormBorderStyle = FormBorderStyle.FixedDialog; dlg.MaximizeBox = false; dlg.MinimizeBox = false;
 dlg.StartPosition = FormStartPosition.CenterParent;
 dlg.BackColor = Color.FromArgb(11, 16, 28); dlg.ForeColor = Color.White;

 Label l1 = new Label(); l1.Text = "Target Resolution:"; l1.SetBounds(18, 20, 140, 22);
 ComboBox cbRes = MakeCombo(new string[] { "1920 x 1080 (Full HD 1080p)", "2560 x 1440 (QHD 1440p)", "3840 x 2160 (4K UHD DirectCompute)", "1280 x 720 (HD 720p Fast)" }, 310);
 cbRes.Location = new Point(170, 18);

 Label l2 = new Label(); l2.Text = "Sequence Duration:"; l2.SetBounds(18, 56, 140, 22);
 ComboBox cbDur = MakeCombo(new string[] { "2.0s @ 30 FPS (60 PNG Frames)", "4.0s @ 30 FPS (120 PNG Frames)", "4.0s @ 60 FPS (240 PNG Frames)", "8.0s @ 30 FPS (240 PNG Frames)" }, 310);
 cbDur.Location = new Point(170, 54);

 ProgressBar pb = new ProgressBar(); pb.SetBounds(18, 102, 472, 24);
 Label lStat = new Label(); lStat.SetBounds(18, 134, 472, 40); lStat.Font = new Font("Consolas", 8.5f);
 lStat.ForeColor = Color.FromArgb(56, 189, 248);
 lStat.Text = "Renders 100% GPU Leech24 + Atomic Wu AA + ACES Post-FX PNGs, 48kHz WAV, .OMSH, and FFmpeg MP4.";

 Button bStart = MakeBtn("Render Offline Sequence", Color.FromArgb(5, 150, 105)); bStart.SetBounds(18, 190, 220, 36);
 Button bCancel = MakeBtn("Close / Cancel", Color.FromArgb(127, 29, 29)); bCancel.SetBounds(268, 190, 222, 36);

 bool cancelReq = false, busy = false;
 bCancel.Click += delegate { if (busy) cancelReq = true; else dlg.Close(); };

 bStart.Click += async delegate
 {
 if (busy) return;
 busy = true; cancelReq = false; bStart.Enabled = false;
 int outW = (cbRes.SelectedIndex == 0) ? 1920 : (cbRes.SelectedIndex == 1 ? 2560 : (cbRes.SelectedIndex == 2 ? 3840 : 1280));
 int outH = (cbRes.SelectedIndex == 0) ? 1080 : (cbRes.SelectedIndex == 1 ? 1440 : (cbRes.SelectedIndex == 2 ? 2160 : 720));
 int fps = (cbDur.SelectedIndex == 2) ? 60 : 30;
 int totalFrames = (cbDur.SelectedIndex == 0) ? 60 : (cbDur.SelectedIndex == 1 ? 120 : 240);
 pb.Maximum = totalFrames; pb.Value = 0;

 string seqDir = Path.Combine(_rendersDir, "seq_" + outH + "p_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
 Directory.CreateDirectory(seqDir);
 File.WriteAllText(Path.Combine(seqDir, "shader_source.omsh"), _txtPixelCode.Text + "\r\n===OMEGA_PROJ8D===\r\n" + _txtProjCode.Text, new UTF8Encoding(false));

 int wavSamples = Math.Min(_audio.TotalSamples, (totalFrames * CymaticAudioEngine.SampleRate) / fps);
 short[] subL = new short[wavSamples], subR = new short[wavSamples];
 Array.Copy(_audio.PcmLeft, subL, wavSamples); Array.Copy(_audio.PcmRight, subR, wavSamples);
 byte[] wavBytes = CymaticAudioEngine.EncodeRiffWav(subL, subR, CymaticAudioEngine.SampleRate);
 File.WriteAllBytes(Path.Combine(seqDir, "audio_48khz.wav"), wavBytes);

 string ffScript = "ffmpeg -y -framerate " + fps + " -i frame_%04d.png -i audio_48khz.wav -c:v libx264 -pix_fmt yuv420p -crf 16 -c:a aac -b:a 256k -shortest omega_av_" + outH + "p.mp4\r\n";
 File.WriteAllText(Path.Combine(seqDir, "Stitch-FFmpeg-MP4.ps1"), ffScript, new UTF8Encoding(false));

 Stopwatch sw = Stopwatch.StartNew();
 await Task.Run(delegate
 {
 int[] hdFb = new int[outW * outH];
 using (Bitmap hdBmp = new Bitmap(outW, outH, PixelFormat.Format32bppPArgb))
 {
  for (int f = 0; f < totalFrames; f++)
  {
  if (cancelReq) break;
  double simT = (double)f / fps;
  RenderSceneToBuffer(outW, outH, hdFb, simT, true, false);
  BitmapData bd = hdBmp.LockBits(new Rectangle(0, 0, outW, outH), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
  try { Marshal.Copy(hdFb, 0, bd.Scan0, hdFb.Length); } finally { hdBmp.UnlockBits(bd); }
  hdBmp.Save(Path.Combine(seqDir, "frame_" + f.ToString("0000") + ".png"), ImageFormat.Png);
  int done = f + 1;
  double rate = done / Math.Max(0.05, sw.Elapsed.TotalSeconds);
  try { dlg.BeginInvoke((Action)delegate { pb.Value = done; lStat.Text = "Rendered GPU frame " + done + "/" + totalFrames + " (" + outW + "x" + outH + ") | " + rate.ToString("0.0") + " fps"; }); } catch { }
  }
 }
 });

 bool mp4Built = false;
 if (!cancelReq)
 {
 try
 {
  ProcessStartInfo psi = new ProcessStartInfo("ffmpeg.exe", "-y -framerate " + fps + " -i \"" + Path.Combine(seqDir, "frame_%04d.png") + "\" -i \"" + Path.Combine(seqDir, "audio_48khz.wav") + "\" -c:v libx264 -pix_fmt yuv420p -crf 16 -c:a aac -shortest \"" + Path.Combine(seqDir, "omega_av_" + outH + "p.mp4") + "\"");
  psi.CreateNoWindow = true; psi.UseShellExecute = false;
  using (Process p = Process.Start(psi)) { p.WaitForExit(45000); mp4Built = File.Exists(Path.Combine(seqDir, "omega_av_" + outH + "p.mp4")); }
 }
 catch { }
 string sha = Sha256Bytes(wavBytes);
 string rec = "{\"resolution\":\"" + outW + "x" + outH + "\",\"fps\":" + fps + ",\"frames\":" + totalFrames + ",\"mp4_muxed\":" + (mp4Built ? "true" : "false") + ",\"wav_sha256\":\"" + sha + "\"}";
 File.WriteAllText(Path.Combine(seqDir, "manifest_1080p.receipt.json"), rec, new UTF8Encoding(false));
 try { Process.Start("explorer.exe", "\"" + seqDir + "\""); } catch { }
 lStat.ForeColor = Color.FromArgb(52, 211, 153);
 lStat.Text = "DONE! Exported " + totalFrames + " GPU PNGs (" + outW + "x" + outH + ") + 48kHz WAV" + (mp4Built ? " + MP4!" : " + Stitch script!");
 }
 else lStat.Text = "Render canceled.";
 busy = false; bStart.Enabled = true;
 };

 dlg.Controls.AddRange(new Control[] { l1, cbRes, l2, cbDur, pb, lStat, bStart, bCancel });
 dlg.ShowDialog(this);
 }
 _modalPaused = false;
 }

 private void SaveShaderFile()
 {
 _modalPaused = true;
 using (SaveFileDialog sfd = new SaveFileDialog())
 {
 sfd.InitialDirectory = _shadersDir; sfd.Filter = "Omega Math Shader (*.omsh)|*.omsh";
 sfd.FileName = "omega_v06_shader_" + DateTime.UtcNow.ToString("HHmmss") + ".omsh";
 if (sfd.ShowDialog(this) == DialogResult.OK)
 {
 File.WriteAllText(sfd.FileName, _txtPixelCode.Text + "\r\n===OMEGA_PROJ8D===\r\n" + _txtProjCode.Text, new UTF8Encoding(false));
 _lblJitStatus.Text = "Saved shader -> " + Path.GetFileName(sfd.FileName);
 }
 }
 _modalPaused = false;
 }

 private void OpenShaderFile()
 {
 _modalPaused = true;
 using (OpenFileDialog ofd = new OpenFileDialog())
 {
 ofd.InitialDirectory = _shadersDir; ofd.Filter = "Omega Math Shader (*.omsh)|*.omsh";
 if (ofd.ShowDialog(this) == DialogResult.OK)
 {
 string[] parts = File.ReadAllText(ofd.FileName, Encoding.UTF8).Split(new string[] { "===OMEGA_PROJ8D===" }, StringSplitOptions.None);
 _txtPixelCode.Text = parts[0].Trim();
 if (parts.Length > 1) _txtProjCode.Text = parts[1].Trim();
 _cmbShader.SelectedIndex = 5;
 CompileBothEditors();
 }
 }
 _modalPaused = false;
 }

 private static string Sha256Bytes(byte[] data)
 {
 using (SHA256 s = SHA256.Create())
 {
 byte[] h = s.ComputeHash(data);
 StringBuilder sb = new StringBuilder(64);
 for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
 return sb.ToString();
 }
 }

 private void BeamToConnectedPixels()
 {
 ThreadPool.QueueUserWorkItem(delegate
 {
 string adb = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe");
  if (!File.Exists(adb)) adb = File.Exists(@"C:\platform-tools\adb.exe") ? @"C:\platform-tools\adb.exe" : "adb.exe";
  try
  {
   ProcessStartInfo psi = new ProcessStartInfo(adb, "devices");
 psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true;
 string raw = "";
 using (Process p = Process.Start(psi)) { raw = p.StandardOutput.ReadToEnd(); p.WaitForExit(2000); }
 foreach (string ln in raw.Split('\n'))
 {
 string tr = ln.Trim(); if (!tr.EndsWith("\tdevice")) continue;
 string ser = tr.Split('\t')[0].Trim();
 RunQuiet(adb, "-s " + ser + " reverse tcp:19779 tcp:19779");
 RunQuiet(adb, "-s " + ser + " shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19779/\"");
 }
 }
 catch { }
 });
 _lblHud.Text = "Beamed OmegaAV v0.7.1 Full-VRAM GPU OLED stream -> http://127.0.0.1:19779/ on connected Pixels!";
 }

 private static void RunQuiet(string exe, string args)
 {
 try { ProcessStartInfo p = new ProcessStartInfo(exe, args); p.CreateNoWindow = true; p.UseShellExecute = false; using (Process pr = Process.Start(p)) pr.WaitForExit(2200); }
 catch { }
 }

 }

 internal static class AviRiffMuxer
 {
  public static bool TryMuxPngFolderAndPcmToAvi(string dir, int frameCount, int fps, short[] pcmL, short[] pcmR, int sampleRate, string outAviPath)
  {
   try
   {
    if (frameCount <= 0 || !Directory.Exists(dir)) return false;
    string firstPng = Path.Combine(dir, "frame_0000.png");
    if (!File.Exists(firstPng)) return false;
    int width = 1280, height = 720;
    List<byte[]> jpegs = new List<byte[]>(frameCount);
    int maxJpg = 0;
    for (int i = 0; i < frameCount; i++)
    {
     string fp = Path.Combine(dir, "frame_" + i.ToString("0000") + ".png");
     if (!File.Exists(fp)) break;
     using (Bitmap bm = new Bitmap(fp))
     using (MemoryStream ms = new MemoryStream())
     {
      if (i == 0) { width = bm.Width; height = bm.Height; }
      bm.Save(ms, ImageFormat.Jpeg);
      byte[] jb = ms.ToArray();
      if (jb.Length > maxJpg) maxJpg = jb.Length;
      jpegs.Add(jb);
     }
    }
    if (jpegs.Count == 0) return false;

    int audSamples = (pcmL != null && pcmR != null) ? Math.Min(pcmL.Length, pcmR.Length) : 0;
    byte[] pcmBytes = new byte[audSamples * 4];
    int p = 0;
    for (int s = 0; s < audSamples; s++)
    {
     short l = pcmL[s], r = pcmR[s];
     pcmBytes[p] = (byte)(l & 255); pcmBytes[p + 1] = (byte)((l >> 8) & 255);
     pcmBytes[p + 2] = (byte)(r & 255); pcmBytes[p + 3] = (byte)((r >> 8) & 255);
     p += 4;
    }

    using (FileStream fs = new FileStream(outAviPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
    using (BinaryWriter bw = new BinaryWriter(fs))
    {
     bw.Write(Encoding.ASCII.GetBytes("RIFF"));
     long riffSizePos = fs.Position; bw.Write((uint)0);
     bw.Write(Encoding.ASCII.GetBytes("AVI "));

     bw.Write(Encoding.ASCII.GetBytes("LIST"));
     long hdrlSizePos = fs.Position; bw.Write((uint)0);
     long hdrlStart = fs.Position;
     bw.Write(Encoding.ASCII.GetBytes("hdrl"));

     uint usecPerFrame = (uint)(1000000 / Math.Max(1, fps));
     uint streams = (uint)(pcmBytes.Length > 0 ? 2 : 1);
     bw.Write(Encoding.ASCII.GetBytes("avih"));
     bw.Write((uint)56);
     bw.Write(usecPerFrame);
     bw.Write((uint)(maxJpg * fps + sampleRate * 4));
     bw.Write((uint)0);
     bw.Write((uint)0x110);
     bw.Write((uint)jpegs.Count);
     bw.Write((uint)0);
     bw.Write(streams);
     bw.Write((uint)Math.Max(65536, maxJpg));
     bw.Write((uint)width);
     bw.Write((uint)height);
     bw.Write((uint)0); bw.Write((uint)0); bw.Write((uint)0); bw.Write((uint)0);

     // Stream 0: Video MJPG
     bw.Write(Encoding.ASCII.GetBytes("LIST"));
     long strl0Pos = fs.Position; bw.Write((uint)0);
     long strl0Start = fs.Position;
     bw.Write(Encoding.ASCII.GetBytes("strl"));
     bw.Write(Encoding.ASCII.GetBytes("strh"));
     bw.Write((uint)56);
     bw.Write(Encoding.ASCII.GetBytes("vids"));
     bw.Write(Encoding.ASCII.GetBytes("MJPG"));
     bw.Write((uint)0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((uint)0);
     bw.Write((uint)1); bw.Write((uint)fps); bw.Write((uint)0); bw.Write((uint)jpegs.Count);
     bw.Write((uint)Math.Max(65536, maxJpg)); bw.Write((uint)10000); bw.Write((uint)0);
     bw.Write((short)0); bw.Write((short)0); bw.Write((short)width); bw.Write((short)height);

     bw.Write(Encoding.ASCII.GetBytes("strf"));
     bw.Write((uint)40);
     bw.Write((uint)40); bw.Write((int)width); bw.Write((int)height);
     bw.Write((ushort)1); bw.Write((ushort)24);
     bw.Write(Encoding.ASCII.GetBytes("MJPG"));
     bw.Write((uint)(width * height * 3));
     bw.Write((int)0); bw.Write((int)0); bw.Write((uint)0); bw.Write((uint)0);
     PatchSize(fs, bw, strl0Pos, strl0Start);

     // Stream 1: Audio PCM 16-bit Stereo
     if (pcmBytes.Length > 0)
     {
      bw.Write(Encoding.ASCII.GetBytes("LIST"));
      long strl1Pos = fs.Position; bw.Write((uint)0);
      long strl1Start = fs.Position;
      bw.Write(Encoding.ASCII.GetBytes("strl"));
      bw.Write(Encoding.ASCII.GetBytes("strh"));
      bw.Write((uint)56);
      bw.Write(Encoding.ASCII.GetBytes("auds"));
      bw.Write((uint)1);
      bw.Write((uint)0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((uint)0);
      bw.Write((uint)4); bw.Write((uint)(sampleRate * 4)); bw.Write((uint)0);
      bw.Write((uint)audSamples); bw.Write((uint)65536); bw.Write((uint)10000); bw.Write((uint)4);
      bw.Write((short)0); bw.Write((short)0); bw.Write((short)0); bw.Write((short)0);

      bw.Write(Encoding.ASCII.GetBytes("strf"));
      bw.Write((uint)16);
      bw.Write((ushort)1); bw.Write((ushort)2);
      bw.Write((uint)sampleRate); bw.Write((uint)(sampleRate * 4));
      bw.Write((ushort)4); bw.Write((ushort)16);
      PatchSize(fs, bw, strl1Pos, strl1Start);
     }
     PatchSize(fs, bw, hdrlSizePos, hdrlStart);

     // LIST movi
     bw.Write(Encoding.ASCII.GetBytes("LIST"));
     long moviSizePos = fs.Position; bw.Write((uint)0);
     long moviStart = fs.Position;
     bw.Write(Encoding.ASCII.GetBytes("movi"));

     List<uint[]> idxEntries = new List<uint[]>(jpegs.Count + 2);
     for (int i = 0; i < jpegs.Count; i++)
     {
      byte[] jb = jpegs[i];
      uint off = (uint)(fs.Position - moviStart);
      bw.Write(Encoding.ASCII.GetBytes("00dc"));
      bw.Write((uint)jb.Length);
      bw.Write(jb);
      if ((jb.Length & 1) == 1) bw.Write((byte)0);
      idxEntries.Add(new uint[] { 0x63643030u, 0x10u, off, (uint)jb.Length });
     }
     if (pcmBytes.Length > 0)
     {
      uint aOff = (uint)(fs.Position - moviStart);
      bw.Write(Encoding.ASCII.GetBytes("01wb"));
      bw.Write((uint)pcmBytes.Length);
      bw.Write(pcmBytes);
      if ((pcmBytes.Length & 1) == 1) bw.Write((byte)0);
      idxEntries.Add(new uint[] { 0x62773130u, 0x10u, aOff, (uint)pcmBytes.Length });
     }
     PatchSize(fs, bw, moviSizePos, moviStart);

     // idx1 index chunk
     bw.Write(Encoding.ASCII.GetBytes("idx1"));
     bw.Write((uint)(idxEntries.Count * 16));
     for (int k = 0; k < idxEntries.Count; k++)
     {
      uint[] en = idxEntries[k];
      bw.Write(en[0]); bw.Write(en[1]); bw.Write(en[2]); bw.Write(en[3]);
     }
     PatchSize(fs, bw, riffSizePos, 8L);
    }
    return File.Exists(outAviPath);
   }
   catch { return false; }
  }

  private static void PatchSize(FileStream fs, BinaryWriter bw, long sizePos, long startPos)
  {
   long endPos = fs.Position;
   fs.Position = sizePos;
   bw.Write((uint)(endPos - startPos));
   fs.Position = endPos;
  }
 }
}
