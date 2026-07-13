using FemBoy_Account_Manager.Models;
using FemBoy_Account_Manager.Services;
using FemBoy_Account_Manager.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FemBoy_Account_Manager;

public partial class MainWindow : Window
{
    private readonly AccountStore _store = new();
    private readonly RobloxApiService _api = new();
    private readonly ObservableCollection<AccountVm> _accounts = new();

    private readonly RecentGamesStore _recentGames = new();
    private readonly ObservableCollection<RecentGame> _recentGamesVm = new();
    private CancellationTokenSource? _lookupCts;

    private readonly AutoRejoinService _rejoinService;

    private Point _dragStartPoint;
    private AccountVm? _draggedItem;

    public MainWindow()
    {
        InitializeComponent();

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
        Closing += (_, _) => _rejoinService.StopAll();
    }

    private void AccountCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Remember where the mouse started; we'll only start dragging after it moves a bit
        _dragStartPoint = e.GetPosition(null);
    }

    private void AccountCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPos;

        // Require a small movement threshold so single clicks don't trigger drag
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Don't start drag if the user is interacting with a button/checkbox inside the card
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

    private void OnRejoinStatusChanged(long userId)
    {
        // Called from a background thread — marshal to UI thread
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

    private void StopAllRejoin_Click(object sender, RoutedEventArgs e)
    {
        _rejoinService.StopAll();
    }

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

        if (selected.Count == 0)
        {
            if (_accounts.Count == 0)
            {
                MessageBox.Show("No saved accounts. Add an account first.", "No Accounts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Fall back to the first account when nothing is checked
            await JoinWithAccountAsync(_accounts[0].Account, placeId, jobId);
            return;
        }

        // Wait 8 seconds between launches so the first Roblox instance has time to fully
        // register its window and settle before the next one starts probing for singletons.
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

        // Fire-and-forget: watch for the new window to appear and label it with the username.
        _ = WindowRenameService.RenameNextRobloxWindowAsync(account.Username);

        // Save this place to recent games (fire-and-forget so we don't block the next launch)
        _ = TrackRecentGameAsync(placeId);
    }

    private async Task TrackRecentGameAsync(long placeId)
    {
        try
        {
            // If we already have this place cached, just promote it to the front
            var existing = _recentGames.Games.FirstOrDefault(g => g.PlaceId == placeId);
            if (existing != null)
            {
                _recentGames.AddOrPromote(existing);
                RefreshRecentGamesUi();
                return;
            }

            // New place — fetch its metadata
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
        catch { /* game metadata is best-effort; don't crash on failure */ }
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

    private void RecentGame_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not long placeId) return;
        PlaceIdBox.Text = placeId.ToString();
    }

    private void RemoveRecentGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not long placeId) return;
        e.Handled = true; // prevent the parent border's click from firing
        _recentGames.Remove(placeId);
        RefreshRecentGamesUi();
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
    }

    private void UpdateAccountCount()
    {
        AccountCountLabel.Text = _accounts.Count == 1 ? "1 account" : $"{_accounts.Count} accounts";
    }
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

    // Pass-through properties bound in XAML
    public long UserId => Account.UserId;
    public string Username => Account.Username;
    public string AvatarUrl => Account.AvatarUrl;
    public long Robux => Account.Robux;
    public string PresenceStatus => Account.PresenceStatus;
    public bool IsCookieValid => Account.IsCookieValid;
    public string Note => Account.Note;

    private bool _isRejoinActive;
    public bool IsRejoinActive
    {
        get => _isRejoinActive;
        set { _isRejoinActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(RejoinButtonText)); OnPropertyChanged(nameof(RejoinButtonColor)); }
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

