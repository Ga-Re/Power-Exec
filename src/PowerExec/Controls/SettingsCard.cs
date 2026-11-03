using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PowerExec.Controls;

/// <summary>Einstellungskarte wie in PowerToys: Symbol, Titel und Beschreibung links, Steuerelement (Content) rechts.</summary>
public class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    /// <summary>Text oder beliebiger Inhalt (z. B. ein Link) unter dem Titel.</summary>
    public object? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>Glyphe aus Segoe Fluent Icons, z. B. "&#xE713;".</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    static SettingsCard() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsCard), new FrameworkPropertyMetadata(typeof(SettingsCard)));
}

/// <summary>Aufklappbare Einstellungskarte: Kopf wie <see cref="SettingsCard"/> (rechts <see cref="HeaderContent"/>), darunter weitere Optionen.</summary>
public sealed class SettingsExpander : SettingsCard
{
    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(SettingsExpander),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public object? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    static SettingsExpander() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsExpander), new FrameworkPropertyMetadata(typeof(SettingsExpander)));

    public SettingsExpander() => ToggleCommand = new Infrastructure.RelayCommand(() => IsExpanded = !IsExpanded);

    public ICommand ToggleCommand { get; }
}
