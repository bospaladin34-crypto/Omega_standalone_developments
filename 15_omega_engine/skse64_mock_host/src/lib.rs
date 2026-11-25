#![no_std]
#![allow(unknown_lints, linker_messages)]
// =============================================================================
// OmegaSkseMockHost64.dll v1.7.7 - Zero-CRT #![no_std] SKSE64 2.2.6 Host Harness
// SPDX-License-Identifier: MIT OR BSD-3-Clause
// Validates SKSEPlugin_Version (848B = 0x350) + SKSEPlugin_Load handshake,
// captures SKSEMessagingInterface::RegisterListener("SKSE", cb), and drives
// deterministic Skyrim AE world-space camera / Sovngarde / Thu'um frames!
// =============================================================================

use core::ffi::c_void;
use core::ptr;
use core::sync::atomic::{AtomicPtr, AtomicU32, Ordering};

#[cfg(not(test))]
mod zero_crt {
    use core::ptr;

    #[panic_handler]
    fn mock_panic(_info: &core::panic::PanicInfo) -> ! {
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

#[repr(C)]
pub struct SkseMessage {
    pub sender: *const u8,
    pub msg_type: u32,
    pub data_len: u32,
    pub data: *mut c_void,
}

type FnMsgCb = unsafe extern "C" fn(*mut SkseMessage);
type FnPluginLoad = unsafe extern "C" fn(*const MockSkseInterface) -> bool;
type FnStopWorker = unsafe extern "C" fn();
type FnPushWorldState = unsafe extern "C" fn(
    *const u16, *const f32, *const f32, u32, u32, f32, u32, *const f32, f32, u32, u32
) -> u32;

#[repr(C)]
pub struct MockSkseInterface {
    pub skse_version: u32,
    pub runtime_version: u32,
    pub editor_version: u32,
    pub is_editor: u32,
    pub query_interface: unsafe extern "C" fn(u32) -> *mut c_void,
    pub get_plugin_handle: unsafe extern "C" fn() -> u32,
    pub get_release_index: unsafe extern "C" fn() -> u32,
    pub get_plugin_info: unsafe extern "C" fn(*const u8) -> *const c_void,
}

#[repr(C)]
pub struct MockSkseMessagingInterface {
    pub interface_version: u32,
    pub register_listener: unsafe extern "C" fn(u32, *const u8, FnMsgCb) -> bool,
    pub dispatch: unsafe extern "C" fn(u32, u32, *mut c_void, u32, *const u8) -> bool,
    pub get_event_dispatcher: unsafe extern "C" fn(u32) -> *mut c_void,
}

static G_CAPTURED_CB: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_BRIDGE_MOD: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_RUNTIME_VER: AtomicU32 = AtomicU32::new(0x0106_4920); // 1.6.1170 default
static G_REG_CALLS: AtomicU32 = AtomicU32::new(0);
static G_DISPATCHED_MSGS: AtomicU32 = AtomicU32::new(0);

unsafe extern "C" fn mock_get_plugin_handle() -> u32 {
    0x0000_0042
}

unsafe extern "C" fn mock_get_release_index() -> u32 {
    72 // SKSE64 2.2.6 release index
}

unsafe extern "C" fn mock_get_plugin_info(_name: *const u8) -> *const c_void {
    ptr::null()
}

unsafe extern "C" fn mock_register_listener(handle: u32, sender: *const u8, cb: FnMsgCb) -> bool {
    if handle == 0 || sender.is_null() {
        return false;
    }
    if *sender == b'S' && *sender.add(1) == b'K' && *sender.add(2) == b'S' && *sender.add(3) == b'E' {
        G_CAPTURED_CB.store(cb as *mut c_void, Ordering::Release);
        G_REG_CALLS.fetch_add(1, Ordering::Relaxed);
        return true;
    }
    false
}

unsafe extern "C" fn mock_dispatch(
    _handle: u32,
    _message_type: u32,
    _data: *mut c_void,
    _data_len: u32,
    _receiver: *const u8,
) -> bool {
    true
}

unsafe extern "C" fn mock_get_dispatcher(_id: u32) -> *mut c_void {
    ptr::null_mut()
}

static G_MESSAGING_IFACE: MockSkseMessagingInterface = MockSkseMessagingInterface {
    interface_version: 2,
    register_listener: mock_register_listener,
    dispatch: mock_dispatch,
    get_event_dispatcher: mock_get_dispatcher,
};

unsafe extern "C" fn mock_query_interface(id: u32) -> *mut c_void {
    if id == 3 {
        return (&G_MESSAGING_IFACE as *const MockSkseMessagingInterface) as *mut c_void;
    }
    ptr::null_mut()
}

#[no_mangle]
pub unsafe extern "C" fn MockSkseAttachPlugin(
    bridge_module: *mut c_void,
    runtime_version: u32,
) -> u32 {
    if bridge_module.is_null() {
        return 0;
    }
    let p_ver = GetProcAddress(bridge_module, b"SKSEPlugin_Version\0".as_ptr()) as *const u8;
    let p_load = GetProcAddress(bridge_module, b"SKSEPlugin_Load\0".as_ptr());
    if p_ver.is_null() || p_load.is_null() {
        return 0;
    }

    // Verify Phase 1 SKSEPlugin_Version static data block (offsets +0x000, +0x304, +0x308)
    let data_ver = ptr::read_volatile(p_ver as *const u32);
    let indep_ex = ptr::read_volatile(p_ver.add(0x304) as *const u32);
    let indep = ptr::read_volatile(p_ver.add(0x308) as *const u32);
    if data_ver != 1 || indep_ex != 1 || (indep & 2) == 0 {
        return 0;
    }

    let rt_ver = if runtime_version != 0 { runtime_version } else { 0x0106_4920 };
    G_RUNTIME_VER.store(rt_ver, Ordering::Relaxed);
    G_CAPTURED_CB.store(ptr::null_mut(), Ordering::Release);
    G_BRIDGE_MOD.store(bridge_module, Ordering::Release);

    let iface = MockSkseInterface {
        skse_version: 0x0202_0060,
        runtime_version: rt_ver,
        editor_version: 0,
        is_editor: 0,
        query_interface: mock_query_interface,
        get_plugin_handle: mock_get_plugin_handle,
        get_release_index: mock_get_release_index,
        get_plugin_info: mock_get_plugin_info,
    };

    let load_fn: FnPluginLoad = core::mem::transmute(p_load);
    if !load_fn(&iface) {
        return 0;
    }

    // Immediately pause the background ambient thread so deterministic host frames control tests cleanly
    let p_stop = GetProcAddress(bridge_module, b"OmegaSkseStopWorker\0".as_ptr());
    if !p_stop.is_null() {
        let stop_fn: FnStopWorker = core::mem::transmute(p_stop);
        stop_fn();
    }
    1
}

#[no_mangle]
pub unsafe extern "C" fn MockSkseFireLifecycle(msg_type: u32, stop_worker_after: u32) -> u32 {
    let cb_ptr = G_CAPTURED_CB.load(Ordering::Acquire);
    if cb_ptr.is_null() {
        return 0;
    }
    let cb: FnMsgCb = core::mem::transmute(cb_ptr);
    let mut msg = SkseMessage {
        sender: b"SKSE\0".as_ptr(),
        msg_type,
        data_len: 0,
        data: ptr::null_mut(),
    };
    cb(&mut msg);
    G_DISPATCHED_MSGS.fetch_add(1, Ordering::Relaxed);

    if stop_worker_after != 0 {
        let bm = G_BRIDGE_MOD.load(Ordering::Acquire);
        if !bm.is_null() {
            let p_stop = GetProcAddress(bm, b"OmegaSkseStopWorker\0".as_ptr());
            if !p_stop.is_null() {
                let stop_fn: FnStopWorker = core::mem::transmute(p_stop);
                stop_fn();
            }
        }
    }
    1
}

#[no_mangle]
pub unsafe extern "C" fn MockSkseStepWorldFrame(
    frame_idx: u32,
    map_name_utf16: *const u16,
) -> u32 {
    let bm = G_BRIDGE_MOD.load(Ordering::Acquire);
    if bm.is_null() {
        return 0;
    }
    let p_push = GetProcAddress(bm, b"OmegaSksePushWorldState\0".as_ptr());
    if p_push.is_null() {
        return 0;
    }
    let push_fn: FnPushWorldState = core::mem::transmute(p_push);

    let t = (frame_idx as f32) * 0.05;
    // Simple deterministic circular camera orbit in Skyrim units (700 units = ~10m)
    let ang = t * 0.5;
    let r2 = (ang - 1.5707963) * (ang - 1.5707963);
    let _ = r2;
    let pos_sky = [140.0 + (frame_idx as f32) * 8.5, -560.0 + (frame_idx as f32) * 6.0, 175.0];
    let m3x3: [f32; 9] = [
        1.0, 0.0, 0.0,
        0.0, 1.0, 0.0,
        0.0, 0.0, 1.0,
    ];
    let shout_id = 0x0001_3E07u32;
    let shout_age = ((frame_idx % 60) as f32) * 0.06;
    let in_spectre = 1u32;
    push_fn(
        map_name_utf16,
        pos_sky.as_ptr(),
        m3x3.as_ptr(),
        0x0010_A243, // SovngardeClear
        0,
        14.5,
        shout_id,
        pos_sky.as_ptr(),
        shout_age,
        0x0000_0027, // Stage 0,1,2 + Seal 5 -> H^1 = 0!
        in_spectre,
    )
}

#[no_mangle]
pub unsafe extern "C" fn MockSkseDetachPlugin() {
    let bm = G_BRIDGE_MOD.swap(ptr::null_mut(), Ordering::AcqRel);
    if !bm.is_null() {
        let p_stop = GetProcAddress(bm, b"OmegaSkseStopWorker\0".as_ptr());
        if !p_stop.is_null() {
            let stop_fn: FnStopWorker = core::mem::transmute(p_stop);
            stop_fn();
        }
    }
    G_CAPTURED_CB.store(ptr::null_mut(), Ordering::Release);
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