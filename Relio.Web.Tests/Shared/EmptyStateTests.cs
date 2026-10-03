using Bunit;
using Relio.Web.Components.Shared;

namespace Relio.Web.Tests.Shared;

public class EmptyStateTests : BunitContext
{
    public EmptyStateTests() => this.UseMudBlazor();

    [Fact]
    public void Renders_the_title_and_message()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No one here yet")
            .Add(p => p.Message, "Add the first person you want to keep in touch with."));

        cut.Markup.Should().Contain("No one here yet");
        cut.Markup.Should().Contain("Add the first person you want to keep in touch with.");
    }

    [Fact]
    public void Uses_h1_by_default_so_FocusOnNavigate_has_something_to_focus()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No one here yet")
            .Add(p => p.Message, "Message."));

        cut.Find("h1").TextContent.Should().Be("No one here yet");
    }

    [Fact]
    public void Honours_a_custom_heading_tag()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No one here yet")
            .Add(p => p.Message, "Message.")
            .Add(p => p.HeadingTag, "h2"));

        cut.Find("h2").TextContent.Should().Be("No one here yet");
    }

    [Fact]
    public void Does_not_render_an_action_button_without_a_handler()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No one here yet")
            .Add(p => p.Message, "Message.")
            .Add(p => p.ActionLabel, "Add a person"));

        cut.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void Renders_and_wires_up_the_action_button_when_a_handler_is_given()
    {
        var clicked = 0;

        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No one here yet")
            .Add(p => p.Message, "Message.")
            .Add(p => p.ActionLabel, "Add a person")
            .Add(p => p.OnAction, () => clicked++));

        var button = cut.Find("button");
        button.TextContent.Should().Contain("Add a person");

        button.Click();

        clicked.Should().Be(1);
    }
}
