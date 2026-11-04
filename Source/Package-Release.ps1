param(
    [string]$OutputPath = (Join-Path ([System.IO.Path]::GetTempPath()) ('JusticiarValor-release\JusticiarValor-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip'))
)
$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw "Release already exists: $OutputPath" }

# Explicit runtime inputs: never include development tools, raw artwork, local documents or test recordings.
$runtimeFiles = @('About\About.xml', 'About\Preview.png', 'About\ModIcon.png', 'About\PublishedFileId.txt', 'LoadFolders.xml',
    '1.6\Assemblies\JusticiarValor.dll', '1.6\AssetBundles\justiciarvalor_win', '1.6\AssetBundles\justiciarvalor_mac',
    '1.7\Assemblies\JusticiarValor.dll')
$runtimeFolders = @('Common\Defs', 'Common\Patches', 'Common\Languages', 'Common\Textures', 'Common\CE',
    '1.7\Defs', '1.7\Shaders', 'WeaponTweakData')
foreach ($relative in $runtimeFiles + $runtimeFolders) {
    if (-not (Test-Path -LiteralPath (Join-Path $modRoot $relative))) { throw "Missing runtime input: $relative" }
}
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$stage = Join-Path $tempRoot ('JusticiarValor-package-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'Justiciar Valor'
try {
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    foreach ($relative in $runtimeFiles) {
        $target = Join-Path $package $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $modRoot $relative) -Destination $target
    }
    foreach ($relative in $runtimeFolders) {
        $target = Join-Path $package $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $modRoot $relative) -Destination $target -Recurse
    }
    # The runtime uses ten animation atlases plus Sheet. Do not package obsolete atlases after an old bake.
    $obsolete = @('CelestialBlade', 'Conversion', 'Deflect', 'InvictusBurst', 'OrbFlight', 'OrbImpact',
        'PsychicShield', 'SermonHalo', 'ShieldBreak', 'ShieldHit', 'ShieldRecharge', 'ThreatPulse')
    foreach ($name in $obsolete) {
        $oldAtlas = Join-Path $package ("Common\Textures\Effects\JusticiarValor\Piloto\$name.png")
        if (Test-Path -LiteralPath $oldAtlas) { Remove-Item -LiteralPath $oldAtlas }
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $OutputPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    [pscustomobject]@{
        Path = $OutputPath
        Bytes = (Get-Item -LiteralPath $OutputPath).Length
        SHA256 = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
    }
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stage)
    if ((-not $resolvedStage.StartsWith($tempRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) -or
        ([System.IO.Path]::GetFileName($resolvedStage) -notlike 'JusticiarValor-package-*')) {
        throw "Refusing to clean unexpected staging path: $resolvedStage"
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
