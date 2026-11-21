param(
    [ValidateSet("Deck","All","Browser","Mirror","AV","Synth","Sonar","Sheaf","Penrose","Steg","Forge","Shell","Conductor","Tensor","BraidRE","EdgeDeploy","StressAll","Tunnel","CleanDesktop","Stop")]
    [string]$Mode = "Deck"
)
$ErrorActionPreference = "Stop"
$R = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:OMEGA_LAB_ROOT = $R

$Lx = Join-Path $R "OmegaLabLauncher.exe"
if (-not (Test-Path $Lx)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $R "Build-OmegaLabs.ps1") -SkipLaunch
}

$Map = @{
    "Mirror"    = Join-Path $R "01_omega_mirror\OmegaMirror.exe"
    "Browser"   = Join-Path $R "02_omega_browser\OmegaBrowser.exe"
    "AV"        = Join-Path $R "03_omega_av\OmegaAV.exe"
    "Synth"     = Join-Path $R "04_omega_synth\OmegaSynth.exe"
    "Sonar"     = Join-Path $R "05_omega_sonar\OmegaSonar.exe"
    "Sheaf"     = Join-Path $R "06_omega_sheaf\OmegaSheaf.exe"
    "Penrose"   = Join-Path $R "07_omega_penrose\OmegaPenrose.exe"
    "Steg"      = Join-Path $R "08_omega_steg\OmegaSteg.exe"
    "Forge"     = Join-Path $R "09_omega_forge\OmegaForge.exe"
    "Shell"     = Join-Path $R "10_omega_shell\OmegaShell.exe"
    "Conductor" = Join-Path $R "12_omega_conductor\OmegaConductor.exe"
    "Tensor"    = Join-Path $R "13_omega_tensor\OmegaTensor.exe"
    "BraidRE"   = Join-Path $R "14_omega_braid_re\OmegaBraidRE.exe"
}

switch ($Mode) {
    "Deck"       { Start-Process -FilePath $Lx -WorkingDirectory $R }
    "EdgeDeploy" { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $R "11_omega_edge_workers\rust_p10_worker\Deploy-And-Run-Pixel10Worker.ps1") -Action BenchAndDaemon }
    "StressAll"  { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $R "Build-OmegaLabs.ps1") -SkipLaunch -RunStressSuites }
    "Tunnel"       { & $Lx --arm-pixels }
    "CleanDesktop" { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $R "Build-OmegaLabs.ps1") -OnlyShortcut -CreateDesktopShortcut }
    "All"        {
        foreach ($k in $Map.Keys) {
            $exe = $Map[$k]
            if (Test-Path $exe) { Start-Process -FilePath $exe -WorkingDirectory ([System.IO.Path]::GetDirectoryName($exe)) }
        }
        Start-Process -FilePath $Lx -WorkingDirectory $R
    }
    "Stop"       {
        foreach ($n in @("OmegaBraidRE","OmegaConductor","OmegaTensor","OmegaSonar","OmegaSteg","OmegaSheaf","OmegaPenrose","OmegaSynth","OmegaShell","OmegaForge","OmegaAV","OmegaBrowser","OmegaMirror","OmegaLabLauncher")) {
            Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
    default      {
        if ($Map.ContainsKey($Mode) -and (Test-Path $Map[$Mode])) {
            $exe = $Map[$Mode]
            Start-Process -FilePath $exe -WorkingDirectory ([System.IO.Path]::GetDirectoryName($exe))
        }
    }
}
