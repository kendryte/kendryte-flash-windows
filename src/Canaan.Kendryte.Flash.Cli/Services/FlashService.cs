using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Canaan.Kendryte.Flash.Cli.Services
{
    internal class FlashService
    {
        private readonly Options _options;
        private readonly uint _chip = 1;
        private readonly ProgressIndicator _progressIndicator;

        public FlashService(Options options, ProgressIndicator progressIndicator)
        {
            _options = options;
            _progressIndicator = progressIndicator;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return StartFlash();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StartFlash()
        {
            if (string.IsNullOrEmpty(_options.Firmware))
                throw new InvalidOperationException("Must specify firmware path.");
            var firmwareType = GetFirmwareType(_options.Firmware);
            if (firmwareType == FirmwareType.Unknown)
                throw new InvalidOperationException("Unknown firmware type.");
            if (string.IsNullOrEmpty(_options.Device))
                throw new InvalidOperationException("Must select device.");
            if (_options.BaudRate < 110)
                throw new InvalidOperationException("Invalid baud rate.");

            using (var loader = new KendryteLoader(_options.Device, _options.BaudRate))
            {
                loader.CurrentJobChanged += (s, e) =>
                {
                    _progressIndicator.SetJobItem(loader.CurrentJob, loader.JobItemsStatus[loader.CurrentJob]);
                };

                Console.WriteLine("Flashing...");
                await loader.DetectBoard();
                await loader.InstallFlashBootloader();
                await loader.BootBootloader();
                await loader.FlashGreeting();
                await loader.ChangeBaudRate();
                await loader.InitializeFlash(_chip);

                if (firmwareType == FirmwareType.Single)
                {
                    using (var file = File.OpenRead(_options.Firmware))
                    using (var br = new BinaryReader(file))
                    {
                        await loader.FlashFirmware(0, br.ReadBytes((int)file.Length), true);
                    }
                }
                else if (firmwareType == FirmwareType.FlashList)
                {
                    using (var pkg = new FlashPackage(File.OpenRead(_options.Firmware)))
                    {
                        await pkg.LoadAsync();

                        foreach (var item in pkg.Files)
                        {
                            using (var br = new BinaryReader(item.Bin))
                            {
                                await loader.FlashFirmware(item.Address, br.ReadBytes((int)item.Length), item.SHA256Prefix);
                            }
                        }
                    }
                }

                await loader.Reboot();
            }

            Console.WriteLine(Environment.NewLine + "Flash completed!");
        }

        private FirmwareType GetFirmwareType(string firmware)
        {
            var ext = Path.GetExtension(firmware).ToLowerInvariant();

            switch (ext)
            {
                case ".bin":
                    return FirmwareType.Single;
                case ".kfpkg":
                    return FirmwareType.FlashList;
                default:
                    return FirmwareType.Unknown;
            }
        }

        private enum FirmwareType
        {
            Single,
            FlashList,
            Unknown
        }
    }
}
