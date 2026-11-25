#![no_std]
#![allow(unknown_lints, linker_messages)]
// =============================================================================
// OmegaReShadeMockHost64.dll v1.7.7 - Zero-CRT #![no_std] ReShade Host Harness
// SPDX-License-Identifier: BSD-3-Clause OR MIT
// Drives exact reshade::api::effect_runtime vtable[19/22/25/27/33] (delta = 0)
// and addon_event 11 (destroy_effect_runtime), 86 (begin_effects), 88 (reload).
// =============================================================================

use core::ffi::c_void;
use core::ptr;
use core::sync::atomic::{AtomicBool, AtomicPtr, AtomicU32, Ordering};

#[cfg(not(test))]
mod zero_crt {
    use core::ptr;

    #[panic_handler]
    fn host_panic(_info: &core::panic::PanicInfo) -> ! {
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

#[link(name = "kernel32")]
extern "system" {
    fn DisableThreadLibraryCalls(module: *mut c_void) -> i32;
    fn GetProcAddress(module: *mut c_void, proc_name: *const u8) -> *mut c_void;
}

type FnAddonInit = unsafe extern "system" fn(*mut c_void, *mut c_void) -> bool;
type FnAddonUninit = unsafe extern "system" fn(*mut c_void, *mut c_void);
type FnBeginEffects = unsafe extern "C" fn(*mut MockEffectRuntime, *mut c_void, u64, u64);
type FnRuntimeEvent = unsafe extern "C" fn(*mut MockEffectRuntime);
type FnEnumCb = unsafe extern "C" fn(*mut MockEffectRuntime, u64, *mut c_void);

#[repr(C)]
pub struct MockEffectRuntime {
    pub vtable: *const *const c_void,
}

static G_MAX_API_VER: AtomicU32 = AtomicU32::new(20);
static G_ANNOTATED_MODE: AtomicBool = AtomicBool::new(true);
static G_NEGOTIATED_VER: AtomicU32 = AtomicU32::new(0);
static G_ADDON_ACTIVE: AtomicBool = AtomicBool::new(false);

static G_CB_DESTROY_RT: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_CB_BEGIN: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_CB_RELOAD: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());

static G_ENUM_CALLS: AtomicU32 = AtomicU32::new(0);
static G_NAME_CALLS: AtomicU32 = AtomicU32::new(0);
static G_SET_CALLS: AtomicU32 = AtomicU32::new(0);
static G_SLOT_MASK: AtomicU32 = AtomicU32::new(0);
static G_STEP_FRAMES: AtomicU32 = AtomicU32::new(0);

static mut G_VTBL_STORAGE: [*const c_void; 68] = [ptr::null(); 68];
static mut G_RUNTIME_OBJ: MockEffectRuntime = MockEffectRuntime { vtable: ptr::null() };

static G_UNIFORM_BITS: [AtomicU32; 80] = [
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
    AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0), AtomicU32::new(0),
];

#[inline]
unsafe fn cstr_eq(a: *const u8, target: &[u8]) -> bool {
    if a.is_null() {
        return false;
    }
    let mut i = 0usize;
    while i < target.len() {
        if *a.add(i) != *target.as_ptr().add(i) {
            return false;
        }
        i += 1;
    }
    true
}

unsafe extern "C" fn vtbl_noop(_this: *mut MockEffectRuntime) -> u64 {
    0
}

unsafe extern "C" fn vtbl_enumerate_uniforms(
    this: *mut MockEffectRuntime,
    _effect_name: *const u8,
    callback: FnEnumCb,
    user_data: *mut c_void,
) {
    G_ENUM_CALLS.fetch_add(1, Ordering::Relaxed);
    let mut i = 0u64;
    while i < 20 {
        callback(this, 0x9000 + i, user_data);
        i += 1;
    }
}

unsafe extern "C" fn vtbl_get_uniform_name(
    _this: *const MockEffectRuntime,
    variable: u64,
    name_out: *mut u8,
    io_size: *mut usize,
) {
    G_NAME_CALLS.fetch_add(1, Ordering::Relaxed);
    if variable < 0x9000 || variable >= 0x9014 {
        if !io_size.is_null() {
            *io_size = 0;
        }
        return;
    }
    let idx = (variable - 0x9000) as u8;
    let mut tmp = [0u8; 4];
    tmp[0] = b'U';
    let len = if idx < 10 {
        tmp[1] = b'0' + idx;
        tmp[2] = 0;
        3usize
    } else {
        tmp[1] = b'1';
        tmp[2] = b'0' + (idx - 10);
        tmp[3] = 0;
        4usize
    };
    if !name_out.is_null() && !io_size.is_null() && *io_size >= len {
        let mut k = 0usize;
        while k < len {
            *name_out.add(k) = *tmp.as_ptr().add(k);
            k += 1;
        }
    }
    if !io_size.is_null() {
        *io_size = len;
    }
}

unsafe extern "C" fn vtbl_get_annotation_int(
    _this: *const MockEffectRuntime,
    variable: u64,
    ann_name: *const u8,
    out_vals: *mut i32,
    count: usize,
    _array_index: usize,
) -> bool {
    if !G_ANNOTATED_MODE.load(Ordering::Relaxed) {
        return false;
    }
    if variable < 0x9000 || variable >= 0x9014 || out_vals.is_null() || count == 0 {
        return false;
    }
    if !cstr_eq(ann_name, b"index\0") {
        return false;
    }
    *out_vals = (variable - 0x9000) as i32;
    true
}

unsafe extern "C" fn vtbl_get_annotation_string(
    _this: *const MockEffectRuntime,
    variable: u64,
    ann_name: *const u8,
    out_buf: *mut u8,
    io_size: *mut usize,
) -> bool {
    if !G_ANNOTATED_MODE.load(Ordering::Relaxed) {
        return false;
    }
    if variable < 0x9000 || variable >= 0x9014 {
        return false;
    }
    if !cstr_eq(ann_name, b"source\0") {
        return false;
    }
    let src = b"omega_tri_engine\0";
    if !out_buf.is_null() && !io_size.is_null() && *io_size >= 17 {
        let mut k = 0usize;
        while k < 17 {
            *out_buf.add(k) = *src.as_ptr().add(k);
            k += 1;
        }
    }
    if !io_size.is_null() {
        *io_size = 17;
    }
    true
}

unsafe extern "C" fn vtbl_set_uniform_float(
    _this: *mut MockEffectRuntime,
    variable: u64,
    values: *const f32,
    count: usize,
    _array_index: usize,
) {
    if variable < 0x9000 || variable >= 0x9014 || values.is_null() {
        return;
    }
    let slot = (variable - 0x9000) as usize;
    let lim = if count < 4 { count } else { 4 };
    let mut c = 0usize;
    while c < lim {
        let bits = (*values.add(c)).to_bits();
        (*G_UNIFORM_BITS.as_ptr().add(slot * 4 + c)).store(bits, Ordering::Release);
        c += 1;
    }
    G_SLOT_MASK.fetch_or(1u32 << (slot as u32), Ordering::Release);
    G_SET_CALLS.fetch_add(1, Ordering::Relaxed);
}

unsafe fn rebuild_vtable(_api_ver: u32) {
    // Exact reshade::api::effect_runtime vtable indices across ReShade 5.x & 6.0..6.8+: delta = 0!
    let vtbl_ptr = ptr::addr_of_mut!(G_VTBL_STORAGE) as *mut *const c_void;
    let mut i = 0usize;
    while i < 68 {
        *vtbl_ptr.add(i) = vtbl_noop as *const c_void;
        i += 1;
    }
    *vtbl_ptr.add(19) = vtbl_enumerate_uniforms as *const c_void;
    *vtbl_ptr.add(22) = vtbl_get_uniform_name as *const c_void;
    *vtbl_ptr.add(25) = vtbl_get_annotation_int as *const c_void;
    *vtbl_ptr.add(27) = vtbl_get_annotation_string as *const c_void;
    *vtbl_ptr.add(33) = vtbl_set_uniform_float as *const c_void;
    let rt_ptr = ptr::addr_of_mut!(G_RUNTIME_OBJ);
    (*rt_ptr).vtable = vtbl_ptr as *const *const c_void;
}

#[inline]
fn expected_events_for_api(api_ver: u32) -> (u32, u32) {
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

#[cfg(not(test))]
#[no_mangle]
unsafe extern "system" fn DllMain(
    hinst_dll: *mut c_void,
    fdw_reason: u32,
    _lpv_reserved: *mut c_void,
) -> i32 {
    if fdw_reason == 1 && !hinst_dll.is_null() {
        DisableThreadLibraryCalls(hinst_dll);
    }
    1
}

#[no_mangle]
pub unsafe extern "C" fn ReShadeRegisterAddon(_addon_module: *mut c_void, api_version: u32) -> bool {
    let max_v = G_MAX_API_VER.load(Ordering::Acquire);
    if api_version >= 8 && api_version <= max_v {
        G_NEGOTIATED_VER.store(api_version, Ordering::Release);
        rebuild_vtable(api_version);
        G_ADDON_ACTIVE.store(true, Ordering::Release);
        return true;
    }
    false
}

#[no_mangle]
pub unsafe extern "C" fn ReShadeUnregisterAddon(_addon_module: *mut c_void) {
    G_ADDON_ACTIVE.store(false, Ordering::Release);
    G_CB_DESTROY_RT.store(ptr::null_mut(), Ordering::Release);
    G_CB_BEGIN.store(ptr::null_mut(), Ordering::Release);
    G_CB_RELOAD.store(ptr::null_mut(), Ordering::Release);
}

#[no_mangle]
pub unsafe extern "C" fn ReShadeRegisterEvent(ev: u32, callback: *const c_void) {
    let ver = G_NEGOTIATED_VER.load(Ordering::Acquire);
    let (ev_begin, ev_reload) = expected_events_for_api(ver);
    if ev == 11 {
        G_CB_DESTROY_RT.store(callback as *mut c_void, Ordering::Release);
    } else if ev == ev_begin {
        G_CB_BEGIN.store(callback as *mut c_void, Ordering::Release);
    } else if ev == ev_reload {
        G_CB_RELOAD.store(callback as *mut c_void, Ordering::Release);
    }
}

#[no_mangle]
pub unsafe extern "C" fn ReShadeUnregisterEvent(ev: u32, _callback: *const c_void) {
    let ver = G_NEGOTIATED_VER.load(Ordering::Acquire);
    let (ev_begin, ev_reload) = expected_events_for_api(ver);
    if ev == 11 {
        G_CB_DESTROY_RT.store(ptr::null_mut(), Ordering::Release);
    } else if ev == ev_begin {
        G_CB_BEGIN.store(ptr::null_mut(), Ordering::Release);
    } else if ev == ev_reload {
        G_CB_RELOAD.store(ptr::null_mut(), Ordering::Release);
    }
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeConfigure(max_api_version: u32, annotated_mode: u32) {
    G_MAX_API_VER.store(max_api_version, Ordering::Release);
    G_ANNOTATED_MODE.store(annotated_mode != 0, Ordering::Release);
    G_ENUM_CALLS.store(0, Ordering::Release);
    G_NAME_CALLS.store(0, Ordering::Release);
    G_SET_CALLS.store(0, Ordering::Release);
    G_SLOT_MASK.store(0, Ordering::Release);
    G_STEP_FRAMES.store(0, Ordering::Release);
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeAttachAddon(
    addon_hmod: *mut c_void,
    self_hmod: *mut c_void,
) -> u32 {
    if addon_hmod.is_null() || self_hmod.is_null() {
        return 0;
    }
    let p_init = GetProcAddress(addon_hmod, b"AddonInit\0".as_ptr());
    if p_init.is_null() {
        return 0;
    }
    G_NEGOTIATED_VER.store(0, Ordering::Release);
    let fn_init: FnAddonInit = core::mem::transmute(p_init);
    if !fn_init(addon_hmod, self_hmod) {
        return 0;
    }
    G_NEGOTIATED_VER.load(Ordering::Acquire)
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeFireBeginEffects() -> u32 {
    let cb = G_CB_BEGIN.load(Ordering::Acquire);
    if cb.is_null() {
        return 0;
    }
    G_SLOT_MASK.store(0, Ordering::Release);
    let before = G_SET_CALLS.load(Ordering::Relaxed);
    let fn_begin: FnBeginEffects = core::mem::transmute(cb);
    fn_begin(ptr::addr_of_mut!(G_RUNTIME_OBJ), ptr::null_mut(), 0, 0);
    let after = G_SET_CALLS.load(Ordering::Relaxed);
    G_STEP_FRAMES.fetch_add(1, Ordering::Relaxed);
    after.wrapping_sub(before)
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeFireReloadEffects() {
    let cb = G_CB_RELOAD.load(Ordering::Acquire);
    if !cb.is_null() {
        let fn_reload: FnRuntimeEvent = core::mem::transmute(cb);
        fn_reload(ptr::addr_of_mut!(G_RUNTIME_OBJ));
    }
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeFireDestroyRuntime() {
    let cb = G_CB_DESTROY_RT.load(Ordering::Acquire);
    if !cb.is_null() {
        let fn_destroy: FnRuntimeEvent = core::mem::transmute(cb);
        fn_destroy(ptr::addr_of_mut!(G_RUNTIME_OBJ));
    }
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeReadUniforms80(out_floats80: *mut f32) -> u32 {
    if !out_floats80.is_null() {
        let mut i = 0usize;
        while i < 80 {
            let bits = (*G_UNIFORM_BITS.as_ptr().add(i)).load(Ordering::Acquire);
            *out_floats80.add(i) = f32::from_bits(bits);
            i += 1;
        }
    }
    G_STEP_FRAMES.load(Ordering::Relaxed)
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeGetStats(
    out_enum_calls: *mut u32,
    out_set_calls: *mut u32,
    out_slot_mask: *mut u32,
) -> u32 {
    if !out_enum_calls.is_null() {
        *out_enum_calls = G_ENUM_CALLS.load(Ordering::Acquire);
    }
    if !out_set_calls.is_null() {
        *out_set_calls = G_SET_CALLS.load(Ordering::Acquire);
    }
    if !out_slot_mask.is_null() {
        *out_slot_mask = G_SLOT_MASK.load(Ordering::Acquire);
    }
    G_NAME_CALLS.load(Ordering::Acquire)
}

#[no_mangle]
pub unsafe extern "C" fn MockReShadeDetachAddon(addon_hmod: *mut c_void, self_hmod: *mut c_void) {
    if !addon_hmod.is_null() {
        let p_uninit = GetProcAddress(addon_hmod, b"AddonUninit\0".as_ptr());
        if !p_uninit.is_null() {
            let fn_uninit: FnAddonUninit = core::mem::transmute(p_uninit);
            fn_uninit(addon_hmod, self_hmod);
        }
    }
}