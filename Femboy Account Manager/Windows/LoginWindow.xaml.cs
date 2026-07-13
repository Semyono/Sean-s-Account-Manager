using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace FemBoy_Account_Manager.Windows;

public partial class LoginWindow : Window
{
    public string? CapturedCookie { get; private set; }

    private readonly DispatcherTimer _pollTimer;

    public LoginWindow()
    {
        InitializeComponent();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _pollTimer.Tick += async (_, _) => await PollForCookieAsync();
        Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await Browser.EnsureCoreWebView2Async();

        // Clear any existing session so each "Add Account" starts with a clean login screen
        Browser.CoreWebView2.CookieManager.DeleteAllCookies();
        Browser.CoreWebView2.Navigate("https://www.roblox.com/login");

        _pollTimer.Start();
    }

    private async Task PollForCookieAsync()
    {
        if (Browser.CoreWebView2 == null) return;

        var cookieManager = Browser.CoreWebView2.CookieManager;
        var cookies = await cookieManager.GetCookiesAsync("https://www.roblox.com");

        var secCookie = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");
        if (secCookie != null && !string.IsNullOrEmpty(secCookie.Value))
        {
            _pollTimer.Stop();
            CapturedCookie = secCookie.Value;
            DialogResult = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer.Stop();
        base.OnClosed(e);
    }
}