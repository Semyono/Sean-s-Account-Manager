using System.IO;
using System.Text.Json;
using Seans_Account_Manager.Models;

namespace Seans_Account_Manager.Services;

public class RecentGamesStore
{
    private const int MaxRecent = 10;

    private readonly string _dataFile;
    public List<RecentGame> Games { get; private set; } = new();

    public RecentGamesStore()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SeansAccountManager");
        Directory.CreateDirectory(dataDir);
        _dataFile = Path.Combine(dataDir, "recent_games.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_dataFile))
        {
            Games = new List<RecentGame>();
            return;
        }
        try
        {
            string json = File.ReadAllText(_dataFile);
            Games = JsonSerializer.Deserialize<List<RecentGame>>(json) ?? new List<RecentGame>();
        }
        catch
        {
            Games = new List<RecentGame>();
        }
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_dataFile, JsonSerializer.Serialize(Games, options));
    }

    public void AddOrPromote(RecentGame game)
    {
        // Remove any existing entry with same PlaceId, then push to front
        Games.RemoveAll(g => g.PlaceId == game.PlaceId);
        game.LastPlayed = DateTime.Now;
        Games.Insert(0, game);

        // Cap at MaxRecent
        if (Games.Count > MaxRecent)
            Games = Games.Take(MaxRecent).ToList();

        Save();
    }

    public void Remove(long placeId)
    {
        Games.RemoveAll(g => g.PlaceId == placeId);
        Save();
    }
}