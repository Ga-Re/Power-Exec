using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <summary>Design-Editor: links Farben, Schrift und Gruppenfarben, rechts die Vorschau des Panels.</summary>
public partial class DesignEditorWindow : Window
{
    private readonly DesignEditorViewModel _vm;

    public DesignEditorWindow(DesignEditorViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        QuietScrollBar.Attach(FormScroll);
        vm.Owner = this;
        vm.CloseRequested = Close;
        // Installierte Schriften; die Felder nehmen auch einen frei eingegebenen Namen
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Where(n => n.Length > 0).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        TextFontBox.ItemsSource = fonts;
        MonoFontBox.ItemsSource = fonts;
        vm.PropertyChanged += OnVmChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnVmChanged;
        ApplyPreviewFonts();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesignEditorViewModel.FontFamily) or nameof(DesignEditorViewModel.MonoFontFamily)) ApplyPreviewFonts();
    }

    /// <summary>Die Vorschau zeigt die gewählten Schriften (fehlt eine, greift die Schrift der App).</summary>
    private void ApplyPreviewFonts()
    {
        if (_vm.FontFamily.Length > 0) TextElement.SetFontFamily(PreviewPanel, new FontFamily(_vm.FontFamily + ", Segoe UI Variable Text, Segoe UI"));
        else PreviewPanel.ClearValue(TextElement.FontFamilyProperty);
        if (_vm.MonoFontFamily.Length > 0) PreviewMono.FontFamily = new FontFamily(_vm.MonoFontFamily + ", Cascadia Mono, Consolas, Courier New");
        else PreviewMono.SetResourceReference(System.Windows.Controls.TextBlock.FontFamilyProperty, "MonoFont");
    }
}
