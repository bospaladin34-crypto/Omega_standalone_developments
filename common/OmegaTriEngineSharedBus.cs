using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmegaCommon
{
    // Phase 2 Complete Multi-Producer 384-Byte Shared-Memory Mesh ("Local\OmegaTriEngineUniforms")
    // Offset   0..319 : float U[80] (20 x float4 registers U0..U19, 16-byte & 64-byte aligned)
    // Offset 320..383 : 64-Byte Lock-Free Seqlock + Multi-Producer Heartbeat Trailer (100% ABI compatible with OmegaTriEngine.addon64):
    //   +0  (320) uint32 MagicOtri       = 0x4F545249 ("OTRI")
    //   +4  (324) uint32 ContractVersion = 0x00010201 (v1.2.1 backward-compatible)
    //   +8  (328) int64  Sequence        (odd = writer active, even = committed snapshot)
    //   +16 (336) uint32 ActiveMask      (live stations + Dual-Pixel cluster bits)
    //   +20 (340) uint32 PayloadCrc32    (IEEE 802.3 CRC-32 over bytes 0..319)
    //   +24 (344) uint32 EdgeClusterBits (MaskPixel10Live | MaskPixel8Live reported by OmegaTensor)
    //   +28 (348) uint32 TensorTick32    (Station 13 :19791 OmegaTensor heartbeat ms | 1u)
    //   +32 (352) uint32 EngineTick32    (Station 15 :19795 OmegaEngine heartbeat ms | 1u)
    //   +36 (356) uint32 SynthTick32     (Station 04 :19782 OmegaSynth heartbeat ms | 1u)
    //   +40 (360) uint32 PenroseTick32   (Station 07 :19785 OmegaPenrose heartbeat ms | 1u)
    //   +44 (364) uint32 AvTick32        (Station 03 :19779 OmegaAV heartbeat ms | 1u)
    //   +48 (368) uint32 AddonTick32     (ReShade .addon64 heartbeat ms | 1u)
    //   +52 (372) uint32 AddonFrameCount (ReShade .addon64 injected frames)
    //   +56 (376) uint32 AddonApiVersion (Negotiated ReShade API version 8..24)
    //   +60 (380) uint32 Skse64Tick32    (Phase 3 SKSE64 CommonLibSSE-NG slot)
    public sealed class OmegaTriEngineSharedBus : IDisposable
    {
        public const string DefaultMapName = "Local\\OmegaTriEngineUniforms";
        public const int FloatCount = 80;
        public const int UniformBytes = 320;
        public const int TrailerOffset = 320;
        public const int LegacyMapBytes = 384;
        public const int ExtOffset = 384;
        public const int ExtFloatCount = 32;
        public const int ExtBytes = 128;
        public const int FullFloatCount = 112;
        public const int TotalMapBytes = 512;
        public const uint HeartbeatTtlMs = 1500u;
        public const uint TensorHeartbeatTtlMs = 4500u;

        public const uint MagicOtri = 0x4F545249u;
        public const uint LegacyContractVersion = 0x00010201u;
        public const uint ContractVersion = 0x00010900u;
        public const float V2SentinelFloat = 180.0f;

        public const uint MaskOmegaEngine  = 1u << 0;
        public const uint MaskOmegaSynth   = 1u << 1;
        public const uint MaskOmegaPenrose = 1u << 2;
        public const uint MaskOmegaAv      = 1u << 3;
        public const uint MaskReshadeAddon = 1u << 4;
        public const uint MaskSkse64Bridge = 1u << 5;
        public const uint MaskOmegaTensor  = 1u << 6;
        public const uint MaskPixel10Live  = 1u << 7;
        public const uint MaskPixel8Live   = 1u << 8;

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint PAGE_READWRITE = 0x04u;
        private const uint FILE_MAP_WRITE = 0x0002u;
        private const uint FILE_MAP_READ  = 0x0004u;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileMappingW(
            IntPtr hFile, IntPtr lpAttributes, uint flProtect,
            uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string lpName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenFileMappingW(
            uint dwDesiredAccess, int bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(
            IntPtr hFileMappingObject, uint dwDesiredAccess,
            uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private readonly string _mapName;
        private readonly object _localSync = new object();
        private readonly byte[] _scratchBytes = new byte[UniformBytes];
        private readonly float[] _mergeFloats = new float[FloatCount];
        private Mutex _writerMutex;
        private IntPtr _hMap = IntPtr.Zero;
        private IntPtr _pView = IntPtr.Zero;
        private ulong _lastSeq = 0UL;
        private uint _lastCrc = 0u;
        private uint _lastAddonApi = 0u;

        public bool IsMapped { get { return _pView != IntPtr.Zero; } }
        public string MapName { get { return _mapName; } }
        public ulong LastSequence { get { return _lastSeq; } }
        public uint LastCrc32 { get { return _lastCrc; } }
        public uint LastAddonApiVersion { get { return _lastAddonApi; } }

        public OmegaTriEngineSharedBus(string mapName)
        {
            _mapName = string.IsNullOrEmpty(mapName) ? DefaultMapName : mapName;
            TryOpenOrCreate();
        }

        private static uint[] BuildCrc32Table()
        {
            uint[] tbl = new uint[256];
            for (uint i = 0u; i < 256u; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = ((c & 1u) != 0u) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                }
                tbl[i] = c;
            }
            return tbl;
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc = Crc32Table[(crc ^ data[i]) & 0xFFu] ^ (crc >> 8);
            }
            return ~crc;
        }

        public static uint NowStamp32()
        {
            return ((uint)GetTickCount64()) | 1u;
        }

        public static bool IsStampAlive(uint now32, uint stamp32)
        {
            return IsStampAliveWithTtl(now32, stamp32, HeartbeatTtlMs);
        }

        public static bool IsStampAliveWithTtl(uint now32, uint stamp32, uint ttlMs)
        {
            if (stamp32 == 0u) return false;
            uint age = unchecked(now32 - stamp32);
            return age <= ttlMs;
        }

        private static string BuildMutexName(string mapName)
        {
            string clean = mapName.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
            return "Local\\OmegaTriMutex_" + clean;
        }

        public bool TryOpenOrCreate()
        {
            if (_pView != IntPtr.Zero) return true;
            lock (_localSync)
            {
                if (_pView != IntPtr.Zero) return true;
                try
                {
                    if (_writerMutex == null)
                    {
                        _writerMutex = new Mutex(false, BuildMutexName(_mapName));
                    }
                    _hMap = CreateFileMappingW(INVALID_HANDLE_VALUE, IntPtr.Zero, PAGE_READWRITE, 0u, (uint)TotalMapBytes, _mapName);
                    if (_hMap == IntPtr.Zero) return false;
                    _pView = MapViewOfFile(_hMap, FILE_MAP_READ | FILE_MAP_WRITE, 0u, 0u, UIntPtr.Zero);
                    if (_pView == IntPtr.Zero)
                    {
                        CloseHandle(_hMap);
                        _hMap = IntPtr.Zero;
                        return false;
                    }

                    bool acquired = AcquireWriterMutex(50);
                    try
                    {
                        uint curMagic = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 0));
                        if (curMagic != MagicOtri)
                        {
                            byte[] zeros = new byte[UniformBytes];
                            uint zeroCrc = ComputeCrc32(zeros, 0, UniformBytes);
                            Marshal.Copy(zeros, 0, _pView, UniformBytes);
                            InitExtendedV2Defaults(_pView);
                            Marshal.WriteInt32(_pView, TrailerOffset + 0, unchecked((int)MagicOtri));
                            Marshal.WriteInt32(_pView, TrailerOffset + 4, unchecked((int)ContractVersion));
                            Marshal.WriteInt32(_pView, TrailerOffset + 16, 0);
                            Marshal.WriteInt32(_pView, TrailerOffset + 20, unchecked((int)zeroCrc));
                            for (int off = 24; off < 64; off += 4)
                            {
                                Marshal.WriteInt32(_pView, TrailerOffset + off, 0);
                            }
                            Thread.MemoryBarrier();
                            Marshal.WriteInt64(_pView, TrailerOffset + 8, 2L);
                            Thread.MemoryBarrier();
                        }
                    }
                    finally
                    {
                        if (acquired) ReleaseWriterMutex();
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private bool AcquireWriterMutex(int timeoutMs)
        {
            if (_writerMutex == null) return false;
            try
            {
                return _writerMutex.WaitOne(timeoutMs, false);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ReleaseWriterMutex()
        {
            if (_writerMutex == null) return;
            try { _writerMutex.ReleaseMutex(); } catch { }
        }

        private uint ComputeLiveMaskLocked(uint now32, out uint addonFramesOut, out uint addonApiOut)
        {
            uint edgeBits = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 24)) & (MaskPixel10Live | MaskPixel8Live);
            uint tTn = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 28));
            uint tEn = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 32));
            uint tSy = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 36));
            uint tPn = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 40));
            uint tAv = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 44));
            uint tRs = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 48));
            addonFramesOut = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 52));
            addonApiOut    = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 56));
            _lastAddonApi  = addonApiOut;
            uint tSk = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 60));

            uint mask = 0u;
            if (IsStampAlive(now32, tEn)) mask |= MaskOmegaEngine;
            if (IsStampAlive(now32, tSy)) mask |= MaskOmegaSynth;
            if (IsStampAlive(now32, tPn)) mask |= MaskOmegaPenrose;
            if (IsStampAlive(now32, tAv)) mask |= MaskOmegaAv;
            if (IsStampAlive(now32, tRs)) mask |= MaskReshadeAddon;
            if (IsStampAlive(now32, tSk)) mask |= MaskSkse64Bridge;
            if (IsStampAliveWithTtl(now32, tTn, TensorHeartbeatTtlMs))
            {
                mask |= MaskOmegaTensor;
                mask |= edgeBits;
            }
            return mask;
        }

        private void CommitMergedUnlocked(uint activeMask, out ulong committedSeq, out uint crc32)
        {
            Buffer.BlockCopy(_mergeFloats, 0, _scratchBytes, 0, UniformBytes);
            crc32 = ComputeCrc32(_scratchBytes, 0, UniformBytes);
            _lastCrc = crc32;

            long baseSeq = Marshal.ReadInt64(_pView, TrailerOffset + 8);
            if ((baseSeq & 1L) != 0L) baseSeq++;
            long oddSeq = baseSeq + 1L;
            long evenSeq = baseSeq + 2L;

            Marshal.WriteInt64(_pView, TrailerOffset + 8, oddSeq);
            Thread.MemoryBarrier();

            Marshal.Copy(_scratchBytes, 0, _pView, UniformBytes);
            EnsureExtendedV2LiveSummary(_pView, activeMask);
            Marshal.WriteInt32(_pView, TrailerOffset + 0, unchecked((int)MagicOtri));
            Marshal.WriteInt32(_pView, TrailerOffset + 4, unchecked((int)ContractVersion));
            Marshal.WriteInt32(_pView, TrailerOffset + 16, unchecked((int)activeMask));
            Marshal.WriteInt32(_pView, TrailerOffset + 20, unchecked((int)crc32));
            Thread.MemoryBarrier();

            Marshal.WriteInt64(_pView, TrailerOffset + 8, evenSeq);
            Thread.MemoryBarrier();

            committedSeq = (ulong)evenSeq;
            _lastSeq = committedSeq;
        }

        // Station 15 OmegaEngine (:19795) primary 144 FPS publisher:
        // Writes camera/world/shockwave/monolith registers while preserving live Synth, Penrose, AV, and OmegaTensor/Dual-Pixel slices!
        public bool PublishEngineFrameAndMerge(
            float[] engineU80,
            uint explicitExtraMask,
            out ulong committedSeq,
            out uint crc32,
            out uint liveMask,
            out uint addonFrames)
        {
            committedSeq = 0UL;
            crc32 = 0u;
            liveMask = 0u;
            addonFrames = 0u;
            if (engineU80 == null || engineU80.Length < FloatCount) return false;
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;

            lock (_localSync)
            {
                if (!AcquireWriterMutex(8)) return false;
                try
                {
                    uint now32 = NowStamp32();
                    Marshal.WriteInt32(_pView, TrailerOffset + 32, unchecked((int)now32));

                    Marshal.Copy(_pView, _scratchBytes, 0, UniformBytes);
                    Buffer.BlockCopy(_scratchBytes, 0, _mergeFloats, 0, UniformBytes);

                    uint addonApi;
                    liveMask = ComputeLiveMaskLocked(now32, out addonFrames, out addonApi) | MaskOmegaEngine | explicitExtraMask;

                    bool syShm = (liveMask & MaskOmegaSynth) != 0u && IsStampAlive(now32, unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 36)));
                    bool pnShm = (liveMask & MaskOmegaPenrose) != 0u && IsStampAlive(now32, unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 40)));
                    bool avShm = (liveMask & MaskOmegaAv) != 0u && IsStampAlive(now32, unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 44)));
                    bool tnShm = (liveMask & MaskOmegaTensor) != 0u && IsStampAliveWithTtl(now32, unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 28)), TensorHeartbeatTtlMs);
                    bool skShm = (liveMask & MaskSkse64Bridge) != 0u && IsStampAlive(now32, unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 60)));

                    if (syShm)
                    {
                        for (int k = 12; k < 24; k++) engineU80[k] = _mergeFloats[k];
                        engineU80[44] = _mergeFloats[44];
                        engineU80[45] = _mergeFloats[45];
                        engineU80[46] = _mergeFloats[46];
                        engineU80[50] = _mergeFloats[50];
                        engineU80[51] = _mergeFloats[51];
                    }
                    if (pnShm)
                    {
                        for (int k = 28; k < 35; k++) engineU80[k] = _mergeFloats[k];
                        if (!tnShm) engineU80[35] = _mergeFloats[35];
                    }
                    if (avShm)
                    {
                        engineU80[48] = _mergeFloats[48];
                    }
                    if (tnShm)
                    {
                        engineU80[35] = _mergeFloats[35]; // Cluster TOPS/s in U[8].w
                        if (!syShm)
                        {
                            engineU80[50] = _mergeFloats[50]; // Tensor E8 SQNR dB
                            engineU80[51] = _mergeFloats[51]; // Tensor active E8 root
                        }
                    }
                    if (skShm)
                    {
                        // Preserve Skyrim AE SKSE64 world-space Thu'um impulses, Weather Aurora, Sheaf H^1=0 guard & camera basis (U[13]..U[19])
                        for (int k = 52; k < 60; k++) engineU80[k] = _mergeFloats[k];
                        engineU80[67] = _mergeFloats[67];
                        for (int k = 68; k < 75; k++) engineU80[k] = _mergeFloats[k];
                        for (int k = 76; k < 79; k++) engineU80[k] = _mergeFloats[k];
                        if (_mergeFloats[67] >= 1.5f) engineU80[36] = 3.0f;
                        else if (_mergeFloats[67] < -0.5f && engineU80[36] == 3.0f) engineU80[36] = 0.0f;
                    }

                    engineU80[47] = (float)liveMask;
                    Array.Copy(engineU80, 0, _mergeFloats, 0, FloatCount);
                    CommitMergedUnlocked(liveMask, out committedSeq, out crc32);
                    return true;
                }
                finally
                {
                    ReleaseWriterMutex();
                }
            }
        }

        // Station 13 OmegaTensor (:19791) + Dual-Pixel (Pixel 10 + Pixel 8) 7-Tier Cluster slice publisher
        public bool PublishTensorSlice(
            float simSec,
            float clusterTops,
            float e8SqnrDb,
            int activeE8Root,
            bool pixel10Live,
            bool pixel8Live)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            lock (_localSync)
            {
                if (!AcquireWriterMutex(6)) return false;
                try
                {
                    uint now32 = NowStamp32();
                    uint edgeBits = (pixel10Live ? MaskPixel10Live : 0u) | (pixel8Live ? MaskPixel8Live : 0u);
                    Marshal.WriteInt32(_pView, TrailerOffset + 24, unchecked((int)edgeBits));
                    Marshal.WriteInt32(_pView, TrailerOffset + 28, unchecked((int)now32));

                    Marshal.Copy(_pView, _scratchBytes, 0, UniformBytes);
                    Buffer.BlockCopy(_scratchBytes, 0, _mergeFloats, 0, UniformBytes);

                    uint addonFrames, addonApi;
                    uint liveMask = ComputeLiveMaskLocked(now32, out addonFrames, out addonApi) | MaskOmegaTensor | edgeBits;

                    if ((liveMask & MaskOmegaEngine) == 0u)
                    {
                        if (_mergeFloats[0] <= 1.0f) { _mergeFloats[0] = 1920.0f; _mergeFloats[1] = 1080.0f; }
                        if (simSec > 0.0f) _mergeFloats[2] = simSec;
                    }
                    _mergeFloats[35] = clusterTops; // U[8].w = live 7-engine cluster TOPS/s
                    if ((liveMask & MaskOmegaSynth) == 0u)
                    {
                        if (e8SqnrDb > 1.0f) _mergeFloats[50] = e8SqnrDb;
                        _mergeFloats[51] = (float)(activeE8Root & 255);
                    }
                    _mergeFloats[47] = (float)liveMask;

                    ulong seq; uint crc;
                    CommitMergedUnlocked(liveMask, out seq, out crc);
                    return true;
                }
                finally
                {
                    ReleaseWriterMutex();
                }
            }
        }

        // Station 04 OmegaSynth (:19782) slice publisher: owns U[3..5], U[11].xyz, U[12].zw
        public bool PublishSynthSlice(
            float simSec,
            float bass, float mid, float high, float peak,
            float[] bands8,
            int chladniM, int chladniN, float arpBpm,
            float sqnrDb, int leadE8Root)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            lock (_localSync)
            {
                if (!AcquireWriterMutex(4)) return false;
                try
                {
                    uint now32 = NowStamp32();
                    Marshal.WriteInt32(_pView, TrailerOffset + 36, unchecked((int)now32));

                    Marshal.Copy(_pView, _scratchBytes, 0, UniformBytes);
                    Buffer.BlockCopy(_scratchBytes, 0, _mergeFloats, 0, UniformBytes);

                    uint addonFrames, addonApi;
                    uint liveMask = ComputeLiveMaskLocked(now32, out addonFrames, out addonApi) | MaskOmegaSynth;

                    if ((liveMask & MaskOmegaEngine) == 0u)
                    {
                        if (_mergeFloats[0] <= 1.0f) { _mergeFloats[0] = 1920.0f; _mergeFloats[1] = 1080.0f; }
                        _mergeFloats[2] = simSec;
                    }
                    _mergeFloats[12] = bass;
                    _mergeFloats[13] = mid;
                    _mergeFloats[14] = high;
                    _mergeFloats[15] = peak;
                    if (bands8 != null)
                    {
                        int n = Math.Min(8, bands8.Length);
                        for (int k = 0; k < n; k++) _mergeFloats[16 + k] = bands8[k];
                    }
                    _mergeFloats[44] = (float)chladniM;
                    _mergeFloats[45] = (float)chladniN;
                    _mergeFloats[46] = arpBpm;
                    _mergeFloats[47] = (float)liveMask;
                    _mergeFloats[50] = sqnrDb;
                    _mergeFloats[51] = (float)leadE8Root;

                    ulong seq; uint crc;
                    CommitMergedUnlocked(liveMask, out seq, out crc);
                    return true;
                }
                finally
                {
                    ReleaseWriterMutex();
                }
            }
        }

        // Station 07 OmegaPenrose (:19785) slice publisher: owns U[7..8] (5D gamma_0..4 + phason_xyz)
        public bool PublishPenroseSlice(
            float simSec,
            double[] gamma5,
            float phasonX, float phasonY, float phasonZ,
            int mappedSymMode, float tileScale)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            lock (_localSync)
            {
                if (!AcquireWriterMutex(4)) return false;
                try
                {
                    uint now32 = NowStamp32();
                    Marshal.WriteInt32(_pView, TrailerOffset + 40, unchecked((int)now32));

                    Marshal.Copy(_pView, _scratchBytes, 0, UniformBytes);
                    Buffer.BlockCopy(_scratchBytes, 0, _mergeFloats, 0, UniformBytes);

                    uint addonFrames, addonApi;
                    uint liveMask = ComputeLiveMaskLocked(now32, out addonFrames, out addonApi) | MaskOmegaPenrose;

                    if (gamma5 != null && gamma5.Length >= 5)
                    {
                        double g0 = gamma5[0], g1 = gamma5[1], g2 = gamma5[2], g3 = gamma5[3];
                        double g4 = -(g0 + g1 + g2 + g3);
                        _mergeFloats[28] = (float)g0;
                        _mergeFloats[29] = (float)g1;
                        _mergeFloats[30] = (float)g2;
                        _mergeFloats[31] = (float)g3;
                        _mergeFloats[32] = (float)g4;
                    }
                    _mergeFloats[33] = phasonX;
                    _mergeFloats[34] = phasonY;
                    if ((liveMask & MaskOmegaTensor) == 0u)
                    {
                        _mergeFloats[35] = phasonZ;
                    }
                    _mergeFloats[47] = (float)liveMask;

                    if ((liveMask & MaskOmegaEngine) == 0u)
                    {
                        if (_mergeFloats[0] <= 1.0f) { _mergeFloats[0] = 1920.0f; _mergeFloats[1] = 1080.0f; }
                        _mergeFloats[2] = simSec;
                        _mergeFloats[36] = (float)Math.Max(0, Math.Min(3, mappedSymMode));
                        _mergeFloats[37] = Math.Max(0.25f, Math.Min(4.0f, tileScale));
                    }

                    ulong seq; uint crc;
                    CommitMergedUnlocked(liveMask, out seq, out crc);
                    return true;
                }
                finally
                {
                    ReleaseWriterMutex();
                }
            }
        }

        // Station 03 OmegaAV (:19779) slice publisher: owns U[12].x (fluid energy) + fallback WASAPI bands
        public bool PublishAvSlice(
            float simSec,
            float fluidEnergy,
            float shimmerBoost,
            double[] fallbackBands8,
            float fallbackPeak,
            float sqnrDb,
            int nearestE8Root)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            lock (_localSync)
            {
                if (!AcquireWriterMutex(4)) return false;
                try
                {
                    uint now32 = NowStamp32();
                    Marshal.WriteInt32(_pView, TrailerOffset + 44, unchecked((int)now32));

                    Marshal.Copy(_pView, _scratchBytes, 0, UniformBytes);
                    Buffer.BlockCopy(_scratchBytes, 0, _mergeFloats, 0, UniformBytes);

                    uint addonFrames, addonApi;
                    uint liveMask = ComputeLiveMaskLocked(now32, out addonFrames, out addonApi) | MaskOmegaAv;

                    _mergeFloats[48] = fluidEnergy;
                    _mergeFloats[47] = (float)liveMask;

                    if ((liveMask & MaskOmegaEngine) == 0u)
                    {
                        if (_mergeFloats[0] <= 1.0f) { _mergeFloats[0] = 1920.0f; _mergeFloats[1] = 1080.0f; }
                        _mergeFloats[2] = simSec;
                        _mergeFloats[75] = shimmerBoost;
                    }
                    if ((liveMask & MaskOmegaSynth) == 0u && fallbackBands8 != null && fallbackBands8.Length >= 8)
                    {
                        float b = (float)((fallbackBands8[0] + fallbackBands8[1]) * 0.5);
                        float m = (float)((fallbackBands8[2] + fallbackBands8[3] + fallbackBands8[4]) / 3.0);
                        float h = (float)((fallbackBands8[5] + fallbackBands8[6] + fallbackBands8[7]) / 3.0);
                        _mergeFloats[12] = b;
                        _mergeFloats[13] = m;
                        _mergeFloats[14] = h;
                        _mergeFloats[15] = fallbackPeak;
                        for (int k = 0; k < 8; k++) _mergeFloats[16 + k] = (float)fallbackBands8[k];
                        if ((liveMask & MaskOmegaTensor) == 0u)
                        {
                            _mergeFloats[50] = sqnrDb;
                            _mergeFloats[51] = (float)nearestE8Root;
                        }
                    }

                    ulong seq; uint crc;
                    CommitMergedUnlocked(liveMask, out seq, out crc);
                    return true;
                }
                finally
                {
                    ReleaseWriterMutex();
                }
            }
        }

        public static bool TryReadSnapshotEx(
            string mapName,
            float[] dst80,
            out ulong seqOut,
            out uint activeMaskOut,
            out uint crcOut,
            out uint addonFramesOut,
            out uint addonApiOut)
        {
            seqOut = 0UL;
            activeMaskOut = 0u;
            crcOut = 0u;
            addonFramesOut = 0u;
            addonApiOut = 0u;
            if (dst80 == null || dst80.Length < FloatCount) return false;

            IntPtr h = IntPtr.Zero;
            IntPtr p = IntPtr.Zero;
            try
            {
                h = OpenFileMappingW(FILE_MAP_READ, 0, string.IsNullOrEmpty(mapName) ? DefaultMapName : mapName);
                if (h == IntPtr.Zero) return false;
                p = MapViewOfFile(h, FILE_MAP_READ, 0u, 0u, UIntPtr.Zero);
                if (p == IntPtr.Zero) return false;

                byte[] raw = new byte[UniformBytes];
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    long s1 = Marshal.ReadInt64(p, TrailerOffset + 8);
                    if ((s1 & 1L) != 0L || s1 == 0L)
                    {
                        Thread.SpinWait(16);
                        continue;
                    }
                    Thread.MemoryBarrier();
                    uint magic  = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 0));
                    uint mask   = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 16));
                    uint crc    = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 20));
                    uint tRs    = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 48));
                    uint aFrm   = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 52));
                    uint aApi   = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 56));
                    uint tSk    = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 60));
                    uint now32  = NowStamp32();
                    if (IsStampAlive(now32, tRs)) mask |= MaskReshadeAddon;
                    if (IsStampAlive(now32, tSk)) mask |= MaskSkse64Bridge;
                    Marshal.Copy(p, raw, 0, UniformBytes);
                    Thread.MemoryBarrier();
                    long s2 = Marshal.ReadInt64(p, TrailerOffset + 8);
                    if (s1 == s2 && magic == MagicOtri)
                    {
                        uint calcCrc = ComputeCrc32(raw, 0, UniformBytes);
                        if (calcCrc == crc)
                        {
                            Buffer.BlockCopy(raw, 0, dst80, 0, UniformBytes);
                            seqOut = (ulong)s1;
                            activeMaskOut = mask;
                            crcOut = crc;
                            addonFramesOut = aFrm;
                            addonApiOut = aApi;
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (p != IntPtr.Zero) { try { UnmapViewOfFile(p); } catch { } }
                if (h != IntPtr.Zero) { try { CloseHandle(h); } catch { } }
            }
        }


        [StructLayout(LayoutKind.Explicit)]
        private struct IntFloatBits
        {
            [FieldOffset(0)] public int I;
            [FieldOffset(0)] public float F;
        }

        public struct TopBarLiveSnapshot
        {
            public bool Mapped;
            public bool V2Active;
            public ulong Sequence;
            public uint ActiveMask;
            public uint AddonFrames;
            public uint AddonApiVersion;
            public bool EngineLive;
            public bool ReshadeLive;
            public bool Skse64Live;
            public bool InShaderHudEnabled;
            public bool ShaderMasterEnabled;
            public bool AutoMenuFadeEnabled;
            public bool DesktopClickThrough;
            public float SkyrimFps;
            public float FrameTimeMs;
            public float MenuFadeAlpha;
            public bool MenuDetected;
            public int ActiveSymmetry;
            public int SymmetryOverride;
            public float AuroraMultiplier;
            public float CausticMultiplier;
            public float ThuumPulseStamp;
            public float CamX;
            public float CamY;
            public float CamZ;
            public float WeatherAuroraGain;
            public float SheafH1State;
            public int AestheticPersona;
            public float PersonaContrast;
            public float PersonaVignette;
            public float HdrBloomMultiplier;
            public float WaterElevationMeters;
            public float WaterRippleGain;
            public int SpellSchoolId;
            public float SpellIntensity;
        }

        private static int CountBits32(uint v)
        {
            int c = 0;
            while (v != 0u)
            {
                v &= (v - 1u);
                c++;
            }
            return c;
        }

        private static float ReadFloatAt(IntPtr basePtr, int byteOffset)
        {
            IntFloatBits u = new IntFloatBits();
            u.I = Marshal.ReadInt32(basePtr, byteOffset);
            return u.F;
        }

        private static void WriteFloatAt(IntPtr basePtr, int byteOffset, float value)
        {
            IntFloatBits u = new IntFloatBits();
            u.F = value;
            Marshal.WriteInt32(basePtr, byteOffset, u.I);
        }

        private static void InitExtendedV2Defaults(IntPtr pView)
        {
            if (pView == IntPtr.Zero) return;
            for (int z = 0; z < ExtBytes; z += 4)
            {
                Marshal.WriteInt32(pView, ExtOffset + z, 0);
            }
            // U20: (aestheticPersona 0..3, contrastScale=1.0, vignetteStrength=0.18, hdrBloomGodrayMult=1.0)
            WriteFloatAt(pView, ExtOffset + 0, 0.0f);
            WriteFloatAt(pView, ExtOffset + 4, 1.0f);
            WriteFloatAt(pView, ExtOffset + 8, 0.18f);
            WriteFloatAt(pView, ExtOffset + 12, 1.0f);
            // U21: (waterElevationMeters=-0.14, waterRippleGain=1.0, spellSchoolId=0, spellIntensity=0.0)
            WriteFloatAt(pView, ExtOffset + 16, -0.14f);
            WriteFloatAt(pView, ExtOffset + 20, 1.0f);
            WriteFloatAt(pView, ExtOffset + 24, 0.0f);
            WriteFloatAt(pView, ExtOffset + 28, 0.0f);
            // U22: (lahiriAyanamsaDeg=24.21, rashiIdx=0, nakshatraIdx=0, nodalEclipsePhase=0)
            WriteFloatAt(pView, ExtOffset + 32, 24.21f);
            // U24: (inShaderTopBar=1, shaderMaster=1, autoMenuFade=1, clickThrough=0)
            WriteFloatAt(pView, ExtOffset + 64, 1.0f);
            WriteFloatAt(pView, ExtOffset + 68, 1.0f);
            WriteFloatAt(pView, ExtOffset + 72, 1.0f);
            WriteFloatAt(pView, ExtOffset + 76, 0.0f);
            // U25: (liveSkyrimFps=60, frameTimeMs=16.67, menuFadeAlpha=1.0, menuOpen=0)
            WriteFloatAt(pView, ExtOffset + 80, 60.0f);
            WriteFloatAt(pView, ExtOffset + 84, 16.67f);
            WriteFloatAt(pView, ExtOffset + 88, 1.0f);
            WriteFloatAt(pView, ExtOffset + 92, 0.0f);
            // U26: (symOverride=-1, auroraMult=1.0, causticMult=1.0, thuumTrigger=0)
            WriteFloatAt(pView, ExtOffset + 96, -1.0f);
            WriteFloatAt(pView, ExtOffset + 100, 1.0f);
            WriteFloatAt(pView, ExtOffset + 104, 1.0f);
            WriteFloatAt(pView, ExtOffset + 108, 0.0f);
            // U27: (sentinel=180.0, stationCount=1, rsLive=0, skLive=0)
            WriteFloatAt(pView, ExtOffset + 112, V2SentinelFloat);
            WriteFloatAt(pView, ExtOffset + 116, 1.0f);
            WriteFloatAt(pView, ExtOffset + 120, 0.0f);
            WriteFloatAt(pView, ExtOffset + 124, 0.0f);
        }

        private static void EnsureExtendedV2LiveSummary(IntPtr pView, uint liveMask)
        {
            if (pView == IntPtr.Zero) return;
            float sent = ReadFloatAt(pView, ExtOffset + 112);
            if (Math.Abs(sent - V2SentinelFloat) > 0.5f)
            {
                InitExtendedV2Defaults(pView);
            }
            WriteFloatAt(pView, ExtOffset + 112, V2SentinelFloat);
            WriteFloatAt(pView, ExtOffset + 116, (float)CountBits32(liveMask));
            WriteFloatAt(pView, ExtOffset + 120, ((liveMask & MaskReshadeAddon) != 0u) ? 1.0f : 0.0f);
            WriteFloatAt(pView, ExtOffset + 124, ((liveMask & MaskSkse64Bridge) != 0u) ? 1.0f : 0.0f);
        }

        public bool WriteTopBarControls(
            bool inShaderHud,
            bool shaderMasterOn,
            bool autoMenuFade,
            bool clickThrough,
            int symOverride,
            float auroraMult,
            float causticMult,
            bool fireThuumPulse)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            lock (_localSync)
            {
                bool acquired = AcquireWriterMutex(12);
                try
                {
                    float sent = ReadFloatAt(_pView, ExtOffset + 112);
                    if (Math.Abs(sent - V2SentinelFloat) > 0.5f)
                    {
                        InitExtendedV2Defaults(_pView);
                    }
                    WriteFloatAt(_pView, ExtOffset + 64, inShaderHud ? 1.0f : 0.0f);
                    WriteFloatAt(_pView, ExtOffset + 68, shaderMasterOn ? 1.0f : 0.0f);
                    WriteFloatAt(_pView, ExtOffset + 72, autoMenuFade ? 1.0f : 0.0f);
                    WriteFloatAt(_pView, ExtOffset + 76, clickThrough ? 1.0f : 0.0f);
                    WriteFloatAt(_pView, ExtOffset + 96, (symOverride >= 0 && symOverride <= 7) ? (float)symOverride : -1.0f);
                    WriteFloatAt(_pView, ExtOffset + 100, Math.Max(0.1f, Math.Min(3.0f, auroraMult)));
                    WriteFloatAt(_pView, ExtOffset + 104, Math.Max(0.1f, Math.Min(3.0f, causticMult)));
                    if (fireThuumPulse)
                    {
                        float curPulse = ReadFloatAt(_pView, ExtOffset + 108);
                        WriteFloatAt(_pView, ExtOffset + 108, curPulse + 1.0f);
                    }
                    Marshal.WriteInt32(_pView, TrailerOffset + 4, unchecked((int)ContractVersion));
                    Thread.MemoryBarrier();
                    return true;
                }
                finally
                {
                    if (acquired) ReleaseWriterMutex();
                }
            }
        }

        public bool WritePhase2PersonaAndWorldCoupling(
            int persona0To3,
            float contrastScale,
            float vignetteStrength,
            float hdrBloomMult,
            float waterElevMeters,
            float waterRippleGain,
            int spellSchool0To5,
            float spellIntensity)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            lock (_localSync)
            {
                bool acquired = AcquireWriterMutex(12);
                try
                {
                    float sent = ReadFloatAt(_pView, ExtOffset + 112);
                    if (Math.Abs(sent - V2SentinelFloat) > 0.5f)
                    {
                        InitExtendedV2Defaults(_pView);
                    }
                    int pClamped = Math.Max(0, Math.Min(3, persona0To3));
                    int spClamped = Math.Max(0, Math.Min(5, spellSchool0To5));
                    WriteFloatAt(_pView, ExtOffset + 0, (float)pClamped);
                    WriteFloatAt(_pView, ExtOffset + 4, Math.Max(0.5f, Math.Min(2.0f, contrastScale)));
                    WriteFloatAt(_pView, ExtOffset + 8, Math.Max(0.0f, Math.Min(0.75f, vignetteStrength)));
                    WriteFloatAt(_pView, ExtOffset + 12, Math.Max(0.0f, Math.Min(3.0f, hdrBloomMult)));
                    WriteFloatAt(_pView, ExtOffset + 16, Math.Max(-500.0f, Math.Min(500.0f, waterElevMeters)));
                    WriteFloatAt(_pView, ExtOffset + 20, Math.Max(0.0f, Math.Min(3.0f, waterRippleGain)));
                    WriteFloatAt(_pView, ExtOffset + 24, (float)spClamped);
                    WriteFloatAt(_pView, ExtOffset + 28, Math.Max(0.0f, Math.Min(3.0f, spellIntensity)));
                    Marshal.WriteInt32(_pView, TrailerOffset + 4, unchecked((int)ContractVersion));
                    Thread.MemoryBarrier();
                    return true;
                }
                finally
                {
                    if (acquired) ReleaseWriterMutex();
                }
            }
        }

        public bool UpdateMenuFadeTelemetry(float menuFadeAlpha, bool menuDetected)
        {
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            float sent = ReadFloatAt(_pView, ExtOffset + 112);
            if (Math.Abs(sent - V2SentinelFloat) > 0.5f)
            {
                InitExtendedV2Defaults(_pView);
            }
            float clamped = Math.Max(0.05f, Math.Min(1.0f, menuFadeAlpha));
            WriteFloatAt(_pView, ExtOffset + 88, clamped);
            WriteFloatAt(_pView, ExtOffset + 92, menuDetected ? 1.0f : 0.0f);
            Thread.MemoryBarrier();
            return true;
        }

        public bool ReadExtendedBank32(float[] dst32)
        {
            if (dst32 == null || dst32.Length < ExtFloatCount) return false;
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            Marshal.Copy(new IntPtr(_pView.ToInt64() + ExtOffset), dst32, 0, ExtFloatCount);
            return true;
        }

        public bool WriteExtendedSlot4(int extSlot20To27, float x, float y, float z, float w)
        {
            if (extSlot20To27 < 20 || extSlot20To27 > 27) return false;
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            int off = ExtOffset + (extSlot20To27 - 20) * 16;
            WriteFloatAt(_pView, off + 0, x);
            WriteFloatAt(_pView, off + 4, y);
            WriteFloatAt(_pView, off + 8, z);
            WriteFloatAt(_pView, off + 12, w);
            Marshal.WriteInt32(_pView, TrailerOffset + 4, unchecked((int)ContractVersion));
            Thread.MemoryBarrier();
            return true;
        }

        public bool ReadTopBarDiagnostics(out TopBarLiveSnapshot snap)
        {
            snap = new TopBarLiveSnapshot();
            if (_pView == IntPtr.Zero && !TryOpenOrCreate()) return false;
            uint now32 = NowStamp32();
            uint magic = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 0));
            if (magic != MagicOtri) return false;

            snap.Mapped = true;
            snap.Sequence = unchecked((ulong)Marshal.ReadInt64(_pView, TrailerOffset + 8));
            uint rawMask = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 16));
            uint tEn = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 32));
            uint tRs = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 48));
            uint tSk = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 60));

            snap.EngineLive = IsStampAlive(now32, tEn);
            snap.ReshadeLive = IsStampAlive(now32, tRs);
            snap.Skse64Live = IsStampAlive(now32, tSk);
            uint liveMask = rawMask;
            if (snap.EngineLive) liveMask |= MaskOmegaEngine; else liveMask &= ~MaskOmegaEngine;
            if (snap.ReshadeLive) liveMask |= MaskReshadeAddon; else liveMask &= ~MaskReshadeAddon;
            if (snap.Skse64Live) liveMask |= MaskSkse64Bridge; else liveMask &= ~MaskSkse64Bridge;
            snap.ActiveMask = liveMask;
            snap.AddonFrames = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 52));
            snap.AddonApiVersion = unchecked((uint)Marshal.ReadInt32(_pView, TrailerOffset + 56));

            snap.CamX = ReadFloatAt(_pView, 4 * 4);
            snap.CamY = ReadFloatAt(_pView, 5 * 4);
            snap.CamZ = ReadFloatAt(_pView, 6 * 4);
            int baseSym = (int)Math.Round(ReadFloatAt(_pView, 36 * 4));
            if (baseSym < 0) baseSym = 0;
            if (baseSym > 7) baseSym = 7;
            snap.WeatherAuroraGain = ReadFloatAt(_pView, 59 * 4);
            snap.SheafH1State = ReadFloatAt(_pView, 67 * 4);

            float sent = ReadFloatAt(_pView, ExtOffset + 112);
            snap.V2Active = (Math.Abs(sent - V2SentinelFloat) < 0.5f);
            if (snap.V2Active)
            {
                snap.InShaderHudEnabled = ReadFloatAt(_pView, ExtOffset + 64) > 0.5f;
                snap.ShaderMasterEnabled = ReadFloatAt(_pView, ExtOffset + 68) > 0.5f;
                snap.AutoMenuFadeEnabled = ReadFloatAt(_pView, ExtOffset + 72) > 0.5f;
                snap.DesktopClickThrough = ReadFloatAt(_pView, ExtOffset + 76) > 0.5f;
                snap.SkyrimFps = ReadFloatAt(_pView, ExtOffset + 80);
                snap.FrameTimeMs = ReadFloatAt(_pView, ExtOffset + 84);
                snap.MenuFadeAlpha = ReadFloatAt(_pView, ExtOffset + 88);
                snap.MenuDetected = ReadFloatAt(_pView, ExtOffset + 92) > 0.5f;
                float symOv = ReadFloatAt(_pView, ExtOffset + 96);
                snap.SymmetryOverride = (symOv >= -0.25f && symOv <= 7.5f) ? (int)Math.Round(symOv) : -1;
                snap.AuroraMultiplier = ReadFloatAt(_pView, ExtOffset + 100);
                snap.CausticMultiplier = ReadFloatAt(_pView, ExtOffset + 104);
                snap.ThuumPulseStamp = ReadFloatAt(_pView, ExtOffset + 108);
                snap.AestheticPersona = Math.Max(0, Math.Min(3, (int)Math.Round(ReadFloatAt(_pView, ExtOffset + 0))));
                float pCon = ReadFloatAt(_pView, ExtOffset + 4);
                snap.PersonaContrast = (pCon > 0.1f) ? pCon : 1.0f;
                snap.PersonaVignette = Math.Max(0.0f, ReadFloatAt(_pView, ExtOffset + 8));
                float hBlm = ReadFloatAt(_pView, ExtOffset + 12);
                snap.HdrBloomMultiplier = (hBlm > 0.01f && hBlm <= 4.0f) ? hBlm : 1.0f;
                snap.WaterElevationMeters = ReadFloatAt(_pView, ExtOffset + 16);
                float wRip = ReadFloatAt(_pView, ExtOffset + 20);
                snap.WaterRippleGain = (wRip > 0.01f) ? wRip : 1.0f;
                snap.SpellSchoolId = Math.Max(0, Math.Min(5, (int)Math.Round(ReadFloatAt(_pView, ExtOffset + 24))));
                snap.SpellIntensity = Math.Max(0.0f, ReadFloatAt(_pView, ExtOffset + 28));
            }
            else
            {
                snap.InShaderHudEnabled = true;
                snap.ShaderMasterEnabled = true;
                snap.AutoMenuFadeEnabled = true;
                snap.DesktopClickThrough = false;
                snap.SkyrimFps = 60.0f;
                snap.FrameTimeMs = 16.67f;
                snap.MenuFadeAlpha = 1.0f;
                snap.MenuDetected = false;
                snap.SymmetryOverride = -1;
                snap.AuroraMultiplier = 1.0f;
                snap.CausticMultiplier = 1.0f;
                snap.ThuumPulseStamp = 0.0f;
                snap.AestheticPersona = 0;
                snap.PersonaContrast = 1.0f;
                snap.PersonaVignette = 0.18f;
                snap.HdrBloomMultiplier = 1.0f;
                snap.WaterElevationMeters = -0.14f;
                snap.WaterRippleGain = 1.0f;
                snap.SpellSchoolId = 0;
                snap.SpellIntensity = 0.0f;
            }
            snap.ActiveSymmetry = (snap.SymmetryOverride >= 0 && snap.SymmetryOverride <= 7) ? snap.SymmetryOverride : baseSym;
            return true;
        }

        public static bool TryReadSnapshot112(
            string mapName,
            float[] dst112,
            out ulong seqOut,
            out uint activeMaskOut,
            out uint crcOut,
            out uint addonFramesOut,
            out uint addonApiOut)
        {
            seqOut = 0UL;
            activeMaskOut = 0u;
            crcOut = 0u;
            addonFramesOut = 0u;
            addonApiOut = 0u;
            if (dst112 == null || dst112.Length < FullFloatCount) return false;
            float[] low80 = new float[FloatCount];
            if (!TryReadSnapshotEx(mapName, low80, out seqOut, out activeMaskOut, out crcOut, out addonFramesOut, out addonApiOut))
            {
                return false;
            }
            Array.Copy(low80, 0, dst112, 0, FloatCount);
            string target = string.IsNullOrEmpty(mapName) ? DefaultMapName : mapName;
            IntPtr h = OpenFileMappingW(FILE_MAP_READ, 0, target);
            if (h == IntPtr.Zero) return true;
            IntPtr p = MapViewOfFile(h, FILE_MAP_READ, 0u, 0u, UIntPtr.Zero);
            if (p == IntPtr.Zero)
            {
                CloseHandle(h);
                return true;
            }
            try
            {
                uint ver = unchecked((uint)Marshal.ReadInt32(p, TrailerOffset + 4));
                if (ver >= 0x00010800u)
                {
                    Marshal.Copy(new IntPtr(p.ToInt64() + ExtOffset), dst112, FloatCount, ExtFloatCount);
                }
            }
            finally
            {
                UnmapViewOfFile(p);
                CloseHandle(h);
            }
            return true;
        }

        public void Dispose()
        {
            lock (_localSync)
            {
                if (_pView != IntPtr.Zero)
                {
                    try { UnmapViewOfFile(_pView); } catch { }
                    _pView = IntPtr.Zero;
                }
                if (_hMap != IntPtr.Zero)
                {
                    try { CloseHandle(_hMap); } catch { }
                    _hMap = IntPtr.Zero;
                }
                if (_writerMutex != null)
                {
                    try { _writerMutex.Dispose(); } catch { }
                    _writerMutex = null;
                }
            }
        }
    }
}