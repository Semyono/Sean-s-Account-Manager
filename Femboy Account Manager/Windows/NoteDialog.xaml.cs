using System.Windows;

namespace FemBoy_Account_Manager.Windows;

public partial class NoteDialog : Window
{
    public string NoteText
    {
        get => NoteBox.Text;
        set => NoteBox.Text = value;
    }

    public NoteDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { NoteBox.Focus(); NoteBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}