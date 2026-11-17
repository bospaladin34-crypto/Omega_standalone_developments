using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaSheafStudio
{
    public sealed class OmegaSheafGpuEngine : IDisposable
    {
        public const int MaxGpuNodes = 128;
        public const int MaxGpuEdges = 512;

        [StructLayout(LayoutKind.Sequential)] private struct BUF_DESC { public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride; }
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
        private static extern int D3D11CreateDeviceAndSwapChain(IntPtr pAdapter, int driverType, IntPtr sw, uint flags, uint[] pFL, uint nFL, uint sdkVer, ref SWAP_DESC pDesc, out IntPtr ppSwap, out IntPtr ppDev, out uint pChosenFL, out IntPtr ppCtx);

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] pSrc, IntPtr srcLen, string srcName, IntPtr pDefs, IntPtr pInc, string entry, string target, uint f1, uint f2, out IntPtr ppCode, out IntPtr ppErr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBufDel(IntPtr d, ref BUF_DESC desc, IntPtr pInit, out IntPtr ppB);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTex2DDel(IntPtr d, ref TEX2D_DESC desc, IntPtr pInit, out IntPtr ppT);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvDel(IntPtr d, IntPtr res, ref SRV_DESC desc, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvNullDel(IntPtr d, IntPtr res, IntPtr pNull, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavDel(IntPtr d, IntPtr res, ref UAV_DESC desc, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavNullDel(IntPtr d, IntPtr res, IntPtr pNull, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCsDel(IntPtr d, IntPtr code, IntPtr len, IntPtr link, out IntPtr ppCs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapDel(IntPtr c, IntPtr res, uint sub, uint typ, uint flg, out MAP_SUB m);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapDel(IntPtr c, IntPtr res, uint sub);
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

        private static Guid IID_Tex2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

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
        private IntPtr _csField, _csEdges, _csNodes, _csComp;

        private IntPtr _nodeBuf, _nodeSrv;
        private IntPtr _edgeBuf, _edgeSrv;
        private IntPtr _labelBuf, _labelSrv;
        private IntPtr _fontBuf, _fontSrv;
        private IntPtr _barBuf, _barSrv;
        private IntPtr _basePixBuf, _basePixSrv, _basePixUav;
        private IntPtr _lineRgbBuf, _lineRgbSrv, _lineRgbUav;
        private IntPtr _rbBuf, _rbUav, _stgBuf;
        private IntPtr _outTex, _outTexUav;

        private CreateBufDel _fBuf; private CreateTex2DDel _fTex;
        private CreateSrvDel _fSrv; private CreateSrvNullDel _fSrvNull;
        private CreateUavDel _fUav; private CreateUavNullDel _fUavNull;
        private CreateCsDel _fCs; private MapDel _fMap; private UnmapDel _fUnmap;
        private UpdateSubDel _fUpd; private CsSetSrvDel _fSetSrv; private CsSetCsDel _fSetCs;
        private CsSetCbDel _fSetCb; private CsSetUavDel _fSetUav; private DispatchDel _fDisp;
        private CopyResDel _fCopy; private GetSwapBufDel _fGetBack; private PresentDel _fPresent; private ResizeBufDel _fResize;

        private int _vpW = 0, _vpH = 0;
        private int _upNodeCount = 0, _upEdgeCount = 0, _upBarCount = 0;
        private readonly float[] _cbData = new float[64];
        private readonly float[] _nodePack = new float[MaxGpuNodes * 12];
        private readonly float[] _edgePack = new float[MaxGpuEdges * 8];
        private readonly uint[] _labelPack = new uint[MaxGpuNodes * 6];
        private readonly float[] _barPack = new float[32 * 4];

        public readonly float[] ScreenNodeX = new float[MaxGpuNodes];
        public readonly float[] ScreenNodeY = new float[MaxGpuNodes];
        public readonly float[] ScreenNodeR = new float[MaxGpuNodes];

        public volatile bool VSyncEnabled = true;
        public volatile bool ShowGpuLabels = true;
        public volatile int HodgeColorMode = 0;
        public bool IsGpuActive { get; private set; }
        public string BackendLabel { get; private set; }
        public string LastError { get; private set; }

        public OmegaSheafGpuEngine()
        {
            BackendLabel = "Initializing RTX DirectCompute...";
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
                    sd.Format = 28u; sd.SampleCount = 1u;
                    sd.BufferUsage = 0x20u; sd.BufferCount = 2u;
                    sd.OutputWindow = hwnd; sd.Windowed = 1; sd.SwapEffect = 0u;

                    uint[] fls = new uint[] { 0xb000u, 0xa100u, 0xa000u };
                    uint chosen;
                    int hr = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, (uint)fls.Length, 7u, ref sd, out _swap, out _dev, out chosen, out _ctx);
                    if (hr < 0 || _dev == IntPtr.Zero || _swap == IntPtr.Zero)
                    {
                        LastError = "D3D11Swap 0x" + hr.ToString("X8");
                        BackendLabel = "CPU Fallback (" + LastError + ")";
                        return false;
                    }

                    _fBuf = Vt<CreateBufDel>(_dev, 3);
                    _fTex = Vt<CreateTex2DDel>(_dev, 5);
                    _fSrv = Vt<CreateSrvDel>(_dev, 7);
                    _fSrvNull = Vt<CreateSrvNullDel>(_dev, 7);
                    _fUav = Vt<CreateUavDel>(_dev, 8);       // EXACT: Slot 8 = CreateUnorderedAccessView!
                    _fUavNull = Vt<CreateUavNullDel>(_dev, 8);
                    _fCs = Vt<CreateCsDel>(_dev, 18);        // EXACT: Slot 18 = CreateComputeShader!

                    _fMap = Vt<MapDel>(_ctx, 14);
                    _fUnmap = Vt<UnmapDel>(_ctx, 15);
                    _fDisp = Vt<DispatchDel>(_ctx, 41);
                    _fCopy = Vt<CopyResDel>(_ctx, 47);
                    _fUpd = Vt<UpdateSubDel>(_ctx, 48);
                    _fSetSrv = Vt<CsSetSrvDel>(_ctx, 67);    // EXACT: Slot 67 = CSSetShaderResources!
                    _fSetUav = Vt<CsSetUavDel>(_ctx, 68);    // EXACT: Slot 68 = CSSetUnorderedAccessViews!
                    _fSetCs = Vt<CsSetCsDel>(_ctx, 69);      // EXACT: Slot 69 = CSSetShader!
                    _fSetCb = Vt<CsSetCbDel>(_ctx, 71);      // EXACT: Slot 71 = CSSetConstantBuffers!

                    _fPresent = Vt<PresentDel>(_swap, 8);
                    _fGetBack = Vt<GetSwapBufDel>(_swap, 9);
                    _fResize = Vt<ResizeBufDel>(_swap, 13);

                    if (_fGetBack(_swap, 0u, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero)
                    {
                        LastError = "BackBuffer failed";
                        return false;
                    }

                    BUF_DESC cbd = new BUF_DESC();
                    cbd.ByteWidth = 256u; cbd.Usage = 0u; cbd.BindFlags = 4u;
                    if (_fBuf(_dev, ref cbd, IntPtr.Zero, out _cb) < 0) { LastError = "CBuf failed"; return false; }

                    if (!MakeSrvStructBuf((uint)(MaxGpuNodes * 3), 16u, out _nodeBuf, out _nodeSrv) ||
                        !MakeSrvStructBuf((uint)(MaxGpuEdges * 2), 16u, out _edgeBuf, out _edgeSrv) ||
                        !MakeSrvStructBuf((uint)(MaxGpuNodes * 6), 4u, out _labelBuf, out _labelSrv) ||
                        !MakeSrvStructBuf(32u, 16u, out _barBuf, out _barSrv))
                        return false;

                    uint[] glyphs = BuildAscii5x7GlyphTable();
                    if (!MakeSrvStructBuf((uint)glyphs.Length, 4u, out _fontBuf, out _fontSrv)) return false;
                    UploadArrayUint(_fontBuf, glyphs);

                    _csField = CompileCs(BuildFieldPassHlsl(), "CSSheafField");
                    _csEdges = CompileCs(BuildEdgesPassHlsl(), "CSSheafEdges");
                    _csNodes = CompileCs(BuildNodesPassHlsl(), "CSSheafNodes");
                    _csComp  = CompileCs(BuildCompositeHlsl(), "CSSheafComp");

                    if (_csField == IntPtr.Zero || _csEdges == IntPtr.Zero || _csNodes == IntPtr.Zero || _csComp == IntPtr.Zero)
                        return false;

                    if (!EnsureViewportBuffers(w, h)) return false;

                    IsGpuActive = true;
                    BackendLabel = "RTX DirectCompute cs_5_0 SDF+Glyph VSync";
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    IsGpuActive = false;
                    return false;
                }
            }
        }

        private bool CreateStructuredSrvSafe(IntPtr buf, uint count, out IntPtr srv)
        {
            SRV_DESC sd = new SRV_DESC();
            sd.Format = 0u;
            sd.ViewDimension = 1u; // D3D11_SRV_DIMENSION_BUFFER = 1u
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
            ud.ViewDimension = 1u; // D3D11_UAV_DIMENSION_BUFFER = 1u
            ud.FirstElement = 0u;
            ud.NumElements = count;
            int hr = _fUav(_dev, buf, ref ud, out uav);
            if (hr >= 0 && uav != IntPtr.Zero) return true;
            hr = _fUavNull(_dev, buf, IntPtr.Zero, out uav);
            if (hr >= 0 && uav != IntPtr.Zero) return true;
            LastError = "UAV 0x" + hr.ToString("X8");
            return false;
        }

        private bool MakeSrvStructBuf(uint count, uint stride, out IntPtr buf, out IntPtr srv)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = count * stride; bd.Usage = 0u; bd.BindFlags = 8u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
            int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            if (hr < 0 || buf == IntPtr.Zero) { LastError = "Buf 0x" + hr.ToString("X8"); return false; }
            return CreateStructuredSrvSafe(buf, count, out srv);
        }

        private bool MakeRwStructBuf(uint count, uint stride, out IntPtr buf, out IntPtr srv, out IntPtr uav)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero; uav = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = count * stride; bd.Usage = 0u; bd.BindFlags = 0x88u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
            int hr = _fBuf(_dev, ref bd, IntPtr.Zero, out buf);
            if (hr < 0 || buf == IntPtr.Zero) { LastError = "RwBuf 0x" + hr.ToString("X8"); return false; }
            if (!CreateStructuredSrvSafe(buf, count, out srv)) return false;
            return CreateStructuredUavSafe(buf, count, out uav);
        }

        private bool EnsureViewportBuffers(int w, int h)
        {
            w = Math.Max(128, w); h = Math.Max(128, h);
            if (_vpW == w && _vpH == h && _outTex != IntPtr.Zero && _basePixBuf != IntPtr.Zero) return true;

            if (_vpW != 0 && (_vpW != w || _vpH != h))
            {
                Rel(_backBuf); _backBuf = IntPtr.Zero;
                if (_fResize(_swap, 2u, (uint)w, (uint)h, 28u, 0u) < 0 ||
                    _fGetBack(_swap, 0u, ref IID_Tex2D, out _backBuf) < 0)
                {
                    LastError = "ResizeBuffers failed";
                    return false;
                }
            }

            Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
            Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
            Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;
            Rel(_rbUav); _rbUav = IntPtr.Zero; Rel(_rbBuf); _rbBuf = IntPtr.Zero; Rel(_stgBuf); _stgBuf = IntPtr.Zero;

            TEX2D_DESC td = new TEX2D_DESC();
            td.Width = (uint)w; td.Height = (uint)h; td.MipLevels = 1u; td.ArraySize = 1u;
            td.Format = 28u; td.SampleCount = 1u; td.BindFlags = 0x80u; // UNORDERED_ACCESS
            if (_fTex(_dev, ref td, IntPtr.Zero, out _outTex) < 0) { LastError = "OutTex failed"; return false; }

            UAV_DESC tud = new UAV_DESC(); tud.Format = 28u; tud.ViewDimension = 4u; // TEXTURE2D
            if (_fUav(_dev, _outTex, ref tud, out _outTexUav) < 0) { LastError = "OutTexUAV failed"; return false; }

            uint pix = (uint)(w * h);
            if (!MakeRwStructBuf(pix, 4u, out _basePixBuf, out _basePixSrv, out _basePixUav)) return false;
            if (!MakeRwStructBuf(pix * 3u, 4u, out _lineRgbBuf, out _lineRgbSrv, out _lineRgbUav)) return false;

            BUF_DESC rbd = new BUF_DESC(); rbd.ByteWidth = pix * 4u; rbd.BindFlags = 0x80u; rbd.MiscFlags = 0x40u; rbd.StructureByteStride = 4u;
            if (_fBuf(_dev, ref rbd, IntPtr.Zero, out _rbBuf) < 0 || !CreateStructuredUavSafe(_rbBuf, pix, out _rbUav)) return false;

            BUF_DESC sgd = new BUF_DESC(); sgd.ByteWidth = pix * 4u; sgd.Usage = 3u; sgd.CPUAccessFlags = 0x20000u;
            if (_fBuf(_dev, ref sgd, IntPtr.Zero, out _stgBuf) < 0) return false;

            _vpW = w; _vpH = h;
            return true;
        }

        private IntPtr CompileCs(string hlsl, string entry)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(hlsl);
            IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;
            try
            {
                int hr = D3DCompile(bytes, (IntPtr)bytes.Length, entry + ".hlsl", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 0u, 0u, out pCode, out pErr);
                if (hr < 0 || pCode == IntPtr.Zero)
                {
                    if (pErr != IntPtr.Zero)
                    {
                        IntPtr ep = Vt<BlobPtrDel>(pErr, 3)(pErr);
                        int elen = (int)Vt<BlobSizeDel>(pErr, 4)(pErr);
                        LastError = entry + ": " + Marshal.PtrToStringAnsi(ep, elen).Trim();
                    }
                    else LastError = entry + " 0x" + hr.ToString("X8");
                    return IntPtr.Zero;
                }
                IntPtr ptr = Vt<BlobPtrDel>(pCode, 3)(pCode);
                IntPtr len = Vt<BlobSizeDel>(pCode, 4)(pCode);
                IntPtr cs;
                if (_fCs(_dev, ptr, len, IntPtr.Zero, out cs) < 0) { LastError = entry + " CreateCS failed"; return IntPtr.Zero; }
                return cs;
            }
            finally { Rel(pCode); Rel(pErr); }
        }

        private void UploadArray(IntPtr dstBuf, float[] arr)
        {
            if (dstBuf == IntPtr.Zero) return;
            GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { _fUpd(_ctx, dstBuf, 0u, IntPtr.Zero, h.AddrOfPinnedObject(), 0u, 0u); }
            finally { h.Free(); }
        }

        private void UploadArrayUint(IntPtr dstBuf, uint[] arr)
        {
            if (dstBuf == IntPtr.Zero) return;
            GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { _fUpd(_ctx, dstBuf, 0u, IntPtr.Zero, h.AddrOfPinnedObject(), 0u, 0u); }
            finally { h.Free(); }
        }

        private static byte SanitizeAscii(char c)
        {
            if (c >= 32 && c <= 126) return (byte)c;
            return (byte)'?';
        }

        public void PackAndUploadSheaf(
            SheafGraphModel g, int w, int h, int viewMode,
            float camZoom, float panX, float panY, float orbitYaw, float orbitPitch,
            int selNodeIdx, int selEdgeIdx)
        {
            int n = Math.Min(MaxGpuNodes, g.Nodes.Count);
            int m = Math.Min(MaxGpuEdges, g.Edges.Count);
            Array.Clear(_nodePack, 0, _nodePack.Length);
            Array.Clear(_edgePack, 0, _edgePack.Length);
            Array.Clear(_labelPack, 0, _labelPack.Length);
            Array.Clear(_barPack, 0, _barPack.Length);

            float stageW = (viewMode == 2) ? (w * 0.62f) : (viewMode == 3 ? (w * 0.42f) : w);
            float cx = stageW * 0.5f + panX;
            float cy = h * 0.5f + panY;
            float scale = Math.Min(stageW, h) * 0.20f * camZoom;
            float cyaw = (float)Math.Cos(orbitYaw), syaw = (float)Math.Sin(orbitYaw);
            float cp = (float)Math.Cos(orbitPitch), sp = (float)Math.Sin(orbitPitch);

            for (int i = 0; i < n; i++)
            {
                SheafStalkNode nd = g.Nodes[i];
                float sx, sy, rad;
                if (viewMode == 1)
                {
                    float rx = nd.X * cyaw - nd.Z * syaw;
                    float rz = nd.X * syaw + nd.Z * cyaw;
                    float ry = nd.Y * cp - rz * sp;
                    float zz = nd.Y * sp + rz * cp + 5.2f;
                    float persp = 4.6f / Math.Max(1.2f, zz);
                    sx = cx + rx * scale * persp;
                    sy = cy + ry * scale * persp;
                    rad = Math.Max(10.0f, Math.Min(26.0f, 16.0f * persp * (float)Math.Sqrt(camZoom)));
                }
                else
                {
                    sx = cx + nd.X * scale;
                    sy = cy + nd.Y * scale;
                    rad = Math.Max(10.0f, Math.Min(25.0f, 15.5f * (float)Math.Sqrt(camZoom)));
                }

                ScreenNodeX[i] = sx; ScreenNodeY[i] = sy; ScreenNodeR[i] = rad;
                int flg = (nd.InContradictionLoop ? 1 : 0) | (nd.Pinned ? 2 : 0) | ((i == selNodeIdx) ? 4 : 0);

                int b0 = i * 12;
                _nodePack[b0 + 0] = sx; _nodePack[b0 + 1] = sy; _nodePack[b0 + 2] = rad; _nodePack[b0 + 3] = flg;
                for (int k = 0; k < 8; k++) _nodePack[b0 + 4 + k] = nd.Stalk8[k];

                string cleanTitle = nd.Title ?? "";
                if (cleanTitle.Length > 11) cleanTitle = cleanTitle.Substring(0, 11);
                string badge = "[" + i + "]" + cleanTitle + " #" + nd.E8RootIndex;
                if (badge.Length > 18) badge = badge.Substring(0, 18);
                int len = badge.Length;
                int lb = i * 6;
                uint curWord = (uint)(len & 0xFF);
                for (int c = 0; c < 23; c++)
                {
                    int slotByte = c + 1;
                    uint ch = (c < len) ? (uint)SanitizeAscii(badge[c]) : 32u;
                    int wordIdx = slotByte >> 2;
                    int shift = (slotByte & 3) * 8;
                    if (shift == 0 && wordIdx > 0) curWord = 0u;
                    curWord |= (ch << shift);
                    _labelPack[lb + wordIdx] = curWord;
                }
                if (len == 0) _labelPack[lb] = 0u;
            }

            int validEdges = 0;
            int hMode = HodgeColorMode;
            for (int e = 0; e < m; e++)
            {
                SheafRestrictionEdge ed = g.Edges[e];
                if (ed.SrcIdx < 0 || ed.SrcIdx >= n || ed.DstIdx < 0 || ed.DstIdx >= n) continue;
                int eb = validEdges * 8;
                _edgePack[eb + 0] = ScreenNodeX[ed.SrcIdx];
                _edgePack[eb + 1] = ScreenNodeY[ed.SrcIdx];
                _edgePack[eb + 2] = ScreenNodeX[ed.DstIdx];
                _edgePack[eb + 3] = ScreenNodeY[ed.DstIdx];

                float metricVal = (hMode == 1) ? ed.ExactGradNorm
                                : (hMode == 2) ? ed.CurlVorticity
                                : (hMode == 3) ? ed.HarmonicObsNorm
                                : ed.ResidualNorm;
                _edgePack[eb + 4] = metricVal;
                int eFlg = ed.InContradictionLoop ? 2 : (ed.IsObstructed ? 1 : 0);
                if (e == selEdgeIdx) eFlg += 4;
                _edgePack[eb + 5] = eFlg;
                _edgePack[eb + 6] = ed.Polarity;
                _edgePack[eb + 7] = ed.TwistRad;
                validEdges++;
            }

            int nb = Math.Min(16, g.PersistenceBarcode.Count);
            for (int b = 0; b < nb; b++)
            {
                HodgePersistenceBar pb = g.PersistenceBarcode[b];
                _barPack[b * 4 + 0] = pb.BirthEps;
                _barPack[b * 4 + 1] = pb.DeathEps;
                _barPack[b * 4 + 2] = pb.Dimension;
                _barPack[b * 4 + 3] = pb.ActiveAtCursor ? 1.0f : 0.0f;
            }
            for (int k = 0; k < 16; k++)
            {
                float ev = (k < g.EigenCount) ? g.EigenSpectrum[k] : 0.0f;
                _barPack[(16 + k) * 4 + 0] = ev;
            }

            _upNodeCount = n;
            _upEdgeCount = validEdges;
            _upBarCount = nb;

            if (!IsGpuActive) return;
            lock (_lk)
            {
                UploadArray(_nodeBuf, _nodePack);
                UploadArray(_edgeBuf, _edgePack);
                UploadArrayUint(_labelBuf, _labelPack);
                UploadArray(_barBuf, _barPack);
            }
        }

        public bool RenderGpuFrame(
            SheafGraphModel g, int w, int h, int viewMode, float simTime, float obsTol,
            float camZoom, float panX, float panY, float orbitYaw, float orbitPitch,
            int selNodeIdx, int selEdgeIdx, int[] optionalReadback)
        {
            if (!IsGpuActive) return false;
            PackAndUploadSheaf(g, w, h, viewMode, camZoom, panX, panY, orbitYaw, orbitPitch, selNodeIdx, selEdgeIdx);

            lock (_lk)
            {
                if (!EnsureViewportBuffers(w, h)) return false;

                Array.Clear(_cbData, 0, _cbData.Length);
                _cbData[0] = w; _cbData[1] = h; _cbData[2] = simTime; _cbData[3] = viewMode;
                _cbData[4] = _upNodeCount; _cbData[5] = _upEdgeCount; _cbData[6] = obsTol; _cbData[7] = g.IsGlobalConsensusH1Zero ? 1.0f : 0.0f;
                _cbData[8] = (float)g.TotalDirichletEnergy; _cbData[9] = _upBarCount; _cbData[10] = g.EigenCount; _cbData[11] = (optionalReadback != null) ? 1.0f : 0.0f;
                _cbData[12] = camZoom; _cbData[13] = ShowGpuLabels ? 1.0f : 0.0f; _cbData[14] = HodgeColorMode; _cbData[15] = selNodeIdx;
                UploadArray(_cb, _cbData);

                IntPtr cb = _cb;
                _fSetCb(_ctx, 0u, 1u, ref cb);
                IntPtr[] zSrv4 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zUav2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zUav1 = new IntPtr[] { IntPtr.Zero };

                uint gx = (uint)((w + 15) / 16), gy = (uint)((h + 15) / 16);

                // Pass 1: CSSheafField (Potential Field + Split Laplacian L_F + Hodge Persistence Barcode!)
                _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _nodeSrv, _edgeSrv, _barSrv });
                _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _basePixUav, _lineRgbUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csField, IntPtr.Zero, 0u);
                _fDisp(_ctx, gx, gy, 1u);
                _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 4u, zSrv4);

                // Pass 2: CSSheafEdges (Sub-Pixel Anti-Aliased SDF Capsules + Directional Arrows + Hodge Modes!)
                if (_upEdgeCount > 0)
                {
                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _edgeSrv });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _lineRgbUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csEdges, IntPtr.Zero, 0u);
                    _fDisp(_ctx, (uint)((_upEdgeCount + 63) / 64), 1u, 1u);
                    _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { IntPtr.Zero });
                }

                // Pass 3: CSSheafNodes (8-Petal E8 Rosettes + Embedded 10x12 GPU Bitmap Glyph Labels!)
                if (_upNodeCount > 0)
                {
                    _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _nodeSrv, _labelSrv, _fontSrv });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _lineRgbUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csNodes, IntPtr.Zero, 0u);
                    _fDisp(_ctx, (uint)((_upNodeCount + 63) / 64), 1u, 1u);
                    _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 4u, zSrv4);
                }

                // Pass 4: CSSheafComp (ACES Filmic Composite -> SwapChain + Optional Readback)
                _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { _basePixSrv, _lineRgbSrv });
                _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _outTexUav, _rbUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csComp, IntPtr.Zero, 0u);
                _fDisp(_ctx, gx, gy, 1u);
                _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 4u, zSrv4);

                if (_backBuf != IntPtr.Zero)
                {
                    _fCopy(_ctx, _backBuf, _outTex);
                    _fPresent(_swap, VSyncEnabled ? 1u : 0u, 0u);
                }

                if (optionalReadback != null && optionalReadback.Length >= w * h)
                {
                    _fCopy(_ctx, _stgBuf, _rbBuf);
                    MAP_SUB mp;
                    if (_fMap(_ctx, _stgBuf, 0u, 1u, 0u, out mp) >= 0 && mp.pData != IntPtr.Zero)
                    {
                        try { Marshal.Copy(mp.pData, optionalReadback, 0, w * h); }
                        finally { _fUnmap(_ctx, _stgBuf, 0u); }
                    }
                }
                return true;
            }
        }

        public void RenderCpuFallback(
            Bitmap bmp, SheafGraphModel g, int viewMode, float simTime,
            float camZoom, float panX, float panY, float orbitYaw, float orbitPitch,
            int selNodeIdx, int selEdgeIdx)
        {
            int w = bmp.Width, h = bmp.Height;
            PackAndUploadSheaf(g, w, h, viewMode, camZoom, panX, panY, orbitYaw, orbitPitch, selNodeIdx, selEdgeIdx);
            using (Graphics gx = Graphics.FromImage(bmp))
            using (Font fn = new Font("Consolas", 8.2f, FontStyle.Bold))
            using (SolidBrush txtBr = new SolidBrush(Color.FromArgb(242, 248, 255)))
            {
                gx.SmoothingMode = SmoothingMode.AntiAlias;
                gx.Clear(Color.FromArgb(7, 11, 20));
                for (int e = 0; e < _upEdgeCount; e++)
                {
                    SheafRestrictionEdge ed = g.Edges[e];
                    Color ec = ed.InContradictionLoop ? Color.FromArgb(244, 63, 94) : (ed.IsObstructed ? Color.FromArgb(251, 191, 36) : Color.FromArgb(16, 185, 129));
                    using (Pen p = new Pen(ec, ed.InContradictionLoop ? 3.0f : 1.9f))
                        gx.DrawLine(p, _edgePack[e * 8], _edgePack[e * 8 + 1], _edgePack[e * 8 + 2], _edgePack[e * 8 + 3]);
                }
                for (int i = 0; i < _upNodeCount; i++)
                {
                    SheafStalkNode nd = g.Nodes[i];
                    float sx = ScreenNodeX[i], sy = ScreenNodeY[i], r = ScreenNodeR[i];
                    Color nc = nd.InContradictionLoop ? Color.FromArgb(225, 29, 72) : Color.FromArgb(16, 185, 129);
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(50, nc))) gx.FillEllipse(br, sx - r * 1.5f, sy - r * 1.5f, r * 3.0f, r * 3.0f);
                    using (Pen p = new Pen(nc, i == selNodeIdx ? 3.2f : 2.1f)) gx.DrawEllipse(p, sx - r, sy - r, r * 2.0f, r * 2.0f);
                    string lbl = nd.Title.Length > 18 ? nd.Title.Substring(0, 18) : nd.Title;
                    gx.DrawString(lbl, fn, txtBr, sx - r, sy + r + 3.0f);
                }
            }
        }

        private static uint PackGlyph5x6(string r0, string r1, string r2, string r3, string r4, string r5)
        {
            string[] rows = new string[] { r0, r1, r2, r3, r4, r5 };
            uint bits = 0u;
            for (int r = 0; r < 6; r++)
            {
                string s = rows[r];
                for (int c = 0; c < 5 && c < s.Length; c++)
                {
                    if (s[c] == '#') bits |= (1u << (r * 5 + c));
                }
            }
            return bits;
        }

        private static uint[] BuildAscii5x7GlyphTable()
        {
            uint[] g = new uint[128];
            g[' '] = 0u;
            g['!'] = PackGlyph5x6("..#..", "..#..", "..#..", "..#..", ".....", "..#..");
            g['#'] = PackGlyph5x6(".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".....");
            g['('] = PackGlyph5x6("...#.", "..#..", "..#..", "..#..", "..#..", "...#.");
            g[')'] = PackGlyph5x6(".#...", "..#..", "..#..", "..#..", "..#..", ".#...");
            g['+'] = PackGlyph5x6(".....", "..#..", "#####", "..#..", ".....", ".....");
            g['-'] = PackGlyph5x6(".....", ".....", "#####", ".....", ".....", ".....");
            g['.'] = PackGlyph5x6(".....", ".....", ".....", ".....", "..##.", "..##.");
            g['/'] = PackGlyph5x6("....#", "...#.", "..#..", ".#...", "#....", ".....");
            g[':'] = PackGlyph5x6(".....", "..#..", ".....", ".....", "..#..", ".....");
            g['='] = PackGlyph5x6(".....", "#####", ".....", "#####", ".....", ".....");
            g['?'] = PackGlyph5x6(".###.", "#...#", "...#.", "..#..", ".....", "..#..");
            g['['] = PackGlyph5x6(".###.", ".#...", ".#...", ".#...", ".#...", ".###.");
            g[']'] = PackGlyph5x6(".###.", "...#.", "...#.", "...#.", "...#.", ".###.");
            g['_'] = PackGlyph5x6(".....", ".....", ".....", ".....", ".....", "#####");

            g['0'] = PackGlyph5x6(".###.", "#..##", "#.#.#", "##..#", "#...#", ".###.");
            g['1'] = PackGlyph5x6("..#..", ".##..", "..#..", "..#..", "..#..", ".###.");
            g['2'] = PackGlyph5x6(".###.", "#...#", "...#.", "..#..", ".#...", "#####");
            g['3'] = PackGlyph5x6("####.", "....#", ".###.", "....#", "#...#", ".###.");
            g['4'] = PackGlyph5x6("#..#.", "#..#.", "#####", "...#.", "...#.", "...#.");
            g['5'] = PackGlyph5x6("#####", "#....", "####.", "....#", "#...#", ".###.");
            g['6'] = PackGlyph5x6(".###.", "#....", "####.", "#...#", "#...#", ".###.");
            g['7'] = PackGlyph5x6("#####", "....#", "...#.", "..#..", ".#...", ".#...");
            g['8'] = PackGlyph5x6(".###.", "#...#", ".###.", "#...#", "#...#", ".###.");
            g['9'] = PackGlyph5x6(".###.", "#...#", ".####", "....#", "#...#", ".###.");

            g['A'] = PackGlyph5x6(".###.", "#...#", "#####", "#...#", "#...#", "#...#");
            g['B'] = PackGlyph5x6("####.", "#...#", "####.", "#...#", "#...#", "####.");
            g['C'] = PackGlyph5x6(".####", "#....", "#....", "#....", "#....", ".####");
            g['D'] = PackGlyph5x6("####.", "#...#", "#...#", "#...#", "#...#", "####.");
            g['E'] = PackGlyph5x6("#####", "#....", "####.", "#....", "#....", "#####");
            g['F'] = PackGlyph5x6("#####", "#....", "####.", "#....", "#....", "#....");
            g['G'] = PackGlyph5x6(".####", "#....", "#.###", "#...#", "#...#", ".####");
            g['H'] = PackGlyph5x6("#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            g['I'] = PackGlyph5x6(".###.", "..#..", "..#..", "..#..", "..#..", ".###.");
            g['J'] = PackGlyph5x6("..###", "...#.", "...#.", "...#.", "#..#.", ".##..");
            g['K'] = PackGlyph5x6("#...#", "#..#.", "###..", "#..#.", "#...#", "#...#");
            g['L'] = PackGlyph5x6("#....", "#....", "#....", "#....", "#....", "#####");
            g['M'] = PackGlyph5x6("#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#");
            g['N'] = PackGlyph5x6("#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#");
            g['O'] = PackGlyph5x6(".###.", "#...#", "#...#", "#...#", "#...#", ".###.");
            g['P'] = PackGlyph5x6("####.", "#...#", "####.", "#....", "#....", "#....");
            g['Q'] = PackGlyph5x6(".###.", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
            g['R'] = PackGlyph5x6("####.", "#...#", "####.", "#..#.", "#...#", "#...#");
            g['S'] = PackGlyph5x6(".####", "#....", ".###.", "....#", "....#", "####.");
            g['T'] = PackGlyph5x6("#####", "..#..", "..#..", "..#..", "..#..", "..#..");
            g['U'] = PackGlyph5x6("#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            g['V'] = PackGlyph5x6("#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
            g['W'] = PackGlyph5x6("#...#", "#...#", "#...#", "#.#.#", "##.##", "#...#");
            g['X'] = PackGlyph5x6("#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
            g['Y'] = PackGlyph5x6("#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
            g['Z'] = PackGlyph5x6("#####", "...#.", "..#..", ".#...", "#....", "#####");

            for (char ch = 'a'; ch <= 'z'; ch++)
                g[ch] = g[char.ToUpperInvariant(ch)];
            return g;
        }

        private static string BuildFieldPassHlsl()
        {
            return @"
cbuffer SheafCB : register(b0) { float4 U[16]; };
StructuredBuffer<float4> Nodes : register(t0);
StructuredBuffer<float4> Edges : register(t1);
StructuredBuffer<float4> Bars  : register(t2);
RWStructuredBuffer<uint> BasePix : register(u0);
RWStructuredBuffer<uint> LineRGB : register(u1);

[numthreads(16, 16, 1)]
void CSSheafField(uint3 dtid : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (dtid.x >= W || dtid.y >= H) return;
    uint idx = dtid.y * W + dtid.x;
    uint b3 = idx * 3u;
    LineRGB[b3 + 0u] = 0u; LineRGB[b3 + 1u] = 0u; LineRGB[b3 + 2u] = 0u;

    float t = U[0].z;
    int vmode = (int)U[0].w;
    uint nNodes = min(128u, (uint)U[1].x);
    uint nEdges = min(512u, (uint)U[1].y);
    float epsCur = U[1].z;
    uint nBars = min(16u, (uint)U[2].y);
    uint nEigs = min(16u, (uint)U[2].z);

    float2 p = float2((float)dtid.x, (float)dtid.y);
    float splitX = (vmode == 2) ? ((float)W * 0.62) : ((vmode == 3) ? ((float)W * 0.42) : (float)W);

    // Right Pane in Split / Hodge Barcode Modes (vmode == 2 or vmode == 3)
    if ((vmode == 2 || vmode == 3) && p.x >= splitX)
    {
        float paneW = (float)W - splitX;
        float pad = 20.0;
        float3 mcol = float3(0.022, 0.034, 0.064);

        // Upper Half (y < H * 0.56): Live Sheaf Laplacian Matrix L_F Heatmap
        float matH = (float)H * 0.54;
        float side = min(paneW - pad * 2.0, matH - pad * 1.5);
        float mx0 = splitX + (paneW - side) * 0.5;
        float my0 = 16.0 + (matH - 16.0 - side) * 0.5;

        if (p.x >= mx0 && p.x < mx0 + side && p.y >= my0 && p.y < my0 + side && nNodes > 0u)
        {
            uint r = min(nNodes - 1u, (uint)(((p.y - my0) / side) * (float)nNodes));
            uint c = min(nNodes - 1u, (uint)(((p.x - mx0) / side) * (float)nNodes));
            float2 cellUv = frac(float2((p.x - mx0) / side, (p.y - my0) / side) * (float)nNodes);
            float gridBorder = (cellUv.x < 0.06 || cellUv.y < 0.06) ? 0.32 : 1.0;
            if (r == c)
            {
                int flg = (int)Nodes[r * 3u].w;
                bool loopN = ((flg & 1) != 0);
                mcol = (loopN ? float3(0.90, 0.20, 0.36) : float3(0.10, 0.80, 0.58)) * 0.78 * gridBorder;
            }
            else
            {
                mcol = float3(0.04, 0.07, 0.13) * gridBorder;
                float2 nr = Nodes[r * 3u].xy, nc = Nodes[c * 3u].xy;
                [loop] for (uint ei = 0u; ei < 512u; ei++)
                {
                    if (ei >= nEdges) break;
                    float4 ep = Edges[ei * 2u];
                    float4 em = Edges[ei * 2u + 1u];
                    bool hit = (length(ep.xy - nr) < 1.5 && length(ep.zw - nc) < 1.5) ||
                               (length(ep.xy - nc) < 1.5 && length(ep.zw - nr) < 1.5);
                    if (hit)
                    {
                        int st = ((int)round(em.y)) & 3;
                        mcol = ((st >= 2) ? float3(0.98, 0.22, 0.38) : (em.z < 0.0 ? float3(0.98, 0.74, 0.18) : float3(0.14, 0.88, 0.64))) * gridBorder;
                        break;
                    }
                }
            }
        }

        // Lower Half (y >= H * 0.58): Persistent Sheaf Cohomology Barcode + Eigen-Spectrum!
        float bx0 = splitX + pad;
        float bw  = max(40.0, paneW - pad * 2.0);
        float by0 = (float)H * 0.58;
        float bh  = (float)H * 0.26;

        if (p.x >= bx0 && p.x < bx0 + bw && p.y >= by0 && p.y < by0 + bh && nBars > 0u)
        {
            mcol = float3(0.032, 0.048, 0.090);
            float uEps = (p.x - bx0) / bw; // [0..1] filtration scale
            uint rowIdx = min(nBars - 1u, (uint)(((p.y - by0) / bh) * (float)nBars));
            float rowFrac = frac(((p.y - by0) / bh) * (float)nBars);
            float4 bar = Bars[rowIdx]; // x=birth, y=death, z=dim(0/1), w=active

            if (rowFrac > 0.18 && rowFrac < 0.82 && uEps >= bar.x && uEps <= bar.y)
            {
                bool isH0 = (bar.z < 0.5);
                float actBoost = (bar.w > 0.5) ? 1.0 : 0.45;
                mcol = (isH0 ? float3(0.12, 0.90, 0.64) : float3(0.98, 0.24, 0.44)) * actBoost;
            }
            // Gold vertical filtration scrubber cursor at epsCur
            if (abs(uEps - clamp(epsCur, 0.0, 1.0)) * bw < 1.6)
                mcol = float3(1.0, 0.84, 0.22);
        }

        // Bottom Strip (y >= H * 0.86): Jacobi Eigenvalue Spectrum Bars lambda_0 .. lambda_k
        float ey0 = (float)H * 0.86;
        float eh  = (float)H * 0.11;
        if (p.x >= bx0 && p.x < bx0 + bw && p.y >= ey0 && p.y < ey0 + eh && nEigs > 0u)
        {
            mcol = float3(0.025, 0.040, 0.078);
            uint ki = min(nEigs - 1u, (uint)(((p.x - bx0) / bw) * (float)nEigs));
            float colFrac = frac(((p.x - bx0) / bw) * (float)nEigs);
            float ev = clamp(Bars[16u + ki].x * 0.18, 0.04, 1.0);
            float vNorm = 1.0 - ((p.y - ey0) / eh);
            if (colFrac > 0.14 && colFrac < 0.86 && vNorm <= ev)
                mcol = (ki == 0u && Bars[16u].x < 0.02) ? float3(0.16, 0.92, 0.66) : float3(0.28, 0.72, 0.98);
        }

        uint3 mc8 = (uint3)clamp(mcol * 255.0, 0.0, 255.0);
        BasePix[idx] = 0xFF000000u | (mc8.x << 16) | (mc8.y << 8) | mc8.z;
        return;
    }

    // Left Stage Background: Subtle E8 Coordinate Grid + Soft Harmonic Potential Aura
    float2 gUv = (p - float2(splitX * 0.5, (float)H * 0.5)) / max(24.0, 48.0 * U[3].x);
    float gx1 = abs(frac(gUv.x) - 0.5), gy1 = abs(frac(gUv.y) - 0.5);
    float gridLine = exp(-min(gx1, gy1) * 42.0) * 0.075;
    float3 col = float3(0.022, 0.034, 0.064) + float3(0.08, 0.24, 0.38) * gridLine;

    float emeraldPot = 0.0;
    float crimsonPot = 0.0;
    [loop] for (uint ni = 0u; ni < 128u; ni++)
    {
        if (ni >= nNodes) break;
        float4 np = Nodes[ni * 3u];
        float2 d = p - np.xy;
        float r2 = dot(d, d);
        int flg = (int)np.w;
        float fall = exp(-r2 / 6400.0);
        if ((flg & 1) != 0)
        {
            float swirl = 0.5 + 0.5 * sin(atan2(d.y, d.x) * 6.0 - t * 4.0 + sqrt(r2) * 0.045);
            crimsonPot += fall * (0.45 + 0.40 * swirl);
        }
        else
        {
            float calm = 0.5 + 0.5 * cos(sqrt(r2) * 0.055 - t * 1.8);
            emeraldPot += fall * (0.36 + 0.26 * calm);
        }
    }

    col += float3(0.03, 0.24, 0.18) * min(1.0, emeraldPot * 0.45);
    col += float3(0.36, 0.05, 0.12) * min(1.1, crimsonPot * 0.55);

    if ((vmode == 2 || vmode == 3) && abs(p.x - splitX) < 1.5) col = float3(0.22, 0.74, 0.98);

    uint3 c8 = (uint3)clamp(col * 255.0, 0.0, 255.0);
    BasePix[idx] = 0xFF000000u | (c8.x << 16) | (c8.y << 8) | c8.z;
}
";
        }

        private static string BuildEdgesPassHlsl()
        {
            return @"
cbuffer SheafCB : register(b0) { float4 U[16]; };
StructuredBuffer<float4> Edges : register(t0);
RWStructuredBuffer<uint> LineRGB : register(u0);

// FIXED: strideW is ALWAYS full viewport W; clipMaxX clips cleanly at split divider!
void PlotAdd(uint strideW, uint clipMaxX, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= clipMaxX || (uint)y >= H) return;
    uint b = ((uint)y * strideW + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

[numthreads(64, 1, 1)]
void CSSheafEdges(uint3 dtid : SV_DispatchThreadID)
{
    uint eid = dtid.x;
    uint nEdges = (uint)U[1].y;
    if (eid >= nEdges) return;

    uint W = (uint)U[0].x, H = (uint)U[0].y;
    int vmode = (int)U[0].w;
    uint maxStageX = (vmode == 2) ? (uint)((float)W * 0.62) : ((vmode == 3) ? (uint)((float)W * 0.42) : W);
    float t = U[0].z;
    int hMode = (int)round(U[3].z);

    float4 ep = Edges[eid * 2u];
    float4 em = Edges[eid * 2u + 1u]; // x=metric, y=flags, z=polarity, w=twist

    float2 p0 = ep.xy, p1 = ep.zw;
    float2 seg = p1 - p0;
    float len = length(seg);
    if (len < 4.0 || len > 3200.0) return;

    int eFlags = (int)round(em.y);
    bool isSel = (eFlags >= 4);
    int state = isSel ? (eFlags - 4) : eFlags;
    bool inLoop = (state >= 2);
    bool isObs = (state == 1);

    float3 baseCol;
    if (hMode == 1)      baseCol = lerp(float3(0.08, 0.36, 0.58), float3(0.22, 0.92, 1.00), saturate(em.x * 2.2));
    else if (hMode == 2) baseCol = lerp(float3(0.18, 0.22, 0.38), float3(1.00, 0.76, 0.16), saturate(em.x * 2.5));
    else if (hMode == 3) baseCol = (em.x > 0.05) ? float3(1.00, 0.18, 0.58) : float3(0.08, 0.32, 0.26);
    else                 baseCol = inLoop ? float3(1.0, 0.20, 0.36) : (isObs ? float3(1.0, 0.76, 0.18) : float3(0.14, 0.92, 0.64));

    if (isSel) baseCol = float3(0.42, 0.92, 1.0);

    float2 dir = seg / len;
    float2 nrm = float2(-dir.y, dir.x);
    int steps = clamp((int)ceil(len * 1.25), 6, 1600);
    float halfThick = (inLoop || isSel) ? 2.1 : 1.45;

    // Sub-pixel SDF anti-aliased ribbon + traveling wavepacket!
    [loop] for (int s = 0; s < 1600; s++)
    {
        if (s > steps) break;
        float u = (float)s / (float)steps;
        float2 center = lerp(p0, p1, u);
        if (inLoop)
            center += nrm * (sin(u * 26.0 - t * 12.0) * 1.6);

        float pulse = exp(-abs(frac(u - t * (inLoop ? 1.25 : 0.45)) - 0.5) * 20.0);
        float gain = (inLoop ? 0.90 : 0.68) + pulse * 0.72;

        int cx = (int)round(center.x), cy = (int)round(center.y);
        [loop] for (int oy = -2; oy <= 2; oy++)
        {
            [loop] for (int ox = -2; ox <= 2; ox++)
            {
                float2 sp = float2((float)(cx + ox), (float)(cy + oy));
                float d = length(sp - center);
                float cov = saturate(halfThick + 0.65 - d);
                if (cov > 0.02)
                {
                    float w = cov * gain * 115.0;
                    PlotAdd(W, maxStageX, H, cx + ox, cy + oy,
                        (uint)clamp(baseCol.x * w, 0.0, 255.0),
                        (uint)clamp(baseCol.y * w, 0.0, 255.0),
                        (uint)clamp(baseCol.z * w, 0.0, 255.0));
                }
            }
        }
    }

    // Directional Restriction Arrowhead Chevron at u = 0.66 along the edge!
    float2 tip = lerp(p0, p1, 0.66);
    float2 wingA = tip - dir * 9.5 + nrm * 5.2;
    float2 wingB = tip - dir * 9.5 - nrm * 5.2;
    [loop] for (int k = 0; k <= 12; k++)
    {
        float fk = (float)k / 12.0;
        float2 pa = lerp(wingA, tip, fk);
        float2 pb = lerp(wingB, tip, fk);
        uint ar = (uint)clamp(baseCol.x * 235.0, 0.0, 255.0);
        uint ag = (uint)clamp(baseCol.y * 235.0, 0.0, 255.0);
        uint ab = (uint)clamp(baseCol.z * 235.0, 0.0, 255.0);
        PlotAdd(W, maxStageX, H, (int)round(pa.x), (int)round(pa.y), ar, ag, ab);
        PlotAdd(W, maxStageX, H, (int)round(pb.x), (int)round(pb.y), ar, ag, ab);
    }
}
";
        }

        private static string BuildNodesPassHlsl()
        {
            return @"
cbuffer SheafCB : register(b0) { float4 U[16]; };
StructuredBuffer<float4> Nodes  : register(t0);
StructuredBuffer<uint>   Labels : register(t1);
StructuredBuffer<uint>   Glyphs : register(t2);
RWStructuredBuffer<uint> LineRGB : register(u0);

void PlotAdd(uint strideW, uint clipMaxX, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= clipMaxX || (uint)y >= H) return;
    uint b = ((uint)y * strideW + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

uint GetPackedChar(uint nid, uint charPos)
{
    uint byteIdx = charPos + 1u;
    uint w = Labels[nid * 6u + (byteIdx >> 2u)];
    return (w >> ((byteIdx & 3u) * 8u)) & 0x7Fu;
}

[numthreads(64, 1, 1)]
void CSSheafNodes(uint3 dtid : SV_DispatchThreadID)
{
    uint nid = dtid.x;
    uint nNodes = (uint)U[1].x;
    if (nid >= nNodes) return;

    uint W = (uint)U[0].x, H = (uint)U[0].y;
    int vmode = (int)U[0].w;
    uint maxStageX = (vmode == 2) ? (uint)((float)W * 0.62) : ((vmode == 3) ? (uint)((float)W * 0.42) : W);
    float t = U[0].z;
    bool showLabels = (U[3].y > 0.5);

    float4 n0 = Nodes[nid * 3u + 0u];
    float4 sA = Nodes[nid * 3u + 1u];
    float4 sB = Nodes[nid * 3u + 2u];

    float2 c = n0.xy;
    float rad = clamp(n0.z, 9.0, 28.0);
    int flg = (int)round(n0.w);
    bool inLoop = ((flg & 1) != 0);
    bool pinned = ((flg & 2) != 0);
    bool sel    = ((flg & 4) != 0);

    float3 ringCol = inLoop ? float3(1.0, 0.24, 0.40) : float3(0.16, 0.96, 0.68);
    if (sel) ringCol = float3(0.42, 0.94, 1.0);

    float st8[8] = { sA.x, sA.y, sA.z, sA.w, sB.x, sB.y, sB.z, sB.w };

    int boxR = (int)ceil(rad + 8.0);
    [loop] for (int dy = -36; dy <= 36; dy++)
    {
        if (dy < -boxR || dy > boxR) continue;
        [loop] for (int dx = -36; dx <= 36; dx++)
        {
            if (dx < -boxR || dx > boxR) continue;
            int px = (int)round(c.x) + dx, py = (int)round(c.y) + dy;
            if (px < 0 || (uint)px >= maxStageX || py < 0 || (uint)py >= H) continue;

            float2 dv = float2((float)px - c.x, (float)py - c.y);
            float r = length(dv);
            float ang = atan2(dv.y, dv.x) + 3.14159265;
            uint sector = ((uint)floor((ang / 6.2831853) * 8.0)) & 7u;
            float petalAmp = clamp(abs(st8[sector]) * 0.85, 0.18, 0.96);
            float petalR = rad * (0.30 + 0.56 * petalAmp);

            float3 pix = float3(0.0, 0.0, 0.0);

            // Crisp SDF outer stalk circle (2.0px anti-aliased)
            float ringCov = saturate(1.85 - abs(r - rad));
            pix += ringCol * ringCov * 1.15;

            // Center core dot
            if (r < 2.6) pix += float3(0.95, 0.98, 1.0) * saturate(2.6 - r) * 0.9;

            // Pinned golden boundary anchor halo
            if (pinned)
            {
                float pinCov = saturate(1.5 - abs(r - (rad + 4.2)));
                pix += float3(1.0, 0.85, 0.25) * pinCov * 0.95;
            }

            // Inner 8-axis E8 stalk rosette jewel + spoke dividers
            if (r <= petalR)
            {
                bool negComp = (st8[sector] < 0.0);
                float3 pCol = negComp ? float3(0.98, 0.36, 0.64) : float3(0.20, 0.84, 1.0);
                float edgeSoft = saturate(petalR - r + 0.6);
                pix += pCol * 0.48 * edgeSoft;
            }

            if (inLoop)
            {
                float alarm = (0.5 + 0.5 * sin(t * 7.5)) * exp(-abs(r - (rad + 2.5)) * 1.1);
                pix += float3(1.0, 0.18, 0.34) * alarm * 0.72;
            }

            uint cr = (uint)clamp(pix.x * 235.0, 0.0, 255.0);
            uint cg = (uint)clamp(pix.y * 235.0, 0.0, 255.0);
            uint cb = (uint)clamp(pix.z * 235.0, 0.0, 255.0);
            if ((cr | cg | cb) != 0u) PlotAdd(W, maxStageX, H, px, py, cr, cg, cb);
        }
    }

    // Embedded 10x12 (2x Scaled 5x6) High-DPI GPU Bitmap Font Renderer beneath each Stalk!
    if (showLabels)
    {
        uint strLen = min(18u, Labels[nid * 6u] & 0xFFu);
        if (strLen > 0u)
        {
            int charAdv = 11; // 10px scaled glyph + 1px kerning -> ultra-crisp at 125% DPI!
            int totalW = (int)strLen * charAdv;
            int startX = (int)round(c.x) - (totalW / 2);
            int startY = (int)round(c.y + rad + 7.0);

            float3 txtCol = sel ? float3(1.0, 0.96, 0.45) : (inLoop ? float3(1.0, 0.76, 0.82) : float3(0.90, 1.0, 0.96));
            uint tr = (uint)(txtCol.x * 235.0), tg = (uint)(txtCol.y * 235.0), tb = (uint)(txtCol.z * 235.0);

            [loop] for (uint ci = 0u; ci < 18u; ci++)
            {
                if (ci >= strLen) break;
                uint ascii = GetPackedChar(nid, ci);
                uint mask = Glyphs[ascii & 127u];
                if (mask == 0u) continue;
                int gx0 = startX + (int)ci * charAdv;

                [loop] for (uint ry = 0u; ry < 6u; ry++)
                {
                    [loop] for (uint rx = 0u; rx < 5u; rx++)
                    {
                        if (((mask >> (ry * 5u + rx)) & 1u) != 0u)
                        {
                            int px0 = gx0 + (int)(rx * 2u);
                            int py0 = startY + (int)(ry * 2u);
                            PlotAdd(W, maxStageX, H, px0,     py0,     tr, tg, tb);
                            PlotAdd(W, maxStageX, H, px0 + 1, py0,     tr, tg, tb);
                            PlotAdd(W, maxStageX, H, px0,     py0 + 1, tr, tg, tb);
                            PlotAdd(W, maxStageX, H, px0 + 1, py0 + 1, tr, tg, tb);
                        }
                    }
                }
            }
        }
    }
}
";
        }

        private static string BuildCompositeHlsl()
        {
            return @"
cbuffer SheafCB : register(b0) { float4 U[16]; };
StructuredBuffer<uint> BasePix : register(t0);
StructuredBuffer<uint> LineRGB : register(t1);
RWTexture2D<float4> OutTex : register(u0);
RWStructuredBuffer<uint> OutReadback : register(u1);

float3 ACESFilm(float3 x)
{
    return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
}

[numthreads(16, 16, 1)]
void CSSheafComp(uint3 dtid : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (dtid.x >= W || dtid.y >= H) return;
    uint idx = dtid.y * W + dtid.x;
    uint bp = BasePix[idx];
    uint b3 = idx * 3u;

    float3 baseCol = float3((float)((bp >> 16) & 255u), (float)((bp >> 8) & 255u), (float)(bp & 255u)) / 255.0;
    float3 lineCol = float3((float)LineRGB[b3 + 0u], (float)LineRGB[b3 + 1u], (float)LineRGB[b3 + 2u]) / 255.0;

    float3 col = ACESFilm((baseCol + lineCol) * 1.06);
    OutTex[dtid.xy] = float4(col.r, col.g, col.b, 1.0);

    if (U[2].w > 0.5)
    {
        uint ur = (uint)(col.r * 255.0 + 0.5), ug = (uint)(col.g * 255.0 + 0.5), ub = (uint)(col.b * 255.0 + 0.5);
        OutReadback[idx] = 0xFF000000u | (min(255u, ur) << 16) | (min(255u, ug) << 8) | min(255u, ub);
    }
}
";
        }

        public void Dispose()
        {
            lock (_lk)
            {
                IsGpuActive = false;
                Rel(_csComp); _csComp = IntPtr.Zero; Rel(_csNodes); _csNodes = IntPtr.Zero;
                Rel(_csEdges); _csEdges = IntPtr.Zero; Rel(_csField); _csField = IntPtr.Zero;
                Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
                Rel(_rbUav); _rbUav = IntPtr.Zero; Rel(_rbBuf); _rbBuf = IntPtr.Zero; Rel(_stgBuf); _stgBuf = IntPtr.Zero;
                Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;
                Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
                Rel(_barSrv); _barSrv = IntPtr.Zero; Rel(_barBuf); _barBuf = IntPtr.Zero;
                Rel(_fontSrv); _fontSrv = IntPtr.Zero; Rel(_fontBuf); _fontBuf = IntPtr.Zero;
                Rel(_labelSrv); _labelSrv = IntPtr.Zero; Rel(_labelBuf); _labelBuf = IntPtr.Zero;
                Rel(_edgeSrv); _edgeSrv = IntPtr.Zero; Rel(_edgeBuf); _edgeBuf = IntPtr.Zero;
                Rel(_nodeSrv); _nodeSrv = IntPtr.Zero; Rel(_nodeBuf); _nodeBuf = IntPtr.Zero;
                Rel(_cb); _cb = IntPtr.Zero; Rel(_backBuf); _backBuf = IntPtr.Zero;
                Rel(_swap); _swap = IntPtr.Zero; Rel(_ctx); _ctx = IntPtr.Zero; Rel(_dev); _dev = IntPtr.Zero;
            }
        }
    }
}
