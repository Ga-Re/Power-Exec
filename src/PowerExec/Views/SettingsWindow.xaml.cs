using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>
/// Einstellungen werden live übernommen: Jede Eingabe startet einen kurzen Zeitgeber, danach gehen die Änderungen
/// an die App (Panels werden nur neu aufgebaut, wenn sich an ihnen etwas geändert hat). „Änderungen verwerfen“
/// stellt den Stand beim Öffnen wieder her. Ändert sich etwas im Panel, zeigt das Fenster den neuen Stand.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private readonly AppSettings _snapshot;
    private readonly DispatcherTimer _applyTimer;
    private SettingsViewModel _vm;
    private string _lastApplied = string.Empty;
    private bool _reloading;

    public SettingsWindow(SettingsViewModel vm, AppController app)
    {
        _vm = vm;
        _app = app;
        _snapshot = SettingsStore.Clone(app.Settings);
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        Attach(vm);
        _lastApplied = Fingerprint(app.Settings);

        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _applyTimer.Tick += (_, _) => ApplyNow();

        // Jede Eingabe (Text, Haken, Auswahl, Schieberegler, Knöpfe, Fokuswechsel) löst die Übernahme aus
        AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        AddHandler(Selector.SelectionChangedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        AddHandler(RangeBase.ValueChangedEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        AddHandler(UIElement.LostKeyboardFocusEvent, new RoutedEventHandler((_, _) => ScheduleApply()));
        Activated += (_, _) => ScheduleApply(); // zurück aus einem Dialog (⚙, Eintrag bearbeiten …)
        Deactivated += (_, _) => _vm.SearchText = string.Empty; // Suchvorschläge nicht über anderen Fenstern stehen lassen

        _app.SettingsChangedOutside += OnChangedOutside;
        _app.MachineGroupsSynced += OnMachineGroupsSynced;
        Closing += (_, _) => ApplyNow(force: true);
        Closed += (_, _) =>
        {
            _app.SettingsChangedOutside -= OnChangedOutside;
            _app.MachineGroupsSynced -= OnMachineGroupsSynced;
            _vm.Detach();
        };
    }

    private void OnMachineGroupsSynced() => _vm.ApplyMachineSync();

    private void Attach(SettingsViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        vm.ReopenRequested += () =>
        {
            Close();
            _app.ShowSettings();
        };
        vm.SwitchWorkspaceRequested += name =>
        {
            ApplyNow(force: true);
            Close();
            _app.SwitchWorkspace(name);
            _app.ShowSettings(section: "workspaces");
        };
    }

    internal void ApplyNowForTest() => ApplyNow(force: true);
    internal SettingsViewModel CurrentViewModel => _vm;

    private void ScheduleApply()
    {
        if (_reloading || !IsLoaded) return;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private static string Fingerprint(AppSettings s) => System.Text.Json.JsonSerializer.Serialize(s);

    /// <summary>Änderungen jetzt übernehmen (nur wenn sich wirklich etwas geändert hat).</summary>
    private void ApplyNow(bool force = false)
    {
        _applyTimer.Stop();
        if (_reloading) return;
        // Während ein eigener Dialog offen ist (z. B. Startprofile), erst danach übernehmen
        if (!force && OwnedWindows.Cast<Window>().Any(w => w.IsVisible))
        {
            _applyTimer.Start();
            return;
        }
        try
        {
            if (Keyboard.FocusedElement is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            var built = SettingsStore.Clone(_vm.Build());
            var fp = Fingerprint(built);
            if (fp == _lastApplied) return;
            var hotkeyError = _app.ApplySettings(built);
            _lastApplied = Fingerprint(_app.Settings);
            _vm.HotkeyError = hotkeyError;
            SaveHint.Text = hotkeyError != null
                ? "Übernommen – aber ein Tastenkürzel konnte nicht registriert werden (siehe Panel › Verhalten)."
                : $"Übernommen um {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Einstellungen konnten nicht übernommen werden", ex);
            SaveHint.Text = "Fehler beim Übernehmen: " + ex.Message;
        }
    }

    /// <summary>Im Panel wurde etwas geändert (Gruppe, Favorit, eigener Eintrag …): neuen Stand anzeigen.</summary>
    private void OnChangedOutside()
    {
        if (_reloading) return;
        Dispatcher.BeginInvoke(() => Reload(), DispatcherPriority.Background);
    }

    private void Reload(string? section = null)
    {
        _applyTimer.Stop();
        _reloading = true;
        try
        {
            var scroll = FindScroll();
            var offset = scroll?.VerticalOffset ?? 0;
            var currentSection = section ?? _vm.Section;
            _vm.Detach();
            var vm = new SettingsViewModel(_app, null) { Section = currentSection };
            Attach(vm);
            _lastApplied = Fingerprint(_app.Settings);
            Dispatcher.BeginInvoke(() => { FindScroll()?.ScrollToVerticalOffset(offset); _reloading = false; }, DispatcherPriority.Loaded);
        }
        catch
        {
            _reloading = false;
            throw;
        }
    }

    private ScrollViewer? FindScroll()
    {
        static ScrollViewer? Find(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is ScrollViewer sv && sv.Name == "PageScroll") return sv;
                if (Find(c) is { } r) return r;
            }
            return null;
        }
        return Find(this);
    }

    /// <summary>Alles auf den Stand beim Öffnen des Fensters zurücksetzen.</summary>
    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Alle Änderungen seit dem Öffnen der Einstellungen verwerfen?", "Änderungen verwerfen",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _applyTimer.Stop();
        _reloading = true;
        var restored = SettingsStore.Clone(_snapshot);
        // Laufzeitstand (Favoriten, Verlauf …) behalten
        restored.Favorites = new List<string>(_app.Settings.Favorites);
        restored.RecentRuns = _app.Settings.RecentRuns.ToList();
        restored.RunStats = new Dictionary<string, RunStat>(_app.Settings.RunStats);
        restored.Schedules = _app.Settings.Schedules.Select(x => x.Clone()).ToList();
        restored.Catalogs = _app.Settings.Catalogs.ToList();
        restored.Fingerprints = new Dictionary<string, string>(_app.Settings.Fingerprints);
        _app.ApplySettings(restored);
        _reloading = false;
        Reload();
        SaveHint.Text = "Änderungen verworfen.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>„Neu ▾“: Menü öffnen (Eintragsarten bzw. Gruppenarten).</summary>
    private void NewEntryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            _vm.Panel.Hotkey = string.Empty;
            ScheduleApply();
            return;
        }
        var text = HotkeyService.Format(Keyboard.Modifiers, key);
        if (text != null)
        {
            _vm.Panel.Hotkey = text;
            _vm.HotkeyError = null;
            ScheduleApply();
        }
    }

    // ---- Eigene Einträge aus der Liste ins Panel ziehen ---------------------------

    public const string EntryDragFormat = "PowerExec.CustomEntry";
    private Point _entryDragStart;
    private CustomEntryRowViewModel? _entryDragRow;

    private void EntryRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _entryDragStart = e.GetPosition(this);
        // Nicht beim Klick auf Haken, ⚙ oder Papierkorb
        _entryDragRow = e.OriginalSource is DependencyObject d && IsInside<ButtonBase>(d) ? null
            : (sender as FrameworkElement)?.DataContext as CustomEntryRowViewModel;
    }

    // ---- Gruppen per Drag & Drop sortieren (unter Geschwistern) -----------------------

    private const string GroupDragFormat = "PowerExec.SettingsGroup";
    private Point _groupDragStart;
    private CategoryItemViewModel? _groupDragCandidate;

    private void GroupCard_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Nicht aus Knöpfen, Auswahlfeldern oder Textfeldern heraus ziehen
        if (e.OriginalSource is DependencyObject src && FindAncestor<ButtonBase>(src) is null && FindAncestor<ComboBox>(src) is null && FindAncestor<TextBox>(src) is null)
        {
            _groupDragStart = e.GetPosition(this);
            _groupDragCandidate = (sender as FrameworkElement)?.DataContext as CategoryItemViewModel;
        }
        else _groupDragCandidate = null;
    }

    private void GroupCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _groupDragCandidate == null) return;
        var d = e.GetPosition(this) - _groupDragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _groupDragCandidate;
        _groupDragCandidate = null;
        var vm = CurrentViewModel;
        _groupDragStartX = e.GetPosition(this).X;
        vm.BeginCategoryDrag(item);
        _dragFeedback = new DragFeedback(this, vm.CategoryDragLabel, vm.CategoryDragBrush, vm.GroupDragGhost, vm.GroupDragExplain);
        var source = (UIElement)sender;
        GiveFeedbackEventHandler follow = (_, _) => _dragFeedback?.Move();
        source.GiveFeedback += follow;
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(GroupDragFormat, item), DragDropEffects.Move);
        }
        finally
        {
            source.GiveFeedback -= follow;
            _dragFeedback?.Dispose();
            _dragFeedback = null;
            vm.EndCategoryDrag();
        }
    }

    private double _groupDragStartX;
    private DragFeedback? _dragFeedback;

    /// <summary>
    /// Bereich auf dem Kartenkopf wie im Panel: oberes Viertel = davor, Mitte = hinein, unteres Viertel = dahinter.
    /// Unterhalb des Kopfs (Details, Untergruppen) null = Ziel beibehalten.
    /// </summary>
    private static Core.Config.GroupDropZone? CardZone(Border card, DragEventArgs e)
    {
        var head = (card.Child as Panel)?.Children.OfType<FrameworkElement>().FirstOrDefault();
        if (head == null || head.ActualHeight <= 0) return null;
        var y = e.GetPosition(head).Y;
        var h = head.ActualHeight;
        if (y > h) return null;
        return y < h * 0.28 ? Core.Config.GroupDropZone.Before
             : y > h * 0.72 ? Core.Config.GroupDropZone.After
             : Core.Config.GroupDropZone.Into;
    }

    /// <summary>Seitliches Ziehen um so viel wechselt die Ebene (wie die Einrückung der Untergruppen).</summary>
    private const double CategoryIndent = 24;

    private void ApplyCategoryPlan(Core.Config.GroupDropPlan? plan, DragEventArgs e)
    {
        e.Effects = plan is { IsValid: true } ? DragDropEffects.Move : DragDropEffects.None;
        _dragFeedback?.Update(plan);
        e.Handled = true;
    }

    private void GroupCard_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not CategoryItemViewModel target || !e.Data.GetDataPresent(GroupDragFormat)) return;
        var offset = (int)Math.Round((e.GetPosition(this).X - _groupDragStartX) / CategoryIndent);
        var zone = CardZone(card, e);
        ApplyCategoryPlan(zone is { } z ? CurrentViewModel.PreviewCategoryDrop(target, z, offset) : CurrentViewModel.CurrentCategoryPlan, e);
    }

    private void GroupCard_DragLeave(object sender, DragEventArgs e)
    {
        // Lücke bleibt offen, bis ein anderes Ziel gefunden ist (sonst springt die Liste)
    }

    private void GroupCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        CurrentViewModel.CommitCategoryDrop();
    }

    /// <summary>Über der offenen Lücke: Ziel beibehalten.</summary>
    private void CategoryGap_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        ApplyCategoryPlan(CurrentViewModel.CurrentCategoryPlan, e);
    }

    private void CategoryGap_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(GroupDragFormat)) return;
        e.Handled = true;
        CurrentViewModel.CommitCategoryDrop();
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private void EntryRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _entryDragRow == null) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _entryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _entryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _entryDragRow;
        _entryDragRow = null;
        ApplyNow(force: true); // Eintrag muss in der App existieren, bevor er im Panel landet
        var data = new DataObject();
        data.SetData(EntryDragFormat, row.Entry.Key);
        data.SetData(DataFormats.UnicodeText, row.Name);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private static bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var c = d; c != null; c = c is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(c) : LogicalTreeHelper.GetParent(c))
            if (c is T) return true;
        return false;
    }
}
