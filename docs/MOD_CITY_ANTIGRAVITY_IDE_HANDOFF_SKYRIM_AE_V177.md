# MOD CITY — GOOGLE ANTIGRAVITY IDE HANDOFF: SKYRIM AE v1.7.7 WORLD-SPACE SHADER, RESHADE 6.8 ZERO-CRT ADD-ON, SKSE64 BRIDGE & FALLOUT 4 UNIFIED MODDING BASELINE

* **Target Modding Workspace**: `C:\ModCity` (`C:\Mod_City_Project`) inside **Google Antigravity IDE**

* **Linked Standalone Engine Repository**: `E:\Omega_standalone_developments` ([bospaladin34-crypto/Omega_standalone_developments](https://github.com/bospaladin34-crypto/Omega_standalone_developments), branch `vesper_production`, Unified Suite `v3.7.1` / Station 15 `OmegaEngine v1.7.7`)

* **Mainline Ecosystem & Cloud Bridge**: `C:\sovereign_manifold_v31` ([bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-](https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-)) | [ACT-Omega Web & Telemetry Portal](https://act-omega-net.dev/) | [ACT_OMEGA_GEMINI_BRIDGE](https://drive.google.com/drive/folders/11UG1jWz4Th0c_R9Nem4I6niXKZEMpGj3) | [Master Handoff #11 (`T399` Pillar 6 Mod Surgeon)](https://docs.google.com/document/d/1iZQeylfbRl7Wef_cnNU0w8FZgyGRFGu-VsTPMGU45Mc/edit?usp=drivesdk&ouid=113906991967086567667)

* **Live Verified Game Target**: *The Elder Scrolls V: Skyrim Special Edition / Anniversary Edition* (`SkyrimSE.exe` `1.7.104.0` at `F:\SteamLibrary\steamapps\common\Skyrim Special Edition`) + [SKSE64 (`2.3.1` / ABI `2.2.6+`)](https://skse.silverlock.org/) + [ReShade (`6.8.0.2155` Add-on API `v20`)](https://reshade.me/) + [Vortex Mod Manager](https://www.nexusmods.com/vortex)

## 1. Standing Terminology & Engineering Non-Negotiables

1. **IDE & Dual-Lens Grounding**: Throughout `C:\ModCity` and `E:\Omega_standalone_developments`, "Antigravity" denotes **Google Antigravity IDE**. Every geometric or topological codename maps 1:1 to its concrete systems-engineering, compiler, or graphics mechanism:

   * `5D Penrose P3` & `8D E8` ([Viazovska 2017](https://arxiv.org/abs/1603.04246)) -> De Bruijn 5-axis aperiodic cosine/pentagrid sky aurora (`UiAuroraBlend`) and `D8 + (D8 + 1/2)` nearest-coset procedural normal/caustic distance fields (`OmegaE8FacetSeam`).

   * `Einstein Hat (1, sqrt(3))` ([Smith et al. 2023a](https://arxiv.org/abs/2303.10798)) & `Chiral Spectre (1, 1)` ([Smith et al. 2023b](https://arxiv.org/abs/2305.17743)) -> two-scale triangular-kite seam distance (`activeSym = 2`) and chiral curl-warped reflectionless dungeon caustics (`activeSym = 3`).

   * `4D Hopf S^3 -> S^2`, `4D 24/120-Cell H4`, `Hyperbolic Poincare {5,3,4}` ([Nelson & Segerman 2015](https://arxiv.org/abs/1511.02851)), & `24D Leech + Golay [24,12,8]` ([Cohn et al. 2017](https://arxiv.org/abs/1603.06518)) -> inverse stereographic Villarceau circle seams (`activeSym = 4`), `H4` golden-ratio reflection folding (`activeSym = 5`), Lorentzian `R^{3,1}` Coxeter folding (`activeSym = 6`), and Golay `[24,12,8]` parity-modulated hexagonal lattices (`activeSym = 7`).

   * `Cellular Sheaf H^1 = 0` -> zero-defect cross-language `#[repr(C)]` FFI layout verification (`384 B` MMF + `848 B` `SKSEPluginVersionData`) and Creation Engine 16-stage Quest DAG acyclicity / prerequisite verification (`U16.w` Spectre dungeon guard).

2. **Zero-CRT, Zero-Python, Pure 7-bit ASCII Tooling**:

   * Both injected Win64 native binaries (`OmegaTriEngine.addon64` and `OmegaSkyrimWorldBridge.dll`) compile as `#![no_std]` Rust `cdylib` targets (`/NODEFAULTLIB /ENTRY:DllMain kernel32.lib`) importing `KERNEL32.dll` **only** (`0` `vcruntime140.dll` / `ucrtbase.dll` dependencies).

   * All PowerShell emitters use 100% 7-bit ASCII (`byte < 0x80`), zero backticks, zero `#Requires`, CRLF line endings, and UTF-8 No-BOM file encoding (`UTF8Encoding($false)`).

## 2. Live Hardware-Verified Skyrim AE `v1.7.7` Stack (`F:\SteamLibrary\steamapps\common\Skyrim Special Edition`)

All 7 runtime components and both distributable archives are verified running smoothly in live full-screen `SkyrimSE.exe` (`1.7.104.0`) with depth linearization, world-space camera locking, and subtle Sovngarde/caustic shading active:

| Component / Archive | Installed Path | Size & Format | Verified Live Role | 
| ----- | ----- | ----- | ----- | 
| `ReShade.fxh` | `reshade-shaders\Shaders\ReShade.fxh` | `3,835 B` (`724 B` `vs_5_0` DXBC) | Upstream CC0-1.0 [`ReShade.fxh`](https://github.com/crosire/reshade-shaders/blob/slim/Shaders/ReShade.fxh) providing `PostProcessVS`, `ReShade::BackBuffer`, and `ReShade::GetLinearizedDepth` | 
| `OmegaTriEngine_SkyrimAE.fx` | `reshade-shaders\Shaders\OmegaTriEngine_SkyrimAE.fx` | `~16.6 KB` (`34,504 B` `ps_5_0` DXBC) | Lexer-clean (`1,664` identifier tokens audited, `0` reserved words) 8-symmetry world-space post-process shader (`0.616 s` live compile in `ReShade.log`) | 
| `OmegaTriEngine.addon64` | `OmegaTriEngine.addon64` (Game Root) | `11.0 KB` PE32+ (`KERNEL32.dll` only) | ReShade 6.8 API `v20` (`& v9..v24`) add-on streaming `20 x float4` (`U0..U19`) from `Local\OmegaTriEngineUniforms` at `0` VAD churn | 
| `OmegaSkyrimWorldBridge.dll` | `Data\SKSE\Plugins\OmegaSkyrimWorldBridge.dll` | `20.0 KB` PE32+ (`KERNEL32.dll` only) | Address-Library-free SKSE64 `2.3.1` plugin (`SKSEPlugin_Version` `848 B`, `0x00010707`) streaming camera, weather, Thu'um, and `H^1 = 0` quest state | 
| `OmegaTriEngine_WorldBridge.esp` | `Data\OmegaTriEngine_WorldBridge.esp` | `514 B` ESL-flagged `.esp` (`0x0201`) | Creation Engine light plugin (`HEDR v1.70`, `0` FormID slot cost) defining `4` `KYWD` records (`OMTE_SpectreGuardDungeon`, `OMTE_SovngardePortalWeather`, `OMTE_ThuumWaveEmitter`, `OMTE_SheafQuestVerified`) | 
| `OmegaTriEngine_Preset.ini` & `ReShade.ini` | Game Root | UTF-8 No-BOM `.ini` | Pins `EffectSearchPaths=.\reshade-shaders\Shaders`, `PerformanceMode=0`, `Shift + F2` overlay (`113,0,1,0`), `Scroll Lock` toggle (`145,0,0,0`), and Skyrim depth macros | 
| `OmegaTriEngine_SkyrimAE_SKSE64_v1.7.7_Vortex.zip` | `downloads\omega_engine_exports\` | Data-rooted FOMOD `.zip` | Drag-and-drop [Vortex](https://www.nexusmods.com/vortex) archive bundling `.esp`, `SKSE/Plugins/*.dll + *.ini`, UTF-8 No-BOM `.psc`/`.pex`, and `fomod/info.xml` | 
| `OmegaTriEngine_SkyrimAE_RootReShade_v1.7.7.zip` | `downloads\omega_engine_exports\` | Game-root `.zip` | Companion root bundle containing `reshade-shaders/Shaders/ReShade.fxh`, `OmegaTriEngine_SkyrimAE.fx`, `OmegaTriEngine_Preset.ini`, and `OmegaTriEngine.addon64` | 

## 3. 384-Byte Lock-Free Seqlock Bus (`Local\OmegaTriEngineUniforms`)

Station 15 (`OmegaEngine.exe` `:19795`), `OmegaSkyrimWorldBridge.dll` (writer mask `0x10`), and `OmegaTriEngine.addon64` coordinate lock-free through the `384`-byte Win32 Memory-Mapped File `Local\OmegaTriEngineUniforms` (`magic = 0x4F544542` `"OTEB"`, `version = 1`, `64`-byte Seqlock + CRC32C header + `320`-byte `U0..U19` `float4` array):

| Uniform Slot | Producer(s) | Exact `float4(x, y, z, w)` World-Space & Multi-Station Mapping | 
| ----- | ----- | ----- | 
| `U0` | `OmegaEngine` (`:19795`) | `(resX, resY, timeSec, deltaTimeSec)` | 
| `U1` | `OmegaSkyrimWorldBridge.dll` / `:19795` | `(camWorldPosMeters.x, camWorldPosMeters.y, camWorldPosMeters.z, tanHalfFovY)` (`1 Skyrim unit = 0.01428 m`) | 
| `U3` | `OmegaAV` (`:19779`) / `OmegaSynth` (`:19782`) | `(subBassRms, midRms, highBrilliance, transientEnvelope)` | 
| `U7..U8` | `OmegaPenrose` (`:19785`) | Live 5-axis De Bruijn pentagrid phason shifts `(gamma0..gamma4)` + inflation ratio | 
| `U9` | `OmegaEngine` (`:19795`) | `(activeSymmetryMode [0..7], tileSpatialScale, normalPlanarWeight, liveBusLockFlag)` | 
| `U12` | `OmegaSynth` (`:19782`) | `(mpePitchHz, mpePressure, mpeTimbre, e8RootIndex [0..239])` | 
| `U14.w` | `OmegaSkyrimWorldBridge.dll` | Smooth Sovngarde / aurora weather blend factor `[0.0 .. 1.5]` | 
| `U16` | `OmegaSkyrimWorldBridge.dll` | `(shoutOriginMeters.x, shoutOriginMeters.y, shoutOriginMeters.z, sheafQuestState)` (`+2.0` Spectre dungeon `H^1 = 0`, `-1.0` coboundary quarantine) | 
| `U17` | `OmegaSkyrimWorldBridge.dll` | `(weatherClass [0=Clear..4=Sovngarde], gameHour [0..24], shoutWaveRadiusMeters, shoutImpulseStrength)` | 
| `U18` | `OmegaSkyrimWorldBridge.dll` | `(camRight.x, camRight.y, camRight.z, skseBridgeHeartbeat > 0.5)` (`NiCamera` `m_world.entry[0..2]`) | 
| `U19` | `OmegaSkyrimWorldBridge.dll` | `(camForward.x, camForward.y, camForward.z, skyrimUnitScale)` (`camUp = normalize(cross(camForward, camRight))`) | 

## 4. Critical ABI, Lexer & Crash-Guard Rules Discovered on Hardware (Must Preserve in Antigravity IDE)

Any future edits by Google Antigravity IDE agents to `OmegaTriEngine.addon64`, `OmegaTriEngine_SkyrimAE.fx`, or `OmegaSkyrimWorldBridge.dll` must preserve these three hardware-verified invariants:

1. **Exact `reshade::api::effect_runtime` Vtable Slots (`vtbl[19/22/25/27/33]`, `delta = 0`)**:

   * Per the [ReShade `effect_runtime` API Reference](https://crosire.github.io/reshade-docs/structreshade_1_1api_1_1effect__runtime.html), `effect_runtime` inherits strictly from `device_object -> api_object` (`3` base virtual functions: `get_private_data`, `set_private_data`, `get_uint64_handle`, followed by `get_device` at `[3]`). It does **not** inherit from `swapchain`!

   * Adding `+2` for swapchain `color_space` methods shifts `set_uniform_value_float` (`[33]`) onto `set_uniform_value_uint` (`[35]`) and `get_annotation_string` (`[27]`) onto `set_annotation_int` (`[29]`), triggering an immediate post-compile access violation (`0xC0000005`) the first frame `.fx` finishes compiling.

   * Always keep `vtable_delta_for_api(_api_ver) == 0`: `enumerate_uniform_variables = 19`, `get_uniform_variable_name = 22`, `get_annotation_int_from_uniform_variable = 25`, `get_annotation_string_from_uniform_variable = 27`, and `set_uniform_value_float = 33`.

   * Register [`reshade::addon_event`](https://crosire.github.io/reshade-docs/namespacereshade.html) `destroy_effect_runtime = 11` alongside `begin_effects = 86` and `reload_effects = 88` (API `v20`) so cached `uniform` handles are invalidated cleanly before swapchain resize or game exit.

2. **ReShadeFX Built-In Compiler Lexer & SSA Parser Discipline**:

   * ReShade compiles `.fx` shaders through its own `reshadefx` front-end ([`source/effect_lexer.cpp`](https://github.com/crosire/reshade/blob/main/source/effect_lexer.cpp) and [`REFERENCE.md`](https://github.com/crosire/reshade-shaders/blob/slim/REFERENCE.md)) before emitting DXBC, which is stricter than standalone `d3dcompiler_47.dll`:

     * **Reserved Type Keywords**: Never name variables `half`, `half2`, `half3`, `half4`, `half2x2..half4x4`, `double*`, `fixed*`, `sample`, `linear`, `point`, `line`, `triangle`, `string`, `vector`, `matrix`, `in`, `out`, or `inout` (throws `error X3000: syntax error: unexpected 'reserved word', expected 'identifier'`; use `kHalf2` / `kHalf4`).

     * **Zero Vector Ternaries & Typed Annotations**: Never write `cond ? float3(...) : float3(...)` or `< string source = ... >`; use `if / else` or `lerp(...)`, untyped `< source = "omega_tri_engine"; index = N; >` annotations with no `= float4(...)` initializer on custom-source uniforms, and keep `PerformanceMode=0` in `ReShade.ini` so live uniforms stay mutable at 60+ FPS.

     * **Clean Search Path**: Keep `EffectSearchPaths=.\reshade-shaders\Shaders` with **no** trailing `\` or `\**` wildcard so `#include "ReShade.fxh"` resolves deterministically on every cold boot.

3. **Zero-VAD Persistent MMF Mapping & OneDrive `My Games` Resolution**:

   * Both `OmegaTriEngine.addon64` and `OmegaSkyrimWorldBridge.dll` map `Local\OmegaTriEngineUniforms` once via atomic compare-exchange (`G_BUS_VIEW`) and keep the `384`-byte view resident for process lifetime (`0` per-frame `OpenFileMappingW`/`UnmapViewOfFile` VAD lock contention with Skyrim's `ScrapHeap`).

   * `OmegaSkyrimWorldBridge.dll` checks `%OneDrive%\Documents\My Games\Skyrim Special Edition\Logs\Script\User\OmegaTriEngine_WorldBridge.0.log` first before falling back to `%USERPROFILE%\Documents\...`, supporting Windows 11 OneDrive Known Folder Redirection automatically.

## 5. Preserved Fallout 4 Mod City Baseline (`C:\ModCity\Fallout4`)

The Skyrim AE `v1.7.7` pipeline coexists cleanly alongside the existing Fallout 4 Mod City modules inside `C:\ModCity`:

* **Papyrus `SOV_EDE8_UarmCoreScript.psc` v7.1 (`Sovereign_EDE8.esp`)**: Zero-allocation 9-node `ObjectReference[]` orbital pool driven by Havok [`TranslateTo`](https://papyrus.bellcube.dev/fallout4/script/objectreference/) keyframed interpolation, `OnTranslationComplete` self-chaining, vanilla `PlayerRef.IsOverEncumbered()` RAS dampener (avoiding non-existent vanilla `ActorValue` properties and save-bloating [`Utility.SetINIFloat`](https://papyrus.bellcube.dev/fallout4/script/utility/function/setinifloat/) mutations), [`ReferenceAlias` lifecycle discipline](https://forums.nexusmods.com/topic/6903216-help-with-reference-alias-scripting/), and full 16-file uncompressed `BA2` (`GNRL` + `DX10`) packaging.

* **Native F4SE Plugin (`SovereignPerfF4SE.dll` v2.1)**: `14 KB` zero-CRT Win32 governor isolating `Fallout4.exe` off Core 0 SMT (`0xFE`) with `PROCESS_POWER_THROTTLING_EXECUTION_SPEED` disabled and `timeBeginPeriod(1)`.

## 6. Copy-Paste Prompt for Google Antigravity IDE (`C:\ModCity` + `E:\Omega_standalone_developments`)

Paste this prompt into Google Antigravity IDE on `C:\ModCity` when ready to jump into Phase-2 Skyrim AE shader & SKSE64 modding:

> Read `MOD_CITY_ANTIGRAVITY_IDE_HANDOFF_SKYRIM_AE_V177.md` (staged in `C:\ModCity\`, `G:\My Drive\ACT_OMEGA_GEMINI_BRIDGE\`, and `E:\Omega_standalone_developments\docs\`) and inspect `E:\Omega_standalone_developments\15_omega_engine\`. Keep all ReShade 6.8 API v20 vtable invariants (`vtbl[19/22/25/27/33]`, `EV_DESTROY_EFFECT_RUNTIME = 11`), `effect_lexer.cpp` reserved-word bans (`kHalf4`/`kHalf2`, zero vector ternaries, `PerformanceMode=0`), and `#![no_std]` `KERNEL32.dll`-only PE rules intact. Set up `C:\ModCity\SkyrimAE_OmegaTriEngine\` linked to `E:\Omega_standalone_developments\15_omega_engine\` and prepare our Phase-2 Skyrim AE expansion harness:
>
> 1. **Multi-Pass HDR Sovngarde Bloom & Depth-Edge Bilateral Godrays (`P0 -> P1 -> P2` in `OmegaTriEngine_SkyrimAE.fx`)**: Add half-resolution intermediate render targets (`RenderTarget = OmegaHdrHalfTex`) with depth-aware bilateral upsampling so sky auroras, Thu'um shockwaves, and torch/spell emissives cast subtle atmospheric scatter without blurring foreground silhouettes or text.
>
> 2. **UI / Menu Auto-Attenuation & Spell-School Chromatic Coupling (`OmegaSkyrimWorldBridge.dll` v1.8)**: Stream menu-open state (`U15.w` smooth `250 ms` fade to `0.0` during Inventory, Map, Dialogue, Book, and Lockpicking menus) plus equipped left/right spell archetype (`Fire`, `Frost`, `Shock`, `Alteration`, `Restoration`) and cell water-plane height (`U15.xyz`) into `Local\OmegaTriEngineUniforms`.
>
> 3. **Creation Kit Resonator Standing Stones & Acoustic-Shader Link (`OmegaTriEngine_WorldBridge.esp` + `:19782` `OmegaSynth`)**: Expand the ESL light plugin and UTF-8 No-BOM Papyrus scripts with interactive `ACTI` Resonator Stones whose `OnActivate` and Thu'um `OnHit` events trigger synchronized E8/Hopf wave-pulses in Skyrim AE and real-time MPE harmonic chords in Station 04 `OmegaSynth` (`:19782`).