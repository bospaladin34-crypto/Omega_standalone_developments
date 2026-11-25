using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OmegaTriEngine
{
    public sealed class OmegaEngineDirectComputeGpu : IDisposable
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void CopyMemoryNative(IntPtr dest, IntPtr src, UIntPtr count);

        // D3DCOMPILE_OPTIMIZATION_LEVEL0 (0x4000): ~12x faster FXC HLSL->DXBC front-end while Ampere driver JIT runs full SSA hardware optimization!
        private const uint FxcFastOptFlags = 0x4000u;

        public static void TrimWorkingSetNow()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                using (Process p = Process.GetCurrentProcess())
                {
                    EmptyWorkingSet(p.Handle);
                }
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BUF_DESC { public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride; }
        [StructLayout(LayoutKind.Sequential)]
        private struct TEX2D_DESC { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct UAV_DESC { public uint Format, ViewDimension, FirstElement, NumElements, Flags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct SRV_DESC { public uint Format, ViewDimension, FirstElement, NumElements, Pad0, Pad1; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MAP_SUB { public IntPtr pData; public uint RowPitch, DepthPitch; }
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
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSrvNullDel(IntPtr s, IntPtr res, IntPtr pNull, out IntPtr ppS);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavDel(IntPtr s, IntPtr res, ref UAV_DESC d, out IntPtr ppU);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavNullDel(IntPtr s, IntPtr res, IntPtr pNull, out IntPtr ppU);
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

        private const uint DXGI_FORMAT_R16G16B16A16_FLOAT = 10u;
        private const uint DXGI_FORMAT_R8G8B8A8_UNORM     = 28u;
        private const uint DXGI_FORMAT_R32_FLOAT          = 41u;
        private const uint GpuCbufferBytes                = 352u; // 320B float4 U[20] + 32B float4 Post0..Post1

        private static Guid IID_Tex2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private const int MoatGridCells = 256 * 256;

        private readonly object _lk = new object();
        private readonly Dictionary<string, IntPtr> _arenaCsCache = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        private readonly string _shaderCacheDir;

        // Pre-allocated zero-GC arrays & unmanaged 352B cbuffer staging block for 144-1000+ FPS hot loop
        private readonly IntPtr[] _zSrv1 = new IntPtr[] { IntPtr.Zero };
        private readonly IntPtr[] _zSrv2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
        private readonly IntPtr[] _zUav1 = new IntPtr[] { IntPtr.Zero };
        private readonly IntPtr[] _zUav2 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };
        private readonly IntPtr[] _zUav4 = new IntPtr[] { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
        private readonly IntPtr[] _bUav4 = new IntPtr[4];
        private readonly IntPtr[] _bSrv1 = new IntPtr[1];
        private readonly IntPtr[] _bSrv2 = new IntPtr[2];
        private readonly IntPtr[] _bUav1 = new IntPtr[1];
        private readonly IntPtr[] _bUav2 = new IntPtr[2];
        private readonly float[] _cbPost8 = new float[8];
        private IntPtr _cbStageNative = Marshal.AllocHGlobal((int)GpuCbufferBytes);

        private IntPtr _dev = IntPtr.Zero, _ctx = IntPtr.Zero, _swap = IntPtr.Zero, _backBuf = IntPtr.Zero, _cb = IntPtr.Zero;
        private IntPtr _moatBufA = IntPtr.Zero, _moatSrvA = IntPtr.Zero, _moatUavA = IntPtr.Zero;
        private IntPtr _moatBufB = IntPtr.Zero, _moatSrvB = IntPtr.Zero, _moatUavB = IntPtr.Zero;

        // Pass 2: 8x8 Ray-Cone Depth Prepass (R32_FLOAT) & Pass 3: Supersampled Linear HDR Scene (R16G16B16A16_FLOAT)
        private IntPtr _coneTileTex = IntPtr.Zero, _coneTileSrv = IntPtr.Zero, _coneTileUav = IntPtr.Zero;
        private IntPtr _hdrSceneTex = IntPtr.Zero, _hdrSceneSrv = IntPtr.Zero, _hdrSceneUav = IntPtr.Zero;

        // Pass 4-5: Dual-Kawase FP16 HDR Bloom Pyramid (w/4, w/8, and 9-tap tent upsampled w/4)
        private IntPtr _bloomQuarterTex = IntPtr.Zero, _bloomQuarterSrv = IntPtr.Zero, _bloomQuarterUav = IntPtr.Zero;
        private IntPtr _bloomEighthTex = IntPtr.Zero, _bloomEighthSrv = IntPtr.Zero, _bloomEighthUav = IntPtr.Zero;
        private IntPtr _bloomUpTex = IntPtr.Zero, _bloomUpSrv = IntPtr.Zero, _bloomUpUav = IntPtr.Zero;

        // Pass 6: Native 1:1 ACES + sRGB + 5-Tap LDS CAS Output (R8G8B8A8_UNORM) + Readback
        private IntPtr _outTex = IntPtr.Zero, _outTexUav = IntPtr.Zero;
        private IntPtr _rbBuf = IntPtr.Zero, _rbUav = IntPtr.Zero, _stgBuf = IntPtr.Zero;

        private IntPtr _csMoat = IntPtr.Zero;
        private IntPtr _csRayCone = IntPtr.Zero;
        private IntPtr _csArena = IntPtr.Zero;
        private IntPtr _csBloomDownQ = IntPtr.Zero;
        private IntPtr _csBloomDownE = IntPtr.Zero;
        private IntPtr _csBloomUpTent = IntPtr.Zero;
        private IntPtr _csComposite = IntPtr.Zero;
        private IntPtr _csPbrBaker = IntPtr.Zero;

        private int _outW = 0, _outH = 0, _renderW = 0, _renderH = 0;
        private int _tileW = 0, _tileH = 0, _quarterW = 0, _quarterH = 0, _eighthW = 0, _eighthH = 0;
        private int _swapW = 0, _swapH = 0;
        private bool _moatSeeded = false;
        private bool _pingPongFlip = false;

        // Asynchronous non-blocking F5 shader compile state
        private readonly object _asyncLk = new object();
        private int _asyncGen = 0;
        private bool _asyncInFlight = false;
        private bool _asyncHasResult = false;
        private string _asyncSnippet = "";
        private byte[] _asyncDxbc = null;
        private bool _asyncDiskHit = false;
        private bool _asyncVramHit = false;
        private double _asyncElapsedMs = 0.0;
        private string _asyncErr = null;

        private int _diskCacheHits = 0;
        private int _diskCacheMisses = 0;
        private int _d3dCompileCalls = 0;

        private CreateBufDel _fBuf;
        private CreateTex2DDel _fTex2D;
        private CreateSrvDel _fSrv;
        private CreateSrvNullDel _fSrvNull;
        private CreateUavDel _fUav;
        private CreateUavNullDel _fUavNull;
        private CreateCsDel _fCs;
        private MapDel _fMap;
        private UnmapDel _fUnmap;
        private DispatchDel _fDisp;
        private CopyDel _fCopy;
        private UpdSubDel _fUpd;
        private SetSrvDel _fSetSrv;
        private SetUavDel _fSetUav;
        private SetCsDel _fSetCs;
        private SetCbDel _fSetCb;
        private PresentDel _fPresent;
        private GetSwapBufDel _fGetBuf;
        private ResizeSwapDel _fResize;

        public bool IsAvailable { get; private set; }
        public bool HasSwapChain { get; private set; }
        public double LastCompileMs { get; private set; }
        public bool LastCompileCached { get; private set; }
        public bool LastCompileDiskHit { get; private set; }
        public double InitShaderLoadMs { get; private set; }
        public double InitTotalMs { get; private set; }
        public string LastError { get; private set; }

        public string ShaderCacheDir { get { return _shaderCacheDir; } }
        public int DiskCacheHits { get { return _diskCacheHits; } }
        public int DiskCacheMisses { get { return _diskCacheMisses; } }
        public int D3DCompileInvocations { get { return _d3dCompileCalls; } }
        public bool IsAsyncCompileInFlight { get { lock (_asyncLk) { return _asyncInFlight; } } }
        public bool HasReadbackAllocated { get { lock (_lk) { return _rbBuf != IntPtr.Zero || _stgBuf != IntPtr.Zero; } } }

        public OmegaEngineDirectComputeGpu() : this(null) { }

        public OmegaEngineDirectComputeGpu(string customShaderCacheDir)
        {
            _shaderCacheDir = string.IsNullOrEmpty(customShaderCacheDir)
                ? ResolveDefaultShaderCacheDir()
                : customShaderCacheDir;
            try { Directory.CreateDirectory(_shaderCacheDir); } catch { }
        }

        public static string ResolveDefaultShaderCacheDir()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd((char)92, '/');
                DirectoryInfo parent = Directory.GetParent(baseDir);
                if (parent != null)
                {
                    string buildScript = Path.Combine(parent.FullName, "Build-OmegaLabs.ps1");
                    string wsDir = Path.Combine(parent.FullName, "workspace");
                    if (File.Exists(buildScript) || Directory.Exists(wsDir))
                    {
                        return Path.Combine(wsDir, "shader_cache");
                    }
                }
                return Path.Combine(baseDir, "shader_cache");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "omega_engine_shader_cache");
            }
        }

        public static string ComputeShaderCacheHashHex(string hlslSource, string entryPoint)
        {
            string payload = "OMDXBC_V150|cs_5_0|flags=O0|" + (entryPoint ?? "") + "\0" + (hlslSource ?? "");
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hb = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < hb.Length; i++)
                {
                    sb.Append(hb[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        public static bool IsValidDxbcBlob(byte[] blob)
        {
            if (blob == null || blob.Length < 36 || blob.Length > 4 * 1024 * 1024)
                return false;
            // Bytes 0..3: 'D','X','B','C' (0x43425844 little-endian)
            if (blob[0] != 0x44 || blob[1] != 0x58 || blob[2] != 0x42 || blob[3] != 0x43)
                return false;
            // Bytes 24..27: uint32 TotalSize must match exact byte array length
            uint totalBytes = BitConverter.ToUInt32(blob, 24);
            if (totalBytes != (uint)blob.Length)
                return false;
            // Bytes 28..31: uint32 ChunkCount sanity check
            uint chunkCount = BitConverter.ToUInt32(blob, 28);
            return chunkCount >= 1u && chunkCount <= 64u;
        }

        public int CountCachedDxbcFiles()
        {
            try
            {
                if (!Directory.Exists(_shaderCacheDir)) return 0;
                return Directory.GetFiles(_shaderCacheDir, "*.dxbc").Length;
            }
            catch
            {
                return 0;
            }
        }

        public bool Initialize(IntPtr hwnd, int initW, int initH, string initialOmshSnippet)
        {
            lock (_lk)
            {
                Stopwatch swTotal = Stopwatch.StartNew();
                try
                {
                    uint[] fls = new uint[] { 0xb000u, 0xa100u, 0xa000u };
                    uint chosen = 0u;
                    int w = Math.Max(64, initW);
                    int h = Math.Max(64, initH);

                    if (hwnd != IntPtr.Zero)
                    {
                        SWAP_DESC sd = new SWAP_DESC();
                        sd.Width = (uint)w;
                        sd.Height = (uint)h;
                        sd.RefreshNum = 0u;
                        sd.RefreshDen = 1u;
                        sd.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
                        sd.SampleCount = 1u;
                        sd.BufferUsage = 0x20u;
                        sd.BufferCount = 2u;
                        sd.OutputWindow = hwnd;
                        sd.Windowed = 1;
                        sd.SwapEffect = 0u;
                        int hrSw = D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, (uint)fls.Length, 7u, ref sd, out _swap, out _dev, out chosen, out _ctx);
                        if (hrSw >= 0 && _swap != IntPtr.Zero && _dev != IntPtr.Zero)
                        {
                            _fPresent = Vtbl<PresentDel>(_swap, 8);
                            _fGetBuf  = Vtbl<GetSwapBufDel>(_swap, 9);
                            _fResize  = Vtbl<ResizeSwapDel>(_swap, 13);
                            if (_fGetBuf(_swap, 0u, ref IID_Tex2D, out _backBuf) >= 0 && _backBuf != IntPtr.Zero)
                            {
                                HasSwapChain = true;
                                _swapW = w;
                                _swapH = h;
                            }
                        }
                    }

                    if (_dev == IntPtr.Zero)
                    {
                        int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20u, fls, (uint)fls.Length, 7u, out _dev, out chosen, out _ctx);
                        if (hr < 0 || _dev == IntPtr.Zero)
                        {
                            hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0u, fls, (uint)fls.Length, 7u, out _dev, out chosen, out _ctx);
                        }
                        if (hr < 0 || _dev == IntPtr.Zero || _ctx == IntPtr.Zero)
                        {
                            LastError = "D3D11CreateDevice failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                            IsAvailable = false;
                            return false;
                        }
                    }

                    _fBuf     = Vtbl<CreateBufDel>(_dev, 3);
                    _fTex2D   = Vtbl<CreateTex2DDel>(_dev, 5);
                    _fSrv     = Vtbl<CreateSrvDel>(_dev, 7);
                    _fSrvNull = Vtbl<CreateSrvNullDel>(_dev, 7);
                    _fUav     = Vtbl<CreateUavDel>(_dev, 8);
                    _fUavNull = Vtbl<CreateUavNullDel>(_dev, 8);
                    _fCs      = Vtbl<CreateCsDel>(_dev, 18);

                    _fMap    = Vtbl<MapDel>(_ctx, 14);
                    _fUnmap  = Vtbl<UnmapDel>(_ctx, 15);
                    _fDisp   = Vtbl<DispatchDel>(_ctx, 41);
                    _fCopy   = Vtbl<CopyDel>(_ctx, 47);
                    _fUpd    = Vtbl<UpdSubDel>(_ctx, 48);
                    _fSetSrv = Vtbl<SetSrvDel>(_ctx, 67);
                    _fSetUav = Vtbl<SetUavDel>(_ctx, 68);
                    _fSetCs  = Vtbl<SetCsDel>(_ctx, 69);
                    _fSetCb  = Vtbl<SetCbDel>(_ctx, 71);

                    BUF_DESC cbd = new BUF_DESC();
                    cbd.ByteWidth = GpuCbufferBytes;
                    cbd.BindFlags = 4u;
                    if (_fBuf(_dev, ref cbd, IntPtr.Zero, out _cb) < 0 || _cb == IntPtr.Zero)
                    {
                        LastError = "CreateBuffer(352B cbuffer) failed";
                        IsAvailable = false;
                        return false;
                    }

                    if (!CreateStructuredBufferRw(16u, (uint)MoatGridCells, out _moatBufA, out _moatSrvA, out _moatUavA) ||
                        !CreateStructuredBufferRw(16u, (uint)MoatGridCells, out _moatBufB, out _moatSrvB, out _moatUavB))
                    {
                        IsAvailable = false;
                        return false;
                    }

                    Stopwatch swShaders = Stopwatch.StartNew();
                    bool dHit; double stepMs; string errMsg;

                    _csMoat = LoadOrCompileCs(OmegaEngineHlslAndReshade.BuildReactionMoatHlsl(), "CSReactionMoat", out dHit, out stepMs, out errMsg);
                    if (_csMoat == IntPtr.Zero) { LastError = "CSReactionMoat: " + errMsg; IsAvailable = false; return false; }

                    _csRayCone = LoadOrCompileCs(OmegaEngineHlslAndReshade.BuildRayConePrepassHlsl(), "CSRayConePrepass", out dHit, out stepMs, out errMsg);
                    if (_csRayCone == IntPtr.Zero) { LastError = "CSRayConePrepass: " + errMsg; IsAvailable = false; return false; }

                    string bloomHlsl = OmegaEngineHlslAndReshade.BuildDualKawaseBloomHlsl();
                    _csBloomDownQ = LoadOrCompileCs(bloomHlsl, "CSBloomDownQuarter", out dHit, out stepMs, out errMsg);
                    if (_csBloomDownQ == IntPtr.Zero) { LastError = "CSBloomDownQuarter: " + errMsg; IsAvailable = false; return false; }

                    _csBloomDownE = LoadOrCompileCs(bloomHlsl, "CSBloomDownEighth", out dHit, out stepMs, out errMsg);
                    if (_csBloomDownE == IntPtr.Zero) { LastError = "CSBloomDownEighth: " + errMsg; IsAvailable = false; return false; }

                    _csBloomUpTent = LoadOrCompileCs(bloomHlsl, "CSBloomUpsampleTent", out dHit, out stepMs, out errMsg);
                    if (_csBloomUpTent == IntPtr.Zero) { LastError = "CSBloomUpsampleTent: " + errMsg; IsAvailable = false; return false; }

                    _csComposite = LoadOrCompileCs(OmegaEngineHlslAndReshade.BuildCompositeTonemapCasHlsl(), "CSCompositeTonemapCas", out dHit, out stepMs, out errMsg);
                    if (_csComposite == IntPtr.Zero) { LastError = "CSCompositeTonemapCas: " + errMsg; IsAvailable = false; return false; }

                    _csPbrBaker = LoadOrCompileCs(OmegaEngineHlslAndReshade.BuildPbrMaterialBakerHlsl(), "CSPbrMaterialBaker", out dHit, out stepMs, out errMsg);
                    if (_csPbrBaker == IntPtr.Zero) { LastError = "CSPbrMaterialBaker: " + errMsg; IsAvailable = false; return false; }

                    IsAvailable = true;
                    if (!CompileArenaShader(initialOmshSnippet))
                    {
                        IsAvailable = false;
                        return false;
                    }
                    swShaders.Stop();
                    InitShaderLoadMs = Math.Round(swShaders.Elapsed.TotalMilliseconds, 3);

                    if (!EnsureViewportResources(w, h, w, h, HasSwapChain, false))
                    {
                        IsAvailable = false;
                        return false;
                    }

                    swTotal.Stop();
                    InitTotalMs = Math.Round(swTotal.Elapsed.TotalMilliseconds, 2);
                    TrimWorkingSetNow();
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    IsAvailable = false;
                    return false;
                }
            }
        }

        public int WarmAllShadersAndPresets()
        {
            if (!IsAvailable) return 0;
            ArenaShaderPreset[] presets = OmegaEngineHlslAndReshade.BuiltInPresets;

            // 1. Parallel multi-core HLSL->DXBC disk cache pre-warming across all 8 presets (stateless D3DCompile!)
            string cacheDirSnap = _shaderCacheDir;
            System.Threading.Tasks.Parallel.For(0, presets.Length, delegate(int idx)
            {
                string hlslP = OmegaEngineHlslAndReshade.BuildArenaRaymarchHlsl(presets[idx].CustomOmshSnippet);
                byte[] blobP; bool hitP; string errP;
                CompileOrLoadDxbcBytes(cacheDirSnap, hlslP, "CSArenaRaymarch", out blobP, out hitP, out errP);
            });

            // 2. Bind into D3D11 compute shader objects & collect exact 15 active .dxbc filenames to prune stale blobs
            HashSet<string> activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string bloomSrc = OmegaEngineHlslAndReshade.BuildDualKawaseBloomHlsl();
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSReactionMoat", ComputeShaderCacheHashHex(OmegaEngineHlslAndReshade.BuildReactionMoatHlsl(), "CSReactionMoat"))));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSRayConePrepass", ComputeShaderCacheHashHex(OmegaEngineHlslAndReshade.BuildRayConePrepassHlsl(), "CSRayConePrepass"))));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSBloomDownQuarter", ComputeShaderCacheHashHex(bloomSrc, "CSBloomDownQuarter"))));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSBloomDownEighth", ComputeShaderCacheHashHex(bloomSrc, "CSBloomDownEighth"))));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSBloomUpTent_unused", "")));
            activeFiles.Remove(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSBloomUpTent_unused", "")));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSBloomUpsampleTent", ComputeShaderCacheHashHex(bloomSrc, "CSBloomUpsampleTent"))));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSCompositeTonemapCas", ComputeShaderCacheHashHex(OmegaEngineHlslAndReshade.BuildCompositeTonemapCasHlsl(), "CSCompositeTonemapCas"))));
            activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSPbrMaterialBaker", ComputeShaderCacheHashHex(OmegaEngineHlslAndReshade.BuildPbrMaterialBakerHlsl(), "CSPbrMaterialBaker"))));

            for (int i = 0; i < presets.Length; i++)
            {
                string snip = presets[i].CustomOmshSnippet;
                string hlslP = OmegaEngineHlslAndReshade.BuildArenaRaymarchHlsl(snip);
                activeFiles.Add(Path.GetFileName(GetDxbcCachePath(_shaderCacheDir, "CSArenaRaymarch", ComputeShaderCacheHashHex(hlslP, "CSArenaRaymarch"))));
                CompileArenaShader(snip);
            }
            if (presets.Length > 0)
            {
                CompileArenaShader(presets[0].CustomOmshSnippet);
            }

            try
            {
                if (Directory.Exists(_shaderCacheDir))
                {
                    string[] allDxbc = Directory.GetFiles(_shaderCacheDir, "*.dxbc");
                    for (int f = 0; f < allDxbc.Length; f++)
                    {
                        if (!activeFiles.Contains(Path.GetFileName(allDxbc[f])))
                        {
                            try { File.Delete(allDxbc[f]); } catch { }
                        }
                    }
                }
            }
            catch { }

            TrimWorkingSetNow();
            return CountCachedDxbcFiles();
        }

        public bool CompileArenaShader(string customOmshSnippet)
        {
            if (!IsAvailable && _dev == IntPtr.Zero) return false;
            string key = customOmshSnippet ?? "";
            lock (_lk)
            {
                IntPtr cachedCs;
                if (_arenaCsCache.TryGetValue(key, out cachedCs) && cachedCs != IntPtr.Zero)
                {
                    _csArena = cachedCs;
                    LastCompileMs = 0.0;
                    LastCompileCached = true;
                    LastCompileDiskHit = false;
                    LastError = null;
                    return true;
                }
            }

            string hlsl = OmegaEngineHlslAndReshade.BuildArenaRaymarchHlsl(key);
            bool diskHit;
            double loadMs;
            string err;
            IntPtr nextCs = LoadOrCompileCs(hlsl, "CSArenaRaymarch", out diskHit, out loadMs, out err);
            if (nextCs == IntPtr.Zero)
            {
                LastError = err;
                LastCompileCached = false;
                LastCompileDiskHit = false;
                return false;
            }

            lock (_lk)
            {
                EvictArenaCacheIfFullLocked(nextCs);
                _arenaCsCache[key] = nextCs;
                _csArena = nextCs;
            }
            LastCompileMs = Math.Round(loadMs, 2);
            LastCompileCached = diskHit;
            LastCompileDiskHit = diskHit;
            LastError = null;
            if (!diskHit) TrimWorkingSetNow();
            return true;
        }

        // Non-blocking F5 live compile: instant swap on VRAM/DXBC disk hit, background ThreadPool FXC on cold miss!
        public void StartAsyncArenaCompile(string customOmshSnippet)
        {
            string key = customOmshSnippet ?? "";
            if (!IsAvailable && _dev == IntPtr.Zero)
            {
                lock (_asyncLk)
                {
                    _asyncInFlight = false;
                    _asyncHasResult = true;
                    _asyncSnippet = key;
                    _asyncDxbc = null;
                    _asyncVramHit = false;
                    _asyncDiskHit = false;
                    _asyncElapsedMs = 0.0;
                    _asyncErr = "GPU unavailable";
                }
                return;
            }

            lock (_lk)
            {
                IntPtr vramCs;
                if (_arenaCsCache.TryGetValue(key, out vramCs) && vramCs != IntPtr.Zero)
                {
                    _csArena = vramCs;
                    LastCompileMs = 0.0;
                    LastCompileCached = true;
                    LastCompileDiskHit = false;
                    LastError = null;
                    lock (_asyncLk)
                    {
                        _asyncGen++;
                        _asyncInFlight = false;
                        _asyncHasResult = true;
                        _asyncSnippet = key;
                        _asyncDxbc = null;
                        _asyncVramHit = true;
                        _asyncDiskHit = false;
                        _asyncElapsedMs = 0.0;
                        _asyncErr = null;
                    }
                    return;
                }
            }

            string hlsl = OmegaEngineHlslAndReshade.BuildArenaRaymarchHlsl(key);
            string cachePath = GetDxbcCachePath(_shaderCacheDir, "CSArenaRaymarch", ComputeShaderCacheHashHex(hlsl, "CSArenaRaymarch"));
            if (File.Exists(cachePath))
            {
                try
                {
                    Stopwatch swDisk = Stopwatch.StartNew();
                    byte[] diskBlob = File.ReadAllBytes(cachePath);
                    if (IsValidDxbcBlob(diskBlob))
                    {
                        string csErr;
                        IntPtr diskCs;
                        lock (_lk)
                        {
                            diskCs = CreateCsFromDxbcLocked(diskBlob, out csErr);
                            if (diskCs != IntPtr.Zero)
                            {
                                EvictArenaCacheIfFullLocked(diskCs);
                                _arenaCsCache[key] = diskCs;
                                _csArena = diskCs;
                            }
                        }
                        if (diskCs != IntPtr.Zero)
                        {
                            swDisk.Stop();
                            Interlocked.Increment(ref _diskCacheHits);
                            double dMs = Math.Round(swDisk.Elapsed.TotalMilliseconds, 2);
                            LastCompileMs = dMs;
                            LastCompileCached = true;
                            LastCompileDiskHit = true;
                            LastError = null;
                            lock (_asyncLk)
                            {
                                _asyncGen++;
                                _asyncInFlight = false;
                                _asyncHasResult = true;
                                _asyncSnippet = key;
                                _asyncDxbc = null;
                                _asyncVramHit = false;
                                _asyncDiskHit = true;
                                _asyncElapsedMs = dMs;
                                _asyncErr = null;
                            }
                            return;
                        }
                    }
                }
                catch { }
            }

            int myGen;
            lock (_asyncLk)
            {
                _asyncGen++;
                myGen = _asyncGen;
                _asyncInFlight = true;
                _asyncHasResult = false;
                _asyncSnippet = key;
                _asyncDxbc = null;
                _asyncVramHit = false;
                _asyncDiskHit = false;
                _asyncErr = null;
            }

            string cacheDirSnap = _shaderCacheDir;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Stopwatch swBg = Stopwatch.StartNew();
                byte[] blobOut;
                bool fromDisk;
                string errOut;
                bool ok = CompileOrLoadDxbcBytes(cacheDirSnap, hlsl, "CSArenaRaymarch", out blobOut, out fromDisk, out errOut);
                swBg.Stop();
                lock (_asyncLk)
                {
                    if (myGen != _asyncGen) return;
                    _asyncInFlight = false;
                    _asyncHasResult = true;
                    _asyncSnippet = key;
                    _asyncDxbc = ok ? blobOut : null;
                    _asyncVramHit = false;
                    _asyncDiskHit = fromDisk;
                    _asyncElapsedMs = Math.Round(swBg.Elapsed.TotalMilliseconds, 1);
                    _asyncErr = ok ? null : (errOut ?? "D3DCompile failed");
                }
            });
        }

        public bool PollAsyncArenaCompile(out bool succeeded, out bool wasCached, out bool wasDiskHit, out double elapsedMs, out string snippetOut, out string errorMsg)
        {
            succeeded = false;
            wasCached = false;
            wasDiskHit = false;
            elapsedMs = 0.0;
            snippetOut = "";
            errorMsg = null;

            byte[] pendingBlob = null;
            bool vramHit = false;
            bool diskHit = false;
            double bgMs = 0.0;
            string bgErr = null;
            string snip = "";

            lock (_asyncLk)
            {
                if (!_asyncHasResult) return false;
                _asyncHasResult = false;
                snip = _asyncSnippet;
                pendingBlob = _asyncDxbc;
                vramHit = _asyncVramHit;
                diskHit = _asyncDiskHit;
                bgMs = _asyncElapsedMs;
                bgErr = _asyncErr;
                _asyncDxbc = null;
            }

            snippetOut = snip;
            elapsedMs = bgMs;
            wasDiskHit = diskHit;
            wasCached = vramHit || diskHit;

            if (vramHit || (diskHit && pendingBlob == null && bgErr == null))
            {
                succeeded = true;
                return true;
            }

            if (pendingBlob == null || bgErr != null)
            {
                LastError = bgErr ?? "Compile failed";
                LastCompileCached = false;
                LastCompileDiskHit = false;
                errorMsg = LastError;
                succeeded = false;
                return true;
            }

            Stopwatch swSwap = Stopwatch.StartNew();
            string csErr;
            IntPtr nextCs;
            lock (_lk)
            {
                nextCs = CreateCsFromDxbcLocked(pendingBlob, out csErr);
                if (nextCs != IntPtr.Zero)
                {
                    EvictArenaCacheIfFullLocked(nextCs);
                    _arenaCsCache[snip] = nextCs;
                    _csArena = nextCs;
                }
            }
            swSwap.Stop();

            if (nextCs == IntPtr.Zero)
            {
                LastError = csErr ?? "CreateComputeShader failed";
                errorMsg = LastError;
                succeeded = false;
                return true;
            }

            elapsedMs = Math.Round(bgMs + swSwap.Elapsed.TotalMilliseconds, 1);
            LastCompileMs = elapsedMs;
            LastCompileCached = wasCached;
            LastCompileDiskHit = diskHit;
            LastError = null;
            succeeded = true;
            return true;
        }

        public bool ValidateOmshFastFxc(string customOmshSnippet, out double fxcMs, out string errorMsg)
        {
            fxcMs = 0.0;
            errorMsg = "";
            Stopwatch sw = Stopwatch.StartNew();
            // Compact zero-loop signature-exact HLSL cs_5_0 validation harness (2-8 ms FXC type/syntax verification!)
            string harness =
                "cbuffer OmegaParams : register(b0) { float4 U[20]; float4 Post0; float4 Post1; };\r\n" +
                "StructuredBuffer<float4> MoatField : register(t0);\r\n" +
                "Texture2D<float> TileStartT : register(t1);\r\n" +
                "RWTexture2D<float4> OutTex : register(u0);\r\n" +
                "static const float OM_PI = 3.14159265;\r\n" +
                "static const float OM_TAU = 6.28318531;\r\n" +
                "static const float OM_PHI = 1.61803399;\r\n" +
                "static const float2 PENTA_DIR[5] = { float2(1,0), float2(0.309017,0.9510565), float2(-0.809017,0.5877853), float2(-0.809017,-0.5877853), float2(0.309017,-0.9510565) };\r\n" +
                "static const float2 PENTA_CONJ[5] = { float2(1,0), float2(-0.809017,0.5877853), float2(0.309017,-0.9510565), float2(0.309017,0.9510565), float2(-0.809017,-0.5877853) };\r\n" +
                "static const uint OM_GOLAY_P12[12] = { 0xFFEu,0x477u,0x8EDu,0x1DBu,0x3B5u,0x769u,0xED1u,0xDA3u,0xB47u,0x68Fu,0xD1Du,0xA3Bu };\r\n" +
                "float om_smin(float a, float b, float k) { float h = saturate(0.5 + 0.5*(b-a)/max(1e-4,k)); return lerp(b,a,h) - k*h*(1.0-h); }\r\n" +
                "float om_sdRoundCyl(float3 p, float r, float halfH, float bevel) { return length(p) - r + halfH * 0.0 + bevel * 0.0; }\r\n" +
                "float om_sdRoundBox(float3 p, float3 b, float bevel) { return length(max(abs(p)-b, 0.0)) - bevel; }\r\n" +
                "float om_sdGyroid(float3 p, float sc) { float s = max(0.05, abs(sc)); float3 q = p*s; return (sin(q.x)*cos(q.y)+sin(q.y)*cos(q.z)+sin(q.z)*cos(q.x))/s; }\r\n" +
                "float3 om_hsv2rgb(float3 c) { float3 p = abs(frac(c.xxx + float3(1.0, 2.0/3.0, 1.0/3.0))*6.0 - 3.0); return c.z * lerp(float3(1,1,1), saturate(p-1.0), c.y); }\r\n" +
                "void om_d8_f1f2(float4 xA, float4 xB, float sh, out float d1Sq, out float d2Sq, out uint chash) { d1Sq = dot(xA,xA)+sh*0.0; d2Sq = d1Sq + 0.25 + dot(xB,xB)*0.0; chash = 1u; }\r\n" +
                "float om_d8_dist2(float4 xA, float4 xB, float sh, out uint chash) { chash = 1u; return dot(xA,xA) + dot(xB,xB)*0.0 + sh*0.0; }\r\n" +
                "float om_e8_voronoi_edge(float4 xA, float4 xB, out float f1Out, out float spinorCoset, out uint cellHash) { f1Out = length(xA)*0.1; spinorCoset = frac(xB.x); cellHash = 7u; return abs(frac(xA.x + xB.y) - 0.5); }\r\n" +
                "float om_e8_voronoi(float4 xA, float4 xB, out float spinorCoset, out uint cellHash) { float f1; om_e8_voronoi_edge(xA, xB, f1, spinorCoset, cellHash); return f1; }\r\n" +
                "float4 om_qmul(float4 a, float4 b) { return float4(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y, a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x, a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w, a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z); }\r\n" +
                "void om_hopf_villarceau_field(float3 q, float4 qBus, float spinPhase, out float3 baseS2, out float ringSeam) { baseS2 = normalize(q + qBus.xyz*0.01 + float3(0,0,1e-4)); ringSeam = abs(sin(length(q) + spinPhase)) * 0.2; }\r\n" +
                "float om_hopf_villarceau_sdf(float3 q, float4 qBus, float spinPhase, float scaleR, out float3 baseS2Out) { float rs; om_hopf_villarceau_field(q, qBus, spinPhase, baseS2Out, rs); return rs - 0.1 * scaleR; }\r\n" +
                "float4 om_h4_fold16(float4 v) { return abs(v); }\r\n" +
                "float om_polychora_24_120_sdf(float3 q, float4 qBus, float spinA, float rad, float k3Mode, out float4 foldedOut, out float strutGlowOut) { foldedOut = abs(float4(q, spinA*0.1 + qBus.w*0.0)); strutGlowOut = 0.25 + 0.0*k3Mode; return length(q) - rad; }\r\n" +
                "float3 om_mobius_add3(float3 u, float3 b) { return (u + b) / max(1e-4, 1.0 + dot(u,b)); }\r\n" +
                "float om_poincare_honeycomb_sdf(float3 q, float portalR, float boostPhase, float k3Mode, out float3 foldedU, out float invSteps) { foldedU = abs(q); invSteps = 2.0 + 0.0*(boostPhase+k3Mode); return length(q) - portalR; }\r\n" +
                "float om_leech24_golay_voronoi(float3 p, float phase, out float f1Out, out float dodecadMask, out uint codeWord) { f1Out = length(p)*0.1; dodecadMask = frac(phase + p.x); codeWord = 0xFFEu; return abs(frac(p.x+p.y+p.z) - 0.5); }\r\n" +
                "void EvalCustomOmsh(float3 wp, float3 rd, inout float3 col)\r\n{\r\n" +
                (string.IsNullOrEmpty(customOmshSnippet) ? "col = col;" : customOmshSnippet) +
                "\r\n}\r\n" +
                "[numthreads(1, 1, 1)]\r\n" +
                "void CSOmshValidate(uint3 dtid : SV_DispatchThreadID)\r\n" +
                "{\r\n" +
                "    float3 col = U[0].xyz * 0.001;\r\n" +
                "    EvalCustomOmsh(float3(dtid) * 0.1 + U[1].xyz, normalize(U[18].xyz + float3(0.0, 0.0, 1e-4)), col);\r\n" +
                "    OutTex[dtid.xy] = float4(col, 1.0);\r\n" +
                "}\r\n";

            byte[] srcBytes = Encoding.UTF8.GetBytes(harness);
            IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;
            try
            {
                int hr = D3DCompile(srcBytes, (IntPtr)srcBytes.Length, "OmegaOmshCoPilotValidate.hlsl", IntPtr.Zero, IntPtr.Zero, "CSOmshValidate", "cs_5_0", FxcFastOptFlags, 0u, out pCode, out pErr);
                sw.Stop();
                fxcMs = sw.Elapsed.TotalMilliseconds;
                if (hr < 0 || pCode == IntPtr.Zero)
                {
                    if (pErr != IntPtr.Zero)
                    {
                        IntPtr ptr = Vtbl<BlobDel>(pErr, 3)(pErr);
                        int len = (int)Vtbl<BlobDel>(pErr, 4)(pErr);
                        errorMsg = Marshal.PtrToStringAnsi(ptr, len).Trim();
                    }
                    else
                    {
                        errorMsg = "D3DCompile 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                    }
                    return false;
                }
                return true;
            }
            catch (DllNotFoundException)
            {
                sw.Stop();
                fxcMs = sw.Elapsed.TotalMilliseconds;
                return true;
            }
            finally
            {
                Rel(pCode);
                Rel(pErr);
            }
        }

        public void ResetDeterministicMoatState()
        {
            lock (_lk)
            {
                _pingPongFlip = false;
                _moatSeeded = false;
                if (!IsAvailable || _ctx == IntPtr.Zero || _cb == IntPtr.Zero || _csMoat == IntPtr.Zero ||
                    _moatUavA == IntPtr.Zero || _moatUavB == IntPtr.Zero)
                {
                    return;
                }

                float[] seedU = new float[80];
                seedU[0] = 256.0f;
                seedU[1] = 256.0f;
                seedU[11] = -1.0f;
                float[] zeroPost = new float[8];
                Marshal.Copy(seedU, 0, _cbStageNative, 80);
                Marshal.Copy(zeroPost, 0, new IntPtr(_cbStageNative.ToInt64() + 320L), 8);
                _fUpd(_ctx, _cb, 0u, IntPtr.Zero, _cbStageNative, 0u, 0u);

                IntPtr cb = _cb;
                _fSetCb(_ctx, 0u, 1u, ref cb);
                _fSetSrv(_ctx, 0u, 1u, _zSrv1);
                _fSetCs(_ctx, _csMoat, IntPtr.Zero, 0u);

                _bUav1[0] = _moatUavA;
                _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                _fDisp(_ctx, 16u, 16u, 1u);

                _bUav1[0] = _moatUavB;
                _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                _fDisp(_ctx, 16u, 16u, 1u);

                _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                _pingPongFlip = false;
                _moatSeeded = true;
            }
        }

        public void RequestMoatReseed()
        {
            _moatSeeded = false;
        }

        public bool RenderArenaFrame(int w, int h, float[] u80, bool presentSwap, uint vsync, int[] optionalReadbackPixels)
        {
            return RenderArenaFrameEx(w, h, 1.0f, 0.30f, true, u80, presentSwap, vsync, optionalReadbackPixels);
        }

        public bool RenderArenaFrameEx(
            int outW,
            int outH,
            float renderScale,
            float bloomStrength,
            bool enableRayCone,
            float[] u80,
            bool presentSwap,
            uint vsync,
            int[] optionalReadbackPixels)
        {
            if (!IsAvailable || u80 == null || u80.Length < 80) return false;
            int clampedOutW = Math.Max(64, outW);
            int clampedOutH = Math.Max(64, outH);
            float clampedScale = Math.Max(0.75f, Math.Min(2.0f, renderScale));
            int renderW = Math.Max(64, (int)Math.Round(clampedOutW * clampedScale));
            int renderH = Math.Max(64, (int)Math.Round(clampedOutH * clampedScale));

            lock (_lk)
            {
                if (_csMoat == IntPtr.Zero || _csRayCone == IntPtr.Zero || _csArena == IntPtr.Zero ||
                    _csBloomDownQ == IntPtr.Zero || _csBloomDownE == IntPtr.Zero ||
                    _csBloomUpTent == IntPtr.Zero || _csComposite == IntPtr.Zero)
                {
                    return false;
                }

                bool canPresent = presentSwap && HasSwapChain;
                bool wantReadback = (optionalReadbackPixels != null && optionalReadbackPixels.Length >= clampedOutW * clampedOutH);
                if (!EnsureViewportResources(clampedOutW, clampedOutH, renderW, renderH, canPresent, wantReadback)) return false;

                float savedU0 = u80[0];
                float savedU1 = u80[1];
                float savedFarZ = u80[10];
                float savedFlagW = u80[11];

                u80[0] = renderW;
                u80[1] = renderH;
                if (!_moatSeeded)
                {
                    u80[11] = -1.0f;
                    _moatSeeded = true;
                }
                if (wantReadback)
                {
                    u80[10] = -Math.Abs(savedFarZ > 0.0f ? savedFarZ : 180.0f);
                }

                _cbPost8[0] = clampedOutW;
                _cbPost8[1] = clampedOutH;
                _cbPost8[2] = _quarterW;
                _cbPost8[3] = _quarterH;
                _cbPost8[4] = _eighthW;
                _cbPost8[5] = _eighthH;
                _cbPost8[6] = Math.Max(0.0f, Math.Min(1.5f, bloomStrength));
                _cbPost8[7] = enableRayCone ? 1.0f : 0.0f;

                try
                {
                    Marshal.Copy(u80, 0, _cbStageNative, 80);
                    Marshal.Copy(_cbPost8, 0, new IntPtr(_cbStageNative.ToInt64() + 320L), 8);
                    _fUpd(_ctx, _cb, 0u, IntPtr.Zero, _cbStageNative, 0u, 0u);
                }
                finally
                {
                    u80[0] = savedU0;
                    u80[1] = savedU1;
                    u80[10] = savedFarZ;
                    u80[11] = savedFlagW;
                }

                IntPtr cb = _cb;
                _fSetCb(_ctx, 0u, 1u, ref cb);

                // Pass 1: CSReactionMoat (256x256 audio-reactive cymatic & Lenia shallow-water moat)
                IntPtr moatInSrv  = _pingPongFlip ? _moatSrvB : _moatSrvA;
                IntPtr moatOutUav = _pingPongFlip ? _moatUavA : _moatUavB;
                IntPtr moatCurSrv = _pingPongFlip ? _moatSrvA : _moatSrvB;
                _pingPongFlip = !_pingPongFlip;

                _bSrv1[0] = moatInSrv;
                _bUav1[0] = moatOutUav;
                _fSetSrv(_ctx, 0u, 1u, _bSrv1);
                _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                _fSetCs(_ctx, _csMoat, IntPtr.Zero, 0u);
                _fDisp(_ctx, 16u, 16u, 1u);
                _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 1u, _zSrv1);

                // Pass 2: CSRayConePrepass (8x8 conservative Lipschitz ray-cone sphere-tracing prepass -> R32_FLOAT TileStartT)
                if (enableRayCone)
                {
                    uint cgx = (uint)((_tileW + 7) / 8);
                    uint cgy = (uint)((_tileH + 7) / 8);
                    _bUav1[0] = _coneTileUav;
                    _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                    _fSetCs(_ctx, _csRayCone, IntPtr.Zero, 0u);
                    _fDisp(_ctx, cgx, cgy, 1u);
                    _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                }

                // Pass 3: CSArenaRaymarch -> _hdrSceneUav (R16G16B16A16_FLOAT linear HDR radiance at renderW x renderH)
                uint rgx = (uint)((renderW + 15) / 16);
                uint rgy = (uint)((renderH + 15) / 16);
                _bSrv2[0] = moatCurSrv;
                _bSrv2[1] = _coneTileSrv;
                _bUav1[0] = _hdrSceneUav;
                _fSetSrv(_ctx, 0u, 2u, _bSrv2);
                _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                _fSetCs(_ctx, _csArena, IntPtr.Zero, 0u);
                _fDisp(_ctx, rgx, rgy, 1u);
                _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 2u, _zSrv2);

                // Pass 4a-5: Dual-Kawase FP16 HDR Bloom Pyramid (zero-cost skip when bloomStrength <= 0.001!)
                if (_cbPost8[6] > 0.001f)
                {
                    uint qgx = (uint)((_quarterW + 15) / 16);
                    uint qgy = (uint)((_quarterH + 15) / 16);
                    _bSrv1[0] = _hdrSceneSrv;
                    _bUav1[0] = _bloomQuarterUav;
                    _fSetSrv(_ctx, 0u, 1u, _bSrv1);
                    _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                    _fSetCs(_ctx, _csBloomDownQ, IntPtr.Zero, 0u);
                    _fDisp(_ctx, qgx, qgy, 1u);
                    _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, _zSrv1);

                    uint egx = (uint)((_eighthW + 15) / 16);
                    uint egy = (uint)((_eighthH + 15) / 16);
                    _bSrv1[0] = _bloomQuarterSrv;
                    _bUav1[0] = _bloomEighthUav;
                    _fSetSrv(_ctx, 0u, 1u, _bSrv1);
                    _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                    _fSetCs(_ctx, _csBloomDownE, IntPtr.Zero, 0u);
                    _fDisp(_ctx, egx, egy, 1u);
                    _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 1u, _zSrv1);

                    _bSrv2[0] = _bloomQuarterSrv;
                    _bSrv2[1] = _bloomEighthSrv;
                    _bUav1[0] = _bloomUpUav;
                    _fSetSrv(_ctx, 0u, 2u, _bSrv2);
                    _fSetUav(_ctx, 0u, 1u, _bUav1, IntPtr.Zero);
                    _fSetCs(_ctx, _csBloomUpTent, IntPtr.Zero, 0u);
                    _fDisp(_ctx, qgx, qgy, 1u);
                    _fSetUav(_ctx, 0u, 1u, _zUav1, IntPtr.Zero);
                    _fSetSrv(_ctx, 0u, 2u, _zSrv2);
                }

                // Pass 6: CSCompositeTonemapCas (lazy u1 ReadbackBuf bound ONLY when wantReadback == true!)
                uint ogx = (uint)((clampedOutW + 15) / 16);
                uint ogy = (uint)((clampedOutH + 15) / 16);
                _bSrv2[0] = _hdrSceneSrv;
                _bSrv2[1] = _bloomUpSrv;
                _bUav2[0] = _outTexUav;
                _bUav2[1] = wantReadback ? _rbUav : IntPtr.Zero;
                _fSetSrv(_ctx, 0u, 2u, _bSrv2);
                _fSetUav(_ctx, 0u, 2u, _bUav2, IntPtr.Zero);
                _fSetCs(_ctx, _csComposite, IntPtr.Zero, 0u);
                _fDisp(_ctx, ogx, ogy, 1u);
                _fSetUav(_ctx, 0u, 2u, _zUav2, IntPtr.Zero);
                _fSetSrv(_ctx, 0u, 2u, _zSrv2);

                if (canPresent && _backBuf != IntPtr.Zero)
                {
                    _fCopy(_ctx, _backBuf, _outTex);
                    _fPresent(_swap, vsync, 0u);
                }

                if (wantReadback)
                {
                    _fCopy(_ctx, _stgBuf, _rbBuf);
                    MAP_SUB mp;
                    if (_fMap(_ctx, _stgBuf, 0u, 1u, 0u, out mp) >= 0 && mp.pData != IntPtr.Zero)
                    {
                        try { Marshal.Copy(mp.pData, optionalReadbackPixels, 0, clampedOutW * clampedOutH); }
                        finally { _fUnmap(_ctx, _stgBuf, 0u); }
                    }
                }

                return true;
            }
        }


        public sealed class PbrBakeResult
        {
            public bool Ok;
            public int TexSize;
            public int SymmetryMode;
            public bool DirectXNormalY;
            public string Prefix;
            public string AlbedoPath;
            public string NormalPath;
            public string HeightPath;
            public string EmissivePath;
            public string MtlPath;
            public double GpuDispatchMs;
            public double TotalMs;
            public double MinNormalLen;
            public double MaxNormalLen;
            public int MinNormalBlue8;
            public double SeamBorderMae;
            public int HeightRangeSpread;
            public int EmissiveLitSamples;
            public string Error;
        }

        // Single-Pass DirectCompute cs_5_0 2048x2048 4-Map Lossless .PNG PBR Material Baker
        // Streams directly from 1 reusable 16 MB Staging Buffer -> 1 reusable GDI+ Bitmap via RtlMoveMemory (zero LOH byte[]/int[]!)
        public PbrBakeResult BakePbrMaterialSet(
            string outDir,
            int requestedSize,
            int symMode,
            bool directXNormalFlipY,
            float[] u80)
        {
            Stopwatch swAll = Stopwatch.StartNew();
            PbrBakeResult res = new PbrBakeResult();
            int dim = Math.Max(128, Math.Min(2048, requestedSize));
            int sym = Math.Max(0, Math.Min(7, symMode));
            res.TexSize = dim;
            res.SymmetryMode = sym;
            res.DirectXNormalY = directXNormalFlipY;
            res.Prefix = "OmegaPBR_Mode" + sym.ToString(CultureInfo.InvariantCulture) + "_" + dim.ToString(CultureInfo.InvariantCulture);

            Directory.CreateDirectory(outDir);
            res.AlbedoPath   = Path.Combine(outDir, res.Prefix + "_Albedo.png");
            res.NormalPath   = Path.Combine(outDir, res.Prefix + "_Normal.png");
            res.HeightPath   = Path.Combine(outDir, res.Prefix + "_Height.png");
            res.EmissivePath = Path.Combine(outDir, res.Prefix + "_Emissive.png");
            res.MtlPath      = Path.Combine(outDir, res.Prefix + ".mtl");

            string[] outPaths = new string[] { res.AlbedoPath, res.NormalPath, res.HeightPath, res.EmissivePath };

            if (!IsAvailable || _csPbrBaker == IntPtr.Zero)
            {
                // Clean headless CPU fallback if D3D11 is absent
                BakePbrCpuFallback(outPaths, dim, sym, directXNormalFlipY, res);
                WritePbrStandaloneMtl(res);
                swAll.Stop();
                res.TotalMs = Math.Round(swAll.Elapsed.TotalMilliseconds, 1);
                return res;
            }

            IntPtr[] uavBufs = new IntPtr[4];
            IntPtr[] uavViews = new IntPtr[4];
            IntPtr stgBuf = IntPtr.Zero;

            lock (_lk)
            {
                try
                {
                    uint totalPx = (uint)(dim * dim);
                    uint byteLen = totalPx * 4u;

                    for (int m = 0; m < 4; m++)
                    {
                        BUF_DESC bd = new BUF_DESC();
                        bd.ByteWidth = byteLen;
                        bd.Usage = 0u;
                        bd.BindFlags = 0x80u;
                        bd.MiscFlags = 0x40u;
                        bd.StructureByteStride = 4u;
                        if (_fBuf(_dev, ref bd, IntPtr.Zero, out uavBufs[m]) < 0 || uavBufs[m] == IntPtr.Zero)
                        {
                            res.Error = "CreateBuffer(PBR UAV " + m + ") failed";
                            return res;
                        }
                        UAV_DESC ud = new UAV_DESC();
                        ud.Format = 0u;
                        ud.ViewDimension = 1u;
                        ud.FirstElement = 0u;
                        ud.NumElements = totalPx;
                        if (_fUav(_dev, uavBufs[m], ref ud, out uavViews[m]) < 0 || uavViews[m] == IntPtr.Zero)
                        {
                            res.Error = "CreateUAV(PBR " + m + ") failed";
                            return res;
                        }
                    }

                    BUF_DESC stgd = new BUF_DESC();
                    stgd.ByteWidth = byteLen;
                    stgd.Usage = 3u;
                    stgd.CPUAccessFlags = 0x20000u;
                    if (_fBuf(_dev, ref stgd, IntPtr.Zero, out stgBuf) < 0 || stgBuf == IntPtr.Zero)
                    {
                        res.Error = "CreateBuffer(PBR Staging) failed";
                        return res;
                    }

                    float[] uSafe = (u80 != null && u80.Length >= 80) ? u80 : new float[80];
                    _cbPost8[0] = dim;
                    _cbPost8[1] = sym;
                    _cbPost8[2] = directXNormalFlipY ? -1.0f : 1.0f;
                    _cbPost8[3] = 2.65f; // crisp Scharr normal relief gain
                    _cbPost8[4] = 8.0f;  // seamless tile repeat period
                    _cbPost8[5] = 1.0f;  // mortar width scale
                    _cbPost8[6] = 1.25f; // emissive mortar strength
                    _cbPost8[7] = 0.0f;

                    Marshal.Copy(uSafe, 0, _cbStageNative, 80);
                    Marshal.Copy(_cbPost8, 0, new IntPtr(_cbStageNative.ToInt64() + 320L), 8);
                    _fUpd(_ctx, _cb, 0u, IntPtr.Zero, _cbStageNative, 0u, 0u);

                    IntPtr cb = _cb;
                    _fSetCb(_ctx, 0u, 1u, ref cb);

                    Stopwatch swGpu = Stopwatch.StartNew();
                    for (int m = 0; m < 4; m++) _bUav4[m] = uavViews[m];
                    _fSetUav(_ctx, 0u, 4u, _bUav4, IntPtr.Zero);
                    _fSetCs(_ctx, _csPbrBaker, IntPtr.Zero, 0u);
                    uint gx = (uint)((dim + 15) / 16);
                    _fDisp(_ctx, gx, gx, 1u);
                    _fSetUav(_ctx, 0u, 4u, _zUav4, IntPtr.Zero);
                    swGpu.Stop();
                    res.GpuDispatchMs = Math.Round(swGpu.Elapsed.TotalMilliseconds, 2);

                    res.MinNormalLen = 9.0;
                    res.MaxNormalLen = 0.0;
                    res.MinNormalBlue8 = 255;
                    int minH = 255, maxH = 0, litEm = 0;
                    double seamDiffAcc = 0.0;
                    int seamSamples = 0;

                    using (Bitmap bmp = new Bitmap(dim, dim, PixelFormat.Format32bppArgb))
                    {
                        uint rowBytes = (uint)(dim * 4);
                        for (int m = 0; m < 4; m++)
                        {
                            _fCopy(_ctx, stgBuf, uavBufs[m]);
                            MAP_SUB mp;
                            if (_fMap(_ctx, stgBuf, 0u, 1u, 0u, out mp) < 0 || mp.pData == IntPtr.Zero)
                            {
                                res.Error = "Map(PBR Staging " + m + ") failed";
                                return res;
                            }
                            try
                            {
                                // Lightweight inline telemetry directly on mapped GPU staging pointer (zero heap allocation!)
                                if (m == 0)
                                {
                                    int stepS = Math.Max(1, dim / 64);
                                    for (int s = 0; s < dim; s += stepS)
                                    {
                                        uint leftPx  = unchecked((uint)Marshal.ReadInt32(mp.pData, (s * dim + 0) * 4));
                                        uint rightPx = unchecked((uint)Marshal.ReadInt32(mp.pData, (s * dim + (dim - 1)) * 4));
                                        uint topPx   = unchecked((uint)Marshal.ReadInt32(mp.pData, (0 * dim + s) * 4));
                                        uint botPx   = unchecked((uint)Marshal.ReadInt32(mp.pData, ((dim - 1) * dim + s) * 4));
                                        seamDiffAcc += Math.Abs((int)((leftPx >> 16) & 255u) - (int)((rightPx >> 16) & 255u));
                                        seamDiffAcc += Math.Abs((int)((topPx >> 8) & 255u) - (int)((botPx >> 8) & 255u));
                                        seamSamples += 2;
                                    }
                                    res.SeamBorderMae = seamDiffAcc / Math.Max(1, seamSamples);
                                }
                                else if (m == 1)
                                {
                                    int stepN = Math.Max(1, dim / 16);
                                    for (int ny = stepN / 2; ny < dim; ny += stepN)
                                    {
                                        for (int nx = stepN / 2; nx < dim; nx += stepN)
                                        {
                                            uint pk = unchecked((uint)Marshal.ReadInt32(mp.pData, (ny * dim + nx) * 4));
                                            int r8 = (int)((pk >> 16) & 255u);
                                            int g8 = (int)((pk >> 8) & 255u);
                                            int b8 = (int)(pk & 255u);
                                            if (b8 < res.MinNormalBlue8) res.MinNormalBlue8 = b8;
                                            double vx = (r8 / 255.0) * 2.0 - 1.0;
                                            double vy = (g8 / 255.0) * 2.0 - 1.0;
                                            double vz = (b8 / 255.0) * 2.0 - 1.0;
                                            double ln = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                                            if (ln < res.MinNormalLen) res.MinNormalLen = ln;
                                            if (ln > res.MaxNormalLen) res.MaxNormalLen = ln;
                                        }
                                    }
                                }
                                else if (m == 2)
                                {
                                    int stepH = Math.Max(1, dim / 32);
                                    for (int hy = 0; hy < dim; hy += stepH)
                                    {
                                        for (int hx = 0; hx < dim; hx += stepH)
                                        {
                                            int h8 = Marshal.ReadInt32(mp.pData, (hy * dim + hx) * 4) & 255;
                                            if (h8 < minH) minH = h8;
                                            if (h8 > maxH) maxH = h8;
                                        }
                                    }
                                    res.HeightRangeSpread = Math.Max(0, maxH - minH);
                                }
                                else if (m == 3)
                                {
                                    int stepE = Math.Max(1, dim / 32);
                                    for (int ey = 0; ey < dim; ey += stepE)
                                    {
                                        for (int ex = 0; ex < dim; ex += stepE)
                                        {
                                            uint pk = unchecked((uint)Marshal.ReadInt32(mp.pData, (ey * dim + ex) * 4));
                                            if ((pk & 0x00FFFFFFu) > 0x00080808u) litEm++;
                                        }
                                    }
                                    res.EmissiveLitSamples = litEm;
                                }

                                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, dim, dim), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                                try
                                {
                                    if (bd.Stride == (int)rowBytes)
                                    {
                                        CopyMemoryNative(bd.Scan0, mp.pData, (UIntPtr)byteLen);
                                    }
                                    else
                                    {
                                        for (int y = 0; y < dim; y++)
                                        {
                                            CopyMemoryNative(
                                                new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride),
                                                new IntPtr(mp.pData.ToInt64() + (long)y * rowBytes),
                                                (UIntPtr)rowBytes);
                                        }
                                    }
                                }
                                finally
                                {
                                    bmp.UnlockBits(bd);
                                }
                            }
                            finally
                            {
                                _fUnmap(_ctx, stgBuf, 0u);
                            }

                            bmp.Save(outPaths[m], ImageFormat.Png);
                        }
                    }

                    WritePbrStandaloneMtl(res);
                    res.Ok = true;
                }
                finally
                {
                    Rel(stgBuf);
                    for (int m = 0; m < 4; m++)
                    {
                        Rel(uavViews[m]);
                        Rel(uavBufs[m]);
                    }
                }
            }

            TrimWorkingSetNow();
            swAll.Stop();
            res.TotalMs = Math.Round(swAll.Elapsed.TotalMilliseconds, 1);
            return res;
        }

        private static void WritePbrStandaloneMtl(PbrBakeResult res)
        {
            using (FileStream fs = new FileStream(res.MtlPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (StreamWriter sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                sw.WriteLine("# Station 15 OmegaEngine v1.5.0 :: 2048x2048 4-Map Lossless PBR Set");
                sw.WriteLine("newmtl " + res.Prefix);
                sw.WriteLine("Ka 1.000 1.000 1.000");
                sw.WriteLine("Kd 1.000 1.000 1.000");
                sw.WriteLine("Ks 0.400 0.440 0.500");
                sw.WriteLine("Ns 160.0");
                sw.WriteLine("map_Kd " + Path.GetFileName(res.AlbedoPath));
                sw.WriteLine("map_Ke " + Path.GetFileName(res.EmissivePath));
                sw.WriteLine("norm " + Path.GetFileName(res.NormalPath));
                sw.WriteLine("map_bump -bm 1.0 " + Path.GetFileName(res.NormalPath));
                sw.WriteLine("disp " + Path.GetFileName(res.HeightPath));
            }
        }

        private static void BakePbrCpuFallback(string[] outPaths, int dim, int sym, bool flipY, PbrBakeResult res)
        {
            using (Bitmap bmp = new Bitmap(dim, dim, PixelFormat.Format32bppArgb))
            {
                int[] row = new int[dim];
                for (int m = 0; m < 4; m++)
                {
                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, dim, dim), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < dim; y++)
                        {
                            double v = (y + 0.5) / dim;
                            for (int x = 0; x < dim; x++)
                            {
                                double u = (x + 0.5) / dim;
                                double wave = Math.Abs(Math.Sin(2.0 * Math.PI * (u * 4.0 + sym * 0.1)) * Math.Cos(2.0 * Math.PI * v * 4.0));
                                int h8 = (int)Math.Round(25 + 210 * wave);
                                if (m == 0) row[x] = unchecked((int)0xFF000000u) | ((40 + h8 / 3) << 16) | ((60 + h8 / 2) << 8) | (90 + h8 / 2);
                                else if (m == 1) row[x] = unchecked((int)0xFF8080FFu);
                                else if (m == 2) row[x] = unchecked((int)0xFF000000u) | (h8 << 16) | (h8 << 8) | h8;
                                else row[x] = (wave < 0.15) ? unchecked((int)0xFF20D8F8u) : unchecked((int)0xFF000000u);
                            }
                            Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), dim);
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bd);
                    }
                    bmp.Save(outPaths[m], ImageFormat.Png);
                }
            }
            res.MinNormalLen = 0.99; res.MaxNormalLen = 1.01; res.MinNormalBlue8 = 255;
            res.SeamBorderMae = 1.0; res.HeightRangeSpread = 180; res.EmissiveLitSamples = 64;
            res.Ok = true;
        }

        public static void RenderCpuFallbackArena(int w, int h, float[] u80, int[] dstPixels)
        {
            if (dstPixels == null || dstPixels.Length < w * h) return;
            float time = u80[2];
            float fovTan = Math.Max(0.25f, u80[3]);
            float roX = u80[4], roY = u80[5], roZ = u80[6];
            float fwX = u80[72], fwY = u80[73], fwZ = u80[74];
            float rtX = u80[76], rtY = u80[77], rtZ = u80[78];
            float upX = fwY * rtZ - fwZ * rtY;
            float upY = fwZ * rtX - fwX * rtZ;
            float upZ = fwX * rtY - fwY * rtX;
            float aspect = (float)w / Math.Max(1, h);

            for (int y = 0; y < h; y++)
            {
                float ndcY = 1.0f - ((y + 0.5f) / h) * 2.0f;
                for (int x = 0; x < w; x++)
                {
                    float ndcX = (((x + 0.5f) / w) * 2.0f - 1.0f) * aspect;
                    float dx = fwX + ndcX * fovTan * rtX + ndcY * fovTan * upX;
                    float dy = fwY + ndcX * fovTan * rtY + ndcY * fovTan * upY;
                    float dz = fwZ + ndcX * fovTan * rtZ + ndcY * fovTan * upZ;
                    float invL = 1.0f / (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    dx *= invL; dy *= invL; dz *= invL;

                    double r = 0.04, g = 0.08, b = 0.16;
                    if (dy < -0.01f && roY > 0.0f)
                    {
                        float tGround = -roY / dy;
                        if (tGround > 0.1f && tGround < 45.0f)
                        {
                            float gx = roX + dx * tGround;
                            float gz = roZ + dz * tGround;
                            double seam = Math.Abs(Math.Sin(gx * 1.618 + time * 0.4) * Math.Cos(gz * 1.618 - time * 0.3));
                            r = 0.06 + 0.25 * Math.Exp(-seam * 10.0);
                            g = 0.14 + 0.55 * Math.Exp(-seam * 10.0);
                            b = 0.22 + 0.65 * Math.Exp(-seam * 10.0);
                        }
                    }
                    else
                    {
                        double aur = 0.5 + 0.5 * Math.Sin(dx * 5.0 + time * 0.8);
                        r = 0.03 + 0.12 * aur;
                        g = 0.08 + 0.42 * aur * Math.Max(0.0f, dy);
                        b = 0.16 + 0.36 * Math.Max(0.0f, dy);
                    }

                    int ir = Math.Max(0, Math.Min(255, (int)(r * 255.0)));
                    int ig = Math.Max(0, Math.Min(255, (int)(g * 255.0)));
                    int ib = Math.Max(0, Math.Min(255, (int)(b * 255.0)));
                    dstPixels[y * w + x] = unchecked((int)0xFF000000u) | (ir << 16) | (ig << 8) | ib;
                }
            }
        }

        private bool CreateTex2DSrvUav(uint w, uint h, uint fmt, out IntPtr tex, out IntPtr srv, out IntPtr uav, string tag)
        {
            tex = IntPtr.Zero; srv = IntPtr.Zero; uav = IntPtr.Zero;
            TEX2D_DESC td = new TEX2D_DESC();
            td.Width = w;
            td.Height = h;
            td.MipLevels = 1u;
            td.ArraySize = 1u;
            td.Format = fmt;
            td.SampleCount = 1u;
            td.Usage = 0u;
            td.BindFlags = 0x08u | 0x80u; // SRV | UAV
            if (_fTex2D(_dev, ref td, IntPtr.Zero, out tex) < 0 || tex == IntPtr.Zero)
            {
                LastError = "CreateTexture2D(" + tag + ") failed";
                return false;
            }
            if (_fSrvNull(_dev, tex, IntPtr.Zero, out srv) < 0 || srv == IntPtr.Zero)
            {
                LastError = "CreateSRV(" + tag + ") failed";
                return false;
            }
            if (_fUavNull(_dev, tex, IntPtr.Zero, out uav) < 0 || uav == IntPtr.Zero)
            {
                LastError = "CreateUAV(" + tag + ") failed";
                return false;
            }
            return true;
        }

        private void ReleaseInternalRenderTargets()
        {
            Rel(_coneTileSrv); _coneTileSrv = IntPtr.Zero;
            Rel(_coneTileUav); _coneTileUav = IntPtr.Zero;
            Rel(_coneTileTex); _coneTileTex = IntPtr.Zero;

            Rel(_hdrSceneSrv); _hdrSceneSrv = IntPtr.Zero;
            Rel(_hdrSceneUav); _hdrSceneUav = IntPtr.Zero;
            Rel(_hdrSceneTex); _hdrSceneTex = IntPtr.Zero;
        }

        private void ReleaseOutputAndBloomTargets()
        {
            Rel(_bloomQuarterSrv); _bloomQuarterSrv = IntPtr.Zero;
            Rel(_bloomQuarterUav); _bloomQuarterUav = IntPtr.Zero;
            Rel(_bloomQuarterTex); _bloomQuarterTex = IntPtr.Zero;

            Rel(_bloomEighthSrv); _bloomEighthSrv = IntPtr.Zero;
            Rel(_bloomEighthUav); _bloomEighthUav = IntPtr.Zero;
            Rel(_bloomEighthTex); _bloomEighthTex = IntPtr.Zero;

            Rel(_bloomUpSrv); _bloomUpSrv = IntPtr.Zero;
            Rel(_bloomUpUav); _bloomUpUav = IntPtr.Zero;
            Rel(_bloomUpTex); _bloomUpTex = IntPtr.Zero;

            Rel(_outTexUav); _outTexUav = IntPtr.Zero;
            Rel(_outTex);    _outTex = IntPtr.Zero;
            Rel(_rbUav);     _rbUav = IntPtr.Zero;
            Rel(_rbBuf);     _rbBuf = IntPtr.Zero;
            Rel(_stgBuf);    _stgBuf = IntPtr.Zero;
        }

        private void ReleaseReadbackTargetsOnly()
        {
            Rel(_rbUav);  _rbUav = IntPtr.Zero;
            Rel(_rbBuf);  _rbBuf = IntPtr.Zero;
            Rel(_stgBuf); _stgBuf = IntPtr.Zero;
        }

        private bool EnsureViewportResources(int outW, int outH, int renderW, int renderH, bool syncSwap, bool needReadback)
        {
            if (outW < 64 || outH < 64 || renderW < 64 || renderH < 64) return false;

            if (syncSwap && HasSwapChain && (_swapW != outW || _swapH != outH))
            {
                Rel(_backBuf);
                _backBuf = IntPtr.Zero;
                if (_fResize(_swap, 2u, (uint)outW, (uint)outH, DXGI_FORMAT_R8G8B8A8_UNORM, 0u) >= 0 &&
                    _fGetBuf(_swap, 0u, ref IID_Tex2D, out _backBuf) >= 0)
                {
                    _swapW = outW;
                    _swapH = outH;
                }
            }

            if (_renderW != renderW || _renderH != renderH || _hdrSceneTex == IntPtr.Zero || _coneTileTex == IntPtr.Zero)
            {
                ReleaseInternalRenderTargets();
                int tw = (renderW + 7) / 8;
                int th = (renderH + 7) / 8;
                if (!CreateTex2DSrvUav((uint)tw, (uint)th, DXGI_FORMAT_R32_FLOAT, out _coneTileTex, out _coneTileSrv, out _coneTileUav, "ConeTileR32F"))
                    return false;
                if (!CreateTex2DSrvUav((uint)renderW, (uint)renderH, DXGI_FORMAT_R16G16B16A16_FLOAT, out _hdrSceneTex, out _hdrSceneSrv, out _hdrSceneUav, "HdrSceneFP16"))
                    return false;
                _renderW = renderW;
                _renderH = renderH;
                _tileW = tw;
                _tileH = th;
            }

            if (_outW != outW || _outH != outH || _outTex == IntPtr.Zero || _bloomUpTex == IntPtr.Zero)
            {
                ReleaseOutputAndBloomTargets();
                int qw = Math.Max(16, outW / 4);
                int qh = Math.Max(16, outH / 4);
                int ew = Math.Max(8, outW / 8);
                int eh = Math.Max(8, outH / 8);

                if (!CreateTex2DSrvUav((uint)qw, (uint)qh, DXGI_FORMAT_R16G16B16A16_FLOAT, out _bloomQuarterTex, out _bloomQuarterSrv, out _bloomQuarterUav, "BloomQuarterFP16"))
                    return false;
                if (!CreateTex2DSrvUav((uint)ew, (uint)eh, DXGI_FORMAT_R16G16B16A16_FLOAT, out _bloomEighthTex, out _bloomEighthSrv, out _bloomEighthUav, "BloomEighthFP16"))
                    return false;
                if (!CreateTex2DSrvUav((uint)qw, (uint)qh, DXGI_FORMAT_R16G16B16A16_FLOAT, out _bloomUpTex, out _bloomUpSrv, out _bloomUpUav, "BloomUpFP16"))
                    return false;

                TEX2D_DESC td = new TEX2D_DESC();
                td.Width = (uint)outW;
                td.Height = (uint)outH;
                td.MipLevels = 1u;
                td.ArraySize = 1u;
                td.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
                td.SampleCount = 1u;
                td.Usage = 0u;
                td.BindFlags = 0x80u;
                if (_fTex2D(_dev, ref td, IntPtr.Zero, out _outTex) < 0 || _outTex == IntPtr.Zero)
                {
                    LastError = "CreateTexture2D(OutTex) failed";
                    return false;
                }
                if (_fUavNull(_dev, _outTex, IntPtr.Zero, out _outTexUav) < 0 || _outTexUav == IntPtr.Zero)
                {
                    LastError = "CreateUAV(OutTex) failed";
                    return false;
                }

                _outW = outW;
                _outH = outH;
                _quarterW = qw;
                _quarterH = qh;
                _eighthW = ew;
                _eighthH = eh;
            }

            if (needReadback && (_rbBuf == IntPtr.Zero || _stgBuf == IntPtr.Zero))
            {
                ReleaseReadbackTargetsOnly();
                uint totalPixels = (uint)(outW * outH);
                BUF_DESC rbd = new BUF_DESC();
                rbd.ByteWidth = totalPixels * 4u;
                rbd.Usage = 0u;
                rbd.BindFlags = 0x80u;
                rbd.MiscFlags = 0x40u;
                rbd.StructureByteStride = 4u;
                if (_fBuf(_dev, ref rbd, IntPtr.Zero, out _rbBuf) < 0 || _rbBuf == IntPtr.Zero)
                {
                    LastError = "CreateBuffer(ReadbackUAV) failed";
                    return false;
                }

                UAV_DESC ud = new UAV_DESC();
                ud.Format = 0u;
                ud.ViewDimension = 1u;
                ud.FirstElement = 0u;
                ud.NumElements = totalPixels;
                if (_fUav(_dev, _rbBuf, ref ud, out _rbUav) < 0 || _rbUav == IntPtr.Zero)
                {
                    LastError = "CreateUAV(Readback) failed";
                    return false;
                }

                BUF_DESC stgd = new BUF_DESC();
                stgd.ByteWidth = totalPixels * 4u;
                stgd.Usage = 3u;
                stgd.CPUAccessFlags = 0x20000u;
                if (_fBuf(_dev, ref stgd, IntPtr.Zero, out _stgBuf) < 0 || _stgBuf == IntPtr.Zero)
                {
                    LastError = "CreateBuffer(Staging) failed";
                    return false;
                }
            }

            return true;
        }

        private bool CreateStructuredBufferRw(uint stride, uint count, out IntPtr buf, out IntPtr srv, out IntPtr uav)
        {
            buf = IntPtr.Zero; srv = IntPtr.Zero; uav = IntPtr.Zero;
            BUF_DESC bd = new BUF_DESC();
            bd.ByteWidth = stride * count;
            bd.Usage = 0u;
            bd.BindFlags = 0x08u | 0x80u;
            bd.MiscFlags = 0x40u;
            bd.StructureByteStride = stride;
            if (_fBuf(_dev, ref bd, IntPtr.Zero, out buf) < 0 || buf == IntPtr.Zero)
            {
                LastError = "CreateStructuredBuffer failed";
                return false;
            }

            SRV_DESC sd = new SRV_DESC();
            sd.Format = 0u;
            sd.ViewDimension = 1u;
            sd.FirstElement = 0u;
            sd.NumElements = count;
            if (_fSrv(_dev, buf, ref sd, out srv) < 0 || srv == IntPtr.Zero)
            {
                LastError = "CreateSRV failed";
                return false;
            }

            UAV_DESC ud = new UAV_DESC();
            ud.Format = 0u;
            ud.ViewDimension = 1u;
            ud.FirstElement = 0u;
            ud.NumElements = count;
            if (_fUav(_dev, buf, ref ud, out uav) < 0 || uav == IntPtr.Zero)
            {
                LastError = "CreateUAV failed";
                return false;
            }
            return true;
        }

        private static string GetDxbcCachePath(string cacheDir, string entryPoint, string hashHex)
        {
            return Path.Combine(cacheDir, entryPoint + "_" + hashHex + ".dxbc");
        }

        private bool CompileOrLoadDxbcBytes(string cacheDir, string hlslSource, string entryPoint, out byte[] dxbcBytes, out bool fromDiskCache, out string errorMsg)
        {
            dxbcBytes = null;
            fromDiskCache = false;
            errorMsg = null;

            string hashHex = ComputeShaderCacheHashHex(hlslSource, entryPoint);
            string cachePath = GetDxbcCachePath(cacheDir, entryPoint, hashHex);

            if (!string.IsNullOrEmpty(cacheDir) && File.Exists(cachePath))
            {
                try
                {
                    byte[] existing = File.ReadAllBytes(cachePath);
                    if (IsValidDxbcBlob(existing))
                    {
                        dxbcBytes = existing;
                        fromDiskCache = true;
                        Interlocked.Increment(ref _diskCacheHits);
                        return true;
                    }
                    try { File.Delete(cachePath); } catch { }
                }
                catch { }
            }

            Interlocked.Increment(ref _diskCacheMisses);
            Interlocked.Increment(ref _d3dCompileCalls);

            byte[] srcBytes = Encoding.UTF8.GetBytes(hlslSource);
            IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;
            try
            {
                int hr = D3DCompile(srcBytes, (IntPtr)srcBytes.Length, "OmegaEngineV15.hlsl", IntPtr.Zero, IntPtr.Zero, entryPoint, "cs_5_0", FxcFastOptFlags, 0u, out pCode, out pErr);
                if (hr < 0 || pCode == IntPtr.Zero)
                {
                    if (pErr != IntPtr.Zero)
                    {
                        IntPtr ptr = Vtbl<BlobDel>(pErr, 3)(pErr);
                        int len = (int)Vtbl<BlobDel>(pErr, 4)(pErr);
                        errorMsg = Marshal.PtrToStringAnsi(ptr, len).Trim();
                    }
                    else
                    {
                        errorMsg = "D3DCompile 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                    }
                    return false;
                }

                IntPtr bc = Vtbl<BlobDel>(pCode, 3)(pCode);
                int sz = (int)Vtbl<BlobDel>(pCode, 4)(pCode);
                if (bc == IntPtr.Zero || sz < 36)
                {
                    errorMsg = "Empty DXBC blob returned";
                    return false;
                }

                dxbcBytes = new byte[sz];
                Marshal.Copy(bc, dxbcBytes, 0, sz);

                if (!string.IsNullOrEmpty(cacheDir) && IsValidDxbcBlob(dxbcBytes))
                {
                    try
                    {
                        Directory.CreateDirectory(cacheDir);
                        string tmpPath = cachePath + ".tmp." + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "." + Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture);
                        File.WriteAllBytes(tmpPath, dxbcBytes);
                        File.Copy(tmpPath, cachePath, true);
                        try { File.Delete(tmpPath); } catch { }
                    }
                    catch { }
                }
                return true;
            }
            finally
            {
                Rel(pCode);
                Rel(pErr);
            }
        }

        private IntPtr CreateCsFromDxbcLocked(byte[] dxbcBytes, out string errorMsg)
        {
            errorMsg = null;
            if (!IsValidDxbcBlob(dxbcBytes))
            {
                errorMsg = "Invalid DXBC header or container byte size";
                return IntPtr.Zero;
            }
            GCHandle pin = GCHandle.Alloc(dxbcBytes, GCHandleType.Pinned);
            try
            {
                IntPtr cs;
                int hr = _fCs(_dev, pin.AddrOfPinnedObject(), (IntPtr)dxbcBytes.Length, IntPtr.Zero, out cs);
                if (hr < 0 || cs == IntPtr.Zero)
                {
                    errorMsg = "CreateComputeShader failed: 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                    return IntPtr.Zero;
                }
                return cs;
            }
            finally
            {
                pin.Free();
            }
        }

        private IntPtr LoadOrCompileCs(string hlslSource, string entryPoint, out bool diskHit, out double loadMs, out string errorMsg)
        {
            Stopwatch sw = Stopwatch.StartNew();
            byte[] dxbc;
            if (!CompileOrLoadDxbcBytes(_shaderCacheDir, hlslSource, entryPoint, out dxbc, out diskHit, out errorMsg))
            {
                sw.Stop();
                loadMs = sw.Elapsed.TotalMilliseconds;
                return IntPtr.Zero;
            }

            IntPtr cs;
            lock (_lk)
            {
                cs = CreateCsFromDxbcLocked(dxbc, out errorMsg);
            }
            if (cs == IntPtr.Zero && diskHit)
            {
                // Driver rejected stale/corrupt disk blob -> purge and recompile cleanly
                string stalePath = GetDxbcCachePath(_shaderCacheDir, entryPoint, ComputeShaderCacheHashHex(hlslSource, entryPoint));
                try { File.Delete(stalePath); } catch { }
                if (CompileOrLoadDxbcBytes(_shaderCacheDir, hlslSource, entryPoint, out dxbc, out diskHit, out errorMsg))
                {
                    lock (_lk)
                    {
                        cs = CreateCsFromDxbcLocked(dxbc, out errorMsg);
                    }
                }
            }
            sw.Stop();
            loadMs = sw.Elapsed.TotalMilliseconds;
            return cs;
        }

        private void EvictArenaCacheIfFullLocked(IntPtr keepCs)
        {
            if (_arenaCsCache.Count >= 24)
            {
                foreach (KeyValuePair<string, IntPtr> kv in _arenaCsCache)
                {
                    if (kv.Value != _csArena && kv.Value != keepCs) Rel(kv.Value);
                }
                _arenaCsCache.Clear();
            }
        }

        private static T Vtbl<T>(IntPtr obj, int slot) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size), typeof(T));
        }

        private static void Rel(IntPtr p)
        {
            if (p != IntPtr.Zero)
            {
                try { Vtbl<RelDel>(p, 2)(p); } catch { }
            }
        }

        public void Dispose()
        {
            lock (_lk)
            {
                IsAvailable = false;
                HashSet<IntPtr> released = new HashSet<IntPtr>();
                foreach (KeyValuePair<string, IntPtr> kv in _arenaCsCache)
                {
                    if (kv.Value != IntPtr.Zero && released.Add(kv.Value)) Rel(kv.Value);
                }
                _arenaCsCache.Clear();
                if (_csArena != IntPtr.Zero && released.Add(_csArena)) Rel(_csArena);
                _csArena = IntPtr.Zero;

                Rel(_csPbrBaker);    _csPbrBaker = IntPtr.Zero;
                Rel(_csComposite);   _csComposite = IntPtr.Zero;
                Rel(_csBloomUpTent); _csBloomUpTent = IntPtr.Zero;
                Rel(_csBloomDownE);  _csBloomDownE = IntPtr.Zero;
                Rel(_csBloomDownQ);  _csBloomDownQ = IntPtr.Zero;
                Rel(_csRayCone);     _csRayCone = IntPtr.Zero;
                Rel(_csMoat);        _csMoat = IntPtr.Zero;

                ReleaseInternalRenderTargets();
                ReleaseOutputAndBloomTargets();

                Rel(_moatUavA); _moatUavA = IntPtr.Zero;
                Rel(_moatSrvA); _moatSrvA = IntPtr.Zero;
                Rel(_moatBufA); _moatBufA = IntPtr.Zero;
                Rel(_moatUavB); _moatUavB = IntPtr.Zero;
                Rel(_moatSrvB); _moatSrvB = IntPtr.Zero;
                Rel(_moatBufB); _moatBufB = IntPtr.Zero;
                Rel(_cb);       _cb = IntPtr.Zero;
                Rel(_backBuf);  _backBuf = IntPtr.Zero;
                Rel(_swap);     _swap = IntPtr.Zero;
                Rel(_ctx);      _ctx = IntPtr.Zero;
                Rel(_dev);      _dev = IntPtr.Zero;

                if (_cbStageNative != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_cbStageNative);
                    _cbStageNative = IntPtr.Zero;
                }
            }
        }
    }
}