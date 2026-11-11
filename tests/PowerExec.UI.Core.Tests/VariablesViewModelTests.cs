using PowerExec.Core.Config;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

public class VariablesViewModelTests
{
    [Fact]
    public async Task AddEditValidateAndTestVariables()
    {
        var host = new FakeEntryHost();
        var s = new AppSettings { Workspaces = { new Workspace { Name = "Standard" }, new Workspace { Name = "Kunde A" } } };
        var copied = new List<string>();
        var vm = new VariablesViewModel(host, s, copied.Add);

        Assert.True(vm.HasNoItems);
        Assert.Equal(5, vm.Builtins.Count);
        vm.DateFormat = "yyyy-MM-dd";
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", vm.Builtins[0].Preview);

        vm.AddCommand.Execute(null);
        var row = Assert.Single(vm.Items);
        Assert.True(row.IsNew);
        Assert.Single(vm.VisibleItems);
        row.Name = "$CompanyName";
        Assert.False(row.HasNameError);
        Assert.Equal("{{$CompanyName}}", row.Token);
        row.Value = "Med360°";
        Assert.Equal("Med360°", row.Preview);
        Assert.Equal("CompanyName", Assert.Single(s.Variables).Name);

        row.Name = "Date";
        Assert.True(row.HasNameError);
        Assert.Equal("CompanyName", s.Variables[0].Name); // ungültiger Name wird nicht übernommen
        row.Name = "CompanyName";

        row.CopyTokenCommand.Execute(null);
        Assert.Equal(new[] { "{{$CompanyName}}" }, copied);

        // je Arbeitsbereich
        row.Kind = VariableKind.Workspace;
        Assert.Equal(2, row.WorkspaceValues.Count);
        row.WorkspaceValues.Single(w => w.Workspace == "Kunde A").Value = "kunde-a.local";
        s.ActiveWorkspace = "Kunde A";
        Assert.Equal("kunde-a.local", row.Preview);

        // geheim: Klartext wird nie gespeichert
        row.Kind = VariableKind.Secret;
        Assert.Equal(string.Empty, row.Value);
        row.SecretValue = "s3cr3t";
        Assert.True(row.HasSecret);
        Assert.DoesNotContain("s3cr3t", s.Variables[0].Value);
        Assert.Equal("••••••••", row.Preview);

        // PowerShell-Befehl: „Jetzt ermitteln“ führt aus und zeigt das Ergebnis
        row.Kind = VariableKind.PowerShell;
        row.Value = "(Get-ADDomain).DNSRoot";
        Assert.Equal("(wird beim Start ermittelt)", row.Preview);
        row.TestCommand.Execute(null);
        for (int i = 0; i < 50 && row.IsTesting; i++) await Task.Delay(20);
        Assert.Contains("ok:(Get-ADDomain).DNSRoot", row.Status);
        Assert.Equal("ok:(Get-ADDomain).DNSRoot", row.Preview);

        host.CommandAnswer = _ => throw new InvalidOperationException("Modul fehlt");
        row.TestCommand.Execute(null);
        for (int i = 0; i < 50 && row.IsTesting; i++) await Task.Delay(20);
        Assert.Contains("Fehler: Modul fehlt", row.Status);

        row.RemoveCommand.Execute(null);
        Assert.True(vm.HasNoItems);
        Assert.Empty(s.Variables);
    }

    [Fact]
    public void TableSearchSelectAndBulkDelete()
    {
        var host = new FakeEntryHost();
        var s = new AppSettings { Variables = { new UserVariable { Name = "CompanyName", Value = "Med360°" }, new UserVariable { Name = "Domain", Value = "firma.local", Description = "AD" } } };
        var vm = new VariablesViewModel(host, s);
        Assert.Equal(2, vm.VisibleItems.Count);
        vm.Query = "firma";
        Assert.Equal("Domain", Assert.Single(vm.VisibleItems).Name);
        Assert.Equal("1 von 2", vm.SummaryText);
        vm.Query = "";
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal("2 Variablen ausgewählt", vm.SelectionText);
        vm.RemoveSelectedCommand.Execute(null);
        Assert.Empty(s.Variables);
        Assert.True(vm.HasNoItems);
    }

    [Fact]
    public void EditorOffersVariablesAndWarnsAboutUnknownOnes()
    {
        var host = new FakeEntryHost();
        host.Settings.Variables.Add(new UserVariable { Name = "CompanyName", Value = "Med360°" });
        var vm = new CustomEntryViewModel(new CustomEntry { Kind = CustomEntryKind.Snippet, Language = SnippetLanguage.Text, Code = "Gruß {{$CompanyName}}" }, host);

        Assert.Contains("{{$CompanyName}}", vm.Builtins);
        Assert.Contains("{{$Date}}", vm.Builtins);
        Assert.False(vm.HasVariableWarning);

        vm.Code = "Gruß {{$Firmenname}} {{Ticket}}";
        Assert.True(vm.HasVariableWarning);
        Assert.Contains("{{$Firmenname}}", vm.VariableWarning);
        Assert.DoesNotContain("Ticket", vm.VariableWarning);
    }
}
