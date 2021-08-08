using DeviceId;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FileAccessTracker
{
    public sealed class FileAccessTrackerService : BackgroundService
    {
        public FileAccessTrackerService(ILogger<FileAccessTrackerService> logger)
        {
            _logger = logger;

            _traceEventSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName, TraceEventSessionOptions.NoRestartOnCreate)
            {
                BufferSizeMB = 128,
            };

            _traceEventSession.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskFileIO |
                    KernelTraceEventParser.Keywords.FileIOInit);
            RegisterCallbacks();
            

            var configuration = new TelemetryConfiguration
            {
                ConnectionString = "InstrumentationKey=21a9798a-d074-4683-ba7b-d9b2d9ecf2c7;IngestionEndpoint=https://francecentral-1.in.applicationinsights.azure.com/"
            };
            _telemetryClient = new TelemetryClient(configuration);

            _localServiceFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileAccessTrackerService");
            Directory.CreateDirectory(_localServiceFolderPath);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _etwProcessingTask = Task.Run(() => _traceEventSession.Source.Process());
            _logger.LogCritical($"DeviceId: {_deviceId}");

            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _traceEventSession.Stop();
            await _etwProcessingTask;

            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var snapshotTask = Task.Run(CreateSnapshotIfNeeded, stoppingToken).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogError(task.Exception, "Snapshot task has failed");
                    _telemetryClient.TrackException(task.Exception);
                }
            });

            while (!stoppingToken.IsCancellationRequested)
            {
                try {
                    if (_eventQueue.TryTake(out var fileAccessTelemetry, Timeout.Infinite, stoppingToken))
                    {
                        _logger.LogDebug(fileAccessTelemetry.ToString());
                        SendTelemetryEvent("FileAccess", fileAccessTelemetry.ToDictionary());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void RegisterCallbacks()
        {
            _traceEventSession.Source.Kernel.FileIODelete += HandleFileETW;
            _traceEventSession.Source.Kernel.FileIOFlush += HandleFileETW;
            _traceEventSession.Source.Kernel.FileIORename += HandleFileETW;
        }

        private void HandleFileETW(TraceEvent traceEvent)
        {
            string? filePath = null;
            switch (traceEvent)
            {
                case FileIOInfoTraceData fileIOInfoTraceData:
                    filePath = fileIOInfoTraceData.FileName;
                    break;
                case FileIOCreateTraceData fileIOCreateTraceData:
                    filePath = fileIOCreateTraceData.FileName;
                    break;
                case FileIOSimpleOpTraceData fileIOSimpleOpTraceData:
                    filePath = fileIOSimpleOpTraceData.FileName;
                    break;
                case FileIOReadWriteTraceData fileIOReadWriteTraceData:
                    filePath = fileIOReadWriteTraceData.FileName;
                    break;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                _eventQueue.Add(new FileAccessTelemetry(filePath, traceEvent.ProcessName, traceEvent.EventName, traceEvent.TimeStamp));
            }
        }

        private void CreateSnapshotIfNeeded()
        {
            var snapshotFile = Path.Combine(_localServiceFolderPath, $"snapshot-{_build_version}");
            if (File.Exists(snapshotFile))
            {
                return;
            }

            var timestamp = DateTime.UtcNow;
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };

            foreach (var driveInfo in DriveInfo.GetDrives())
            {
                foreach (var file in Directory.EnumerateFiles(driveInfo.Name, "*.*", enumerationOptions))
                {
                    var snapshotTelemetry = new FileInfoTelemetry(file).ToDictionary();
                    snapshotTelemetry.Add("snapshotTimestamp", timestamp.ToString());
                    SendTelemetryEvent("Snapshot", snapshotTelemetry);
                }
            }

            File.Create(snapshotFile).Dispose();
        }

        private void SendTelemetryEvent(string eventName, IDictionary<string, string> telemetryData)
        {
            var eventTelemetry = new EventTelemetry(eventName);
            telemetryData.ToList().ForEach(kvp => eventTelemetry.Properties.Add(kvp));
            eventTelemetry.Context.User.Id = _deviceId;
            eventTelemetry.Context.Component.Version = _build_version;
            RemovePII(eventTelemetry);
            _telemetryClient.TrackEvent(eventTelemetry);
        }

        private EventTelemetry RemovePII(EventTelemetry eventTelemetry)
        {
            eventTelemetry.Context.Cloud.RoleInstance = "<scrubbed>";
            eventTelemetry.Context.Cloud.RoleName = "<scrubbed>";
            eventTelemetry.Context.Location.Ip = "0.0.0.0";
            return eventTelemetry;
        }

        private readonly ILogger<FileAccessTrackerService> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _localServiceFolderPath;
        private readonly BlockingCollection<FileAccessTelemetry> _eventQueue = new BlockingCollection<FileAccessTelemetry>();
        private readonly TraceEventSession _traceEventSession;
        private Task _etwProcessingTask;
        private static readonly string _build_version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "no-build-version";
        private static readonly string _deviceId = new DeviceIdBuilder().AddSystemUUID().AddBuildVersion(_build_version).ToString();
    }
}