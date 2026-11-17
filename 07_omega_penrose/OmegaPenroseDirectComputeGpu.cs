using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OmegaPenroseStudio
{
    public sealed class OmegaPenroseGpuEngine : IDisposable
    {
        public const int GridN = 512;
        public const int FftN = 256;
        public const int KernelSide = 33;

        [StructLayout(LayoutKind.Sequential)] private struct BUF_DESC { public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride; }
        [StructLayout(LayoutKind.Sequential)] private struct TEX2D_DESC { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct UAV_DESC { public uint Format, ViewDimension, FirstElement, NumElements, Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct SRV_DESC { public uint Format, ViewDimension, FirstElement, NumElements, Pad0, Pad1; }
        [StructLayout(LayoutKind.Sequential)] private struct SWAP_DESC
        {
            public uint Width, Height, RefreshNum, RefreshDen, Format, ScanlineOrdering, Scaling;
            public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
            public IntPtr OutputWindow;
            public int Windowed;
            public uint SwapEffect, Flags;
        }

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3D11CreateDeviceAndSwapChain(IntPtr pAdapter, int driverType, IntPtr sw, uint flags, uint[] pFL, uint nFL, uint sdkVer, ref SWAP_DESC pDesc, out IntPtr ppSwap, out IntPtr ppDev, out uint pChosenFL, out IntPtr ppCtx);

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] pSrc, IntPtr srcLen, string srcName, IntPtr pDefs, IntPtr pInc, string entry, string target, uint f1, uint f2, out IntPtr ppCode, out IntPtr ppErr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBufDel(IntPtr d, ref BUF_DESC desc, IntPtr pInit, out IntPtr ppB);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTex2DDel(IntPtr d, ref TEX2D_DESC desc, IntPtr pInit, out IntPtr ppT);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvDel(IntPtr d, IntPtr res, ref SRV_DESC desc, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavDel(IntPtr d, IntPtr res, ref UAV_DESC desc, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCsDel(IntPtr d, IntPtr code, IntPtr len, IntPtr link, out IntPtr ppCs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UpdateSubDel(IntPtr c, IntPtr dst, uint sub, IntPtr box, IntPtr src, uint rowP, uint depthP);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetSrvDel(IntPtr c, uint slot, uint num, IntPtr[] srvs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetCsDel(IntPtr c, IntPtr cs, IntPtr inst, uint num);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetCbDel(IntPtr c, uint slot, uint num, ref IntPtr cb);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetUavDel(IntPtr c, uint slot, uint num, IntPtr[] uavs, IntPtr initCounts);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DispatchDel(IntPtr c, uint x, uint y, uint z);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResDel(IntPtr c, IntPtr dst, IntPtr src);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSwapBufDel(IntPtr sw, uint buf, ref Guid iid, out IntPtr ppSurf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PresentDel(IntPtr sw, uint sync, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResizeBufDel(IntPtr sw, uint cnt, uint w, uint h, uint fmt, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPtrDel(IntPtr b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobSizeDel(IntPtr b);

        private static T Vt<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vt, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
        }

        private static void Rel(IntPtr p)
        {
            if (p != IntPtr.Zero) { try { Marshal.Release(p); } catch { } }
        }

        private readonly object _lk = new object();
        private IntPtr _dev, _ctx, _swap, _backBuf, _cb;
        private IntPtr _csQuasi, _csFlowPot, _csFlowReint, _csQuantum, _csFftRows, _csFftCols, _csStage, _csRhombs, _csCrystal3d, _csComp;

        // Simulation, Genome, Hologram & Optics Buffers
        private IntPtr _leniaBufA, _leniaSrvA, _leniaUavA;
        private IntPtr _leniaBufB, _leniaSrvB, _leniaUavB;
        private IntPtr _genomeBufA, _genomeSrvA, _genomeUavA; // 512x512 float2: localized heritable (Mu, Sigma)
        private IntPtr _genomeBufB, _genomeSrvB, _genomeUavB;
        private IntPtr _flowPotBuf, _flowPotSrv, _flowPotUav;
        private IntPtr _quasiBuf, _quasiSrv, _quasiUav;
        private IntPtr _fftBufA, _fftSrvA, _fftUavA;
        private IntPtr _fftBufB, _fftSrvB, _fftUavB;
        private IntPtr _kernelBuf, _kernelSrv;
        private IntPtr _holoPhaseBuf, _holoPhaseSrv;          // 256x256 float Gerchberg-Saxton CGH phase mask!
        private IntPtr _rhombBuf, _rhombSrv;
        private IntPtr _quad3dBuf, _quad3dSrv;

        // Viewport & Hardware Z-Buffer
        private IntPtr _basePixBuf, _basePixSrv, _basePixUav;
        private IntPtr _lineRgbBuf, _lineRgbSrv, _lineRgbUav;
        private IntPtr _zBuf3d, _zBuf3dSrv, _zBuf3dUav;       // W*H uint: (depth14 << 18) | RGB666 via InterlockedMax!
        private IntPtr _outTex, _outTexUav;
        private int _vpW, _vpH;

        private CreateBufDel _fBuf;
        private CreateTex2DDel _fTex;
        private CreateSrvDel _fSrv;
        private CreateUavDel _fUav;
        private CreateCsDel _fCs;
        private UpdateSubDel _fUpd;
        private CsSetSrvDel _fSetSrv;
        private CsSetCsDel _fSetCs;
        private CsSetCbDel _fSetCb;
        private CsSetUavDel _fSetUav;
        private DispatchDel _fDisp;
        private CopyResDel _fCopy;
        private GetSwapBufDel _fGetBack;
        private PresentDel _fPresent;
        private ResizeBufDel _fResize;

        private readonly float[] _cbData = new float[96]; // 24 float4 registers (384 bytes)
        private readonly float[] _rhombPack = new float[OmegaPenroseDeBruijnMath.MaxGpuRhombs * 16];
        private readonly float[] _quad3dPack = new float[OmegaPenroseIcosahedral3D.MaxGpuQuads * 12];
        private readonly float[] _cpuFieldUpload = new float[GridN * GridN * 4];
        private readonly float[] _cpuGenomeUpload = new float[GridN * GridN * 2];
        private bool _pendingFieldUpload = false;
        private bool _pingPongFlip = false;
        private uint _frameCounter = 0u;
        private int _uploadedRhombCount = 0;
        private int _uploadedQuad3dCount = 0;

        public volatile bool VSyncEnabled = true;
        public bool IsGpuActive { get; private set; }
        public string BackendLabel { get; private set; }
        public string LastError { get; private set; }
        public float TotalConservedMassR { get; private set; }
        public float TotalConservedMassG { get; private set; }
        public float TotalConservedMassB { get; private set; }

        public OmegaPenroseGpuEngine()
        {
            BackendLabel = "Initializing...";
            LastError = "";
        }

        public bool Initialize(IntPtr hwnd, int w, int h)
        {
            lock (_lk)
            {
                try
                {
                    w = Math.Max(320, w);
                    h = Math.Max(240, h);

                    SWAP_DESC sd = new SWAP_DESC();
                    sd.Width = (uint)w; sd.Height = (uint)h;
                    sd.RefreshNum = 0u; sd.RefreshDen = 1u;
                    sd.Format = 28u;
                    sd.SampleCount = 1u;
                    sd.BufferUsage = 32u;
                    sd.BufferCount = 2u;
                    sd.OutputWindow = hwnd;
                    sd.Windowed = 1;
                    sd.SwapEffect = 0u;

                    uint[] fls = new uint[] { 0xb000u, 0xa100u, 0xa000u };
                    uint chosen;
                    int hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, (uint)fls.Length, 7u, ref sd, out _swap, out _dev, out chosen, out _ctx);
                    if (hr < 0 || _dev == IntPtr.Zero)
                    {
                        LastError = "D3D11Create 0x" + hr.ToString("X8");
                        BackendLabel = "CPU Fallback (" + LastError + ")";
                        return false;
                    }

                    _fBuf     = Vt<CreateBufDel>(_dev, 3);
                    _fTex     = Vt<CreateTex2DDel>(_dev, 5);
                    _fSrv     = Vt<CreateSrvDel>(_dev, 7);
                    _fUav     = Vt<CreateUavDel>(_dev, 8);
                    _fCs      = Vt<CreateCsDel>(_dev, 18);
                    _fSetSrv  = Vt<CsSetSrvDel>(_ctx, 67); // ID3D11DeviceContext::CSSetShaderResources = slot 67!
                    _fUpd     = Vt<UpdateSubDel>(_ctx, 48);
                    _fSetCs   = Vt<CsSetCsDel>(_ctx, 69);
                    _fSetUav  = Vt<CsSetUavDel>(_ctx, 68);
                    _fSetCb   = Vt<CsSetCbDel>(_ctx, 71);
                    _fDisp    = Vt<DispatchDel>(_ctx, 41);
                    _fCopy    = Vt<CopyResDel>(_ctx, 47);
                    _fPresent = Vt<PresentDel>(_swap, 8);
                    _fGetBack = Vt<GetSwapBufDel>(_swap, 9);
                    _fResize  = Vt<ResizeBufDel>(_swap, 13);

                    BUF_DESC cbd = new BUF_DESC();
                    cbd.ByteWidth = 384u; cbd.Usage = 0u; cbd.BindFlags = 4u;
                    if (_fBuf(_dev, ref cbd, IntPtr.Zero, out _cb) < 0) return false;

                    uint gridCount = (uint)(GridN * GridN);
                    uint fftCount = (uint)(FftN * FftN);
                    if (!MakeRwStructBuf(gridCount, 16u, out _leniaBufA, out _leniaSrvA, out _leniaUavA) ||
                        !MakeRwStructBuf(gridCount, 16u, out _leniaBufB, out _leniaSrvB, out _leniaUavB) ||
                        !MakeRwStructBuf(gridCount, 8u,  out _genomeBufA, out _genomeSrvA, out _genomeUavA) ||
                        !MakeRwStructBuf(gridCount, 8u,  out _genomeBufB, out _genomeSrvB, out _genomeUavB) ||
                        !MakeRwStructBuf(gridCount, 16u, out _flowPotBuf, out _flowPotSrv, out _flowPotUav) ||
                        !MakeRwStructBuf(gridCount, 8u,  out _quasiBuf,  out _quasiSrv,  out _quasiUav) ||
                        !MakeRwStructBuf(fftCount,  8u,  out _fftBufA,   out _fftSrvA,   out _fftUavA) ||
                        !MakeRwStructBuf(fftCount,  8u,  out _fftBufB,   out _fftSrvB,   out _fftUavB))
                    {
                        LastError = "RWStructBuf failed";
                        BackendLabel = "CPU Fallback (" + LastError + ")";
                        return false;
                    }

                    if (!MakeSrvStructBuf((uint)(KernelSide * KernelSide), 4u, out _kernelBuf, out _kernelSrv) ||
                        !MakeSrvStructBuf(fftCount, 4u, out _holoPhaseBuf, out _holoPhaseSrv) ||
                        !MakeSrvStructBuf((uint)(OmegaPenroseDeBruijnMath.MaxGpuRhombs * 4), 16u, out _rhombBuf, out _rhombSrv) ||
                        !MakeSrvStructBuf((uint)(OmegaPenroseIcosahedral3D.MaxGpuQuads * 3), 16u, out _quad3dBuf, out _quad3dSrv))
                    {
                        LastError = "SRV StructBuf failed";
                        BackendLabel = "CPU Fallback (" + LastError + ")";
                        return false;
                    }

                    _csQuasi     = CompileCs(BuildQuasiPotentialHlsl(),  "CSQuasiPotential");
                    _csFlowPot   = CompileCs(BuildFlowPotentialHlsl(),   "CSFlowPotential");
                    _csFlowReint = CompileCs(BuildFlowReintegrateHlsl(), "CSFlowReintegrate");
                    _csQuantum   = CompileCs(BuildQuantumStepHlsl(),     "CSQuantumStep");
                    _csFftRows   = CompileCs(BuildFftRows256Hlsl(),      "CSFftRows256");
                    _csFftCols   = CompileCs(BuildFftCols256Hlsl(),      "CSFftCols256");
                    _csStage     = CompileCs(BuildStageRenderHlsl(),     "CSStageRender");
                    _csRhombs    = CompileCs(BuildRhombWireframeHlsl(),  "CSRhombWireframe");
                    _csCrystal3d = CompileCs(BuildCrystal3dHlsl(),       "CSCrystal3D");
                    _csComp      = CompileCs(BuildCompositeHlsl(),       "CSComposite");

                    if (_csQuasi == IntPtr.Zero || _csFlowPot == IntPtr.Zero || _csFlowReint == IntPtr.Zero ||
                        _csQuantum == IntPtr.Zero || _csFftRows == IntPtr.Zero || _csFftCols == IntPtr.Zero ||
                        _csStage == IntPtr.Zero || _csRhombs == IntPtr.Zero || _csCrystal3d == IntPtr.Zero || _csComp == IntPtr.Zero)
                    {
                        BackendLabel = "CPU Fallback (" + LastError + ")";
                        return false;
                    }

                    if (!EnsureViewportBuffers(w, h))
                    {
                        BackendLabel = "CPU Fallback (" + LastError + ")";
                        return false;
                    }

                    IsGpuActive = true;
                    BackendLabel = "RTX DirectCompute cs_5_0 VSync";
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    BackendLabel = "CPU Fallback (" + ex.Message + ")";
                    IsGpuActive = false;
                    return false;
                }
            }
        }

        private bool MakeSrvStructBuf(uint count, uint stride, out IntPtr buf, out IntPtr srv)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = count * stride; bd.Usage = 0u; bd.BindFlags = 8u; bd.MiscFlags = 64u; bd.StructureByteStride = stride;
            if (_fBuf(_dev, ref bd, IntPtr.Zero, out buf) < 0 || buf == IntPtr.Zero) return false;

            SRV_DESC sd = new SRV_DESC();
            sd.Format = 0u; sd.ViewDimension = 1u; sd.FirstElement = 0u; sd.NumElements = count;
            return (_fSrv(_dev, buf, ref sd, out srv) >= 0 && srv != IntPtr.Zero);
        }

        private bool MakeRwStructBuf(uint count, uint stride, out IntPtr buf, out IntPtr srv, out IntPtr uav)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero; uav = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = count * stride; bd.Usage = 0u; bd.BindFlags = 8u | 128u; bd.MiscFlags = 64u; bd.StructureByteStride = stride;
            if (_fBuf(_dev, ref bd, IntPtr.Zero, out buf) < 0 || buf == IntPtr.Zero) return false;

            SRV_DESC sd = new SRV_DESC();
            sd.Format = 0u; sd.ViewDimension = 1u; sd.FirstElement = 0u; sd.NumElements = count;
            if (_fSrv(_dev, buf, ref sd, out srv) < 0 || srv == IntPtr.Zero) return false;

            UAV_DESC ud = new UAV_DESC();
            ud.Format = 0u; ud.ViewDimension = 1u; ud.FirstElement = 0u; ud.NumElements = count;
            return (_fUav(_dev, buf, ref ud, out uav) >= 0 && uav != IntPtr.Zero);
        }

        public void UploadLeniaKernel33(float[] kernel1089)
        {
            lock (_lk)
            {
                if (!IsGpuActive || _kernelBuf == IntPtr.Zero || kernel1089 == null) return;
                GCHandle pin = GCHandle.Alloc(kernel1089, GCHandleType.Pinned);
                try { _fUpd(_ctx, _kernelBuf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
                finally { pin.Free(); }
            }
        }

        public void UploadHologramPhase256(float[] phase65536)
        {
            lock (_lk)
            {
                if (!IsGpuActive || _holoPhaseBuf == IntPtr.Zero || phase65536 == null || phase65536.Length < FftN * FftN) return;
                GCHandle pin = GCHandle.Alloc(phase65536, GCHandleType.Pinned);
                try { _fUpd(_ctx, _holoPhaseBuf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
                finally { pin.Free(); }
            }
        }

        public void UploadDualRhombMesh(List<PenroseRhombTile> tiles)
        {
            lock (_lk)
            {
                int n = (tiles == null) ? 0 : Math.Min(OmegaPenroseDeBruijnMath.MaxGpuRhombs, tiles.Count);
                _uploadedRhombCount = n;
                Array.Clear(_rhombPack, 0, _rhombPack.Length);
                for (int i = 0; i < n; i++)
                {
                    PenroseRhombTile t = tiles[i];
                    int o = i * 16;
                    _rhombPack[o + 0]  = t.X0;               _rhombPack[o + 1]  = t.Y0;
                    _rhombPack[o + 2]  = t.X1;               _rhombPack[o + 3]  = t.Y1;
                    _rhombPack[o + 4]  = t.X2;               _rhombPack[o + 5]  = t.Y2;
                    _rhombPack[o + 6]  = t.X3;               _rhombPack[o + 7]  = t.Y3;
                    _rhombPack[o + 8]  = t.IsThick;          _rhombPack[o + 9]  = (float)(t.WormFamilyA + t.EdgeMask * 16);
                    _rhombPack[o + 10] = t.IndexSumK;        _rhombPack[o + 11] = t.PhasonFlipEnergy;
                    _rhombPack[o + 12] = t.PerpX;            _rhombPack[o + 13] = t.PerpY;
                    _rhombPack[o + 14] = t.ZTierHeight;      _rhombPack[o + 15] = t.IsCorridorPath;
                }
                if (IsGpuActive && _rhombBuf != IntPtr.Zero)
                {
                    GCHandle pin = GCHandle.Alloc(_rhombPack, GCHandleType.Pinned);
                    try { _fUpd(_ctx, _rhombBuf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
                    finally { pin.Free(); }
                }
            }
        }

        public void UploadProjected3DQuads(float[] pack12PerQuad, int quadCount)
        {
            lock (_lk)
            {
                _uploadedQuad3dCount = Math.Max(0, Math.Min(OmegaPenroseIcosahedral3D.MaxGpuQuads, quadCount));
                if (_uploadedQuad3dCount <= 0 || !IsGpuActive || _quad3dBuf == IntPtr.Zero) return;
                Array.Clear(_quad3dPack, 0, _quad3dPack.Length);
                Array.Copy(pack12PerQuad, 0, _quad3dPack, 0, _uploadedQuad3dCount * 12);
                GCHandle pin = GCHandle.Alloc(_quad3dPack, GCHandleType.Pinned);
                try { _fUpd(_ctx, _quad3dBuf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
                finally { pin.Free(); }
            }
        }

        public void QueueRgbAndGenomeUpload(float[] rgbaField512, float[] genome2Field512, float defaultMu, float defaultSig)
        {
            lock (_lk)
            {
                if (rgbaField512 == null || rgbaField512.Length < _cpuFieldUpload.Length) return;
                Array.Copy(rgbaField512, 0, _cpuFieldUpload, 0, _cpuFieldUpload.Length);
                if (genome2Field512 != null && genome2Field512.Length >= _cpuGenomeUpload.Length)
                {
                    Array.Copy(genome2Field512, 0, _cpuGenomeUpload, 0, _cpuGenomeUpload.Length);
                }
                else
                {
                    for (int i = 0; i < GridN * GridN; i++)
                    {
                        _cpuGenomeUpload[i * 2 + 0] = defaultMu;
                        _cpuGenomeUpload[i * 2 + 1] = defaultSig;
                    }
                }
                double sr = 0, sg = 0, sb = 0;
                for (int i = 0; i < _cpuFieldUpload.Length; i += 4)
                {
                    sr += Math.Abs(_cpuFieldUpload[i + 0]);
                    sg += Math.Abs(_cpuFieldUpload[i + 1]);
                    sb += Math.Abs(_cpuFieldUpload[i + 2]);
                }
                TotalConservedMassR = (float)sr;
                TotalConservedMassG = (float)sg;
                TotalConservedMassB = (float)sb;
                _pendingFieldUpload = true;
            }
        }

        private bool EnsureViewportBuffers(int w, int h)
        {
            w = Math.Max(64, w); h = Math.Max(64, h);
            if (_vpW == w && _vpH == h && _outTex != IntPtr.Zero && _backBuf != IntPtr.Zero) return true;

            Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
            Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;
            Rel(_zBuf3dUav);  _zBuf3dUav  = IntPtr.Zero; Rel(_zBuf3dSrv);  _zBuf3dSrv  = IntPtr.Zero; Rel(_zBuf3d);     _zBuf3d     = IntPtr.Zero;
            Rel(_outTexUav);  _outTexUav  = IntPtr.Zero; Rel(_outTex);     _outTex     = IntPtr.Zero; Rel(_backBuf);    _backBuf    = IntPtr.Zero;

            if (_vpW != 0 && _swap != IntPtr.Zero)
                _fResize(_swap, 2u, (uint)w, (uint)h, 28u, 0u);

            Guid iidTex = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            if (_fGetBack(_swap, 0u, ref iidTex, out _backBuf) < 0 || _backBuf == IntPtr.Zero) return false;

            TEX2D_DESC td = new TEX2D_DESC();
            td.Width = (uint)w; td.Height = (uint)h; td.MipLevels = 1u; td.ArraySize = 1u;
            td.Format = 28u; td.SampleCount = 1u; td.Usage = 0u; td.BindFlags = 128u;
            if (_fTex(_dev, ref td, IntPtr.Zero, out _outTex) < 0) return false;

            UAV_DESC tud = new UAV_DESC(); tud.Format = 28u; tud.ViewDimension = 4u;
            if (_fUav(_dev, _outTex, ref tud, out _outTexUav) < 0) return false;

            uint px = (uint)(w * h);
            if (!MakeRwStructBuf(px, 4u, out _basePixBuf, out _basePixSrv, out _basePixUav) ||
                !MakeRwStructBuf(px * 3u, 4u, out _lineRgbBuf, out _lineRgbSrv, out _lineRgbUav) ||
                !MakeRwStructBuf(px, 4u, out _zBuf3d, out _zBuf3dSrv, out _zBuf3dUav))
                return false;

            _vpW = w; _vpH = h;
            return true;
        }

        private IntPtr CompileCs(string hlsl, string entry)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(hlsl);
            IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;
            try
            {
                int hr = D3DCompile(bytes, (IntPtr)bytes.Length, entry + ".hlsl", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 0u, 0u, out pCode, out pErr);
                if (hr < 0 || pCode == IntPtr.Zero)
                {
                    string errTxt = "0x" + hr.ToString("X8");
                    if (pErr != IntPtr.Zero)
                    {
                        IntPtr ptr = Vt<BlobPtrDel>(pErr, 3)(pErr);
                        int len = (int)Vt<BlobSizeDel>(pErr, 4)(pErr);
                        if (ptr != IntPtr.Zero && len > 0) errTxt = Marshal.PtrToStringAnsi(ptr, Math.Min(220, len));
                    }
                    LastError = entry + ": " + errTxt;
                    return IntPtr.Zero;
                }
                IntPtr bcPtr = Vt<BlobPtrDel>(pCode, 3)(pCode);
                IntPtr bcLen = Vt<BlobSizeDel>(pCode, 4)(pCode);
                IntPtr cs;
                if (_fCs(_dev, bcPtr, bcLen, IntPtr.Zero, out cs) < 0) return IntPtr.Zero;
                return cs;
            }
            finally { Rel(pCode); Rel(pErr); }
        }

        // Pass 1: 9-Symmetry Quasicrystal Bloch Potential (incl. 30-Fold E8 & Hat/Spectre) + Inverse Laser Hologram CGH!
        private static string BuildQuasiPotentialHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> LeniaCur : register(t0);
StructuredBuffer<float>  HoloPhase : register(t1);
RWStructuredBuffer<float2> QuasiOut : register(u0);
RWStructuredBuffer<float2> FftAperture : register(u1);

float BlackmanHarris4(float t)
{
    return 0.35875 - 0.48829 * cos(6.283185307 * t) + 0.14128 * cos(12.56637061 * t) - 0.01168 * cos(18.84955592 * t);
}

float EvalZeroMeanQuasiLattice(float2 wpos, int symMode, float phason, float g0, float g1, float g2, float g3, float g4, out float ridgeOut)
{
    float waveSum = 0.0;
    float multiShell = 0.0;
    float ridge = 0.0;

    if (symMode <= 3)
    {
        float g[5]; g[0] = g0; g[1] = g1; g[2] = g2; g[3] = g3; g[4] = g4;
        float ph[5];
        [loop] for (int k = 0; k < 5; k++)
        {
            float ang = 6.283185307 * (float)k / 5.0;
            float2 nk = float2(cos(ang), sin(ang));
            ph[k] = dot(wpos, nk) + g[k];
            float c1 = cos(6.283185307 * ph[k]);
            waveSum += c1;
            multiShell += c1 + 0.58 * cos(12.56637061 * ph[k]);
            ridge += exp(-abs(frac(ph[k]) - 0.5) * 28.0);
        }
        [loop] for (int m = 0; m < 5; m++)
        {
            int m1 = (m + 1) % 5; int m2 = (m + 2) % 5;
            multiShell += 0.42 * cos(6.283185307 * (ph[m] + ph[m1]));
            multiShell += 0.34 * cos(6.283185307 * (ph[m] + ph[m2]));
            multiShell += 0.24 * cos(6.283185307 * (ph[m] - ph[m1]));
        }
        waveSum *= 0.20; multiShell *= 0.14;
    }
    else if (symMode == 4)
    {
        float ph4[4];
        [loop] for (int k = 0; k < 4; k++)
        {
            float ang = 3.14159265 * (float)k / 4.0;
            float2 nk = float2(cos(ang), sin(ang));
            ph4[k] = dot(wpos, nk) + 0.17 * cos(phason + ang * 2.0) + g0 * 0.6;
            float c1 = cos(6.283185307 * ph4[k]);
            waveSum += c1;
            multiShell += c1 + 0.55 * cos(12.56637061 * ph4[k]);
            ridge += exp(-abs(frac(ph4[k]) - 0.5) * 28.0);
        }
        [loop] for (int m = 0; m < 4; m++)
        {
            int m1 = (m + 1) & 3; int m2 = (m + 2) & 3;
            multiShell += 0.44 * cos(6.283185307 * (ph4[m] + ph4[m1]));
            multiShell += 0.32 * cos(6.283185307 * (ph4[m] - ph4[m1]));
            multiShell += 0.26 * cos(6.283185307 * (ph4[m] + ph4[m2]));
        }
        waveSum *= 0.25; multiShell *= 0.16;
    }
    else if (symMode == 5)
    {
        float ph6[6];
        [loop] for (int k = 0; k < 6; k++)
        {
            float ang = 3.14159265 * (float)k / 6.0;
            float2 nk = float2(cos(ang), sin(ang));
            ph6[k] = dot(wpos, nk) + 0.17 * sin(phason + ang * 2.0) + g1 * 0.6;
            float c1 = cos(6.283185307 * ph6[k]);
            waveSum += c1;
            multiShell += c1 + 0.52 * cos(12.56637061 * ph6[k]);
            ridge += exp(-abs(frac(ph6[k]) - 0.5) * 28.0);
        }
        [loop] for (int m = 0; m < 6; m++)
        {
            int m1 = (m + 1) % 6; int m2 = (m + 2) % 6;
            multiShell += 0.40 * cos(6.283185307 * (ph6[m] + ph6[m1]));
            multiShell += 0.30 * cos(6.283185307 * (ph6[m] + ph6[m2]));
            multiShell += 0.22 * cos(6.283185307 * (ph6[m] - ph6[m1]));
        }
        waveSum /= 6.0; multiShell *= 0.12;
    }
    else if (symMode == 6)
    {
        // Mode 07: 8D E8 Elser-Sloane 30-Fold Coxeter-Icosian Multi-Ring Bloch Potential!
        [loop] for (int k = 0; k < 15; k++)
        {
            float ang = 3.14159265 * (float)k / 15.0;
            float2 nk = float2(cos(ang), sin(ang));
            float pA = dot(wpos, nk) + 0.15 * cos(phason + ang * 3.0);
            float pB = dot(wpos * 1.618034, nk) - 0.15 * sin(phason + ang * 5.0);
            waveSum += cos(6.283185307 * pA);
            multiShell += 0.68 * cos(6.283185307 * pA) + 0.48 * cos(6.283185307 * pB);
            if ((k % 3) == 0) ridge += exp(-abs(frac(pA) - 0.5) * 28.0);
        }
        waveSum /= 15.0; multiShell *= 0.105;
    }
    else
    {
        // Modes 08 & 09: Einstein Hat / Chiral Spectre CAP Golden-Hexagonal Model Set (Phi^2 & Chiral Skew!)
        float chiralSkew = (symMode == 8) ? 0.261799 : 0.0; // 15-deg chiral twist for Spectre!
        [loop] for (int k = 0; k < 6; k++)
        {
            float ang = 3.14159265 * (float)k / 3.0 + chiralSkew;
            float2 nk = float2(cos(ang), sin(ang));
            float p1 = dot(wpos * 0.78, nk) + g0 * 0.5;
            float p2 = dot(wpos * 1.262, nk) + 0.21 * sin(phason + (float)k * 1.0472); // Phi-scaled CAP shell!
            float p3 = dot(wpos * 2.042, nk) - 0.17 * cos(phason + (float)k * 2.0944);
            waveSum += cos(6.283185307 * p1);
            multiShell += 0.72 * cos(6.283185307 * p1) + 0.56 * cos(6.283185307 * p2) + 0.34 * cos(6.283185307 * p3);
            ridge += exp(-abs(frac(p1) - 0.5) * 26.0);
        }
        waveSum /= 6.0; multiShell *= 0.15;
    }

    ridgeOut = saturate(ridge * 0.45);
    return multiShell + waveSum * 0.35;
}

[numthreads(16, 16, 1)]
void CSQuasiPotential(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 512 || id.y >= 512) return;
    uint idx = id.y * 512u + id.x;

    float zoom = max(2.5, U[1].x);
    float2 pan = U[8].xy;
    float2 wpos = pan + ((float2(id.x, id.y) - 256.0) / 512.0) * zoom;
    int symMode = (int)U[1].y;
    float phason = U[1].z;
    int fftSrc = (int)U[5].x;

    float ridgePhys;
    float multiPhys = EvalZeroMeanQuasiLattice(wpos, symMode, phason, U[2].x, U[2].y, U[2].z, U[2].w, U[3].x, ridgePhys);

    // If Inverse Laser Hologram mode is active, blend hologram phase ripples into the real-space view!
    if (fftSrc == 4)
    {
        float hp = HoloPhase[(id.y >> 1) * 256u + (id.x >> 1)];
        multiPhys = multiPhys * 0.55 + 0.45 * cos(hp + phason);
    }
    QuasiOut[idx] = float2(multiPhys, ridgePhys);

    if ((id.x & 1u) == 0u && (id.y & 1u) == 0u)
    {
        uint fx = id.x >> 1;
        uint fy = id.y >> 1;
        float tx = (float)fx / 255.0;
        float ty = (float)fy / 255.0;
        float2 fuv = (float2(fx, fy) - 128.0) / 128.0;
        float r2 = dot(fuv, fuv);

        int winMode = (int)U[5].w;
        float win;
        if (winMode == 0)      win = BlackmanHarris4(tx) * BlackmanHarris4(ty);
        else if (winMode == 1) win = exp(-6.8 * r2 * r2);
        else if (winMode == 2) win = (0.5 * (1.0 - cos(6.2831853 * tx))) * (0.5 * (1.0 - cos(6.2831853 * ty)));
        else                   win = (r2 < 0.65) ? 1.0 : 0.0;

        float optSpan = clamp(zoom, 18.0, 28.0);
        float2 optPos = pan + fuv * (optSpan * 0.5);
        float optRidge;
        float zeroMeanScatter = EvalZeroMeanQuasiLattice(optPos, symMode, phason, U[2].x, U[2].y, U[2].z, U[2].w, U[3].x, optRidge);

        float4 life = LeniaCur[idx];
        float bioTot = (abs(life.x) + abs(life.y) + abs(life.z)) * 0.333333;
        float audioPulse = U[9].w;

        float ampRe = 0.0, ampIm = 0.0;
        if (fftSrc == 0)
        {
            ampRe = zeroMeanScatter * (1.15 + audioPulse * 0.45) * win;
        }
        else if (fftSrc == 1)
        {
            ampRe = zeroMeanScatter * (0.95 + bioTot * 0.65) * win;
            ampIm = zeroMeanScatter * (life.x - life.z) * 0.45 * win;
        }
        else if (fftSrc == 2)
        {
            float bioAc = (life.x * 0.5 + life.y * 0.8 - life.z * 0.6);
            ampRe = bioAc * win;
            ampIm = (life.y - life.x) * 0.5 * win;
        }
        else if (fftSrc == 3)
        {
            float cym = sin(zeroMeanScatter * (3.14159 + audioPulse * 4.5));
            ampRe = zeroMeanScatter * (1.0 + 0.8 * audioPulse) * win;
            ampIm = cym * 0.35 * audioPulse * win;
        }
        else
        {
            // Mode 4: Gerchberg-Saxton Phase-Only Computer-Generated Laser Hologram!
            float hp = HoloPhase[fy * 256u + fx];
            float laserAmp = 0.95 * (1.0 + audioPulse * 0.35) * win;
            ampRe = laserAmp * cos(hp);
            ampIm = laserAmp * sin(hp);
        }

        float centerSign = (((fx + fy) & 1u) == 0u) ? 1.0 : -1.0;
        FftAperture[fy * 256u + fx] = float2(ampRe * centerSign, ampIm * centerSign);
    }
}
";
        }

        // Pass 2A: RGB Flow-Lenia & Darwinian Genome-Localized Convolution!
        private static string BuildFlowPotentialHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> LeniaIn : register(t0);
StructuredBuffer<float2> QuasiField : register(t1);
StructuredBuffer<float>  Kern33 : register(t2);
StructuredBuffer<float2> GenomeIn : register(t3);
RWStructuredBuffer<float4> FlowPotOut : register(u0);

[numthreads(16, 16, 1)]
void CSFlowPotential(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 512 || id.y >= 512) return;
    uint idx = id.y * 512u + id.x;

    int rad = clamp((int)U[3].w, 4, 16);
    float3 conv = float3(0.0, 0.0, 0.0);
    [loop] for (int dy = -rad; dy <= rad; dy++)
    {
        uint sy = (uint)((int)id.y + dy + 512) & 511u;
        uint kRow = (uint)(dy + 16) * 33u;
        [loop] for (int dx = -rad; dx <= rad; dx++)
        {
            float kw = Kern33[kRow + (uint)(dx + 16)];
            if (kw > 1e-6)
            {
                uint sx = (uint)((int)id.x + dx + 512) & 511u;
                conv += kw * LeniaIn[sy * 512u + sx].xyz;
            }
        }
    }

    int lifeMode = (int)U[8].w;
    float2 gene = GenomeIn[idx];
    float muBase = (lifeMode == 1 && gene.x > 0.04) ? gene.x : U[3].y;
    float sig    = (lifeMode == 1 && gene.y > 0.005) ? gene.y : max(0.006, U[3].z);
    float coupling = U[4].y;
    float cross = U[8].z;
    float audioMid = U[9].y;
    float2 q = QuasiField[idx];

    float potR = conv.x * 0.84 + conv.y * (0.42 * cross) - conv.z * (0.18 * cross);
    float potG = conv.y * 0.84 + conv.z * (0.42 * cross) - conv.x * (0.18 * cross);
    float potB = conv.z * 0.84 + conv.x * (0.42 * cross) - conv.y * (0.18 * cross);

    float muR = muBase - coupling * q.x * 0.055 - audioMid * 0.015;
    float muG = muBase + coupling * q.x * 0.055;
    float muB = muBase - coupling * (q.y - 0.35) * 0.065;

    float dR = (potR - muR) / sig;
    float dG = (potG - muG) / sig;
    float dB = (potB - muB) / sig;

    float affR = 2.0 * exp(-0.5 * dR * dR) - 1.0 + coupling * (-q.x) * 0.22;
    float affG = 2.0 * exp(-0.5 * dG * dG) - 1.0 + coupling * ( q.x) * 0.22;
    float affB = 2.0 * exp(-0.5 * dB * dB) - 1.0 + coupling * (q.y - 0.3) * 0.28;

    float4 cur = LeniaIn[idx];
    FlowPotOut[idx] = float4(affR, affG, affB, cur.x + cur.y + cur.z);
}
";
        }

        // Pass 2B: Exact 9-Tap Mass-Conserving Reintegration + Heritable Darwinian Genome Advection & Mutation!
        private static string BuildFlowReintegrateHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> LeniaIn : register(t0);
StructuredBuffer<float4> FlowPot : register(t1);
StructuredBuffer<float2> GenomeIn : register(t2);
RWStructuredBuffer<float4> LeniaOut : register(u0);
RWStructuredBuffer<float2> GenomeOut : register(u1);

uint HashPcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float4 SamplePot(int x, int y)
{
    uint ux = (uint)(x + 512) & 511u;
    uint uy = (uint)(y + 512) & 511u;
    return FlowPot[uy * 512u + ux];
}

void ComputeCellVelocities(int sx, int sy, float speedScale, out float2 vR, out float2 vG, out float2 vB)
{
    float4 pxp = SamplePot(sx + 1, sy);
    float4 pxm = SamplePot(sx - 1, sy);
    float4 pyp = SamplePot(sx, sy + 1);
    float4 pym = SamplePot(sx, sy - 1);

    float3 gx = (pxp.xyz - pxm.xyz) * 0.5;
    float3 gy = (pyp.xyz - pym.xyz) * 0.5;

    float pxCrowd = max(0.0, pxp.w - 0.95) * max(0.0, pxp.w - 0.95) - max(0.0, pxm.w - 0.95) * max(0.0, pxm.w - 0.95);
    float pyCrowd = max(0.0, pyp.w - 0.95) * max(0.0, pyp.w - 0.95) - max(0.0, pym.w - 0.95) * max(0.0, pym.w - 0.95);

    vR = clamp(float2(gx.x - pxCrowd * 0.65, gy.x - pyCrowd * 0.65) * speedScale, -0.78, 0.78);
    vG = clamp(float2(gx.y - pxCrowd * 0.65, gy.y - pyCrowd * 0.65) * speedScale, -0.78, 0.78);
    vB = clamp(float2(gx.z - pxCrowd * 0.65, gy.z - pyCrowd * 0.65) * speedScale, -0.78, 0.78);
}

float BoxOverlap1D(float centerRel)
{
    float lo = max(-0.5, centerRel - 0.55);
    float hi = min( 0.5, centerRel + 0.55);
    return max(0.0, hi - lo) * (1.0 / 1.10);
}

[numthreads(16, 16, 1)]
void CSFlowReintegrate(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 512 || id.y >= 512) return;
    uint idx = id.y * 512u + id.x;

    float4 prev = LeniaIn[idx];
    float2 nextGene = GenomeIn[idx];
    bool paused = (U[4].w > 0.5);
    int lifeMode = (int)U[8].w; // 0=Flow, 1=Darwinian Flow, 3=Classic Scalar
    float mutRate = U[12].x;
    uint frameSeed = (uint)U[12].y;
    float3 nextRGB = prev.xyz;

    if (!paused)
    {
        if (lifeMode == 0 || lifeMode == 1)
        {
            float speedScale = clamp(U[4].x * 4.2 * (1.0 + U[9].y * 0.65), 0.15, 1.85);
            float3 acc = float3(0.0, 0.0, 0.0);
            float2 geneWeighted = float2(0.0, 0.0);
            float totalFlux = 0.0;

            [loop] for (int dy = -1; dy <= 1; dy++)
            {
                int sy = ((int)id.y + dy + 512) & 511;
                [loop] for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = ((int)id.x + dx + 512) & 511;
                    uint sIdx = (uint)sy * 512u + (uint)sx;
                    float3 srcM = LeniaIn[sIdx].xyz;
                    if (srcM.x + srcM.y + srcM.z > 1e-7)
                    {
                        float2 vR, vG, vB;
                        ComputeCellVelocities(sx, sy, speedScale, vR, vG, vB);

                        float mR = srcM.x * BoxOverlap1D((float)dx + vR.x) * BoxOverlap1D((float)dy + vR.y);
                        float mG = srcM.y * BoxOverlap1D((float)dx + vG.x) * BoxOverlap1D((float)dy + vG.y);
                        float mB = srcM.z * BoxOverlap1D((float)dx + vB.x) * BoxOverlap1D((float)dy + vB.y);
                        acc += float3(mR, mG, mB);

                        float flux = mR + mG + mB;
                        geneWeighted += GenomeIn[sIdx] * flux;
                        totalFlux += flux;
                    }
                }
            }
            nextRGB = max(float3(0.0, 0.0, 0.0), acc);

            if (lifeMode == 1 && totalFlux > 1e-5)
            {
                nextGene = geneWeighted / totalFlux;
                if (mutRate > 0.001 && (frameSeed & 15u) == 0u)
                {
                    uint h1 = HashPcg(idx ^ (frameSeed * 1664525u));
                    uint h2 = HashPcg(h1 ^ 0x9E3779B9u);
                    float r1 = ((float)(h1 & 65535u) / 32767.5) - 1.0;
                    float r2 = ((float)(h2 & 65535u) / 32767.5) - 1.0;
                    nextGene.x = clamp(nextGene.x + r1 * mutRate * 0.0065, 0.09, 0.36);
                    nextGene.y = clamp(nextGene.y + r2 * mutRate * 0.0018, 0.012, 0.085);
                }
            }
        }
        else
        {
            float4 aff = FlowPot[idx];
            nextRGB = saturate(prev.xyz + U[4].x * aff.xyz);
        }
    }

    int bMode = (int)U[6].z;
    if (bMode > 0)
    {
        float2 bPos = U[6].xy * 512.0;
        float bRad = max(5.0, U[6].w);
        float d = length(float2(id.x, id.y) - bPos);
        if (d < bRad)
        {
            if (bMode == 2) nextRGB = float3(0.0, 0.0, 0.0);
            else
            {
                float ring = exp(-pow((d / bRad) - 0.45, 2.0) / 0.12) * 0.88;
                if (bMode == 1)      nextRGB = max(nextRGB, float3(ring, ring * 0.85, ring * 0.72));
                else if (bMode == 3) nextRGB.x = max(nextRGB.x, ring);
                else if (bMode == 4) nextRGB.y = max(nextRGB.y, ring);
                else if (bMode == 5) nextRGB.z = max(nextRGB.z, ring);
                nextGene = float2(U[3].y, U[3].z);
            }
        }
    }

    float peak = max(nextRGB.x, max(nextRGB.y, nextRGB.z));
    LeniaOut[idx] = float4(nextRGB, max(min(1.2, peak), prev.w * 0.972));
    GenomeOut[idx] = nextGene;
}
";
        }

        // Pass 2Q: Unitary Symplectic Leapfrog Kohmoto-Sutherland Quantum Tight-Binding Propagator!
        private static string BuildQuantumStepHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> PsiIn : register(t0);
StructuredBuffer<float2> QuasiField : register(t1);
RWStructuredBuffer<float4> PsiOut : register(u0);

float4 FetchPsi(int x, int y)
{
    uint ux = (uint)(x + 512) & 511u;
    uint uy = (uint)(y + 512) & 511u;
    return PsiIn[uy * 512u + ux];
}

[numthreads(16, 16, 1)]
void CSQuantumStep(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 512 || id.y >= 512) return;
    uint idx = id.y * 512u + id.x;

    float4 c = PsiIn[idx];
    bool paused = (U[4].w > 0.5);
    int parity = (int)U[12].z; // 0 = update Re(psi), 1 = update Im(psi)
    float dtQ = 0.14;          // Strictly inside 2D leapfrog stability bound (< 0.5)!
    float vPot = QuasiField[idx].x * (U[4].y * 1.65);

    float re = c.x, im = c.y;
    if (!paused)
    {
        int x = (int)id.x, y = (int)id.y;
        if (parity == 0)
        {
            float lapIm = FetchPsi(x + 1, y).y + FetchPsi(x - 1, y).y + FetchPsi(x, y + 1).y + FetchPsi(x, y - 1).y - 4.0 * im;
            re = re + dtQ * (-0.22 * lapIm + vPot * im);
        }
        else
        {
            float lapRe = FetchPsi(x + 1, y).x + FetchPsi(x - 1, y).x + FetchPsi(x, y + 1).x + FetchPsi(x, y - 1).x - 4.0 * re;
            im = im - dtQ * (-0.22 * lapRe + vPot * re);
        }
    }

    // Interactive Click -> Launch Coherent 5-Fold Quantum Wavepacket!
    int bMode = (int)U[6].z;
    if (bMode > 0 && parity == 0)
    {
        float2 bPos = U[6].xy * 512.0;
        float2 d = float2(id.x, id.y) - bPos;
        float r2 = dot(d, d);
        if (r2 < 520.0)
        {
            if (bMode == 2) { re = 0.0; im = 0.0; }
            else
            {
                float env = exp(-r2 / 145.0) * 0.92;
                float ang = atan2(d.y, d.x + 1e-5) * 5.0; // 5-fold angular momentum m=5 ring state!
                re += env * cos(ang);
                im += env * sin(ang);
            }
        }
    }

    float prob = re * re + im * im;
    float envMax = max(min(1.5, sqrt(prob)), c.w * 0.985);
    PsiOut[idx] = float4(re, im, prob, envMax);
}
";
        }

        private static string BuildFftRows256Hlsl()
        {
            return @"
StructuredBuffer<float2> FftIn : register(t0);
RWStructuredBuffer<float2> FftRowOut : register(u0);
groupshared float2 sRow[256];

uint BitRev8(uint x)
{
    x = ((x & 0x55u) << 1) | ((x >> 1) & 0x55u);
    x = ((x & 0x33u) << 2) | ((x >> 2) & 0x33u);
    x = ((x & 0x0Fu) << 4) | ((x >> 4) & 0x0Fu);
    return x;
}

[numthreads(128, 1, 1)]
void CSFftRows256(uint3 gid : SV_GroupID, uint3 tid : SV_GroupThreadID)
{
    uint row = gid.x, t = tid.x, i0 = t, i1 = t + 128u;
    sRow[BitRev8(i0)] = FftIn[row * 256u + i0];
    sRow[BitRev8(i1)] = FftIn[row * 256u + i1];
    GroupMemoryBarrierWithGroupSync();

    [loop] for (uint s = 1u; s <= 8u; s++)
    {
        uint m = 1u << s, halfM = m >> 1;
        uint k = (t / halfM) * m, j = t & (halfM - 1u);
        float ang = -6.283185307 * (float)j / (float)m;
        float cw = cos(ang), sw = sin(ang);
        float2 u = sRow[k + j], v = sRow[k + j + halfM];
        float2 tv = float2(v.x * cw - v.y * sw, v.x * sw + v.y * cw);
        sRow[k + j] = u + tv;
        sRow[k + j + halfM] = u - tv;
        GroupMemoryBarrierWithGroupSync();
    }
    FftRowOut[row * 256u + i0] = sRow[i0];
    FftRowOut[row * 256u + i1] = sRow[i1];
}
";
        }

        private static string BuildFftCols256Hlsl()
        {
            return @"
StructuredBuffer<float2> FftRowIn : register(t0);
RWStructuredBuffer<float2> FftBraggOut : register(u0);
groupshared float2 sCol[256];

uint BitRev8(uint x)
{
    x = ((x & 0x55u) << 1) | ((x >> 1) & 0x55u);
    x = ((x & 0x33u) << 2) | ((x >> 2) & 0x33u);
    x = ((x & 0x0Fu) << 4) | ((x >> 4) & 0x0Fu);
    return x;
}

[numthreads(128, 1, 1)]
void CSFftCols256(uint3 gid : SV_GroupID, uint3 tid : SV_GroupThreadID)
{
    uint col = gid.x, t = tid.x, i0 = t, i1 = t + 128u;
    sCol[BitRev8(i0)] = FftRowIn[i0 * 256u + col];
    sCol[BitRev8(i1)] = FftRowIn[i1 * 256u + col];
    GroupMemoryBarrierWithGroupSync();

    [loop] for (uint s = 1u; s <= 8u; s++)
    {
        uint m = 1u << s, halfM = m >> 1;
        uint k = (t / halfM) * m, j = t & (halfM - 1u);
        float ang = -6.283185307 * (float)j / (float)m;
        float cw = cos(ang), sw = sin(ang);
        float2 u = sCol[k + j], v = sCol[k + j + halfM];
        float2 tv = float2(v.x * cw - v.y * sw, v.x * sw + v.y * cw);
        sCol[k + j] = u + tv;
        sCol[k + j + halfM] = u - tv;
        GroupMemoryBarrierWithGroupSync();
    }
    FftBraggOut[i0 * 256u + col] = sCol[i0] / 256.0;
    FftBraggOut[i1 * 256u + col] = sCol[i1] / 256.0;
}
";
        }

        // Pass 4: Stage Viewport + ZBuf3D Clear + Quantum Rainbow Phase + Standing 5D Miller Planes + Peak Reticle!
        private static string BuildStageRenderHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> LeniaCur : register(t0);
StructuredBuffer<float2> QuasiField : register(t1);
StructuredBuffer<float2> BraggFft : register(t2);
StructuredBuffer<float2> GenomeCur : register(t3);
RWStructuredBuffer<uint> BasePix : register(u0);
RWStructuredBuffer<uint> LineRGB : register(u1);
RWStructuredBuffer<uint> ZBuf3D  : register(u2);

float3 HsvToRgb(float h, float s, float v)
{
    float3 k = frac(float3(h, h + 0.666667, h + 0.333333)) * 6.0 - 3.0;
    float3 p = saturate(abs(k) - 1.0);
    return v * lerp(float3(1.0, 1.0, 1.0), p, s);
}

float3 LaserPalette(float v, int laserMode)
{
    v = saturate(v);
    float core = pow(v, 3.2);
    if (laserMode == 0) return float3(core * 0.75 + pow(v, 2.0) * 0.18, pow(v, 1.15) * 1.15, core * 0.65 + pow(v, 1.7) * 0.42);
    if (laserMode == 1) return float3(pow(v, 1.12) * 1.18, core * 0.72 + pow(v, 2.2) * 0.22, core * 0.55 + pow(v, 2.6) * 0.16);
    if (laserMode == 2) return float3(pow(v, 1.45) * 0.85 + core * 0.4, core * 0.65 + pow(v, 2.2) * 0.28, pow(v, 1.08) * 1.22);
    return float3(pow(v, 1.20), pow(v, 1.08) * 0.98, pow(v, 0.95) * 1.08);
}

float3 ThemeColorField(float2 uv, float qv, float amm, float4 lenia, float2 gene, int theme, int lifeMode, bool showAmmann)
{
    float3 bg, spR, spG, spB, ridge;
    if (theme == 0)
    {
        bg = float3(0.02, 0.04, 0.09) + float3(0.03, 0.07, 0.13) * saturate(qv * 0.5 + 0.5);
        spR = float3(1.00, 0.42, 0.18); spG = float3(0.10, 0.95, 0.92); spB = float3(0.92, 0.30, 0.96);
        ridge = float3(0.28, 0.48, 0.82);
    }
    else if (theme == 1)
    {
        bg = float3(0.05, 0.04, 0.02) + float3(0.10, 0.07, 0.03) * saturate(qv * 0.5 + 0.5);
        spR = float3(1.00, 0.78, 0.18); spG = float3(0.18, 0.94, 0.72); spB = float3(0.96, 0.36, 0.58);
        ridge = float3(0.85, 0.65, 0.22);
    }
    else if (theme == 2)
    {
        bg = float3(0.015, 0.055, 0.045) + float3(0.025, 0.10, 0.075) * saturate(qv * 0.5 + 0.5);
        spR = float3(0.98, 0.68, 0.20); spG = float3(0.20, 0.98, 0.58); spB = float3(0.32, 0.72, 1.00);
        ridge = float3(0.18, 0.70, 0.50);
    }
    else
    {
        bg = float3(0.025, 0.02, 0.045) + float3(0.08, 0.04, 0.11) * saturate(qv * 0.5 + 0.5);
        spR = float3(1.00, 0.36, 0.22); spG = float3(0.28, 0.88, 1.00); spB = float3(0.95, 0.22, 0.75);
        ridge = float3(0.65, 0.32, 0.85);
    }

    float3 col = bg;
    if (showAmmann) col += ridge * amm * 0.26;

    if (lifeMode == 2)
    {
        // Kohmoto-Sutherland Quantum Wavefunction: |psi|^2 luminance + Phase Rainbow!
        float amp = saturate(sqrt(max(0.0, lenia.z)) * 1.45);
        float phase01 = atan2(lenia.y, lenia.x + 1e-6) / 6.2831853 + 0.5;
        col += HsvToRgb(phase01, 0.78, 1.0) * pow(amp, 0.85) * 1.35;
        col += float3(0.20, 0.55, 0.95) * saturate(lenia.w) * 0.15;
    }
    else
    {
        float3 rgb = max(float3(0.0, 0.0, 0.0), lenia.xyz);
        if (lifeMode == 1)
        {
            // Darwinian mutant hue shift shows speciation across niches!
            float mutShift = clamp((gene.x - U[3].y) * 6.0, -0.45, 0.45);
            spG = saturate(spG + float3(-mutShift * 0.5, 0.0, mutShift * 0.8));
        }
        col += (spG * 0.4 + spB * 0.4) * saturate(lenia.w) * 0.14;
        col += spR * pow(saturate(rgb.x * 0.92), 0.80) * 1.18;
        col += spG * pow(saturate(rgb.y * 0.92), 0.80) * 1.18;
        col += spB * pow(saturate(rgb.z * 0.92), 0.80) * 1.18;
    }

    // Optional Standing 5D Miller Bragg Wave Plane Overlay (when user clicks a Bragg spot!)
    if (U[13].z > 1.5)
    {
        float2 wpos = U[8].xy + (uv - 0.5) * max(2.5, U[1].x);
        float braggPlane = cos(6.2831853 * dot(wpos, U[14].xy));
        float crest = pow(saturate(braggPlane), 6.0);
        col += float3(0.98, 0.86, 0.30) * crest * 0.38;
    }
    return col;
}

float3 EvalBraggPixel(float2 buv, int laserMode, float gainDb, float floorGate, bool showPhiRings, int fftSrc)
{
    float zoomScale = (fftSrc == 4) ? 0.92 : 0.72; // Full optical frame for Holograms!
    float2 centered = (buv - 0.5) * zoomScale + 0.5;
    float2 fc = clamp(centered * 255.0, 0.0, 254.0);
    uint x0 = (uint)fc.x, y0 = (uint)fc.y;
    float2 f = frac(fc);

    float2 c00 = BraggFft[y0 * 256u + x0];
    float2 c10 = BraggFft[y0 * 256u + x0 + 1u];
    float2 c01 = BraggFft[(y0 + 1u) * 256u + x0];
    float2 c11 = BraggFft[(y0 + 1u) * 256u + x0 + 1u];

    float mag = lerp(lerp(length(c00), length(c10), f.x), lerp(length(c01), length(c11), f.x), f.y);
    float2 dCenter = buv - 0.5;
    float rCenter = length(dCenter);
    float dcMask = 1.0 - 0.94 * exp(-rCenter * rCenter * 2400.0);

    float effFloor = (fftSrc == 4) ? (floorGate * 0.85) : floorGate;
    float rawNorm = log(1.0 + mag * dcMask * gainDb) / log(1.0 + gainDb * 0.45);
    float gated = saturate((rawNorm - effFloor) / max(0.05, 1.0 - effFloor));
    float3 col = float3(0.008, 0.012, 0.024) + LaserPalette(gated, laserMode) * 1.35;

    if (showPhiRings && fftSrc != 4)
    {
        float rBase = 0.228;
        float g0 = exp(-abs(rCenter - rBase / 2.618034) * 240.0);
        float g1 = exp(-abs(rCenter - rBase / 1.618034) * 240.0);
        float g2 = exp(-abs(rCenter - rBase) * 240.0);
        float g3 = exp(-abs(rCenter - rBase * 1.618034) * 240.0);
        col += float3(0.95, 0.80, 0.25) * (g0 * 0.6 + g1 + g2 + g3) * 0.22;
    }

    // Selected 5D Miller Peak Reticle!
    if (U[13].z > 0.5)
    {
        float dRet = length(buv - U[13].xy);
        float ring = exp(-abs(dRet - 0.024) * 320.0);
        col += float3(0.25, 0.95, 1.00) * ring * 0.95;
    }
    return col;
}

float3 EvalAcceptanceBackdrop(float2 uvR, int symMode)
{
    float2 d = (uvR - 0.5) * 3.1;
    float r = length(d);
    float th = atan2(d.y, d.x + 1e-6);
    int gon = (symMode == 4) ? 8 : ((symMode == 5 || symMode >= 7) ? 12 : (symMode == 6 ? 30 : 10));
    float sec = 3.14159265 / (float)gon;
    float poly = cos(sec) / max(0.25, cos(fmod(abs(th) + sec, 2.0 * sec) - sec));

    float rOuter = 1.24 * poly;
    float rInner = 0.96 * poly;
    return float3(0.012, 0.018, 0.038)
        + float3(0.03, 0.09, 0.16) * saturate((rOuter - r) * 24.0)
        + float3(0.16, 0.88, 0.98) * exp(-abs(r - rOuter) * 38.0) * 0.95
        + float3(0.96, 0.42, 0.82) * exp(-abs(r - rInner) * 38.0) * 0.85
        + float3(0.98, 0.82, 0.28) * exp(-abs(r - rInner / 1.618034) * 38.0) * 0.45;
}

[numthreads(16, 16, 1)]
void CSStageRender(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (id.x >= W || id.y >= H) return;
    uint pIdx = id.y * W + id.x;
    LineRGB[pIdx * 3u + 0u] = 0u;
    LineRGB[pIdx * 3u + 1u] = 0u;
    LineRGB[pIdx * 3u + 2u] = 0u;
    ZBuf3D[pIdx] = 0u;

    int vmode = (int)U[0].w, symMode = (int)U[1].y, theme = (int)U[4].z, lifeMode = (int)U[8].w;
    bool showAmmann = (U[1].w > 0.5);
    int fftSrc = (int)U[5].x, laserMode = (int)U[5].y;
    float gainDb = max(10.0, U[5].z), floorGate = clamp(U[10].x, 0.0, 0.65);
    bool showRings = (U[7].x > 0.5);

    float2 p = float2(id.x, id.y);
    float3 col = float3(0.02, 0.028, 0.052);

    if (vmode == 1)
    {
        float side = min((float)W, (float)H) * 0.95;
        float2 tl = float2(((float)W - side) * 0.5, ((float)H - side) * 0.5);
        float2 uv = (p - tl) / side;
        if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0)
        {
            uint2 gc = min(uint2(uv * 512.0), uint2(511u, 511u));
            uint gIdx = gc.y * 512u + gc.x;
            float2 q = QuasiField[gIdx];
            col = ThemeColorField(uv, q.x, q.y, LeniaCur[gIdx], GenomeCur[gIdx], theme, lifeMode, showAmmann);
        }
    }
    else if (vmode == 2)
    {
        float side = min((float)W, (float)H) * 0.95;
        float2 tl = float2(((float)W - side) * 0.5, ((float)H - side) * 0.5);
        float2 uv = (p - tl) / side;
        if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0)
            col = EvalBraggPixel(uv, laserMode, gainDb, floorGate, showRings, fftSrc);
    }
    else if (vmode == 4)
    {
        float2 cuv = (p - float2(W, H) * 0.5) / max(1.0, min((float)W, (float)H));
        col = float3(0.012, 0.018, 0.036) + float3(0.04, 0.10, 0.20) * exp(-dot(cuv, cuv) * 5.5);
    }
    else
    {
        float side = min((float)W * 0.475, (float)H * 0.93);
        float2 tlL = float2(18.0, ((float)H - side) * 0.5);
        float2 uvL = (p - tlL) / side;
        float2 tlR = float2((float)W - side - 18.0, ((float)H - side) * 0.5);
        float2 uvR = (p - tlR) / side;

        if (uvL.x >= 0.0 && uvL.x <= 1.0 && uvL.y >= 0.0 && uvL.y <= 1.0)
        {
            uint2 gc = min(uint2(uvL * 512.0), uint2(511u, 511u));
            uint gIdx = gc.y * 512u + gc.x;
            float2 q = QuasiField[gIdx];
            col = ThemeColorField(uvL, q.x, q.y, LeniaCur[gIdx], GenomeCur[gIdx], theme, lifeMode, showAmmann);
        }
        else if (uvR.x >= 0.0 && uvR.x <= 1.0 && uvR.y >= 0.0 && uvR.y <= 1.0)
        {
            if (vmode == 0)      col = EvalBraggPixel(uvR, laserMode, gainDb, floorGate, showRings, fftSrc);
            else if (vmode == 3) col = EvalAcceptanceBackdrop(uvR, symMode);
            else                 col = float3(0.012, 0.018, 0.038) + float3(0.05, 0.12, 0.22) * exp(-dot(uvR - 0.5, uvR - 0.5) * 6.5);
        }
    }

    uint3 c8 = uint3(clamp(col * 255.0, 0.0, 255.0));
    BasePix[pIdx] = 0xFF000000u | (c8.x << 16) | (c8.y << 8) | c8.z;
}
";
        }

        // Pass 5A: Seamless Hat/Spectre EdgeMask + Jewel Facet Shading + A* Waveguide + 5D/8D Sparkles!
        private static string BuildRhombWireframeHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> Rhombs : register(t0);
RWStructuredBuffer<uint> LineRGB : register(u0);

void PlotAdd(uint W, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= W || (uint)y >= H) return;
    uint b = ((uint)y * W + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

void DrawSeg(uint W, uint H, float2 a, float2 b, int minX, int maxX, int minY, int maxY, uint cr, uint cg, uint cb)
{
    int x0 = (int)round(a.x), y0 = (int)round(a.y);
    int x1 = (int)round(b.x), y1 = (int)round(b.y);
    if ((x0 < minX && x1 < minX) || (x0 > maxX && x1 > maxX) || (y0 < minY && y1 < minY) || (y0 > maxY && y1 > maxY)) return;
    int dx = abs(x1 - x0), dy = -abs(y1 - y0);
    if (dx > 960 || dy < -960) return;
    int sx = (x0 < x1) ? 1 : -1, sy = (y0 < y1) ? 1 : -1, err = dx + dy;
    [loop] for (int st = 0; st < 512; st++)
    {
        if (x0 >= minX && x0 <= maxX && y0 >= minY && y0 <= maxY)
            PlotAdd(W, H, x0, y0, cr, cg, cb);
        if (x0 == x1 && y0 == y1) break;
        int e2 = err * 2;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
    }
}

bool InsideSeamlessQuad(float2 p, float2 v0, float2 v1, float2 v2, float2 v3)
{
    float c0 = (v1.x - v0.x) * (p.y - v0.y) - (v1.y - v0.y) * (p.x - v0.x);
    float c1 = (v2.x - v1.x) * (p.y - v1.y) - (v2.y - v1.y) * (p.x - v1.x);
    float c2 = (v3.x - v2.x) * (p.y - v2.y) - (v3.y - v2.y) * (p.x - v2.x);
    float c3 = (v0.x - v3.x) * (p.y - v3.y) - (v0.y - v3.y) * (p.x - v3.x);
    return (c0 >= 0.0 && c1 >= 0.0 && c2 >= 0.0 && c3 >= 0.0) ||
           (c0 <= 0.0 && c1 <= 0.0 && c2 <= 0.0 && c3 <= 0.0);
}

[numthreads(64, 1, 1)]
void CSRhombWireframe(uint3 id : SV_DispatchThreadID)
{
    uint rCount = (uint)U[7].y;
    int vmode = (int)U[0].w;
    if (vmode == 2 || vmode == 4 || id.x >= rCount) return;

    bool showEdges = (U[7].z > 0.5);
    bool showWorms = (U[7].w > 0.5);
    bool showJewel = (U[10].y > 0.5);
    bool showPath  = (U[10].z > 0.5);

    uint W = (uint)U[0].x, H = (uint)U[0].y;
    float zoom = max(2.5, U[1].x);
    float2 pan = U[8].xy;

    float side; float2 tl;
    if (vmode == 1) { side = min((float)W, (float)H) * 0.95; tl = float2(((float)W - side) * 0.5, ((float)H - side) * 0.5); }
    else            { side = min((float)W * 0.475, (float)H * 0.93); tl = float2(18.0, ((float)H - side) * 0.5); }

    float2 centerPx = tl + side * 0.5;
    float pxPerUnit = side / zoom;

    uint baseIdx = id.x * 4u;
    float4 p01  = Rhombs[baseIdx + 0u];
    float4 p23  = Rhombs[baseIdx + 1u];
    float4 meta = Rhombs[baseIdx + 2u];
    float4 ext  = Rhombs[baseIdx + 3u];

    float2 v0 = centerPx + (p01.xy - pan) * pxPerUnit;
    float2 v1 = centerPx + (p01.zw - pan) * pxPerUnit;
    float2 v2 = centerPx + (p23.xy - pan) * pxPerUnit;
    float2 v3 = centerPx + (p23.zw - pan) * pxPerUnit;

    int minX = (int)tl.x, maxX = (int)(tl.x + side) - 1;
    int minY = (int)tl.y, maxY = (int)(tl.y + side) - 1;

    int packedWormMask = (int)round(meta.y);
    int wormFam = packedWormMask & 15;
    int edgeMask = (packedWormMask >> 4) & 15;

    bool thick = (meta.x > 0.5);
    int kSheet = (int)round(meta.z);
    float flipE = meta.w;
    bool isPath = showPath && (ext.w > 0.5);
    bool isPureLineSeg = (edgeMask == 1) && (distance(v2, v1) < 0.01) && (distance(v3, v0) < 0.01);

    int bx0 = max(minX, (int)floor(min(min(v0.x, v1.x), min(v2.x, v3.x))));
    int bx1 = min(maxX, (int)ceil(max(max(v0.x, v1.x), max(v2.x, v3.x))));
    int by0 = max(minY, (int)floor(min(min(v0.y, v1.y), min(v2.y, v3.y))));
    int by1 = min(maxY, (int)ceil(max(max(v0.y, v1.y), max(v2.y, v3.y))));
    int spanW = bx1 - bx0, spanH = by1 - by0;

    if (!isPureLineSeg && (showJewel || isPath || (flipE > 0.72 && zoom <= 18.0)) && spanW >= 3 && spanW <= 165 && spanH >= 3 && spanH <= 165)
    {
        uint fr = 8u, fg = 18u, fb = 34u;
        if (kSheet == 1)      { fr = 12u; fg = 36u; fb = 68u; }
        else if (kSheet == 2) { fr = 10u; fg = 52u; fb = 54u; }
        else if (kSheet == 3) { fr = 46u; fg = 18u; fb = 56u; }
        else                  { fr = 58u; fg = 40u; fb = 14u; }

        if (!thick) { fr = (fr * 3u) / 4u + 16u; fb += 12u; }
        if (flipE > 0.68)
        {
            uint boost = (uint)((flipE - 0.65) * 145.0);
            fr += boost; fg += (boost * 3u) / 4u;
        }
        if (isPath) { fr = 28u; fg = 92u; fb = 48u; }

        [loop] for (int dy = 0; dy < 166; dy++)
        {
            if (dy > spanH) break;
            int py = by0 + dy;
            [loop] for (int dx = 0; dx < 166; dx++)
            {
                if (dx > spanW) break;
                int px = bx0 + dx;
                // Deterministic irrational sub-pixel nudge guarantees zero double-plotting on shared Hat/Spectre diagonals!
                if (InsideSeamlessQuad(float2((float)px + 0.5013, (float)py + 0.4987), v0, v1, v2, v3))
                    PlotAdd(W, H, px, py, fr, fg, fb);
            }
        }
    }

    if (showEdges && edgeMask > 0)
    {
        uint cr = thick ? 46u : 102u;
        uint cg = thick ? 112u : 56u;
        uint cb = thick ? 142u : 124u;
        if (flipE > 0.68) { cr = 225u; cg = 170u; cb = 48u; }
        if (isPath)       { cr = 65u;  cg = 235u; cb = 125u; }
        if (isPureLineSeg) { cr = 65u; cg = 52u; cb = 22u; }

        if ((edgeMask & 1) != 0) DrawSeg(W, H, v0, v1, minX, maxX, minY, maxY, cr, cg, cb);
        if ((edgeMask & 2) != 0) DrawSeg(W, H, v1, v2, minX, maxX, minY, maxY, cr, cg, cb);
        if ((edgeMask & 4) != 0) DrawSeg(W, H, v2, v3, minX, maxX, minY, maxY, cr, cg, cb);
        if ((edgeMask & 8) != 0) DrawSeg(W, H, v3, v0, minX, maxX, minY, maxY, cr, cg, cb);
    }

    if (showWorms && edgeMask == 15 && (wormFam == 0 || wormFam == 2))
    {
        DrawSeg(W, H, 0.5 * (v0 + v1), 0.5 * (v2 + v3), minX, maxX, minY, maxY, 155u, 115u, 28u);
    }

    if (vmode == 3)
    {
        float2 tlR = float2((float)W - side - 18.0, ((float)H - side) * 0.5);
        float2 ctrR = tlR + side * 0.5;
        float2 pPx = ctrR + (ext.xy / 3.1) * side;
        int rx = (int)round(pPx.x), ry = (int)round(pPx.y);
        int rMinX = (int)tlR.x, rMaxX = (int)(tlR.x + side) - 1;
        int rMinY = (int)tlR.y, rMaxY = (int)(tlR.y + side) - 1;

        if (rx >= rMinX && rx <= rMaxX && ry >= rMinY && ry <= rMaxY)
        {
            if (flipE > 0.68)
            {
                [loop] for (int oy = -1; oy <= 1; oy++)
                [loop] for (int ox = -1; ox <= 1; ox++)
                {
                    uint wgt = (ox == 0 && oy == 0) ? 235u : 110u;
                    PlotAdd(W, H, rx + ox, ry + oy, wgt, (wgt * 4u) / 5u, 35u);
                }
            }
            else
            {
                uint sr = (kSheet == 1) ? 45u  : ((kSheet == 2) ? 35u  : ((kSheet == 3) ? 165u : 185u));
                uint sg = (kSheet == 1) ? 165u : ((kSheet == 2) ? 195u : ((kSheet == 3) ? 75u  : 145u));
                uint sb = (kSheet == 1) ? 215u : ((kSheet == 2) ? 155u : ((kSheet == 3) ? 195u : 45u));
                PlotAdd(W, H, rx, ry, sr, sg, sb);
            }
        }
    }
}
";
        }

        // Pass 5B: True Hardware Atomic Z-Buffered Solid Specular Jewel 3D + X-Ray Quartz Mode!
        private static string BuildCrystal3dHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<float4> Quads3D : register(t0);
RWStructuredBuffer<uint> LineRGB : register(u0);
RWStructuredBuffer<uint> ZBuf3D  : register(u1);

void PlotAdd3D(uint W, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= W || (uint)y >= H) return;
    uint b = ((uint)y * W + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

void DrawEdge3D(uint W, uint H, float2 a, float2 b, int minX, int maxX, int minY, int maxY, uint cr, uint cg, uint cb)
{
    int x0 = (int)round(a.x), y0 = (int)round(a.y);
    int x1 = (int)round(b.x), y1 = (int)round(b.y);
    if ((x0 < minX && x1 < minX) || (x0 > maxX && x1 > maxX) || (y0 < minY && y1 < minY) || (y0 > maxY && y1 > maxY)) return;
    int dx = abs(x1 - x0), dy = -abs(y1 - y0);
    if (dx > 960 || dy < -960) return;
    int sx = (x0 < x1) ? 1 : -1, sy = (y0 < y1) ? 1 : -1, err = dx + dy;
    [loop] for (int st = 0; st < 512; st++)
    {
        if (x0 >= minX && x0 <= maxX && y0 >= minY && y0 <= maxY)
            PlotAdd3D(W, H, x0, y0, cr, cg, cb);
        if (x0 == x1 && y0 == y1) break;
        int e2 = err * 2;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
    }
}

bool InQuad(float2 p, float2 v0, float2 v1, float2 v2, float2 v3)
{
    float c0 = (v1.x - v0.x) * (p.y - v0.y) - (v1.y - v0.y) * (p.x - v0.x);
    float c1 = (v2.x - v1.x) * (p.y - v1.y) - (v2.y - v1.y) * (p.x - v1.x);
    float c2 = (v3.x - v2.x) * (p.y - v2.y) - (v3.y - v2.y) * (p.x - v2.x);
    float c3 = (v0.x - v3.x) * (p.y - v3.y) - (v0.y - v3.y) * (p.x - v3.x);
    return (c0 >= -0.25 && c1 >= -0.25 && c2 >= -0.25 && c3 >= -0.25) ||
           (c0 <=  0.25 && c1 <=  0.25 && c2 <=  0.25 && c3 <=  0.25);
}

[numthreads(64, 1, 1)]
void CSCrystal3D(uint3 id : SV_DispatchThreadID)
{
    uint qCount = (uint)U[10].w;
    int vmode = (int)U[0].w;
    if ((vmode != 4 && vmode != 5) || id.x >= qCount) return;

    uint W = (uint)U[0].x, H = (uint)U[0].y;
    int minX = (int)U[11].x, minY = (int)U[11].y;
    int maxX = minX + (int)U[11].z - 1;
    int maxY = minY + (int)U[11].z - 1;
    bool solidZMode = (U[11].w < 0.5); // 0 = Solid Z-Buffered Specular Jewel, 1 = X-Ray Quartz!

    uint baseIdx = id.x * 3u;
    float4 p01 = Quads3D[baseIdx + 0u];
    float4 p23 = Quads3D[baseIdx + 1u];
    float4 col = Quads3D[baseIdx + 2u]; // xyz = lit RGB, w = DepthZ

    float2 v0 = p01.xy, v1 = p01.zw, v2 = p23.xy, v3 = p23.zw;
    int bx0 = max(minX, (int)floor(min(min(v0.x, v1.x), min(v2.x, v3.x))));
    int bx1 = min(maxX, (int)ceil(max(max(v0.x, v1.x), max(v2.x, v3.x))));
    int by0 = max(minY, (int)floor(min(min(v0.y, v1.y), min(v2.y, v3.y))));
    int by1 = min(maxY, (int)ceil(max(max(v0.y, v1.y), max(v2.y, v3.y))));

    int sw = bx1 - bx0, sh = by1 - by0;
    if (sw >= 1 && sw <= 180 && sh >= 1 && sh <= 180)
    {
        if (solidZMode)
        {
            uint depth14 = (uint)clamp((18.5 - col.w) * 900.0, 1.0, 16383.0);
            uint r6 = (uint)clamp(col.x * 63.0, 0.0, 63.0);
            uint g6 = (uint)clamp(col.y * 63.0, 0.0, 63.0);
            uint b6 = (uint)clamp(col.z * 63.0, 0.0, 63.0);
            uint zPack = (depth14 << 18) | (r6 << 12) | (g6 << 6) | b6;

            [loop] for (int dy = 0; dy < 182; dy++)
            {
                if (dy > sh) break;
                int py = by0 + dy;
                [loop] for (int dx = 0; dx < 182; dx++)
                {
                    if (dx > sw) break;
                    int px = bx0 + dx;
                    if (InQuad(float2((float)px + 0.5, (float)py + 0.5), v0, v1, v2, v3))
                        InterlockedMax(ZBuf3D[(uint)py * W + (uint)px], zPack);
                }
            }
        }
        else
        {
            uint fr = (uint)clamp(col.x * 26.0, 0.0, 60.0);
            uint fg = (uint)clamp(col.y * 26.0, 0.0, 60.0);
            uint fb = (uint)clamp(col.z * 26.0, 0.0, 60.0);
            [loop] for (int dy = 0; dy < 182; dy++)
            {
                if (dy > sh) break;
                int py = by0 + dy;
                [loop] for (int dx = 0; dx < 182; dx++)
                {
                    if (dx > sw) break;
                    int px = bx0 + dx;
                    if (InQuad(float2((float)px + 0.5, (float)py + 0.5), v0, v1, v2, v3))
                        PlotAdd3D(W, H, px, py, fr, fg, fb);
                }
            }
        }
    }

    float edgeMul = solidZMode ? 42.0 : 115.0;
    uint er = (uint)clamp(col.x * edgeMul + 10.0, 12.0, 220.0);
    uint eg = (uint)clamp(col.y * edgeMul + 10.0, 12.0, 220.0);
    uint ebl = (uint)clamp(col.z * edgeMul + 10.0, 12.0, 220.0);
    DrawEdge3D(W, H, v0, v1, minX, maxX, minY, maxY, er, eg, ebl);
    DrawEdge3D(W, H, v1, v2, minX, maxX, minY, maxY, er, eg, ebl);
    DrawEdge3D(W, H, v2, v3, minX, maxX, minY, maxY, er, eg, ebl);
    DrawEdge3D(W, H, v3, v0, minX, maxX, minY, maxY, er, eg, ebl);
}
";
        }

        // Pass 6: Composite BasePix + Hardware ZBuf3D Opaque Jewel + LineRGB -> SwapChain!
        private static string BuildCompositeHlsl()
        {
            return @"
cbuffer PenCB : register(b0) { float4 U[24]; };
StructuredBuffer<uint> BasePix : register(t0);
StructuredBuffer<uint> LineRGB : register(t1);
StructuredBuffer<uint> ZBuf3D  : register(t2);
RWTexture2D<float4> OutTex : register(u0);

[numthreads(16, 16, 1)]
void CSComposite(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (id.x >= W || id.y >= H) return;
    uint pIdx = id.y * W + id.x;

    uint bp = BasePix[pIdx];
    float3 baseCol = float3(((bp >> 16) & 255u) / 255.0, ((bp >> 8) & 255u) / 255.0, (bp & 255u) / 255.0);

    uint lBase = pIdx * 3u;
    float3 lineCol = float3(LineRGB[lBase + 0u], LineRGB[lBase + 1u], LineRGB[lBase + 2u]) / 255.0;

    uint zp = ZBuf3D[pIdx];
    if ((zp >> 18) > 0u)
    {
        float3 zCol = float3(((zp >> 12) & 63u) / 63.0, ((zp >> 6) & 63u) / 63.0, (zp & 63u) / 63.0);
        // Opaque solid crystal facet + subtle specular edge rim (capped so hidden back-edges don't X-ray bleed!)
        baseCol = zCol;
        lineCol = min(lineCol * 0.35, float3(0.28, 0.28, 0.28));
    }

    float3 hdr = baseCol + lineCol;
    float3 mapped = saturate((hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14));
    OutTex[id.xy] = float4(mapped, 1.0);
}
";
        }

        public bool RenderGpuFrame(
            int w, int h, float timeSec, int viewMode,
            float zoomUnits, float panX, float panY,
            int symMode, float phasonPhase, double[] gamma5,
            float mu, float sigma, int kernelRadius, float dt, float coupling, float crossSpecies,
            int lifeMode, float mutationRate,
            int themeIdx, bool paused,
            int fftSource, int laserMode, float gainDb, int fftWinMode, float braggFloorGate,
            float brushX, float brushY, int brushMode, float brushRadius,
            bool showPhiRings, bool showAmmann, bool showRhombEdges, bool showConwayWorms,
            bool showJewelFacets, bool showWaveguidePath,
            float audioSub, float audioMid, float audioHi, float audioPulse,
            float rect3dX, float rect3dY, float rect3dSide, int shader3dMode,
            MillerPeakInfo5D millerPeak, bool showMillerPlanes,
            int subStepsPerFrame)
        {
            lock (_lk)
            {
                if (!IsGpuActive || _dev == IntPtr.Zero) return false;
                if (!EnsureViewportBuffers(w, h)) return false;

                if (_pendingFieldUpload)
                {
                    GCHandle pinF = GCHandle.Alloc(_cpuFieldUpload, GCHandleType.Pinned);
                    try
                    {
                        _fUpd(_ctx, _leniaBufA, 0u, IntPtr.Zero, pinF.AddrOfPinnedObject(), 0u, 0u);
                        _fUpd(_ctx, _leniaBufB, 0u, IntPtr.Zero, pinF.AddrOfPinnedObject(), 0u, 0u);
                    }
                    finally { pinF.Free(); }

                    GCHandle pinG = GCHandle.Alloc(_cpuGenomeUpload, GCHandleType.Pinned);
                    try
                    {
                        _fUpd(_ctx, _genomeBufA, 0u, IntPtr.Zero, pinG.AddrOfPinnedObject(), 0u, 0u);
                        _fUpd(_ctx, _genomeBufB, 0u, IntPtr.Zero, pinG.AddrOfPinnedObject(), 0u, 0u);
                    }
                    finally { pinG.Free(); }
                    _pendingFieldUpload = false;
                }

                _frameCounter++;
                _cbData[0]  = w; _cbData[1] = h; _cbData[2] = timeSec; _cbData[3] = viewMode;
                _cbData[4]  = zoomUnits; _cbData[5] = symMode; _cbData[6] = phasonPhase; _cbData[7] = showAmmann ? 1.0f : 0.0f;
                _cbData[8]  = (float)gamma5[0]; _cbData[9] = (float)gamma5[1]; _cbData[10] = (float)gamma5[2]; _cbData[11] = (float)gamma5[3];
                _cbData[12] = (float)gamma5[4]; _cbData[13] = mu; _cbData[14] = sigma; _cbData[15] = kernelRadius;
                _cbData[16] = dt; _cbData[17] = coupling; _cbData[18] = themeIdx; _cbData[19] = paused ? 1.0f : 0.0f;
                _cbData[20] = fftSource; _cbData[21] = laserMode; _cbData[22] = gainDb; _cbData[23] = fftWinMode;
                _cbData[24] = brushX; _cbData[25] = brushY; _cbData[26] = brushMode; _cbData[27] = brushRadius;
                _cbData[28] = showPhiRings ? 1.0f : 0.0f;
                _cbData[29] = _uploadedRhombCount;
                _cbData[30] = showRhombEdges ? 1.0f : 0.0f;
                _cbData[31] = showConwayWorms ? 1.0f : 0.0f;
                _cbData[32] = panX; _cbData[33] = panY; _cbData[34] = crossSpecies; _cbData[35] = lifeMode;
                _cbData[36] = audioSub; _cbData[37] = audioMid; _cbData[38] = audioHi; _cbData[39] = audioPulse;
                _cbData[40] = braggFloorGate; _cbData[41] = showJewelFacets ? 1.0f : 0.0f;
                _cbData[42] = showWaveguidePath ? 1.0f : 0.0f; _cbData[43] = _uploadedQuad3dCount;
                _cbData[44] = rect3dX; _cbData[45] = rect3dY; _cbData[46] = rect3dSide; _cbData[47] = shader3dMode;
                // U[12]: mutationRate, frameCounter, quantumParity, 0
                _cbData[48] = mutationRate; _cbData[49] = _frameCounter; _cbData[50] = 0.0f; _cbData[51] = 0.0f;
                // U[13]: millerU, millerV, millerState (0=off, 1=reticle, 2=reticle+planes), 0
                _cbData[52] = millerPeak.ScreenU; _cbData[53] = millerPeak.ScreenV;
                _cbData[54] = millerPeak.Active ? (showMillerPlanes ? 2.0f : 1.0f) : 0.0f;
                _cbData[55] = 0.0f;
                // U[14]: millerGx, millerGy, 0, 0
                _cbData[56] = millerPeak.Gx; _cbData[57] = millerPeak.Gy; _cbData[58] = 0.0f; _cbData[59] = 0.0f;

                UploadCb();
                IntPtr cb = _cb;
                _fSetCb(_ctx, 0u, 1u, ref cb);

                IntPtr[] zUav1 = new IntPtr[] { IntPtr.Zero };
                IntPtr[] zUav2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zUav3 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zSrv1 = new IntPtr[] { IntPtr.Zero };
                IntPtr[] zSrv2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zSrv3 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zSrv4 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };

                IntPtr curSrv = _pingPongFlip ? _leniaSrvB : _leniaSrvA;

                // Pass 1: Quasicrystal Potential + Hologram / Laser Aperture
                _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { curSrv, _holoPhaseSrv });
                _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _quasiUav, _fftUavA }, IntPtr.Zero);
                _fSetCs(_ctx, _csQuasi, IntPtr.Zero, 0u);
                _fDisp(_ctx, 32u, 32u, 1u);
                _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 2u, zSrv2);

                // Pass 2: Life / Darwinian / Quantum Wavepacket Steps
                int steps = paused ? (brushMode > 0 ? 1 : 0) : Math.Max(1, Math.Min(4, subStepsPerFrame));
                if (lifeMode == 2)
                {
                    // Quantum Leapfrog: each sub-step performs Parity 0 (Re) then Parity 1 (Im) for exact unitarity!
                    _fSetCs(_ctx, _csQuantum, IntPtr.Zero, 0u);
                    int qPairs = paused ? 1 : (steps * 2);
                    for (int s = 0; s < qPairs; s++)
                    {
                        for (int par = 0; par < 2; par++)
                        {
                            _cbData[50] = par;
                            UploadCb();
                            IntPtr inSrv = _pingPongFlip ? _leniaSrvB : _leniaSrvA;
                            IntPtr outUav = _pingPongFlip ? _leniaUavA : _leniaUavB;
                            _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { inSrv, _quasiSrv });
                            _fSetUav(_ctx, 0u, 1u, new IntPtr[] { outUav }, IntPtr.Zero);
                            _fDisp(_ctx, 32u, 32u, 1u);
                            _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                            _fSetSrv(_ctx, 0u, 2u, zSrv2);
                            _pingPongFlip = !_pingPongFlip;

                            if (par == 0 && brushMode > 0) _cbData[26] = 0.0f;
                        }
                    }
                }
                else
                {
                    for (int s = 0; s < steps; s++)
                    {
                        IntPtr inSrv = _pingPongFlip ? _leniaSrvB : _leniaSrvA;
                        IntPtr outUav = _pingPongFlip ? _leniaUavA : _leniaUavB;
                        IntPtr gInSrv = _pingPongFlip ? _genomeSrvB : _genomeSrvA;
                        IntPtr gOutUav = _pingPongFlip ? _genomeUavA : _genomeUavB;

                        if (!paused)
                        {
                            _fSetSrv(_ctx, 0u, 4u, new IntPtr[] { inSrv, _quasiSrv, _kernelSrv, gInSrv });
                            _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _flowPotUav }, IntPtr.Zero);
                            _fSetCs(_ctx, _csFlowPot, IntPtr.Zero, 0u);
                            _fDisp(_ctx, 32u, 32u, 1u);
                            _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                            _fSetSrv(_ctx, 0u, 4u, zSrv4);
                        }

                        _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { inSrv, _flowPotSrv, gInSrv });
                        _fSetUav(_ctx, 0u, 2u, new IntPtr[] { outUav, gOutUav }, IntPtr.Zero);
                        _fSetCs(_ctx, _csFlowReint, IntPtr.Zero, 0u);
                        _fDisp(_ctx, 32u, 32u, 1u);
                        _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
                        _fSetSrv(_ctx, 0u, 3u, zSrv3);
                        _pingPongFlip = !_pingPongFlip;

                        if (s == 0 && brushMode > 0)
                        {
                            _cbData[26] = 0.0f;
                            UploadCb();
                        }
                    }
                }

                // Pass 3A & 3B: Live 256x256 GPU 2D Radix-2 FFT Optical Bragg / Hologram Diffractometer
                _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _fftSrvA });
                _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _fftUavB }, IntPtr.Zero);
                _fSetCs(_ctx, _csFftRows, IntPtr.Zero, 0u);
                _fDisp(_ctx, 256u, 1u, 1u);
                _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 1u, zSrv1);

                _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _fftSrvB });
                _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _fftUavA }, IntPtr.Zero);
                _fSetCs(_ctx, _csFftCols, IntPtr.Zero, 0u);
                _fDisp(_ctx, 256u, 1u, 1u);
                _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 1u, zSrv1);

                // Pass 4: Stage Viewport + Clear LineRGB & ZBuf3D
                IntPtr latestLeniaSrv = _pingPongFlip ? _leniaSrvB : _leniaSrvA;
                IntPtr latestGenSrv   = _pingPongFlip ? _genomeSrvB : _genomeSrvA;
                _fSetSrv(_ctx, 0u, 4u, new IntPtr[] { latestLeniaSrv, _quasiSrv, _fftSrvA, latestGenSrv });
                _fSetUav(_ctx, 0u, 3u, new IntPtr[] { _basePixUav, _lineRgbUav, _zBuf3dUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csStage, IntPtr.Zero, 0u);
                _fDisp(_ctx, (uint)((w + 15) / 16), (uint)((h + 15) / 16), 1u);
                _fSetUav(_ctx, 0u, 3u, zUav3, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 4u, zSrv4);

                // Pass 5A: Jewel Facets + Seamless Hat/Spectre/Rhomb Wireframe + A* Waveguide + Sparkles
                if (_uploadedRhombCount > 0 && viewMode != 2 && viewMode != 4)
                {
                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _rhombSrv });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _lineRgbUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csRhombs, IntPtr.Zero, 0u);
                    _fDisp(_ctx, (uint)((_uploadedRhombCount + 63) / 64), 1u, 1u);
                    _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, zSrv1);
                }

                // Pass 5B: Hardware Z-Buffered Solid Specular Jewel 3D / X-Ray Quartz (Modes 4 & 5)
                if (_uploadedQuad3dCount > 0 && (viewMode == 4 || viewMode == 5))
                {
                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _quad3dSrv });
                    _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _lineRgbUav, _zBuf3dUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csCrystal3d, IntPtr.Zero, 0u);
                    _fDisp(_ctx, (uint)((_uploadedQuad3dCount + 63) / 64), 1u, 1u);
                    _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, zSrv1);
                }

                // Pass 6: ACES + ZBuf3D Composite -> OutTex -> SwapChain
                _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _basePixSrv, _lineRgbSrv, _zBuf3dSrv });
                _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _outTexUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csComp, IntPtr.Zero, 0u);
                _fDisp(_ctx, (uint)((w + 15) / 16), (uint)((h + 15) / 16), 1u);
                _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 3u, zSrv3);

                if (_backBuf != IntPtr.Zero)
                {
                    _fCopy(_ctx, _backBuf, _outTex);
                    _fPresent(_swap, VSyncEnabled ? 1u : 0u, 0u);
                }
                return true;
            }
        }

        private void UploadCb()
        {
            GCHandle pin = GCHandle.Alloc(_cbData, GCHandleType.Pinned);
            try { _fUpd(_ctx, _cb, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
            finally { pin.Free(); }
        }

        public void RenderCpuFallbackBitmap(Bitmap bmp, double[] gamma5, float zoom)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                byte[] buf = new byte[stride * h];
                Parallel.For(0, h, delegate(int py)
                {
                    int row = py * stride;
                    double ny = ((double)py / h - 0.5) * zoom;
                    for (int px = 0; px < w; px++)
                    {
                        double nx = ((double)px / w - 0.5) * zoom;
                        double s = 0.0;
                        for (int k = 0; k < 5; k++)
                        {
                            double a = 2.0 * Math.PI * k / 5.0;
                            s += Math.Cos(2.0 * Math.PI * (nx * Math.Cos(a) + ny * Math.Sin(a) + gamma5[k]));
                        }
                        double v = Math.Max(0.0, Math.Min(1.0, (s / 5.0) * 0.5 + 0.5));
                        int o = row + px * 4;
                        buf[o + 0] = (byte)(35 + v * 195);
                        buf[o + 1] = (byte)(20 + v * 175);
                        buf[o + 2] = (byte)(15 + Math.Pow(v, 2.0) * 140);
                        buf[o + 3] = 255;
                    }
                });
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(bd); }
        }

        public void Dispose()
        {
            lock (_lk)
            {
                IsGpuActive = false;
                Rel(_csQuasi); Rel(_csFlowPot); Rel(_csFlowReint); Rel(_csQuantum); Rel(_csFftRows); Rel(_csFftCols);
                Rel(_csStage); Rel(_csRhombs); Rel(_csCrystal3d); Rel(_csComp);
                Rel(_leniaUavA); Rel(_leniaSrvA); Rel(_leniaBufA);
                Rel(_leniaUavB); Rel(_leniaSrvB); Rel(_leniaBufB);
                Rel(_genomeUavA); Rel(_genomeSrvA); Rel(_genomeBufA);
                Rel(_genomeUavB); Rel(_genomeSrvB); Rel(_genomeBufB);
                Rel(_flowPotUav); Rel(_flowPotSrv); Rel(_flowPotBuf);
                Rel(_quasiUav); Rel(_quasiSrv); Rel(_quasiBuf);
                Rel(_fftUavA); Rel(_fftSrvA); Rel(_fftBufA);
                Rel(_fftUavB); Rel(_fftSrvB); Rel(_fftBufB);
                Rel(_kernelSrv); Rel(_kernelBuf);
                Rel(_holoPhaseSrv); Rel(_holoPhaseBuf);
                Rel(_rhombSrv); Rel(_rhombBuf);
                Rel(_quad3dSrv); Rel(_quad3dBuf);
                Rel(_basePixUav); Rel(_basePixSrv); Rel(_basePixBuf);
                Rel(_lineRgbUav); Rel(_lineRgbSrv); Rel(_lineRgbBuf);
                Rel(_zBuf3dUav); Rel(_zBuf3dSrv); Rel(_zBuf3d);
                Rel(_outTexUav); Rel(_outTex); Rel(_backBuf);
                Rel(_cb); Rel(_swap); Rel(_ctx); Rel(_dev);
            }
        }
    }
}
