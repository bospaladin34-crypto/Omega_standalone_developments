using System;
using System.Runtime.InteropServices;

namespace OmegaAVStudio
{
 public sealed partial class DirectComputeHlslEngine
 {
 private bool CreateStructuredSrvSafe(IntPtr buf, uint count, out IntPtr srv)
 {
  SRV_DESC sd = new SRV_DESC();
  sd.Format = 0u;
  sd.ViewDimension = 1u; // D3D11_SRV_DIMENSION_BUFFER = 1 (was 4 = TEXTURE2D!)
  sd.FirstElement = 0u;
  sd.NumElements = count;
  int hr = _fSrv(_dev, buf, ref sd, out srv);
  if (hr >= 0 && srv != IntPtr.Zero) return true;
  hr = _fSrvNull(_dev, buf, IntPtr.Zero, out srv);
  if (hr >= 0 && srv != IntPtr.Zero) return true;
  LastError = "SRV 0x" + hr.ToString("X8");
  return false;
 }

 private bool CreateStructuredUavSafe(IntPtr buf, uint count, out IntPtr uav)
 {
  UAV_DESC ud = new UAV_DESC();
  ud.Format = 0u;
  ud.ViewDimension = 1u; // D3D11_UAV_DIMENSION_BUFFER = 1
  ud.FirstElement = 0u;
  ud.NumElements = count;
  int hr = _fUav(_dev, buf, ref ud, out uav);
  if (hr >= 0 && uav != IntPtr.Zero) return true;
  hr = _fUavNull(_dev, buf, IntPtr.Zero, out uav);
  if (hr >= 0 && uav != IntPtr.Zero) return true;
  LastError = "UAV 0x" + hr.ToString("X8");
  return false;
 }

 private bool UploadStaticGeometryBuffers()
 {
  float[] e8Data = new float[240 * 8];
  for (int i = 0; i < 240; i++) for (int k = 0; k < 8; k++) e8Data[i * 8 + k] = (float)E8LatticeMath.Roots[i][k];
  if (!MakeStaticSrv(e8Data, 16u, 480u, out _e8Buf, out _e8Srv)) { LastError = "E8Buf " + LastError; return false; }

  float[] leechData = new float[720 * 24];
  for (int i = 0; i < 720; i++) for (int k = 0; k < 24; k++) leechData[i * 24 + k] = (float)GolayLeech24Math.Vectors[i][k];
  if (!MakeStaticSrv(leechData, 16u, 4320u, out _leechBuf, out _leechSrv)) { LastError = "LeechBuf " + LastError; return false; }

  uint[] meta = new uint[960];
  for (int i = 0; i < 240; i++) meta[i] = E8LatticeMath.RootRing[i];
  for (int i = 0; i < 720; i++) meta[240 + i] = GolayLeech24Math.VecShape[i];
  if (!MakeStaticSrvUint(meta, 4u, 960u, out _metaBuf, out _metaSrv)) { LastError = "MetaBuf " + LastError; return false; }

  _e8Edges = E8LatticeMath.EdgeA.Length;
  _leechEdges = GolayLeech24Math.EdgeA.Length;
  _totalEdges = _e8Edges + _leechEdges;
  uint[] edgeData = new uint[_totalEdges * 4];
  int p = 0;
  for (int e = 0; e < _e8Edges; e++)
  {
  int a = E8LatticeMath.EdgeA[e], b = E8LatticeMath.EdgeB[e];
  byte ring = Math.Max(E8LatticeMath.RootRing[a], E8LatticeMath.RootRing[b]);
  uint cr = (ring >= 6) ? 30u : 12u, cg = (ring >= 6) ? 62u : 26u, cb = (ring >= 6) ? 94u : 48u;
  edgeData[p++] = (uint)a; edgeData[p++] = (uint)b; edgeData[p++] = 0u; edgeData[p++] = (cr << 16) | (cg << 8) | cb;
  }
  for (int e = 0; e < _leechEdges; e++)
  {
  int a = GolayLeech24Math.EdgeA[e], b = GolayLeech24Math.EdgeB[e];
  byte sh = GolayLeech24Math.VecShape[a];
  uint cr = (sh == 0) ? 14u : (sh == 1 ? 32u : 34u);
  uint cg = (sh == 0) ? 48u : (sh == 1 ? 18u : 42u);
  uint cb = (sh == 0) ? 72u : (sh == 1 ? 68u : 18u);
  edgeData[p++] = (uint)(240 + a); edgeData[p++] = (uint)(240 + b); edgeData[p++] = 1u; edgeData[p++] = (cr << 16) | (cg << 8) | cb;
  }
  if (!MakeStaticSrvUint(edgeData, 16u, (uint)_totalEdges, out _edgesBuf, out _edgesSrv)) { LastError = "EdgesBuf " + LastError; return false; }

  if (!MakeRwStructured(16u, 960u, out _projBuf, out _projUav, out _projSrv)) { LastError = "ProjBuf " + LastError; return false; }
  if (!MakeRwStructured(16u, 960u, out _velBuf, out _velUav, out _velSrv)) { LastError = "VelBuf " + LastError; return false; }

  BUF_DESC wd = new BUF_DESC(); wd.ByteWidth = 2048u; wd.Usage = 0u; wd.BindFlags = 8u; wd.MiscFlags = 0x40u; wd.StructureByteStride = 8u;
  int hrW = _fBuf(_dev, ref wd, IntPtr.Zero, out _waveBuf);
  if (hrW < 0 || _waveBuf == IntPtr.Zero) { LastError = "WaveBuf 0x" + hrW.ToString("X8"); return false; }
  return CreateStructuredSrvSafe(_waveBuf, 256u, out _waveSrv);
 }

 private bool EnsureBuffers(int w, int h, bool syncSwapSize)
 {
  if (w < 64 || h < 64) return false;
  if (syncSwapSize && HasSwapChain && (_swapW != w || _swapH != h))
  {
  Rel(_backBuf); _backBuf = IntPtr.Zero;
  if (_fResize(_swap, 2u, (uint)w, (uint)h, 28u, 0u) >= 0 && _fGetBuf(_swap, 0u, ref IID_Tex2D, out _backBuf) >= 0)
  {
   _swapW = w; _swapH = h;
  }
  }
  if (_w == w && _h == h && _outTex != IntPtr.Zero && _histBuf != IntPtr.Zero && _hdrBuf != IntPtr.Zero && _rdBufA != IntPtr.Zero && _rdBufB != IntPtr.Zero && _injBuf != IntPtr.Zero && _rbBuf != IntPtr.Zero && _stg != IntPtr.Zero) return true;

  Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
  Rel(_histSrv); _histSrv = IntPtr.Zero; Rel(_histUav); _histUav = IntPtr.Zero; Rel(_histBuf); _histBuf = IntPtr.Zero;
  Rel(_hdrSrv); _hdrSrv = IntPtr.Zero; Rel(_hdrUav); _hdrUav = IntPtr.Zero; Rel(_hdrBuf); _hdrBuf = IntPtr.Zero;
  Rel(_rdSrvA); _rdSrvA = IntPtr.Zero; Rel(_rdUavA); _rdUavA = IntPtr.Zero; Rel(_rdBufA); _rdBufA = IntPtr.Zero;
  Rel(_rdSrvB); _rdSrvB = IntPtr.Zero; Rel(_rdUavB); _rdUavB = IntPtr.Zero; Rel(_rdBufB); _rdBufB = IntPtr.Zero;
  Rel(_injUav); _injUav = IntPtr.Zero; Rel(_injBuf); _injBuf = IntPtr.Zero;
  Rel(_rbUav); _rbUav = IntPtr.Zero; Rel(_rbBuf); _rbBuf = IntPtr.Zero; Rel(_stg); _stg = IntPtr.Zero;

  TEX2D_DESC td = new TEX2D_DESC();
  td.Width = (uint)w; td.Height = (uint)h; td.MipLevels = 1u; td.ArraySize = 1u;
  td.Format = 28u; td.SampleCount = 1u; td.BindFlags = 0x80u;
  int hrT = _fTex2D(_dev, ref td, IntPtr.Zero, out _outTex);
  if (hrT < 0 || _outTex == IntPtr.Zero) { LastError = "OutTex 0x" + hrT.ToString("X8"); return false; }
  UAV_DESC tud = new UAV_DESC(); tud.Format = 28u; tud.ViewDimension = 4u; // D3D11_UAV_DIMENSION_TEXTURE2D = 4
  int hrTu = _fUav(_dev, _outTex, ref tud, out _outTexUav);
  if (hrTu < 0 || _outTexUav == IntPtr.Zero) { LastError = "OutTexUAV 0x" + hrTu.ToString("X8"); return false; }

  uint pix = (uint)(w * h);
  if (!MakeRwStructured(4u, pix, out _histBuf, out _histUav, out _histSrv)) { LastError = "HistBuf " + LastError; return false; }
  if (!MakeRwStructured(4u, pix * 4u, out _hdrBuf, out _hdrUav, out _hdrSrv)) { LastError = "HdrBuf " + LastError; return false; }

  int fw = Math.Max(64, (w + 1) / 2), fh = Math.Max(36, (h + 1) / 2);
  uint fpix = (uint)(fw * fh);
  if (!MakeRwStructured(16u, fpix, out _rdBufA, out _rdUavA, out _rdSrvA)) { LastError = "RdA " + LastError; return false; }
  if (!MakeRwStructured(16u, fpix, out _rdBufB, out _rdUavB, out _rdSrvB)) { LastError = "RdB " + LastError; return false; }

  BUF_DESC id = new BUF_DESC(); id.ByteWidth = fpix * 12u; id.BindFlags = 0x80u; id.MiscFlags = 0x40u; id.StructureByteStride = 4u;
  int hrI = _fBuf(_dev, ref id, IntPtr.Zero, out _injBuf);
  if (hrI < 0 || _injBuf == IntPtr.Zero) { LastError = "InjBuf 0x" + hrI.ToString("X8"); return false; }
  if (!CreateStructuredUavSafe(_injBuf, fpix * 3u, out _injUav)) { LastError = "InjUAV " + LastError; return false; }

  BUF_DESC rbd = new BUF_DESC(); rbd.ByteWidth = pix * 4u; rbd.BindFlags = 0x80u; rbd.MiscFlags = 0x40u; rbd.StructureByteStride = 4u;
  int hrRb = _fBuf(_dev, ref rbd, IntPtr.Zero, out _rbBuf);
  if (hrRb < 0 || _rbBuf == IntPtr.Zero) { LastError = "RbBuf 0x" + hrRb.ToString("X8"); return false; }
  if (!CreateStructuredUavSafe(_rbBuf, pix, out _rbUav)) { LastError = "RbUAV " + LastError; return false; }

  BUF_DESC sd = new BUF_DESC(); sd.ByteWidth = pix * 4u; sd.Usage = 3u; sd.CPUAccessFlags = 0x20000u;
  int hrS = _fBuf(_dev, ref sd, IntPtr.Zero, out _stg);
  if (hrS < 0 || _stg == IntPtr.Zero) { LastError = "StgBuf 0x" + hrS.ToString("X8"); return false; }

  _w = w; _h = h; _fw = fw; _fh = fh; _resetHist = true;
  return true;
 }

 private bool MakeStaticSrv(float[] data, uint stride, uint count, out IntPtr buf, out IntPtr srv)
 {
  buf = IntPtr.Zero; srv = IntPtr.Zero;
  BUF_DESC bd = new BUF_DESC(); bd.ByteWidth = stride * count; bd.Usage = 0u; bd.BindFlags = 8u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
  int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
  if (hr < 0 || buf == IntPtr.Zero) { LastError = "BufF 0x" + hr.ToString("X8"); return false; }
  GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
  try { _fUpd(_ctx, buf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
  finally { pin.Free(); }
  return CreateStructuredSrvSafe(buf, count, out srv);
 }

 private bool MakeStaticSrvUint(uint[] data, uint stride, uint count, out IntPtr buf, out IntPtr srv)
 {
  buf = IntPtr.Zero; srv = IntPtr.Zero;
  BUF_DESC bd = new BUF_DESC(); bd.ByteWidth = stride * count; bd.Usage = 0u; bd.BindFlags = 8u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
  int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
  if (hr < 0 || buf == IntPtr.Zero) { LastError = "BufU 0x" + hr.ToString("X8"); return false; }
  GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
  try { _fUpd(_ctx, buf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
  finally { pin.Free(); }
  return CreateStructuredSrvSafe(buf, count, out srv);
 }

 private bool MakeRwStructured(uint stride, uint count, out IntPtr buf, out IntPtr uav, out IntPtr srv)
 {
  buf = IntPtr.Zero; uav = IntPtr.Zero; srv = IntPtr.Zero;
  BUF_DESC bd = new BUF_DESC(); bd.ByteWidth = stride * count; bd.Usage = 0u; bd.BindFlags = 0x88u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
  int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
  if (hr < 0 || buf == IntPtr.Zero) { LastError = "RwBuf 0x" + hr.ToString("X8"); return false; }
  if (!CreateStructuredUavSafe(buf, count, out uav)) return false;
  return CreateStructuredSrvSafe(buf, count, out srv);
 }

 private static T Vtbl<T>(IntPtr p, int slot) where T : class { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size), typeof(T)); }
 private static void Rel(IntPtr p) { if (p != IntPtr.Zero) { try { Vtbl<RelDel>(p, 2)(p); } catch { } } }

 public void Dispose()
 {
  lock (_lk)
  {
  Rel(_csProj); _csProj = IntPtr.Zero; Rel(_csSplat); _csSplat = IntPtr.Zero; Rel(_csFluid); _csFluid = IntPtr.Zero;
  Rel(_csField); _csField = IntPtr.Zero; Rel(_csLines); _csLines = IntPtr.Zero; Rel(_csComp); _csComp = IntPtr.Zero;
  Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
  Rel(_histSrv); _histSrv = IntPtr.Zero; Rel(_histUav); _histUav = IntPtr.Zero; Rel(_histBuf); _histBuf = IntPtr.Zero;
  Rel(_hdrSrv); _hdrSrv = IntPtr.Zero; Rel(_hdrUav); _hdrUav = IntPtr.Zero; Rel(_hdrBuf); _hdrBuf = IntPtr.Zero;
  Rel(_rdSrvA); _rdSrvA = IntPtr.Zero; Rel(_rdUavA); _rdUavA = IntPtr.Zero; Rel(_rdBufA); _rdBufA = IntPtr.Zero;
  Rel(_rdSrvB); _rdSrvB = IntPtr.Zero; Rel(_rdUavB); _rdUavB = IntPtr.Zero; Rel(_rdBufB); _rdBufB = IntPtr.Zero;
  Rel(_injUav); _injUav = IntPtr.Zero; Rel(_injBuf); _injBuf = IntPtr.Zero;
  Rel(_rbUav); _rbUav = IntPtr.Zero; Rel(_rbBuf); _rbBuf = IntPtr.Zero; Rel(_stg); _stg = IntPtr.Zero;
  Rel(_projSrv); _projSrv = IntPtr.Zero; Rel(_projUav); _projUav = IntPtr.Zero; Rel(_projBuf); _projBuf = IntPtr.Zero;
  Rel(_velSrv); _velSrv = IntPtr.Zero; Rel(_velUav); _velUav = IntPtr.Zero; Rel(_velBuf); _velBuf = IntPtr.Zero;
  Rel(_waveSrv); _waveSrv = IntPtr.Zero; Rel(_waveBuf); _waveBuf = IntPtr.Zero;
  Rel(_edgesSrv); _edgesSrv = IntPtr.Zero; Rel(_edgesBuf); _edgesBuf = IntPtr.Zero;
  Rel(_metaSrv); _metaSrv = IntPtr.Zero; Rel(_metaBuf); _metaBuf = IntPtr.Zero;
  Rel(_leechSrv); _leechSrv = IntPtr.Zero; Rel(_leechBuf); _leechBuf = IntPtr.Zero;
  Rel(_e8Srv); _e8Srv = IntPtr.Zero; Rel(_e8Buf); _e8Buf = IntPtr.Zero;
  Rel(_backBuf); _backBuf = IntPtr.Zero; Rel(_swap); _swap = IntPtr.Zero; Rel(_cb); _cb = IntPtr.Zero;
  Rel(_ctx); _ctx = IntPtr.Zero; Rel(_dev); _dev = IntPtr.Zero;
  IsAvailable = false; HasSwapChain = false;
  }
 }
 }
}
