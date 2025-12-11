using LGSTrayCore;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace LGSTrayUI
{
    public class LogiDeviceCollection : ILogiDeviceCollection
    {
        private readonly UserSettingsWrapper _userSettings;
        private readonly LogiDeviceViewModelFactory _logiDeviceViewModelFactory;
        private readonly ISubscriber<IPCMessage> _subscriber;

        // Track pending updates that arrived before their INIT messages
        private readonly ConcurrentDictionary<string, UpdateMessage> _pendingUpdates = new();
        // Track devices that are currently being initialized
        private readonly ConcurrentDictionary<string, bool> _initializingDevices = new();

        public ObservableCollection<LogiDeviceViewModel> Devices { get; } = [];
        public IEnumerable<LogiDevice> GetDevices() => Devices;

        public LogiDeviceCollection(
            UserSettingsWrapper userSettings,
            LogiDeviceViewModelFactory logiDeviceViewModelFactory,
            ISubscriber<IPCMessage> subscriber
        )
        {
            _userSettings = userSettings;
            _logiDeviceViewModelFactory = logiDeviceViewModelFactory;
            _subscriber = subscriber;

            _subscriber.Subscribe(x =>
            {
                if (x is InitMessage initMessage)
                {
                    OnInitMessage(initMessage);
                }
                else if (x is UpdateMessage updateMessage)
                {
                    OnUpdateMessage(updateMessage);
                }
            });

            LoadPreviouslySelectedDevices();
        }

        private void LoadPreviouslySelectedDevices()
        {
            foreach (var deviceId in _userSettings.SelectedDevices)
            {
                if (string.IsNullOrEmpty(deviceId))
                {
                    continue;
                }

                Devices.Add(
                    _logiDeviceViewModelFactory.CreateViewModel((x) =>
                    {
                        x.DeviceId = deviceId!;
                        x.DeviceName = "Not Initialised";
                        x.IsChecked = true;
                    })
                );
            }
        }

        public bool TryGetDevice(string deviceId, [NotNullWhen(true)] out LogiDevice? device)
        {
            device = Devices.SingleOrDefault(x => x.DeviceId == deviceId);

            return device != null;
        }

        public void OnInitMessage(InitMessage initMessage)
        {
            // Mark device as initializing
            _initializingDevices[initMessage.deviceId] = true;

            try
            {
                LogiDeviceViewModel? dev = Devices.SingleOrDefault(x => x.DeviceId == initMessage.deviceId);
                if (dev != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        dev.UpdateState(initMessage);
                        ApplyPendingUpdate(initMessage.deviceId, dev);
                    });
                    return;
                }

                dev = _logiDeviceViewModelFactory.CreateViewModel((x) => x.UpdateState(initMessage));

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    Devices.Add(dev);
                    ApplyPendingUpdate(initMessage.deviceId, dev);
                });
            }
            finally
            {
                _initializingDevices.TryRemove(initMessage.deviceId, out _);
            }
        }

        public void OnUpdateMessage(UpdateMessage updateMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(async () =>
            {
                var device = Devices.FirstOrDefault(dev => dev.DeviceId == updateMessage.deviceId);

                if (device == null)
                {
                    // Device not found - check if it's being initialized
                    if (_initializingDevices.ContainsKey(updateMessage.deviceId))
                    {
                        // Store the update to apply after initialization
                        _pendingUpdates[updateMessage.deviceId] = updateMessage;
                        return;
                    }

                    // Wait briefly and retry - the INIT message might be in-flight
                    await Task.Delay(500);
                    device = Devices.FirstOrDefault(dev => dev.DeviceId == updateMessage.deviceId);

                    if (device == null)
                    {
                        // Still not found - store for later
                        _pendingUpdates[updateMessage.deviceId] = updateMessage;
                        return;
                    }
                }

                device.UpdateState(updateMessage);
            });
        }

        private void ApplyPendingUpdate(string deviceId, LogiDeviceViewModel device)
        {
            if (_pendingUpdates.TryRemove(deviceId, out var pendingUpdate))
            {
                device.UpdateState(pendingUpdate);
            }
        }
    }
}
