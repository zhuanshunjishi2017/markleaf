# MarkLeaf Release Builder
# Builds MSIs for all architectures (x64, x86, arm64) in both variants.
# Version is read automatically from src/MarkLeaf/MarkLeaf.csproj.
#
# Usage:
#   powershell -File setup/release.ps1
#   powershell -File setup/release.ps1 -Version 2.0.0
#   powershell -File setup/release.ps1 -Runtime win-x64 -SelfContained $true

param([string]$Version, [string]$Runtime, [bool]$SelfContained)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$setupProj = Join-Path $root "setup\MarkLeaf.Setup.wixproj"
$releaseDir = Join-Path $root "release"

# Read version from csproj if not specified
if (-not $Version) {
    $csproj = Join-Path $root "src\MarkLeaf\MarkLeaf.csproj"
    $xml = [xml](Get-Content $csproj)
    $Version = $xml.Project.PropertyGroup.Version
    if (-not $Version) { Write-Error "Version not found in $csproj"; exit 1 }
}

# Determine what to build
$runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-x86", "win-arm64") }
$scFlags  = if ($PSBoundParameters.ContainsKey('SelfContained')) { @($SelfContained) } else { @($true, $false) }

Write-Host "=== MarkLeaf v$Version Release Build ===" -ForegroundColor Cyan
Write-Host "Architectures: $($runtimes -join ', ')"
Write-Host "Variants: $(if (1 -eq $scFlags.Count) { $scFlags[0] } else { 'self-contained + framework-dependent' })"
Write-Host ""

# Clean previous output
Remove-Item -Recurse -Force $releaseDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory $releaseDir -Force | Out-Null

# Clean WiX intermediates to avoid cross-arch contamination
dotnet clean $setupProj -v q | Out-Null

$total = $runtimes.Count * $scFlags.Count
$i = 0
foreach ($rt in $runtimes) {
    foreach ($sc in $scFlags) {
        $i++
        $label = if ($sc) { "self-contained" } else { "framework-dependent" }
        Write-Host "[$i/$total] Building $rt $label..." -ForegroundColor Yellow
        dotnet build $setupProj -c Release -p:Runtime=$rt -p:SelfContained=$sc -p:Version=$Version
        $arch = $rt.Substring(4)
        $suffix = if ($sc) { $arch } else { "${arch}fd" }
        $msi = "$root\setup\bin\Release\MarkLeaf-$Version-$suffix.msi"
        if (Test-Path $msi) {
            Copy-Item $msi $releaseDir
        }
    }
}

# Copy changelog
$changelog = Join-Path $root "src\MarkLeaf\Resources\Changelog\changelog.md"
if (Test-Path $changelog) { Copy-Item $changelog "$releaseDir\CHANGELOG.md" }

# Generate checksums
Get-ChildItem $releaseDir -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    "$hash  $($_.Name)" | Out-File "$releaseDir\SHA256SUMS.txt" -Append
}

# Summary
Write-Host ""
Write-Host "=== Release Files ($releaseDir) ===" -ForegroundColor Green
Get-ChildItem $releaseDir -File | Sort-Object Name | ForEach-Object {
    $size = "{0,6:N1} MB" -f ($_.Length / 1MB)
    Write-Host "  $size  $($_.Name)"
}

Write-Host ""
Write-Host "Done. $($runtimes.Count * 2) MSIs in release/" -ForegroundColor Green
