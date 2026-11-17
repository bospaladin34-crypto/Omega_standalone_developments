# Omega Standalone Developments — Unified Suite v3.0.0

**Zero-Crate `#![no_std]` Rust + Zero-NuGet Native Windows C# 5.0 + DirectCompute `cs_5_0` + Universal C11 Edge Compute Suite**

`Omega_standalone_developments` packages all **13 ACT-Omega Standalone Lab Stations** and all **5 Omega R&D Digital Lab Tracks** into a single, hardware-agnostic, supply-chain-proof desktop and heterogeneous edge suite that compiles from clean source in seconds using the built-in Windows `.NET Framework 4.x` compiler (`csc.exe`) with **zero NuGet packages, zero npm dependencies, and zero Python**.

---

## 3-Step Quickstart (Any Windows 10 / Windows 11 PC)

```powershell
git clone -b vesper_production https://github.com/bospaladin34-crypto/Omega_standalone_developments.git
cd Omega_standalone_developments

# 1. Auto-detect your GPU, CPU, optional ADB devices & generate gitignored config/omega.config.json
powershell -ExecutionPolicy Bypass -File .\Init-OmegaHardware.ps1

# 2. Compile all stations from pure source + run #![no_std] Rust & C# stress audits + launch the Deck
powershell -ExecutionPolicy Bypass -File .\Build-OmegaLabs.ps1 -RunStressSuites -CreateDesktopShortcut
```

* **0 Android phones required**: Every station runs 100% locally on any Windows 10/11 PC (NVIDIA, AMD, Intel Arc, or CPU/GDI+ automatic fallback).
* **0 external DLLs required**: `02_omega_browser` automatically uses `Microsoft.Web.WebView2` if installed locally or in your NuGet cache, and seamlessly compiles a zero-DLL Native Workbench browser/PDF harvester on `:9778` otherwise.
* **Plug-and-Play `0..N` Android / Linux Edge Scaling**: Plug in 1, 2, or more Android devices over USB or Wi-Fi ADB at any time to unlock live screen mirroring (`:19777`), ARMv8/ARMv9/Vulkan/NPU worker offload (`:19788/:19789`, `:19792/:19793`), and heterogeneous split-inference (`:19791`).

---

## Unified 13-Station + Deck Architecture (18-to-13 Fusion Map)

| Station | Folder | Port | Fused R&D Capability | Core Engine Highlights |
| :--- | :--- | :--- | :--- | :--- |
| **00. OmegaLabLauncher** | `.` (Root) | `:19800` | `00_rnd_deck` + `00_core_rust_audit` | Master 13-station launcher, live `config/omega.config.json` editor, and `255,398`-trial 8-vector Chaos Torture Harness |
| **01. OmegaMirror** | `01_omega_mirror` | `:19777` | Dual-Net ADB Tunneling | Low-latency Android screen capture/scrcpy host, USB-tether/Wi-Fi failover, drag-and-drop `/sdcard/Download` bridge |
| **02. OmegaBrowser** | `02_omega_browser` | `:9778` | **Track #01** `OmegaWorkbench` | Research browser + native zlib PDF Harvester, SimHash-64 deduplication, zero-BOM/Trojan Unicode sanitizer, HTTP 206 downloader |
| **03. OmegaAV** | `03_omega_av` | `:19779` | DirectCompute `cs_5_0` Fluid/AV | Real-time 60 FPS HLSL fluid & lattice shader studio, WASAPI loopback audio reactivity, `.OMSH` live hot-reload |
| **04. OmegaSynth** | `04_omega_synth` | `:19782` | **Track #04** `OmegaSpatialRig` | E8/microtonal wavefield synthesizer fused with unit-norm SLERP quaternion spatializer, zero-GC MPE engine & Seqlock MMF bus |
| **05. OmegaSonar** | `05_omega_sonar` | `:19783` | **Track #04** Golay-64 Radar | Ultrasonic acoustic phased-array & IMU wand tracker with exact zero-sidelobe Golay-64 complementary sequence pairs |
| **06. OmegaSheaf** | `06_omega_sheaf` | `:19784` | **Track #05** `OmegaSheafModSdk` | Cellular Sheaf `H^1` cohomology visualizer + **Pillar 6 (`F9`)**: Rust/C++23/C# `#[repr(C)]` ABI verifier, Quest DAG cycle detector, SIMD AOB scanner & SEH crash triage |
| **07. OmegaPenrose** | `07_omega_penrose` | `:19785` | DirectCompute Quasicrystal | De Bruijn pentagrid Penrose tiling, 3D icosahedral projection, Lenia continuous cellular automata, `.STL`/`.OBJ`/`.SVG` exporter |
| **08. OmegaSteg** | `08_omega_steg` | `:19786` | **Track #03** `OmegaVault` (`F8`) | Extended Golay `[24,12,8]` (`d_min=8`) + Leech-24 air-gap `.WAV`/`.PNG` transceiver + Content-Addressed SHA-256 Vault, Torn-Power Seqlock WAL & 3-Node LWW CRDT |
| **09. OmegaForge** | `09_omega_forge` | `:19780` | **Track #01 & #03** Guardrails | Multi-compiler polyglot build studio (`csc`, `rustc`, `clang++`, `fxc`, `dxc`, `lean`) + Supply-Chain Lockfile/Postinstall Firewall |
| **10. OmegaShell** | `10_omega_shell` | `:19781` | **Track #01** `JobObject` Reaper | Native Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` anti-zombie sandbox, local CAS cloud, SSH/AVF bridge & polyglot code studio |
| **11. Edge Workers** | `11_omega_edge_workers` | `:19788..:19793` | **Track #02** Universal C11 + Rust | `rust_p10_worker` (ARMv9.2 `i8mm`/`dotprod` + Vulkan Subgroup/CoopMat) + `c11_universal_worker` (`omega_pocket_worker.c` for any Android/Linux/AVF VM) |
| **12. OmegaConductor** | `12_omega_conductor` | `:19790` | **Track #04** Binary OSC / MPE | Master suite timeline orchestrator, cross-station state cueing, RFC Binary OSC 1.0 & 14-bit MPE modulation bus |
| **13. OmegaTensor** | `13_omega_tensor` | `:19791` | **Track #02** `OmegaPocketFusion` | Heterogeneous split-inference coordinator across PC GPU + `0..N` edge nodes, Seqlock KV-cache hot-unplug failover, and OpenAI-compatible `/v1/chat/completions` |

---

## Customizing for Your Own Hardware (`config/omega.config.json`)

Running `.\Init-OmegaHardware.ps1` generates `config/omega.config.json` (which is gitignored so your personal machine paths and endpoints are never committed). You can also edit it directly or press **Vault -> Unified Hardware Config & R&D Chaos Harness (`:19800`)** inside `OmegaLabLauncher`:

```json
{
  "suite_version": "3.0.0",
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
* **Edge Worker Compilation (`11_omega_edge_workers`)**:
  * Universal C11 worker (Linux / Android NDK / Debian AVF VM): `gcc -O3 -std=c11 -pthread 11_omega_edge_workers/c11_universal_worker/omega_pocket_worker.c -lm -ldl -o omega_pocket_worker`
  * Rust ARMv9/x64 Vulkan worker: `powershell -File 11_omega_edge_workers/rust_p10_worker/Deploy-And-Run-Pixel10Worker.ps1 -Action HostOnly`

---

## Reference Hardware Verification Benchmarks

Verified on bare metal across a 3-device heterogeneous testbed (`14/14` Windows binaries + `10/10` `#![no_std]` Rust invariant tests + `275,444` randomized stress-test trials):

| Tier | Reference Hardware | Verified Throughput & Native Footprint |
| :--- | :--- | :--- |
| **Workstation Host** | 8-Core x86_64 Laptop CPU + 6 GB DirectCompute `cs_5_0` / Vulkan 1.3 Laptop GPU (Windows 11) | `2,498.0 GOPS/s` INT8 compute; `< 70 MB` native GUI working sets (`24.9 MB` Vault GUI / `3.0 MB` CLI; `40.5 MB` Fusion GUI / `2.9 MB` CLI) |
| **Edge Node Slot 0** | Tensor G5 Class Android Phone (Unrooted USB-C + 5.0 GB AVF Debian 13 VM) | `5,078.6 GOPS/s` Edge TPU INT8 (`K11 Conv2D`) + `543.5 GOPS/s` Vulkan 1.4 + `509.9 GOPS/s` AVF Virtio-GPU |
| **Edge Node Slot 1** | Tensor G3 Class Android Phone (Stationary Compute Mode + 5.3 GB AVF Debian 13 VM) | `1,014.5 GFLOPS/s` Mali-G715 `Vec4 FMA` + `685.2 GOPS/s` `OpUDotAccSat` + `826.3 GOPS/s` 9-core `SMMLA` (`0.015 ms` USB hot-unplug failover) |

---

## License

Released under the [MIT License](LICENSE).
