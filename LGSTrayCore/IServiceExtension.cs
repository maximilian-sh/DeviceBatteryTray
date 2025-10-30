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
        var settings = configs.Get<AppSettings>()!;
        bool isEnabled = typeof(T) switch
        {
            { } when typeof(T) == typeof(LGSTrayHIDManager) => settings.Native.Enabled,
            _ => false
        };
        if (!isEnabled) return;

        services.AddSingleton<T>();
        services.AddSingleton<IDeviceManager>(p => p.GetRequiredService<T>());
        services.AddSingleton<IHostedService>(p => p.GetRequiredService<T>());
    }
}
