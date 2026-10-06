using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Plays "somebody else saved at the same moment": just before the first save of the context it is
/// attached to, it inserts a tag through a <b>separate</b> context, then lets the save carry on.
/// That is the only way to hit the window between the service reading a user's tags and saving the
/// new one it decided to create, which two sequential calls can never reach.
/// </summary>
internal sealed class InsertTagOnFirstSaveInterceptor(SqlServerDatabaseFixture fixture, string ownerId, string tagName)
    : SaveChangesInterceptor
{
    private int _fired;

    /// <summary>True once the competing tag has been inserted.</summary>
    public bool Fired => _fired > 0;

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _fired) == 1)
        {
            await using var other = fixture.CreateDbContext();
            other.Tags.Add(new Tag { OwnerId = ownerId, Name = tagName });
            await other.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
