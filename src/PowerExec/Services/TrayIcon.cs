using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PowerExec.Services;

/// <summary>Symbol im Infobereich mit Menü (Panel öffnen, Einstellungen, Beenden).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(AppController app)
    {
        _icon = new Forms.NotifyIcon
        {
            Text = "Power-Exec",
            Icon = LoadIcon(),
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Panel öffnen", null, (_, _) => app.ShowPanelFromHotkey());
        menu.Items.Add("Ordner neu einlesen", null, (_, _) => _ = app.Repository.ScanAsync());
        menu.Items.Add("Einstellungen", null, (_, _) => app.ShowSettings());
        menu.Items.Add("Nach Updates suchen", null, async (_, _) =>
        {
            var r = await app.CheckUpdatesAsync(manual: true, includePrereleases: app.Settings.IncludePrereleases);
            if (r == null)
                ShowBalloon("Keine Updates", app.Updates.LastError != null
                    ? "Prüfung fehlgeschlagen: " + app.Updates.LastError
                    : $"Version {UpdateService.CurrentVersionText} ist aktuell.");
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => app.Exit());
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
        catch { }
        return Drawing.SystemIcons.Application;
    }

    private Action? _balloonClick;

    public void ShowBalloon(string title, string text, Action? onClick = null, bool warning = false)
    {
        _balloonClick = onClick;
        try { _icon.ShowBalloonTip(6000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info); } catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
