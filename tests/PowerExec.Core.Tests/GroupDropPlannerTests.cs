using PowerExec.Core.Config;

namespace PowerExec.Core.Tests;

public class GroupDropPlannerTests
{
    private static readonly string[] Order =
        ["Kunde A", "Kunde A/AD", "Kunde A/Exchange", "Kunde B", "Kunde B/Server", "Täglich", "Notfall"];

    private static GroupDropPlan Plan(string dragged, string hovered, GroupDropZone zone, int offset = 0, bool siblingsOnly = false, Func<string, bool>? exists = null) =>
        GroupDropPlanner.Plan(Order, dragged, hovered, zone, offset, siblingsOnly, exists);

    [Fact]
    public void BetweenGroupsStaysOnOwnLevelWithoutSidewaysMove()
    {
        var p = Plan("Täglich", "Kunde B", GroupDropZone.Before);
        Assert.True(p.IsValid);
        Assert.Equal((GroupDropPosition.Before, "Kunde B", (string?)null, 0), (p.Position, p.Target, p.NewParent, p.Depth));
        Assert.Equal((0, 1), (p.MinDepth, p.MaxDepth));
        Assert.True(p.CanChooseDepth);
    }

    [Fact]
    public void SidewaysToTheRightGoesOneLevelDeeper()
    {
        var p = Plan("Täglich", "Kunde B", GroupDropZone.Before, offset: 1);
        Assert.Equal((GroupDropPosition.After, "Kunde A/Exchange", "Kunde A", 1), (p.Position, p.Target, p.NewParent, p.Depth));
    }

    [Fact]
    public void SidewaysIsClampedToWhatTheNeighboursAllow()
    {
        Assert.Equal(1, Plan("Täglich", "Kunde B", GroupDropZone.Before, offset: 5).Depth);
        var top = Plan("Kunde A/AD", "Kunde A", GroupDropZone.Before, offset: -3);
        Assert.Equal((GroupDropPosition.Before, "Kunde A", (string?)null, 0), (top.Position, top.Target, top.NewParent, top.Depth));
        var last = Plan("Täglich", "Notfall", GroupDropZone.After, offset: 2);
        Assert.Equal((GroupDropPosition.After, "Notfall", 0), (last.Position, last.Target, last.Depth));
    }

    [Fact]
    public void BelowAParentWithChildrenBecomesTheFirstChild()
    {
        var p = Plan("Täglich", "Kunde A", GroupDropZone.After, offset: -2);
        Assert.Equal((GroupDropPosition.Before, "Kunde A/AD", "Kunde A", 1), (p.Position, p.Target, p.NewParent, p.Depth));
        Assert.False(p.CanChooseDepth);
    }

    [Fact]
    public void MiddleOfTheHeaderMakesASubgroup()
    {
        var p = Plan("Täglich", "Kunde B", GroupDropZone.Into);
        Assert.Equal((GroupDropPosition.Into, "Kunde B", "Kunde B", 1), (p.Position, p.Target, p.NewParent, p.Depth));
    }

    [Fact]
    public void OverItselfOrItsChildrenNothingChanges()
    {
        Assert.True(Plan("Kunde A", "Kunde A/AD", GroupDropZone.Into).IsNoOp);
        Assert.True(Plan("Kunde A", "Kunde A", GroupDropZone.Before).IsNoOp);
    }

    [Fact]
    public void SameSpotIsRecognisedAsNoChange()
    {
        // „Täglich“ direkt vor „Notfall“ = da, wo es schon ist
        Assert.True(Plan("Täglich", "Notfall", GroupDropZone.Before).IsNoOp);
        Assert.False(Plan("Täglich", "Kunde B", GroupDropZone.Before).IsNoOp);
    }

    [Fact]
    public void SettingsOnlyReorderSiblings()
    {
        var swap = Plan("Kunde A/Exchange", "Kunde A/AD", GroupDropZone.Before, siblingsOnly: true);
        Assert.True(swap.IsValid);
        Assert.False(swap.IsNoOp);
        Assert.Equal((GroupDropPosition.Before, "Kunde A/AD"), (swap.Position, swap.Target));

        // Zwischen „AD“ und „Kunde B“ wäre die oberste Ebene möglich – in den Einstellungen bleibt Exchange in Kunde A
        var stay = Plan("Kunde A/Exchange", "Kunde B", GroupDropZone.Before, offset: -1, siblingsOnly: true);
        Assert.True(stay.IsValid);
        Assert.True(stay.IsNoOp);

        var into = Plan("Täglich", "Kunde B", GroupDropZone.Into, siblingsOnly: true);
        Assert.False(into.IsValid);
        Assert.Contains("Reihenfolge", into.Reason);

        var away = Plan("Kunde A/AD", "Täglich", GroupDropZone.After, siblingsOnly: true);
        Assert.False(away.IsValid);
        Assert.Contains("„AD“ bleibt in „Kunde A“", away.Reason);
    }

    [Fact]
    public void SameNameAtTheTargetIsRejected()
    {
        var p = Plan("Kunde B/Server", "Kunde A", GroupDropZone.Into, exists: path => path == "Kunde A/Server");
        Assert.False(p.IsValid);
        Assert.Contains("In „Kunde A“ gibt es schon eine Gruppe „Server“", p.Reason);
    }

    [Fact]
    public void DescribeNamesTargetAndLevel()
    {
        Assert.Equal("„Täglich“: oberste Ebene, vor „Kunde B“", GroupDropPlanner.Describe(Plan("Täglich", "Kunde B", GroupDropZone.Before), "Täglich"));
        Assert.Equal("„Täglich“: in „Kunde A“, nach „Exchange“", GroupDropPlanner.Describe(Plan("Täglich", "Kunde B", GroupDropZone.Before, 1), "Täglich"));
        Assert.Equal("„Täglich“ wird Untergruppe von „Kunde B“.", GroupDropPlanner.Describe(Plan("Täglich", "Kunde B", GroupDropZone.Into), "Täglich"));
    }

    [Theory]
    [InlineData("Täglich", "Kunde B", GroupDropZone.Before, 0, "Kunde A|Kunde A/AD|Kunde A/Exchange|Täglich|Kunde B|Kunde B/Server|Notfall")]
    [InlineData("Täglich", "Kunde B", GroupDropZone.Before, 1, "Kunde A|Kunde A/AD|Kunde A/Exchange|Kunde A/Täglich|Kunde B|Kunde B/Server|Notfall")]
    [InlineData("Täglich", "Kunde A", GroupDropZone.After, 0, "Kunde A|Kunde A/Täglich|Kunde A/AD|Kunde A/Exchange|Kunde B|Kunde B/Server|Notfall")]
    [InlineData("Kunde B", "Kunde A", GroupDropZone.Before, 0, "Kunde B|Kunde B/Server|Kunde A|Kunde A/AD|Kunde A/Exchange|Täglich|Notfall")]
    [InlineData("Notfall", "Kunde B", GroupDropZone.Into, 0, "Kunde A|Kunde A/AD|Kunde A/Exchange|Kunde B|Kunde B/Server|Kunde B/Notfall|Täglich")]
    public void PlanAppliedWithGroupManagerGivesTheShownOrder(string dragged, string hovered, GroupDropZone zone, int offset, string expected)
    {
        var s = new AppSettings();
        foreach (var path in Order) GroupManager.Create(s, GroupManager.LeafOf(path), GroupManager.ParentOf(path));
        var plan = GroupDropPlanner.Plan(GroupManager.All(s), dragged, hovered, zone, offset, false, p => GroupManager.Exists(s, p));
        Assert.True(plan.IsValid);
        Assert.NotNull(GroupManager.MoveGroup(s, dragged, plan.Target, plan.Position));
        Assert.Equal(expected, string.Join("|", GroupManager.All(s)));
    }
}
