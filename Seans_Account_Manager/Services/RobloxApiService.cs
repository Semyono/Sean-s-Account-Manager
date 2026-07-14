using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Seans_Account_Manager.Models;

namespace Seans_Account_Manager.Services;

public class RobloxAuthResult
{
    public bool Success { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class RobloxApiService
{
    private static long _launchSequence;

    private static HttpClient CreateClient(string cookie)
    {
        var handler = new HttpClientHandler { UseCookies = false };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("Cookie", $".ROBLOSECURITY={cookie}");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 SeansAccountManager");
        return client;
    }

    public async Task<RobloxAuthResult> GetAuthenticatedUserAsync(string cookie)
    {
        try
        {
            using var client = CreateClient(cookie);
            var response = await client.GetAsync("https://users.roblox.com/v1/users/authenticated");
            if (!response.IsSuccessStatusCode)
                return new RobloxAuthResult { Success = false };

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new RobloxAuthResult
            {
                Success = true,
                UserId = root.GetProperty("id").GetInt64(),
                Username = root.GetProperty("name").GetString() ?? string.Empty,
                DisplayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? string.Empty : string.Empty
            };
        }
        catch
        {
            return new RobloxAuthResult { Success = false };
        }
    }

    public async Task<long> GetRobuxAsync(string cookie)
    {
        try
        {
            using var client = CreateClient(cookie);
            var response = await client.GetAsync("https://economy.roblox.com/v1/user/currency");
            if (!response.IsSuccessStatusCode) return 0;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("robux").GetInt64();
        }
        catch
        {
            return 0;
        }
    }

    public async Task<string> GetAvatarThumbnailAsync(long userId)
    {
        try
        {
            using var client = new HttpClient();
            var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return string.Empty;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return string.Empty;
            return data[0].GetProperty("imageUrl").GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<string> GetPresenceAsync(string cookie, long userId)
    {
        try
        {
            using var client = CreateClient(cookie);
            var payload = JsonSerializer.Serialize(new { userIds = new[] { userId } });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://presence.roblox.com/v1/presence/users", content);
            if (!response.IsSuccessStatusCode) return "Offline";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.GetProperty("userPresences");
            if (arr.GetArrayLength() == 0) return "Offline";

            int presenceType = arr[0].GetProperty("userPresenceType").GetInt32();
            return presenceType switch
            {
                0 => "Offline",
                1 => "Online",
                2 => "In Game",
                3 => "In Studio",
                _ => "Offline"
            };
        }
        catch
        {
            return "Offline";
        }
    }

    private async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.PostAsync("https://auth.roblox.com/v1/authentication-ticket", new StringContent(""));
        if (response.Headers.TryGetValues("x-csrf-token", out var values))
            return values.First();
        return string.Empty;
    }

    public async Task<string> GetAuthTicketAsync(string cookie)
    {
        try
        {
            using var client = CreateClient(cookie);
            string csrf = await GetCsrfTokenAsync(client);
            if (string.IsNullOrEmpty(csrf)) return string.Empty;

            client.DefaultRequestHeaders.Add("x-csrf-token", csrf);
            client.DefaultRequestHeaders.Referrer = new Uri("https://www.roblox.com/");

            var response = await client.PostAsync("https://auth.roblox.com/v1/authentication-ticket", new StringContent(""));
            if (!response.IsSuccessStatusCode) return string.Empty;

            if (response.Headers.TryGetValues("rbx-authentication-ticket", out var ticketValues))
                return ticketValues.First();
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void LaunchGame(string authTicket, long placeId, string? jobId = null, string? accessCode = null)
    {
        string joinScript;
        if (!string.IsNullOrEmpty(accessCode))
        {
            joinScript = $"+launchmode:play+gameinfo:{authTicket}+launchtime:{DateTimeOffset.Now.ToUnixTimeMilliseconds()}" +
                         $"+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestPrivateGame%26placeId%3D{placeId}%26accessCode%3D{accessCode}" +
                         "+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us+channel:";
        }
        else if (!string.IsNullOrEmpty(jobId))
        {
            joinScript = $"+launchmode:play+gameinfo:{authTicket}+launchtime:{DateTimeOffset.Now.ToUnixTimeMilliseconds()}" +
                         $"+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGameJob%26placeId%3D{placeId}%26gameId%3D{jobId}" +
                         "+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us+channel:";
        }
        else
        {
            joinScript = $"+launchmode:play+gameinfo:{authTicket}+launchtime:{DateTimeOffset.Now.ToUnixTimeMilliseconds()}" +
                         $"+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGame%26placeId%3D{placeId}" +
                         "+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us+channel:";
        }

        long sequence = Interlocked.Increment(ref _launchSequence);
        string uri = "roblox-player:1" + joinScript + $"+launchIdentifier:{sequence}";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    public async Task<long> GetUniverseIdAsync(long placeId)
    {
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
            if (!response.IsSuccessStatusCode) return 0;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("universeId").GetInt64();
        }
        catch
        {
            return 0;
        }
    }

    public async Task<string> GetGameNameAsync(long universeId)
    {
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"https://games.roblox.com/v1/games?universeIds={universeId}");
            if (!response.IsSuccessStatusCode) return string.Empty;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return string.Empty;
            return data[0].GetProperty("name").GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<string> GetGameIconAsync(long universeId)
    {
        try
        {
            using var client = new HttpClient();
            var url = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=128x128&format=Png&isCircular=false";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return string.Empty;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return string.Empty;
            return data[0].GetProperty("imageUrl").GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}