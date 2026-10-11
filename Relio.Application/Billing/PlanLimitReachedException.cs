namespace Relio.Application.Billing;

/// <summary>
/// Thrown when adding or restoring people would take the user over their plan's active-people limit.
/// Nothing was saved. The message carries only the limit, never a name or any other content.
/// </summary>
public sealed class PlanLimitReachedException : Exception
{
    public PlanLimitReachedException(int limit)
        : base($"The current plan allows up to {limit} active people.")
    {
        Limit = limit;
    }

    /// <summary>The active-people limit of the user's current plan.</summary>
    public int Limit { get; }
}
