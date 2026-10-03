using Relio.Web.Theme;

namespace Relio.Web.Tests.Theme;

public class EntryKindVisualsTests
{
    [Theory]
    [InlineData(EntryKind.Interaction)]
    [InlineData(EntryKind.Note)]
    [InlineData(EntryKind.DifficultMoment)]
    public void For_returns_a_complete_visual_for_every_kind(EntryKind kind)
    {
        var visual = EntryKindVisuals.For(kind);

        visual.DisplayName.Should().NotBeNullOrWhiteSpace();
        visual.Icon.Should().NotBeNullOrWhiteSpace();
        visual.LabelCssClass.Should().NotBeNullOrWhiteSpace();
        visual.MarkerCssClass.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_kind_has_a_distinct_icon()
    {
        var icons = EntryKindVisuals.All.Select(k => EntryKindVisuals.For(k).Icon);

        icons.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_kind_has_a_distinct_label_colour_class()
    {
        var classes = EntryKindVisuals.All.Select(k => EntryKindVisuals.For(k).LabelCssClass);

        classes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_kind_has_a_distinct_marker_class()
    {
        var classes = EntryKindVisuals.All.Select(k => EntryKindVisuals.For(k).MarkerCssClass);

        classes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_covers_every_declared_enum_value()
    {
        EntryKindVisuals.All.Should().BeEquivalentTo(Enum.GetValues<EntryKind>());
    }
}
