using System.IO;
using System.Text.Json;
using Seans_Account_Manager.Models;

namespace Seans_Account_Manager.Services;

public class AppSettingsStore
{
    private readonly string _settingsFile;

    public AppSettingsStore()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SeansAccountManager");
        Directory.CreateDirectory(dataDir);
        _settingsFile = Path.Combine(dataDir, "app_settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFile))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(_settingsFile, json);
    }
}