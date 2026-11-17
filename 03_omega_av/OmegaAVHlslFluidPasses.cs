using System;
using System.Collections.Generic;
using System.Text;

namespace OmegaAVStudio
{
 public static partial class OmegaHlslTranspiler
 {
 public static string BuildProjPassHlsl(string projSrc, out string err)
 {
  err = null;
  string projBody = "";
  try
  {
  List<HT> tks2 = Lex(projSrc ?? "");
  HashSet<string> ro2 = new HashSet<string>(StringComparer.Ordinal) {
   "pi","tau","phi","e","sqrt2","t","bass","mid","high","yaw","pitch","zoom","k1","k2","k3","k4",
   "m1","m2","m3","m4","padLx","padLy","padRx","padRy","padLt","padRt","note","vel",
   "mx","my","mclick","b0","b1","b2","b3","b4","b5","b6","b7","q0","q1","q2","q3","q4","q5","q6","q7",
   "c1","c2","c3","c4","x0","x1","x2","x3","x4","x5","x6","x7","r0","r1","r2","r3","r4","r5","r6","r7","t1","t2","t3"
  };
  HashSet<string> loc2 = new HashSet<string>(StringComparer.Ordinal) { "projX", "projY", "projZ" };
  List<string> st2 = new List<string>();
  HP hp2 = new HP(tks2, ro2, loc2, true);
  hp2.ParseAll(st2);
  StringBuilder sb2 = new StringBuilder();
  foreach (string v in loc2) if (v != "projX" && v != "projY" && v != "projZ") sb2.Append(" float v_").Append(v).Append(" = 0.0;\n");
  for (int i = 0; i < st2.Count; i++) sb2.Append(" ").Append(st2[i]).Append("\n");
  projBody = sb2.ToString();
  }
  catch (Exception ex) { err = ex.Message; }

  StringBuilder h = new StringBuilder(11264);
  h.Append(MathHeaderHlsl);
  h.Append(@"StructuredBuffer<float4> E8Roots : register(t0);
StructuredBuffer<float4> LeechVecs : register(t1);
StructuredBuffer<uint> VertMeta : register(t2);
RWStructuredBuffer<float4> ProjVertsOut : register(u0);
RWStructuredBuffer<float4> VertVelOut : register(u1);

float Dot8F(float4 a0, float4 a1, float4 b0, float4 b1) { return dot(a0, b0) + dot(a1, b1); }

void EvalUserProj(float v_c1, float v_c2, float v_c3, float v_c4, float4 xA, float4 xB, float4 rA, float4 rB, float v_t1, float v_t2, float v_t3, out float oX, out float oY, out float oZ) {
 float v_pi=3.14159265, v_tau=6.28318531, v_phi=1.61803399, v_e=2.71828183, v_sqrt2=1.41421356;
 float v_t=U[0].z, v_zoom=U[0].w, v_bass=U[1].x, v_mid=U[1].y, v_high=U[1].z, v_yaw=U[1].w, v_pitch=U[2].x;
 float v_k1=U[2].y, v_k2=U[2].z, v_k3=U[2].w, v_k4=U[3].x, v_mx=U[13].x, v_my=U[13].y, v_mclick=U[13].z;
 float v_m1=U[17].x, v_m2=U[17].y, v_m3=U[17].z, v_m4=U[17].w;
 float v_padLx=U[18].x, v_padLy=U[18].y, v_padRx=U[18].z, v_padRy=U[18].w, v_padLt=U[19].x, v_padRt=U[19].y, v_note=U[19].z, v_vel=U[19].w;
 float v_b0=U[7].x, v_b1=U[7].y, v_b2=U[7].z, v_b3=U[7].w, v_b4=U[8].x, v_b5=U[8].y, v_b6=U[8].z, v_b7=U[8].w;
 float v_q0=U[9].x, v_q1=U[9].y, v_q2=U[9].z, v_q3=U[9].w, v_q4=U[10].x, v_q5=U[10].y, v_q6=U[10].z, v_q7=U[10].w;
 float v_x0=xA.x, v_x1=xA.y, v_x2=xA.z, v_x3=xA.w, v_x4=xB.x, v_x5=xB.y, v_x6=xB.z, v_x7=xB.w;
 float v_r0=rA.x, v_r1=rA.y, v_r2=rA.z, v_r3=rA.w, v_r4=rB.x, v_r5=rB.y, v_r6=rB.z, v_r7=rB.w;
 float v_projX = v_c1, v_projY = v_c2, v_projZ = v_c3;
");
  h.Append(projBody);
  h.Append(@" oX = v_projX; oY = v_projY; oZ = v_projZ;
}

[numthreads(64, 1, 1)]
void CSProject(uint3 dtid : SV_DispatchThreadID) {
 uint tid = dtid.x; if (tid >= 960u) return;
 float w = U[0].x, h = U[0].y, t = U[0].z, zm = U[0].w, bass = U[1].x, mid = U[1].y, yw = U[1].w, pt = U[2].x;
 float cx = w * 0.5, cy = h * 0.5;
 static const float4 cU1A = float4(0.0, 0.0, -0.11911770, -0.11911770), cU1B = float4(-0.19273649, -0.19273649, -0.21763887, -0.92193305);
 static const float4 cU2A = float4(-0.09689907, 0.66982357, -0.46350566, 0.46350566), cU2B = float4(-0.23693033, 0.23693033, 0.0, 0.0);
 static const float4 cU3A = float4(0.0, 0.0, -0.38336132, -0.38336132), cU3B = float4(0.23693033, 0.23693033, 0.74996791, -0.17704341);
 static const float4 cU4A = float4(-0.15941060, 0.54488358, 0.11911770, -0.11911770), cU4B = float4(0.56978596, -0.56978596, 0.0, 0.0);
 float sx = 0.0, sy = 0.0, persp = 1.0;
 if (tid < 240u) {
  float4 rA = E8Roots[tid * 2u], rB = E8Roots[tid * 2u + 1u];
  float a01 = t * 0.42 + yw, a23 = t * 0.31 + pt, a45 = t * 0.23 + bass * 0.35, a67 = t * 0.19 + mid * 0.18;
  float4 dA = float4(rA.x*cos(a01)-rA.y*sin(a01), rA.x*sin(a01)+rA.y*cos(a01), rA.z*cos(a23)-rA.w*sin(a23), rA.z*sin(a23)+rA.w*cos(a23));
  float4 dB = float4(rB.x*cos(a45)-rB.y*sin(a45), rB.x*sin(a45)+rB.y*cos(a45), rB.z*cos(a67)-rB.w*sin(a67), rB.z*sin(a67)+rB.w*cos(a67));
  float p1 = Dot8F(dA, dB, cU1A, cU1B), p2 = Dot8F(dA, dB, cU2A, cU2B), p3 = Dot8F(dA, dB, cU3A, cU3B), p4 = Dot8F(dA, dB, cU4A, cU4B);
  float xP, yP, zP;
  if (U[12].z > 0.5) EvalUserProj(p1, p2, p3, p4, dA, dB, rA, rB, 1.41421356, 0.0, 0.0, xP, yP, zP);
  else { float tilt = 0.28 * sin(t * 0.37) + bass * 0.14; xP = p1*cos(tilt) + p3*sin(tilt); yP = p2; zP = -p1*sin(tilt) + p3*cos(tilt); }
  persp = 1.0 / max(0.25, 1.0 - zP * 0.22);
  float sc = (h * 0.39) * zm * (1.0 + bass * 0.08);
  sx = cx + xP * persp * sc; sy = cy - yP * persp * sc;
 } else {
  uint li = tid - 240u, baseOff = li * 6u;
  float bArr[8] = { U[7].x, U[7].y, U[7].z, U[7].w, U[8].x, U[8].y, U[8].z, U[8].w };
  float4 s0 = LeechVecs[baseOff + 0u], s1 = LeechVecs[baseOff + 1u], s2 = LeechVecs[baseOff + 2u];
  float4 s3 = LeechVecs[baseOff + 3u], s4 = LeechVecs[baseOff + 4u], s5 = LeechVecs[baseOff + 5u];
  float src[24] = { s0.x,s0.y,s0.z,s0.w, s1.x,s1.y,s1.z,s1.w, s2.x,s2.y,s2.z,s2.w, s3.x,s3.y,s3.z,s3.w, s4.x,s4.y,s4.z,s4.w, s5.x,s5.y,s5.z,s5.w };
  float rot[24];
  [unroll] for (int p = 0; p < 12; p++) {
   float ang = t * (0.16 + 0.035 * p) + (((p & 1) == 0) ? yw : pt) + bArr[p & 7] * 0.14;
   float cs = cos(ang), sn = sin(ang); int j = p * 2;
   rot[j] = src[j] * cs - src[j + 1] * sn; rot[j + 1] = src[j] * sn + src[j + 1] * cs;
  }
  float4 aA = float4(rot[0],rot[1],rot[2],rot[3]), aB = float4(rot[4],rot[5],rot[6],rot[7]);
  float4 bA = float4(rot[8],rot[9],rot[10],rot[11]), bB = float4(rot[12],rot[13],rot[14],rot[15]);
  float4 dA = float4(rot[16],rot[17],rot[18],rot[19]), dB = float4(rot[20],rot[21],rot[22],rot[23]);
  float nA = sqrt(dot(aA,aA)+dot(aB,aB)), nB = sqrt(dot(bA,bA)+dot(bB,bB)), nC = sqrt(dot(dA,dA)+dot(dB,dB));
  float uA1 = Dot8F(aA,aB,cU1A,cU1B), uA2 = Dot8F(aA,aB,cU2A,cU2B);
  float uB1 = Dot8F(bA,bB,cU1A,cU1B), uB2 = Dot8F(bA,bB,cU2A,cU2B);
  float uD1 = Dot8F(dA,dB,cU1A,cU1B), uD2 = Dot8F(dA,dB,cU2A,cU2B);
  float c1 = 0.62 * (uA1 - 0.5 * (uB1 + uD1) - 0.8660254 * (uB2 - uD2));
  float c2 = 0.62 * (uA2 + 0.8660254 * (uB1 - uD1) - 0.5 * (uB2 + uD2));
  float c3 = 0.58 * (Dot8F(aA,aB,cU3A,cU3B) + Dot8F(bA,bB,cU4A,cU4B) + uD1);
  float c4 = 0.58 * (Dot8F(dA,dB,cU3A,cU3B) - Dot8F(aA,aB,cU4A,cU4B));
  float xP, yP, zP;
  if (U[12].z > 0.5) EvalUserProj(c1, c2, c3, c4, (aA+bA+dA)*0.57735027, (aB+bB+dB)*0.57735027, s0, s1, nA, nB, nC, xP, yP, zP);
  else { xP = c1; yP = c2; zP = c3; }
  persp = 1.0 / max(0.28, 1.0 - zP * 0.20);
  float sc = (h * 0.34) * zm * (1.0 + bass * 0.07);
  sx = cx + xP * persp * sc; sy = cy - yP * persp * sc;
 }
 if (U[13].z > 0.5) {
  float dx = sx - U[14].x, dy = sy - U[14].y, r2 = dx*dx + dy*dy, gRad = 175.0 * U[14].z;
  float pull = U[13].z * exp(-r2 / max(1.0, 2.0 * gRad * gRad));
  float swirl = pull * (0.52 + 0.28 * bass), pinch = 1.0 - 0.38 * pull * sin(t * 4.2 + sqrt(r2) * 0.035);
  sx = U[14].x + dx * pinch - dy * swirl; sy = U[14].y + dx * swirl + dy * pinch;
 }
 float4 prevV = ProjVertsOut[tid];
 bool validPrev = (U[6].z < 0.5) && (prevV.z > 0.01);
 float vx = validPrev ? clamp(sx - prevV.x, -42.0, 42.0) : 0.0;
 float vy = validPrev ? clamp(sy - prevV.y, -42.0, 42.0) : 0.0;
 float spd = sqrt(vx * vx + vy * vy);
 ProjVertsOut[tid] = float4(sx, sy, persp, (float)VertMeta[tid]);
 VertVelOut[tid] = float4(vx, vy, spd, 1.0);
}");
  return h.ToString();
 }

 public static string BuildFluidSplatPassHlsl(int e8EdgeCount, int totalEdgeCount)
 {
  StringBuilder s = new StringBuilder(6144);
  s.Append("cbuffer OmegaParams : register(b0) { float4 U[20]; };\n");
  s.Append("static const uint E8_EDGES = ").Append(e8EdgeCount).Append("u;\n");
  s.Append("static const uint TOT_EDGES = ").Append(totalEdgeCount).Append("u;\n");
  s.Append(@"StructuredBuffer<float4> ProjVerts : register(t0);
StructuredBuffer<float4> VertVel : register(t1);
StructuredBuffer<uint4> LatticeEdges : register(t2);
RWStructuredBuffer<int> FluidInject : register(u0);

[numthreads(64, 1, 1)]
void CSFluidSplat(uint3 dtid : SV_DispatchThreadID) {
 uint tid = dtid.x;
 if (tid >= TOT_EDGES) return;
 int latMode = (int)U[11].x, e8Str = (int)U[11].z, leStr = (int)U[11].w;
 if (tid < E8_EDGES) {
  if (latMode == 1 || (latMode != 3 && (e8Str <= 0 || (tid % (uint)e8Str) != 0u))) return;
  if (latMode == 3) return;
 } else {
  uint li = tid - E8_EDGES;
  int effStr = (latMode == 3) ? 4 : leStr;
  if (latMode == 0 || effStr <= 0 || (li % (uint)effStr) != 0u) return;
 }
 uint fw = (uint)U[15].x, fh = (uint)U[15].y;
 float advGain = U[16].z, bass = U[1].x;
 uint4 ed = LatticeEdges[tid];
 float2 p0 = ProjVerts[ed.x].xy * 0.5, p1 = ProjVerts[ed.y].xy * 0.5;
 float2 v0 = VertVel[ed.x].xy * 0.5, v1 = VertVel[ed.y].xy * 0.5;
 float2 seg = p1 - p0;
 float segLen = length(seg);
 if (segLen < 0.5 || segLen > 1200.0) return;
 int steps = clamp((int)(segLen * 0.28), 2, 24);
 bool isSeedChord = ((tid & 15u) == 0u);
 [loop] for (int st = 0; st < 24; st++) {
  if (st >= steps) break;
  float f = ((float)st + 0.5) / (float)steps;
  float2 p = lerp(p0, p1, f);
  int ix = (int)p.x, iy = (int)p.y;
  if ((uint)ix < fw && (uint)iy < fh) {
   float2 v = lerp(v0, v1, f);
   float spd = length(v);
   int ivx = (int)clamp(v.x * advGain * 105.0, -2400.0, 2400.0);
   int ivy = (int)clamp(v.y * advGain * 105.0, -2400.0, 2400.0);
   uint c3 = ((uint)iy * fw + (uint)ix) * 3u;
   if (ivx != 0) InterlockedAdd(FluidInject[c3 + 0u], ivx);
   if (ivy != 0) InterlockedAdd(FluidInject[c3 + 1u], ivy);
   if (isSeedChord && st == (steps >> 1) && spd > 0.22) {
    int ic = (int)clamp((spd * 26.0 + bass * 18.0) * advGain, 0.0, 210.0);
    if (ic > 0) InterlockedAdd(FluidInject[c3 + 2u], ic);
   }
  }
 }
}");
  return s.ToString();
 }

 public static string BuildFluidStepPassHlsl()
 {
  return @"cbuffer OmegaParams : register(b0) { float4 U[20]; };
StructuredBuffer<float4> RdIn : register(t0);
RWStructuredBuffer<float4> RdOut : register(u0);
RWStructuredBuffer<int> FluidInject : register(u1);

float4 FetchRdClamped(int x, int y, uint fw, uint fh) {
 uint cx = (uint)clamp(x, 0, (int)fw - 1), cy = (uint)clamp(y, 0, (int)fh - 1);
 return RdIn[cy * fw + cx];
}

float4 SampleRdBilinear(float2 pos, uint fw, uint fh) {
 float2 p = clamp(pos, float2(0.0, 0.0), float2((float)fw - 1.001, (float)fh - 1.001));
 int x0 = (int)floor(p.x), y0 = (int)floor(p.y);
 float fx = p.x - (float)x0, fy = p.y - (float)y0;
 float4 c00 = FetchRdClamped(x0, y0, fw, fh), c10 = FetchRdClamped(x0 + 1, y0, fw, fh);
 float4 c01 = FetchRdClamped(x0, y0 + 1, fw, fh), c11 = FetchRdClamped(x0 + 1, y0 + 1, fw, fh);
 return lerp(lerp(c00, c10, fx), lerp(c01, c11, fx), fy);
}

[numthreads(16, 16, 1)]
void CSFluidStep(uint3 dtid : SV_DispatchThreadID) {
 uint fw = (uint)U[15].x, fh = (uint)U[15].y;
 if (dtid.x >= fw || dtid.y >= fh) return;
 uint idx = dtid.y * fw + dtid.x, c3 = idx * 3u;
 int rawIx = FluidInject[c3 + 0u], rawIy = FluidInject[c3 + 1u], rawIc = FluidInject[c3 + 2u];
 FluidInject[c3 + 0u] = 0; FluidInject[c3 + 1u] = 0; FluidInject[c3 + 2u] = 0;

 float cx = (float)fw * 0.5, cy = (float)fh * 0.5;
 float dxC = (float)dtid.x - cx, dyC = (float)dtid.y - cy;
 float radC = sqrt(dxC * dxC + dyC * dyC);

 // Initial 12-Fold Golay/Turyn Rosette Seed on Reset
 if (U[6].z > 0.5) {
  float ang = atan2(dyC, dxC);
  float ring = exp(-abs(radC - min(cx, cy) * 0.26) * 0.35) * (0.5 + 0.5 * cos(12.0 * ang));
  float core = exp(-radC * 0.22);
  float v0 = (ring > 0.52 || core > 0.45) ? 0.36 : 0.0;
  float u0 = 1.0 - v0 * 1.25;
  RdOut[idx] = float4(saturate(u0), saturate(v0), 0.0, 0.0);
  return;
 }

 int x = (int)dtid.x, y = (int)dtid.y;
 float4 cur = RdIn[idx];
 float2 injVel = clamp(float2((float)rawIx, (float)rawIy) / 256.0, -14.0, 14.0);
 float injCat = clamp((float)rawIc / 1024.0, 0.0, 0.24);

 // Interactive Mouse / Xbox / DualSense Right-Stick Fluid Vortex Wand
 if (U[13].z > 0.5 || length(U[18].zw) > 0.16) {
  float2 wand = U[14].xy * 0.5;
  float2 dW = float2((float)x, (float)y) - wand;
  float rW = length(dW);
  float wPull = exp(-(rW * rW) / max(16.0, 950.0 * U[14].z));
  injVel += float2(-dW.y, dW.x) * (0.22 * wPull * (0.6 + U[1].x));
  if (rW < 9.0 * U[14].z) injCat = max(injCat, 0.18 * wPull);
 }

 // MIDI Note-On / Gamepad A-Cross / Hotkey S Golay 24-Petal Fluid Shockwave
 if (U[15].w > 0.5) {
  float targetR = min(cx, cy) * (0.14 + 0.46 * U[19].z);
  float wave = exp(-abs(radC - targetR) * 0.42) * (0.5 + 0.5 * cos(24.0 * atan2(dyC, dxC) + U[0].z));
  if (wave > 0.42) {
   injCat = max(injCat, 0.30 * wave);
   float2 outDir = (radC > 1e-3) ? (float2(dxC, dyC) / radC) : float2(1.0, 0.0);
   injVel += outDir * (4.5 * wave);
  }
 }

 // Jos Stam (1999) Semi-Lagrangian Velocity + Chemical Advection
 float2 vel = clamp(cur.zw * 0.945 + injVel * 0.32, -12.0, 12.0);
 float spd = length(vel);
 float2 backPos = float2((float)x, (float)y) - vel * 0.58;
 float4 adv = SampleRdBilinear(backPos, fw, fh);
 vel = lerp(vel, adv.zw * 0.96, 0.35);
 float advBlend = smoothstep(0.02, 0.32, spd);
 float uVal = lerp(cur.x, adv.x, advBlend);
 float vVal = lerp(cur.y, adv.y, advBlend);

 // John E. Pearson (1993) 9-Point Isotropic Laplacian + Gray-Scott Reaction-Diffusion
 float2 cN  = FetchRdClamped(x, y - 1, fw, fh).xy;
 float2 cS  = FetchRdClamped(x, y + 1, fw, fh).xy;
 float2 cW  = FetchRdClamped(x - 1, y, fw, fh).xy;
 float2 cE  = FetchRdClamped(x + 1, y, fw, fh).xy;
 float2 cNW = FetchRdClamped(x - 1, y - 1, fw, fh).xy;
 float2 cNE = FetchRdClamped(x + 1, y - 1, fw, fh).xy;
 float2 cSW = FetchRdClamped(x - 1, y + 1, fw, fh).xy;
 float2 cSE = FetchRdClamped(x + 1, y + 1, fw, fh).xy;
 float2 lap = (cN + cS + cW + cE) * 0.20 + (cNW + cNE + cSW + cSE) * 0.05 - float2(uVal, vVal);

 float F = U[16].x, K = U[16].y;
 float uvv = uVal * vVal * vVal;
 float nextU = saturate(uVal + (0.2097 * lap.x - uvv + F * (1.0 - uVal)));
 float nextV = saturate(vVal + (0.1050 * lap.y + uvv - (F + K) * vVal) + injCat);

 RdOut[idx] = float4(nextU, nextV, vel.x, vel.y);
}";
 }
 }
}
