namespace LGSTrayPrimitives;

public class AppSettings
{
    public UISettings UI { get; set; } = null!;

    public NativeDeviceManagerSettings Native { get; set; } = null!;
}

public class UISettings
{
    public bool EnableRichToolTips { get; set; }
}

public class NativeDeviceManagerSettings
{
    public int RetryTime { get; set; } = 5;
    public int PollPeriod { get; set; } = 120;

    public IEnumerable<string> DisabledDevices { get; set; } = [];
}
