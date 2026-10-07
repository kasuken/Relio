using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Web.Components.Settings;
using Relio.Web.Tests.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Settings;

public class TagSettingsTests
{
    // See ConfirmDialogTests for why the context is created per test with "await using".
    private static BunitContext CreateContext(
        FakeTagService tags,
        out IRenderedComponent<MudDialogProvider> dialogs,
        out IRenderedComponent<MudSnackbarProvider> snackbars)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<ITagService>(tags);
        context.Render<MudPopoverProvider>();
        dialogs = context.Render<MudDialogProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    private static FakeTagService SomeTags(out Relio.Domain.Tag climbing)
    {
        var tags = new FakeTagService();
        tags.Add("Book club");
        climbing = tags.Add("Climbing", people: 2);
        tags.Add("Mentor", people: 1);
        return tags;
    }

    private static IReadOnlyList<string> Names(IRenderedComponent<TagSettings> cut) =>
        cut.FindAll("[data-testid='tag-row-name']").Select(e => e.TextContent.Trim()).ToList();

    private static void Click(IRenderedComponent<TagSettings> cut, string testId, int row) =>
        cut.FindAll($"[data-testid='{testId}']")[row].Click();

    [Fact]
    public async Task Lists_tags_by_name_with_how_many_people_have_each()
    {
        await using var context = CreateContext(SomeTags(out _), out _, out _);

        var cut = context.Render<TagSettings>();

        Names(cut).Should().Equal("Book club", "Climbing", "Mentor");
        cut.FindAll("[data-testid='tag-row-count']").Select(e => e.TextContent.Trim())
            .Should().Equal("No one yet", "2 people", "1 person");
    }

    [Fact]
    public async Task Adding_a_tag_sends_the_name_shows_Tag_added_and_reloads()
    {
        var tags = SomeTags(out _);
        await using var context = CreateContext(tags, out _, out var snackbars);
        var cut = context.Render<TagSettings>();

        cut.Find("[data-testid='tag-add-field'] input").Input("Chess");
        cut.Find("[data-testid='tag-add-form']").Submit();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Book club", "Chess", "Climbing", "Mentor"));
        tags.Created.Should().Equal("Chess");
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Tag added"));
    }

    [Fact]
    public async Task A_taken_name_is_explained_under_the_add_field()
    {
        await using var context = CreateContext(SomeTags(out _), out _, out _);
        var cut = context.Render<TagSettings>();

        cut.Find("[data-testid='tag-add-field'] input").Input("CLIMBING");
        cut.Find("[data-testid='tag-add-form']").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-testid='tag-add-field']").TextContent
            .Should().Contain("You already have a tag with that name."));
        Names(cut).Should().HaveCount(3);
    }

    [Fact]
    public async Task Rename_opens_a_dialog_prefilled_with_the_name_and_saves()
    {
        var tags = SomeTags(out var climbing);
        await using var context = CreateContext(tags, out var dialogs, out var snackbars);
        var cut = context.Render<TagSettings>();

        Click(cut, "tag-rename", 1);
        var field = dialogs.WaitForElement("[data-testid='label-name-dialog-field'] input");
        field.GetAttribute("value").Should().Be("Climbing");
        field.Input("Bouldering");
        dialogs.Find("[data-testid='label-name-dialog-save']").Click();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Book club", "Bouldering", "Mentor"));
        tags.Renamed.Should().Equal((climbing.Id, "Bouldering"));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Tag renamed"));
    }

    [Fact]
    public async Task A_rename_error_stays_in_the_dialog()
    {
        await using var context = CreateContext(SomeTags(out _), out var dialogs, out _);
        var cut = context.Render<TagSettings>();

        Click(cut, "tag-rename", 1);
        dialogs.WaitForElement("[data-testid='label-name-dialog-field'] input").Input("mentor");
        dialogs.Find("[data-testid='label-name-dialog-save']").Click();

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='label-name-dialog-field']").TextContent
            .Should().Contain("You already have a tag with that name."));
        dialogs.FindAll("[data-testid='label-name-dialog-save']").Should().ContainSingle();
    }

    [Fact]
    public async Task A_tag_removed_elsewhere_shows_a_problem_and_reloads()
    {
        var tags = SomeTags(out var climbing);
        await using var context = CreateContext(tags, out var dialogs, out var snackbars);
        var cut = context.Render<TagSettings>();
        Click(cut, "tag-rename", 1);
        dialogs.WaitForElement("[data-testid='label-name-dialog-field'] input").Input("Bouldering");
        tags.Tags.Remove(climbing);
        tags.RenameResult = false;

        dialogs.Find("[data-testid='label-name-dialog-save']").Click();

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("That tag was already removed."));
        cut.WaitForAssertion(() => Names(cut).Should().Equal("Book club", "Mentor"));
    }

    [Fact]
    public async Task Removing_a_tag_says_how_many_people_have_it()
    {
        var tags = SomeTags(out var climbing);
        await using var context = CreateContext(tags, out var dialogs, out var snackbars);
        var cut = context.Render<TagSettings>();

        Click(cut, "tag-remove", 1);
        dialogs.WaitForElement(".mud-dialog").TextContent
            .Should().Contain("Remove Climbing?")
            .And.Contain("2 people have this tag. Removing it takes it off them; nothing else about them changes.");
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Remove tag").Click();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Book club", "Mentor"));
        tags.Deleted.Should().Equal(climbing.Id);
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Tag removed"));
    }

    [Fact]
    public async Task Removing_an_unused_tag_says_no_one_has_it()
    {
        await using var context = CreateContext(SomeTags(out _), out var dialogs, out _);
        var cut = context.Render<TagSettings>();

        Click(cut, "tag-remove", 0);

        dialogs.WaitForElement(".mud-dialog").TextContent.Should().Contain("No one has this tag.");
    }

    [Fact]
    public async Task Cancelling_removes_nothing()
    {
        var tags = SomeTags(out _);
        await using var context = CreateContext(tags, out var dialogs, out _);
        var cut = context.Render<TagSettings>();

        Click(cut, "tag-remove", 1);
        dialogs.WaitForElement(".mud-dialog");
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        dialogs.WaitForAssertion(() => dialogs.FindAll(".mud-dialog").Should().BeEmpty());
        tags.Deleted.Should().BeEmpty();
        Names(cut).Should().HaveCount(3);
    }

    [Fact]
    public async Task The_count_is_refreshed_right_before_the_confirmation()
    {
        var tags = SomeTags(out var climbing);
        await using var context = CreateContext(tags, out var dialogs, out _);
        var cut = context.Render<TagSettings>();
        tags.PeopleCounts[climbing.Id] = 9;

        Click(cut, "tag-remove", 1);

        dialogs.WaitForElement(".mud-dialog").TextContent.Should().Contain("9 people have this tag.");
    }

    [Fact]
    public async Task A_tag_removed_elsewhere_before_the_confirmation_shows_a_problem()
    {
        var tags = SomeTags(out var climbing);
        await using var context = CreateContext(tags, out _, out var snackbars);
        var cut = context.Render<TagSettings>();
        tags.Tags.Remove(climbing);

        Click(cut, "tag-remove", 1);

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("That tag was already removed."));
        cut.WaitForAssertion(() => Names(cut).Should().Equal("Book club", "Mentor"));
        tags.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_tags_shows_the_empty_state_with_an_h2()
    {
        await using var context = CreateContext(new FakeTagService(), out _, out _);

        var cut = context.Render<TagSettings>();

        var empty = cut.Find("[data-testid='tags-empty']");
        empty.QuerySelector("h2")!.TextContent.Should().Be("No tags yet");
        empty.QuerySelector("h1").Should().BeNull();
    }

    [Fact]
    public async Task Icon_buttons_have_accessible_names()
    {
        await using var context = CreateContext(SomeTags(out _), out _, out _);

        var cut = context.Render<TagSettings>();

        cut.FindAll("[data-testid='tag-rename']").Select(b => b.GetAttribute("aria-label"))
            .Should().Equal("Rename Book club", "Rename Climbing", "Rename Mentor");
        cut.FindAll("[data-testid='tag-remove']").Select(b => b.GetAttribute("aria-label"))
            .Should().Equal("Remove Book club", "Remove Climbing", "Remove Mentor");
    }
}
