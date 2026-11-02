using System.Windows;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class ProfilesWindow : Window
{
    public ProfilesWindow(ProfilesViewModel vm)
    {
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        vm.CloseRequested = Close;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
