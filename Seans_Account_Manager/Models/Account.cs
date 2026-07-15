namespace Seans_Account_Manager.Models;

public class Account
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EncryptedCookie { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public long Robux { get; set; }
    public string PresenceStatus { get; set; } = "Offline";
    public string Note { get; set; } = string.Empty;
    public bool IsCookieValid { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public int Order { get; set; }
    public bool IsSelected { get; set; }
}

public enum PresenceType
{
    Offline = 0,
    Online = 1,
    InGame = 2,
    InStudio = 3
}
