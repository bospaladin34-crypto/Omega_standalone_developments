param(
    [switch]$NonInteractive,
    [string]$HostAlias = "",
    [string]$LocalLlmEndpoint = "http://127.0.0.1:11434",
    [string]$CloudBridgeDir = ""
)
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigDir = Join-Path $RepoRoot "config"
New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host " OMEGA UNIFIED LABS v3.0.0 :: HARDWARE DISCOVERY & PROFILE INITIALIZER" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

# 1. Hostname & OS
$DetectedHost = if ($HostAlias -ne "") { $HostAlias } else { "omega-workstation" }
if ([string]::IsNullOrEmpty($DetectedHost)) { $DetectedHost = "omega-workstation" }

# 2. Primary GPU Discovery via CIM/WMI
$GpuName = "DirectCompute cs_5_0 Adapter"
$GpuVramMb = 0
try {
    $gpus = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue)
    foreach ($g in $gpus) {
        $nm = [string]$g.Name
        if (($nm -ne "") -and ($nm -notmatch "Basic Render|Remote Desktop")) {
            $ram = 0
            if ($null -ne $g.AdapterRAM) { $ram = [Math]::Round(([double]$g.AdapterRAM / 1MB), 0) }
            if (($GpuName -eq "DirectCompute cs_5_0 Adapter") -or ($ram -gt $GpuVramMb)) {
                $GpuName = $nm
                $GpuVramMb = [int]$ram
            }
        }
    }
} catch { }

# 3. Built-in Windows C# Compiler (csc.exe)
$CscPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$HasCsc = Test-Path $CscPath

# 4. Optional Rust / Cargo / ADB / Clang / NDK Discovery
$CargoCmd = Get-Command "cargo.exe" -ErrorAction SilentlyContinue
$RustcCmd = Get-Command "rustc.exe" -ErrorAction SilentlyContinue
$AdbCmd   = Get-Command "adb.exe" -ErrorAction SilentlyContinue
$AdbCount = 0
if ($AdbCmd) {
    try {
        $adbOut = & $AdbCmd.Source devices 2>$null
        foreach ($ln in $adbOut) {
            if ($ln -match "\tdevice$") { $AdbCount++ }
        }
    } catch { }
}

Write-Host ("  [HOST]     {0}" -f $DetectedHost) -ForegroundColor Green
Write-Host ("  [GPU]      {0} ({1} MB reported)" -f $GpuName, $GpuVramMb) -ForegroundColor Green
Write-Host ("  [CSC.EXE]  {0}" -f $(if ($HasCsc) { $CscPath } else { "MISSING (.NET Framework 4.x required)" })) -ForegroundColor $(if ($HasCsc) { "Green" } else { "Red" })
Write-Host ("  [RUST]     {0}" -f $(if ($CargoCmd) { $CargoCmd.Source } else { "Optional (install rustup for 00_core_rust_audit & edge workers)" })) -ForegroundColor Yellow
Write-Host ("  [ADB]      {0} ({1} Android device(s) online)" -f $(if ($AdbCmd) { $AdbCmd.Source } else { "Optional (0 phones required for full PC operation)" }), $AdbCount) -ForegroundColor Yellow

# Ensure local working folders exist
foreach ($sub in @("downloads", "corpus", "bridge_export", "workspace", "icons")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot $sub) | Out-Null
}

$ResolvedBridge = if ($CloudBridgeDir -ne "") { $CloudBridgeDir } else { "bridge_export" }

$CfgObj = [ordered]@{
    suite_version          = "3.0.0"
    host_alias             = $DetectedHost
    gpu_label              = $GpuName
    gpu_vram_mb            = $GpuVramMb
    downloads_dir          = "downloads"
    corpus_dir             = "corpus"
    cloud_bridge_dir       = $ResolvedBridge
    external_project_dir   = "workspace"
    local_llm_endpoint     = $LocalLlmEndpoint
    openai_compat_endpoint = "http://127.0.0.1:19791/v1/chat/completions"
    adb_discovery          = "auto"
}

$CfgJson = $CfgObj | ConvertTo-Json -Depth 4
$UserCfgPath = Join-Path $ConfigDir "omega.config.json"
[System.IO.File]::WriteAllText($UserCfgPath, $CfgJson + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("  [SAVED]    {0} (gitignored local hardware profile)" -f $UserCfgPath) -ForegroundColor Cyan
