using PowerExec.Core.Config;
using PowerExec.UI.ViewModels;

namespace PowerExec.UI.Core.Tests;

public class DesignEditorViewModelTests
{
    private static DesignPreset Start(bool custom = false) =>
        new(custom ? "custom:abc12345" : "oled", "OLED", "", ThemeMode.Dark, "#000000", 100, 0, false, PanelBackdrop.Solid, AccentColor: "#3B82F6", IsCustom: custom);

    [Fact]
    public void SlotsShowDerivedColorsUntilTheyAreSet()
    {
        var dialogs = new FakeDialogs { ColorAnswer = "#334455" };
        var vm = new DesignEditorViewModel(dialogs, Start(), _ => null);
        var card = vm.Slots.Single(s => s.Key == "Card");
        Assert.True(card.IsDerived);
        Assert.True(PanelColors.TryParse(card.Hex, out _));            // abgeleitet, aber sichtbar

        card.PickCommand.Execute(null);
        Assert.False(card.IsDerived);
        Assert.Equal("#334455", card.Hex);
        Assert.Equal("#334455", vm.Preview.Palette!.Card);

        card.ClearCommand.Execute(null);
        Assert.True(card.IsDerived);
        Assert.Null(vm.Preview.Palette);
    }

    [Fact]
    public void ContrastChecksWarnAboutUnreadableText()
    {
        var dialogs = new FakeDialogs { ColorAnswer = "#101010" };
        var vm = new DesignEditorViewModel(dialogs, Start(), _ => null);
        Assert.All(vm.Checks, c => Assert.True(c.Ok));

        vm.Slots.Single(s => s.Key == "Text").PickCommand.Execute(null); // fast schwarzer Text auf Schwarz
        Assert.Contains(vm.Checks, c => !c.Ok);
    }

    [Fact]
    public void PairKeepsBothVariantsAndSingleDesignMovesWithTheSwitch()
    {
        var dialogs = new FakeDialogs { ColorAnswer = "#F5F5F5" };
        var vm = new DesignEditorViewModel(dialogs, Start(), _ => null);

        // Ohne Paar: „Hell“ macht aus dem Design ein helles – die Farben ziehen mit
        vm.EditLight = true;
        Assert.Equal(ThemeMode.Light, vm.Preview.Theme);
        Assert.Equal("#000000", vm.Preview.PanelColor);
        vm.EditDark = true;
        Assert.Equal(ThemeMode.Dark, vm.Preview.Theme);

        // Mit Paar: die helle Variante bekommt eigene Farben, die dunkle bleibt
        vm.HasPair = true;
        vm.EditLight = true;
        vm.Slots.Single(s => s.Key == "Surface").PickCommand.Execute(null);
        var design = vm.Preview;
        Assert.Equal("#000000", design.PanelColor);
        Assert.Equal("#F5F5F5", design.Pair!.PanelColor);
        Assert.Equal(ThemeMode.Dark, design.Theme);
    }

    [Fact]
    public void SaveNeedsANameAndReplacesOnlyOwnDesigns()
    {
        var vm = new DesignEditorViewModel(new FakeDialogs(), Start(), n => n == "Vergeben" ? "gibt es schon" : null) { Name = " " };
        var closed = false;
        vm.CloseRequested = () => closed = true;
        vm.SaveCommand.Execute(null);
        Assert.Null(vm.Result);
        Assert.NotNull(vm.Error);

        vm.Name = "Vergeben";
        vm.SaveCommand.Execute(null);
        Assert.Equal("gibt es schon", vm.Error);

        vm.Name = "Mein Schwarz";
        vm.FontFamily = "Bahnschrift";
        vm.SuggestGroupColorsCommand.Execute(null);
        vm.GroupColorsForTags = true;
        vm.SaveCommand.Execute(null);
        Assert.True(closed);
        Assert.StartsWith("custom:", vm.Result!.Key, StringComparison.Ordinal);
        Assert.NotEqual("oled", vm.Result.Key);                        // mitgeliefertes Design bleibt unangetastet
        Assert.True(vm.Result.IsCustom);
        Assert.Equal("Bahnschrift", vm.Result.FontFamily);
        Assert.Equal(8, Appearance.SplitColors(vm.Result.GroupColors).Count);
        Assert.True(vm.Result.GroupColorsForTags);
        Assert.Equal(PanelBackdrop.Solid, vm.Result.Backdrop);         // Hintergrund vom Ausgangsstand

        // Ein eigenes Design behält seine Kennung (wird ersetzt)
        var own = new DesignEditorViewModel(new FakeDialogs(), Start(custom: true), _ => null);
        own.SaveCommand.Execute(null);
        Assert.Equal("custom:abc12345", own.Result!.Key);
    }

    [Fact]
    public void ExportWritesAFileTheMarketplaceCanRead()
    {
        var file = Path.Combine(Path.GetTempPath(), "px-design-" + Guid.NewGuid().ToString("N") + ".design.json");
        try
        {
            var vm = new DesignEditorViewModel(new FakeDialogs { FileAnswer = file }, Start(), _ => null);
            vm.SuggestGroupColorsCommand.Execute(null);
            vm.ExportCommand.Execute(null);
            var back = Marketplace.ParseDesign(File.ReadAllText(file), new MarketplaceFile("Designs/X.design.json", "v1", MarketplaceItemType.Design));
            Assert.Equal("OLED", back.Name);
            Assert.Equal(8, Appearance.SplitColors(back.GroupColors).Count);
        }
        finally { File.Delete(file); }
    }
}
