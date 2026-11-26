#![no_std]
#![allow(unknown_lints, linker_messages)]
// =============================================================================
// OmegaTriEngine.addon64 v1.9.0 - Zero-CRT #![no_std] Win64 ReShade Add-on DLL
// SPDX-License-Identifier: BSD-3-Clause OR MIT
// Zero external crates. Zero MSVC CRT (imports ONLY KERNEL32.dll).
// Verified against ReShade 6.8.0 (API v20) reshade_api.hpp & namespacereshade:
//   - reshade::api::effect_runtime inherits device_object -> api_object ONLY
//     (NOT swapchain! Exact vtable slots across 5.x & 6.0..6.8+: [19/22/25/27/33])
//   - Supports both 384B (U0..U19) and 512B OTRI v1.8 (U0..U27 + TopBar + MenuFade)
//   - Measures microsecond Skyrim AE FPS & frametime via QueryPerformanceCounter
// =============================================================================

use core::ffi::c_void;
use core::ptr;
use core::sync::atomic::{AtomicBool, AtomicPtr, AtomicU32, AtomicU64, Ordering};

#[cfg(not(test))]
mod zero_crt {
    use core::ptr;

    #[panic_handler]
    fn omega_panic(_info: &core::panic::PanicInfo) -> ! {
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

#[repr(transparent)]
pub struct ExportCStr(pub *const u8);
unsafe impl Sync for ExportCStr {}

#[no_mangle]
pub static OMEGA_ADDON_EXPORT_NAME: ExportCStr = ExportCStr(b"OmegaTriEngine\0".as_ptr());

#[no_mangle]
pub static OMEGA_ADDON_EXPORT_DESC: ExportCStr = ExportCStr(
    b"Zero-CRT Win64 ReShade Add-on v1.9.0 streaming 512B Local\\OmegaTriEngineUniforms into U0..U27 + live QPC FPS (vtable[19/22/25/27/33]).\0"
        .as_ptr(),
);

#[no_mangle]
pub static OMEGA_ADDON_EXPORT_WEBSITE: ExportCStr = ExportCStr(
    b"https://github.com/bospaladin34-crypto/Omega_standalone_developments\0".as_ptr(),
);

#[no_mangle]
pub static OMEGA_ADDON_EXPORT_ISSUES: ExportCStr = ExportCStr(
    b"https://github.com/bospaladin34-crypto/Omega_standalone_developments/issues\0".as_ptr(),
);

#[no_mangle]
pub static OMEGA_ADDON_EXPORT_AUTHOR: ExportCStr =
    ExportCStr(b"ACT-Omega Unified Labs (BSD-3-Clause)\0".as_ptr());

const FILE_MAP_WRITE: u32 = 0x0002;
const FILE_MAP_READ: u32 = 0x0004;
const UNIFORM_BYTES: usize = 320;
const EXT_OFFSET: usize = 384;
const EXT_BYTES: usize = 128;
const MAX_U_SLOTS: usize = 28;
const MAX_FLOATS: usize = 112;
const MAGIC_OTRI: u32 = 0x4F54_5249;
const CONTRACT_V180: u32 = 0x0001_0800;
const DLL_PROCESS_DETACH: u32 = 0;
const DLL_PROCESS_ATTACH: u32 = 1;
const EV_DESTROY_EFFECT_RUNTIME: u32 = 11;

#[link(name = "kernel32")]
extern "system" {
    fn DisableThreadLibraryCalls(module: *mut c_void) -> i32;
    fn OpenFileMappingW(desired_access: u32, inherit_handle: i32, name: *const u16) -> *mut c_void;
    fn MapViewOfFile(
        file_mapping: *mut c_void,
        desired_access: u32,
        offset_high: u32,
        offset_low: u32,
        bytes_to_map: usize,
    ) -> *mut u8;
    fn UnmapViewOfFile(base_addr: *const c_void) -> i32;
    fn CloseHandle(handle: *mut c_void) -> i32;
    fn GetModuleHandleW(module_name: *const u16) -> *mut c_void;
    fn GetProcAddress(module: *mut c_void, proc_name: *const u8) -> *mut c_void;
    fn GetTickCount64() -> u64;
    fn QueryPerformanceCounter(lp_performance_count: *mut i64) -> i32;
    fn QueryPerformanceFrequency(lp_frequency: *mut i64) -> i32;
    fn GetCurrentProcess() -> *mut c_void;
    fn GetProcessHeap() -> *mut c_void;
    fn HeapAlloc(heap: *mut c_void, flags: u32, bytes: usize) -> *mut c_void;
    fn HeapFree(heap: *mut c_void, flags: u32, mem: *mut c_void) -> i32;
    fn K32EnumProcessModules(
        process: *mut c_void,
        modules: *mut *mut c_void,
        cb: u32,
        cb_needed: *mut u32,
    ) -> i32;
}

type FnReShadeRegisterAddon = unsafe extern "C" fn(*mut c_void, u32) -> bool;
type FnReShadeUnregisterAddon = unsafe extern "C" fn(*mut c_void);
type FnReShadeRegisterEvent = unsafe extern "C" fn(u32, *const c_void);
type FnReShadeUnregisterEvent = unsafe extern "C" fn(u32, *const c_void);

type FnEnumerateUniforms = unsafe extern "C" fn(
    runtime: *mut ReShadeEffectRuntime,
    effect_name: *const u8,
    callback: unsafe extern "C" fn(*mut ReShadeEffectRuntime, u64, *mut c_void),
    user_data: *mut c_void,
);
type FnGetUniformName = unsafe extern "C" fn(
    runtime: *const ReShadeEffectRuntime,
    variable: u64,
    name: *mut u8,
    name_size: *mut usize,
);
type FnGetAnnotationInt = unsafe extern "C" fn(
    runtime: *const ReShadeEffectRuntime,
    variable: u64,
    name: *const u8,
    values: *mut i32,
    count: usize,
    array_index: usize,
) -> bool;
type FnGetAnnotationString = unsafe extern "C" fn(
    runtime: *const ReShadeEffectRuntime,
    variable: u64,
    name: *const u8,
    value: *mut u8,
    value_size: *mut usize,
) -> bool;
type FnSetUniformFloat = unsafe extern "C" fn(
    runtime: *mut ReShadeEffectRuntime,
    variable: u64,
    values: *const f32,
    count: usize,
    array_index: usize,
);

#[repr(C)]
pub struct ReShadeEffectRuntime {
    pub vtable: *const *const c_void,
}

static G_REGISTERED: AtomicBool = AtomicBool::new(false);
static G_RESHADE_MOD: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_NEGOTIATED_API: AtomicU32 = AtomicU32::new(20);
static G_EV_BEGIN: AtomicU32 = AtomicU32::new(86);
static G_EV_RELOAD: AtomicU32 = AtomicU32::new(88);

static G_MAP_HANDLE: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_MAP_VIEW: AtomicPtr<u8> = AtomicPtr::new(ptr::null_mut());
static G_MAP_WRITABLE: AtomicBool = AtomicBool::new(false);

static G_CACHED_RUNTIME: AtomicPtr<ReShadeEffectRuntime> = AtomicPtr::new(ptr::null_mut());
static G_CACHE_DIRTY: AtomicBool = AtomicBool::new(true);
static G_FRAME_COUNTER: AtomicU32 = AtomicU32::new(0);
static G_EMPTY_RETRY_TICK: AtomicU32 = AtomicU32::new(0);
static G_LAST_QPC: AtomicU64 = AtomicU64::new(0);
static G_QPC_FREQ: AtomicU64 = AtomicU64::new(0);
static G_EMA_FPS_BITS: AtomicU32 = AtomicU32::new(0x4270_0000);
static G_EMA_MS_BITS: AtomicU32 = AtomicU32::new(0x4185_5555);

macro_rules! atomic_u32_112 {
    () => {
        [
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
            AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
        ]
    };
}

static G_LAST_SNAP_BITS: [AtomicU32; MAX_FLOATS] = atomic_u32_112!();

static G_CACHED_VARS: [AtomicU64; MAX_U_SLOTS] = [
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
    AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0), AtomicU64::new(0),
];

static DEFAULT_MMF_UTF16: [u16; 29] = [
    b'L' as u16, b'o' as u16, b'c' as u16, b'a' as u16, b'l' as u16, b'\\' as u16,
    b'O' as u16, b'm' as u16, b'e' as u16, b'g' as u16, b'a' as u16,
    b'T' as u16, b'r' as u16, b'i' as u16, b'E' as u16, b'n' as u16, b'g' as u16, b'i' as u16, b'n' as u16, b'e' as u16,
    b'U' as u16, b'n' as u16, b'i' as u16, b'f' as u16, b'o' as u16, b'r' as u16, b'm' as u16, b's' as u16,
    0,
];

static MOD_RESHADE64: [u16; 14] = [
    b'R' as u16, b'e' as u16, b'S' as u16, b'h' as u16, b'a' as u16, b'd' as u16, b'e' as u16,
    b'6' as u16, b'4' as u16, b'.' as u16, b'd' as u16, b'l' as u16, b'l' as u16, 0,
];
static MOD_DXGI: [u16; 9] = [
    b'd' as u16, b'x' as u16, b'g' as u16, b'i' as u16, b'.' as u16, b'd' as u16, b'l' as u16, b'l' as u16, 0,
];
static MOD_D3D11: [u16; 10] = [
    b'd' as u16, b'3' as u16, b'd' as u16, b'1' as u16, b'1' as u16, b'.' as u16, b'd' as u16, b'l' as u16, b'l' as u16, 0,
];
static MOD_OPENGL32: [u16; 13] = [
    b'o' as u16, b'p' as u16, b'e' as u16, b'n' as u16, b'g' as u16, b'l' as u16, b'3' as u16, b'2' as u16,
    b'.' as u16, b'd' as u16, b'l' as u16, b'l' as u16, 0,
];

#[inline]
unsafe fn purge_cached_uniform_handles() {
    G_CACHE_DIRTY.store(true, Ordering::Release);
    G_CACHED_RUNTIME.store(ptr::null_mut(), Ordering::Release);
    let mut k = 0usize;
    while k < MAX_U_SLOTS {
        (*G_CACHED_VARS.as_ptr().add(k)).store(0, Ordering::Release);
        k += 1;
    }
}

#[inline]
unsafe fn sample_qpc_fps_and_ms() -> (f32, f32) {
    let mut freq = G_QPC_FREQ.load(Ordering::Relaxed);
    if freq == 0 {
        let mut fq_i64: i64 = 0;
        if QueryPerformanceFrequency(&mut fq_i64) != 0 && fq_i64 > 1000 {
            freq = fq_i64 as u64;
            G_QPC_FREQ.store(freq, Ordering::Relaxed);
        }
    }
    let mut now_i64: i64 = 0;
    if freq > 0 && QueryPerformanceCounter(&mut now_i64) != 0 && now_i64 > 0 {
        let now_u64 = now_i64 as u64;
        let prev = G_LAST_QPC.swap(now_u64, Ordering::AcqRel);
        if prev > 0 && now_u64 > prev {
            let dt_sec = ((now_u64 - prev) as f32) / (freq as f32);
            if dt_sec >= 0.0002 && dt_sec <= 1.0 {
                let inst_fps = 1.0 / dt_sec;
                let inst_ms = dt_sec * 1000.0;
                let prev_fps = f32::from_bits(G_EMA_FPS_BITS.load(Ordering::Relaxed));
                let prev_ms = f32::from_bits(G_EMA_MS_BITS.load(Ordering::Relaxed));
                let ema_fps = prev_fps * 0.85 + inst_fps * 0.15;
                let ema_ms = prev_ms * 0.85 + inst_ms * 0.15;
                G_EMA_FPS_BITS.store(ema_fps.to_bits(), Ordering::Relaxed);
                G_EMA_MS_BITS.store(ema_ms.to_bits(), Ordering::Relaxed);
                return (ema_fps, ema_ms);
            }
        }
    }
    (
        f32::from_bits(G_EMA_FPS_BITS.load(Ordering::Relaxed)),
        f32::from_bits(G_EMA_MS_BITS.load(Ordering::Relaxed)),
    )
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
pub fn map_api_version_to_events(api_ver: u32) -> (u32, u32) {
    if api_ver >= 13 {
        (86, 88)
    } else if api_ver == 12 {
        (83, 85)
    } else if api_ver >= 10 {
        (82, 84)
    } else {
        (74, 76)
    }
}

#[inline]
pub fn vtable_delta_for_api(_api_ver: u32) -> usize {
    0
}

#[inline]
unsafe fn cstr_eq_omega_tri_engine(buf: *const u8) -> bool {
    let target = b"omega_tri_engine\0";
    let mut k = 0usize;
    while k < 17 {
        if *buf.add(k) != *target.as_ptr().add(k) {
            return false;
        }
        k += 1;
    }
    true
}

#[inline]
unsafe fn parse_u_slot_name(buf: *const u8) -> i32 {
    let b0 = *buf;
    if b0 != b'U' && b0 != b'u' {
        return -1;
    }
    let b1 = *buf.add(1);
    let b2 = *buf.add(2);
    if b1 >= b'0' && b1 <= b'9' && b2 == 0 {
        return (b1 - b'0') as i32;
    }
    if b1 == b'1' && b2 >= b'0' && b2 <= b'9' && *buf.add(3) == 0 {
        return 10 + ((b2 - b'0') as i32);
    }
    if b1 == b'2' && b2 >= b'0' && b2 <= b'7' && *buf.add(3) == 0 {
        return 20 + ((b2 - b'0') as i32);
    }
    -1
}

unsafe fn find_reshade_module() -> *mut c_void {
    let fast_names: [*const u16; 4] = [
        MOD_RESHADE64.as_ptr(),
        MOD_DXGI.as_ptr(),
        MOD_D3D11.as_ptr(),
        MOD_OPENGL32.as_ptr(),
    ];
    let mut fi = 0usize;
    while fi < 4 {
        let hm = GetModuleHandleW(*fast_names.as_ptr().add(fi));
        if !hm.is_null() {
            let p = GetProcAddress(hm, b"ReShadeRegisterAddon\0".as_ptr());
            if !p.is_null() {
                return hm;
            }
        }
        fi += 1;
    }

    let heap = GetProcessHeap();
    if heap.is_null() {
        return ptr::null_mut();
    }
    let cap = 512usize;
    let byte_cap = cap * core::mem::size_of::<*mut c_void>();
    let mods = HeapAlloc(heap, 0x0000_0008, byte_cap) as *mut *mut c_void;
    if mods.is_null() {
        return ptr::null_mut();
    }
    let mut found: *mut c_void = ptr::null_mut();
    let mut needed: u32 = 0;
    if K32EnumProcessModules(GetCurrentProcess(), mods, byte_cap as u32, &mut needed) != 0 {
        let mut count = (needed as usize) / core::mem::size_of::<*mut c_void>();
        if count > cap {
            count = cap;
        }
        let mut i = 0usize;
        while i < count {
            let m = *mods.add(i);
            if !m.is_null() {
                let p = GetProcAddress(m, b"ReShadeRegisterAddon\0".as_ptr());
                if !p.is_null() {
                    found = m;
                    break;
                }
            }
            i += 1;
        }
    }
    HeapFree(heap, 0, mods as *mut c_void);
    found
}

unsafe fn open_view_for_name(utf16_name: *const u16) -> (*mut c_void, *mut u8, bool) {
    let mut writable = true;
    let mut h = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, 0, utf16_name);
    if h.is_null() {
        writable = false;
        h = OpenFileMappingW(FILE_MAP_READ, 0, utf16_name);
    }
    if h.is_null() {
        return (ptr::null_mut(), ptr::null_mut(), false);
    }
    let access = if writable { FILE_MAP_READ | FILE_MAP_WRITE } else { FILE_MAP_READ };
    let view = MapViewOfFile(h, access, 0, 0, 0);
    if view.is_null() {
        CloseHandle(h);
        return (ptr::null_mut(), ptr::null_mut(), false);
    }
    (h, view, writable)
}

unsafe fn read_seqlock_from_view(view: *mut u8, writable: bool, out_floats112: *mut f32) -> bool {
    if view.is_null() || out_floats112.is_null() {
        return false;
    }
    let seq_ptr = view.add(328) as *const AtomicU64;
    let magic_ptr = view.add(320) as *const u32;
    let ver_ptr = view.add(324) as *const u32;
    let crc_ptr = view.add(340) as *const u32;
    let dst_bytes = out_floats112 as *mut u8;

    let mut attempt = 0usize;
    while attempt < 16 {
        let s1 = (*seq_ptr).load(Ordering::Acquire);
        if (s1 & 1) != 0 || s1 == 0 {
            core::hint::spin_loop();
            attempt += 1;
            continue;
        }
        let mut b = 0usize;
        while b < UNIFORM_BYTES {
            *dst_bytes.add(b) = ptr::read_volatile(view.add(b));
            b += 1;
        }
        let magic = ptr::read_volatile(magic_ptr);
        let contract_ver = ptr::read_volatile(ver_ptr);
        let expected_crc = ptr::read_volatile(crc_ptr);
        if contract_ver >= CONTRACT_V180 {
            let mut eb = 0usize;
            while eb < EXT_BYTES {
                *dst_bytes.add(UNIFORM_BYTES + eb) = ptr::read_volatile(view.add(EXT_OFFSET + eb));
                eb += 1;
            }
        } else {
            let mut z = 80usize;
            while z < MAX_FLOATS {
                *out_floats112.add(z) = 0.0;
                z += 1;
            }
        }
        let s2 = (*seq_ptr).load(Ordering::Acquire);
        if s1 == s2 && magic == MAGIC_OTRI {
            if crc32_ieee_ptr(dst_bytes as *const u8, UNIFORM_BYTES) == expected_crc {
                let (ema_fps, ema_ms) = sample_qpc_fps_and_ms();
                if contract_ver >= CONTRACT_V180 {
                    *out_floats112.add(100) = ema_fps;
                    *out_floats112.add(101) = ema_ms;
                    *out_floats112.add(108) = 180.0;
                    *out_floats112.add(110) = 1.0;
                }
                let mut k = 0usize;
                while k < MAX_FLOATS {
                    let bits = (*out_floats112.add(k)).to_bits();
                    (*G_LAST_SNAP_BITS.as_ptr().add(k)).store(bits, Ordering::Relaxed);
                    k += 1;
                }
                let _local_frm = G_FRAME_COUNTER.fetch_add(1, Ordering::Relaxed).wrapping_add(1);
                if writable {
                    let tick32 = (GetTickCount64() as u32) | 1u32;
                    let addon_tick = view.add(368) as *const AtomicU32;
                    let addon_frm = view.add(372) as *const AtomicU32;
                    let addon_api = view.add(376) as *const AtomicU32;
                    let api_v = G_NEGOTIATED_API.load(Ordering::Relaxed);
                    if api_v != 0 {
                        (*addon_api).store(api_v, Ordering::Release);
                    }
                    (*addon_frm).fetch_add(1, Ordering::AcqRel);
                    (*addon_tick).store(tick32, Ordering::Release);
                    if contract_ver >= CONTRACT_V180 {
                        let fps_atom = view.add(EXT_OFFSET + 80) as *const AtomicU32;
                        let ms_atom = view.add(EXT_OFFSET + 84) as *const AtomicU32;
                        let rsl_atom = view.add(EXT_OFFSET + 120) as *const AtomicU32;
                        (*fps_atom).store(ema_fps.to_bits(), Ordering::Release);
                        (*ms_atom).store(ema_ms.to_bits(), Ordering::Release);
                        (*rsl_atom).store(1.0f32.to_bits(), Ordering::Release);
                    }
                }
                return true;
            }
        }
        attempt += 1;
    }
    false
}

unsafe extern "C" fn enum_uniform_cb(
    runtime: *mut ReShadeEffectRuntime,
    variable: u64,
    _user_data: *mut c_void,
) {
    if runtime.is_null() || (*runtime).vtable.is_null() || variable == 0 {
        return;
    }
    let delta = vtable_delta_for_api(G_NEGOTIATED_API.load(Ordering::Relaxed));
    let vtbl = (*runtime).vtable;
    let get_str: FnGetAnnotationString = core::mem::transmute(*vtbl.add(27 + delta));
    let get_int: FnGetAnnotationInt = core::mem::transmute(*vtbl.add(25 + delta));

    let mut src_buf = [0u8; 32];
    let mut src_len: usize = 32;
    if get_str(
        runtime,
        variable,
        b"source\0".as_ptr(),
        src_buf.as_mut_ptr(),
        &mut src_len,
    ) {
        if cstr_eq_omega_tri_engine(src_buf.as_ptr()) {
            let mut idx: i32 = -1;
            if get_int(runtime, variable, b"index\0".as_ptr(), &mut idx, 1, 0) {
                if idx >= 0 && (idx as usize) < MAX_U_SLOTS {
                    (*G_CACHED_VARS.as_ptr().add(idx as usize)).store(variable, Ordering::Release);
                    return;
                }
            }
        }
    }

    let get_name: FnGetUniformName = core::mem::transmute(*vtbl.add(22 + delta));
    let mut name_buf = [0u8; 16];
    let mut name_len: usize = 16;
    get_name(runtime, variable, name_buf.as_mut_ptr(), &mut name_len);
    let slot = parse_u_slot_name(name_buf.as_ptr());
    if slot >= 0 && (slot as usize) < MAX_U_SLOTS {
        let cell = &*G_CACHED_VARS.as_ptr().add(slot as usize);
        let _ = cell.compare_exchange(0, variable, Ordering::AcqRel, Ordering::Relaxed);
    }
}

unsafe fn inject_uniforms_into_runtime(runtime: *mut ReShadeEffectRuntime, floats112: *const f32) -> u32 {
    if runtime.is_null() || (*runtime).vtable.is_null() || floats112.is_null() {
        return 0;
    }
    let delta = vtable_delta_for_api(G_NEGOTIATED_API.load(Ordering::Relaxed));
    let vtbl = (*runtime).vtable;
    let prev_rt = G_CACHED_RUNTIME.load(Ordering::Acquire);
    let need_refresh = prev_rt != runtime || G_CACHE_DIRTY.load(Ordering::Acquire);

    if need_refresh {
        let mut k = 0usize;
        while k < MAX_U_SLOTS {
            (*G_CACHED_VARS.as_ptr().add(k)).store(0, Ordering::Relaxed);
            k += 1;
        }
        let enum_fn: FnEnumerateUniforms = core::mem::transmute(*vtbl.add(19 + delta));
        enum_fn(runtime, ptr::null(), enum_uniform_cb, ptr::null_mut());
        G_CACHED_RUNTIME.store(runtime, Ordering::Release);
        G_CACHE_DIRTY.store(false, Ordering::Release);
    }

    let set_float: FnSetUniformFloat = core::mem::transmute(*vtbl.add(33 + delta));
    let mut pushed = 0u32;
    let mut i = 0usize;
    while i < MAX_U_SLOTS {
        let var_h = (*G_CACHED_VARS.as_ptr().add(i)).load(Ordering::Acquire);
        if var_h != 0 {
            set_float(runtime, var_h, floats112.add(i * 4), 4, 0);
            pushed += 1;
        }
        i += 1;
    }
    if pushed == 0 && !need_refresh {
        let retry = G_EMPTY_RETRY_TICK.fetch_add(1, Ordering::Relaxed).wrapping_add(1);
        if (retry & 127) == 0 {
            G_CACHE_DIRTY.store(true, Ordering::Release);
        }
    }
    pushed
}

unsafe extern "C" fn on_reshade_begin_effects(
    runtime: *mut ReShadeEffectRuntime,
    _cmd_list: *mut c_void,
    _rtv: u64,
    _rtv_srgb: u64,
) {
    let mut view = G_MAP_VIEW.load(Ordering::Acquire);
    if view.is_null() {
        let (h, v, w) = open_view_for_name(DEFAULT_MMF_UTF16.as_ptr());
        if !v.is_null() {
            if G_MAP_VIEW
                .compare_exchange(ptr::null_mut(), v, Ordering::AcqRel, Ordering::Acquire)
                .is_ok()
            {
                G_MAP_HANDLE.store(h, Ordering::Release);
                G_MAP_WRITABLE.store(w, Ordering::Release);
                view = v;
            } else {
                UnmapViewOfFile(v as *const c_void);
                CloseHandle(h);
                view = G_MAP_VIEW.load(Ordering::Acquire);
            }
        }
    }
    if view.is_null() {
        return;
    }
    let mut snap = [0.0f32; MAX_FLOATS];
    if read_seqlock_from_view(view, G_MAP_WRITABLE.load(Ordering::Relaxed), snap.as_mut_ptr()) {
        let _ = inject_uniforms_into_runtime(runtime, snap.as_ptr());
    }
}

unsafe extern "C" fn on_reshade_reloaded_effects(_runtime: *mut ReShadeEffectRuntime) {
    purge_cached_uniform_handles();
}

unsafe extern "C" fn on_destroy_effect_runtime(_runtime: *mut ReShadeEffectRuntime) {
    purge_cached_uniform_handles();
}

unsafe fn release_mmf_handles() {
    let view = G_MAP_VIEW.swap(ptr::null_mut(), Ordering::AcqRel);
    if !view.is_null() {
        UnmapViewOfFile(view as *const c_void);
    }
    let h = G_MAP_HANDLE.swap(ptr::null_mut(), Ordering::AcqRel);
    if !h.is_null() {
        CloseHandle(h);
    }
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
        if lpv_reserved.is_null() {
            release_mmf_handles();
        }
    }
    1
}

#[no_mangle]
pub unsafe extern "system" fn AddonInit(addon_module: *mut c_void, reshade_module: *mut c_void) -> bool {
    if G_REGISTERED.load(Ordering::Acquire) {
        return true;
    }
    let host = if !reshade_module.is_null() {
        reshade_module
    } else {
        find_reshade_module()
    };
    if host.is_null() {
        return false;
    }
    let p_reg = GetProcAddress(host, b"ReShadeRegisterAddon\0".as_ptr());
    let p_ev = GetProcAddress(host, b"ReShadeRegisterEvent\0".as_ptr());
    if p_reg.is_null() || p_ev.is_null() {
        return false;
    }
    let reg_addon: FnReShadeRegisterAddon = core::mem::transmute(p_reg);
    let reg_event: FnReShadeRegisterEvent = core::mem::transmute(p_ev);

    let mut chosen_api: u32 = 0;
    let mut ver: u32 = 24;
    while ver >= 8 {
        if reg_addon(addon_module, ver) {
            chosen_api = ver;
            break;
        }
        ver -= 1;
    }
    if chosen_api == 0 {
        return false;
    }

    let (ev_begin, ev_reload) = map_api_version_to_events(chosen_api);
    G_RESHADE_MOD.store(host, Ordering::Release);
    G_NEGOTIATED_API.store(chosen_api, Ordering::Release);
    G_EV_BEGIN.store(ev_begin, Ordering::Release);
    G_EV_RELOAD.store(ev_reload, Ordering::Release);
    purge_cached_uniform_handles();

    reg_event(EV_DESTROY_EFFECT_RUNTIME, on_destroy_effect_runtime as *const c_void);
    reg_event(ev_begin, on_reshade_begin_effects as *const c_void);
    reg_event(ev_reload, on_reshade_reloaded_effects as *const c_void);
    G_REGISTERED.store(true, Ordering::Release);
    true
}

#[no_mangle]
pub unsafe extern "system" fn AddonUninit(addon_module: *mut c_void, reshade_module: *mut c_void) {
    release_mmf_handles();
    if G_REGISTERED.swap(false, Ordering::AcqRel) {
        let host = if !reshade_module.is_null() {
            reshade_module
        } else {
            G_RESHADE_MOD.load(Ordering::Acquire)
        };
        if !host.is_null() {
            let p_un_ev = GetProcAddress(host, b"ReShadeUnregisterEvent\0".as_ptr());
            if !p_un_ev.is_null() {
                let un_ev: FnReShadeUnregisterEvent = core::mem::transmute(p_un_ev);
                un_ev(EV_DESTROY_EFFECT_RUNTIME, on_destroy_effect_runtime as *const c_void);
                un_ev(G_EV_BEGIN.load(Ordering::Relaxed), on_reshade_begin_effects as *const c_void);
                un_ev(G_EV_RELOAD.load(Ordering::Relaxed), on_reshade_reloaded_effects as *const c_void);
            }
            let p_un = GetProcAddress(host, b"ReShadeUnregisterAddon\0".as_ptr());
            if !p_un.is_null() {
                let un_addon: FnReShadeUnregisterAddon = core::mem::transmute(p_un);
                un_addon(addon_module);
            }
        }
    }
    purge_cached_uniform_handles();
}

#[no_mangle]
pub unsafe extern "C" fn OmegaAddonInjectRuntime(
    runtime: *mut ReShadeEffectRuntime,
    custom_map_utf16: *const u16,
) -> u32 {
    let name_ptr = if custom_map_utf16.is_null() {
        DEFAULT_MMF_UTF16.as_ptr()
    } else {
        custom_map_utf16
    };
    let (h, v, w) = open_view_for_name(name_ptr);
    if v.is_null() {
        return 0;
    }
    if G_NEGOTIATED_API.load(Ordering::Relaxed) == 0 {
        G_NEGOTIATED_API.store(20, Ordering::Relaxed);
    }
    let mut snap = [0.0f32; MAX_FLOATS];
    let mut count = 0u32;
    if read_seqlock_from_view(v, w, snap.as_mut_ptr()) {
        count = inject_uniforms_into_runtime(runtime, snap.as_ptr());
    }
    UnmapViewOfFile(v as *const c_void);
    CloseHandle(h);
    count
}

#[no_mangle]
pub unsafe extern "C" fn OmegaAddonReadLastSnapshot80(out_floats80: *mut f32) -> u32 {
    if !out_floats80.is_null() {
        let mut i = 0usize;
        while i < 80 {
            let bits = (*G_LAST_SNAP_BITS.as_ptr().add(i)).load(Ordering::Relaxed);
            *out_floats80.add(i) = f32::from_bits(bits);
            i += 1;
        }
    }
    G_FRAME_COUNTER.load(Ordering::Relaxed)
}

#[no_mangle]
pub unsafe extern "C" fn OmegaAddonReadLastSnapshot112(out_floats112: *mut f32) -> u32 {
    if !out_floats112.is_null() {
        let mut i = 0usize;
        while i < MAX_FLOATS {
            let bits = (*G_LAST_SNAP_BITS.as_ptr().add(i)).load(Ordering::Relaxed);
            *out_floats112.add(i) = f32::from_bits(bits);
            i += 1;
        }
    }
    G_FRAME_COUNTER.load(Ordering::Relaxed)
}

#[no_mangle]
pub unsafe extern "C" fn OmegaAddonResetCache() {
    purge_cached_uniform_handles();
    G_NEGOTIATED_API.store(20, Ordering::Release);
}
