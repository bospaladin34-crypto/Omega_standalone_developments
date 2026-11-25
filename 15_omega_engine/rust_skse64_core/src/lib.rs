#![no_std]
//! Station 15 Task #3: 'omega_skyrim_world_bridge_core' v1.7.7 ('#![no_std]', zero external crates)
//! Verifies the 848-byte ('0x350') 'SksePluginVersionData' binary contract, packed SKSE runtime
//! version algebra ('1.6.1170' & '1.7.104'), 'OmegaBraidRE' RIP-relative '[disp32]' instruction
//! target resolution, Skyrim Z-up -> DirectX Y-up camera basis isometry, and Cellular Sheaf
//! 'H^1(X; F) = 0' quest coboundary verification.

#[repr(C)]
#[derive(Clone, Copy)]
pub struct SksePluginVersionDataLayout {
    pub data_version: u32,
    pub plugin_version: u32,
    pub name: [u8; 256],
    pub author: [u8; 256],
    pub support_email: [u8; 252],
    pub version_independence_ex: u32,
    pub version_independence: u32,
    pub compatible_versions: [u32; 16],
    pub se_version_required: u32,
}

pub const fn pack_skyrim_version(major: u32, minor: u32, build: u32, sub: u32) -> u32 {
    ((major & 0xFF) << 24) | ((minor & 0xFF) << 16) | ((build & 0xFFF) << 4) | (sub & 0xF)
}

pub fn resolve_rip_disp32(buf: &[u8], base_va: usize, pat: &[u8], mask: &[u8], disp_off: usize, instr_len: usize) -> Option<usize> {
    if pat.is_empty() || pat.len() != mask.len() || buf.len() < pat.len() || disp_off + 4 > instr_len {
        return None;
    }
    let limit = buf.len() - pat.len();
    let mut i = 0usize;
    while i <= limit {
        let mut matched = true;
        let mut k = 0usize;
        while k < pat.len() {
            if mask[k] != 0 && buf[i + k] != pat[k] {
                matched = false;
                break;
            }
            k += 1;
        }
        if matched {
            if i + instr_len > buf.len() {
                return None;
            }
            let d0 = buf[i + disp_off] as u32;
            let d1 = buf[i + disp_off + 1] as u32;
            let d2 = buf[i + disp_off + 2] as u32;
            let d3 = buf[i + disp_off + 3] as u32;
            let disp = (d0 | (d1 << 8) | (d2 << 16) | (d3 << 24)) as i32;
            let next_ip = base_va.wrapping_add(i + instr_len);
            return Some((next_ip as isize).wrapping_add(disp as isize) as usize);
        }
        i += 1;
    }
    None
}

pub fn evaluate_sheaf_coboundary_h1(stage_mask: u32, edges: &[u16]) -> u32 {
    let mut defect = 0u32;
    let mut i = 0usize;
    while i < edges.len() {
        let e = edges[i];
        let u = (stage_mask >> ((e & 0x0F) as u32)) & 1;
        let w = (stage_mask >> (((e >> 4) & 0x0F) as u32)) & 1;
        let rel = (e >> 8) & 0x0F;
        if rel == 0 && w == 1 && u == 0 {
            defect += 1;
        } else if rel == 1 && u == 1 && w == 1 {
            defect += 1;
        } else if rel == 2 && u != w {
            defect += 1;
        }
        i += 1;
    }
    defect
}

pub fn convert_skyrim_zup_to_dx_yup_meters(sky_xyz: [f32; 3]) -> [f32; 3] {
    const S: f32 = 0.014280;
    [sky_xyz[0] * S, sky_xyz[2] * S, sky_xyz[1] * S]
}

#[cfg(test)]
mod tests {
    use super::*;
    use core::mem::size_of;

    #[test]
    fn t01_skse_version_struct_is_848_bytes() {
        assert_eq!(size_of::<SksePluginVersionDataLayout>(), 0x350);
        assert_eq!(size_of::<SksePluginVersionDataLayout>(), 848);
    }

    #[test]
    fn t02_skse_version_exact_ae_field_offsets() {
        let s = SksePluginVersionDataLayout {
            data_version: 1,
            plugin_version: 0x0001_0700,
            name: [0u8; 256],
            author: [0u8; 256],
            support_email: [0u8; 252],
            version_independence_ex: 1,
            version_independence: 2,
            compatible_versions: [0u32; 16],
            se_version_required: 0,
        };
        let base = (&s as *const SksePluginVersionDataLayout) as usize;
        assert_eq!(((&s.data_version as *const u32) as usize) - base, 0x000);
        assert_eq!(((&s.plugin_version as *const u32) as usize) - base, 0x004);
        assert_eq!(((&s.name as *const [u8; 256]) as usize) - base, 0x008);
        assert_eq!(((&s.author as *const [u8; 256]) as usize) - base, 0x108);
        assert_eq!(((&s.support_email as *const [u8; 252]) as usize) - base, 0x208);
        assert_eq!(((&s.version_independence_ex as *const u32) as usize) - base, 0x304);
        assert_eq!(((&s.version_independence as *const u32) as usize) - base, 0x308);
        assert_eq!(((&s.compatible_versions as *const [u32; 16]) as usize) - base, 0x30C);
        assert_eq!(((&s.se_version_required as *const u32) as usize) - base, 0x34C);
    }

    #[test]
    fn t03_packed_skyrim_runtime_versions() {
        assert_eq!(pack_skyrim_version(1, 5, 97, 0), 0x0105_0610);
        assert_eq!(pack_skyrim_version(1, 6, 640, 0), 0x0106_2800);
        assert_eq!(pack_skyrim_version(1, 6, 1130, 0), 0x0106_46A0);
        assert_eq!(pack_skyrim_version(1, 6, 1170, 0), 0x0106_4920);
        assert_eq!(pack_skyrim_version(1, 6, 1179, 0), 0x0106_49B0);
        assert_eq!(pack_skyrim_version(1, 7, 104, 0), 0x0107_0680);
    }

    #[test]
    fn t04_braidre_rip_relative_positive_disp() {
        // mov rax, [rip + 0x00123450] at offset 4 inside base 0x140000000 -> 0x140000004 + 7 + 0x123450 = 0x14012345B
        let code = [0x90, 0x90, 0x90, 0x90, 0x48, 0x8B, 0x05, 0x50, 0x34, 0x12, 0x00, 0xC3];
        let pat  = [0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0xC3];
        let mask = [1,    1,    1,    0,    0,    0,    0,    1];
        let va = resolve_rip_disp32(&code, 0x140000000usize, &pat, &mask, 3, 7);
        assert_eq!(va, Some(0x14012345Busize));
    }

    #[test]
    fn t05_braidre_rip_relative_negative_disp() {
        // lea rcx, [rip - 0x20] (-32 = 0xFFFFFFE0) at offset 2 -> 0x140001002 + 7 - 0x20 = 0x140000FE9
        let code = [0xCC, 0xCC, 0x48, 0x8D, 0x0D, 0xE0, 0xFF, 0xFF, 0xFF, 0x90];
        let pat  = [0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00];
        let mask = [1,    1,    1,    0,    0,    0,    0];
        let va = resolve_rip_disp32(&code, 0x140001000usize, &pat, &mask, 3, 7);
        assert_eq!(va, Some(0x140000FE9usize));
    }

    #[test]
    fn t06_sheaf_h1_zero_valid_quest_chain() {
        let edges: [u16; 4] = [0x0010, 0x0021, 0x0143, 0x0252];
        // Stages 0, 1, 2 + Branch 3 + Coupled Seal 5 -> 0b101111 = 0x2F
        assert_eq!(evaluate_sheaf_coboundary_h1(0x2F, &edges), 0);
    }

    #[test]
    fn t07_sheaf_h1_detects_sequence_break_and_mutex() {
        let edges: [u16; 4] = [0x0010, 0x0021, 0x0143, 0x0252];
        // Sequence break: Stage 2 set without Stage 1 (0b100101 = 0x25)
        assert!(evaluate_sheaf_coboundary_h1(0x25, &edges) > 0);
        // Mutex violation: both Branch 3 and Branch 4 active (0b111111 = 0x3F)
        assert!(evaluate_sheaf_coboundary_h1(0x3F, &edges) > 0);
    }

    #[test]
    fn t08_skyrim_zup_to_dx_yup_unit_scaling() {
        let dx = convert_skyrim_zup_to_dx_yup_meters([700.0, 1400.0, 350.0]);
        assert!((dx[0] - 9.996).abs() < 1e-3);
        assert!((dx[1] - 4.998).abs() < 1e-3);
        assert!((dx[2] - 19.992).abs() < 1e-3);
    }

    #[test]
    fn t09_esl_light_formid_12bit_invariant() {
        let valid_ids: [u32; 4] = [0x0100_0800, 0x0100_0801, 0x0100_0802, 0x0100_0803];
        for id in valid_ids {
            assert_eq!(id & 0x00FF_F000, 0);
        }
    }
}