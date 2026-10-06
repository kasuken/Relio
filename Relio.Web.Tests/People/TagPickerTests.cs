using Bunit;
using MudBlazor;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class TagPickerTests
{
    private static BunitContext CreateContext(out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        popovers = context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<TagPicker> Render(
        BunitContext context, IReadOnlyList<Tag> available, List<TagSelection> selected, Action? changed = null) =>
        context.Render<TagPicker>(parameters => parameters
            .Add(p => p.Available, available)
            .Add(p => p.Selected, selected)
            .Add(p => p.Changed, () => changed?.Invoke()));

    [Fact]
    public async Task Selected_tags_show_as_chips()
    {
        await using var context = CreateContext(out _);

        var cut = Render(context, [], [new TagSelection(Guid.NewGuid(), "Chess"), new TagSelection(null, "Climbing")]);

        cut.FindAll("[data-testid='person-tag-chip']").Select(chip => chip.TextContent.Trim())
            .Should().Equal("Chess", "Climbing");
    }

    [Fact]
    public async Task With_no_tags_chosen_there_is_no_empty_chip_row()
    {
        await using var context = CreateContext(out _);

        var cut = Render(context, [], []);

        cut.FindAll("[data-testid='person-tags-selected']").Should().BeEmpty();
        cut.Find("[data-testid='person-tags-field']").TextContent.Should().Contain("Type to find a tag, or to create a new one.");
    }

    [Fact]
    public async Task Closing_a_chip_removes_the_tag()
    {
        await using var context = CreateContext(out _);
        var selected = new List<TagSelection> { new(Guid.NewGuid(), "Chess"), new(null, "Climbing") };
        var changes = 0;
        var cut = Render(context, [], selected, () => changes++);

        cut.FindAll("[data-testid='person-tag-remove']")[0].Click();

        selected.Select(s => s.Name).Should().Equal("Climbing");
        changes.Should().Be(1);
        cut.FindAll("[data-testid='person-tag-chip']").Should().ContainSingle();
    }

    [Fact]
    public async Task Each_chip_has_a_remove_button_with_an_accessible_name()
    {
        await using var context = CreateContext(out _);
        var cut = Render(context, [], [new TagSelection(null, "Chess"), new TagSelection(null, "Climbing")]);

        var buttons = cut.FindAll("[data-testid='person-tag-chip'] button");

        buttons.Should().HaveCount(2);
        buttons.Select(b => b.GetAttribute("aria-label")).Should().Equal("Remove tag Chess", "Remove tag Climbing");
        buttons.Should().OnlyContain(b => b.GetAttribute("type") == "button", "a chip's button must never submit a form");
    }

    [Fact]
    public async Task Typing_lists_matching_tags_and_choosing_one_adds_it()
    {
        await using var context = CreateContext(out var popovers);
        var chess = new Tag { Name = "Chess" };
        var selected = new List<TagSelection>();
        var changes = 0;
        var cut = Render(context, [chess, new Tag { Name = "Work" }], selected, () => changes++);

        cut.Find("[data-testid='person-tags-field'] input").Input("ch");
        var options = popovers.WaitForElements(".mud-popover-open .mud-list-item");
        options.Select(o => o.TextContent.Trim()).Should().Equal("Chess", "Create tag “ch”");
        options.First(o => o.TextContent.Trim() == "Chess").Click();

        // The choice is handled asynchronously (the box is cleared after the tag is added), so wait for it.
        cut.WaitForAssertion(() => selected.Should().Equal(new TagSelection(chess.Id, "Chess")));
        changes.Should().Be(1);
        cut.WaitForAssertion(() => cut.Find("[data-testid='person-tags-field'] input").GetAttribute("value").Should().BeNullOrEmpty());
    }

    [Fact]
    public async Task Choosing_the_create_line_adds_a_new_name_without_an_id()
    {
        await using var context = CreateContext(out var popovers);
        var selected = new List<TagSelection>();
        var cut = Render(context, [new Tag { Name = "Work" }], selected);

        cut.Find("[data-testid='person-tags-field'] input").Input("  Rock   climbing ");
        var create = popovers.WaitForElements(".mud-popover-open .mud-list-item")
            .Single(o => o.TextContent.Contains("Create tag"));
        create.TextContent.Trim().Should().Be("Create tag “Rock climbing”");
        create.Click();

        cut.WaitForAssertion(() => selected.Should().Equal(new TagSelection(null, "Rock climbing")));
    }

    [Fact]
    public async Task The_picker_is_disabled_at_20_tags()
    {
        await using var context = CreateContext(out _);
        var selected = Enumerable.Range(0, 20).Select(i => new TagSelection(null, $"Tag {i}")).ToList();

        var cut = Render(context, [], selected);

        cut.Find("[data-testid='person-tags-field'] input").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='person-tags-field']").TextContent.Should().Contain("A person can have up to 20 tags.");
    }

    [Fact]
    public async Task An_error_is_shown_under_the_field()
    {
        await using var context = CreateContext(out _);

        var cut = context.Render<TagPicker>(parameters => parameters
            .Add(p => p.Available, [])
            .Add(p => p.Selected, [])
            .Add(p => p.Error, "Keep tag names to 50 characters or fewer."));

        cut.Find("[data-testid='person-tags-field']").TextContent.Should().Contain("Keep tag names to 50 characters or fewer.");
    }
}
