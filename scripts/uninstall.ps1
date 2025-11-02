# DeviceBatteryTray Uninstaller Script
# Usage: Right-click uninstall.ps1 -> Run with PowerShell

param(
    [switch]$Help
)

$ErrorActionPreference = "Stop"

if ($Help) {
    Write-Host "DeviceBatteryTray Uninstaller"
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  .\uninstall.ps1    # Remove DeviceBatteryTray installation"
    Write-Host ""
    Write-Host "This will:"
    Write-Host "  - Remove auto-start registry entry"
    Write-Host "  - Remove desktop shortcut"
    Write-Host "  - Remove Start Menu shortcut"
    Write-Host "  - Remove all installation files"
    exit 0
}

Write-Host "DeviceBatteryTray Uninstaller" -ForegroundColor Cyan
Write-Host ""

# Find installation directory from registry or common locations
$installDir = $null
$exePath = $null

# Check registry first (auto-start location)
$regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$regValue = Get-ItemProperty -Path $regPath -Name "DeviceBatteryTray" -ErrorAction SilentlyContinue
if ($regValue) {
    $exePath = $regValue.DeviceBatteryTray.Trim('"')
    if (Test-Path $exePath) {
        $installDir = Split-Path -Parent $exePath
    }
}

# If not found in registry, check common locations
if (-not $installDir) {
    $commonPaths = @(
        "$env:LOCALAPPDATA\DeviceBatteryTray",
        "C:\Program Files\DeviceBatteryTray",
        "C:\Program Files (x86)\DeviceBatteryTray"
    )
    
    foreach ($path in $commonPaths) {
        $testExe = Join-Path $path "DeviceBatteryTray.exe"
        if (Test-Path $testExe) {
            $installDir = $path
            $exePath = $testExe
            break
        }
    }
}

if (-not $installDir -or -not (Test-Path $exePath)) {
    Write-Host "DeviceBatteryTray installation not found." -ForegroundColor Yellow
    Write-Host "If installed in a custom location, please run this script from the installation directory." -ForegroundColor Yellow
    exit 1
}

Write-Host "Found installation at: $installDir" -ForegroundColor Green
Write-Host ""

# Ask for confirmation
Write-Host "This will remove DeviceBatteryTray and all its files." -ForegroundColor Yellow
Write-Host "Are you sure you want to continue? (Y/N)" -ForegroundColor Cyan
$response = Read-Host
if ($response -ne "Y" -and $response -ne "y") {
    Write-Host "Uninstall cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Uninstalling..." -ForegroundColor Cyan

# Stop running processes
Write-Host "Stopping processes..." -ForegroundColor Cyan
$procNames = @('DeviceBatteryTray', 'LGSTrayHID')
foreach ($name in $procNames) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            Write-Host "  Stopped $name" -ForegroundColor Green
        } catch {
            Write-Host "  Could not stop $name (may already be stopped)" -ForegroundColor Yellow
        }
    }
}

Start-Sleep -Seconds 1

# Remove auto-start registry entry
Write-Host "Removing auto-start entry..." -ForegroundColor Cyan
try {
    Remove-ItemProperty -Path $regPath -Name "DeviceBatteryTray" -ErrorAction SilentlyContinue
    Write-Host "  Removed auto-start registry entry" -ForegroundColor Green
} catch {
    Write-Host "  Auto-start entry not found or already removed" -ForegroundColor Yellow
}

# Remove desktop shortcut
Write-Host "Removing shortcuts..." -ForegroundColor Cyan
$desktop = [Environment]::GetFolderPath("Desktop")
$desktopShortcut = Join-Path $desktop "DeviceBatteryTray.lnk"
if (Test-Path $desktopShortcut) {
    Remove-Item -Path $desktopShortcut -Force -ErrorAction SilentlyContinue
    Write-Host "  Removed desktop shortcut" -ForegroundColor Green
}

# Remove Start Menu shortcut
$startMenu = [Environment]::GetFolderPath("Programs")
$startMenuDir = Join-Path $startMenu "DeviceBatteryTray"
if (Test-Path $startMenuDir) {
    Remove-Item -Path $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Removed Start Menu shortcut" -ForegroundColor Green
}

# Remove installation directory
Write-Host "Removing installation files..." -ForegroundColor Cyan
if (Test-Path $installDir) {
    try {
        Remove-Item -Path $installDir -Recurse -Force -ErrorAction Stop
        Write-Host "  Removed installation directory" -ForegroundColor Green
    } catch {
        Write-Host "  Error removing installation directory: $_" -ForegroundColor Red
        Write-Host "  Some files may be locked. Please close any running instances and try again." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ""
Write-Host "DeviceBatteryTray has been successfully uninstalled." -ForegroundColor Green
Write-Host "Installation directory removed: $installDir" -ForegroundColor Cyan
