# MarkLeaf Release Builder
# Produces MSIs for x64, x86, arm64 in both variants.
#   MarkLeaf-X.Y.Z-arch.msi               framework-dependent (slim, needs .NET 10)
#   MarkLeaf-X.Y.Z-arch-with-runtime.msi   self-contained (bundles .NET runtime)
#
# Usage: powershell -File setup/release.ps1 [-Version X.Y.Z] [-Runtime win-x64] [-SelfContained]

param([string]$Version, [string]$Runtime, [bool]$SelfContained)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$setupProj = Join-Path $root "setup\MarkLeaf.Setup.wixproj"
$releaseDir = Join-Path $root "release"

if (-not $Version) {
    $csproj = Join-Path $root "src\MarkLeaf\MarkLeaf.csproj"
    $xml = [xml](Get-Content $csproj)
    $Version = $xml.Project.PropertyGroup.Version
    if (-not $Version) { Write-Error "Version not found in $csproj"; exit 1 }
}

$runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-x86", "win-arm64") }
$scFlags  = if ($PSBoundParameters.ContainsKey('SelfContained')) { @($SelfContained) } else { @($true, $false) }

Write-Host "=== MarkLeaf v$Version Release ===" -ForegroundColor Cyan
Write-Host ""

# Clean
Remove-Item -Recurse -Force $releaseDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory $releaseDir -Force | Out-Null
dotnet clean $setupProj -v q | Out-Null

$total = $runtimes.Count * $scFlags.Count
$i = 0
foreach ($rt in $runtimes) {
    foreach ($sc in $scFlags) {
        $i++
        $label = if ($sc) { "with-runtime" } else { "slim" }
        Write-Host "[$i/$total] $rt $label" -ForegroundColor Yellow
        dotnet build $setupProj -c Release -p:Runtime=$rt -p:SelfContained=$sc -p:Version=$Version
        # Copy to release
        $arch = $rt.Substring(4)
        $prefix = "MarkLeaf-$Version-$arch"
        $pattern = Join-Path $root "setup\bin\Release\$prefix*.msi"
        Get-ChildItem $pattern | Copy-Item -Destination $releaseDir
    }
}

# Collect extras
$changelog = Join-Path $root "src\MarkLeaf\Resources\Changelog\changelog.md"
if (Test-Path $changelog) { Copy-Item $changelog "$releaseDir\CHANGELOG.md" }

# Checksums
Get-ChildItem $releaseDir -File | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    "$hash  $($_.Name)" | Out-File "$releaseDir\SHA256SUMS.txt" -Append
}

# Summary
Write-Host ""
Write-Host "=== $releaseDir ===" -ForegroundColor Green
Get-ChildItem $releaseDir -File | Sort-Object Name | ForEach-Object {
    $size = "{0,7:N1} MB" -f ($_.Length / 1MB)
    $label = if ($_.Name -match "with-runtime") { "(含 .NET 运行时)" } else { "(需安装 .NET 10)" }
    Write-Host "  $size  $($_.Name)  $label"
}
Write-Host ""
Write-Host "Done." -ForegroundColor Green
