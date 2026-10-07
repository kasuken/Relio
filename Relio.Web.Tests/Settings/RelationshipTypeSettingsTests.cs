using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Ownership;
using Relio.Web.Components.Settings;
using Relio.Web.Tests.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Settings;

public class RelationshipTypeSettingsTests
{
    // See ConfirmDialogTests for why the context is created per test with "await using".
    private static BunitContext CreateContext(
        FakeRelationshipTypeService types,
        out IRenderedComponent<MudPopoverProvider> popovers,
        out IRenderedComponent<MudDialogProvider> dialogs,
        out IRenderedComponent<MudSnackbarProvider> snackbars)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<Relio.Application.People.IRelationshipTypeService>(types);
        popovers = context.Render<MudPopoverProvider>();
        dialogs = context.Render<MudDialogProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    private static FakeRelationshipTypeService TypesWithPeople(out Relio.Domain.RelationshipType friend)
    {
        var types = new FakeRelationshipTypeService();
        friend = types.Add("Friend", people: 2);
        types.Add("Colleague", people: 1);
        types.Add("Other");
        return types;
    }

    private static IReadOnlyList<string> Names(IRenderedComponent<RelationshipTypeSettings> cut) =>
        cut.FindAll("[data-testid='relationship-type-row-name']").Select(e => e.TextContent.Trim()).ToList();

    private static void Click(IRenderedComponent<RelationshipTypeSettings> cut, string testId, int row) =>
        cut.FindAll($"[data-testid='{testId}']")[row].Click();

    [Fact]
    public async Task Lists_types_in_order_with_how_many_people_have_each()
    {
        await using var context = CreateContext(TypesWithPeople(out _), out _, out _, out _);

        var cut = context.Render<RelationshipTypeSettings>();

        Names(cut).Should().Equal("Friend", "Colleague", "Other");
        cut.FindAll("[data-testid='relationship-type-row-count']").Select(e => e.TextContent.Trim())
            .Should().Equal("2 people", "1 person", "No one yet");
    }

    [Fact]
    public async Task Adding_a_type_sends_the_name_shows_Relationship_type_added_and_reloads()
    {
        var types = TypesWithPeople(out _);
        await using var context = CreateContext(types, out _, out _, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();
        var loadsBefore = types.ListWithUsageCalls;

        cut.Find("[data-testid='relationship-type-add-field'] input").Input("  Mentor ");
        cut.Find("[data-testid='relationship-type-add-form']").Submit();

        cut.WaitForAssertion(() => Names(cut).Should().EndWith("Mentor"));
        types.Created.Should().Equal("  Mentor ");
        types.ListWithUsageCalls.Should().BeGreaterThan(loadsBefore);
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Relationship type added"));
        cut.Find("[data-testid='relationship-type-add-field'] input").GetAttribute("value")
            .Should().BeNullOrEmpty("the field is cleared for the next name");
    }

    [Fact]
    public async Task A_taken_name_is_explained_under_the_add_field()
    {
        await using var context = CreateContext(TypesWithPeople(out _), out _, out _, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();

        cut.Find("[data-testid='relationship-type-add-field'] input").Input("friend");
        cut.Find("[data-testid='relationship-type-add-form']").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-testid='relationship-type-add-field']").TextContent
            .Should().Contain("You already have a relationship type with that name."));
        Names(cut).Should().HaveCount(3);
        snackbars.Markup.Should().NotContain("Relationship type added");
    }

    [Fact]
    public async Task A_blank_name_is_explained_under_the_add_field()
    {
        await using var context = CreateContext(TypesWithPeople(out _), out _, out _, out _);
        var cut = context.Render<RelationshipTypeSettings>();

        cut.Find("[data-testid='relationship-type-add-form']").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-testid='relationship-type-add-field']").TextContent
            .Should().Contain("Enter a name."));
    }

    [Fact]
    public async Task Rename_opens_a_dialog_prefilled_with_the_name_and_saves()
    {
        var types = TypesWithPeople(out var friend);
        await using var context = CreateContext(types, out _, out var dialogs, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-rename", 0);
        var field = dialogs.WaitForElement("[data-testid='label-name-dialog-field'] input");
        field.GetAttribute("value").Should().Be("Friend");
        field.Input("Close friend");
        dialogs.Find("[data-testid='label-name-dialog-save']").Click();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Close friend", "Colleague", "Other"));
        types.Renamed.Should().Equal((friend.Id, "Close friend"));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Relationship type renamed"));
        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-testid='label-name-dialog-save']").Should().BeEmpty());
    }

    [Fact]
    public async Task A_rename_error_stays_in_the_dialog()
    {
        var types = TypesWithPeople(out _);
        await using var context = CreateContext(types, out _, out var dialogs, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-rename", 0);
        dialogs.WaitForElement("[data-testid='label-name-dialog-field'] input").Input("colleague");
        dialogs.Find("[data-testid='label-name-dialog-save']").Click();

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='label-name-dialog-field']").TextContent
            .Should().Contain("You already have a relationship type with that name."));
        dialogs.FindAll("[data-testid='label-name-dialog-save']").Should().ContainSingle("the dialog is still open");
        dialogs.Find("[data-testid='label-name-dialog-field'] input").GetAttribute("value").Should().Be("colleague");
        Names(cut).Should().Equal("Friend", "Colleague", "Other");
        snackbars.Markup.Should().NotContain("renamed");
    }

    [Fact]
    public async Task Cancelling_a_rename_changes_nothing()
    {
        var types = TypesWithPeople(out _);
        await using var context = CreateContext(types, out _, out var dialogs, out _);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-rename", 0);
        dialogs.WaitForElement("[data-testid='label-name-dialog-cancel']").Click();

        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-testid='label-name-dialog-save']").Should().BeEmpty());
        types.Renamed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_type_removed_elsewhere_shows_a_problem_and_reloads()
    {
        var types = TypesWithPeople(out var friend);
        await using var context = CreateContext(types, out _, out var dialogs, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();
        Click(cut, "relationship-type-rename", 0);
        dialogs.WaitForElement("[data-testid='label-name-dialog-field'] input").Input("Mate");
        types.Types.Remove(friend);
        types.RenameResult = false;

        dialogs.Find("[data-testid='label-name-dialog-save']").Click();

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("That relationship type was already removed."));
        cut.WaitForAssertion(() => Names(cut).Should().Equal("Colleague", "Other"));
    }

    [Fact]
    public async Task Removing_an_unused_type_asks_for_a_plain_confirmation()
    {
        var types = TypesWithPeople(out _);
        var other = types.Types.Single(t => t.Name == "Other");
        await using var context = CreateContext(types, out _, out var dialogs, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-remove", 2);
        dialogs.WaitForElement(".mud-dialog").TextContent.Should().Contain("Remove Other?").And.Contain("No one has this relationship type.");
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Remove").Click();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Friend", "Colleague"));
        types.Deleted.Should().Equal((other.Id, (Guid?)null));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Relationship type removed"));
    }

    [Fact]
    public async Task Removing_a_type_in_use_moves_people_to_the_chosen_type()
    {
        var types = TypesWithPeople(out var friend);
        var colleague = types.Types.Single(t => t.Name == "Colleague");
        await using var context = CreateContext(types, out var popovers, out var dialogs, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-remove", 0);
        dialogs.WaitForElement("[data-testid='remove-type-dialog-message']").TextContent
            .Should().Contain("2 people have this relationship type.");
        dialogs.Find("[data-testid='remove-type-dialog-confirm']").HasAttribute("disabled")
            .Should().BeTrue("a target must be chosen first");
        dialogs.Find("[data-testid='remove-type-dialog-target-field'] .mud-input-control").MouseDown();
        popovers.WaitForElements(".mud-popover-open .mud-list-item").Select(i => i.TextContent.Trim())
            .Should().Equal("Colleague", "Other");
        popovers.FindAll(".mud-popover-open .mud-list-item").Single(i => i.TextContent.Trim() == "Colleague").Click();
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='remove-type-dialog-confirm']").HasAttribute("disabled").Should().BeFalse());
        dialogs.Find("[data-testid='remove-type-dialog-confirm']").Click();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Colleague", "Other"));
        types.Deleted.Should().Equal((friend.Id, (Guid?)colleague.Id));
        cut.FindAll("[data-testid='relationship-type-row-count']").First().TextContent.Trim().Should().Be("3 people");
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Relationship type removed"));
    }

    [Fact]
    public async Task Removing_a_type_in_use_can_leave_people_without_a_type()
    {
        var types = TypesWithPeople(out var friend);
        await using var context = CreateContext(types, out _, out var dialogs, out _);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-remove", 0);
        dialogs.WaitForElement("[data-testid='remove-type-dialog-choice']");
        dialogs.FindAll("label.mud-radio").Single(l => l.TextContent.Contains("Leave their relationship type empty")).QuerySelector("input")!.Click();
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='remove-type-dialog-confirm']").HasAttribute("disabled").Should().BeFalse());
        dialogs.Find("[data-testid='remove-type-dialog-confirm']").Click();

        cut.WaitForAssertion(() => Names(cut).Should().Equal("Colleague", "Other"));
        types.Deleted.Should().Equal((friend.Id, (Guid?)null));
    }

    [Fact]
    public async Task With_no_other_types_only_leaving_empty_is_offered()
    {
        var types = new FakeRelationshipTypeService();
        var only = types.Add("Friend", people: 3);
        await using var context = CreateContext(types, out _, out var dialogs, out _);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-remove", 0);

        dialogs.WaitForElement("[data-testid='remove-type-dialog-message']");
        dialogs.FindAll("[data-testid='remove-type-dialog-choice']").Should().BeEmpty();
        dialogs.Find(".mud-dialog").TextContent.Should().Contain("They'll have no relationship type.");
        dialogs.Find("[data-testid='remove-type-dialog-confirm']").HasAttribute("disabled").Should().BeFalse();
        dialogs.Find("[data-testid='remove-type-dialog-confirm']").Click();

        cut.WaitForAssertion(() => types.Deleted.Should().Equal((only.Id, (Guid?)null)));
    }

    [Fact]
    public async Task Cancelling_removes_nothing()
    {
        var types = TypesWithPeople(out _);
        await using var context = CreateContext(types, out _, out var dialogs, out _);
        var cut = context.Render<RelationshipTypeSettings>();

        Click(cut, "relationship-type-remove", 0);
        dialogs.WaitForElement("[data-testid='remove-type-dialog-cancel']").Click();

        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-testid='remove-type-dialog-confirm']").Should().BeEmpty());
        types.Deleted.Should().BeEmpty();
        Names(cut).Should().HaveCount(3);
    }

    [Fact]
    public async Task The_count_is_refreshed_right_before_the_dialog_opens()
    {
        var types = TypesWithPeople(out var friend);
        await using var context = CreateContext(types, out _, out var dialogs, out _);
        var cut = context.Render<RelationshipTypeSettings>();
        types.PeopleCounts[friend.Id] = 7;

        Click(cut, "relationship-type-remove", 0);

        dialogs.WaitForElement("[data-testid='remove-type-dialog-message']").TextContent
            .Should().Contain("7 people have this relationship type.");
    }

    [Fact]
    public async Task A_target_removed_in_the_meantime_shows_a_problem()
    {
        var types = TypesWithPeople(out _);
        await using var context = CreateContext(types, out var popovers, out var dialogs, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();
        types.ThrowOnNextDelete = new ForeignEntityNotOwnedException(ForeignEntityNames.RelationshipTypes);

        Click(cut, "relationship-type-remove", 0);
        dialogs.WaitForElement("[data-testid='remove-type-dialog-target-field'] .mud-input-control").MouseDown();
        popovers.WaitForElements(".mud-popover-open .mud-list-item").First().Click();
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='remove-type-dialog-confirm']").HasAttribute("disabled").Should().BeFalse());
        dialogs.Find("[data-testid='remove-type-dialog-confirm']").Click();

        snackbars.WaitForAssertion(() => snackbars.Markup.Should()
            .Contain("The relationship type you chose was removed in the meantime. Try again."));
        Names(cut).Should().Equal("Friend", "Colleague", "Other");
    }

    [Fact]
    public async Task A_type_removed_elsewhere_before_the_dialog_opens_shows_a_problem()
    {
        var types = TypesWithPeople(out var friend);
        await using var context = CreateContext(types, out _, out _, out var snackbars);
        var cut = context.Render<RelationshipTypeSettings>();
        types.Types.Remove(friend);

        Click(cut, "relationship-type-remove", 0);

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("That relationship type was already removed."));
        cut.WaitForAssertion(() => Names(cut).Should().Equal("Colleague", "Other"));
        types.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_types_shows_the_empty_state_with_an_h2()
    {
        await using var context = CreateContext(new FakeRelationshipTypeService(), out _, out _, out _);

        var cut = context.Render<RelationshipTypeSettings>();

        var empty = cut.Find("[data-testid='relationship-types-empty']");
        empty.QuerySelector("h2")!.TextContent.Should().Be("No relationship types");
        empty.QuerySelector("h1").Should().BeNull("the page has its own h1");
        cut.FindAll("[data-testid='relationship-type-list']").Should().BeEmpty();
        cut.Find("[data-testid='relationship-type-add-form']").Should().NotBeNull("you can still add one");
    }

    [Fact]
    public async Task Icon_buttons_have_accessible_names()
    {
        await using var context = CreateContext(TypesWithPeople(out _), out _, out _, out _);

        var cut = context.Render<RelationshipTypeSettings>();

        cut.FindAll("[data-testid='relationship-type-rename']").Select(b => b.GetAttribute("aria-label"))
            .Should().Equal("Rename Friend", "Rename Colleague", "Rename Other");
        cut.FindAll("[data-testid='relationship-type-remove']").Select(b => b.GetAttribute("aria-label"))
            .Should().Equal("Remove Friend", "Remove Colleague", "Remove Other");
    }
}
