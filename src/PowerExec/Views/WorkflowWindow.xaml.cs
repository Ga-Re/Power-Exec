using System.Windows;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class WorkflowWindow : Window
{
    public WorkflowWindow(WorkflowEditorViewModel vm)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        ViewModel = vm;
        vm.CloseRequested += Close;
    }

    public WorkflowEditorViewModel ViewModel { get; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
