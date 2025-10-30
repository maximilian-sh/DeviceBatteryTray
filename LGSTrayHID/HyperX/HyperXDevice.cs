using LGSTrayHID.HidApi;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayHID.HyperX
{
    internal static class HyperXDevice
    {
        public static Task StartPollingAsync(HidDevicePtr dev, Guid containerId, string manufacturer, string product, HidppManagerContext.HidppDeviceEventHandler? publisher, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                // Initial fast update then back off to default poll period
                TimeSpan delay = TimeSpan.FromSeconds(2);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        int batteryByteIdx = 7;
                        const int WRITE_BUFFER_SIZE = 52;
                        const int DATA_BUFFER_SIZE = 20;

                        byte[] writeBuffer = new byte[WRITE_BUFFER_SIZE];

                        bool isHP = manufacturer?.IndexOf("HP", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isHP)
                        {
                            if (!string.IsNullOrEmpty(product) && product.IndexOf("Cloud II Core", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                writeBuffer[0] = 0x66;
                                writeBuffer[1] = 0x89;
                                batteryByteIdx = 4;
                            }
                            else if (!string.IsNullOrEmpty(product) && (product.IndexOf("Cloud II Wireless", StringComparison.OrdinalIgnoreCase) >= 0 || product.IndexOf("Cloud Stinger 2 Wireless", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                writeBuffer[0] = 0x06;
                                writeBuffer[1] = 0xFF;
                                writeBuffer[2] = 0xBB;
                                writeBuffer[3] = 0x02;
                                batteryByteIdx = 7;
                            }
                            else if (!string.IsNullOrEmpty(product) && product.IndexOf("Cloud Alpha Wireless", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                writeBuffer[0] = 0x21;
                                writeBuffer[1] = 0xBB;
                                writeBuffer[2] = 0x0B;
                                batteryByteIdx = 3;
                            }
                            else
                            {
                                // Fallback HP path
                                writeBuffer[0] = 0x06;
                                writeBuffer[1] = 0xFF;
                                writeBuffer[2] = 0xBB;
                                writeBuffer[3] = 0x02;
                                batteryByteIdx = 7;
                            }
                        }
                        else
                        {
                            // Kingston Cloud II Wireless requires input report(6) read before write
                            byte[] inputBuf = new byte[160];
                            inputBuf[0] = 0x06;
                            _ = LGSTrayHID.HidApi.HidApi.HidGetInputReport(dev, inputBuf, (nuint)inputBuf.Length);

                            writeBuffer[0] = 0x06;
                            writeBuffer[2] = 0x02;
                            writeBuffer[4] = 0x9A;
                            writeBuffer[7] = 0x68;
                            writeBuffer[8] = 0x4A;
                            writeBuffer[9] = 0x8E;
                            writeBuffer[10] = 0x0A;
                            writeBuffer[14] = 0xBB;
                            writeBuffer[15] = 0x02;
                            batteryByteIdx = 7;
                        }

                        _ = await dev.WriteAsync(writeBuffer);

                        byte[] dataBuffer = new byte[DATA_BUFFER_SIZE];
                        int ret = dev.Read(dataBuffer, DATA_BUFFER_SIZE, 1000);
                        if (ret <= 0)
                        {
                            goto Publish;
                        }

                        int batteryRaw = (batteryByteIdx >= 0 && batteryByteIdx < ret) ? dataBuffer[batteryByteIdx] : -1;
                        double batteryPercent = (batteryRaw >= 0 && batteryRaw <= 100) ? batteryRaw : -1;

Publish:
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


