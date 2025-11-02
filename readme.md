# DeviceBatteryTray

Minimal Windows tray app that shows battery for supported USB HID devices using native HID only. No Logitech G Hub or other vendor apps required.

## Install

### Option 1: Automated Installer (Recommended)

1) Download the latest `DeviceBatteryTray-vX.Y.Z-win-x64.zip` from Releases.

2) Extract the ZIP and run `install.ps1`:
   - **For current user install:** Right-click `install.ps1` → Run with PowerShell
     - Installs to `%LOCALAPPDATA%\DeviceBatteryTray\`
   - **For system-wide install:** Right-click PowerShell → Run as Administrator, then:
     ```powershell
     cd "C:\path\to\extracted\folder"
     .\install.ps1 -ProgramFiles
     ```
     - Installs to `C:\Program Files\DeviceBatteryTray\`

The installer will:
- Extract files to the proper location
- Unblock both executables (no manual SmartScreen steps needed)
- Create desktop and Start Menu shortcuts
- Optionally enable auto-start with Windows

### Option 2: Manual Install

1) Download the latest `DeviceBatteryTray-vX.Y.Z-win-x64.zip` from Releases.

2) Extract the ZIP to a permanent location:
   - `%LOCALAPPDATA%\DeviceBatteryTray\` (recommended for per-user)
   - `C:\Program Files\DeviceBatteryTray\` (requires admin)

3) **First-time setup (Windows SmartScreen):**
   - Right-click `DeviceBatteryTray.exe` → Properties → Unblock (if shown)
   - Right-click `LGSTrayHID.exe` → Properties → Unblock (if shown)

4) Run `DeviceBatteryTray.exe`

**Notes:**
- The folder must contain these files: `DeviceBatteryTray.exe`, `LGSTrayHID.exe`, `hidapi.dll`, `appsettings.toml`
- The shipped build is self-contained (.NET 8 not required)
- The automated installer handles everything automatically

## Devices

-   Logitech HID++ receivers/devices (native HID)
-   HyperX Wireless headsets (Cloud II, Cloud II Core, Cloud Alpha, Cloud Stinger 2) — via native HID reports (based on public reverse engineering)

## Features

-   Tray icon shows device battery as an icon or number
-   Tooltip: "Device — XX%" and "Updated: Xm ago" (only shown if >10 minutes)
-   Multiple devices; headset/mouse/keyboard icons
-   Theme-aware (light/dark)
-   Fast polling when a device is off/unavailable; slower when on

## What this app does NOT do

-   No G Hub/WebSocket integration
-   No HTTP server

## How it works (short)

-   Uses hidapi to hot-detect devices and read/write HID reports.
-   Logitech devices use HID++ short/long report channels.
-   HyperX headsets use model-specific report sequences to obtain battery percentage.

## appsettings.toml

Refer to https://toml.io/en/ for TOML syntax. If settings are invalid, the app will prompt to reset defaults.

### `[Native]` settings

-   `retryTime` - seconds between polls when device is off/unavailable (default 5)
-   `pollPeriod` - seconds between polls when device is on (default 120)
-   `disabledDevices` - array of substrings to ignore device names

Example:

```
[Native]
enabled = true
retryTime = 5
pollPeriod = 120

disabledDevices = [
]
```

## Auto-Start with Windows

The app can be configured to start automatically when Windows starts. Use the tray menu option "Autostart with Windows" to enable/disable it.

**Registry Location (for manual inspection/cleanup):**
- Registry key: `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- Value name: `DeviceBatteryTray`
- Value data: Full path to `DeviceBatteryTray.exe`

To manually view or edit:
1. Press `Win + R`, type `regedit`, press Enter
2. Navigate to: `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
3. Look for the `DeviceBatteryTray` entry

**Note:** The app automatically validates and cleans up invalid registry entries (from old versions or moved installations) on startup. If you moved the app to a different location, the old registry entry will be automatically removed.

## Known Notes

-   Some wired devices echo reads across multiple interfaces.
-   Device percentages can differ from vendor apps that use device-specific lookup tables.

## Build

- Install .NET 8 SDK (Windows)
- Debug build

```
dotnet build -c Debug LGSTrayUI/LGSTrayUI.csproj
```

- Release (uses the script and publish profile)

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\release.ps1 -Clean -Configuration Release
```

Artifacts:
- Output: `LGSTrayUI\bin\Release\net8.0-windows\win-x64\standalone` (contains the EXE, `LGSTrayHID.exe`, `hidapi.dll`, `appsettings.toml`).

## Releasing

**The release workflow is fully automatic.** Just follow these steps:

1. Update the version in `LGSTrayUI/LGSTrayUI.csproj`:
   ```xml
   <VersionPrefix>4.0.4</VersionPrefix>  <!-- Bump from current version -->
   ```

2. Stage, commit, and push:
   ```bash
   git add LGSTrayUI/LGSTrayUI.csproj
   git commit -m "Bump version to 4.0.4"
   git push origin master
   ```

3. The GitHub Actions workflow will automatically:
   - Detect the version change
   - Compare it to the latest git tag
   - If the new version is higher → build, create tag, and publish release
   - If unchanged or lower → skip (no build/release)

**Important:**
- Do NOT create git tags manually. The workflow creates tags automatically.
- Do NOT push version bumps without changes, or the workflow will skip.
- The workflow runs on every push to master, but only releases when version is bumped.

## Acknowledgements

-   hidapi
-   Community reverse engineering of Logitech HID++ and HyperX reports
-   Project inspiration and references:
    -   [LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery)
    -   [HyperX-Cloud-2-Battery-Monitor](https://github.com/auto94/HyperX-Cloud-2-Battery-Monitor)
