using System.Reflection;
using System.Runtime.InteropServices;

namespace vali_deploy.Utils;

public static class Util
{
    public static Spectre.Console.Color GetRandomColor()
    {
        var colors = new List<Spectre.Console.Color>
        {
            Spectre.Console.Color.Red,
            Spectre.Console.Color.Green,
            Spectre.Console.Color.Blue,
            Spectre.Console.Color.Yellow,
            Spectre.Console.Color.Purple,
            Spectre.Console.Color.Orange1,
            Spectre.Console.Color.Aquamarine1,
            Spectre.Console.Color.Aquamarine3,
            Spectre.Console.Color.Aquamarine1_1,
            Spectre.Console.Color.Blue3,
            Spectre.Console.Color.Blue3_1,
            Spectre.Console.Color.Chartreuse1,
            Spectre.Console.Color.Chartreuse2,
            Spectre.Console.Color.Chartreuse3,
            Spectre.Console.Color.Grey0,
            Spectre.Console.Color.Grey3,
            Spectre.Console.Color.Grey7,
            Spectre.Console.Color.Grey11,
            Spectre.Console.Color.Grey15,
            Spectre.Console.Color.Grey19,
            Spectre.Console.Color.Grey100,
            Spectre.Console.Color.Gold1,
            Spectre.Console.Color.Gold3,
            Spectre.Console.Color.Gold3_1,
            Spectre.Console.Color.Fuchsia,
            Spectre.Console.Color.Honeydew2,
            Spectre.Console.Color.Khaki1,
            Spectre.Console.Color.Khaki3,
            Spectre.Console.Color.HotPink,
            Spectre.Console.Color.HotPink_1,
            Spectre.Console.Color.Navy,
            Spectre.Console.Color.Magenta1,
            Spectre.Console.Color.DarkMagenta_1,
            Spectre.Console.Color.Olive,
            Spectre.Console.Color.Tan,
            Spectre.Console.Color.Plum1,
            Spectre.Console.Color.Plum2,
            Spectre.Console.Color.Plum3
        };

        var random = new Random();
        return colors[random.Next(colors.Count)];
    }

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