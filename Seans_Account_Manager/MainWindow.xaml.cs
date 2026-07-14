using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Seans_Account_Manager.Models;
using Seans_Account_Manager.Services;
using Seans_Account_Manager.Windows;

namespace Seans_Account_Manager;

public partial class MainWindow : Window
{
    private readonly AccountStore _store = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly RobloxApiService _api = new();
    private readonly ObservableCollection<AccountVm> _accounts = new();

    private readonly RecentGamesStore _recentGames = new();
    private readonly ObservableCollection<RecentGame> _recentGamesVm = new();

    private readonly AutoRejoinService _rejoinService;
    private readonly AntiAfkService _antiAfk = new();
    private readonly AppSettings _appSettings;
    private int _afkCount;

    private CancellationTokenSource? _lookupCts;
    private DispatcherTimer? _openWindowsTimer;
    private const int MaxConsoleLines = 600;
    private readonly List<ConsoleLogEntry> _consoleEntries = new();
    private string _consoleFilter = "All";
    private UiTraceListener? _uiTraceListener;
    private bool _isBindingAfkKey;
    private ushort _afkBoundVk = 0x57; // W
    private string _afkBoundName = "W";

    private Point _dragStartPoint;
    private AccountVm? _draggedItem;

    public MainWindow()
    {
        InitializeComponent();

        _appSettings = _settingsStore.Load();

        _rejoinService = new AutoRejoinService(_api, _store);
        _rejoinService.StatusChanged += OnRejoinStatusChanged;

        AccountsList.ItemsSource = _accounts;
        foreach (var acc in _store.Accounts)
            _accounts.Add(new AccountVm(acc));

        UpdateAccountCount();

        RecentGamesList.ItemsSource = _recentGamesVm;
        foreach (var g in _recentGames.Games) _recentGamesVm.Add(g);
        UpdateRecentGamesVisibility();

        Loaded += async (_, _) => await RefreshAllAsync();
        Closing += (_, _) =>
        {
            _rejoinService.StopAll();
            _antiAfk.Stop();
            MultiRobloxService.Disable();
            _openWindowsTimer?.Stop();
            if (_uiTraceListener != null)
                Trace.Listeners.Remove(_uiTraceListener);
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        _uiTraceListener = new UiTraceListener(msg => LogDebug($"TRACE {msg}", "Trace"));
        Trace.Listeners.Add(_uiTraceListener);
        LogDebug("Application started.", "App");

        if (AfkKeyBindBox != null)
            AfkKeyBindBox.Text = _afkBoundName;

        UpdateMultiRobloxUiState();
        ApplyGeneralSettingsToUi();
    }

    // ================= NAV / TABS =================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        if (AccountsTab == null) return; // during InitializeComponent

        AccountsTab.Visibility = tag == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        MultiRobloxTab.Visibility = tag == "MultiRoblox" ? Visibility.Visible : Visibility.Collapsed;
        AntiAfkTab.Visibility = tag == "AntiAfk" ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ConsoleTab.Visibility = tag == "Console" ? Visibility.Visible : Visibility.Collapsed;

        LogDebug($"Tab changed -> {tag}", "App");

        if (tag == "MultiRoblox")
        {
            UpdateMultiRobloxUiState();
            RefreshOpenWindowsLabel();
            StartOpenWindowsTimer();
        }
        else
        {
            StopOpenWindowsTimer();
        }
    }

    private void LogDebug(string message, string category = "App")
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        bool isError = category == "Errors" ||
            message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exception", StringComparison.OrdinalIgnoreCase);

        void WriteLine()
        {
            _consoleEntries.Add(new ConsoleLogEntry(line, category, isError));
            while (_consoleEntries.Count > MaxConsoleLines)
                _consoleEntries.RemoveAt(0);

            RefreshConsoleView();
        }

        if (Dispatcher.CheckAccess()) WriteLine();
        else Dispatcher.BeginInvoke((Action)WriteLine);
    }

    private void RefreshConsoleView()
    {
        if (ConsoleOutputBox == null)
            return;

        IEnumerable<ConsoleLogEntry> filtered = _consoleEntries;
        if (_consoleFilter == "AntiAfk")
            filtered = filtered.Where(x => x.Category == "AntiAfk");
        else if (_consoleFilter == "MultiRoblox")
            filtered = filtered.Where(x => x.Category == "MultiRoblox");
        else if (_consoleFilter == "Errors")
            filtered = filtered.Where(x => x.IsError);
        else if (_consoleFilter == "App")
            filtered = filtered.Where(x => x.Category == "App");
        else if (_consoleFilter == "Trace")
            filtered = filtered.Where(x => x.Category == "Trace");

        ConsoleOutputBox.Text = string.Join(Environment.NewLine, filtered.Select(x => x.Text));
        ConsoleOutputBox.ScrollToEnd();
    }

    private void ConsoleFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConsoleFilterBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            _consoleFilter = tag;
        else
            _consoleFilter = "All";

        RefreshConsoleView();
    }

    private void ClearConsole_Click(object sender, RoutedEventArgs e)
    {
        _consoleEntries.Clear();
        if (ConsoleOutputBox != null)
            ConsoleOutputBox.Text = string.Empty;
        LogDebug("Console cleared.", "App");
    }

    private void CopyConsole_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = ConsoleOutputBox?.Text ?? string.Empty;
            Clipboard.SetText(text);
            LogDebug("Console copied to clipboard.", "App");
        }
        catch (Exception ex)
        {
            LogDebug($"Console copy failed: {ex.Message}", "Errors");
        }
    }

    // ================= ACCOUNTS =================

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow { Owner = this };
        bool? result = loginWindow.ShowDialog();

        if (result != true || string.IsNullOrEmpty(loginWindow.CapturedCookie))
            return;

        string cookie = loginWindow.CapturedCookie;
        var authResult = await _api.GetAuthenticatedUserAsync(cookie);

        if (!authResult.Success)
        {
            MessageBox.Show("Could not verify the account. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var account = new Account
        {
            UserId = authResult.UserId,
            Username = authResult.Username,
            DisplayName = authResult.DisplayName,
            EncryptedCookie = CryptoService.Encrypt(cookie),
            IsCookieValid = true
        };

        account.AvatarUrl = await _api.GetAvatarThumbnailAsync(account.UserId);
        account.Robux = await _api.GetRobuxAsync(cookie);
        account.PresenceStatus = await _api.GetPresenceAsync(cookie, account.UserId);

        _store.AddOrUpdate(account);

        var existing = _accounts.FirstOrDefault(a => a.Account.UserId == account.UserId);
        if (existing != null) _accounts.Remove(existing);
        _accounts.Add(new AccountVm(account));

        UpdateAccountCount();
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async Task RefreshAllAsync()
    {
        foreach (var vm in _accounts.ToList())
        {
            var account = vm.Account;
            string cookie = CryptoService.Decrypt(account.EncryptedCookie);
            if (string.IsNullOrEmpty(cookie))
            {
                account.IsCookieValid = false;
                vm.NotifyAllChanged();
                continue;
            }

            var authResult = await _api.GetAuthenticatedUserAsync(cookie);
            account.IsCookieValid = authResult.Success;

            if (authResult.Success)
            {
                account.Robux = await _api.GetRobuxAsync(cookie);
                account.PresenceStatus = await _api.GetPresenceAsync(cookie, account.UserId);
                if (string.IsNullOrEmpty(account.AvatarUrl))
                    account.AvatarUrl = await _api.GetAvatarThumbnailAsync(account.UserId);
            }
            vm.NotifyAllChanged();
        }

        _store.Accounts = _accounts.Select(vm => vm.Account).ToList();
        _store.Save();
    }

    private async void JoinAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not long userId) return;
        var vm = _accounts.FirstOrDefault(a => a.Account.UserId == userId);
        if (vm == null) return;

        if (!long.TryParse(PlaceIdBox.Text, out long placeId))
        {
            MessageBox.Show("Enter a valid Place ID above.", "Missing Place ID", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? jobId = string.IsNullOrWhiteSpace(JobIdBox.Text) ? null : JobIdBox.Text.Trim();
        if (!ConfirmBeforeLaunch($"Launch Roblox for account '{vm.Account.Username}'?"))
            return;

        await JoinWithAccountAsync(vm.Account, placeId, jobId);
    }

    private async void JoinPlace_Click(object sender, RoutedEventArgs e)
    {
        var selected = _accounts.Where(a => a.IsSelected).ToList();

        if (!long.TryParse(PlaceIdBox.Text, out long placeId))
        {
            MessageBox.Show("Enter a valid Place ID.", "Missing Place ID", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? jobId = string.IsNullOrWhiteSpace(JobIdBox.Text) ? null : JobIdBox.Text.Trim();
        int launchCount = selected.Count == 0 ? 1 : selected.Count;
        if (!ConfirmBeforeLaunch($"Launch Roblox for {launchCount} account(s)?"))
            return;

        if (selected.Count == 0)
        {
            if (_accounts.Count == 0)
            {
                MessageBox.Show("No saved accounts. Add an account first.", "No Accounts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await JoinWithAccountAsync(_accounts[0].Account, placeId, jobId);
            return;
        }

        for (int i = 0; i < selected.Count; i++)
        {
            await JoinWithAccountAsync(selected[i].Account, placeId, jobId);
            if (i < selected.Count - 1)
                await Task.Delay(8000);
        }
    }

    private async Task JoinWithAccountAsync(Account account, long placeId, string? jobId = null)
    {
        string cookie = CryptoService.Decrypt(account.EncryptedCookie);
        if (string.IsNullOrEmpty(cookie))
        {
            MessageBox.Show("This account's cookie is invalid. Try re-adding it.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string ticket = await _api.GetAuthTicketAsync(cookie);
        if (string.IsNullOrEmpty(ticket))
        {
            MessageBox.Show("Could not get an authentication ticket. The cookie may have expired.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _api.LaunchGame(ticket, placeId, jobId);
        _ = WindowRenameService.RenameNextRobloxWindowAsync(account.Username);
        _ = TrackRecentGameAsync(placeId);
    }

    private async Task TrackRecentGameAsync(long placeId)
    {
        try
        {
            var existing = _recentGames.Games.FirstOrDefault(g => g.PlaceId == placeId);
            if (existing != null)
            {
                _recentGames.AddOrPromote(existing);
                RefreshRecentGamesUi();
                return;
            }

            long universeId = await _api.GetUniverseIdAsync(placeId);
            if (universeId == 0) return;

            string name = await _api.GetGameNameAsync(universeId);
            string iconUrl = await _api.GetGameIconAsync(universeId);

            var game = new RecentGame
            {
                PlaceId = placeId,
                UniverseId = universeId,
                Name = string.IsNullOrEmpty(name) ? $"Place {placeId}" : name,
                IconUrl = iconUrl,
                LastPlayed = DateTime.Now
            };

            _recentGames.AddOrPromote(game);
            RefreshRecentGamesUi();
        }
        catch { }
    }

    private void RefreshRecentGamesUi()
    {
        _recentGamesVm.Clear();
        foreach (var g in _recentGames.Games) _recentGamesVm.Add(g);
        UpdateRecentGamesVisibility();
    }

    private void UpdateRecentGamesVisibility()
    {
        NoRecentLabel.Visibility = _recentGamesVm.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RecentGame_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not long placeId) return;
        PlaceIdBox.Text = placeId.ToString();
    }

    private void RemoveRecentGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not long placeId) return;
        e.Handled = true;
        _recentGames.Remove(placeId);
        RefreshRecentGamesUi();
    }

    private async void PlaceIdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _lookupCts?.Cancel();
        _lookupCts = new CancellationTokenSource();
        var token = _lookupCts.Token;

        string text = PlaceIdBox.Text.Trim();

        if (string.IsNullOrEmpty(text) || !long.TryParse(text, out long placeId))
        {
            GameNameLabel.Text = "";
            return;
        }

        var cached = _recentGames.Games.FirstOrDefault(g => g.PlaceId == placeId);
        if (cached != null)
        {
            GameNameLabel.Text = cached.Name;
            return;
        }

        try { await Task.Delay(500, token); }
        catch (TaskCanceledException) { return; }

        if (token.IsCancellationRequested) return;

        GameNameLabel.Text = "Looking up…";

        try
        {
            long universeId = await _api.GetUniverseIdAsync(placeId);
            if (token.IsCancellationRequested) return;

            if (universeId == 0)
            {
                GameNameLabel.Text = "Game not found";
                return;
            }

            string name = await _api.GetGameNameAsync(universeId);
            if (token.IsCancellationRequested) return;

            GameNameLabel.Text = string.IsNullOrEmpty(name) ? $"Place {placeId}" : name;
        }
        catch
        {
            GameNameLabel.Text = "";
        }
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not long userId) return;

        var result = MessageBox.Show("Delete this account from the list?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var vm = _accounts.FirstOrDefault(a => a.Account.UserId == userId);
        if (vm != null) _accounts.Remove(vm);

        _store.Remove(userId);
        UpdateAccountCount();
    }

    private void EditNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not long userId) return;
        var vm = _accounts.FirstOrDefault(a => a.Account.UserId == userId);
        if (vm == null) return;

        var dlg = new NoteDialog { Owner = this, NoteText = vm.Account.Note };
        if (dlg.ShowDialog() == true)
        {
            vm.Account.Note = dlg.NoteText;
            vm.NotifyAllChanged();
            _store.AddOrUpdate(vm.Account);
        }
    }

    private void UpdateAccountCount()
    {
        AccountCountLabel.Text = _accounts.Count == 1 ? "1 account" : $"{_accounts.Count} accounts";
    }

    // ================= MULTI ROBLOX TAB =================

    private void CloseAllRoblox_Click(object sender, RoutedEventArgs e)
    {
        int count = RobloxProcessService.CountRunning();
        if (count == 0)
        {
            MessageBox.Show("No Roblox processes are currently running.", "Nothing to Close",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Kill {count} Roblox-related process(es)?\n\nThis includes any running games and leftover crash handlers.",
            "Close All Roblox", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        int killed = RobloxProcessService.KillAll();
        MessageBox.Show($"Closed {killed} process(es).", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        LogDebug($"Closed {killed} Roblox process(es).", "MultiRoblox");
        RefreshOpenWindowsLabel();
    }

    private void EnableMultiRobloxCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (MultiRobloxService.Enable())
        {
            LogDebug("Multi Roblox enabled.", "MultiRoblox");
            Handle64StatusLabel.Text = "Handle64 ready.";
            Handle64StatusLabel.Foreground = FindResource("SuccessBrush") as System.Windows.Media.Brush;
            return;
        }

        EnableMultiRobloxCheckBox.IsChecked = false;
        var err = string.IsNullOrWhiteSpace(MultiRobloxService.LastError)
            ? "[Handle64 not found]"
            : MultiRobloxService.LastError;
        Handle64StatusLabel.Text = err;
        Handle64StatusLabel.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
        LogDebug($"Multi Roblox enable failed: {err}", "Errors");
    }

    private void EnableMultiRobloxCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        MultiRobloxService.Disable();
        LogDebug("Multi Roblox disabled.", "MultiRoblox");
        UpdateMultiRobloxUiState();
    }

    private void DownloadHandle64_Click(object sender, RoutedEventArgs e)
    {
        Handle64StatusLabel.Text = "Downloading Handle64...";
        Handle64StatusLabel.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;

        bool ok = MultiRobloxService.DownloadHandle64();
        if (ok)
        {
            Handle64StatusLabel.Text = "Handle64 downloaded.";
            Handle64StatusLabel.Foreground = FindResource("SuccessBrush") as System.Windows.Media.Brush;
            LogDebug("Handle64 downloaded successfully.", "MultiRoblox");
            return;
        }

        var err = string.IsNullOrWhiteSpace(MultiRobloxService.LastError)
            ? "Failed to download Handle64."
            : MultiRobloxService.LastError;
        Handle64StatusLabel.Text = err;
        Handle64StatusLabel.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
        LogDebug($"Handle64 download failed: {err}", "Errors");
    }

    private void UpdateMultiRobloxUiState()
    {
        bool hasHandle = MultiRobloxService.IsHandle64Available();

        if (EnableMultiRobloxCheckBox != null)
            EnableMultiRobloxCheckBox.IsChecked = MultiRobloxService.IsEnabled;

        if (DownloadHandle64Button != null)
            DownloadHandle64Button.Visibility = hasHandle ? Visibility.Collapsed : Visibility.Visible;

        if (Handle64StatusLabel == null)
            return;

        if (hasHandle)
        {
            Handle64StatusLabel.Text = "Handle64 ready.";
            Handle64StatusLabel.Foreground = FindResource("SuccessBrush") as System.Windows.Media.Brush;
        }
        else
        {
            Handle64StatusLabel.Text = "[Handle64 not found]";
            Handle64StatusLabel.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
        }
    }

    private void RefreshOpenWindows_Click(object sender, RoutedEventArgs e) => RefreshOpenWindowsLabel();

    private void RefreshOpenWindowsLabel()
    {
        int count = RobloxProcessService.CountRunning();
        OpenWindowsLabel.Text = count == 0
            ? "No Roblox processes are currently running."
            : $"{count} Roblox-related process(es) running.";
    }

    private void StartOpenWindowsTimer()
    {
        _openWindowsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _openWindowsTimer.Tick -= OpenWindowsTimer_Tick;
        _openWindowsTimer.Tick += OpenWindowsTimer_Tick;
        _openWindowsTimer.Start();
    }

    private void StopOpenWindowsTimer() => _openWindowsTimer?.Stop();

    private void OpenWindowsTimer_Tick(object? sender, EventArgs e) => RefreshOpenWindowsLabel();

    // ================= AUTO-REJOIN =================

    private void OnRejoinStatusChanged(long userId)
    {
        Dispatcher.Invoke(() =>
        {
            var vm = _accounts.FirstOrDefault(a => a.Account.UserId == userId);
            if (vm == null) return;

            var cfg = _rejoinService.GetConfig(userId);
            vm.IsRejoinActive = cfg?.IsActive ?? false;
            vm.RejoinCount = cfg?.RejoinCount ?? 0;
        });
    }

    private async void ToggleRejoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not long userId) return;
        var vm = _accounts.FirstOrDefault(a => a.Account.UserId == userId);
        if (vm == null) return;

        if (_rejoinService.IsActive(userId))
        {
            _rejoinService.Stop(userId);
            return;
        }

        if (!long.TryParse(PlaceIdBox.Text, out long placeId))
        {
            MessageBox.Show("Enter a Place ID in the Actions panel before enabling Auto-Rejoin.",
                "Missing Place ID", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? jobId = string.IsNullOrWhiteSpace(JobIdBox.Text) ? null : JobIdBox.Text.Trim();

        await JoinWithAccountAsync(vm.Account, placeId, jobId);
        _rejoinService.Start(userId, placeId, jobId);
    }

    // ================= ANTI-AFK (KEEP-AWAKE) =================

    private void ToggleAntiAfk_Click(object sender, RoutedEventArgs e)
    {
        if (_antiAfk.IsRunning)
        {
            _antiAfk.Ticked       -= OnAntiAfkTicked;
            _antiAfk.CountdownTick -= OnCountdownTick;
            _antiAfk.Stop();
            AntiAfkToggleBtn.Content = "Start Anti-AFK";
            AfkStatusLabel.Text = "Stopped.";
            LogDebug("Anti-AFK stopped.", "AntiAfk");
            return;
        }

        if (!int.TryParse(AfkIntervalBox.Text, out int intervalMin) || intervalMin < 1 || intervalMin > 120)
        {
            MessageBox.Show("Interval must be between 1 and 120 minutes.", "Invalid Interval",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(AfkPressCountBox.Text, out int pressCount) || pressCount < 1 || pressCount > 10)
        {
            MessageBox.Show("Press count must be between 1 and 10.", "Invalid Press Count",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string keyName = _afkBoundName;

        _antiAfk.IntervalMinutes = intervalMin;
        _antiAfk.KeyName  = keyName;
        _antiAfk.VirtualKeyCode = _afkBoundVk;
        _antiAfk.PressCount = pressCount;
        _afkCount = 0;
        _antiAfk.Start();
        _antiAfk.Ticked       += OnAntiAfkTicked;
        _antiAfk.CountdownTick += OnCountdownTick;

        AntiAfkToggleBtn.Content = "Stop Anti-AFK";
        AfkStatusLabel.Text = $"Next press in {intervalMin}:00";
        LogDebug($"Anti-AFK started: key={_afkBoundName}, interval={intervalMin}m, count={pressCount}", "AntiAfk");
    }

    private async void TestAntiAfk_Click(object sender, RoutedEventArgs e)
    {
        string keyName = _afkBoundName;
        if (!int.TryParse(AfkPressCountBox.Text, out int pressCount) || pressCount < 1 || pressCount > 10)
            pressCount = 1;

        _antiAfk.KeyName = keyName;
        _antiAfk.VirtualKeyCode = _afkBoundVk;
        _antiAfk.PressCount = pressCount;

        int windows = _antiAfk.GetOpenRobloxWindowCount();
        if (windows == 0)
        {
            AfkStatusLabel.Text = "Test: no Roblox windows found.";
            LogDebug("Anti-AFK test: no Roblox windows found.", "AntiAfk");
            return;
        }

        AfkStatusLabel.Text = $"Test: sending {keyName} to {windows} window(s)…";
        string summary = await _antiAfk.TestNowAsync();
        AfkStatusLabel.Text = $"Test done: {summary}";
        LogDebug($"Anti-AFK test done: {summary}", "AntiAfk");
    }

    private void BeginBindAfkKey_Click(object sender, RoutedEventArgs e)
    {
        _isBindingAfkKey = true;
        AfkStatusLabel.Text = "Press any key to bind Anti-AFK…";
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isBindingAfkKey)
            return;

        if (e.Key == Key.System)
            return;

        Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0)
            return;

        _afkBoundVk = (ushort)vk;
        _afkBoundName = key.ToString();
        AfkKeyBindBox.Text = _afkBoundName;
        _isBindingAfkKey = false;
        AfkStatusLabel.Text = $"Bound key: {_afkBoundName}";
        LogDebug($"Anti-AFK key bound: {_afkBoundName} (VK={_afkBoundVk})", "AntiAfk");
        e.Handled = true;
    }

    // ================= SETTINGS =================

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SeansAccountManager");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void GeneralSettingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _appSettings.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _appSettings.ConfirmBeforeLaunch = ConfirmBeforeLaunchCheckBox.IsChecked == true;
        Topmost = _appSettings.AlwaysOnTop;
        _settingsStore.Save(_appSettings);
        LogDebug($"General settings updated: AlwaysOnTop={_appSettings.AlwaysOnTop}, ConfirmBeforeLaunch={_appSettings.ConfirmBeforeLaunch}", "App");
    }

    private void ApplyGeneralSettingsToUi()
    {
        Topmost = _appSettings.AlwaysOnTop;

        if (AlwaysOnTopCheckBox != null)
            AlwaysOnTopCheckBox.IsChecked = _appSettings.AlwaysOnTop;

        if (ConfirmBeforeLaunchCheckBox != null)
            ConfirmBeforeLaunchCheckBox.IsChecked = _appSettings.ConfirmBeforeLaunch;
    }

    private bool ConfirmBeforeLaunch(string text)
    {
        if (!_appSettings.ConfirmBeforeLaunch)
            return true;

        var result = MessageBox.Show(text, "Confirm Launch", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    // ================= TITLE BAR =================

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
        => Close();

    // ================= DRAG & DROP =================

    private void AccountCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void AccountCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (e.OriginalSource is Button || e.OriginalSource is CheckBox) return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) != null) return;

        if (sender is not Border border || border.Tag is not long userId) return;
        var vm = _accounts.FirstOrDefault(a => a.Account.UserId == userId);
        if (vm == null) return;

        _draggedItem = vm;
        DragDrop.DoDragDrop(border, vm, DragDropEffects.Move);
        _draggedItem = null;
    }

    private void AccountCard_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border && e.Data.GetDataPresent(typeof(AccountVm)))
        {
            border.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentPink");
            border.BorderThickness = new Thickness(2);
        }
    }

    private void AccountCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtle");
            border.BorderThickness = new Thickness(1);
        }
    }

    private void AccountCard_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border border) return;

        border.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtle");
        border.BorderThickness = new Thickness(1);

        if (!e.Data.GetDataPresent(typeof(AccountVm))) return;
        if (e.Data.GetData(typeof(AccountVm)) is not AccountVm dragged) return;
        if (border.Tag is not long targetUserId) return;

        var target = _accounts.FirstOrDefault(a => a.Account.UserId == targetUserId);
        if (target == null || target == dragged) return;

        int oldIndex = _accounts.IndexOf(dragged);
        int newIndex = _accounts.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0) return;

        _accounts.Move(oldIndex, newIndex);
        _store.SaveOrder(_accounts.Select(a => a.Account.UserId));
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T t) return t;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnAntiAfkTicked(int count)
    {
        _afkCount = count;
        Dispatcher.Invoke(() =>
        {
            AfkStatusLabel.Text = $"Pressed {_antiAfk.KeyName}! ({count} total)  ·  next in {_antiAfk.IntervalMinutes}:00";
        });
    }

    private void OnCountdownTick(int secondsRemaining)
    {
        int m = secondsRemaining / 60;
        int s = secondsRemaining % 60;
        Dispatcher.Invoke(() =>
        {
            string label = $"Next press in {m}:{s:D2}";
            if (_afkCount > 0)
                label += $"  ·  sent {_antiAfk.KeyName} {_afkCount}x";
            AfkStatusLabel.Text = label;
        });
    }

    private sealed class UiTraceListener : TraceListener
    {
        private readonly Action<string> _sink;
        public UiTraceListener(Action<string> sink) => _sink = sink;

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                _sink(message);
        }
    }

    private sealed record ConsoleLogEntry(string Text, string Category, bool IsError);
}

// Wraps Account with UI-only state (selection, change notifications)
public class AccountVm : INotifyPropertyChanged
{
    public Account Account { get; }
    public AccountVm(Account account) { Account = account; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private bool _isRejoinActive;
    public bool IsRejoinActive
    {
        get => _isRejoinActive;
        set { _isRejoinActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(RejoinButtonText)); OnPropertyChanged(nameof(RejoinButtonColor)); OnPropertyChanged(nameof(RejoinStatusText)); }
    }

    private int _rejoinCount;
    public int RejoinCount
    {
        get => _rejoinCount;
        set { _rejoinCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(RejoinStatusText)); }
    }

    public string RejoinButtonText => IsRejoinActive ? "Stop" : "Rejoin";
    public string RejoinButtonColor => IsRejoinActive ? "#E85D75" : "#26262F";
    public string RejoinStatusText => IsRejoinActive
        ? (RejoinCount > 0 ? $"● Auto-Rejoin ({RejoinCount})" : "● Auto-Rejoin")
        : "";

    public long UserId => Account.UserId;
    public string Username => Account.Username;
    public string AvatarUrl => Account.AvatarUrl;
    public long Robux => Account.Robux;
    public string PresenceStatus => Account.PresenceStatus;
    public bool IsCookieValid => Account.IsCookieValid;
    public string Note => Account.Note;

    public void NotifyAllChanged()
    {
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(AvatarUrl));
        OnPropertyChanged(nameof(Robux));
        OnPropertyChanged(nameof(PresenceStatus));
        OnPropertyChanged(nameof(IsCookieValid));
        OnPropertyChanged(nameof(Note));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}