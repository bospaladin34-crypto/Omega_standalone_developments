using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaConductorStudio
{
    public sealed class ConductorTargetTelemetry
    {
        public int Index;
        public string Name = "";
        public int Port;
        public string Role = "";
        public bool Online = false;
        public long PacketsRouted = 0;
        public double LastRttMs = 0.0;
        public string LastAction = "Standby";
    }

    public sealed class ConductorHudSnapshot
    {
        public QuatS3 Quat = QuatS3.Identity;
        public HopfRayS2 Hopf;
        public double YawDeg, PitchDeg, RollDeg;
        public double RangeM = 0.62;
        public double DopplerMps = 0.0;
        public double AzimuthDeg = 0.0;
        public SpatialGestureKind Gesture = SpatialGestureKind.CONDUCT_GLIDE;
        public long GestureCount = 0;
        public int E8RootIndex = 0;
        public double E8SqnrDb = 23.4;
        public int GolayHealedBits = 0;
        public long ShmSequence = 0;
        public int ActiveShmSlot = 0;
        public bool SheafH1Zero = true;
        public string SheafSummary = "H^1 = 0 EXACT";
        public string InputSource = "15.965Hz Lissajous Baton + Sonar Fusion";
        public float[] TrailXY = new float[128]; // 64 points * (x, y) in [-1..1]
        public float[] RingOccupancy = new float[64];
        public ConductorTargetTelemetry[] Targets = new ConductorTargetTelemetry[4];
    }

    public sealed class OmegaConductorDirectComputeGpu : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BUF_DESC
        {
            public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SWAP_DESC
        {
            public uint Width, Height, RefreshNum, RefreshDen, Format, ScanlineOrdering, Scaling;
            public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
            public IntPtr OutputWindow;
            public int Windowed;
            public uint SwapEffect, Flags;
        }

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3D11CreateDeviceAndSwapChain(
            IntPtr pAdapter, int driverType, IntPtr sw, uint flags,
            uint[] pFL, uint nFL, uint sdkVer, ref SWAP_DESC pDesc,
            out IntPtr ppSwap, out IntPtr ppDev, out uint pChosenFL, out IntPtr ppCtx);

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(
            byte[] pSrc, IntPtr srcLen, string srcName, IntPtr pDefs, IntPtr pInc,
            string entry, string target, uint f1, uint f2, out IntPtr ppCode, out IntPtr ppErr);

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

        private readonly object _gpuLock = new object();
        private IntPtr _dev, _ctx, _swap, _backBuf, _backUav, _cb, _csMain;
        private IntPtr _trailBuf, _trailSrv;
        private IntPtr _ringBuf, _ringSrv;

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

        private const string HlslConductorCs = @"
cbuffer Params : register(b0)
{
    float4 Meta0; // x=W, y=H, z=Time, w=ViewMode
    float4 Meta1; // x=HopfRx, y=HopfRy, z=HopfRz, w=FiberTwist
    float4 Meta2; // x=RangeM, y=DopplerMps, z=GestureKind, w=SheafOk
    float4 Meta3; // x=OnAV, y=OnSynth, z=OnPenrose, w=OnTensor
};

StructuredBuffer<float> TrailXY : register(t0); // 128 floats (64 * [x, y])
StructuredBuffer<float> ShmRing : register(t1); // 64 floats (slot energy)
RWTexture2D<float4> OutTex      : register(u0);

float DistSeg(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(1e-6, dot(ba, ba)));
    return length(pa - ba * h);
}

float3 GestureColor(int g)
{
    if (g == 1) return float3(0.22, 0.78, 0.98); // ORBIT_SWEEP (Cyan)
    if (g == 2) return float3(1.00, 0.84, 0.22); // DOUBLE_TAP_PULSE (Gold)
    if (g == 3) return float3(0.76, 0.46, 0.99); // HOVER_LOCK (Violet)
    if (g == 4) return float3(0.98, 0.44, 0.56); // SURGE_CRESCENDO (Coral)
    return float3(0.16, 0.94, 0.66);             // CONDUCT_GLIDE (Emerald)
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)Meta0.x;
    uint H = (uint)Meta0.y;
    if (id.x >= W || id.y >= H) return;

    float2 uv = float2((id.x + 0.5) / Meta0.x, (id.y + 0.5) / Meta0.y);
    int mode = (int)Meta0.w;
    bool leftPane = (mode == 1) || (mode == 0 && uv.x < 0.52);
    bool divider  = (mode == 0 && abs(uv.x - 0.52) < 0.0025);

    if (divider)
    {
        OutTex[id.xy] = float4(0.20, 0.72, 0.94, 1.0);
        return;
    }

    float t = Meta0.z;
    int gest = (int)(Meta2.z + 0.5);
    float3 gCol = GestureColor(gest);

    if (leftPane)
    {
        // Left Pane: 3D Hopf Fibration S^3 -> S^2 Conduction Dome + 4 Station Portals + 64-Pt Wand Trail
        float lx = (mode == 0) ? (uv.x / 0.52) : uv.x;
        float aspect = (mode == 0) ? (Meta0.x * 0.52 / max(1.0, Meta0.y)) : (Meta0.x / max(1.0, Meta0.y));
        float2 p = float2((lx - 0.5) * 2.0 * aspect, (0.5 - uv.y) * 2.0);

        float3 col = float3(0.024, 0.038, 0.074);
        float rad = length(p);

        // S^2 celestial conduction dome & Hopf fiber rings
        if (rad < 0.82)
        {
            float DomeZ = sqrt(max(0.0, 0.6724 - rad * rad));
            col += float3(0.02, 0.05, 0.10) * (0.4 + 0.6 * DomeZ);
            float lat = abs(frac(rad * 4.2 - Meta1.w * 0.159) - 0.5);
            if (lat > 0.47) col += gCol * 0.09;
        }
        if (abs(rad - 0.82) < 0.007) col += gCol * 0.62;
        if (abs(p.x) < 0.003 && rad < 0.82) col += float3(0.08, 0.18, 0.30);
        if (abs(p.y) < 0.003 && rad < 0.82) col += float3(0.08, 0.18, 0.30);

        // 4 Downstream Station Portals on outer ring: AV(:19779), Synth(:19782), Penrose(:19785), Tensor(:19791)
        float2 portals[4];
        portals[0] = float2(-0.74 * aspect,  0.70);
        portals[1] = float2( 0.74 * aspect,  0.70);
        portals[2] = float2(-0.74 * aspect, -0.70);
        portals[3] = float2( 0.74 * aspect, -0.70);

        float onFlags[4];
        onFlags[0] = Meta3.x;
        onFlags[1] = Meta3.y;
        onFlags[2] = Meta3.z;
        onFlags[3] = Meta3.w;

        float3 pCols[4];
        pCols[0] = float3(0.68, 0.42, 0.98); // OmegaAV
        pCols[1] = float3(0.96, 0.32, 0.62); // OmegaSynth
        pCols[2] = float3(0.22, 0.80, 0.98); // OmegaPenrose
        pCols[3] = float3(0.16, 0.94, 0.66); // OmegaTensor

        float2 tip = float2(Meta1.x * 0.76, Meta1.y * 0.76);

        // Animated Golay packet beams from Wand Tip -> Online Stations!
        for (uint k = 0u; k < 4u; k++)
        {
            float db = DistSeg(p, tip, portals[k]);
            float isOn = onFlags[k];
            if (db < 0.014)
            {
                float2 ba = portals[k] - tip;
                float uSeg = saturate(dot(p - tip, ba) / max(1e-5, dot(ba, ba)));
                float pkt = exp(-pow((frac(uSeg * 4.0 - t * 2.6 + k * 0.25) - 0.5) / 0.14, 2.0));
                float3 beamC = (isOn > 0.5) ? lerp(gCol, pCols[k], uSeg) : float3(0.18, 0.22, 0.30);
                col += beamC * ((isOn > 0.5) ? (0.30 + 0.70 * pkt) : 0.14) * saturate((0.014 - db) / 0.014);
            }
            float dp = length(p - portals[k]);
            if (dp < 0.065)
                col = lerp(col, (isOn > 0.5) ? pCols[k] : float3(0.25, 0.30, 0.38), saturate((0.065 - dp) / 0.065));
            else if (abs(dp - 0.088) < 0.006 && isOn > 0.5)
                col += pCols[k] * (0.6 + 0.4 * sin(t * 5.0 + k));
        }

        // 64-Sample S^3 Hopf Wand Ribbon Trail
        for (uint s = 1u; s < 64u; s++)
        {
            float2 a = float2(TrailXY[(s - 1u) * 2u + 0u], TrailXY[(s - 1u) * 2u + 1u]) * 0.76;
            float2 b = float2(TrailXY[s * 2u + 0u], TrailXY[s * 2u + 1u]) * 0.76;
            float dSeg = DistSeg(p, a, b);
            float age = (float)s / 64.0;
            float wThk = 0.005 + 0.016 * age;
            if (dSeg < wThk)
            {
                col += gCol * age * saturate((wThk - dSeg) / wThk) * 0.78;
            }
        }

        // Active Wand Reticle + Fiber Twist Crosshair
        float dTip = length(p - tip);
        if (dTip < 0.042)
            col = lerp(col, float3(1.0, 0.98, 0.90), saturate((0.042 - dTip) / 0.042));
        else if (abs(dTip - 0.076 - (gest == 2 ? 0.03 * sin(t * 24.0) : 0.0)) < 0.008)
            col += gCol * 0.95;

        OutTex[id.xy] = float4(saturate(col), 1.0);
        return;
    }
    else
    {
        // Right Pane: Top = 19 kHz Golay Range-Doppler Gesture Scope | Bottom = 64-Slot Seqlock SHM Matrix
        float rx = (mode == 0) ? ((uv.x - 0.52) / 0.48) : uv.x;
        float3 bg = float3(0.026, 0.038, 0.072);

        if (uv.y < 0.66)
        {
            float sy = uv.y / 0.66;
            float aspect = (mode == 0) ? (Meta0.x * 0.48 / max(1.0, Meta0.y * 0.66)) : (Meta0.x / max(1.0, Meta0.y * 0.66));
            float2 q = float2((rx - 0.5) * 2.0 * aspect, (0.5 - sy) * 2.0);
            float r = length(q);

            if (abs(r - 0.82) < 0.007 || abs(r - 0.54) < 0.004 || abs(r - 0.27) < 0.004)
                bg += float3(0.08, 0.24, 0.36);
            if (abs(q.x) < 0.004 || abs(q.y) < 0.004)
                bg += float3(0.06, 0.16, 0.26);

            // Rotating radar sweep + live Range/Doppler target blip
            float ang = atan2(q.y, q.x);
            float sweep = frac((ang + 3.14159265) / 6.2831853 - t * 0.45);
            if (r < 0.82 && sweep < 0.18)
                bg += gCol * (0.18 - sweep) * 0.9;

            float blipX = clamp(Meta2.y * 0.55, -0.75, 0.75);
            float blipY = clamp((Meta2.x - 0.85) * 0.65, -0.75, 0.75);
            float dBlip = length(q - float2(blipX, blipY));
            if (dBlip < 0.052)
                bg = lerp(bg, gCol, saturate((0.052 - dBlip) / 0.052));
            else if (abs(dBlip - 0.09) < 0.007)
                bg += float3(1.0, 0.92, 0.36);

            OutTex[id.xy] = float4(saturate(bg), 1.0);
            return;
        }
        else if (uv.y < 0.68)
        {
            OutTex[id.xy] = float4(0.12, 0.30, 0.48, 1.0);
            return;
        }
        else
        {
            // 64-Slot (16 x 4) Zero-Copy Shared Memory Seqlock Occupancy Grid
            float gy = (uv.y - 0.70) / 0.26;
            if (gy >= 0.0 && gy <= 1.0 && rx >= 0.04 && rx <= 0.96)
            {
                float gx = (rx - 0.04) / 0.92;
                uint colIdx = min(15u, (uint)(gx * 16.0));
                uint rowIdx = min(3u, (uint)(gy * 4.0));
                uint slot = rowIdx * 16u + colIdx;
                float cx = frac(gx * 16.0);
                float cy = frac(gy * 4.0);
                if (cx > 0.10 && cx < 0.90 && cy > 0.14 && cy < 0.86)
                {
                    float occ = saturate(ShmRing[slot]);
                    bg = lerp(float3(0.07, 0.11, 0.19), gCol, 0.25 + 0.75 * occ);
                }
            }
            OutTex[id.xy] = float4(saturate(bg), 1.0);
            return;
        }
    }
}
";

        public OmegaConductorDirectComputeGpu()
        {
            BackendLabel = "Initializing DirectCompute cs_5_0...";
        }

        public bool Initialize(IntPtr hwnd, int width, int height)
        {
            lock (_gpuLock)
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

                    if (!CompileComputeShader(HlslConductorCs, "CSMain", out _csMain))
                        return false;

                    BUF_DESC cbd = new BUF_DESC { ByteWidth = 64, Usage = 0, BindFlags = 4u };
                    _fBuf(_dev, ref cbd, IntPtr.Zero, out _cb);

                    CreateStructuredBuffer(128, out _trailBuf, out _trailSrv);
                    CreateStructuredBuffer(64, out _ringBuf, out _ringSrv);

                    if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero)
                        return false;
                    if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) < 0 || _backUav == IntPtr.Zero)
                        return false;

                    IsGpuActive = true;
                    BackendLabel = "DirectCompute cs_5_0 GPU HLSL cs_5_0 (Hopf S^3->S^2 Dome + Seqlock SHM Matrix)";
                    return true;
                }
                catch (Exception ex)
                {
                    BackendLabel = "CPU GDI+ Double-Buffered (" + ex.Message + ")";
                    IsGpuActive = false;
                    return false;
                }
            }
        }

        private bool CompileComputeShader(string hlsl, string entry, out IntPtr outCs)
        {
            outCs = IntPtr.Zero;
            byte[] src = Encoding.ASCII.GetBytes(hlsl);
            IntPtr blob, err;
            if (D3DCompile(src, (IntPtr)src.Length, entry, IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 0, 0, out blob, out err) < 0 || blob == IntPtr.Zero)
            {
                Rel(ref err);
                return false;
            }
            IntPtr ptr = Vt<BlobPtrDel>(blob, 3)(blob);
            IntPtr sz = Vt<BlobSizeDel>(blob, 4)(blob);
            int hr = _fCs(_dev, ptr, sz, IntPtr.Zero, out outCs);
            Rel(ref blob);
            Rel(ref err);
            return (hr >= 0 && outCs != IntPtr.Zero);
        }

        private void CreateStructuredBuffer(int floatCount, out IntPtr buf, out IntPtr srv)
        {
            BUF_DESC bd = new BUF_DESC
            {
                ByteWidth = (uint)(floatCount * 4),
                Usage = 0,
                BindFlags = 8u,
                CPUAccessFlags = 0,
                MiscFlags = 0x40u,
                StructureByteStride = 4
            };
            _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            _fSrvNull(_dev, buf, IntPtr.Zero, out srv);
        }

        private void UploadFloats(IntPtr dstBuf, float[] arr)
        {
            if (dstBuf == IntPtr.Zero || arr == null || arr.Length == 0) return;
            GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try
            {
                _fUpd(_ctx, dstBuf, 0, IntPtr.Zero, h.AddrOfPinnedObject(), 0, 0);
            }
            finally
            {
                h.Free();
            }
        }

        public void ResizeSwapChain(int newWidth, int newHeight)
        {
            lock (_gpuLock)
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
                    int hr = _fResize(_swap, 1u, (uint)w, (uint)h, 28u, 0u);
                    if (hr < 0)
                    {
                        if (hr == unchecked((int)0x887A0005) || hr == unchecked((int)0x887A0007))
                        {
                            IsGpuActive = false;
                            BackendLabel = "GDI+ Auto-Fallback (DXGI Device Reset Shield)";
                        }
                        return;
                    }
                    if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) >= 0 && _backBuf != IntPtr.Zero)
                    {
                        if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) >= 0 && _backUav != IntPtr.Zero)
                        {
                            _w = w;
                            _h = h;
                        }
                    }
                }
                catch
                {
                    IsGpuActive = false;
                }
            }
        }

        public void RenderFrame(float timeSec, ConductorHudSnapshot snap, int vpW, int vpH)
        {
            if (vpW > 64 && vpH > 64 && (vpW != _w || vpH != _h))
                ResizeSwapChain(vpW, vpH);

            lock (_gpuLock)
            {
                if (!IsGpuActive || _backUav == IntPtr.Zero || snap == null) return;
                try
                {
                    UploadFloats(_trailBuf, snap.TrailXY);
                    UploadFloats(_ringBuf, snap.RingOccupancy);

                    float onAv = (snap.Targets[0] != null && snap.Targets[0].Online) ? 1.0f : 0.0f;
                    float onSy = (snap.Targets[1] != null && snap.Targets[1].Online) ? 1.0f : 0.0f;
                    float onPn = (snap.Targets[2] != null && snap.Targets[2].Online) ? 1.0f : 0.0f;
                    float onTn = (snap.Targets[3] != null && snap.Targets[3].Online) ? 1.0f : 0.0f;

                    float[] cb16 = new float[16]
                    {
                        _w, _h, timeSec, ViewMode,
                        (float)snap.Hopf.Rx, (float)snap.Hopf.Ry, (float)snap.Hopf.Rz, (float)snap.Hopf.FiberTwistRad,
                        (float)snap.RangeM, (float)snap.DopplerMps, (int)snap.Gesture, snap.SheafH1Zero ? 1.0f : 0.0f,
                        onAv, onSy, onPn, onTn
                    };
                    UploadFloats(_cb, cb16);

                    IntPtr[] srvs = new IntPtr[] { _trailSrv, _ringSrv };
                    IntPtr[] uavs = new IntPtr[] { _backUav };
                    _fSetCb(_ctx, 0, 1, ref _cb);
                    _fSetSrv(_ctx, 0, 2, srvs);
                    _fSetUav(_ctx, 0, 1, uavs, IntPtr.Zero);
                    _fSetCs(_ctx, _csMain, IntPtr.Zero, 0);

                    uint gx = (uint)((_w + 15) / 16);
                    uint gy = (uint)((_h + 15) / 16);
                    _fDisp(_ctx, gx, gy, 1u);

                    IntPtr[] clrUav = new IntPtr[] { IntPtr.Zero };
                    _fSetUav(_ctx, 0, 1, clrUav, IntPtr.Zero);
                    int hr = _fPresent(_swap, 0u, 0u);
                    if (hr == unchecked((int)0x887A0005) || hr == unchecked((int)0x887A0007))
                    {
                        IsGpuActive = false;
                        BackendLabel = "GDI+ Auto-Fallback (DXGI Device Reset Shield)";
                    }
                }
                catch
                {
                    IsGpuActive = false;
                }
            }
        }

        public void RenderSoftwareFallback(Graphics g, int w, int h, float timeSec, ConductorHudSnapshot snap)
        {
            if (g == null || snap == null || w < 32 || h < 32) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(8, 12, 22));

            int midX = (w * 52) / 100;
            float cx = midX * 0.5f;
            float cy = h * 0.5f;
            float rad = Math.Min(midX, h) * 0.38f;

            Color gCol = Color.FromArgb(42, 238, 168);
            if (snap.Gesture == SpatialGestureKind.ORBIT_SWEEP) gCol = Color.FromArgb(56, 189, 248);
            else if (snap.Gesture == SpatialGestureKind.DOUBLE_TAP_PULSE) gCol = Color.FromArgb(250, 204, 21);
            else if (snap.Gesture == SpatialGestureKind.HOVER_LOCK) gCol = Color.FromArgb(192, 132, 252);
            else if (snap.Gesture == SpatialGestureKind.SURGE_CRESCENDO) gCol = Color.FromArgb(244, 92, 140);

            using (Pen domePen = new Pen(gCol, 1.8f))
            {
                g.DrawEllipse(domePen, cx - rad, cy - rad, rad * 2f, rad * 2f);
            }

            float tipX = cx + (float)snap.Hopf.Rx * rad * 0.92f;
            float tipY = cy - (float)snap.Hopf.Ry * rad * 0.92f;

            PointF[] portals = new PointF[4]
            {
                new PointF(42f, 40f),
                new PointF(midX - 42f, 40f),
                new PointF(42f, h - 40f),
                new PointF(midX - 42f, h - 40f)
            };

            using (Font fn = new Font("Consolas", 10.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush tx = new SolidBrush(Color.White))
            {
                for (int k = 0; k < 4; k++)
                {
                    bool on = snap.Targets[k] != null && snap.Targets[k].Online;
                    Color pc = on ? gCol : Color.FromArgb(80, 95, 115);
                    using (Pen bp = new Pen(Color.FromArgb(on ? 190 : 70, pc), 2.0f))
                        g.DrawLine(bp, tipX, tipY, portals[k].X, portals[k].Y);
                    using (SolidBrush sb = new SolidBrush(pc))
                        g.FillEllipse(sb, portals[k].X - 9f, portals[k].Y - 9f, 18f, 18f);
                    if (snap.Targets[k] != null)
                        g.DrawString(snap.Targets[k].Name, fn, tx, portals[k].X - 28f, portals[k].Y + 12f);
                }
            }

            using (Pen trailP = new Pen(Color.FromArgb(190, gCol), 2.2f))
            {
                for (int s = 1; s < 64; s++)
                {
                    float x0 = cx + snap.TrailXY[(s - 1) * 2 + 0] * rad * 0.92f;
                    float y0 = cy - snap.TrailXY[(s - 1) * 2 + 1] * rad * 0.92f;
                    float x1 = cx + snap.TrailXY[s * 2 + 0] * rad * 0.92f;
                    float y1 = cy - snap.TrailXY[s * 2 + 1] * rad * 0.92f;
                    g.DrawLine(trailP, x0, y0, x1, y1);
                }
            }

            using (SolidBrush wb = new SolidBrush(Color.White))
                g.FillEllipse(wb, tipX - 7f, tipY - 7f, 14f, 14f);

            // Right Pane radar + 64-slot ring
            float rcx = midX + (w - midX) * 0.5f;
            float rcy = h * 0.34f;
            float rrad = Math.Min(w - midX, h * 0.62f) * 0.38f;
            using (Pen rp = new Pen(Color.FromArgb(56, 189, 248), 1.4f))
                g.DrawEllipse(rp, rcx - rrad, rcy - rrad, rrad * 2f, rrad * 2f);
            float bx = rcx + (float)Math.Max(-0.9, Math.Min(0.9, snap.DopplerMps * 0.65)) * rrad;
            float by = rcy - (float)Math.Max(-0.9, Math.Min(0.9, (snap.RangeM - 0.85) * 0.75)) * rrad;
            using (SolidBrush bb = new SolidBrush(gCol))
                g.FillEllipse(bb, bx - 7f, by - 7f, 14f, 14f);

            int gx0 = midX + 22;
            int gy0 = (h * 70) / 100;
            int cw = Math.Max(8, (w - gx0 - 22) / 16);
            int ch = Math.Max(8, (h - gy0 - 18) / 4);
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 16; c++)
                {
                    int idx = r * 16 + c;
                    int alpha = 55 + (int)(Math.Max(0f, Math.Min(1f, snap.RingOccupancy[idx])) * 195f);
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(alpha, gCol)))
                        g.FillRectangle(cb, gx0 + c * cw, gy0 + r * ch, cw - 3, ch - 3);
                }
            }
        }

        public string SaveDiagnosticSnapshotPng(string baseDir, ConductorHudSnapshot snap)
        {
            string expDir = Path.Combine(baseDir, "exports");
            Directory.CreateDirectory(expDir);
            string outPath = Path.Combine(expDir, "omega_conductor_snapshot_" + DateTime.Now.ToString("HHmmss") + ".png");
            using (Bitmap bmp = new Bitmap(1280, 720))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                RenderSoftwareFallback(g, 1280, 720, 1.5f, snap);
                using (Font hdrF = new Font("Consolas", 14.0f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush br = new SolidBrush(Color.FromArgb(52, 211, 153)))
                {
                    g.DrawString(
                        string.Format(CultureInfo.InvariantCulture,
                            "OMEGACONDUCTOR v1.0.0 GM (:19790) | Gesture={0} | SHM Seq=#{1} | {2}",
                            snap.Gesture, snap.ShmSequence, snap.SheafSummary),
                        hdrF, br, 18, 692);
                }
                bmp.Save(outPath, ImageFormat.Png);
            }
            return outPath;
        }

        public void Dispose()
        {
            lock (_gpuLock)
            {
                IsGpuActive = false;
                Rel(ref _ringSrv); Rel(ref _ringBuf);
                Rel(ref _trailSrv); Rel(ref _trailBuf);
                Rel(ref _backUav); Rel(ref _backBuf);
                Rel(ref _cb); Rel(ref _csMain);
                Rel(ref _swap); Rel(ref _ctx); Rel(ref _dev);
            }
        }
    }
}
