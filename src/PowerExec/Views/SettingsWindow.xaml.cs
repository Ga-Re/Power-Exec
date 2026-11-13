using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.UI.Services;
using PowerExec.Desktop.Services;
using PowerExec.UI.ViewModels;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Views;

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
        Infrastructure.FluentTheme.Attach(this);
        InitNavigation();
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
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        SyncNavigation();
        vm.ReopenRequested += () =>
        {
            Close();
            _app.ShowSettings();
        };
        // Marktplatz: Installiertes sofort übernehmen und alle Seiten neu lesen (neue Gruppen, Variablen, Designs)
        vm.MarketplaceChanged += () => Dispatcher.BeginInvoke(() => { ApplyNow(force: true); Reload(); });
        vm.SwitchWorkspaceRequested += name =>
        {
            ApplyNow(force: true);
            Close();
            _app.SwitchWorkspace(name);
            _app.ShowSettings(section: "workspaces");
        };
    }

    // ---- Navigationsleiste ↔ Abschnitt -----------------------------------------------------

    private bool _navSync;

    private void InitNavigation()
    {
        Infrastructure.NavigationMotion.Attach(Nav); // weiches Auf-/Zuklappen, gleitender Auswahlbalken
        Infrastructure.QuietScrollBar.Attach(PageScroll); // Scrollbalken nur, wenn nötig und gerade gebraucht
        Snackbar.Shown += OnSnackbar;
        Closed += (_, _) => Snackbar.Shown -= OnSnackbar;
        Nav.Loaded += (_, _) => SyncNavigation();
        // Klick auf einen Eintrag oder „Zurück“: Abschnitt umschalten
        Nav.SelectionChanged += (_, _) =>
        {
            if (_navSync || Nav.SelectedItem?.TargetPageTag is not { Length: > 0 } tag || tag == _vm.Section) return;
            _navSync = true;
            try { _vm.Section = tag; }
            finally { _navSync = false; }
        };
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Section)) SyncNavigation();
    }

    /// <summary>Abschnitt wurde im Code gewechselt (Suche, „Einstellungen öffnen bei …“): Auswahl der Leiste nachziehen.</summary>
    private void SyncNavigation()
    {
        if (_navSync || Nav == null || !Nav.IsLoaded || Nav.SelectedItem?.TargetPageTag == _vm.Section) return;
        _navSync = true;
        try { Nav.Navigate(_vm.Section); }
        finally { _navSync = false; }
    }

    /// <summary>Für den Selbsttest: Abschnitt der ausgewählten Navigationseintrags.</summary>
    internal string? NavigationSectionForTest => Nav.SelectedItem?.TargetPageTag;
    internal Wpf.Ui.Controls.NavigationView NavigationForTest => Nav;

    internal void ApplyNowForTest() => ApplyNow(force: true);
    internal SettingsViewModel CurrentViewModel => _vm;

    internal void ScheduleApply()
    {
        if (_reloading || !IsLoaded) return;
        _applyTimer.Stop();
        _applyTimer.Start();
        ShowSaveState(SaveState.Pending);
    }

    private enum SaveState { Saved, Pending, Warning, Error }

    /// <summary>Speicherstand in der Fußzeile: Symbol und Text.</summary>
    private void ShowSaveState(SaveState state, string? text = null)
    {
        (SaveIcon.Text, var brush, SaveHint.Text) = state switch
        {
            SaveState.Pending => ("\uE895", "Text.Secondary", text ?? L.T("Saving …")),
            SaveState.Warning => ("\uE7BA", "Status.Warning", text ?? string.Empty),
            SaveState.Error => ("\uEA39", "Status.Error", text ?? string.Empty),
            _ => ("\uE73E", "Status.Success", text ?? L.T("All changes saved"))
        };
        SaveIcon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        SaveHint.ToolTip = state == SaveState.Saved ? L.F("Last saved at {0:T}", DateTime.Now) : null;
    }

    private static string Fingerprint(AppSettings s) => System.Text.Json.JsonSerializer.Serialize(s);

    /// <summary>Änderungen jetzt übernehmen (nur wenn sich wirklich etwas geändert hat).</summary>
    internal void ApplyNow(bool force = false)
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
            if (fp == _lastApplied) { ShowSaveState(SaveState.Saved); return; }
            var hotkeyError = _app.ApplySettings(built);
            _lastApplied = Fingerprint(_app.Settings);
            _vm.HotkeyError = hotkeyError;
            if (hotkeyError != null)
                ShowSaveState(SaveState.Warning, L.T("Saved – but a hotkey couldn't be registered (see Panel › Behavior)."));
            else ShowSaveState(SaveState.Saved);
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings couldn't be applied", ex);
            ShowSaveState(SaveState.Error, L.T("Not saved: ") + ex.Message);
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

    private DispatcherTimer? _snackbarTimer;

    /// <summary>Für den Selbsttest: gerade sichtbare Snackbar-Meldung oder null.</summary>
    internal string? SnackbarForTest => SnackbarHost.Visibility == Visibility.Visible ? SnackbarText.Text : null;

    /// <summary>Kurze Rückmeldung unten einblenden und nach einigen Sekunden wieder ausblenden.</summary>
    private void OnSnackbar(string message) => Dispatcher.BeginInvoke(() =>
    {
        SnackbarText.Text = message;
        SnackbarHost.Visibility = Visibility.Visible;
        var duration = SystemParameters.ClientAreaAnimation ? Tokens.MotionFast : TimeSpan.Zero;
        SnackbarHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, duration));
        SnackbarShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, duration)
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        _snackbarTimer?.Stop();
        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(2.5 + message.Length / 30.0, 3.5, 8)) };
        _snackbarTimer.Tick += (_, _) => HideSnackbar();
        _snackbarTimer.Start();
    });

    private void HideSnackbar()
    {
        _snackbarTimer?.Stop();
        var duration = SystemParameters.ClientAreaAnimation ? Tokens.MotionNormal : TimeSpan.Zero;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, duration);
        fade.Completed += (_, _) => { if (SnackbarHost.Opacity < 0.05) SnackbarHost.Visibility = Visibility.Collapsed; };
        SnackbarHost.BeginAnimation(OpacityProperty, fade);
        SnackbarShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(12, duration));
    }

    private void SnackbarClose_Click(object sender, RoutedEventArgs e) => HideSnackbar();

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
        if (!Services.MessageDialog.Confirm(this, L.T("Discard all changes since the settings were opened?"), L.T("Discard changes"), destructive: true)) return;
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
        ShowSaveState(SaveState.Saved, L.T("Changes discarded – everything saved"));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Format, in dem ein eigener Eintrag aus der Liste der Einstellungen ins Panel gezogen wird.</summary>
    public const string EntryDragFormat = "PowerExec.CustomEntry";
}
