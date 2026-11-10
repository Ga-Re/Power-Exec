using PowerExec.Core.Localization;
namespace PowerExec.Core.Config;

/// <summary>Vorlage im Katalog („Aus Vorlagen hinzufügen“).</summary>
public sealed record CatalogTemplate(string Category, string Name, CustomEntryKind Kind, string Target, string Arguments, string Description, string Icon)
{
    public CustomEntry ToEntry() => new()
    {
        Name = Name,
        Kind = Kind,
        Target = Target,
        Arguments = Arguments,
        Description = Description,
        Icon = Icon
    };

    public string SearchText => $"{Category} {Name} {Target} {Description}";
}

/// <summary>Häufige Windows- und Admin-Werkzeuge, Einstellungsseiten und Beispiele.</summary>
public static class ToolCatalog
{
    private static string Mmc => L.T(L.T("Verwaltung (MMC)"));
    private static string Cpl => L.T(L.T("Systemsteuerung"));
    private static string Tools => L.T(L.T("Werkzeuge"));
    private static string Settings => L.T(L.T("Windows-Einstellungen"));
    private static string Examples => L.T(L.T("Beispiele"));

    private static CatalogTemplate T(string cat, string name, string target, string desc, string icon, string args = "") =>
        new(cat, name, CustomEntryKind.Tool, target, args, desc, icon);

    public static IReadOnlyList<CatalogTemplate> All { get; } = new List<CatalogTemplate>
    {
        // MMC-Snap-ins
        T(Mmc, L.T("Active Directory-Benutzer und -Computer"), "dsa.msc", L.T("Benutzer, Gruppen und Computer verwalten (RSAT)"), "fluent:people_team"),
        T(Mmc, L.T("Active Directory-Standorte und -Dienste"), "dssite.msc", L.T("Standorte, Subnetze und Replikation (RSAT)"), "fluent:globe"),
        T(Mmc, L.T("Active Directory-Domänen und -Vertrauensstellungen"), "domain.msc", L.T("Domänen und Vertrauensstellungen (RSAT)"), "fluent:link"),
        T(Mmc, L.T("ADSI-Editor"), "adsiedit.msc", L.T("Low-Level-Bearbeitung des Verzeichnisses (RSAT)"), "fluent:database"),
        T(Mmc, L.T("Gruppenrichtlinienverwaltung"), "gpmc.msc", L.T("GPOs anlegen, verknüpfen und auswerten (RSAT)"), "fluent:document_bullet_list"),
        T(Mmc, L.T("DNS-Manager"), "dnsmgmt.msc", L.T("DNS-Zonen und -Einträge (RSAT)"), "fluent:server"),
        T(Mmc, "DHCP", "dhcpmgmt.msc", L.T("DHCP-Bereiche und Reservierungen (RSAT)"), "fluent:router"),
        T(Mmc, L.T("Dienste"), "services.msc", L.T("Windows-Dienste starten, stoppen, konfigurieren"), "fluent:settings"),
        T(Mmc, L.T("Ereignisanzeige"), "eventvwr.msc", L.T("Ereignisprotokolle"), "fluent:book_open"),
        T(Mmc, L.T("Computerverwaltung"), "compmgmt.msc", L.T("Datenträger, Freigaben, Benutzer, Dienste"), "fluent:desktop"),
        T(Mmc, L.T("Geräte-Manager"), "devmgmt.msc", L.T("Hardware und Treiber"), "fluent:developer_board"),
        T(Mmc, L.T("Datenträgerverwaltung"), "diskmgmt.msc", L.T("Partitionen und Laufwerke"), "fluent:hard_drive"),
        T(Mmc, L.T("Lokale Benutzer und Gruppen"), "lusrmgr.msc", L.T("Lokale Konten verwalten"), "fluent:person_accounts"),
        T(Mmc, L.T("Aufgabenplanung"), "taskschd.msc", L.T("Geplante Aufgaben"), "fluent:calendar_clock"),
        T(Mmc, L.T("Zertifikate (Computer)"), "certlm.msc", L.T("Zertifikatspeicher des Computers"), "fluent:certificate"),
        T(Mmc, L.T("Zertifikate (Benutzer)"), "certmgr.msc", L.T("Zertifikatspeicher des Benutzers"), "fluent:certificate"),
        T(Mmc, L.T("Leistungsüberwachung"), "perfmon.msc", L.T("Leistungsindikatoren und Berichte"), "fluent:data_line"),
        T(Mmc, L.T("Freigegebene Ordner"), "fsmgmt.msc", L.T("Freigaben, Sitzungen, geöffnete Dateien"), "fluent:folder_people"),
        T(Mmc, L.T("Druckverwaltung"), "printmanagement.msc", L.T("Drucker und Treiber"), "fluent:print"),
        T(Mmc, L.T("Windows Defender Firewall mit erweiterter Sicherheit"), "wf.msc", L.T("Firewallregeln"), "fluent:shield"),
        T(Mmc, L.T("Lokale Sicherheitsrichtlinie"), "secpol.msc", L.T("Sicherheitseinstellungen des Computers"), "fluent:shield_lock"),
        T(Mmc, L.T("Editor für lokale Gruppenrichtlinien"), "gpedit.msc", L.T("Lokale Richtlinien (nicht in Home-Editionen)"), "fluent:document_edit"),
        T(Mmc, L.T("Komponentendienste"), "comexp.msc", L.T("COM+ und DCOM"), "fluent:puzzle_piece"),
        T(Mmc, L.T("Hyper-V-Manager"), "virtmgmt.msc", L.T("Virtuelle Maschinen"), "fluent:cube"),
        T(Mmc, L.T("Failovercluster-Manager"), "cluadmin.msc", L.T("Cluster verwalten (RSAT)"), "fluent:server_link"),

        // Systemsteuerung
        T(Cpl, L.T("Netzwerkverbindungen"), "control.exe", L.T("Netzwerkadapter"), "fluent:network_check", "ncpa.cpl"),
        T(Cpl, L.T("Programme und Features"), "control.exe", L.T("Software deinstallieren"), "fluent:apps_list", "appwiz.cpl"),
        T(Cpl, L.T("Systemeigenschaften"), "control.exe", L.T("Computername, Domäne, Remote, Umgebungsvariablen"), "fluent:desktop_tower", "sysdm.cpl"),
        T(Cpl, L.T("Windows-Firewall"), "control.exe", L.T("Firewall-Einstellungen"), "fluent:shield", "firewall.cpl"),
        T(Cpl, L.T("Internetoptionen"), "control.exe", L.T("Proxy, Zonen, Zertifikate"), "fluent:globe_desktop", "inetcpl.cpl"),
        T(Cpl, L.T("Datum und Uhrzeit"), "control.exe", L.T("Zeitzone und Zeitserver"), "fluent:clock", "timedate.cpl"),
        T(Cpl, L.T("Energieoptionen"), "control.exe", L.T("Energiesparpläne"), "fluent:battery_charge", "powercfg.cpl"),
        T(Cpl, "Sound", "control.exe", L.T("Wiedergabe- und Aufnahmegeräte"), "fluent:speaker_2", "mmsys.cpl"),
        T(Cpl, "Region", "control.exe", L.T("Formate und Sprache"), "fluent:local_language", "intl.cpl"),
        T(Cpl, L.T("Anmeldeinformationsverwaltung"), "control.exe", L.T("Gespeicherte Windows- und Web-Anmeldedaten"), "fluent:key", "/name Microsoft.CredentialManager"),
        T(Cpl, L.T("Windows-Features"), "optionalfeatures.exe", L.T("Windows-Funktionen ein- und ausschalten"), "fluent:apps_add_in"),
        T(Cpl, L.T("Systemsteuerung"), "control.exe", L.T("Klassische Systemsteuerung"), "fluent:options"),

        // Werkzeuge
        T(Tools, "Windows Terminal", "wt.exe", L.T("Terminal mit PowerShell, cmd und WSL"), "fluent:window_dev_tools"),
        T(Tools, "PowerShell 7", "pwsh.exe", L.T("PowerShell 7 (falls installiert)"), "fluent:code"),
        T(Tools, "Windows PowerShell", "powershell.exe", "Windows PowerShell 5.1", "fluent:code"),
        T(Tools, L.T("Eingabeaufforderung"), "cmd.exe", "cmd.exe", "fluent:window_dev_tools"),
        T(Tools, L.T("Registrierungs-Editor"), "regedit.exe", L.T("Registry bearbeiten"), "fluent:database"),
        T(Tools, "Task-Manager", "taskmgr.exe", L.T("Prozesse und Leistung"), "fluent:data_histogram"),
        T(Tools, L.T("Ressourcenmonitor"), "resmon.exe", L.T("CPU, Datenträger, Netzwerk im Detail"), "fluent:pulse"),
        T(Tools, L.T("Systeminformationen"), "msinfo32.exe", L.T("Hardware- und Softwareübersicht"), "fluent:info"),
        T(Tools, L.T("Systemkonfiguration"), "msconfig.exe", L.T("Start und Dienste"), "fluent:wrench"),
        T(Tools, L.T("Remotedesktopverbindung"), "mstsc.exe", L.T("RDP-Client"), "fluent:desktop_arrow_right"),
        T(Tools, L.T("Datenträgerbereinigung"), "cleanmgr.exe", L.T("Temporäre Dateien entfernen"), "fluent:broom"),
        T(Tools, L.T("DirectX-Diagnose"), "dxdiag.exe", L.T("Grafik und Treiber"), "fluent:games"),
        T(Tools, L.T("Windows-Version"), "winver.exe", L.T("Version und Build anzeigen"), "fluent:info"),
        T(Tools, "Explorer", "explorer.exe", L.T("Datei-Explorer"), "fluent:folder_open"),
        T(Tools, L.IsEnglish ? "Notepad" : "Editor", "notepad.exe", L.T("Texteditor"), "fluent:document_text"),
        T(Tools, L.T("Schrittaufzeichnung / Snipping Tool"), "snippingtool.exe", L.T("Bildschirmausschnitte"), "fluent:screenshot"),

        // Windows-Einstellungen (ms-settings:)
        T(Settings, L.T("Netzwerk & Internet"), "ms-settings:network-status", L.T("Netzwerkstatus"), "fluent:wifi_1"),
        T(Settings, "Proxy", "ms-settings:network-proxy", L.T("Proxyeinstellungen"), "fluent:globe_shield"),
        T(Settings, "VPN", "ms-settings:network-vpn", L.T("VPN-Verbindungen"), "fluent:shield_globe"),
        T(Settings, "Windows Update", "ms-settings:windowsupdate", L.T("Updates suchen und installieren"), "fluent:arrow_sync_circle"),
        T(Settings, L.T("Installierte Apps"), "ms-settings:appsfeatures", L.T("Apps verwalten"), "fluent:apps"),
        T(Settings, L.T("Standard-Apps"), "ms-settings:defaultapps", L.T("Standardprogramme"), "fluent:app_generic"),
        T(Settings, L.T("Anzeige"), "ms-settings:display", L.T("Auflösung, Skalierung, Monitore"), "fluent:desktop"),
        T(Settings, "Sound", "ms-settings:sound", L.T("Audiogeräte"), "fluent:speaker_2"),
        T(Settings, L.T("Bluetooth & Geräte"), "ms-settings:bluetooth", L.T("Bluetooth-Geräte"), "fluent:bluetooth"),
        T(Settings, L.T("Drucker & Scanner"), "ms-settings:printers", L.T("Drucker"), "fluent:print"),
        T(Settings, L.T("Speicher"), "ms-settings:storagesense", L.T("Speicherplatz und Speicheroptimierung"), "fluent:storage"),
        T(Settings, "Info", "ms-settings:about", L.T("Geräteinfo und Umbenennen"), "fluent:info"),
        T(Settings, L.T("Datenschutz"), "ms-settings:privacy", L.T("Datenschutz und Sicherheit"), "fluent:lock_closed"),
        T(Settings, L.T("Zugriff auf Arbeit oder Schule"), "ms-settings:workplace", "Azure AD / Entra ID, MDM", "fluent:building"),
        T(Settings, L.T("Optionale Features"), "ms-settings:optionalfeatures", L.T("RSAT und weitere Features installieren"), "fluent:apps_add_in"),

        // Beispiele für andere Arten
        new(Examples, L.T("Ticket öffnen"), CustomEntryKind.Url, "https://helpdesk.example.com/ticket/{{Ticketnummer}}", string.Empty,
            L.T("Beispiel mit Platzhalter: fragt die Ticketnummer beim Start ab"), "fluent:ticket_diagonal"),
        new(Examples, L.T("Remotedesktop auf …"), CustomEntryKind.Rdp, "{{Rechner}}", string.Empty,
            L.T("Fragt den Rechnernamen beim Start ab"), "fluent:desktop_arrow_right"),
        new(Examples, "Ping", CustomEntryKind.Snippet, string.Empty, string.Empty,
            L.T("PowerShell-Snippet mit Platzhalter"), "fluent:pulse"),
        new(Examples, L.T("Letzte Neustarts"), CustomEntryKind.Snippet, string.Empty, string.Empty,
            L.T("Ereignisse 1074/6005/6006 der letzten 7 Tage"), "fluent:arrow_clockwise"),
    };

    /// <summary>Code der Snippet-Beispiele.</summary>
    public static string SnippetCode(string name) => name switch
    {
        _ when name == "Ping" => "Test-Connection -ComputerName '{{Rechner}}' -Count 4",
        _ when name == L.T("Letzte Neustarts") => "Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 1074, 6005, 6006; StartTime = (Get-Date).AddDays(-7) } |\n    Select-Object TimeCreated, Id, Message | Format-Table -AutoSize -Wrap",
        _ => string.Empty
    };

    public static CustomEntry Create(CatalogTemplate t)
    {
        var e = t.ToEntry();
        if (t.Kind == CustomEntryKind.Snippet) e.Code = SnippetCode(t.Name);
        return e;
    }
}
