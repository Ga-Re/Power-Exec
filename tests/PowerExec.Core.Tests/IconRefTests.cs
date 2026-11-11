using PowerExec.Core.Config;
using PowerExec.Core.Models;

namespace PowerExec.Core.Tests;

/// <summary>Symbol-Verweise (Quelle:Wert) einlesen.</summary>
public class IconRefTests
{
    [Fact]
    public void IconRefParse()
    {
        Assert.Equal(IconSource.Hero, IconRef.Parse("hero:server").Source);
        Assert.Equal(@"C:\a.png", IconRef.Parse(@"file:C:\a.png").Value);
        Assert.True(IconRef.Parse("quatsch").IsEmpty);
    }
}
