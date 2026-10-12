using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.Interactions;
using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

public class PersonConnectionsTests
{
    private static readonly Guid PersonId = Guid.NewGuid();

    private sealed record Providers(
        IRenderedComponent<MudDialogProvider> Dialogs,
        IRenderedComponent<MudSnackbarProvider> Snackbars);

    private static BunitContext CreateContext(
        FakePersonLinkService links,
        out Providers providers,
        FakeInteractionService? interactions = null)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<IPersonLinkService>(links);
        context.Services.AddSingleton<IInteractionService>(interactions ?? new FakeInteractionService());
        context.Render<MudPopoverProvider>();
        providers = new Providers(context.Render<MudDialogProvider>(), context.Render<MudSnackbarProvider>());
        return context;
    }

    private static IRenderedComponent<PersonConnections> RenderSection(
        BunitContext context,
        bool archived = false,
        bool busy = false) =>
        context.Render<PersonConnections>(parameters => parameters
            .Add(p => p.PersonId, PersonId)
            .Add(p => p.PersonFirstName, "Anna")
            .Add(p => p.PersonIsArchived, archived)
            .Add(p => p.Busy, busy));

    private static PersonConnection Connection(string name, PersonLinkKind role, bool archived = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, archived, role);

    [Fact]
    public async Task Rows_show_the_role_and_a_link_to_the_other_profile()
    {
        var marco = Connection("Marco Rossi", PersonLinkKind.Parent);
        var links = new FakePersonLinkService();
        links.Connections.Add(marco);
        await using var context = CreateContext(links, out _);

        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connection-role']").TextContent.Should().Be("Parent");
        var link = cut.Find("[data-testid='person-connection-link']");
        link.TextContent.Should().Be("Marco Rossi");
        link.GetAttribute("href").Should().Be($"/people/{marco.OtherPersonId}");
        cut.FindAll("[data-testid='person-connection-archived']").Should().BeEmpty();
        cut.Find("h2").TextContent.Should().Be("Connections");
        cut.FindAll("h1").Should().BeEmpty("the page, not the section, owns the h1");
    }

    [Fact]
    public async Task An_archived_connection_is_labelled_and_names_are_rendered_as_text()
    {
        var links = new FakePersonLinkService();
        links.Connections.Add(Connection("<b>Old</b> Friend", PersonLinkKind.Friend, archived: true));
        await using var context = CreateContext(links, out _);

        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connection-archived']").TextContent.Should().Contain("Archived");
        cut.Find("[data-testid='person-connection-link']").TextContent.Should().Be("<b>Old</b> Friend");
        cut.FindAll("b").Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_connections_a_calm_empty_text_and_the_add_button_show()
    {
        await using var context = CreateContext(new FakePersonLinkService(), out _);

        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connections-empty']").TextContent
            .Should().Be(PersonLinkText.Empty("Anna"));
        cut.Find("[data-testid='person-add-connection']").TextContent.Should().Contain("Add a connection");
    }

    [Fact]
    public async Task An_archived_person_with_no_connections_shows_no_section_at_all()
    {
        await using var context = CreateContext(new FakePersonLinkService(), out _);

        var cut = RenderSection(context, archived: true);

        cut.FindAll("[data-testid='person-connections']").Should().BeEmpty();
        cut.FindAll("[data-testid='person-add-connection']").Should().BeEmpty();
    }

    [Fact]
    public async Task An_archived_person_keeps_their_connections_but_cannot_add_more()
    {
        var links = new FakePersonLinkService();
        links.Connections.Add(Connection("Marco Rossi", PersonLinkKind.Sibling));
        await using var context = CreateContext(links, out _);

        var cut = RenderSection(context, archived: true);

        cut.FindAll("[data-testid='person-connection']").Should().ContainSingle();
        cut.FindAll("[data-testid='person-add-connection']").Should().BeEmpty();
        cut.Find("[data-testid='person-connection-edit']").Should().NotBeNull();
    }

    [Fact]
    public async Task The_row_buttons_have_accessible_names_and_are_disabled_while_the_page_is_busy()
    {
        var links = new FakePersonLinkService();
        links.Connections.Add(Connection("Marco Rossi", PersonLinkKind.Friend));
        await using var context = CreateContext(links, out _);

        var cut = RenderSection(context, busy: true);

        cut.Find("[data-testid='person-connection-edit']").GetAttribute("aria-label")
            .Should().Be("Edit connection with Marco Rossi");
        cut.Find("[data-testid='person-connection-remove']").GetAttribute("aria-label")
            .Should().Be("Remove connection with Marco Rossi");
        cut.Find("[data-testid='person-connection-edit']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='person-connection-remove']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='person-add-connection']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Removing_asks_first_then_deletes_notifies_without_a_name_and_reloads()
    {
        var marco = Connection("Marco Rossi", PersonLinkKind.Friend);
        var links = new FakePersonLinkService();
        links.Connections.Add(marco);
        await using var context = CreateContext(links, out var providers);
        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connection-remove']").Click();

        providers.Dialogs.WaitForAssertion(() =>
            providers.Dialogs.Markup.Should().Contain(PersonLinkText.RemoveTitle)
                .And.Contain(PersonLinkText.RemoveMessage));
        providers.Dialogs.Markup.Should().NotContain("Marco");
        links.Deleted.Should().BeEmpty("nothing is removed before the confirmation");

        providers.Dialogs.FindAll("button").Single(b => b.TextContent.Contains("Remove")).Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='person-connection']").Should().BeEmpty());
        links.Deleted.Should().Equal(marco.LinkId);
        providers.Snackbars.WaitForAssertion(() =>
            providers.Snackbars.Markup.Should().Contain(PersonLinkText.Removed).And.NotContain("Marco"));
        links.Listed.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Cancelling_the_removal_deletes_nothing()
    {
        var links = new FakePersonLinkService();
        links.Connections.Add(Connection("Marco Rossi", PersonLinkKind.Friend));
        await using var context = CreateContext(links, out var providers);
        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connection-remove']").Click();
        providers.Dialogs.WaitForAssertion(() => providers.Dialogs.Markup.Should().Contain(PersonLinkText.RemoveTitle));
        providers.Dialogs.FindAll("button").Single(b => b.TextContent.Contains("Cancel")).Click();

        links.Deleted.Should().BeEmpty();
        cut.FindAll("[data-testid='person-connection']").Should().ContainSingle();
    }

    [Fact]
    public async Task A_connection_removed_elsewhere_says_so_and_the_list_reloads()
    {
        var links = new FakePersonLinkService { DeleteResult = false };
        links.Connections.Add(Connection("Marco Rossi", PersonLinkKind.Friend));
        await using var context = CreateContext(links, out var providers);
        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connection-remove']").Click();
        providers.Dialogs.WaitForAssertion(() => providers.Dialogs.Markup.Should().Contain(PersonLinkText.RemoveTitle));
        providers.Dialogs.FindAll("button").Single(b => b.TextContent.Contains("Remove")).Click();

        providers.Snackbars.WaitForAssertion(() => providers.Snackbars.Markup.Should().Contain(PersonLinkText.Gone));
    }

    [Fact]
    public async Task A_load_failure_shows_an_alert_with_try_again_that_recovers()
    {
        var links = new FakePersonLinkService { ThrowOnList = new InvalidOperationException("boom Marco Rossi") };
        await using var context = CreateContext(links, out _);
        var cut = RenderSection(context);

        cut.Find("[data-testid='person-connections-error']").TextContent
            .Should().Contain(PersonLinkText.LoadFailed).And.NotContain("boom");

        links.ThrowOnList = null;
        links.Connections.Add(Connection("Marco Rossi", PersonLinkKind.Friend));
        cut.Find("[data-testid='person-connections-reload']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='person-connection']").Should().ContainSingle());
    }


    [Fact]
    public async Task Adding_opens_the_dialog_offering_active_people_and_cancelling_adds_nothing()
    {
        var links = new FakePersonLinkService();
        var interactions = new FakeInteractionService();
        await using var context = CreateContext(links, out var providers, interactions);
        var cut = RenderSection(context);

        cut.Find("[data-testid='person-add-connection']").Click();

        providers.Dialogs.WaitForAssertion(() => providers.Dialogs.Find("[data-testid='person-link-form']"));
        interactions.CandidateQueries.Should().ContainSingle().Which.ProfilePersonId.Should().Be(Guid.Empty);
        providers.Dialogs.Find("[data-testid='person-link-cancel']").Click();

        links.Created.Should().BeEmpty();
    }
}
