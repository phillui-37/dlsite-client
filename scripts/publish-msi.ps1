#Requires -Version 5.1
<#
.SYNOPSIS
  Build Windows MSI installer (WiX) over AOT publish output.
  Wizard asks per-user (no UAC) or all users (UAC).
#>
param([switch]$SkipPublish)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$AotDir = Join-Path $Root "artifacts\aot-win-x64"
$MsiOut = Join-Path $Root "artifacts\msi"
$Wxs = Join-Path $Root "packaging\windows\Product.wxs"

if (-not $SkipPublish -or -not (Test-Path (Join-Path $AotDir "DlsiteClient.App.exe"))) {
    Write-Host "Publishing current AOT payload ..."
    & "$PSScriptRoot\publish-aot.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Native AOT publish failed." }
}

# Ship the full decoder (7z.exe + 7z.dll includes RAR support), with its license.
$sevenZipFolders = @(
    "$env:USERPROFILE\scoop\apps\7zip\current",
    "$env:ProgramFiles\7-Zip"
)
$sevenZipFolder = $sevenZipFolders | Where-Object {
    (Test-Path (Join-Path $_ "7z.exe")) -and (Test-Path (Join-Path $_ "7z.dll")) -and
    (Test-Path (Join-Path $_ "License.txt"))
} | Select-Object -First 1
if (-not $sevenZipFolder) { throw "Install 7-Zip before building the MSI; its decoder and license are required." }
$toolsDir = Join-Path $AotDir "tools"
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
foreach ($file in @("7z.exe", "7z.dll", "License.txt")) {
    Copy-Item -LiteralPath (Join-Path $sevenZipFolder $file) -Destination $toolsDir -Force
}

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    Write-Host "Installing WiX CLI (dotnet tool) ..."
    dotnet tool update --global wix
    $env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
}

New-Item -ItemType Directory -Force -Path $MsiOut | Out-Null
$msi = Join-Path $MsiOut "DlsiteClient-0.1.2-win-x64.msi"

# Use native Windows Installer dual-purpose scope. Do not patch SummaryInformation:
# the UAC-compliant bit makes Windows Installer ignore MSIINSTALLPERUSER.
& wix extension add -g WixToolset.UI.wixext/7.0.0 -acceptEula wix7
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& wix build $Wxs -ext WixToolset.UI.wixext/7.0.0 -arch x64 -b "app=$AotDir" -acceptEula wix7 -o $msi
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: $msi"
Write-Host "Install: msiexec /i `"$msi`""
