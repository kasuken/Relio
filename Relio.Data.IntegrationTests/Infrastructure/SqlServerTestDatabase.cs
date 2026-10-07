using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Relio.Data.IntegrationTests.Infrastructure;

internal static class SqlServerTestDatabase
{
    public static async Task CreateAndMigrateAsync(RelioDbContext dbContext)
    {
        var previousTimeout = dbContext.Database.GetCommandTimeout();
        dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        try
        {
            // Provision before Migrate so its session-owned lock stays on the final database
            // connection rather than crossing database creation and connection reopening.
            await dbContext.GetService<IRelationalDatabaseCreator>().CreateAsync();
            await dbContext.Database.MigrateAsync();
        }
        finally
        {
            dbContext.Database.SetCommandTimeout(previousTimeout);
        }
    }
}
