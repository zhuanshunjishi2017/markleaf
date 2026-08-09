# Build both MarkLeaf MSI variants:
#   - Self-contained (includes .NET runtime, ~44 MB)
#   - Framework-dependent (requires .NET 10 Runtime, ~3 MB)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$setupProj = Join-Path $root "MarkLeaf.Setup.wixproj"

Write-Host "=== Building self-contained MSI ===" -ForegroundColor Cyan
dotnet build $setupProj -p:SelfContained=true -c Release

Write-Host "=== Building framework-dependent MSI ===" -ForegroundColor Cyan
dotnet build $setupProj -p:SelfContained=false -c Release

Write-Host "=== Done ===" -ForegroundColor Green
Get-ChildItem (Join-Path $root "bin\Release\") -Filter "*.msi" | ForEach-Object {
    Write-Host "$($_.Name)  $('{0:N1}' -f ($_.Length / 1MB)) MB"
}
