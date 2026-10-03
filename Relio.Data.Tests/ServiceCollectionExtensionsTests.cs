using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data.DependencyInjection;

namespace Relio.Data.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRelioData_with_connection_string_registers_RelioDbContext_using_sql_server()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration("Server=.;Database=Relio;Trusted_Connection=True;TrustServerCertificate=True;");

        services.AddRelioData(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();

        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddRelioData_without_connection_string_throws_a_clear_exception(string? connectionString)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(connectionString);

        var act = () => services.AddRelioData(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Relio*");
    }

    [Fact]
    public void AddRelioData_with_null_services_throws()
    {
        IServiceCollection? services = null;
        var configuration = BuildConfiguration("Server=.;Database=Relio;Trusted_Connection=True;TrustServerCertificate=True;");

        var act = () => services!.AddRelioData(configuration);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddRelioData_with_null_configuration_throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddRelioData(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static IConfiguration BuildConfiguration(string? relioConnectionString)
    {
        var data = new Dictionary<string, string?>();
        if (relioConnectionString is not null)
        {
            data[$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = relioConnectionString;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }
}
