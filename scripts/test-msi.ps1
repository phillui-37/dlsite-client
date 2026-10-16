#Requires -Version 5.1
# Tests only the newly built package in artifacts/msi-test-install. Needs elevation for machine scope.
param([switch]$SkipIfInstalled)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $root "artifacts\msi\DlsiteClient-0.1.2-win-x64.msi"
$target = Join-Path $root "artifacts\msi-test-install"
$logs = Join-Path $root "artifacts\msi-test-logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($package, 0)
$view = $db.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductCode'")
$view.Execute(); $product = $view.Fetch().StringData(1); $view.Close()
$view = $db.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='UpgradeCode'")
$view.Execute(); $upgrade = $view.Fetch().StringData(1); $view.Close()
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)
# ProductCode changes between builds; the family check also protects older installs.
if (@($installer.RelatedProducts($upgrade)).Count -gt 0) {
    if ($SkipIfInstalled) {
        Write-Output '::notice::SKIP MSI install/repair/removal: a related DLsite Client product is installed. Use a clean account or VM for maintenance checks.'
        return
    }
    throw 'A related DLsite Client product is installed. Run maintenance checks in a clean account or VM.'
}
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class DlsiteMsiContext {
 [DllImport("msi.dll", CharSet=CharSet.Unicode)] public static extern uint MsiGetProductInfoEx(string p, string sid, uint context, string property, StringBuilder value, ref uint length);
 public static uint Query(string p, uint context) { var b=new StringBuilder(512); uint n=512; return MsiGetProductInfoEx(p,null,context,"InstalledProductName",b,ref n); }
}
"@
function Run-Msi([string]$arguments, [string]$name) {
    $log = Join-Path $logs ($name + ".log")
    $process = Start-Process msiexec.exe -ArgumentList "$arguments /qn /norestart /l*v `"$log`"" -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin @(0,3010)) { throw "$name failed: $($process.ExitCode); $log" }
    Write-Output "PASS $name"
}
# Refuse to replace a real installation of this version.
if ([DlsiteMsiContext]::Query($product,2) -eq 0 -or [DlsiteMsiContext]::Query($product,4) -eq 0) {
    throw "This package is already installed. Remove the test install before running this script."
}
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
foreach ($scope in @("user", "machine")) {
    if ($scope -eq "machine" -and -not $isAdmin) {
        $process = Start-Process msiexec.exe -ArgumentList "/i `"$package`" /qn /norestart ALLUSERS=1 MSIINSTALLPERUSER=`"`" INSTALLFOLDER=`"$target`" /l*v `"$logs\machine-no-admin.log`"" -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 1603 -or -not (Select-String -LiteralPath "$logs\machine-no-admin.log" -Pattern "Error 1925" -Quiet)) { throw "Expected no-admin rejection" }
        Write-Output "PASS machine scope refuses silent install without administrator rights"
        Write-Output "SKIP elevated machine install/repair/removal: run this script from an elevated PowerShell to test it"
        continue
    }
    $context = if ($scope -eq "user") { 2 } else { 4 }
    $properties = if ($scope -eq "user") { "ALLUSERS=2 MSIINSTALLPERUSER=1" } else { 'ALLUSERS=1 MSIINSTALLPERUSER=""' }
    Run-Msi "/i `"$package`" $properties INSTALLFOLDER=`"$target`"" "$scope-install"
    try {
        if ([DlsiteMsiContext]::Query($product,$context) -ne 0) { throw "Wrong installer scope: $scope" }
        Write-Output "PASS $scope native install context"
        foreach ($relative in @("DlsiteClient.App.exe","locales\ja.json","locales\zh-TW.json","tools\7z.exe","tools\7z.dll","tools\License.txt")) {
            if (-not (Test-Path -LiteralPath (Join-Path $target $relative))) { throw "Missing payload: $relative" }
        }
        Write-Output "PASS $scope payload"
        $save = Join-Path $target "test-user-data\save.txt"
        New-Item -ItemType Directory -Force -Path (Split-Path $save) | Out-Null
        Set-Content -LiteralPath $save -Value "keep-test-save"
        # The file belongs to this disposable install, within the verified test root.
        Remove-Item -LiteralPath (Join-Path $target "DlsiteClient.App.exe")
        Run-Msi "/fa $product" "$scope-repair"
        if (-not (Test-Path -LiteralPath (Join-Path $target "DlsiteClient.App.exe"))) { throw "Repair did not restore executable" }
    } finally {
        Run-Msi "/x $product" "$scope-uninstall"
    }
    if ((Get-Content -LiteralPath $save -Raw).Trim() -ne "keep-test-save") { throw "Uninstall removed user data" }
    if (Test-Path -LiteralPath (Join-Path $target "DlsiteClient.App.exe")) { throw "Uninstall left executable" }
    Write-Output "PASS $scope removal preserves user data"
}
