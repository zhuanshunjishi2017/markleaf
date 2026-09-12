# Builds a Microsoft Store-ready MSIX package for MarkLeaf.
# Partner Center identity values are set to the MarkLeaf store listing by default.
# Pass the parameters to override them for another listing or local test identity.
[CmdletBinding()]
param(
    [string]$Version,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Architectures,
    [bool]$SelfContained,
    [string]$OutputDirectory,
    [string]$Certificate,
    [string]$CertificatePassword
)

$ErrorActionPreference = "Stop"
$IdentityName = if ($IdentityName) { $IdentityName } else { "BD9CB97B.MarkLeaf" }
$Publisher = if ($Publisher) { $Publisher } else { "CN=D15CE179-6279-483E-9779-C9E4AC4A3BFA" }
$PublisherDisplayName = if ($PublisherDisplayName) { $PublisherDisplayName } else { ([char]0x6D6E).ToString() + ([char]0x6C89).ToString() + ([char]0x5B50).ToString() }
$architectureSpec = if ($Architectures) { $Architectures } else { 'win-x64' + [char]59 + 'win-arm64' }
$SelfContained = $true
$msixDir = $PSScriptRoot
$windowsDir = Split-Path -Parent $msixDir
$repoRoot = Split-Path -Parent (Split-Path -Parent $windowsDir)
$csproj = Join-Path $windowsDir "MarkLeaf\MarkLeaf.csproj"

if (-not $Version) {
    $xml = [xml](Get-Content -LiteralPath $csproj)
    $Version = [string]$xml.Project.PropertyGroup.Version
}
if (-not $Version) { throw "Version was not found in $csproj." }
$packageVersion = if ($Version -match '^\d+\.\d+\.\d+\.\d+$') { $Version } else { "${Version}.0" }

$buildNumber = (git -C $repoRoot rev-list --count HEAD 2>$null).Trim()
if (-not $buildNumber) { $buildNumber = "0" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $windowsDir "release" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$runtimes = $architectureSpec -split '[,;]' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
foreach ($runtimeName in $runtimes) {
    if ($runtimeName -notin @("win-x64", "win-arm64")) { throw "Unsupported runtime: $runtimeName" }
}

$makeAppx = (Get-Command makeappx.exe -ErrorAction SilentlyContinue).Source
if (-not $makeAppx) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $makeAppx = Get-ChildItem -LiteralPath $sdkRoot -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $makeAppx) { throw "makeappx.exe was not found. Install the Windows 10/11 SDK." }

foreach ($runtimeName in $runtimes) {
    $architecture = if ($runtimeName -eq "win-arm64") { "arm64" } else { "x64" }
    $publishDir = Join-Path $msixDir "publish-$runtimeName-$SelfContained"
    $stagingDir = Join-Path $msixDir "staging-$runtimeName"
    $packagePath = Join-Path $OutputDirectory "MarkLeaf-$Version-$architecture.msix"

    Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    Write-Host "Publishing MarkLeaf v$Version runtime $runtimeName self-contained $SelfContained..." -ForegroundColor Cyan
    dotnet publish $csproj -c Release -r $runtimeName --self-contained $SelfContained `
        -p:Version=$Version -p:BuildNumber=$buildNumber -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtimeName." }
    Copy-Item -Path (Join-Path $publishDir "*") -Destination $stagingDir -Recurse -Force

    $assetsDir = Join-Path $stagingDir "Assets"
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
    Copy-Item (Join-Path $windowsDir "MarkLeaf\Resources\App\fileicon.ico") (Join-Path $assetsDir "fileicon.ico") -Force

    Add-Type -AssemblyName System.Drawing
    $sourceImage = [System.Drawing.Image]::FromFile((Join-Path $windowsDir "MarkLeaf\Resources\App\App.png"))
    try {
        foreach ($size in @(44, 150)) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            } finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $assetsDir "Square${size}x${size}Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
        }
    } finally { $sourceImage.Dispose() }

$manifest = Get-Content -LiteralPath (Join-Path $msixDir "Package.appxmanifest.template") -Raw
$replacements = @{
    "__IDENTITY_NAME__" = $IdentityName
    "__PUBLISHER__" = $Publisher
    "__PUBLISHER_DISPLAY_NAME__" = $PublisherDisplayName
    "__VERSION__" = $packageVersion
    "__ARCHITECTURE__" = $architecture
}
foreach ($key in $replacements.Keys) {
    $manifest = $manifest.Replace($key, [System.Security.SecurityElement]::Escape($replacements[$key]))
}
    Set-Content -LiteralPath (Join-Path $stagingDir "AppxManifest.xml") -Value $manifest -Encoding UTF8

    Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
    & $makeAppx pack /d $stagingDir /p $packagePath /overwrite
    if ($LASTEXITCODE -ne 0) { throw "makeappx packaging failed for $runtimeName." }

    if ($Certificate) {
    $signTool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not $signTool) {
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
        $signTool = Get-ChildItem -LiteralPath $sdkRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
        if (-not $signTool) { throw "signtool.exe was not found." }
        $signArgs = @('sign', '/fd', 'SHA256', '/a', '/f', $Certificate)
        if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
        $signArgs += $packagePath
        & $signTool @signArgs
        if ($LASTEXITCODE -ne 0) { throw "MSIX signing failed for $runtimeName." }
    }
    Write-Host "Created $packagePath" -ForegroundColor Green
}
Write-Host "Identity: $IdentityName / $Publisher" -ForegroundColor Yellow
