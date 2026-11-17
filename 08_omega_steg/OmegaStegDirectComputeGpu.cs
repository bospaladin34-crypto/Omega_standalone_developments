using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace OmegaStegStudio
{
    public sealed class OmegaStegGpuEngine : IDisposable
    {
        public const int MaxGpuPoints = 256;

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
        private IntPtr _csField, _csPoints, _csHud, _csComp;

        // Structured GPU Buffers (All SRV ViewDimension == 1u!)
        private IntPtr _ptBuf, _ptSrv;       // MaxGpuPoints * 2 float4: [idealX, idealY, noisyX, noisyY], [corrBits, syndrome, word24, idx]
        private IntPtr _specBuf, _specSrv;   // 4096 floats (64x64 Waterfall Spectrogram)
        private IntPtr _carrBuf, _carrSrv;   // 32 floats (24 carrier levels + 8 Golay parity stats)
        private IntPtr _fontBuf, _fontSrv;   // 128 uints (5x6 ASCII glyph bitmap table)
        private IntPtr _hudTextBuf, _hudTextSrv; // 64 uints (up to 256 HUD banner chars on GPU!)

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
        private int _upPointCount = 0;
        private readonly float[] _cbData = new float[64];
        private readonly float[] _ptPack = new float[MaxGpuPoints * 8];
        private readonly float[] _specPack = new float[4096];
        private readonly float[] _carrPack = new float[32];
        private readonly uint[] _hudPack = new uint[64];

        public volatile bool VSyncEnabled = true;
        public volatile int ViewMode = 2; // 0 = Full 24D Radar, 1 = Full Spectrogram, 2 = Split Radar | Waterfall, 3 = 4D 24-Cell SO(4) | 5-Layer OmegaSec
        public byte ActiveSecLayerMask = 0x1F;
        public bool LastSecSheafOk = true;
        public float LastSecSacPct = 50.0f;
        public bool IsGpuActive { get; private set; }
        public string BackendLabel { get; private set; }
        public string LastError { get; private set; }

        public OmegaStegGpuEngine()
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
                        return false;
                    }

                    _fBuf = Vt<CreateBufDel>(_dev, 3);
                    _fTex = Vt<CreateTex2DDel>(_dev, 5);
                    _fSrv = Vt<CreateSrvDel>(_dev, 7);
                    _fSrvNull = Vt<CreateSrvNullDel>(_dev, 7);
                    _fUav = Vt<CreateUavDel>(_dev, 8);       // Slot 8 = CreateUnorderedAccessView!
                    _fUavNull = Vt<CreateUavNullDel>(_dev, 8);
                    _fCs = Vt<CreateCsDel>(_dev, 18);        // Slot 18 = CreateComputeShader!

                    _fMap = Vt<MapDel>(_ctx, 14);
                    _fUnmap = Vt<UnmapDel>(_ctx, 15);
                    _fDisp = Vt<DispatchDel>(_ctx, 41);
                    _fCopy = Vt<CopyResDel>(_ctx, 47);
                    _fUpd = Vt<UpdateSubDel>(_ctx, 48);
                    _fSetSrv = Vt<CsSetSrvDel>(_ctx, 67);    // Slot 67 = CSSetShaderResources!
                    _fSetUav = Vt<CsSetUavDel>(_ctx, 68);    // Slot 68 = CSSetUnorderedAccessViews!
                    _fSetCs = Vt<CsSetCsDel>(_ctx, 69);      // Slot 69 = CSSetShader!
                    _fSetCb = Vt<CsSetCbDel>(_ctx, 71);      // Slot 71 = CSSetConstantBuffers!

                    _fPresent = Vt<PresentDel>(_swap, 8);
                    _fGetBack = Vt<GetSwapBufDel>(_swap, 9);
                    _fResize = Vt<ResizeBufDel>(_swap, 13);

                    if (_fGetBack(_swap, 0u, ref IID_Tex2D, out _backBuf) < 0 || _backBuf == IntPtr.Zero)
                        return false;

                    BUF_DESC cbd = new BUF_DESC();
                    cbd.ByteWidth = 256u; cbd.Usage = 0u; cbd.BindFlags = 4u;
                    if (_fBuf(_dev, ref cbd, IntPtr.Zero, out _cb) < 0) return false;

                    if (!MakeSrvStructBuf((uint)(MaxGpuPoints * 2), 16u, out _ptBuf, out _ptSrv) ||
                        !MakeSrvStructBuf(4096u, 4u, out _specBuf, out _specSrv) ||
                        !MakeSrvStructBuf(32u, 4u, out _carrBuf, out _carrSrv) ||
                        !MakeSrvStructBuf(64u, 4u, out _hudTextBuf, out _hudTextSrv))
                        return false;

                    uint[] glyphs = BuildAsciiGlyphTable();
                    if (!MakeSrvStructBuf((uint)glyphs.Length, 4u, out _fontBuf, out _fontSrv)) return false;
                    UploadUint(_fontBuf, glyphs);

                    _csField  = CompileCs(BuildFieldHlsl(), "CSLeechField");
                    _csPoints = CompileCs(BuildConstellationHlsl(), "CSLeechPoints");
                    _csHud    = CompileCs(BuildHudGlyphsHlsl(), "CSLeechHud");
                    _csComp   = CompileCs(BuildCompHlsl(), "CSLeechComp");

                    if (_csField == IntPtr.Zero || _csPoints == IntPtr.Zero || _csHud == IntPtr.Zero || _csComp == IntPtr.Zero)
                        return false;

                    if (!EnsureViewportBuffers(w, h)) return false;

                    IsGpuActive = true;
                    BackendLabel = "RTX DirectCompute cs_5_0 VSync";
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
            if (_fBuf(_dev, ref bd, IntPtr.Zero, out buf) < 0 || buf == IntPtr.Zero) return false;
            return CreateStructuredSrvSafe(buf, count, out srv);
        }

        private bool MakeRwStructBuf(uint count, uint stride, out IntPtr buf, out IntPtr srv, out IntPtr uav)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero; uav = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = count * stride; bd.Usage = 0u; bd.BindFlags = 0x88u; bd.MiscFlags = 0x40u; bd.StructureByteStride = stride;
            if (_fBuf(_dev, ref bd, IntPtr.Zero, out buf) < 0 || buf == IntPtr.Zero) return false;
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
                    return false;
            }

            Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
            Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
            Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;
            Rel(_rbUav); _rbUav = IntPtr.Zero; Rel(_rbBuf); _rbBuf = IntPtr.Zero; Rel(_stgBuf); _stgBuf = IntPtr.Zero;

            TEX2D_DESC td = new TEX2D_DESC();
            td.Width = (uint)w; td.Height = (uint)h; td.MipLevels = 1u; td.ArraySize = 1u;
            td.Format = 28u; td.SampleCount = 1u; td.BindFlags = 0x80u;
            if (_fTex(_dev, ref td, IntPtr.Zero, out _outTex) < 0) return false;

            UAV_DESC tud = new UAV_DESC(); tud.Format = 28u; tud.ViewDimension = 4u;
            if (_fUav(_dev, _outTex, ref tud, out _outTexUav) < 0) return false;

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
                if (_fCs(_dev, ptr, len, IntPtr.Zero, out cs) < 0) return IntPtr.Zero;
                return cs;
            }
            finally { Rel(pCode); Rel(pErr); }
        }

        private void UploadFloat(IntPtr dst, float[] arr)
        {
            if (dst == IntPtr.Zero) return;
            GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { _fUpd(_ctx, dst, 0u, IntPtr.Zero, h.AddrOfPinnedObject(), 0u, 0u); }
            finally { h.Free(); }
        }

        private void UploadUint(IntPtr dst, uint[] arr)
        {
            if (dst == IntPtr.Zero) return;
            GCHandle h = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try { _fUpd(_ctx, dst, 0u, IntPtr.Zero, h.AddrOfPinnedObject(), 0u, 0u); }
            finally { h.Free(); }
        }

        private void PackHudBannerStrings(string lineTopLeft, string lineTopRight)
        {
            Array.Clear(_hudPack, 0, _hudPack.Length);
            PackStringAtWord(_hudPack, 0, lineTopLeft ?? "", 44);   // words 0..11 (48 bytes)
            PackStringAtWord(_hudPack, 16, lineTopRight ?? "", 44); // words 16..27 (48 bytes)
        }

        private static void PackStringAtWord(uint[] dst, int baseWord, string s, int maxChars)
        {
            int len = Math.Min(maxChars, s.Length);
            uint cur = (uint)(len & 0xFF);
            for (int i = 0; i < maxChars; i++)
            {
                int slotByte = i + 1;
                uint ch = (i < len && s[i] >= 32 && s[i] <= 126) ? (uint)s[i] : 32u;
                int wIdx = slotByte >> 2;
                int sh = (slotByte & 3) * 8;
                if (sh == 0 && wIdx > 0) cur = 0u;
                cur |= (ch << sh);
                dst[baseWord + wIdx] = cur;
            }
            if (len == 0) dst[baseWord] = 0u;
        }

        public bool RenderGpuFrame(
            GolayDecodeReceipt receipt, OmegaStegAcousticModem modem,
            int activeWord24, int activeSyndrome12, int activeHealedBits,
            int w, int h, float simTime, int[] optionalReadback)
        {
            if (!IsGpuActive) return false;

            int vmode = ViewMode;
            float splitX = (vmode == 2 || vmode == 3) ? (w * 0.58f) : ((vmode == 1) ? 0.0f : w);
            float cx = splitX * 0.5f;
            float cy = h * 0.52f;
            float radScale = Math.Min(splitX, h) * 0.36f;

            Array.Clear(_ptPack, 0, _ptPack.Length);
            if (vmode == 3)
            {
                // Project all 24 Hurwitz Quaternion Vertices & 96 Octahedral Edges of the 4D 24-Cell {3,4,3} under SO(4) Double-Quaternion Rotation!
                float aL = simTime * 0.46f, aR = simTime * 0.31f;
                float cL = (float)Math.Cos(aL), sL = (float)Math.Sin(aL) * 0.57735f;
                float cR = (float)Math.Cos(aR), sR = (float)Math.Sin(aR) * 0.70711f;
                float[] uQ = new float[4] { cL, sL, sL, sL };
                float[] vQ = new float[4] { cR, 0.0f, sR, sR };

                float[] sx24 = new float[24];
                float[] sy24 = new float[24];
                float[] sw24 = new float[24];
                for (int i = 0; i < 24; i++)
                {
                    float q0 = OmegaSecEngine.Hurwitz24Cell[i, 0] * 0.5f;
                    float q1 = OmegaSecEngine.Hurwitz24Cell[i, 1] * 0.5f;
                    float q2 = OmegaSecEngine.Hurwitz24Cell[i, 2] * 0.5f;
                    float q3 = OmegaSecEngine.Hurwitz24Cell[i, 3] * 0.5f;
                    // Left Hamilton product t = uQ * q
                    float t0 = uQ[0]*q0 - uQ[1]*q1 - uQ[2]*q2 - uQ[3]*q3;
                    float t1 = uQ[0]*q1 + uQ[1]*q0 + uQ[2]*q3 - uQ[3]*q2;
                    float t2 = uQ[0]*q2 - uQ[1]*q3 + uQ[2]*q0 + uQ[3]*q1;
                    float t3 = uQ[0]*q3 + uQ[1]*q2 - uQ[2]*q1 + uQ[3]*q0;
                    // Right Hamilton product p = t * vQ
                    float p0 = t0*vQ[0] - t1*vQ[1] - t2*vQ[2] - t3*vQ[3];
                    float p1 = t0*vQ[1] + t1*vQ[0] + t2*vQ[3] - t3*vQ[2];
                    float p2 = t0*vQ[2] - t1*vQ[3] + t2*vQ[0] + t3*vQ[1];
                    float p3 = t0*vQ[3] + t1*vQ[2] - t2*vQ[1] + t3*vQ[0];

                    // 4D -> 3D -> 2D stereographic projection
                    float persp4 = 1.62f / Math.Max(0.55f, 2.35f - p3 * 0.82f);
                    float x3 = (p0 * 0.88f - p2 * 0.46f) * persp4;
                    float y3 = (p1 * 0.90f + p2 * 0.32f) * persp4;
                    sx24[i] = cx + x3 * radScale * 0.92f;
                    sy24[i] = cy + y3 * radScale * 0.92f;
                    sw24[i] = p3;
                }

                // Pack 96 octahedral edges (corrBits = 10..14)
                for (int e = 0; e < 96; e++)
                {
                    int va = OmegaSecEngine.Edges24Cell[e, 0];
                    int vb = OmegaSecEngine.Edges24Cell[e, 1];
                    int o = e * 8;
                    _ptPack[o + 0] = sx24[va];
                    _ptPack[o + 1] = sy24[va];
                    _ptPack[o + 2] = sx24[vb];
                    _ptPack[o + 3] = sy24[vb];
                    _ptPack[o + 4] = LastSecSheafOk ? (10 + (e % 3)) : 19;
                    _ptPack[o + 5] = (sw24[va] + sw24[vb]) * 0.5f;
                    _ptPack[o + 6] = va;
                    _ptPack[o + 7] = vb;
                }
                // Pack 24 Hurwitz quaternion vertices (corrBits = 20 or -20)
                for (int v = 0; v < 24; v++)
                {
                    int o = (96 + v) * 8;
                    _ptPack[o + 0] = sx24[v];
                    _ptPack[o + 1] = sy24[v];
                    _ptPack[o + 2] = sx24[v];
                    _ptPack[o + 3] = sy24[v];
                    _ptPack[o + 4] = LastSecSheafOk ? 20 : -20;
                    _ptPack[o + 5] = sw24[v];
                    _ptPack[o + 6] = v;
                    _ptPack[o + 7] = v;
                }
                _upPointCount = 120;
            }
            else
            {
                int nPts = (receipt != null) ? Math.Min(MaxGpuPoints, receipt.ConstellationPoints.Count) : 0;
                for (int i = 0; i < nPts; i++)
                {
                    GolayFrameVisualPoint vp = receipt.ConstellationPoints[i];
                    int o = i * 8;
                    _ptPack[o + 0] = cx + vp.IdealX * radScale;
                    _ptPack[o + 1] = cy + vp.IdealY * radScale;
                    _ptPack[o + 2] = cx + vp.NoisyX * radScale;
                    _ptPack[o + 3] = cy + vp.NoisyY * radScale;
                    _ptPack[o + 4] = vp.CorrectedBits;
                    _ptPack[o + 5] = vp.Syndrome12;
                    _ptPack[o + 6] = vp.HealedWord24;
                    _ptPack[o + 7] = i;
                }
                _upPointCount = nPts;
            }

            if (modem != null)
            {
                lock (modem.WaterfallSpec)
                {
                    Array.Copy(modem.WaterfallSpec, 0, _specPack, 0, 4096);
                }
                for (int k = 0; k < 24; k++) _carrPack[k] = modem.LiveCarrierLevels24[k];
            }
            for (int r = 0; r < 8; r++) _carrPack[24 + r] = GolayLeechCodec.ParityRows12[r];

            string hudL = (vmode == 3)
                ? ("4D 24-CELL {3,4,3} SO(4) :: MASK=0x" + ActiveSecLayerMask.ToString("X2") + (LastSecSheafOk ? " H1=0 OK" : " H1!=0 QUARANTINE"))
                : ("LEECH-24 COXETER RADAR :: G24[24,12,8] W=" + activeWord24.ToString("X6") + " SYN=" + activeSyndrome12.ToString("X3"));
            string hudR = (vmode == 3)
                ? ("OMEGASEC 5-MATH SHIELD :: SAC=" + LastSecSacPct.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%")
                : ((modem != null && modem.IsTransmitting) ? "48KHZ HARMONIC CHORD TX LIVE"
                 : (modem != null && modem.IsMicListening) ? "48KHZ AIR-GAP MIC RX LIVE"
                 : "WATERFALL SPECTROGRAM + GOLAY [I12 | B12]");
            PackHudBannerStrings(hudL, hudR);

            lock (_lk)
            {
                if (!EnsureViewportBuffers(w, h)) return false;

                UploadFloat(_ptBuf, _ptPack);
                UploadFloat(_specBuf, _specPack);
                UploadFloat(_carrBuf, _carrPack);
                UploadUint(_hudTextBuf, _hudPack);

                Array.Clear(_cbData, 0, _cbData.Length);
                _cbData[0] = w; _cbData[1] = h; _cbData[2] = simTime; _cbData[3] = vmode;
                _cbData[4] = _upPointCount; _cbData[5] = activeWord24; _cbData[6] = activeSyndrome12; _cbData[7] = activeHealedBits;
                _cbData[8] = cx; _cbData[9] = cy; _cbData[10] = radScale; _cbData[11] = (optionalReadback != null) ? 1.0f : 0.0f;
                _cbData[12] = splitX; _cbData[13] = ActiveSecLayerMask; _cbData[14] = LastSecSheafOk ? 1.0f : 0.0f; _cbData[15] = LastSecSacPct;
                UploadFloat(_cb, _cbData);

                IntPtr cb = _cb;
                _fSetCb(_ctx, 0u, 1u, ref cb);
                IntPtr[] zSrv4 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zUav2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
                IntPtr[] zUav1 = new IntPtr[] { IntPtr.Zero };

                uint gx = (uint)((w + 15) / 16), gy = (uint)((h + 15) / 16);

                // Pass 1: CSLeechField (24-Fold Coxeter Shell + Right-Pane Waterfall Spectrogram + Golay Matrix)
                _fSetSrv(_ctx, 0u, 2u, new IntPtr[] { _specSrv, _carrSrv });
                _fSetUav(_ctx, 0u, 2u, new IntPtr[] { _basePixUav, _lineRgbUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csField, IntPtr.Zero, 0u);
                _fDisp(_ctx, gx, gy, 1u);
                _fSetUav(_ctx, 0u, 2u, zUav2, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 4u, zSrv4);

                // Pass 2: CSLeechPoints (24D->2D Constellation Jewels + Error Snap Vectors!)
                if (_upPointCount > 0 && vmode != 1)
                {
                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { _ptSrv });
                    _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _lineRgbUav }, IntPtr.Zero);
                    _fSetCs(_ctx, _csPoints, IntPtr.Zero, 0u);
                    _fDisp(_ctx, (uint)((_upPointCount + 63) / 64), 1u, 1u);
                    _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, new IntPtr[] { IntPtr.Zero });
                }

                // Pass 3: CSLeechHud (Crisp 10x12 GPU Bitmap Font Titles & 24 Carrier Spokes!)
                _fSetSrv(_ctx, 0u, 3u, new IntPtr[] { _carrSrv, _hudTextSrv, _fontSrv });
                _fSetUav(_ctx, 0u, 1u, new IntPtr[] { _lineRgbUav }, IntPtr.Zero);
                _fSetCs(_ctx, _csHud, IntPtr.Zero, 0u);
                _fDisp(_ctx, 2u, 1u, 1u); // 128 threads: 0..23 = 24 spokes, 32..119 = HUD glyphs!
                _fSetUav(_ctx, 0u, 1u, zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 4u, zSrv4);

                // Pass 4: CSLeechComp (ACES Tone-Mapping -> SwapChain)
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

        public void RenderCpuFallback(Bitmap bmp, GolayDecodeReceipt receipt, OmegaStegAcousticModem modem, int activeWord24, int activeSyndrome12)
        {
            int w = bmp.Width, h = bmp.Height;
            float cx = w * 0.30f, cy = h * 0.52f, rad = Math.Min(w * 0.55f, h) * 0.36f;
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font fn = new Font("Consolas", 9.0f, FontStyle.Bold))
            using (SolidBrush brTxt = new SolidBrush(Color.FromArgb(240, 248, 255)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(7, 11, 20));
                using (Pen pRing = new Pen(Color.FromArgb(40, 56, 189, 248), 1.4f))
                {
                    g.DrawEllipse(pRing, cx - rad, cy - rad, rad * 2, rad * 2);
                    g.DrawEllipse(pRing, cx - rad * 0.65f, cy - rad * 0.65f, rad * 1.3f, rad * 1.3f);
                }
                if (receipt != null)
                {
                    for (int i = 0; i < receipt.ConstellationPoints.Count; i++)
                    {
                        GolayFrameVisualPoint vp = receipt.ConstellationPoints[i];
                        float ix = cx + vp.IdealX * rad, iy = cy + vp.IdealY * rad;
                        float nx = cx + vp.NoisyX * rad, ny = cy + vp.NoisyY * rad;
                        if (vp.CorrectedBits > 0)
                        {
                            using (Pen pe = new Pen(Color.FromArgb(251, 191, 36), 1.5f)) g.DrawLine(pe, nx, ny, ix, iy);
                        }
                        using (SolidBrush bp = new SolidBrush(vp.CorrectedBits == 0 ? Color.FromArgb(16, 185, 129) : Color.FromArgb(245, 158, 11)))
                            g.FillEllipse(bp, ix - 4, iy - 4, 8, 8);
                    }
                }
                g.DrawString("OmegaSteg v1.0 :: Golay [24,12,8] Word=0x" + activeWord24.ToString("X6") + " Syn=0x" + activeSyndrome12.ToString("X3"), fn, brTxt, 14, 12);
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
                    if (s[c] == '#') bits |= (1u << (r * 5 + c));
            }
            return bits;
        }

        private static uint[] BuildAsciiGlyphTable()
        {
            uint[] g = new uint[128];
            g[' '] = 0u;
            g['#'] = PackGlyph5x6(".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".....");
            g['('] = PackGlyph5x6("...#.", "..#..", "..#..", "..#..", "..#..", "...#.");
            g[')'] = PackGlyph5x6(".#...", "..#..", "..#..", "..#..", "..#..", ".#...");
            g['+'] = PackGlyph5x6(".....", "..#..", "#####", "..#..", ".....", ".....");
            g[','] = PackGlyph5x6(".....", ".....", ".....", "..##.", "..#..", ".#...");
            g['-'] = PackGlyph5x6(".....", ".....", "#####", ".....", ".....", ".....");
            g['.'] = PackGlyph5x6(".....", ".....", ".....", ".....", "..##.", "..##.");
            g['/'] = PackGlyph5x6("....#", "...#.", "..#..", ".#...", "#....", ".....");
            g[':'] = PackGlyph5x6(".....", "..#..", ".....", ".....", "..#..", ".....");
            g['='] = PackGlyph5x6(".....", "#####", ".....", "#####", ".....", ".....");
            g['['] = PackGlyph5x6(".###.", ".#...", ".#...", ".#...", ".#...", ".###.");
            g[']'] = PackGlyph5x6(".###.", "...#.", "...#.", "...#.", "...#.", ".###.");
            g['|'] = PackGlyph5x6("..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
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
            for (char ch = 'a'; ch <= 'z'; ch++) g[ch] = g[char.ToUpperInvariant(ch)];
            return g;
        }

        private static string BuildFieldHlsl()
        {
            return @"
cbuffer StegCB : register(b0) { float4 U[16]; };
StructuredBuffer<float> Spec : register(t0);
StructuredBuffer<float> Carr : register(t1);
RWStructuredBuffer<uint> BasePix : register(u0);
RWStructuredBuffer<uint> LineRGB : register(u1);

static const uint PARITY12[12] = { 0xA3Bu, 0xC76u, 0x8EDu, 0x9DAu, 0xBB4u, 0xF68u, 0xED1u, 0xDA3u, 0xB47u, 0xE8Eu, 0xD1Du, 0x7FFu };

[numthreads(16, 16, 1)]
void CSLeechField(uint3 dtid : SV_DispatchThreadID)
{
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    if (dtid.x >= W || dtid.y >= H) return;
    uint idx = dtid.y * W + dtid.x;
    uint b3 = idx * 3u;
    LineRGB[b3 + 0u] = 0u; LineRGB[b3 + 1u] = 0u; LineRGB[b3 + 2u] = 0u;

    float t = U[0].z;
    int vmode = (int)U[0].w;
    uint word24 = (uint)U[1].y;
    uint syn12  = (uint)U[1].z;
    float2 center = U[2].xy;
    float radScale = U[2].z;
    float splitX = U[3].x;

    float2 p = float2((float)dtid.x, (float)dtid.y);

    // Right Pane (or Full Spectrogram Mode): Waterfall OR 5-Layer OmegaSec Shield + 12x24 Golay Matrix!
    if (((vmode == 2 || vmode == 3) && p.x >= splitX) || vmode == 1)
    {
        float x0 = (vmode == 1) ? 0.0 : splitX;
        float paneW = (float)W - x0;
        float pad = 20.0;
        float3 mcol = float3(0.022, 0.032, 0.060);

        float sy0 = 38.0, sh = (float)H * 0.50;
        float sx0 = x0 + pad, sw = max(40.0, paneW - pad * 2.0);
        if (p.x >= sx0 && p.x < sx0 + sw && p.y >= sy0 && p.y < sy0 + sh)
        {
            if (vmode == 3)
            {
                // 5-Layer Modular Geometric Security Shield Bars (L1 Trig, L2 Alg, L3 Poly, L4 Topo, L5 Geom)
                uint mask = (uint)U[3].y;
                bool h1Ok = (U[3].z > 0.5);
                float uvy = (p.y - sy0) / sh * 5.0;
                uint lid = min(4u, (uint)uvy);
                float rowFrac = frac(uvy);
                float uvx = (p.x - sx0) / sw;
                bool armed = (((mask >> lid) & 1u) != 0u);
                if (rowFrac > 0.14 && rowFrac < 0.86)
                {
                    float wave = 0.78 + 0.22 * sin(uvx * 18.0 - t * (2.2 + 0.4 * (float)lid) + (float)lid * 1.3);
                    float3 activeCol = !h1Ok ? float3(0.95, 0.18, 0.36)
                        : (lid == 0u ? float3(0.16, 0.78, 0.98)
                        : (lid == 1u ? float3(0.62, 0.42, 0.98)
                        : (lid == 2u ? float3(0.98, 0.76, 0.18)
                        : (lid == 3u ? float3(0.12, 0.92, 0.64)
                        :              float3(0.24, 0.86, 0.78)))));
                    mcol = armed ? (activeCol * wave * (uvx < 0.96 ? 0.88 : 0.35)) : float3(0.06, 0.09, 0.15);
                }
            }
            else
            {
                uint col = min(63u, (uint)(((p.x - sx0) / sw) * 64.0));
                uint row = min(63u, (uint)(((p.y - sy0) / sh) * 64.0));
                float amp = saturate(Spec[row * 64u + col]);
                float3 cLow  = float3(0.03, 0.07, 0.16);
                float3 cMid  = float3(0.08, 0.78, 0.68);
                float3 cHigh = float3(1.00, 0.84, 0.22);
                mcol = (amp < 0.5) ? lerp(cLow, cMid, amp * 2.0) : lerp(cMid, cHigh, (amp - 0.5) * 2.0);
            }
        }

        // Bottom Half (y in H * 0.60 .. H * 0.94): 12x24 Systematic Golay Generator G=[I12 | B12] & Active Codeword Bits!
        float gy0 = (float)H * 0.60, gh = (float)H * 0.34;
        if (p.x >= sx0 && p.x < sx0 + sw && p.y >= gy0 && p.y < gy0 + gh)
        {
            uint gc = min(23u, (uint)(((p.x - sx0) / sw) * 24.0));
            uint gr = min(13u, (uint)(((p.y - gy0) / gh) * 14.0));
            float2 cellUv = frac(float2((p.x - sx0) / sw * 24.0, (p.y - gy0) / gh * 14.0));
            float border = (cellUv.x < 0.08 || cellUv.y < 0.08) ? 0.30 : 1.0;

            if (gr < 12u)
            {
                bool gBit = (gc < 12u) ? (gr == gc) : (((PARITY12[gr] >> (gc - 12u)) & 1u) != 0u);
                mcol = (gBit ? (gc < 12u ? float3(0.14, 0.86, 0.64) : float3(0.24, 0.68, 0.98)) : float3(0.04, 0.06, 0.12)) * border;
            }
            else if (gr == 12u)
            {
                mcol = float3(0.02, 0.03, 0.05);
            }
            else
            {
                // Row 13 = Active 24-bit Golay Codeword + Syndrome highlight!
                bool wBit = (((word24 >> gc) & 1u) != 0u);
                bool sBit = (gc >= 12u) && (((syn12 >> (gc - 12u)) & 1u) != 0u);
                mcol = (sBit ? float3(0.98, 0.24, 0.42) : (wBit ? float3(0.98, 0.80, 0.20) : float3(0.05, 0.09, 0.16))) * border;
            }
        }

        if ((vmode == 2 || vmode == 3) && abs(p.x - splitX) < 1.5) mcol = float3(0.24, 0.76, 0.98);
        uint3 mc8 = (uint3)clamp(mcol * 255.0, 0.0, 255.0);
        BasePix[idx] = 0xFF000000u | (mc8.x << 16) | (mc8.y << 8) | mc8.z;
        return;
    }

    // Left Pane: 24-Fold Coxeter-Plane Leech Lattice Shell & Steiner Octad Harmonics!
    float2 dv = p - center;
    float r = length(dv) / max(32.0, radScale);
    float ang = atan2(dv.y, dv.x);

    float3 col = float3(0.020, 0.030, 0.058);
    // Concentric Leech shells at r = 0.35, 0.68, 1.00
    float sh1 = exp(-abs(r - 0.35) * 48.0) * 0.22;
    float sh2 = exp(-abs(r - 0.68) * 48.0) * 0.32;
    float sh3 = exp(-abs(r - 1.00) * 44.0) * 0.38;
    float spokes24 = exp(-abs(sin(ang * 12.0)) * 28.0) * (r < 1.12 ? 0.10 : 0.02);
    float mandala = 0.5 + 0.5 * cos(24.0 * ang - t * 1.4) * exp(-abs(r - 0.68) * 5.0);

    col += float3(0.06, 0.55, 0.48) * (sh1 + sh2 + spokes24);
    col += float3(0.18, 0.62, 0.92) * sh3;
    col += float3(0.03, 0.12, 0.14) * mandala * 0.35;

    uint3 c8 = (uint3)clamp(col * 255.0, 0.0, 255.0);
    BasePix[idx] = 0xFF000000u | (c8.x << 16) | (c8.y << 8) | c8.z;
}
";
        }

        private static string BuildConstellationHlsl()
        {
            return @"
cbuffer StegCB : register(b0) { float4 U[16]; };
StructuredBuffer<float4> Pts : register(t0);
RWStructuredBuffer<uint> LineRGB : register(u0);

void PlotAdd(uint strideW, uint clipMaxX, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= clipMaxX || (uint)y >= H) return;
    uint b = ((uint)y * strideW + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

[numthreads(64, 1, 1)]
void CSLeechPoints(uint3 dtid : SV_DispatchThreadID)
{
    uint pid = dtid.x;
    uint nPts = (uint)U[1].x;
    if (pid >= nPts) return;

    uint W = (uint)U[0].x, H = (uint)U[0].y;
    int vmode = (int)U[0].w;
    uint clipX = (vmode == 2 || vmode == 3) ? (uint)U[3].x : W;

    float4 p0 = Pts[pid * 2u + 0u]; // idealX, idealY, noisyX, noisyY
    float4 p1 = Pts[pid * 2u + 1u]; // corrBits, syndrome, word24, idx

    float2 ideal = p0.xy;
    float2 noisy = p0.zw;
    int corr = (int)round(p1.x);

    // Mode 3: 4D 24-Cell {3,4,3} SO(4) Octahedral Wireframe Edges (corr in 10..19)
    if (corr >= 10 && corr <= 19)
    {
        float2 seg4 = ideal - noisy;
        float len4 = length(seg4);
        int steps4 = clamp((int)ceil(len4), 2, 360);
        float depthBoost = clamp(0.65 + p1.y * 0.35, 0.30, 1.15);
        float3 ecol = (corr == 19) ? float3(1.0, 0.20, 0.38)
                    : ((corr == 10) ? float3(0.16, 0.86, 0.98)
                    : ((corr == 11) ? float3(0.58, 0.44, 1.00) : float3(0.14, 0.94, 0.68)));
        ecol *= depthBoost;
        [loop] for (int s = 0; s <= 360; s++)
        {
            if (s > steps4) break;
            float2 q = lerp(noisy, ideal, (float)s / (float)steps4);
            PlotAdd(W, clipX, H, (int)round(q.x), (int)round(q.y),
                (uint)(ecol.x * 95.0), (uint)(ecol.y * 95.0), (uint)(ecol.z * 95.0));
        }
        return;
    }

    // Draw golden/crimson Golay syndrome snap trajectory from Noisy -> Ideal Lattice Point!
    if (corr != 0 && abs(corr) != 20)
    {
        float2 seg = ideal - noisy;
        float len = length(seg);
        int steps = clamp((int)ceil(len), 2, 320);
        float3 lcol = (corr > 0) ? float3(1.0, 0.78, 0.18) : float3(1.0, 0.20, 0.38);
        [loop] for (int s = 0; s <= 320; s++)
        {
            if (s > steps) break;
            float2 q = lerp(noisy, ideal, (float)s / (float)steps);
            PlotAdd(W, clipX, H, (int)round(q.x), (int)round(q.y),
                (uint)(lcol.x * 165.0), (uint)(lcol.y * 165.0), (uint)(lcol.z * 165.0));
        }
    }

    // Draw glowing Leech / Hurwitz 24-Cell Jewel at Ideal Point
    float3 jcol = (corr == 20) ? float3(0.28, 0.92, 1.00)
                : ((corr == -20) ? float3(1.00, 0.22, 0.40)
                : ((corr == 0) ? float3(0.14, 0.96, 0.66) : ((corr > 0) ? float3(1.0, 0.84, 0.22) : float3(1.0, 0.22, 0.40))));
    [loop] for (int dy = -8; dy <= 8; dy++)
    {
        [loop] for (int dx = -8; dx <= 8; dx++)
        {
            float r = sqrt((float)(dx * dx + dy * dy));
            if (r > 7.5) continue;
            float v = exp(-r * 0.55) * 210.0;
            PlotAdd(W, clipX, H, (int)round(ideal.x) + dx, (int)round(ideal.y) + dy,
                (uint)clamp(jcol.x * v, 0.0, 255.0),
                (uint)clamp(jcol.y * v, 0.0, 255.0),
                (uint)clamp(jcol.z * v, 0.0, 255.0));
        }
    }
}
";
        }

        private static string BuildHudGlyphsHlsl()
        {
            return @"
cbuffer StegCB : register(b0) { float4 U[16]; };
StructuredBuffer<float> Carr   : register(t0);
StructuredBuffer<uint>  HudTxt : register(t1);
StructuredBuffer<uint>  Glyphs : register(t2);
RWStructuredBuffer<uint> LineRGB : register(u0);

void PlotAdd(uint strideW, uint clipMaxX, uint H, int x, int y, uint cr, uint cg, uint cb)
{
    if (x < 0 || y < 0 || (uint)x >= clipMaxX || (uint)y >= H) return;
    uint b = ((uint)y * strideW + (uint)x) * 3u;
    if (cr > 0u) InterlockedAdd(LineRGB[b + 0u], cr);
    if (cg > 0u) InterlockedAdd(LineRGB[b + 1u], cg);
    if (cb > 0u) InterlockedAdd(LineRGB[b + 2u], cb);
}

uint GetHudChar(uint baseWord, uint charIdx)
{
    uint byteIdx = charIdx + 1u;
    uint w = HudTxt[baseWord + (byteIdx >> 2u)];
    return (w >> ((byteIdx & 3u) * 8u)) & 0x7Fu;
}

[numthreads(64, 1, 1)]
void CSLeechHud(uint3 dtid : SV_DispatchThreadID)
{
    uint tid = dtid.x;
    uint W = (uint)U[0].x, H = (uint)U[0].y;
    int vmode = (int)U[0].w;
    float2 center = U[2].xy;
    float radScale = U[2].z;
    uint splitX = (uint)U[3].x;

    // Threads 0..23: Draw 24 Live Harmonic Subcarrier Amplitude Bars around Outer Coxeter Ring!
    if (tid < 24u && vmode != 1)
    {
        uint clipL = (vmode == 2 || vmode == 3) ? splitX : W;
        float ang = (6.2831853 * (float)tid) / 24.0;
        float2 dir = float2(cos(ang), sin(ang));
        float lvl = clamp(Carr[tid], 0.08, 1.15);
        float2 p0 = center + dir * (radScale * 1.02);
        float2 p1 = center + dir * (radScale * (1.04 + 0.22 * lvl));
        [loop] for (int s = 0; s <= 32; s++)
        {
            float2 q = lerp(p0, p1, (float)s / 32.0);
            PlotAdd(W, clipL, H, (int)round(q.x), (int)round(q.y), 35u, 220u, 245u);
            PlotAdd(W, clipL, H, (int)round(q.x) + 1, (int)round(q.y), 25u, 150u, 190u);
        }
    }

    // Threads 32..75: Left Pane Top 10x12 Crisp GPU Bitmap Font Title!
    if (tid >= 32u && tid < 76u && vmode != 1)
    {
        uint ci = tid - 32u;
        uint len = min(44u, HudTxt[0] & 0xFFu);
        if (ci < len)
        {
            uint ch = GetHudChar(0u, ci);
            uint mask = Glyphs[ch & 127u];
            int gx0 = 14 + (int)ci * 11;
            int gy0 = 12;
            [loop] for (uint ry = 0u; ry < 6u; ry++)
            {
                [loop] for (uint rx = 0u; rx < 5u; rx++)
                {
                    if (((mask >> (ry * 5u + rx)) & 1u) != 0u)
                    {
                        int px = gx0 + (int)(rx * 2u), py = gy0 + (int)(ry * 2u);
                        PlotAdd(W, W, H, px, py, 90u, 245u, 195u);
                        PlotAdd(W, W, H, px + 1, py, 90u, 245u, 195u);
                        PlotAdd(W, W, H, px, py + 1, 90u, 245u, 195u);
                        PlotAdd(W, W, H, px + 1, py + 1, 90u, 245u, 195u);
                    }
                }
            }
        }
    }

    // Threads 80..123: Right Pane Top 10x12 Crisp GPU Bitmap Font Title!
    if (tid >= 80u && tid < 124u && (vmode == 2 || vmode == 1))
    {
        uint ci = tid - 80u;
        uint len = min(44u, HudTxt[16] & 0xFFu);
        if (ci < len)
        {
            uint ch = GetHudChar(16u, ci);
            uint mask = Glyphs[ch & 127u];
            int xBase = (vmode == 1) ? 16 : ((int)splitX + 18);
            int gx0 = xBase + (int)ci * 11;
            int gy0 = 12;
            [loop] for (uint ry = 0u; ry < 6u; ry++)
            {
                [loop] for (uint rx = 0u; rx < 5u; rx++)
                {
                    if (((mask >> (ry * 5u + rx)) & 1u) != 0u)
                    {
                        int px = gx0 + (int)(rx * 2u), py = gy0 + (int)(ry * 2u);
                        PlotAdd(W, W, H, px, py, 120u, 220u, 255u);
                        PlotAdd(W, W, H, px + 1, py, 120u, 220u, 255u);
                        PlotAdd(W, W, H, px, py + 1, 120u, 220u, 255u);
                        PlotAdd(W, W, H, px + 1, py + 1, 120u, 220u, 255u);
                    }
                }
            }
        }
    }
}
";
        }

        private static string BuildCompHlsl()
        {
            return @"
cbuffer StegCB : register(b0) { float4 U[16]; };
StructuredBuffer<uint> BasePix : register(t0);
StructuredBuffer<uint> LineRGB : register(t1);
RWTexture2D<float4> OutTex : register(u0);
RWStructuredBuffer<uint> OutReadback : register(u1);

float3 ACESFilm(float3 x)
{
    return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
}

[numthreads(16, 16, 1)]
void CSLeechComp(uint3 dtid : SV_DispatchThreadID)
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
                Rel(_csComp); _csComp = IntPtr.Zero; Rel(_csHud); _csHud = IntPtr.Zero;
                Rel(_csPoints); _csPoints = IntPtr.Zero; Rel(_csField); _csField = IntPtr.Zero;
                Rel(_outTexUav); _outTexUav = IntPtr.Zero; Rel(_outTex); _outTex = IntPtr.Zero;
                Rel(_rbUav); _rbUav = IntPtr.Zero; Rel(_rbBuf); _rbBuf = IntPtr.Zero; Rel(_stgBuf); _stgBuf = IntPtr.Zero;
                Rel(_lineRgbUav); _lineRgbUav = IntPtr.Zero; Rel(_lineRgbSrv); _lineRgbSrv = IntPtr.Zero; Rel(_lineRgbBuf); _lineRgbBuf = IntPtr.Zero;
                Rel(_basePixUav); _basePixUav = IntPtr.Zero; Rel(_basePixSrv); _basePixSrv = IntPtr.Zero; Rel(_basePixBuf); _basePixBuf = IntPtr.Zero;
                Rel(_hudTextSrv); _hudTextSrv = IntPtr.Zero; Rel(_hudTextBuf); _hudTextBuf = IntPtr.Zero;
                Rel(_fontSrv); _fontSrv = IntPtr.Zero; Rel(_fontBuf); _fontBuf = IntPtr.Zero;
                Rel(_carrSrv); _carrSrv = IntPtr.Zero; Rel(_carrBuf); _carrBuf = IntPtr.Zero;
                Rel(_specSrv); _specSrv = IntPtr.Zero; Rel(_specBuf); _specBuf = IntPtr.Zero;
                Rel(_ptSrv); _ptSrv = IntPtr.Zero; Rel(_ptBuf); _ptBuf = IntPtr.Zero;
                Rel(_cb); _cb = IntPtr.Zero; Rel(_backBuf); _backBuf = IntPtr.Zero;
                Rel(_swap); _swap = IntPtr.Zero; Rel(_ctx); _ctx = IntPtr.Zero; Rel(_dev); _dev = IntPtr.Zero;
            }
        }
    }
}
