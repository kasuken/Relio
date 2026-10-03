using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Relio.Data.Tests.Seeding;

/// <summary>Minimal <see cref="IHostEnvironment"/> test double - only <see cref="EnvironmentName"/> matters here.</summary>
internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Relio.Data.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
