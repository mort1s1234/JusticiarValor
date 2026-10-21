param(
    [string]$GameDir = (Join-Path $PSScriptRoot '..\..\..\..'),
    [int]$TimeoutSeconds = 600,
    # Also start without this mod and only report the warnings and errors it adds.
    [switch]$Baseline = $true
)
# Headless boot of RimWorld 1.7 with developer mode on (so Def ConfigErrors run) and -quicktest (a colony and a map are
# generated): one start with the dependencies only, one with Justiciar Valor added. Lists the ERROR and WARN lines after
# "Running with command line mod list" that only the second start produced. Save data goes to a fresh temporary folder;
# the player's settings, mod list, saves and Player.log are untouched (-savedatafolder, -NoFileLog).
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path $GameDir).Path
if (Get-Process -Name 'RimWorld' -ErrorAction SilentlyContinue) { throw 'RimWorld is running; close it first.' }
$deps = 'brrainz.harmony;OskarPotocki.VanillaFactionsExpanded.Core'
$work = Join-Path ([System.IO.Path]::GetTempPath()) 'justiciarvalor-build\smoke'
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Boot([string]$Name, [string]$Mods) {
    $data = Join-Path $work $Name
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $data 'Config') | Out-Null
    '<?xml version="1.0" encoding="utf-8"?><PrefsData><devMode>True</devMode><volumeMaster>0</volumeMaster></PrefsData>' |
        Set-Content -LiteralPath (Join-Path $data 'Config\Prefs.xml') -Encoding utf8
    $log = Join-Path $work "$Name.log"
    $p = Start-Process -FilePath (Join-Path $GameDir 'RimWorld.exe') -WorkingDirectory $GameDir -PassThru -WindowStyle Hidden `
        -ArgumentList @('-Headless', '-QuitAfterBoot', '-NoCrashDialog', '-NoFileLog', "-savedatafolder=$data", "-mod=$Mods", '-quicktest') `
        -RedirectStandardOutput $log -RedirectStandardError (Join-Path $work "$Name.err.log")
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { $p.Kill($true); throw "$Name boot timed out; see $log" }
    $lines = Get-Content -LiteralPath $log -Encoding utf8
    $start = ($lines | Select-String -SimpleMatch 'Running with command line mod list' | Select-Object -First 1).LineNumber
    if (-not $start) { throw "$Name boot did not reach the mod list; see $log" }
    if (-not ($lines | Select-String -SimpleMatch 'Initializing new game with mods')) { Write-Warning "$Name boot did not start a game; see $log" }
    Write-Host ("{0}: {1}" -f $Name, ($lines[$start - 1].Trim()))
    $noise = 'Resolution too small|DrawTexture \(Graphics\.cs|Deferred loading: \d+ textures|was not freed before shutdown'
    return $lines | Select-Object -Skip $start | Where-Object { $_ -match '\s(ERROR|WARN)\s' -and $_ -notmatch $noise } |
        ForEach-Object { ($_ -replace '^\S+\s+', '') -replace '0x[0-9A-Fa-f]+', '0x#' -replace '\d+', '#' }
}

$candidate = Invoke-Boot 'candidate' "$deps;Mortis.JusticiarValor"
$added = $candidate
if ($Baseline) {
    $base = Invoke-Boot 'baseline' $deps
    $added = $candidate | Where-Object { $base -notcontains $_ -and $_.Trim() -ne 'WARN' }
}
if ($added) {
    Write-Host 'New warnings and errors:'
    $added | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host 'No new warnings or errors.'
