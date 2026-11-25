#pragma once
// =============================================================================
// OmegaSkyrimWorldBridge_CleanABI.hpp (OmegaEngine v1.7.7 / Suite v3.7.1)
// SPDX-License-Identifier: MIT OR BSD-3-Clause
// Clean-room C++23 SKSE64 2.2.6+ Binary ABI & Local\OmegaTriEngineUniforms SDK.
// Avoids 100% of upstream CommonLibSSE-NG GPL-3.0 copyleft lock-in while
// supporting Skyrim AE 1.6.640..1.6.1179, 1.7.99..1.7.104, and SE 1.5.97.
// =============================================================================

#include <cstddef>
#include <cstdint>

namespace omega::skse_clean {

inline constexpr std::uint32_t kPackedSkyrim_1_7_104 = 0x01070680u;
inline constexpr std::uint32_t kPackedSkyrim_1_6_1170 = 0x01064920u;
inline constexpr std::uint32_t kPackedSkyrim_1_6_1179 = 0x010649B0u;
inline constexpr std::uint32_t kPackedSkyrim_1_6_640 = 0x01062800u;
inline constexpr std::uint32_t kPackedSkyrim_1_5_97 = 0x01050610u;

struct SKSEPluginVersionData {
    std::uint32_t dataVersion;             // +0x000 = 1
    std::uint32_t pluginVersion;           // +0x004 = 0x00010700
    char          name[256];               // +0x008
    char          author[256];             // +0x108
    char          supportEmail[252];       // +0x208
    std::uint32_t versionIndependenceEx;   // +0x304 = 1 (NoStructUse)
    std::uint32_t versionIndependence;     // +0x308 = 2 (Signatures)
    std::uint32_t compatibleVersions[16];  // +0x30C
    std::uint32_t seVersionRequired;       // +0x34C
};

static_assert(sizeof(SKSEPluginVersionData) == 0x350, "SKSEPluginVersionData must be 848 bytes");
static_assert(offsetof(SKSEPluginVersionData, versionIndependenceEx) == 0x304);
static_assert(offsetof(SKSEPluginVersionData, versionIndependence) == 0x308);
static_assert(offsetof(SKSEPluginVersionData, compatibleVersions) == 0x30C);

struct SKSEInterface {
    std::uint32_t skseVersion;
    std::uint32_t runtimeVersion;
    std::uint32_t editorVersion;
    std::uint32_t isEditor;
    void* (*QueryInterface)(std::uint32_t id);
    std::uint32_t (*GetPluginHandle)();
    std::uint32_t (*GetReleaseIndex)();
    const void* (*GetPluginInfo)(const char* name);
};

struct NiPoint3 {
    float x, y, z;
};

struct NiMatrix3 {
    float entry[3][3];
};

struct NiTransform {
    NiMatrix3 rotate;
    NiPoint3  translate;
    float     scale;
};

static_assert(sizeof(NiTransform) == 0x34, "Creation Engine NiTransform must be 52 bytes");

} // namespace omega::skse_clean