using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Einstellungen eines Panels.</summary>
public class PanelConfigTests
{
    [Fact]
    public void PanelContentFilter()
    {
        var p = new PanelConfig();
        Assert.True(p.ShowsFolder(@"C:\x"));
        p.Folders = [@"C:\Scripts", PanelConfig.CustomEntriesKey];
        Assert.True(p.ShowsFolder(@"c:\scripts"));
        Assert.False(p.ShowsFolder(@"C:\Tools"));
        Assert.True(p.ShowsFolder(PanelConfig.CustomEntriesKey));
        p.Categories = ["AD"];
        Assert.True(p.ShowsCategory("ad"));
        Assert.False(p.ShowsCategory("Drucker"));
    }

    /// <summary>
    /// Ändert jede Eigenschaft und prüft, dass <see cref="PanelConfig.Clone"/> sie mitnimmt. Eine neue Einstellung, die
    /// in Clone() fehlt, ginge sonst beim Übernehmen der Einstellungen still verloren.
    /// </summary>
    [Fact]
    public void CloneCopiesEveryProperty()
    {
        var p = new PanelConfig();
        foreach (var prop in typeof(PanelConfig).GetProperties().Where(x => x.CanWrite && x.GetIndexParameters().Length == 0))
        {
            var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object? value = prop.GetValue(p);
            if (t == typeof(bool)) value = !(bool)(value ?? false);
            else if (t == typeof(int)) value = (int)(value ?? 0) + 7;
            else if (t == typeof(string)) value = "#123456";
            else if (t.IsEnum) value = Enum.GetValues(t).Cast<object>().Last();
            else if (t == typeof(List<string>)) value = new List<string> { "x" };
            else continue;
            prop.SetValue(p, value);
        }
        var json = System.Text.Json.JsonSerializer.Serialize(p);
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(p.Clone()));
    }

    [Fact]
    public void TriggerKeyComparesByValue()
    {
        var a = new PanelConfig { PanelWidth = 420 };
        var b = a.Clone();
        Assert.Equal(a.TriggerKey, b.TriggerKey);
        b.Pinned = true;            // betrifft die Hover-Zone nicht
        Assert.Equal(a.TriggerKey, b.TriggerKey);
        b.HotZoneLength += 10;
        Assert.NotEqual(a.TriggerKey, b.TriggerKey);
    }
}
