#Requires -Version 5.1
<#
.SYNOPSIS
  Native AOT publish for win-x64 (needs VS C++ / vcvars64).
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root "artifacts\aot-win-x64"

Push-Location $Root
try {
    $Dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    $SdkVersion = if ($Dotnet) { & $Dotnet --version 2>$null }
    if (-not $SdkVersion -or $LASTEXITCODE -ne 0) {
        $Dotnet = "$env:USERPROFILE\scoop\apps\dotnet-sdk\current\dotnet.exe"
        if (-not (Test-Path $Dotnet)) { throw "Install the .NET SDK required by global.json and add dotnet to PATH." }
        $SdkVersion = & $Dotnet --version
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # The MSBuild task host must resolve through the same SDK and runtime root as
    # the selected dotnet executable. Inherited machine paths may point elsewhere.
    $env:MSBuildSDKsPath = $null
    $DotnetSdkRoot = Split-Path -Parent $Dotnet
    if (Test-Path (Join-Path $DotnetSdkRoot "sdk")) {
        $SdkResolverPath = Join-Path $DotnetSdkRoot "sdk\$SdkVersion\Sdks"
        if (-not (Test-Path $SdkResolverPath)) {
            throw "MSBuild SDK resolver directory not found: $SdkResolverPath"
        }

        $env:DOTNET_ROOT = $DotnetSdkRoot
        $env:DOTNET_ROOT_X64 = $DotnetSdkRoot
        $env:MSBuildSDKsPath = $SdkResolverPath
        $env:Path = "$DotnetSdkRoot;$env:Path"
    }

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
    $cmd = "`"$vcvars`" && `"$Dotnet`" publish `"$proj`" -c Release -f net11.0-windows -r win-x64 -p:PublishAot=true --self-contained true -o `"$Out`""
    Write-Host "Running: $cmd"
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "OK: $Out\DlsiteClient.App.exe"
} finally {
    Pop-Location
}
