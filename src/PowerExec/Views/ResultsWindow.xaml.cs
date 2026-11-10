using PowerExec.Core.Localization;
using System.ComponentModel;
using System.Windows;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class ResultsWindow : Window
{
    private readonly ResultsViewModel _vm;

    public ResultsWindow(ResultsViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsRunning &&
            !Services.MessageDialog.Confirm(this, L.T("The run is still in progress. Cancel and close the window?"), "Power-Exec", defaultNo: true))
        {
            e.Cancel = true;
            return;
        }
        if (_vm.IsRunning) _vm.CancelCommand.Execute(null);
        base.OnClosing(e);
    }
}
