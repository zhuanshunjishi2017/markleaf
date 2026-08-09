# MarkLeaf Release Builder
# Builds both self-contained and framework-dependent MSIs.
# Version is read automatically from src/MarkLeaf/MarkLeaf.csproj.
#
# Usage: powershell -File setup/release.ps1
#   or:  powershell -File setup/release.ps1 -Version 2.0.0

param([string]$Version)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$setupProj = Join-Path $root "setup\MarkLeaf.Setup.wixproj"
$releaseDir = Join-Path $root "release"

# Read version from csproj if not explicitly provided
if (-not $Version) {
    $csproj = Join-Path $root "src\MarkLeaf\MarkLeaf.csproj"
    $xml = [xml](Get-Content $csproj)
    $Version = $xml.Project.PropertyGroup.Version
    if (-not $Version) {
        Write-Error "Version not found in $csproj"
        exit 1
    }
}

Write-Host "=== MarkLeaf v$Version Release Build ===" -ForegroundColor Cyan
Write-Host ""

# Clean previous output
Remove-Item -Recurse -Force $releaseDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory $releaseDir -Force | Out-Null

# Build self-contained MSI
Write-Host "[1/2] Building self-contained MSI..." -ForegroundColor Yellow
dotnet build $setupProj -c Release -p:SelfContained=true -p:Version=$Version
Copy-Item "$root\setup\bin\Release\MarkLeaf-$Version-x64.msi" $releaseDir

# Build framework-dependent MSI
Write-Host "[2/2] Building framework-dependent MSI..." -ForegroundColor Yellow
dotnet build $setupProj -c Release -p:SelfContained=false -p:Version=$Version
Copy-Item "$root\setup\bin\Release\MarkLeaf-$Version-x64fd.msi" $releaseDir

# Copy changelog
$changelog = Join-Path $root "src\MarkLeaf\Resources\Changelog\changelog.md"
if (Test-Path $changelog) {
    Copy-Item $changelog "$releaseDir\CHANGELOG.md"
}

# Generate checksums
Write-Host ""
Write-Host "=== Release Files ===" -ForegroundColor Green
Get-ChildItem $releaseDir | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    "$($hash)  $($_.Name)" | Out-File "$releaseDir\SHA256SUMS.txt" -Append
    $size = "{0:N1} MB" -f ($_.Length / 1MB)
    Write-Host "  $($_.Name)  $size"
    Write-Host "    SHA256: $hash"
}

Write-Host ""
Write-Host "Release files in: $releaseDir" -ForegroundColor Green
