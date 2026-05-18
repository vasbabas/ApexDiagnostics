<#
.SYNOPSIS
Builds a bootable WinPE ISO for Apex Diagnostics featuring the WPF Desktop Shell.

.DESCRIPTION
Requires the Windows Assessment and Deployment Kit (ADK) and Windows PE add-on.
Includes required WinPE optional components (.NET, WMI).
#>

param (
    [string]$AdkPath = "C:\Users\User\Desktop\Masaüstü\Assessment and Deployment Kit",
    [string]$Architecture = "amd64",
    [string]$WorkingDir = "C:\WinPE_ApexDesktop",
    [string]$IsoPath = "C:\WinPE_ApexDesktop\ApexDiagnosticSuite.iso",
    [string]$DiagnosticsSourcePath = "C:\Users\User\Desktop\Apex\ApexDiagnostics\bin\Release\net8.0-windows\win-x64\publish",
    [string]$ShellSourcePath = "C:\Users\User\Desktop\Apex\ApexShell\bin\Release\net8.0-windows\win-x64\publish"
)

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Yonetici izinleri gerekiyor. Script yeniden baslatiliyor..."
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Write-Host "Verifying ADK Installation..." -ForegroundColor Cyan
$CopyPEDir = "$AdkPath\Windows Preinstallation Environment"
$MakeWinPEMedia = "$CopyPEDir\MakeWinPEMedia.cmd"
$CopyPE = "$CopyPEDir\copype.cmd"
$DandISetEnv = "$AdkPath\Deployment Tools\DandISetEnv.bat"

if (-not (Test-Path $CopyPE)) {
    Write-Error "Windows ADK veya Windows PE eklentisi bulunamadi: '$AdkPath'."
    Read-Host "Kapatmak icin Enter'a basin"
    exit
}

if (-not (Test-Path $DiagnosticsSourcePath)) {
    Write-Error "Uygulama yayini '$DiagnosticsSourcePath' bulunamadi. Lutfen 'dotnet publish' calistirin."
    exit
}

if (Test-Path $WorkingDir) {
    Write-Host "Cleaning working directory..." -ForegroundColor Yellow
    Remove-Item -Path $WorkingDir -Recurse -Force
}

Write-Host "Creating base WinPE environment..." -ForegroundColor Cyan
cmd.exe /c "`"$DandISetEnv`" && `"$CopyPE`" $Architecture `"$WorkingDir`""

$MountDir = "$WorkingDir\mount"
Write-Host "Mounting boot image..." -ForegroundColor Cyan
DISM /Mount-Image /ImageFile:"$WorkingDir\media\sources\boot.wim" /index:1 /MountDir:"$MountDir"

Write-Host "Adding Optional Components (.NET and WMI)..." -ForegroundColor Cyan
$OCDir = "$CopyPEDir\$Architecture\WinPE_OCs"

# Need WMI for hardware telemetry and NetFx for the .NET runtime (if we were using framework, but we use self-contained .NET 8 so NetFx might be optional, 
# but it's safer for WMI dependencies).
DISM /Image:"$MountDir" /Add-Package /PackagePath:"$OCDir\WinPE-WMI.cab"
DISM /Image:"$MountDir" /Add-Package /PackagePath:"$OCDir\tr-tr\WinPE-WMI_tr-tr.cab"

Write-Host "Injecting Apex Desktop Environment..." -ForegroundColor Cyan
$TargetAppDir = "$MountDir\ApexSuite"
New-Item -ItemType Directory -Path $TargetAppDir -Force | Out-Null

Copy-Item -Path "$DiagnosticsSourcePath\*" -Destination $TargetAppDir -Recurse -Force
Copy-Item -Path "$ShellSourcePath\*" -Destination $TargetAppDir -Recurse -Force

Write-Host "Configuring auto-start for ApexShell..." -ForegroundColor Cyan
$StartnetPath = "$MountDir\Windows\System32\startnet.cmd"
$StartnetContent = @"
wpeinit
@echo off
color 0f
cls
echo Starting Apex WinPE Desktop Environment...
cd /d X:\ApexSuite
ApexShell.exe
"@
Set-Content -Path $StartnetPath -Value $StartnetContent -Encoding ASCII

Write-Host "Unmounting and saving changes..." -ForegroundColor Cyan
DISM /Unmount-Image /MountDir:"$MountDir" /Commit

Write-Host "Generating bootable ISO..." -ForegroundColor Cyan
cmd.exe /c "`"$DandISetEnv`" && `"$MakeWinPEMedia`" /ISO `"$WorkingDir`" `"$IsoPath`""

Write-Host "Basarili! WinPE ISO olusturuldu: $IsoPath" -ForegroundColor Green
Read-Host "Islem tamamlandi. Kapatmak icin Enter'a basin"
