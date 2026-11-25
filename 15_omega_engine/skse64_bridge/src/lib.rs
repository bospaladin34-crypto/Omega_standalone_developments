#![no_std]
#![allow(unknown_lints, linker_messages)]
// =============================================================================
// OmegaSkyrimWorldBridge.dll v1.7.7 - Zero-CRT #![no_std] SKSE64 World Bridge
// SPDX-License-Identifier: MIT OR BSD-3-Clause (Zero GPL-3.0 CommonLib code!)
// Zero external crates. Zero MSVC CRT (imports ONLY KERNEL32.dll).
// Exports:
//   - SKSEPlugin_Version (848B = 0x350 static data read by SKSE64 2.2.x+ via
//     LOAD_LIBRARY_AS_IMAGE_RESOURCE; versionIndependenceEx=1 NoStructUse,
//     versionIndependence=2 Signatures across AE 1.6.640..1.6.1179 & 1.7.104)
//   - SKSEPlugin_Load / SKSEPlugin_Query (registers SKSEMessagingInterface
//     "SKSE" lifecycle listener + background world-space telemetry worker)
//   - OmegaSkseScanRipSingleton (OmegaBraidRE v1.4.0 AOB + RIP [disp32] scanner)
//   - OmegaSkseEvaluateSheafGuardH1 (Cellular Sheaf H^1(X; F) = 0 quest guard)
//   - OmegaSkseClassifyWeatherAurora (Sovngarde / Skyrim weather -> U[14].w & U[17])
//   - OmegaSksePushWorldState (Z-up Skyrim NiMatrix3 -> Y-up DX U[13]..U[19] + U[1])
//   - OmegaSkseParsePapyrusTraceLine ("OTW|..." Creation Kit UserLog bridge)
//   - OmegaSkseGetDiagnostics (live runtime/message/frame/weather/sheaf counters)
// =============================================================================

use core::ffi::c_void;
use core::ptr;
use core::sync::atomic::{AtomicBool, AtomicI32, AtomicPtr, AtomicU32, AtomicU64, Ordering};

#[cfg(not(test))]
mod zero_crt {
    use core::ptr;

    #[panic_handler]
    fn skse_panic(_info: &core::panic::PanicInfo) -> ! {
        loop {
            core::hint::spin_loop();
        }
    }

    #[no_mangle]
    pub static _fltused: i32 = 0;

    #[no_mangle]
    pub unsafe extern "C" fn __CxxFrameHandler3() -> i32 {
        0
    }

    #[no_mangle]
    pub unsafe extern "C" fn memcpy(dst: *mut u8, src: *const u8, n: usize) -> *mut u8 {
        let mut i = 0usize;
        while i < n {
            ptr::write_volatile(dst.add(i), ptr::read_volatile(src.add(i)));
            i += 1;
        }
        dst
    }

    #[no_mangle]
    pub unsafe extern "C" fn memmove(dst: *mut u8, src: *const u8, n: usize) -> *mut u8 {
        if (dst as usize) <= (src as usize) {
            let mut i = 0usize;
            while i < n {
                ptr::write_volatile(dst.add(i), ptr::read_volatile(src.add(i)));
                i += 1;
            }
        } else {
            let mut i = n;
            while i > 0 {
                i -= 1;
                ptr::write_volatile(dst.add(i), ptr::read_volatile(src.add(i)));
            }
        }
        dst
    }

    #[no_mangle]
    pub unsafe extern "C" fn memset(dst: *mut u8, val: i32, n: usize) -> *mut u8 {
        let b = val as u8;
        let mut i = 0usize;
        while i < n {
            ptr::write_volatile(dst.add(i), b);
            i += 1;
        }
        dst
    }

    #[no_mangle]
    pub unsafe extern "C" fn memcmp(a: *const u8, b: *const u8, n: usize) -> i32 {
        let mut i = 0usize;
        while i < n {
            let va = ptr::read_volatile(a.add(i)) as i32;
            let vb = ptr::read_volatile(b.add(i)) as i32;
            if va != vb {
                return va - vb;
            }
            i += 1;
        }
        0
    }

    #[no_mangle]
    pub unsafe extern "C" fn bcmp(a: *const u8, b: *const u8, n: usize) -> i32 {
        memcmp(a, b, n)
    }

    #[no_mangle]
    pub unsafe extern "C" fn strlen(s: *const u8) -> usize {
        if s.is_null() {
            return 0;
        }
        let mut len = 0usize;
        while ptr::read_volatile(s.add(len)) != 0 {
            len += 1;
        }
        len
    }
}

const fn make_fixed_ascii<const N: usize>(s: &[u8]) -> [u8; N] {
    let mut out = [0u8; N];
    let mut i = 0usize;
    let max_copy = if s.len() < (N - 1) { s.len() } else { N - 1 };
    while i < max_copy {
        out[i] = s[i];
        i += 1;
    }
    out
}

// Exact 848-byte (0x350) SKSEPluginVersionData layout for SKSE64 2.2.0..2.2.6+
#[repr(C)]
pub struct SksePluginVersionData {
    pub data_version: u32,             // +0x000 = 1
    pub plugin_version: u32,           // +0x004 = 0x00010700 (v1.7.0)
    pub name: [u8; 256],               // +0x008
    pub author: [u8; 256],             // +0x108
    pub support_email: [u8; 252],      // +0x208 (252 bytes in SKSE 2.2.0+ for +0x304 alignment)
    pub version_independence_ex: u32,  // +0x304 = 1 (kVersionIndependentEx_NoStructUse)
    pub version_independence: u32,     // +0x308 = 2 (kVersionIndependent_Signatures)
    pub compatible_versions: [u32; 16],// +0x30C (AE 1.7.104, 1.7.99, 1.6.1170, 1.6.1179, 1.6.1130, 1.6.640, SE 1.5.97)
    pub se_version_required: u32,      // +0x34C = 0
}

const _: [(); 0x350] = [(); core::mem::size_of::<SksePluginVersionData>()];

#[no_mangle]
pub static OMEGA_SKSE_PLUGIN_VERSION: SksePluginVersionData = SksePluginVersionData {
    data_version: 1,
    plugin_version: 0x0001_0707,
    name: make_fixed_ascii::<256>(b"OmegaSkyrimWorldBridge"),
    author: make_fixed_ascii::<256>(b"ACT-Omega Unified Labs (MIT Clean-Room)"),
    support_email: make_fixed_ascii::<252>(b"https://github.com/bospaladin34-crypto/Omega_standalone_developments"),
    version_independence_ex: 1, // kVersionIndependentEx_NoStructUse
    version_independence: 2,    // kVersionIndependent_Signatures (zero Address Library lock-in!)
    compatible_versions: [
        0x0107_0680, // 1.7.104.0 (Steam Aug-2026 update)
        0x0107_0630, // 1.7.99.0
        0x0106_4920, // 1.6.1170.0 (Steam AE standard)
        0x0106_49B0, // 1.6.1179.0 (GOG AE standard)
        0x0106_46A0, // 1.6.1130.0
        0x0106_2930, // 1.6.659.0
        0x0106_2800, // 1.6.640.0
        0x0106_1610, // 1.6.353.0
        0x0106_13E0, // 1.6.318.0
        0x0105_0610, // 1.5.97.0 (SE)
        0, 0, 0, 0, 0, 0,
    ],
    se_version_required: 0,
};

#[repr(C)]
pub struct SksePluginInfo {
    pub info_version: u32,
    pub name: *const u8,
    pub version: u32,
}

#[repr(C)]
pub struct SkseInterface {
    pub skse_version: u32,
    pub runtime_version: u32,
    pub editor_version: u32,
    pub is_editor: u32,
    pub query_interface: Option<unsafe extern "C" fn(u32) -> *mut c_void>,
    pub get_plugin_handle: Option<unsafe extern "C" fn() -> u32>,
    pub get_release_index: Option<unsafe extern "C" fn() -> u32>,
    pub get_plugin_info: Option<unsafe extern "C" fn(*const u8) -> *const SksePluginInfo>,
}

#[repr(C)]
pub struct SkseMessage {
    pub sender: *const u8,
    pub msg_type: u32,
    pub data_len: u32,
    pub data: *mut c_void,
}

pub type FnSkseMessageEventCallback = unsafe extern "C" fn(*mut SkseMessage);

#[repr(C)]
pub struct SkseMessagingInterface {
    pub interface_version: u32,
    pub register_listener: Option<unsafe extern "C" fn(u32, *const u8, FnSkseMessageEventCallback) -> bool>,
    pub dispatch: Option<unsafe extern "C" fn(u32, u32, *mut c_void, u32, *const u8) -> bool>,
    pub get_event_dispatcher: Option<unsafe extern "C" fn(u32) -> *mut c_void>,
}

const K_INTERFACE_MESSAGING: u32 = 3;
const K_MSG_POST_LOAD: u32 = 0;
const K_MSG_POST_LOAD_GAME: u32 = 3;
const K_MSG_NEW_GAME: u32 = 7;
const K_MSG_DATA_LOADED: u32 = 8;

const PAGE_READWRITE: u32 = 0x04;
const FILE_MAP_WRITE: u32 = 0x0002;
const FILE_MAP_READ: u32 = 0x0004;
const GENERIC_READ: u32 = 0x8000_0000;
const FILE_SHARE_READ: u32 = 0x0000_0001;
const FILE_SHARE_WRITE: u32 = 0x0000_0002;
const OPEN_EXISTING: u32 = 3;
const TOTAL_MAP_BYTES: usize = 384;
const UNIFORM_BYTES: usize = 320;
const TRAILER_OFFSET: usize = 320;
const MAGIC_OTRI: u32 = 0x4F54_5249;
const CONTRACT_VERSION: u32 = 0x0001_0201;
const MASK_OMEGA_ENGINE: u32 = 1 << 0;
const MASK_OMEGA_SYNTH: u32 = 1 << 1;
const MASK_OMEGA_PENROSE: u32 = 1 << 2;
const MASK_OMEGA_AV: u32 = 1 << 3;
const MASK_RESHADE_ADDON: u32 = 1 << 4;
const MASK_SKSE64_BRIDGE: u32 = 1 << 5;
const MASK_OMEGA_TENSOR: u32 = 1 << 6;
const MASK_PIXEL10_LIVE: u32 = 1 << 7;
const MASK_PIXEL8_LIVE: u32 = 1 << 8;

const SKYRIM_UNIT_TO_METERS: f32 = 0.014280;
const DLL_PROCESS_DETACH: u32 = 0;
const DLL_PROCESS_ATTACH: u32 = 1;

#[link(name = "kernel32")]
extern "system" {
    fn DisableThreadLibraryCalls(module: *mut c_void) -> i32;
    fn CreateFileMappingW(
        file: *mut c_void,
        attrs: *mut c_void,
        protect: u32,
        max_high: u32,
        max_low: u32,
        name: *const u16,
    ) -> *mut c_void;
    fn MapViewOfFile(
        file_mapping: *mut c_void,
        desired_access: u32,
        offset_high: u32,
        offset_low: u32,
        bytes_to_map: usize,
    ) -> *mut u8;
    fn UnmapViewOfFile(base_addr: *const c_void) -> i32;
    fn CreateMutexW(attrs: *mut c_void, initial_owner: i32, name: *const u16) -> *mut c_void;
    fn WaitForSingleObject(handle: *mut c_void, milliseconds: u32) -> u32;
    fn ReleaseMutex(handle: *mut c_void) -> i32;
    fn CloseHandle(handle: *mut c_void) -> i32;
    fn GetTickCount64() -> u64;
    fn CreateThread(
        attrs: *mut c_void,
        stack_size: usize,
        start_routine: unsafe extern "system" fn(*mut c_void) -> u32,
        param: *mut c_void,
        flags: u32,
        thread_id: *mut u32,
    ) -> *mut c_void;
    fn Sleep(milliseconds: u32);
    fn GetEnvironmentVariableW(name: *const u16, buffer: *mut u16, size: u32) -> u32;
    fn CreateFileW(
        filename: *const u16,
        desired_access: u32,
        share_mode: u32,
        sec_attrs: *mut c_void,
        creation_disp: u32,
        flags_and_attrs: u32,
        template_file: *mut c_void,
    ) -> *mut c_void;
    fn GetFileSize(file: *mut c_void, high: *mut u32) -> u32;
    fn SetFilePointer(file: *mut c_void, dist_low: i32, dist_high: *mut i32, method: u32) -> u32;
    fn ReadFile(
        file: *mut c_void,
        buf: *mut u8,
        bytes_to_read: u32,
        bytes_read: *mut u32,
        overlapped: *mut c_void,
    ) -> i32;
}

static DEFAULT_MMF_UTF16: [u16; 29] = [
    b'L' as u16, b'o' as u16, b'c' as u16, b'a' as u16, b'l' as u16, b'\\' as u16,
    b'O' as u16, b'm' as u16, b'e' as u16, b'g' as u16, b'a' as u16,
    b'T' as u16, b'r' as u16, b'i' as u16, b'E' as u16, b'n' as u16, b'g' as u16, b'i' as u16, b'n' as u16, b'e' as u16,
    b'U' as u16, b'n' as u16, b'i' as u16, b'f' as u16, b'o' as u16, b'r' as u16, b'm' as u16, b's' as u16,
    0,
];

static ENV_ONEDRIVE_UTF16: [u16; 9] = [
    b'O' as u16, b'n' as u16, b'e' as u16, b'D' as u16, b'r' as u16,
    b'i' as u16, b'v' as u16, b'e' as u16, 0,
];
static ENV_USERPROFILE_UTF16: [u16; 12] = [
    b'U' as u16, b'S' as u16, b'E' as u16, b'R' as u16, b'P' as u16, b'R' as u16,
    b'O' as u16, b'F' as u16, b'I' as u16, b'L' as u16, b'E' as u16, 0,
];
static mut G_LOG_PATH_ALT_UTF16: [u16; 320] = [0u16; 320];

// Default Cellular Sheaf Quest Edges:
// Edge 0: Stage 1 requires Stage 0 (0 | (1<<4) | (0<<8))
// Edge 1: Stage 2 requires Stage 1 (1 | (2<<4) | (0<<8))
// Edge 2: Branch 3 & Branch 4 mutually exclusive (3 | (4<<4) | (1<<8))
// Edge 3: Stage 2 <-> Spectre Seal 5 coupled (2 | (5<<4) | (2<<8))
static DEFAULT_SHEAF_EDGES: [u16; 4] = [
    0x0010,
    0x0021,
    0x0143,
    0x0252,
];

static G_RUNTIME_VERSION: AtomicU32 = AtomicU32::new(0);
static G_PLUGIN_HANDLE: AtomicU32 = AtomicU32::new(0);
static G_MSG_COUNT: AtomicU32 = AtomicU32::new(0);
static G_FRAMES_PUSHED: AtomicU32 = AtomicU32::new(0);
static G_LAST_WEATHER_ID: AtomicU32 = AtomicU32::new(0x0010_A243);
static G_LAST_H1_STATE: AtomicI32 = AtomicI32::new(1);
static G_WORKER_RUNNING: AtomicBool = AtomicBool::new(false);
static G_WORKER_STOP: AtomicBool = AtomicBool::new(false);
static G_WORKER_THREAD: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_BOOT_TICK64: AtomicU64 = AtomicU64::new(0);
static G_LAST_LOG_SIZE: AtomicU32 = AtomicU32::new(0);
static G_PERSIST_MTX: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_PERSIST_MAP: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_PERSIST_VIEW: AtomicPtr<u8> = AtomicPtr::new(ptr::null_mut());

// Static scratch buffers (< 1 KB stack everywhere -> zero __chkstk!)
static mut G_LOG_PATH_UTF16: [u16; 320] = [0u16; 320];
static mut G_LOG_TAIL_BYTES: [u8; 512] = [0u8; 512];

#[inline]
pub fn omega_sqrt_f32(x: f32) -> f32 {
    if x <= 0.0 {
        return 0.0;
    }
    let mut i = x.to_bits();
    i = 0x5f37_5a86u32.wrapping_sub(i >> 1);
    let mut y = f32::from_bits(i);
    y = y * (1.5 - 0.5 * x * y * y);
    y = y * (1.5 - 0.5 * x * y * y);
    y = y * (1.5 - 0.5 * x * y * y);
    x * y
}

#[inline]
pub fn omega_sin_f32(x: f32) -> f32 {
    const INV_TWO_PI: f32 = 0.159154943;
    const TWO_PI: f32 = 6.283185307;
    const PI: f32 = 3.141592654;
    const HALF_PI: f32 = 1.570796327;
    let k = (x * INV_TWO_PI) as i32;
    let mut r = x - (k as f32) * TWO_PI;
    while r > PI {
        r -= TWO_PI;
    }
    while r < -PI {
        r += TWO_PI;
    }
    if r > HALF_PI {
        r = PI - r;
    } else if r < -HALF_PI {
        r = -PI - r;
    }
    let r2 = r * r;
    r * (1.0 + r2 * (-0.16666667 + r2 * (0.0083333310 - r2 * (0.00019841270 - r2 * 0.0000027557319))))
}

#[inline]
pub fn omega_cos_f32(x: f32) -> f32 {
    omega_sin_f32(x + 1.570796327)
}

#[inline]
pub fn omega_exp_neg_f32(mut x: f32) -> f32 {
    if x <= 0.0 {
        return 1.0;
    }
    if x >= 16.0 {
        return 0.0;
    }
    let mut steps = 0u32;
    while x > 0.5 {
        x *= 0.5;
        steps += 1;
    }
    let x2 = x * x;
    let x3 = x2 * x;
    let num = 1.0 - 0.5 * x + 0.1 * x2 - (1.0 / 120.0) * x3;
    let den = 1.0 + 0.5 * x + 0.1 * x2 + (1.0 / 120.0) * x3;
    let mut e = num / den;
    while steps > 0 {
        e *= e;
        steps -= 1;
    }
    e
}

#[inline]
unsafe fn crc32_ieee_ptr(ptr_bytes: *const u8, len: usize) -> u32 {
    let mut crc: u32 = 0xFFFF_FFFF;
    let mut i = 0usize;
    while i < len {
        crc ^= (*ptr_bytes.add(i)) as u32;
        let mut bit = 0;
        while bit < 8 {
            let mask = (crc & 1).wrapping_neg();
            crc = (crc >> 1) ^ (0xEDB8_8320 & mask);
            bit += 1;
        }
        i += 1;
    }
    !crc
}

#[inline]
unsafe fn build_mutex_name_utf16(map_utf16: *const u16, dst: *mut u16, cap: usize) {
    // Prefix: "Local\OmegaTriMutex_"
    let pfx = b"Local\\OmegaTriMutex_";
    let mut w = 0usize;
    while w < pfx.len() && w + 1 < cap {
        *dst.add(w) = pfx[w] as u16;
        w += 1;
    }
    let mut r = 0usize;
    while w + 1 < cap {
        let ch = *map_utf16.add(r);
        if ch == 0 {
            break;
        }
        let clean = if ch == (b'\\' as u16) || ch == (b'/' as u16) || ch == (b':' as u16) {
            b'_' as u16
        } else {
            ch
        };
        *dst.add(w) = clean;
        w += 1;
        r += 1;
    }
    *dst.add(w) = 0;
}

#[inline]
fn is_stamp_alive(now32: u32, stamp32: u32, ttl_ms: u32) -> bool {
    if stamp32 == 0 {
        return false;
    }
    now32.wrapping_sub(stamp32) <= ttl_ms
}

// =============================================================================
// 1. OmegaBraidRE v1.4.0 Bounds-Safe AOB + RIP-Relative [disp32] Singleton Scanner
// =============================================================================
#[no_mangle]
pub unsafe extern "C" fn OmegaSkseScanRipSingleton(
    module_base: *const u8,
    module_size: usize,
    pattern: *const u8,
    mask: *const u8,
    pat_len: usize,
    disp_offset: usize,
    instr_len: usize,
) -> usize {
    if module_base.is_null()
        || pattern.is_null()
        || mask.is_null()
        || pat_len == 0
        || pat_len > 64
        || module_size < pat_len
        || disp_offset + 4 > instr_len
        || instr_len > module_size
    {
        return 0;
    }
    let limit = module_size - pat_len;
    let mut pos = 0usize;
    while pos <= limit {
        let mut ok = true;
        let mut k = 0usize;
        while k < pat_len {
            if *mask.add(k) != 0 {
                if ptr::read_volatile(module_base.add(pos + k)) != *pattern.add(k) {
                    ok = false;
                    break;
                }
            }
            k += 1;
        }
        if ok {
            if pos + instr_len > module_size {
                return 0;
            }
            let b0 = ptr::read_volatile(module_base.add(pos + disp_offset)) as u32;
            let b1 = ptr::read_volatile(module_base.add(pos + disp_offset + 1)) as u32;
            let b2 = ptr::read_volatile(module_base.add(pos + disp_offset + 2)) as u32;
            let b3 = ptr::read_volatile(module_base.add(pos + disp_offset + 3)) as u32;
            let disp32 = (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24)) as i32;
            let next_ip = (module_base as usize).wrapping_add(pos + instr_len);
            let target_va = (next_ip as isize).wrapping_add(disp32 as isize) as usize;
            return target_va;
        }
        pos += 1;
    }
    0
}

// =============================================================================
// 2. Cellular Sheaf H^1(X; F) = 0 Quest Consistency Verifier
//    Returns +2 (Spectre Mode 3 Authorized), +1 (Consistent Exterior),
//    or -1 (Coboundary Defect H^1 != 0 -> Quarantine & 5D Penrose Restitch)
// =============================================================================
#[no_mangle]
pub unsafe extern "C" fn OmegaSkseEvaluateSheafGuardH1(
    stage_mask: u32,
    required_edges_packed: *const u16,
    edge_count: u32,
    in_spectre_cell: u32,
    out_coboundary_defect: *mut u32,
) -> i32 {
    let (edges_ptr, count) = if required_edges_packed.is_null() || edge_count == 0 {
        (DEFAULT_SHEAF_EDGES.as_ptr(), 4usize)
    } else {
        let clamped = if edge_count > 64 { 64usize } else { edge_count as usize };
        (required_edges_packed, clamped)
    };

    let mut defect = 0u32;
    let mut i = 0usize;
    while i < count {
        let e = *edges_ptr.add(i);
        let u = (stage_mask >> ((e & 0x0Fu16) as u32)) & 1u32;
        let w = (stage_mask >> (((e >> 4) & 0x0Fu16) as u32)) & 1u32;
        let rel = (e >> 8) & 0x0Fu16;
        if rel == 0 {
            if w == 1 && u == 0 {
                defect += 1;
            }
        } else if rel == 1 {
            if u == 1 && w == 1 {
                defect += 1;
            }
        } else if rel == 2 {
            if u != w {
                defect += 1;
            }
        }
        i += 1;
    }

    if !out_coboundary_defect.is_null() {
        *out_coboundary_defect = defect;
    }
    let state = if defect != 0 {
        -1i32
    } else if in_spectre_cell != 0 {
        2i32
    } else {
        1i32
    };
    G_LAST_H1_STATE.store(state, Ordering::Relaxed);
    state
}

// =============================================================================
// 3. Skyrim AE Weather FormID -> Sovngarde Phason Aurora & Solar Direction
// =============================================================================
#[no_mangle]
pub unsafe extern "C" fn OmegaSkseClassifyWeatherAurora(
    weather_form_id: u32,
    weather_class: u32,
    game_hour: f32,
    out_sun_xyz_w: *mut f32,
) -> f32 {
    let base_id = weather_form_id & 0x00FF_FFFF;
    let is_sovngarde = base_id == 0x0010_A243
        || base_id == 0x0010_A244
        || base_id == 0x0009_23FD
        || base_id == 0x0010_A240;
    let is_aurora_clear = base_id == 0x0010_E1F2
        || base_id == 0x0010_E1F0
        || base_id == 0x0010_A232
        || base_id == 0x0001_5E17;

    let mut hour = game_hour;
    if hour < 0.0 {
        hour = 0.0;
    } else if hour > 24.0 {
        hour = 24.0;
    }
    // Solar azimuth/elevation from 24h Tamriel clock (noon = +Y peak)
    let sun_ang = (hour - 6.0) * (3.141592654 / 12.0);
    let sx = omega_cos_f32(sun_ang) * 0.78;
    let sy = omega_sin_f32(sun_ang);
    let sz = -0.46;
    let norm = omega_sqrt_f32(sx * sx + sy * sy + sz * sz);
    let inv_n = if norm > 1e-5 { 1.0 / norm } else { 1.0 };

    let night_factor = if sy < 0.08 {
        let nf = (0.08 - sy) * 1.45;
        if nf > 1.0 { 1.0 } else { nf }
    } else {
        0.18
    };

    let aurora = if is_sovngarde {
        1.42
    } else if is_aurora_clear {
        0.62 + 0.63 * night_factor
    } else if weather_class == 3 {
        // Snow / Blizzard -> charged high-altitude aurora veil
        0.58 + 0.30 * night_factor
    } else if weather_class == 2 {
        // Rain / Storm
        0.42 + 0.22 * night_factor
    } else if weather_class == 1 {
        0.48 + 0.34 * night_factor
    } else {
        0.35 + 0.65 * night_factor
    };

    let fog_ext = if is_sovngarde {
        0.010
    } else if is_aurora_clear {
        0.011
    } else if weather_class == 3 {
        0.024
    } else if weather_class == 2 {
        0.020
    } else if weather_class == 1 {
        0.016
    } else {
        0.013
    };

    if !out_sun_xyz_w.is_null() {
        let (cx_l, cy_l, cz_l) = if is_sovngarde {
            (0.42f32, 0.78f32, -0.46f32)
        } else {
            let raw_x = sx * inv_n;
            let raw_y = sy * inv_n;
            let raw_z = sz * inv_n;
            let lift_y = if raw_y < 0.36 {
                let moon_y = 0.36 + (-raw_y) * 0.28;
                if moon_y > 0.68 { 0.68 } else { moon_y }
            } else {
                raw_y
            };
            (raw_x, lift_y, raw_z)
        };
        let l_norm = omega_sqrt_f32(cx_l * cx_l + cy_l * cy_l + cz_l * cz_l);
        let inv_ln = if l_norm > 1e-5 { 1.0 / l_norm } else { 1.0 };
        *out_sun_xyz_w.add(0) = cx_l * inv_ln;
        *out_sun_xyz_w.add(1) = cy_l * inv_ln;
        *out_sun_xyz_w.add(2) = cz_l * inv_ln;
        *out_sun_xyz_w.add(3) = fog_ext;
    }
    G_LAST_WEATHER_ID.store(weather_form_id, Ordering::Relaxed);
    aurora
}

#[inline]
fn map_shout_parameters(shout_form_id: u32) -> (f32, f32) {
    let base = shout_form_id & 0x00FF_FFFF;
    if base == 0 {
        return (0.0, 9.0);
    }
    if base == 0x0001_3E07 || base == 0x0001_3F3A || base == 0x0007_0314 {
        // Unrelenting Force (Fus Ro Dah)
        (1.65, 11.5)
    } else if base == 0x0002_F7BA || base == 0x0003_F9EA {
        // Fire Breath (Yol Toor Shul) -> 15.965 Hz resonance
        (1.48, 15.965)
    } else if base == 0x0004_4250 || base == 0x0006_029A {
        // Storm Call (Strun Bah Qo)
        (1.80, 7.83)
    } else if base == 0x0003_2921 {
        // Clear Skies (Lok Vah Koor)
        (1.28, 5.50)
    } else {
        // Generic Spell / Dragon Shout impulse
        (1.32, 9.60)
    }
}

// =============================================================================
// 4. Push Skyrim AE World-Space Camera + Weather + Thu'um + H^1=0 Guard -> MMF
// =============================================================================
#[no_mangle]
pub unsafe extern "C" fn OmegaSksePushWorldState(
    map_name_utf16: *const u16,
    cam_skyrim_xyz: *const f32,
    cam_matrix3x3: *const f32,
    weather_form_id: u32,
    weather_class: u32,
    game_hour: f32,
    shout_form_id: u32,
    shout_origin_skyrim_xyz: *const f32,
    shout_age_sec: f32,
    quest_stage_mask: u32,
    in_spectre_cell: u32,
) -> u32 {
    let use_persist = map_name_utf16.is_null();
    let target_name = if use_persist {
        DEFAULT_MMF_UTF16.as_ptr()
    } else {
        map_name_utf16
    };

    let (h_mtx, h_map, view) = if use_persist {
        let mut pm = G_PERSIST_MTX.load(Ordering::Acquire);
        if pm.is_null() {
            let mut mutex_name = [0u16; 96];
            build_mutex_name_utf16(target_name, mutex_name.as_mut_ptr(), 96);
            let nm = CreateMutexW(ptr::null_mut(), 0, mutex_name.as_ptr());
            if nm.is_null() {
                return 0;
            }
            if G_PERSIST_MTX
                .compare_exchange(ptr::null_mut(), nm, Ordering::AcqRel, Ordering::Acquire)
                .is_ok()
            {
                pm = nm;
            } else {
                CloseHandle(nm);
                pm = G_PERSIST_MTX.load(Ordering::Acquire);
            }
        }
        let mut pv = G_PERSIST_VIEW.load(Ordering::Acquire);
        if pv.is_null() {
            let nh = CreateFileMappingW(
                (-1isize) as *mut c_void,
                ptr::null_mut(),
                PAGE_READWRITE,
                0,
                TOTAL_MAP_BYTES as u32,
                target_name,
            );
            if nh.is_null() {
                return 0;
            }
            let nv = MapViewOfFile(nh, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, TOTAL_MAP_BYTES);
            if nv.is_null() {
                CloseHandle(nh);
                return 0;
            }
            if G_PERSIST_VIEW
                .compare_exchange(ptr::null_mut(), nv, Ordering::AcqRel, Ordering::Acquire)
                .is_ok()
            {
                G_PERSIST_MAP.store(nh, Ordering::Release);
                pv = nv;
            } else {
                UnmapViewOfFile(nv as *const c_void);
                CloseHandle(nh);
                pv = G_PERSIST_VIEW.load(Ordering::Acquire);
            }
        }
        (pm, G_PERSIST_MAP.load(Ordering::Acquire), pv)
    } else {
        let mut mutex_name = [0u16; 96];
        build_mutex_name_utf16(target_name, mutex_name.as_mut_ptr(), 96);
        let m = CreateMutexW(ptr::null_mut(), 0, mutex_name.as_ptr());
        if m.is_null() {
            return 0;
        }
        let hm = CreateFileMappingW(
            (-1isize) as *mut c_void,
            ptr::null_mut(),
            PAGE_READWRITE,
            0,
            TOTAL_MAP_BYTES as u32,
            target_name,
        );
        if hm.is_null() {
            CloseHandle(m);
            return 0;
        }
        let vw = MapViewOfFile(hm, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, TOTAL_MAP_BYTES);
        if vw.is_null() {
            CloseHandle(hm);
            CloseHandle(m);
            return 0;
        }
        (m, hm, vw)
    };

    if h_mtx.is_null() || view.is_null() {
        return 0;
    }

    let wait_rc = WaitForSingleObject(h_mtx, 12);
    if wait_rc != 0 && wait_rc != 0x0000_0080 {
        if !use_persist {
            UnmapViewOfFile(view as *const c_void);
            CloseHandle(h_map);
            CloseHandle(h_mtx);
        }
        return 0;
    }

    // Read existing 80 floats (or zero-initialize if brand new)
    let mut u80 = [0.0f32; 80];
    let magic_now = ptr::read_volatile((view.add(TRAILER_OFFSET)) as *const u32);
    if magic_now == MAGIC_OTRI {
        let src_f = view as *const f32;
        let mut k = 0usize;
        while k < 80 {
            u80[k] = ptr::read_volatile(src_f.add(k));
            k += 1;
        }
    } else {
        u80[0] = 1920.0;
        u80[1] = 1080.0;
        u80[3] = 0.6249;
        u80[27] = 1.0;
        u80[37] = 0.85;
        u80[60] = 1.0;
        u80[61] = 1.0;
        u80[62] = 0.85;
        u80[63] = 1.54;
        u80[75] = 1.12;
        u80[79] = 0.35;
        let mut z = 24usize;
        while z < 64 {
            ptr::write_volatile(view.add(TRAILER_OFFSET + z) as *mut u32, 0);
            z += 4;
        }
        ptr::write_volatile(view.add(TRAILER_OFFSET + 8) as *mut u64, 2);
    }

    // 1. Convert Skyrim Z-up world eye (X_east, Y_north, Z_up) -> DirectX Y-up meters (X, Z, Y) * 0.01428
    let (cx, cy, cz) = if !cam_skyrim_xyz.is_null() {
        let sx = *cam_skyrim_xyz.add(0);
        let sy = *cam_skyrim_xyz.add(1);
        let sz = *cam_skyrim_xyz.add(2);
        (sx * SKYRIM_UNIT_TO_METERS, sz * SKYRIM_UNIT_TO_METERS, sy * SKYRIM_UNIT_TO_METERS)
    } else {
        (0.0, 2.35, -10.2)
    };

    // 2. Orthonormalize Skyrim 3x3 NiMatrix3 (col0=Right, col1=Forward, col2=Up in Z-up) -> DX Y-up Forward & Right
    let (mut fx, mut fy, mut fz, mut rx, mut ry, mut rz) = if !cam_matrix3x3.is_null() {
        // Row-major NiMatrix3:
        // col0 (Skyrim Right)   = (m[0], m[3], m[6]) -> DX (m[0], m[6], m[3])
        // col1 (Skyrim Forward) = (m[1], m[4], m[7]) -> DX (m[1], m[7], m[4])
        let r_dx_x = *cam_matrix3x3.add(0);
        let r_dx_y = *cam_matrix3x3.add(6);
        let r_dx_z = *cam_matrix3x3.add(3);
        let f_dx_x = *cam_matrix3x3.add(1);
        let f_dx_y = *cam_matrix3x3.add(7);
        let f_dx_z = *cam_matrix3x3.add(4);
        (f_dx_x, f_dx_y, f_dx_z, r_dx_x, r_dx_y, r_dx_z)
    } else {
        (0.0, 0.0, 1.0, 1.0, 0.0, 0.0)
    };

    let f_len = omega_sqrt_f32(fx * fx + fy * fy + fz * fz);
    if f_len > 1e-5 {
        let inv_f = 1.0 / f_len;
        fx *= inv_f;
        fy *= inv_f;
        fz *= inv_f;
    } else {
        fx = 0.0;
        fy = 0.0;
        fz = 1.0;
    }
    // Gram-Schmidt orthogonalization: r = r - <r, f> f
    let rf_dot = rx * fx + ry * fy + rz * fz;
    rx -= rf_dot * fx;
    ry -= rf_dot * fy;
    rz -= rf_dot * fz;
    let r_len = omega_sqrt_f32(rx * rx + ry * ry + rz * rz);
    if r_len > 1e-5 {
        let inv_r = 1.0 / r_len;
        rx *= inv_r;
        ry *= inv_r;
        rz *= inv_r;
    } else {
        rx = 1.0;
        ry = 0.0;
        rz = 0.0;
    }

    // 3. Weather -> Sovngarde Aurora + Celestial Sun Direction
    let mut sun4 = [0.42f32, 0.78, -0.46, 0.0];
    let weather_aurora = OmegaSkseClassifyWeatherAurora(weather_form_id, weather_class, game_hour, sun4.as_mut_ptr());

    // 4. Dragon Shout / Spell Cymatic Wavefront -> U[13]..U[14]
    let (peak_str, freq_hz) = map_shout_parameters(shout_form_id);
    let age = if shout_age_sec < 0.0 { 0.0 } else { shout_age_sec };
    let (ox, oy, oz) = if !shout_origin_skyrim_xyz.is_null() {
        (
            *shout_origin_skyrim_xyz.add(0) * SKYRIM_UNIT_TO_METERS,
            *shout_origin_skyrim_xyz.add(2) * SKYRIM_UNIT_TO_METERS,
            *shout_origin_skyrim_xyz.add(1) * SKYRIM_UNIT_TO_METERS,
        )
    } else {
        (cx + fx * 1.8, cy, cz + fz * 1.8)
    };
    let live_str = if peak_str > 0.0 && age < 8.0 {
        peak_str * omega_exp_neg_f32(age * 0.55)
    } else {
        0.0
    };
    let wave_radius = if peak_str > 0.0 && age < 8.0 {
        0.45 + age * 14.5
    } else {
        0.0
    };

    // 5. Cellular Sheaf H^1(X; F) = 0 Quest Guard -> U[16].w & Spectre Mode 3 Lock
    let mut defect = 0u32;
    let h1_state = OmegaSkseEvaluateSheafGuardH1(
        quest_stage_mask,
        ptr::null(),
        0,
        in_spectre_cell,
        &mut defect,
    );

    let tick64 = GetTickCount64();
    let now32 = (tick64 as u32) | 1u32;
    ptr::write_volatile(view.add(TRAILER_OFFSET + 60) as *mut u32, now32);

    let edge_bits = ptr::read_volatile(view.add(TRAILER_OFFSET + 24) as *const u32) & (MASK_PIXEL10_LIVE | MASK_PIXEL8_LIVE);
    let t_tn = ptr::read_volatile(view.add(TRAILER_OFFSET + 28) as *const u32);
    let t_en = ptr::read_volatile(view.add(TRAILER_OFFSET + 32) as *const u32);
    let t_sy = ptr::read_volatile(view.add(TRAILER_OFFSET + 36) as *const u32);
    let t_pn = ptr::read_volatile(view.add(TRAILER_OFFSET + 40) as *const u32);
    let t_av = ptr::read_volatile(view.add(TRAILER_OFFSET + 44) as *const u32);
    let t_rs = ptr::read_volatile(view.add(TRAILER_OFFSET + 48) as *const u32);

    let mut live_mask = MASK_SKSE64_BRIDGE;
    if is_stamp_alive(now32, t_en, 1500) { live_mask |= MASK_OMEGA_ENGINE; }
    if is_stamp_alive(now32, t_sy, 1500) { live_mask |= MASK_OMEGA_SYNTH; }
    if is_stamp_alive(now32, t_pn, 1500) { live_mask |= MASK_OMEGA_PENROSE; }
    if is_stamp_alive(now32, t_av, 1500) { live_mask |= MASK_OMEGA_AV; }
    if is_stamp_alive(now32, t_rs, 1500) { live_mask |= MASK_RESHADE_ADDON; }
    if is_stamp_alive(now32, t_tn, 4500) { live_mask |= MASK_OMEGA_TENSOR | edge_bits; }

    if (live_mask & MASK_OMEGA_ENGINE) == 0 {
        let boot0 = G_BOOT_TICK64.load(Ordering::Relaxed);
        let elapsed_s = if boot0 > 0 && tick64 >= boot0 {
            ((tick64 - boot0) as f32) * 0.001
        } else {
            u80[2] + 0.016
        };
        u80[2] = elapsed_s;
        u80[4] = cx;
        u80[5] = cy;
        u80[6] = cz;
    }

    if h1_state == 2 {
        u80[36] = 3.0; // Authorized Chiral Spectre (1,1) Dungeon!
    } else if h1_state < 0 && u80[36] == 3.0 {
        u80[36] = 0.0; // H^1 != 0 Paradox Quarantined -> Restitch to 5D Penrose P3!
    }

    u80[47] = live_mask as f32;
    // U[13] = (ImpulseOrigX, ImpulseOrigY, ImpulseOrigZ, ImpulseAgeSec)
    u80[52] = ox;
    u80[53] = oy;
    u80[54] = oz;
    u80[55] = age;
    // U[14] = (ImpulseLiveStrength, ImpulseRadius, ImpulseFreq, WeatherAurora)
    u80[56] = live_str;
    u80[57] = wave_radius;
    u80[58] = freq_hz;
    u80[59] = weather_aurora;
    // U[16].w = Cellular Sheaf H^1 Guard State (+2 Spectre, +1 Exterior, -1 Quarantined)
    u80[67] = h1_state as f32;
    // U[17] = (SunX, SunY, SunZ, FogExtinctionMetersInv)
    u80[68] = sun4[0];
    u80[69] = sun4[1];
    u80[70] = sun4[2];
    u80[71] = sun4[3];
    // U[18].xyz = World-Space Camera Forward
    u80[72] = fx;
    u80[73] = fy;
    u80[74] = fz;
    if u80[75] <= 0.05 {
        u80[75] = 1.12;
    }
    // U[19].xyz = World-Space Camera Right
    u80[76] = rx;
    u80[77] = ry;
    u80[78] = rz;

    // Commit via odd -> even Seqlock + IEEE CRC-32
    let seq_atom = view.add(TRAILER_OFFSET + 8) as *const AtomicU64;
    let mut base_seq = (*seq_atom).load(Ordering::Relaxed);
    if (base_seq & 1) != 0 {
        base_seq = base_seq.wrapping_add(1);
    }
    let odd_seq = base_seq.wrapping_add(1);
    let even_seq = base_seq.wrapping_add(2);

    let crc = crc32_ieee_ptr(u80.as_ptr() as *const u8, UNIFORM_BYTES);
    (*seq_atom).store(odd_seq, Ordering::Release);

    let dst_f = view as *mut f32;
    let mut j = 0usize;
    while j < 80 {
        ptr::write_volatile(dst_f.add(j), u80[j]);
        j += 1;
    }
    ptr::write_volatile(view.add(TRAILER_OFFSET + 0) as *mut u32, MAGIC_OTRI);
    ptr::write_volatile(view.add(TRAILER_OFFSET + 4) as *mut u32, CONTRACT_VERSION);
    ptr::write_volatile(view.add(TRAILER_OFFSET + 16) as *mut u32, live_mask);
    ptr::write_volatile(view.add(TRAILER_OFFSET + 20) as *mut u32, crc);
    (*seq_atom).store(even_seq, Ordering::Release);

    ReleaseMutex(h_mtx);
    if !use_persist {
        UnmapViewOfFile(view as *const c_void);
        CloseHandle(h_map);
        CloseHandle(h_mtx);
    }

    G_FRAMES_PUSHED.fetch_add(1, Ordering::Relaxed);
    live_mask
}

// =============================================================================
// 5. Parse Creation Kit / Papyrus UserLog Line:
//    "OTW|x|y|z|yawDeg|pitchDeg|weatherDecOrHex|weatherClass|gameHour|shoutId|questMask|spectreFlag"
// =============================================================================
unsafe fn parse_f32_token(buf: *const u8, start: usize, end: usize) -> f32 {
    if start >= end {
        return 0.0;
    }
    let mut i = start;
    while i < end && *buf.add(i) == b' ' {
        i += 1;
    }
    let mut neg = false;
    if i < end && *buf.add(i) == b'-' {
        neg = true;
        i += 1;
    } else if i < end && *buf.add(i) == b'+' {
        i += 1;
    }
    let mut int_part = 0.0f32;
    while i < end {
        let c = *buf.add(i);
        if c >= b'0' && c <= b'9' {
            int_part = int_part * 10.0 + ((c - b'0') as f32);
            i += 1;
        } else {
            break;
        }
    }
    let mut frac_part = 0.0f32;
    let mut scale = 0.1f32;
    if i < end && *buf.add(i) == b'.' {
        i += 1;
        while i < end {
            let c = *buf.add(i);
            if c >= b'0' && c <= b'9' {
                frac_part += ((c - b'0') as f32) * scale;
                scale *= 0.1;
                i += 1;
            } else {
                break;
            }
        }
    }
    let val = int_part + frac_part;
    if neg { -val } else { val }
}

unsafe fn parse_u32_token(buf: *const u8, start: usize, end: usize) -> u32 {
    if start >= end {
        return 0;
    }
    let mut i = start;
    while i < end && *buf.add(i) == b' ' {
        i += 1;
    }
    if i + 2 <= end && *buf.add(i) == b'0' && (*buf.add(i + 1) == b'x' || *buf.add(i + 1) == b'X') {
        i += 2;
        let mut v = 0u32;
        while i < end {
            let c = *buf.add(i);
            let d = if c >= b'0' && c <= b'9' {
                (c - b'0') as u32
            } else if c >= b'a' && c <= b'f' {
                10 + ((c - b'a') as u32)
            } else if c >= b'A' && c <= b'F' {
                10 + ((c - b'A') as u32)
            } else {
                break;
            };
            v = (v << 4) | d;
            i += 1;
        }
        return v;
    }
    let mut v = 0u32;
    while i < end {
        let c = *buf.add(i);
        if c >= b'0' && c <= b'9' {
            v = v.wrapping_mul(10).wrapping_add((c - b'0') as u32);
            i += 1;
        } else {
            break;
        }
    }
    v
}

#[no_mangle]
pub unsafe extern "C" fn OmegaSkseParsePapyrusTraceLine(
    line_ascii: *const u8,
    line_len: usize,
    map_name_utf16: *const u16,
) -> u32 {
    if line_ascii.is_null() || line_len < 16 {
        return 0;
    }
    // Locate "OTW|" marker anywhere inside the Papyrus log line
    let mut marker = line_len;
    let mut p = 0usize;
    while p + 4 <= line_len {
        if *line_ascii.add(p) == b'O'
            && *line_ascii.add(p + 1) == b'T'
            && *line_ascii.add(p + 2) == b'W'
            && *line_ascii.add(p + 3) == b'|'
        {
            marker = p + 4;
            break;
        }
        p += 1;
    }
    if marker >= line_len {
        return 0;
    }

    let mut starts = [0usize; 12];
    let mut ends = [0usize; 12];
    let mut tok_count = 0usize;
    let mut cur = marker;
    let mut i = marker;
    while i <= line_len && tok_count < 11 {
        let is_sep = if i == line_len {
            true
        } else {
            let b = *line_ascii.add(i);
            b == b'|' || b == b'\r' || b == b'\n'
        };
        if is_sep {
            starts[tok_count] = cur;
            ends[tok_count] = i;
            tok_count += 1;
            let b = if i < line_len { *line_ascii.add(i) } else { b'\n' };
            if b == b'\r' || b == b'\n' {
                break;
            }
            cur = i + 1;
        }
        i += 1;
    }
    if tok_count < 11 {
        return 0;
    }

    let sky_x = parse_f32_token(line_ascii, starts[0], ends[0]);
    let sky_y = parse_f32_token(line_ascii, starts[1], ends[1]);
    let sky_z = parse_f32_token(line_ascii, starts[2], ends[2]);
    let yaw_deg = parse_f32_token(line_ascii, starts[3], ends[3]);
    let pitch_deg = parse_f32_token(line_ascii, starts[4], ends[4]);
    let weather_id = parse_u32_token(line_ascii, starts[5], ends[5]);
    let weather_cls = parse_u32_token(line_ascii, starts[6], ends[6]);
    let game_hour = parse_f32_token(line_ascii, starts[7], ends[7]);
    let shout_id = parse_u32_token(line_ascii, starts[8], ends[8]);
    let quest_mask = parse_u32_token(line_ascii, starts[9], ends[9]);
    let spectre_flag = parse_u32_token(line_ascii, starts[10], ends[10]);

    // Creation Engine Heading = clockwise from +Y (North); Pitch = positive down -> negate for +Up
    let yaw = yaw_deg * (3.141592654 / 180.0);
    let pitch = -pitch_deg * (3.141592654 / 180.0);
    let cy = omega_cos_f32(yaw);
    let sy = omega_sin_f32(yaw);
    let cp = omega_cos_f32(pitch);
    let sp = omega_sin_f32(pitch);

    // Construct Skyrim Z-up 3x3 NiMatrix3 (col0=Right, col1=Forward, col2=Up):
    // Forward_sky = (sy*cp, cy*cp, sp), Right_sky = (cy, -sy, 0)
    let m3x3: [f32; 9] = [
        cy,  sy * cp, 0.0,
        -sy, cy * cp, 0.0,
        0.0, sp,      1.0,
    ];
    let pos_sky: [f32; 3] = [sky_x, sky_y, sky_z];
    let shout_age = if shout_id != 0 { 0.15 } else { 99.0 };

    OmegaSksePushWorldState(
        map_name_utf16,
        pos_sky.as_ptr(),
        m3x3.as_ptr(),
        weather_id,
        weather_cls,
        game_hour,
        shout_id,
        pos_sky.as_ptr(),
        shout_age,
        quest_mask,
        spectre_flag,
    )
}

unsafe fn build_env_userlog_candidate(env_name: *const u16, dst: *mut u16) -> bool {
    let n = GetEnvironmentVariableW(env_name, dst, 200) as usize;
    if n == 0 || n >= 200 {
        *dst = 0;
        return false;
    }
    let suffix = b"\\Documents\\My Games\\Skyrim Special Edition\\Logs\\Script\\User\\OmegaTriEngine_WorldBridge.0.log";
    let mut i = 0usize;
    while i < suffix.len() && (n + i + 1) < 320 {
        *dst.add(n + i) = suffix[i] as u16;
        i += 1;
    }
    *dst.add(n + i) = 0;
    true
}

unsafe fn init_userlog_path() -> bool {
    let p_primary = ptr::addr_of_mut!(G_LOG_PATH_UTF16) as *mut u16;
    let p_alt = ptr::addr_of_mut!(G_LOG_PATH_ALT_UTF16) as *mut u16;
    let has_od = build_env_userlog_candidate(ENV_ONEDRIVE_UTF16.as_ptr(), p_primary);
    let has_up = build_env_userlog_candidate(ENV_USERPROFILE_UTF16.as_ptr(), if has_od { p_alt } else { p_primary });
    has_od || has_up
}

unsafe fn poll_papyrus_userlog_once() -> bool {
    let path_ptr = ptr::addr_of!(G_LOG_PATH_UTF16) as *const u16;
    if *path_ptr == 0 && !init_userlog_path() {
        return false;
    }
    let mut hf = CreateFileW(
        path_ptr,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        ptr::null_mut(),
        OPEN_EXISTING,
        0,
        ptr::null_mut(),
    );
    if hf.is_null() || (hf as isize) == -1 {
        let alt_ptr = ptr::addr_of!(G_LOG_PATH_ALT_UTF16) as *const u16;
        if *alt_ptr != 0 {
            hf = CreateFileW(
                alt_ptr,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                ptr::null_mut(),
                OPEN_EXISTING,
                0,
                ptr::null_mut(),
            );
        }
    }
    if hf.is_null() || (hf as isize) == -1 {
        return false;
    }
    let fsize = GetFileSize(hf, ptr::null_mut());
    if fsize == 0xFFFF_FFFF || fsize < 16 {
        CloseHandle(hf);
        return false;
    }
    let prev_size = G_LAST_LOG_SIZE.swap(fsize, Ordering::Relaxed);
    if fsize == prev_size {
        CloseHandle(hf);
        return false;
    }
    let tail_len = if fsize > 480 { 480u32 } else { fsize };
    let seek_off = (fsize - tail_len) as i32;
    SetFilePointer(hf, seek_off, ptr::null_mut(), 0);
    let buf_ptr = ptr::addr_of_mut!(G_LOG_TAIL_BYTES) as *mut u8;
    let mut got = 0u32;
    let ok_r = ReadFile(hf, buf_ptr, tail_len, &mut got, ptr::null_mut());
    CloseHandle(hf);
    if ok_r == 0 || got < 16 {
        return false;
    }

    // Find the latest "OTW|" line from the tail end
    let n = got as usize;
    let mut pos = n - 4;
    loop {
        if *buf_ptr.add(pos) == b'O'
            && *buf_ptr.add(pos + 1) == b'T'
            && *buf_ptr.add(pos + 2) == b'W'
            && *buf_ptr.add(pos + 3) == b'|'
        {
            let mask = OmegaSkseParsePapyrusTraceLine(buf_ptr.add(pos), n - pos, ptr::null());
            return mask != 0;
        }
        if pos == 0 {
            break;
        }
        pos -= 1;
    }
    false
}

unsafe extern "system" fn skse_bridge_worker_proc(_param: *mut c_void) -> u32 {
    let boot = GetTickCount64();
    G_BOOT_TICK64.store(boot, Ordering::Relaxed);
    let mut tick_idx = 0u32;
    while !G_WORKER_STOP.load(Ordering::Acquire) {
        let mut pushed_from_log = false;
        if (tick_idx & 7) == 0 {
            pushed_from_log = poll_papyrus_userlog_once();
        }
        if !pushed_from_log {
            // Keep SKSE64 heartbeat & gentle Sovngarde ambient camera pose alive while in menus or before Papyrus ticks
            let now = GetTickCount64();
            let t = ((now.wrapping_sub(boot)) as f32) * 0.001;
            let yaw = t * 0.12;
            let cy = omega_cos_f32(yaw);
            let sy = omega_sin_f32(yaw);
            let pos_sky = [sy * 680.0, -cy * 680.0, 185.0];
            let m3x3 = [
                cy,  sy,  0.0,
                -sy, cy,  0.0,
                0.0, 0.0, 1.0,
            ];
            let wid = G_LAST_WEATHER_ID.load(Ordering::Relaxed);
            let _ = OmegaSksePushWorldState(
                ptr::null(),
                pos_sky.as_ptr(),
                m3x3.as_ptr(),
                wid,
                0,
                22.5,
                0,
                ptr::null(),
                99.0,
                0x0000_0027,
                0,
            );
        }
        tick_idx = tick_idx.wrapping_add(1);
        Sleep(16);
    }
    G_WORKER_RUNNING.store(false, Ordering::Release);
    0
}

unsafe fn start_worker_if_needed() {
    if G_WORKER_RUNNING
        .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
        .is_ok()
    {
        G_WORKER_STOP.store(false, Ordering::Release);
        let mut tid = 0u32;
        let th = CreateThread(ptr::null_mut(), 0, skse_bridge_worker_proc, ptr::null_mut(), 0, &mut tid);
        if !th.is_null() {
            G_WORKER_THREAD.store(th, Ordering::Release);
        } else {
            G_WORKER_RUNNING.store(false, Ordering::Release);
        }
    }
}

unsafe extern "C" fn on_skse_message(msg: *mut SkseMessage) {
    if msg.is_null() {
        return;
    }
    G_MSG_COUNT.fetch_add(1, Ordering::Relaxed);
    let mt = (*msg).msg_type;
    if mt == K_MSG_POST_LOAD || mt == K_MSG_DATA_LOADED || mt == K_MSG_POST_LOAD_GAME || mt == K_MSG_NEW_GAME {
        start_worker_if_needed();
    }
}

#[no_mangle]
pub unsafe extern "C" fn SKSEPlugin_Query(skse: *const SkseInterface, info: *mut SksePluginInfo) -> bool {
    if !info.is_null() {
        (*info).info_version = 1;
        (*info).name = OMEGA_SKSE_PLUGIN_VERSION.name.as_ptr();
        (*info).version = OMEGA_SKSE_PLUGIN_VERSION.plugin_version;
    }
    if skse.is_null() || (*skse).is_editor != 0 {
        return false;
    }
    true
}

#[no_mangle]
pub unsafe extern "C" fn SKSEPlugin_Load(skse: *const SkseInterface) -> bool {
    if skse.is_null() {
        return false;
    }
    if (*skse).is_editor != 0 {
        return false;
    }
    G_RUNTIME_VERSION.store((*skse).runtime_version, Ordering::Relaxed);

    let mut handle = 0u32;
    if let Some(get_h) = (*skse).get_plugin_handle {
        handle = get_h();
        G_PLUGIN_HANDLE.store(handle, Ordering::Relaxed);
    }

    let mut registered_msg = false;
    if let Some(query_fn) = (*skse).query_interface {
        let msg_ptr = query_fn(K_INTERFACE_MESSAGING) as *const SkseMessagingInterface;
        if !msg_ptr.is_null() {
            if let Some(reg_fn) = (*msg_ptr).register_listener {
                registered_msg = reg_fn(handle, b"SKSE\0".as_ptr(), on_skse_message);
            }
        }
    }

    // If loaded inside a minimal host without SKSEMessagingInterface, start worker directly
    if !registered_msg {
        start_worker_if_needed();
    }
    true
}

unsafe fn release_persist_bus_handles() {
    let pv = G_PERSIST_VIEW.swap(ptr::null_mut(), Ordering::AcqRel);
    if !pv.is_null() {
        UnmapViewOfFile(pv as *const c_void);
    }
    let pm = G_PERSIST_MAP.swap(ptr::null_mut(), Ordering::AcqRel);
    if !pm.is_null() {
        CloseHandle(pm);
    }
    let mx = G_PERSIST_MTX.swap(ptr::null_mut(), Ordering::AcqRel);
    if !mx.is_null() {
        CloseHandle(mx);
    }
}

#[no_mangle]
pub unsafe extern "C" fn OmegaSkseStopWorker() {
    G_WORKER_STOP.store(true, Ordering::Release);
    let th = G_WORKER_THREAD.swap(ptr::null_mut(), Ordering::AcqRel);
    if !th.is_null() {
        WaitForSingleObject(th, 250);
        CloseHandle(th);
    }
    G_WORKER_RUNNING.store(false, Ordering::Release);
    release_persist_bus_handles();
}

#[no_mangle]
pub unsafe extern "C" fn OmegaSkseGetDiagnostics(out_stats6: *mut u32) {
    if out_stats6.is_null() {
        return;
    }
    *out_stats6.add(0) = G_RUNTIME_VERSION.load(Ordering::Relaxed);
    *out_stats6.add(1) = G_PLUGIN_HANDLE.load(Ordering::Relaxed);
    *out_stats6.add(2) = G_MSG_COUNT.load(Ordering::Relaxed);
    *out_stats6.add(3) = G_FRAMES_PUSHED.load(Ordering::Relaxed);
    *out_stats6.add(4) = G_LAST_WEATHER_ID.load(Ordering::Relaxed);
    *out_stats6.add(5) = G_LAST_H1_STATE.load(Ordering::Relaxed) as u32;
}

#[cfg(not(test))]
#[no_mangle]
unsafe extern "system" fn DllMain(
    hinst_dll: *mut c_void,
    fdw_reason: u32,
    lpv_reserved: *mut c_void,
) -> i32 {
    if fdw_reason == DLL_PROCESS_ATTACH {
        if !hinst_dll.is_null() {
            DisableThreadLibraryCalls(hinst_dll);
        }
    } else if fdw_reason == DLL_PROCESS_DETACH {
        G_WORKER_STOP.store(true, Ordering::Release);
        if lpv_reserved.is_null() {
            let th = G_WORKER_THREAD.swap(ptr::null_mut(), Ordering::AcqRel);
            if !th.is_null() {
                WaitForSingleObject(th, 100);
                CloseHandle(th);
            }
            release_persist_bus_handles();
        }
    }
    1
}