namespace Seans_Account_Manager.Models;

public class AutoRejoinConfig
{
    public long UserId { get; set; }
    public long PlaceId { get; set; }
    public string? JobId { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartedAt { get; set; }
    public int RejoinCount { get; set; }
}