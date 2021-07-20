using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FileAccessTracker
{
    public class TelemetryFileInfo
    {
        public TelemetryFileInfo(string filePath)
        {
            var fileInfo = new FileInfo(filePath);

            try
            {
                FileSize = fileInfo.Length;
            }
            catch (Exception)
            {
                FileSize = -1;
            }

            FileNameHashed = GetStableHashCode(fileInfo.Name).ToString();
            FileExtension = Path.GetExtension(filePath);
            FileDirectoryScrubbed = GetScrubbedFolderPath(fileInfo.Directory?.ToString() ?? string.Empty);
        }

        public string FileNameHashed { get; }

        public string FileDirectoryScrubbed { get; }

        public string FileExtension { get; }

        public long FileSize { get; }

        public override string ToString()
        {
            return $"FileNameHashed: {FileNameHashed}, FileDirectoryScrubbed: {FileDirectoryScrubbed}, FileExtension: {FileExtension}, FileSize: {FileSize}";
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
    }
}
