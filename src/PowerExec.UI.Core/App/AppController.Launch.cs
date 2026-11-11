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

/// <content>Starten: lokal, als Administrator, als anderer Benutzer, remote; Rückfragen, Verlauf, Startprofile.</content>
public partial class AppController
{
    /// <summary>Standardaktion im Panel: Standardprofil des Eintrags ausführen.</summary>
    public void Run(LaunchItem item, ExecutionMode mode) =>
        RunProfile(item, null, mode == ExecutionMode.CurrentUser ? null : mode);

    /// <summary>
    /// Startet ein Profil. <paramref name="overrideMode"/> erzwingt einmalig Admin oder anderes Konto
    /// (Knöpfe „Admin“ und „Als …“ im Panel).
    /// </summary>
    /// <param name="record">false = Test aus dem Eintragsdialog: nicht in „Zuletzt ausgeführt“, ungespeicherten Stand verwenden.</param>
    public void RunProfile(LaunchItem item, LaunchProfile? profile, ExecutionMode? overrideMode = null, bool record = true)
    {
        if (item.Source is { Kind: CustomEntryKind.Workflow } workflow) { RunWorkflowEntry(item, workflow); return; }
        // „Nach Profil fragen“: Profil (und Platzhalter-Werte) in einem Fenster, nur beim normalen Start mit mehreren Profilen
        IReadOnlyDictionary<string, string>? presetValues = null;
        if (profile == null && record && ProfileResolver.AsksProfile(Settings, item))
        {
            var current = Refresh(item);
            var placeholders = !current.PlaceholdersResolved && current.Source is { NeedsResolving: true, CopiesToClipboard: false } src
                ? src.Placeholders : (IReadOnlyList<Placeholder>)Array.Empty<Placeholder>();
            var key = current.Source?.Key ?? item.FullPath;
            var answer = WithDialog(() => Dialogs.AskProfile(item.DisplayName, placeholders,
                name => ParameterHistory.Get(Settings, key, name), ProfileResolver.GetProfiles(Settings, item), ProfileResolver.GetDefault(Settings, item)));
            if (answer is not { } a) return;
            profile = a.Profile;
            if (placeholders.Count > 0) presetValues = a.Values;
        }
        profile ??= ProfileResolver.GetDefault(Settings, item);
        item = WithProfileOptions(record ? Refresh(item) : item, profile); // Parameter könnten sich geändert haben; Browser, Anzeige … des Profils
        if (item.Source?.CopiesToClipboard == true)
        {
            CopySnippet(item, record);
            return;
        }
        if (ResolvePlaceholders(item, presetValues) is not { } resolvedItem) return;
        item = resolvedItem;
        bool remote = item.SupportsRemote && profile.Target != ProfileTarget.Local;
        var mode = Requirements.ModeFor(item, overrideMode ?? ProfileMode(profile), remote);
        var targets = remote ? MachineList.Resolve(Settings, profile.Machines) : Array.Empty<string>();
        if (remote && profile.Target == ProfileTarget.Fixed && targets.Count == 0)
        {
            OpenRunDialog(item, mode, remote, profile); // z. B. leere Rechnergruppe
            return;
        }

        if (item.IsPowerShell)
        {
            Dictionary<string, object> values = new();
            var errors = ParameterValueConverter.TryConvert(item.Parameters, ProfileResolver.InitialValues(profile, item), out values);
            if (ProfileResolver.NeedsDialog(profile, item) || errors.Count > 0)
            {
                OpenRunDialog(item, mode, remote, profile);
                return;
            }
            var request = BuildRequest(item, profile, mode, values);
            var raw = ProfileResolver.InitialValues(profile, item);
            if (!ConfirmRun(item, profile, targets.Count, null)) return;
            HidePanels();
            if (record) RecordRun(item, profile);
            AppLog.Info($"Launch: {item.FullPath} · profile {profile.Name} · {(remote ? targets.Count + " computers" : "local")}{(mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
            if (remote)
            {
                // Gruppenmitglieder nicht in „Zuletzt verwendet“ übernehmen, nur direkt eingetragene Rechner.
                MachineList.MarkUsed(Settings, profile.Machines.Where(n => !n.TrimStart().StartsWith('@')));
                SaveSettings();
                StartRemote(new ExecutionRequest
                {
                    Item = request.Item,
                    Mode = request.Mode,
                    Engine = Requirements.EngineFor(item, profile.Engine),
                    NoExit = request.NoExit,
                    NoProfile = request.NoProfile,
                    ExecutionPolicy = request.ExecutionPolicy,
                    Parameters = request.Parameters,
                    ResolvedPath = request.ResolvedPath,
                    Targets = targets.ToList(),
                    CopyFolder = item.CopyFolderByDefault
                }, null, raw, profile);
            }
            else
            {
                var origin = HistoryOrigin;
                StartLocalTracked(item, request, () => BeginHistory(item, profile, mode, request.Engine, null, raw, origin), null);
            }
            return;
        }

        if (!ConfirmRun(item, profile, 0, null)) return;
        HidePanels();
        try { item.Source?.PrepareLaunch(); } // z. B. .rdp-Datei mit Benutzer/Gateway (ohne Kennwort)
        catch (Exception ex) { AppLog.Error("Preparing the launch failed", ex); }
        var plainOrigin = HistoryOrigin;
        if (StartLocalTracked(item, BuildRequest(item, profile, mode, new Dictionary<string, object>()),
                () => BeginHistory(item, profile, mode, PsEngine.WindowsPowerShell, null, null, plainOrigin), null))
        {
            if (record) RecordRun(item, profile);
            AppLog.Info($"Launch: {item.FullPath} · profile {profile.Name}{(mode == ExecutionMode.Administrator ? " · Admin" : string.Empty)}");
        }
    }

    /// <summary>Text-Parameterwerte für die Vorschläge im Ausführen-Dialog merken (nie geheime Werte).</summary>
    public void RecordParameters(LaunchItem item, IReadOnlyDictionary<string, string?> raw)
    {
        foreach (var p in item.Parameters)
        {
            if (p.Kind is ParameterKind.Secure or ParameterKind.Switch or ParameterKind.Boolean or ParameterKind.Choice) continue;
            if (p.IsSecure) continue;
            if (raw.TryGetValue(p.Name, out var v) && !string.IsNullOrWhiteSpace(v) && v != p.DefaultValueLiteral)
                ParameterHistory.Add(Settings, item.FullPath, p.Name, v);
        }
    }

    /// <summary>Sicherheitsabfrage, falls im Profil eingestellt.</summary>
    public bool ConfirmRun(LaunchItem item, LaunchProfile profile, int targetCount, object? owner)
    {
        if (!profile.ConfirmBeforeRun) return true;
        var name = Settings.FindEntry(item.FullPath)?.DisplayName is { Length: > 0 } dn ? dn : item.DisplayName;
        var where = targetCount > 0 ? (targetCount == 1 ? L.T(" on 1 computer") : L.F(" on {0} computers", targetCount)) : string.Empty;
        var text = L.F("Really run “{0}” with the profile “{1}”{2}?", name, profile.Name, where) +
                   (profile.RunAsAdmin ? L.T("\n\nIt runs with administrator rights.") : string.Empty);
        return Dialogs.Confirm(text, L.T("Confirm run"), owner: owner, defaultNo: true);
    }

    /// <summary>Entfernt die Zonen-Kennung („aus dem Internet“) einer Datei.</summary>
    public void UnblockItem(LaunchItem item)
    {
        if (WindowsMetadata.Unblock(item.FullPath))
        {
            AppLog.Info(string.Format("Unblocked: {0}", item.FullPath));
            Repository.Reload(item);
            Repository.ScanAsync().Forget();
        }
        else
        {
            Dialogs.Warn(L.F("{0} couldn't be unblocked (missing write permissions?).", item.FileName), L.T("Unblock"));
        }
    }

    /// <summary>Für „Zuletzt ausgeführt“ merken.</summary>
    public void RecordRun(LaunchItem item, LaunchProfile profile)
    {
        RecentRunList.Add(Settings, item.FullPath, profile.IsImplicit ? "Standard" : profile.Name);
        RunStats.Record(Settings, item.FullPath);
        CommitUserChange();
    }

    /// <summary>Eigener Eintrag mit den Startoptionen des Profils (Browser und Browserprofil, RDP-Anzeige, SSH-Optionen …).</summary>
    private LaunchItem WithProfileOptions(LaunchItem item, LaunchProfile profile)
    {
        if (profile.Options == null || item.Source == null || item.PlaceholdersResolved) return item;
        var result = DescribeCustom(item.Source.WithProfile(profile));
        return result;
    }

    /// <summary>Startmodus eines Profils: Administrator, anderer Benutzer (Anmeldung beim Start) oder aktuelles Konto.</summary>
    public static ExecutionMode ProfileMode(LaunchProfile p) => ProfileResolver.ModeOf(p);

    private ExecutionRequest BuildRequest(LaunchItem item, LaunchProfile profile, ExecutionMode mode, Dictionary<string, object> values) => new()
    {
        Item = item,
        Mode = mode,
        Engine = Launcher.EffectiveEngine(Requirements.EngineFor(item, profile.Engine)),
        NoExit = profile.NoExit,
        NoProfile = profile.NoProfile,
        ExecutionPolicy = Settings.ExecutionPolicy,
        Parameters = values,
        ExtraArguments = (item.LaunchArguments + " " + profile.Arguments).Trim(),
        ResolvedPath = item.LaunchTarget ?? PathResolver.ToUnc(item.FullPath)
    };

    public void OpenRunDialog(LaunchItem item, ExecutionMode mode, bool remote, LaunchProfile? profile = null)
    {
        item = Refresh(item);
        if (ResolvePlaceholders(item) is not { } resolvedItem) return;
        item = resolvedItem;
        HidePanels();
        Ui.ShowRun(new RunViewModel(this, item, mode, remote, profile));
    }

    /// <summary>Profil-Dialog aus dem Panel: arbeitet direkt auf den Einstellungen und speichert sofort.</summary>
    public void ShowProfiles(LaunchItem item)
    {
        if (item.Source is { Kind: CustomEntryKind.Workflow } wf) { EditWorkflow(wf); return; }
        HidePanels(force: true);
        item = Refresh(item);
        Ui.ShowProfiles(new ProfilesViewModel(this, item, Settings, saveImmediately: true));
    }

    /// <summary>Wird vom Profil-Dialog nach dem Speichern aufgerufen.</summary>
    public void OnProfilesSaved()
    {
        CommitUserChange();
    }

    /// <summary>Startet die Remote-/Massenausführung und öffnet die Ergebnisliste.</summary>
    /// <param name="raw">Parameterwerte als Text für den Verlauf (null = aus den typisierten Werten).</param>
    public bool StartRemote(ExecutionRequest request, object? owner, IReadOnlyDictionary<string, string?>? raw = null, LaunchProfile? profile = null)
    {
        RunCredential? cred = null;
        if (request.Mode == ExecutionMode.OtherUser)
        {
            cred = Ui.AskCredential(owner, request.Item.DisplayName, remote: true);
            if (cred == null) return false;
        }
        var account = cred?.Account ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        var vm = new ResultsViewModel(this, request, account);
        var history = BeginHistory(request.Item, profile, request.Mode, request.Engine, request.Targets,
            raw ?? RunHistory.RawFrom(request.Parameters), HistoryOrigin);
        IAppWindow? w = null;
        var activity = Activities.TrackRemote(vm, () =>
        {
            HidePanels();
            w?.BringToFront();
        });
        activity.Completed += () => CompleteHistory(history,
            vm.RunnerError != null ? HistoryResult.Error : vm.ErrorCount + vm.UnreachableCount > 0 ? HistoryResult.Warning : HistoryResult.Success,
            activity.Detail, null,
            vm.Rows.Where(r => r.Status is RemoteStatus.Error or RemoteStatus.Unreachable).Select(r => r.Computer), vm.LogFile);
        vm.Finished += () =>
        {
            if (!Settings.NotifyOnFinish || w?.IsActive == true) return;
            bool problems = vm.ErrorCount + vm.UnreachableCount > 0 || vm.RunnerError != null;
            Ui.Notify(L.F("{0} finished", request.Item.DisplayName),
                vm.RunnerError ?? L.F("{0} succeeded · {1} errors · {2} unreachable", vm.SuccessCount, vm.ErrorCount, vm.UnreachableCount),
                () =>
                {
                    if (w?.IsOpen == true) w.BringToFront();
                },
                warning: problems);
        };
        w = Ui.ShowResults(vm);
        vm.Start(cred);
        return true;
    }
}
