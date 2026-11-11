using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

/// <summary>Erkennung von Vollbild-Programmen.</summary>
public class FullscreenTests
{
    [Fact]
    public void FullscreenDetection()
    {
        var mon = new Rectangle(0, 0, 1920, 1080);
        Assert.True(Fullscreen.IsFullscreen(new Rectangle(0, 0, 1920, 1080), mon, "Chrome_WidgetWin_1"));
        Assert.True(Fullscreen.IsFullscreen(new Rectangle(-1, -1, 1922, 1082), mon, "mpv"));
        Assert.False(Fullscreen.IsFullscreen(new Rectangle(0, 0, 1920, 1032), mon, "Notepad")); // maximiert, Taskleiste sichtbar
        Assert.False(Fullscreen.IsFullscreen(new Rectangle(0, 0, 1920, 1080), mon, "Progman"));  // Desktop
        Assert.False(Fullscreen.IsFullscreen(new Rectangle(1920, 0, 1920, 1080), mon, "x"));      // anderer Monitor
    }
}
