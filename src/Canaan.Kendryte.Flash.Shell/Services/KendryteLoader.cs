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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Force.Crc32;

namespace Canaan.Kendryte.Flash.Shell.Services
{
    public enum JobItemType
    {
        BootToISPMode,
        Greeting,
        InstallFlashBootloader,
        FlashGreeting,
        InitializeFlash,
        FlashFirmware,
        Reboot
    }

    public enum JobItemRunningStatus
    {
        NotStarted,
        Running,
        Finished,
        Error
    }

    public class JobItemStatus : PropertyChangedBase
    {
        private JobItemRunningStatus _runningStatus;

        public JobItemRunningStatus RunningStatus
        {
            get => _runningStatus;
            set => Set(ref _runningStatus, value);
        }

        private float _progress;
        public float Progress
        {
            get => _progress;
            set => Set(ref _progress, value);
        }
    }

    public class KendryteLoader : IDisposable
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

        public Dictionary<JobItemType, JobItemStatus> JobItemsStatus { get; }

        private JobItemType _currentJob;
        public JobItemType CurrentJob
        {
            get => _currentJob;
            set
            {
                _currentJob = value;
                CurrentJobChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler CurrentJobChanged;

        public KendryteLoader(string device, int baudRate)
        {
            JobItemsStatus = (from e in (JobItemType[])Enum.GetValues(typeof(JobItemType))
                              select new
                              {
                                  Key = e,
                                  Value = new JobItemStatus()
                              }).ToDictionary(o => o.Key, o => o.Value);

            _port = new SerialPort(device, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 2000
            };

            _port.Open();
        }

        public async Task BootToISPMode()
        {
            var status = JobItemsStatus[JobItemType.BootToISPMode];
            CurrentJob = JobItemType.BootToISPMode;
            await DoJob(status, async () =>
            {
                _port.DtrEnable = true;
                _port.RtsEnable = true;
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                _port.DtrEnable = false;
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            });
        }

        public async Task Greeting()
        {
            var status = JobItemsStatus[JobItemType.Greeting];
            CurrentJob = JobItemType.Greeting;
            await DoJob(status, () =>
            {
                return Task.Run(() =>
                {
                    _port.Write(_greeting, 0, _greeting.Length);
                    var resp = ISPResponse.Parse(ReceiveOnReturn());
                    if (resp.errorCode != ISPResponse.ErrorCode.ISP_RET_OK)
                        throw new InvalidOperationException("Error in greeting.");
                });
            });
        }

        public async Task InstallFlashBootloader(byte[] bootloader)
        {
            var status = JobItemsStatus[JobItemType.InstallFlashBootloader];
            await DoJob(status, () =>
            {
                return Task.Run(() =>
                {
                    FlashDataFrame(bootloader, 0x80000000, p => Execute.OnUIThread(() => status.Progress = p));
                });
            });
        }

        public async Task BootBootloader()
        {
            var status = JobItemsStatus[JobItemType.InstallFlashBootloader];
            CurrentJob = JobItemType.InstallFlashBootloader;
            await DoJob(status, () =>
            {
                return Task.Run(async () =>
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
                    Execute.OnUIThread(() => status.Progress = 0.5f);
                    await Task.Delay(TimeSpan.FromSeconds(2));
                });
            });
        }

        public async Task FlashGreeting()
        {
            var status = JobItemsStatus[JobItemType.FlashGreeting];
            CurrentJob = JobItemType.FlashGreeting;
            await DoJob(status, () =>
            {
                return Task.Run(() =>
                {
                    _port.Write(_flashGreeting, 0, _flashGreeting.Length);
                    var resp = FlashModeResponse.Parse(ReceiveOnReturn());
                    if (resp.errorCode != FlashModeResponse.ErrorCode.ISP_RET_OK)
                        throw new InvalidOperationException("Error in flash greeting.");
                });
            });
        }

        public async Task InitializeFlash(uint chip)
        {
            var status = JobItemsStatus[JobItemType.InitializeFlash];
            CurrentJob = JobItemType.InitializeFlash;
            await DoJob(status, () =>
            {
                return Task.Run(() =>
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
                });
            });
        }

        public async Task FlashFirmware(byte[] data)
        {
            var status = JobItemsStatus[JobItemType.FlashFirmware];
            CurrentJob = JobItemType.FlashFirmware;
            await DoJob(status, () =>
            {
                return Task.Run(() =>
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
                    uint totalWritten = 0;

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
                        totalWritten += (uint)chunk.Length;
                        Execute.OnUIThread(() => status.Progress = (float)totalWritten / data.Length);
                    }
                });
            });
        }

        public async Task Reboot()
        {
            var status = JobItemsStatus[JobItemType.Reboot];
            CurrentJob = JobItemType.Reboot;
            await DoJob(status, async () =>
            {
                _port.RtsEnable = false;
                _port.DtrEnable = true;
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                _port.DtrEnable = false;
            });
        }

        private async Task DoJob(JobItemStatus status, Func<Task> job)
        {
            try
            {
                status.RunningStatus = JobItemRunningStatus.Running;
                status.Progress = 0;

                await job();

                status.Progress = 1;
                status.RunningStatus = JobItemRunningStatus.Finished;
            }
            catch
            {
                status.RunningStatus = JobItemRunningStatus.Error;
                throw;
            }
        }

        private void FlashDataFrame(byte[] data, uint address, Action<float> progressHandler)
        {
            const int dataframeSize = 1024;

            var rest = data.AsEnumerable();
            uint totalWritten = 0;

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
                totalWritten += (uint)chunk.Length;
                progressHandler((float)totalWritten / data.Length);
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
        private bool _disposedValue = false;

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
}
