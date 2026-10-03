namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> that is dynamically skipped when no SQL Server instance is
/// available (<see cref="SqlServerTestEnvironment.IsAvailable"/> is <see langword="false"/>),
/// instead of failing or silently passing.
/// </summary>
/// <remarks>
/// xUnit 2.9 has no built-in way to decide <c>Skip</c> at runtime, so this sets it in the
/// constructor instead of pulling in an extra package (e.g. Xunit.SkippableFact) for one
/// attribute's worth of behaviour. When <c>ConnectionStrings__Relio</c> *is* set but the server it
/// points at is unreachable, tests are deliberately left to run (and fail) rather than skip - that
/// is the CI case this project exists to catch.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerTestEnvironment.IsAvailable)
        {
            Skip = SqlServerTestEnvironment.SkipReason;
        }
    }
}
