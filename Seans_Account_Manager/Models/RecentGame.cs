namespace Seans_Account_Manager.Models;

public class RecentGame
{
    public long PlaceId { get; set; }
    public long UniverseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public DateTime LastPlayed { get; set; } = DateTime.Now;
}