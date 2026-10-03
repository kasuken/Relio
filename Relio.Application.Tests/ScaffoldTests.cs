namespace Relio.Application.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Application_assembly_is_loadable()
    {
        typeof(Relio.Application.AssemblyMarker).Assembly.GetName().Name.Should().Be("Relio.Application");
    }
}
