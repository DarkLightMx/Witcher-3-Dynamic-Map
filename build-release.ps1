# Builds the release folder "publish": exe + Icons + Data\Maps (map tiles shipped with the app).
# README.md and the modMapSync mod folder inside "publish" are left untouched.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-release.ps1
# The tiles are copied from %LocalAppData%\Witcher3DynamicMap\Data\Maps (the folder the dev build uses).

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'
$tilesSource = Join-Path $env:LOCALAPPDATA 'Witcher3DynamicMap\Data\Maps'
$worlds = 'hos_velen', 'skellige', 'kaer_morhen', 'toussaint', 'white_orchard'

dotnet publish (Join-Path $root 'Witcher 3 Dynamic Map.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

foreach ($w in $worlds) {
    $src = Join-Path $tilesSource $w
    if (-not (Test-Path $src)) { throw "Tiles not found: $src" }
    $dst = Join-Path $out "Data\Maps\$w"
    robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $w" }
}

$mb = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
'Done: {0} ({1:N0} MB)' -f $out, $mb
