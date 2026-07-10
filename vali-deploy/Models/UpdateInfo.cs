namespace vali_deploy.Models;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public Dictionary<string, string?> Downloads { get; set; } = new();
    public string ReleaseDate { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public Dictionary<string, string?> Checksums { get; set; } = new();
}
