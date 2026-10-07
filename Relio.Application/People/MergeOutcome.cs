namespace Relio.Application.People;

/// <summary>What <c>IPersonMergeService.MergeAsync</c> did.</summary>
public enum MergeOutcome
{
    /// <summary>The duplicate's contents were moved to the primary and the duplicate was removed.</summary>
    Merged,

    /// <summary>
    /// Either person is missing or is not the current user's. The two cases are indistinguishable on
    /// purpose, and nothing was changed.
    /// </summary>
    NotFound,
}
