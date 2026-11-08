using System.Windows;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class ScheduleWindow : Window
{
    public ScheduleWindow(ScheduleViewModel vm)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        ViewModel = vm;
        vm.CloseRequested += Close;
    }

    public ScheduleViewModel ViewModel { get; }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
