using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data.Encryption;
using Relio.Data.DependencyInjection;

namespace Relio.Data.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRelioData_with_connection_string_registers_RelioDbContext_using_sql_server()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionFieldProtector>(FieldProtector);
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

    [Fact]
    public void AddRelioData_without_database_provider_configured_defaults_to_sql_server()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionFieldProtector>(FieldProtector);
        var configuration = BuildConfiguration("Server=.;Database=Relio;Trusted_Connection=True;TrustServerCertificate=True;");

        services.AddRelioData(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();

        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("inmemory")]
    [InlineData("INMEMORY")]
    public void AddRelioData_with_database_provider_InMemory_registers_RelioDbContext_using_the_in_memory_provider(string providerValue)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionFieldProtector>(FieldProtector);
        var configuration = BuildConfiguration(relioConnectionString: null, databaseProvider: providerValue);

        services.AddRelioData(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();

        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.InMemory");
    }

    [Fact]
    public void AddRelioData_with_database_provider_InMemory_does_not_require_a_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(relioConnectionString: null, databaseProvider: "InMemory");

        var act = () => services.AddRelioData(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddRelioData_with_an_unknown_database_provider_throws_a_clear_exception()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(relioConnectionString: null, databaseProvider: "Postgres");

        var act = () => services.AddRelioData(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Database:Provider*Postgres*");
    }

    [Theory]
    [InlineData(null, ServiceCollectionExtensions.SqlServerProvider)]
    [InlineData("", ServiceCollectionExtensions.SqlServerProvider)]
    [InlineData("SqlServer", ServiceCollectionExtensions.SqlServerProvider)]
    [InlineData("InMemory", ServiceCollectionExtensions.InMemoryProvider)]
    public void GetProviderName_resolves_the_expected_provider(string? configuredValue, string expectedProvider)
    {
        var configuration = BuildConfiguration(relioConnectionString: null, databaseProvider: configuredValue);

        ServiceCollectionExtensions.GetProviderName(configuration).Should().Be(expectedProvider);
    }

    [Fact]
    public void IsInMemoryProvider_is_false_by_default()
    {
        var configuration = BuildConfiguration(relioConnectionString: null, databaseProvider: null);

        ServiceCollectionExtensions.IsInMemoryProvider(configuration).Should().BeFalse();
    }

    [Fact]
    public void IsInMemoryProvider_is_true_when_configured()
    {
        var configuration = BuildConfiguration(relioConnectionString: null, databaseProvider: "InMemory");

        ServiceCollectionExtensions.IsInMemoryProvider(configuration).Should().BeTrue();
    }

    [Fact]
    public void GetProviderName_with_null_configuration_throws()
    {
        var act = () => ServiceCollectionExtensions.GetProviderName(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static IConfiguration BuildConfiguration(string? relioConnectionString, string? databaseProvider = null)
    {
        var data = new Dictionary<string, string?>();
        if (relioConnectionString is not null)
        {
            data[$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = relioConnectionString;
        }

        if (databaseProvider is not null)
        {
            data[ServiceCollectionExtensions.ProviderConfigurationKey] = databaseProvider;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }
}
