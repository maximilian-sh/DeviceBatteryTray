# DeviceBatteryTray Installer Script
# Usage: Right-click install.ps1 -> Run with PowerShell (as Administrator for Program Files install)

param(
    [switch]$ProgramFiles,
    [switch]$CurrentUser,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

if ($Help) {
    Write-Host "DeviceBatteryTray Installer"
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  .\install.ps1 -ProgramFiles    # Install to C:\Program Files\DeviceBatteryTray (requires admin)"
    Write-Host "  .\install.ps1 -CurrentUser     # Install to %LOCALAPPDATA%\DeviceBatteryTray (default)"
    Write-Host ""
    Write-Host "Or right-click this file and select 'Run with PowerShell'"
    exit 0
}

# Determine installation directory
$installDir = $null

if ($ProgramFiles) {
    $installDir = "C:\Program Files\DeviceBatteryTray"
    # Check if running as admin
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "Program Files installation requires administrator privileges." -ForegroundColor Red
        Write-Host "Please right-click PowerShell and select 'Run as Administrator', then run this script again." -ForegroundColor Yellow
        exit 1
    }
} elseif ($CurrentUser) {
    $installDir = Join-Path $env:LOCALAPPDATA "DeviceBatteryTray"
} else {
    # Default: Current User location
    $installDir = Join-Path $env:LOCALAPPDATA "DeviceBatteryTray"
}

Write-Host "Installing DeviceBatteryTray to: $installDir" -ForegroundColor Cyan

# Find the ZIP file in the same directory as this script
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$zipFile = Get-ChildItem -Path $scriptDir -Filter "DeviceBatteryTray-v*-win-x64.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $zipFile) {
    Write-Host "Error: Could not find DeviceBatteryTray ZIP file in the script directory." -ForegroundColor Red
    Write-Host "Please ensure the ZIP file is in the same folder as install.ps1" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found ZIP: $($zipFile.Name)" -ForegroundColor Green

# Create installation directory
if (Test-Path $installDir) {
    Write-Host "Installation directory exists. Removing old installation..." -ForegroundColor Yellow
    Remove-Item -Path $installDir -Recurse -Force
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Write-Host "Created installation directory: $installDir" -ForegroundColor Green

# Extract ZIP
Write-Host "Extracting files..." -ForegroundColor Cyan
Expand-Archive -Path $zipFile.FullName -DestinationPath $installDir -Force

# Unblock all files (remove Zone.Identifier)
Write-Host "Unblocking files..." -ForegroundColor Cyan
Get-ChildItem -Path $installDir -Recurse -File | ForEach-Object {
    Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
}

# Verify critical files
$exePath = Join-Path $installDir "DeviceBatteryTray.exe"
$hidExePath = Join-Path $installDir "LGSTrayHID.exe"
$dllPath = Join-Path $installDir "hidapi.dll"

if (-not (Test-Path $exePath)) {
    Write-Host "Error: DeviceBatteryTray.exe not found after extraction!" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $hidExePath)) {
    Write-Host "Error: LGSTrayHID.exe not found after extraction!" -ForegroundColor Red
    exit 1
}

Write-Host "Installation completed successfully!" -ForegroundColor Green
Write-Host ""

# Create desktop shortcut
$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "DeviceBatteryTray.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.WorkingDirectory = $installDir
$Shortcut.IconLocation = $exePath
$Shortcut.Description = "Device Battery Tray - Monitor device battery levels"
$Shortcut.Save()

Write-Host "Created desktop shortcut" -ForegroundColor Green

# Create Start Menu shortcut
$startMenu = [Environment]::GetFolderPath("Programs")
$startMenuDir = Join-Path $startMenu "DeviceBatteryTray"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null

$startMenuShortcut = Join-Path $startMenuDir "DeviceBatteryTray.lnk"
$StartMenuShortcut = $WshShell.CreateShortcut($startMenuShortcut)
$StartMenuShortcut.TargetPath = $exePath
$StartMenuShortcut.WorkingDirectory = $installDir
$StartMenuShortcut.IconLocation = $exePath
$StartMenuShortcut.Description = "Device Battery Tray - Monitor device battery levels"
$StartMenuShortcut.Save()

Write-Host "Created Start Menu shortcut" -ForegroundColor Green
Write-Host ""

# Ask about auto-start
Write-Host "Would you like DeviceBatteryTray to start automatically with Windows? (Y/N)" -ForegroundColor Cyan
$response = Read-Host
if ($response -eq "Y" -or $response -eq "y") {
    $regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $regPath -Name "DeviceBatteryTray" -Value "`"$exePath`"" -Type String
    Write-Host "Auto-start enabled" -ForegroundColor Green
}

# Copy uninstaller to installation directory
$uninstallerSource = Join-Path $scriptDir "uninstall.ps1"
if (Test-Path $uninstallerSource) {
    Copy-Item -Path $uninstallerSource -Destination $installDir -Force
    Write-Host "Uninstaller copied to installation directory" -ForegroundColor Green
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "Location: $installDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can now:"
Write-Host "  - Run DeviceBatteryTray from the desktop shortcut"
Write-Host "  - Or run: $exePath" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can safely delete:" -ForegroundColor Yellow
Write-Host "  - The ZIP file (DeviceBatteryTray-v*-win-x64.zip)"
Write-Host "  - The extracted folder in Downloads" -ForegroundColor Cyan

