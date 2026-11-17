# Track 03: Supply-Chain-Proof Zero-Crate Local-First Vault & Sync

- **Reserved Loopback Port:** `127.0.0.1:19803`
- **Google Task Tracker:** [Track 03: Supply-Chain-Proof Zero-Crate Local-First Vault & Sync](https://tasks.google.com/task/xIFgohFDDiumWn_o?sa=DLSA_GEMINI)
- **Mandate:** Strict Zero-Python, UTF-8 No-BOM, Sub-70MB Native Working-Set, Fail-Closed Telemetry.

## Core Engineering & Edge-Case Targets

- Eliminate npm/PyPI transitive supply-chain exposure via zero-crate #![no_std] Rust & zero-NuGet C# primitives.
- Prevent power-loss torn files using dual-slot Seqlock + IEEE CRC-32 write-ahead journals.
- Heal noisy offline/air-gap sync frames with Extended Golay [24,12,8] 3-bit ECC and commutative CRDT merges.
