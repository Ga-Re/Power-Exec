using System.Windows;
using System.Windows.Controls;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Einstellungen › Gruppen: feste Gruppen (Schloss) mit eigener, schlichter Karte.</summary>
public sealed class CategoryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Group { get; set; }
    public DataTemplate? Fixed { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is CategoryItemViewModel { IsFixed: true } ? Fixed : Group;
}
