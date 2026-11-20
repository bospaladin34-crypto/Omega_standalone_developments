# Omega Standalone Developments — Unified Suite v3.0.6

[![Windows CI](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/ci-windows.yml/badge.svg?branch=vesper_production)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/ci-windows.yml)
[![Release Bundle](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/release-windows-x64.yml/badge.svg)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/actions/workflows/release-windows-x64.yml)
[![Latest Release](https://img.shields.io/github/v/release/bospaladin34-crypto/Omega_standalone_developments?label=Latest%20Release&color=10b981)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest)
[![ACT-Omega Portal](https://img.shields.io/badge/Portal-act--omega--net.dev-8b5cf6)](https://act-omega-net.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-38bdf8.svg)](https://github.com/bospaladin34-crypto/Omega_standalone_developments/blob/vesper_production/LICENSE)

**Zero-Crate `#![no_std]` Rust + Zero-NuGet Native Windows C# 5.0 + DirectCompute `cs_5_0` + Universal C11 Edge & Reverse-Engineering Suite**

`Omega_standalone_developments` packages all **14 ACT-Omega Standalone Lab Stations** and all **5 Omega R&D Digital Lab Tracks** into a single, hardware-agnostic, supply-chain-proof desktop and heterogeneous edge suite that compiles from clean source in seconds using the built-in Windows `.NET Framework 4.x` compiler (`csc.exe`) with **zero NuGet packages, zero npm dependencies, and zero Python**.

## ACT-Omega Ecosystem & Prebuilt Releases

| Resource | Canonical Link | Notes |
| :--- | :--- | :--- |
| **ACT-Omega Web & Telemetry Portal** | [https://act-omega-net.dev/](https://act-omega-net.dev/) | Live architecture portal, documentation, and remote Sovereign Manifold surface |
| **Mainline Sovereign Manifold Repo** | [bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-](https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-) | Upstream `ACT-Omega` mainline runtime (`https://github.com/bospaladin34-crypto/Sovereign-Manifold-V30-A.P.H.U.S-.git`) |
| **Instant Prebuilt Windows x64 (`v3.0.6`)** | [Download Latest Prebuilt `.zip` + `SHA256SUMS.txt`](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest) | Ready-to-run prebuilt bundle (`14/14` signed-hash `.exe` stations + `SHA256SUMS.txt`, zero build setup required) |

## 3-Step Source Quickstart (Any Windows 10 / Windows 11 PC)

```powershell
git clone -b vesper_production https://github.com/bospaladin34-crypto/Omega_standalone_developments.git
cd Omega_standalone_developments

# 1. Auto-detect your GPU, CPU, optional ADB devices & generate gitignored config/omega.config.json
powershell -ExecutionPolicy Bypass -File .\Init-OmegaHardware.ps1

# 2. Compile all 14 stations from pure source + run #![no_std] Rust & C# stress audits + launch the Deck
powershell -ExecutionPolicy Bypass -File .\Build-OmegaLabs.ps1 -RunStressSuites -CreateDesktopShortcut
```

* **Zero-Compile Prebuilt Option**: Prefer not to run the compiler script? Grab [`Omega_Standalone_Labs_v3.0.6_win-x64_Prebuilt.zip`](https://github.com/bospaladin34-crypto/Omega_standalone_developments/releases/latest), verify its digest against `SHA256SUMS.txt`, extract anywhere, and launch `OmegaLabLauncher.exe`.
* **0 Android phones required**: Every station runs 100% locally on any Windows 10/11 PC (NVIDIA, AMD, Intel Arc, or CPU/GDI+ automatic fallback).
* **0 external DLLs required**: `02_omega_browser` automatically uses `Microsoft.Web.WebView2` if installed locally or in your NuGet cache, and seamlessly compiles a zero-DLL Native Workbench browser/PDF harvester on `:9778` otherwise.
* **Plug-and-Play `0..N` Android / Linux Edge Scaling**: Plug in 1, 2, or more Android devices over USB or Wi-Fi ADB at any time to unlock live screen mirroring (`:19777`), ARMv8/ARMv9/Vulkan/NPU worker offload (`:19788/:19789`, `:19792/:19793`), and heterogeneous split-inference (`:19791`).

## Unified 14-Station + Deck Architecture

| Station | Folder | Port | Fused R&D Capability | Core Engine Highlights |
| :--- | :--- | :--- | :--- | :--- |
| **00. OmegaLabLauncher** | `.` (Root) | `:19800` | `00_rnd_deck` + `00_core_rust_audit` | Master 14-station launcher, live `config/omega.config.json` editor, and `255,398`-trial 8-vector Chaos Torture Harness (`17.8 MB` CLI RAM) |
| **01. OmegaMirror** | `01_omega_mirror` | `:19777` | Dual-Net ADB Tunneling | Low-latency Android screen capture/scrcpy host, USB-tether/Wi-Fi failover, drag-and-drop `/sdcard/Download` bridge |
| **02. OmegaBrowser** | `02_omega_browser` | `:9778` | **Track #01** `OmegaWorkbench` | Research browser + native zlib PDF Harvester, SimHash-64 deduplication, zero-BOM/Trojan Unicode sanitizer, HTTP 206 downloader (`7.0 MB` CLI RAM) |
| **03. OmegaAV** | `03_omega_av` | `:19779` | DirectCompute `cs_5_0` Fluid/AV | Real-time 60 FPS HLSL fluid & lattice shader studio, WASAPI loopback audio reactivity, `.OMSH` live hot-reload |
| **04. OmegaSynth** | `04_omega_synth` | `:19782` | **Track #04** `OmegaSpatialRig` | E8/microtonal wavefield synthesizer fused with unit-norm SLERP quaternion spatializer, zero-GC MPE engine & `OSP4` Seqlock MMF bus (`3.1 MB` CLI RAM) |
| **05. OmegaSonar** | `05_omega_sonar` | `:19783` | **Track #04** Golay-64 Radar | Ultrasonic acoustic phased-array & IMU wand tracker with exact zero-sidelobe Golay-64 complementary sequence pairs (`0.42 mm` mean error, `3,428.5 FPS`) |
| **06. OmegaSheaf** | `06_omega_sheaf` | `:19784` | **Track #05** `OmegaSheafModSdk` | Cellular Sheaf `H^1` cohomology visualizer + **Pillar 6 (`F9`)**: Rust/C++23/C# `#[repr(C)]` ABI verifier, Quest DAG cycle detector, SIMD AOB scanner & SEH crash triage (`3.1 MB` CLI RAM) |
| **07. OmegaPenrose** | `07_omega_penrose` | `:19785` | DirectCompute Quasicrystal | De Bruijn pentagrid Penrose tiling, 3D icosahedral projection, Lenia continuous cellular automata, `.STL`/`.OBJ`/`.SVG` exporter |
| **08. OmegaSteg** | `08_omega_steg` | `:19786` | **Track #03** `OmegaVault` (`F8`) | Extended Golay `[24,12,8]` (`d_min=8`) + Leech-24 air-gap `.WAV`/`.PNG` transceiver + Content-Addressed SHA-256 Vault, Torn-Power Seqlock WAL & 3-Node LWW CRDT (`16/16` vectors) |
| **09. OmegaForge** | `09_omega_forge` | `:19780` | **Track #01 & #03** Guardrails | Multi-compiler polyglot build studio (`csc`, `rustc`, `clang++`, `fxc`, `dxc`, `lean`) + Supply-Chain Lockfile/Postinstall Firewall |
| **10. OmegaShell** | `10_omega_shell` | `:19781` | **Track #01** `JobObject` Reaper | Native Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` anti-zombie sandbox, local CAS cloud, SSH/AVF bridge & polyglot code studio (`5.8 MB` CLI RAM) |
| **11. Edge Workers** | `11_omega_edge_workers` | `:19788..:19793` | **Track #02** Universal C11 + Rust | `rust_p10_worker` (ARMv9.2 `i8mm`/`dotprod` + Vulkan Subgroup/CoopMat) + `c11_universal_worker` (`omega_pocket_worker.c` for any Android/Linux/AVF VM) |
| **12. OmegaConductor** | `12_omega_conductor` | `:19790` | **Track #04** Binary OSC / MPE | Master suite timeline orchestrator, cross-station state cueing (`101,831.8 frm/s`), RFC Binary OSC 1.0 & 14-bit MPE modulation bus |
| **13. OmegaTensor** | `13_omega_tensor` | `:19791` | **Track #02** `OmegaPocketFusion` | Heterogeneous split-inference coordinator across PC GPU + `0..N` edge nodes, Seqlock KV-cache hot-unplug failover (`0.015 ms`), and OpenAI-compatible `/v1/chat/completions` (`16/16` vectors) |
| **14. OmegaBraidRE** | `14_omega_braid_re` | `:19794` | **BraidIR + E8 + `#~` CIL** (`[Shift+F7]`) | Zero-crate PE `.pdata` unwind + `IAT`/`EAT` + `RIP` strings + ECMA-335 `#~` `TypeDef`/`MethodDef` `.NET` CIL lifter, `x86_64`/`ARM64` SSA dead-flag strip, 240-root `E8` cross-patch differ & Sheaf `#[repr(C)]` struct recovery (`13/13` vectors, `3.1 MB` CLI / `1.3 MB` trimmed GUI) |

## Customizing for Your Own Hardware (`config/omega.config.json`)

Running `.\Init-OmegaHardware.ps1` generates `config/omega.config.json` (which is gitignored so your personal machine paths and endpoints are never committed). You can also edit it directly or press **Vault -> Unified Hardware Config & R&D Chaos Harness (`:19800`)** inside `OmegaLabLauncher`:

```json
{
  "suite_version": "3.0.6",
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
* **Edge & Binary Lifter C11 Compilation**:
  * Universal C11 edge worker: `gcc -O3 -std=c11 -pthread 11_omega_edge_workers/c11_universal_worker/omega_pocket_worker.c -lm -ldl -o omega_pocket_worker`
  * Universal C11 binary lifter & differ: `gcc -O3 -std=c11 -Wall -Wextra -Werror 14_omega_braid_re/c11_cli/omega_braid_re_cli.c -lm -o omega_braid_re_cli`

## Reference Hardware Verification Benchmarks (`v3.0.6 GM`)

Verified on bare metal and GitHub Actions Windows x64 runners across a 3-device + 2-AVF-VM heterogeneous testbed (`14/14` Windows binaries + `15/15` `#![no_std]` Rust invariant tests + `307,080+` randomized stress-test trials):

| Tier | Reference Hardware | Verified Throughput & Native Footprint |
| :--- | :--- | :--- |
| **Workstation Host** | 8-Core x86_64 Laptop CPU + 6 GB DirectCompute `cs_5_0` / Vulkan 1.3 Laptop GPU (Windows 11) | `2,498.0 GOPS/s` INT8 compute; `1.3 MB - 17.8 MB` trimmed working sets (`< 70 MB` live GUI ceiling) |
| **Edge Node Slot 0** | Tensor G5 Class Android Phone (Unrooted USB-C + 5.0 GB AVF Debian 13 VM) | `5,078.6 GOPS/s` Edge TPU INT8 (`K11 Conv2D`) + `543.5 GOPS/s` Vulkan 1.4 + `509.94 GOPS/s` AVF Virtio-GPU |
| **Edge Node Slot 1** | Tensor G3 Class Android Phone (Stationary Compute Mode + 5.3 GB AVF Debian 13 VM) | `1,497.94 GOPS/s` Mali-G715 + 9-core `SMMLA` + `1,792.46 GOPS/s` 5.3 GB AVF VM (`12.30 TOPS/s` 7-tier fused pool, `0.015 ms` USB hot-unplug failover) |

## License

Released under the [MIT License](https://github.com/bospaladin34-crypto/Omega_standalone_developments/blob/vesper_production/LICENSE).
