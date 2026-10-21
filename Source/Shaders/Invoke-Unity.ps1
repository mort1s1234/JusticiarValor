param(
    # Build: the 1.6 asset bundles into 1.6/AssetBundles. Preview: a PNG of every effect at several moments.
    [ValidateSet('Build', 'Preview', 'BakePiloto')][string]$Task = 'Build',
    # The Unity 2022.3 project the shaders are generated into (RimWorld 1.6 runs Unity 2022.3).
    [string]$UnityProject = (Join-Path $env:USERPROFILE 'Desktop\project\MRKshader合集'),
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe',
    [string]$PreviewOut = (Join-Path ([System.IO.Path]::GetTempPath()) 'justiciarvalor-build\fx-preview.png'),
    [string]$PreviewTexture,
    [string]$PilotoOut = (Join-Path ([System.IO.Path]::GetTempPath()) 'justiciarvalor-build\piloto'),
    [string[]]$PilotoOnly,
    [int]$TimeoutSeconds = 1200
)
# Regenerates the shaders (build_shaders.py --unity) and runs the Unity editor in batch mode. The project must not be
# open in another editor. The log goes to %TEMP%\justiciarvalor-build\unity-<task>.log.
$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& python (Join-Path $PSScriptRoot 'build_shaders.py') --unity $UnityProject
if ($LASTEXITCODE -ne 0) { throw 'build_shaders.py failed' }
if (Test-Path -LiteralPath (Join-Path $UnityProject 'Temp\UnityLockfile')) {
    $lock = Get-Item -LiteralPath (Join-Path $UnityProject 'Temp\UnityLockfile')
    try { $stream = $lock.Open('Open', 'ReadWrite', 'None'); $stream.Close() } catch { throw "$UnityProject is open in the Unity editor; close it first." }
}
$logDir = Join-Path ([System.IO.Path]::GetTempPath()) 'justiciarvalor-build'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ("unity-" + $Task.ToLowerInvariant() + ".log")
$arguments = @('-batchmode', '-quit', '-projectPath', "`"$UnityProject`"", '-logFile', "`"$log`"")
if ($Task -eq 'BakePiloto') {
    $arguments += @('-executeMethod', 'JusticiarPilotoBaker.Bake', '-jvPilotoOut', "`"$PilotoOut`"")
    if ($PilotoOnly) { $arguments += @('-jvPilotoOnly', ($PilotoOnly -join ',')) }
}
elseif ($Task -eq 'Build') {
    $arguments += @('-executeMethod', 'JusticiarValorBundleBuilder.Build', '-jvOutput', "`"$(Join-Path $modRoot '1.6\AssetBundles')`"")
}
else {
    if (-not $PreviewTexture) {
        $PreviewTexture = Join-Path $modRoot 'Common\Textures\Things\Pawn\Humanlike\Apparel\PromachosArmor\PromachosArmor_Male_south.png'
    }
    $arguments += @('-executeMethod', 'JusticiarValorBundleBuilder.Preview', '-jvPreviewOut', "`"$PreviewOut`"", '-jvPreviewTexture', "`"$PreviewTexture`"",
        '-jvTextureRoot', "`"$(Join-Path $modRoot 'Common\Textures\Effects\JusticiarValor\Piloto')`"")
}
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    throw "Unity did not finish within $TimeoutSeconds s; see $log"
}
Select-String -LiteralPath $log -Pattern '\[JusticiarValor\]|error CS|Shader error|Exception' | ForEach-Object { $_.Line }
if ($process.ExitCode -ne 0) { throw "Unity exited with code $($process.ExitCode); see $log" }
if ($Task -eq 'Preview') { Write-Output "Preview: $PreviewOut" }
if ($Task -eq 'BakePiloto') {
    $textureDir = Join-Path $modRoot 'Common\Textures\Effects\JusticiarValor\Piloto'
    New-Item -ItemType Directory -Force -Path $textureDir | Out-Null
    $manifest = if ($PilotoOnly) { 'sources-partial.tsv' } else { 'sources.tsv' }
    $clips = @(Import-Csv -LiteralPath (Join-Path $PilotoOut $manifest) -Delimiter "`t")
    $expected = if ($PilotoOnly) { $PilotoOnly.Count } else { 22 }
    if ($clips.Count -ne $expected) { throw "Expected $expected successfully baked Piloto clips" }
    foreach ($clip in $clips) {
        Copy-Item -LiteralPath (Join-Path $PilotoOut ($clip.clip + '.png')) -Destination $textureDir -Force
    }
    $sourceManifest = Join-Path $PSScriptRoot 'PilotoSources.tsv'
    if ($PilotoOnly) {
        $merged = @(Import-Csv -LiteralPath $sourceManifest -Delimiter "`t" | Where-Object { $_.clip -notin $PilotoOnly }) + $clips
        $merged | Export-Csv -LiteralPath $sourceManifest -Delimiter "`t" -NoTypeInformation -UseQuotes Never
    } else {
        Copy-Item -LiteralPath (Join-Path $PilotoOut 'sources.tsv') -Destination $sourceManifest -Force
    }
    Write-Output "Piloto atlases: $textureDir"
}
