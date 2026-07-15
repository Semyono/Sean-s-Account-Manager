using System.Windows;

namespace Seans_Account_Manager.Windows;

public partial class CookieImportDialog : Window
{
    public string CookieValue { get; private set; } = string.Empty;

    public CookieImportDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CookieBox.Focus();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        string raw = CookieBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            ShowError("Paste a cookie value first.");
            return;
        }

        if (raw.Length < 50)
        {
            ShowError("That doesn't look like a valid .ROBLOSECURITY cookie (too short).");
            return;
        }

        CookieValue = raw;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}