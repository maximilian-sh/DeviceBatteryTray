# DeviceBatteryTray Installer Script
# Usage: 
#   - Double-click install.bat (recommended - no execution policy issues)
#   - Or run: powershell -ExecutionPolicy Bypass -File install.ps1
#   - For Program Files: Run install.bat as Administrator

param(
    [switch]$ProgramFiles,
    [switch]$CurrentUser,
    [switch]$Help
)

# Keep window open on errors
$ErrorActionPreference = "Stop"
trap {
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

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

# Find the ZIP file or extracted files in the same directory as this script
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$zipFile = Get-ChildItem -Path $scriptDir -Filter "DeviceBatteryTray-v*-win-x64.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1

# Check if required files are already extracted in the script directory
$requiredFiles = @("DeviceBatteryTray.exe", "LGSTrayHID.exe", "hidapi.dll")
$allFilesPresent = $true
foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Join-Path $scriptDir $file))) {
        $allFilesPresent = $false
        break
    }
}

if (-not $zipFile -and -not $allFilesPresent) {
    Write-Host "Error: Could not find DeviceBatteryTray ZIP file or extracted files." -ForegroundColor Red
    Write-Host ""
    Write-Host "Please do ONE of the following:" -ForegroundColor Yellow
    Write-Host "  1. Ensure the ZIP file is in the same folder as install.bat" -ForegroundColor Cyan
    Write-Host "  2. OR extract the ZIP file before running install.bat" -ForegroundColor Cyan
    Write-Host ""
    exit 1
}

if ($zipFile) {
    Write-Host "Found ZIP: $($zipFile.Name)" -ForegroundColor Green
} else {
    Write-Host "Found extracted files in current directory" -ForegroundColor Green
}

# Clean up old installation directory and any ClickOnce-style folders
if (Test-Path $installDir) {
    Write-Host "Installation directory exists. Removing old installation..." -ForegroundColor Yellow
    Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Also clean up any old ClickOnce-style folders in the parent directory
$parentDir = Split-Path -Parent $installDir
if ($parentDir -and (Test-Path $parentDir)) {
    Get-ChildItem -Path $parentDir -Directory -Filter "DeviceBatteryTray_*" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Removing old folder: $($_.Name)" -ForegroundColor Yellow
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Write-Host "Created installation directory: $installDir" -ForegroundColor Green

# Copy files from ZIP or extracted directory
if ($zipFile) {
    # Extract ZIP
    Write-Host "Extracting files from ZIP..." -ForegroundColor Cyan
    Expand-Archive -Path $zipFile.FullName -DestinationPath $installDir -Force
} else {
    # Copy already-extracted files
    Write-Host "Copying files from current directory..." -ForegroundColor Cyan
    $filesToCopy = @("DeviceBatteryTray.exe", "LGSTrayHID.exe", "hidapi.dll", "appsettings.toml", "install.ps1", "install.bat", "uninstall.ps1", "uninstall.bat", "INSTALL.txt")
    foreach ($file in $filesToCopy) {
        $sourcePath = Join-Path $scriptDir $file
        if (Test-Path $sourcePath) {
            Copy-Item -Path $sourcePath -Destination $installDir -Force
        }
    }
}

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

# Create Start Menu shortcut (proper structure for Windows Search)
$startMenu = [Environment]::GetFolderPath("Programs")
$startMenuDir = Join-Path $startMenu "DeviceBatteryTray"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null

$startMenuShortcut = Join-Path $startMenuDir "DeviceBatteryTray.lnk"
$StartMenuShortcut = $WshShell.CreateShortcut($startMenuShortcut)
$StartMenuShortcut.TargetPath = $exePath
$StartMenuShortcut.WorkingDirectory = $installDir
$StartMenuShortcut.IconLocation = $exePath
$StartMenuShortcut.Description = "Monitor battery levels for Logitech and HyperX wireless devices"
$StartMenuShortcut.Save()

# Also create a shortcut directly in Programs (for better Windows Search visibility)
$directShortcut = Join-Path $startMenu "DeviceBatteryTray.lnk"
$DirectShortcut = $WshShell.CreateShortcut($directShortcut)
$DirectShortcut.TargetPath = $exePath
$DirectShortcut.WorkingDirectory = $installDir
$DirectShortcut.IconLocation = $exePath
$DirectShortcut.Description = "Monitor battery levels for Logitech and HyperX wireless devices"
$DirectShortcut.Save()

Write-Host "Created Start Menu shortcuts" -ForegroundColor Green
Write-Host ""

# Ask about auto-start
Write-Host ""
Write-Host "Would you like DeviceBatteryTray to start automatically with Windows? (Y/N)" -ForegroundColor Cyan
$autoStartResponse = Read-Host
if ($autoStartResponse -eq "Y" -or $autoStartResponse -eq "y") {
    $regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $regPath -Name "DeviceBatteryTray" -Value "`"$exePath`"" -Type String
    Write-Host "Auto-start enabled" -ForegroundColor Green
}

# Ask if user wants to start the app now
Write-Host ""
Write-Host "Would you like to start DeviceBatteryTray now? (Y/N)" -ForegroundColor Cyan
$startNowResponse = Read-Host
if ($startNowResponse -eq "Y" -or $startNowResponse -eq "y") {
    Write-Host "Starting DeviceBatteryTray..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath -WorkingDirectory $installDir
    Write-Host "DeviceBatteryTray started!" -ForegroundColor Green
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
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

