# MarkLeaf Release Builder
# Produces Inno Setup installers for x64 and arm64 in both variants,
# plus self-contained portable ZIP archives.
#   MarkLeaf-X.Y.Z-win-arch.exe               framework-dependent
#   MarkLeaf-X.Y.Z-win-arch-with-runtime.exe  self-contained installer
#   MarkLeaf-X.Y.Z-win-arch-with-runtime.zip  self-contained portable build
#
# Usage: powershell -File apps/windows/setup/release.ps1 [-Version X.Y.Z] [-BuildNumber N] [-Runtime win-x64] [-SelfContained]

param([string]$Version, [string]$BuildNumber, [string]$Runtime, [bool]$SelfContained)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$repoRoot = Split-Path -Parent (Split-Path -Parent $root)
$editorWebDir = Join-Path $repoRoot "packages\editor-web"
$setupScript = Join-Path $root "setup\markleaf.iss"
$releaseDir = Join-Path $root "release"
$csproj = Join-Path $root "MarkLeaf\MarkLeaf.csproj"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 (ISCC.exe) was not found." }

if (-not $Version) {
    $xml = [xml](Get-Content $csproj)
    $Version = $xml.Project.PropertyGroup.Version
    if (-not $Version) { Write-Error "Version not found in $csproj"; exit 1 }
}
if (-not $BuildNumber) {
    try {
        $BuildNumber = (git -C $root rev-list --count HEAD 2>$null).Trim()
    } catch {
        $BuildNumber = $null
    }
    if (-not $BuildNumber) { $BuildNumber = "0" }
}

$runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-arm64") }
$scFlags  = if ($PSBoundParameters.ContainsKey('SelfContained')) { @($SelfContained) } else { @($true, $false) }

Write-Host "=== MarkLeaf v$Version (Build $BuildNumber) Release ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Building EditorWeb..." -ForegroundColor Yellow
pnpm --dir $editorWebDir install --frozen-lockfile
if ($LASTEXITCODE -ne 0) { throw "EditorWeb dependency installation failed." }
pnpm --dir $editorWebDir build
if ($LASTEXITCODE -ne 0) { throw "EditorWeb build failed." }

# Clean
Remove-Item -Recurse -Force $releaseDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory $releaseDir -Force | Out-Null
$total = $runtimes.Count * $scFlags.Count
$i = 0
foreach ($rt in $runtimes) {
    foreach ($sc in $scFlags) {
        $i++
        $label = if ($sc) { "with-runtime" } else { "slim" }
        Write-Host "[$i/$total] $rt $label" -ForegroundColor Yellow
        $arch = $rt.Substring(4)
        $archAllowed = if ($arch -eq "arm64") { "arm64" } else { "x64compatible" }
        $publishDir = Join-Path $root "setup\publish-$rt-$sc"
        $publishDir = [IO.Path]::GetFullPath($publishDir)
        Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
        dotnet publish (Join-Path $root "MarkLeaf\MarkLeaf.csproj") -c Release -r $rt --self-contained $sc -p:Version=$Version -p:BuildNumber=$BuildNumber -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw ".NET publish failed for $rt ($label)." }
        $editorIndex = Join-Path $publishDir "EditorWeb\index.html"
        $editorAssets = Join-Path $publishDir "EditorWeb\assets"
        if (-not (Test-Path $editorIndex) -or -not (Test-Path $editorAssets)) {
            throw "Published EditorWeb resources are missing for $rt ($label)."
        }
        & $iscc "/DMyAppVersion=$Version" "/DBuildNumber=$BuildNumber" "/DAppArchitecture=$arch" "/DAppArchitectureAllowed=$archAllowed" "/DSelfContained=$(if($sc){1}else{0})" "/DSourceDir=$publishDir" "/O$(Join-Path $root 'setup')" $setupScript
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for $rt ($label)." }
        $installerName = "MarkLeaf-$Version-win-$arch$(if($sc){'-with-runtime'}else{''}).exe"
        Copy-Item (Join-Path $root "setup\$installerName") $releaseDir
        if ($sc) {
            Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath (Join-Path $releaseDir "MarkLeaf-$Version-win-$arch-with-runtime.zip") -Force
        }
    }
}

# Collect extras
$changelog = Join-Path $root "MarkLeaf\Resources\Changelog\changelog.md"
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
