using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaNativeWorkbench
{
    public sealed class OmegaWorkbenchDirectComputeGpu : IDisposable
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

        private readonly object _gpuLock = new object();
        private IntPtr _dev, _ctx, _swap, _backBuf, _backUav, _cb, _csMain;
        private IntPtr _histBuf, _histSrv;
        private IntPtr _anomBuf, _anomSrv;

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
        public bool IsGpuActive { get; private set; }
        public string BackendLabel { get; private set; }

        private const string HlslXRayCs = @"
cbuffer Params : register(b0)
{
    float4 P0; // x=W, y=H, z=Time, w=EntropyNorm(0..1)
    float4 P1; // x=NativeRamRatio, y=HasBomOrBidi, z=PassRatio, w=SimHashWave
};
StructuredBuffer<float> Hist256 : register(t0);
StructuredBuffer<float> Anom64  : register(t1);
RWTexture2D<float4> OutTex      : register(u0);

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)P0.x;
    uint H = (uint)P0.y;
    if (id.x >= W || id.y >= H) return;

    float2 uv = float2((float)id.x / max(1.0, P0.x), (float)id.y / max(1.0, P0.y));
    float3 col = float3(0.028, 0.043, 0.082);

    // Subtle diagnostic grid
    if ((id.x % 32u) == 0u || (id.y % 28u) == 0u)
        col += float3(0.014, 0.024, 0.045);

    if (uv.x < 0.62)
    {
        // Left 62%: 256-Bin GPU Byte-Entropy Spectrum + Trojan-Unicode Anomaly Strip
        float lx = uv.x / 0.62;
        uint bin = min(255u, (uint)(lx * 256.0));
        float hv = saturate(Hist256[bin]);

        if (uv.y > 0.22)
        {
            float barAreaY = 1.0 - ((uv.y - 0.22) / 0.72);
            if (barAreaY <= hv && barAreaY >= 0.0)
            {
                float3 cLow = float3(0.08, 0.72, 0.54);
                float3 cHigh = float3(0.22, 0.74, 0.98);
                col = lerp(cLow, cHigh, saturate(barAreaY)) * (0.55 + 0.45 * sin(P0.z * 2.5 + lx * 8.0));
            }
        }
        else
        {
            // Top 20%: 64-Bucket BOM / Trojan-Source Bidi Hazard Strip
            uint ab = min(63u, (uint)(lx * 64.0));
            float av = saturate(Anom64[ab]);
            col = (av > 0.5)
                ? float3(0.95, 0.24, 0.32) * (0.7 + 0.3 * sin(P0.z * 10.0))
                : ((av > 0.1) ? float3(0.95, 0.68, 0.16) : float3(0.06, 0.24, 0.22));
        }
    }
    else
    {
        // Right 38%: Native <50MB vs 1,840MB 3-App Electron Stack Comparison Bars
        float rx = (uv.x - 0.64) / 0.34;
        if (rx >= 0.0 && rx <= 1.0)
        {
            uint band = (uint)(uv.y * 4.0);
            float ly = frac(uv.y * 4.0);
            float fillAmt = 0.04;
            float3 bCol = float3(0.10, 0.82, 0.56);
            if (band == 0u) { fillAmt = clamp(P1.x, 0.025, 0.12); bCol = float3(0.12, 0.86, 0.60); }
            else if (band == 1u) { fillAmt = 0.35; bCol = float3(0.96, 0.62, 0.14); }
            else if (band == 2u) { fillAmt = 0.68; bCol = float3(0.94, 0.38, 0.22); }
            else { fillAmt = 0.96; bCol = float3(0.92, 0.22, 0.30); }

            if (ly > 0.36 && ly < 0.78)
            {
                col = (rx <= fillAmt) ? bCol : float3(0.06, 0.09, 0.16);
            }
        }
    }

    if (abs(uv.x - 0.625) < 0.002) col = float3(0.16, 0.36, 0.58);
    OutTex[id.xy] = float4(saturate(col), 1.0);
}
";

        public OmegaWorkbenchDirectComputeGpu()
        {
            BackendLabel = "CPU GDI+ Standby";
        }

        public bool Initialize(IntPtr hwnd, int width, int height)
        {
            lock (_gpuLock)
            {
                _w = Math.Max(320, width);
                _h = Math.Max(110, height);
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

                    byte[] src = Encoding.ASCII.GetBytes(HlslXRayCs);
                    IntPtr blob, err;
                    if (D3DCompile(src, (IntPtr)src.Length, "CSMain", IntPtr.Zero, IntPtr.Zero, "CSMain", "cs_5_0", 0, 0, out blob, out err) < 0 || blob == IntPtr.Zero)
                    {
                        Rel(ref err);
                        return false;
                    }
                    IntPtr ptr = Vt<BlobPtrDel>(blob, 3)(blob);
                    IntPtr sz = Vt<BlobSizeDel>(blob, 4)(blob);
                    int chr = _fCs(_dev, ptr, sz, IntPtr.Zero, out _csMain);
                    Rel(ref blob);
                    Rel(ref err);
                    if (chr < 0 || _csMain == IntPtr.Zero) return false;

                    BUF_DESC cbd = new BUF_DESC { ByteWidth = 32, Usage = 0, BindFlags = 4u };
                    _fBuf(_dev, ref cbd, IntPtr.Zero, out _cb);

                    CreateStructured(256, out _histBuf, out _histSrv);
                    CreateStructured(64, out _anomBuf, out _anomSrv);

                    if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero) return false;
                    if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) < 0 || _backUav == IntPtr.Zero) return false;

                    IsGpuActive = true;
                    BackendLabel = "DirectCompute cs_5_0 GPU HLSL cs_5_0";
                    return true;
                }
                catch
                {
                    IsGpuActive = false;
                    BackendLabel = "GDI+ Fallback";
                    return false;
                }
            }
        }

        private void CreateStructured(int count, out IntPtr buf, out IntPtr srv)
        {
            BUF_DESC bd = new BUF_DESC
            {
                ByteWidth = (uint)(count * 4),
                Usage = 0,
                BindFlags = 8u,
                MiscFlags = 0x40u,
                StructureByteStride = 4
            };
            _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            _fSrvNull(_dev, buf, IntPtr.Zero, out srv);
        }

        private void Upload(IntPtr dst, float[] vals)
        {
            if (dst == IntPtr.Zero || vals == null || vals.Length == 0) return;
            GCHandle h = GCHandle.Alloc(vals, GCHandleType.Pinned);
            try { _fUpd(_ctx, dst, 0, IntPtr.Zero, h.AddrOfPinnedObject(), 0, 0); }
            finally { h.Free(); }
        }

        public void ResizeSwapChain(int nw, int nh)
        {
            lock (_gpuLock)
            {
                if (!IsGpuActive || _swap == IntPtr.Zero) return;
                int w = Math.Max(64, nw);
                int h = Math.Max(64, nh);
                if (w == _w && h == _h) return;
                try
                {
                    _fSetUav(_ctx, 0, 1, new IntPtr[] { IntPtr.Zero }, IntPtr.Zero);
                    Rel(ref _backUav);
                    Rel(ref _backBuf);
                    if (_fResize(_swap, 1u, (uint)w, (uint)h, 28u, 0u) < 0) { IsGpuActive = false; return; }
                    if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) >= 0 && _backBuf != IntPtr.Zero)
                    {
                        if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) >= 0 && _backUav != IntPtr.Zero)
                        {
                            _w = w; _h = h;
                        }
                    }
                }
                catch { IsGpuActive = false; }
            }
        }

        public void RenderGpuXRay(float timeSec, BinaryXRaySummary xray, double nativeRamMb)
        {
            lock (_gpuLock)
            {
                if (!IsGpuActive || _backUav == IntPtr.Zero) return;
                try
                {
                    Upload(_histBuf, xray.HistogramNorm256);
                    Upload(_anomBuf, xray.AnomalyStrip64);

                    float[] cbData = new float[8];
                    cbData[0] = _w;
                    cbData[1] = _h;
                    cbData[2] = timeSec;
                    cbData[3] = (float)(xray.ShannonEntropyBits / 8.0);
                    cbData[4] = (float)(nativeRamMb / 1840.0);
                    cbData[5] = (xray.HasUtf8Bom || xray.TrojanBidiCount > 0) ? 1.0f : 0.0f;
                    cbData[6] = 1.0f;
                    cbData[7] = (float)(xray.SimHash64 & 0xFFFFUL) / 65535.0f;
                    Upload(_cb, cbData);

                    _fSetCb(_ctx, 0, 1, ref _cb);
                    _fSetSrv(_ctx, 0, 2, new IntPtr[] { _histSrv, _anomSrv });
                    _fSetUav(_ctx, 0, 1, new IntPtr[] { _backUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csMain, IntPtr.Zero, 0);
                    _fDisp(_ctx, (uint)((_w + 15) / 16), (uint)((_h + 15) / 16), 1u);
                    if (_fPresent(_swap, 0, 0) < 0) IsGpuActive = false;
                }
                catch { IsGpuActive = false; }
            }
        }

        public void RenderSoftwareFallback(Graphics g, int w, int h, BinaryXRaySummary xray, double nativeRamMb)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(7, 11, 21));
            if (w < 80 || h < 50) return;

            int splitX = (w * 62) / 100;
            using (Font f = new Font("Consolas", 10.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush bCyan = new SolidBrush(Color.FromArgb(56, 189, 248)))
            using (Brush bGreen = new SolidBrush(Color.FromArgb(16, 185, 129)))
            using (Brush bRed = new SolidBrush(Color.FromArgb(239, 68, 68)))
            using (Brush bTxt = new SolidBrush(Color.FromArgb(226, 232, 240)))
            {
                g.DrawString(string.Format(CultureInfo.InvariantCulture,
                    "256-BIN SHANNON ENTROPY X-RAY ({0:0.000} b/B) & TROJAN-UNICODE STRIP", xray.ShannonEntropyBits), f, bCyan, 10, 6);
                float bw = Math.Max(1.0f, (splitX - 24.0f) / 256.0f);
                int baseH = h - 32;
                for (int i = 0; i < 256; i++)
                {
                    int bh = (int)(xray.HistogramNorm256[i] * (baseH - 12));
                    if (bh > 0)
                        g.FillRectangle(bGreen, 12 + i * bw, h - 8 - bh, Math.Max(1.0f, bw), bh);
                }
                g.DrawString(string.Format(CultureInfo.InvariantCulture,
                    "OmegaWorkbench Native: {0:0.0} MB vs 1,840 MB (VS Code + Postman + PDF)", nativeRamMb), f, bTxt, splitX + 12, 10);
                g.FillRectangle(bGreen, splitX + 12, 30, Math.Max(8, (int)((w - splitX - 30) * (nativeRamMb / 1840.0))), 14);
                g.DrawString("VS Code + Postman + Electron PDF (~1,840 MB):", f, bTxt, splitX + 12, 54);
                g.FillRectangle(bRed, splitX + 12, 74, w - splitX - 30, 14);
            }
        }

        public void Dispose()
        {
            lock (_gpuLock)
            {
                IsGpuActive = false;
                Rel(ref _anomSrv); Rel(ref _anomBuf);
                Rel(ref _histSrv); Rel(ref _histBuf);
                Rel(ref _backUav); Rel(ref _backBuf);
                Rel(ref _csMain); Rel(ref _cb);
                Rel(ref _swap); Rel(ref _ctx); Rel(ref _dev);
            }
        }
    }
}
