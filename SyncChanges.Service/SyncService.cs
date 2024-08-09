﻿using System;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace DbSync.Service
{
    public partial class SyncChanges : ServiceBase
    {
        static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private Synchronizer Synchronizer;
        private CancellationTokenSource CancellationTokenSource;
        private Task SyncTask;

        public SyncChanges()
        {
            InitializeComponent();
        }

        protected override async void OnStart(string[] args)
        {
            Config config = null;

            try
            {
                var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(path, "config.json")));
            }
            catch (Exception ex)
            {
                ExitCode = 1064;
                Log.Error(ex, $"Error reading configuration file config.json");
                throw;
            }

            try
            {
                var timeout = config.Timeout;
                var interval = config.Interval;
                var dryRun = config.DryRun;

                CancellationTokenSource = new();
                Synchronizer = new Synchronizer(config) { Timeout = timeout, Interval = interval, DryRun = dryRun };
                SyncTask = Task.Factory.StartNew(() => Synchronizer.SyncLoop(CancellationTokenSource.Token), TaskCreationOptions.LongRunning);
                await SyncTask;
            }
            catch (Exception ex)
            {
                ExitCode = 1064;
                Log.Error(ex, $"Error synchronizing databases");
                throw;
            }
        }

        protected override void OnStop()
        {
            CancellationTokenSource.Cancel();
            SyncTask.Wait();
        }
    }
}
