# DeviceBatteryTray

Minimal Windows tray app that shows battery for supported USB HID devices using native HID only. No Logitech G Hub or other vendor apps required.

## How to install

Please visit the latest release page and download the release zip. Standalone builds include .NET 8; non-standalone builds require .NET 8 installed.

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

## Known Notes

-   Some wired devices echo reads across multiple interfaces.
-   Device percentages can differ from vendor apps that use device-specific lookup tables.

## Build

-   Install .NET 8 SDK
-   Windows only (WPF)
-   Debug build

```
dotnet build -c Debug LGSTrayUI/LGSTrayUI.csproj
```

-   Release, standalone

```
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true LGSTrayUI/LGSTrayUI.csproj
```

## Acknowledgements

-   hidapi
-   Community reverse engineering of Logitech HID++ and HyperX reports
-   Project inspiration and references:
    -   [LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery)
    -   [HyperX-Cloud-2-Battery-Monitor](https://github.com/auto94/HyperX-Cloud-2-Battery-Monitor)
