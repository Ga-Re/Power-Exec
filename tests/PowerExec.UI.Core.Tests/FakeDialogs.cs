using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec.UI.Core.Tests;

/// <summary>Dialoge ohne Fenster: Antworten werden vorher festgelegt, Aufrufe mitgeschrieben.</summary>
internal sealed class FakeDialogs : IDialogs
{
    public string? Answer { get; set; }
    public bool ConfirmAnswer { get; set; } = true;
    public bool? ConfirmOrCancelAnswer { get; set; }
    public string? FileAnswer { get; set; }
    public IReadOnlyList<string> FilesAnswer { get; set; } = Array.Empty<string>();
    public string? FolderAnswer { get; set; }
    public string? ColorAnswer { get; set; }
    public IconRef? IconAnswer { get; set; }
    public NewGroupSpec? NewGroupAnswer { get; set; }
    public IReadOnlyList<CatalogTemplate>? CatalogAnswer { get; set; }

    public List<string> Asked { get; } = new();
    public List<(string Kind, string Title, string Message)> Messages { get; } = new();

    public string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null)
    {
        Asked.Add(heading);
        return Answer is { } a && validate?.Invoke(a) == null ? a : null;
    }

    public void Inform(string message, string title, object? owner = null) => Messages.Add(("info", title, message));
    public void Warn(string message, string title, object? owner = null) => Messages.Add(("warn", title, message));

    public bool Confirm(string message, string title, bool destructive = false, object? owner = null, bool defaultNo = false)
    {
        Messages.Add((destructive ? "confirm-destructive" : "confirm", title, message));
        return ConfirmAnswer;
    }

    public bool? ConfirmOrCancel(string message, string title, object? owner = null)
    {
        Messages.Add(("confirm-cancel", title, message));
        return ConfirmOrCancelAnswer;
    }

    public string? OpenFile(string filter, string? title = null, object? owner = null) { Asked.Add("OpenFile"); return FileAnswer; }
    public IReadOnlyList<string> OpenFiles(string filter, string? title = null, object? owner = null) { Asked.Add("OpenFiles"); return FilesAnswer; }
    public string? SaveFile(string filter, string fileName, string? title = null, object? owner = null) { Asked.Add("SaveFile " + fileName); return FileAnswer; }
    public string? PickFolder(string? title = null, object? owner = null) { Asked.Add("PickFolder"); return FolderAnswer; }
    public string? PickColor(string title, string? initial, object? owner = null) { Asked.Add("PickColor " + title); return ColorAnswer; }
    public IconRef? PickIcon(IconRef current, string heading) { Asked.Add("PickIcon"); return IconAnswer; }

    public NewGroupSpec? AskNewGroup(string heading, string initialName, Func<string, string?> validateName, Func<string?, string?>? validateFolder)
    {
        Asked.Add("AskNewGroup " + heading);
        return NewGroupAnswer;
    }

    public IReadOnlyList<CatalogTemplate>? PickCatalogTemplates() { Asked.Add("PickCatalogTemplates"); return CatalogAnswer; }
}
