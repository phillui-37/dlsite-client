#Requires -Version 5.1
<#
.SYNOPSIS
  Native AOT publish for win-x64 (needs VS C++ / vcvars64).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root "artifacts\aot-win-x64"
$Dotnet = "$env:USERPROFILE\scoop\apps\dotnet-sdk\current\dotnet.exe"
if (-not (Test-Path $Dotnet)) { $Dotnet = "dotnet" }

$vcvarsCandidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat"
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
)
$vcvars = $vcvarsCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $vcvars) {
    Write-Error "vcvars64.bat not found. Install VS C++ Desktop workload."
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$proj = Join-Path $Root "src\DlsiteClient.App\DlsiteClient.App.fsproj"
$cmd = "`"$vcvars`" && `"$Dotnet`" publish `"$proj`" -c Release -f net10.0-windows -r win-x64 -p:PublishAot=true --self-contained true -o `"$Out`""
Write-Host "Running: $cmd"
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: $Out\DlsiteClient.App.exe"
