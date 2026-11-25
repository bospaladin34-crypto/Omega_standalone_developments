fn main() {
    if std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default() == "windows" {
        println!("cargo:rustc-cdylib-link-arg=/NODEFAULTLIB");
        println!("cargo:rustc-cdylib-link-arg=/ENTRY:DllMain");
        println!("cargo:rustc-cdylib-link-arg=kernel32.lib");
        println!("cargo:rustc-cdylib-link-arg=/EXPORT:SKSEPlugin_Version=OMEGA_SKSE_PLUGIN_VERSION,DATA");
    }
}