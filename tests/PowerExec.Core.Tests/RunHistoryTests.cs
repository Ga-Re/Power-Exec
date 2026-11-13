using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Verlauf: keine Kennwörter, Wiederholen mit denselben Werten, Filter.</summary>
public class RunHistoryTests
{
    [Fact]
    public void HistoryKeepsNoSecretsAndReplaysExactly()
    {
        var item = new LaunchItem
        {
            FullPath = @"C:\s\Reset.ps1",
            RootFolder = @"C:\s",
            Type = ItemType.PowerShell,
            Parameters = new[]
            {
                new ScriptParameter { Name = "User", TypeName = "string" },
                new ScriptParameter { Name = "Password", TypeName = "string" },
                new ScriptParameter { Name = "Force", TypeName = "switch" }
            }
        };
        var raw = new Dictionary<string, string?> { ["User"] = "lena", ["Password"] = "geheim", ["Force"] = "true" };
        var e = RunHistory.Create(item, "Reset", "Standard", ExecutionMode.Administrator, PsEngine.WindowsPowerShell, new[] { "A", "B" }, raw);
        Assert.False(e.Parameters.ContainsKey("Password"));
        Assert.Equal("lena", e.Parameters["User"]);
        e.FailedTargets = new List<string> { "B" };

        var replay = RunHistory.ReplayProfile(e);
        Assert.True(replay.RunAsAdmin);
        Assert.Equal(ProfileTarget.Fixed, replay.Target);
        Assert.Equal(new[] { "A", "B" }, replay.Machines);
        Assert.Equal(ParameterMode.Fixed, replay.Parameters["User"].Mode);
        Assert.Equal(new[] { "B" }, RunHistory.ReplayProfile(e, onlyFailed: true).Machines);
        Assert.Equal(ProfileTarget.Local, RunHistory.ReplayProfile(new HistoryEntry()).Target);
    }

    [Fact]
    public void HistoryFiltersAndRoundTrips()
    {
        var h = new RunHistory();
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Local);
        h.Add(new HistoryEntry { Name = "Alt", StartUtc = now.AddDays(-2).ToUniversalTime(), Result = HistoryResult.Error });
        h.Add(new HistoryEntry { Name = "Heute", StartUtc = now.ToUniversalTime(), Result = HistoryResult.Success, Targets = new List<string> { "SRV01" } });
        Assert.Equal("Heute", h.Entries[0].Name);
        Assert.Single(h.Filter("today", null, now));
        Assert.Single(h.Filter("errors", null, now));
        Assert.Single(h.Filter("remote", null, now));
        Assert.Single(h.Filter("all", "srv01", now));

        var path = Path.Combine(Path.GetTempPath(), "pe-history-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            h.Save(path);
            var loaded = RunHistory.Load(path);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal(HistoryResult.Error, loaded.Entries[1].Result);
        }
        finally { File.Delete(path); }
        Assert.Empty(RunHistory.Load(path + ".fehlt").Entries);
    }
}
