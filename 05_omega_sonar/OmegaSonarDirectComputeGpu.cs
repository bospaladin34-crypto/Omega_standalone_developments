using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaSonarStudio
{
    public sealed class OmegaSonarDirectComputeGpu : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BUF_DESC { public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride; }
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
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvNullDel(IntPtr d, IntPtr res, IntPtr pNull, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavNullDel(IntPtr d, IntPtr res, IntPtr pNull, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCsDel(IntPtr d, IntPtr code, IntPtr len, IntPtr link, out IntPtr ppCs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UpdateSubDel(IntPtr c, IntPtr dst, uint sub, IntPtr box, IntPtr src, uint rowP, uint depthP);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetSrvDel(IntPtr c, uint slot, uint num, IntPtr[] srvs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetCsDel(IntPtr c, IntPtr cs, IntPtr inst, uint num);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetCbDel(IntPtr c, uint slot, uint num, ref IntPtr cb);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetUavDel(IntPtr c, uint slot, uint num, IntPtr[] uavs, IntPtr initCounts);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DispatchDel(IntPtr c, uint x, uint y, uint z);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSwapBufDel(IntPtr sw, uint buf, ref Guid iid, out IntPtr ppSurf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PresentDel(IntPtr sw, uint sync, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResizeBuffersDel(IntPtr sw, uint bufCount, uint width, uint height, uint newFormat, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPtrDel(IntPtr b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobSizeDel(IntPtr b);

        private static Guid IID_Tex2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private static T Vt<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vt, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
        }

        private static void Rel(ref IntPtr p)
        {
            if (p != IntPtr.Zero)
            {
                try { Marshal.Release(p); } catch { }
                p = IntPtr.Zero;
            }
        }

        private IntPtr _dev, _ctx, _swap, _backBuf, _backUav, _cb, _csMain;
        private IntPtr _rdBuf, _rdSrv;
        private IntPtr _corrBuf, _corrSrv;
        private IntPtr _fanBuf, _fanSrv;
        private IntPtr _trailBuf, _trailSrv;

        private CreateBufDel _fBuf;
        private CreateSrvNullDel _fSrvNull;
        private CreateUavNullDel _fUavNull;
        private CreateCsDel _fCs;
        private UpdateSubDel _fUpd;
        private CsSetSrvDel _fSetSrv;
        private CsSetCsDel _fSetCs;
        private CsSetCbDel _fSetCb;
        private CsSetUavDel _fSetUav;
        private DispatchDel _fDisp;
        private GetSwapBufDel _fGetBack;
        private PresentDel _fPresent;
        private ResizeBuffersDel _fResize;

        private int _w = 0, _h = 0;
        public volatile int ViewMode = 0;
        public bool IsGpuActive { get; private set; }
        public string BackendLabel { get; private set; }

        private const string HlslSonarCs = @"
cbuffer Params : register(b0)
{
    float4 Meta0; // x=W, y=H, z=Time, w=ViewMode
    float4 Meta1; // x=RangeM, y=VelMps, z=AzDeg, w=GolayN
    float4 WandQ; // Quat (w, x, y, z)
    float4 WandTip; // Tip (x, y, z, trigger)
};

StructuredBuffer<float> RdMap    : register(t0); // 64 * 32
StructuredBuffer<float> CorrPair : register(t1); // 0..127 Pair, 128..255 Single
StructuredBuffer<float> AzFan    : register(t2); // 0..60 (-60..+60 deg)
StructuredBuffer<float> Trail3D  : register(t3); // 96 * 3
RWTexture2D<float4> OutTex       : register(u0);

float3 HeatColor(float v)
{
    v = saturate(v);
    float3 c0 = float3(0.025, 0.045, 0.10);
    float3 c1 = float3(0.02, 0.44, 0.68);
    float3 c2 = float3(0.16, 0.88, 0.62);
    float3 c3 = float3(1.00, 0.84, 0.20);
    if (v < 0.35) return lerp(c0, c1, v / 0.35);
    if (v < 0.72) return lerp(c1, c2, (v - 0.35) / 0.37);
    return lerp(c2, c3, (v - 0.72) / 0.28);
}

float2 Proj3D(float3 p, float aspect)
{
    float camZ = p.z - 1.35;
    float invZ = 1.0 / max(0.25, -camZ);
    return float2((p.x * invZ * 1.15) / max(0.5, aspect), p.y * invZ * 1.15);
}

float DistSeg(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / max(1e-6, dot(ba, ba)));
    return length(pa - ba * h);
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)Meta0.x;
    uint H = (uint)Meta0.y;
    if (id.x >= W || id.y >= H) return;

    float2 uv = float2((id.x + 0.5) / Meta0.x, (id.y + 0.5) / Meta0.y);
    int mode = (int)Meta0.w;
    bool leftPane = (mode == 1) || (mode == 0 && uv.x < 0.495);
    bool divider  = (mode == 0 && abs(uv.x - 0.495) < 0.0025);

    if (divider)
    {
        OutTex[id.xy] = float4(0.14, 0.65, 0.88, 1.0);
        return;
    }

    if (leftPane)
    {
        float lx = (mode == 0) ? (uv.x / 0.495) : uv.x;
        if (uv.y < 0.64)
        {
            // Top-Left: Bilinearly interpolated 64x32 Range-Doppler Matrix + Target Reticle
            float ry = saturate(1.0 - (uv.y / 0.64));
            float rf = ry * 63.0;
            float df = saturate(lx) * 31.0;
            uint r0 = min(63u, (uint)rf);
            uint r1 = min(63u, r0 + 1u);
            uint d0 = min(31u, (uint)df);
            uint d1 = min(31u, d0 + 1u);
            float fr = frac(rf);
            float fd = frac(df);
            float v00 = RdMap[r0 * 32u + d0];
            float v01 = RdMap[r0 * 32u + d1];
            float v10 = RdMap[r1 * 32u + d0];
            float v11 = RdMap[r1 * 32u + d1];
            float val = lerp(lerp(v00, v01, fd), lerp(v10, v11, fd), fr);

            float3 col = HeatColor(pow(saturate(val), 0.72));
            if (abs(lx - 0.5) < 0.0035) col = lerp(col, float3(0.35, 0.78, 0.98), 0.45);
            if ((r0 % 16u) == 0u && fr < 0.12) col += float3(0.05, 0.08, 0.14);

            // Crisp target range/velocity lock ring
            float tgtRy = saturate((Meta1.x - 0.06) / 1.60);
            float tgtDx = saturate(0.5 + (Meta1.y / 2.50));
            float2 dLock = float2((lx - tgtDx) * 1.4, (ry - tgtRy));
            float rLock = length(dLock);
            if (abs(rLock - 0.028) < 0.004)
                col = lerp(col, float3(1.0, 0.92, 0.35), 0.85);

            OutTex[id.xy] = float4(saturate(col), 1.0);
            return;
        }
        else if (uv.y < 0.66)
        {
            OutTex[id.xy] = float4(0.09, 0.22, 0.36, 1.0);
            return;
        }
        else
        {
            // Bottom-Left: Golay Complementary Autocorrelation Sum (Zero Sidelobes!) vs Single-Code Sidelobes
            float gy = 1.0 - ((uv.y - 0.66) / 0.32);
            float3 bg = float3(0.03, 0.05, 0.09);
            if (gy >= 0.0 && gy <= 1.0)
            {
                uint cIdx = min(127u, (uint)(lx * 128.0));
                float pairV = abs(CorrPair[cIdx]);
                float singV = saturate(CorrPair[128u + cIdx]);
                if (abs(gy - singV * 0.88) < 0.030) bg = lerp(bg, float3(0.92, 0.36, 0.28), 0.78);
                if (gy <= pairV * 0.94) bg = lerp(bg, float3(0.18, 0.95, 0.68), 0.90);
                if (abs(gy - 0.02) < 0.008) bg += float3(0.08, 0.14, 0.22);
            }
            OutTex[id.xy] = float4(saturate(bg), 1.0);
            return;
        }
    }
    else
    {
        // Right Pane: 120-Deg Phased-Array Ultrasonic Fan + Continuous 96-Segment 3D Spatial Wand
        float rx = (mode == 0) ? ((uv.x - 0.50) / 0.50) : uv.x;
        float2 p = float2((rx - 0.5) * 2.0, (0.5 - uv.y) * 2.0);
        float aspect = (mode == 0) ? (Meta0.x * 0.5 / max(1.0, Meta0.y)) : (Meta0.x / max(1.0, Meta0.y));

        float3 col = float3(0.025, 0.04, 0.08);

        float2 fanOrigin = float2(0.0, -0.80);
        float2 dFan = float2(p.x * aspect, p.y - fanOrigin.y);
        float rPol = length(dFan);
        float angDeg = degrees(atan2(dFan.x, max(1e-4, dFan.y)));
        if (dFan.y > 0.0 && abs(angDeg) <= 60.0 && rPol <= 1.55)
        {
            float fIdx = (angDeg + 60.0) / 2.0;
            uint i0 = min(60u, (uint)fIdx);
            uint i1 = min(60u, i0 + 1u);
            float beam = lerp(AzFan[i0], AzFan[i1], frac(fIdx));
            float tgtR = saturate(Meta1.x / 1.65) * 1.50;
            float rBell = exp(-pow((rPol - tgtR) / 0.085, 2.0));
            col += float3(0.03, 0.24, 0.36) * beam * 0.55;
            col += float3(0.18, 0.92, 0.66) * beam * rBell * 0.88;
            if (abs(frac(rPol * 2.75) - 0.5) > 0.47) col += float3(0.05, 0.11, 0.18);
            if (abs(angDeg) > 59.2 || abs(angDeg) < 0.45) col += float3(0.04, 0.10, 0.18);
        }

        float2 o2 = Proj3D(float3(0.0, -0.18, -0.05), aspect);
        float3 tip3 = float3(WandTip.x, WandTip.y - 0.18, WandTip.z);
        float2 t2 = Proj3D(tip3, aspect);

        // Continuous 96-Point 3D Quaternion Wand Ribbon (k += 1u -> zero dotted gaps!)
        for (uint k = 1u; k < 96u; k += 1u)
        {
            float3 a3 = float3(Trail3D[(k - 1u) * 3u + 0u], Trail3D[(k - 1u) * 3u + 1u] - 0.18, Trail3D[(k - 1u) * 3u + 2u]);
            float3 b3 = float3(Trail3D[k * 3u + 0u], Trail3D[k * 3u + 1u] - 0.18, Trail3D[k * 3u + 2u]);
            float2 pa2 = Proj3D(a3, aspect);
            float2 pb2 = Proj3D(b3, aspect);
            float dt = DistSeg(p, pa2, pb2);
            float fade = (float)k / 96.0;
            if (dt < 0.012)
            {
                float w = fade * saturate((0.012 - dt) / 0.012) * 0.72;
                col = lerp(col, float3(0.68, 0.38, 0.99), w);
            }
        }

        float dWand = DistSeg(p, o2, t2);
        if (dWand < 0.015)
            col = lerp(col, float3(0.28, 0.85, 0.99), saturate((0.015 - dWand) / 0.015));

        float dTip = length((p - t2) * float2(aspect, 1.0));
        if (dTip < 0.042)
        {
            float3 tipClr = (WandTip.w > 0.5) ? float3(1.0, 0.78, 0.16) : float3(0.20, 0.98, 0.72);
            col = lerp(col, tipClr, saturate((0.042 - dTip) / 0.042));
        }
        else if (abs(dTip - 0.068) < 0.0055)
        {
            col += float3(0.32, 0.88, 0.98) * 0.82;
        }

        OutTex[id.xy] = float4(saturate(col), 1.0);
    }
}
";

        public OmegaSonarDirectComputeGpu()
        {
            BackendLabel = "Initializing DirectCompute cs_5_0...";
        }

        public bool Initialize(IntPtr hwnd, int width, int height)
        {
            _w = Math.Max(320, width);
            _h = Math.Max(200, height);
            try
            {
                SWAP_DESC sd = new SWAP_DESC();
                sd.Width = (uint)_w;
                sd.Height = (uint)_h;
                sd.RefreshNum = 60;
                sd.RefreshDen = 1;
                sd.Format = 28u;
                sd.SampleCount = 1;
                sd.BufferUsage = 0x20u | 0x400u;
                sd.BufferCount = 1;
                sd.OutputWindow = hwnd;
                sd.Windowed = 1;
                sd.SwapEffect = 0;

                uint[] fls = new uint[] { 0xB000u };
                uint chosen;
                int hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0, fls, 1, 7, ref sd, out _swap, out _dev, out chosen, out _ctx);
                if (hr < 0 || _dev == IntPtr.Zero) return false;

                _fBuf = Vt<CreateBufDel>(_dev, 3);
                _fSrvNull = Vt<CreateSrvNullDel>(_dev, 7);
                _fUavNull = Vt<CreateUavNullDel>(_dev, 8);
                _fCs = Vt<CreateCsDel>(_dev, 18);

                _fSetCb = Vt<CsSetCbDel>(_ctx, 71);
                _fSetSrv = Vt<CsSetSrvDel>(_ctx, 67);
                _fSetUav = Vt<CsSetUavDel>(_ctx, 68);
                _fSetCs = Vt<CsSetCsDel>(_ctx, 69);
                _fDisp = Vt<DispatchDel>(_ctx, 41);
                _fUpd = Vt<UpdateSubDel>(_ctx, 48);

                _fPresent = Vt<PresentDel>(_swap, 8);
                _fGetBack = Vt<GetSwapBufDel>(_swap, 9);
                _fResize = Vt<ResizeBuffersDel>(_swap, 13);

                byte[] src = Encoding.ASCII.GetBytes(HlslSonarCs);
                IntPtr blob, err;
                if (D3DCompile(src, (IntPtr)src.Length, "SonarCs", IntPtr.Zero, IntPtr.Zero, "CSMain", "cs_5_0", 0, 0, out blob, out err) < 0 || blob == IntPtr.Zero)
                {
                    Rel(ref err);
                    return false;
                }
                IntPtr ptr = Vt<BlobPtrDel>(blob, 3)(blob);
                IntPtr sz = Vt<BlobSizeDel>(blob, 4)(blob);
                hr = _fCs(_dev, ptr, sz, IntPtr.Zero, out _csMain);
                Rel(ref blob);
                Rel(ref err);
                if (hr < 0 || _csMain == IntPtr.Zero) return false;

                BUF_DESC cbd = new BUF_DESC { ByteWidth = 64, Usage = 0, BindFlags = 4u };
                _fBuf(_dev, ref cbd, IntPtr.Zero, out _cb);

                CreateStructuredBuffer(2048, out _rdBuf, out _rdSrv);
                CreateStructuredBuffer(256, out _corrBuf, out _corrSrv);
                CreateStructuredBuffer(64, out _fanBuf, out _fanSrv);
                CreateStructuredBuffer(288, out _trailBuf, out _trailSrv);

                if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero)
                    return false;
                if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) < 0 || _backUav == IntPtr.Zero)
                    return false;

                IsGpuActive = true;
                BackendLabel = "DirectCompute cs_5_0 GPU HLSL cs_5_0 (Non-Blocking Resizable SwapChain)";
                return true;
            }
            catch (Exception ex)
            {
                BackendLabel = "CPU GDI+ Double-Buffered (" + ex.Message + ")";
                IsGpuActive = false;
                return false;
            }
        }

        public void ResizeSwapChain(int newWidth, int newHeight)
        {
            if (!IsGpuActive || _swap == IntPtr.Zero || _fResize == null) return;
            int w = Math.Max(64, newWidth);
            int h = Math.Max(64, newHeight);
            if (w == _w && h == _h) return;
            try
            {
                IntPtr[] nullUav = new IntPtr[] { IntPtr.Zero };
                _fSetUav(_ctx, 0, 1, nullUav, IntPtr.Zero);
                Rel(ref _backUav);
                Rel(ref _backBuf);
                if (_fResize(_swap, 1u, (uint)w, (uint)h, 28u, 0u) >= 0)
                {
                    if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) >= 0 && _backBuf != IntPtr.Zero)
                    {
                        if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) >= 0 && _backUav != IntPtr.Zero)
                        {
                            _w = w;
                            _h = h;
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private void CreateStructuredBuffer(int floatCount, out IntPtr buf, out IntPtr srv)
        {
            BUF_DESC bd = new BUF_DESC
            {
                ByteWidth = (uint)(floatCount * 4),
                Usage = 0,
                BindFlags = 8u,
                MiscFlags = 0x40u,
                StructureByteStride = 4
            };
            _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            _fSrvNull(_dev, buf, IntPtr.Zero, out srv);
        }

        private static float SafeF(double v, float fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return (float)v;
        }

        private void UploadFloats(IntPtr dstBuf, float[] arr)
        {
            GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { _fUpd(_ctx, dstBuf, 0, IntPtr.Zero, h.AddrOfPinnedObject(), 0, 0); }
            finally { h.Free(); }
        }

        public void RenderFrame(float timeSec, SonarRadarSnapshot snap, Wand6DofState wand, float[] trail3D, Graphics fallbackGdi, int vpW, int vpH)
        {
            if (vpW < 32 || vpH < 32) return;
            if (!IsGpuActive || _csMain == IntPtr.Zero)
            {
                if (fallbackGdi != null) RenderSoftwareFallback(fallbackGdi, vpW, vpH, snap, wand);
                return;
            }
            if (vpW != _w || vpH != _h)
                ResizeSwapChain(vpW, vpH);
            if (_backUav == IntPtr.Zero) return;

            float[] cbFloats = new float[16]
            {
                _w, _h, timeSec, ViewMode,
                SafeF(snap.TargetRangeM, 0.45f), SafeF(snap.RadialVelocityMps, 0.0f), SafeF(snap.AzimuthDeg, 0.0f), snap.GolayLength,
                SafeF(wand.Q.W, 1.0f), SafeF(wand.Q.X, 0.0f), SafeF(wand.Q.Y, 0.0f), SafeF(wand.Q.Z, 0.0f),
                SafeF(wand.TipX, 0.0f), SafeF(wand.TipY, 0.0f), SafeF(wand.TipZ, -0.45f), wand.TriggerPressed ? 1.0f : 0.0f
            };

            float[] corr256 = new float[256];
            Array.Copy(snap.GolayPairCorr128, 0, corr256, 0, 128);
            Array.Copy(snap.SingleCodeCorr128, 0, corr256, 128, 128);

            float[] fan64 = new float[64];
            Array.Copy(snap.AzimuthFan61, 0, fan64, 0, 61);

            UploadFloats(_cb, cbFloats);
            UploadFloats(_rdBuf, snap.RangeDoppler64x32);
            UploadFloats(_corrBuf, corr256);
            UploadFloats(_fanBuf, fan64);
            UploadFloats(_trailBuf, trail3D);

            _fSetCs(_ctx, _csMain, IntPtr.Zero, 0);
            _fSetCb(_ctx, 0, 1, ref _cb);
            IntPtr[] srvs = new IntPtr[] { _rdSrv, _corrSrv, _fanSrv, _trailSrv };
            _fSetSrv(_ctx, 0, 4, srvs);
            IntPtr[] uavs = new IntPtr[] { _backUav };
            _fSetUav(_ctx, 0, 1, uavs, IntPtr.Zero);

            uint gx = (uint)((_w + 15) / 16);
            uint gy = (uint)((_h + 15) / 16);
            _fDisp(_ctx, gx, gy, 1);

            IntPtr[] nullUav = new IntPtr[] { IntPtr.Zero };
            _fSetUav(_ctx, 0, 1, nullUav, IntPtr.Zero);
            // SyncInterval = 0 prevents UI message-pump stalls during mouse drag & resize!
            _fPresent(_swap, 0, 0);
        }

        public static void RenderSoftwareFallback(Graphics g, int w, int h, SonarRadarSnapshot snap, Wand6DofState wand)
        {
            if (w < 20 || h < 20) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(7, 11, 20));
            int mid = w / 2;

            // Top-Left: Range-Doppler Heatmap cells
            int rdH = (int)(h * 0.64f);
            float cellW = (mid - 16) / 32.0f;
            float cellH = (rdH - 16) / 64.0f;
            for (int r = 0; r < 64; r++)
            {
                for (int d = 0; d < 32; d++)
                {
                    float v = snap.RangeDoppler64x32[r * 32 + d];
                    if (v < 0.08f) continue;
                    int alpha = Math.Min(255, (int)(v * 245));
                    Color c = (v > 0.72f) ? Color.FromArgb(alpha, 250, 204, 21) : Color.FromArgb(alpha, 52, 211, 153);
                    using (SolidBrush br = new SolidBrush(c))
                        g.FillRectangle(br, 8 + d * cellW, rdH - 8 - (r + 1) * cellH, Math.Max(2f, cellW), Math.Max(2f, cellH));
                }
            }
            using (Pen zLine = new Pen(Color.FromArgb(90, 56, 189, 248), 1f))
                g.DrawLine(zLine, 8 + 16 * cellW, 8, 8 + 16 * cellW, rdH - 8);

            // Bottom-Left: Single-code sidelobes vs Golay zero-sidelobe Kronecker delta
            using (Pen pSing = new Pen(Color.FromArgb(180, 239, 68, 68), 1.4f))
            using (Pen pPair = new Pen(Color.FromArgb(52, 211, 153), 2.2f))
            {
                float baseY = h - 16;
                float ampH = (h - rdH - 28) * 0.88f;
                for (int i = 1; i < 128; i++)
                {
                    float x0 = (i - 1) * (mid - 20) / 128f + 10;
                    float x1 = i * (mid - 20) / 128f + 10;
                    g.DrawLine(pSing, x0, baseY - snap.SingleCodeCorr128[i - 1] * ampH, x1, baseY - snap.SingleCodeCorr128[i] * ampH);
                    g.DrawLine(pPair, x0, baseY - Math.Abs(snap.GolayPairCorr128[i - 1]) * ampH, x1, baseY - Math.Abs(snap.GolayPairCorr128[i]) * ampH);
                }
            }

            using (Pen div = new Pen(Color.FromArgb(56, 189, 248), 2f))
                g.DrawLine(div, mid, 0, mid, h);

            // Right: Polar Phased-Array Sector + Target Lock
            float cx = mid + (w - mid) * 0.5f;
            float cy = h * 0.84f;
            float maxR = Math.Min((w - mid) * 0.46f, h * 0.72f);
            using (Pen arcPen = new Pen(Color.FromArgb(55, 56, 189, 248), 1f))
            {
                for (int ring = 1; ring <= 4; ring++)
                {
                    float rr = maxR * ring / 4.0f;
                    g.DrawArc(arcPen, cx - rr, cy - rr, rr * 2, rr * 2, 210, 120);
                }
            }
            double rad = snap.AzimuthDeg * Math.PI / 180.0;
            float rPx = (float)(Math.Min(1.65, Math.Max(0.06, snap.TargetRangeM)) / 1.65 * maxR);
            float tx = cx + (float)(Math.Sin(rad) * rPx);
            float ty = cy - (float)(Math.Cos(rad) * rPx);
            using (Pen beam = new Pen(Color.FromArgb(52, 211, 153), 2.5f))
                g.DrawLine(beam, cx, cy, tx, ty);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(250, 204, 21)))
                g.FillEllipse(br, tx - 7, ty - 7, 14, 14);
        }

        public static void SaveDiagnosticSnapshotPng(string outPath, SonarRadarSnapshot snap, Wand6DofState wand)
        {
            using (Bitmap bm = new Bitmap(1024, 576, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bm))
            {
                RenderSoftwareFallback(g, 1024, 576, snap, wand);
                using (Font fn = new Font("Consolas", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(56, 189, 248)))
                {
                    g.DrawString(
                        "ACT-Omega Sonar v1.1.0 GM :: R=" + snap.TargetRangeM.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                        "m | V=" + snap.RadialVelocityMps.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture) +
                        "m/s | Az=" + snap.AzimuthDeg.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture) +
                        "deg | Gesture=" + snap.ActiveGesture,
                        fn, sb, 14, 10);
                }
                bm.Save(outPath, ImageFormat.Png);
            }
        }

        public void Dispose()
        {
            Rel(ref _trailSrv); Rel(ref _trailBuf);
            Rel(ref _fanSrv); Rel(ref _fanBuf);
            Rel(ref _corrSrv); Rel(ref _corrBuf);
            Rel(ref _rdSrv); Rel(ref _rdBuf);
            Rel(ref _cb); Rel(ref _csMain);
            Rel(ref _backUav); Rel(ref _backBuf);
            Rel(ref _ctx); Rel(ref _dev); Rel(ref _swap);
        }
    }
}
