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
        public const int TotalMapBytes = 384;
        public const uint HeartbeatTtlMs = 1500u;
        public const uint TensorHeartbeatTtlMs = 4500u;

        public const uint MagicOtri = 0x4F545249u;
        public const uint ContractVersion = 0x00010201u;

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
                    _pView = MapViewOfFile(_hMap, FILE_MAP_READ | FILE_MAP_WRITE, 0u, 0u, (UIntPtr)TotalMapBytes);
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
                p = MapViewOfFile(h, FILE_MAP_READ, 0u, 0u, (UIntPtr)TotalMapBytes);
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