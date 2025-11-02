@echo off
REM DeviceBatteryTray Uninstaller - Double-click to run
REM This wrapper bypasses PowerShell execution policy restrictions

setlocal

REM Find the PowerShell script in the same directory as this batch file
REM Or use the installation directory if running from there
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%uninstall.ps1"

REM If uninstall.ps1 is not in the same directory, check common installation locations
if not exist "%PS_SCRIPT%" (
    set "PS_SCRIPT=%LOCALAPPDATA%\DeviceBatteryTray\uninstall.ps1"
    if not exist "%PS_SCRIPT%" (
        set "PS_SCRIPT=C:\Program Files\DeviceBatteryTray\uninstall.ps1"
        if not exist "%PS_SCRIPT%" (
            echo Error: uninstall.ps1 not found.
            echo Please run this from the DeviceBatteryTray installation directory.
            echo.
            pause
            exit /b 1
        )
    )
)

REM Run PowerShell with execution policy bypass
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%"

REM Keep window open if there was an error
if errorlevel 1 (
    echo.
    echo Uninstallation may have failed. Press any key to exit...
    pause >nul
)

