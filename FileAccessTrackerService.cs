using DeviceId;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FileAccessTracker
{
    public sealed class FileAccessTrackerService : BackgroundService
    {
        public FileAccessTrackerService(ILogger<FileAccessTrackerService> logger)
        {
            _logger = logger;
            foreach (var drive in DriveInfo.GetDrives())
            {
                _fileSystemWatchers.Add(CreateFileSystemWatcherForDrive(drive));
            }
            

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
            await base.StartAsync(cancellationToken);

            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.Dispose();
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Task snapshotTask = Task.Run(CreateSnapshotIfNeeded, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try {
                    if (snapshotTask.IsFaulted)
                    {
                        _logger.LogError(snapshotTask.Exception, "Snapshot task has failed");
                        _telemetryClient.TrackException(snapshotTask.Exception);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void HandleFileEvent(object sender, FileSystemEventArgs e, DriveInfo driveInfo)
        {
            if (filterOutRegex.IsMatch(e.FullPath))
            {
                // return;
            }

            var telemetryFileInfo = new TelemetryFileInfo(e.FullPath);

            _telemetryClient.TrackEvent("FileAccess", new Dictionary<string, string>
            {
                {"changeType", e.ChangeType.ToString()},
                {"fileDir", telemetryFileInfo.FileDirectoryScrubbed},
                {"fileNameHashed", telemetryFileInfo.FileNameHashed},
                {"fileExtension", telemetryFileInfo.FileExtension},
                {"fileSize", telemetryFileInfo.FileSize.ToString()},
                {"driveType", driveInfo.DriveType.ToString()},
                {"deviceId", _deviceId},
                {"buildVersion", _build_version}
            });
            var item = $"Timestamp: {DateTime.UtcNow}, ChangeType: {e.ChangeType}, {telemetryFileInfo}, DriveType: {driveInfo.DriveType.ToString()}, DeviceId: {_deviceId}";
            _logger.LogInformation(item);
        }

        private FileSystemWatcher CreateFileSystemWatcherForDrive(DriveInfo driveInfo)
        {
            var fileSystemWatcher = new FileSystemWatcher(driveInfo.Name);
            fileSystemWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            FileSystemEventHandler handleFileEventFunc = (object sender, FileSystemEventArgs e) => HandleFileEvent(sender, e, driveInfo);
            fileSystemWatcher.Deleted += handleFileEventFunc;
            fileSystemWatcher.Changed += handleFileEventFunc;
            fileSystemWatcher.Created += handleFileEventFunc;
            fileSystemWatcher.IncludeSubdirectories = true;
           
            return fileSystemWatcher;
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
                foreach (var file in Directory.EnumerateFiles(@"C:\", "*.*", enumerationOptions))
                {
                    var telemetryFileInfo = new TelemetryFileInfo(file);
                    _telemetryClient.TrackEvent("Snapshot", new Dictionary<string, string>
                        {
                            {"snapshotTimestamp", timestamp.ToString()},
                            {"fileDir", telemetryFileInfo.FileDirectoryScrubbed},
                            {"fileNameHashed", telemetryFileInfo.FileNameHashed},
                            {"fileExtension", telemetryFileInfo.FileExtension},
                            {"fileSize", telemetryFileInfo.FileSize.ToString()},
                            {"driveType", driveInfo.DriveType.ToString()},
                            {"deviceId", _deviceId},
                            {"buildVersion", _build_version}
                        });
                }
            }

            File.Create(snapshotFile).Dispose();
        }

        private readonly ILogger<FileAccessTrackerService> _logger;
        private static Regex filterOutRegex = new Regex(@"(c:\\windows\\.*)|(c:\\users\\[^\\]*\\appdata\\.*)|(c:\\programdata\\.*)|(c:\\program files \(x86\)\\.*)|(c:\\program files\\.*)", RegexOptions.IgnoreCase);
        private readonly IList<FileSystemWatcher> _fileSystemWatchers = new List<FileSystemWatcher>();
        private readonly TelemetryClient _telemetryClient;
        private readonly string _localServiceFolderPath;
        private static readonly string _build_version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "no-build-version";
        private static readonly string _deviceId = new DeviceIdBuilder().AddSystemUUID().AddBuildVersion(_build_version).ToString();
    }
}