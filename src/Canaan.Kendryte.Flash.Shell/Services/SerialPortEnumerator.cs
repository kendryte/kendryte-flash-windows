// Copyright 2018 Canaan Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Caliburn.Micro;

namespace Canaan.Kendryte.Flash.Shell.Services
{
    public class SerialDevice : IEquatable<SerialDevice>
    {
        public string Name { get; set; }
        public string Port { get; set; }

        public override bool Equals(object obj)
        {
            return Equals(obj as SerialDevice);
        }

        public bool Equals(SerialDevice other)
        {
            return other != null &&
                   Name == other.Name &&
                   Port == other.Port;
        }

        public override int GetHashCode()
        {
            var hashCode = 48670396;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Port);
            return hashCode;
        }

        public static bool operator ==(SerialDevice device1, SerialDevice device2)
        {
            return EqualityComparer<SerialDevice>.Default.Equals(device1, device2);
        }

        public static bool operator !=(SerialDevice device1, SerialDevice device2)
        {
            return !(device1 == device2);
        }
    }

    public class SerialPortEnumerator : IDisposable
    {
        private static readonly Version _osVersionSupportsPnP = new Version(6, 2);

        private ManagementEventWatcher _arrivalWatcher;
        private ManagementEventWatcher _removalWatcher;

        public BindableCollection<SerialDevice> Devices { get; } = new BindableCollection<SerialDevice>();

        public event EventHandler DevicesUpdated;

        public SerialPortEnumerator()
        {
            UpdateDevices();
            RegisterUpdateEvents();
        }

        private void RegisterUpdateEvents()
        {
            var deviceArrivalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
            var deviceRemovalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

            _arrivalWatcher = new ManagementEventWatcher(deviceArrivalQuery);
            _removalWatcher = new ManagementEventWatcher(deviceRemovalQuery);

            _arrivalWatcher.EventArrived += DeviceUpdated_EventHandler;
            _removalWatcher.EventArrived += DeviceUpdated_EventHandler;

            _arrivalWatcher.Start();
            _removalWatcher.Start();
        }

        private void UnregisterUpdateEvents()
        {
            if (_arrivalWatcher != null)
            {
                _arrivalWatcher.Stop();
                _arrivalWatcher.EventArrived -= DeviceUpdated_EventHandler;
                _arrivalWatcher.Dispose();
            }

            if (_removalWatcher != null)
            {
                _removalWatcher.Stop();
                _removalWatcher.EventArrived -= DeviceUpdated_EventHandler;
                _removalWatcher.Dispose();
            }
        }

        private void DeviceUpdated_EventHandler(object sender, EventArrivedEventArgs e)
        {
            UpdateDevices();
        }

        private void UpdateDevices()
        {
            SelectQuery query;

            // Workaround for Win 7 that doesn't has 'PNPClass' property
            if (Environment.OSVersion.Version < _osVersionSupportsPnP)
            {
                query = new SelectQuery("Win32_PnPEntity", "Caption like '%(COM%)'", new[] { "Caption" });
            }
            else
            {
                query = new SelectQuery("Win32_PnPEntity", "PNPClass='Ports'", new[] { "Caption" });
            }

            var searcher = new ManagementObjectSearcher(query);

            Devices.Clear();
            foreach (var pnpDevice in searcher.Get())
            {
                var port = Regex.Match((string)pnpDevice["Caption"], @"(?<=\()COM\d+(?=\))");
                if (port.Success)
                {
                    Devices.Add(new SerialDevice
                    {
                        Name = (string)pnpDevice["Caption"],
                        Port = port.Value
                    });
                }
            }

            DevicesUpdated?.Invoke(this, EventArgs.Empty);
        }

        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    UnregisterUpdateEvents();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
