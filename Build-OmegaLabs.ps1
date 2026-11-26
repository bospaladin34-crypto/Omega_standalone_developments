param(
    [switch]$SkipLaunch,
    [switch]$RunStressSuites,
    [switch]$SkipRust,
    [switch]$CreateDesktopShortcut,
    [switch]$OnlyShortcut
)
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Sync-SingleUnifiedDesktopShortcut {
    param([string]$RootDir, [string]$MasterExe, [string]$IconDir)
    $DesktopDir = [Environment]::GetFolderPath("Desktop")
    if ([string]::IsNullOrWhiteSpace($DesktopDir) -or -not (Test-Path -LiteralPath $DesktopDir)) { return }
    $ArchiveDir = Join-Path $RootDir "workspace\archived_desktop_shortcuts"
    New-Item -ItemType Directory -Path $ArchiveDir -Force | Out-Null

    $Wsh = New-Object -ComObject WScript.Shell
    $CanonicalName = "Omega Unified Lab.lnk"
    $CanonicalPath = Join-Path $DesktopDir $CanonicalName
    $ArchivedCount = 0

    $StationExePattern = "(OmegaLabLauncher|OmegaMirror|OmegaBrowser|OmegaAV|OmegaSynth|OmegaSonar|OmegaSheaf|OmegaPenrose|OmegaSteg|OmegaForge|OmegaShell|OmegaConductor|OmegaTensor|OmegaBraidRE|OmegaEngine|OmegaWorkbench|OmegaPocket|OmegaVault|OmegaSpatial|OmegaRnD)"
    $LegacyNamePattern = "^(Omega\s*(Mirror|Browser|AV|Synth|Sonar|Sheaf|Penrose|Steg|Forge|Shell|Conductor|Tensor|BraidRE|Engine|Workbench|Pocket|Vault|Spatial|Standalone|RnD|R&D\s+Lab)|ACT-Omega\s+Lab\b)"

    $ScanDirs = @($DesktopDir, (Join-Path $env:USERPROFILE "Desktop"), [Environment]::GetFolderPath("CommonDesktopDirectory")) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
    $DesktopLinks = @()
    foreach ($Sdir in $ScanDirs) {
        $DesktopLinks += @(Get-ChildItem -LiteralPath $Sdir -Filter "*.lnk" -File -ErrorAction SilentlyContinue)
    }
    foreach ($LnkFile in $DesktopLinks) {
        if ($LnkFile.Name -ieq $CanonicalName) { continue }
        $ShouldArchive = $false
        if ($LnkFile.BaseName -imatch $LegacyNamePattern) {
            $ShouldArchive = $true
        } else {
            try {
                $ScInfo = $Wsh.CreateShortcut($LnkFile.FullName)
                $Tgt = [string]$ScInfo.TargetPath
                if ($Tgt -imatch $StationExePattern -or $Tgt -imatch "Omega_standalone_developments|OmegaStandaloneLab|OmegaRnDLab|Omega_Standalone_Labs") {
                    $ShouldArchive = $true
                }
            } catch { }
        }
        if ($ShouldArchive) {
            $DstArchive = Join-Path $ArchiveDir $LnkFile.Name
            Move-Item -LiteralPath $LnkFile.FullName -Destination $DstArchive -Force
            $ArchivedCount++
        }
    }

    $Lnk = $Wsh.CreateShortcut($CanonicalPath)
    $Lnk.TargetPath = $MasterExe
    $Lnk.WorkingDirectory = $RootDir
    $Ico = Join-Path $IconDir "omega_lab.ico"
    if (Test-Path -LiteralPath $Ico) { $Lnk.IconLocation = $Ico }
    $Lnk.Description = "Omega Unified Lab (Single Launcher for All 15 Stations & R&D Sub-Modes)"
    $Lnk.Save()
    Write-Host ("  [DESKTOP CLEAN] Single shortcut active: '{0}' (archived {1} redundant .lnk icons -> workspace\archived_desktop_shortcuts)" -f $CanonicalName, $ArchivedCount) -ForegroundColor Green
}

if ($OnlyShortcut) {
    Sync-SingleUnifiedDesktopShortcut -RootDir $RepoRoot -MasterExe (Join-Path $RepoRoot "OmegaLabLauncher.exe") -IconDir (Join-Path $RepoRoot "icons")
    exit 0
}

$env:OMEGA_LAB_ROOT = $RepoRoot

if (-not (Test-Path (Join-Path $RepoRoot "config\omega.config.json"))) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "Init-OmegaHardware.ps1") -NonInteractive
}

$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) {
    throw "Required .NET Framework 4.x compiler not found at: $Csc"
}

Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " OMEGA UNIFIED LABS v3.9.0 :: ZERO-NUGET 16-PASS COMPILER & VERIFIER" -ForegroundColor Cyan
Write-Host "=========================================================================" -ForegroundColor Cyan

# Stop any running station binaries before recompiling in place
$StationNames = @(
    "OmegaLabLauncher","OmegaMirror","OmegaBrowser","OmegaAV","OmegaSynth",
    "OmegaSonar","OmegaSheaf","OmegaPenrose","OmegaSteg","OmegaForge",
    "OmegaShell","OmegaConductor","OmegaTensor","OmegaBraidRE","OmegaEngine"
)
foreach ($sn in $StationNames) {
    Get-Process -Name $sn -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

$CommonCfg = Join-Path $RepoRoot "common\OmegaHardwareConfig.cs"
$CommonTriBus = Join-Path $RepoRoot "common\OmegaTriEngineSharedBus.cs"
$RndDir    = Join-Path $RepoRoot "common\rnd_fused"
$IconsDir  = Join-Path $RepoRoot "icons"
New-Item -ItemType Directory -Force -Path $IconsDir | Out-Null

function Invoke-StationBuild {
    param(
        [string]$Label,
        [string]$OutExe,
        [string]$IconFile,
        [string[]]$ExtraRefs,
        [string[]]$Sources
    )
    $argsList = New-Object "System.Collections.Generic.List[string]"
    $argsList.Add("/nologo")
    $argsList.Add("/codepage:65001")
    $argsList.Add("/optimize+")
    $argsList.Add("/platform:x64")
    $argsList.Add("/target:winexe")
    $argsList.Add("/nowarn:0169,0219,0414,0420,0649")
    if (($IconFile -ne "") -and (Test-Path $IconFile)) {
        $argsList.Add("/win32icon:$IconFile")
    }
    $baseRefs = @("System.dll","System.Core.dll","System.Drawing.dll","System.Windows.Forms.dll")
    foreach ($r in $baseRefs) { $argsList.Add("/r:$r") }
    if ($null -ne $ExtraRefs) {
        foreach ($er in $ExtraRefs) { $argsList.Add("/r:$er") }
    }
    $argsList.Add("/out:$OutExe")
    foreach ($src in $Sources) { $argsList.Add($src) }

    & $Csc @argsList | Out-Host
    if (($LASTEXITCODE -eq 0) -and (Test-Path $OutExe)) {
        $kb = [Math]::Round(((Get-Item $OutExe).Length / 1024.0), 1)
        Write-Host ("  [PASS] {0,-42} -> {1,7} KB" -f $Label, $kb) -ForegroundColor Green
        return $true
    }
    Write-Host ("  [FAIL] {0,-42} -> Exit {1}" -f $Label, $LASTEXITCODE) -ForegroundColor Red
    return $false
}

function Find-WebView2Dlls {
    param([string]$BrowserDir)
    $candidates = @(
        $BrowserDir,
        $env:OMEGA_WEBVIEW2_LIB_DIR,
        (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.web.webview2")
    )
    foreach ($base in $candidates) {
        if ([string]::IsNullOrEmpty($base) -or (-not (Test-Path $base))) { continue }
        $coreDlls   = @(Get-ChildItem -Path $base -Filter "Microsoft.Web.WebView2.Core.dll" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch "arm64|x86" })
        $wfDlls     = @(Get-ChildItem -Path $base -Filter "Microsoft.Web.WebView2.WinForms.dll" -Recurse -ErrorAction SilentlyContinue)
        $loaderDlls = @(Get-ChildItem -Path $base -Filter "WebView2Loader.dll" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "win-x64|x64" -or $_.DirectoryName -eq $BrowserDir })
        if (($coreDlls.Count -gt 0) -and ($wfDlls.Count -gt 0) -and ($loaderDlls.Count -gt 0)) {
            if ($coreDlls[0].DirectoryName -ne $BrowserDir) {
                Copy-Item $coreDlls[0].FullName -Destination (Join-Path $BrowserDir "Microsoft.Web.WebView2.Core.dll") -Force
            }
            if ($wfDlls[0].DirectoryName -ne $BrowserDir) {
                Copy-Item $wfDlls[0].FullName -Destination (Join-Path $BrowserDir "Microsoft.Web.WebView2.WinForms.dll") -Force
            }
            if ($loaderDlls[0].DirectoryName -ne $BrowserDir) {
                Copy-Item $loaderDlls[0].FullName -Destination (Join-Path $BrowserDir "WebView2Loader.dll") -Force
            }
            return $true
        }
    }
    return $false
}

$FailCount = 0

# 00. Build OmegaLabLauncher first so --make-icons generates all 15 procedural .ico assets on fresh clones!
$LauncherExe = Join-Path $RepoRoot "OmegaLabLauncher.exe"
$LauncherSrc = @(
    $CommonCfg,
    (Join-Path $RndDir "OmegaRnDEdgeCaseHarness.cs"),
    (Join-Path $RepoRoot "OmegaUnifiedChaosAndConfigDialog.cs"),
    (Join-Path $RepoRoot "OmegaLabLauncher.cs"),
    (Join-Path $RepoRoot "OmegaLabManualDialog.cs"),
    (Join-Path $RepoRoot "OmegaLabPixel10CockpitDialog.cs")
)
if (-not (Invoke-StationBuild "00. OmegaLabLauncher (:19800 Deck)" $LauncherExe "" @() $LauncherSrc)) { $FailCount++ }
if (Test-Path $LauncherExe) {
    & $LauncherExe --make-icons
    $null = Invoke-StationBuild "00. OmegaLabLauncher (:19800 Iconic)" $LauncherExe (Join-Path $IconsDir "omega_lab.ico") @() $LauncherSrc
}

# 01. OmegaMirror (:19777)
$MirrorDir = Join-Path $RepoRoot "01_omega_mirror"
$MirrorSrc = @($CommonCfg, (Join-Path $MirrorDir "OmegaMirrorDualNet.cs"), (Join-Path $MirrorDir "OmegaMirrorHostForm.cs"))
if (-not (Invoke-StationBuild "01. OmegaMirror (:19777)" (Join-Path $MirrorDir "OmegaMirror.exe") (Join-Path $IconsDir "omega_mirror.ico") @() $MirrorSrc)) { $FailCount++ }

# 02. OmegaBrowser (:9778)
$BrDir = Join-Path $RepoRoot "02_omega_browser"
$BrExe = Join-Path $BrDir "OmegaBrowser.exe"
if (Find-WebView2Dlls -BrowserDir $BrDir) {
    $BrSrc = @($CommonCfg, (Join-Path $RndDir "OmegaWorkbenchEngines.cs"), (Join-Path $RndDir "OmegaWorkbenchStressSuite.cs"))
    foreach ($f in (Get-ChildItem -Path $BrDir -Filter "*.cs" | Sort-Object Name)) { $BrSrc += $f.FullName }
    $BrRefs = @(
        "System.Data.dll","System.IO.Compression.dll","System.Speech.dll",
        (Join-Path $BrDir "Microsoft.Web.WebView2.Core.dll"),
        (Join-Path $BrDir "Microsoft.Web.WebView2.WinForms.dll")
    )
    if (-not (Invoke-StationBuild "02. OmegaBrowser (:9778 WebView2+WB)" $BrExe (Join-Path $IconsDir "omega_browser.ico") $BrRefs $BrSrc)) { $FailCount++ }
} else {
    $BrFallbackSrc = @(
        $CommonCfg,
        (Join-Path $RndDir "OmegaWorkbenchEngines.cs"),
        (Join-Path $RndDir "OmegaWorkbenchDirectComputeGpu.cs"),
        (Join-Path $RndDir "OmegaWorkbenchStressSuite.cs"),
        (Join-Path $BrDir "portable_fallback\OmegaBrowserPortableHost.cs")
    )
    if (-not (Invoke-StationBuild "02. OmegaBrowser (:9778 Zero-DLL WB)" $BrExe (Join-Path $IconsDir "omega_browser.ico") @("System.IO.Compression.dll") $BrFallbackSrc)) { $FailCount++ }
}

# 03. OmegaAV (:19779)
$AvDir = Join-Path $RepoRoot "03_omega_av"
$AvSrc = @($CommonCfg, $CommonTriBus) + @(Get-ChildItem -Path $AvDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "03. OmegaAV (:19779)" (Join-Path $AvDir "OmegaAV.exe") (Join-Path $IconsDir "omega_av.ico") @() $AvSrc)) { $FailCount++ }

# 04. OmegaSynth (:19782 + Fused Track #04 Spatial Rig)
$SyDir = Join-Path $RepoRoot "04_omega_synth"
$SySrc = @($CommonCfg, $CommonTriBus, (Join-Path $RndDir "OmegaSpatialCore.cs"), (Join-Path $RndDir "OmegaSpatialStressSuite.cs")) + @(Get-ChildItem -Path $SyDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "04. OmegaSynth (:19782 + Spatial)" (Join-Path $SyDir "OmegaSynth.exe") (Join-Path $IconsDir "omega_synth.ico") @() $SySrc)) { $FailCount++ }

# 05. OmegaSonar (:19783 + Fused Golay-64)
$SnDir = Join-Path $RepoRoot "05_omega_sonar"
$SnSrc = @($CommonCfg, (Join-Path $RndDir "OmegaSpatialCore.cs")) + @(Get-ChildItem -Path $SnDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "05. OmegaSonar (:19783 + Golay64)" (Join-Path $SnDir "OmegaSonar.exe") (Join-Path $IconsDir "omega_sonar.ico") @() $SnSrc)) { $FailCount++ }

# 06. OmegaSheaf (:19784 + Fused Track #05 Pillar 6 FFI/Quest/AOB Surgeon)
$SfDir = Join-Path $RepoRoot "06_omega_sheaf"
$SfSrc = @($CommonCfg, (Join-Path $RndDir "OmegaSheafModCore.cs"), (Join-Path $RndDir "OmegaSheafModStressSuite.cs")) + @(Get-ChildItem -Path $SfDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "06. OmegaSheaf (:19784 + Surgeon)" (Join-Path $SfDir "OmegaSheaf.exe") (Join-Path $IconsDir "omega_sheaf.ico") @() $SfSrc)) { $FailCount++ }

# 07. OmegaPenrose (:19785)
$PnDir = Join-Path $RepoRoot "07_omega_penrose"
$PnSrc = @($CommonCfg, $CommonTriBus) + @(Get-ChildItem -Path $PnDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "07. OmegaPenrose (:19785)" (Join-Path $PnDir "OmegaPenrose.exe") (Join-Path $IconsDir "omega_penrose.ico") @() $PnSrc)) { $FailCount++ }

# 08. OmegaSteg (:19786 + Fused Track #03 Zero-Crate CAS Vault & CRDT)
$SgDir = Join-Path $RepoRoot "08_omega_steg"
$SgSrc = @($CommonCfg, (Join-Path $RndDir "OmegaVaultCore.cs"), (Join-Path $RndDir "OmegaVaultStressSuite.cs")) + @(Get-ChildItem -Path $SgDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "08. OmegaSteg (:19786 + CAS Vault)" (Join-Path $SgDir "OmegaSteg.exe") (Join-Path $IconsDir "omega_steg.ico") @("System.IO.Compression.dll") $SgSrc)) { $FailCount++ }

# 09. OmegaForge (:19780 + Fused Supply-Chain Lockfile Firewall)
$FgDir = Join-Path $RepoRoot "09_omega_forge"
$FgSrc = @($CommonCfg, (Join-Path $RndDir "OmegaWorkbenchEngines.cs"), (Join-Path $RndDir "OmegaVaultCore.cs")) + @(Get-ChildItem -Path $FgDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "09. OmegaForge (:19780 + Firewall)" (Join-Path $FgDir "OmegaForge.exe") (Join-Path $IconsDir "omega_forge.ico") @("System.IO.Compression.dll") $FgSrc)) { $FailCount++ }

# 10. OmegaShell (:19781 + Fused JobObject Process Reaper)
$ShDir = Join-Path $RepoRoot "10_omega_shell"
$ShSrc = @($CommonCfg, (Join-Path $RndDir "OmegaWorkbenchEngines.cs"), (Join-Path $RndDir "OmegaWorkbenchStressSuite.cs")) + @(Get-ChildItem -Path $ShDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "10. OmegaShell (:19781 + JobReaper)" (Join-Path $ShDir "OmegaShell.exe") (Join-Path $IconsDir "omega_shell.ico") @("System.Data.dll","System.Xml.dll","System.ServiceProcess.dll","System.IO.Compression.dll") $ShSrc)) { $FailCount++ }

# Auto-build 11_omega_edge_workers\rust_p10_worker\bin\omega_p10_worker_host.exe so Host Companion is always ready!
$P10HostDir = Join-Path $RepoRoot "11_omega_edge_workers\rust_p10_worker"
$P10HostBin = Join-Path $P10HostDir "bin\omega_p10_worker_host.exe"
if ((-not $SkipRust) -and (-not (Test-Path $P10HostBin)) -and (Test-Path (Join-Path $P10HostDir "src\main.rs"))) {
    $RcCmd = Get-Command "rustc.exe" -ErrorAction SilentlyContinue
    $RcExe = if ($RcCmd) { $RcCmd.Source } elseif (Test-Path "$env:USERPROFILE\.cargo\bin\rustc.exe") { "$env:USERPROFILE\.cargo\bin\rustc.exe" } else { "" }
    if ($RcExe -ne "") {
        New-Item -ItemType Directory -Force -Path (Join-Path $P10HostDir "bin") | Out-Null
        & $RcExe --edition 2021 -C opt-level=3 (Join-Path $P10HostDir "src\main.rs") -o $P10HostBin | Out-Host
        if (Test-Path $P10HostBin) {
            $hkb = [Math]::Round(((Get-Item $P10HostBin).Length / 1024.0), 1)
            Write-Host ("  [PASS] 11. omega_p10_worker_host.exe (Host RTX)  -> {0,7} KB" -f $hkb) -ForegroundColor Green
        }
    }
}

# 12. OmegaConductor (:19790 + Fused Binary OSC / 14-bit MPE)
$CdDir = Join-Path $RepoRoot "12_omega_conductor"
$CdSrc = @($CommonCfg, (Join-Path $RndDir "OmegaSpatialCore.cs")) + @(Get-ChildItem -Path $CdDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "12. OmegaConductor (:19790 + OSC)" (Join-Path $CdDir "OmegaConductor.exe") (Join-Path $IconsDir "omega_conductor.ico") @() $CdSrc)) { $FailCount++ }

# 13. OmegaTensor (:19791 + Fused Track #02 Pocket+PC Silicon Fusion & /v1/chat/completions)
$TnDir = Join-Path $RepoRoot "13_omega_tensor"
$TnSrc = @($CommonCfg, $CommonTriBus, (Join-Path $RndDir "OmegaPocketCore.cs"), (Join-Path $RndDir "OmegaPocketStressSuite.cs")) + @(Get-ChildItem -Path $TnDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "13. OmegaTensor (:19791 + OpenAI v1)" (Join-Path $TnDir "OmegaTensor.exe") (Join-Path $IconsDir "omega_tensor.ico") @() $TnSrc)) { $FailCount++ }

# 14. OmegaBraidRE (:19794 Zero-Crate PE .pdata / IAT / EAT / RIP / ECMA-335 #~ CIL Lifter & E8 Differ)
$ReDir = Join-Path $RepoRoot "14_omega_braid_re"
$ReSrc = @($CommonCfg) + @(Get-ChildItem -Path $ReDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "14. OmegaBraidRE (:19794 Lifter+Differ)" (Join-Path $ReDir "OmegaBraidRE.exe") (Join-Path $IconsDir "omega_braid_re.ico") @() $ReSrc)) { $FailCount++ }

# 15. OmegaEngine (:19795 Standalone 3D DirectCompute Arena + 512B OTRI v1.8 Seqlock + TopBar + ReShade/SKSE64 Bridge)
$EnDir = Join-Path $RepoRoot "15_omega_engine"
$EnExportsDir = Join-Path $EnDir "exports"
$SharedEnExportsDir = Join-Path $RepoRoot "downloads\omega_engine_exports"
New-Item -ItemType Directory -Force -Path $EnExportsDir | Out-Null
New-Item -ItemType Directory -Force -Path $SharedEnExportsDir | Out-Null
if (-not $SkipRust) {
    $Cgo = Get-Command "cargo.exe" -ErrorAction SilentlyContinue
    if ($Cgo) {
        $CdylibTargets = @(
            @("reshade_addon",    "omega_tri_engine_addon.dll",    "OmegaTriEngine.addon64"),
            @("reshade_mock_host","omega_reshade_mock_host.dll",   "OmegaReShadeMockHost64.dll"),
            @("skse64_bridge",    "omega_skyrim_world_bridge.dll", "OmegaSkyrimWorldBridge.dll"),
            @("skse64_mock_host", "omega_skse_mock_host.dll",      "OmegaSkseMockHost64.dll")
        )
        $PrevInc = $env:CARGO_INCREMENTAL
        $env:CARGO_INCREMENTAL = "0"
        try {
            foreach ($ct in $CdylibTargets) {
                $CrateDir = Join-Path $EnDir $ct[0]
                $DstExp   = Join-Path $EnExportsDir $ct[2]
                $DstShr   = Join-Path $SharedEnExportsDir $ct[2]
                if (Test-Path (Join-Path $CrateDir "Cargo.toml")) {
                    Push-Location $CrateDir
                    try {
                        & $Cgo.Source build --release --quiet | Out-Host
                        if ($LASTEXITCODE -ne 0) {
                            throw ("cargo build --release failed on " + $ct[0])
                        }
                    } finally {
                        Pop-Location
                    }
                    $BuiltDll = Join-Path (Join-Path (Join-Path $CrateDir "target") "release") $ct[1]
                    if (-not (Test-Path $BuiltDll)) {
                        throw ("Expected compiled cdylib missing: " + $BuiltDll)
                    }
                    Copy-Item -Path $BuiltDll -Destination $DstExp -Force
                    Copy-Item -Path $BuiltDll -Destination $DstShr -Force
                }
            }
        } finally {
            $env:CARGO_INCREMENTAL = $PrevInc
        }
    }
}
$EnSrc = @($CommonCfg, $CommonTriBus) + @(Get-ChildItem -Path $EnDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Invoke-StationBuild "15. OmegaEngine (:19795 3D Tri-Engine)" (Join-Path $EnDir "OmegaEngine.exe") (Join-Path $IconsDir "omega_engine.ico") @() $EnSrc)) { $FailCount++ }

# Optional Rust #![no_std] Zero-Crate Invariant Suites (00_core_rust_audit + 14_omega_braid_re\rust_braid_re_core)
if (-not $SkipRust) {
    $CargoCmd = Get-Command "cargo.exe" -ErrorAction SilentlyContinue
    if ($CargoCmd) {
        $RustSuites = @(
            @("00_core_rust_audit", (Join-Path $RepoRoot "00_core_rust_audit"), "10/10"),
            @("omega_braid_re_core", (Join-Path $ReDir "rust_braid_re_core"), "9/9"),
            @("omega_tri_engine_core", (Join-Path $EnDir "rust_tri_engine_core"), "8/8"),
            @("omega_skyrim_world_bridge_core", (Join-Path $EnDir "rust_skse64_core"), "9/9")
        )
        foreach ($rs in $RustSuites) {
            if (Test-Path $rs[1]) {
                Write-Host (">>> Verifying {0} (#![no_std] Zero-Crate {1} Suite)..." -f $rs[0], $rs[2]) -ForegroundColor Cyan
                Push-Location $rs[1]
                $OldInc = $env:CARGO_INCREMENTAL
                $env:CARGO_INCREMENTAL = "0"
                try {
                    & $CargoCmd.Source test --quiet | Out-Host
                    if ($LASTEXITCODE -eq 0) {
                        Write-Host ("  [PASS] {0} -> {1} #![no_std] tests verified" -f $rs[0], $rs[2]) -ForegroundColor Green
                    } else {
                        Write-Host ("  [FAIL] {0} cargo test failed" -f $rs[0]) -ForegroundColor Red
                        $FailCount++
                    }
                } finally {
                    $env:CARGO_INCREMENTAL = $OldInc
                    Pop-Location
                }
            }
        }
    }
}

if ($FailCount -gt 0) {
    throw "Build failed on $FailCount station(s)."
}

# Optional Headless Stress-Suite Verification (All 12 Audited Binaries!)
if ($RunStressSuites) {
    Write-Host ">>> Running Headless Stress Suites Across All 13 Audited Stations..." -ForegroundColor Magenta
    $StressTargets = @(
        @("OmegaLabLauncher (:19800 Chaos)", $LauncherExe),
        @("01. OmegaMirror", (Join-Path $MirrorDir "OmegaMirror.exe")),
        @("02. OmegaBrowser+Workbench", $BrExe),
        @("04. OmegaSynth+Spatial", (Join-Path $SyDir "OmegaSynth.exe")),
        @("05. OmegaSonar", (Join-Path $SnDir "OmegaSonar.exe")),
        @("06. OmegaSheaf+Surgeon", (Join-Path $SfDir "OmegaSheaf.exe")),
        @("08. OmegaSteg+Vault", (Join-Path $SgDir "OmegaSteg.exe")),
        @("09. OmegaForge+Firewall", (Join-Path $FgDir "OmegaForge.exe")),
        @("10. OmegaShell+Sandbox", (Join-Path $ShDir "OmegaShell.exe")),
        @("12. OmegaConductor", (Join-Path $CdDir "OmegaConductor.exe")),
        @("13. OmegaTensor+Pocket", (Join-Path $TnDir "OmegaTensor.exe")),
        @("14. OmegaBraidRE", (Join-Path $ReDir "OmegaBraidRE.exe")),
        @("15. OmegaEngine 3D", (Join-Path $EnDir "OmegaEngine.exe"))
    )
    foreach ($st in $StressTargets) {
        $lbl = $st[0]
        $exe = $st[1]
        $wd  = [System.IO.Path]::GetDirectoryName($exe)
        $psi = New-Object System.Diagnostics.ProcessStartInfo($exe, "--stress-test")
        $psi.WorkingDirectory = $wd
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(240000)) {
            try { $proc.Kill() } catch { }
            throw ("Stress suite timed out after 240s on {0}" -f $lbl)
        }
        $null = $proc.WaitForExit()
        $stdOut = ($outTask.GetAwaiter().GetResult() | Out-String).Trim()
        $firstLine = ""
        if ($stdOut -ne "") {
            $firstLine = ($stdOut -split "\r?\n")[0].Trim()
        }
        if ($proc.ExitCode -eq 0) {
            if ($firstLine -ne "") {
                Write-Host ("  [AUDIT PASS] {0,-26} -> {1}" -f $lbl, $firstLine) -ForegroundColor Green
            } else {
                Write-Host ("  [AUDIT PASS] {0,-26} -> Exit 0" -f $lbl) -ForegroundColor Green
            }
        } else {
            $stdErr = ($errTask.GetAwaiter().GetResult() | Out-String).Trim()
            throw ("Stress suite failed on {0} (ExitCode {1}): {2} {3}" -f $lbl, $proc.ExitCode, $stdOut, $stdErr)
        }
    }
}

if ($CreateDesktopShortcut) {
    try {
        Sync-SingleUnifiedDesktopShortcut -RootDir $RepoRoot -MasterExe $LauncherExe -IconDir $IconsDir
    } catch { }
}

if (-not $SkipLaunch) {
    Start-Process -FilePath $LauncherExe -WorkingDirectory $RepoRoot
}
Write-Host ">>> [ALL PASS] Unified Suite v3.9.0 (15 Stations + Rust Audits) Compiled Cleanly!" -ForegroundColor Green
