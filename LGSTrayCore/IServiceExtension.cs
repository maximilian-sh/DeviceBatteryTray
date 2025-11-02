using LGSTrayCore.Managers;
using LGSTrayPrimitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LGSTrayCore;

public static class IServiceExtension
{
    public static void AddIDeviceManager<T>(this IServiceCollection services, IConfiguration configs) where T : class, IDeviceManager, IHostedService
    {
        var settings = configs.Get<AppSettings>();
        if (settings == null)
        {
            // If settings are null, create default settings
            settings = new AppSettings
            {
                UI = new UISettings(),
                Native = new NativeDeviceManagerSettings()
            };
        }
        
        // Ensure UI section exists
        if (settings.UI == null)
        {
            settings.UI = new UISettings();
        }
        
        // Ensure Native section exists
        if (settings.Native == null)
        {
            settings.Native = new NativeDeviceManagerSettings();
        }
        
        // Always register the manager (no enabled check - this is the only manager we have)
        services.AddSingleton<T>();
        services.AddSingleton<IDeviceManager>(p => p.GetRequiredService<T>());
        services.AddSingleton<IHostedService>(p => p.GetRequiredService<T>());
    }
}
