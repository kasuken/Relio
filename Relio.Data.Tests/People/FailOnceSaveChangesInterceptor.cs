using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Relio.Data.Tests.People;

/// <summary>
/// Makes the first <c>SaveChangesAsync</c> on a context fail, so a test can prove a service does not
/// leave the failed entity tracked for the next save to insert again.
/// </summary>
internal sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
{
    private bool _failed;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_failed)
        {
            _failed = true;
            throw new InvalidOperationException("simulated save failure");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
