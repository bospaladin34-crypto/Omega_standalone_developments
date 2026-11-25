fn main() {
    if std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default() == "windows" {
        println!("cargo:rustc-cdylib-link-arg=/NODEFAULTLIB");
        println!("cargo:rustc-cdylib-link-arg=/ENTRY:DllMain");
        println!("cargo:rustc-cdylib-link-arg=kernel32.lib");
        println!("cargo:rustc-cdylib-link-arg=/EXPORT:NAME=OMEGA_ADDON_EXPORT_NAME,DATA");
        println!("cargo:rustc-cdylib-link-arg=/EXPORT:DESCRIPTION=OMEGA_ADDON_EXPORT_DESC,DATA");
        println!("cargo:rustc-cdylib-link-arg=/EXPORT:WEBSITE=OMEGA_ADDON_EXPORT_WEBSITE,DATA");
        println!("cargo:rustc-cdylib-link-arg=/EXPORT:ISSUES=OMEGA_ADDON_EXPORT_ISSUES,DATA");
        println!("cargo:rustc-cdylib-link-arg=/EXPORT:AUTHOR=OMEGA_ADDON_EXPORT_AUTHOR,DATA");
    }
}