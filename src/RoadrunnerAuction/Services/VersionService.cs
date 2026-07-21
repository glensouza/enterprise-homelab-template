using System.Reflection;

namespace RoadrunnerAuction.Services;

public class VersionService
{
    public string GetVersion()
    {
        var attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        string infoVersion = attribute?.InformationalVersion ?? "1.0.0";
        int plusIndex = infoVersion.IndexOf('+');
        string semVer = plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;

        return $"v{semVer}";
    }
}
