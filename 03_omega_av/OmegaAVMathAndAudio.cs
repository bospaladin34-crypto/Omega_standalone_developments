using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmegaAVStudio
{
 public static class E8LatticeMath
 {
  public static readonly double[][] Roots = Build240Roots();
  public static readonly int[] EdgeA;
  public static readonly int[] EdgeB;
  public static readonly byte[] RootRing = new byte[240];

  // Exact orthonormal E8 Coxeter eigenplanes (m=1 and m=7, 30-fold symmetry)
  public static readonly double[] U1 = new double[] { 0.0, 0.0, -0.11911770, -0.11911770, -0.19273649, -0.19273649, -0.21763887, -0.92193305 };
  public static readonly double[] U2 = new double[] { -0.09689907, 0.66982357, -0.46350566, 0.46350566, -0.23693033, 0.23693033, 0.0, 0.0 };
  public static readonly double[] U3 = new double[] { 0.0, 0.0, -0.38336132, -0.38336132, 0.23693033, 0.23693033, 0.74996791, -0.17704341 };
  public static readonly double[] U4 = new double[] { -0.15941060, 0.54488358, 0.11911770, -0.11911770, 0.56978596, -0.56978596, 0.0, 0.0 };

  static E8LatticeMath()
  {
   List<int> ea = new List<int>(6720);
   List<int> eb = new List<int>(6720);
   double[] ringCuts = new double[] { 0.30, 0.43, 0.52, 0.64, 0.73, 0.84, 1.02, 2.0 };
   for (int i = 0; i < 240; i++)
   {
    double px = Dot8(Roots[i], U1), py = Dot8(Roots[i], U2);
    double rad = Math.Sqrt(px * px + py * py);
    byte rIdx = 7;
    for (byte c = 0; c < 8; c++) { if (rad < ringCuts[c]) { rIdx = c; break; } }
    RootRing[i] = rIdx;

    for (int j = i + 1; j < 240; j++)
    {
     double d = Dot8(Roots[i], Roots[j]);
     if (d > 0.999 && d < 1.001) { ea.Add(i); eb.Add(j); }
    }
   }
   EdgeA = ea.ToArray();
   EdgeB = eb.ToArray();
  }

  private static double[][] Build240Roots()
  {
   double[][] list = new double[240][];
   int n = 0;
   for (int i = 0; i < 8; i++)
   {
    for (int j = i + 1; j < 8; j++)
    {
     for (int s1 = -1; s1 <= 1; s1 += 2)
     {
      for (int s2 = -1; s2 <= 1; s2 += 2)
      {
       double[] v = new double[8];
       v[i] = s1; v[j] = s2;
       list[n++] = v;
      }
     }
    }
   }
   for (int mask = 0; mask < 256; mask++)
   {
    int bits = 0;
    for (int k = 0; k < 8; k++) if (((mask >> k) & 1) != 0) bits++;
    if ((bits & 1) == 0)
    {
     double[] v = new double[8];
     for (int k = 0; k < 8; k++) v[k] = ((mask >> k) & 1) != 0 ? -0.5 : 0.5;
     list[n++] = v;
    }
   }
   return list;
  }

  public static double Dot8(double[] a, double[] b)
  {
   return a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3] + a[4] * b[4] + a[5] * b[5] + a[6] * b[6] + a[7] * b[7];
  }

  // Allocation-free O(8) Voronoi cell distance for per-pixel 8D E8 Honeycomb Shader
  public static double FastVoronoiCellDist(double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7, out bool isSpinorCoset, out int cellHash)
  {
   int h1, h2;
   double d1 = DistSqD8(x0, x1, x2, x3, x4, x5, x6, x7, 0.0, out h1);
   double d2 = DistSqD8(x0, x1, x2, x3, x4, x5, x6, x7, 0.5, out h2);
   if (d2 < d1) { isSpinorCoset = true; cellHash = h2; return Math.Sqrt(d2); }
   isSpinorCoset = false; cellHash = h1; return Math.Sqrt(d1);
  }

  private static double DistSqD8(double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7, double sh, out int hash)
  {
   double u0 = x0 - sh, u1 = x1 - sh, u2 = x2 - sh, u3 = x3 - sh, u4 = x4 - sh, u5 = x5 - sh, u6 = x6 - sh, u7 = x7 - sh;
   int z0 = (int)Math.Floor(u0 + 0.5), z1 = (int)Math.Floor(u1 + 0.5), z2 = (int)Math.Floor(u2 + 0.5), z3 = (int)Math.Floor(u3 + 0.5);
   int z4 = (int)Math.Floor(u4 + 0.5), z5 = (int)Math.Floor(u5 + 0.5), z6 = (int)Math.Floor(u6 + 0.5), z7 = (int)Math.Floor(u7 + 0.5);
   double e0 = u0 - z0, e1 = u1 - z1, e2 = u2 - z2, e3 = u3 - z3, e4 = u4 - z4, e5 = u5 - z5, e6 = u6 - z6, e7 = u7 - z7;
   int sum = z0 + z1 + z2 + z3 + z4 + z5 + z6 + z7;
   if ((sum & 1) != 0)
   {
    double a0 = Math.Abs(e0), a1 = Math.Abs(e1), a2 = Math.Abs(e2), a3 = Math.Abs(e3);
    double a4 = Math.Abs(e4), a5 = Math.Abs(e5), a6 = Math.Abs(e6), a7 = Math.Abs(e7);
    double mx = a0; int idx = 0;
    if (a1 > mx) { mx = a1; idx = 1; } if (a2 > mx) { mx = a2; idx = 2; } if (a3 > mx) { mx = a3; idx = 3; }
    if (a4 > mx) { mx = a4; idx = 4; } if (a5 > mx) { mx = a5; idx = 5; } if (a6 > mx) { mx = a6; idx = 6; }
    if (a7 > mx) { idx = 7; }
    if (idx == 0) { z0 += (e0 > 0 ? 1 : -1); e0 = u0 - z0; }
    else if (idx == 1) { z1 += (e1 > 0 ? 1 : -1); e1 = u1 - z1; }
    else if (idx == 2) { z2 += (e2 > 0 ? 1 : -1); e2 = u2 - z2; }
    else if (idx == 3) { z3 += (e3 > 0 ? 1 : -1); e3 = u3 - z3; }
    else if (idx == 4) { z4 += (e4 > 0 ? 1 : -1); e4 = u4 - z4; }
    else if (idx == 5) { z5 += (e5 > 0 ? 1 : -1); e5 = u5 - z5; }
    else if (idx == 6) { z6 += (e6 > 0 ? 1 : -1); e6 = u6 - z6; }
    else { z7 += (e7 > 0 ? 1 : -1); e7 = u7 - z7; }
   }
   hash = ((z0 * 73856093) ^ (z1 * 19349663) ^ (z2 * 83492791) ^ (z3 * 26544357) ^ (z4 * 49979687)) & 0x7FFFFFFF;
   return e0 * e0 + e1 * e1 + e2 * e2 + e3 * e3 + e4 * e4 + e5 * e5 + e6 * e6 + e7 * e7;
  }

  public static double[] QuantizeE8(double[] x, out double sqnrDb, out int nearestRootIdx)
  {
   double[] q1 = NearestD8(x, 0.0);
   double[] q2 = NearestD8(x, 0.5);
   double e1 = 0.0, e2 = 0.0, sig = 1e-12;
   for (int i = 0; i < 8; i++)
   {
    sig += x[i] * x[i];
    double d1 = x[i] - q1[i], d2 = x[i] - q2[i];
    e1 += d1 * d1; e2 += d2 * d2;
   }
   double[] best = (e1 <= e2) ? q1 : q2;
   sqnrDb = 10.0 * Math.Log10(sig / Math.Max(1e-12, Math.Min(e1, e2)));
   int bestIdx = 0; double bestDot = -999.0;
   for (int r = 0; r < 240; r++)
   {
    double d = Dot8(x, Roots[r]);
    if (d > bestDot) { bestDot = d; bestIdx = r; }
   }
   nearestRootIdx = bestIdx;
   return best;
  }

  private static double[] NearestD8(double[] x, double shift)
  {
   double[] z = new double[8];
   int sum = 0, worstIdx = 0; double worstDiff = -1.0;
   for (int i = 0; i < 8; i++)
   {
    double u = x[i] - shift;
    double r = Math.Floor(u + 0.5);
    z[i] = r; sum += (int)r;
    double df = Math.Abs(u - r);
    if (df > worstDiff) { worstDiff = df; worstIdx = i; }
   }
   if ((sum & 1) != 0)
   {
    double u = x[worstIdx] - shift;
    z[worstIdx] += (u > z[worstIdx]) ? 1.0 : -1.0;
   }
   if (shift != 0.0) for (int i = 0; i < 8; i++) z[i] += shift;
   return z;
  }
 }

 // Jack E. Bresenham (1965) Pure-Integer Zero-Float Line & Additive Glow Rasterizer
 public static class BresenhamRaster
 {
  public static void DrawLineAdditive(int[] fb, int w, int h, int x0, int y0, int x1, int y1, int cr, int cg, int cb)
  {
   if ((x0 < 0 && x1 < 0) || (x0 >= w && x1 >= w) || (y0 < 0 && y1 < 0) || (y0 >= h && y1 >= h)) return;
   int dx = x1 - x0; if (dx < 0) dx = -dx;
   int dy = y1 - y0; if (dy > 0) dy = -dy;
   if (dx > 4096 || dy < -4096) return;
   int sx = (x0 < x1) ? 1 : -1;
   int sy = (y0 < y1) ? 1 : -1;
   int err = dx + dy;
   for (int steps = 0; steps < 4096; steps++)
   {
    if ((uint)x0 < (uint)w && (uint)y0 < (uint)h)
    {
     int idx = y0 * w + x0;
     int cur = fb[idx];
     int r = ((cur >> 16) & 255) + cr; if (r > 255) r = 255;
     int g = ((cur >> 8) & 255) + cg;  if (g > 255) g = 255;
     int b = (cur & 255) + cb;         if (b > 255) b = 255;
     fb[idx] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }
    if (x0 == x1 && y0 == y1) break;
    int e2 = err << 1;
    if (e2 >= dy) { err += dy; x0 += sx; }
    if (e2 <= dx) { err += dx; y0 += sy; }
   }
  }

  public static void DrawDiscAdditive(int[] fb, int w, int h, int cx, int cy, int rad, int cr, int cg, int cb)
  {
   int r2 = rad * rad;
   for (int dy = -rad; dy <= rad; dy++)
   {
    int py = cy + dy; if ((uint)py >= (uint)h) continue;
    for (int dx = -rad; dx <= rad; dx++)
    {
     if (dx * dx + dy * dy > r2) continue;
     int px = cx + dx; if ((uint)px >= (uint)w) continue;
     int idx = py * w + px;
     int cur = fb[idx];
     int r = ((cur >> 16) & 255) + cr; if (r > 255) r = 255;
     int g = ((cur >> 8) & 255) + cg;  if (g > 255) g = 255;
     int b = (cur & 255) + cb;         if (b > 255) b = 255;
     fb[idx] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }
   }
  }
 }

 // Zero-Privilege Loopback TCP Pixel OLED Streamer (:19779)
 public sealed class PixelOledLoopbackServer
 {
  private TcpListener _listener;
  private volatile bool _running;
  public volatile byte[] LatestJpeg = new byte[0];
  public volatile string LatestShaderOmsh = "";
  public volatile bool RemoteSplashRequested = false;
  public volatile bool RemoteCycleFluidRequested = false;
  public volatile float RemoteWandX = 0f;
  public volatile float RemoteWandY = 0f;
  public long RemoteWandUtcTicks = 0L;

  public void Start(int port)
  {
   try
   {
    _listener = new TcpListener(IPAddress.Loopback, port);
    _listener.Start();
    _running = true;
    ThreadPool.QueueUserWorkItem(delegate
    {
     while (_running)
     {
      try
      {
       TcpClient client = _listener.AcceptTcpClient();
       ThreadPool.QueueUserWorkItem(HandleClient, client);
      }
      catch { if (!_running) break; }
     }
    });
   }
   catch { }
  }

  public void Stop()
  {
   _running = false;
   try { if (_listener != null) _listener.Stop(); } catch { }
  }

  private void HandleClient(object state)
  {
   using (TcpClient c = (TcpClient)state)
   {
    try
    {
     c.ReceiveTimeout = 2000; c.SendTimeout = 2000;
     NetworkStream ns = c.GetStream();
     byte[] req = new byte[1024];
     int rd = ns.Read(req, 0, req.Length);
     string head = rd > 0 ? Encoding.ASCII.GetString(req, 0, rd) : "GET / ";
           if (head.StartsWith("GET /frame.jpg", StringComparison.OrdinalIgnoreCase))
      {
       byte[] jpg = LatestJpeg;
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\nCache-Control: no-store\r\nContent-Length: " + jpg.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length);
       if (jpg.Length > 0) ns.Write(jpg, 0, jpg.Length);
      }
      else if (head.StartsWith("GET /download/snapshot.jpg", StringComparison.OrdinalIgnoreCase))
      {
       byte[] jpg = LatestJpeg;
       string fn = "omega_av_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".jpg";
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\nContent-Disposition: attachment; filename=\"" + fn + "\"\r\nContent-Length: " + jpg.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length);
       if (jpg.Length > 0) ns.Write(jpg, 0, jpg.Length);
      }
      else if (head.StartsWith("GET /download/active.omsh", StringComparison.OrdinalIgnoreCase))
      {
       byte[] om = Encoding.UTF8.GetBytes(LatestShaderOmsh ?? "");
       string fn = "omega_av_shader_" + DateTime.UtcNow.ToString("HHmmss") + ".omsh";
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Disposition: attachment; filename=\"" + fn + "\"\r\nContent-Length: " + om.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length);
       if (om.Length > 0) ns.Write(om, 0, om.Length);
      }
      else if (head.StartsWith("GET /api/splash", StringComparison.OrdinalIgnoreCase))
      {
       RemoteSplashRequested = true;
       byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true,\"action\":\"splash\"}");
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + ok.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length); ns.Write(ok, 0, ok.Length);
      }
      else if (head.StartsWith("GET /api/fluid", StringComparison.OrdinalIgnoreCase))
      {
       RemoteCycleFluidRequested = true;
       byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true,\"action\":\"cycle_fluid\"}");
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + ok.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length); ns.Write(ok, 0, ok.Length);
      }
      else if (head.StartsWith("GET /api/wand", StringComparison.OrdinalIgnoreCase))
      {
       int qIdx = head.IndexOf('?');
       int spEnd = head.IndexOf(' ', 4);
       if (qIdx > 0 && spEnd > qIdx)
       {
        string qs = head.Substring(qIdx + 1, spEnd - qIdx - 1);
        string[] kv = qs.Split('&');
        for (int k = 0; k < kv.Length; k++)
        {
         float v;
         if (kv[k].StartsWith("x=", StringComparison.OrdinalIgnoreCase) && float.TryParse(kv[k].Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
          RemoteWandX = Math.Max(-1f, Math.Min(1f, v));
         else if (kv[k].StartsWith("y=", StringComparison.OrdinalIgnoreCase) && float.TryParse(kv[k].Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
          RemoteWandY = Math.Max(-1f, Math.Min(1f, v));
        }
        System.Threading.Interlocked.Exchange(ref RemoteWandUtcTicks, DateTime.UtcNow.Ticks);
       }
       byte[] ok = Encoding.UTF8.GetBytes("{\"ok\":true,\"action\":\"wand_orbit\"}");
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + ok.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length); ns.Write(ok, 0, ok.Length);
      }
      else
      {
       string html = "<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,maximum-scale=1'><title>OmegaAV v0.7.1 OLED Deck</title>" +
        "<style>*{box-sizing:border-box;margin:0;padding:0;font-family:Consolas,monospace}body{background:#000;color:#38bdf8;display:flex;flex-direction:column;align-items:center;justify-content:space-between;min-height:100vh;padding:10px}" +
        ".hdr{font-size:12px;font-weight:700;letter-spacing:.06em;color:#a78bfa;padding:4px 10px;border:1px solid #312e81;border-radius:99px}" +
        "img{width:100%;max-height:74vh;object-fit:contain;border-radius:6px}" +
        ".bar{display:flex;gap:8px;flex-wrap:wrap;width:100%;justify-content:center;padding-bottom:6px}" +
        "button,a.btn{padding:10px 12px;border-radius:8px;border:1px solid #0284c7;background:#081426;color:#f8fafc;font-size:12px;font-weight:700;text-decoration:none;text-align:center;flex:1;min-width:130px}" +
        "button:active,a.btn:active{background:#0284c7}</style></head><body>" +
        "<div class='hdr'>OMEGAAV v0.7.1 :: 24D LEECH + FLUID RD OLED STREAM</div>" +
        "<img id='v' src='/frame.jpg' onclick='fetch(\"/api/splash\")'>" +
        "<div class='bar'>" +
        "<button onclick='fetch(\"/api/splash\")'>Splash 24D Fluid</button>" +
        "<button onclick='fetch(\"/api/fluid\")'>Cycle Fluid Mode</button>" +
        "<a class='btn' href='/download/snapshot.jpg' download>Save Frame .JPG</a>" +
        "<a class='btn' href='/download/active.omsh' download>Save Shader .OMSH</a>" +
        "</div><script>var im=document.getElementById('v');setInterval(function(){im.src='/frame.jpg?t='+Date.now();},85);</script></body></html>";
       byte[] b = Encoding.UTF8.GetBytes(html);
       byte[] hdr = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + b.Length + "\r\nConnection: close\r\n\r\n");
       ns.Write(hdr, 0, hdr.Length); ns.Write(b, 0, b.Length);
      }
     }
    catch { }
   }
  }
 }

 // First-Principles 48,000 Hz 16-Bit Stereo RIFF/WAV Harmonic Synthesizer + Goertzel 8-Band Filterbank
 public sealed class CymaticAudioEngine
 {
  public const int SampleRate = 48000;
  public short[] PcmLeft = new short[SampleRate * 8];
  public short[] PcmRight = new short[SampleRate * 8];
  public byte[] WavBytes = new byte[0];
  public int TotalSamples = SampleRate * 8;
  public bool IsPlaying = false;
  private SoundPlayer _player;
  private MemoryStream _wavStream;
  private readonly Stopwatch _clock = new Stopwatch();

  public void BuildPreset(int presetIdx)
  {
   int n = SampleRate * 8;
   TotalSamples = n;
   PcmLeft = new short[n]; PcmRight = new short[n];
   double[] freqs;
   if (presetIdx == 0) freqs = new double[] { 55.0, 110.0, 165.0, 220.0, 302.5, 385.0, 495.0, 654.5 };
   else if (presetIdx == 1) freqs = new double[] { 54.0, 108.0, 162.0, 216.0, 270.0, 324.0, 432.0, 648.0 };
   else freqs = new double[] { 111.0, 166.5, 222.0, 333.0, 444.0, 528.0, 666.0, 888.0 };

   Parallel.For(0, n, delegate(int i)
   {
    double t = (double)i / SampleRate;
    double beatEnv = (presetIdx == 1) ? (0.45 + 0.55 * Math.Pow(0.5 + 0.5 * Math.Cos(2.0 * Math.PI * 1.75 * t), 3.0)) : (0.78 + 0.22 * Math.Sin(2.0 * Math.PI * 0.25 * t));
    double accL = 0.0, accR = 0.0;
    for (int k = 0; k < 8; k++)
    {
     double amp = 1.0 / (1.0 + 0.42 * k);
     double mod = 0.72 + 0.28 * Math.Sin(2.0 * Math.PI * (0.125 * (k + 1)) * t);
     accL += amp * mod * Math.Sin(2.0 * Math.PI * freqs[k] * t);
     accR += amp * mod * Math.Sin(2.0 * Math.PI * (freqs[k] + ((k & 1) == 0 ? 0.5 : -0.5)) * t + (k * 0.35));
    }
    if (presetIdx == 1)
    {
     double sp = (t * 1.75) - Math.Floor(t * 1.75);
     double kick = Math.Exp(-sp * 9.5) * Math.Sin(2.0 * Math.PI * (92.0 * Math.Exp(-sp * 3.2) + 42.0) * sp);
     accL += 1.35 * kick; accR += 1.35 * kick;
    }
    PcmLeft[i] = (short)(Math.Max(-0.96, Math.Min(0.96, accL * 0.23 * beatEnv)) * 32000.0);
    PcmRight[i] = (short)(Math.Max(-0.96, Math.Min(0.96, accR * 0.23 * beatEnv)) * 32000.0);
   });
   WavBytes = EncodeRiffWav(PcmLeft, PcmRight, SampleRate);
   if (IsPlaying) PlayLoop();
  }

  public bool LoadExternalWav(string path)
  {
   try
   {
    byte[] raw = File.ReadAllBytes(path);
    if (raw.Length < 48) return false;
    int pos = 12, ch = 2, rate = 48000, bits = 16, dOff = 44, dLen = raw.Length - 44;
    while (pos + 8 <= raw.Length)
    {
     string id = Encoding.ASCII.GetString(raw, pos, 4);
     int sz = BitConverter.ToInt32(raw, pos + 4);
     if (id == "fmt " && pos + 24 <= raw.Length) { ch = BitConverter.ToInt16(raw, pos + 10); rate = BitConverter.ToInt32(raw, pos + 12); bits = BitConverter.ToInt16(raw, pos + 22); }
     else if (id == "data") { dOff = pos + 8; dLen = Math.Min(sz, raw.Length - dOff); break; }
     pos += 8 + Math.Max(0, sz);
    }
    if (bits != 16 || ch < 1) return false;
    int fb = ch * 2, frames = Math.Min(rate * 60, dLen / fb);
    if (frames < 512) return false;
    PcmLeft = new short[frames]; PcmRight = new short[frames];
    for (int i = 0; i < frames; i++)
    {
     int p = dOff + i * fb;
     short l = BitConverter.ToInt16(raw, p);
     PcmLeft[i] = l; PcmRight[i] = (ch >= 2) ? BitConverter.ToInt16(raw, p + 2) : l;
    }
    TotalSamples = frames; WavBytes = raw; PlayLoop(); return true;
   }
   catch { return false; }
  }

  public void PlayLoop()
  {
   try { Stop(); _wavStream = new MemoryStream(WavBytes, false); _player = new SoundPlayer(_wavStream); _player.PlayLooping(); _clock.Restart(); IsPlaying = true; }
   catch { IsPlaying = false; }
  }

  public void Stop()
  {
   try { if (_player != null) _player.Stop(); } catch { }
   _clock.Stop(); IsPlaying = false;
  }

  public void SampleAcousticFrame(double simTime, double[] bands8, short[] waveL, short[] waveR)
  {
   int cur = IsPlaying ? (int)((_clock.Elapsed.TotalSeconds * SampleRate) % Math.Max(1, TotalSamples)) : (int)((simTime * SampleRate) % Math.Max(1, TotalSamples));
   for (int i = 0; i < waveL.Length; i++) { int idx = (cur + i) % TotalSamples; waveL[i] = PcmLeft[idx]; waveR[i] = PcmRight[idx]; }
   double[] hz = new double[] { 58.0, 112.0, 215.0, 340.0, 530.0, 880.0, 1650.0, 3100.0 };
   int N = 256;
   for (int b = 0; b < 8; b++)
   {
    double w = (2.0 * Math.PI * hz[b]) / SampleRate, cf = 2.0 * Math.Cos(w), s1 = 0.0, s2 = 0.0;
    for (int n = 0; n < N; n++)
    {
     double x = ((waveL[n] + waveR[n]) * 0.5 / 32768.0) * (0.54 - 0.46 * Math.Cos(2.0 * Math.PI * n / (N - 1)));
     double s0 = x + cf * s1 - s2; s2 = s1; s1 = s0;
    }
    double mag = Math.Sqrt(Math.Max(0.0, s1 * s1 + s2 * s2 - cf * s1 * s2)) / (N * 0.18);
    if (!IsPlaying) mag = 0.28 + 0.24 * Math.Sin(simTime * (1.1 + b * 0.37) + b);
    bands8[b] = bands8[b] * 0.62 + Math.Min(2.4, mag * 2.1) * 0.38;
   }
  }

  public static byte[] EncodeRiffWav(short[] left, short[] right, int sampleRate)
  {
   int n = Math.Min(left.Length, right.Length), dataBytes = n * 4;
   byte[] buf = new byte[44 + dataBytes];
   Encoding.ASCII.GetBytes("RIFF").CopyTo(buf, 0); BitConverter.GetBytes(36 + dataBytes).CopyTo(buf, 4);
   Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(buf, 8); BitConverter.GetBytes(16).CopyTo(buf, 16);
   BitConverter.GetBytes((short)1).CopyTo(buf, 20); BitConverter.GetBytes((short)2).CopyTo(buf, 22);
   BitConverter.GetBytes(sampleRate).CopyTo(buf, 24); BitConverter.GetBytes(sampleRate * 4).CopyTo(buf, 28);
   BitConverter.GetBytes((short)4).CopyTo(buf, 32); BitConverter.GetBytes((short)16).CopyTo(buf, 34);
   Encoding.ASCII.GetBytes("data").CopyTo(buf, 36); BitConverter.GetBytes(dataBytes).CopyTo(buf, 40);
   int p = 44;
   for (int i = 0; i < n; i++)
   {
    short l = left[i], r = right[i];
    buf[p] = (byte)(l & 255); buf[p + 1] = (byte)((l >> 8) & 255);
    buf[p + 2] = (byte)(r & 255); buf[p + 3] = (byte)((r >> 8) & 255); p += 4;
   }
   return buf;
  }
 }
}
