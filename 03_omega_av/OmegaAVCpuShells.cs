using System;
using System.Drawing;
using System.Windows.Forms;

namespace OmegaAVStudio
{
 public sealed partial class OmegaAvForm
 {
 private void DrawCpuE8Shell(int w, int h, int[] fb, double t, double bass, double mid, double[] u, double hdScale, int hiIdx, double mPx, double mPy, double mClk)
 {
  double a01 = t * 0.42 + _yaw, a23 = t * 0.31 + _pitch, a45 = t * 0.23 + bass * 0.35, a67 = t * 0.19 + mid * 0.18;
  double c0 = Math.Cos(a01), s0 = Math.Sin(a01), c1 = Math.Cos(a23), s1 = Math.Sin(a23), c2 = Math.Cos(a45), s2 = Math.Sin(a45), c3 = Math.Cos(a67), s3 = Math.Sin(a67);
  double tilt = 0.28 * Math.Sin(t * 0.37) + bass * 0.14, ct = Math.Cos(tilt), st = Math.Sin(tilt);
  double scale = (h * 0.39) * _zoom * (1.0 + bass * 0.08), cx = w * 0.5, cy = h * 0.5;
  CompiledMathProgram projJit = _useCustomProj8D ? _activeProjProg : null;
  double[] r8Buf = new double[23], projOut = new double[4], sx = new double[240], sy = new double[240];
  for (int i = 0; i < 240; i++)
  {
  double[] src = E8LatticeMath.Roots[i], dst = _rotRoots[i];
  dst[0] = src[0] * c0 - src[1] * s0; dst[1] = src[0] * s0 + src[1] * c0; dst[2] = src[2] * c1 - src[3] * s1; dst[3] = src[2] * s1 + src[3] * c1;
  dst[4] = src[4] * c2 - src[5] * s2; dst[5] = src[4] * s2 + src[5] * c2; dst[6] = src[6] * c3 - src[7] * s3; dst[7] = src[6] * s3 + src[7] * c3;
  double p1 = E8LatticeMath.Dot8(dst, E8LatticeMath.U1), p2 = E8LatticeMath.Dot8(dst, E8LatticeMath.U2), p3 = E8LatticeMath.Dot8(dst, E8LatticeMath.U3), p4 = E8LatticeMath.Dot8(dst, E8LatticeMath.U4);
  double xP, yP, zP;
  if (projJit != null && projJit.Kernel != null)
  {
   for (int k = 0; k < 8; k++) { r8Buf[k] = dst[k]; r8Buf[8 + k] = src[k]; }
   r8Buf[16] = 1.4142; projJit.Kernel(p1, p2, p3, p4, 0.0, 0.0, u, r8Buf, projOut); xP = projOut[0]; yP = projOut[1]; zP = projOut[2];
  }
  else { xP = p1 * ct + p3 * st; yP = p2; zP = -p1 * st + p3 * ct; }
  double persp = 1.0 / Math.Max(0.25, 1.0 - zP * 0.22);
  sx[i] = cx + xP * persp * scale; sy[i] = cy - yP * persp * scale;
  if (mClk > 0.5) ApplyCpuMouseLens(ref sx[i], ref sy[i], mPx, mPy, hdScale, t, bass);
  }
  if (_edgeBudget > 0)
  {
  int tot = E8LatticeMath.EdgeA.Length, stride = (_edgeBudget >= 6720) ? 1 : Math.Max(1, tot / _edgeBudget);
  for (int e = 0; e < tot; e += stride)
  {
   int a = E8LatticeMath.EdgeA[e], b = E8LatticeMath.EdgeB[e]; byte ring = Math.Max(E8LatticeMath.RootRing[a], E8LatticeMath.RootRing[b]);
   WuSubpixelRaster.DrawLineUnified(fb, w, h, sx[a], sy[a], sx[b], sy[b], (ring >= 6) ? 30 : 12, (ring >= 6) ? 62 : 26, (ring >= 6) ? 94 : 48, _rasterMode);
  }
  }
  for (int i = 0; i < 240; i++)
  {
  byte r = E8LatticeMath.RootRing[i]; bool hi = (i == hiIdx);
  WuSubpixelRaster.DrawVertexUnified(fb, w, h, sx[i], sy[i], (hi ? 4.2 : (r >= 6 ? 2.1 : 1.3)) * hdScale, hi ? 255 : (45 + r * 24), hi ? 230 : (110 + r * 16), hi ? 90 : (210 - r * 12), _rasterMode);
  }
 }

 private void DrawCpuLeech24Shell(int w, int h, int[] fb, double t, double bass, double[] u, double hdScale, int hiIdx, double mPx, double mPy, double mClk)
 {
  double[] cs = new double[12], sn = new double[12];
  for (int p = 0; p < 12; p++) { double ang = t * (0.16 + 0.035 * p) + ((p & 1) == 0 ? _yaw : _pitch) + _bands[p & 7] * 0.14; cs[p] = Math.Cos(ang); sn[p] = Math.Sin(ang); }
  double scale = (h * 0.34) * _zoom * (1.0 + bass * 0.07), cx = w * 0.5, cy = h * 0.5;
  CompiledMathProgram projJit = _useCustomProj8D ? _activeProjProg : null;
  double[] r8Buf = new double[23], projOut = new double[4], rot24 = new double[24], bA = new double[8], bB = new double[8], bC = new double[8], sx = new double[720], sy = new double[720];
  for (int i = 0; i < 720; i++)
  {
  double[] src = GolayLeech24Math.Vectors[i];
  for (int p = 0; p < 12; p++) { int j = p << 1; rot24[j] = src[j] * cs[p] - src[j + 1] * sn[p]; rot24[j + 1] = src[j] * sn[p] + src[j + 1] * cs[p]; }
  double nA = 0.0, nB = 0.0, nC = 0.0;
  for (int k = 0; k < 8; k++) { bA[k] = rot24[k]; bB[k] = rot24[8 + k]; bC[k] = rot24[16 + k]; nA += bA[k] * bA[k]; nB += bB[k] * bB[k]; nC += bC[k] * bC[k]; }
  nA = Math.Sqrt(nA); nB = Math.Sqrt(nB); nC = Math.Sqrt(nC);
  double a1 = E8LatticeMath.Dot8(bA, E8LatticeMath.U1), a2 = E8LatticeMath.Dot8(bA, E8LatticeMath.U2);
  double b1 = E8LatticeMath.Dot8(bB, E8LatticeMath.U1), b2 = E8LatticeMath.Dot8(bB, E8LatticeMath.U2);
  double d1 = E8LatticeMath.Dot8(bC, E8LatticeMath.U1), d2 = E8LatticeMath.Dot8(bC, E8LatticeMath.U2);
  double c1 = 0.62 * (a1 - 0.5 * (b1 + d1) - 0.8660254 * (b2 - d2)), c2 = 0.62 * (a2 + 0.8660254 * (b1 - d1) - 0.5 * (b2 + d2));
  double c3 = 0.58 * (E8LatticeMath.Dot8(bA, E8LatticeMath.U3) + E8LatticeMath.Dot8(bB, E8LatticeMath.U4) + d1), c4 = 0.58 * (E8LatticeMath.Dot8(bC, E8LatticeMath.U3) - E8LatticeMath.Dot8(bA, E8LatticeMath.U4));
  double xP, yP, zP;
  if (projJit != null && projJit.Kernel != null)
  {
   for (int k = 0; k < 8; k++) { r8Buf[k] = (bA[k] + bB[k] + bC[k]) * 0.57735; r8Buf[8 + k] = src[k]; }
   r8Buf[16] = nA; r8Buf[17] = nB; r8Buf[18] = nC; projJit.Kernel(c1, c2, c3, c4, 0.0, 0.0, u, r8Buf, projOut); xP = projOut[0]; yP = projOut[1]; zP = projOut[2];
  }
  else { xP = c1; yP = c2; zP = c3; }
  double persp = 1.0 / Math.Max(0.28, 1.0 - zP * 0.20);
  sx[i] = cx + xP * persp * scale; sy[i] = cy - yP * persp * scale;
  if (mClk > 0.5) ApplyCpuMouseLens(ref sx[i], ref sy[i], mPx, mPy, hdScale, t, bass);
  }
  if (_edgeBudget > 0)
  {
  int tot = GolayLeech24Math.EdgeA.Length, stride = (_edgeBudget >= 6720) ? 1 : Math.Max(1, tot / Math.Max(400, _edgeBudget));
  for (int e = 0; e < tot; e += stride)
  {
   int a = GolayLeech24Math.EdgeA[e], b = GolayLeech24Math.EdgeB[e]; byte sh = GolayLeech24Math.VecShape[a];
   WuSubpixelRaster.DrawLineUnified(fb, w, h, sx[a], sy[a], sx[b], sy[b], (sh == 0) ? 14 : (sh == 1 ? 32 : 34), (sh == 0) ? 48 : (sh == 1 ? 18 : 42), (sh == 0) ? 72 : (sh == 1 ? 68 : 18), _rasterMode);
  }
  }
  for (int i = 0; i < 720; i++)
  {
  byte sh = GolayLeech24Math.VecShape[i]; bool hi = (i == hiIdx);
  WuSubpixelRaster.DrawVertexUnified(fb, w, h, sx[i], sy[i], (hi ? 4.4 : 1.65) * hdScale, hi ? 255 : (sh == 0 ? 56 : (sh == 1 ? 192 : 250)), hi ? 245 : (sh == 0 ? 215 : (sh == 1 ? 115 : 204)), hi ? 120 : (sh == 0 ? 255 : (sh == 1 ? 255 : 65)), _rasterMode);
  }
 }

 private static void ApplyCpuMouseLens(ref double sx, ref double sy, double mPx, double mPy, double hdScale, double t, double bass)
 {
  double dx = sx - mPx, dy = sy - mPy, r2 = dx * dx + dy * dy, gRad = 175.0 * hdScale;
  double pull = Math.Exp(-r2 / Math.Max(1.0, 2.0 * gRad * gRad));
  double swirl = pull * (0.52 + 0.28 * bass), pinch = 1.0 - 0.38 * pull * Math.Sin(t * 4.2 + Math.Sqrt(r2) * 0.035);
  sx = mPx + dx * pinch - dy * swirl; sy = mPy + dx * swirl + dy * pinch;
 }

 private void DrawCpuHud(int w, int h, int[] fb, double hdScale)
 {
  int vRad = (int)(44.0 * hdScale), vCx = w - (int)(58.0 * hdScale), vCy = h - (int)(58.0 * hdScale);
  WuSubpixelRaster.DrawLineUnified(fb, w, h, vCx - vRad, vCy, vCx + vRad, vCy, 25, 45, 70, _rasterMode);
  WuSubpixelRaster.DrawLineUnified(fb, w, h, vCx, vCy - vRad, vCx, vCy + vRad, 25, 45, 70, _rasterMode);
  double prevX = vCx + (_waveL[0] * vRad) / 32768.0, prevY = vCy - (_waveR[0] * vRad) / 32768.0;
  for (int i = 1; i < 200; i++)
  {
  double x = vCx + (_waveL[i] * vRad) / 32768.0, y = vCy - (_waveR[i] * vRad) / 32768.0;
  WuSubpixelRaster.DrawLineUnified(fb, w, h, prevX, prevY, x, y, 32, 205, 138, _rasterMode);
  prevX = x; prevY = y;
  }
  int barW = Math.Max(6, (int)(8.0 * hdScale)), barStep = Math.Max(10, (int)(13.0 * hdScale)), padL = (int)(12.0 * hdScale), padB = (int)(8.0 * hdScale);
  for (int b = 0; b < 8; b++)
  {
  int bx = padL + b * barStep, bh = Math.Min((int)(62.0 * hdScale), (int)(_bands[b] * 32.0 * hdScale));
  for (int dx = 0; dx < barW; dx++) BresenhamRaster.DrawLineAdditive(fb, w, h, bx + dx, h - padB, bx + dx, h - padB - bh, 35 + b * 22, 160, 245 - b * 18);
  }
 }

 private void BuildShaderIdeSidebar()
 {
  _idePanel = new Panel(); _idePanel.Dock = DockStyle.Right; _idePanel.Width = 468; _idePanel.BackColor = Color.FromArgb(11, 16, 28);
  _idePanel.Padding = new Padding(10, 8, 10, 8);

  Label hdr = new Label(); hdr.Text = "OMEGAAV v0.7.1 :: PING-PONG FLUID RD + MIDI & GAMEPAD IDE";
  hdr.SetBounds(10, 8, 448, 18); hdr.Font = new Font("Segoe UI", 9f, FontStyle.Bold); hdr.ForeColor = Color.FromArgb(167, 139, 250);

  string[] pNames = new string[_customPresets.Length];
  for (int i = 0; i < _customPresets.Length; i++) pNames[i] = _customPresets[i].Name;
  _cmbPreset = MakeCombo(pNames, 304); _cmbPreset.Location = new Point(10, 30);
  _cmbPreset.SelectedIndexChanged += delegate { LoadCustomPreset(_cmbPreset.SelectedIndex); };

  Button bSave = MakeBtn("Save .omsh", Color.FromArgb(15, 118, 110)); bSave.SetBounds(320, 29, 68, 25);
  bSave.Click += delegate { SaveShaderFile(); };
  Button bLoad = MakeBtn("Open", Color.FromArgb(30, 58, 138)); bLoad.SetBounds(392, 29, 64, 25);
  bLoad.Click += delegate { OpenShaderFile(); };

  _lblKVal = new Label(); _lblKVal.SetBounds(10, 60, 448, 16); _lblKVal.Font = new Font("Consolas", 8.1f);
  _lblKVal.ForeColor = Color.FromArgb(56, 189, 248); _lblKVal.Text = "Knobs -> k1=1.00  k2=1.00  k3=1.00  k4=1.00 (MIDI/Pad Bindable)";
  _slK1 = MakeSlider(10, 78); _slK2 = MakeSlider(122, 78); _slK3 = MakeSlider(234, 78); _slK4 = MakeSlider(346, 78);

  Label lPix = new Label(); lPix.SetBounds(10, 108, 448, 16); lPix.Font = new Font("Consolas", 8.1f, FontStyle.Bold);
  lPix.ForeColor = Color.FromArgb(52, 211, 153);
  lPix.Text = "1. GPU CSField (rdU, rdV, velX, velY, m1..m4, padLx..padRt, sdf=...)";

  _txtPixelCode = MakeCodeBox(10, 126, 448, 248);

  _chkUseProj = new CheckBox(); _chkUseProj.SetBounds(10, 380, 448, 20); _chkUseProj.Checked = true;
  _chkUseProj.Font = new Font("Consolas", 8.1f, FontStyle.Bold); _chkUseProj.ForeColor = Color.FromArgb(250, 204, 21);
  _chkUseProj.Text = "2. GPU CSProject 24D LEECH VELOCITY (c1..c4, m1..m4 -> projX,Y,Z)";
  _chkUseProj.CheckedChanged += delegate { _useCustomProj8D = _chkUseProj.Checked; };

  _txtProjCode = MakeCodeBox(10, 402, 448, 132);

  Button bRun = MakeBtn("Compile 6-Shader HLSL GPU (Ctrl+Enter)", Color.FromArgb(109, 40, 217));
  bRun.SetBounds(10, 540, 252, 28);
  bRun.Click += delegate { _cmbShader.SelectedIndex = 5; CompileBothEditors(); };

  _chkAutoJit = new CheckBox(); _chkAutoJit.SetBounds(270, 544, 188, 22); _chkAutoJit.Checked = true;
  _chkAutoJit.Text = "Live Auto-Compile"; _chkAutoJit.ForeColor = Color.FromArgb(203, 213, 225);
  _chkAutoJit.Font = new Font("Segoe UI", 8.3f);

  _lblJitStatus = new Label(); _lblJitStatus.SetBounds(10, 574, 448, 46);
  _lblJitStatus.BackColor = Color.FromArgb(15, 23, 42); _lblJitStatus.ForeColor = Color.FromArgb(52, 211, 153);
  _lblJitStatus.Font = new Font("Consolas", 8.0f); _lblJitStatus.Padding = new Padding(6, 4, 6, 4);

  Label help = new Label(); help.SetBounds(10, 626, 448, 158);
  help.Font = new Font("Consolas", 7.75f); help.ForeColor = Color.FromArgb(148, 163, 184);
  help.Text =
  "v0.7 Fluid + OLED Saver + Drag-Drop Deck:\r\n" +
  "F = Cycle GPU Ping-Pong Fluid (Coral / Soliton / Labyrinth / Spiral / Audio / Off)\r\n" +
  "S (or MIDI Note / Pad Cross-A) = Splash 24D Golay Reaction-Diffusion Shockwave!\r\n" +
  "K = Open MIDI + Xbox / Sony DualSense Knob Learn Deck | Shift+F = Reset Fluid\r\n" +
  "Xbox/DualSense: L-Stick=Orbit, R-Stick=Vortex Wand, L2/R2=Advect/Glow, DPad=Morph\r\n" +
  "B = Post-FX | G = 720p/1080p/1440p | F11 = VJ Mode | Ctrl+R = Rec Live MP4";

  _idePanel.Controls.AddRange(new Control[] {
  hdr, _cmbPreset, bSave, bLoad, _lblKVal, _slK1, _slK2, _slK3, _slK4,
  lPix, _txtPixelCode, _chkUseProj, _txtProjCode, bRun, _chkAutoJit, _lblJitStatus, help
  });
 }


 private void LoadCustomPreset(int idx)
 {
  if (idx < 0 || idx >= _customPresets.Length) return;
  _chkAutoJit.Checked = false;
  _txtPixelCode.Text = _customPresets[idx].PixelCode.Replace("\n", "\r\n");
  _txtProjCode.Text = _customPresets[idx].Proj8DCode.Replace("\n", "\r\n");
  _chkAutoJit.Checked = true;
  _cmbShader.SelectedIndex = 5;
  CompileBothEditors();
 }

 private void CompileBothEditors()
 {
  if (_oledServer != null) _oledServer.LatestShaderOmsh = _txtPixelCode.Text + "\r\n===OMEGA_PROJ8D===\r\n" + _txtProjCode.Text;
  CompiledMathProgram pProg = OmegaShaderJit.Compile(_txtPixelCode.Text, false);
  CompiledMathProgram jProg = OmegaShaderJit.Compile(_txtProjCode.Text, true);
  if (pProg.Error == null && pProg.Kernel != null) _activePixelProg = pProg;
  if (jProg.Error == null && jProg.Kernel != null) _activeProjProg = jProg;

  bool gpuOk = false;
  if (pProg.Error == null && jProg.Error == null && _gpu != null && _gpu.IsAvailable)
  gpuOk = _gpu.CompileCustomShaders(_txtPixelCode.Text, _txtProjCode.Text);

  if (pProg.Error != null)
  {
  _lblJitStatus.ForeColor = Color.FromArgb(251, 191, 36);
  _lblJitStatus.Text = "Pixel Shader Syntax Hold (previous shader kept live):\r\n" + pProg.Error;
  }
  else if (jProg.Error != null)
  {
  _lblJitStatus.ForeColor = Color.FromArgb(251, 191, 36);
  _lblJitStatus.Text = "Projection Syntax Hold (previous projection kept live):\r\n" + jProg.Error;
  }
  else if (_gpu != null && _gpu.IsAvailable && !gpuOk)
  {
  _lblJitStatus.ForeColor = Color.FromArgb(251, 191, 36);
  _lblJitStatus.Text = "CPU x64 JIT OK | HLSL FXC Hold: " + _gpu.LastError;
  }
  else
  {
  string gpuMsg = (_gpu != null && _gpu.IsAvailable) ? ("RTX 6-Shader HLSL cs_5_0 (" + _gpu.LastCompileMs.ToString("0.0") + "ms)") : ("CPU x64 Fallback (" + ((_gpu != null && !string.IsNullOrEmpty(_gpu.LastError)) ? _gpu.LastError : "CPU Mode") + ")");
  _lblJitStatus.ForeColor = Color.FromArgb(52, 211, 153);
  _lblJitStatus.Text = gpuMsg + " + Ping-Pong Fluid ACTIVE\r\n" +
   (pProg.UsesSdf ? "Mode: 26-Step 4D SDF + Leech Velocity Fluid RD" : "Mode: Leech24/Golay + Velocity Fluid RD + MIDI/Pad");
  }
 }

 }
}
