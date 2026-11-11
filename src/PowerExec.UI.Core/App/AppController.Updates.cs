using PowerExec.Core.Localization;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Profiles;
using PowerExec.Core.Search;
using PowerExec.Core.Updates;
using PowerExec.UI.Infrastructure;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI;

/// <content>Updates suchen und einspielen, Neustart.</content>
public partial class AppController
{
    // ---- Updates --------------------------------------------------------

    public async Task<ReleaseInfo?> CheckUpdatesAsync(bool manual, bool includePrereleases)
    {
        var r = await Updates.CheckAsync(includePrereleases);
        if (r != null)
        {
            Update.Title = L.F("Version {0} is available", r.Version.ToString(3));
            Update.Subtitle = L.F("Installed: {0} · from GitHub Releases", UpdateService.CurrentVersionText);
            Update.Available = true;
            if (!manual) Ui.Notify(L.T("Update available"), L.F("Power-Exec {0} can be installed. Open the panel.", r.Version.ToString(3)));
        }
        return r;
    }

    public async Task InstallUpdateAsync()
    {
        var r = Updates.Available;
        if (r == null) return;
        Update.Busy = true;
        Update.Subtitle = L.T("Downloading …");
        string? error;
        try
        {
            error = await Updates.DownloadAndPrepareAsync(r, new Progress<double>(q => Update.Subtitle = L.F("Downloading … {0:P0}", q)));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        Update.Busy = false;
        if (error == null)
        {
            Exit();
            return;
        }
        Update.Subtitle = L.T("Update failed");
        if (Dialogs.Confirm(error + L.T("\n\nOpen the release page in the browser?"), "Update"))
            Shell.OpenUrl(r.HtmlUrl);
    }

    /// <summary>Power-Exec wird gerade beendet (z. B. für ein Update).</summary>
    public bool IsExiting { get; private set; }

    /// <summary>Neu starten: kurz warten (bis diese Instanz beendet ist), dann dieselbe EXE erneut öffnen.</summary>
    public void RestartApp()
    {
        if (Environment.ProcessPath is not { Length: > 0 } exe) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & start \"\" \"{exe}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Restart failed", ex);
            return;
        }
        Exit();
    }
}
