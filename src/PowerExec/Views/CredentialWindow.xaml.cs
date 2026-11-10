using PowerExec.Core.Localization;
using System.Windows;
using PowerExec.Services;

namespace PowerExec.Views;

public partial class CredentialWindow : Window
{
    // Nur der Benutzername wird für die Dauer der Sitzung im Speicher gehalten – nie auf der Festplatte.
    private static string _lastAccount = string.Empty;

    private RunCredential? _result;

    private CredentialWindow(string target, bool remote)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        Subtitle.Text = remote ? L.F("{0} · remote connection", target) : target;
        if (remote)
            Hint.Text = L.T("The credentials are only passed to the remote runner for this run (in memory) and are not saved.");
        UserBox.Text = _lastAccount;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(UserBox.Text)) UserBox.Focus();
            else PasswordBox.Focus();
        };
    }

    public static RunCredential? Prompt(Window? owner, string target, bool remote)
    {
        var w = new CredentialWindow(target, remote);
        if (owner is { IsVisible: true })
            w.Owner = owner;
        else
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        return w._result;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var account = UserBox.Text.Trim();
        if (account.Length == 0)
        {
            ErrorText.Text = L.T("Please enter a user name.");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        var (user, domain) = RunCredential.Split(account);
        var pw = PasswordBox.SecurePassword.Copy();
        pw.MakeReadOnly();
        PasswordBox.Clear();
        _lastAccount = account;
        _result = new RunCredential { Account = account, UserName = user, Domain = domain, Password = pw };
        DialogResult = true;
    }
}
