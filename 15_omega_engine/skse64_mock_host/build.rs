fn main() {
    if std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default() == "windows" {
        println!("cargo:rustc-cdylib-link-arg=/NODEFAULTLIB");
        println!("cargo:rustc-cdylib-link-arg=/ENTRY:DllMain");
        println!("cargo:rustc-cdylib-link-arg=kernel32.lib");
    }
}