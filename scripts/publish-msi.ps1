#Requires -Version 5.1
<#
.SYNOPSIS
  Build Windows MSI installer (WiX) over AOT publish output.
  Wizard UI = standard MSI UI (WiX default).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$AotDir = Join-Path $Root "artifacts\aot-win-x64"
$MsiOut = Join-Path $Root "artifacts\msi"
$Wxs = Join-Path $Root "packaging\windows\Product.wxs"

if (-not (Test-Path (Join-Path $AotDir "DlsiteClient.App.exe"))) {
    Write-Host "AOT payload missing — running publish-aot.ps1 ..."
    & "$PSScriptRoot\publish-aot.ps1"
}

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    Write-Host "Installing WiX CLI (dotnet tool) ..."
    dotnet tool update --global wix
    $env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
}

New-Item -ItemType Directory -Force -Path $MsiOut | Out-Null
$msi = Join-Path $MsiOut "DlsiteClient-0.1.0-win-x64.msi"

# WiX v4/v5: bindpath.app points at AOT folder
& wix build $Wxs -b "app=$AotDir" -o $msi
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: $msi"
Write-Host "Install: msiexec /i `"$msi`""
