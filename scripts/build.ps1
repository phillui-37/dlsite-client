#Requires -Version 5.1
<#
.SYNOPSIS
  Build DLsite Client (Release).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Dotnet = "$env:USERPROFILE\scoop\apps\dotnet-sdk\current\dotnet.exe"
if (-not (Test-Path $Dotnet)) { $Dotnet = "dotnet" }

$env:Path = "$(Split-Path $Dotnet -Parent);$env:Path"
Set-Location $Root

& $Dotnet build "$Root\src\DlsiteClient.App\DlsiteClient.App.fsproj" -c Release -f net10.0-windows
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: build Release"
