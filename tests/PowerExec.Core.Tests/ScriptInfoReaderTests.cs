using PowerExec.Core.Metadata;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

public class ScriptInfoReaderTests
{
    private const string Sample = """
        #Requires -Modules ActiveDirectory
        <#
        .SYNOPSIS
            Zeigt den freien Speicherplatz aller Laufwerke an.
        .DESCRIPTION
            Prüft alle Laufwerke und warnt,
            wenn der freie Speicher unter einen Schwellwert fällt.
        .PARAMETER MinFreePercent
            Warnschwelle in Prozent freiem Speicher.
        .PARAMETER DriveType
            Welche Laufwerke geprüft werden.
        .NOTES
            PowerExec: CopyFolder
        #>
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)]
            [int]$MinFreePercent = 15,

            [ValidateSet('Fixed', 'Removable', 'Network')]
            [string]$DriveType = 'Fixed',

            [Parameter(Mandatory = $false, HelpMessage = 'Pfad für den CSV-Export')]
            [string]$ExportPath,

            [switch]$IncludeSystemReserved,

            [string[]]$ComputerName
        )
        $answer = Read-Host 'Weiter?'
        Get-PSDrive
        """;

    private static LaunchItem Load(string text)
    {
        var item = new LaunchItem { FullPath = "/tmp/Get-DiskSpace.ps1", RootFolder = "/tmp", Type = ItemType.PowerShell };
        ScriptInfoReader.FillFromText(item, text);
        return item;
    }

    [Fact]
    public void ReadsHelp()
    {
        var item = Load(Sample);
        Assert.Equal("Zeigt den freien Speicherplatz aller Laufwerke an.", item.Synopsis);
        Assert.Equal("Prüft alle Laufwerke und warnt, wenn der freie Speicher unter einen Schwellwert fällt.", item.Description);
        Assert.True(item.CopyFolderByDefault);
        Assert.True(item.UsesReadHost);
        Assert.Equal(new[] { "ActiveDirectory" }, item.RequiredModules);
        Assert.Null(item.ParseError);
    }

    [Fact]
    public void ReadsParameters()
    {
        var p = Load(Sample).Parameters;
        Assert.Equal(5, p.Count);

        Assert.Equal("MinFreePercent", p[0].Name);
        Assert.True(p[0].IsMandatory);
        Assert.Equal(ParameterKind.Number, p[0].Kind);
        Assert.Equal("15", p[0].DefaultValueLiteral);
        Assert.Equal("Warnschwelle in Prozent freiem Speicher.", p[0].HelpText);

        Assert.Equal(ParameterKind.Choice, p[1].Kind);
        Assert.Equal(new[] { "Fixed", "Removable", "Network" }, p[1].ValidValues);
        Assert.Equal("Fixed", p[1].DefaultValueLiteral);
        Assert.False(p[1].IsMandatory);

        Assert.False(p[2].IsMandatory);
        Assert.Equal("Pfad für den CSV-Export", p[2].HelpText);
        Assert.Equal(ParameterKind.Text, p[2].Kind);

        Assert.Equal(ParameterKind.Switch, p[3].Kind);
        Assert.Equal(ParameterKind.List, p[4].Kind);
    }

    [Fact]
    public void ScriptWithoutHelpOrParams()
    {
        var item = Load("Get-Process | Select-Object -First 5");
        Assert.Equal(string.Empty, item.Synopsis);
        Assert.Empty(item.Parameters);
        Assert.False(item.UsesReadHost);
        Assert.False(item.HasParameters);
    }

    [Fact]
    public void ParseErrorIsReported()
    {
        var item = Load("param([string]$a\nfunction {");
        Assert.NotNull(item.ParseError);
    }

    [Fact]
    public void BatchSynopsis()
    {
        var text = "@echo off\r\nREM Startet den Dienst Druckwarteschlange neu.\r\nnet stop spooler\r\n";
        Assert.Equal("Startet den Dienst Druckwarteschlange neu.", BatchInfoReader.ReadSynopsis(new StringReader(text)));
        Assert.Equal("Hallo", BatchInfoReader.ReadSynopsis(new StringReader(":: Hallo\r\necho x")));
        Assert.Equal(string.Empty, BatchInfoReader.ReadSynopsis(new StringReader("echo x\r\nREM spät")));
    }

    /// <summary>Beispielordner (samples/): jedes Script lässt sich lesen, hat Kurzbeschreibung und Hilfe zu jedem Parameter.</summary>
    [Fact]
    public void AllSamplesHaveSynopsisAndParameterHelp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "samples"), "*.ps1");
        Assert.True(files.Length >= 8, $"nur {files.Length} Beispiele");
        foreach (var file in files)
        {
            // Mit BOM gespeichert: Windows PowerShell 5.1 liest Umlaute sonst falsch
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{Path.GetFileName(file)}: ohne UTF-8-BOM");
            var item = new LaunchItem { FullPath = file, RootFolder = Path.GetDirectoryName(file)!, Type = ItemType.PowerShell, DisplayName = Path.GetFileNameWithoutExtension(file) };
            ScriptInfoReader.Fill(item);
            Assert.Null(item.ParseError);
            Assert.False(string.IsNullOrWhiteSpace(item.Synopsis), $"{item.DisplayName}: keine Kurzbeschreibung");
            Assert.All(item.Parameters, p => Assert.False(string.IsNullOrWhiteSpace(p.HelpText), $"{item.DisplayName}: -{p.Name} ohne Hilfe"));
        }
    }
}
