param(
    [string]$SeedsFile = "",
    [int]$WaitMs = 3200
)
$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($SeedsFile)) { $SeedsFile = "$Here\harvest_seeds.txt" }
if (-not (Test-Path $SeedsFile)) {
    @(
        "https://ocw.mit.edu/courses/18-06-linear-algebra-spring-2010/pages/syllabus/",
        "https://ocw.mit.edu/courses/6-006-introduction-to-algorithms-spring-2020/pages/lecture-notes/",
        "https://arxiv.org/pdf/1706.03762.pdf"
    ) | Set-Content -Path $SeedsFile -Encoding UTF8
}
$Api = "http://127.0.0.1:9778"
function Call-Omega([string]$Path) {
    $wc = New-Object System.Net.WebClient
    $wc.Encoding = [System.Text.Encoding]::UTF8
    try { return ($wc.DownloadString("$Api$Path") | ConvertFrom-Json) } finally { $wc.Dispose() }
}
$st = Call-Omega "/api/status"
Write-Host ">>> Connected to OmegaBrowser v$($st.version) on :9778 | Shell RAM: $($st.host_ram_mb) MB" -ForegroundColor Cyan
$urls = Get-Content -Path $SeedsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.Trim().StartsWith("#") }
$i = 0; $saved = 0; $dupes = 0
foreach ($u in $urls) {
    $i++
    $trimU = $u.Trim()
    Write-Host "[$i/$($urls.Count)] Harvesting -> $trimU" -ForegroundColor Yellow
    $enc = [System.Uri]::EscapeDataString($trimU)
    Call-Omega "/api/navigate?url=$enc" | Out-Null
    $extra = if ($trimU.ToLowerInvariant().EndsWith(".pdf") -or $trimU.IndexOf("/pdf/") -ge 0) { 2000 } else { 0 }
    Start-Sleep -Milliseconds ($WaitMs + $extra)
    $res = Call-Omega "/api/corpus/save?tags=batch_v05"
    if ($res.saved) { $saved++; Write-Host "    [SAVED] SHA:$($res.sha256.Substring(0,12)) -> $($res.path)" -ForegroundColor Green }
    elseif ($res.duplicate) { $dupes++; Write-Host "    [DEDUP] SHA:$($res.sha256.Substring(0,12)) already in corpus" -ForegroundColor DarkCyan }
}
Call-Omega "/api/freeze" | Out-Null
$sync = Call-Omega "/api/bridge/sync"
Write-Host ">>> HARVEST + BRIDGE SYNC COMPLETE! Saved:$saved | Deduped:$dupes | $($sync.summary)" -ForegroundColor Cyan
