using System.Reflection;
using System.Runtime.InteropServices;

namespace vali_deploy.Utils;

public static class Util
{
    public static string GetOsIdentifier()
    {
        if (OperatingSystem.IsWindows()) return Constants.ArchitectureWinX64;
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Constants.ArchitectureOsxArmx64
                : Constants.ArchitectureOsxX64;
        if (OperatingSystem.IsLinux()) return Constants.ArchitectureLinuxX64;
        throw new PlatformNotSupportedException("Sistema operativo no compatible");
    }

    public static bool IsNewerVersion(string newVersion, string currentVersion)
    {
        return Version.Parse(newVersion).CompareTo(Version.Parse(currentVersion)) > 0;
    }

    public static string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }
}