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
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Force.Crc32;

namespace Canaan.Kendryte.Flash
{
    public enum JobItemType
    {
        DetectBoard,
        BootToISPMode,
        Greeting,
        InstallFlashBootloader,
        FlashGreeting,
        ChangeBaudRate,
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

    public class JobItemStatus : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

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

        private bool Set<T>(ref T property, T value, [CallerMemberName]string propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(property, value))
            {
                property = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                return true;
            }

            return false;
        }
    }

    public enum Board
    {
        KD233,
        Generic,
        Unknown
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
        private readonly int _baudRate;
        private Board _board;
        private readonly SynchronizationContext _synchronizationContext;

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
            _baudRate = baudRate;
            _synchronizationContext = SynchronizationContext.Current;
            JobItemsStatus = (from e in (JobItemType[])Enum.GetValues(typeof(JobItemType))
                              select new
                              {
                                  Key = e,
                                  Value = new JobItemStatus()
                              }).ToDictionary(o => o.Key, o => o.Value);

            _port = new SerialPort(device, 115200, Parity.None, 8, StopBits.One)
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
                if (_board == Board.KD233)
                {
                    await BootToISPModeForBoard1();
                }
                else if (_board == Board.Generic)
                {
                    await BootToISPModeForBoard2();
                }
                else
                {
                    throw new NotSupportedException("Unable to enter ISP mode.");
                }
            });
        }

        private async Task BootToISPModeForBoard1()
        {
            _port.DtrEnable = true;
            _port.RtsEnable = true;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            _port.DtrEnable = false;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        private async Task BootToISPModeForBoard2()
        {
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            _port.DtrEnable = false;
            _port.RtsEnable = true;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            _port.RtsEnable = false;
            _port.DtrEnable = true;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
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

        public async Task DetectBoard()
        {
            foreach (var board in (Board[])Enum.GetValues(typeof(Board)))
            {
                if (await DetectBoard(board)) break;
            }
        }

        private async Task<bool> DetectBoard(Board board)
        {
            _board = board;

            var status = JobItemsStatus[JobItemType.DetectBoard];
            CurrentJob = JobItemType.DetectBoard;
            try
            {
                await BootToISPMode();
                await Greeting();
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public async Task InstallFlashBootloader()
        {
            var status = JobItemsStatus[JobItemType.InstallFlashBootloader];
            await DoJob(status, () =>
            {
                return Task.Run(async () =>
                {
                    byte[] bootloader;
                    using (var bootloaderStream = typeof(KendryteLoader).Assembly.GetManifestResourceStream("Canaan.Kendryte.Flash.Resources.isp_flash.bin"))
                    {
                        bootloader = new byte[bootloaderStream.Length];
                        bootloaderStream.Read(bootloader, 0, bootloader.Length);
                    }

                    const int dataframeSize = 1024;

                    uint totalWritten = 0;
                    var buffer = new byte[4 * 4 + dataframeSize];
                    uint address = 0x80000000;

                    foreach (var chunk in SplitToChunks(bootloader, dataframeSize))
                    {
                        SendPacket(buffer, (ushort)ISPResponse.Operation.ISP_MEMORY_WRITE, address, payload: chunk, shouldRetry: () =>
                        {
                            var result = ISPResponse.Parse(ReceiveOnReturn());
                            return !CheckResponse(result.errorCode);
                        });

                        address += (uint)chunk.Count;
                        totalWritten += (uint)chunk.Count;
                        await ExecuteOnUIAsync(() => status.Progress = (float)totalWritten / bootloader.Length);
                    }
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
                    SendPacket(buffer, (ushort)ISPResponse.Operation.ISP_MEMORY_BOOT, 0x80000000);
                    await ExecuteOnUIAsync(() => status.Progress = 0.5f);
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
                    if (!CheckResponse(resp.errorCode))
                        throw new InvalidOperationException("Error in flash greeting.");
                });
            });
        }

        public async Task ChangeBaudRate()
        {
            var status = JobItemsStatus[JobItemType.ChangeBaudRate];
            CurrentJob = JobItemType.ChangeBaudRate;
            await DoJob(status, () =>
            {
                return Task.Run(async () =>
                {
                    var buffer = new byte[4 * 5];
                    var payload = new ArraySegment<byte>(BitConverter.GetBytes(_baudRate));
                    SendPacket(buffer, (ushort)FlashModeResponse.Operation.ISP_UARTHS_BAUDRATE_SET, 0, payload: payload);

                    _port.Close();
                    await Task.Delay(50);
                    _port.BaudRate = _baudRate;
                    _port.Open();
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
                    SendPacket(buffer, (ushort)FlashModeResponse.Operation.FLASHMODE_FLASH_INIT, chip, () =>
                    {
                        var resp = FlashModeResponse.Parse(ReceiveOnReturn());
                        if (!CheckResponse(resp.errorCode))
                            throw new InvalidOperationException("Error in flash initializing.");
                        return false;
                    });
                });
            });
        }

        public async Task FlashFirmware(uint address, byte[] data, bool sha256Prefix)
        {
            var status = JobItemsStatus[JobItemType.FlashFirmware];
            CurrentJob = JobItemType.FlashFirmware;
            await DoJob(status, () =>
            {
                return Task.Run(async () =>
                {
                    byte[] dataPack;
                    if (sha256Prefix)
                    {
                        dataPack = new byte[1 + 4 + data.Length + 32];
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
                    }
                    else
                    {
                        dataPack = data;
                    }

                    const int dataframeSize = 4096;

                    uint totalWritten = 0;
                    var buffer = new byte[4 * 4 + dataframeSize];

                    foreach (var chunk in SplitToChunks(dataPack, dataframeSize))
                    {
                        SendPacket(buffer, (ushort)FlashModeResponse.Operation.ISP_FLASH_WRITE, address, payload: chunk, shouldRetry: () =>
                        {
                            var result = FlashModeResponse.Parse(ReceiveOnReturn());
                            return !CheckResponse(result.errorCode);
                        });

                        address += dataframeSize;
                        totalWritten += (uint)chunk.Count;
                        await ExecuteOnUIAsync(() => status.Progress = (float)totalWritten / dataPack.Length);
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
                await DoJob(status, async () =>
                {
                    if (_board == Board.KD233)
                    {
                        await RebootForBoard1();
                    }
                    else if (_board == Board.Generic)
                    {
                        await RebootForBoard2();
                    }
                });
            });
        }

        public async Task RebootForBoard1()
        {
            _port.RtsEnable = false;
            _port.DtrEnable = true;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            _port.DtrEnable = false;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        public async Task RebootForBoard2()
        {
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            _port.DtrEnable = false;
            _port.RtsEnable = true;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
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

        private bool CheckResponse(ISPResponse.ErrorCode errorCode)
        {
            return errorCode == ISPResponse.ErrorCode.ISP_RET_OK || errorCode == ISPResponse.ErrorCode.ISP_RET_DEFAULT;
        }

        private bool CheckResponse(FlashModeResponse.ErrorCode errorCode)
        {
            return errorCode == FlashModeResponse.ErrorCode.ISP_RET_OK || errorCode == FlashModeResponse.ErrorCode.ISP_RET_DEFAULT;
        }

        private void SendPacket(byte[] buffer, ushort operation, uint address, Func<bool> shouldRetry = null, ArraySegment<byte>? payload = null)
        {
            int toWrite = 4 * 4;
            using (var stream = new MemoryStream(buffer))
            using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(operation);
                bw.Write((ushort)0x00);
                bw.Write((uint)0);  // checksum
                bw.Write(address);

                if (payload is ArraySegment<byte> realPayload)
                {
                    bw.Write((uint)realPayload.Count);
                    bw.Write(realPayload.Array, realPayload.Offset, realPayload.Count);
                    toWrite += realPayload.Count;
                }
                else
                {
                    bw.Write(0U);
                }

                bw.Flush();
                var checksum = Crc32Algorithm.Compute(buffer, 4 * 2, toWrite - 4 * 2);
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write(checksum);
            }

            while (true)
            {
                Write(new ReadOnlyMemory<byte>(buffer, 0, toWrite));
                if (shouldRetry == null || !shouldRetry()) break;
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

        private void Write(ReadOnlyMemory<byte> data)
        {
            IEnumerable<byte> EscapeData()
            {
                yield return 0xc0;
                for (int i = 0; i < data.Length; i++)
                {
                    var b = data.Span[i];
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

        private IEnumerable<ArraySegment<byte>> SplitToChunks(byte[] data, int chunkSize)
        {
            int start = 0;
            while (true)
            {
                var count = Math.Min(data.Length - start, chunkSize);
                if (count == 0)
                    yield break;
                yield return new ArraySegment<byte>(data, start, count);
                start += count;
            }
        }

        private Task ExecuteOnUIAsync(Action action)
        {
            if (_synchronizationContext != null)
            {
                var tcs = new TaskCompletionSource<object>();
                _synchronizationContext.Send(o =>
                {
                    try
                    {
                        action();
                        tcs.SetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, null);
                return tcs.Task;
            }
            else
            {
                action();
                return Task.CompletedTask;
            }
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
