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
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Canaan.Kendryte.Flash.Shell.Properties;
using Canaan.Kendryte.Flash.Shell.Services;
using Ookii.Dialogs.Wpf;

namespace Canaan.Kendryte.Flash.Shell.ViewModels
{
    public class FlashViewModel : PropertyChangedBase
    {
        private SerialPortEnumerator _serialPortEnumerator = new SerialPortEnumerator();

        public BindableCollection<SerialDevice> Devices => _serialPortEnumerator.Devices;

        public Dictionary<string, uint> Chips { get; } = new Dictionary<string, uint>
        {
            { "In-Chip", 1 }
        };

        public IReadOnlyList<int> BaudRates { get; } = new List<int>
        {
            115200,
            1500000
        };

        private JobItemType? _currentJob;
        public JobItemType? CurrentJob
        {
            get => _currentJob;
            set => Set(ref _currentJob, value);
        }

        private JobItemStatus _currentJobStatus;
        public JobItemStatus CurrentJobStatus
        {
            get => _currentJobStatus;
            set => Set(ref _currentJobStatus, value);
        }

        private uint _chip;
        public uint Chip
        {
            get => _chip;
            set => Set(ref _chip, value);
        }

        private int _baudRate;
        public int BaudRate
        {
            get => _baudRate;
            set => Set(ref _baudRate, value);
        }

        private string _device;
        public string Device
        {
            get => _device;
            set => Set(ref _device, value);
        }

        private string _firmware;
        public string Firmware
        {
            get => _firmware;
            set => Set(ref _firmware, value);
        }

        private bool _isFlashing;
        public bool IsFlashing
        {
            get => _isFlashing;
            set
            {
                if (Set(ref _isFlashing, value))
                {
                    NotifyOfPropertyChange(nameof(CanStartFlash));
                }
            }
        }

        public bool CanStartFlash => !IsFlashing;

        private string _license;
        public string License
        {
            get => _license;
            set => Set(ref _license, value);
        }

        public FlashViewModel()
        {
            Chip = Chips.First().Value;
            BaudRate = BaudRates.Last();
            License = Resources.LICENSE;

            Device = Devices.FirstOrDefault()?.Port;
        }

        public async void StartFlash()
        {
            if (string.IsNullOrEmpty(Firmware))
                throw new InvalidOperationException("Must specify firmware path.");
            if (string.IsNullOrEmpty(Device))
                throw new InvalidOperationException("Must select device.");

            try
            {
                IsFlashing = true;
                var firmware = await Task.Run(() => File.ReadAllBytes(Firmware));

                using (var loader = new KendryteLoader(Device, BaudRate))
                {
                    loader.CurrentJobChanged += (s, e) =>
                      {
                          CurrentJob = loader.CurrentJob;
                          CurrentJobStatus = loader.JobItemsStatus[loader.CurrentJob];
                      };

                    await loader.BootToISPMode();
                    await loader.Greeting();
                    await loader.InstallFlashBootloader(Resources.ISP_PROG);
                    await loader.BootBootloader();
                    await loader.FlashGreeting();
                    await loader.ChangeBaudRate();
                    await loader.InitializeFlash(Chip);
                    await loader.FlashFirmware(firmware);
                    await loader.Reboot();
                }

                IsFlashing = false;
                CurrentJob = null;
                CurrentJobStatus = null;
                MessageBox.Show("Flash completed!", "K-Flash", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsFlashing = false;
            }
        }

        public void BrowseFirmware()
        {
            var dialog = new VistaOpenFileDialog
            {
                Title = "Open firmware",
                Filter = "Firmware (*.bin)|*.bin",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                Firmware = dialog.FileName;
            }
        }
    }
}
