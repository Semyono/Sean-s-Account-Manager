namespace Seans_Account_Manager.Models;

public class AppSettings
{
    public bool AlwaysOnTop { get; set; }
    public bool ConfirmBeforeLaunch { get; set; } = true;

    public string RobloxLauncher { get; set; } = "default";
    public bool ForceFramerateCap { get; set; }
    public int FramerateCapValue { get; set; } = 60;
    public bool BoostRobloxRam { get; set; }
    public int RamLimitMb { get; set; } = 750;
    public Dictionary<string, string> CustomLauncherPaths { get; set; } = new();

    public string LastPlaceId { get; set; } = "";
    public string LastJobId { get; set; } = "";
    public string LastPrivateServerLink { get; set; } = "";
}