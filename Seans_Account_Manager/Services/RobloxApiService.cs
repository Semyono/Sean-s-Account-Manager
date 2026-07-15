using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Seans_Account_Manager.Models;

namespace Seans_Account_Manager.Services;

public class RobloxAuthResult
{
    public bool Success { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class PrivateServerResult
{
    public bool Success { get; set; }
    public long? PlaceId { get; set; }
    public string? AccessCode { get; set; }
    public string? ErrorMessage { get; set; }
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

    public void LaunchGame(string authTicket, long placeId, string? jobId = null, string? accessCode = null, string? launcherExePath = null)
    {
        long browserTrackerId = Random.Shared.NextInt64(55393295400L, 55393295500L);
        long launchTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        string joinScript =
            $"+launchmode:play+gameinfo:{authTicket}+launchtime:{launchTime}" +
            $"+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGameJob" +
            $"%26browserTrackerId%3D{browserTrackerId}%26placeId%3D{placeId}%26isPlayTogetherGame%3Dfalse";

        if (!string.IsNullOrEmpty(accessCode))
            joinScript += $"%26linkCode%3D{accessCode}";
        else if (!string.IsNullOrEmpty(jobId))
            joinScript += $"%26gameId%3D{jobId}";

        joinScript += $"+browsertrackerid:{browserTrackerId}+robloxLocale:en_us+gameLocale:en_us+channel:";

        long sequence = Interlocked.Increment(ref _launchSequence);
        string uri = "roblox-player:1" + joinScript + $"+launchIdentifier:{sequence}";

        if (!string.IsNullOrEmpty(launcherExePath) && File.Exists(launcherExePath))
        {
            var launcherPsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = launcherExePath,
                Arguments = $"\"{uri}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(launcherPsi);
            return;
        }

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

    public async Task<PrivateServerResult> ResolvePrivateServerAsync(string input, string cookie)
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input))
            return new PrivateServerResult { Success = false, ErrorMessage = "Empty input." };

        var vipMatch = Regex.Match(input,
            @"roblox\.com/games/(\d+)/[^?#]*\?[^#]*privateServerLinkCode=([A-Za-z0-9]+)",
            RegexOptions.IgnoreCase);
        if (vipMatch.Success)
        {
            return new PrivateServerResult
            {
                Success = true,
                PlaceId = long.Parse(vipMatch.Groups[1].Value),
                AccessCode = vipMatch.Groups[2].Value
            };
        }

        var shareMatch = Regex.Match(input, @"roblox\.com/share[^?#]*[?&]code=([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (shareMatch.Success)
            return await ResolveShareCodeAsync(shareMatch.Groups[1].Value, cookie);

        if (Regex.IsMatch(input, @"^[A-Za-z0-9_\-]+$"))
            return new PrivateServerResult { Success = true, PlaceId = null, AccessCode = input };

        return new PrivateServerResult { Success = false, ErrorMessage = "Could not recognize this as a VIP link, share link, or access code." };
    }

    private async Task<PrivateServerResult> ResolveShareCodeAsync(string code, string cookie)
    {
        try
        {
            using var client = CreateClient(cookie);
            string csrf = await GetCsrfTokenAsync(client);
            if (!string.IsNullOrEmpty(csrf))
                client.DefaultRequestHeaders.Add("x-csrf-token", csrf);

            var payloads = new[]
            {
            JsonSerializer.Serialize(new { linkId = code, linkType = "Server" }),
            JsonSerializer.Serialize(new { code = code, type = "Server" })
        };

            string? lastError = null;

            foreach (var payloadJson in payloads)
            {
                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://apis.roblox.com/sharelinks/v1/resolve-link", content);

                if (response.StatusCode == HttpStatusCode.Forbidden &&
                    response.Headers.TryGetValues("x-csrf-token", out var freshTokens))
                {
                    client.DefaultRequestHeaders.Remove("x-csrf-token");
                    client.DefaultRequestHeaders.Add("x-csrf-token", freshTokens.First());

                    var retryContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                    response = await client.PostAsync("https://apis.roblox.com/sharelinks/v1/resolve-link", retryContent);
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode}";
                    continue;
                }

                string raw = await response.Content.ReadAsStringAsync();
                var placeIdMatch = Regex.Match(raw, @"""placeId""\s*:\s*(\d+)");
                var codeMatch = Regex.Match(raw, @"""(?:linkCode|privateServerLinkCode|accessCode|linkcode)""\s*:\s*""([A-Za-z0-9_\-]+)""");

                if (placeIdMatch.Success && codeMatch.Success)
                {
                    return new PrivateServerResult
                    {
                        Success = true,
                        PlaceId = long.Parse(placeIdMatch.Groups[1].Value),
                        AccessCode = codeMatch.Groups[1].Value
                    };
                }

                lastError = "Response didn't contain the expected placeId/accessCode fields.";
            }

            return new PrivateServerResult { Success = false, ErrorMessage = $"Could not resolve share link: {lastError}" };
        }
        catch (Exception ex)
        {
            return new PrivateServerResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<string?> GetSmallestServerJobIdAsync(long placeId)
    {
        try
        {
            using var client = new HttpClient();
            var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Asc&limit=100";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var servers = doc.RootElement.GetProperty("data");
            if (servers.GetArrayLength() == 0) return null;

            string? bestId = null;
            int bestPlaying = int.MaxValue;
            string? fallbackId = null;
            int fallbackPlaying = int.MaxValue;

            foreach (var server in servers.EnumerateArray())
            {
                int playing = server.TryGetProperty("playing", out var p) ? p.GetInt32() : 0;
                int maxPlayers = server.TryGetProperty("maxPlayers", out var m) ? m.GetInt32() : 0;
                string? id = server.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (id == null) continue;

                if (playing < maxPlayers && playing < bestPlaying)
                {
                    bestPlaying = playing;
                    bestId = id;
                }
                if (playing < fallbackPlaying)
                {
                    fallbackPlaying = playing;
                    fallbackId = id;
                }
            }

            return bestId ?? fallbackId;
        }
        catch
        {
            return null;
        }
    }
}