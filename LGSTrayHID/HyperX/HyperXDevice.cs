using LGSTrayHID.HidApi;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayHID.HyperX
{
    internal static class HyperXDevice
    {
        public static Task StartPollingAsync(HidDevicePtr dev, Guid containerId, HidppManagerContext.HidppDeviceEventHandler? publisher, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                // Initial fast update then back off to default poll period
                TimeSpan delay = TimeSpan.FromSeconds(2);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Placeholder: batteryPercent retrieval logic should mirror HyperX repo's HID report sequence.
                        // For now, publish an update as unknown (-1) so UI registers the device.
                        double batteryPercent = -1;

                        publisher?.Invoke(
                            IPCMessageType.UPDATE,
                            new UpdateMessage(
                                containerId.ToString(),
                                batteryPercent,
                                PowerSupplyStatus.POWER_SUPPLY_STATUS_UNKNOWN,
                                0,
                                DateTimeOffset.Now
                            )
                        );
                    }
                    catch { }

                    await Task.Delay(delay, CancellationToken.None);
                    delay = TimeSpan.FromSeconds(GlobalSettings.settings.PollPeriod);
                }
            });
        }
    }
}


