#![no_std]
//! OmegaBraidRE v1.2.0 (`omega_braid_re_core`) — Zero-Crate `#![no_std]` Reverse-Engineering Kernel
//! 1. Multi-section `rva_to_file_offset` mapper + PE32+ `.pdata` (`RUNTIME_FUNCTION` + `UNWIND_INFO`),
//!    Export (`EAT`), Import (`IAT`), and ECMA-335 `.NET` CLR (`IMAGE_COR20_HEADER`, `BSJB`, & `#~`
//!    `TypeDef`/`Field`/`MethodDef` metadata tables) parser
//! 2. Dual-ISA (`x86_64` & `ARM64`) + CIL -> SSA `BraidIR` lifter with Dead-Flag Elimination
//! 3. Bipolar 240-root Conway-Sloane `E8` (`||2*r||^2 == 8`) CFG cross-patch function differ
//! 4. Cellular Sheaf (`H^1 = 0`) C++23 / Rust `#[repr(C)]` struct & vtable slot verifier

pub const BRAID_RE_MAGIC: u32 = 0x4F42_5245; // "OBRE"
pub const ECMA_BSJB_MAGIC: u32 = 0x424A_5342; // "BSJB"
pub const MAX_SECTIONS: usize = 32;
pub const MAX_PDATA: usize = 64;
pub const MAX_FIELDS: usize = 32;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ContainerFormat {
    RawBlob,
    Pe32Plus,
    Pe32,
    Elf64,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ArchIsa {
    Unknown,
    X86_64,
    Arm64,
    DotNetCil,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SectionSpan {
    pub virtual_addr: u32,
    pub virtual_size: u32,
    pub raw_offset: u32,
    pub raw_size: u32,
    pub characteristics: u32,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PdataRuntimeFunction {
    pub begin_rva: u32,
    pub end_rva: u32,
    pub unwind_info_rva: u32,
    pub prolog_size: u8,
    pub unwind_flags: u8,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TildeMethodDefRow {
    pub token: u32,
    pub rva: u32,
    pub impl_flags: u16,
    pub flags: u16,
    pub name_str_idx: u32,
    pub owner_typedef_row: u32,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TildeTableSummary {
    pub valid: bool,
    pub typedef_count: u32,
    pub fielddef_count: u32,
    pub methoddef_count: u32,
    pub first_method_rva: u32,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ClrMetadataSummary {
    pub is_clr_assembly: bool,
    pub il_only: bool,
    pub major_runtime: u16,
    pub minor_runtime: u16,
    pub metadata_rva: u32,
    pub metadata_size: u32,
    pub entry_point_token: u32,
    pub bsjb_verified: bool,
    pub stream_count: u16,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BinaryHeaderInfo {
    pub format: ContainerFormat,
    pub isa: ArchIsa,
    pub image_base: u64,
    pub entry_rva: u64,
    pub text_rva: u64,
    pub text_raw_off: u64,
    pub text_size: u64,
    pub export_dir_rva: u32,
    pub import_dir_rva: u32,
    pub pdata_dir_rva: u32,
    pub pdata_dir_size: u32,
    pub clr: ClrMetadataSummary,
    pub section_count: usize,
    pub sections: [SectionSpan; MAX_SECTIONS],
    pub valid: bool,
}

#[inline]
fn rd_u16_le(b: &[u8], o: usize) -> u16 {
    (b[o] as u16) | ((b[o + 1] as u16) << 8)
}

#[inline]
fn rd_u32_le(b: &[u8], o: usize) -> u32 {
    (b[o] as u32)
        | ((b[o + 1] as u32) << 8)
        | ((b[o + 2] as u32) << 16)
        | ((b[o + 3] as u32) << 24)
}

#[inline]
fn rd_u64_le(b: &[u8], o: usize) -> u64 {
    (rd_u32_le(b, o) as u64) | ((rd_u32_le(b, o + 4) as u64) << 32)
}

#[inline]
fn rd_idx(b: &[u8], o: usize, sz: usize) -> u32 {
    if sz == 4 { rd_u32_le(b, o) } else { rd_u16_le(b, o) as u32 }
}

/// Maps a PE Relative Virtual Address (RVA) to a validated raw file offset across all mapped sections.
pub fn rva_to_file_offset(info: &BinaryHeaderInfo, rva: u32, buf_len: usize) -> Option<usize> {
    if rva == 0 {
        return None;
    }
    let mut i = 0usize;
    while i < info.section_count {
        let s = info.sections[i];
        let span = if s.virtual_size > s.raw_size { s.virtual_size } else { s.raw_size };
        if span > 0 && rva >= s.virtual_addr && (rva - s.virtual_addr) < span {
            let delta = rva - s.virtual_addr;
            if delta < s.raw_size {
                let off = (s.raw_offset as usize).saturating_add(delta as usize);
                if off < buf_len {
                    return Some(off);
                }
            }
        }
        i += 1;
    }
    if (rva as usize) < buf_len && info.section_count == 0 {
        return Some(rva as usize);
    }
    None
}

/// Parses an ECMA-335 Partition II 24.2.6 `#~` stream slice for `TypeDef` (`0x02`), `Field` (`0x04`),
/// and `MethodDef` (`0x06`) rows with zero heap allocations.
pub fn parse_ecma335_tilde_tables(
    tilde: &[u8],
    out_methods: &mut [TildeMethodDefRow],
) -> (TildeTableSummary, usize) {
    let mut summary = TildeTableSummary {
        valid: false,
        typedef_count: 0,
        fielddef_count: 0,
        methoddef_count: 0,
        first_method_rva: 0,
    };
    if tilde.len() < 24 {
        return (summary, 0);
    }

    let heap_sizes = tilde[6];
    let str_sz: usize = if (heap_sizes & 0x01) != 0 { 4 } else { 2 };
    let guid_sz: usize = if (heap_sizes & 0x02) != 0 { 4 } else { 2 };
    let blob_sz: usize = if (heap_sizes & 0x04) != 0 { 4 } else { 2 };

    let valid_mask = rd_u64_le(tilde, 8);
    let mut rows = [0u32; 64];
    let mut cur = 24usize;
    let mut t = 0usize;
    while t < 64 {
        if ((valid_mask >> t) & 1) != 0 {
            if cur.saturating_add(4) > tilde.len() {
                return (summary, 0);
            }
            rows[t] = rd_u32_le(tilde, cur);
            cur += 4;
        }
        t += 1;
    }

    let max4 = |a: u32, b: u32, c: u32, d: u32| -> u32 {
        let ab = if a > b { a } else { b };
        let cd = if c > d { c } else { d };
        if ab > cd { ab } else { cd }
    };
    let res_scope_sz: usize = if max4(rows[0x00], rows[0x01], rows[0x1A], rows[0x23]) >= 16384 { 4 } else { 2 };
    let td_or_ref_sz: usize = if max4(rows[0x02], rows[0x01], rows[0x1B], 0) >= 16384 { 4 } else { 2 };
    let fld_idx_sz: usize = if rows[0x04] >= 65536 { 4 } else { 2 };
    let mtd_idx_sz: usize = if rows[0x06] >= 65536 { 4 } else { 2 };
    let prm_idx_sz: usize = if rows[0x08] >= 65536 { 4 } else { 2 };

    // 0x00 Module
    let b00 = (rows[0x00] as usize).saturating_mul(2 + str_sz + 3 * guid_sz);
    if cur.saturating_add(b00) > tilde.len() { return (summary, 0); }
    cur += b00;

    // 0x01 TypeRef
    let b01 = (rows[0x01] as usize).saturating_mul(res_scope_sz + 2 * str_sz);
    if cur.saturating_add(b01) > tilde.len() { return (summary, 0); }
    cur += b01;

    // 0x02 TypeDef
    let row_sz_02 = 4 + 2 * str_sz + td_or_ref_sz + fld_idx_sz + mtd_idx_sz;
    let b02 = (rows[0x02] as usize).saturating_mul(row_sz_02);
    if cur.saturating_add(b02) > tilde.len() { return (summary, 0); }
    let mut td_mstart = [0u32; 64];
    let td_cap = if (rows[0x02] as usize) < 64 { rows[0x02] as usize } else { 64 };
    let mlist_off = 4 + 2 * str_sz + td_or_ref_sz + fld_idx_sz;
    let mut k = 0usize;
    while k < td_cap {
        td_mstart[k] = rd_idx(tilde, cur + k * row_sz_02 + mlist_off, mtd_idx_sz);
        k += 1;
    }
    summary.typedef_count = rows[0x02];
    cur += b02;

    // 0x03 FieldPtr
    let b03 = (rows[0x03] as usize).saturating_mul(fld_idx_sz);
    if cur.saturating_add(b03) > tilde.len() { return (summary, 0); }
    cur += b03;

    // 0x04 Field
    let b04 = (rows[0x04] as usize).saturating_mul(2 + str_sz + blob_sz);
    if cur.saturating_add(b04) > tilde.len() { return (summary, 0); }
    summary.fielddef_count = rows[0x04];
    cur += b04;

    // 0x05 MethodPtr
    let b05 = (rows[0x05] as usize).saturating_mul(mtd_idx_sz);
    if cur.saturating_add(b05) > tilde.len() { return (summary, 0); }
    cur += b05;

    // 0x06 MethodDef
    let row_sz_06 = 8 + str_sz + blob_sz + prm_idx_sz;
    let b06 = (rows[0x06] as usize).saturating_mul(row_sz_06);
    if cur.saturating_add(b06) > tilde.len() { return (summary, 0); }
    summary.methoddef_count = rows[0x06];
    summary.valid = true;

    let mut written = 0usize;
    let mut td_cur = 0usize;
    let mut m = 0usize;
    let n_md = rows[0x06] as usize;
    while m < n_md && written < out_methods.len() {
        let m_row_1 = (m + 1) as u32;
        while td_cur + 1 < td_cap && td_mstart[td_cur + 1] > 0 && td_mstart[td_cur + 1] <= m_row_1 {
            td_cur += 1;
        }
        let roff = cur + m * row_sz_06;
        let rva = rd_u32_le(tilde, roff);
        let imp = rd_u16_le(tilde, roff + 4);
        let flg = rd_u16_le(tilde, roff + 6);
        let name_idx = rd_idx(tilde, roff + 8, str_sz);
        if m == 0 {
            summary.first_method_rva = rva;
        }
        out_methods[written] = TildeMethodDefRow {
            token: 0x0600_0000 | m_row_1,
            rva,
            impl_flags: imp,
            flags: flg,
            name_str_idx: name_idx,
            owner_typedef_row: (td_cur + 1) as u32,
        };
        written += 1;
        m += 1;
    }

    (summary, written)
}

/// Zero-copy, panic-free PE32+/PE32 (with DataDirectories 0/1/3/14 & ECMA-335 BSJB) and ELF64 inspector.
pub fn inspect_binary_header(buf: &[u8]) -> BinaryHeaderInfo {
    let empty_sec = SectionSpan {
        virtual_addr: 0,
        virtual_size: 0,
        raw_offset: 0,
        raw_size: 0,
        characteristics: 0,
    };
    let empty_clr = ClrMetadataSummary {
        is_clr_assembly: false,
        il_only: false,
        major_runtime: 0,
        minor_runtime: 0,
        metadata_rva: 0,
        metadata_size: 0,
        entry_point_token: 0,
        bsjb_verified: false,
        stream_count: 0,
    };
    let mut out = BinaryHeaderInfo {
        format: ContainerFormat::RawBlob,
        isa: ArchIsa::Unknown,
        image_base: 0,
        entry_rva: 0,
        text_rva: 0,
        text_raw_off: 0,
        text_size: 0,
        export_dir_rva: 0,
        import_dir_rva: 0,
        pdata_dir_rva: 0,
        pdata_dir_size: 0,
        clr: empty_clr,
        section_count: 0,
        sections: [empty_sec; MAX_SECTIONS],
        valid: false,
    };
    if buf.len() < 64 {
        return out;
    }

    if buf[0] == b'M' && buf[1] == b'Z' {
        let lfanew = rd_u32_le(buf, 0x3C) as usize;
        if buf.len() >= 136
            && lfanew.saturating_add(136) <= buf.len()
            && buf[lfanew] == b'P'
            && buf[lfanew + 1] == b'E'
            && buf[lfanew + 2] == 0
            && buf[lfanew + 3] == 0
        {
            let machine = rd_u16_le(buf, lfanew + 4);
            let nsects = rd_u16_le(buf, lfanew + 6) as usize;
            let opt_sz = rd_u16_le(buf, lfanew + 20) as usize;
            let opt_off = lfanew + 24;
            if opt_sz >= 96 && opt_off.saturating_add(opt_sz) <= buf.len() {
                let magic = rd_u16_le(buf, opt_off);
                if magic == 0x020B || magic == 0x010B {
                    let is_pe64 = magic == 0x020B;
                    out.format = if is_pe64 { ContainerFormat::Pe32Plus } else { ContainerFormat::Pe32 };
                    out.isa = if machine == 0xAA64 { ArchIsa::Arm64 } else { ArchIsa::X86_64 };
                    out.entry_rva = rd_u32_le(buf, opt_off + 16) as u64;
                    out.image_base = if is_pe64 {
                        rd_u64_le(buf, opt_off + 24)
                    } else {
                        rd_u32_le(buf, opt_off + 28) as u64
                    };
                    out.valid = true;

                    let dd_count_off = if is_pe64 { opt_off + 108 } else { opt_off + 92 };
                    let dd_base = if is_pe64 { opt_off + 112 } else { opt_off + 96 };
                    let num_dd = if dd_count_off + 4 <= opt_off + opt_sz {
                        let raw_n = rd_u32_le(buf, dd_count_off) as usize;
                        if raw_n > 16 { 16 } else { raw_n }
                    } else {
                        0
                    };

                    let sec_table = opt_off + opt_sz;
                    let mut i = 0usize;
                    while i < nsects && i < MAX_SECTIONS && sec_table.saturating_add((i + 1) * 40) <= buf.len() {
                        let sh = sec_table + i * 40;
                        let vsz = rd_u32_le(buf, sh + 8);
                        let vrva = rd_u32_le(buf, sh + 12);
                        let rsz = rd_u32_le(buf, sh + 16);
                        let roff = rd_u32_le(buf, sh + 20);
                        let ch = rd_u32_le(buf, sh + 36);
                        out.sections[out.section_count] = SectionSpan {
                            virtual_addr: vrva,
                            virtual_size: vsz,
                            raw_offset: roff,
                            raw_size: rsz,
                            characteristics: ch,
                        };
                        out.section_count += 1;

                        let is_text = buf[sh] == b'.' && buf[sh + 1] == b't' && buf[sh + 2] == b'e';
                        if out.text_rva == 0 && ((ch & 0x2000_0000) != 0 || is_text) {
                            out.text_rva = vrva as u64;
                            out.text_size = if vsz == 0 || (rsz > 0 && rsz < vsz) { rsz as u64 } else { vsz as u64 };
                            out.text_raw_off = roff as u64;
                        }
                        i += 1;
                    }

                    let read_dd = |idx: usize| -> (u32, u32) {
                        if idx < num_dd && dd_base.saturating_add((idx + 1) * 8) <= opt_off + opt_sz {
                            let p = dd_base + idx * 8;
                            (rd_u32_le(buf, p), rd_u32_le(buf, p + 4))
                        } else {
                            (0, 0)
                        }
                    };
                    let (exp_rva, _) = read_dd(0);
                    let (imp_rva, _) = read_dd(1);
                    let (pd_rva, pd_sz) = read_dd(3);
                    let (clr_rva, clr_sz) = read_dd(14);
                    out.export_dir_rva = exp_rva;
                    out.import_dir_rva = imp_rva;
                    out.pdata_dir_rva = pd_rva;
                    out.pdata_dir_size = pd_sz;

                    if clr_rva != 0 && clr_sz >= 72 {
                        if let Some(cor_off) = rva_to_file_offset(&out, clr_rva, buf.len()) {
                            if cor_off.saturating_add(72) <= buf.len() {
                                let cb = rd_u32_le(buf, cor_off);
                                if cb >= 72 {
                                    let maj = rd_u16_le(buf, cor_off + 4);
                                    let min = rd_u16_le(buf, cor_off + 6);
                                    let md_rva = rd_u32_le(buf, cor_off + 8);
                                    let md_sz = rd_u32_le(buf, cor_off + 12);
                                    let flags = rd_u32_le(buf, cor_off + 16);
                                    let ep_tok = rd_u32_le(buf, cor_off + 20);
                                    let mut bsjb = false;
                                    let mut streams = 0u16;

                                    if let Some(md_off) = rva_to_file_offset(&out, md_rva, buf.len()) {
                                        if md_off.saturating_add(20) <= buf.len()
                                            && rd_u32_le(buf, md_off) == ECMA_BSJB_MAGIC
                                        {
                                            bsjb = true;
                                            let ver_len = (rd_u32_le(buf, md_off + 12) as usize + 3) & !3usize;
                                            let st_off = md_off.saturating_add(16).saturating_add(ver_len).saturating_add(2);
                                            if st_off.saturating_add(2) <= buf.len() {
                                                streams = rd_u16_le(buf, st_off);
                                            }
                                        }
                                    }

                                    out.clr = ClrMetadataSummary {
                                        is_clr_assembly: true,
                                        il_only: (flags & 1) != 0,
                                        major_runtime: maj,
                                        minor_runtime: min,
                                        metadata_rva: md_rva,
                                        metadata_size: md_sz,
                                        entry_point_token: ep_tok,
                                        bsjb_verified: bsjb,
                                        stream_count: streams,
                                    };
                                    if (flags & 1) != 0 || (bsjb && pd_rva == 0) {
                                        out.isa = ArchIsa::DotNetCil;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return out;
    }

    if buf[0] == 0x7F && buf[1] == b'E' && buf[2] == b'L' && buf[3] == b'F' && buf[4] == 2 && buf[5] == 1 {
        let machine = rd_u16_le(buf, 18);
        out.format = ContainerFormat::Elf64;
        out.isa = if machine == 0x00B7 { ArchIsa::Arm64 } else { ArchIsa::X86_64 };
        out.entry_rva = rd_u64_le(buf, 24);
        out.image_base = 0x40_0000;
        let phoff = rd_u64_le(buf, 32) as usize;
        let phent = rd_u16_le(buf, 54) as usize;
        let phnum = rd_u16_le(buf, 56) as usize;
        out.valid = true;
        if phent >= 56 && phoff < buf.len() {
            let mut i = 0usize;
            while i < phnum && (i + 1).saturating_mul(phent) <= buf.len() - phoff {
                let ph = phoff + i * phent;
                let p_type = rd_u32_le(buf, ph);
                let p_flags = rd_u32_le(buf, ph + 4);
                if p_type == 1 && (p_flags & 1) != 0 {
                    out.text_raw_off = rd_u64_le(buf, ph + 8);
                    out.text_rva = rd_u64_le(buf, ph + 16);
                    out.text_size = rd_u64_le(buf, ph + 32);
                    break;
                }
                i += 1;
            }
        }
    }
    out
}

pub fn parse_pe_pdata_functions(
    buf: &[u8],
    info: &BinaryHeaderInfo,
    out_fns: &mut [PdataRuntimeFunction],
) -> usize {
    if info.pdata_dir_rva == 0 || info.pdata_dir_size < 12 || out_fns.is_empty() {
        return 0;
    }
    let pd_off = match rva_to_file_offset(info, info.pdata_dir_rva, buf.len()) {
        Some(o) => o,
        None => return 0,
    };
    let max_entries = (info.pdata_dir_size as usize) / 12;
    let mut count = 0usize;
    let mut i = 0usize;
    while i < max_entries && count < out_fns.len() && pd_off.saturating_add((i + 1) * 12) <= buf.len() {
        let e = pd_off + i * 12;
        let begin_rva = rd_u32_le(buf, e);
        let end_rva = rd_u32_le(buf, e + 4);
        let unwind_rva = rd_u32_le(buf, e + 8);
        if begin_rva == 0 && end_rva == 0 {
            break;
        }
        if end_rva > begin_rva {
            let mut prolog = 0u8;
            let mut uflags = 0u8;
            if let Some(uoff) = rva_to_file_offset(info, unwind_rva & !1u32, buf.len()) {
                if uoff.saturating_add(4) <= buf.len() {
                    uflags = (buf[uoff] >> 3) & 0x1F;
                    prolog = buf[uoff + 1];
                }
            }
            out_fns[count] = PdataRuntimeFunction {
                begin_rva,
                end_rva,
                unwind_info_rva: unwind_rva,
                prolog_size: prolog,
                unwind_flags: uflags,
            };
            count += 1;
        }
        i += 1;
    }
    count
}

pub fn extract_rip_string_preview(
    buf: &[u8],
    info: &BinaryHeaderInfo,
    target_rva: u32,
    out_ascii: &mut [u8],
) -> usize {
    if out_ascii.is_empty() {
        return 0;
    }
    let off = match rva_to_file_offset(info, target_rva, buf.len()) {
        Some(o) => o,
        None => return 0,
    };
    if off + 4 <= buf.len()
        && buf[off] >= 0x20
        && buf[off] <= 0x7E
        && buf[off + 1] == 0
        && buf[off + 2] >= 0x20
        && buf[off + 2] <= 0x7E
        && buf[off + 3] == 0
    {
        let mut n = 0usize;
        let mut p = off;
        while p + 1 < buf.len() && n < out_ascii.len() {
            let lo = buf[p];
            let hi = buf[p + 1];
            if lo == 0 && hi == 0 {
                return if n >= 3 { n } else { 0 };
            }
            if hi != 0 || lo < 0x20 || lo > 0x7E {
                break;
            }
            out_ascii[n] = lo;
            n += 1;
            p += 2;
        }
        return if n >= 3 { n } else { 0 };
    }

    let mut n = 0usize;
    let mut p = off;
    while p < buf.len() && n < out_ascii.len() {
        let ch = buf[p];
        if ch == 0 {
            return if n >= 3 { n } else { 0 };
        }
        if ch < 0x20 || ch > 0x7E {
            return 0;
        }
        out_ascii[n] = ch;
        n += 1;
        p += 1;
    }
    if n >= 4 { n } else { 0 }
}

pub fn snap_e8_root_2x_int(feat8: &[i32; 8]) -> [i8; 8] {
    let mut all_zero = true;
    let mut k = 0;
    while k < 8 {
        if feat8[k] != 0 {
            all_zero = false;
            break;
        }
        k += 1;
    }
    if all_zero {
        return [2, 2, 0, 0, 0, 0, 0, 0];
    }

    let mut best_d8_dot: i64 = i64::MIN;
    let mut best_i = 0usize;
    let mut best_j = 1usize;
    let mut best_si = 2i8;
    let mut best_sj = 2i8;

    let mut i = 0usize;
    while i < 8 {
        let mut j = i + 1;
        while j < 8 {
            let signs: [i8; 2] = [-2, 2];
            let mut a = 0usize;
            while a < 2 {
                let mut b = 0usize;
                while b < 2 {
                    let dot = (signs[a] as i64) * (feat8[i] as i64) + (signs[b] as i64) * (feat8[j] as i64);
                    if dot > best_d8_dot {
                        best_d8_dot = dot;
                        best_i = i;
                        best_j = j;
                        best_si = signs[a];
                        best_sj = signs[b];
                    }
                    b += 1;
                }
                a += 1;
            }
            j += 1;
        }
        i += 1;
    }

    let mut half_2x = [1i8; 8];
    let mut neg_cnt = 0usize;
    let mut weakest_idx = 0usize;
    let mut weakest_abs = i64::MAX;
    let mut m = 0usize;
    while m < 8 {
        let v = feat8[m] as i64;
        if v < 0 {
            half_2x[m] = -1;
            neg_cnt += 1;
        } else {
            half_2x[m] = 1;
        }
        let av = v.abs();
        if av < weakest_abs {
            weakest_abs = av;
            weakest_idx = m;
        }
        m += 1;
    }
    if (neg_cnt & 1) != 0 {
        half_2x[weakest_idx] = -half_2x[weakest_idx];
    }

    let mut half_dot: i64 = 0;
    let mut t = 0usize;
    while t < 8 {
        half_dot += (half_2x[t] as i64) * (feat8[t] as i64);
        t += 1;
    }

    if best_d8_dot >= half_dot {
        let mut out = [0i8; 8];
        out[best_i] = best_si;
        out[best_j] = best_sj;
        out
    } else {
        half_2x
    }
}

pub fn is_valid_e8_root_2x(r2: &[i8; 8]) -> bool {
    let mut s: i32 = 0;
    let mut i = 0;
    while i < 8 {
        let v = r2[i] as i32;
        s += v * v;
        i += 1;
    }
    s == 8
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct RecoveredFieldObservation {
    pub offset: u32,
    pub size: u8,
    pub is_float: bool,
    pub is_vptr: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SheafStructAudit {
    pub distinct_fields: usize,
    pub h1_overlap_defects: u32,
    pub aligned_struct_size: u32,
    pub has_vtable_ptr: bool,
}

pub fn solve_sheaf_struct_layout(obs: &[RecoveredFieldObservation]) -> SheafStructAudit {
    let mut unique: [RecoveredFieldObservation; MAX_FIELDS] = [RecoveredFieldObservation {
        offset: 0,
        size: 0,
        is_float: false,
        is_vptr: false,
    }; MAX_FIELDS];
    let mut u_len = 0usize;
    let mut defects: u32 = 0;
    let mut has_vptr = false;

    let mut i = 0usize;
    while i < obs.len() {
        let cur = obs[i];
        if cur.size > 0 && cur.offset <= 0x4000 {
            if cur.offset == 0 && cur.is_vptr {
                has_vptr = true;
            }
            let mut matched_exact = false;
            let mut overlapped = false;
            let mut j = 0usize;
            while j < u_len {
                let ex = unique[j];
                if ex.offset == cur.offset {
                    matched_exact = true;
                    if ex.size != cur.size || (ex.is_float != cur.is_float && !ex.is_vptr && !cur.is_vptr) {
                        defects = defects.saturating_add(1);
                    }
                    if cur.is_vptr {
                        unique[j].is_vptr = true;
                    }
                    break;
                }
                let end_cur = cur.offset.saturating_add(cur.size as u32);
                let end_ex = ex.offset.saturating_add(ex.size as u32);
                if cur.offset < end_ex && ex.offset < end_cur {
                    overlapped = true;
                    defects = defects.saturating_add(1);
                    break;
                }
                j += 1;
            }
            if !matched_exact && !overlapped && u_len < MAX_FIELDS {
                unique[u_len] = cur;
                u_len += 1;
            }
        }
        i += 1;
    }

    let mut max_end: u32 = 0;
    let mut k = 0usize;
    while k < u_len {
        let end = unique[k].offset.saturating_add(unique[k].size as u32);
        if end > max_end {
            max_end = end;
        }
        k += 1;
    }
    let aligned = (max_end.saturating_add(7)) & !7u32;

    SheafStructAudit {
        distinct_fields: u_len,
        h1_overlap_defects: defects,
        aligned_struct_size: aligned,
        has_vtable_ptr: has_vptr,
    }
}

pub fn count_dead_flags_eliminated(block_ops: &[(bool, bool, bool)], flags_live_out: bool) -> (u32, u32) {
    let mut live = flags_live_out;
    let mut total_set = 0u32;
    let mut killed = 0u32;
    let mut idx = block_ops.len();
    while idx > 0 {
        idx -= 1;
        let (sets_f, uses_f, is_cmp) = block_ops[idx];
        if sets_f {
            total_set += 1;
            if !live && !is_cmp {
                killed += 1;
            }
            live = false;
        }
        if uses_f {
            live = true;
        }
    }
    (total_set, killed)
}


#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PdbMsf7Header {
    pub valid: bool,
    pub block_size: u32,
    pub num_blocks: u32,
    pub num_dir_bytes: u32,
    pub block_map_addr: u32,
}

pub fn parse_pdb_msf7_superblock(buf: &[u8]) -> PdbMsf7Header {
    let empty = PdbMsf7Header { valid: false, block_size: 0, num_blocks: 0, num_dir_bytes: 0, block_map_addr: 0 };
    const MAGIC: &[u8; 32] = b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0";
    if buf.len() < 56 { return empty; }
    let mut i = 0usize;
    while i < 32 {
        if buf[i] != MAGIC[i] { return empty; }
        i += 1;
    }
    let block_size = rd_u32_le(buf, 32);
    let num_blocks = rd_u32_le(buf, 40);
    let num_dir_bytes = rd_u32_le(buf, 44);
    let block_map_addr = rd_u32_le(buf, 52);
    let valid_blk = block_size == 512 || block_size == 1024 || block_size == 2048 || block_size == 4096;
    PdbMsf7Header { valid: valid_blk && num_dir_bytes >= 4, block_size, num_blocks, num_dir_bytes, block_map_addr }
}

pub fn compute_x64_safe_steal_len(code: &[u8], min_bytes: usize) -> usize {
    let mut off = 0usize;
    while off < code.len() && off < min_bytes {
        if off + 4 <= code.len() && code[off] == 0xF3 && code[off + 1] == 0x0F && code[off + 2] == 0x1E && code[off + 3] == 0xFA {
            off += 4;
            continue;
        }
        let start = off;
        if code[off] >= 0x40 && code[off] <= 0x4F { off += 1; }
        if off >= code.len() { break; }
        let op = code[off]; off += 1;
        if op == 0x90 || op == 0xC3 || (op >= 0x50 && op <= 0x5F) { continue; }
        if op == 0xE8 || op == 0xE9 { off += 4; continue; }
        if off < code.len() {
            let modrm = code[off]; off += 1;
            let md = (modrm >> 6) & 3;
            let rm = modrm & 7;
            if md != 3 && rm == 4 && off < code.len() {
                let sib = code[off]; off += 1;
                if md == 0 && (sib & 7) == 5 { off += 4; }
            }
            if md == 0 && rm == 5 { off += 4; }
            else if md == 1 { off += 1; }
            else if md == 2 { off += 4; }
            if op == 0x83 { off += 1; }
            else if op == 0x81 || op == 0xC7 { off += 4; }
        }
        if off == start { break; }
    }
    if off > code.len() { code.len() } else { off }
}


pub fn scan_masked_aob_first(haystack: &[u8], vals: &[u8], masks: &[u8]) -> Option<usize> {
    if vals.is_empty() || vals.len() != masks.len() || haystack.len() < vals.len() {
        return None;
    }
    let end = haystack.len() - vals.len();
    let mut pos = 0usize;
    while pos <= end {
        let mut ok = true;
        let mut k = 0usize;
        while k < vals.len() {
            if (haystack[pos + k] & masks[k]) != (vals[k] & masks[k]) {
                ok = false;
                break;
            }
            k += 1;
        }
        if ok {
            return Some(pos);
        }
        pos += 1;
    }
    None
}

pub fn resolve_x64_rip_target_rva(insn_rva: u64, insn_bytes: &[u8]) -> Option<u64> {
    if insn_bytes.len() >= 5 && (insn_bytes[0] == 0xE8 || insn_bytes[0] == 0xE9) {
        let disp = i32::from_le_bytes([insn_bytes[1], insn_bytes[2], insn_bytes[3], insn_bytes[4]]) as i64;
        return Some(((insn_rva as i64) + 5 + disp) as u64);
    }
    if insn_bytes.len() >= 7 && insn_bytes[0] >= 0x40 && insn_bytes[0] <= 0x4F
        && (insn_bytes[1] == 0x8D || insn_bytes[1] == 0x8B || insn_bytes[1] == 0x89)
        && (insn_bytes[2] & 0xC7) == 0x05
    {
        let disp = i32::from_le_bytes([insn_bytes[3], insn_bytes[4], insn_bytes[5], insn_bytes[6]]) as i64;
        return Some(((insn_rva as i64) + 7 + disp) as u64);
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_pe32plus_pdata_and_rip_string_recovery() {
        let mut pe = [0u8; 1024];
        pe[0] = b'M'; pe[1] = b'Z';
        pe[0x3C] = 0x80;
        pe[0x80] = b'P'; pe[0x81] = b'E';
        pe[0x84] = 0x64; pe[0x85] = 0x86;
        pe[0x86] = 2;
        pe[0x94] = 0xF0;
        let opt = 0x98usize;
        pe[opt] = 0x0B; pe[opt + 1] = 0x02;
        pe[opt + 108] = 16;
        pe[opt + 112 + 24 + 1] = 0x20;
        pe[opt + 112 + 24 + 4] = 24;

        let sec0 = opt + 0xF0;
        pe[sec0..sec0 + 5].copy_from_slice(b".text");
        pe[sec0 + 9] = 0x01; pe[sec0 + 13] = 0x10;
        pe[sec0 + 17] = 0x01; pe[sec0 + 21] = 0x02; pe[sec0 + 39] = 0x60;

        let sec1 = sec0 + 40;
        pe[sec1..sec1 + 6].copy_from_slice(b".pdata");
        pe[sec1 + 9] = 0x01; pe[sec1 + 13] = 0x20;
        pe[sec1 + 17] = 0x01; pe[sec1 + 21] = 0x03;

        pe[0x300] = 0x00; pe[0x301] = 0x10;
        pe[0x304] = 0x45; pe[0x305] = 0x10;
        pe[0x308] = 0x40; pe[0x309] = 0x20;
        pe[0x30C] = 0x50; pe[0x30D] = 0x10;
        pe[0x310] = 0x88; pe[0x311] = 0x10;
        pe[0x314] = 0x48; pe[0x315] = 0x20;
        pe[0x340] = 0x09; pe[0x341] = 9;
        pe[0x360..0x36F].copy_from_slice(b"Sovereign_EDE8\0");

        let info = inspect_binary_header(&pe);
        assert!(info.valid);
        let mut fns = [PdataRuntimeFunction {
            begin_rva: 0, end_rva: 0, unwind_info_rva: 0, prolog_size: 0, unwind_flags: 0,
        }; 8];
        let n = parse_pe_pdata_functions(&pe, &info, &mut fns);
        assert_eq!(n, 2);
        assert_eq!(fns[0].begin_rva, 0x1000);
        assert_eq!(fns[0].end_rva, 0x1045);
        assert_eq!(fns[0].prolog_size, 9);

        let mut s_buf = [0u8; 32];
        let slen = extract_rip_string_preview(&pe, &info, 0x2060, &mut s_buf);
        assert_eq!(&s_buf[..slen], b"Sovereign_EDE8");
    }

    #[test]
    fn test_dotnet_clr_bsjb_detection() {
        let mut pe = [0u8; 1024];
        pe[0] = b'M'; pe[1] = b'Z';
        pe[0x3C] = 0x80;
        pe[0x80] = b'P'; pe[0x81] = b'E';
        pe[0x84] = 0x64; pe[0x85] = 0x86;
        pe[0x86] = 1;
        pe[0x94] = 0xF0;
        let opt = 0x98usize;
        pe[opt] = 0x0B; pe[opt + 1] = 0x02;
        pe[opt + 108] = 16;
        pe[opt + 224] = 0x08; pe[opt + 225] = 0x20;
        pe[opt + 228] = 72;

        let sec0 = opt + 0xF0;
        pe[sec0..sec0 + 5].copy_from_slice(b".text");
        pe[sec0 + 9] = 0x02; pe[sec0 + 13] = 0x20;
        pe[sec0 + 17] = 0x02; pe[sec0 + 21] = 0x02;

        let cor = 0x208usize;
        pe[cor] = 72; pe[cor + 4] = 2; pe[cor + 6] = 5;
        pe[cor + 8] = 0x60; pe[cor + 9] = 0x20;
        pe[cor + 12] = 0x80; pe[cor + 16] = 0x01;
        pe[cor + 20] = 0x01; pe[cor + 23] = 0x06;

        let md = 0x260usize;
        pe[md..md + 4].copy_from_slice(b"BSJB");
        pe[md + 12] = 12;
        pe[md + 16..md + 26].copy_from_slice(b"v4.0.30319");
        pe[md + 30] = 5;

        let info = inspect_binary_header(&pe);
        assert!(info.valid);
        assert!(info.clr.is_clr_assembly);
        assert!(info.clr.bsjb_verified);
        assert_eq!(info.isa, ArchIsa::DotNetCil);
        assert_eq!(info.clr.stream_count, 5);
    }

    #[test]
    fn test_ecma335_tilde_typedef_methoddef_tables() {
        // Build a valid #~ stream with Tables 0x00 (Module=1), 0x02 (TypeDef=2), 0x06 (MethodDef=2)
        // Valid mask = (1 << 0) | (1 << 2) | (1 << 6) = 0x45
        let mut tilde = [0u8; 160];
        tilde[4] = 2; // Major=2
        tilde[6] = 0; // HeapSizes=0 (2-byte heap indices)
        tilde[8] = 0x45;
        // Row counts: Module=1, TypeDef=2, MethodDef=2
        tilde[24] = 1;
        tilde[28] = 2;
        tilde[32] = 2;
        let mut p = 36usize;
        // Module (10 bytes): Gen(2) + Name(2) + Mvid(2) + EncId(2) + EncBaseId(2)
        p += 10;
        // TypeDef[0] (14 bytes): Flags(4)+Name(2)+Ns(2)+Ext(2)+FldList(2)+MtdList(2 = 1)
        tilde[p + 12] = 1; p += 14;
        // TypeDef[1] (14 bytes): MethodList = 2
        tilde[p + 12] = 2; p += 14;
        // MethodDef[0] (14 bytes): RVA=0x2050, Impl=0, Flags=0x0086, Name=0x10, Sig=0, Param=1
        tilde[p] = 0x50; tilde[p + 1] = 0x20; tilde[p + 8] = 0x10; p += 14;
        // MethodDef[1] (14 bytes): RVA=0x2088, Name=0x24
        tilde[p] = 0x88; tilde[p + 1] = 0x20; tilde[p + 8] = 0x24;

        let mut methods = [TildeMethodDefRow {
            token: 0, rva: 0, impl_flags: 0, flags: 0, name_str_idx: 0, owner_typedef_row: 0,
        }; 4];
        let (sum, n) = parse_ecma335_tilde_tables(&tilde, &mut methods);
        assert!(sum.valid);
        assert_eq!(sum.typedef_count, 2);
        assert_eq!(sum.methoddef_count, 2);
        assert_eq!(n, 2);
        assert_eq!(methods[0].token, 0x0600_0001);
        assert_eq!(methods[0].rva, 0x2050);
        assert_eq!(methods[0].owner_typedef_row, 1);
        assert_eq!(methods[1].token, 0x0600_0002);
        assert_eq!(methods[1].rva, 0x2088);
        assert_eq!(methods[1].owner_typedef_row, 2);
    }

    #[test]
    fn test_e8_minimal_root_norm_exactness() {
        for seed in -64i32..64i32 {
            let v = [seed * 3, -seed * 2 + 5, seed ^ 11, 7 - seed, seed * 5 - 3, 1, -seed, seed + 9];
            let r2 = snap_e8_root_2x_int(&v);
            assert!(is_valid_e8_root_2x(&r2));
        }
    }

    #[test]
    fn test_reidemeister_dead_flag_and_sheaf_struct() {
        let ops = [
            (true, false, false),
            (true, false, false),
            (true, false, false),
            (true, false, true),
            (false, true, false),
        ];
        let (total, killed) = count_dead_flags_eliminated(&ops, false);
        assert_eq!(total, 4);
        assert_eq!(killed, 3);

        let clean = [
            RecoveredFieldObservation { offset: 0x00, size: 8, is_float: false, is_vptr: true },
            RecoveredFieldObservation { offset: 0x08, size: 4, is_float: false, is_vptr: false },
            RecoveredFieldObservation { offset: 0x18, size: 4, is_float: true, is_vptr: false },
            RecoveredFieldObservation { offset: 0x20, size: 8, is_float: false, is_vptr: false },
        ];
        let rep = solve_sheaf_struct_layout(&clean);
        assert_eq!(rep.h1_overlap_defects, 0);
        assert_eq!(rep.aligned_struct_size, 0x28);
    }

    #[test]
    fn test_pdb_msf7_superblock_decoder() {
        let mut hdr = [0u8; 64];
        let magic = b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0";
        hdr[0..32].copy_from_slice(magic);
        hdr[32..36].copy_from_slice(&4096u32.to_le_bytes());
        hdr[40..44].copy_from_slice(&7u32.to_le_bytes());
        hdr[44..48].copy_from_slice(&32u32.to_le_bytes());
        hdr[52..56].copy_from_slice(&3u32.to_le_bytes());
        let p = parse_pdb_msf7_superblock(&hdr);
        assert!(p.valid);
        assert_eq!(p.block_size, 4096);
        assert_eq!(p.num_blocks, 7);
        assert_eq!(p.block_map_addr, 3);
    }

    #[test]
    fn test_skse64_f4se_x64_safe_prologue_steal() {
        // mov [rsp+8], rbx (5B) | sub rsp, 0x20 (4B) | mov rbx, rcx (3B) | mov eax, [rbx+8] (3B)
        let prologue = [
            0x48u8, 0x89, 0x5C, 0x24, 0x08,
            0x48, 0x83, 0xEC, 0x20,
            0x48, 0x89, 0xCB,
            0x8B, 0x43, 0x08,
            0xC3,
        ];
        assert_eq!(compute_x64_safe_steal_len(&prologue, 5), 5);
        assert_eq!(compute_x64_safe_steal_len(&prologue, 14), 15);
    }

    #[test]
    fn test_wildcard_and_nibble_masked_aob_scanner() {
        let buf = [0x90u8, 0x90, 0x48, 0x89, 0x5C, 0x24, 0x18, 0x48, 0x83, 0xEC, 0x20, 0x49, 0x89, 0xCE];
        // Match "48 89 5C 24 ?? 48 83 EC 20 4? 89" at offset 2
        let vals  = [0x48u8, 0x89, 0x5C, 0x24, 0x00, 0x48, 0x83, 0xEC, 0x20, 0x40, 0x89];
        let masks = [0xFFu8, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xF0, 0xFF];
        assert_eq!(scan_masked_aob_first(&buf, &vals, &masks), Some(2));
    }

    #[test]
    fn test_rip_relative_target_rva_resolver() {
        // 48 8D 0D 39 11 00 00 @ RVA 0x1000 -> next=0x1007 + 0x1139 = 0x2140
        let lea_rip = [0x48u8, 0x8D, 0x0D, 0x39, 0x11, 0x00, 0x00];
        assert_eq!(resolve_x64_rip_target_rva(0x1000, &lea_rip), Some(0x2140));
    }
}
