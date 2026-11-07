using System.Windows;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow(WelcomeViewModel vm, Action<bool>? closed = null)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        ViewModel = vm;
        vm.CloseRequested += tryPanel =>
        {
            Close();
            closed?.Invoke(tryPanel);
        };
    }

    public WelcomeViewModel ViewModel { get; }
}
