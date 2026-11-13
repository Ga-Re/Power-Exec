using PowerExec.Core.Localization;
using System.Windows;
using PowerExec.UI.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PowerExec.Desktop.Services;

/// <summary>Symbol im Infobereich mit Menü (Panel öffnen, Einstellungen, Beenden).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(AppController app)
    {
        _icon = new Forms.NotifyIcon
        {
            Text = L.F("Power-Exec {0}", UpdateService.CurrentVersionText),
            Icon = LoadIcon(),
            Visible = true
        };

        _app = app;
        var menu = new Forms.ContextMenuStrip();
        _panels = new Forms.ToolStripMenuItem(L.T("Open panel"), null, (_, _) => app.ShowPanelFromHotkey());
        _favorites = new Forms.ToolStripMenuItem(L.T("Favorites"));
        _recent = new Forms.ToolStripMenuItem(L.T("Recently run"));
        _workspaces = new Forms.ToolStripMenuItem(L.T("Workspace"));
        menu.Items.Add(_panels);
        menu.Items.Add(_favorites);
        menu.Items.Add(_recent);
        menu.Items.Add(_workspaces);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Opening += (_, _) => RebuildQuickMenus();
        menu.Items.Add(L.T("Rescan folders"), null, (_, _) => app.Repository.ScanAsync().Forget());
        menu.Items.Add(L.T("Settings"), null, (_, _) => app.ShowSettings());
        menu.Items.Add(L.T("Check for updates"), null, async (_, _) =>
        {
            var r = await app.CheckUpdatesAsync(manual: true, includePrereleases: app.Settings.IncludePrereleases);
            if (r == null)
                ShowBalloon(L.T("No updates"), app.Updates.LastError != null
                    ? L.T("Check failed: ") + app.Updates.LastError
                    : L.F("Version {0} is up to date.", UpdateService.CurrentVersionText));
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.T("Exit"), null, (_, _) => app.Exit());
        _icon.ContextMenuStrip = menu;
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonClick;
            _balloonClick = null;
            if (action != null) Application.Current.Dispatcher.BeginInvoke(action);
        };
        _icon.BalloonTipClosed += (_, _) => _balloonClick = null;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) app.ShowPanelFromHotkey();
        };
    }

    private readonly AppController _app;
    private readonly Forms.ToolStripMenuItem _panels;
    private readonly Forms.ToolStripMenuItem _favorites;
    private readonly Forms.ToolStripMenuItem _recent;
    private readonly Forms.ToolStripMenuItem _workspaces;

    /// <summary>Schnellstart: Favoriten, zuletzt ausgeführte Einträge und Arbeitsbereiche direkt aus dem Tray.</summary>
    private void RebuildQuickMenus()
    {
        var s = _app.Settings;

        _panels.DropDownItems.Clear();
        if (_app.Hosts.Count > 1)
            foreach (var h in _app.Hosts)
            {
                var host = h;
                _panels.DropDownItems.Add(new Forms.ToolStripMenuItem(host.Config.Name.Replace("&", "&&"), null, (_, _) => Run(() => _app.ShowPanelFromHotkey(host)))
                {
                    ShortcutKeyDisplayString = string.IsNullOrWhiteSpace(host.Config.Hotkey) ? null : HotkeyService.Display(host.Config.Hotkey)
                });
            }

        _favorites.DropDownItems.Clear();
        // Dieselbe Liste wie die Gruppe „Favoriten“ im Panel und das Mini-Dock (Reihenfolge, nur vorhandene Einträge)
        var favorites = _app.Hosts.Count > 0
            ? _app.Hosts[0].ViewModel.FavoriteItems
            : s.Favorites.Select(_app.FindItem).OfType<Core.Models.LaunchItem>().ToList();
        foreach (var item in favorites)
            _favorites.DropDownItems.Add(Label(item), null, (_, _) => Run(() => _app.RunProfile(item, null)));
        _favorites.Enabled = _favorites.DropDownItems.Count > 0;

        _recent.DropDownItems.Clear();
        foreach (var r in s.RecentRuns.Take(Math.Max(5, s.RecentRunsCount)))
        {
            var item = _app.FindItem(r.Path);
            if (item == null) continue;
            var profile = Core.Profiles.ProfileResolver.GetProfiles(s, item)
                .FirstOrDefault(p => p.Name.Equals(r.ProfileName, StringComparison.CurrentCultureIgnoreCase) || (p.IsImplicit && r.ProfileName == "Standard"));
            var text = Label(item) + (profile != null && !profile.IsImplicit ? $"  ·  {profile.Name}" : string.Empty);
            _recent.DropDownItems.Add(text, null, (_, _) => Run(() => _app.RunProfile(item, profile)));
        }
        _recent.Enabled = _recent.DropDownItems.Count > 0;

        _workspaces.DropDownItems.Clear();
        foreach (var w in s.Workspaces)
        {
            var name = w.Name;
            var mi = new Forms.ToolStripMenuItem(name, null, (_, _) => Run(() => _app.SwitchWorkspace(name)))
            {
                Checked = name.Equals(s.ActiveWorkspace, StringComparison.CurrentCultureIgnoreCase)
            };
            _workspaces.DropDownItems.Add(mi);
        }
        _workspaces.Visible = s.Workspaces.Count > 1;
    }

    private string Label(Core.Models.LaunchItem item)
    {
        var dn = _app.Settings.FindEntry(item.FullPath)?.DisplayName;
        var text = string.IsNullOrWhiteSpace(dn) ? item.DisplayName : dn!;
        return text.Replace("&", "&&");
    }

    private static void Run(Action action) => Application.Current.Dispatcher.BeginInvoke(action);

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PowerExec.ico"));
            if (info != null)
            {
                using var s = info.Stream;
                return new Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex) { AppLog.Warn($"Tray icon could not be loaded, using the default application icon: {ex.Message}"); }
        return Drawing.SystemIcons.Application;
    }

    private Action? _balloonClick;

    public void ShowBalloon(string title, string text, Action? onClick = null, bool warning = false)
    {
        _balloonClick = onClick;
        try { _icon.ShowBalloonTip(6000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info); } catch { /* Benachrichtigungen können in Windows abgeschaltet sein – dann erscheint keine */ }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
