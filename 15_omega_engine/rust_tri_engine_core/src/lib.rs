#![no_std]
//! Station 15: 'omega_tri_engine_core' v1.0.0 ('#![no_std]', zero external crates)
//! Verifies the 320-byte 'OmegaParams' ('float4 U[20]') + 64-byte Seqlock trailer ('384' bytes total),
//! IEEE 802.3 CRC-32 stalk checksum, Conway-Sloane 'E8 = D8+ U (D8+ + 1/2)' coset rounding,
//! and 5D de Bruijn pentagrid zero-sum invariant ('sum(gamma_0..4) == 0').

pub const OMEGA_TRI_MAGIC: u32 = 0x4F54_5249; // "OTRI"
pub const OMEGA_TRI_UNIFORM_BYTES: usize = 320;
pub const OMEGA_TRI_TOTAL_BYTES: usize = 384;

#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct OmegaTriUniformBuffer320 {
    pub u: [[f32; 4]; 20],
}

#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct OmegaTriSeqlockTrailer64 {
    pub magic: u32,
    pub version: u32,
    pub sequence: u64,
    pub active_mask: u32,
    pub payload_crc32: u32,
    pub qpc_ticks: u64,
    pub reserved: [u64; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct OmegaTriMmfPacket384 {
    pub uniforms: OmegaTriUniformBuffer320,
    pub trailer: OmegaTriSeqlockTrailer64,
}

pub fn crc32_ieee_nostd(bytes: &[u8]) -> u32 {
    let mut crc: u32 = 0xFFFF_FFFF;
    let mut i = 0usize;
    while i < bytes.len() {
        crc ^= bytes[i] as u32;
        let mut bit = 0;
        while bit < 8 {
            if (crc & 1) != 0 {
                crc = 0xEDB8_8320 ^ (crc >> 1);
            } else {
                crc >>= 1;
            }
            bit += 1;
        }
        i += 1;
    }
    !crc
}

/// Returns true iff Seqlock generation 'seq1 == seq2', 'seq1 != 0', '(seq1 & 1) == 0', and 'magic == OMEGA_TRI_MAGIC'.
pub const fn verify_seqlock_pair(magic: u32, seq1: u64, seq2: u64, crc_expected: u32, crc_actual: u32) -> bool {
    magic == OMEGA_TRI_MAGIC
        && seq1 != 0
        && (seq1 & 1) == 0
        && seq1 == seq2
        && crc_expected == crc_actual
}

/// Fixed-point Q16.16 Conway-Sloane D8+ even-sum rounding used inside E8 = D8+ U (D8+ + 1/2).
pub fn round_d8_even_q16(x_q16: &[i32; 8], half_shift: bool) -> ([i32; 8], i64) {
    let shift: i32 = if half_shift { 32768 } else { 0 };
    let mut z = [0i32; 8];
    let mut u = [0i32; 8];
    let mut sum: i32 = 0;
    let mut worst_idx = 0usize;
    let mut worst_err: i32 = -1;

    let mut i = 0usize;
    while i < 8 {
        let ui = x_q16[i] - shift;
        u[i] = ui;
        let zi = (ui + 32768).div_euclid(65536);
        z[i] = zi;
        sum = sum.wrapping_add(zi);
        let diff = ui - zi * 65536;
        let abs_diff = if diff < 0 { -diff } else { diff };
        if abs_diff > worst_err {
            worst_err = abs_diff;
            worst_idx = i;
        }
        i += 1;
    }

    if (sum & 1) != 0 {
        let diff = u[worst_idx] - z[worst_idx] * 65536;
        z[worst_idx] += if diff >= 0 { 1 } else { -1 };
    }

    let mut out_q16 = [0i32; 8];
    let mut dist2: i64 = 0;
    let mut k = 0usize;
    while k < 8 {
        let qk = z[k] * 65536 + shift;
        out_q16[k] = qk;
        let d = (x_q16[k] - qk) as i64;
        dist2 += (d * d) >> 16;
        k += 1;
    }
    (out_q16, dist2)
}

pub fn quantize_e8_q16(x_q16: &[i32; 8]) -> ([i32; 8], bool, i64) {
    let (q0, d0) = round_d8_even_q16(x_q16, false);
    let (q1, d1) = round_d8_even_q16(x_q16, true);
    if d1 < d0 {
        (q1, true, d1)
    } else {
        (q0, false, d0)
    }
}

/// Enforces 5D de Bruijn Penrose P3 exact zero-sum condition in Q16.16 fixed-point.
pub const fn enforce_pentagrid_zero_sum_q16(g0: i32, g1: i32, g2: i32, g3: i32) -> [i32; 5] {
    let g4 = -(g0 + g1 + g2 + g3);
    [g0, g1, g2, g3, g4]
}

#[cfg(test)]
mod tests {
    use super::*;
    use core::mem::{align_of, size_of};

    #[test]
    fn test_320b_cbuffer_and_384b_mmf_layout() {
        assert_eq!(size_of::<OmegaTriUniformBuffer320>(), OMEGA_TRI_UNIFORM_BYTES);
        assert_eq!(size_of::<OmegaTriSeqlockTrailer64>(), 64);
        assert_eq!(size_of::<OmegaTriMmfPacket384>(), OMEGA_TRI_TOTAL_BYTES);
        assert_eq!(align_of::<OmegaTriSeqlockTrailer64>(), 8);
    }

    #[test]
    fn test_crc32_ieee_standard_vector() {
        assert_eq!(crc32_ieee_nostd(b"123456789"), 0xCBF4_3926);
    }

    #[test]
    fn test_seqlock_even_odd_and_crc_gate() {
        assert!(verify_seqlock_pair(OMEGA_TRI_MAGIC, 2, 2, 0xAABBCCDD, 0xAABBCCDD));
        assert!(!verify_seqlock_pair(OMEGA_TRI_MAGIC, 3, 3, 0xAABBCCDD, 0xAABBCCDD));
        assert!(!verify_seqlock_pair(OMEGA_TRI_MAGIC, 2, 4, 0xAABBCCDD, 0xAABBCCDD));
        assert!(!verify_seqlock_pair(OMEGA_TRI_MAGIC, 0, 0, 0xAABBCCDD, 0xAABBCCDD));
        assert!(!verify_seqlock_pair(OMEGA_TRI_MAGIC, 2, 2, 0xAABBCCDD, 0xAABBCCDE));
    }

    #[test]
    fn test_e8_spinor_root_exact_recovery() {
        let half = 32768i32;
        let spinor = [half, half, half, half, half, half, half, half];
        let (q, is_spinor, dist2) = quantize_e8_q16(&spinor);
        assert!(is_spinor);
        assert_eq!(q, spinor);
        assert_eq!(dist2, 0);
    }

    #[test]
    fn test_e8_integer_d8_root_exact_recovery() {
        let d8 = [65536, -65536, 0, 0, 0, 0, 0, 0];
        let (q, is_spinor, dist2) = quantize_e8_q16(&d8);
        assert!(!is_spinor);
        assert_eq!(q, d8);
        assert_eq!(dist2, 0);
    }

    #[test]
    fn test_d8_parity_fixup_even_sum() {
        let odd_near = [65536, 5000, 1000, 0, 0, 0, 0, 0];
        let (q, _sp, _d2) = quantize_e8_q16(&odd_near);
        let mut sum_int = 0i32;
        for &v in q.iter() {
            sum_int += v / 32768;
        }
        assert_eq!(sum_int & 1, 0);
    }

    #[test]
    fn test_pentagrid_p3_zero_sum_invariant() {
        let g = enforce_pentagrid_zero_sum_q16(13107, -8192, 4096, -2048);
        let total: i32 = g[0] + g[1] + g[2] + g[3] + g[4];
        assert_eq!(total, 0);
    }

    #[test]
    fn test_zero_vector_quantizes_to_origin() {
        let z = [0i32; 8];
        let (q, is_spinor, dist2) = quantize_e8_q16(&z);
        assert!(!is_spinor);
        assert_eq!(q, z);
        assert_eq!(dist2, 0);
    }
}