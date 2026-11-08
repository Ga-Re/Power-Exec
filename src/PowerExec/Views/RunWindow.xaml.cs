using System.Windows;
using System.Windows.Input;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class RunWindow : Window
{
    private readonly RunViewModel _vm;

    public RunWindow(RunViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        vm.CloseRequested = Close;
        vm.OwnerWindow = () => this;
        if (!vm.HasParameters && !vm.IsRemote) Height = 640;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void MachineBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_vm.AddMachineCommand.CanExecute(null)) _vm.AddMachineCommand.Execute(null);
            e.Handled = true;
        }
    }
}
