@echo off
REM DeviceBatteryTray Installer - Double-click to run
REM This wrapper bypasses PowerShell execution policy restrictions

setlocal

REM Find the PowerShell script in the same directory
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%install.ps1"

REM Check if PowerShell script exists
if not exist "%PS_SCRIPT%" (
    echo Error: install.ps1 not found in the same directory as install.bat
    echo.
    pause
    exit /b 1
)

REM Run PowerShell with execution policy bypass
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%"

REM Keep window open if there was an error
if errorlevel 1 (
    echo.
    echo Installation failed. Press any key to exit...
    pause >nul
)

