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

using static LGSTrayUI.AppExtensions;
using System.Threading.Tasks;

namespace LGSTrayUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        var host = builder.Build();
        await host.RunAsync();
        Dispatcher.InvokeShutdown();
    }

    static async Task LoadAppSettings(Microsoft.Extensions.Configuration.ConfigurationManager config)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.toml");
        
        // If appsettings.toml doesn't exist, create it from default automatically
        if (!File.Exists(settingsPath))
        {
            try
            {
                await File.WriteAllBytesAsync(settingsPath, LGSTrayUI.Properties.Resources.defaultAppsettings);
            }
            catch
            {
                // If we can't write, try to continue - will show error later
            }
        }

        try
        {
            config.AddTomlFile("appsettings.toml");
        }
        catch (Exception ex)
        {
            if (ex is FileNotFoundException || ex is InvalidDataException)
            {
                // Auto-reset to default on first error (better UX - don't prompt immediately)
                try
                {
                    await File.WriteAllBytesAsync(settingsPath, LGSTrayUI.Properties.Resources.defaultAppsettings);
                    config.AddTomlFile("appsettings.toml");
                    // Successfully reset, continue silently
                }
                catch
                {
                    // If we still can't fix it, ask user
                    var msgBoxRet = MessageBox.Show(
                        "Failed to read settings file. The file may be corrupted or inaccessible.\n\nClick Yes to reset to defaults, or No to exit.", 
                        "LGSTray - Settings Load Error", 
                        MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes
                    );

                    if (msgBoxRet == MessageBoxResult.Yes)
                    {
                        try
                        {
                            await File.WriteAllBytesAsync(settingsPath, LGSTrayUI.Properties.Resources.defaultAppsettings);
                            config.AddTomlFile("appsettings.toml");
                        }
                        catch (Exception writeEx)
                        {
                            MessageBox.Show(
                                $"Failed to write settings file: {writeEx.Message}\n\nPlease check file permissions.", 
                                "LGSTray - Error", 
                                MessageBoxButton.OK, MessageBoxImage.Error
                            );
                            throw;
                        }
                    }
                    else
                    {
                        Environment.Exit(1);
                    }
                }
            }
            else
            {
                throw;
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