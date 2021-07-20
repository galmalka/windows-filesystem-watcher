using DeviceId;

namespace FileAccessTracker
{
    public static class DeviceIdExtensions
    {
        public static DeviceIdBuilder AddBuildVersion(this DeviceIdBuilder deviceIdBuilder, string buildVersion) => deviceIdBuilder.AddComponent(new BuildVersionDeviceIdComponent(buildVersion));

        public class BuildVersionDeviceIdComponent : IDeviceIdComponent
        {
            public BuildVersionDeviceIdComponent(string buildVersion) => _buildVersion = buildVersion;

            public string Name => "Build Version";

            public string GetValue()
            {
                return _buildVersion;
            }

            private readonly string _buildVersion;
        }
    }
}
