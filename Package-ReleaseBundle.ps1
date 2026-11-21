param(
    [string]$Version = "v3.0.7",
    [string]$BaselineCommit = "",
    [switch]$SkipRebuild
)
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($BaselineCommit)) {
    try {
        $BaselineCommit = (& git -C $RepoRoot rev-parse --short HEAD 2>$null | Out-String).Trim()
    } catch { }
    if ([string]::IsNullOrWhiteSpace($BaselineCommit)) { $BaselineCommit = "v3.0.7" }
}

$DistDir  = Join-Path $RepoRoot "dist"
$StageDir = Join-Path $DistDir ("Omega_Standalone_Labs_" + $Version + "_win-x64")
$ZipName  = "Omega_Standalone_Labs_" + $Version + "_win-x64_Prebuilt.zip"
$ZipPath  = Join-Path $DistDir $ZipName
$SumsPath = Join-Path $DistDir "SHA256SUMS.txt"
$NotesPath = Join-Path $DistDir ("RELEASE_NOTES_" + $Version + ".md")

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host (" OMEGA UNIFIED LABS {0} :: PREBUILT WINDOWS X64 BUNDLER & SHA-256 SIGNER" -f $Version) -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

if (-not $SkipRebuild) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "Init-OmegaHardware.ps1") -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "Init-OmegaHardware.ps1 failed ($LASTEXITCODE)" }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "Build-OmegaLabs.ps1") -SkipLaunch -RunStressSuites
    if ($LASTEXITCODE -ne 0) { throw "Build-OmegaLabs.ps1 -RunStressSuites failed ($LASTEXITCODE)" }
}

if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

# 1. Stage all 14 compiled Windows x64 binaries (Deck + Stations 01..10, 12..14) in canonical layout
$RequiredBinaries = @(
    "OmegaLabLauncher.exe",
    "01_omega_mirror\OmegaMirror.exe",
    "02_omega_browser\OmegaBrowser.exe",
    "03_omega_av\OmegaAV.exe",
    "04_omega_synth\OmegaSynth.exe",
    "05_omega_sonar\OmegaSonar.exe",
    "06_omega_sheaf\OmegaSheaf.exe",
    "07_omega_penrose\OmegaPenrose.exe",
    "08_omega_steg\OmegaSteg.exe",
    "09_omega_forge\OmegaForge.exe",
    "10_omega_shell\OmegaShell.exe",
    "12_omega_conductor\OmegaConductor.exe",
    "13_omega_tensor\OmegaTensor.exe",
    "14_omega_braid_re\OmegaBraidRE.exe"
)

foreach ($relBin in $RequiredBinaries) {
    $srcBin = Join-Path $RepoRoot $relBin
    if (-not (Test-Path $srcBin)) {
        throw "Missing required compiled binary for release bundle: $relBin"
    }
    $dstBin = Join-Path $StageDir $relBin
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dstBin) | Out-Null
    Copy-Item -Path $srcBin -Destination $dstBin -Force
}

foreach ($wvDll in @("Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","WebView2Loader.dll")) {
    $srcDll = Join-Path $RepoRoot ("02_omega_browser\" + $wvDll)
    if (Test-Path $srcDll) {
        Copy-Item -Path $srcDll -Destination (Join-Path $StageDir ("02_omega_browser\" + $wvDll)) -Force
    }
}

$RootFiles = @("README.md","LICENSE","Init-OmegaHardware.ps1","Build-OmegaLabs.ps1","Start-OmegaLabs.ps1")
foreach ($rf in $RootFiles) {
    $srcRf = Join-Path $RepoRoot $rf
    if (Test-Path $srcRf) { Copy-Item -Path $srcRf -Destination (Join-Path $StageDir $rf) -Force }
}

New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "config") | Out-Null
Copy-Item -Path (Join-Path $RepoRoot "config\omega.config.example.json") -Destination (Join-Path $StageDir "config\omega.config.example.json") -Force

if (Test-Path (Join-Path $RepoRoot "icons")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "icons") | Out-Null
    Get-ChildItem -Path (Join-Path $RepoRoot "icons") -Filter "*.ico" | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $StageDir ("icons\" + $_.Name)) -Force
    }
}

if (Test-Path (Join-Path $RepoRoot "02_omega_browser\home")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "02_omega_browser\home") | Out-Null
    Get-ChildItem -Path (Join-Path $RepoRoot "02_omega_browser\home") -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $StageDir ("02_omega_browser\home\" + $_.Name)) -Force
    }
}

if (Test-Path (Join-Path $RepoRoot "11_omega_edge_workers")) {
    Copy-Item -Path (Join-Path $RepoRoot "11_omega_edge_workers") -Destination (Join-Path $StageDir "11_omega_edge_workers") -Recurse -Force
    $edgeBin = Join-Path $StageDir "11_omega_edge_workers\rust_p10_worker\bin"
    if (Test-Path $edgeBin) { Remove-Item $edgeBin -Recurse -Force }
}

if (Test-Path (Join-Path $RepoRoot "14_omega_braid_re\c11_cli\omega_braid_re_cli.c")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "14_omega_braid_re\c11_cli") | Out-Null
    Copy-Item -Path (Join-Path $RepoRoot "14_omega_braid_re\c11_cli\omega_braid_re_cli.c") -Destination (Join-Path $StageDir "14_omega_braid_re\c11_cli\omega_braid_re_cli.c") -Force
}

foreach ($emptyDir in @("downloads","corpus","bridge_export","workspace")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $StageDir $emptyDir) | Out-Null
}

if (Test-Path (Join-Path $StageDir "config\omega.config.json")) {
    Remove-Item (Join-Path $StageDir "config\omega.config.json") -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($StageDir, $ZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$SumLines = New-Object "System.Collections.Generic.List[string]"
$ZipHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$ZipSizeKb = [Math]::Round(((Get-Item $ZipPath).Length / 1024.0), 1)
$SumLines.Add(("{0}  {1}" -f $ZipHash, $ZipName))

$MdTableRows = New-Object "System.Collections.Generic.List[string]"
$MdTableRows.Add(("| '{0}' | '{1} KB' | '{2}' |" -f $ZipName, $ZipSizeKb, $ZipHash))

foreach ($relBin in $RequiredBinaries) {
    $binPath = Join-Path $StageDir $relBin
    $bh = (Get-FileHash -Path $binPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $bkb = [Math]::Round(((Get-Item $binPath).Length / 1024.0), 1)
    $normRel = $relBin.Replace('\', '/')
    $SumLines.Add(("{0}  {1}" -f $bh, $normRel))
    $MdTableRows.Add(("| '{0}' | '{1} KB' | '{2}' |" -f $normRel, $bkb, $bh))
}

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($SumsPath, (($SumLines -join [string][char]10) + [string][char]10), $Utf8NoBom)

$NotesLines = @(
    "## Unified Omega Standalone & R&D Labs $Version GM (Windows x64 -- 14 Stations)",
    "",
    "**Verified Source Baseline:** '$BaselineCommit' on 'vesper_production'",
    "**Architecture:** Zero-NuGet Windows C# 5.0 ('csc.exe' x64) + Zero-Crate '#![no_std]' Rust ('19/19' invariant tests across 2 crates) + DirectCompute 'cs_5_0' + Universal C11 Edge & RE Engines",
    "",
    "### What's New in $Version",
    "- **Station 14 -- OmegaBraidRE v1.4.0 (':19794'):** Clean-room PE32+/PE32/ELF64 inspector, exact '.pdata' 'RUNTIME_FUNCTION' unwind bounds, 'IAT'/'EAT' + 'FF 25' thunk resolver, 'RIP'-relative ASCII/UTF-16LE strings, ECMA-335 '#~' 'TypeDef'/'MethodDef' CIL lifter, 240-root Conway-Sloane 'E8' cross-patch function differ, Cellular Sheaf 'H^1 = 0' Rust/C++23/C# struct synthesizer, PDB 7.00 / ELF64 DWARF symbol resolver, 'Module32NextW' live loaded-DLL module picker, wildcard & nibble-masked AOB signature scanner + SKSE64/F4SE trampoline codegen, and post-'D3DCompile' working-set trim ('3.1 MB' CLI / '3.2 MB' live 60 FPS GUI).",
    "- **Unified 14-Station Master Deck (':19800'):** Wired 'OmegaBraidRE' ('[Shift+F7]'), live RAM/status polling, procedural 'omega_braid_re.ico', Vault export shortcut, and ADB ':19794' tunnel into 'OmegaLabLauncher.exe'.",
    "",
    "### Quick Run (Zero Compilation Needed)",
    "1. Download **'$ZipName'** below (and optionally verify against 'SHA256SUMS.txt': 'certutil -hashfile $ZipName SHA256').",
    "2. Extract anywhere on Windows 10 or Windows 11 and double-click **'OmegaLabLauncher.exe'**.",
    "",
    "### SHA-256 Checksums",
    "| Artifact / Binary | Size | SHA-256 |",
    "| :--- | :--- | :--- |"
) + $MdTableRows

[System.IO.File]::WriteAllText($NotesPath, (($NotesLines -join [string][char]10) + [string][char]10), $Utf8NoBom)

Remove-Item $StageDir -Recurse -Force

Write-Host ("  [BUNDLE READY] {0} ({1} KB)" -f $ZipPath, $ZipSizeKb) -ForegroundColor Green
Write-Host ("  [SHA256 ZIP]   {0}" -f $ZipHash) -ForegroundColor Green
Write-Host ("  [CHECKSUMS]    {0}" -f $SumsPath) -ForegroundColor Green
Write-Host ("  [NOTES]        {0}" -f $NotesPath) -ForegroundColor Green
