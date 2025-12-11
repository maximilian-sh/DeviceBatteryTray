using static LGSTrayHID.HidApi.HidApi;
using static LGSTrayHID.HidApi.HidApiWinApi;
using static LGSTrayHID.HidApi.HidApiHotPlug;
using LGSTrayHID.HidApi;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayHID
{
    public sealed class HidppManagerContext
    {
        public static readonly HidppManagerContext _instance = new();
        public static HidppManagerContext Instance => _instance;

        private readonly Dictionary<string, Guid> _containerMap = [];
        private readonly Dictionary<Guid, HidppDevices> _deviceMap = [];
        private readonly object _deviceMapLock = new();
        private readonly BlockingCollection<HidDeviceInfo> _deviceQueue = [];

        public delegate void HidppDeviceEventHandler(IPCMessageType messageType, IPCMessage message);

        public event HidppDeviceEventHandler? HidppDeviceEvent;

        private HidppManagerContext()
        {

        }

        static HidppManagerContext()
        {
            _ = HidInit();
        }

        public void SignalDeviceEvent(IPCMessageType messageType, IPCMessage message)
        {
            HidppDeviceEvent?.Invoke(messageType, message);
        }

        private unsafe int EnqueueDevice(HidHotPlugCallbackHandle _, HidDeviceInfo* device, HidApiHotPlugEvent hidApiHotPlugEvent, nint __)
        {
            if (hidApiHotPlugEvent == HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED)
            {
                _deviceQueue.Add(*device);
            }

            return 0;
        }

        private async Task<int> InitDevice(HidDeviceInfo deviceInfo)
        {
            string devPath = (deviceInfo).GetPath();

            HidDevicePtr dev = HidOpenPath(ref deviceInfo);
            _ = HidWinApiGetContainerId(dev, out Guid containerId);

#if DEBUG
            Console.WriteLine(devPath);
            Console.WriteLine(containerId.ToString());
            Console.WriteLine("x{0:X04}", (deviceInfo).Usage);
            Console.WriteLine("x{0:X04}", (deviceInfo).UsagePage);
            Console.WriteLine();
#endif

            // Logitech path via HID++ interfaces
            var messageType = (deviceInfo).GetHidppMessageType();
            if (messageType is HidppMessageType.SHORT or HidppMessageType.LONG)
            {
                HidppDevices? value;
                lock (_deviceMapLock)
                {
                    if (!_deviceMap.TryGetValue(containerId, out value))
                    {
                        value = new();
                        _deviceMap[containerId] = value;
                        _containerMap[devPath] = containerId;
                    }
                }

                switch (messageType)
                {
                    case HidppMessageType.SHORT:
                        await value.SetDevShort(dev);
                        break;
                    case HidppMessageType.LONG:
                        await value.SetDevLong(dev);
                        break;
                }

                return 0;
            }

            // HyperX path: detect by known vendor IDs (Kingston/HP) and handle separately
            if (deviceInfo.VendorId == 0x0951 || deviceInfo.VendorId == 0x03F0)
            {
                string manufacturer = deviceInfo.GetManufacturerString();
                string product = deviceInfo.GetProductString();

                // Choose the best HyperX interface (highest usage/usage_page), similar to HyperX project
                unsafe
                {
                    var head = HidEnumerate(deviceInfo.VendorId, deviceInfo.ProductId);
                    HidDeviceInfo* best = null;
                    int bestUsage = -1;
                    int bestUsagePage = -1;
                    for (var cur = head; cur != null; cur = cur->Next)
                    {
                        if (cur->Usage > bestUsage || (cur->Usage == bestUsage && cur->UsagePage >= bestUsagePage))
                        {
                            best = cur;
                            bestUsage = cur->Usage;
                            bestUsagePage = cur->UsagePage;
                        }
                    }

                    if (best != null)
                    {
                        // Open the best interface path for battery query
                        dev = HidOpenPath(ref *best);
                    }

                    if (head != null)
                    {
                        HidFreeEnumeration(head);
                    }
                }
                // Publish minimal init now; polling handled by HyperX handler
                HidppDeviceEvent?.Invoke(
                    LGSTrayPrimitives.MessageStructs.IPCMessageType.INIT,
                    new LGSTrayPrimitives.MessageStructs.InitMessage(
                        containerId.ToString(),
                        string.IsNullOrEmpty(product) ? "HyperX Wireless" : product,
                        true,
                        LGSTrayPrimitives.DeviceType.Headset
                    )
                );

                // Start polling in background (implementation inside HyperXDevice)
                _ = HyperX.HyperXDevice.StartPollingAsync(dev, containerId, manufacturer, product, HidppDeviceEvent, CancellationToken.None);
                lock (_deviceMapLock)
                {
                    _containerMap[devPath] = containerId;
                }
                return 0;
            }

            return 0;
        }

        private unsafe int DeviceLeft(HidHotPlugCallbackHandle callbackHandle, HidDeviceInfo* deviceInfo, HidApiHotPlugEvent hidApiHotPlugEvent, nint userData)
        {
            string devPath = (*deviceInfo).GetPath();

            HidppDevices? deviceToDispose = null;
            Guid containerId;

            lock (_deviceMapLock)
            {
                if (_containerMap.TryGetValue(devPath, out containerId))
                {
                    if (_deviceMap.TryGetValue(containerId, out deviceToDispose))
                    {
                        _deviceMap.Remove(containerId);
                    }
                    _containerMap.Remove(devPath);
                }
                else
                {
                    return 0;
                }
            }

            // Notify UI that device is now off/not active (outside of lock)
            HidppDeviceEvent?.Invoke(
                LGSTrayPrimitives.MessageStructs.IPCMessageType.UPDATE,
                new LGSTrayPrimitives.MessageStructs.UpdateMessage(
                    containerId.ToString(),
                    -1,
                    LGSTrayPrimitives.PowerSupplyStatus.POWER_SUPPLY_STATUS_UNKNOWN,
                    0,
                    DateTimeOffset.Now
                )
            );

            // Dispose outside of lock to prevent deadlocks
            deviceToDispose?.Dispose();

            return 0;
        }

        public void Start(CancellationToken cancellationToken)
        {
            new Thread(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var dev = _deviceQueue.Take();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await InitDevice(dev);
                }
            }).Start();

            unsafe
            {
                // Logitech
                HidHotplugRegisterCallback(0x046D, 0x00, HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED, HidApiHotPlugFlag.HID_API_HOTPLUG_ENUMERATE, EnqueueDevice, IntPtr.Zero, (int*)IntPtr.Zero);
                HidHotplugRegisterCallback(0x046D, 0x00, HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT, HidApiHotPlugFlag.NONE, DeviceLeft, IntPtr.Zero, (int*)IntPtr.Zero);
                // HyperX (Kingston)
                HidHotplugRegisterCallback(0x0951, 0x00, HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED, HidApiHotPlugFlag.HID_API_HOTPLUG_ENUMERATE, EnqueueDevice, IntPtr.Zero, (int*)IntPtr.Zero);
                HidHotplugRegisterCallback(0x0951, 0x00, HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT, HidApiHotPlugFlag.NONE, DeviceLeft, IntPtr.Zero, (int*)IntPtr.Zero);
                // HyperX (HP)
                HidHotplugRegisterCallback(0x03F0, 0x00, HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED, HidApiHotPlugFlag.HID_API_HOTPLUG_ENUMERATE, EnqueueDevice, IntPtr.Zero, (int*)IntPtr.Zero);
                HidHotplugRegisterCallback(0x03F0, 0x00, HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT, HidApiHotPlugFlag.NONE, DeviceLeft, IntPtr.Zero, (int*)IntPtr.Zero);
            }
        }
    
        public async Task ForceBatteryUpdates()
        {
            // Get a snapshot of devices under lock
            List<HidppDevices> devices;
            lock (_deviceMapLock)
            {
                devices = _deviceMap.Values.ToList();
            }

            foreach (var hidppDevice in devices)
            {
                if (hidppDevice.Disposed) continue;

                var tasks = hidppDevice.DeviceCollection
                    .Select(x => x.Value)
                    .Select(x => x.UpdateBattery(true));

                await Task.WhenAll(tasks);
            }
        }
    }
}
