using Microsoft.EntityFrameworkCore;

namespace Relio.Data;

/// <summary>
/// Entity Framework Core database context for Relio. Entity configurations are discovered
/// automatically from this assembly via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>.
/// </summary>
public sealed class RelioDbContext(DbContextOptions<RelioDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RelioDbContext).Assembly);
    }
}
