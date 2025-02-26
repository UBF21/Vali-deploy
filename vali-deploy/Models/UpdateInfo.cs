namespace vali_deploy.Models;

public class UpdateInfo
{
    public string Version { get; set; }
    public Dictionary<string, string?> Downloads { get; set; }
    public string ReleaseNotes { get; set; }
}