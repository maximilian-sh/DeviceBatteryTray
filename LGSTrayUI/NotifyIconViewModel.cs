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
        public string AvailableUpdateVersion => _availableUpdate != null ? $"Update to v{_availableUpdate.Version}" : "Update";
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
                LogUpdateInfo("CheckForUpdates called but already checking - ignoring");
                return;
            }

            CheckingForUpdates = true;
            LogUpdateInfo("Starting update check", $"Current version: {AssemblyVersion}");
            ShowBalloonTip("Checking for updates...", "Please wait while we check for the latest version.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);

            try
            {
                _updateCheckCts?.Cancel();
                _updateCheckCts?.Dispose();
                _updateCheckCts = new CancellationTokenSource();
                LogUpdateInfo("Created cancellation token source for update check");

                LogUpdateInfo("Calling UpdateChecker.CheckForUpdatesAsync");
                var updateInfo = await UpdateChecker.CheckForUpdatesAsync(_updateCheckCts.Token);
                _userSettings.LastUpdateCheck = DateTime.Now;
                LogUpdateInfo("Update check completed", $"Result: {(updateInfo == null ? "null" : $"Version {updateInfo.Version}, IsNewer: {updateInfo.IsNewer}")}");
                
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
            var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
            
            // Initialize log file at the very start
            try
            {
                File.WriteAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========================================\n");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UPDATE PROCESS STARTED\n");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Current version: {AssemblyVersion}\n");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========================================\n\n");
            }
            catch { }
            
            if (_availableUpdate == null)
            {
                LogUpdateError("DownloadAndInstallUpdate called but _availableUpdate is null");
                return;
            }
            
            if (CheckingForUpdates)
            {
                LogUpdateInfo("DownloadAndInstallUpdate called but already in progress - ignoring");
                return;
            }

            CheckingForUpdates = true;
            LogUpdateInfo("Starting download and install", $"Target version: {_availableUpdate.Version}", $"Download URL: {_availableUpdate.DownloadUrl}");

            // Create and show progress window
            UpdateProgressWindow? progressWindow = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogUpdateInfo("Creating progress window");
                progressWindow = new UpdateProgressWindow(_availableUpdate.Version)
                {
                    Owner = null // No owner to prevent closing issues
                };
                progressWindow.Show();
                LogUpdateInfo("Progress window shown");
            });

            try
            {
                LogUpdateInfo("Setting up progress callback");
                var progress = new Progress<int>(percent =>
                {
                    if (percent % 10 == 0 || percent == 100)
                    {
                        LogUpdateInfo($"Download progress: {percent}%");
                    }
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

                LogUpdateInfo("Starting download", $"URL: {_availableUpdate.DownloadUrl}");
                string? extractedPath = null;
                Exception? downloadException = null;
                
                try
                {
                    extractedPath = await UpdateChecker.DownloadUpdateAsync(
                        _availableUpdate.DownloadUrl,
                        progress,
                        CancellationToken.None);
                    LogUpdateInfo($"Download completed", $"Extracted path: {extractedPath ?? "null"}");
                }
                catch (Exception ex)
                {
                    downloadException = ex;
                    LogUpdateError($"Download exception: {ex.GetType().Name}", ex.ToString());
                }

                if (string.IsNullOrEmpty(extractedPath))
                {
                    LogUpdateError("Download failed - extractedPath is null or empty", downloadException?.ToString() ?? "No exception details");
                    var errorMsg = downloadException != null 
                        ? $"Download failed: {downloadException.Message}\n\nCheck log: {logFile}"
                        : "Download failed. Please check your internet connection and try again.\n\nCheck log: " + logFile;
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressWindow?.Close();
                        ShowBalloonTip("Download Failed", errorMsg, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                    });
                    CheckingForUpdates = false;
                    LogUpdateInfo("Update process aborted due to download failure");
                    return;
                }

                LogUpdateInfo("Download successful, proceeding to installation", $"Extracted to: {extractedPath}");
                
                // Keep window open, show installation status
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (progressWindow != null)
                    {
                        progressWindow.UpdateProgress(100, "Installing update...");
                        progressWindow.SetStatus($"Download complete!\n\nPreparing installation...\n\nLog: {logFile}");
                    }
                });

                // Small delay to show completion
                await Task.Delay(1000);
                
                LogUpdateInfo("Clearing update state and proceeding to installation");
                // Clear the available update since we're installing it
                _availableUpdate = null;
                OnPropertyChanged(nameof(UpdateAvailable));
                OnPropertyChanged(nameof(AvailableUpdateVersion));
                
                // Install update - this will restart the app, so window will close naturally
                LogUpdateInfo("Calling InstallUpdate");
                await InstallUpdate(extractedPath, progressWindow);
            }
            catch (Exception ex)
            {
                LogUpdateError($"Unexpected error in DownloadAndInstallUpdate: {ex.GetType().Name}", ex.ToString());
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow?.Close();
                    var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
                    ShowBalloonTip("Download Error", $"Error downloading update: {ex.Message}\n\nCheck log: {logFile}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                });
                CheckingForUpdates = false;
                LogUpdateInfo("Update process ended due to exception");
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

        private async Task InstallUpdate(string extractedPath, UpdateProgressWindow? progressWindow)
        {
            LogUpdateInfo("InstallUpdate called", $"Extracted path: {extractedPath}");
            
            try
            {
                var currentExePath = Environment.ProcessPath!;
                var currentDir = Path.GetDirectoryName(currentExePath)!;
                var updateExePath = Path.Combine(extractedPath, "DeviceBatteryTray.exe");

                LogUpdateInfo("Checking update package", $"Current EXE: {currentExePath}", $"Current dir: {currentDir}", $"Update EXE: {updateExePath}");

                if (!File.Exists(updateExePath))
                {
                    LogUpdateError("Update package is invalid - DeviceBatteryTray.exe not found in extracted path", extractedPath);
                    UpdateStatusMessage = "Update package is invalid.";
                    return;
                }
                
                LogUpdateInfo("Update package validated successfully");

                // Create log file for update process - create it early to ensure it exists
                var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
                var logFileQuoted = $"\"{logFile}\"";
                
                // Initialize log file
                try
                {
                    File.WriteAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Update process initialized\n");
                    File.AppendAllText(logFile, $"Log file location: {logFile}\n");
                }
                catch
                {
                    // If we can't create log, continue anyway
                }

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
                LogUpdateInfo("Starting update installation", $"Current: {currentExePath}", $"Update from: {extractedPath}", $"Log: {logFile}", $"Script: {updateScript}");

                // Update progress window
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (progressWindow != null)
                    {
                        progressWindow.SetStatus("Preparing update... Starting installation script...");
                    }
                });

                // CRITICAL: Start the update script FIRST, before shutting down
                // The script will wait for the app to exit, then do the update
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = updateScript,
                    CreateNoWindow = false, // Show window so user can see progress
                    WindowStyle = ProcessWindowStyle.Normal,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetTempPath()
                };

                try
                {
                    LogUpdateInfo("Starting update script", $"Script: {updateScript}");
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (progressWindow != null)
                        {
                            progressWindow.SetStatus("Starting update script...");
                        }
                    });
                    
                    var updateProcess = Process.Start(processStartInfo);
                    
                    if (updateProcess == null)
                    {
                        LogUpdateError("Failed to start update script - Process.Start returned null");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            progressWindow?.Close();
                            ShowBalloonTip("Update Error", $"Failed to start update script. Check log: {logFile}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                        });
                        CheckingForUpdates = false;
                        return;
                    }
                    
                    LogUpdateInfo("Update script started successfully", $"Process ID: {updateProcess.Id}", $"Script path: {updateScript}");
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (progressWindow != null)
                        {
                            progressWindow.SetStatus($"Update script running (PID: {updateProcess.Id})\nThe app will restart in 2 seconds...\n\nSee log: {logFile}");
                        }
                    });
                    
                    // Give the script a moment to start and initialize, then shutdown the app
                    // The script will wait for us to exit before proceeding
                    LogUpdateInfo("Waiting 2 seconds for script to initialize");
                    await Task.Delay(2000); // Give script time to initialize
                    
                    LogUpdateInfo("Shutting down application for update", "Calling Environment.Exit(0)");
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (progressWindow != null)
                        {
                            progressWindow.SetStatus("Restarting application...\n\nCheck the update script window for progress.");
                        }
                    });
                    
                    // Small delay to show the message
                    await Task.Delay(500);
                    
                    LogUpdateInfo("=== APPLICATION EXITING FOR UPDATE ===", "The update script will continue the process");
                    
                    // Force exit immediately - don't wait for graceful shutdown
                    // The script will handle the rest
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    LogUpdateError($"Failed to start update script: {ex.Message}", ex.ToString());
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressWindow?.Close();
                        ShowBalloonTip("Update Error", $"Failed to start update script: {ex.Message}\n\nCheck log: {logFile}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                    });
                    CheckingForUpdates = false;
                }
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
