using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OmegaSynthStudio
{
    public sealed class OmegaCymaticsGpuEngine : IDisposable
    {
        public const int GridN = 512;

        [StructLayout(LayoutKind.Sequential)] private struct BUF_DESC { public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride; }
        [StructLayout(LayoutKind.Sequential)] private struct SUB_DATA { public IntPtr pSysMem; public uint SysMemPitch, SysMemSlicePitch; }
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint RelDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobDel(IntPtr s);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBufDel(IntPtr s, ref BUF_DESC d, IntPtr init, out IntPtr ppB);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTex2DDel(IntPtr s, ref TEX2D_DESC d, IntPtr init, out IntPtr ppT);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvDel(IntPtr s, IntPtr res, ref SRV_DESC d, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvNullDel(IntPtr s, IntPtr res, IntPtr pNullDesc, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavDel(IntPtr s, IntPtr res, ref UAV_DESC d, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavNullDel(IntPtr s, IntPtr res, IntPtr pNullDesc, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCsDel(IntPtr s, IntPtr bc, IntPtr len, IntPtr lnk, out IntPtr ppCs);
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
        private IntPtr _dev, _ctx, _swap, _backBuf, _cb;
        private IntPtr _plateBufA, _plateUavA, _plateSrvA;
        private IntPtr _plateBufB, _plateUavB, _plateSrvB;
        private IntPtr _e8MetaBuf, _e8MetaSrv;
        private IntPtr _e8DispBuf, _e8DispSrv;
        private IntPtr _edgesBuf, _edgesSrv;
        private IntPtr _waveBuf, _waveSrv;
        private IntPtr _waterfallBuf, _waterfallUav, _waterfallSrv;
        private IntPtr _basePixBuf, _basePixUav, _basePixSrv;
        private IntPtr _lineRgbBuf, _lineRgbUav, _lineRgbSrv;
        private IntPtr _outTex, _outTexUav;

        private IntPtr _csStep, _csStage, _csEdges, _csComp;

        private CreateBufDel _fBuf; private CreateTex2DDel _fTex2D; private CreateSrvDel _fSrv; private CreateSrvNullDel _fSrvNull; private CreateUavDel _fUav; private CreateUavNullDel _fUavNull; private CreateCsDel _fCs;
        private DispatchDel _fDisp; private CopyDel _fCopy; private UpdSubDel _fUpd;
        private SetSrvDel _fSetSrv; private SetUavDel _fSetUav; private SetCsDel _fSetCs; private SetCbDel _fSetCb;
        private PresentDel _fPresent; private GetSwapBufDel _fGetBuf; private ResizeSwapDel _fResize;

        private int _w = 0, _h = 0, _swapW = 0, _swapH = 0;
        private bool _resetPlate = true;
        private uint _subStepCounter = 0u;
        private uint _waterfallRow = 0u;
        private readonly float[] _cbData = new float[80];
        private readonly float[] _waveGpu = new float[512];
        private readonly float[] _cpuSand = new float[128 * 128];
        private int[] _cpuPixels;

        public volatile bool VSyncEnabled = true;
        public bool IsGpuActive { get; private set; }
        public string BackendLabel { get; private set; }
        public string LastError { get; private set; }

        public OmegaCymaticsGpuEngine()
        {
            BackendLabel = "Initializing RTX DirectCompute...";
        }

        public void ResetSandAndWavefield()
        {
            _resetPlate = true;
            for (int i = 0; i < _cpuSand.Length; i++) _cpuSand[i] = 0.35f;
        }

        public bool Initialize(IntPtr hwnd, int w, int h)
        {
            ResetSandAndWavefield();
            lock (_lk)
            {
                try
                {
                    if (w < 64) w = 640;
                    if (h < 64) h = 420;
                    uint[] fls = new uint[] { 0xb000u };
                    uint fl = 0u;
                    SWAP_DESC sd = new SWAP_DESC();
                    sd.Width = (uint)w; sd.Height = (uint)h;
                    sd.RefreshNum = 0u; sd.RefreshDen = 1u;
                    sd.Format = 28u;
                    sd.SampleCount = 1u; sd.SampleQuality = 0u;
                    sd.BufferUsage = 0x20u; sd.BufferCount = 2u;
                    sd.OutputWindow = hwnd; sd.Windowed = 1;
                    sd.SwapEffect = 0u; sd.Flags = 0u;

                    int hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, 1u, 7u, ref sd, out _swap, out _dev, out fl, out _ctx);
                    if (hr < 0 || _dev == IntPtr.Zero || _swap == IntPtr.Zero)
                    {
                        FailToCpu("D3D11 SwapChain 0x" + hr.ToString("X"));
                        return false;
                    }

                    _fPresent = Vtbl<PresentDel>(_swap, 8);
                    _fGetBuf = Vtbl<GetSwapBufDel>(_swap, 9);
                    _fResize = Vtbl<ResizeSwapDel>(_swap, 13);
                    if (_fGetBuf(_swap, 0u, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero)
                    {
                        FailToCpu("BackBuffer failed");
                        return false;
                    }
                    _swapW = w; _swapH = h;

                    _fBuf = Vtbl<CreateBufDel>(_dev, 3);
                    _fTex2D = Vtbl<CreateTex2DDel>(_dev, 5);
                    _fSrv = Vtbl<CreateSrvDel>(_dev, 7);
                    _fSrvNull = Vtbl<CreateSrvNullDel>(_dev, 7);
                    _fUav = Vtbl<CreateUavDel>(_dev, 8);
                    _fUavNull = Vtbl<CreateUavNullDel>(_dev, 8);
                    _fCs = Vtbl<CreateCsDel>(_dev, 18);
                    _fDisp = Vtbl<DispatchDel>(_ctx, 41);
                    _fCopy = Vtbl<CopyDel>(_ctx, 47);
                    _fUpd = Vtbl<UpdSubDel>(_ctx, 48);
                    _fSetSrv = Vtbl<SetSrvDel>(_ctx, 67);
                    _fSetUav = Vtbl<SetUavDel>(_ctx, 68);
                    _fSetCs = Vtbl<SetCsDel>(_ctx, 69);
                    _fSetCb = Vtbl<SetCbDel>(_ctx, 71);

                    BUF_DESC cbd = new BUF_DESC(); cbd.ByteWidth = 320u; cbd.BindFlags = 4u;
                    if (_fBuf(_dev, ref cbd, IntPtr.Zero, out _cb) < 0) { FailToCpu("CBuf failed"); return false; }

                    if (!BuildStaticBuffers()) { FailToCpu(LastError ?? "StaticBuf failed"); return false; }
                    if (!CompileAllIsolatedPasses()) { FailToCpu(LastError ?? "HLSL failed"); return false; }
                    if (!EnsureViewportBuffers(w, h)) { FailToCpu(LastError ?? "ViewportBuf failed"); return false; }

                    IsGpuActive = true;
                    BackendLabel = "RTX DirectCompute cs_5_0 VSync";
                    return true;
                }
                catch (Exception ex)
                {
                    FailToCpu(ex.Message);
                    return false;
                }
            }
        }

        private void FailToCpu(string reason)
        {
            LastError = reason;
            Dispose();
            BackendLabel = "CPU Fallback (" + reason + ")";
        }

        private bool CreateStructuredSrvSafe(IntPtr buf, uint count, out IntPtr srv)
        {
            SRV_DESC sd = new SRV_DESC();
            sd.Format = 0u;
            sd.ViewDimension = 1u; // D3D_SRV_DIMENSION_BUFFER = 1
            sd.FirstElement = 0u;
            sd.NumElements = count;
            int hr = _fSrv(_dev, buf, ref sd, out srv);
            if (hr >= 0 && srv != IntPtr.Zero) return true;
            hr = _fSrvNull(_dev, buf, IntPtr.Zero, out srv);
            if (hr >= 0 && srv != IntPtr.Zero) return true;
            LastError = "SRV 0x" + hr.ToString("X8");
            return false;
        }

        private bool BuildStaticBuffers()
        {
            uint cells = (uint)(GridN * GridN);
            if (!MakeRwStructured(16u, cells, out _plateBufA, out _plateUavA, out _plateSrvA)) { LastError = "PlateA " + LastError; return false; }
            if (!MakeRwStructured(16u, cells, out _plateBufB, out _plateUavB, out _plateSrvB)) { LastError = "PlateB " + LastError; return false; }
            if (!MakeRwStructured(4u, 32u * 64u, out _waterfallBuf, out _waterfallUav, out _waterfallSrv)) { LastError = "Waterfall " + LastError; return false; }

            float[] meta = new float[240 * 4];
            for (int i = 0; i < 240; i++)
            {
                meta[i * 4 + 0] = E8WaveLatticeMath.CoxeterX[i];
                meta[i * 4 + 1] = E8WaveLatticeMath.CoxeterY[i];
                meta[i * 4 + 2] = E8WaveLatticeMath.CoxeterZ[i];
                meta[i * 4 + 3] = E8WaveLatticeMath.CoxeterW[i];
            }
            if (!MakeStaticSrvFloat(meta, 16u, 240u, out _e8MetaBuf, out _e8MetaSrv)) { LastError = "E8Meta " + LastError; return false; }

            int nEdges = E8WaveLatticeMath.EdgeA.Length;
            uint[] edgePacked = new uint[nEdges * 2];
            for (int e = 0; e < nEdges; e++)
            {
                edgePacked[e * 2] = (uint)E8WaveLatticeMath.EdgeA[e];
                edgePacked[e * 2 + 1] = (uint)E8WaveLatticeMath.EdgeB[e];
            }
            if (!MakeStaticSrvUint(edgePacked, 8u, (uint)nEdges, out _edgesBuf, out _edgesSrv)) { LastError = "E8Edges " + LastError; return false; }

            BUF_DESC ed = new BUF_DESC(); ed.ByteWidth = 240u * 4u; ed.BindFlags = 8u; ed.MiscFlags = 0x40u; ed.StructureByteStride = 4u;
            int hrE = _fBuf(_dev, ref ed, IntPtr.Zero, out _e8DispBuf);
            if (hrE < 0 || _e8DispBuf == IntPtr.Zero) { LastError = "E8DispBuf 0x" + hrE.ToString("X8"); return false; }
            if (!CreateStructuredSrvSafe(_e8DispBuf, 240u, out _e8DispSrv)) { LastError = "E8DispSRV " + LastError; return false; }

            BUF_DESC wd = new BUF_DESC(); wd.ByteWidth = 256u * 8u; wd.BindFlags = 8u; wd.MiscFlags = 0x40u; wd.StructureByteStride = 8u;
            int hrW = _fBuf(_dev, ref wd, IntPtr.Zero, out _waveBuf);
            if (hrW < 0 || _waveBuf == IntPtr.Zero) { LastError = "WaveBuf 0x" + hrW.ToString("X8"); return false; }
            if (!CreateStructuredSrvSafe(_waveBuf, 256u, out _waveSrv)) { LastError = "WaveSRV " + LastError; return false; }
            return true;
        }

        private bool EnsureViewportBuffers(int w, int h)
        {
            if (w < 64 || h < 64) return false;
            if (_swapW != w || _swapH != h)
            {
                Rel(_backBuf); _backBuf = IntPtr.Zero;
                if (_fResize(_swap, 2u, (uint)w, (uint)h, 28u, 0u) >= 0 && _fGetBuf(_swap, 0u, ref IID_Tex2D, out _backBuf) >= 0)
                {
                    _swapW = w; _swapH = h;
                }
            }
            if (_w == w && _h == h && _outTex != IntPtr.Zero && _basePixBuf != IntPtr.Zero && _lineRgbBuf != IntPtr.Zero) return true;

            Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
            Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
            Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;

            TEX2D_DESC td = new TEX2D_DESC();
            td.Width = (uint)w; td.Height = (uint)h; td.MipLevels = 1u; td.ArraySize = 1u;
            td.Format = 28u; td.SampleCount = 1u; td.BindFlags = 0x80u;
            if (_fTex2D(_dev, ref td, IntPtr.Zero, out _outTex) < 0) return false;
            UAV_DESC tud = new UAV_DESC(); tud.Format = 28u; tud.ViewDimension = 4u;
            if (_fUav(_dev, _outTex, ref tud, out _outTexUav) < 0) return false;

            uint pix = (uint)(w * h);
            if (!MakeRwStructured(4u, pix, out _basePixBuf, out _basePixUav, out _basePixSrv)) return false;
            if (!MakeRwStructured(4u, pix * 3u, out _lineRgbBuf, out _lineRgbUav, out _lineRgbSrv)) return false;

            _w = w; _h = h;
            return true;
        }

        private bool CompileAllIsolatedPasses()
        {
            string err;
            _csStep = CompileKernel(Encoding.UTF8.GetBytes(BuildPlateStepHlsl()), "CSPlateStep", out err);
            if (_csStep == IntPtr.Zero) { LastError = "CSPlateStep: " + err; return false; }

            _csStage = CompileKernel(Encoding.UTF8.GetBytes(BuildStagePassHlsl()), "CSRenderStage", out err);
            if (_csStage == IntPtr.Zero) { LastError = "CSStage: " + err; return false; }

            _csEdges = CompileKernel(Encoding.UTF8.GetBytes(BuildE8LinesHlsl(E8WaveLatticeMath.EdgeA.Length)), "CSE8Lines", out err);
            if (_csEdges == IntPtr.Zero) { LastError = "CSE8Lines: " + err; return false; }

            _csComp = CompileKernel(Encoding.UTF8.GetBytes(BuildCompositeHlsl()), "CSComposite", out err);
            if (_csComp == IntPtr.Zero) { LastError = "CSComp: " + err; return false; }

            return true;
        }

        private IntPtr CompileKernel(byte[] src, string entry, out string err)
        {
            err = null;
            IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;
            try
            {
                int hr = D3DCompile(src, (IntPtr)src.Length, entry + ".hlsl", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 0u, 0u, out pCode, out pErr);
                if (hr < 0 || pCode == IntPtr.Zero)
                {
                    err = (pErr != IntPtr.Zero) ? Marshal.PtrToStringAnsi(Vtbl<BlobDel>(pErr, 3)(pErr), (int)Vtbl<BlobDel>(pErr, 4)(pErr)).Trim() : ("0x" + hr.ToString("X8"));
                    return IntPtr.Zero;
                }
                IntPtr bc = Vtbl<BlobDel>(pCode, 3)(pCode), len = Vtbl<BlobDel>(pCode, 4)(pCode), cs;
                if (_fCs(_dev, bc, len, IntPtr.Zero, out cs) < 0) return IntPtr.Zero;
                return cs;
            }
            finally { Rel(pCode); Rel(pErr); }
        }

        private static string BuildPlateStepHlsl()
        {
            return @"
cbuffer SynthCB : register(b0) { float4 U[20]; };
StructuredBuffer<float4> PlateIn : register(t0);
RWStructuredBuffer<float4> PlateOut : register(u0);

float4 FetchCell(int x, int y, int bmode)
{
    if (bmode == 3) { x = (x + 512) & 511; y = (y + 512) & 511; return PlateIn[y * 512 + x]; }
    if (bmode == 1) { if (x <= 0 || x >= 511 || y <= 0 || y >= 511) return float4(0, 0, 0, 0); return PlateIn[y * 512 + x]; }
    x = clamp(x, 0, 511); y = clamp(y, 0, 511);
    return PlateIn[y * 512 + x];
}

float VoiceForce(float4 v, float2 uv, float ph)
{
    if (v.w <= 0.002) return 0.0;
    float2 d = uv - v.xy;
    float gauss = exp(-dot(d, d) * 460.0);
    return v.w * sin(ph * v.z) * gauss * 0.24;
}

[numthreads(16, 16, 1)]
void CSPlateStep(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 512 || id.y >= 512) return;
    uint idx = id.y * 512 + id.x;
    if (U[1].w > 0.5)
    {
        float h = frac(sin(dot(float2(id.x, id.y), float2(12.9898, 78.233))) * 43758.5453);
        PlateOut[idx] = float4(0.0, 0.0, 0.32 + 0.45 * h, 0.0);
        return;
    }

    int x = (int)id.x, y = (int)id.y;
    int bm = (int)U[1].x;
    float2 uv = (float2(x, y) + 0.5) / 512.0;

    if (bm == 2)
    {
        float2 cd = uv - 0.5;
        if (dot(cd, cd) > 0.238) { PlateOut[idx] = float4(0.0, 0.0, 0.03, 0.0); return; }
    }

    float4 c = PlateIn[idx];
    float u = c.x, uPrev = c.y, sand = c.z, rms = c.w;

    float uL = FetchCell(x - 1, y, bm).x, uR = FetchCell(x + 1, y, bm).x;
    float uU = FetchCell(x, y - 1, bm).x, uD = FetchCell(x, y + 1, bm).x;
    float uUL = FetchCell(x - 1, y - 1, bm).x, uUR = FetchCell(x + 1, y - 1, bm).x;
    float uDL = FetchCell(x - 1, y + 1, bm).x, uDR = FetchCell(x + 1, y + 1, bm).x;

    float lap = 0.20 * (uL + uR + uU + uD) + 0.05 * (uUL + uUR + uDL + uDR) - u;
    float bih = (FetchCell(x - 2, y, bm).x + FetchCell(x + 2, y, bm).x + FetchCell(x, y - 2, bm).x + FetchCell(x, y + 2, bm).x)
              - 4.0 * (uL + uR + uU + uD) + 12.0 * u;

    float mMode = max(1.0, U[12].x);
    float nMode = max(1.0, U[12].y);
    float activeEnergy = U[12].z;
    float ph = U[3].x;

    float eigenField;
    if (bm == 2)
    {
        float2 cd = (uv - 0.5) * 2.0;
        float rad = length(cd);
        float th = atan2(cd.y, cd.x + 1e-6);
        eigenField = cos(rad * 3.14159265 * mMode) * cos(nMode * th);
    }
    else
    {
        float sgn = (((int)(mMode + nMode) & 1) == 0) ? 1.0 : -1.0;
        eigenField = cos(mMode * 3.14159265 * uv.x) * cos(nMode * 3.14159265 * uv.y)
                   + sgn * cos(nMode * 3.14159265 * uv.x) * cos(mMode * 3.14159265 * uv.y);
    }

    float frc = VoiceForce(U[4], uv, ph) + VoiceForce(U[5], uv, ph) + VoiceForce(U[6], uv, ph) + VoiceForce(U[7], uv, ph)
              + VoiceForce(U[8], uv, ph) + VoiceForce(U[9], uv, ph) + VoiceForce(U[10], uv, ph) + VoiceForce(U[11], uv, ph)
              + eigenField * sin(ph * 1.35) * (0.012 + 0.032 * activeEnergy);

    float vel = (u - uPrev) * 0.991;
    float uNext = clamp(u + vel + U[1].y * lap - U[1].z * bih + frc, -2.2, 2.2);
    float newRms = lerp(rms, abs(uNext) + abs(eigenField) * (0.18 + 0.35 * activeEnergy), 0.06);

    float nodalSharp = exp(-abs(eigenField) * 8.5) * 0.85 + exp(-newRms * 9.5) * 0.55;
    sand = clamp(lerp(sand, nodalSharp, 0.045 + 0.075 * activeEnergy), 0.0, 1.4);

    PlateOut[idx] = float4(uNext, u, sand, newRms);
}
";
        }

        // Pass 2: Stage + 512x512 Chladni Sand + Viewport Mode 3 (3D Polar Cymatic Rose Sound-Shape Mandala!) + Waterfall
        private static string BuildStagePassHlsl()
        {
            return @"
cbuffer SynthCB : register(b0) { float4 U[20]; };
StructuredBuffer<float4> PlateIn : register(t0);
StructuredBuffer<float2> ScopeWv : register(t1);
RWStructuredBuffer<uint> BasePix : register(u0);
RWStructuredBuffer<uint> LineClr : register(u1);
RWStructuredBuffer<float> Waterfall : register(u2);

float3 RingGlow(float4 v, float2 uv)
{
    if (v.w <= 0.02) return float3(0, 0, 0);
    float d = length(uv - v.xy);
    return float3(0.20, 0.98, 0.74) * (exp(-abs(d - (0.020 + 0.016 * v.w)) * 210.0) * 1.15 + exp(-d * 95.0) * 0.85);
}

float ReadSpecBin(uint binIdx)
{
    uint vecIdx = 13u + (binIdx >> 2);
    uint comp = binIdx & 3u;
    float4 v = U[vecIdx];
    if (comp == 0u) return v.x;
    if (comp == 1u) return v.y;
    if (comp == 2u) return v.z;
    return v.w;
}

[numthreads(16, 16, 1)]
void CSRenderStage(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (id.x >= W || id.y >= H) return;

    uint wfRow = (uint)U[12].w;
    if (id.y == 0u && id.x < 28u)
    {
        Waterfall[(wfRow & 63u) * 32u + id.x] = ReadSpecBin(id.x);
    }

    uint pIdx = id.y * W + id.x;
    uint lBase = pIdx * 3u;
    LineClr[lBase + 0u] = 0u;
    LineClr[lBase + 1u] = 0u;
    LineClr[lBase + 2u] = 0u;

    float2 p = float2(id.x, id.y);
    int vmode = (int)U[0].w;
    float t = U[0].z;
    float hudH = 64.0;
    float availH = max(120.0, (float)H - hudH);
    float3 col = float3(0.022, 0.034, 0.064);

    bool inPlate = false;
    float2 plateUv = float2(0, 0);
    if (vmode == 1)
    {
        float side = min((float)W * 0.92, availH * 0.94);
        float2 tl = float2(((float)W - side) * 0.5, (availH - side) * 0.5);
        plateUv = (p - tl) / side;
        inPlate = (plateUv.x >= 0.0 && plateUv.x <= 1.0 && plateUv.y >= 0.0 && plateUv.y <= 1.0);
    }
    else if (vmode == 0 || vmode == 3)
    {
        float side = min((float)W * 0.48, availH * 0.93);
        float2 tl = float2(18.0, (availH - side) * 0.5);
        plateUv = (p - tl) / side;
        inPlate = (plateUv.x >= 0.0 && plateUv.x <= 1.0 && plateUv.y >= 0.0 && plateUv.y <= 1.0);
    }

    if (inPlate)
    {
        float2 gf = plateUv * 511.0;
        int gx0 = clamp((int)gf.x, 0, 510);
        int gy0 = clamp((int)gf.y, 0, 510);
        float2 fr = frac(gf);
        float4 st = lerp(
            lerp(PlateIn[gy0 * 512 + gx0], PlateIn[gy0 * 512 + gx0 + 1], fr.x),
            lerp(PlateIn[(gy0 + 1) * 512 + gx0], PlateIn[(gy0 + 1) * 512 + gx0 + 1], fr.x), fr.y);

        float u = st.x, sand = st.z, rms = st.w;
        float3 posWave = float3(0.05, 0.56, 0.95) * saturate(u * 1.5);
        float3 negWave = float3(0.72, 0.16, 0.88) * saturate(-u * 1.5);
        float sandGrain = pow(saturate(sand), 1.85);
        float3 goldSand = float3(1.00, 0.86, 0.38) * sandGrain * 1.35;
        float3 cyanNode = float3(0.18, 0.85, 0.98) * exp(-rms * 14.0) * 0.30;

        col = float3(0.025, 0.042, 0.088) + posWave + negWave + goldSand + cyanNode;

        float gx = abs(frac(plateUv.x * 12.0) - 0.5);
        float gy = abs(frac(plateUv.y * 8.0) - 0.5);
        if (gx > 0.490 || gy > 0.490) col += float3(0.04, 0.07, 0.13);
        if (plateUv.x < 0.005 || plateUv.x > 0.995 || plateUv.y < 0.005 || plateUv.y > 0.995)
            col = float3(0.22, 0.74, 0.98);

        col += RingGlow(U[4], plateUv) + RingGlow(U[5], plateUv) + RingGlow(U[6], plateUv) + RingGlow(U[7], plateUv)
             + RingGlow(U[8], plateUv) + RingGlow(U[9], plateUv) + RingGlow(U[10], plateUv) + RingGlow(U[11], plateUv);
    }

    // Viewport Mode 3 Right Half: Live Sound-Shape Polar Cymatic Rose Mandala sculpted by 28 Spectral Harmonics!
    if (vmode == 3 && !inPlate && p.y < availH)
    {
        float2 ctr = float2((float)W * 0.755, availH * 0.50);
        float maxRad = min((float)W * 0.45, availH) * 0.46;
        float2 dp = (p - ctr) / max(1.0, maxRad);
        float rad = length(dp);
        if (rad < 1.12)
        {
            float th = atan2(dp.y, dp.x + 1e-6);
            float mMode = max(1.0, U[12].x);
            float nMode = max(1.0, U[12].y);
            float petals = mMode + nMode;

            float harmSum1 = 0.0, harmSum2 = 0.0;
            [loop] for (uint b = 0u; b < 14u; b++)
            {
                float sp0 = ReadSpecBin(b * 2u);
                float sp1 = ReadSpecBin(b * 2u + 1u);
                harmSum1 += sp0 * cos((petals + (float)b) * th + t * 0.8);
                harmSum2 += sp1 * sin((mMode * (float)(b + 1u)) * th - t * 0.6);
            }

            float roseR1 = 0.48 + 0.16 * cos(petals * th + t * 0.4) + 0.08 * harmSum1;
            float roseR2 = 0.74 + 0.12 * cos((petals * 2.0) * th - t * 0.3) + 0.07 * harmSum2;
            float webWave = sin(rad * 28.0 - harmSum1 * 3.2 - t * 2.4) * cos(petals * th);

            float glow1 = exp(-abs(rad - roseR1) * 34.0);
            float glow2 = exp(-abs(rad - roseR2) * 30.0);
            float nodalWeb = exp(-abs(webWave) * 9.0) * saturate(1.0 - rad * 0.92);

            col += float3(0.12, 0.88, 0.98) * glow1 * 1.25
                 + float3(0.96, 0.38, 0.76) * glow2 * 1.15
                 + float3(0.98, 0.84, 0.32) * nodalWeb * 0.52;
        }
    }

    if (p.y >= availH + 4.0)
    {
        float hy = (p.y - (availH + 4.0)) / max(1.0, hudH - 8.0);
        if (p.x < (float)W * 0.52)
        {
            float nx = p.x / ((float)W * 0.52);
            uint sx = clamp((uint)(nx * 255.0), 0u, 255u);
            float2 wv = ScopeWv[sx];
            float yL = 0.5 - wv.x * 0.44;
            float yR = 0.5 - wv.y * 0.44;
            col = float3(0.015, 0.025, 0.05)
                + float3(0.15, 0.88, 1.0) * exp(-abs(hy - yL) * 26.0)
                + float3(0.96, 0.42, 0.84) * exp(-abs(hy - yR) * 26.0);
        }
        else
        {
            float wx = (p.x - (float)W * 0.53) / max(1.0, (float)W * 0.46);
            if (wx >= 0.0 && wx <= 1.0)
            {
                uint bin = clamp((uint)(wx * 27.99), 0u, 27u);
                uint age = clamp((uint)(hy * 63.0), 0u, 63u);
                uint row = (wfRow + 64u - age) & 63u;
                float mag = saturate(Waterfall[row * 32u + bin]);
                col = lerp(float3(0.02, 0.03, 0.08), lerp(float3(0.06, 0.72, 0.92), float3(1.0, 0.82, 0.24), mag), mag);
            }
        }
    }

    uint3 c8 = (uint3)clamp(col * 255.0, 0.0, 255.0);
    BasePix[pIdx] = 0xFF000000u | (c8.x << 16) | (c8.y << 8) | c8.z;
}
";
        }

        // Pass 3: [loop] + InterlockedAdd 8D E8 Polytope + Live 255-Segment Stereo Phase Lissajous Knot (Mode 3)!
        private static string BuildE8LinesHlsl(int totalEdges)
        {
            return @"
cbuffer SynthCB : register(b0) { float4 U[20]; };
StructuredBuffer<float4> E8Meta  : register(t0);
StructuredBuffer<float>  E8Disp  : register(t1);
StructuredBuffer<uint2>  E8Edges : register(t2);
StructuredBuffer<float2> ScopeWv : register(t3);
RWStructuredBuffer<uint> LineRGB : register(u0);

static const uint TOT_EDGES = " + totalEdges.ToString(CultureInfo.InvariantCulture) + @"u;

void PlotAdd(uint W, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= W || (uint)y >= H) return;
    uint b = ((uint)y * W + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

float2 ProjectE8(uint rIdx, float W, float availH, int vmode, float t)
{
    float4 m = E8Meta[rIdx];
    float disp = E8Disp[rIdx];
    float ang = t * 0.25 + U[2].y * 0.020;
    float tilt = U[2].x * 0.016;
    float ca = cos(ang), sa = sin(ang);
    float rx = m.x * ca - m.z * sa;
    float ry = m.y * cos(tilt) + m.w * sin(tilt);
    float sc = 1.0 + disp * 0.26;
    float2 ctr = (vmode == 2) ? float2(W * 0.50, availH * 0.50) : float2(W * 0.755, availH * 0.50);
    float rad  = (vmode == 2) ? (min(W, availH) * 0.43) : (min(W * 0.45, availH) * (vmode == 3 ? 0.34 : 0.43));
    return ctr + float2(rx, ry) * rad * sc;
}

[numthreads(64, 1, 1)]
void CSE8Lines(uint3 id : SV_DispatchThreadID)
{
    int vmode = (int)U[0].w;
    if (vmode == 1) return;

    uint W = (uint)U[0].x, H = (uint)U[0].y;
    uint availH = (H > 68u) ? (H - 64u) : H;
    float t = U[0].z;
    uint tid = id.x;

    if (tid < TOT_EDGES)
    {
        uint2 ed = E8Edges[tid];
        float eAmp = abs(E8Disp[ed.x]) + abs(E8Disp[ed.y]);
        uint thinMask = (vmode == 3) ? 5u : 3u;
        if ((tid % thinMask) != 0u && eAmp < 0.06) return;

        float2 a = ProjectE8(ed.x, (float)W, (float)availH, vmode, t);
        float2 b = ProjectE8(ed.y, (float)W, (float)availH, vmode, t);
        int x0 = (int)round(a.x), y0 = (int)round(a.y);
        int x1 = (int)round(b.x), y1 = (int)round(b.y);
        int dx = abs(x1 - x0), dy = -abs(y1 - y0);
        if (dx > 2048 || dy < -2048) return;
        int sx = (x0 < x1) ? 1 : -1, sy = (y0 < y1) ? 1 : -1, err = dx + dy;

        float boost = saturate(eAmp * 4.2);
        uint cr = (uint)(6.0 + 135.0 * boost);
        uint cg = (uint)(18.0 + 150.0 * boost);
        uint cb = (uint)(36.0 + 175.0 * boost);

        [loop] for (int st = 0; st < 1024; st++)
        {
            PlotAdd(W, availH, x0, y0, cr, cg, cb);
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
    else if (tid < TOT_EDGES + 240u)
    {
        uint rIdx = tid - TOT_EDGES;
        float2 p = ProjectE8(rIdx, (float)W, (float)availH, vmode, t);
        float u = E8Disp[rIdx];
        float mag = saturate(abs(u) * 4.5);
        bool isLead = ((int)rIdx == (int)U[3].w);
        uint cr = isLead ? 255u : (uint)(55.0 + 185.0 * mag);
        uint cg = isLead ? 230u : (uint)(155.0 + 90.0 * mag);
        uint cb = isLead ? 60u  : 255u;
        int cx = (int)round(p.x), cy = (int)round(p.y);
        int rad = isLead ? 5 : ((mag > 0.22) ? 3 : 2);
        [loop] for (int oy = -5; oy <= 5; oy++)
        {
            [loop] for (int ox = -5; ox <= 5; ox++)
            {
                if (ox * ox + oy * oy <= rad * rad)
                    PlotAdd(W, availH, cx + ox, cy + oy, cr, cg, cb);
            }
        }
    }
    else if (vmode == 3 && tid < TOT_EDGES + 240u + 255u)
    {
        // Live Stereo Phase Lissajous Knot Ribbon inside Sound-Shape Hologram!
        uint sIdx = tid - (TOT_EDGES + 240u);
        float2 w0 = ScopeWv[sIdx];
        float2 w1 = ScopeWv[sIdx + 1u];
        float2 ctr = float2((float)W * 0.755, (float)availH * 0.50);
        float lRad = min((float)W * 0.45, (float)availH) * 0.36;
        float2 a = ctr + float2(w0.x, -w0.y) * lRad;
        float2 b = ctr + float2(w1.x, -w1.y) * lRad;
        int x0 = (int)round(a.x), y0 = (int)round(a.y);
        int x1 = (int)round(b.x), y1 = (int)round(b.y);
        int dx = abs(x1 - x0), dy = -abs(y1 - y0);
        if (dx > 1024 || dy < -1024) return;
        int sx = (x0 < x1) ? 1 : -1, sy = (y0 < y1) ? 1 : -1, err = dx + dy;
        [loop] for (int st = 0; st < 512; st++)
        {
            PlotAdd(W, availH, x0, y0, 175u, 150u, 38u);
            PlotAdd(W, availH, x0 + 1, y0, 85u, 70u, 20u);
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
";
        }

        private static string BuildCompositeHlsl()
        {
            return @"
cbuffer SynthCB : register(b0) { float4 U[20]; };
StructuredBuffer<uint> BasePix : register(t0);
StructuredBuffer<uint> LineRGB : register(t1);
RWTexture2D<float4> OutTex : register(u0);

[numthreads(16, 16, 1)]
void CSComposite(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (id.x >= W || id.y >= H) return;
    uint pIdx = id.y * W + id.x;
    uint bp = BasePix[pIdx];
    uint lb = pIdx * 3u;
    float3 baseCol = float3((float)((bp >> 16) & 255u), (float)((bp >> 8) & 255u), (float)(bp & 255u)) / 255.0;
    float3 lineCol = float3((float)LineRGB[lb + 0u], (float)LineRGB[lb + 1u], (float)LineRGB[lb + 2u]) / 255.0;
    float3 hdr = baseCol + lineCol / (1.0 + lineCol * 0.32);
    OutTex[id.xy] = float4(saturate(hdr), 1.0);
}
";
        }

        public bool RenderGpuFrame(
            int w, int h, double simTime, int viewMode, int boundaryMode,
            int chladniM, int chladniN, int leadRootIdx,
            float peakL, float peakR, float gyroBeta, float gyroGamma,
            float[] e8Disp240, short[] scopeL256, short[] scopeR256, float[] spec32, MpeVoiceSnapshot[] voices8)
        {
            if (!IsGpuActive) return false;
            lock (_lk)
            {
                if (_dev == IntPtr.Zero || !EnsureViewportBuffers(w, h)) return false;

                float activeEn = Math.Min(1.0f, (peakL + peakR) * 0.85f);
                _waterfallRow = (_waterfallRow + 1u) & 63u;

                _cbData[0] = w; _cbData[1] = h; _cbData[2] = (float)simTime; _cbData[3] = viewMode;
                _cbData[4] = boundaryMode; _cbData[5] = 0.25f; _cbData[6] = 0.008f; _cbData[7] = _resetPlate ? 1.0f : 0.0f;
                _cbData[8] = gyroBeta; _cbData[9] = gyroGamma; _cbData[10] = peakL; _cbData[11] = peakR;
                _cbData[12] = 0.0f; _cbData[13] = 0.991f; _cbData[14] = 0.06f; _cbData[15] = leadRootIdx;

                for (int v = 0; v < 8; v++)
                {
                    int o = 16 + v * 4;
                    if (voices8 != null && v < voices8.Length && voices8[v].Active)
                    {
                        _cbData[o + 0] = (float)voices8[v].NormX;
                        _cbData[o + 1] = (float)voices8[v].NormY;
                        _cbData[o + 2] = (float)Math.Max(0.4, Math.Min(4.0, voices8[v].FreqHz / 180.0));
                        _cbData[o + 3] = (float)Math.Min(1.0, voices8[v].Pressure);
                        activeEn = Math.Max(activeEn, _cbData[o + 3]);
                    }
                    else
                    {
                        _cbData[o + 0] = 0.5f; _cbData[o + 1] = 0.5f; _cbData[o + 2] = 1.0f; _cbData[o + 3] = 0.0f;
                    }
                }

                _cbData[48] = chladniM; _cbData[49] = chladniN; _cbData[50] = activeEn; _cbData[51] = _waterfallRow;
                for (int s = 0; s < 28; s++) _cbData[52 + s] = (spec32 != null && s < spec32.Length) ? spec32[s] : 0.0f;

                for (int i = 0; i < 256; i++)
                {
                    _waveGpu[i * 2] = (scopeL256 != null ? scopeL256[i] : 0) / 32768.0f;
                    _waveGpu[i * 2 + 1] = (scopeR256 != null ? scopeR256[i] : 0) / 32768.0f;
                }

                GCHandle pE8 = GCHandle.Alloc(e8Disp240, GCHandleType.Pinned);
                try { _fUpd(_ctx, _e8DispBuf, 0u, IntPtr.Zero, pE8.AddrOfPinnedObject(), 0u, 0u); } finally { pE8.Free(); }
                GCHandle pWv = GCHandle.Alloc(_waveGpu, GCHandleType.Pinned);
                try { _fUpd(_ctx, _waveBuf, 0u, IntPtr.Zero, pWv.AddrOfPinnedObject(), 0u, 0u); } finally { pWv.Free(); }

                IntPtr cb = _cb;
                IntPtr[] zUav1 = new IntPtr[] { IntPtr.Zero };
                IntPtr[] zUav3 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zSrv1 = new IntPtr[] { IntPtr.Zero };
                IntPtr[] zSrv2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zSrv4 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };

                // Pass 1: 4 GPU Chladni 512x512 sub-steps (2 ping-pong pairs -> ultra-smooth 60/144Hz VSync!)
                int pairs = _resetPlate ? 1 : 2;
                _fSetCs(_ctx, _csStep, IntPtr.Zero, 0u);
                uint pg = (uint)(GridN / 16);
                for (int p = 0; p < pairs; p++)
                {
                    _subStepCounter++;
                    _cbData[7] = (_resetPlate && p == 0) ? 1.0f : 0.0f;
                    _cbData[12] = _subStepCounter * 0.36f;
                    UploadCb();
                    _fSetCb(_ctx, 0u, 1u, ref cb);

                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _plateSrvA });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _plateUavB }, IntPtr.Zero);
                    _fDisp(_ctx, pg, pg, 1u);
                    _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, zSrv1);

                    _subStepCounter++;
                    _cbData[7] = 0.0f;
                    _cbData[12] = _subStepCounter * 0.36f;
                    UploadCb();

                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _plateSrvB });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _plateUavA }, IntPtr.Zero);
                    _fDisp(_ctx, pg, pg, 1u);
                    _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, zSrv1);
                }
                _resetPlate = false;

                // Pass 2: Stage + Chladni Sand + Cymatic Rose Mandala + Waterfall + Clear LineRGB
                _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { _plateSrvA, _waveSrv });
                _fSetUav(_ctx, 0u, 3u, new IntPtr[] { _basePixUav, _lineRgbUav, _waterfallUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csStage, IntPtr.Zero, 0u);
                _fDisp(_ctx, (uint)((w + 15) / 16), (uint)((h + 15) / 16), 1u);
                _fSetUav(_ctx, 0u, 3u, zUav3, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 2u, zSrv2);

                // Pass 3: Atomic InterlockedAdd 8D E8 Lines + Verts + 255-Segment Stereo Phase Lissajous Knot
                uint eThreads = (uint)(E8WaveLatticeMath.EdgeA.Length + 240 + 255);
                _fSetSrv(_ctx, 0u, 4u, new IntPtr[] { _e8MetaSrv, _e8DispSrv, _edgesSrv, _waveSrv });
                _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _lineRgbUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csEdges, IntPtr.Zero, 0u);
                _fDisp(_ctx, (eThreads + 63u) / 64u, 1u, 1u);
                _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 4u, zSrv4);

                // Pass 4: Composite -> OutTex -> SwapChain
                _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { _basePixSrv, _lineRgbSrv });
                _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _outTexUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csComp, IntPtr.Zero, 0u);
                _fDisp(_ctx, (uint)((w + 15) / 16), (uint)((h + 15) / 16), 1u);
                _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 2u, zSrv2);

                if (_backBuf != IntPtr.Zero)
                {
                    _fCopy(_ctx, _backBuf, _outTex);
                    _fPresent(_swap, VSyncEnabled ? 1u : 0u, 0u);
                }
                BackendLabel = VSyncEnabled ? "RTX DirectCompute cs_5_0 VSync" : "RTX DirectCompute cs_5_0 Uncapped";
                return true;
            }
        }

        public void RenderCpuFallbackBitmap(Bitmap bmp, int mMode, int nMode, int viewMode, float[] spec32, short[] scopeL, short[] scopeR)
        {
            int w = bmp.Width, h = bmp.Height;
            if (_cpuPixels == null || _cpuPixels.Length != w * h) _cpuPixels = new int[w * h];
            int splitX = (viewMode == 3 || viewMode == 0) ? (w / 2) : w;
            Parallel.For(0, h, delegate(int py)
            {
                int row = py * w;
                double uy = (double)py / Math.Max(1, h);
                for (int px = 0; px < w; px++)
                {
                    if (px < splitX)
                    {
                        double ux = (double)px / Math.Max(1, splitX);
                        double eig = Math.Cos(mMode * Math.PI * ux) * Math.Cos(nMode * Math.PI * uy)
                                   - Math.Cos(nMode * Math.PI * ux) * Math.Cos(mMode * Math.PI * uy);
                        double sand = Math.Exp(-Math.Abs(eig) * 7.5);
                        int r = Math.Min(255, 16 + (int)(sand * 230));
                        int g = Math.Min(255, 24 + (int)(sand * 195));
                        int b = Math.Min(255, 48 + (int)(sand * 80));
                        _cpuPixels[row + px] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                    }
                    else
                    {
                        double cx = splitX + (w - splitX) * 0.5;
                        double cy = h * 0.5;
                        double mr = Math.Min(w - splitX, h) * 0.44;
                        double dx = (px - cx) / Math.Max(1.0, mr);
                        double dy = (py - cy) / Math.Max(1.0, mr);
                        double rad = Math.Sqrt(dx * dx + dy * dy);
                        double th = Math.Atan2(dy, dx);
                        int petals = Math.Max(3, mMode + nMode);
                        double sp = (spec32 != null && spec32.Length > 4) ? (spec32[1] + spec32[3]) * 0.12 : 0.08;
                        double r1 = 0.52 + 0.18 * Math.Cos(petals * th) + sp * Math.Sin((petals + 2) * th);
                        double glow = Math.Exp(-Math.Abs(rad - r1) * 26.0);
                        int r = Math.Min(255, 10 + (int)(glow * 60));
                        int g = Math.Min(255, 16 + (int)(glow * 215));
                        int b = Math.Min(255, 32 + (int)(glow * 245));
                        _cpuPixels[row + px] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                    }
                }
            });
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(_cpuPixels, 0, bd.Scan0, _cpuPixels.Length); }
            finally { bmp.UnlockBits(bd); }
        }

        private void UploadCb()
        {
            GCHandle pin = GCHandle.Alloc(_cbData, GCHandleType.Pinned);
            try { _fUpd(_ctx, _cb, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
            finally { pin.Free(); }
        }

        private bool MakeRwStructured(uint stride, uint count, out IntPtr buf, out IntPtr uav, out IntPtr srv)
        {
            buf = IntPtr.Zero; uav = IntPtr.Zero; srv = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = stride * count;
            bd.BindFlags = 0x88u;
            bd.MiscFlags = 0x40u;
            bd.StructureByteStride = stride;
            int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            if (hr < 0 || buf == IntPtr.Zero) { LastError = "Buf 0x" + hr.ToString("X8"); return false; }

            UAV_DESC ud = new UAV_DESC(); ud.Format = 0u; ud.ViewDimension = 1u; ud.NumElements = count;
            hr = _fUav(_dev, buf, ref ud, out uav);
            if (hr < 0 || uav == IntPtr.Zero)
            {
                hr = _fUavNull(_dev, buf, IntPtr.Zero, out uav);
                if (hr < 0 || uav == IntPtr.Zero) { LastError = "UAV 0x" + hr.ToString("X8"); return false; }
            }
            return CreateStructuredSrvSafe(buf, count, out srv);
        }

        private bool MakeStaticSrvFloat(float[] data, uint stride, uint count, out IntPtr buf, out IntPtr srv)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = stride * count; bd.Usage = 0u; bd.BindFlags = 8u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
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
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = stride * count; bd.Usage = 0u; bd.BindFlags = 8u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
            int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            if (hr < 0 || buf == IntPtr.Zero) { LastError = "BufU 0x" + hr.ToString("X8"); return false; }
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { _fUpd(_ctx, buf, 0u, IntPtr.Zero, pin.AddrOfPinnedObject(), 0u, 0u); }
            finally { pin.Free(); }
            return CreateStructuredSrvSafe(buf, count, out srv);
        }

        private static T Vtbl<T>(IntPtr ptr, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(ptr);
            IntPtr fn = Marshal.ReadIntPtr(vt, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        private static void Rel(IntPtr p)
        {
            if (p != IntPtr.Zero) { try { Vtbl<RelDel>(p, 2)(p); } catch { } }
        }

        public void Dispose()
        {
            lock (_lk)
            {
                IsGpuActive = false;
                Rel(_csComp); _csComp = IntPtr.Zero; Rel(_csEdges); _csEdges = IntPtr.Zero; Rel(_csStage); _csStage = IntPtr.Zero; Rel(_csStep); _csStep = IntPtr.Zero;
                Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
                Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;
                Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
                Rel(_waterfallSrv); _waterfallSrv = IntPtr.Zero; Rel(_waterfallUav); _waterfallUav = IntPtr.Zero; Rel(_waterfallBuf); _waterfallBuf = IntPtr.Zero;
                Rel(_waveSrv); _waveSrv = IntPtr.Zero; Rel(_waveBuf); _waveBuf = IntPtr.Zero;
                Rel(_edgesSrv); _edgesSrv = IntPtr.Zero; Rel(_edgesBuf); _edgesBuf = IntPtr.Zero;
                Rel(_e8DispSrv); _e8DispSrv = IntPtr.Zero; Rel(_e8DispBuf); _e8DispBuf = IntPtr.Zero;
                Rel(_e8MetaSrv); _e8MetaSrv = IntPtr.Zero; Rel(_e8MetaBuf); _e8MetaBuf = IntPtr.Zero;
                Rel(_plateSrvB); _plateSrvB = IntPtr.Zero; Rel(_plateUavB); _plateUavB = IntPtr.Zero; Rel(_plateBufB); _plateBufB = IntPtr.Zero;
                Rel(_plateSrvA); _plateSrvA = IntPtr.Zero; Rel(_plateUavA); _plateUavA = IntPtr.Zero; Rel(_plateBufA); _plateBufA = IntPtr.Zero;
                Rel(_cb); _cb = IntPtr.Zero; Rel(_backBuf); _backBuf = IntPtr.Zero; Rel(_swap); _swap = IntPtr.Zero; Rel(_ctx); _ctx = IntPtr.Zero; Rel(_dev); _dev = IntPtr.Zero;
            }
        }
    }
}
