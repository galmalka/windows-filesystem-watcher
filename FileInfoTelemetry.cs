using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FileAccessTracker
{
    public class FileInfoTelemetry
    {
        public FileInfoTelemetry(string filePath)
        {
            _fileInfo = new Lazy<FileInfo?>(() => {
                try
                {
                    return new FileInfo(filePath);
                }
                catch
                {
                    return null;
                }
            });

            _length = new Lazy<long>(() =>
            {
                try
                {
                    return FileInfo?.Length ?? -1;
                }
                catch (Exception)
                {
                    return -1;
                }
            });

            _hashedName = new Lazy<string>(() => GetSHA256Hash(FileInfo?.Name?? Path.GetFileName(filePath)).ToString());
            FileExtension = Path.GetExtension(filePath);
            _scrubbedDirectoryName = new Lazy<string>(() => GetScrubbedFolderPath(FileInfo?.Directory?.ToString() ?? string.Empty));
            _driveInfo = new Lazy<DriveInfo>(() => new DriveInfo(Path.GetPathRoot(filePath)));
        }

        public string FileNameHashed => _hashedName.Value;

        public string FileDirectoryScrubbed => _scrubbedDirectoryName.Value;

        public string FileExtension { get; }

        public long FileSize => _length.Value;

        public DriveType DriveType => _driveInfo.Value.DriveType;

        public override string ToString()
        {
            return $"FileNameHashed: {FileNameHashed}, FileDirectoryScrubbed: {FileDirectoryScrubbed}, FileExtension: {FileExtension}, FileSize: {FileSize}, DriveInfo: {DriveType}";
        }

        public IDictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>()
            {
                { "fileNameHashed", FileNameHashed },
                { "fileDirectoryScrubbed", FileDirectoryScrubbed },
                { "fileExtension", FileExtension },
                { "fileSize", FileSize.ToString() },
                { "driveType", DriveType.ToString() }
            };
        }

        private FileInfo? FileInfo => _fileInfo.Value;

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

        private static string GetSHA256Hash(string str)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(str));
                return Convert.ToBase64String(hash);
            }
        }

        private readonly Lazy<FileInfo?> _fileInfo;
        private readonly Lazy<long> _length;
        private readonly Lazy<string> _hashedName;
        private readonly Lazy<string> _scrubbedDirectoryName;
        private readonly Lazy<DriveInfo> _driveInfo;
    }
}
