using DeviceId;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
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
            while (!stoppingToken.IsCancellationRequested)
            {
                try {
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
                //return;
            }

            long fileSize;
            try
            {
                fileSize = new FileInfo(e.FullPath).Length;
            }
            catch (FileNotFoundException)
            {
                // probably a temp file
                fileSize = -1;
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                fileSize = -1;
            }

            _telemetryClient.TrackEvent("FileAccess", new Dictionary<string, string>
            {
                {"ChangeType", e.ChangeType.ToString()},
                {"FilePath", e.FullPath},
                {"FileSize", fileSize.ToString()},
                {"DriveType", driveInfo.DriveType.ToString() },
                {"DeviceId", _deviceId }
            });
            var item = $"{DateTime.UtcNow}, {e.ChangeType}, {e.FullPath}, {fileSize}";
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

        private readonly ILogger<FileAccessTrackerService> _logger;
        private static Regex filterOutRegex = new Regex(@"(c:\\windows\\.*)|(c:\\users\\[^\\]*\\appdata\\.*)|(c:\\programdata\\.*)|(c:\\program files \(x86\)\\.*)|(c:\\program files\\.*)", RegexOptions.IgnoreCase);
        private readonly IList<FileSystemWatcher> _fileSystemWatchers = new List<FileSystemWatcher>();
        private readonly TelemetryClient _telemetryClient;
        private readonly string _deviceId = new DeviceIdBuilder().AddSystemUUID().ToString();
    }
}