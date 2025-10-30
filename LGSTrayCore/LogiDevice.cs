using CommunityToolkit.Mvvm.ComponentModel;
using LGSTrayPrimitives;

namespace LGSTrayCore
{
    public partial class LogiDevice : ObservableObject
    {
        public const string NOT_FOUND = "NOT FOUND";

        [ObservableProperty]
        private DeviceType _deviceType;

        [ObservableProperty]
        private string _deviceId = NOT_FOUND;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private string _deviceName = NOT_FOUND;

        [ObservableProperty]
        private bool _hasBattery = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private double _batteryPercentage = -1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private double _batteryVoltage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private double _batteryMileage;


        [ObservableProperty]
        private PowerSupplyStatus _powerSupplyStatus;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;

        public string ToolTipString
        {
            get
            {
                if (BatteryPercentage < 0)
                {
                    return $"{DeviceName} — Off";
                }

                string percentText = $"{(int)Math.Round(BatteryPercentage)}%";

                string header = $"{DeviceName} — {percentText}";

                if (LastUpdate == DateTimeOffset.MinValue)
                {
                    return header;
                }

                var delta = DateTimeOffset.Now - LastUpdate;
                if (delta.TotalMinutes >= 10)
                {
                    string ago = $"{(int)Math.Floor(delta.TotalMinutes)}m ago";
                    return $"{header}\nUpdated: {ago}";
                }

                return header;
            }
        }

        public Func<Task>? UpdateBatteryFunc;
        public async Task UpdateBatteryAsync()
        {
            if (UpdateBatteryFunc != null)
            {
                await UpdateBatteryFunc.Invoke();
            }
        }

        partial void OnLastUpdateChanged(DateTimeOffset value)
        {
            Console.WriteLine(ToolTipString);
        }

        public string GetXmlData()
        {
            return
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                $"<xml>" +
                $"<device_id>{DeviceId}</device_id>" +
                $"<device_name>{DeviceName}</device_name>" +
                $"<device_type>{DeviceType}</device_type>" +
                $"<battery_percent>{BatteryPercentage:f2}</battery_percent>" +
                $"<battery_voltage>{BatteryVoltage:f2}</battery_voltage>" +
                $"<mileage>{BatteryMileage:f2}</mileage>" +
                $"<charging>{PowerSupplyStatus == PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING}</charging>" +
                $"<last_update>{LastUpdate}</last_update>" +
                $"</xml>"
                ;
        }
    }
}
