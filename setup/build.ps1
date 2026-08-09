# Quick dev build - reads version from csproj automatically
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$setupProj = Join-Path $root "setup\MarkLeaf.Setup.wixproj"

$csproj = Join-Path $root "src\MarkLeaf\MarkLeaf.csproj"
$xml = [xml](Get-Content $csproj)
$v = $xml.Project.PropertyGroup.Version
if (-not $v) { Write-Error "Version not found"; exit 1 }

Write-Host "=== MarkLeaf v$v ===" -ForegroundColor Cyan

dotnet clean $setupProj -v q | Out-Null

dotnet build $setupProj -c Release -p:SelfContained=true -p:Version=$v
Write-Host ""

dotnet build $setupProj -c Release -p:SelfContained=false -p:Version=$v
Write-Host ""

Get-ChildItem "$root\setup\bin\Release" -Filter "*.msi" | ForEach-Object {
    Write-Host "$($_.Name)  $('{0:N1}' -f ($_.Length/1MB)) MB"
}
