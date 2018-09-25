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
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Canaan.Kendryte.Flash.Shell.Properties;
using Force.Crc32;
using Ookii.Dialogs.Wpf;

namespace Canaan.Kendryte.Flash.Shell.ViewModels
{
    public class SerialDevice
    {
        public string Name { get; set; }
        public string Port { get; set; }
    }

    public class FlashViewModel : PropertyChangedBase
    {
        private static readonly Version _osVersionSupportsPnP = new Version(6, 2);

        public BindableCollection<SerialDevice> Devices { get; } = new BindableCollection<SerialDevice>();

        public Dictionary<string, uint> Chips { get; } = new Dictionary<string, uint>
        {
            { "In-Chip", 1 }
        };

        public IReadOnlyList<int> BaudRates { get; } = new List<int>
        {
            115200
        };

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
            BaudRate = BaudRates[0];
            License = Resources.LICENSE;

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
                await Task.Run(() =>
                {
                    var firmware = File.ReadAllBytes(Firmware);

                    using (var loader = new Loader(Device, BaudRate))
                    {
                        loader.Greeting();
                        loader.InstallFlashBootloader(Resources.ISP_PROG);
                        loader.Boot();
                        loader.FlashGreeting();
                        loader.InitializeFlash(Chip);
                        loader.FlashFirmware(firmware);
                    }
                });

                IsFlashing = false;
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

        class Loader : IDisposable
        {
            private static readonly byte[] _greeting = new byte[]
            {
                0xc0, 0xc2, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xc0
            };

            private static readonly byte[] _flashGreeting = new byte[]
            {
                0xc0, 0xd2, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xc0
            };

            private readonly SerialPort _port;

            public Loader(string device, int baudRate)
            {
                _port = new SerialPort(device, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 2000
                };

                _port.Open();
            }

            public void Greeting()
            {
                _port.Write(_greeting, 0, _greeting.Length);
                var resp = ISPResponse.Parse(ReceiveOnReturn());
                if (resp.errorCode != ISPResponse.ErrorCode.ISP_RET_OK)
                    throw new InvalidOperationException("Error in greeting.");
            }

            public void InstallFlashBootloader(byte[] bootloader)
            {
                FlashDataFrame(bootloader, 0x80000000);
            }

            public void Boot()
            {
                var buffer = new byte[4 * 4];
                using (var stream = new MemoryStream(buffer))
                using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write((ushort)ISPResponse.Operation.ISP_MEMORY_BOOT);
                    bw.Write((ushort)0x00);
                    bw.Write((uint)0);  // checksum
                    bw.Write(0x80000000);
                    bw.Write(0);

                    bw.Flush();
                    var checksum = Crc32Algorithm.Compute(buffer, 4 * 2, buffer.Length - 4 * 2);
                    bw.Seek(4, SeekOrigin.Begin);
                    bw.Write(checksum);
                }

                Write(buffer);
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }

            public void FlashGreeting()
            {
                _port.Write(_flashGreeting, 0, _flashGreeting.Length);
                var resp = FlashModeResponse.Parse(ReceiveOnReturn());
                if (resp.errorCode != FlashModeResponse.ErrorCode.ISP_RET_OK)
                    throw new InvalidOperationException("Error in flash greeting.");
            }

            public void InitializeFlash(uint chip)
            {
                var buffer = new byte[4 * 4];
                using (var stream = new MemoryStream(buffer))
                using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write((ushort)FlashModeResponse.Operation.FLASHMODE_FLASH_INIT);
                    bw.Write((ushort)0x00);
                    bw.Write((uint)0);  // checksum
                    bw.Write(chip);
                    bw.Write(0);

                    bw.Flush();
                    var checksum = Crc32Algorithm.Compute(buffer, 4 * 2, buffer.Length - 4 * 2);
                    bw.Seek(4, SeekOrigin.Begin);
                    bw.Write(checksum);
                }

                Write(buffer);
                var resp = FlashModeResponse.Parse(ReceiveOnReturn());
                if (resp.errorCode != FlashModeResponse.ErrorCode.ISP_RET_OK)
                    throw new InvalidOperationException("Error in flash initializing.");
            }

            public void FlashFirmware(byte[] data)
            {
                var dataPack = new byte[1 + 4 + data.Length + 32];
                using (var stream = new MemoryStream(dataPack))
                using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write((byte)0);
                    bw.Write((uint)data.Length);
                    bw.Write(data, 0, data.Length);

                    bw.Flush();
                    using (var sha256 = SHA256.Create())
                    {
                        var digest = sha256.ComputeHash(dataPack, 0, 1 + 4 + data.Length);
                        bw.Write(digest);
                    }
                }

                const int dataframeSize = 4096;

                var rest = dataPack.AsEnumerable();
                uint address = 0;

                while (true)
                {
                    var chunk = rest.Take(dataframeSize).ToArray();
                    rest = rest.Skip(dataframeSize);
                    if (!chunk.Any()) break;

                    var buffer = new byte[4 * 4 + chunk.Length];
                    using (var stream = new MemoryStream(buffer))
                    using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                    {
                        bw.Write((ushort)FlashModeResponse.Operation.ISP_FLASH_WRITE);
                        bw.Write((ushort)0x00);
                        bw.Write((uint)0);  // checksum
                        bw.Write(address);
                        bw.Write((uint)chunk.Length);
                        bw.Write(chunk);

                        bw.Flush();
                        var checksum = Crc32Algorithm.Compute(buffer, 4 * 2, buffer.Length - 4 * 2);
                        bw.Seek(4, SeekOrigin.Begin);
                        bw.Write(checksum);
                    }

                    while (true)
                    {
                        Write(buffer);
                        var result = FlashModeResponse.Parse(ReceiveOnReturn());
                        if (result.errorCode == FlashModeResponse.ErrorCode.ISP_RET_OK)
                            break;
                    }

                    address += dataframeSize;
                }
            }

            private void FlashDataFrame(byte[] data, uint address)
            {
                const int dataframeSize = 1024;

                var rest = data.AsEnumerable();

                while (true)
                {
                    var chunk = rest.Take(dataframeSize).ToArray();
                    rest = rest.Skip(dataframeSize);
                    if (!chunk.Any()) break;

                    var buffer = new byte[4 * 4 + chunk.Length];
                    using (var stream = new MemoryStream(buffer))
                    using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                    {
                        bw.Write((ushort)ISPResponse.Operation.ISP_MEMORY_WRITE);
                        bw.Write((ushort)0x00);
                        bw.Write((uint)0);  // checksum
                        bw.Write(address);
                        bw.Write((uint)chunk.Length);
                        bw.Write(chunk);

                        bw.Flush();
                        var checksum = Crc32Algorithm.Compute(buffer, 4 * 2, buffer.Length - 4 * 2);
                        bw.Seek(4, SeekOrigin.Begin);
                        bw.Write(checksum);
                    }

                    while (true)
                    {
                        Write(buffer);
                        var result = ISPResponse.Parse(ReceiveOnReturn());
                        if (result.errorCode == ISPResponse.ErrorCode.ISP_RET_OK)
                            break;
                    }

                    address += (uint)chunk.Length;
                }
            }

            private byte[] ReceiveOnReturn()
            {
                using (var stream = new MemoryStream())
                {
                    while (_port.ReadByte() != 0xc0) ;

                    bool escapeNext = false;
                    while (true)
                    {
                        var data = _port.ReadByte();
                        if (data == 0xc0) break;
                        if (data == 0xdb)
                        {
                            escapeNext = true;
                        }
                        else if (escapeNext)
                        {
                            escapeNext = false;
                            if (data == 0xdc)
                                stream.WriteByte(0xc0);
                            else if (data == 0xdd)
                                stream.WriteByte(0xdb);
                            else
                                throw new InvalidDataException($"Invalid SLIP escape: {data:X2}.");
                        }
                        else
                        {
                            stream.WriteByte((byte)data);
                        }
                    }

                    return stream.ToArray();
                }
            }

            private void Write(byte[] data)
            {
                IEnumerable<byte> EscapeData()
                {
                    yield return 0xc0;
                    foreach (var b in data)
                    {
                        if (b == 0xdb)
                        {
                            yield return 0xdb;
                            yield return 0xdd;
                        }
                        else if (b == 0xc0)
                        {
                            yield return 0xdb;
                            yield return 0xdc;
                        }
                        else
                        {
                            yield return b;
                        }
                    }

                    yield return 0xc0;
                }

                var buffer = EscapeData().ToArray();
                _port.Write(buffer, 0, buffer.Length);
            }

            #region IDisposable Support
            private bool _disposedValue = false; // 要检测冗余调用

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposedValue)
                {
                    if (disposing)
                    {
                        _port.Dispose();
                    }

                    _disposedValue = true;
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
            #endregion
        }

        class ISPResponse
        {
            public enum Operation
            {
                ISP_ECHO = 0xC1,
                ISP_NOP = 0xC2,
                ISP_MEMORY_WRITE = 0xC3,
                ISP_MEMORY_READ = 0xC4,
                ISP_MEMORY_BOOT = 0xC5,
                ISP_DEBUG_INFO = 0xD1
            }

            public enum ErrorCode
            {
                ISP_RET_DEFAULT = 0x00,
                ISP_RET_OK = 0xE0,
                ISP_RET_BAD_DATA_LEN = 0xE1,
                ISP_RET_BAD_DATA_CHECKSUM = 0xE2,
                ISP_RET_INVALID_COMMAND = 0xE3
            }

            public static (Operation op, ErrorCode errorCode) Parse(ReadOnlySpan<byte> rawData)
            {
                return ((Operation)rawData[0], (ErrorCode)rawData[1]);
            }
        }

        class FlashModeResponse
        {
            public enum Operation
            {
                ISP_DEBUG_INFO = 0xD1,
                ISP_NOP = 0xD2,
                ISP_FLASH_ERASE = 0xD3,
                ISP_FLASH_WRITE = 0xD4,
                ISP_REBOOT = 0xD5,
                ISP_UARTHS_BAUDRATE_SET = 0xD6,
                FLASHMODE_FLASH_INIT = 0xD7
            }

            public enum ErrorCode
            {
                ISP_RET_DEFAULT = 0x00,
                ISP_RET_OK = 0xE0,
                ISP_RET_BAD_DATA_LEN = 0xE1,
                ISP_RET_BAD_DATA_CHECKSUM = 0xE2,
                ISP_RET_INVALID_COMMAND = 0xE3
            }

            public static (Operation op, ErrorCode errorCode) Parse(ReadOnlySpan<byte> rawData)
            {
                return ((Operation)rawData[0], (ErrorCode)rawData[1]);
            }
        }
    }
}
