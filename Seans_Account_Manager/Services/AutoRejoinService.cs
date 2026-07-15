using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Seans_Account_Manager.Models;

namespace Seans_Account_Manager.Services;

public class AutoRejoinService
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private readonly RobloxApiService _api;
    private readonly AccountStore _accountStore;
    private readonly Dictionary<long, AutoRejoinConfig> _configs = new();
    private readonly Dictionary<long, CancellationTokenSource> _cancellations = new();

    public event Action<long>? StatusChanged;

    public AutoRejoinService(RobloxApiService api, AccountStore accountStore)
    {
        _api = api;
        _accountStore = accountStore;
    }

    public AutoRejoinConfig? GetConfig(long userId)
        => _configs.TryGetValue(userId, out var cfg) ? cfg : null;

    public bool IsActive(long userId)
        => _configs.TryGetValue(userId, out var cfg) && cfg.IsActive;

    public void Start(long userId, long placeId, string? jobId = null)
    {
        Stop(userId); 

        var config = new AutoRejoinConfig
        {
            UserId = userId,
            PlaceId = placeId,
            JobId = jobId,
            IsActive = true,
            StartedAt = DateTime.Now,
            RejoinCount = 0
        };
        _configs[userId] = config;

        var cts = new CancellationTokenSource();
        _cancellations[userId] = cts;

        _ = Task.Run(() => MonitorLoopAsync(config, cts.Token));
        StatusChanged?.Invoke(userId);
    }

    public void Stop(long userId)
    {
        if (_cancellations.TryGetValue(userId, out var cts))
        {
            cts.Cancel();
            _cancellations.Remove(userId);
        }
        if (_configs.TryGetValue(userId, out var cfg))
        {
            cfg.IsActive = false;
            StatusChanged?.Invoke(userId);
        }
    }

    public void StopAll()
    {
        foreach (var userId in _configs.Keys.ToList())
            Stop(userId);
    }

    private async Task MonitorLoopAsync(AutoRejoinConfig config, CancellationToken ct)
    {
        var account = _accountStore.Accounts.FirstOrDefault(a => a.UserId == config.UserId);
        if (account == null) return;

        await Task.Delay(15000, ct).ContinueWith(_ => { });

        while (!ct.IsCancellationRequested && config.IsActive)
        {
            try
            {
                bool windowExists = FindWindowForUser(account.Username);

                if (!windowExists)
                {
                    await RelaunchAsync(account, config);
                    config.RejoinCount++;
                    StatusChanged?.Invoke(config.UserId);

                    await Task.Delay(20000, ct);
                    continue;
                }

                await Task.Delay(5000, ct);
            }
            catch (TaskCanceledException) { break; }
            catch { await Task.Delay(5000);  }
        }
    }

    private async Task RelaunchAsync(Account account, AutoRejoinConfig config)
    {
        string cookie = CryptoService.Decrypt(account.EncryptedCookie);
        if (string.IsNullOrEmpty(cookie)) return;

        string ticket = await _api.GetAuthTicketAsync(cookie);
        if (string.IsNullOrEmpty(ticket)) return;

        _api.LaunchGame(ticket, config.PlaceId, config.JobId);
        _ = WindowRenameService.RenameNextRobloxWindowAsync(account.Username);
    }

    private bool FindWindowForUser(string username)
    {
        bool found = false;
        string targetTitle = $"Roblox - {username}";

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (!proc.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
                    return true;

                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.ToString().Equals(targetTitle, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    return false;
                }
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}