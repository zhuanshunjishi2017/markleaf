# Quick local installer build.
# Self-contained → MarkLeaf-X.Y.Z-win-arch-with-runtime.exe
# Framework-dependent → MarkLeaf-X.Y.Z-win-arch.exe
param([string]$Runtime, [string]$BuildNumber, [bool]$SelfContained)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$repoRoot = Split-Path -Parent (Split-Path -Parent $root)
$editorWebDir = Join-Path $repoRoot "packages\editor-web"
$setupScript = Join-Path $root "setup\markleaf.iss"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 (ISCC.exe) was not found." }

$csproj = Join-Path $root "MarkLeaf\MarkLeaf.csproj"
$xml = [xml](Get-Content $csproj)
$v = $xml.Project.PropertyGroup.Version
if (-not $v) { Write-Error "Version not found"; exit 1 }
if (-not $BuildNumber) {
    try {
        $BuildNumber = (git -C $root rev-list --count HEAD 2>$null).Trim()
    } catch {
        $BuildNumber = $null
    }
    if (-not $BuildNumber) { $BuildNumber = "0" }
}

$runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-arm64") }
$scFlags = if ($PSBoundParameters.ContainsKey('SelfContained')) { @($SelfContained) } else { @($true, $false) }

Write-Host "=== MarkLeaf v$v (Build $BuildNumber) ===" -ForegroundColor Cyan

Write-Host "  Building EditorWeb..." -ForegroundColor Yellow
pnpm --dir $editorWebDir install --frozen-lockfile
if ($LASTEXITCODE -ne 0) { throw "EditorWeb dependency installation failed." }
pnpm --dir $editorWebDir build
if ($LASTEXITCODE -ne 0) { throw "EditorWeb build failed." }

foreach ($rt in $runtimes) {
    foreach ($sc in $scFlags) {
        $label = if ($sc) { "with-runtime" } else { "slim" }
        Write-Host "  Building $rt $label..." -ForegroundColor Yellow
        $arch = $rt.Substring(4)
        $archAllowed = if ($arch -eq "arm64") { "arm64" } else { "x64compatible" }
        $publishDir = Join-Path $root "setup\publish-$rt-$sc"
        $publishDir = [IO.Path]::GetFullPath($publishDir)
        Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
        dotnet publish (Join-Path $root "MarkLeaf\MarkLeaf.csproj") -c Release -r $rt --self-contained $sc -p:Version=$v -p:BuildNumber=$BuildNumber -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw ".NET publish failed for $rt ($label)." }
        $editorIndex = Join-Path $publishDir "EditorWeb\index.html"
        $editorAssets = Join-Path $publishDir "EditorWeb\assets"
        if (-not (Test-Path $editorIndex) -or -not (Test-Path $editorAssets)) {
            throw "Published EditorWeb resources are missing for $rt ($label)."
        }
        & $iscc "/DMyAppVersion=$v" "/DBuildNumber=$BuildNumber" "/DAppArchitecture=$arch" "/DAppArchitectureAllowed=$archAllowed" "/DSelfContained=$(if($sc){1}else{0})" "/DSourceDir=$publishDir" "/O$(Join-Path $root 'setup')" $setupScript
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for $rt ($label)." }
    }
}

Write-Host ""
Write-Host "=== Output ===" -ForegroundColor Green
Get-ChildItem "$root\setup" -Filter "MarkLeaf-*.exe" | Sort-Object Name | ForEach-Object {
    Write-Host "  $('{0,6:N1}' -f ($_.Length/1MB)) MB  $($_.Name)"
}
