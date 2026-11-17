using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmegaAVStudio
{
 // Dual-AST HLSL cs_5_0 Transpiler + Full-VRAM GPU Ping-Pong Fluid, 24D Leech Projection & Atomic Wu AA
 public static partial class OmegaHlslTranspiler
 {
 internal enum HK { End, Num, Id, Op, LP, RP, Com, Semi, QM, Col }
 internal struct HT { public HK K; public string T; public int L; }

 internal const string MathHeaderHlsl = @"cbuffer OmegaParams : register(b0) { float4 U[20]; };
float om_div(float a, float b) { return abs(b) < 1e-7 ? 0.0 : (a / b); }
float om_sat(float a) { return saturate(a); }
float om_pow(float a, float b) { return pow(max(1e-6, abs(a)), clamp(b, -32.0, 32.0)); }
float om_mod(float a, float b) { return abs(b) < 1e-7 ? 0.0 : (a - b * floor(a / b)); }
float om_fract(float a) { return a - floor(a); }
float om_frac(float a) { return a - floor(a); }
float om_asin(float a) { return asin(clamp(a, -1.0, 1.0)); }
float om_acos(float a) { return acos(clamp(a, -1.0, 1.0)); }
float om_sinh(float a) { float c = clamp(a, -20.0, 20.0); return 0.5 * (exp(c) - exp(-c)); }
float om_cosh(float a) { float c = clamp(a, -20.0, 20.0); return 0.5 * (exp(c) + exp(-c)); }
float om_tanh(float a) { float c = clamp(a, -20.0, 20.0); float e2 = exp(2.0 * c); return (e2 - 1.0) / (e2 + 1.0); }
float om_exp(float a) { return exp(clamp(a, -40.0, 40.0)); }
float om_log(float a) { return log(max(1e-12, abs(a))); }
float om_sqrt(float a) { return sqrt(max(0.0, a)); }
float om_len2(float x, float y) { return sqrt(x*x + y*y); }
float om_len3(float x, float y, float z) { return sqrt(x*x + y*y + z*z); }
float om_len4(float x, float y, float z, float w) { return sqrt(x*x + y*y + z*z + w*w); }
float om_rot1(float a, float b, float ang) { return a * cos(ang) - b * sin(ang); }
float om_rot2(float a, float b, float ang) { return a * sin(ang) + b * cos(ang); }
float om_rep(float p, float per) { return om_mod(p + 0.5 * per, per) - 0.5 * per; }
float om_smin(float a, float b, float k) { float kk = max(0.0001, k); float h = saturate(0.5 + 0.5 * (b - a) / kk); return b + (a - b) * h - kk * h * (1.0 - h); }
float om_smin2(float a, float b) { return om_smin(a, b, 0.2); }
float om_smax(float a, float b, float k) { return -om_smin(-a, -b, k); }
float om_sdSphere(float x, float y, float z, float r) { return sqrt(x*x + y*y + z*z) - r; }
float om_sdTorus(float x, float y, float z, float R, float r) { float q = sqrt(x*x + z*z) - R; return sqrt(q*q + y*y) - r; }
float om_sdBox(float x, float y, float z, float bx, float by, float bz) { float dx = abs(x)-bx, dy = abs(y)-by, dz = abs(z)-bz; float ox = max(dx,0.0), oy = max(dy,0.0), oz = max(dz,0.0); return sqrt(ox*ox+oy*oy+oz*oz) + min(max(dx,max(dy,dz)),0.0); }
float om_sdOcta(float x, float y, float z, float s) { return (abs(x) + abs(y) + abs(z) - s) * 0.57735027; }
float om_sdGyroid(float x, float y, float z, float sc) { float s = max(0.05, abs(sc)); return (sin(x*s)*cos(y*s) + sin(y*s)*cos(z*s) + sin(z*s)*cos(x*s)) / s; }
float om_sdLink(float x, float y, float z, float le, float r1, float r2) { float qy = max(abs(y)-le, 0.0), qxy = sqrt(x*x + qy*qy) - r1; return sqrt(qxy*qxy + z*z) - r2; }
float om_sdClifford4D(float x, float y, float z, float w, float R, float r) { float d1 = sqrt(x*x+y*y)-R, d2 = sqrt(z*z+w*w)-R; return sqrt(d1*d1+d2*d2)-r; }
float om_mandelbulb(float x, float y, float z, float pwr) {
 float zx = x, zy = y, zz = z, dr = 1.0, r = 0.0, pw = max(2.0, pwr);
 [loop] for (int i = 0; i < 6; i++) {
  r = sqrt(zx*zx + zy*zy + zz*zz); if (r > 2.5) break;
  float th = om_acos(om_div(zz, r)) * pw, ph = atan2(zy, zx) * pw;
  dr = om_pow(max(1e-5, r), pw - 1.0) * pw * dr + 1.0;
  float zr = om_pow(max(1e-5, r), pw);
  zx = zr * sin(th) * cos(ph) + x; zy = zr * sin(th) * sin(ph) + y; zz = zr * cos(th) + z;
 }
 return 0.5 * log(max(1.00001, r)) * r / max(1e-5, dr);
}
float om_d8_dist2(float x0, float x1, float x2, float x3, float x4, float x5, float x6, float x7, float sh, out uint chash) {
 float u0=x0-sh, u1=x1-sh, u2=x2-sh, u3=x3-sh, u4=x4-sh, u5=x5-sh, u6=x6-sh, u7=x7-sh;
 int z0=(int)floor(u0+0.5), z1=(int)floor(u1+0.5), z2=(int)floor(u2+0.5), z3=(int)floor(u3+0.5);
 int z4=(int)floor(u4+0.5), z5=(int)floor(u5+0.5), z6=(int)floor(u6+0.5), z7=(int)floor(u7+0.5);
 float e0=u0-z0, e1=u1-z1, e2=u2-z2, e3=u3-z3, e4=u4-z4, e5=u5-z5, e6=u6-z6, e7=u7-z7;
 if (((z0+z1+z2+z3+z4+z5+z6+z7) & 1) != 0) {
  float a0=abs(e0), a1=abs(e1), a2=abs(e2), a3=abs(e3), a4=abs(e4), a5=abs(e5), a6=abs(e6), a7=abs(e7);
  float mx=a0; int idx=0;
  if (a1>mx){mx=a1;idx=1;} if (a2>mx){mx=a2;idx=2;} if (a3>mx){mx=a3;idx=3;}
  if (a4>mx){mx=a4;idx=4;} if (a5>mx){mx=a5;idx=5;} if (a6>mx){mx=a6;idx=6;} if (a7>mx){idx=7;}
  if (idx==0){z0+=(e0>0.0?1:-1);e0=u0-z0;} else if (idx==1){z1+=(e1>0.0?1:-1);e1=u1-z1;}
  else if (idx==2){z2+=(e2>0.0?1:-1);e2=u2-z2;} else if (idx==3){z3+=(e3>0.0?1:-1);e3=u3-z3;}
  else if (idx==4){z4+=(e4>0.0?1:-1);e4=u4-z4;} else if (idx==5){z5+=(e5>0.0?1:-1);e5=u5-z5;}
  else if (idx==6){z6+=(e6>0.0?1:-1);e6=u6-z6;} else {z7+=(e7>0.0?1:-1);e7=u7-z7;}
 }
 chash = (uint)(((z0*73856093) ^ (z1*19349663) ^ (z2*83492791) ^ (z3*26544357) ^ (z4*49979687)) & 0x7FFFFFFF);
 return e0*e0 + e1*e1 + e2*e2 + e3*e3 + e4*e4 + e5*e5 + e6*e6 + e7*e7;
}
float om_e8fast(float x0, float x1, float x2, float x3, float x4, float x5, float x6, float x7, out bool sp, out uint ch) {
 uint h1, h2; float d1 = om_d8_dist2(x0,x1,x2,x3,x4,x5,x6,x7,0.0,h1), d2 = om_d8_dist2(x0,x1,x2,x3,x4,x5,x6,x7,0.5,h2);
 if (d2 < d1) { sp = true; ch = h2; return sqrt(d2); } sp = false; ch = h1; return sqrt(d1);
}
float om_e8voronoi(float x0, float x1, float x2, float x3, float x4, float x5, float x6, float x7) { bool sp; uint ch; return om_e8fast(x0,x1,x2,x3,x4,x5,x6,x7,sp,ch); }
float om_e8voronoi4(float x0, float x1, float x2, float x3) { bool sp; uint ch; return om_e8fast(x0,x1,x2,x3,0.0,0.0,0.0,0.0,sp,ch); }
float om_e8spinor(float x0, float x1, float x2, float x3, float x4, float x5, float x6, float x7) { bool sp; uint ch; om_e8fast(x0,x1,x2,x3,x4,x5,x6,x7,sp,ch); return sp ? 1.0 : 0.0; }
float om_leech24(float x, float y, float z, float t) {
 bool sA, sB, sC; uint hA, hB, hC; float c120 = -0.5, s120 = 0.8660254;
 float xB = x*c120 - y*s120, yB = x*s120 + y*c120, xC = x*c120 + y*s120, yC = -x*s120 + y*c120;
 float dA = om_e8fast(x, y, z, t*0.35, x+y, x-y, z*0.5, 0.0, sA, hA);
 float dB = om_e8fast(xB, yB, z*c120, -t*0.28, xB-z, yB+z, 0.25, 0.0, sB, hB);
 float dC = om_e8fast(xC, yC, -z*c120, t*0.21, yC-xC, xC+z, -0.25, 0.0, sC, hC);
 float pen = (((sA ? 1 : 0) ^ (sB ? 1 : 0) ^ (sC ? 1 : 0)) != 0) ? 0.14 : 0.0;
 return sqrt((dA*dA + dB*dB + dC*dC) / 3.0) + pen;
}
float om_golay(float u) {
 static const uint gRows[12] = { 0xFFE001u, 0xB8B002u, 0x717004u, 0xE2D008u, 0xC5B010u, 0x8B7020u, 0x16F040u, 0x2DD080u, 0x5B9100u, 0xB71200u, 0x6E3400u, 0xDC5800u };
 uint m = ((uint)abs((int)floor(u * 64.0))) & 4095u, cw = 0u;
 [unroll] for (int k = 0; k < 12; k++) if (((m >> k) & 1u) != 0u) cw ^= gRows[k];
 return (float)countbits(cw & 0xFFFFFFu) / 24.0;
}
float om_palR(float t) { return 0.5 + 0.5 * cos(6.2831853 * (t + 0.00)); }
float om_palG(float t) { return 0.5 + 0.5 * cos(6.2831853 * (t + 0.33)); }
float om_palB(float t) { return 0.5 + 0.5 * cos(6.2831853 * (t + 0.67)); }
";

 internal static List<HT> Lex(string s)
 {
  List<HT> ls = new List<HT>(); int i = 0, n = s.Length, ln = 1;
  while (i < n)
  {
  char c = s[i];
  if (c == '\n') { ln++; i++; continue; }
  if (char.IsWhiteSpace(c)) { i++; continue; }
  if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n') i++; continue; }
  if (c == '/' && i + 1 < n && s[i + 1] == '*') { i += 2; while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') ln++; i++; } i += 2; continue; }
  if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
  {
   int st = i; i++; while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
   if (i < n && (s[i] == 'e' || s[i] == 'E')) { i++; if (i < n && (s[i] == '+' || s[i] == '-')) i++; while (i < n && char.IsDigit(s[i])) i++; }
   double v; if (!double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new Exception("Line " + ln + ": Bad number");
   HT tk = new HT(); tk.K = HK.Num; tk.T = v.ToString("0.0#####", CultureInfo.InvariantCulture); tk.L = ln; ls.Add(tk); continue;
  }
  if (char.IsLetter(c) || c == '_')
  {
   int st = i; i++; while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
   string id = s.Substring(st, i - st);
   if (id == "double" || id == "float" || id == "var" || id == "let") continue;
   HT tk = new HT(); tk.K = HK.Id; tk.T = id; tk.L = ln; ls.Add(tk); continue;
  }
  if (i + 1 < n)
  {
   string w2 = s.Substring(i, 2);
   if (w2 == "<=" || w2 == ">=" || w2 == "==" || w2 == "!=" || w2 == "&&" || w2 == "||" || w2 == "+=" || w2 == "-=" || w2 == "*=")
   { HT tk = new HT(); tk.K = HK.Op; tk.T = w2; tk.L = ln; ls.Add(tk); i += 2; continue; }
  }
  HT sg = new HT(); sg.T = c.ToString(); sg.L = ln;
  if (c == '(') sg.K = HK.LP; else if (c == ')') sg.K = HK.RP; else if (c == ',') sg.K = HK.Com;
  else if (c == ';') sg.K = HK.Semi; else if (c == '?') sg.K = HK.QM; else if (c == ':') sg.K = HK.Col;
  else if ("+-*/%^=<>!".IndexOf(c) >= 0) sg.K = HK.Op; else throw new Exception("Line " + ln + ": Unexpected '" + c + "'");
  ls.Add(sg); i++;
  }
  HT e = new HT(); e.K = HK.End; e.T = ""; e.L = ln; ls.Add(e); return ls;
 }

 internal sealed class HP
 {
  private readonly List<HT> _t; private int _p = 0;
  private readonly HashSet<string> _ro, _loc;
  private readonly bool _isProj;
  public bool HasSdf = false, HasRgb = false;
  public HP(List<HT> t, HashSet<string> ro, HashSet<string> loc, bool isProj) { _t = t; _ro = ro; _loc = loc; _isProj = isProj; }
  private HT C { get { return _t[_p]; } }
  private HT N() { HT c = _t[_p]; if (_p + 1 < _t.Count) _p++; return c; }

  public void ParseAll(List<string> ls)
  {
  while (C.K != HK.End)
  {
   if (C.K == HK.Semi) { N(); continue; }
   if (C.K == HK.Id && _p + 1 < _t.Count && _t[_p + 1].K == HK.Op && (_t[_p + 1].T == "=" || _t[_p + 1].T == "+=" || _t[_p + 1].T == "-=" || _t[_p + 1].T == "*="))
   {
   HT id = N(); string op = N().T;
   if (_ro.Contains(id.T)) throw new Exception("Line " + id.L + ": Read-only '" + id.T + "'");
   string rhs = PTern(); _loc.Add(id.T);
   if (id.T == "sdf") HasSdf = true;
   if (id.T == "r" || id.T == "g" || id.T == "b") HasRgb = true;
   ls.Add("v_" + id.T + " " + op + " " + rhs + ";");
   }
   else if (!_isProj) ls.Add("v_v = " + PTern() + ";");
   else throw new Exception("Line " + C.L + ": Assign projX = ...; projY = ...; projZ = ...;");
   if (C.K == HK.Semi) N();
  }
  }

  private string PTern() { string c = POr(); if (C.K == HK.QM) { N(); string a = PTern(); if (C.K != HK.Col) throw new Exception("Expected ':'"); N(); return "((" + c + " != 0.0) ? (" + a + ") : (" + PTern() + "))"; } return c; }
  private string POr() { string l = PAnd(); while (C.K == HK.Op && C.T == "||") { N(); l = "(((" + l + " != 0.0) || (" + PAnd() + " != 0.0)) ? 1.0 : 0.0)"; } return l; }
  private string PAnd() { string l = PCmp(); while (C.K == HK.Op && C.T == "&&") { N(); l = "(((" + l + " != 0.0) && (" + PCmp() + " != 0.0)) ? 1.0 : 0.0)"; } return l; }
  private string PCmp() { string l = PAdd(); while (C.K == HK.Op && (C.T == "<" || C.T == ">" || C.T == "<=" || C.T == ">=" || C.T == "==" || C.T == "!=")) { string op = N().T; l = "((" + l + " " + op + " " + PAdd() + ") ? 1.0 : 0.0)"; } return l; }
  private string PAdd() { string l = PMul(); while (C.K == HK.Op && (C.T == "+" || C.T == "-")) { string op = N().T; l = "(" + l + " " + op + " " + PMul() + ")"; } return l; }
  private string PMul() { string l = PPow(); while (C.K == HK.Op && (C.T == "*" || C.T == "/" || C.T == "%")) { string op = N().T, r = PPow(); l = (op == "*") ? ("(" + l + " * " + r + ")") : (op == "/" ? ("om_div(" + l + ", " + r + ")") : ("om_mod(" + l + ", " + r + ")")); } return l; }
  private string PPow() { string l = PUn(); if (C.K == HK.Op && C.T == "^") { N(); return "om_pow(" + l + ", " + PPow() + ")"; } return l; }
  private string PUn() { if (C.K == HK.Op && C.T == "-") { N(); return "(-" + PUn() + ")"; } if (C.K == HK.Op && C.T == "+") { N(); return PUn(); } if (C.K == HK.Op && C.T == "!") { N(); return "((" + PUn() + " == 0.0) ? 1.0 : 0.0)"; } return PPri(); }
  private string PPri()
  {
  if (C.K == HK.Num) return N().T;
  if (C.K == HK.LP) { N(); string e = PTern(); if (C.K != HK.RP) throw new Exception("Expected ')'"); N(); return "(" + e + ")"; }
  if (C.K == HK.Id)
  {
   HT id = N();
   if (C.K == HK.LP)
   {
   N(); List<string> a = new List<string>();
   if (C.K != HK.RP) { while (true) { a.Add(PTern()); if (C.K == HK.Com) { N(); continue; } break; } }
   if (C.K != HK.RP) throw new Exception("Expected ')'"); N();
   return MapFn(id.T, a);
   }
   if (_loc.Contains(id.T) || _ro.Contains(id.T)) return "v_" + id.T;
   throw new Exception("Unknown '" + id.T + "'");
  }
  throw new Exception("Unexpected '" + C.T + "'");
  }
  private static string MapFn(string fn, List<string> a)
  {
  string j = string.Join(", ", a.ToArray());
  if (fn == "sin" || fn == "cos" || fn == "tan" || fn == "atan" || fn == "atan2" || fn == "abs" || fn == "floor" || fn == "ceil" || fn == "round" || fn == "sign" || fn == "min" || fn == "max" || fn == "clamp" || fn == "step" || fn == "smoothstep") return fn + "(" + j + ")";
  if (fn == "mix" || fn == "lerp") return "lerp(" + j + ")";
  if (fn == "saturate") return "saturate(" + j + ")";
  if (fn == "smin" && a.Count == 2) return "om_smin2(" + j + ")";
  if (fn == "e8voronoi" && a.Count == 4) return "om_e8voronoi4(" + j + ")";
  return "om_" + fn + "(" + j + ")";
  }
 }
 }
}
