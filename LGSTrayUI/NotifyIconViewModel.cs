using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LGSTrayCore;
using LGSTrayCore.Managers;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LGSTrayUI
{
    public partial class NotifyIconViewModel : ObservableObject, IHostedService
    {
        private readonly MainTaskbarIconWrapper _mainTaskbarIconWrapper;

        [ObservableProperty]
        private ObservableCollection<LogiDeviceViewModel> _logiDevices;

        private readonly UserSettingsWrapper _userSettings;
        public bool NumericDisplay
        {
            get
            {
                return _userSettings.NumericDisplay;
            }

            set
            {
                _userSettings.NumericDisplay = value;
                OnPropertyChanged();
            }
        }

        public static string AssemblyVersion
        {
            get
            {
                return "v" + Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "Missing";
            }
        }

        private const string AutoStartRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartRegKeyValue = "DeviceBatteryTray";
        private bool? _autoStart = null;
        public bool AutoStart
        {
            get
            {
                if (_autoStart == null)
                {
                    _autoStart = IsAutoStartEnabled();
                }

                return _autoStart ?? false;
            }
            set
            {
                using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);

                if (registryKey == null)
                {
                    return;
                }

                if (value)
                {
                    registryKey.SetValue(AutoStartRegKeyValue, Environment.ProcessPath!);
                }
                else
                {
                    registryKey.DeleteValue(AutoStartRegKeyValue, false);
                }

                _autoStart = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Checks if auto-start is enabled and validates the registry entry points to the current executable.
        /// Also cleans up invalid/old registry entries.
        /// </summary>
        private bool IsAutoStartEnabled()
        {
            using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);
            if (registryKey == null)
            {
                return false;
            }

            object? regValue = registryKey.GetValue(AutoStartRegKeyValue);
            if (regValue == null)
            {
                return false;
            }

            string? registryPath = regValue.ToString();
            if (string.IsNullOrEmpty(registryPath))
            {
                return false;
            }

            // Remove surrounding quotes if present (Windows Run keys sometimes have quotes)
            registryPath = registryPath.Trim('"', '\'');
            string currentPath = Environment.ProcessPath!;

            // Normalize paths for comparison (get full paths and compare case-insensitively)
            string normalizedRegistryPath = Path.GetFullPath(registryPath);
            string normalizedCurrentPath = Path.GetFullPath(currentPath);

            // Validate that the registry entry points to the current executable
            if (!normalizedRegistryPath.Equals(normalizedCurrentPath, StringComparison.OrdinalIgnoreCase))
            {
                // Registry entry exists but points to wrong path (old version/moved app)
                // Clean it up by removing the invalid entry
                try
                {
                    registryKey.DeleteValue(AutoStartRegKeyValue, false);
                }
                catch
                {
                    // Ignore errors when cleaning up
                }
                return false;
            }

            return true;
        }

        [ObservableProperty]
        private bool _rediscoverDevicesEnabled = true;

        [ObservableProperty]
        private bool _checkingForUpdates = false;

        [ObservableProperty]
        private string _updateStatusMessage = string.Empty;

        private UpdateInfo? _availableUpdate;
        
        public bool UpdateAvailable => _availableUpdate != null && _availableUpdate.IsNewer;
        public string AvailableUpdateVersion => _availableUpdate != null ? $"Download v{_availableUpdate.Version}" : "Download Update";
        private CancellationTokenSource? _updateCheckCts;
        private Timer? _updateCheckTimer;

        public bool AutoUpdateEnabled
        {
            get => _userSettings.AutoUpdateEnabled;
            set
            {
                _userSettings.AutoUpdateEnabled = value;
                OnPropertyChanged();
                if (value)
                {
                    ScheduleUpdateCheck();
                }
                else
                {
                    _updateCheckTimer?.Dispose();
                    _updateCheckTimer = null;
                }
            }
        }

        private readonly IEnumerable<IDeviceManager> _deviceManagers;

        public NotifyIconViewModel(
            MainTaskbarIconWrapper mainTaskbarIconWrapper,
            ILogiDeviceCollection logiDeviceCollection,
            UserSettingsWrapper userSettings,
            IEnumerable<IDeviceManager> deviceManagers
        )
        {
            _mainTaskbarIconWrapper = mainTaskbarIconWrapper;
            ((ContextMenu)Application.Current.FindResource("SysTrayMenu")).DataContext = this;

            _logiDevices = (logiDeviceCollection as LogiDeviceCollection)!.Devices;
            _userSettings = userSettings;
            _deviceManagers = deviceManagers;

            // Clean up invalid auto-start entries on startup
            CleanupInvalidAutoStartEntries();

            // Warn if installed in a suboptimal location
            CheckInstallationLocation();
        }

        /// <summary>
        /// Checks if the app is installed in a recommended location and warns if not
        /// </summary>
        private void CheckInstallationLocation()
        {
            try
            {
                var currentPath = Environment.ProcessPath!;
                var currentDir = Path.GetDirectoryName(currentPath)!;
                var lowerPath = currentDir.ToLowerInvariant();

                // Check if in a bad location (only warn once per session)
                var isBadLocation = lowerPath.Contains("downloads") ||
                                   lowerPath.Contains("desktop") ||
                                   lowerPath.Contains("temp") ||
                                   lowerPath.Contains("appdata\\temp");

                if (isBadLocation && _mainTaskbarIconWrapper?.TaskbarIcon != null)
                {
                    _mainTaskbarIconWrapper.TaskbarIcon.ShowBalloonTip(
                        "Installation Location Warning",
                        "DeviceBatteryTray is installed in a temporary or non-permanent location. Consider moving it to Program Files or %LOCALAPPDATA%\\DeviceBatteryTray for better reliability.",
                        Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                }
            }
            catch
            {
                // Ignore errors - this is just a helpful warning
            }
        }

        /// <summary>
        /// Cleans up invalid auto-start registry entries from old versions or moved installations.
        /// This ensures the checkbox shows the correct state on startup.
        /// </summary>
        private void CleanupInvalidAutoStartEntries()
        {
            // Force refresh by resetting cached value
            _autoStart = null;
            // Access the getter which will validate and clean up if needed
            _ = AutoStart;
        }

        [RelayCommand]
        private static void ExitApplication()
        {
            Environment.Exit(0);
        }

        [RelayCommand]
        private void DeviceClicked(object? sender)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            LogiDevice logiDevice = (LogiDevice)menuItem.DataContext;

            if (menuItem.IsChecked)
            {
                _userSettings.AddDevice(logiDevice.DeviceId);
            }
            else
            {
                _userSettings.RemoveDevice(logiDevice.DeviceId);
            }
        }

        [RelayCommand]
        private async Task RediscoverDevices()
        {
            Console.WriteLine("Rediscover");
            RediscoverDevicesEnabled = false;

            foreach (var manager in _deviceManagers)
            {
                manager.RediscoverDevices();
            }

            await Task.Delay(10_000);

            RediscoverDevicesEnabled = true;
        }

        [RelayCommand]
        private async Task CheckForUpdates()
        {
            if (CheckingForUpdates)
            {
                return;
            }

            CheckingForUpdates = true;
            ShowBalloonTip("Checking for updates...", "Please wait while we check for the latest version.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);

            try
            {
                _updateCheckCts?.Cancel();
                _updateCheckCts?.Dispose();
                _updateCheckCts = new CancellationTokenSource();

                var updateInfo = await UpdateChecker.CheckForUpdatesAsync(_updateCheckCts.Token);
                _userSettings.LastUpdateCheck = DateTime.Now;
                
                // Ensure UI updates are on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (updateInfo == null)
                    {
                        ShowBalloonTip("Update Check Failed", "Unable to check for updates. Please check your internet connection.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                        // Clear any old update
                        _availableUpdate = null;
                        OnPropertyChanged(nameof(UpdateAvailable));
                        OnPropertyChanged(nameof(AvailableUpdateVersion));
                    }
                    else if (!updateInfo.IsNewer)
                    {
                        ShowBalloonTip("Up to Date", $"You are running the latest version ({AssemblyVersion}).", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                        // Clear any old update
                        _availableUpdate = null;
                        OnPropertyChanged(nameof(UpdateAvailable));
                        OnPropertyChanged(nameof(AvailableUpdateVersion));
                    }
                    else
                    {
                        _availableUpdate = updateInfo;
                        OnPropertyChanged(nameof(UpdateAvailable));
                        OnPropertyChanged(nameof(AvailableUpdateVersion));
                        ShowUpdateNotification(updateInfo);
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowBalloonTip("Update Check Error", $"Error checking for updates: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CheckingForUpdates = false;
                });
            }
        }

        [RelayCommand]
        private async Task DownloadAndInstallUpdate()
        {
            if (_availableUpdate == null || CheckingForUpdates)
            {
                return;
            }

            CheckingForUpdates = true;

            // Create and show progress window
            UpdateProgressWindow? progressWindow = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                progressWindow = new UpdateProgressWindow(_availableUpdate.Version)
                {
                    Owner = null // No owner to prevent closing issues
                };
                progressWindow.Show();
            });

            try
            {
                var progress = new Progress<int>(percent =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (progressWindow != null)
                        {
                            progressWindow.UpdateProgress(percent, "Downloading update...");
                            // Force the window to process messages and update
                            Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    });
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow?.SetStatus("Downloading update...");
                });

                var extractedPath = await UpdateChecker.DownloadUpdateAsync(
                    _availableUpdate.DownloadUrl,
                    progress,
                    CancellationToken.None);

                if (string.IsNullOrEmpty(extractedPath))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressWindow?.Close();
                        ShowBalloonTip("Download Failed", "Failed to download update. Please try again later.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                    });
                    CheckingForUpdates = false;
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow?.UpdateProgress(100, "Installing update...");
                    progressWindow?.SetStatus("Installing update. The application will restart shortly...");
                });

                // Small delay to show 100% and installation message
                await Task.Delay(500);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow?.Close();
                });
                
                // Clear the available update since we're installing it
                _availableUpdate = null;
                OnPropertyChanged(nameof(UpdateAvailable));
                OnPropertyChanged(nameof(AvailableUpdateVersion));
                
                InstallUpdate(extractedPath);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow?.Close();
                    ShowBalloonTip("Download Error", $"Error downloading update: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                });
                CheckingForUpdates = false;
            }
        }

        private void ShowUpdateNotification(UpdateInfo updateInfo)
        {
            var title = $"Update Available: v{updateInfo.Version}";
            var message = $"A new version ({updateInfo.Version}) is available.\nClick 'Download v{updateInfo.Version}' in the menu to install.";
            ShowBalloonTip(title, message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }

        private void ShowBalloonTip(string title, string message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon icon)
        {
            if (_mainTaskbarIconWrapper?.TaskbarIcon == null)
            {
                return;
            }

            _mainTaskbarIconWrapper.TaskbarIcon.ShowBalloonTip(title, message, icon);
        }

        private void InstallUpdate(string extractedPath)
        {
            try
            {
                var currentExePath = Environment.ProcessPath!;
                var currentDir = Path.GetDirectoryName(currentExePath)!;
                var updateExePath = Path.Combine(extractedPath, "DeviceBatteryTray.exe");

                if (!File.Exists(updateExePath))
                {
                    LogUpdateError("Update package is invalid - DeviceBatteryTray.exe not found in extracted path", extractedPath);
                    UpdateStatusMessage = "Update package is invalid.";
                    return;
                }

                // Create log file for update process
                var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
                var logFileQuoted = $"\"{logFile}\"";

                // Create update script that will replace files, unblock them, and restart the app
                var updateScript = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.bat");
                
                // Use quotes around paths in batch file to handle spaces
                var extractedPathQuoted = $"\"{extractedPath}\"";
                var currentDirQuoted = $"\"{currentDir}\"";
                var currentExePathQuoted = $"\"{currentExePath}\"";
                var deviceExePathQuoted = $"\"{Path.Combine(currentDir, "DeviceBatteryTray.exe")}\"";
                var hidExePathQuoted = $"\"{Path.Combine(currentDir, "LGSTrayHID.exe")}\"";
                
                var scriptContent = $@"@echo off
setlocal enabledelayedexpansion
set LOGFILE={logFileQuoted}

echo [%date% %time%] Starting DeviceBatteryTray update process >> %LOGFILE%
echo [%date% %time%] Source: {extractedPathQuoted} >> %LOGFILE%
echo [%date% %time%] Destination: {currentDirQuoted} >> %LOGFILE%
echo [%date% %time%] Current EXE: {currentExePathQuoted} >> %LOGFILE%
echo.
echo Updating DeviceBatteryTray...
echo See log file: %LOGFILE%

REM Wait a moment for the current app to fully exit
echo [%date% %time%] Waiting 3 seconds for app to exit... >> %LOGFILE%
timeout /t 3 /nobreak >nul

REM Kill running processes forcefully and wait for them to actually exit
echo [%date% %time%] Starting process termination loop... >> %LOGFILE%
set KILL_COUNT=0

:kill_loop
set /a KILL_COUNT+=1
echo [%date% %time%] Kill attempt !KILL_COUNT! >> %LOGFILE%
taskkill /F /IM DeviceBatteryTray.exe >> %LOGFILE% 2>&1
taskkill /F /IM LGSTrayHID.exe >> %LOGFILE% 2>&1

REM Check if processes are still running
tasklist /FI ""IMAGENAME eq DeviceBatteryTray.exe"" 2>nul | find /I /N ""DeviceBatteryTray.exe"">nul
if ""!errorlevel!""==""0"" (
    echo [%date% %time%] DeviceBatteryTray.exe still running, waiting... >> %LOGFILE%
    goto :wait_more
)
tasklist /FI ""IMAGENAME eq LGSTrayHID.exe"" 2>nul | find /I /N ""LGSTrayHID.exe"">nul
if ""!errorlevel!""==""0"" (
    echo [%date% %time%] LGSTrayHID.exe still running, waiting... >> %LOGFILE%
    goto :wait_more
)
echo [%date% %time%] All processes terminated successfully >> %LOGFILE%
goto :copy_files

:wait_more
if !KILL_COUNT! GEQ 10 (
    echo [%date% %time%] ERROR: Could not terminate processes after 10 attempts! >> %LOGFILE%
    echo ERROR: Could not terminate processes. See log: %LOGFILE%
    pause
    exit /b 1
)
timeout /t 1 /nobreak >nul
goto :kill_loop

:copy_files
REM Ensure processes are really gone
echo [%date% %time%] Waiting additional 2 seconds before copying... >> %LOGFILE%
timeout /t 2 /nobreak >nul

REM Copy all files from extracted update (robocopy returns 0-7 for success)
echo [%date% %time%] Starting file copy with robocopy... >> %LOGFILE%
robocopy {extractedPathQuoted} {currentDirQuoted} /E /IS /IT /R:5 /W:2 /NP /NDL /NFL /NJH /NJS /LOG+:%LOGFILE%
set COPY_EXIT=%errorlevel%
echo [%date% %time%] Robocopy exit code: %COPY_EXIT% >> %LOGFILE%

REM Robocopy returns 0-7 for success, 8+ for errors
if %COPY_EXIT% GEQ 8 (
    echo [%date% %time%] ERROR: Failed to copy files. Error code: %COPY_EXIT% >> %LOGFILE%
    echo ERROR: Failed to copy files. Error code: %COPY_EXIT%
    echo See log file: %LOGFILE%
    pause
    exit /b 1
)
echo [%date% %time%] File copy completed successfully >> %LOGFILE%

REM Unblock executables
echo [%date% %time%] Unblocking executables... >> %LOGFILE%
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Unblock-File -Path {deviceExePathQuoted} -ErrorAction SilentlyContinue"" >> %LOGFILE% 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Unblock-File -Path {hidExePathQuoted} -ErrorAction SilentlyContinue"" >> %LOGFILE% 2>&1
echo [%date% %time%] Executables unblocked >> %LOGFILE%

REM Start the updated application
echo [%date% %time%] Starting updated application: {currentExePathQuoted} >> %LOGFILE%
start """" {currentExePathQuoted}
if errorlevel 1 (
    echo [%date% %time%] ERROR: Failed to start application! >> %LOGFILE%
    echo ERROR: Failed to start application. See log: %LOGFILE%
    pause
    exit /b 1
)
echo [%date% %time%] Application started successfully >> %LOGFILE%

REM Cleanup after a delay
echo [%date% %time%] Waiting 3 seconds before cleanup... >> %LOGFILE%
timeout /t 3 /nobreak >nul

echo [%date% %time%] Cleaning up extracted files... >> %LOGFILE%
rmdir /S /Q {extractedPathQuoted} >> %LOGFILE% 2>&1
echo [%date% %time%] Update process completed successfully >> %LOGFILE%
del ""%~f0""
";

                File.WriteAllText(updateScript, scriptContent, Encoding.UTF8);

                // Log update initiation
                LogUpdateInfo("Starting update installation", $"Current: {currentExePath}", $"Update from: {extractedPath}", $"Log: {logFile}");

                // Shutdown the application gracefully first
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });

                // Give the app a moment to start shutting down, then start the update script
                Task.Run(async () =>
                {
                    await Task.Delay(1000); // Wait 1 second for app to start shutting down
                    
                    // Start the update script (no admin needed - we're in user directory)
                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = updateScript,
                        CreateNoWindow = false, // Show window so user can see progress
                        WindowStyle = ProcessWindowStyle.Normal,
                        UseShellExecute = true
                    };

                    try
                    {
                        Process.Start(processStartInfo);
                    }
                    catch
                    {
                        // Script will still run even if process start fails
                    }
                });
            }
            catch (Exception ex)
            {
                LogUpdateError($"Failed to install update: {ex.Message}", ex.ToString());
                ShowBalloonTip("Update Error", $"Failed to install update: {ex.Message}\n\nCheck log file: {Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log")}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
        }

        private void LogUpdateInfo(string message, params string[] details)
        {
            try
            {
                var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
                var logLines = new List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}"
                };
                
                foreach (var detail in details)
                {
                    logLines.Add($"  -> {detail}");
                }
                
                File.AppendAllLines(logFile, logLines);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private void LogUpdateError(string message, string details = "")
        {
            try
            {
                var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
                var logLines = new List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}"
                };
                
                if (!string.IsNullOrEmpty(details))
                {
                    logLines.Add($"  Details: {details}");
                }
                
                File.AppendAllLines(logFile, logLines);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private void ScheduleUpdateCheck()
        {
            _updateCheckTimer?.Dispose();

            if (!AutoUpdateEnabled)
            {
                return;
            }

            // Check for updates daily
            var lastCheck = _userSettings.LastUpdateCheck;
            var timeSinceLastCheck = DateTime.Now - lastCheck;
            var interval = TimeSpan.FromHours(24);

            if (lastCheck == DateTime.MinValue || timeSinceLastCheck >= interval)
            {
                // Check immediately
                _ = Task.Run(async () => await CheckForUpdates());
                _updateCheckTimer = new Timer(_ => _ = Task.Run(async () => await CheckForUpdates()), null, interval, interval);
            }
            else
            {
                // Schedule next check
                var delay = interval - timeSinceLastCheck;
                _updateCheckTimer = new Timer(_ => _ = Task.Run(async () => await CheckForUpdates()), null, delay, interval);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Schedule automatic update checks if enabled
            if (AutoUpdateEnabled)
            {
                ScheduleUpdateCheck();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _updateCheckTimer?.Dispose();
            _updateCheckCts?.Cancel();
            _updateCheckCts?.Dispose();
            _mainTaskbarIconWrapper.Dispose();
            return Task.CompletedTask;
        }
    }
}
