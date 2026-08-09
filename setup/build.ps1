# Quick dev build — all architectures, self-contained + framework-dependent
# Usage: powershell -File setup/build.ps1 [-Runtime win-x64] [-SelfContained $true]
param([string]$Runtime, [bool]$SelfContained)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$setupProj = Join-Path $root "setup\MarkLeaf.Setup.wixproj"

$csproj = Join-Path $root "src\MarkLeaf\MarkLeaf.csproj"
$xml = [xml](Get-Content $csproj)
$v = $xml.Project.PropertyGroup.Version
if (-not $v) { Write-Error "Version not found"; exit 1 }

$runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-x86", "win-arm64") }
$scFlags = if ($PSBoundParameters.ContainsKey('SelfContained')) { @($SelfContained) } else { @($true, $false) }

Write-Host "=== MarkLeaf v$v ===" -ForegroundColor Cyan

foreach ($rt in $runtimes) {
    foreach ($sc in $scFlags) {
        Write-Host "Building $rt self-contained=$sc..." -ForegroundColor Yellow
        dotnet build $setupProj -c Release -p:Runtime=$rt -p:SelfContained=$sc -p:Version=$v
    }
}

Write-Host ""
Write-Host "=== Output ===" -ForegroundColor Green
Get-ChildItem "$root\setup\bin\Release" -Filter "*.msi" | Sort-Object Name | ForEach-Object {
    Write-Host "$($_.Name)  $('{0:N1}' -f ($_.Length/1MB)) MB"
}
