namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// The <see cref="TheoryAttribute"/> twin of <see cref="SqlServerFactAttribute"/>: dynamically
/// skipped, not failed and not silently passed, when no SQL Server instance is available.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        if (!SqlServerTestEnvironment.IsAvailable)
        {
            Skip = SqlServerTestEnvironment.SkipReason;
        }
    }
}
