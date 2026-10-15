#Requires -Version 5.1
<#
.SYNOPSIS
  Build Windows MSI installer (WiX) over AOT publish output.
  Wizard asks per-user (no UAC) or all users (UAC).
#>
$ErrorActionPreference = "Stop"

function Set-DualScopeLaunch([string]$Path) {
    $installer = $null
    $db = $null
    $sum = $null
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $db = $installer.OpenDatabase($Path, 1)
        $view = $db.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='SecureCustomProperties'")
        $view.Execute()
        $rec = $view.Fetch()
        $secure = ""
        if ($rec) { $secure = $rec.StringData(1) }
        $view.Close()
        foreach ($name in @("ALLUSERS", "MSIINSTALLPERUSER", "SCOPECHOSEN")) {
            if ($secure -notmatch "(^|;)$([regex]::Escape($name))(;|$)") {
                if ([string]::IsNullOrEmpty($secure)) { $secure = $name } else { $secure = "$secure;$name" }
            }
        }
        $upd = $db.OpenView("UPDATE ``Property`` SET ``Value``=? WHERE ``Property``='SecureCustomProperties'")
        $params = $installer.CreateRecord(1)
        $params.StringData(1) = $secure
        $upd.Execute($params)
        $upd.Close()

        $sum = $db.SummaryInformation(1)
        $word = [int]$sum.Property(15)
        $newWord = [int]($word -bor 8)
        [void]$sum.GetType().InvokeMember("Property", "SetProperty", $null, $sum, @([int]15, $newWord))
        $sum.Persist()
        $db.Commit()
    } finally {
        if ($sum) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sum) }
        if ($db) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) }
        if ($installer) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) }
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }
}

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

# x64 dual-scope. -acceptEula wix7 is the WiX v7 OSMF EULA id.
# WiX leaves Word Count bit 3 clear, so Explorer would request UAC before the scope dialog.
# Set bit 3 after the build. The all-users button then requests UAC on its own.
& wix build $Wxs -arch x64 -b "app=$AotDir" -acceptEula wix7 -o $msi
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Set-DualScopeLaunch $msi
Write-Host "OK: $msi"
Write-Host "Install: msiexec /i `"$msi`""
