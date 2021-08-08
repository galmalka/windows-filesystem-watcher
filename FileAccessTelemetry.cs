using System;
using System.Collections.Generic;
using System.Linq;

namespace FileAccessTracker
{
    public class FileAccessTelemetry
    {
        public FileAccessTelemetry(string filePath, string processName, string accessType, DateTime timestamp)
        {
            FileInfoTelemetry = new FileInfoTelemetry(filePath);
            ProcessName = processName;
            AccessType = accessType;
            Timestamp = timestamp;
        }

        public FileInfoTelemetry FileInfoTelemetry { get; }

        public string ProcessName { get; }

        public string AccessType { get; }

        public DateTime Timestamp { get; }

        public override string ToString()
        {
            return $"FileInfoTelemetry: {FileInfoTelemetry}, ProcessName: {ProcessName}, AccessType: {AccessType}, Timestamp: {Timestamp}";
        }

        public IDictionary<string, string> ToDictionary()
        {
            var dictionary = new Dictionary<string, string>()
            {
                { "processName", ProcessName },
                { "accessType", AccessType },
                { "timestamp", Timestamp.ToString() }
            };

            FileInfoTelemetry.ToDictionary().ToList().ForEach(x => dictionary.Add(x.Key, x.Value));
            return dictionary;
        }
    }
}
