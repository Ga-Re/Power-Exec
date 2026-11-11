using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Vorlagen-Katalog der Windows-Werkzeuge.</summary>
public class ToolCatalogTests
{
    [Fact]
    public void CatalogIsValid()
    {
        Assert.True(ToolCatalog.All.Count > 50);
        foreach (var t in ToolCatalog.All)
        {
            var e = ToolCatalog.Create(t);
            Assert.False(string.IsNullOrWhiteSpace(e.Target + e.Code), t.Name);
            Assert.False(IconRef.Parse(t.Icon).IsEmpty, t.Name);
        }
    }
}
