# Builds the release folder "publish": exe + Data\Icons + Data\Maps (icons and map tiles shipped with the app).
# README.md and the modMapSync mod folder inside "publish" are left untouched.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-release.ps1
# The icons are copied by the build itself (Data\Icons in the project). The tiles are taken from the project's
# Data\Maps, or from %LocalAppData%\Witcher3DynamicMap\Data\Maps when the project does not have them.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'
$worlds = 'hos_velen', 'skellige', 'kaer_morhen', 'toussaint', 'white_orchard'

$tilesSource = Join-Path $root 'Data\Maps'
if (-not (Test-Path (Join-Path $tilesSource $worlds[0]))) {
    $tilesSource = Join-Path $env:LOCALAPPDATA 'Witcher3DynamicMap\Data\Maps'
}

dotnet publish (Join-Path $root 'Witcher 3 Dynamic Map.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# Icons used to live in publish\Icons; they are in publish\Data\Icons now.
$oldIcons = Join-Path $out 'Icons'
if (Test-Path -LiteralPath $oldIcons) { Remove-Item -LiteralPath $oldIcons -Recurse -Force }

foreach ($w in $worlds) {
    $src = Join-Path $tilesSource $w
    if (-not (Test-Path $src)) { throw "Tiles not found: $src" }
    $dst = Join-Path $out "Data\Maps\$w"
    robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $w" }
}

$mb = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
'Done: {0} ({1:N0} MB)' -f $out, $mb
