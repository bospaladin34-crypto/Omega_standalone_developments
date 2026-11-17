using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaAVStudio
{
 // Zero-NuGet Full-VRAM DirectCompute (cs_5_0) Ping-Pong Reaction-Diffusion Fluid + 24D Leech + Wu AA + DXGI SwapChain
 public sealed partial class DirectComputeHlslEngine : IDisposable
 {
 [StructLayout(LayoutKind.Sequential)] private struct BUF_DESC { public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride; }
 [StructLayout(LayoutKind.Sequential)] private struct SUB_DATA { public IntPtr pSysMem; public uint SysMemPitch, SysMemSlicePitch; }
 [StructLayout(LayoutKind.Sequential)] private struct TEX2D_DESC { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags; }
 [StructLayout(LayoutKind.Sequential)] private struct UAV_DESC { public uint Format, ViewDimension, FirstElement, NumElements, Flags; }
 [StructLayout(LayoutKind.Sequential)] private struct SRV_DESC { public uint Format, ViewDimension, FirstElement, NumElements, Pad0, Pad1; }
 [StructLayout(LayoutKind.Sequential)] private struct MAP_SUB { public IntPtr pData; public uint RowPitch, DepthPitch; }
 [StructLayout(LayoutKind.Sequential)] private struct SWAP_DESC
 {
  public uint Width, Height, RefreshNum, RefreshDen, Format, ScanlineOrdering, Scaling;
  public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
  public IntPtr OutputWindow;
  public int Windowed;
  public uint SwapEffect, Flags;
 }

 [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr sw, uint flags, uint[] pFL, uint nFL, uint sdkVer, out IntPtr ppDev, out uint pChosenFL, out IntPtr ppCtx);

 [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
 private static extern int D3D11CreateDeviceAndSwapChain(IntPtr pAdapter, int driverType, IntPtr sw, uint flags, uint[] pFL, uint nFL, uint sdkVer, ref SWAP_DESC pDesc, out IntPtr ppSwap, out IntPtr ppDev, out uint pChosenFL, out IntPtr ppCtx);

 [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
 private static extern int D3DCompile(byte[] pSrc, IntPtr srcLen, string srcName, IntPtr pDefs, IntPtr pInc, string entry, string target, uint f1, uint f2, out IntPtr ppCode, out IntPtr ppErr);

 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint RelDel(IntPtr s);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobDel(IntPtr s);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBufDel(IntPtr s, ref BUF_DESC d, IntPtr init, out IntPtr ppB);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTex2DDel(IntPtr s, ref TEX2D_DESC d, IntPtr init, out IntPtr ppT);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvDel(IntPtr s, IntPtr res, ref SRV_DESC d, out IntPtr ppS);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvNullDel(IntPtr s, IntPtr res, IntPtr pNullDesc, out IntPtr ppS);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavDel(IntPtr s, IntPtr res, ref UAV_DESC d, out IntPtr ppU);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavNullDel(IntPtr s, IntPtr res, IntPtr pNullDesc, out IntPtr ppU);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCsDel(IntPtr s, IntPtr bc, IntPtr len, IntPtr lnk, out IntPtr ppCs);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapDel(IntPtr s, IntPtr res, uint sub, uint typ, uint flg, out MAP_SUB m);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapDel(IntPtr s, IntPtr res, uint sub);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DispatchDel(IntPtr s, uint gx, uint gy, uint gz);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyDel(IntPtr s, IntPtr dst, IntPtr src);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UpdSubDel(IntPtr s, IntPtr dst, uint sub, IntPtr box, IntPtr data, uint rp, uint dp);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetSrvDel(IntPtr s, uint slot, uint n, IntPtr[] srvs);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetUavDel(IntPtr s, uint slot, uint n, IntPtr[] uavs, IntPtr cnt);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetCsDel(IntPtr s, IntPtr cs, IntPtr cls, uint n);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetCbDel(IntPtr s, uint slot, uint n, ref IntPtr cb);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PresentDel(IntPtr s, uint sync, uint flags);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSwapBufDel(IntPtr s, uint buf, ref Guid riid, out IntPtr ppSurf);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResizeSwapDel(IntPtr s, uint cnt, uint w, uint h, uint fmt, uint flags);

 private static Guid IID_Tex2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

 private readonly object _lk = new object();
 private IntPtr _dev = IntPtr.Zero, _ctx = IntPtr.Zero, _swap = IntPtr.Zero, _backBuf = IntPtr.Zero, _cb = IntPtr.Zero;
 private IntPtr _e8Buf = IntPtr.Zero, _e8Srv = IntPtr.Zero, _leechBuf = IntPtr.Zero, _leechSrv = IntPtr.Zero;
 private IntPtr _metaBuf = IntPtr.Zero, _metaSrv = IntPtr.Zero, _edgesBuf = IntPtr.Zero, _edgesSrv = IntPtr.Zero;
 private IntPtr _projBuf = IntPtr.Zero, _projUav = IntPtr.Zero, _projSrv = IntPtr.Zero;
 private IntPtr _velBuf = IntPtr.Zero, _velUav = IntPtr.Zero, _velSrv = IntPtr.Zero;
 private IntPtr _waveBuf = IntPtr.Zero, _waveSrv = IntPtr.Zero;
 private IntPtr _injBuf = IntPtr.Zero, _injUav = IntPtr.Zero;
 private IntPtr _rdBufA = IntPtr.Zero, _rdUavA = IntPtr.Zero, _rdSrvA = IntPtr.Zero;
 private IntPtr _rdBufB = IntPtr.Zero, _rdUavB = IntPtr.Zero, _rdSrvB = IntPtr.Zero;
 private IntPtr _outTex = IntPtr.Zero, _outTexUav = IntPtr.Zero;
 private IntPtr _histBuf = IntPtr.Zero, _histUav = IntPtr.Zero, _histSrv = IntPtr.Zero;
 private IntPtr _hdrBuf = IntPtr.Zero, _hdrUav = IntPtr.Zero, _hdrSrv = IntPtr.Zero;
 private IntPtr _rbBuf = IntPtr.Zero, _rbUav = IntPtr.Zero, _stg = IntPtr.Zero;

 private IntPtr _csProj = IntPtr.Zero, _csSplat = IntPtr.Zero, _csFluid = IntPtr.Zero;
 private IntPtr _csField = IntPtr.Zero, _csLines = IntPtr.Zero, _csComp = IntPtr.Zero;

 private int _w = 0, _h = 0, _fw = 0, _fh = 0, _swapW = 0, _swapH = 0, _totalEdges = 0, _e8Edges = 0, _leechEdges = 0;
 private bool _resetHist = true;
 private float _pendingSplash = 0.0f;
 private readonly float[] _cbData = new float[80]; // 20 x float4 = 320 bytes
 private readonly float[] _waveGpu = new float[512];

 private CreateBufDel _fBuf; private CreateTex2DDel _fTex2D; private CreateSrvDel _fSrv; private CreateSrvNullDel _fSrvNull; private CreateUavDel _fUav; private CreateUavNullDel _fUavNull; private CreateCsDel _fCs;
 private MapDel _fMap; private UnmapDel _fUnmap; private DispatchDel _fDisp;
 private CopyDel _fCopy; private UpdSubDel _fUpd; private SetSrvDel _fSetSrv; private SetUavDel _fSetUav; private SetCsDel _fSetCs; private SetCbDel _fSetCb;
 private PresentDel _fPresent; private GetSwapBufDel _fGetBuf; private ResizeSwapDel _fResize;

 public bool IsAvailable { get; private set; }
 public bool HasSwapChain { get; private set; }
 public double LastCompileMs { get; private set; }
 public string LastError { get; private set; }

 public void TriggerFluidSplash() { _pendingSplash = 1.0f; }
 public void ResetFluidField() { _resetHist = true; }

 public bool Initialize(IntPtr stageHwnd, int initW, int initH)
 {
  lock (_lk)
  {
  try
  {
   uint[] fls = new uint[] { 0xb000u, 0xa100u, 0xa000u }; uint fl = 0u;
   if (stageHwnd != IntPtr.Zero)
   {
   SWAP_DESC sd = new SWAP_DESC();
   sd.Width = (uint)Math.Max(64, initW); sd.Height = (uint)Math.Max(64, initH); sd.RefreshNum = 0u; sd.RefreshDen = 1u;
   sd.Format = 28u; sd.SampleCount = 1u; sd.BufferUsage = 0x20u; sd.BufferCount = 2u;
   sd.OutputWindow = stageHwnd; sd.Windowed = 1; sd.SwapEffect = 0u; sd.Flags = 0u;
   int hrSw = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, (uint)fls.Length, 7u, ref sd, out _swap, out _dev, out fl, out _ctx);
   if (hrSw >= 0 && _swap != IntPtr.Zero && _dev != IntPtr.Zero)
   {
    _fPresent = Vtbl<PresentDel>(_swap, 8); _fGetBuf = Vtbl<GetSwapBufDel>(_swap, 9); _fResize = Vtbl<ResizeSwapDel>(_swap, 13);
    if (_fGetBuf(_swap, 0u, ref IID_Tex2D, out _backBuf) >= 0 && _backBuf != IntPtr.Zero)
    {
    HasSwapChain = true; _swapW = initW; _swapH = initH;
    }
   }
   }
   if (_dev == IntPtr.Zero)
   {
   int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, 1u, 7u, out _dev, out fl, out _ctx);
   if (hr < 0 || _dev == IntPtr.Zero) hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0u, fls, 1u, 7u, out _dev, out fl, out _ctx);
   if (hr < 0 || _dev == IntPtr.Zero || _ctx == IntPtr.Zero) { IsAvailable = false; return false; }
   }

   _fBuf = Vtbl<CreateBufDel>(_dev, 3); _fTex2D = Vtbl<CreateTex2DDel>(_dev, 5);
   _fSrv = Vtbl<CreateSrvDel>(_dev, 7); _fSrvNull = Vtbl<CreateSrvNullDel>(_dev, 7); _fUav = Vtbl<CreateUavDel>(_dev, 8); _fUavNull = Vtbl<CreateUavNullDel>(_dev, 8); _fCs = Vtbl<CreateCsDel>(_dev, 18);
   _fMap = Vtbl<MapDel>(_ctx, 14); _fUnmap = Vtbl<UnmapDel>(_ctx, 15); _fDisp = Vtbl<DispatchDel>(_ctx, 41);
   _fCopy = Vtbl<CopyDel>(_ctx, 47); _fUpd = Vtbl<UpdSubDel>(_ctx, 48);
   _fSetSrv = Vtbl<SetSrvDel>(_ctx, 67); _fSetUav = Vtbl<SetUavDel>(_ctx, 68);
   _fSetCs = Vtbl<SetCsDel>(_ctx, 69); _fSetCb = Vtbl<SetCbDel>(_ctx, 71);

   BUF_DESC cbd = new BUF_DESC(); cbd.ByteWidth = 320u; cbd.BindFlags = 4u;
   if (_fBuf(_dev, ref cbd, IntPtr.Zero, out _cb) < 0 || _cb == IntPtr.Zero) { IsAvailable = false; return false; }

   if (!UploadStaticGeometryBuffers()) { IsAvailable = false; return false; }
   if (!CompileStaticPasses()) { IsAvailable = false; return false; }

   if (!EnsureBuffers(initW, initH, HasSwapChain)) { IsAvailable = false; return false; }
   IsAvailable = true;
   return true;
  }
  catch (Exception ex) { LastError = ex.Message; IsAvailable = false; return false; }
  }
 }

 private bool CompileStaticPasses()
 {
  string err;
  IntPtr csSp = CompileEntry(Encoding.UTF8.GetBytes(OmegaHlslTranspiler.BuildFluidSplatPassHlsl(_e8Edges, _totalEdges)), "CSFluidSplat", out err);
  if (csSp == IntPtr.Zero) { LastError = "CSSplat: " + err; return false; }

  IntPtr csFl = CompileEntry(Encoding.UTF8.GetBytes(OmegaHlslTranspiler.BuildFluidStepPassHlsl()), "CSFluidStep", out err);
  if (csFl == IntPtr.Zero) { Rel(csSp); LastError = "CSFluid: " + err; return false; }

  IntPtr csLn = CompileEntry(Encoding.UTF8.GetBytes(OmegaHlslTranspiler.BuildLinesPassHlsl(_e8Edges, _totalEdges)), "CSLinesAndHud", out err);
  if (csLn == IntPtr.Zero) { Rel(csSp); Rel(csFl); LastError = "CSLines: " + err; return false; }

  IntPtr csCp = CompileEntry(Encoding.UTF8.GetBytes(OmegaHlslTranspiler.BuildCompositePassHlsl()), "CSComposite", out err);
  if (csCp == IntPtr.Zero) { Rel(csSp); Rel(csFl); Rel(csLn); LastError = "CSComposite: " + err; return false; }

  _csSplat = csSp; _csFluid = csFl; _csLines = csLn; _csComp = csCp;
  return true;
 }

 public bool CompileCustomShaders(string pixelExpr, string projExpr)
 {
  if (!IsAvailable) return false;
  bool sdf, rgb; string pErr1, pErr2;
  string projHlsl = OmegaHlslTranspiler.BuildProjPassHlsl(projExpr, out pErr1);
  if (pErr1 != null) { LastError = pErr1; return false; }
  string fieldHlsl = OmegaHlslTranspiler.BuildFieldPassHlsl(pixelExpr, out sdf, out rgb, out pErr2);
  if (pErr2 != null) { LastError = pErr2; return false; }

  Stopwatch sw = Stopwatch.StartNew();
  string err1, err2;
  IntPtr nProj = CompileEntry(Encoding.UTF8.GetBytes(projHlsl), "CSProject", out err1);
  if (nProj == IntPtr.Zero) { LastError = "Proj HLSL: " + err1; return false; }
  IntPtr nField = CompileEntry(Encoding.UTF8.GetBytes(fieldHlsl), "CSField", out err2);
  if (nField == IntPtr.Zero) { Rel(nProj); LastError = "Field HLSL: " + err2; return false; }

  lock (_lk)
  {
  IntPtr oP = _csProj, oF = _csField;
  _csProj = nProj; _csField = nField;
  Rel(oP); Rel(oF);
  }
  sw.Stop();
  LastCompileMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
  LastError = null;
  return true;
 }

 private IntPtr CompileEntry(byte[] src, string entry, out string err)
 {
  err = null;
  IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;
  try
  {
  int hr = D3DCompile(src, (IntPtr)src.Length, "OmegaV06.hlsl", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 0u, 0u, out pCode, out pErr);
  if (hr < 0 || pCode == IntPtr.Zero)
  {
   err = (pErr != IntPtr.Zero) ? Marshal.PtrToStringAnsi(Vtbl<BlobDel>(pErr, 3)(pErr), (int)Vtbl<BlobDel>(pErr, 4)(pErr)).Trim() : ("FXC 0x" + hr.ToString("X8"));
   return IntPtr.Zero;
  }
  IntPtr bc = Vtbl<BlobDel>(pCode, 3)(pCode), len = Vtbl<BlobDel>(pCode, 4)(pCode), cs;
  if (_fCs(_dev, bc, len, IntPtr.Zero, out cs) < 0 || cs == IntPtr.Zero) { err = "CreateComputeShader failed"; return IntPtr.Zero; }
  return cs;
  }
  finally { Rel(pCode); Rel(pErr); }
 }

 public static void GetGrayScottParams(int fluidMode, double bass, double high, double m1, double m2, out float feedF, out float killK)
 {
  // Pearson (1993) Science 261:189 Gray-Scott regimes + live MIDI/Gamepad m1/m2 morphing
  double baseF = 0.0545, baseK = 0.0620;
  if (fluidMode == 2) { baseF = 0.0367; baseK = 0.0649; }      // Soliton Dividing Spots
  else if (fluidMode == 3) { baseF = 0.0290; baseK = 0.0570; } // Labyrinthine Turing Maze
  else if (fluidMode == 4) { baseF = 0.0140; baseK = 0.0450; } // Excitable Spiral Waves
  else if (fluidMode == 5)                                     // Cymatic Audio Phase Morph
  {
  baseF = 0.0280 + 0.0240 * Math.Max(0.0, Math.Min(1.0, bass * 0.48));
  baseK = 0.0565 + 0.0065 * Math.Max(0.0, Math.Min(1.0, high * 0.45));
  }
  feedF = (float)Math.Max(0.010, Math.Min(0.085, baseF + (m1 - 0.5) * 0.018));
  killK = (float)Math.Max(0.038, Math.Min(0.072, baseK + (m2 - 0.5) * 0.010));
 }

 public bool RenderFullGpuFrame(
  int w, int h, bool presentSwap, uint vsyncInterval, int[] optionalReadbackFb,
  int shaderMode, int latticeMode, int rasterMode, int edgeBudget, bool useCustomProj, bool showVectors, int postFxMode, int fluidMode,
  double simT, double zm, double yw, double pt, double bass, double mid, double high,
  double k1, double k2, double k3, double k4,
  double m1, double m2, double m3, double m4,
  double padLx, double padLy, double padRx, double padRy, double padLt, double padRt, double midiNote, double midiVel,
  double roX, double roY, double roZ, double fwX, double fwY, double fwZ, double rtX, double rtY, double rtZ, double upX, double upY, double upZ,
  double[] bands8, double[] qE8, short[] waveL, short[] waveR, int hiRoot8, int hiLeech24,
  double mx, double my, double mclick, double mousePx, double mousePy, bool recActive)
 {
  if (!IsAvailable) return false;
  lock (_lk)
  {
  if (_csProj == IntPtr.Zero || _csSplat == IntPtr.Zero || _csFluid == IntPtr.Zero || _csField == IntPtr.Zero || _csLines == IntPtr.Zero || _csComp == IntPtr.Zero) return false;
  bool canPresent = presentSwap && HasSwapChain;
  bool needReadback = (optionalReadbackFb != null) || (presentSwap && !HasSwapChain);
  if (!EnsureBuffers(w, h, canPresent)) return false;

  int e8Stride = 0, leechStride = 0;
  if (edgeBudget > 0)
  {
   if (latticeMode == 0 || latticeMode == 2) e8Stride = (edgeBudget >= 6720) ? 1 : Math.Max(1, _e8Edges / edgeBudget);
   if (latticeMode == 1 || latticeMode == 2) leechStride = (edgeBudget >= 6720) ? 1 : Math.Max(1, _leechEdges / Math.Max(400, edgeBudget));
  }

  float gsF, gsK;
  GetGrayScottParams(fluidMode, bass, high, m1, m2, out gsF, out gsK);
  float advGain = (float)(0.25 + 2.25 * m3);
  float glowGain = (float)(0.25 + 1.95 * m4);
  float splash = _pendingSplash;
  _pendingSplash = 0.0f;

  _cbData[0] = w; _cbData[1] = h; _cbData[2] = (float)simT; _cbData[3] = (float)zm;
  _cbData[4] = (float)bass; _cbData[5] = (float)mid; _cbData[6] = (float)high; _cbData[7] = (float)yw;
  _cbData[8] = (float)pt; _cbData[9] = (float)k1; _cbData[10] = (float)k2; _cbData[11] = (float)k3;
  _cbData[12] = (float)k4; _cbData[13] = shaderMode; _cbData[14] = (float)roX; _cbData[15] = (float)roY;
  _cbData[16] = (float)roZ; _cbData[17] = (float)fwX; _cbData[18] = (float)fwY; _cbData[19] = (float)fwZ;
  _cbData[20] = (float)rtX; _cbData[21] = (float)rtY; _cbData[22] = (float)rtZ; _cbData[23] = (float)upX;
  _cbData[24] = (float)upY; _cbData[25] = (float)upZ; _cbData[26] = _resetHist ? 1.0f : 0.0f; _cbData[27] = needReadback ? 1.0f : 0.0f;
  for (int i = 0; i < 8; i++) { _cbData[28 + i] = (float)bands8[i]; _cbData[36 + i] = (float)qE8[i]; }
  _cbData[44] = latticeMode; _cbData[45] = rasterMode; _cbData[46] = e8Stride; _cbData[47] = leechStride;
  _cbData[48] = hiRoot8; _cbData[49] = hiLeech24; _cbData[50] = useCustomProj ? 1.0f : 0.0f; _cbData[51] = showVectors ? 1.0f : 0.0f;
  _cbData[52] = (float)mx; _cbData[53] = (float)my; _cbData[54] = (float)mclick; _cbData[55] = postFxMode;
  _cbData[56] = (float)mousePx; _cbData[57] = (float)mousePy; _cbData[58] = (float)(h / 324.0); _cbData[59] = recActive ? 1.0f : 0.0f;
  _cbData[60] = _fw; _cbData[61] = _fh; _cbData[62] = fluidMode; _cbData[63] = splash;
  _cbData[64] = gsF; _cbData[65] = gsK; _cbData[66] = advGain; _cbData[67] = glowGain;
  _cbData[68] = (float)m1; _cbData[69] = (float)m2; _cbData[70] = (float)m3; _cbData[71] = (float)m4;
  _cbData[72] = (float)padLx; _cbData[73] = (float)padLy; _cbData[74] = (float)padRx; _cbData[75] = (float)padRy;
  _cbData[76] = (float)padLt; _cbData[77] = (float)padRt; _cbData[78] = (float)midiNote; _cbData[79] = (float)midiVel;

  bool doReset = _resetHist;
  _resetHist = false;

  for (int i = 0; i < 256; i++) { _waveGpu[i * 2] = waveL[i] / 32768.0f; _waveGpu[i * 2 + 1] = waveR[i] / 32768.0f; }

  GCHandle pinCb = GCHandle.Alloc(_cbData, GCHandleType.Pinned);
  try { _fUpd(_ctx, _cb, 0u, IntPtr.Zero, pinCb.AddrOfPinnedObject(), 0u, 0u); } finally { pinCb.Free(); }
  GCHandle pinWv = GCHandle.Alloc(_waveGpu, GCHandleType.Pinned);
  try { _fUpd(_ctx, _waveBuf, 0u, IntPtr.Zero, pinWv.AddrOfPinnedObject(), 0u, 0u); } finally { pinWv.Free(); }

  IntPtr cb = _cb;
  _fSetCb(_ctx, 0u, 1u, ref cb);
  IntPtr[] zUav1 = new IntPtr[] { IntPtr.Zero };
  IntPtr[] zUav2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
  IntPtr[] zSrv1 = new IntPtr[] { IntPtr.Zero };
  IntPtr[] zSrv3 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
  IntPtr[] zSrv4 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };

  // Pass 1: CSProject (960 threads -> 240 E8 + 720 Leech24 projected screen coords + per-vertex screen velocity)
  _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _e8Srv, _leechSrv, _metaSrv });
  _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _projUav, _velUav }, IntPtr.Zero);
  _fSetCs(_ctx, _csProj, IntPtr.Zero, 0u);
  _fDisp(_ctx, 15u, 1u, 1u);
  _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
  _fSetSrv(_ctx, 0u, 3u, zSrv3);

  // Pass 2 & 3: GPU Ping-Pong Advection + Gray-Scott Reaction-Diffusion Driven by Leech Chord Velocities
  if (fluidMode > 0 || doReset)
  {
   if (!doReset && fluidMode > 0)
   {
   _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _projSrv, _velSrv, _edgesSrv });
   _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _injUav }, IntPtr.Zero);
   _fSetCs(_ctx, _csSplat, IntPtr.Zero, 0u);
   _fDisp(_ctx, ((uint)_totalEdges + 63u) / 64u, 1u, 1u);
   _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
   _fSetSrv(_ctx, 0u, 3u, zSrv3);
   }

   uint fgx = (uint)((_fw + 15) / 16), fgy = (uint)((_fh + 15) / 16);
   _fSetCs(_ctx, _csFluid, IntPtr.Zero, 0u);

   // Ping -> Pong (consumes & zeroes FluidInject, applies semi-Lagrangian advection + Gray-Scott step 1)
   _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _rdSrvA });
   _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _rdUavB, _injUav }, IntPtr.Zero);
   _fDisp(_ctx, fgx, fgy, 1u);
   _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
   _fSetSrv(_ctx, 0u, 1u, zSrv1);

   // Pong -> Ping (FluidInject is now 0 -> pure Gray-Scott + momentum relaxation step 2 back into RdA)
   _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _rdSrvB });
   _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _rdUavA, _injUav }, IntPtr.Zero);
   _fDisp(_ctx, fgx, fgy, 1u);
   _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
   _fSetSrv(_ctx, 0u, 1u, zSrv1);
  }

  // Pass 4: CSField (Per-pixel 4D SDF / Leech24 / E8 shader with live rdU, rdV, velX, velY + m1..m4 uniforms)
  _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _rdSrvA });
  _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _histUav, _hdrUav }, IntPtr.Zero);
  _fSetCs(_ctx, _csField, IntPtr.Zero, 0u);
  _fDisp(_ctx, (uint)((w + 15) / 16), (uint)((h + 15) / 16), 1u);
  _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
  _fSetSrv(_ctx, 0u, 1u, zSrv1);

  // Pass 5: CSLinesAndHud (Velocity-luminescent Wu AA / Bresenham Chords + 960 AA Verts + Lissajous + Spectrum)
  uint totalLineThreads = (uint)(_totalEdges + 960 + 212);
  _fSetSrv(_ctx, 0u, 4u, new IntPtr[] { _projSrv, _edgesSrv, _waveSrv, _velSrv });
  _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _hdrUav }, IntPtr.Zero);
  _fSetCs(_ctx, _csLines, IntPtr.Zero, 0u);
  _fDisp(_ctx, (totalLineThreads + 63u) / 64u, 1u, 1u);
  _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
  _fSetSrv(_ctx, 0u, 4u, zSrv4);

  // Pass 6: CSComposite (Fluid Surface Refraction + Bioluminescent Coral Specular + ACES + Prism + Kaleidoscope)
  _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _histSrv, _hdrSrv, _rdSrvA });
  _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _outTexUav, _rbUav }, IntPtr.Zero);
  _fSetCs(_ctx, _csComp, IntPtr.Zero, 0u);
  _fDisp(_ctx, (uint)((w + 15) / 16), (uint)((h + 15) / 16), 1u);
  _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
  _fSetSrv(_ctx, 0u, 3u, zSrv3);

  if (canPresent && _backBuf != IntPtr.Zero)
  {
   _fCopy(_ctx, _backBuf, _outTex);
   _fPresent(_swap, vsyncInterval, 0u);
  }

  if (needReadback && optionalReadbackFb != null && optionalReadbackFb.Length >= w * h)
  {
   _fCopy(_ctx, _stg, _rbBuf);
   MAP_SUB mp;
   if (_fMap(_ctx, _stg, 0u, 1u, 0u, out mp) >= 0 && mp.pData != IntPtr.Zero)
   {
   try { Marshal.Copy(mp.pData, optionalReadbackFb, 0, w * h); }
   finally { _fUnmap(_ctx, _stg, 0u); }
   }
  }
  return true;
  }
 }

 }
}
