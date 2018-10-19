using System;
using System.Threading;
using System.Threading.Tasks;
using Canaan.Kendryte.Flash.Cli.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Canaan.Kendryte.Flash.Cli
{
    public class Options
    {
        [Option('d', "device", Required = true, HelpText = "Set the serial device to communicate with Kendryte.")]
        public string Device { get; set; }

        [Option('b', "baudrate", Required = false, HelpText = "Set the serial device to communicate with Kendryte.", Default = 2000000)]
        public int BaudRate { get; set; }

        [Value(0, MetaName = "firmware", HelpText = "Firmware path")]
        public string Firmware { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            FlashService flashService = null;

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    var serviceProvider = new ServiceCollection()
                        .AddSingleton<FlashService>()
                        .AddSingleton<ProgressIndicator>()
                        .AddSingleton(options)
                        .BuildServiceProvider();

                    flashService = serviceProvider.GetRequiredService<FlashService>();
                });

            if (flashService != null)
            {
                await flashService.StartAsync(default(CancellationToken));
            }
        }
    }
}
