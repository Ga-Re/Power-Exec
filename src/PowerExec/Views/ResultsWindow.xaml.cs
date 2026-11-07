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
            MessageBox.Show(this, "Die Ausführung läuft noch. Abbrechen und Fenster schließen?", "Power-Exec",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        if (_vm.IsRunning) _vm.CancelCommand.Execute(null);
        base.OnClosing(e);
    }
}
