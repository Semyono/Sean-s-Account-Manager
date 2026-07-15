using System.IO;

namespace Seans_Account_Manager.Services;

public static class RobloxLauncherService
{
    public static readonly Dictionary<string, (string DisplayName, string RelativeExePath)> Launchers = new()
    {
        ["default"] = ("Default (Roblox)", ""),
        ["fishstrap"] = ("Fishstrap", Path.Combine("Fishstrap", "Fishstrap.exe")),
        ["bloxstrap"] = ("Bloxstrap", Path.Combine("Bloxstrap", "Bloxstrap.exe")),
        ["froststrap"] = ("Froststrap", Path.Combine("Froststrap", "Froststrap.exe")),
        ["voidstrap"] = ("Voidstrap", Path.Combine("Voidstrap", "Voidstrap.exe")),
        ["bubblestrap"] = ("Bubblestrap", Path.Combine("Bubblestrap", "Bubblestrap.exe")),
        ["luczystrap"] = ("Luczystrap", Path.Combine("Luczystrap", "Luczystrap.exe")),
    };

    public static string? ResolveExePath(string launcherKey, string? customPath = null)
    {
        if (launcherKey == "default")
            return null;

        if (!Launchers.TryGetValue(launcherKey, out var info))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string standardPath = Path.Combine(localAppData, info.RelativeExePath);
        return File.Exists(standardPath) ? standardPath : string.Empty;
    }

    public static bool IsInstalled(string launcherKey, string? customPath = null)
    {
        if (launcherKey == "default") return true;
        var path = ResolveExePath(launcherKey, customPath);
        return !string.IsNullOrEmpty(path);
    }

    public static string? ResolveDefaultRobloxExePath()
    {
        string versionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "Versions");

        if (!Directory.Exists(versionsDir)) return null;

        return new DirectoryInfo(versionsDir)
            .GetDirectories("version-*")
            .Select(d => Path.Combine(d.FullName, "RobloxPlayerBeta.exe"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}