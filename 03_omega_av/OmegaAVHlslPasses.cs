using System;
using System.Collections.Generic;
using System.Text;

namespace OmegaAVStudio
{
 public static partial class OmegaHlslTranspiler
 {
 public static string BuildFieldPassHlsl(string pixelSrc, out bool usesSdf, out bool assignsRgb, out string err)
 {
  usesSdf = false; assignsRgb = false; err = null;
  string pixelBody = " v_r = 0.0; v_g = 0.0; v_b = 0.0;\n";
  try
  {
  List<HT> tks1 = Lex(pixelSrc ?? "");
  HashSet<string> ro1 = new HashSet<string>(StringComparer.Ordinal) {
   "pi","tau","phi","e","sqrt2","t","bass","mid","high","yaw","pitch","zoom","k1","k2","k3","k4",
   "m1","m2","m3","m4","padLx","padLy","padRx","padRy","padLt","padRt","note","vel",
   "rdU","rdV","velX","velY",
   "mx","my","mclick","b0","b1","b2","b3","b4","b5","b6","b7","q0","q1","q2","q3","q4","q5","q6","q7",
   "x","y","z","w","px","py","pz","pw","rad","theta"
  };
  HashSet<string> loc1 = new HashSet<string>(StringComparer.Ordinal) { "r", "g", "b", "v", "sdf" };
  List<string> st1 = new List<string>();
  HP hp1 = new HP(tks1, ro1, loc1, false);
  hp1.ParseAll(st1);
  usesSdf = hp1.HasSdf; assignsRgb = hp1.HasRgb;
  StringBuilder sb1 = new StringBuilder();
  foreach (string v in loc1) if (v != "r" && v != "g" && v != "b" && v != "v" && v != "sdf") sb1.Append(" float v_").Append(v).Append(" = 0.0;\n");
  for (int i = 0; i < st1.Count; i++) sb1.Append(" ").Append(st1[i]).Append("\n");
  pixelBody = sb1.ToString();
  }
  catch (Exception ex) { err = ex.Message; }

  StringBuilder h = new StringBuilder(14336);
  h.Append(MathHeaderHlsl);
  h.Append(@"StructuredBuffer<float4> RdField : register(t0);
RWStructuredBuffer<uint> HistPixels : register(u0);
RWStructuredBuffer<uint> HdrClear : register(u1);

float4 SampleFluidAtScreen(float sx, float sy) {
 uint fw = max(1u, (uint)U[15].x), fh = max(1u, (uint)U[15].y);
 float2 p = clamp(float2(sx * 0.5, sy * 0.5), float2(0.0, 0.0), float2((float)fw - 1.001, (float)fh - 1.001));
 uint x0 = (uint)floor(p.x), y0 = (uint)floor(p.y);
 uint x1 = min(fw - 1u, x0 + 1u), y1 = min(fh - 1u, y0 + 1u);
 float fx = p.x - (float)x0, fy = p.y - (float)y0;
 return lerp(lerp(RdField[y0 * fw + x0], RdField[y0 * fw + x1], fx), lerp(RdField[y1 * fw + x0], RdField[y1 * fw + x1], fx), fy);
}

void EvalUserKernel(float v_x, float v_y, float v_z, float v_w, float v_rad, float v_theta, float4 fl, out float outR, out float outG, out float outB, out float outV, out float outSdf) {
 float v_px=v_x, v_py=v_y, v_pz=v_z, v_pw=v_w, v_pi=3.14159265, v_tau=6.28318531, v_phi=1.61803399, v_e=2.71828183, v_sqrt2=1.41421356;
 float v_t=U[0].z, v_zoom=U[0].w, v_bass=U[1].x, v_mid=U[1].y, v_high=U[1].z, v_yaw=U[1].w;
 float v_pitch=U[2].x, v_k1=U[2].y, v_k2=U[2].z, v_k3=U[2].w, v_k4=U[3].x, v_mx=U[13].x, v_my=U[13].y, v_mclick=U[13].z;
 float v_m1=U[17].x, v_m2=U[17].y, v_m3=U[17].z, v_m4=U[17].w;
 float v_padLx=U[18].x, v_padLy=U[18].y, v_padRx=U[18].z, v_padRy=U[18].w, v_padLt=U[19].x, v_padRt=U[19].y, v_note=U[19].z, v_vel=U[19].w;
 float v_rdU=fl.x, v_rdV=fl.y, v_velX=fl.z, v_velY=fl.w;
 float v_b0=U[7].x, v_b1=U[7].y, v_b2=U[7].z, v_b3=U[7].w, v_b4=U[8].x, v_b5=U[8].y, v_b6=U[8].z, v_b7=U[8].w;
 float v_q0=U[9].x, v_q1=U[9].y, v_q2=U[9].z, v_q3=U[9].w, v_q4=U[10].x, v_q5=U[10].y, v_q6=U[10].z, v_q7=U[10].w;
 float v_r=0.0, v_g=0.0, v_b=0.0, v_v=0.0, v_sdf=999.0;
");
  h.Append(pixelBody);
  h.Append(@" outR=v_r; outG=v_g; outB=v_b; outV=v_v; outSdf=v_sdf;
}

[numthreads(16, 16, 1)]
void CSField(uint3 dtid : SV_DispatchThreadID) {
 uint w = (uint)U[0].x, h = (uint)U[0].y;
 if (dtid.x >= w || dtid.y >= h) return;
 uint idx = dtid.y * w + dtid.x;
 uint b4 = idx * 4u;
 HdrClear[b4 + 0u] = 0u; HdrClear[b4 + 1u] = 0u; HdrClear[b4 + 2u] = 0u; HdrClear[b4 + 3u] = 0u;
 float simTime = U[0].z, zm = U[0].w, bass = U[1].x, mid = U[1].y, high = U[1].z, yw = U[1].w, pt = U[2].x;
 int mode = (int)U[3].y;
 float rawNx = ((float)dtid.x - w * 0.5) / (h * 0.5), rawNy = (h * 0.5 - (float)dtid.y) / (h * 0.5);
 float nx = rawNx / zm, ny = rawNy / zm;
 float4 fl = (U[15].z > 0.5) ? SampleFluidAtScreen((float)dtid.x, (float)dtid.y) : float4(1.0, 0.0, 0.0, 0.0);
 uint prev = (U[6].z > 0.5) ? 0u : HistPixels[idx];
 uint pr = (((prev >> 16) & 255u) * 138u) >> 8, pg = (((prev >> 8) & 255u) * 144u) >> 8, pb = ((prev & 255u) * 158u) >> 8;
 int sr = 0, sg = 0, sb = 0;
 if (mode == 0) {
  float bArr[7] = { U[7].x, U[7].y, U[7].z, U[7].w, U[8].x, U[8].y, U[8].z };
  float s = 0.0, c2 = 0.0, sc = 7.5 + bass * 2.4;
  [unroll] for (int k = 0; k < 7; k++) { float ang = (3.14159265*k)/7.0 + yw*0.25; float proj = (nx*cos(ang)+ny*sin(ang))*sc + simTime*(0.8+0.15*k) + bArr[k]*1.9; s += cos(proj); if ((k&1)==0) c2 += sin(proj*1.6180339); }
  float v = abs(s)*0.16, gl = max(0.0, 1.0 - sqrt(nx*nx+ny*ny)*0.65);
  sr = (int)min(120.0, (v*v*42.0+bass*18.0)*gl); sg = (int)min(145.0, (v*58.0+abs(c2)*16.0)*gl); sb = (int)min(195.0, (v*92.0+18.0+high*22.0)*gl);
 } else if (mode == 1) {
  bool sp; uint ch; float s = 2.6 + bass * 0.4;
  float vd = om_e8fast(nx*s+cos(simTime*0.35+yw), ny*s+sin(simTime*0.31+pt), (nx-ny)*1.4+simTime*0.28, (nx+ny)*1.4-simTime*0.22, bass*0.65, mid*0.45, high*0.45, sin(simTime*0.5), sp, ch);
  float wall = exp(-abs(vd-0.72)*11.5), core = exp(-vd*4.2); int hm = (int)(ch & 63u);
  sr = (int)min(255.0, wall*(sp?235.0:65.0)+core*(50.0+hm)); sg = (int)min(255.0, wall*(sp?120.0:225.0)+core*95.0); sb = (int)min(255.0, wall*250.0+core*(145.0+(63-hm)));
 } else if (mode == 2) {
  float3 rd = normalize(float3(nx*0.85, ny*0.85, 1.35));
  float cw = -0.42+0.16*cos(simTime*0.65+yw)+bass*0.06, cx = 0.28*sin(simTime*0.51+pt)+U[9].x*0.05, cy2 = -0.18*cos(simTime*0.43)+U[9].z*0.05, cz = 0.14*sin(simTime*0.8)+mid*0.03;
  float distT = 0.0, trap = 99.0; int hit = 0;
  [loop] for (int st = 0; st < 22; st++) {
   float pX = rd.x*distT, pY = rd.y*distT, pZ = -2.35+rd.z*distT, cY = cos(simTime*0.35+yw), sY = sin(simTime*0.35+yw);
   float qx = pX*cY-pZ*sY, qy = pY, qz = pX*sY+pZ*cY, qw = 0.18*sin(simTime*0.5), dq2 = 1.0, r2 = 0.0;
   [loop] for (int it = 0; it < 7; it++) { r2 = qx*qx+qy*qy+qz*qz+qw*qw; if (r2<trap) trap=r2; if (r2>6.0) break; dq2 *= 4.0*r2; float nxq = qx*qx-qy*qy-qz*qz-qw*qw+cw; float nyq = 2.0*qx*qy+cx, nzq = 2.0*qx*qz+cy2, nwq = 2.0*qx*qw+cz; qx=nxq; qy=nyq; qz=nzq; qw=nwq; }
   float r = sqrt(r2), de = 0.5*r*log(max(1.0001,r))/sqrt(max(1e-9,dq2)); if (de < 0.0045) { hit = st+1; break; } distT += de*0.88; if (distT > 4.2) break;
  }
  if (hit > 0) { float occ = 1.0 - hit/24.0; sr = (int)min(245.0, (70.0+170.0*(1.0-min(1.0,trap)))*occ+bass*35.0); sg = (int)min(245.0, 45.0+155.0*occ+mid*28.0); sb = (int)min(255.0, 125.0+130.0*occ); }
  else { float bg = exp(-2.2*(nx*nx+ny*ny)); sr = (int)(12.0*bg); sg = (int)(22.0*bg); sb = (int)(48.0*bg); }
 } else if (mode == 3) {
  float r2 = nx*nx+ny*ny, invD = 1.0/(1.0+r2), s1 = 2.0*nx*invD, s2 = 2.0*ny*invD, s3 = (r2-1.0)*invD;
  float a1 = simTime*0.85+yw, a2 = simTime*0.55+pt, u1 = s1*cos(a1)-s3*sin(a1), v1 = s2*cos(a2)+(s1*sin(a1)+s3*cos(a1))*sin(a2);
  float tor = abs(sqrt(u1*u1+s2*s2)-(0.68+0.12*sin(simTime+bass))), gr = abs(sin(12.0*atan2(v1,u1)+simTime*2.0)*sin(12.0*tor)), itn = exp(-tor*5.2)*(0.55+0.85*gr);
  sr = (int)min(220.0, itn*(55.0+high*75.0)); sg = (int)min(235.0, itn*(145.0+mid*45.0)); sb = (int)min(255.0, itn*225.0);
 } else if (mode == 4) {
  float nM = 3.0+floor((U[9].x+2.0)*1.5), mM = 5.0+floor((U[9].y+2.0)*1.5); if (abs(nM-mM)<0.5) mM += 2.0;
  float ch = cos(nM*3.14159265*nx*0.65)*cos(mM*3.14159265*ny*0.65) - cos(mM*3.14159265*nx*0.65)*cos(nM*3.14159265*ny*0.65), nd = exp(-abs(ch)*(9.0+bass*4.0));
  sr = (int)min(255.0, nd*(210.0+bass*40.0)); sg = (int)min(255.0, nd*185.0+abs(ch)*18.0); sb = (int)min(255.0, nd*95.0+abs(ch)*42.0);
 } else {
  float rad = sqrt(nx*nx+ny*ny), theta = atan2(ny, nx);
");
  if (usesSdf)
  {
  h.Append(@"  float3 ro = float3(U[3].z, U[3].w, U[4].x), fw = float3(U[4].y, U[4].z, U[4].w), rt = float3(U[5].x, U[5].y, U[5].z), up = float3(U[5].w, U[6].x, U[6].y);
  float3 rd = normalize(rt*(rawNx*0.85) + up*(rawNy*0.85) + fw*1.35);
  float marchT = 0.0, glow = 0.0, pw = 0.35*sin(simTime*0.65), oR = 0.0, oG = 0.0, oB = 0.0, oV = 0.0, d = 999.0; int hitStep = -1;
  [loop] for (int st = 0; st < 26; st++) {
   float3 p = ro + rd * marchT; EvalUserKernel(p.x, p.y, p.z, pw, rad, theta, fl, oR, oG, oB, oV, d);
   glow += 0.014 / (0.025 + d*d*8.0); if (abs(d) < 0.004) { hitStep = st; break; } marchT += d*0.88; if (marchT > 5.5) break;
  }
  if (hitStep >= 0) {
   float3 hp = ro + rd * marchT; float eps = 0.003, d1, d2, d3, d4, tR, tG, tB, tV;
   EvalUserKernel(hp.x+eps, hp.y-eps, hp.z-eps, pw, rad, theta, fl, tR, tG, tB, tV, d1);
   EvalUserKernel(hp.x-eps, hp.y-eps, hp.z+eps, pw, rad, theta, fl, tR, tG, tB, tV, d2);
   EvalUserKernel(hp.x-eps, hp.y+eps, hp.z-eps, pw, rad, theta, fl, tR, tG, tB, tV, d3);
   EvalUserKernel(hp.x+eps, hp.y+eps, hp.z+eps, pw, rad, theta, fl, tR, tG, tB, tV, d4);
   float3 nrm = normalize(float3(d1-d2-d3+d4, -d1-d2+d3+d4, -d1+d2-d3+d4));
   EvalUserKernel(hp.x, hp.y, hp.z, pw, rad, theta, fl, oR, oG, oB, oV, d);
");
  h.Append(assignsRgb ? "   float cr = saturate(oR), cg = saturate(oG), cb = saturate(oB);\n" : "   float cr = om_palR(oV), cg = om_palG(oV), cb = om_palB(oV);\n");
  h.Append(@"   float diff = max(0.12, nrm.x*0.52 + nrm.y*0.68 - nrm.z*0.51), fres = om_pow(1.0 - max(0.0, -dot(nrm, rd)), 2.6), ao = 1.0 - ((float)hitStep / 30.0);
   sr = (int)min(255.0, (cr*diff*225.0 + fres*95.0 + glow*35.0)*ao); sg = (int)min(255.0, (cg*diff*225.0 + fres*135.0 + glow*55.0)*ao); sb = (int)min(255.0, (cb*diff*225.0 + fres*195.0 + glow*85.0)*ao);
  } else { sr = (int)min(255.0, glow*(42.0+bass*35.0)); sg = (int)min(255.0, glow*(75.0+mid*25.0)); sb = (int)min(255.0, glow*138.0 + 10.0*exp(-rad*1.4)); }
");
  }
  else
  {
  h.Append("  float oR=0.0, oG=0.0, oB=0.0, oV=0.0, oS=999.0; EvalUserKernel(nx, ny, 0.0, 0.0, rad, theta, fl, oR, oG, oB, oV, oS);\n");
  h.Append(assignsRgb
   ? "  sr = (int)(saturate(oR)*255.0); sg = (int)(saturate(oG)*255.0); sb = (int)(saturate(oB)*255.0);\n"
   : "  float itn = 0.5 + 0.5*sin(oV*3.14159265); sr = (int)(om_palR(oV*0.35+simTime*0.05)*itn*255.0); sg = (int)(om_palG(oV*0.35+simTime*0.05)*itn*255.0); sb = (int)(om_palB(oV*0.35+simTime*0.05)*itn*255.0);\n");
  }
  h.Append(@" }
 uint fr = min(255u, max(pr, (uint)max(0, sr))), fg = min(255u, max(pg, (uint)max(0, sg))), fb = min(255u, max(pb, (uint)max(0, sb)));
 HistPixels[idx] = 0xFF000000u | (fr << 16) | (fg << 8) | fb;
}");
  return h.ToString();
 }

 public static string BuildLinesPassHlsl(int e8EdgeCount, int totalEdgeCount)
 {
  StringBuilder s = new StringBuilder(10240);
  s.Append("cbuffer OmegaParams : register(b0) { float4 U[20]; };\n");
  s.Append("static const uint E8_EDGES = ").Append(e8EdgeCount).Append("u;\n");
  s.Append("static const uint TOT_EDGES = ").Append(totalEdgeCount).Append("u;\n");
  s.Append(@"StructuredBuffer<float4> ProjVerts : register(t0);
StructuredBuffer<uint4> LatticeEdges : register(t1);
StructuredBuffer<float2> WaveStereo : register(t2);
StructuredBuffer<float4> VertVel : register(t3);
RWStructuredBuffer<uint> HdrLineRGB : register(u0);

float fpart_om(float v) { return v - floor(v); }

void PlotHdr(uint w, uint h, int x, int y, uint cr, uint cg, uint cb, uint a255, bool isHud) {
 if ((uint)x >= w || (uint)y >= h || a255 == 0u) return;
 uint b = ((uint)y * w + (uint)x) * 4u;
 uint wr = (cr * a255) >> 8, wg = (cg * a255) >> 8, wb = (cb * a255) >> 8;
 if (isHud) {
  uint pk = (min(127u, wr >> 1) << 20) | (min(127u, wg >> 1) << 10) | min(127u, wb >> 1);
  if (pk > 0u) InterlockedAdd(HdrLineRGB[b + 3u], pk);
 } else {
  if (wr > 0u) InterlockedAdd(HdrLineRGB[b + 0u], wr);
  if (wg > 0u) InterlockedAdd(HdrLineRGB[b + 1u], wg);
  if (wb > 0u) InterlockedAdd(HdrLineRGB[b + 2u], wb);
 }
}

void DrawBresGpu(uint w, uint h, int x0, int y0, int x1, int y1, uint cr, uint cg, uint cb, bool isHud) {
 if ((x0 < 0 && x1 < 0) || (x0 >= (int)w && x1 >= (int)w) || (y0 < 0 && y1 < 0) || (y0 >= (int)h && y1 >= (int)h)) return;
 int dx = abs(x1 - x0), dy = -abs(y1 - y0);
 if (dx > 4096 || dy < -4096) return;
 int sx = (x0 < x1) ? 1 : -1, sy = (y0 < y1) ? 1 : -1, err = dx + dy;
 [loop] for (int st = 0; st < 4096; st++) {
  PlotHdr(w, h, x0, y0, cr, cg, cb, 255u, isHud);
  if (x0 == x1 && y0 == y1) break;
  int e2 = err * 2;
  if (e2 >= dy) { err += dy; x0 += sx; }
  if (e2 <= dx) { err += dx; y0 += sy; }
 }
}

void DrawWuGpu(uint w, uint h, float x0, float y0, float x1, float y1, uint cr, uint cg, uint cb, int rMode, bool isHud) {
 if (rMode == 1 || (rMode == 2 && ((x0 + x1) * 0.5) >= (w * 0.5))) {
  DrawBresGpu(w, h, (int)round(x0), (int)round(y0), (int)round(x1), (int)round(y1), cr, cg, cb, isHud); return;
 }
 if ((x0 < -64.0 && x1 < -64.0) || (x0 > w + 64.0 && x1 > w + 64.0) || (y0 < -64.0 && y1 < -64.0) || (y0 > h + 64.0 && y1 > h + 64.0)) return;
 bool steep = abs(y1 - y0) > abs(x1 - x0);
 if (steep) { float t0 = x0; x0 = y0; y0 = t0; float t1 = x1; x1 = y1; y1 = t1; }
 if (x0 > x1) { float tx = x0; x0 = x1; x1 = tx; float ty = y0; y0 = y1; y1 = ty; }
 float dx = x1 - x0, dy = y1 - y0;
 if (dx > 4096.0) return;
 float grad = (dx < 1e-5) ? 1.0 : (dy / dx);
 float xend = round(x0), yend = y0 + grad * (xend - x0), xgap = 1.0 - fpart_om(x0 + 0.5);
 int xpx1 = (int)xend, ypx1 = (int)floor(yend); float fy1 = fpart_om(yend);
 if (steep) { PlotHdr(w,h,ypx1,xpx1,cr,cg,cb,(uint)((1.0-fy1)*xgap*255.0),isHud); PlotHdr(w,h,ypx1+1,xpx1,cr,cg,cb,(uint)(fy1*xgap*255.0),isHud); }
 else       { PlotHdr(w,h,xpx1,ypx1,cr,cg,cb,(uint)((1.0-fy1)*xgap*255.0),isHud); PlotHdr(w,h,xpx1,ypx1+1,cr,cg,cb,(uint)(fy1*xgap*255.0),isHud); }
 float intery = yend + grad;
 xend = round(x1); yend = y1 + grad * (xend - x1); xgap = fpart_om(x1 + 0.5);
 int xpx2 = (int)xend, ypx2 = (int)floor(yend); float fy2 = fpart_om(yend);
 if (steep) { PlotHdr(w,h,ypx2,xpx2,cr,cg,cb,(uint)((1.0-fy2)*xgap*255.0),isHud); PlotHdr(w,h,ypx2+1,xpx2,cr,cg,cb,(uint)(fy2*xgap*255.0),isHud); }
 else       { PlotHdr(w,h,xpx2,ypx2,cr,cg,cb,(uint)((1.0-fy2)*xgap*255.0),isHud); PlotHdr(w,h,xpx2,ypx2+1,cr,cg,cb,(uint)(fy2*xgap*255.0),isHud); }
 int interFix = (int)(intery * 65536.0), gradFix = (int)(grad * 65536.0);
 int maxLim = steep ? ((int)h + 1) : ((int)w + 1);
 int stX = max(0, xpx1 + 1), enX = min(maxLim, xpx2);
 if (stX > xpx1 + 1) interFix += (stX - (xpx1 + 1)) * gradFix;
 int spanWu = enX - stX;
 [loop] for (int stepWu = 0; stepWu < 4096; stepWu++) {
  if (stepWu >= spanWu) break;
  int x = stX + stepWu;
  int yInt = interFix >> 16; uint fr8 = (uint)((interFix >> 8) & 255);
  if (steep) { PlotHdr(w,h,yInt,x,cr,cg,cb,255u-fr8,isHud); PlotHdr(w,h,yInt+1,x,cr,cg,cb,fr8,isHud); }
  else       { PlotHdr(w,h,x,yInt,cr,cg,cb,255u-fr8,isHud); PlotHdr(w,h,x,yInt+1,cr,cg,cb,fr8,isHud); }
  interFix += gradFix;
 }
}

void DrawVertGpu(uint w, uint h, float cx, float cy, float rad, uint cr, uint cg, uint cb, int rMode) {
 int x0 = max(0, (int)floor(cx - rad - 1.0)), x1 = min((int)w - 1, (int)ceil(cx + rad + 1.0));
 int y0 = max(0, (int)floor(cy - rad - 1.0)), y1 = min((int)h - 1, (int)ceil(cy + rad + 1.0));
 bool hard = (rMode == 1) || (rMode == 2 && cx >= w * 0.5);
 int spanVy = y1 - y0, spanVx = x1 - x0;
 [loop] for (int dyi = 0; dyi <= 32; dyi++) {
  if (dyi > spanVy) break;
  int py = y0 + dyi;
  float dy = (float)py - cy;
  [loop] for (int dxi = 0; dxi <= 32; dxi++) {
   if (dxi > spanVx) break;
   int px = x0 + dxi;
   float dx = (float)px - cx, dist = sqrt(dx*dx + dy*dy);
   if (hard) { if (dist <= max(1.0, round(rad))) PlotHdr(w, h, px, py, cr, cg, cb, 255u, false); }
   else { float cov = saturate(rad + 0.55 - dist); if (cov > 0.0) PlotHdr(w, h, px, py, cr, cg, cb, (uint)(cov * 255.0), false); }
  }
 }
}

[numthreads(64, 1, 1)]
void CSLinesAndHud(uint3 dtid : SV_DispatchThreadID) {
 uint tid = dtid.x, w = (uint)U[0].x, h = (uint)U[0].y;
 int latMode = (int)U[11].x, rMode = (int)U[11].y, e8Str = (int)U[11].z, leStr = (int)U[11].w;
 float hdScale = U[14].z;
 if (tid < TOT_EDGES) {
  if (tid < E8_EDGES) {
   if ((latMode != 0 && latMode != 2) || e8Str <= 0 || (tid % (uint)e8Str) != 0u) return;
  } else {
   uint li = tid - E8_EDGES;
   if ((latMode != 1 && latMode != 2) || leStr <= 0 || (li % (uint)leStr) != 0u) return;
  }
  uint4 ed = LatticeEdges[tid];
  float4 va = ProjVerts[ed.x], vb = ProjVerts[ed.y];
  float spdBoost = 1.0 + min(0.85, (VertVel[ed.x].z + VertVel[ed.y].z) * 0.045);
  uint pk = ed.w;
  uint cr = (uint)min(255.0, ((float)((pk >> 16) & 255u)) * spdBoost);
  uint cg = (uint)min(255.0, ((float)((pk >> 8) & 255u)) * spdBoost);
  uint cb = (uint)min(255.0, ((float)(pk & 255u)) * spdBoost);
  DrawWuGpu(w, h, va.x, va.y, vb.x, vb.y, cr, cg, cb, rMode, false);
  return;
 }
 uint vOff = tid - TOT_EDGES;
 if (vOff < 960u) {
  if (vOff < 240u) {
   if (latMode != 0 && latMode != 2) return;
   float4 vp = ProjVerts[vOff]; uint r = (uint)vp.w; bool hi = ((int)vOff == (int)U[12].x);
   float rad = (hi ? 4.2 : (r >= 6u ? 2.1 : 1.3)) * hdScale;
   DrawVertGpu(w, h, vp.x, vp.y, rad, hi ? 255u : (45u + r*24u), hi ? 230u : (110u + r*16u), hi ? 90u : (210u - r*12u), rMode);
  } else {
   if (latMode != 1 && latMode != 2) return;
   uint li = vOff - 240u; float4 vp = ProjVerts[vOff]; uint sh = (uint)vp.w; bool hi = ((int)li == (int)U[12].y);
   float rad = (hi ? 4.4 : 1.65) * hdScale;
   uint cr = hi ? 255u : (sh == 0u ? 56u : (sh == 1u ? 192u : 250u));
   uint cg = hi ? 245u : (sh == 0u ? 215u : (sh == 1u ? 115u : 204u));
   uint cb = hi ? 120u : (sh == 0u ? 255u : (sh == 1u ? 255u : 65u));
   DrawVertGpu(w, h, vp.x, vp.y, rad, cr, cg, cb, rMode);
  }
  return;
 }
 uint hOff = vOff - 960u;
 if (hOff == 210u && rMode == 2) { DrawBresGpu(w, h, (int)(w >> 1), 0, (int)(w >> 1), (int)h - 1, 56u, 189u, 248u, true); return; }
 if (U[12].w < 0.5) return;
 float vRad = 44.0 * hdScale, vCx = (float)w - 58.0 * hdScale, vCy = (float)h - 58.0 * hdScale;
 if (hOff < 199u) {
  float2 s0 = WaveStereo[hOff], s1 = WaveStereo[hOff + 1u];
  DrawWuGpu(w, h, vCx + s0.x * vRad, vCy - s0.y * vRad, vCx + s1.x * vRad, vCy - s1.y * vRad, 32u, 205u, 138u, rMode, true);
 } else if (hOff == 199u) {
  DrawBresGpu(w, h, (int)(vCx - vRad), (int)vCy, (int)(vCx + vRad), (int)vCy, 25u, 45u, 70u, true);
  DrawBresGpu(w, h, (int)vCx, (int)(vCy - vRad), (int)vCx, (int)(vCy + vRad), 25u, 45u, 70u, true);
 } else if (hOff >= 200u && hOff < 208u) {
  uint b = hOff - 200u;
  float bArr[8] = { U[7].x, U[7].y, U[7].z, U[7].w, U[8].x, U[8].y, U[8].z, U[8].w };
  int barW = max(6, (int)(8.0 * hdScale)), barStep = max(10, (int)(13.0 * hdScale));
  int bx = (int)(12.0 * hdScale) + (int)b * barStep, padB = (int)(8.0 * hdScale);
  int bh = min((int)(62.0 * hdScale), (int)(bArr[b] * 32.0 * hdScale));
  [loop] for (int dx = 0; dx < 32; dx++) { if (dx >= barW) break; DrawBresGpu(w, h, bx + dx, (int)h - padB, bx + dx, (int)h - padB - bh, 35u + b * 22u, 160u, 245u - b * 18u, true); }
 }
}");
  return s.ToString();
 }

 public static string BuildCompositePassHlsl()
 {
  return @"cbuffer OmegaParams : register(b0) { float4 U[20]; };
StructuredBuffer<uint> InHist : register(t0);
StructuredBuffer<uint> InHdr : register(t1);
StructuredBuffer<float4> InFluid : register(t2);
RWTexture2D<float4> OutTex : register(u0);
RWStructuredBuffer<uint> OutReadback : register(u1);

float4 SampleFluidComp(float sx, float sy) {
 uint fw = max(1u, (uint)U[15].x), fh = max(1u, (uint)U[15].y);
 float2 p = clamp(float2(sx * 0.5, sy * 0.5), float2(0.0, 0.0), float2((float)fw - 1.001, (float)fh - 1.001));
 uint x0 = (uint)floor(p.x), y0 = (uint)floor(p.y);
 uint x1 = min(fw - 1u, x0 + 1u), y1 = min(fh - 1u, y0 + 1u);
 float fx = p.x - (float)x0, fy = p.y - (float)y0;
 return lerp(lerp(InFluid[y0 * fw + x0], InFluid[y0 * fw + x1], fx), lerp(InFluid[y1 * fw + x0], InFluid[y1 * fw + x1], fx), fy);
}

float3 SampleSceneHdr(int x, int y, uint w, uint h) {
 uint cx = (uint)clamp(x, 0, (int)w - 1), cy = (uint)clamp(y, 0, (int)h - 1);
 uint idx = cy * w + cx, hp = InHist[idx], b = idx * 4u;
 float3 fld = float3((float)((hp >> 16) & 255u), (float)((hp >> 8) & 255u), (float)(hp & 255u)) / 255.0;
 float3 ln = float3((float)InHdr[b + 0u], (float)InHdr[b + 1u], (float)InHdr[b + 2u]) / 255.0;
 return fld + ln;
}

float3 ACESFilm(float3 x) {
 return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
}

[numthreads(16, 16, 1)]
void CSComposite(uint3 dtid : SV_DispatchThreadID) {
 uint w = (uint)U[0].x, h = (uint)U[0].y;
 if (dtid.x >= w || dtid.y >= h) return;
 int fx = (int)U[13].w, fluidMode = (int)U[15].z;
 float cx = w * 0.5, cy = h * 0.5, sx = (float)dtid.x, sy = (float)dtid.y;
 if (fx == 2 || fx == 3) {
  float dx = sx - cx, dy = cy - sy, r = sqrt(dx*dx + dy*dy);
  float sectors = (fx == 2) ? 6.0 : 8.0, seg = 6.28318531 / sectors, halfSeg = 0.5 * seg;
  float ang = (r < 1e-4) ? 0.0 : (atan2(dy, dx) + U[0].z * 0.08 + 6.28318531);
  float m = ang - seg * floor(ang / seg);
  if (m > halfSeg) m = seg - m;
  sx = cx + r * cos(m); sy = cy - r * sin(m);
 }

 float3 fluidAdd = float3(0.0, 0.0, 0.0);
 if (fluidMode > 0) {
  float4 flC = SampleFluidComp(sx, sy);
  float4 flX = SampleFluidComp(sx + 2.0, sy);
  float4 flY = SampleFluidComp(sx, sy + 2.0);
  float2 gradV = float2(flX.y - flC.y, flY.y - flC.y);
  float glowGain = U[16].w;
  sx += gradV.x * 36.0 * glowGain * U[14].z;
  sy += gradV.y * 36.0 * glowGain * U[14].z;
  float3 nrm = normalize(float3(-gradV.x * 11.0, -gradV.y * 11.0, 1.0));
  float spec = pow(max(0.0, dot(nrm, normalize(float3(0.38, -0.45, 0.81)))), 14.0);
  float vRidge = smoothstep(0.04, 0.36, flC.y);
  float kin = min(1.0, length(flC.zw) * 0.14);
  float3 dyeA = (fluidMode == 2) ? float3(0.18, 0.92, 0.72) : (fluidMode == 3 ? float3(0.68, 0.35, 1.0) : float3(0.12, 0.78, 1.0));
  float3 dyeB = float3(1.0, 0.62, 0.22) * kin + float3(0.35, 0.98, 0.88) * vRidge;
  fluidAdd = (dyeA * vRidge * 0.62 + dyeB * 0.42 + float3(0.85, 0.96, 1.0) * spec * 0.75 * vRidge) * glowGain;
 }

 int ix = (int)round(sx), iy = (int)round(sy);
 float3 col;
 if (fx == 0) {
  float3 hdr = SampleSceneHdr(ix, iy, w, h) + fluidAdd;
  col = saturate(hdr / (1.0 + 0.16 * hdr));
 } else {
  float2 dir = float2(sx - cx, sy - cy) / max(1.0, h * 0.5);
  int sh = (int)round((1.5 + U[1].x * 2.4) * U[14].z * length(dir));
  int ox = (dir.x >= 0.0) ? sh : -sh, oy = (dir.y >= 0.0) ? sh : -sh;
  float cR = SampleSceneHdr(ix + ox, iy + oy, w, h).r;
  float cG = SampleSceneHdr(ix, iy, w, h).g;
  float cB = SampleSceneHdr(ix - ox, iy - oy, w, h).b;
  float3 baseHdr = float3(cR, cG, cB) + fluidAdd;
  int br = max(2, (int)round(2.6 * U[14].z));
  float3 bloom = SampleSceneHdr(ix+br, iy, w, h) + SampleSceneHdr(ix-br, iy, w, h)
               + SampleSceneHdr(ix, iy+br, w, h) + SampleSceneHdr(ix, iy-br, w, h)
               + (SampleSceneHdr(ix+br, iy+br, w, h) + SampleSceneHdr(ix-br, iy-br, w, h)
                + SampleSceneHdr(ix+br, iy-br, w, h) + SampleSceneHdr(ix-br, iy+br, w, h)) * 0.7;
  col = ACESFilm(baseHdr * 1.08 + max(float3(0.0,0.0,0.0), bloom * 0.14 - 0.06) * (0.65 + U[1].x * 0.45));
 }
 if (U[13].z > 0.5) {
  float md = length(float2((float)dtid.x - U[14].x, (float)dtid.y - U[14].y));
  float ring = exp(-abs(md - 26.0 * U[14].z) * 0.55) + 0.45 * exp(-md * 0.12);
  col = saturate(col + float3(0.22, 0.82, 1.0) * ring * 0.65);
 }
 uint rawIdx = dtid.y * w + dtid.x;
 uint hudPk = InHdr[rawIdx * 4u + 3u];
 if (hudPk != 0u) {
  float3 hud = float3((float)(((hudPk >> 20) & 1023u) << 1), (float)(((hudPk >> 10) & 1023u) << 1), (float)((hudPk & 1023u) << 1)) / 255.0;
  col = saturate(col + hud);
 }
 OutTex[dtid.xy] = float4(col.r, col.g, col.b, 1.0);
 if (U[6].w > 0.5) {
  uint ur = (uint)(col.r * 255.0 + 0.5), ug = (uint)(col.g * 255.0 + 0.5), ub = (uint)(col.b * 255.0 + 0.5);
  OutReadback[rawIdx] = 0xFF000000u | (min(255u, ur) << 16) | (min(255u, ug) << 8) | min(255u, ub);
 }
}";
 }
 }
}
