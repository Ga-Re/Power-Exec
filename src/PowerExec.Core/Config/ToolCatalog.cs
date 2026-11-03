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
    private const string Mmc = "Verwaltung (MMC)";
    private const string Cpl = "Systemsteuerung";
    private const string Tools = "Werkzeuge";
    private const string Settings = "Windows-Einstellungen";
    private const string Examples = "Beispiele";

    private static CatalogTemplate T(string cat, string name, string target, string desc, string icon, string args = "") =>
        new(cat, name, CustomEntryKind.Tool, target, args, desc, icon);

    public static IReadOnlyList<CatalogTemplate> All { get; } = new List<CatalogTemplate>
    {
        // MMC-Snap-ins
        T(Mmc, "Active Directory-Benutzer und -Computer", "dsa.msc", "Benutzer, Gruppen und Computer verwalten (RSAT)", "fluent:people_team"),
        T(Mmc, "Active Directory-Standorte und -Dienste", "dssite.msc", "Standorte, Subnetze und Replikation (RSAT)", "fluent:globe"),
        T(Mmc, "Active Directory-Domänen und -Vertrauensstellungen", "domain.msc", "Domänen und Vertrauensstellungen (RSAT)", "fluent:link"),
        T(Mmc, "ADSI-Editor", "adsiedit.msc", "Low-Level-Bearbeitung des Verzeichnisses (RSAT)", "fluent:database"),
        T(Mmc, "Gruppenrichtlinienverwaltung", "gpmc.msc", "GPOs anlegen, verknüpfen und auswerten (RSAT)", "fluent:document_bullet_list"),
        T(Mmc, "DNS-Manager", "dnsmgmt.msc", "DNS-Zonen und -Einträge (RSAT)", "fluent:server"),
        T(Mmc, "DHCP", "dhcpmgmt.msc", "DHCP-Bereiche und Reservierungen (RSAT)", "fluent:router"),
        T(Mmc, "Dienste", "services.msc", "Windows-Dienste starten, stoppen, konfigurieren", "fluent:settings"),
        T(Mmc, "Ereignisanzeige", "eventvwr.msc", "Ereignisprotokolle", "fluent:book_open"),
        T(Mmc, "Computerverwaltung", "compmgmt.msc", "Datenträger, Freigaben, Benutzer, Dienste", "fluent:desktop"),
        T(Mmc, "Geräte-Manager", "devmgmt.msc", "Hardware und Treiber", "fluent:developer_board"),
        T(Mmc, "Datenträgerverwaltung", "diskmgmt.msc", "Partitionen und Laufwerke", "fluent:hard_drive"),
        T(Mmc, "Lokale Benutzer und Gruppen", "lusrmgr.msc", "Lokale Konten verwalten", "fluent:person_accounts"),
        T(Mmc, "Aufgabenplanung", "taskschd.msc", "Geplante Aufgaben", "fluent:calendar_clock"),
        T(Mmc, "Zertifikate (Computer)", "certlm.msc", "Zertifikatspeicher des Computers", "fluent:certificate"),
        T(Mmc, "Zertifikate (Benutzer)", "certmgr.msc", "Zertifikatspeicher des Benutzers", "fluent:certificate"),
        T(Mmc, "Leistungsüberwachung", "perfmon.msc", "Leistungsindikatoren und Berichte", "fluent:data_line"),
        T(Mmc, "Freigegebene Ordner", "fsmgmt.msc", "Freigaben, Sitzungen, geöffnete Dateien", "fluent:folder_people"),
        T(Mmc, "Druckverwaltung", "printmanagement.msc", "Drucker und Treiber", "fluent:print"),
        T(Mmc, "Windows Defender Firewall mit erweiterter Sicherheit", "wf.msc", "Firewallregeln", "fluent:shield"),
        T(Mmc, "Lokale Sicherheitsrichtlinie", "secpol.msc", "Sicherheitseinstellungen des Computers", "fluent:shield_lock"),
        T(Mmc, "Editor für lokale Gruppenrichtlinien", "gpedit.msc", "Lokale Richtlinien (nicht in Home-Editionen)", "fluent:document_edit"),
        T(Mmc, "Komponentendienste", "comexp.msc", "COM+ und DCOM", "fluent:puzzle_piece"),
        T(Mmc, "Hyper-V-Manager", "virtmgmt.msc", "Virtuelle Maschinen", "fluent:cube"),
        T(Mmc, "Failovercluster-Manager", "cluadmin.msc", "Cluster verwalten (RSAT)", "fluent:server_link"),

        // Systemsteuerung
        T(Cpl, "Netzwerkverbindungen", "control.exe", "Netzwerkadapter", "fluent:network_check", "ncpa.cpl"),
        T(Cpl, "Programme und Features", "control.exe", "Software deinstallieren", "fluent:apps_list", "appwiz.cpl"),
        T(Cpl, "Systemeigenschaften", "control.exe", "Computername, Domäne, Remote, Umgebungsvariablen", "fluent:desktop_tower", "sysdm.cpl"),
        T(Cpl, "Windows-Firewall", "control.exe", "Firewall-Einstellungen", "fluent:shield", "firewall.cpl"),
        T(Cpl, "Internetoptionen", "control.exe", "Proxy, Zonen, Zertifikate", "fluent:globe_desktop", "inetcpl.cpl"),
        T(Cpl, "Datum und Uhrzeit", "control.exe", "Zeitzone und Zeitserver", "fluent:clock", "timedate.cpl"),
        T(Cpl, "Energieoptionen", "control.exe", "Energiesparpläne", "fluent:battery_charge", "powercfg.cpl"),
        T(Cpl, "Sound", "control.exe", "Wiedergabe- und Aufnahmegeräte", "fluent:speaker_2", "mmsys.cpl"),
        T(Cpl, "Region", "control.exe", "Formate und Sprache", "fluent:local_language", "intl.cpl"),
        T(Cpl, "Anmeldeinformationsverwaltung", "control.exe", "Gespeicherte Windows- und Web-Anmeldedaten", "fluent:key", "/name Microsoft.CredentialManager"),
        T(Cpl, "Windows-Features", "optionalfeatures.exe", "Windows-Funktionen ein- und ausschalten", "fluent:apps_add_in"),
        T(Cpl, "Systemsteuerung", "control.exe", "Klassische Systemsteuerung", "fluent:options"),

        // Werkzeuge
        T(Tools, "Windows Terminal", "wt.exe", "Terminal mit PowerShell, cmd und WSL", "fluent:window_dev_tools"),
        T(Tools, "PowerShell 7", "pwsh.exe", "PowerShell 7 (falls installiert)", "fluent:code"),
        T(Tools, "Windows PowerShell", "powershell.exe", "Windows PowerShell 5.1", "fluent:code"),
        T(Tools, "Eingabeaufforderung", "cmd.exe", "cmd.exe", "fluent:window_dev_tools"),
        T(Tools, "Registrierungs-Editor", "regedit.exe", "Registry bearbeiten", "fluent:database"),
        T(Tools, "Task-Manager", "taskmgr.exe", "Prozesse und Leistung", "fluent:data_histogram"),
        T(Tools, "Ressourcenmonitor", "resmon.exe", "CPU, Datenträger, Netzwerk im Detail", "fluent:pulse"),
        T(Tools, "Systeminformationen", "msinfo32.exe", "Hardware- und Softwareübersicht", "fluent:info"),
        T(Tools, "Systemkonfiguration", "msconfig.exe", "Start und Dienste", "fluent:wrench"),
        T(Tools, "Remotedesktopverbindung", "mstsc.exe", "RDP-Client", "fluent:desktop_arrow_right"),
        T(Tools, "Datenträgerbereinigung", "cleanmgr.exe", "Temporäre Dateien entfernen", "fluent:broom"),
        T(Tools, "DirectX-Diagnose", "dxdiag.exe", "Grafik und Treiber", "fluent:games"),
        T(Tools, "Windows-Version", "winver.exe", "Version und Build anzeigen", "fluent:info"),
        T(Tools, "Explorer", "explorer.exe", "Datei-Explorer", "fluent:folder_open"),
        T(Tools, "Editor", "notepad.exe", "Texteditor", "fluent:document_text"),
        T(Tools, "Schrittaufzeichnung / Snipping Tool", "snippingtool.exe", "Bildschirmausschnitte", "fluent:screenshot"),

        // Windows-Einstellungen (ms-settings:)
        T(Settings, "Netzwerk & Internet", "ms-settings:network-status", "Netzwerkstatus", "fluent:wifi_1"),
        T(Settings, "Proxy", "ms-settings:network-proxy", "Proxyeinstellungen", "fluent:globe_shield"),
        T(Settings, "VPN", "ms-settings:network-vpn", "VPN-Verbindungen", "fluent:shield_globe"),
        T(Settings, "Windows Update", "ms-settings:windowsupdate", "Updates suchen und installieren", "fluent:arrow_sync_circle"),
        T(Settings, "Installierte Apps", "ms-settings:appsfeatures", "Apps verwalten", "fluent:apps"),
        T(Settings, "Standard-Apps", "ms-settings:defaultapps", "Standardprogramme", "fluent:app_generic"),
        T(Settings, "Anzeige", "ms-settings:display", "Auflösung, Skalierung, Monitore", "fluent:desktop"),
        T(Settings, "Sound", "ms-settings:sound", "Audiogeräte", "fluent:speaker_2"),
        T(Settings, "Bluetooth & Geräte", "ms-settings:bluetooth", "Bluetooth-Geräte", "fluent:bluetooth"),
        T(Settings, "Drucker & Scanner", "ms-settings:printers", "Drucker", "fluent:print"),
        T(Settings, "Speicher", "ms-settings:storagesense", "Speicherplatz und Speicheroptimierung", "fluent:storage"),
        T(Settings, "Info", "ms-settings:about", "Geräteinfo und Umbenennen", "fluent:info"),
        T(Settings, "Datenschutz", "ms-settings:privacy", "Datenschutz und Sicherheit", "fluent:lock_closed"),
        T(Settings, "Zugriff auf Arbeit oder Schule", "ms-settings:workplace", "Azure AD / Entra ID, MDM", "fluent:building"),
        T(Settings, "Optionale Features", "ms-settings:optionalfeatures", "RSAT und weitere Features installieren", "fluent:apps_add_in"),

        // Beispiele für andere Arten
        new(Examples, "Ticket öffnen", CustomEntryKind.Url, "https://helpdesk.example.com/ticket/{{Ticketnummer}}", string.Empty,
            "Beispiel mit Platzhalter: fragt die Ticketnummer beim Start ab", "fluent:ticket_diagonal"),
        new(Examples, "Remotedesktop auf …", CustomEntryKind.Rdp, "{{Rechner}}", string.Empty,
            "Fragt den Rechnernamen beim Start ab", "fluent:desktop_arrow_right"),
        new(Examples, "Ping", CustomEntryKind.Snippet, string.Empty, string.Empty,
            "PowerShell-Snippet mit Platzhalter", "fluent:pulse"),
        new(Examples, "Letzte Neustarts", CustomEntryKind.Snippet, string.Empty, string.Empty,
            "Ereignisse 1074/6005/6006 der letzten 7 Tage", "fluent:arrow_clockwise"),
    };

    /// <summary>Code der Snippet-Beispiele.</summary>
    public static string SnippetCode(string name) => name switch
    {
        "Ping" => "Test-Connection -ComputerName '{{Rechner}}' -Count 4",
        "Letzte Neustarts" => "Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 1074, 6005, 6006; StartTime = (Get-Date).AddDays(-7) } |\n    Select-Object TimeCreated, Id, Message | Format-Table -AutoSize -Wrap",
        _ => string.Empty
    };

    public static CustomEntry Create(CatalogTemplate t)
    {
        var e = t.ToEntry();
        if (t.Kind == CustomEntryKind.Snippet) e.Code = SnippetCode(t.Name);
        return e;
    }
}
