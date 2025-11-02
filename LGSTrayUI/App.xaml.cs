using LGSTrayCore;
using LGSTrayCore.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using System;
using LGSTrayPrimitives.IPC;
using System.Globalization;
using System.IO;
using System.Threading;
using LGSTrayPrimitives;
using Tommy.Extensions.Configuration;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using System.Text;

using static LGSTrayUI.AppExtensions;
using System.Threading.Tasks;

namespace LGSTrayUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check if another instance is already running
        const string mutexName = "DeviceBatteryTray_SingleInstance_Mutex";
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
        
        if (!createdNew)
        {
            // Another instance is already running
            MessageBox.Show(
                "DeviceBatteryTray is already running.\n\nPlease check your system tray for the application icon.",
                "DeviceBatteryTray - Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);

        EnableEfficiencyMode();

        var builder = Host.CreateEmptyApplicationBuilder(null);
        await LoadAppSettings(builder.Configuration);

        builder.Services.Configure<AppSettings>(builder.Configuration);
        builder.Services.AddLGSMessagePipe(true);
        builder.Services.AddSingleton<UserSettingsWrapper>();

        builder.Services.AddSingleton<LogiDeviceIconFactory>();
        builder.Services.AddSingleton<LogiDeviceViewModelFactory>();

        // HTTP webserver disabled for minimal HID-only build

        builder.Services.AddIDeviceManager<LGSTrayHIDManager>(builder.Configuration);
        builder.Services.AddSingleton<ILogiDeviceCollection, LogiDeviceCollection>();

        builder.Services.AddSingleton<MainTaskbarIconWrapper>();
        builder.Services.AddHostedService<NotifyIconViewModel>();

        try
        {
            var host = builder.Build();
            await host.RunAsync();
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("Pipe") || ioEx.Message.Contains("pipe") || ioEx.Message.Contains("Pipeinstanzen"))
        {
            // Named pipe conflict - another instance might be running or pipe not cleaned up
            MessageBox.Show(
                $"Another instance of DeviceBatteryTray may be running, or a previous instance did not exit cleanly.\n\nError: {ioEx.Message}\n\nPlease:\n1. Check Task Manager for other instances\n2. Restart your computer if the issue persists",
                "DeviceBatteryTray - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown();
            return;
        }
        finally
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        
        Dispatcher.InvokeShutdown();
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    static async Task LoadAppSettings(Microsoft.Extensions.Configuration.ConfigurationManager config)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.toml");
        var settingsDir = Path.GetDirectoryName(settingsPath)!;
        
        // Ensure directory exists and has write permissions
        try
        {
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }
            
            // Test if we can actually write to this directory (simpler check)
            var testFile = Path.Combine(settingsDir, ".write_test");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception testEx)
            {
                throw new IOException($"Cannot write to directory {settingsDir}: {testEx.Message}", testEx);
            }
        }
        catch (Exception dirEx)
        {
            MessageBox.Show(
                $"Cannot create or access application directory:\n{settingsDir}\n\nError: {dirEx.Message}\n\nPlease check permissions or run as administrator.", 
                "LGSTray - Directory Error", 
                MessageBoxButton.OK, MessageBoxImage.Error
            );
            Environment.Exit(1);
        }
        
        // If appsettings.toml doesn't exist, create it from default automatically
        if (!File.Exists(settingsPath))
        {
            try
            {
                // Try embedded resource first
                bool fileCreated = false;
                try
                {
                    var defaultBytes = LGSTrayUI.Properties.Resources.defaultAppsettings;
                    if (defaultBytes != null && defaultBytes.Length > 0)
                    {
                        await File.WriteAllBytesAsync(settingsPath, defaultBytes);
                        fileCreated = true;
                    }
                }
                catch { }
                
                if (!fileCreated)
                {
                    // Fallback: Create minimal valid TOML file directly
                    var minimalToml = @"[UI]
enableRichToolTips = true

[Native]
retryTime = 5
pollPeriod = 120
disabledDevices = []
";
                    await File.WriteAllTextAsync(settingsPath, minimalToml, Encoding.UTF8);
                }
            }
            catch (Exception createEx)
            {
                MessageBox.Show(
                    $"Cannot create settings file:\n{settingsPath}\n\nError: {createEx.Message}\n\nThis may be a permissions issue. Please:\n1. Run the installer again\n2. Or run this app as administrator\n\nThe app cannot continue.", 
                    "LGSTray - Settings Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error
                );
                Environment.Exit(1);
            }
        }

        // Try to load the configuration file
        try
        {
            config.AddTomlFile("appsettings.toml");
        }
        catch (Exception loadEx)
        {
            // Always try to auto-fix by recreating the file
            // This handles corruption, parsing errors, or permission issues
            try
            {
                // Backup existing file if it exists
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var backupPath = settingsPath + ".backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        File.Copy(settingsPath, backupPath);
                    }
                    catch { }
                    
                    // Remove read-only if needed and delete
                    try
                    {
                        var fileInfo = new FileInfo(settingsPath);
                        if (fileInfo.IsReadOnly)
                        {
                            fileInfo.IsReadOnly = false;
                        }
                        File.Delete(settingsPath);
                    }
                    catch
                    {
                        // If delete fails, try renaming
                        try
                        {
                            var oldPath = settingsPath + ".old";
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(settingsPath, oldPath);
                        }
                        catch { }
                    }
                }
                
                // Create fresh file - try embedded resource first, fallback to minimal TOML
                bool fileCreated = false;
                try
                {
                    // Try embedded resource
                    var defaultBytes = LGSTrayUI.Properties.Resources.defaultAppsettings;
                    if (defaultBytes != null && defaultBytes.Length > 0)
                    {
                        await File.WriteAllBytesAsync(settingsPath, defaultBytes);
                        fileCreated = true;
                    }
                }
                catch { }
                
                if (!fileCreated)
                {
                    // Fallback: Create minimal valid TOML file directly
                    var minimalToml = @"[UI]
enableRichToolTips = true

[Native]
retryTime = 5
pollPeriod = 120
disabledDevices = []
";
                    await File.WriteAllTextAsync(settingsPath, minimalToml, Encoding.UTF8);
                }
                
                // Ensure it's not read-only
                var newFileInfo = new FileInfo(settingsPath);
                if (newFileInfo.IsReadOnly)
                {
                    newFileInfo.IsReadOnly = false;
                }
                
                // Small delay to ensure file is fully written
                await Task.Delay(50);
                
                // Try loading the recreated file
                config.AddTomlFile("appsettings.toml");
                // Success! File was recreated and loaded
            }
            catch (Exception fixEx)
            {
                // Even recreation failed - show error with details
                var errorDetails = $"Error loading settings: {loadEx.GetType().Name} - {loadEx.Message}";
                if (loadEx.InnerException != null)
                {
                    errorDetails += $"\nInner: {loadEx.InnerException.Message}";
                }
                errorDetails += $"\n\nRecreation failed: {fixEx.GetType().Name} - {fixEx.Message}";
                
                MessageBox.Show(
                    $"Failed to load or recreate settings file:\n{settingsPath}\n\n{errorDetails}\n\nPlease manually delete the file and restart the app, or run the installer again.",
                    "LGSTray - Settings Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Environment.Exit(1);
            }
        }
    }

    private void CrashHandler(object sender, UnhandledExceptionEventArgs args)
    {
        Exception e = (Exception)args.ExceptionObject;
        long unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

        using StreamWriter writer = new($"./crashlog_{unixTime}.log", false);
        writer.WriteLine(e.ToString());
    }
}