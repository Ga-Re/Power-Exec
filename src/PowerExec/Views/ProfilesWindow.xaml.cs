using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>
/// Eintragsdialog: erst die Art (Kacheln), dann ein Formular mit den Feldern dieser Art, dem Startverhalten
/// (= Standardprofil), weiteren Startprofilen und Gruppe/Schlagworten. Auch für Dateien aus smarten Gruppen.
/// Die Fenstergröße wird je Art gemerkt.
/// </summary>
public partial class ProfilesWindow : Window
{
    private readonly ProfilesViewModel _vm;
    private readonly AppController? _app;
    private string _sizeKey = string.Empty;

    public ProfilesWindow(ProfilesViewModel vm) : this(vm, null) { }

    public ProfilesWindow(ProfilesViewModel vm, AppController? app)
    {
        _vm = vm;
        _app = app;
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        vm.CloseRequested = () =>
        {
            try { DialogResult = vm.SavedKey != null; }
            catch (InvalidOperationException) { Close(); } // nicht modal geöffnet
        };
        vm.KindChanged += _ => { RememberSize(); ApplySize(); Dispatcher.BeginInvoke(() => NameBox.Focus()); };
        SourceInitialized += (_, _) => ApplySize();
        Closing += (_, _) => RememberSize();
        Loaded += (_, _) => { if (vm.ShowForm) NameBox.Focus(); };
    }

    /// <summary>Schlüssel für die gemerkte Größe: Art bzw. „Script“/„Programm“ bei Dateien, „Auswahl“ in Schritt 1.</summary>
    private string SizeKey => _vm.ShowKindPicker ? "Auswahl"
        : _vm.Entry?.KindKey ?? (_vm.Item.IsPowerShell ? "Script" : "Programm");

    private static (double W, double H) DefaultSize(string key) => key switch
    {
        "Auswahl" => (760, 600),
        "Url" or "Folder" => (860, 760),
        "Rdp" or "Ssh" => (880, 860),
        "Snippet" => (1000, 920),
        "TextSnippet" => (900, 820),
        "Tool" => (900, 900),
        "Script" => (1000, 880),
        _ => (900, 820)
    };

    private void ApplySize()
    {
        _sizeKey = SizeKey;
        var (w, h) = DefaultSize(_sizeKey);
        if (_app?.Settings.EditorSizes.TryGetValue(_sizeKey, out var saved) == true)
        {
            var parts = saved.Split('x');
            if (parts.Length == 2 && double.TryParse(parts[0], out var sw) && double.TryParse(parts[1], out var sh)) (w, h) = (sw, sh);
        }
        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(w, MinWidth, area.Width - 40);
        Height = Math.Clamp(h, MinHeight, area.Height - 40);
        if (IsLoaded)
        {
            Left = Math.Max(area.Left + 20, Math.Min(Left, area.Right - Width - 20));
            Top = Math.Max(area.Top + 20, Math.Min(Top, area.Bottom - Height - 20));
        }
    }

    private void RememberSize()
    {
        if (_app == null || _sizeKey.Length == 0 || WindowState != WindowState.Normal || !IsLoaded) return;
        _app.Settings.EditorSizes[_sizeKey] = $"{(int)ActualWidth}x{(int)ActualHeight}";
    }

    /// <summary>Eigenen Eintrag modal anlegen oder bearbeiten. <paramref name="entry"/> = null: erst die Art wählen.</summary>
    public static EntryEditorResult EditEntryEx(AppController app, AppSettings target, CustomEntry? entry, bool isNew, bool saveImmediately,
        string? group = null, IEnumerable<string>? tags = null, Window? owner = null)
    {
        bool pick = entry == null;
        entry ??= new CustomEntry { Kind = CustomEntryKind.Url };
        LaunchItem item = entry.ToLaunchItem();
        var vm = new ProfilesViewModel(app, item, target, saveImmediately, entry, isNew, group, tags, pickKind: pick);
        var w = new ProfilesWindow(vm, app);
        if (owner != null) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        var ok = w.ShowDialog() == true;
        return new EntryEditorResult(ok ? vm.SavedKey : null, vm.AlternativeRequested, vm.SmartGroupPath);
    }

    /// <returns>Schlüssel des gespeicherten Eintrags oder null bei Abbruch.</returns>
    public static string? EditEntry(AppController app, AppSettings target, CustomEntry entry, bool isNew, bool saveImmediately,
        string? group = null, IEnumerable<string>? tags = null, Window? owner = null) =>
        EditEntryEx(app, target, entry, isNew, saveImmediately, group, tags, owner).SavedKey;

    internal static Window ForTest(AppController app, CustomEntry? entry) =>
        new ProfilesWindow(new ProfilesViewModel(app, (entry ?? new CustomEntry()).ToLaunchItem(), app.Settings, false, entry ?? new CustomEntry(), isNew: true,
            pickKind: entry == null), app);

    internal ProfilesViewModel ViewModelForTest => _vm;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
