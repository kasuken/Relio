using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Relio.Data.Encryption;

/// <summary>
/// Keeps EF models that capture different Data Protection providers or migration modes isolated.
/// </summary>
public sealed class RelioDbContextModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is RelioDbContext relioDbContext)
        {
            return new RelioModelCacheKey(
                context.GetType(),
                relioDbContext.FieldProtectionModelIdentity,
                relioDbContext.FieldProtectionMode,
                designTime);
        }

        return new DefaultModelCacheKey(context.GetType(), designTime);
    }

    private readonly record struct RelioModelCacheKey(
        Type ContextType,
        Guid ProtectorIdentity,
        FieldProtectionMode ProtectionMode,
        bool DesignTime);

    private readonly record struct DefaultModelCacheKey(Type ContextType, bool DesignTime);
}
