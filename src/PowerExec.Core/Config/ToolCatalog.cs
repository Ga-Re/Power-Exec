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
    private static string Mmc => L.T(L.T("Administration (MMC)"));
    private static string Cpl => L.T(L.T("Control Panel"));
    private static string Tools => L.T(L.T("Tools"));
    private static string Settings => L.T(L.T("Windows Settings"));
    private static string Examples => L.T(L.T("Examples"));

    private static CatalogTemplate T(string cat, string name, string target, string desc, string icon, string args = "") =>
        new(cat, name, CustomEntryKind.Tool, target, args, desc, icon);

    public static IReadOnlyList<CatalogTemplate> All { get; } = new List<CatalogTemplate>
    {
        // MMC-Snap-ins
        T(Mmc, L.T("Active Directory Users and Computers"), "dsa.msc", L.T("Manage users, groups and computers (RSAT)"), "fluent:people_team"),
        T(Mmc, L.T("Active Directory Sites and Services"), "dssite.msc", L.T("Sites, subnets and replication (RSAT)"), "fluent:globe"),
        T(Mmc, L.T("Active Directory Domains and Trusts"), "domain.msc", L.T("Domains and trusts (RSAT)"), "fluent:link"),
        T(Mmc, L.T("ADSI Edit"), "adsiedit.msc", L.T("Low-level directory editing (RSAT)"), "fluent:database"),
        T(Mmc, L.T("Group Policy Management"), "gpmc.msc", L.T("Create, link and evaluate GPOs (RSAT)"), "fluent:document_bullet_list"),
        T(Mmc, L.T("DNS Manager"), "dnsmgmt.msc", L.T("DNS zones and records (RSAT)"), "fluent:server"),
        T(Mmc, "DHCP", "dhcpmgmt.msc", L.T("DHCP scopes and reservations (RSAT)"), "fluent:router"),
        T(Mmc, L.T("Services"), "services.msc", L.T("Start, stop and configure Windows services"), "fluent:settings"),
        T(Mmc, L.T("Event Viewer"), "eventvwr.msc", L.T("Event logs"), "fluent:book_open"),
        T(Mmc, L.T("Computer Management"), "compmgmt.msc", L.T("Disks, shares, users, services"), "fluent:desktop"),
        T(Mmc, L.T("Device Manager"), "devmgmt.msc", L.T("Hardware and drivers"), "fluent:developer_board"),
        T(Mmc, L.T("Disk Management"), "diskmgmt.msc", L.T("Partitions and drives"), "fluent:hard_drive"),
        T(Mmc, L.T("Local Users and Groups"), "lusrmgr.msc", L.T("Manage local accounts"), "fluent:person_accounts"),
        T(Mmc, L.T("Task Scheduler"), "taskschd.msc", L.T("Scheduled tasks"), "fluent:calendar_clock"),
        T(Mmc, L.T("Certificates (computer)"), "certlm.msc", L.T("Computer certificate store"), "fluent:certificate"),
        T(Mmc, L.T("Certificates (user)"), "certmgr.msc", L.T("User certificate store"), "fluent:certificate"),
        T(Mmc, L.T("Performance Monitor"), "perfmon.msc", L.T("Performance counters and reports"), "fluent:data_line"),
        T(Mmc, L.T("Shared Folders"), "fsmgmt.msc", L.T("Shares, sessions, open files"), "fluent:folder_people"),
        T(Mmc, L.T("Print Management"), "printmanagement.msc", L.T("Printers and drivers"), "fluent:print"),
        T(Mmc, L.T("Windows Defender Firewall with Advanced Security"), "wf.msc", L.T("Firewall rules"), "fluent:shield"),
        T(Mmc, L.T("Local Security Policy"), "secpol.msc", L.T("Computer security settings"), "fluent:shield_lock"),
        T(Mmc, L.T("Local Group Policy Editor"), "gpedit.msc", L.T("Local policies (not in Home editions)"), "fluent:document_edit"),
        T(Mmc, L.T("Component Services"), "comexp.msc", L.T("COM+ and DCOM"), "fluent:puzzle_piece"),
        T(Mmc, L.T("Hyper-V Manager"), "virtmgmt.msc", L.T("Virtual machines"), "fluent:cube"),
        T(Mmc, L.T("Failover Cluster Manager"), "cluadmin.msc", L.T("Manage clusters (RSAT)"), "fluent:server_link"),

        // Systemsteuerung
        T(Cpl, L.T("Network Connections"), "control.exe", L.T("Network adapters"), "fluent:network_check", "ncpa.cpl"),
        T(Cpl, L.T("Programs and Features"), "control.exe", L.T("Uninstall software"), "fluent:apps_list", "appwiz.cpl"),
        T(Cpl, L.T("System Properties"), "control.exe", L.T("Computer name, domain, remote, environment variables"), "fluent:desktop_tower", "sysdm.cpl"),
        T(Cpl, L.T("Windows Firewall"), "control.exe", L.T("Firewall settings"), "fluent:shield", "firewall.cpl"),
        T(Cpl, L.T("Internet Options"), "control.exe", L.T("Proxy, zones, certificates"), "fluent:globe_desktop", "inetcpl.cpl"),
        T(Cpl, L.T("Date and Time"), "control.exe", L.T("Time zone and time server"), "fluent:clock", "timedate.cpl"),
        T(Cpl, L.T("Power Options"), "control.exe", L.T("Power plans"), "fluent:battery_charge", "powercfg.cpl"),
        T(Cpl, "Sound", "control.exe", L.T("Playback and recording devices"), "fluent:speaker_2", "mmsys.cpl"),
        T(Cpl, "Region", "control.exe", L.T("Formats and language"), "fluent:local_language", "intl.cpl"),
        T(Cpl, L.T("Credential Manager"), "control.exe", L.T("Saved Windows and web credentials"), "fluent:key", "/name Microsoft.CredentialManager"),
        T(Cpl, L.T("Windows Features"), "optionalfeatures.exe", L.T("Turn Windows features on or off"), "fluent:apps_add_in"),
        T(Cpl, L.T("Control Panel"), "control.exe", L.T("Classic Control Panel"), "fluent:options"),

        // Werkzeuge
        T(Tools, "Windows Terminal", "wt.exe", L.T("Terminal with PowerShell, cmd and WSL"), "fluent:window_dev_tools"),
        T(Tools, "PowerShell 7", "pwsh.exe", L.T("PowerShell 7 (if installed)"), "fluent:code"),
        T(Tools, "Windows PowerShell", "powershell.exe", "Windows PowerShell 5.1", "fluent:code"),
        T(Tools, L.T("Command Prompt"), "cmd.exe", "cmd.exe", "fluent:window_dev_tools"),
        T(Tools, L.T("Registry Editor"), "regedit.exe", L.T("Edit the registry"), "fluent:database"),
        T(Tools, "Task-Manager", "taskmgr.exe", L.T("Processes and performance"), "fluent:data_histogram"),
        T(Tools, L.T("Resource Monitor"), "resmon.exe", L.T("CPU, disk, network in detail"), "fluent:pulse"),
        T(Tools, L.T("System Information"), "msinfo32.exe", L.T("Hardware and software overview"), "fluent:info"),
        T(Tools, L.T("System Configuration"), "msconfig.exe", L.T("Startup and services"), "fluent:wrench"),
        T(Tools, L.T("Remote Desktop Connection"), "mstsc.exe", L.T("RDP client"), "fluent:desktop_arrow_right"),
        T(Tools, L.T("Disk Cleanup"), "cleanmgr.exe", L.T("Remove temporary files"), "fluent:broom"),
        T(Tools, L.T("DirectX Diagnostics"), "dxdiag.exe", L.T("Graphics and drivers"), "fluent:games"),
        T(Tools, L.T("Windows version"), "winver.exe", L.T("Show version and build"), "fluent:info"),
        T(Tools, "Explorer", "explorer.exe", L.T("File Explorer"), "fluent:folder_open"),
        T(Tools, L.IsEnglish ? "Notepad" : "Editor", "notepad.exe", L.T("Text editor"), "fluent:document_text"),
        T(Tools, L.T("Steps Recorder / Snipping Tool"), "snippingtool.exe", L.T("Screenshots"), "fluent:screenshot"),

        // Windows-Einstellungen (ms-settings:)
        T(Settings, L.T("Network & internet"), "ms-settings:network-status", L.T("Network status"), "fluent:wifi_1"),
        T(Settings, "Proxy", "ms-settings:network-proxy", L.T("Proxy settings"), "fluent:globe_shield"),
        T(Settings, "VPN", "ms-settings:network-vpn", L.T("VPN connections"), "fluent:shield_globe"),
        T(Settings, "Windows Update", "ms-settings:windowsupdate", L.T("Check for and install updates"), "fluent:arrow_sync_circle"),
        T(Settings, L.T("Installed apps"), "ms-settings:appsfeatures", L.T("Manage apps"), "fluent:apps"),
        T(Settings, L.T("Default apps"), "ms-settings:defaultapps", L.T("Default programs"), "fluent:app_generic"),
        T(Settings, L.T("Display"), "ms-settings:display", L.T("Resolution, scaling, monitors"), "fluent:desktop"),
        T(Settings, "Sound", "ms-settings:sound", L.T("Audio devices"), "fluent:speaker_2"),
        T(Settings, L.T("Bluetooth & devices"), "ms-settings:bluetooth", L.T("Bluetooth devices"), "fluent:bluetooth"),
        T(Settings, L.T("Printers & scanners"), "ms-settings:printers", L.T("Printers"), "fluent:print"),
        T(Settings, L.T("Storage"), "ms-settings:storagesense", L.T("Disk space and Storage Sense"), "fluent:storage"),
        T(Settings, "Info", "ms-settings:about", L.T("Device info and rename"), "fluent:info"),
        T(Settings, L.T("Privacy"), "ms-settings:privacy", L.T("Privacy and security"), "fluent:lock_closed"),
        T(Settings, L.T("Access work or school"), "ms-settings:workplace", "Azure AD / Entra ID, MDM", "fluent:building"),
        T(Settings, L.T("Optional features"), "ms-settings:optionalfeatures", L.T("Install RSAT and other features"), "fluent:apps_add_in"),

        // Beispiele für andere Arten
        new(Examples, L.T("Open ticket"), CustomEntryKind.Url, "https://helpdesk.example.com/ticket/{{Ticketnummer}}", string.Empty,
            L.T("Example with placeholder: asks for the ticket number at launch"), "fluent:ticket_diagonal"),
        new(Examples, L.T("Remote Desktop to …"), CustomEntryKind.Rdp, "{{Rechner}}", string.Empty,
            L.T("Asks for the computer name at launch"), "fluent:desktop_arrow_right"),
        new(Examples, "Ping", CustomEntryKind.Snippet, string.Empty, string.Empty,
            L.T("PowerShell snippet with placeholder"), "fluent:pulse"),
        new(Examples, L.T("Recent restarts"), CustomEntryKind.Snippet, string.Empty, string.Empty,
            L.T("Events 1074/6005/6006 of the last 7 days"), "fluent:arrow_clockwise"),
    };

    /// <summary>Code der Snippet-Beispiele.</summary>
    public static string SnippetCode(string name) => name switch
    {
        _ when name == "Ping" => "Test-Connection -ComputerName '{{Rechner}}' -Count 4",
        _ when name == L.T("Recent restarts") => "Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 1074, 6005, 6006; StartTime = (Get-Date).AddDays(-7) } |\n    Select-Object TimeCreated, Id, Message | Format-Table -AutoSize -Wrap",
        _ => string.Empty
    };

    public static CustomEntry Create(CatalogTemplate t)
    {
        var e = t.ToEntry();
        if (t.Kind == CustomEntryKind.Snippet) e.Code = SnippetCode(t.Name);
        return e;
    }
}
