param(
    # 1.7: the RimWorld this mod is installed in. 1.6: a RimWorld 1.6 install given with -GameDir.
    [ValidateSet('1.6', '1.7')][string]$Version = '1.7',
    [string]$GameDir,
    [string]$WorkDir = (Join-Path ([System.IO.Path]::GetTempPath()) "justiciarvalor-build\validation-$Version"),
    [int]$TimeoutSeconds = 900,
    # Validate the existing <Version>/Assemblies/JusticiarValor.dll instead of rebuilding it first.
    [switch]$NoBuild,
    # Combat/gear, effect assets and save/reload, excluding the unrelated random mission/quest lifecycle.
    [switch]$EffectsOnly,
    # Also load Vanilla Psycasts Expanded (Workshop 2842502659), for the Warlockbane checks against its psycasts.
    [switch]$WithVPE,
    # Explicitly requested interactive 1.6 validation: Unity needs a presented window for full map screenshots.
    [switch]$VisibleWindow,
    # Record a clip of every effect instead of validating (GameValidation/EffectRecorder.cs): one MP4 per effect in
    # -RecordOut (default <WorkDir>/clips), encoded with ffmpeg (-Ffmpeg, default the one on PATH).
    [switch]$Record,
    [string]$RecordOut,
    # Record only these scenes (EffectRecorder method names, e.g. ThreatPulse,PsyOrbs,OrbImpact,CelestialPierce).
    [string[]]$RecordOnly,
    [string]$Ffmpeg,
    # Keep the PNG frames of the recorded clips (<WorkDir>/SaveData/Screenshots/jvrec).
    [switch]$KeepFrames
)
# In-game validation (GameValidation/*.cs) on a quick-test colony with Harmony, Vanilla Expanded Framework (and with
# -WithVPE Vanilla Psycasts Expanded), Justiciar Valor and a temporary test mod (Mods/JusticiarValor-<Version>-validation-tmp,
# removed afterwards).
# <Version>/Assemblies/JusticiarValor.dll is the assembly under test. Report, screenshots, logs: $WorkDir.
# - 1.7: RimWorld.exe hidden (-NoWindow), -NoFileLog keeps the player's Player.log, -mod= picks the mods.
# - 1.6: RimWorldWin64.exe with a hidden 1920x1080 window by default; -VisibleWindow opts into an interactive window.
#   Unity may return a black screenshot when hidden. -logFile keeps the player's Player.log,
#   ModsConfig.xml in the save data folder picks the mods, the mod folder reaches Mods through a junction that is removed
#   afterwards, and Unity's window settings in the registry are restored afterwards.
$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $GameDir) {
    if ($Version -eq '1.6') { throw 'Give the RimWorld 1.6 folder with -GameDir' }
    $GameDir = Join-Path $modRoot '..\..'
}
$GameDir = (Resolve-Path $GameDir).Path
$gameVersion = (Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -TotalCount 1).Trim()
if (-not $gameVersion.StartsWith("$Version.")) { throw "$GameDir is RimWorld $gameVersion, not $Version" }
if (Get-Process -Name 'RimWorld', 'RimWorldWin64' -ErrorAction SilentlyContinue) { throw 'RimWorld is running; close it first.' }
$workshop = Join-Path $modRoot '..\..\..\..\workshop\content\294100'
if ($Record) {
    if (-not $Ffmpeg) { $Ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source }
    if (-not $Ffmpeg -or -not (Test-Path -LiteralPath $Ffmpeg)) { throw 'ffmpeg not found; give it with -Ffmpeg' }
    $ffprobe = Join-Path (Split-Path $Ffmpeg) 'ffprobe.exe'
    # Recording takes longer than validating.
    if (-not $PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 3600 }
}

$buildRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'justiciarvalor-build'
if ($Version -eq '1.7') {
    if (-not $NoBuild) {
        & dotnet build (Join-Path $modRoot 'Source\JusticiarValor\JusticiarValor.csproj') -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Build failed: JusticiarValor (1.7)' }
    }
    & dotnet build (Join-Path $PSScriptRoot 'GameValidation\GameValidation.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed: GameValidation' }
    $harness = Join-Path $buildRoot 'GameValidation\bin\Release'
}
else {
    if (-not $NoBuild) {
        & dotnet build (Join-Path $modRoot 'Source\JusticiarValor\JusticiarValor.1.6.csproj') -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Build failed: JusticiarValor (1.6)' }
    }
    & dotnet build (Join-Path $PSScriptRoot 'GameValidation\GameValidation.1.6.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed: GameValidation (1.6)' }
    $harness = Join-Path $buildRoot 'GameValidation.1.6\bin\Release'
}

function Reset-Folder([string]$Path, [string]$Marker) {
    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $Marker))) { throw "Refusing to clear $Path; it was not created by this script." }
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    New-Item -ItemType File -Path (Join-Path $Path $Marker) | Out-Null
}

$saveData = Join-Path $WorkDir 'SaveData'
Reset-Folder $WorkDir '.justiciarvalor-validation'
New-Item -ItemType Directory -Force -Path (Join-Path $saveData 'Config') | Out-Null
@'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <volumeMaster>0</volumeMaster>
  <screenWidth>1920</screenWidth>
  <screenHeight>1080</screenHeight>
  <fullscreen>False</fullscreen>
  <runInBackground>True</runInBackground>
  <devMode>True</devMode>
  <pauseOnLoad>False</pauseOnLoad>
  <langFolderName>English</langFolderName>
</PrefsData>
'@ | Set-Content -LiteralPath (Join-Path $saveData 'Config\Prefs.xml') -Encoding utf8
$report = Join-Path $WorkDir 'report.txt'
$console = Join-Path $WorkDir 'console.log'
$testPackage = 'Mortis.JusticiarValor.Validation' + ($Version -replace '\.', '')
$testMod = Join-Path $GameDir "Mods\JusticiarValor-$Version-validation-tmp"
$testModMarker = '.justiciarvalor-validation-tmp'
$modLink = Join-Path $GameDir 'Mods\JusticiarValor-validation-link'
$registryKey = 'HKCU\Software\Ludeon Studios\RimWorld by Ludeon Studios'
$registryBackup = Join-Path $WorkDir 'unity-registry-backup.reg'
$links = @()
$clipDir = Join-Path $WorkDir 'clips'
$encoded = @{}

# Encodes every clip the recorder has finished (Screenshots/jvrec/<clip>.done) and not encoded yet: 60 fps H.264, BT.709.
function Invoke-ClipEncoding {
    $frames = Join-Path $saveData 'Screenshots\jvrec'
    if (-not (Test-Path -LiteralPath $frames)) { return }
    foreach ($marker in Get-ChildItem -LiteralPath $frames -Filter '*.done' -File) {
        $name = $marker.BaseName
        if ($encoded.ContainsKey($name)) { continue }
        New-Item -ItemType Directory -Force -Path $clipDir | Out-Null
        $out = Join-Path $clipDir "$name.mp4"
        & $Ffmpeg -hide_banner -loglevel error -y -framerate 60 -i (Join-Path $frames "$name\f%04d.png") -c:v libx264 -preset slow -crf 16 `
            -vf 'scale=out_color_matrix=bt709:out_range=tv,format=yuv420p' -color_primaries bt709 -color_trc bt709 -colorspace bt709 `
            -color_range tv -movflags +faststart $out
        $encoded[$name] = ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $out))
        if (-not $encoded[$name]) { Write-Warning "ffmpeg failed on $name" }
        elseif (-not $KeepFrames) { Remove-Item -LiteralPath (Join-Path $frames $name) -Recurse -Force }
    }
}

try {
    Reset-Folder $testMod $testModMarker
    New-Item -ItemType Directory -Force -Path (Join-Path $testMod 'About'), (Join-Path $testMod 'Assemblies') | Out-Null
    @"
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>Justiciar Valor $Version validation (temporary)</name>
  <author>Mortis</author>
  <packageId>$testPackage</packageId>
  <supportedVersions><li>$Version</li></supportedVersions>
  <modDependencies>
    <li>
      <packageId>Mortis.JusticiarValor</packageId>
      <displayName>Justiciar Valor</displayName>
      <downloadUrl>https://github.com/mort1s1234/JusticiarValor</downloadUrl>
    </li>
  </modDependencies>
  <loadAfter><li>Mortis.JusticiarValor</li></loadAfter>
  <description>Created and removed by Justiciar Valor's Source/Tests/Run-GameValidation.ps1.</description>
</ModMetaData>
"@ | Set-Content -LiteralPath (Join-Path $testMod 'About\About.xml') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $harness 'JusticiarValorValidation.dll') -Destination (Join-Path $testMod 'Assemblies')

    # Neither 1.6 nor 1.7 (since 1.7.4990, whose -mod only takes mods from the local Mods folder) loads the Workshop's
    # copies: junctions bring the dependencies into the Mods folder.
    $deps = @(@{ Id = '2009463077'; Name = 'Harmony' }, @{ Id = '2023507013'; Name = 'VEF' })
    if ($WithVPE) { $deps += @{ Id = '2842502659'; Name = 'VPE' } }
    foreach ($dep in $deps) {
        # An install that already has the dependency in its Mods folder uses its own copy.
        if (Test-Path -LiteralPath (Join-Path $GameDir ("Mods\" + $dep.Id))) { continue }
        $link = Join-Path $GameDir ("Mods\JusticiarValor-validation-" + $dep.Name)
        if (-not (Test-Path -LiteralPath $link)) {
            New-Item -ItemType Junction -Path $link -Target (Resolve-Path (Join-Path $workshop $dep.Id)).Path | Out-Null
            $links += $link
        }
    }

    if ($Version -eq '1.7') {
        $exe = Join-Path $GameDir 'RimWorld.exe'
        $windowStyle = 'Hidden'
        $vpe = if ($WithVPE) { 'VanillaExpanded.VPsycastsE;' } else { '' }
        $arguments = @('-NoWindow', '-NoFileLog', '-quicktest', '-jvvalidate', "-jvreport=$report", "-savedatafolder=$saveData",
            "-mod=brrainz.harmony;OskarPotocki.VanillaFactionsExpanded.Core;$($vpe)Mortis.JusticiarValor;$testPackage")
        if ($Record) { $arguments += '-jvrecord' }
    }
    else {
        # 1.6 does not see this mod folder either.
        if (Test-Path -LiteralPath $modLink) { throw "$modLink already exists" }
        New-Item -ItemType Junction -Path $modLink -Target $modRoot | Out-Null
        $links += $modLink
        # Expansions in release order: Odyssey's defs inherit from Royalty's, so alphabetical order breaks them.
        $releaseOrder = @('Royalty', 'Ideology', 'Biotech', 'Anomaly', 'Odyssey')
        $installed = Get-ChildItem -LiteralPath (Join-Path $GameDir 'Data') -Directory | Where-Object Name -ne 'Core' | ForEach-Object Name
        $expansions = ($releaseOrder | Where-Object { $installed -contains $_ }) + ($installed | Where-Object { $releaseOrder -notcontains $_ }) |
            ForEach-Object { '    <li>ludeon.rimworld.' + $_.ToLowerInvariant() + '</li>' }
        $active = @('brrainz.harmony', 'ludeon.rimworld') + ($expansions | ForEach-Object { ($_ -replace '\s*</?li>\s*', '') }) +
            @('oskarpotocki.vanillafactionsexpanded.core') + @(if ($WithVPE) { 'vanillaexpanded.vpsycastse' }) +
            @('mortis.justiciarvalor', $testPackage.ToLowerInvariant())
        @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>$gameVersion</version>
  <activeMods>
$(($active | ForEach-Object { "    <li>$_</li>" }) -join "`n")
  </activeMods>
  <knownExpansions>
$($expansions -join "`n")
  </knownExpansions>
</ModsConfigData>
"@ | Set-Content -LiteralPath (Join-Path $saveData 'Config\ModsConfig.xml') -Encoding utf8
        & reg.exe export $registryKey $registryBackup /y 2>&1 | Out-Null
        $exe = Join-Path $GameDir 'RimWorldWin64.exe'
        $windowStyle = if ($VisibleWindow) { 'Normal' } else { 'Hidden' }
        $arguments = @('-quicktest', '-jvvalidate', "-jvreport=$report", "-savedatafolder=$saveData", '-logFile', (Join-Path $WorkDir 'Player.log'),
            '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080')
        if ($Record) { $arguments += '-jvrecord' }
    }
    if ($EffectsOnly) { $arguments += '-jvfxonly' }
    if ($Record -and $RecordOnly) { $arguments += "-jvrecordonly=$($RecordOnly -join ',')" }
    $started = Get-Date
    $process = Start-Process -FilePath $exe -WorkingDirectory $GameDir -ArgumentList $arguments -PassThru -WindowStyle $windowStyle `
        -RedirectStandardOutput $console -RedirectStandardError (Join-Path $WorkDir 'console-error.log')
    # While recording, each finished clip is encoded as the game goes on with the next.
    while (-not $process.WaitForExit(1000)) {
        if ($Record) { Invoke-ClipEncoding }
        if (((Get-Date) - $started).TotalSeconds -gt $TimeoutSeconds) {
            $process.Kill($true)
            throw "RimWorld did not finish within $TimeoutSeconds s; see $console and $report"
        }
    }
    Write-Host ("RimWorld {0} exited with code {1} after {2:N0} s" -f $gameVersion, $process.ExitCode, ((Get-Date) - $started).TotalSeconds)
}
finally {
    if (Test-Path -LiteralPath (Join-Path $testMod $testModMarker)) { Remove-Item -LiteralPath $testMod -Recurse -Force }
    foreach ($link in $links) {
        $item = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
        # Deleting a junction leaves the folder it points to untouched.
        if ($item -and $item.LinkType -eq 'Junction') { [System.IO.Directory]::Delete($link) }
    }
    if (Test-Path -LiteralPath $registryBackup) {
        & reg.exe delete $registryKey /f 2>&1 | Out-Null
        & reg.exe import $registryBackup 2>&1 | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $report)) { throw "No validation report; see $console" }
Get-Content -LiteralPath $report -Encoding utf8

if ($Record) {
    Invoke-ClipEncoding
    $clips = @(Get-ChildItem -LiteralPath $clipDir -Filter '*.mp4' -File -ErrorAction SilentlyContinue | Sort-Object Name)
    foreach ($clip in $clips) {
        $info = if (Test-Path -LiteralPath $ffprobe) {
            (& $ffprobe -v error -select_streams v:0 -count_frames -show_entries stream=width,height,nb_read_frames -of csv=p=0 $clip.FullName) -join ''
        }
        Write-Host ("{0}: {1:N0} KB, {2}" -f $clip.Name, ($clip.Length / 1KB), $info)
    }
    if ($RecordOut) {
        New-Item -ItemType Directory -Force -Path $RecordOut | Out-Null
        $clips | Copy-Item -Destination $RecordOut -Force
        Write-Host "Clips: $RecordOut"
    }
    else { Write-Host "Clips: $clipDir" }
    if (-not (Select-String -LiteralPath $report -Pattern '^RECORDING_PASSED' -Quiet) -or $encoded.Values -contains $false -or $clips.Count -eq 0) {
        throw "Recording failed; see $report and $console"
    }
    return
}

# The effects screenshot against the baseline: same camera, every changed pixel belongs to the effects. A shader that
# failed to compile is drawn with the engine's magenta fallback.
Add-Type -AssemblyName System.Drawing
function Read-Pixels([string]$Path) {
    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        $rect = [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
        $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bytes = [byte[]]::new($data.Stride * $bitmap.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $bitmap.UnlockBits($data)
        return @{ Width = $bitmap.Width; Height = $bitmap.Height; Stride = $data.Stride; Bytes = $bytes }
    } finally { $bitmap.Dispose() }
}
$shots = Join-Path $saveData 'Screenshots'
$prefix = "jv-$Version-"
$baselinePath = Join-Path $shots ($prefix + 'baseline.png')
$failed = -not (Select-String -LiteralPath $report -Pattern '^RUNTIME_VALIDATION_PASSED' -Quiet)
$a = if (Test-Path -LiteralPath $baselinePath) { Read-Pixels $baselinePath } else { $null }
# Each effects screenshot against the baseline: how many pixels the effects changed, and how many of those are magenta.
foreach ($shot in @(@{ Name = 'effects'; MinChanged = 5000 }, @{ Name = 'missions'; MinChanged = 3000 })) {
    $path = Join-Path $shots ($prefix + $shot.Name + '.png')
    if (-not $a -or -not (Test-Path -LiteralPath $path)) {
        throw "Runtime validation failed before its screenshots; see $report and $console"
    }
    $b = Read-Pixels $path
    $changed = 0; $magenta = 0
    for ($i = 0; $i -lt $b.Bytes.Length; $i += 4) {
        $d = [Math]::Abs([int]$b.Bytes[$i] - $a.Bytes[$i]) + [Math]::Abs([int]$b.Bytes[$i + 1] - $a.Bytes[$i + 1]) + [Math]::Abs([int]$b.Bytes[$i + 2] - $a.Bytes[$i + 2])
        if ($d -gt 24) {
            $changed++
            if ($b.Bytes[$i + 2] -gt 220 -and $b.Bytes[$i] -gt 220 -and $b.Bytes[$i + 1] -lt 40) { $magenta++ }
        }
    }
    Write-Host ("{0} screenshot {1}x{2}: {3} pixels changed against the baseline, {4} magenta" -f $shot.Name, $b.Width, $b.Height, $changed, $magenta)
    if ($changed -lt $shot.MinChanged) { Write-Warning "The $($shot.Name) effects barely changed the screenshot"; $failed = $true }
    if ($magenta * 50 -gt $changed) { Write-Warning "Magenta pixels in $($shot.Name): the engine substituted its fallback shader"; $failed = $true }
}
Write-Host "Screenshots: $shots"
if ($failed) { throw "Runtime validation failed; see $report and $console" }
