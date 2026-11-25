// =============================================================================
// OmegaSkyrimWorldBridge_Companion.cpp (MIT Clean-Room Reference Implementation)
// Production Win64 binary is built from #![no_std] zero-CRT Rust crate
// (15_omega_engine/skse64_bridge -> OmegaSkyrimWorldBridge.dll, KERNEL32.dll ONLY).
// =============================================================================
#include "OmegaSkyrimWorldBridge_CleanABI.hpp"

extern "C" __declspec(dllexport) const omega::skse_clean::SKSEPluginVersionData SKSEPlugin_Version = {
    1u,
    0x00010700u,
    "OmegaSkyrimWorldBridge",
    "ACT-Omega Unified Labs (MIT Clean-Room)",
    "https://github.com/bospaladin34-crypto/Omega_standalone_developments",
    1u, // NoStructUse
    2u, // Signatures
    {
        omega::skse_clean::kPackedSkyrim_1_7_104,
        omega::skse_clean::kPackedSkyrim_1_6_1170,
        omega::skse_clean::kPackedSkyrim_1_6_1179,
        omega::skse_clean::kPackedSkyrim_1_6_640,
        omega::skse_clean::kPackedSkyrim_1_5_97,
        0u
    },
    0u
};