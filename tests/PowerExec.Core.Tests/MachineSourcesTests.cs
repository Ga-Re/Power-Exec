using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Rechnergruppen aus Listen (Text/CSV mit Filtern), Export und OU-Baum.</summary>
public class MachineSourcesTests
{
    [Fact]
    public void NameListFiltersByName()
    {
        var table = MachineTable.Parse("# Kommentar\nsrv-web01\nSRV-WEB02\nsrv-db01\n\n  pc-0815  \n");
        Assert.False(table.IsCsv);
        Assert.Equal(new[] { MachineTable.NameOnlyColumn }, table.Columns);

        var all = table.Apply(new ListGroupQuery());
        Assert.Equal(new[] { "PC-0815", "SRV-DB01", "SRV-WEB01", "SRV-WEB02" }, all);

        var web = table.Apply(new ListGroupQuery { Filters = { new ListFilter { Operator = AdOperator.Equals, Value = "srv-web*" } } });
        Assert.Equal(new[] { "SRV-WEB01", "SRV-WEB02" }, web);
    }

    [Fact]
    public void CsvFiltersByAnyColumnAndFindsNameColumn()
    {
        var csv = "Standort;ComputerName;Betriebssystem;RAM\n" +
                  "Berlin;srv-01;Windows Server 2022;32\n" +
                  "München;\"srv-02\";Windows Server 2019;16\n" +
                  "Berlin;pc-07;Windows 11;8\n";
        var table = MachineTable.Parse(csv);
        Assert.True(table.IsCsv);
        Assert.Equal(new[] { "Standort", "ComputerName", "Betriebssystem", "RAM" }, table.Columns);
        Assert.Equal("ComputerName", table.NameColumnFor(null));

        var berlinServer = table.Apply(new ListGroupQuery
        {
            Filters =
            {
                new ListFilter { Column = "Standort", Operator = AdOperator.Equals, Value = "berlin" },
                new ListFilter { Column = "Betriebssystem", Operator = AdOperator.Contains, Value = "Server" }
            }
        });
        Assert.Equal(new[] { "SRV-01" }, berlinServer);

        var bigRam = table.Apply(new ListGroupQuery { Filters = { new ListFilter { Column = "RAM", Operator = AdOperator.AtLeast, Value = "16" } } });
        Assert.Equal(new[] { "SRV-01", "SRV-02" }, bigRam);

        // Namensspalte frei wählbar
        Assert.Equal(new[] { "BERLIN", "MÜNCHEN" }, table.Apply(new ListGroupQuery { NameColumn = "Standort" }));
    }

    [Fact]
    public void CsvWithCommasAndQuotesIsSplitCorrectly()
    {
        var table = MachineTable.Parse("Name,Beschreibung\nweb1,\"Web, extern\"\nweb2,\"Sagt \"\"Hallo\"\"\"\n");
        Assert.Equal("Web, extern", table.Rows[0]["Beschreibung"]);
        Assert.Equal("Sagt \"Hallo\"", table.Rows[1]["Beschreibung"]);
        var hits = table.Apply(new ListGroupQuery { Filters = { new ListFilter { Column = "Beschreibung", Operator = AdOperator.Contains, Value = "extern" } } });
        Assert.Equal(new[] { "WEB1" }, hits);
    }

    [Fact]
    public void ExportWritesNamesAsCsvOrText()
    {
        var g = new MachineGroup { Name = "Web", Machines = { "SRV-01", "SRV-02" } };
        Assert.Equal("Name\nSRV-01\nSRV-02\n", MachineGroupExport.ToFileText(g, csv: true).Replace("\r", ""));
        Assert.Equal("SRV-01\nSRV-02\n", MachineGroupExport.ToFileText(g, csv: false).Replace("\r", ""));
        // Export ist wieder importierbar
        Assert.Equal(g.Machines, MachineTable.Parse(MachineGroupExport.ToFileText(g, csv: true)).Apply(new ListGroupQuery()));
    }

    [Fact]
    public void OuTreeNestsByDistinguishedName()
    {
        var roots = OuTree.Build(new[]
        {
            "OU=Web,OU=Server,DC=firma,DC=local",
            "DC=firma,DC=local",
            "OU=Server,DC=firma,DC=local",
            "CN=Computers,DC=firma,DC=local",
            "OU=Tief,OU=Fehlt,OU=Server,DC=firma,DC=local", // Zwischenebene fehlt: hängt am nächsten Vorfahren
            "OU=Mit\\, Komma,DC=firma,DC=local"
        });
        var domain = Assert.Single(roots);
        Assert.Equal("firma.local", domain.Name);
        Assert.Equal(new[] { "Computers", "Mit, Komma", "Server" }, domain.Children.Select(c => c.Name));
        var server = domain.Children.Single(c => c.Name == "Server");
        Assert.Equal(new[] { "Tief", "Web" }, server.Children.Select(c => c.Name));
        Assert.Equal("OU=Web,OU=Server,DC=firma,DC=local", server.Children.Single(c => c.Name == "Web").DistinguishedName);
    }

    [Fact]
    public void ListGroupIsDynamicAndDue()
    {
        var g = new MachineGroup { Name = "Liste", ListQuery = new ListGroupQuery { Path = @"C:\x.csv" } };
        Assert.True(g.IsDynamic);
        Assert.True(AdRules.IsDue(g, 24, DateTime.UtcNow));
        var clone = g.Clone();
        Assert.Equal(@"C:\x.csv", clone.ListQuery!.Path);
        Assert.NotSame(g.ListQuery, clone.ListQuery);
    }

    [Fact]
    public void MachineGroupsAndImport()
    {
        var s = new AppSettings();
        s.MachineGroups.Add(new MachineGroup { Name = "Druckserver", Machines = ["PRT01", "PRT02"] });
        Assert.Equal(new[] { "PRT01", "PRT02", "PC1" }, MachineList.Resolve(s, ["@druckserver", "pc1", "prt01"]));
        var csv = "Name;Standort\r\nPC-0142;Nord\r\n\"PC-0187\";Süd\r\n# Kommentar\r\npc-0142;x\r\n";
        Assert.Equal(new[] { "PC-0142", "PC-0187" }, MachineList.ParseImport(csv));
        Assert.Equal(new[] { "A", "B" }, MachineList.ParseImport("a\nb\n"));
    }
}
