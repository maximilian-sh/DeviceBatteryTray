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
            string currentPath = Environment.ProcessPath!;

            // Validate that the registry entry points to the current executable
            if (string.IsNullOrEmpty(registryPath) || 
                !Path.GetFullPath(registryPath).Equals(Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
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
            UpdateStatusMessage = "Checking for updates...";
            OnPropertyChanged(nameof(UpdateStatusMessage));

            try
            {
                _updateCheckCts?.Cancel();
                _updateCheckCts?.Dispose();
                _updateCheckCts = new CancellationTokenSource();

                var updateInfo = await UpdateChecker.CheckForUpdatesAsync(_updateCheckCts.Token);
                _userSettings.LastUpdateCheck = DateTime.Now;

                if (updateInfo == null)
                {
                    UpdateStatusMessage = "Unable to check for updates.";
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                    await Task.Delay(3000);
                    UpdateStatusMessage = string.Empty;
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                }
                else if (!updateInfo.IsNewer)
                {
                    UpdateStatusMessage = "You are running the latest version.";
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                    await Task.Delay(3000);
                    UpdateStatusMessage = string.Empty;
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                }
                else
                {
                    _availableUpdate = updateInfo;
                    UpdateStatusMessage = $"Update available: v{updateInfo.Version}";
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                    ShowUpdateNotification(updateInfo);
                }
            }
            catch (Exception)
            {
                UpdateStatusMessage = "Error checking for updates.";
                OnPropertyChanged(nameof(UpdateStatusMessage));
                await Task.Delay(3000);
                UpdateStatusMessage = string.Empty;
                OnPropertyChanged(nameof(UpdateStatusMessage));
            }
            finally
            {
                CheckingForUpdates = false;
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
            UpdateStatusMessage = "Downloading update...";
            OnPropertyChanged(nameof(UpdateStatusMessage));

            try
            {
                var progress = new Progress<int>(percent =>
                {
                    UpdateStatusMessage = $"Downloading update... {percent}%";
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                });

                var extractedPath = await UpdateChecker.DownloadUpdateAsync(
                    _availableUpdate.DownloadUrl,
                    progress,
                    CancellationToken.None);

                if (string.IsNullOrEmpty(extractedPath))
                {
                    UpdateStatusMessage = "Failed to download update.";
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                    await Task.Delay(3000);
                    UpdateStatusMessage = string.Empty;
                    OnPropertyChanged(nameof(UpdateStatusMessage));
                    return;
                }

                UpdateStatusMessage = "Update downloaded. Installing...";
                OnPropertyChanged(nameof(UpdateStatusMessage));
                InstallUpdate(extractedPath);
            }
            catch (Exception)
            {
                UpdateStatusMessage = "Error downloading update.";
                OnPropertyChanged(nameof(UpdateStatusMessage));
                await Task.Delay(3000);
                UpdateStatusMessage = string.Empty;
                OnPropertyChanged(nameof(UpdateStatusMessage));
            }
            finally
            {
                CheckingForUpdates = false;
            }
        }

        private void ShowUpdateNotification(UpdateInfo updateInfo)
        {
            if (_mainTaskbarIconWrapper?.TaskbarIcon == null)
            {
                return;
            }

            var title = $"Update Available: v{updateInfo.Version}";
            var message = "A new version is available. Click 'Download Update' in the menu to install.";

            _mainTaskbarIconWrapper.TaskbarIcon.ShowBalloonTip(
                title,
                message,
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
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
                    UpdateStatusMessage = "Update package is invalid.";
                    return;
                }

                // Create update script that will replace files, unblock them, and restart the app
                var updateScript = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.bat");
                var deviceExePath = Path.Combine(currentDir, "DeviceBatteryTray.exe").Replace("\\", "\\\\");
                var hidExePath = Path.Combine(currentDir, "LGSTrayHID.exe").Replace("\\", "\\\\");
                
                var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /F /IM DeviceBatteryTray.exe >nul 2>&1
taskkill /F /IM LGSTrayHID.exe >nul 2>&1
timeout /t 1 /nobreak >nul
xcopy /Y /E /I ""{extractedPath}\*"" ""{currentDir}\""
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Unblock-File -Path '{deviceExePath}' -ErrorAction SilentlyContinue""
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Unblock-File -Path '{hidExePath}' -ErrorAction SilentlyContinue""
start """" ""{currentExePath}""
timeout /t 2 /nobreak >nul
rmdir /S /Q ""{extractedPath}"" >nul 2>&1
del ""%~f0""
";

                File.WriteAllText(updateScript, scriptContent);

                // Start the update script
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = updateScript,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true,
                    Verb = "runas" // Run as admin if needed
                };

                Process.Start(processStartInfo);
                Environment.Exit(0);
            }
            catch (Exception)
            {
                UpdateStatusMessage = "Failed to install update. Please update manually.";
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
