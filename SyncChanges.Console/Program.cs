using Mono.Options;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncChanges.Console
{
    class Program
    {
        static readonly Logger Log = LogManager.GetCurrentClassLogger();

        List<string> ConfigFiles = new List<string>();
        bool DryRun = false;
        bool Error = false;
        int Timeout = 0;
        bool Loop = false;
        int Interval = 30;

        static int Main(string[] args)
        {
            try
            {
                System.Console.OutputEncoding = Encoding.UTF8;
                var program = new Program();
                

                if (!File.Exists("config.json"))
                {
                    Log.Error("No config files supplied");
                    return 1;
                }

                program.ConfigFiles.Add("config.json");
                program.Sync();

                return program.Error ? 1 : 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error has occurred");
                return 2;
            }
        }


        void Sync()
        {
            foreach (var configFile in ConfigFiles)
            {
                Config config = null;
                try
                {
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configFile));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error reading configuration file {configFile}");
                    Error = true;
                    continue;
                }

                Loop = config.Loop;
                Interval = config.Interval;
                Timeout = config.Timeout;
                DryRun = config.DryRun;

                try
                {
                    var synchronizer = new Synchronizer(config) { DryRun = DryRun, Timeout = Timeout };
                    if (!Loop)
                    {
                        var success = synchronizer.Sync();
                        Error = Error || !success;
                    }
                    else
                    {
                        synchronizer.Interval = Interval;
                        using var cancellationTokenSource = new CancellationTokenSource();
                        System.Console.CancelKeyPress += (s, e) =>
                        {
                            cancellationTokenSource.Cancel();
                            e.Cancel = true;
                        };
                        synchronizer.SyncLoop(cancellationTokenSource.Token);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error synchronizing databases for configuration {configFile}");
                    Error = true;
                }
            }
        }
    }
}