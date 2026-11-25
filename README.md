# Omega Standalone Developments - Unified Suite v3.7.1

[![Windows CI](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/ci-windows.yml/badge.svg?branch=vesper_production)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/ci-windows.yml)
[![Release Bundle](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/release-windows-x64.yml/badge.svg)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/release-windows-x64.yml)
[![Latest Release](https://img.shields.io/github/v/release/bospaladin34-crypto/Omega_standalone_developments?label=Latest%20Release&color=10b981)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest)
[![ACT-Omega Portal](https://img.shields.io/badge/Portal-act--omega--net.dev-8b5cf6)](https://act-omega-net.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-38bdf8.svg)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/blob/vesper_production/LICENSE)

**Zero-Crate `#![no_std]` Rust + Zero-NuGet Native Windows C# 5.0 + DirectCompute `cs_5_0` + ReShade 6.8 / SKSE64 World-Space Graphics + Universal C11 Edge Compute Suite**

`Omega_standalone_developments` packages all **15 ACT-Omega Standalone Lab Stations** (`00` Launcher + Stations `01..15`, including `OmegaBraidRE v1.4.0` and `OmegaEngine v1.7.7` with its hardware-verified *Skyrim Special Edition / Anniversary Edition* `1.7.104.0` + [SKSE64](https://skse.silverlock.org/) + [ReShade 6.8](https://reshade.me/) world-space shader bridge) and all **5 Omega R&D Digital Lab Tracks** into a supply-chain-proof desktop and heterogeneous edge suite that compiles from clean source using the built-in Windows `.NET Framework 4.x` compiler (`csc.exe`) and `#![no_std]` Rust (`KERNEL32.dll` only) with **zero NuGet packages, zero npm dependencies, and zero Python**.

## ACT-Omega Ecosystem & Prebuilt Releases

| Resource | Canonical Link | Notes |
| :--- | :--- | :--- |
| **ACT-Omega Web & Telemetry Portal** | [https://act-omega-net.dev/](https://act-omega-net.dev/) | Live architecture portal, documentation, and remote Sovereign Manifold surface |
| **Mainline Sovereign Manifold Repo** | [bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-](https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-) | Upstream `ACT-Omega` mainline runtime (`https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-.git`) |
| **Instant Prebuilt Windows x64 (`v3.7.1`)** | [Download Latest Prebuilt `.zip` + `SHA256SUMS.txt`](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest) | Ready-to-run Windows x64 bundle (`16` `.exe` binaries + zero-CRT `.addon64`/SKSE64 bridges, zero setup required) |

## 3-Step Source Quickstart (Any Windows 10 / Windows 11 PC)

```powershell
git clone -b vesper_production https://github.com/bospaladin34-crypto/Omega_standalone_developments.git
cd Omega_standalone_developments

# 1. Auto-detect your GPU, CPU, optional ADB devices & generate gitignored config/omega.config.json
powershell -ExecutionPolicy Bypass -File .\Init-OmegaHardware.ps1

# 2. Compile all 15 stations + #![no_std] Rust crates (36/36 tests) & launch the Master Deck
powershell -ExecutionPolicy Bypass -File .\Build-OmegaLabs.ps1 -StressAll -CreateDesktopShortcut
```

* **0 Android phones required**: Every station runs 100% locally on any Windows 10/11 PC (NVIDIA, AMD, Intel Arc, or CPU/GDI+ automatic fallback).
* **Live Skyrim AE World-Space Shader & SKSE64 Mod Builder (`15_omega_engine`)**: Run `.\15_omega_engine\OmegaEngine.exe --build-skyrim-mod` to package drag-and-drop [Vortex](https://www.nexusmods.com/vortex) (`OmegaTriEngine_SkyrimAE_SKSE64_v1.7.7_Vortex.zip`) and Game-Root ReShade (`OmegaTriEngine_SkyrimAE_RootReShade_v1.7.7.zip`) bundles with zero-CRT `OmegaTriEngine.addon64` (`vtbl[19/22/25/27/33]`), zero-CRT `OmegaSkyrimWorldBridge.dll`, ESL `.esp`, and lexer-clean `OmegaTriEngine_SkyrimAE.fx`.
* **Plug-and-Play `0..N` Android / Linux Edge Scaling**: Connect 1, 2, or more Android devices over USB or Wi-Fi ADB to unlock live screen mirroring (`:19777`), ARMv8/ARMv9/Vulkan/NPU offload (`:19788/:19789`, `:19792/:19793`), and heterogeneous split-inference (`:19791`).

## Unified 15-Station + Deck Architecture

| Station | Folder | Port | Fused R&D / Graphics Capability | Core Engine Highlights |
| :--- | :--- | :--- | :--- | :--- |
| **00. OmegaLabLauncher** | `.` (Root) | `:19800` | `00_rnd_deck` + `00_core_rust_audit` | Master 15-station launcher, tray hotkey (`Shift+F3`), live `config/omega.config.json` editor, and Chaos Torture Harness |
| **01. OmegaMirror** | `01_omega_mirror` | `:19777` | Dual-Net ADB Tunneling | Low-latency Android screen capture/scrcpy host, USB-tether/Wi-Fi failover, drag-and-drop `/sdcard/Download` bridge |
| **02. OmegaBrowser** | `02_omega_browser` | `:9778` | **Track #01** `OmegaWorkbench` | Research browser + native zlib PDF Harvester, SimHash-64 deduplication, zero-BOM/Trojan Unicode sanitizer, HTTP 206 downloader |
| **03. OmegaAV** | `03_omega_av` | `:19779` | DirectCompute `cs_5_0` Fluid/AV | Real-time 60 FPS HLSL fluid & lattice shader studio, WASAPI loopback audio reactivity, `.OMSH` live hot-reload |
| **04. OmegaSynth** | `04_omega_synth` | `:19782` | **Track #04** `OmegaSpatialRig` | E8/microtonal wavefield synthesizer fused with unit-norm SLERP quaternion spatializer, zero-GC MPE engine & `OSP4` Seqlock MMF bus |
| **05. OmegaSonar** | `05_omega_sonar` | `:19783` | **Track #04** Golay-64 Radar | Ultrasonic acoustic phased-array & IMU wand tracker with zero-sidelobe Golay-64 complementary sequence pairs (`0.42 mm` mean error) |
| **06. OmegaSheaf** | `06_omega_sheaf` | `:19784` | **Track #05** `OmegaSheafModSdk` | Cellular Sheaf `H^1` cohomology visualizer + **Pillar 6 (`F9`)**: `#[repr(C)]` ABI verifier, Quest DAG cycle detector, SIMD AOB scanner & SEH crash triage |
| **07. OmegaPenrose** | `07_omega_penrose` | `:19785` | DirectCompute Quasicrystal | De Bruijn pentagrid Penrose tiling, 3D icosahedral projection, Lenia continuous cellular automata, `.STL`/`.OBJ`/`.SVG` exporter |
| **08. OmegaSteg** | `08_omega_steg` | `:19786` | **Track #03** `OmegaVault` (`F8`) | Extended Golay `[24,12,8]` + Leech-24 `.WAV`/`.PNG` transceiver + Content-Addressed SHA-256 Vault, Seqlock WAL & 3-Node LWW CRDT |
| **09. OmegaForge** | `09_omega_forge` | `:19780` | **Track #01 & #03** Guardrails | Multi-compiler polyglot build studio (`csc`, `rustc`, `clang++`, `fxc`, `dxc`, `lean`) + Supply-Chain Lockfile/Postinstall Firewall |
| **10. OmegaShell** | `10_omega_shell` | `:19781` | **Track #01** `JobObject` Reaper | Native Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` anti-zombie sandbox, local CAS cloud, SSH/AVF bridge & polyglot code studio |
| **11. Edge Workers** | `11_omega_edge_workers` | `:19788..:19793` | **Track #02** Universal C11 + Rust | `rust_p10_worker` (ARMv9.2 `i8mm`/`dotprod` + Vulkan Subgroup/CoopMat) + `c11_universal_worker` (`omega_pocket_worker.c`) |
| **12. OmegaConductor** | `12_omega_conductor` | `:19790` | **Track #04** Binary OSC / MPE | Master suite timeline orchestrator, cross-station state cueing (`101,831.8 frm/s`), RFC Binary OSC 1.0 & 14-bit MPE modulation bus |
| **13. OmegaTensor** | `13_omega_tensor` | `:19791` | **Track #02** `OmegaPocketFusion` | Heterogeneous split-inference coordinator across PC GPU + `0..N` edge nodes, Seqlock KV failover, and OpenAI `/v1/chat/completions` |
| **14. OmegaBraidRE** | `14_omega_braid_re` | `:19794` | PE32+ / ELF64 / Mach-O / SPIR-V RE | Native x86-64/ARM64 disassembler, BraidIR SSA lifter, Reidemeister II simplification, `.pdata` unwind bounds, and Creation Engine RTTI/AOB inspector |
| **15. OmegaEngine** | `15_omega_engine` | `:19795` | Tri-Engine 60 FPS HDR + Skyrim AE Bridge | SHA-256 DXBC `cs_5_0` cache, FP16 HDR ACES bloom, Hermite Dual Contouring PBR forge, AI `.omsh` Co-Pilot, and zero-CRT ReShade 6.8 + SKSE64 world-space shader bridge (`40/40` vectors) |

## License

Released under the [MIT License](https://github.com/bospaladin34-crypto/Omega_standalone_developments/blob/vesper_production/LICENSE).
