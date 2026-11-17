using System;
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
 public static class OmegaPresetCatalog
 {
 public static MathShaderPreset[] GetPresets()
 {
  return new MathShaderPreset[]
  {
  new MathShaderPreset(
   "Custom 1: Leech Velocity Reaction-Diffusion + MIDI/Pad Morph",
   "// GPU Ping-Pong Gray-Scott Fluid (rdU, rdV, velX, velY) + 24D Leech Lattice!\n" +
   "// Twist MIDI knobs or pull Xbox/DualSense L2/R2 triggers (m1..m4, padLt, padRt)!\n" +
   "spd = len2(velX, velY) * 0.14;\n" +
   "warpX = x + velX * 0.015 * k1; warpY = y + velY * 0.015 * k1;\n" +
   "ld = leech24(warpX * (2.1 + padRt * 0.8), warpY * (2.1 + padRt * 0.8), rdV * 1.4 * k2, t);\n" +
   "coral = smoothstep(0.03, 0.34, rdV);\n" +
   "shell = exp(-abs(ld - 0.67) * (13.0 * k3));\n" +
   "r = shell * (0.28 + 0.65 * spd) + coral * (0.18 + 0.72 * m1 + bass * 0.25);\n" +
   "g = shell * 0.86 + coral * (0.88 - 0.30 * m2) + spd * 0.28;\n" +
   "b = shell * 1.15 + coral * 0.95 + 0.16 * exp(-rad * 1.2);",
   "// 24D Leech Chord Stirrer (Gamepad Triggers & MIDI m3/m4 modulate 24D spin!):\n" +
   "ang = t * (0.34 + 0.28 * m3) + bass * 0.16 + padLt * 0.6;\n" +
   "tri = 1.0 + 0.18 * (t1 - t3) * k4;\n" +
   "projX = (c1 * cos(ang) + c3 * sin(ang)) * tri;\n" +
   "projY = (c2 * cos(ang * 0.6) + c4 * sin(ang * 0.6)) * tri;\n" +
   "projZ = -c1 * sin(ang) + c3 * cos(ang);"
  ),
  new MathShaderPreset(
   "Custom 2: Turing Coral Sculpted 4D Torus-Gyroid SDF",
   "// Gray-Scott chemical V (rdV) carves live organic relief into a 4D Raymarched SDF!\n" +
   "rx = rot1(px, pz, t * 0.44 + padLx * 0.8); rz = rot2(px, pz, t * 0.44 + padLx * 0.8);\n" +
   "tor = sdTorus(rx, py, rz, 0.86 * k1 + bass * 0.12, 0.28 * k2 + rdV * 0.085);\n" +
   "gyr = sdGyroid(px, py, pz, 5.2 * k3 + mid * 1.3 + m1 * 2.5) * 0.68;\n" +
   "shell = max(sdSphere(px, py, pz, 1.22 + padRt * 0.22), abs(gyr) - 0.055 - rdV * 0.03);\n" +
   "sdf = smin(tor, shell, 0.18);\n" +
   "v = 0.25 * (px + py + pz) + rdV * 0.85 + t * 0.15;",
   "// 30-Fold Coxeter Petrie Projection on GPU:\n" +
   "tilt = 0.30 * sin(t * 0.38) + bass * 0.15 + m2 * 0.5;\n" +
   "projX = c1 * cos(tilt) + c3 * sin(tilt);\n" +
   "projY = c2;\n" +
   "projZ = -c1 * sin(tilt) + c3 * cos(tilt);"
  ),
  new MathShaderPreset(
   "Custom 3: 24D Leech Golay Triality + Turyn Wave",
   "s = 2.2 * k1 + bass * 0.35;\n" +
   "ld = leech24(x * s, y * s, sin(t * 0.4) * k2, t);\n" +
   "gw = golay(rad * 3.0 * k3 - t * 0.5);\n" +
   "shell = exp(-abs(ld - 0.68) * 13.5);\n" +
   "core = exp(-ld * 3.6) * (0.45 + 0.55 * gw);\n" +
   "r = shell * (0.35 + 0.65 * gw) + core * (0.30 + bass * 0.4);\n" +
   "g = shell * 0.88 + core * 0.55;\n" +
   "b = shell * 1.15 + core * 0.95 + 0.18 * exp(-rad * 1.2);",
   "ang = t * 0.32 + bass * 0.14;\n" +
   "tri = 1.0 + 0.16 * (t1 - t3) * k4;\n" +
   "projX = (c1 * cos(ang) + c3 * sin(ang)) * tri;\n" +
   "projY = (c2 * cos(ang * 0.5) + c4 * sin(ang * 0.5)) * tri;\n" +
   "projZ = -c1 * sin(ang) + c3 * cos(ang);"
  ),
  new MathShaderPreset(
   "Custom 4: 8D E8 Voronoi Crystal + Standing Wave",
   "s = 2.6 * k1 + bass * 0.35;\n" +
   "vd = e8voronoi(x*s + cos(t*0.4), y*s + sin(t*0.3), (x-y)*k2, (x+y)*k2, bass*0.6, mid*0.5, high*0.5, q0*0.3);\n" +
   "sp = e8spinor(x*s + cos(t*0.4), y*s + sin(t*0.3), (x-y)*k2, (x+y)*k2, bass*0.6, mid*0.5, high*0.5, q0*0.3);\n" +
   "wall = exp(-abs(vd - 0.71) * (12.0 * k3));\n" +
   "wave = 0.5 + 0.5 * sin(14.0 * rad - t * 3.5 + bass * 3.0);\n" +
   "r = wall * (0.25 + 0.75 * sp) + 0.14 * wave;\n" +
   "g = wall * (0.90 - 0.40 * sp) + 0.22 * exp(-vd * 3.5);\n" +
   "b = wall * 1.05 + 0.35 * exp(-rad * 1.3);",
   "w8 = 1.95 - 0.38 * k4 * (x6 * cos(t * 0.6) + x7 * sin(t * 0.6));\n" +
   "projX = (x0 + 0.55 * x4) / w8; projY = (x1 + 0.55 * x5) / w8; projZ = (x2 + 0.55 * x3) / w8;"
  ),
  new MathShaderPreset(
   "Custom 5: 4D Clifford Hyper-Torus + Braid Links SDF",
   "ux = rot1(px, py, t * 0.35); uy = rot2(px, py, t * 0.35);\n" +
   "clif = sdClifford4D(ux, uy, pz, 0.35 * sin(t * 0.8) * k4, 0.78 * k1, 0.16 * k2);\n" +
   "lnk = sdLink(px, pz, py, 0.42, 0.36 + bass * 0.08, 0.09 * k3);\n" +
   "sdf = smin(clif, lnk, 0.16);\n" +
   "v = atan2(py, px) / tau + t * 0.1;",
   "projX = 0.68 * (x0*x2 + x1*x3 + x4*x6 + x5*x7);\n" +
   "projY = 0.68 * (x1*x2 - x0*x3 + x5*x6 - x4*x7);\n" +
   "projZ = 0.36 * (x0*x0 + x1*x1 + x4*x4 - x2*x2 - x3*x3 - x6*x6);"
  ),
  new MathShaderPreset(
   "Custom 6: Mandelbulb Power-8 + Leech Halo SDF",
   "pwr = 7.0 + 2.0 * k1 + sin(t * 0.35) * 1.5 + bass * 0.6;\n" +
   "sdf = mandelbulb(px * 0.95, py * 0.95, pz * 0.95, pwr);\n" +
   "v = len3(px, py, pz) * 0.7 * k2 - t * 0.12;",
   "projX = c1 * (1.0 + 0.30 * b0) + 0.18 * x0 * sin(t + b2);\n" +
   "projY = c2 * (1.0 + 0.30 * b1) + 0.18 * x1 * cos(t + b3);\n" +
   "projZ = c3 + 0.25 * high * sin(x4 * 3.0 + t);"
  ),
  new MathShaderPreset(
   "Custom 7: Infinite Crystal Lattice (Domain Rep SDF)",
   "per = 0.72 * k1;\n" +
   "qx = rep(px + t * 0.18, per); qy = rep(py, per); qz = rep(pz + t * 0.12, per);\n" +
   "sdf = max(sdSphere(px, py, pz, 1.35 * k3), sdOcta(qx, qy, qz, 0.24 * k2 + bass * 0.05));\n" +
   "v = (qx + qy + qz) * 1.5 + t * 0.2;",
   "m = 0.5 + 0.5 * sin(t * 0.5) * k4;\n" +
   "projX = mix(c1, c3, m * 0.6); projY = mix(c2, c4, m * 0.6); projZ = c3 * cos(t * 0.3) - c4 * sin(t * 0.3);"
  ),
  new MathShaderPreset(
   "Custom 8: Cymatic Wand & DualSense Vortex (mx, my)",
   "// Sweep Right-Stick on Xbox/DualSense or Right-Drag Mouse across the fluid!\n" +
   "dx = x - mx; dy = y - my; md = len2(dx, dy);\n" +
   "lens = 1.0 + (0.65 + 0.45 * mclick) * exp(-md * 2.2) * k1;\n" +
   "ld = leech24(x * 2.1 * lens, y * 2.1 * lens, md * k2 - t * 0.35, t);\n" +
   "rip = 0.5 + 0.5 * sin(18.0 * md - t * 5.0 + bass * 4.0) + rdV * 0.8;\n" +
   "sh = exp(-abs(ld - 0.67) * 14.0) + 0.38 * exp(-md * 3.2) * rip;\n" +
   "r = sh * (0.35 + 0.55 * rip + bass * 0.3);\n" +
   "g = sh * (0.85 + 0.25 * golay(md * 4.0 * k3));\n" +
   "b = sh * 1.20 + 0.25 * exp(-md * 1.8);",
   "tw = 0.35 * (mx * c1 + my * c2) * k4 + t * 0.28;\n" +
   "projX = c1 * cos(tw) + c3 * sin(tw);\n" +
   "projY = c2 * cos(tw * 0.6) + c4 * sin(tw * 0.6);\n" +
   "projZ = -c1 * sin(tw) + c3 * cos(tw);"
  )
  };
 }

 public static void EvalBuiltIn(int mode, double nx, double ny, double t, double yw, double pt, double bass, double mid, double high, double[] bands, double[] qE8, out int sr, out int sg, out int sb)
 {
  sr = 0; sg = 0; sb = 0;
  if (mode == 0)
  {
  double s = 0.0, c2 = 0.0, sc = 7.5 + bass * 2.4;
  for (int k = 0; k < 7; k++)
  {
   double ang = (Math.PI * k) / 7.0 + yw * 0.25;
   double proj = (nx * Math.Cos(ang) + ny * Math.Sin(ang)) * sc + t * (0.8 + 0.15 * k) + bands[k] * 1.9;
   s += Math.Cos(proj); if ((k & 1) == 0) c2 += Math.Sin(proj * 1.6180339);
  }
  double v = Math.Abs(s) * 0.16, gl = Math.Max(0.0, 1.0 - Math.Sqrt(nx * nx + ny * ny) * 0.65);
  sr = (int)Math.Min(120.0, (v * v * 42.0 + bass * 18.0) * gl);
  sg = (int)Math.Min(145.0, (v * 58.0 + Math.Abs(c2) * 16.0) * gl);
  sb = (int)Math.Min(195.0, (v * 92.0 + 18.0 + high * 22.0) * gl);
  }
  else if (mode == 1)
  {
  bool isSpinor; int cHash; double s = 2.6 + bass * 0.4;
  double vDist = E8LatticeMath.FastVoronoiCellDist(nx * s + Math.Cos(t * 0.35 + yw), ny * s + Math.Sin(t * 0.31 + pt), (nx - ny) * 1.4 + t * 0.28, (nx + ny) * 1.4 - t * 0.22, bass * 0.65, mid * 0.45, high * 0.45, Math.Sin(t * 0.5), out isSpinor, out cHash);
  double wall = Math.Exp(-Math.Abs(vDist - 0.72) * 11.5), core = Math.Exp(-vDist * 4.2); int hm = cHash & 63;
  sr = (int)Math.Min(255.0, wall * (isSpinor ? 235.0 : 65.0) + core * (50.0 + hm));
  sg = (int)Math.Min(255.0, wall * (isSpinor ? 120.0 : 225.0) + core * 95.0);
  sb = (int)Math.Min(255.0, wall * 250.0 + core * (145.0 + (63 - hm)));
  }
  else if (mode == 2)
  {
  double rdX = nx * 0.85, rdY = ny * 0.85, rdZ = 1.35;
  double invL = 1.0 / Math.Sqrt(rdX * rdX + rdY * rdY + rdZ * rdZ); rdX *= invL; rdY *= invL; rdZ *= invL;
  double cw = -0.42 + 0.16 * Math.Cos(t * 0.65 + yw) + bass * 0.06, cx = 0.28 * Math.Sin(t * 0.51 + pt) + qE8[0] * 0.05;
  double cy2 = -0.18 * Math.Cos(t * 0.43) + qE8[2] * 0.05, cz = 0.14 * Math.Sin(t * 0.8) + mid * 0.03;
  double distT = 0.0, trap = 99.0; int hit = 0;
  for (int st = 0; st < 22; st++)
  {
   double pX = rdX * distT, pY = rdY * distT, pZ = -2.35 + rdZ * distT;
   double cY = Math.Cos(t * 0.35 + yw), sY = Math.Sin(t * 0.35 + yw);
   double qx = pX * cY - pZ * sY, qy = pY, qz = pX * sY + pZ * cY, qw = 0.18 * Math.Sin(t * 0.5), dq2 = 1.0, r2 = 0.0;
   for (int it = 0; it < 7; it++)
   {
   r2 = qx * qx + qy * qy + qz * qz + qw * qw; if (r2 < trap) trap = r2; if (r2 > 6.0) break;
   dq2 *= 4.0 * r2;
   double nxq = qx * qx - qy * qy - qz * qz - qw * qw + cw;
   double nyq = 2.0 * qx * qy + cx, nzq = 2.0 * qx * qz + cy2, nwq = 2.0 * qx * qw + cz;
   qx = nxq; qy = nyq; qz = nzq; qw = nwq;
   }
   double r = Math.Sqrt(r2), de = 0.5 * r * Math.Log(Math.Max(1.0001, r)) / Math.Sqrt(Math.Max(1e-9, dq2));
   if (de < 0.0045) { hit = st + 1; break; }
   distT += de * 0.88; if (distT > 4.2) break;
  }
  if (hit > 0) { double occ = 1.0 - hit / 24.0; sr = (int)Math.Min(245.0, (70.0 + 170.0 * (1.0 - Math.Min(1.0, trap))) * occ + bass * 35.0); sg = (int)Math.Min(245.0, 45.0 + 155.0 * occ + mid * 28.0); sb = (int)Math.Min(255.0, 125.0 + 130.0 * occ); }
  else { double bg = Math.Exp(-2.2 * (nx * nx + ny * ny)); sr = (int)(12.0 * bg); sg = (int)(22.0 * bg); sb = (int)(48.0 * bg); }
  }
  else if (mode == 3)
  {
  double r2 = nx * nx + ny * ny, invDen = 1.0 / (1.0 + r2);
  double s1 = 2.0 * nx * invDen, s2 = 2.0 * ny * invDen, s3 = (r2 - 1.0) * invDen;
  double ang1 = t * 0.85 + yw, ang2 = t * 0.55 + pt;
  double u1 = s1 * Math.Cos(ang1) - s3 * Math.Sin(ang1), v1 = s2 * Math.Cos(ang2) + (s1 * Math.Sin(ang1) + s3 * Math.Cos(ang1)) * Math.Sin(ang2);
  double torus = Math.Abs(Math.Sqrt(u1 * u1 + s2 * s2) - (0.68 + 0.12 * Math.Sin(t + bass)));
  double grid = Math.Abs(Math.Sin(12.0 * Math.Atan2(v1, u1) + t * 2.0) * Math.Sin(12.0 * torus));
  double inten = Math.Exp(-torus * 5.2) * (0.55 + 0.85 * grid);
  sr = (int)Math.Min(220.0, inten * (55.0 + high * 75.0)); sg = (int)Math.Min(235.0, inten * (145.0 + mid * 45.0)); sb = (int)Math.Min(255.0, inten * 225.0);
  }
  else
  {
  double nM = 3.0 + Math.Floor((qE8[0] + 2.0) * 1.5), mM = 5.0 + Math.Floor((qE8[1] + 2.0) * 1.5);
  if (Math.Abs(nM - mM) < 0.5) mM += 2.0;
  double ch = Math.Cos(nM * Math.PI * nx * 0.65) * Math.Cos(mM * Math.PI * ny * 0.65) - Math.Cos(mM * Math.PI * nx * 0.65) * Math.Cos(nM * Math.PI * ny * 0.65);
  double nodal = Math.Exp(-Math.Abs(ch) * (9.0 + bass * 4.0));
  sr = (int)Math.Min(255.0, nodal * (210.0 + bass * 40.0)); sg = (int)Math.Min(255.0, nodal * 185.0 + Math.Abs(ch) * 18.0); sb = (int)Math.Min(255.0, nodal * 95.0 + Math.Abs(ch) * 42.0);
  }
 }
 }

 public sealed partial class OmegaAvForm
 {
 public double[] RenderSceneToBuffer(int w, int h, int[] optionalReadbackFb, double simTime, bool forceOfflineClock, bool presentSwapChain)
 {
  int activeRate = CymaticAudioEngine.SampleRate;
  bool usedWasapi = false;
  if (!forceOfflineClock && _wasapiEnabled && _wasapi != null && _wasapi.IsCapturing)
  {
  int hwRate;
  if (_wasapi.ReadLatestWindow(_waveL, _waveR, out hwRate)) { activeRate = hwRate; usedWasapi = true; }
  }
  if (!usedWasapi)
  {
  int cur = (!forceOfflineClock && _audio.IsPlaying)
   ? (int)((_simClock.Elapsed.TotalSeconds * CymaticAudioEngine.SampleRate) % Math.Max(1, _audio.TotalSamples))
   : (int)((simTime * CymaticAudioEngine.SampleRate) % Math.Max(1, _audio.TotalSamples));
  for (int i = 0; i < _waveL.Length; i++) { int idx = (cur + i) % _audio.TotalSamples; _waveL[i] = _audio.PcmLeft[idx]; _waveR[i] = _audio.PcmRight[idx]; }
  }

  double[] bandFreqs = new double[] { 58.0, 112.0, 215.0, 340.0, 530.0, 880.0, 1650.0, 3100.0 };
  for (int b = 0; b < 8; b++)
  {
  double om = (2.0 * Math.PI * bandFreqs[b]) / Math.Max(8000, activeRate);
  double cf = 2.0 * Math.Cos(om), s1 = 0.0, s2 = 0.0;
  for (int n = 0; n < 256; n++)
  {
   double sampleVal = ((_waveL[n] + _waveR[n]) * 0.5 / 32768.0) * (0.54 - 0.46 * Math.Cos(2.0 * Math.PI * n / 255.0));
   double s0 = sampleVal + cf * s1 - s2; s2 = s1; s1 = s0;
  }
  double mag = Math.Sqrt(Math.Max(0.0, s1 * s1 + s2 * s2 - cf * s1 * s2)) / (256.0 * 0.18);
  if (!usedWasapi && !forceOfflineClock && !_audio.IsPlaying) mag = 0.24 + 0.20 * Math.Sin(simTime * (1.1 + b * 0.35) + b);
  _bands[b] = _bands[b] * 0.58 + Math.Min(2.4, mag * 2.15) * 0.42;
  }

  double[] scaledState = new double[8];
  for (int i = 0; i < 8; i++) scaledState[i] = (_bands[i] - 0.45) * 2.2 + 0.35 * Math.Sin(simTime * 0.7 + i);
  double sqnr8; int rootIdx8;
  double[] qE8 = E8LatticeMath.QuantizeE8(scaledState, out sqnr8, out rootIdx8);
  _lastSqnrDb = sqnr8; _lastRootIdx = rootIdx8;

  int leechIdx;
  _lastLeechSqnrDb = GolayLeech24Math.QuantizeTuryn24Sqnr(_bands, simTime, out leechIdx);
  _lastLeechIdx = leechIdx;

  double bass = _bands[0] + _bands[1] * 0.7, mid = _bands[2] + _bands[3] + _bands[4], high = _bands[5] + _bands[6] + _bands[7];
  double m1 = _inputDeck.MacroValues[4], m2 = _inputDeck.MacroValues[5], m3 = _inputDeck.MacroValues[6], m4 = _inputDeck.MacroValues[7];
  double padLx = _inputDeck.PadLx, padLy = _inputDeck.PadLy, padRx = _inputDeck.PadRx, padRy = _inputDeck.PadRy;
  double padLt = _inputDeck.PadLt, padRt = _inputDeck.PadRt, mNote = _inputDeck.MidiNoteNorm, mVel = _inputDeck.MidiVelNorm;

  double zm = _zoom, yw = _yaw, pt = _pitch;
  double stW = Math.Max(1, (_stage != null) ? _stage.ClientSize.Width : w);
  double stH = Math.Max(1, (_stage != null) ? _stage.ClientSize.Height : h);
  double mousePx = (_mouseStagePt.X / stW) * w, mousePy = (_mouseStagePt.Y / stH) * h;
  if (!forceOfflineClock && _inputDeck.PadConnected && (Math.Abs(padRx) > 0.05 || Math.Abs(padRy) > 0.05))
  {
  mousePx = w * (0.5 + 0.42 * padRx);
  mousePy = h * (0.5 - 0.42 * padRy);
  }
  double mxNorm = ((mousePx - w * 0.5) / (h * 0.5)) / zm;
  double myNorm = ((h * 0.5 - mousePy) / (h * 0.5)) / zm;
  double mClickVal = (!forceOfflineClock && (_rightAttract || _gravityHover || _inputDeck.PadRightStickClick)) ? 1.0 : 0.0;

  double cy = Math.Cos(yw), sy = Math.Sin(yw), cp = Math.Cos(pt), sp = Math.Sin(pt);
  double camDist = 2.65 / Math.Max(0.35, zm);
  double roX = camDist * sy * cp, roY = camDist * sp, roZ = -camDist * cy * cp;
  double fwX = -sy * cp, fwY = -sp, fwZ = cy * cp;
  double rtX = cy, rtY = 0.0, rtZ = sy;
  double upX = fwY * rtZ - fwZ * rtY, upY = fwZ * rtX - fwX * rtZ, upZ = fwX * rtY - fwY * rtX;

  if (_useGpuHlsl && _gpu != null && _gpu.IsAvailable)
  {
  bool gpuOk = _gpu.RenderFullGpuFrame(
   w, h, presentSwapChain, _vsyncInterval, optionalReadbackFb,
   _shaderMode, _latticeMode, _rasterMode, _edgeBudget, _useCustomProj8D, _showVectors, _postFxMode, _fluidMode,
   simTime, zm, yw, pt, bass, mid, high, _k1, _k2, _k3, _k4,
   m1, m2, m3, m4, padLx, padLy, padRx, padRy, padLt, padRt, mNote, mVel,
   roX, roY, roZ, fwX, fwY, fwZ, rtX, rtY, rtZ, upX, upY, upZ,
   _bands, qE8, _waveL, _waveR, rootIdx8, leechIdx,
   mxNorm, myNorm, mClickVal, mousePx, mousePy, _liveRecActive);
  if (gpuOk) return qE8;
  }

  int[] fb = optionalReadbackFb ?? _fb;
  if (fb == null || fb.Length < w * h) return qE8;

  if (_fluidMode > 0)
  {
  float gsF, gsK;
  DirectComputeHlslEngine.GetGrayScottParams(_fluidMode, bass, high, m1, m2, out gsF, out gsK);
  _cpuFluid.Step(gsF, gsK);
  }

  double[] u = new double[42];
  u[0] = simTime; u[1] = bass; u[2] = mid; u[3] = high; u[4] = yw; u[5] = pt; u[6] = zm;
  u[7] = _k1; u[8] = _k2; u[9] = _k3; u[10] = _k4;
  for (int i = 0; i < 8; i++) { u[11 + i] = _bands[i]; u[19 + i] = qE8[i]; }
  u[27] = mxNorm; u[28] = myNorm; u[29] = mClickVal;
  u[30] = m1; u[31] = m2; u[32] = m3; u[33] = m4;
  u[34] = padLx; u[35] = padLy; u[36] = padRx; u[37] = padRy; u[38] = padLt; u[39] = padRt; u[40] = mNote; u[41] = mVel;

  int mode = _shaderMode;
  CompiledMathProgram cust = _activePixelProg;
  int fMode = _fluidMode;
  Parallel.For(0, h, delegate(int py)
  {
  double[] res = new double[4], dummyR8 = new double[23];
  double rawNy = (h * 0.5 - py) / (h * 0.5), ny = rawNy / zm;
  int rowOff = py * w;
  for (int px = 0; px < w; px++)
  {
   double rawNx = (px - w * 0.5) / (h * 0.5), nx = rawNx / zm;
   double rdU = 1.0, rdV = 0.0, velX = 0.0, velY = 0.0;
   if (fMode > 0) _cpuFluid.SampleNormalized((double)px / Math.Max(1, w - 1), (double)py / Math.Max(1, h - 1), out rdU, out rdV, out velX, out velY);
   dummyR8[19] = rdU; dummyR8[20] = rdV; dummyR8[21] = velX; dummyR8[22] = velY;

   int prev = fb[rowOff + px];
   int pr = ((prev >> 16) & 255) * 138 >> 8, pg = ((prev >> 8) & 255) * 144 >> 8, pb = (prev & 255) * 158 >> 8;
   int sr = 0, sg = 0, sb = 0;
   if (mode == 5 && cust != null && cust.Kernel != null)
   {
   double rad = Math.Sqrt(nx * nx + ny * ny), theta = Math.Atan2(ny, nx);
   if (cust.UsesSdf)
   {
    double rdX = rtX * rawNx * 0.85 + upX * rawNy * 0.85 + fwX * 1.35;
    double rdY = rtY * rawNx * 0.85 + upY * rawNy * 0.85 + fwY * 1.35;
    double rdZ = rtZ * rawNx * 0.85 + upZ * rawNy * 0.85 + fwZ * 1.35;
    double invL = 1.0 / Math.Sqrt(rdX * rdX + rdY * rdY + rdZ * rdZ); rdX *= invL; rdY *= invL; rdZ *= invL;
    double marchT = 0.0, glow = 0.0, pw = 0.35 * Math.Sin(simTime * 0.65); int hitStep = -1;
    for (int st = 0; st < 26; st++)
    {
    cust.Kernel(roX + rdX * marchT, roY + rdY * marchT, roZ + rdZ * marchT, pw, rad, theta, u, dummyR8, res);
    double d = res[3]; glow += 0.014 / (0.025 + d * d * 8.0);
    if (Math.Abs(d) < 0.004) { hitStep = st; break; }
    marchT += d * 0.88; if (marchT > 5.5) break;
    }
    if (hitStep >= 0)
    {
    double hitX = roX + rdX * marchT, hitY = roY + rdY * marchT, hitZ = roZ + rdZ * marchT, eps = 0.003;
    cust.Kernel(hitX + eps, hitY - eps, hitZ - eps, pw, rad, theta, u, dummyR8, res); double d1 = res[3];
    cust.Kernel(hitX - eps, hitY - eps, hitZ + eps, pw, rad, theta, u, dummyR8, res); double d2 = res[3];
    cust.Kernel(hitX - eps, hitY + eps, hitZ - eps, pw, rad, theta, u, dummyR8, res); double d3 = res[3];
    cust.Kernel(hitX + eps, hitY + eps, hitZ + eps, pw, rad, theta, u, dummyR8, res); double d4 = res[3];
    double nX = d1 - d2 - d3 + d4, nY = -d1 - d2 + d3 + d4, nZ = -d1 + d2 - d3 + d4;
    double nLen = 1.0 / Math.Max(1e-9, Math.Sqrt(nX * nX + nY * nY + nZ * nZ)); nX *= nLen; nY *= nLen; nZ *= nLen;
    cust.Kernel(hitX, hitY, hitZ, pw, rad, theta, u, dummyR8, res);
    double cr = cust.AssignsRgb ? ShaderMath.sat(res[0]) : ShaderMath.palR(res[0]);
    double cg = cust.AssignsRgb ? ShaderMath.sat(res[1]) : ShaderMath.palG(res[0]);
    double cb = cust.AssignsRgb ? ShaderMath.sat(res[2]) : ShaderMath.palB(res[0]);
    double diff = Math.Max(0.12, nX * 0.52 + nY * 0.68 - nZ * 0.51), fres = Math.Pow(1.0 - Math.Max(0.0, -(nX * rdX + nY * rdY + nZ * rdZ)), 2.6), ao = 1.0 - (hitStep / 30.0);
    sr = (int)Math.Min(255.0, (cr * diff * 225.0 + fres * 95.0 + glow * 35.0) * ao);
    sg = (int)Math.Min(255.0, (cg * diff * 225.0 + fres * 135.0 + glow * 55.0) * ao);
    sb = (int)Math.Min(255.0, (cb * diff * 225.0 + fres * 195.0 + glow * 85.0) * ao);
    }
    else { sr = (int)Math.Min(255.0, glow * (42.0 + bass * 35.0)); sg = (int)Math.Min(255.0, glow * (75.0 + mid * 25.0)); sb = (int)Math.Min(255.0, glow * 138.0 + 10.0 * Math.Exp(-rad * 1.4)); }
   }
   else
   {
    cust.Kernel(nx, ny, 0.0, 0.0, rad, theta, u, dummyR8, res);
    if (cust.AssignsRgb) { sr = (int)(ShaderMath.sat(res[0]) * 255.0); sg = (int)(ShaderMath.sat(res[1]) * 255.0); sb = (int)(ShaderMath.sat(res[2]) * 255.0); }
    else { double v = res[0], inten = 0.5 + 0.5 * Math.Sin(v * 3.14159); sr = (int)(ShaderMath.palR(v * 0.35 + simTime * 0.05) * inten * 255.0); sg = (int)(ShaderMath.palG(v * 0.35 + simTime * 0.05) * inten * 255.0); sb = (int)(ShaderMath.palB(v * 0.35 + simTime * 0.05) * inten * 255.0); }
   }
   }
   else OmegaPresetCatalog.EvalBuiltIn(mode, nx, ny, simTime, yw, pt, bass, mid, high, _bands, qE8, out sr, out sg, out sb);

   if (fMode > 0 && rdV > 0.02)
   {
   sr = Math.Min(255, sr + (int)(rdV * 110.0));
   sg = Math.Min(255, sg + (int)(rdV * 220.0));
   sb = Math.Min(255, sb + (int)(rdV * 245.0));
   }
   fb[rowOff + px] = unchecked((int)0xFF000000) | (Math.Min(255, Math.Max(pr, sr)) << 16) | (Math.Min(255, Math.Max(pg, sg)) << 8) | Math.Min(255, Math.Max(pb, sb));
  }
  });

  double hdScale = h / 324.0;
  if (_latticeMode == 0 || _latticeMode == 2) DrawCpuE8Shell(w, h, fb, simTime, bass, mid, u, hdScale, rootIdx8, mousePx, mousePy, mClickVal);
  if (_latticeMode == 1 || _latticeMode == 2) DrawCpuLeech24Shell(w, h, fb, simTime, bass, u, hdScale, leechIdx, mousePx, mousePy, mClickVal);
  if (_rasterMode == 2) BresenhamRaster.DrawLineAdditive(fb, w, h, w >> 1, 0, w >> 1, h - 1, 56, 189, 248);
  if (_showVectors) DrawCpuHud(w, h, fb, hdScale);
  return qE8;
 }

 }
}
