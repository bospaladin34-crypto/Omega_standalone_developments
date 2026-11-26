using System;
using System.Globalization;
using OmegaCommon;

namespace OmegaTriEngine
{
    // Station 15: OmegaEngine v1.9.0 (Suite v3.9.0, HTTP :19795)
    // Phase 1 Multi-Station 512-Byte OTRI v1.8 Shared-Memory Mesh Adapter (U0..U27 + TopBar Controls + 250ms Menu Auto-Fade).
    public sealed class OmegaTriEngineUniformBus : IDisposable
    {
        public const string DefaultMapName = OmegaTriEngineSharedBus.DefaultMapName;
        public const int FloatCount = OmegaTriEngineSharedBus.FloatCount;
        public const int UniformBytes = OmegaTriEngineSharedBus.UniformBytes;
        public const int TrailerOffset = OmegaTriEngineSharedBus.TrailerOffset;
        public const int ExtOffset = OmegaTriEngineSharedBus.ExtOffset;
        public const int ExtFloatCount = OmegaTriEngineSharedBus.ExtFloatCount;
        public const int FullFloatCount = OmegaTriEngineSharedBus.FullFloatCount;
        public const int TotalMapBytes = OmegaTriEngineSharedBus.TotalMapBytes;

        public const uint MagicOtri = OmegaTriEngineSharedBus.MagicOtri;
        public const uint ContractVersion = OmegaTriEngineSharedBus.ContractVersion;

        public const uint MaskOmegaEngine  = OmegaTriEngineSharedBus.MaskOmegaEngine;
        public const uint MaskOmegaSynth   = OmegaTriEngineSharedBus.MaskOmegaSynth;
        public const uint MaskOmegaPenrose = OmegaTriEngineSharedBus.MaskOmegaPenrose;
        public const uint MaskOmegaAv      = OmegaTriEngineSharedBus.MaskOmegaAv;
        public const uint MaskReshadeAddon = OmegaTriEngineSharedBus.MaskReshadeAddon;
        public const uint MaskSkse64Bridge = OmegaTriEngineSharedBus.MaskSkse64Bridge;
        public const uint MaskOmegaTensor  = OmegaTriEngineSharedBus.MaskOmegaTensor;
        public const uint MaskPixel10Live  = OmegaTriEngineSharedBus.MaskPixel10Live;
        public const uint MaskPixel8Live   = OmegaTriEngineSharedBus.MaskPixel8Live;

        private readonly OmegaTriEngineSharedBus _inner;

        public bool IsMapped { get { return _inner.IsMapped; } }
        public string MapName { get { return _inner.MapName; } }
        public ulong LastSequence { get { return _inner.LastSequence; } }
        public uint LastCrc32 { get { return _inner.LastCrc32; } }
        public uint LastAddonApiVersion { get { return _inner.LastAddonApiVersion; } }
        public OmegaTriEngineSharedBus InnerBus { get { return _inner; } }

        public OmegaTriEngineUniformBus(string mapName)
        {
            _inner = new OmegaTriEngineSharedBus(mapName);
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            return OmegaTriEngineSharedBus.ComputeCrc32(data, offset, count);
        }

        public static uint ComputeFloat80Crc32(float[] u80)
        {
            if (u80 == null || u80.Length < FloatCount) return 0u;
            byte[] tmp = new byte[UniformBytes];
            Buffer.BlockCopy(u80, 0, tmp, 0, UniformBytes);
            return OmegaTriEngineSharedBus.ComputeCrc32(tmp, 0, UniformBytes);
        }

        public bool PublishSnapshot(float[] u80, uint explicitExtraMask, out ulong committedSeq, out uint crc32)
        {
            uint liveMask, addonFrames;
            return _inner.PublishEngineFrameAndMerge(u80, explicitExtraMask, out committedSeq, out crc32, out liveMask, out addonFrames);
        }

        public bool PublishAndMergeMesh(float[] u80, uint explicitExtraMask, out ulong committedSeq, out uint crc32, out uint liveMask, out uint addonFrames)
        {
            return _inner.PublishEngineFrameAndMerge(u80, explicitExtraMask, out committedSeq, out crc32, out liveMask, out addonFrames);
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
            return _inner.WriteTopBarControls(inShaderHud, shaderMasterOn, autoMenuFade, clickThrough, symOverride, auroraMult, causticMult, fireThuumPulse);
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
            return _inner.WritePhase2PersonaAndWorldCoupling(
                persona0To3, contrastScale, vignetteStrength, hdrBloomMult,
                waterElevMeters, waterRippleGain, spellSchool0To5, spellIntensity);
        }

        public bool UpdateMenuFadeTelemetry(float menuFadeAlpha, bool menuDetected)
        {
            return _inner.UpdateMenuFadeTelemetry(menuFadeAlpha, menuDetected);
        }

        public bool ReadTopBarDiagnostics(out OmegaTriEngineSharedBus.TopBarLiveSnapshot snap)
        {
            return _inner.ReadTopBarDiagnostics(out snap);
        }

        public bool WriteExtendedSlot4(int slot20To27, float x, float y, float z, float w)
        {
            return _inner.WriteExtendedSlot4(slot20To27, x, y, z, w);
        }

        public static bool TryReadSnapshot(string mapName, float[] dst80, out ulong seqOut, out uint activeMaskOut, out uint crcOut)
        {
            uint addonFrames, addonApi;
            return OmegaTriEngineSharedBus.TryReadSnapshotEx(mapName, dst80, out seqOut, out activeMaskOut, out crcOut, out addonFrames, out addonApi);
        }

        public static bool TryReadSnapshotWithAddon(string mapName, float[] dst80, out ulong seqOut, out uint activeMaskOut, out uint crcOut, out uint addonFramesOut, out uint addonApiOut)
        {
            return OmegaTriEngineSharedBus.TryReadSnapshotEx(mapName, dst80, out seqOut, out activeMaskOut, out crcOut, out addonFramesOut, out addonApiOut);
        }

        public static bool TryReadSnapshot112(string mapName, float[] dst112, out ulong seqOut, out uint activeMaskOut, out uint crcOut, out uint addonFramesOut, out uint addonApiOut)
        {
            return OmegaTriEngineSharedBus.TryReadSnapshot112(mapName, dst112, out seqOut, out activeMaskOut, out crcOut, out addonFramesOut, out addonApiOut);
        }

        public static string FormatSlotSummary(float[] u80)
        {
            if (u80 == null || u80.Length < FloatCount) return "invalid";
            return string.Format(CultureInfo.InvariantCulture,
                "Res={0:0}x{1:0} t={2:0.00}s | Cam=({3:0.00},{4:0.00},{5:0.00}) | Audio=({6:0.00},{7:0.00},{8:0.00}) | Sym={9:0} | ImpR={10:0.00}",
                u80[0], u80[1], u80[2], u80[4], u80[5], u80[6], u80[12], u80[13], u80[14], u80[36], u80[57]);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
