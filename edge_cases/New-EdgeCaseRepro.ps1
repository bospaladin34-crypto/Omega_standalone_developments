param(
    [Parameter(Mandatory=$true)][ValidateRange(1,5)][int]$Track,
    [Parameter(Mandatory=$true)][string]$Slug,
    [Parameter(Mandatory=$true)][string]$Complaint,
    [string]$EdgeCase = "Isolate deterministic minimal trigger"
)
$ErrorActionPreference = "Stop"
$SafeSlug = ($Slug -replace "[^A-Za-z0-9_-]", "_")
$Stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd_HHmmss")
$TargetDir = Join-Path $PSScriptRoot ("EC_T0{0}_{1}_{2}" -f $Track, $Stamp, $SafeSlug)
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

$Md = @(
    "# Omega R&D Edge-Case Repro :: $SafeSlug",
    "",
    "- **Track:** Track 0$Track",
    "- **Captured UTC:** $Stamp",
    "- **User / Dev Complaint:** $Complaint",
    "- **Suspected Edge Case:** $EdgeCase",
    "",
    "## 1. Minimal Deterministic Trigger",
    "- Input state / environment:",
    "- Exact failure symptom in mainstream apps:",
    "",
    "## 2. Bare-Metal Invariant & Fail-Closed Fix",
    "- Mathematical / systems guard (CRC32 WAL, Seqlock, Sheaf H^1=0, Golay24, Roofline Failover):",
    "- Verification metric (RAM MB, tail latency ms, 0 torn reads):"
) -join "`r`n"

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $TargetDir "REPRO_CASE.md"), $Md, $Utf8NoBom)
Write-Host "[REPRO CREATED] $TargetDir" -ForegroundColor Green
