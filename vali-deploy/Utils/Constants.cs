namespace vali_deploy.Utils;

public static class Constants
{
    public const string ArchitectureWinX64 = "win-x64";
    public const string ArchitectureOsxX64 = "osx-x64";
    public const string ArchitectureOsxArmx64 = "osx-arm64";
    public const string ArchitectureLinuxX64 = "linux-x64";
    public const string UrlVersion = "https://api.github.com/repos/UBF21/Vali-deploy/releases/latest";

    public static string DefaultLogsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "vali-deploy", "logs");
}
