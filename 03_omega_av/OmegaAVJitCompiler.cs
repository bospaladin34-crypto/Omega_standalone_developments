using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace OmegaAVStudio
{
 public delegate void ShaderKernelFn(double x, double y, double z, double w, double rad, double theta, double[] u, double[] r8, double[] res);

 public sealed class CompiledMathProgram
 {
 public ShaderKernelFn Kernel;
 public bool UsesSdf, AssignsRgb, AssignsV;
 public double CompileMs;
 public string Error;
 }

 public sealed class MathShaderPreset
 {
 public string Name, PixelCode, Proj8DCode;
 public MathShaderPreset(string n, string p, string pr) { Name = n; PixelCode = p; Proj8DCode = pr; }
 }

 // Lightweight CPU Ping-Pong Gray-Scott Fluid Solver so CPU x64 Fallback mirrors GPU Reaction-Diffusion
 public sealed class CpuFluidGrid
 {
 public int W = 144, H = 81;
 public float[] U = new float[144 * 81], V = new float[144 * 81], Vx = new float[144 * 81], Vy = new float[144 * 81];
 private float[] _uB = new float[144 * 81], _vB = new float[144 * 81];
 private bool _seeded = false;

 public void Reset()
 {
  float cx = W * 0.5f, cy = H * 0.5f;
  for (int y = 0; y < H; y++)
  {
  for (int x = 0; x < W; x++)
  {
   int idx = y * W + x;
   double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy);
   double ring = Math.Exp(-Math.Abs(r - cy * 0.30) * 0.55) * (0.5 + 0.5 * Math.Cos(12.0 * Math.Atan2(dy, dx)));
   float v0 = (ring > 0.52 || r < 4.5) ? 0.36f : 0.0f;
   U[idx] = 1.0f - v0; V[idx] = v0; Vx[idx] = 0.0f; Vy[idx] = 0.0f;
  }
  }
  _seeded = true;
 }

 public void Splash()
 {
  if (!_seeded) Reset();
  float cx = W * 0.5f, cy = H * 0.5f;
  for (int y = 0; y < H; y++)
  for (int x = 0; x < W; x++)
  {
   double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy);
   if (Math.Abs(r - cy * 0.35) < 2.2) V[y * W + x] = Math.Max(V[y * W + x], 0.35f);
  }
 }

 public void Step(float feedF, float killK)
 {
  if (!_seeded) Reset();
  for (int y = 0; y < H; y++)
  {
  int ym = Math.Max(0, y - 1) * W, yc = y * W, yp = Math.Min(H - 1, y + 1) * W;
  for (int x = 0; x < W; x++)
  {
   int xm = Math.Max(0, x - 1), xp = Math.Min(W - 1, x + 1), idx = yc + x;
   float u = U[idx], v = V[idx];
   float lapU = (U[ym + x] + U[yp + x] + U[yc + xm] + U[yc + xp]) * 0.20f + (U[ym + xm] + U[ym + xp] + U[yp + xm] + U[yp + xp]) * 0.05f - u;
   float lapV = (V[ym + x] + V[yp + x] + V[yc + xm] + V[yc + xp]) * 0.20f + (V[ym + xm] + V[ym + xp] + V[yp + xm] + V[yp + xp]) * 0.05f - v;
   float uvv = u * v * v;
   _uB[idx] = Math.Max(0.0f, Math.Min(1.0f, u + 0.2097f * lapU - uvv + feedF * (1.0f - u)));
   _vB[idx] = Math.Max(0.0f, Math.Min(1.0f, v + 0.1050f * lapV + uvv - (feedF + killK) * v));
   Vx[idx] *= 0.92f; Vy[idx] *= 0.92f;
  }
  }
  float[] tU = U; U = _uB; _uB = tU;
  float[] tV = V; V = _vB; _vB = tV;
 }

 public void SampleNormalized(double uNorm, double vNorm, out double outU, out double outV, out double outVx, out double outVy)
 {
  if (!_seeded) { outU = 1.0; outV = 0.0; outVx = 0.0; outVy = 0.0; return; }
  int ix = Math.Max(0, Math.Min(W - 1, (int)(uNorm * (W - 1))));
  int iy = Math.Max(0, Math.Min(H - 1, (int)(vNorm * (H - 1))));
  int idx = iy * W + ix;
  outU = U[idx]; outV = V[idx]; outVx = Vx[idx]; outVy = Vy[idx];
 }
 }

 public static class ShaderMath
 {
 public static double SafeDiv(double a, double b) { return Math.Abs(b) < 1e-12 ? 0.0 : a / b; }
 public static double sin(double a) { return Math.Sin(a); }
 public static double cos(double a) { return Math.Cos(a); }
 public static double tan(double a) { return Math.Tan(a); }
 public static double asin(double a) { return Math.Asin(clamp(a, -1.0, 1.0)); }
 public static double acos(double a) { return Math.Acos(clamp(a, -1.0, 1.0)); }
 public static double atan(double a) { return Math.Atan(a); }
 public static double atan2(double y, double x) { return Math.Atan2(y, x); }
 public static double sinh(double a) { return Math.Sinh(clamp(a, -20.0, 20.0)); }
 public static double cosh(double a) { return Math.Cosh(clamp(a, -20.0, 20.0)); }
 public static double tanh(double a) { return Math.Tanh(a); }
 public static double exp(double a) { return Math.Exp(clamp(a, -40.0, 40.0)); }
 public static double log(double a) { return Math.Log(Math.Max(1e-12, Math.Abs(a))); }
 public static double sqrt(double a) { return Math.Sqrt(Math.Max(0.0, a)); }
 public static double abs(double a) { return Math.Abs(a); }
 public static double floor(double a) { return Math.Floor(a); }
 public static double ceil(double a) { return Math.Ceiling(a); }
 public static double round(double a) { return Math.Round(a); }
 public static double sign(double a) { return a > 0.0 ? 1.0 : (a < 0.0 ? -1.0 : 0.0); }
 public static double fract(double a) { return a - Math.Floor(a); }
 public static double frac(double a) { return a - Math.Floor(a); }
 public static double sat(double a) { return a < 0.0 ? 0.0 : (a > 1.0 ? 1.0 : a); }
 public static double saturate(double a) { return sat(a); }
 public static double pow(double a, double b) { return Math.Pow(Math.Max(1e-12, Math.Abs(a)), clamp(b, -32.0, 32.0)); }
 public static double min(double a, double b) { return a < b ? a : b; }
 public static double max(double a, double b) { return a > b ? a : b; }
 public static double mod(double a, double b) { return Math.Abs(b) < 1e-9 ? 0.0 : a - b * Math.Floor(a / b); }
 public static double step(double edge, double x) { return x < edge ? 0.0 : 1.0; }
 public static double clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }
 public static double mix(double a, double b, double t) { return a + (b - a) * t; }
 public static double lerp(double a, double b, double t) { return a + (b - a) * t; }
 public static double smoothstep(double e0, double e1, double x) { double t = sat(SafeDiv(x - e0, e1 - e0)); return t * t * (3.0 - 2.0 * t); }
 public static double len2(double x, double y) { return Math.Sqrt(x * x + y * y); }
 public static double len3(double x, double y, double z) { return Math.Sqrt(x * x + y * y + z * z); }
 public static double len4(double x, double y, double z, double w) { return Math.Sqrt(x * x + y * y + z * z + w * w); }
 public static double rot1(double a, double b, double ang) { return a * Math.Cos(ang) - b * Math.Sin(ang); }
 public static double rot2(double a, double b, double ang) { return a * Math.Sin(ang) + b * Math.Cos(ang); }
 public static double rep(double p, double per) { return mod(p + 0.5 * per, per) - 0.5 * per; }
 public static double smin2(double a, double b) { return smin(a, b, 0.2); }
 public static double smin(double a, double b, double k) { double kk = Math.Max(0.0001, k); double h = sat(0.5 + 0.5 * (b - a) / kk); return b + (a - b) * h - kk * h * (1.0 - h); }
 public static double smax(double a, double b, double k) { return -smin(-a, -b, k); }
 public static double sdSphere(double x, double y, double z, double r) { return Math.Sqrt(x * x + y * y + z * z) - r; }
 public static double sdTorus(double x, double y, double z, double R, double r) { double q = Math.Sqrt(x * x + z * z) - R; return Math.Sqrt(q * q + y * y) - r; }
 public static double sdBox(double x, double y, double z, double bx, double by, double bz)
 {
  double dx = Math.Abs(x) - bx, dy = Math.Abs(y) - by, dz = Math.Abs(z) - bz;
  double ox = Math.Max(dx, 0.0), oy = Math.Max(dy, 0.0), oz = Math.Max(dz, 0.0);
  return Math.Sqrt(ox * ox + oy * oy + oz * oz) + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0.0);
 }
 public static double sdOcta(double x, double y, double z, double s) { return (Math.Abs(x) + Math.Abs(y) + Math.Abs(z) - s) * 0.57735027; }
 public static double sdGyroid(double x, double y, double z, double sc) { double s = Math.Max(0.05, Math.Abs(sc)); return (Math.Sin(x * s) * Math.Cos(y * s) + Math.Sin(y * s) * Math.Cos(z * s) + Math.Sin(z * s) * Math.Cos(x * s)) / s; }
 public static double sdLink(double x, double y, double z, double le, double r1, double r2) { double qy = Math.Max(Math.Abs(y) - le, 0.0), qxy = Math.Sqrt(x * x + qy * qy) - r1; return Math.Sqrt(qxy * qxy + z * z) - r2; }
 public static double sdClifford4D(double x, double y, double z, double w, double R, double r) { double d1 = Math.Sqrt(x * x + y * y) - R, d2 = Math.Sqrt(z * z + w * w) - R; return Math.Sqrt(d1 * d1 + d2 * d2) - r; }
 public static double mandelbulb(double x, double y, double z, double pwr)
 {
  double zx = x, zy = y, zz = z, dr = 1.0, r = 0.0, pw = Math.Max(2.0, pwr);
  for (int i = 0; i < 6; i++)
  {
  r = Math.Sqrt(zx * zx + zy * zy + zz * zz); if (r > 2.5) break;
  double th = Math.Acos(clamp(SafeDiv(zz, r), -1.0, 1.0)) * pw, ph = Math.Atan2(zy, zx) * pw;
  dr = Math.Pow(Math.Max(1e-6, r), pw - 1.0) * pw * dr + 1.0;
  double zr = Math.Pow(Math.Max(1e-6, r), pw);
  zx = zr * Math.Sin(th) * Math.Cos(ph) + x; zy = zr * Math.Sin(th) * Math.Sin(ph) + y; zz = zr * Math.Cos(th) + z;
  }
  return 0.5 * Math.Log(Math.Max(1.00001, r)) * r / Math.Max(1e-6, dr);
 }
 public static double e8voronoi4(double x0, double x1, double x2, double x3) { bool sp; int h; return E8LatticeMath.FastVoronoiCellDist(x0, x1, x2, x3, 0.0, 0.0, 0.0, 0.0, out sp, out h); }
 public static double e8voronoi(double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7) { bool sp; int h; return E8LatticeMath.FastVoronoiCellDist(x0, x1, x2, x3, x4, x5, x6, x7, out sp, out h); }
 public static double e8spinor(double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7) { bool sp; int h; E8LatticeMath.FastVoronoiCellDist(x0, x1, x2, x3, x4, x5, x6, x7, out sp, out h); return sp ? 1.0 : 0.0; }
 public static double leech24(double x, double y, double z, double t) { return GolayLeech24Math.Leech24VoronoiDist(x, y, z, t); }
 public static double golay(double u) { return GolayLeech24Math.NearestGolayNormWeight(u); }
 public static double palR(double t) { return 0.5 + 0.5 * Math.Cos(6.2831853 * (t + 0.00)); }
 public static double palG(double t) { return 0.5 + 0.5 * Math.Cos(6.2831853 * (t + 0.33)); }
 public static double palB(double t) { return 0.5 + 0.5 * Math.Cos(6.2831853 * (t + 0.67)); }
 public static void StoreOutputs(double[] res, double a, double b, double c, double d) { res[0] = a; res[1] = b; res[2] = c; res[3] = d; }
 }

 public static class OmegaShaderJit
 {
 private enum TokKind { End, Num, Ident, Op, LParen, RParen, Comma, Semi, QMark, Colon }
 private struct Tok { public TokKind Kind; public string Text; public double Val; public int Line; }

 public static CompiledMathProgram Compile(string src, bool isProj8D)
 {
  CompiledMathProgram res = new CompiledMathProgram();
  System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
  try
  {
  List<Tok> toks = Lex(src ?? "");
  ParameterExpression pX = Expression.Parameter(typeof(double), "x"), pY = Expression.Parameter(typeof(double), "y");
  ParameterExpression pZ = Expression.Parameter(typeof(double), "z"), pW = Expression.Parameter(typeof(double), "w");
  ParameterExpression pRad = Expression.Parameter(typeof(double), "rad"), pTh = Expression.Parameter(typeof(double), "theta");
  ParameterExpression pU = Expression.Parameter(typeof(double[]), "u"), pR8 = Expression.Parameter(typeof(double[]), "r8"), pOut = Expression.Parameter(typeof(double[]), "res");

  Dictionary<string, Expression> roVars = new Dictionary<string, Expression>(StringComparer.Ordinal);
  roVars["pi"] = Expression.Constant(Math.PI); roVars["tau"] = Expression.Constant(Math.PI * 2.0);
  roVars["phi"] = Expression.Constant(1.618033988749895); roVars["e"] = Expression.Constant(Math.E); roVars["sqrt2"] = Expression.Constant(1.4142135623730951);

  // u[0..41] uniform table shared across CPU JIT
  string[] uNames = new string[] {
   "t", "bass", "mid", "high", "yaw", "pitch", "zoom", "k1", "k2", "k3", "k4",
   "b0", "b1", "b2", "b3", "b4", "b5", "b6", "b7",
   "q0", "q1", "q2", "q3", "q4", "q5", "q6", "q7",
   "mx", "my", "mclick",
   "m1", "m2", "m3", "m4",
   "padLx", "padLy", "padRx", "padRy", "padLt", "padRt", "note", "vel"
  };
  for (int i = 0; i < uNames.Length; i++) roVars[uNames[i]] = Expression.ArrayIndex(pU, Expression.Constant(i));
  for (int i = 0; i < 8; i++) { roVars["x" + i] = Expression.ArrayIndex(pR8, Expression.Constant(i)); roVars["r" + i] = Expression.ArrayIndex(pR8, Expression.Constant(8 + i)); }
  roVars["t1"] = Expression.ArrayIndex(pR8, Expression.Constant(16)); roVars["t2"] = Expression.ArrayIndex(pR8, Expression.Constant(17)); roVars["t3"] = Expression.ArrayIndex(pR8, Expression.Constant(18));
  roVars["rdU"] = Expression.ArrayIndex(pR8, Expression.Constant(19)); roVars["rdV"] = Expression.ArrayIndex(pR8, Expression.Constant(20));
  roVars["velX"] = Expression.ArrayIndex(pR8, Expression.Constant(21)); roVars["velY"] = Expression.ArrayIndex(pR8, Expression.Constant(22));

  if (isProj8D) { roVars["c1"] = pX; roVars["c2"] = pY; roVars["c3"] = pZ; roVars["c4"] = pW; }
  else { roVars["x"] = pX; roVars["y"] = pY; roVars["z"] = pZ; roVars["w"] = pW; roVars["px"] = pX; roVars["py"] = pY; roVars["pz"] = pZ; roVars["pw"] = pW; roVars["rad"] = pRad; roVars["theta"] = pTh; }

  Dictionary<string, ParameterExpression> locals = new Dictionary<string, ParameterExpression>(StringComparer.Ordinal);
  List<Expression> stmts = new List<Expression>();
  if (isProj8D)
  {
   stmts.Add(Expression.Assign(MakeLocal(locals, "projX"), pX));
   stmts.Add(Expression.Assign(MakeLocal(locals, "projY"), pY));
   stmts.Add(Expression.Assign(MakeLocal(locals, "projZ"), pZ));
  }
  else
  {
   stmts.Add(Expression.Assign(MakeLocal(locals, "r"), Expression.Constant(0.0)));
   stmts.Add(Expression.Assign(MakeLocal(locals, "g"), Expression.Constant(0.0)));
   stmts.Add(Expression.Assign(MakeLocal(locals, "b"), Expression.Constant(0.0)));
   stmts.Add(Expression.Assign(MakeLocal(locals, "v"), Expression.Constant(0.0)));
   stmts.Add(Expression.Assign(MakeLocal(locals, "sdf"), Expression.Constant(999.0)));
  }

  Parser p = new Parser(toks, roVars, locals);
  p.ParseAll(stmts, isProj8D);
  res.UsesSdf = p.AssignedSdf; res.AssignsRgb = p.AssignedRgb; res.AssignsV = p.AssignedV;

  MethodInfo storeMi = typeof(ShaderMath).GetMethod("StoreOutputs", BindingFlags.Public | BindingFlags.Static);
  if (isProj8D) stmts.Add(Expression.Call(storeMi, pOut, locals["projX"], locals["projY"], locals["projZ"], Expression.Constant(1.0)));
  else if (p.AssignedRgb) stmts.Add(Expression.Call(storeMi, pOut, locals["r"], locals["g"], locals["b"], locals["sdf"]));
  else stmts.Add(Expression.Call(storeMi, pOut, locals["v"], locals["g"], locals["b"], locals["sdf"]));

  BlockExpression body = Expression.Block(new List<ParameterExpression>(locals.Values), stmts);
  res.Kernel = Expression.Lambda<ShaderKernelFn>(body, pX, pY, pZ, pW, pRad, pTh, pU, pR8, pOut).Compile();
  sw.Stop(); res.CompileMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
  }
  catch (Exception ex) { res.Error = ex.Message; }
  return res;
 }

 private static ParameterExpression MakeLocal(Dictionary<string, ParameterExpression> d, string n) { ParameterExpression p = Expression.Parameter(typeof(double), n); d[n] = p; return p; }

 private static List<Tok> Lex(string s)
 {
  List<Tok> list = new List<Tok>(); int i = 0, n = s.Length, line = 1;
  while (i < n)
  {
  char c = s[i];
  if (c == '\n') { line++; i++; continue; }
  if (char.IsWhiteSpace(c)) { i++; continue; }
  if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n') i++; continue; }
  if (c == '/' && i + 1 < n && s[i + 1] == '*') { i += 2; while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') line++; i++; } i += 2; continue; }
  if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
  {
   int st = i; i++; while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
   if (i < n && (s[i] == 'e' || s[i] == 'E')) { i++; if (i < n && (s[i] == '+' || s[i] == '-')) i++; while (i < n && char.IsDigit(s[i])) i++; }
   string sub = s.Substring(st, i - st); double val;
   if (!double.TryParse(sub, NumberStyles.Float, CultureInfo.InvariantCulture, out val)) throw new Exception("Line " + line + ": Invalid number '" + sub + "'");
   Tok tk = new Tok(); tk.Kind = TokKind.Num; tk.Text = sub; tk.Val = val; tk.Line = line; list.Add(tk); continue;
  }
  if (char.IsLetter(c) || c == '_')
  {
   int st = i; i++; while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
   string id = s.Substring(st, i - st);
   if (id == "double" || id == "float" || id == "var" || id == "let") continue;
   Tok tk = new Tok(); tk.Kind = TokKind.Ident; tk.Text = id; tk.Line = line; list.Add(tk); continue;
  }
  if (i + 1 < n)
  {
   string two = s.Substring(i, 2);
   if (two == "<=" || two == ">=" || two == "==" || two == "!=" || two == "&&" || two == "||" || two == "+=" || two == "-=" || two == "*=")
   { Tok tk = new Tok(); tk.Kind = TokKind.Op; tk.Text = two; tk.Line = line; list.Add(tk); i += 2; continue; }
  }
  Tok sg = new Tok(); sg.Text = c.ToString(); sg.Line = line;
  if (c == '(') sg.Kind = TokKind.LParen; else if (c == ')') sg.Kind = TokKind.RParen; else if (c == ',') sg.Kind = TokKind.Comma;
  else if (c == ';') sg.Kind = TokKind.Semi; else if (c == '?') sg.Kind = TokKind.QMark; else if (c == ':') sg.Kind = TokKind.Colon;
  else if ("+-*/%^=<>!".IndexOf(c) >= 0) sg.Kind = TokKind.Op; else throw new Exception("Line " + line + ": Unexpected '" + c + "'");
  list.Add(sg); i++;
  }
  Tok end = new Tok(); end.Kind = TokKind.End; end.Text = "EOF"; end.Line = line; list.Add(end); return list;
 }

 private sealed class Parser
 {
  private readonly List<Tok> _t; private int _pos = 0;
  private readonly Dictionary<string, Expression> _ro;
  private readonly Dictionary<string, ParameterExpression> _loc;
  public bool AssignedSdf = false, AssignedRgb = false, AssignedV = false;
  public Parser(List<Tok> t, Dictionary<string, Expression> ro, Dictionary<string, ParameterExpression> loc) { _t = t; _ro = ro; _loc = loc; }
  private Tok Cur { get { return _t[_pos]; } }
  private Tok Next() { Tok c = _t[_pos]; if (_pos + 1 < _t.Count) _pos++; return c; }

  public void ParseAll(List<Expression> stmts, bool isProj8D)
  {
  while (Cur.Kind != TokKind.End)
  {
   if (Cur.Kind == TokKind.Semi) { Next(); continue; }
   if (Cur.Kind == TokKind.Ident && _pos + 1 < _t.Count && _t[_pos + 1].Kind == TokKind.Op &&
   (_t[_pos + 1].Text == "=" || _t[_pos + 1].Text == "+=" || _t[_pos + 1].Text == "-=" || _t[_pos + 1].Text == "*="))
   {
   Tok idTok = Next(); string op = Next().Text;
   if (_ro.ContainsKey(idTok.Text)) throw new Exception("Line " + idTok.Line + ": '" + idTok.Text + "' is read-only");
   Expression rhs = ParseTernary(); ParameterExpression target;
   if (!_loc.TryGetValue(idTok.Text, out target))
   {
    target = Expression.Parameter(typeof(double), idTok.Text); _loc[idTok.Text] = target;
    stmts.Insert(0, Expression.Assign(target, Expression.Constant(0.0)));
   }
   if (idTok.Text == "sdf") AssignedSdf = true;
   if (idTok.Text == "r" || idTok.Text == "g" || idTok.Text == "b") AssignedRgb = true;
   if (idTok.Text == "v") AssignedV = true;
   if (op == "+=") rhs = Expression.Add(target, rhs);
   else if (op == "-=") rhs = Expression.Subtract(target, rhs);
   else if (op == "*=") rhs = Expression.Multiply(target, rhs);
   stmts.Add(Expression.Assign(target, rhs));
   }
   else if (!isProj8D) { AssignedV = true; stmts.Add(Expression.Assign(_loc["v"], ParseTernary())); }
   else throw new Exception("Line " + Cur.Line + ": Assign projX = ...; projY = ...; projZ = ...;");
   if (Cur.Kind == TokKind.Semi) Next();
  }
  }

  private Expression ParseTernary() { Expression c = ParseOr(); if (Cur.Kind == TokKind.QMark) { Next(); Expression tB = ParseTernary(); if (Cur.Kind != TokKind.Colon) throw new Exception("Line " + Cur.Line + ": Expected ':'"); Next(); return Expression.Condition(Expression.NotEqual(c, Expression.Constant(0.0)), tB, ParseTernary()); } return c; }
  private Expression ParseOr() { Expression l = ParseAnd(); while (Cur.Kind == TokKind.Op && Cur.Text == "||") { Next(); Expression r = ParseAnd(); l = Expression.Condition(Expression.OrElse(Expression.NotEqual(l, Expression.Constant(0.0)), Expression.NotEqual(r, Expression.Constant(0.0))), Expression.Constant(1.0), Expression.Constant(0.0)); } return l; }
  private Expression ParseAnd() { Expression l = ParseCmp(); while (Cur.Kind == TokKind.Op && Cur.Text == "&&") { Next(); Expression r = ParseCmp(); l = Expression.Condition(Expression.AndAlso(Expression.NotEqual(l, Expression.Constant(0.0)), Expression.NotEqual(r, Expression.Constant(0.0))), Expression.Constant(1.0), Expression.Constant(0.0)); } return l; }
  private Expression ParseCmp()
  {
  Expression l = ParseAdd();
  while (Cur.Kind == TokKind.Op && (Cur.Text == "<" || Cur.Text == ">" || Cur.Text == "<=" || Cur.Text == ">=" || Cur.Text == "==" || Cur.Text == "!="))
  {
   string op = Next().Text; Expression r = ParseAdd(), cmp;
   if (op == "<") cmp = Expression.LessThan(l, r); else if (op == ">") cmp = Expression.GreaterThan(l, r);
   else if (op == "<=") cmp = Expression.LessThanOrEqual(l, r); else if (op == ">=") cmp = Expression.GreaterThanOrEqual(l, r);
   else if (op == "==") cmp = Expression.Equal(l, r); else cmp = Expression.NotEqual(l, r);
   l = Expression.Condition(cmp, Expression.Constant(1.0), Expression.Constant(0.0));
  }
  return l;
  }
  private Expression ParseAdd() { Expression l = ParseMul(); while (Cur.Kind == TokKind.Op && (Cur.Text == "+" || Cur.Text == "-")) { string op = Next().Text; Expression r = ParseMul(); l = (op == "+") ? Expression.Add(l, r) : Expression.Subtract(l, r); } return l; }
  private Expression ParseMul() { Expression l = ParsePow(); while (Cur.Kind == TokKind.Op && (Cur.Text == "*" || Cur.Text == "/" || Cur.Text == "%")) { string op = Next().Text; Expression r = ParsePow(); if (op == "*") l = Expression.Multiply(l, r); else if (op == "/") l = Expression.Call(typeof(ShaderMath).GetMethod("SafeDiv"), l, r); else l = Expression.Call(typeof(ShaderMath).GetMethod("mod"), l, r); } return l; }
  private Expression ParsePow() { Expression l = ParseUnary(); if (Cur.Kind == TokKind.Op && Cur.Text == "^") { Next(); return Expression.Call(typeof(ShaderMath).GetMethod("pow"), l, ParsePow()); } return l; }
  private Expression ParseUnary()
  {
  if (Cur.Kind == TokKind.Op && Cur.Text == "-") { Next(); return Expression.Negate(ParseUnary()); }
  if (Cur.Kind == TokKind.Op && Cur.Text == "+") { Next(); return ParseUnary(); }
  if (Cur.Kind == TokKind.Op && Cur.Text == "!") { Next(); return Expression.Condition(Expression.Equal(ParseUnary(), Expression.Constant(0.0)), Expression.Constant(1.0), Expression.Constant(0.0)); }
  return ParsePrimary();
  }
  private Expression ParsePrimary()
  {
  if (Cur.Kind == TokKind.Num) return Expression.Constant(Next().Val);
  if (Cur.Kind == TokKind.LParen) { Next(); Expression e = ParseTernary(); if (Cur.Kind != TokKind.RParen) throw new Exception("Line " + Cur.Line + ": Expected ')'"); Next(); return e; }
  if (Cur.Kind == TokKind.Ident)
  {
   Tok id = Next();
   if (Cur.Kind == TokKind.LParen)
   {
   Next(); List<Expression> args = new List<Expression>();
   if (Cur.Kind != TokKind.RParen) { while (true) { args.Add(ParseTernary()); if (Cur.Kind == TokKind.Comma) { Next(); continue; } break; } }
   if (Cur.Kind != TokKind.RParen) throw new Exception("Line " + Cur.Line + ": Expected ')'"); Next();
   return ResolveCall(id.Text, args, id.Line);
   }
   ParameterExpression locVar; if (_loc.TryGetValue(id.Text, out locVar)) return locVar;
   Expression roVar; if (_ro.TryGetValue(id.Text, out roVar)) return roVar;
   throw new Exception("Line " + id.Line + ": Unknown variable '" + id.Text + "'");
  }
  throw new Exception("Line " + Cur.Line + ": Unexpected '" + Cur.Text + "'");
  }
  private static Expression ResolveCall(string fn, List<Expression> args, int line)
  {
  string target = fn;
  if (fn == "smin" && args.Count == 2) target = "smin2";
  if (fn == "e8voronoi" && args.Count == 4) target = "e8voronoi4";
  Type[] sig = new Type[args.Count]; for (int i = 0; i < args.Count; i++) sig[i] = typeof(double);
  MethodInfo mi = typeof(ShaderMath).GetMethod(target, BindingFlags.Public | BindingFlags.Static, null, sig, null);
  if (mi == null) throw new Exception("Line " + line + ": Unknown function '" + fn + "' with " + args.Count + " arg(s)");
  return Expression.Call(mi, args.ToArray());
  }
 }
 }
}
