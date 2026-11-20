using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaBraidRE
{
    public sealed class OmegaBraidReDirectComputeGpu : IDisposable
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static void TrimWorkingSetNow()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using (Process cur = Process.GetCurrentProcess())
                {
                    EmptyWorkingSet(cur.Handle);
                }
            }
            catch { }
        }

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
        private IntPtr _histBuf, _histSrv;
        private IntPtr _simMatBuf, _simMatSrv;

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

        private const string HlslBraidReCs = @"
cbuffer Params : register(b0)
{
    float4 P0; // x=W, y=H, z=Time, w=EntropyNorm
    float4 P1; // x=DeadFlagKillRatio, y=H1DefectFlag, z=MatDim, w=TopMatchSim
};
StructuredBuffer<float> Hist256  : register(t0);
StructuredBuffer<float> SimMat64 : register(t1);
RWTexture2D<float4> OutTex       : register(u0);

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint W = (uint)P0.x;
    uint H = (uint)P0.y;
    if (id.x >= W || id.y >= H) return;

    float2 uv = float2((float)id.x / max(1.0, P0.x), (float)id.y / max(1.0, P0.y));
    float3 col = float3(0.024, 0.038, 0.072);

    if ((id.x % 32u) == 0u || (id.y % 28u) == 0u)
        col += float3(0.012, 0.022, 0.042);

    if (uv.x < 0.56)
    {
        float lx = uv.x / 0.56;
        uint bin = min(255u, (uint)(lx * 256.0));
        float hv = saturate(Hist256[bin]);
        if (uv.y > 0.24)
        {
            float by = 1.0 - ((uv.y - 0.24) / 0.70);
            if (by <= hv && by >= 0.0)
            {
                float3 cLow = float3(0.05, 0.65, 0.91);
                float3 cHigh = float3(0.06, 0.85, 0.58);
                col = lerp(cLow, cHigh, saturate(by)) * (0.65 + 0.35 * sin(P0.z * 2.4 + lx * 9.0));
            }
        }
        else
        {
            float fill = saturate(P1.x);
            if (uv.y > 0.06 && uv.y < 0.18)
            {
                float3 okCol = (P1.y > 0.5) ? float3(0.94, 0.26, 0.32) : float3(0.08, 0.80, 0.54);
                col = (lx <= fill) ? okCol : float3(0.06, 0.10, 0.18);
            }
        }
    }
    else
    {
        float rx = (uv.x - 0.58) / 0.40;
        float ry = (uv.y - 0.08) / 0.84;
        if (rx >= 0.0 && rx < 1.0 && ry >= 0.0 && ry < 1.0)
        {
            uint gx = min(7u, (uint)(rx * 8.0));
            uint gy = min(7u, (uint)(ry * 8.0));
            float cx = frac(rx * 8.0);
            float cy = frac(ry * 8.0);
            if (cx > 0.06 && cx < 0.94 && cy > 0.06 && cy < 0.94)
            {
                float sim = clamp(SimMat64[gy * 8u + gx], -1.0, 1.0);
                float normSim = saturate((sim + 0.35) / 1.35);
                float3 cold = float3(0.07, 0.12, 0.24);
                float3 mid  = float3(0.04, 0.52, 0.82);
                float3 hot  = float3(0.10, 0.90, 0.56);
                col = (normSim < 0.5) ? lerp(cold, mid, normSim * 2.0) : lerp(mid, hot, (normSim - 0.5) * 2.0);
            }
        }
    }

    if (abs(uv.x - 0.568) < 0.002) col = float3(0.18, 0.42, 0.68);
    OutTex[id.xy] = float4(saturate(col), 1.0);
}
";

        public OmegaBraidReDirectComputeGpu()
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

                    byte[] src = Encoding.ASCII.GetBytes(HlslBraidReCs);
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

                    BUF_DESC cbd = new BUF_DESC();
                    cbd.ByteWidth = 32u;
                    cbd.Usage = 0u;
                    cbd.BindFlags = 4u;
                    _fBuf(_dev, ref cbd, IntPtr.Zero, out _cb);

                    CreateStructured(256, out _histBuf, out _histSrv);
                    CreateStructured(64, out _simMatBuf, out _simMatSrv);

                    if (_fGetBack(_swap, 0, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero) return false;
                    if (_fUavNull(_dev, _backBuf, IntPtr.Zero, out _backUav) < 0 || _backUav == IntPtr.Zero) return false;

                    // Reclaim transient d3dcompiler_47.dll HLSL AST pages immediately after shader creation!
                    TrimWorkingSetNow();

                    IsGpuActive = true;
                    BackendLabel = "DirectCompute cs_5_0 HLSL";
                    return true;
                }
                catch
                {
                    IsGpuActive = false;
                    BackendLabel = "GDI+ Software Fallback";
                    return false;
                }
            }
        }

        private void CreateStructured(int count, out IntPtr buf, out IntPtr srv)
        {
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = (uint)(count * 4);
            bd.Usage = 0u;
            bd.BindFlags = 8u;
            bd.MiscFlags = 0x40u;
            bd.StructureByteStride = 4u;
            _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            _fSrvNull(_dev, buf, IntPtr.Zero, out srv);
        }

        private void Upload(IntPtr dst, float[] vals)
        {
            if (dst == IntPtr.Zero || vals == null || vals.Length == 0) return;
            GCHandle h = GCHandle.Alloc(vals, GCHandleType.Pinned);
            try { _fUpd(_ctx, dst, 0u, IntPtr.Zero, h.AddrOfPinnedObject(), 0u, 0u); }
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
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { IntPtr.Zero }, IntPtr.Zero);
                    Rel(ref _backUav);
                    Rel(ref _backBuf);
                    if (_fResize(_swap, 1u, (uint)w, (uint)h, 28u, 0u) < 0) { IsGpuActive = false; return; }
                    if (_fGetBack(_swap, 0u, ref IID_Tex2D, out _backBuf) >= 0 && _backBuf != IntPtr.Zero)
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

        public void RenderBraidReSurface(float timeSec, float[] hist256, float[] simMat64, double entropyBits, float deadFlagRatio, bool hasH1Defect, float topSim)
        {
            lock (_gpuLock)
            {
                if (!IsGpuActive || _backUav == IntPtr.Zero) return;
                try
                {
                    Upload(_histBuf, hist256);
                    Upload(_simMatBuf, simMat64);

                    float[] cbData = new float[8];
                    cbData[0] = _w;
                    cbData[1] = _h;
                    cbData[2] = timeSec;
                    cbData[3] = (float)(entropyBits / 8.0);
                    cbData[4] = deadFlagRatio;
                    cbData[5] = hasH1Defect ? 1.0f : 0.0f;
                    cbData[6] = 8.0f;
                    cbData[7] = topSim;
                    Upload(_cb, cbData);

                    _fSetCb(_ctx, 0u, 1u, ref _cb);
                    _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { _histSrv, _simMatSrv });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _backUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csMain, IntPtr.Zero, 0u);
                    _fDisp(_ctx, (uint)((_w + 15) / 16), (uint)((_h + 15) / 16), 1u);
                    if (_fPresent(_swap, 0u, 0u) < 0) IsGpuActive = false;
                }
                catch { IsGpuActive = false; }
            }
        }

        public void RenderSoftwareFallback(Graphics g, int w, int h, float[] hist256, float[] simMat64, double entropyBits, float deadFlagRatio)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(8, 12, 22));
            if (w < 80 || h < 50) return;

            int splitX = (w * 56) / 100;
            using (Font f = new Font("Consolas", 10.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush bCyan = new SolidBrush(Color.FromArgb(56, 189, 248)))
            using (Brush bGreen = new SolidBrush(Color.FromArgb(16, 185, 129)))
            {
                g.DrawString(string.Format(CultureInfo.InvariantCulture,
                    ".TEXT ENTROPY ({0:0.00} b/B) | DEAD-FLAGS STRIPPED: {1:0.0}%", entropyBits, deadFlagRatio * 100.0f), f, bCyan, 10, 6);
                float bw = Math.Max(1.0f, (splitX - 24.0f) / 256.0f);
                int baseH = h - 30;
                for (int i = 0; i < 256 && hist256 != null && i < hist256.Length; i++)
                {
                    int bh = (int)(hist256[i] * (baseH - 12));
                    if (bh > 0) g.FillRectangle(bGreen, 12 + i * bw, h - 8 - bh, Math.Max(1.0f, bw), bh);
                }

                g.DrawString("8x8 E8 CROSS-PATCH SIMILARITY MATRIX", f, bCyan, splitX + 12, 6);
                int cellW = Math.Max(8, (w - splitX - 28) / 8);
                int cellH = Math.Max(8, (h - 34) / 8);
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        float sv = (simMat64 != null && (r * 8 + c) < simMat64.Length) ? simMat64[r * 8 + c] : 0.0f;
                        int gCol = (int)Math.Max(20, Math.Min(235, (sv + 0.3f) * 180.0f));
                        using (Brush cb = new SolidBrush(Color.FromArgb(14, gCol, Math.Max(40, 220 - gCol / 2))))
                        {
                            g.FillRectangle(cb, splitX + 12 + c * cellW, 24 + r * cellH, cellW - 2, cellH - 2);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_gpuLock)
            {
                IsGpuActive = false;
                Rel(ref _simMatSrv); Rel(ref _simMatBuf);
                Rel(ref _histSrv); Rel(ref _histBuf);
                Rel(ref _backUav); Rel(ref _backBuf);
                Rel(ref _csMain); Rel(ref _cb);
                Rel(ref _swap); Rel(ref _ctx); Rel(ref _dev);
            }
        }
    }
}
