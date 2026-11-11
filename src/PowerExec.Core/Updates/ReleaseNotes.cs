using PowerExec.Core.Localization;
namespace PowerExec.Core.Updates;

/// <summary>Neuerungen einer Version (für die Karte „Neu in dieser Version“ im Panel).</summary>
public sealed record ReleaseNote(Version Version, string Title, IReadOnlyList<string> Highlights);

public static class ReleaseNotes
{
    public static readonly IReadOnlyList<ReleaseNote> All = new[]
    {
        new ReleaseNote(new Version(0, 31, 1), L.T("No frame next to the retracted panel"), new[]
        {
            L.T("When retracted, the panel no longer leaves a frame on a neighboring monitor or over the taskbar")
        }),
        new ReleaseNote(new Version(0, 31, 0), L.T("New design preview on your own desktop"), new[]
        {
            L.T("Design previews show your real desktop background, a window in the Windows theme and the panel on the right with search box, lines, buttons, accent color and glowing hover zone – the same under Appearance and in the marketplace"),
            L.T("The design templates show the current design even when collapsed"),
            L.T("A design deleted under Appearance shows as not installed in the marketplace again; the delete button glows red when hovered")
        }),
        new ReleaseNote(new Version(0, 30, 0), L.T("Clearer dialogs, three base designs, translations based on English"), new[]
        {
            L.T("Buttons like “Apply” are clearly readable again in the variable and group dialogs; the variable description has several lines"),
            L.T("When downloading from the marketplace you choose an existing group from a list or create a new one"),
            L.T("Three base designs (Fluent, Glass, OLED); Fluent Clean, Nordic Frost, Espresso, Graphite, Paper and Midnight are now in the marketplace. Design tiles have the same size, the delete button sits inside the tile"),
            L.T("The type of an existing entry can no longer be changed – only while creating it"),
            L.T("Translations are based on English: every other language is a file under Translations (e.g. de.json) and is offered automatically")
        }),
        new ReleaseNote(new Version(0, 29, 0), L.T("Marketplace as its own area, Fluent messages"), new[]
        {
            L.T("The marketplace is its own area in the settings with Entries, Scripts, Designs, Installed and Sources; in the repository, categories are folders with the groups below them"),
            L.T("Downloaded entries now reliably end up in the chosen group"),
            L.T("Preview: designs show a mini panel in their colors, entries and scripts expand their target or code and required variables"),
            L.T("Required variables come with their type (text, secret, environment variable, PowerShell …) and are created in the variable dialog"),
            L.T("Messages and confirmations appear as a Fluent dialog instead of a Windows message box"),
            L.T("Backgrounds are now called “Glass (blur)” and “Transparent”; the translations are in the repository under Translations (de.json, en.json)")
        }),
        new ReleaseNote(new Version(0, 28, 0), L.T("English user interface"), new[]
        {
            L.T("Power-Exec is now also available in English: the language follows Windows (German on German Windows, otherwise English) and can be changed under Settings › General › Language"),
            L.T("All windows, messages, tool templates and release notes are translated; the search understands English filters like type:, group: and is: – the German ones still work"),
            L.T("After changing the language, Power-Exec restarts at the push of a button")
        }),
        new ReleaseNote(new Version(0, 27, 0), L.T("Marketplace from Git folders, variables table, snackbars"), new[]
        {
            L.T("Marketplace: folders from the Git repository instead of packages – expandable, with tabs Entries · Scripts · Designs · Installed and a compact list"),
            L.T("Check whole folders or individual files to download or uninstall them; entries and scripts end up in your custom entries in the group of your choice (suggested: the folder name)"),
            L.T("Uninstalling removes entries, designs and script files again; entries you changed only after confirmation. Updates stay in their group"),
            L.T("Variables as a compact table with search, checkboxes and their own edit dialog; built-in variables are now called {{$Date}}, {{$Time}}, {{$User}}, {{$Computer}} and {{$Clipboard}}"),
            L.T("Success messages briefly appear as a snackbar and disappear again; errors stay"),
            L.T("The favorites star follows the accent color")
        }),
        new ReleaseNote(new Version(0, 26, 0), L.T("Custom marketplace sources"), new[]
        {
            L.T("Marketplace › Sources: simply add more marketplaces by repository URL (GitHub or GitLab, subfolders too) – e.g. your team's catalog"),
            L.T("Third-party sources are confirmed as trusted once before the first installation; then the list shows their status and the source filter narrows the items"),
            L.T("An unreachable source doesn't hold up the others; removing a source keeps what's already installed")
        }),
        new ReleaseNote(new Version(0, 25, 0), L.T("Variables, marketplace and tidier settings"), new[]
        {
            L.T("New: marketplace (Content › Marketplace) – install packages (e.g. Matrix42, research), links (Google, Microsoft Learn, VirusTotal, IP Whois, GitHub/GitLab issues), designs and the sample scripts with one click; a preview shows the content first, required variables are asked for, changes appear as “Update available”"),
            L.T("New: variables (Content › Variables). {{$CompanyName}} is filled into texts, links, commands, targets, PowerShell code and script parameters without asking – as fixed text, per workspace, secret (encrypted), from an environment variable or as the result of a PowerShell command (with cache)"),
            L.T("Date, time, user, computer and clipboard are built in, the date and time format is adjustable; normal {{placeholders}} are still asked for before launch"),
            L.T("In the entry editor: suggestions when typing {{$, insert by click and a notice for variables that don't exist; the preview in the panel shows the filled-in values"),
            L.T("Settings: consistent cards, compact type filters under “Custom entries”, colored feedback (InfoBar), accent bar in the navigation and “All changes saved” in the footer")
        }),
        new ReleaseNote(new Version(0, 24, 0), L.T("New designs, solid background and calmer groups"), new[]
        {
            L.T("Design templates collapsible and extended: Glass now turns on the real blur (blur 60, saturation 125 %), new are OLED, Nordic Frost, Fluent Clean and Espresso – and you can save your own designs"),
            L.T("New background “Solid” (opaque, without transparency); under panel color you choose whether the hover zone, selection and buttons follow the Windows accent color, a custom accent color or the panel color"),
            L.T("Group style “Accent stripe”: calm, semi-transparent cards with a color stripe; tiles float subtly, type abbreviations as pills; item size per group in the group menu; the pinned group header sits exactly on the group"),
            L.T("Settings tidied up: mini dock, hover zone and panel dimensions as expandable sections, scroll bars only while scrolling; a pinned panel collapsed by click slides out again on hover")
        }),
        new ReleaseNote(new Version(0, 23, 0), L.T("Computer groups from lists, OU picker and export"), new[]
        {
            L.T("Computer groups: source “Dynamic from list” – filter a name list by name or a CSV by any columns; the list is re-read when syncing. “Import group from list …” creates such a group directly"),
            L.T("Each computer group has Sync and Export (CSV or text) in its header"),
            L.T("Dynamic from AD: “Choose OU …” shows the whole OU tree with search and puts the chosen OU into “From OU”"),
            L.T("Create sample scripts is now under Help › Examples; the panel sits more precisely when sliding out and only checks auto-collapse once the mouse pointer has left the panel")
        }),
        new ReleaseNote(new Version(0, 22, 3), L.T("Polish: sliding out, drop-downs, settings"), new[]
        {
            L.T("The panel slides out faster and more evenly; when parked it starts without being placed again"),
            L.T("Drop-downs no longer show a superfluous input field; the panel shows the program icon"),
            L.T("Settings: panel color and background as expandable sections – below them, depending on the background, blur strength, saturation, frosted glass and opacity"),
            L.T("Glass: content sits exactly in the panel again after switching the background")
        }),
        new ReleaseNote(new Version(0, 22, 2), L.T("Calmer sliding out, details for grid groups"), new[]
        {
            L.T("The panel slides out more smoothly: focus and background checks only follow after the movement, nothing is needlessly re-measured or redrawn while gliding"),
            L.T("If the panel is set to “List” and a group to “Grid”, the details of the selected entry now appear at the bottom of the panel")
        }),
        new ReleaseNote(new Version(0, 22, 1), L.T("Fix when running with Glass"), new[]
        {
            L.T("With the “Glass” background, running a script from the panel showed the message “Value cannot be null (defaultDestinationValue)” – fixed"),
            L.T("Setup: sample scripts can be created in addition to your own folder (group “Examples”)")
        }),
        new ReleaseNote(new Version(0, 22, 0), L.T("Smoother: the panel adapts directly, Glass glides calmly"), new[]
        {
            L.T("Changed settings take effect immediately in the open panel – without flicker and without a rebuild (it's only recreated when switching the design or between clear glass and glass)"),
            L.T("Glass: the panel starts smoothly and waits collapsed behind the edge – no more stutter when sliding out"),
            L.T("Settings: sections expand and collapse smoothly, the selection bar glides to the new menu item"),
            L.T("Eight sample scripts (system info, network test, large files, installed programs, error log, temp cleanup, passwords, disk space) – under Content › Groups with “Create sample scripts”")
        }),
        new ReleaseNote(new Version(0, 21, 0), L.T("Glass with its own blur, new settings"), new[]
        {
            L.T("New background “Glass” (Windows 11): the desktop behind the panel is blurred and tinted with the panel color – blur strength and saturation adjustable. Aero blur, Acrylic and Mica were removed, clear glass gets a calmer frosted glass"),
            L.T("Settings with a navigation bar: collapsible pane, collapsible sections and Back; you choose the panel color like the accent color in Windows"),
            L.T("Preview (Space) in its own window next to the panel – it follows the selected entry"),
            L.T("Collapse and expand a pinned panel by clicking the hover zone; dropping a folder as a group opens the group dialog with color and appearance right away"),
            L.T("When changing settings, the panel stays visible and keeps its size; the hotkey (Ctrl+Alt+Space) can already be set during setup; the installer follows Windows light/dark")
        }),
        new ReleaseNote(new Version(0, 20, 0), L.T("New look in the style of Windows 11"), new[]
        {
            L.T("Buttons, toggles, input fields, drop-downs, menus and scroll bars in Fluent design – in the settings, all dialogs, the panel, the Spotlight search and the mini dock"),
            L.T("Light, dark, contrast theme and accent color apply to all controls and also switch in open windows; in the panel they follow the panel color"),
            L.T("The bottom of the panel shows which version is running and whether it's installed or portable")
        }),
        new ReleaseNote(new Version(0, 19, 5), L.T("Blur smooth at the taskbar and on a second monitor too, .NET 10"), new[]
        {
            L.T("If the taskbar or another monitor is at the panel edge, the panel now also glides in as a whole and is clipped at the edge of the work area – no more growing, no stutter, no longer over the taskbar"),
            L.T("Now runs on .NET 10 (long-term support until 2028) and PowerShell 7.6")
        }),
        new ReleaseNote(new Version(0, 19, 4), L.T("Smoother with blur"), new[]
        {
            L.T("With Aero blur, Acrylic or Mica the panel is moved as a whole when sliding in and out instead of being re-measured – much smoother (with another screen behind the edge it grows as before)")
        }),
        new ReleaseNote(new Version(0, 19, 3), L.T("Blur moves with the panel"), new[]
        {
            L.T("Aero blur, Acrylic and Mica slide in and out exactly with the panel – no more background before or after the panel")
        }),
        new ReleaseNote(new Version(0, 19, 2), L.T("Blur fixed"), new[]
        {
            L.T("Aero blur stays after collapsing; no more blurry strip next to the panel"),
            L.T("No more Windows close button on the panel"),
            L.T("Opacity sits right next to background and frosted glass, now from 20 %; with blur it's set to 70 % if it was at 100 %")
        }),
        new ReleaseNote(new Version(0, 19, 1), L.T("Choose background: Aero, Acrylic, Mica or clear glass"), new[]
        {
            L.T("Background behind the panel selectable: clear glass, Aero blur, Acrylic or Mica – Windows 11 now rounds the corners itself and smoothly"),
            L.T("Frosted glass slider instead of levels; Windows 10 automatically gets Aero blur with Acrylic and Mica"),
            L.T("“Ask for profile” now also applies when launching from the panel, with Alt+1 … 9 and in Spotlight"),
            L.T("Fixed groups (Recently run, Favorites, Custom entries) can also be moved in the panel"),
            L.T("List ↔ grid per group changes the height smoothly; the slider for “Recently run” works again")
        }),
        new ReleaseNote(new Version(0, 19, 0), L.T("Dynamic computer groups, arrange fixed groups freely"), new[]
        {
            L.T("Computer groups dynamically from AD: rules like “Operating system contains Server”, combined in blocks with AND/OR; refreshed at startup and regularly"),
            L.T("Recently run, Favorites and Custom entries appear with a lock in the group list, can be moved and shown or hidden per workspace"),
            L.T("Running scripts: the end is detected even if the window stays open; “Run again” after the end; “running” right on the entry"),
            L.T("Ask for the profile at launch (globally or per entry) – in the same window as the values for {{placeholders}}"),
            L.T("Groups expand and collapse smoothly, buttons in the group header fade in, “Recently run” shows the time"),
            L.T("Blur strength adjustable, opacity exactly as set again; a pinned panel no longer covers the settings"),
            L.T("Drag folders into the panel: as a group or as a link; tags with their own color and removable from entries individually")
        }),
        new ReleaseNote(new Version(0, 18, 0), L.T("Filtering from the search box, more readable in light mode"), new[]
        {
            L.T("Clicking into the search box shows the filter types (tag, type, group, property) – the choice lists the existing values; the filter buttons below the search are gone"),
            L.T("Light design: open windows switch immediately, group colors and icons with more contrast, light panels barely see-through"),
            L.T("Group menu tidied up: “New subgroup” at the top, “Custom color” and “Remove color” in the Color submenu, without “Move up/down”"),
            L.T("Rounder search box")
        }),
        new ReleaseNote(new Version(0, 17, 2), L.T("Create and arrange groups"), new[]
        {
            L.T("New group with icon or image, color, appearance and optionally a folder (becomes a smart group) in one dialog"),
            L.T("Right-clicking a group creates a subgroup, an empty area creates a new group at the bottom"),
            L.T("Settings › Groups: subgroups inside their group's frame, moving and nesting like in the panel"),
            L.T("Panel: menu button in the group header only on hover; the group frame lights up when dragging in")
        }),
        new ReleaseNote(new Version(0, 17, 1), L.T("Moving groups: see where they land"), new[]
        {
            L.T("When dragging a group, a gap with a placeholder opens at the target – in the panel and in the settings"),
            L.T("Between two groups, dragging sideways chooses the level: right goes deeper, left goes higher"),
            L.T("Drag image at the mouse pointer with name, color and number of subgroups"),
            L.T("Adjustable under Panel › Behavior: drag image, smooth movement, collapse contents, reason at invalid places")
        }),
        new ReleaseNote(new Version(0, 17, 0), L.T("Noticeably faster while typing"), new[]
        {
            L.T("Search in the panel three to five times faster on average (measured with 500 entries)"),
            L.T("Switching filters and typing on no longer rebuilds the whole list"),
            L.T("Context menus are only built when opened – less memory, faster to show"),
            L.T("Selection, multi-select checkboxes and group header stay put while typing")
        }),
        new ReleaseNote(new Version(0, 16, 2), L.T("Acrylic adapts to the panel"), new[]
        {
            L.T("Blurred background with rounded corners like the panel"),
            L.T("The background moves with the panel when sliding in and out")
        }),
        new ReleaseNote(new Version(0, 16, 1), L.T("Polish after the first feedback"), new[]
        {
            L.T("Suggestions while typing: #tag, type:, group:, is: – only what exists, with counts"),
            L.T("Color picker for group and panel color; the color menu shows all colors again"),
            L.T("Sort groups in the settings by drag & drop"),
            L.T("Jump bar: a second click collapses; jump bar and scroll bars can be turned off"),
            L.T("Mini dock replaces the hover zone, number of favorites adjustable"),
            L.T("File extensions separated by semicolons")
        }),
        new ReleaseNote(new Version(0, 16, 0), L.T("Find faster, see what's running"), new[]
        {
            L.T("Fuzzy search with highlighting, “Best matches” and filters like type:ps1, group:, is:fav"),
            L.T("Undo (Ctrl+Z) after hiding, removing and moving"),
            L.T("Keyboard: Ctrl+Enter as admin, Alt+1…9 for favorites, F1 shows all shortcuts"),
            L.T("Pinned group header, jump bar and Ctrl+K for groups"),
            L.T("Activity bar with progress; tile map for remote runs"),
            L.T("Preview with Space: code, parameters, signature"),
            L.T("Design templates, Windows accent color, Mica and font size"),
            L.T("Setup assistant and optional mini dock at the screen edge")
        }),
    };

    /// <summary>Neuerungen seit <paramref name="lastSeen"/> (exklusive) bis <paramref name="current"/> (inklusive), neueste zuerst.</summary>
    public static IReadOnlyList<ReleaseNote> Since(Version? lastSeen, Version current) =>
        All.Where(n => (lastSeen == null || Normalize(n.Version) > Normalize(lastSeen)) && Normalize(n.Version) <= Normalize(current))
           .OrderByDescending(n => n.Version).ToList();

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static Version? Parse(string? text) => Version.TryParse(text, out var v) ? v : null;
}
