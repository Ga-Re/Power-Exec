using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec.Views;

/// <summary>Symbolauswahl: Fluent UI System Icons, Heroicons, Segoe-Glyphen, Emoji oder eigene Bilddatei.</summary>
public partial class IconPickerWindow : Window
{
    private const int MaxShown = 480;
    private string _source = "fluent";
    private IconRef _selected;
    private bool _cleared;

    public sealed record Entry(string Key, string Name);

    private IconPickerWindow(IconRef current, string heading)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        Heading.Text = heading;
        _selected = current;
        UpdatePreview();
        var tab = current.Source switch
        {
            IconSource.Hero => HeroTab,
            IconSource.Glyph => GlyphTab,
            IconSource.Emoji => EmojiTab,
            IconSource.File => FileTab,
            _ => FluentTab
        };
        tab.IsChecked = true;
        Loaded += (_, _) => SearchBox.Focus();
    }

    internal static Window ForTest() => new IconPickerWindow(IconRef.Parse("fluent:folder"), "Test");

    /// <returns>Das gewählte Symbol, <see cref="IconRef.None"/> für „kein Symbol“ oder null bei Abbruch.</returns>
    public static IconRef? Pick(IconRef current, string heading = "Symbol wählen")
    {
        var w = new IconPickerWindow(current, heading);
        return w.ShowDialog() == true ? (w._cleared ? IconRef.None : w._selected) : null;
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        _source = tag;
        bool file = tag == "file";
        FilePanel.Visibility = file ? Visibility.Visible : Visibility.Collapsed;
        ResultBorder.Visibility = file ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.IsEnabled = !file;
        License.Text = tag switch
        {
            "fluent" => $"Fluent UI System Icons · {IconLibrary.FluentCount} Symbole · MIT-Lizenz, © Microsoft",
            "hero" => $"Heroicons · {IconLibrary.HeroCount} Symbole · MIT-Lizenz, © Tailwind Labs",
            "glyph" => "Segoe Fluent Icons (in Windows enthalten)",
            "emoji" => "Segoe UI Emoji (in Windows enthalten)",
            _ => string.Empty
        };
        Filter();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();

    private void Filter()
    {
        if (Results == null) return;
        IEnumerable<IconChoice> all = _source switch
        {
            "hero" => IconLibrary.Hero(),
            "glyph" => IconLibrary.Glyphs(),
            "emoji" => IconLibrary.Emoji(),
            "file" => Enumerable.Empty<IconChoice>(),
            _ => IconLibrary.Fluent()
        };
        var words = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = all.Where(c => words.All(w => c.Search.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        Results.ItemsSource = matches.Take(MaxShown).Select(c => new Entry(c.Ref.ToString(), c.Name)).ToList();
        MoreText.Text = matches.Count > MaxShown ? $"{matches.Count} Treffer, die ersten {MaxShown} werden angezeigt – Suche eingrenzen." :
                        _source == "file" ? string.Empty : $"{matches.Count} Treffer";
    }

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Entry entry })
        {
            _selected = IconRef.Parse(entry.Key);
            _cleared = false;
            UpdatePreview(entry.Name);
        }
    }

    private void Icon_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        Icon_Click(sender, e);
        DialogResult = true;
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Bilder (*.png;*.ico;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.gif;*.bmp" };
        if (dlg.ShowDialog(this) != true) return;
        _selected = new IconRef(IconSource.File, dlg.FileName);
        _cleared = false;
        UpdatePreview(Path.GetFileName(dlg.FileName));
    }

    private void UpdatePreview(string? name = null)
    {
        Preview.Icon = _selected.ToString();
        PreviewName.Text = _selected.IsEmpty ? "Kein Symbol ausgewählt" : name ?? _selected.Value;
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        _cleared = true;
        DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
