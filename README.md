# Omega Standalone Developments - Unified Suite v3.9.0

[![Windows CI](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/ci-windows.yml/badge.svg?branch=vesper_production)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/ci-windows.yml)
[![Release Bundle](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/release-windows-x64.yml/badge.svg)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/release-windows-x64.yml)
[![Zero-Crate Rust](https://img.shields.io/badge/Rust-%23!%5Bno__std%5D%20Zero--Crate-f97316)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/tree/vesper_production/00_core_rust_audit)
[![ACT-Omega Portal](https://img.shields.io/badge/Portal-act--omega--net.dev-8b5cf6)](https://act-omega-net.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-38bdf8.svg)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/blob/vesper_production/LICENSE)

**Zero-Crate `#![no_std]` Rust + Zero-NuGet Native Windows C# 5.0 + DirectCompute `cs_5_0` + ReShade 6.8 / SKSE64 World-Space Graphics + Universal C11 Edge Compute Suite**

`Omega_standalone_developments` packages all **15 ACT-Omega Standalone Lab Stations** (`00` Launcher + Stations `01..15`, including `OmegaBraidRE v1.4.0` and `OmegaEngine v1.9.0` with its hardware-verified *Skyrim Special Edition / Anniversary Edition* `1.7.104.0` + [SKSE64](https://skse.silverlock.org/) + [ReShade 6.8](https://reshade.me/) Phase 2 Dark-Fantasy / Dreamcore / Nihilist multi-pass HDR shader & 28 px real-time diagnostics bar) and all **5 Omega R&D Digital Lab Tracks** into a hardware-agnostic, supply-chain-proof desktop and heterogeneous edge suite that compiles from clean source in seconds using the built-in Windows `.NET Framework 4.x` compiler (`csc.exe`) and `#![no_std]` Rust (`KERNEL32.dll` only) with **zero NuGet packages, zero npm dependencies, and zero Python**.

## ACT-Omega Ecosystem & Prebuilt Releases

| Resource | Link | Description |
| :--- | :--- | :--- |
| **ACT-Omega Web & Telemetry Portal** | [https://act-omega-net.dev/](https://act-omega-net.dev/) | Live architecture portal, documentation, and remote Sovereign Manifold surface |
| **Mainline Sovereign Manifold Repo** | [bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-](https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-) | Upstream `ACT-Omega` mainline runtime (`https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-.git`) |
| **Instant Prebuilt Windows x64 (`v3.9.0`)** | [Download Latest Prebuilt `.zip` + `SHA256SUMS.txt`](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest) | Ready-to-run Windows x64 bundle (`16` signed-hash `.exe` binaries + zero-CRT `.addon64`/SKSE64 bridges + `SHA256SUMS.txt`) |

## 3-Step Source Quickstart (Any Windows 10 / Windows 11 PC)

```powershell
git clone https://github.com/bospaladin34-crypto/Omega_standalone_developments.git
cd Omega_standalone_developments

# 1. Auto-detect your GPU, CPU, optional ADB devices & generate gitignored config/omega.config.json
powershell -ExecutionPolicy Bypass -File .\Init-OmegaHardware.ps1

# 2. Compile all 15 stations + #![no_std] Rust crates (36/36 tests) + run all 13 headless stress suites & launch Deck
powershell -ExecutionPolicy Bypass -File .\Build-OmegaLabs.ps1 -RunStressSuites -CreateDesktopShortcut
```

* **Zero-Compile Prebuilt Option**: Prefer not to run the compiler script? Grab [`Omega_Standalone_Labs_v3.9.0_win-x64_Prebuilt.zip`](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest), verify its digest against `SHA256SUMS.txt`, extract anywhere, and launch `OmegaLabLauncher.exe`.
* **0 Android phones required**: Every station runs 100% locally on any Windows 10/11 PC (NVIDIA, AMD, Intel Arc, or CPU/GDI+ automatic fallback).
* **0 external DLLs required**: `02_omega_browser` automatically uses `Microsoft.Web.WebView2` if installed locally or in your NuGet cache, and seamlessly compiles a zero-DLL Native Workbench browser/PDF harvester on `:9778` otherwise.
* **Live Skyrim AE Phase 2 World-Space Shader, 28 px Top Diagnostics Bar & SKSE64 Mod Builder (`15_omega_engine`)**: Run `.\15_omega_engine\OmegaEngine.exe --build-skyrim-mod` (or `--top-bar` for the `WS_EX_NOACTIVATE` desktop overlay) to package drag-and-drop [Vortex](https://www.nexusmods.com/vortex) (`OmegaTriEngine_SkyrimAE_SKSE64_v1.9.0_Vortex.zip`) and Game-Root ReShade (`OmegaTriEngine_SkyrimAE_RootReShade_v1.9.0.zip`) bundles with zero-CRT `OmegaTriEngine.addon64` (`vtbl[19/22/25/27/33]`), zero-CRT `OmegaSkyrimWorldBridge.dll`, ESL `.esp`, 28 px HUD (`Alt+F2` / `F10`), 250 ms Menu Auto-Fade (`Alt+F6`), and 4 live shader personas (`Alt+F8`: Classic Sovngarde, Obsidian Dark Fantasy, Liminal Dreamcore, Stark Nihilist Monolith) + half-res `RGBA16F` Dual-Kawase HDR bloom & bilateral godrays.
* **Plug-and-Play `0..N` Android / Linux Edge Scaling**: Plug in 1, 2, or more Android devices over USB or Wi-Fi ADB at any time to unlock live screen mirroring (`:19777`), ARMv8/ARMv9/Vulkan/NPU worker offload (`:19788/:19789`, `:19792/:19793`), and heterogeneous split-inference (`:19791`). Ephemeral OpenSSH Ed25519 keys are generated exclusively outside the repository in `%LOCALAPPDATA%\OmegaAvfSsh`.

## Unified 15-Station + Deck Architecture

| Station | Folder | Port | Fused R&D / Graphics Capability | Core Engine Highlights |
| :--- | :--- | :--- | :--- | :--- |
| **00. OmegaLabLauncher** | `.` (Root) | `:19800` | `00_rnd_deck` + `00_core_rust_audit` | Master 15-station launcher, tray hotkey (`Shift+F3`), live `config/omega.config.json` editor, and `255,398`-trial 8-vector Chaos Torture Harness (`18.2 MB` CLI RAM) |
| **01. OmegaMirror** | `01_omega_mirror` | `:19777` | Dual-Net ADB Tunneling | Low-latency Android screen capture/scrcpy host, USB-tether/Wi-Fi failover, drag-and-drop `/sdcard/Download` bridge |
| **02. OmegaBrowser** | `02_omega_browser` | `:9778` | **Track #01** `OmegaWorkbench` | Research browser + native zlib PDF Harvester, SimHash-64 deduplication, zero-BOM/Trojan Unicode sanitizer, HTTP 206 downloader (`9.3 MB` CLI RAM) |
| **03. OmegaAV** | `03_omega_av` | `:19779` | DirectCompute `cs_5_0` Fluid/AV | Real-time 60 FPS HLSL fluid & lattice shader studio, WASAPI loopback audio reactivity, `.OMSH` live hot-reload |
| **04. OmegaSynth** | `04_omega_synth` | `:19782` | **Track #04** `OmegaSpatialRig` | E8/microtonal wavefield synthesizer fused with unit-norm SLERP quaternion spatializer, zero-GC MPE engine & `OSP4` Seqlock MMF bus (`3.1 MB` CLI RAM) |
| **05. OmegaSonar** | `05_omega_sonar` | `:19783` | **Track #04** Golay-64 Radar | Ultrasonic acoustic phased-array & IMU wand tracker with exact zero-sidelobe Golay-64 complementary sequence pairs (`0.42 mm` mean error, `3,372.6 FPS`) |
| **06. OmegaSheaf** | `06_omega_sheaf` | `:19784` | **Track #05** `OmegaSheafModSdk` | Cellular Sheaf `H^1` cohomology visualizer + **Pillar 6 (`F9`)**: Rust/C++23/C# `#[repr(C)]` ABI verifier, Quest DAG cycle detector, SIMD AOB scanner & SEH crash triage (`3.2 MB` CLI RAM) |
| **07. OmegaPenrose** | `07_omega_penrose` | `:19785` | DirectCompute Quasicrystal | De Bruijn pentagrid Penrose tiling, 3D icosahedral projection, Lenia continuous cellular automata, `.STL`/`.OBJ`/`.SVG` exporter |
| **08. OmegaSteg** | `08_omega_steg` | `:19786` | **Track #03** `OmegaVault` (`F8`) | Extended Golay `[24,12,8]` (`d_min=8`) + Leech-24 air-gap `.WAV`/`.PNG` transceiver + Content-Addressed SHA-256 Vault, Torn-Power Seqlock WAL & 3-Node LWW CRDT (`16/16` vectors) |
| **09. OmegaForge** | `09_omega_forge` | `:19780` | **Track #01 & #03** Guardrails | Multi-compiler polyglot build studio (`csc`, `rustc`, `clang++`, `fxc`, `dxc`, `lean`) + Supply-Chain Lockfile/Postinstall Firewall |
| **10. OmegaShell** | `10_omega_shell` | `:19781` | **Track #01** `JobObject` Reaper | Native Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` anti-zombie sandbox, local CAS cloud, `%LOCALAPPDATA%\OmegaAvfSsh` AVF bridge & polyglot code studio (`8.0 MB` CLI RAM) |
| **11. Edge Workers** | `11_omega_edge_workers` | `:19788..:19793` | **Track #02** Universal C11 + Rust | `rust_p10_worker` (ARMv9.2 `i8mm`/`dotprod` + Vulkan Subgroup/CoopMat) + `c11_universal_worker` (`omega_pocket_worker.c` for any Android/Linux/AVF VM) |
| **12. OmegaConductor** | `12_omega_conductor` | `:19790` | **Track #04** Binary OSC / MPE | Master suite timeline orchestrator, cross-station state cueing (`101,831.8 frm/s`), RFC Binary OSC 1.0 & 14-bit MPE modulation bus |
| **13. OmegaTensor** | `13_omega_tensor` | `:19791` | **Track #02** `OmegaPocketFusion` | Heterogeneous split-inference coordinator across PC GPU + `0..N` edge nodes, Seqlock KV-cache hot-unplug failover (`0.015 ms`), and OpenAI-compatible `/v1/chat/completions` (`16/16` vectors) |
| **14. OmegaBraidRE** | `14_omega_braid_re` | `:19794` | **BraidIR + E8 + `#~` CIL** (`[Shift+F7]`) | Zero-crate PE `.pdata` unwind + `IAT`/`EAT` + `RIP` strings + ECMA-335 `#~` `TypeDef`/`MethodDef` `.NET` CIL lifter, `x86_64`/`ARM64` SSA dead-flag strip, 240-root `E8` cross-patch differ & PDB/SKSE64 trampoline codegen (`18/18` vectors, `3.2 MB` CLI RAM) |
| **15. OmegaEngine** | `15_omega_engine` | `:19795` | **Tri-Engine 60 FPS HDR + Skyrim AE v1.9.0** | SHA-256 DXBC `cs_5_0` cache (`0` FXC calls on warm boot), FP16 Dual-Kawase HDR bloom, Hermite Dual Contouring 2K PBR forge, 512 B `OTRI v1.9` (`U0..U27`) bus, 28 px Top Diagnostics Bar, 4 Dark-Fantasy/Dreamcore/Nihilist personas, and zero-CRT ReShade 6.8 + SKSE64 world-space bridge (`44/44` vectors, `1.30 MB` CLI RAM) |

## Customizing for Your Own Hardware (`config/omega.config.json`)

Running `.\Init-OmegaHardware.ps1` generates `config/omega.config.json` (which is gitignored so your personal machine paths and endpoints are never committed). You can also edit it directly or press **Vault -> Unified Hardware Config & R&D Chaos Harness (`:19800`)** inside `OmegaLabLauncher`:

```json
{
  "suite_version": "3.9.0",
  "host_alias": "my-workstation",
  "gpu_label": "AUTO_DETECT_DXGI",
  "downloads_dir": "downloads",
  "corpus_dir": "corpus",
  "cloud_bridge_dir": "bridge_export",
  "external_project_dir": "workspace",
  "local_llm_endpoint": "http://127.0.0.1:11434",
  "openai_compat_endpoint": "http://127.0.0.1:19791/v1/chat/completions",
  "adb_discovery": "auto"
}
```

* **Local AI / LLM Routing**: Point `local_llm_endpoint` at Ollama (`http://127.0.0.1:11434`), `llama-server`, LM Studio, or `OmegaTensor`'s built-in OpenAI-compatible endpoint (`http://127.0.0.1:19791/v1/chat/completions`).
* **Edge, Binary Lifter & Zero-CRT Skyrim AE Bridge Compilation**:
  * Universal C11 edge worker: `gcc -O3 -std=c11 -pthread 11_omega_edge_workers/c11_universal_worker/omega_pocket_worker.c -lm -ldl -o omega_pocket_worker`
  * Universal C11 binary lifter & differ: `gcc -O3 -std=c11 -Wall -Wextra -Werror 14_omega_braid_re/c11_cli/omega_braid_re_cli.c -lm -o omega_braid_re_cli`
  * Zero-CRT `#![no_std]` ReShade 6.8 & SKSE64 bridges (`KERNEL32.dll` only): `cargo build --release --manifest-path 15_omega_engine/reshade_addon/Cargo.toml` and `cargo build --release --manifest-path 15_omega_engine/skse64_bridge/Cargo.toml`

## Reference Hardware Verification Benchmarks (`v3.9.0`)

Verified on bare metal and GitHub Actions Windows x64 runners across a 3-device + 2-AVF-VM heterogeneous testbed (`16/16` Windows `.exe` binaries + `4/4` zero-CRT `#![no_std]` Win64 DLLs + `36/36` `#![no_std]` Rust invariant tests + `6,560,000+` randomized stress-test trials):

| Tier | Reference Hardware | Verified Throughput & Native Footprint |
| :--- | :--- | :--- |
| **Workstation Host** | 8-Core x86_64 Laptop CPU + 6 GB DirectCompute `cs_5_0` / Vulkan 1.3 Laptop GPU (Windows 11) | `2,498.0 GOPS/s` INT8 compute; `1.29 MB - 18.2 MB` trimmed CLI working sets (`< 70 MB` live GUI ceiling) |
| **Edge Node Slot 0** | Tensor G5 Class Android Phone (Unrooted USB-C + 5.0 GB AVF Debian 13 VM) | `5,078.6 GOPS/s` Edge TPU INT8 (`K11 Conv2D`) + `543.5 GOPS/s` Vulkan 1.4 + `509.94 GOPS/s` AVF Virtio-GPU |
| **Edge Node Slot 1** | Tensor G3 Class Android Phone (Stationary Compute Mode + 5.3 GB AVF Debian 13 VM) | `1,497.94 GOPS/s` Mali-G715 + 9-core `SMMLA` + `1,792.46 GOPS/s` 5.3 GB AVF VM (`12.30 TOPS/s` 7-tier fused pool, `0.015 ms` USB hot-unplug failover) |

### Headless Stress-Suite & `#![no_std]` Rust Audit Ledger

| Audited Subsystem | Vectors / Tests | Trials | Trimmed CLI RAM / Key Telemetry |
| :--- | :--- | :--- | :--- |
| **`00_core_rust_audit` + `rust_braid_re_core` + `rust_tri_engine_core` + `rust_skse64_core`** | `36/36 PASS` | Deterministic + Fuzz | Zero external crates (`#![no_std]`), zero MSVC CRT (`Imports = KERNEL32.dll` ONLY) |
| **`00. OmegaLabLauncher (:19800 Chaos Harness)`** | `8/8 PASS` | `255,398` | `18.2 MB` RAM; Seqlock WAL + LWW CRDT + JobObject isolation |
| **`01. OmegaMirror` & `09. OmegaForge+Firewall`** | `Self-Test PASS` | Deterministic | Dual-ADB tunnel probe + `4/4` supply-chain postinstall/lockfile blocks |
| **`02. OmegaBrowser+Workbench (:9778)`** | `8/8 PASS` | `3,009` | `9.3 MB` RAM; native zlib PDF stream harvester + SimHash-64 + bidi/Trojan strip |
| **`04. OmegaSynth+Spatial (:19782)`** | `8/8 PASS` | `6,913` | `3.1 MB` RAM; unit-quaternion SLERP + zero-GC MPE + `OSP4` Seqlock bus |
| **`05. OmegaSonar (:19783)`** | `8/8 PASS` | `2,735` | `0.42 mm` phased-array mean error (`3,372.6 FPS` Golay-64 correlator) |
| **`06. OmegaSheaf+Surgeon (:19784)`** | `8/8 PASS` | `3,073` | `3.2 MB` RAM; `H^1 = 0` quest cycle check + `#[repr(C)]` ABI layout verifier |
| **`08. OmegaSteg+Vault (:19786)`** | `16/16 PASS` | `7,981` | `SAC = 50.15%`, `H = 7.988 bits/byte`, Golay `[24,12,8]` 3-bit ECC + CAS Vault |
| **`10. OmegaShell+Sandbox (:19781)`** | `8/8 PASS` | `3,009` | `8.0 MB` RAM; Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` + out-of-repo SSH hygiene |
| **`12. OmegaConductor (:19790)`** | `8/8 PASS` | `6,816` | `91,412 - 101,831 frm/s` Binary OSC 1.0 + 14-bit MPE cue bus |
| **`13. OmegaTensor+Pocket (:19791)`** | `16/16 PASS` | `6,081` | `E8 SQNR = 22.8 dB`, `Golay = 2,325/2,325`, `12.30 TOPS/s` heterogeneous pool |
| **`14. OmegaBraidRE v1.4.0 (:19794)`** | `18/18 PASS` | `5,986` | `3.2 MB` RAM; PE32+/ELF64/CIL `#~`/PDB MSF 7.00 lifter + SKSE64 trampoline codegen |
| **`15. OmegaEngine v1.9.0 (:19795 + Skyrim AE Phase 2)`** | `44/44 PASS` | `6,255,146` | `1.30 MB` trimmed CLI RAM; `15` `.dxbc` blobs in `5.68 ms`, `512 B` `OTRI v1.9` bus (`U0..U27`), 28 px Top Bar, 4 Dark-Fantasy/Dreamcore/Nihilist personas, multi-pass `RGBA16F` HDR bloom & godrays |

## License

Released under the [MIT License](LICENSE).
