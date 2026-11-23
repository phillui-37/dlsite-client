#Requires -Version 5.1
<#
.SYNOPSIS
  Build DLsite Client (Release).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
$SdkVersion = if ($Dotnet) { & $Dotnet --version 2>$null }
if (-not $SdkVersion -or $LASTEXITCODE -ne 0) {
    $Dotnet = "$env:USERPROFILE\scoop\apps\dotnet-sdk\current\dotnet.exe"
    if (-not (Test-Path $Dotnet)) { throw "Install the .NET SDK required by global.json and add dotnet to PATH." }
    & $Dotnet --version
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$DotnetSdkRoot = Split-Path -Parent $Dotnet
if (Test-Path (Join-Path $DotnetSdkRoot "sdk")) {
    $env:DOTNET_ROOT = $DotnetSdkRoot
    $env:DOTNET_ROOT_X64 = $DotnetSdkRoot
}
$env:MSBuildSDKsPath = $null
$env:Path = "$DotnetSdkRoot;$env:Path"
& $Dotnet build "$Root\src\DlsiteClient.App\DlsiteClient.App.fsproj" -c Release -f net11.0-windows -p:PublishAot=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: build Release"
