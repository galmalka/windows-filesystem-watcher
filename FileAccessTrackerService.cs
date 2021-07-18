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
                return;
            }

            var fullPath = e.FullPath.Replace(@"\\", @"\");
            var fileInfo = new FileInfo(e.FullPath);

            long fileSize;
            try
            {
                fileSize = fileInfo.Length;
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

            var fileNameHashed = GetStableHashCode(fileInfo.Name);
            var fileExtension = Path.GetExtension(e.FullPath);
            var fileDirectoryScrubbed = GetScrubbedFolderPath(fileInfo.Directory?.ToString() ?? string.Empty);

            _telemetryClient.TrackEvent("FileAccess", new Dictionary<string, string>
            {
                {"changeType", e.ChangeType.ToString()},
                {"fileDir", fileDirectoryScrubbed},
                {"fileNameHashed", fileNameHashed.ToString()},
                {"fileExtension", fileExtension},
                {"fileSize", fileSize.ToString()},
                {"driveType", driveInfo.DriveType.ToString() },
                {"deviceId", _deviceId }
            });
            var item = $"{DateTime.UtcNow}, {e.ChangeType}, {fileDirectoryScrubbed}, {fileNameHashed}, {fileExtension}, {fileSize}";
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

        private static string GetScrubbedFolderPath(string folderPath)
        {
            if (!Regex.IsMatch(folderPath, @".:\\users\\.*", RegexOptions.IgnoreCase))
            {
                return folderPath;
            }

            var parts = folderPath.Split(@"\");
            parts[2] = "<scrubbed-user>";
            return string.Join(@"\", parts);
        }

        private static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        private readonly ILogger<FileAccessTrackerService> _logger;
        private static Regex filterOutRegex = new Regex(@"(c:\\windows\\.*)|(c:\\users\\[^\\]*\\appdata\\.*)|(c:\\programdata\\.*)|(c:\\program files \(x86\)\\.*)|(c:\\program files\\.*)", RegexOptions.IgnoreCase);
        private readonly IList<FileSystemWatcher> _fileSystemWatchers = new List<FileSystemWatcher>();
        private readonly TelemetryClient _telemetryClient;
        private const string BUILD_VERSION = "0.0.1"; // TODO: replace with real build version
        private readonly string _deviceId = new DeviceIdBuilder().AddSystemUUID().AddBuildVersion(BUILD_VERSION).ToString();
    }
}