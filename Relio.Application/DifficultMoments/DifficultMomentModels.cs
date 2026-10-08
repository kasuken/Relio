using Relio.Domain;

namespace Relio.Application.DifficultMoments;

/// <summary>Request to create a difficult moment.</summary>
public sealed record CreateDifficultMomentRequest
{
    /// <summary>The person this difficult moment is associated with.</summary>
    public required Guid PersonId { get; init; }

    /// <summary>The date when the moment occurred.</summary>
    public required DateOnly OccurredOn { get; init; }

    /// <summary>What happened during the moment.</summary>
    public required string Description { get; init; }

    /// <summary>What set off the moment (optional).</summary>
    public string? Trigger { get; init; }

    /// <summary>How the moment was resolved (optional).</summary>
    public string? Resolution { get; init; }

    /// <summary>Lessons learned or takeaways (optional).</summary>
    public string? LessonsLearned { get; init; }

    /// <summary>The status of the moment.</summary>
    public DifficultMomentStatus Status { get; init; } = DifficultMomentStatus.Open;

    /// <summary>The date when the moment was resolved (optional).</summary>
    public DateOnly? ResolvedOn { get; init; }

    /// <summary>Optional id of an earlier moment this is a recurrence of.</summary>
    public Guid? RecurrenceOfId { get; init; }
}

/// <summary>Request to update an existing difficult moment.</summary>
public sealed record UpdateDifficultMomentRequest
{
    /// <summary>The date when the moment occurred.</summary>
    public required DateOnly OccurredOn { get; init; }

    /// <summary>What happened during the moment.</summary>
    public required string Description { get; init; }

    /// <summary>What set off the moment (optional).</summary>
    public string? Trigger { get; init; }

    /// <summary>How the moment was resolved (optional).</summary>
    public string? Resolution { get; init; }

    /// <summary>Lessons learned or takeaways (optional).</summary>
    public string? LessonsLearned { get; init; }

    /// <summary>The status of the moment.</summary>
    public required DifficultMomentStatus Status { get; init; }

    /// <summary>The date when the moment was resolved (optional).</summary>
    public DateOnly? ResolvedOn { get; init; }

    /// <summary>Optional id of an earlier moment this is a recurrence of.</summary>
    public Guid? RecurrenceOfId { get; init; }
}

/// <summary>Full details of a difficult moment, including its recurrences and parent link.</summary>
public sealed record DifficultMomentDetails(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    bool PersonIsArchived,
    DateOnly OccurredOn,
    string Description,
    string? Trigger,
    string? Resolution,
    string? LessonsLearned,
    DifficultMomentStatus Status,
    DateOnly? ResolvedOn,
    Guid? RecurrenceOfId,
    string? RecurrenceOfDescription,
    IReadOnlyList<DifficultMomentRecurrenceItem> Recurrences,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Summary of a recurring moment linked to a parent moment.</summary>
public sealed record DifficultMomentRecurrenceItem(
    Guid Id,
    DateOnly OccurredOn,
    string Description,
    DifficultMomentStatus Status);

/// <summary>Summary of a difficult moment for lists on a person's profile.</summary>
public sealed record DifficultMomentSummary(
    Guid Id,
    Guid PersonId,
    DateOnly OccurredOn,
    string Description,
    DifficultMomentStatus Status,
    DateOnly? ResolvedOn,
    Guid? RecurrenceOfId,
    int RecurrencesCount,
    DateTime CreatedAtUtc);

/// <summary>Item for the overview reflection list across people.</summary>
public sealed record DifficultMomentOverviewItem(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    bool PersonIsArchived,
    DateOnly OccurredOn,
    string Description,
    string? Trigger,
    string? Resolution,
    string? LessonsLearned,
    DifficultMomentStatus Status,
    DateOnly? ResolvedOn,
    Guid? RecurrenceOfId,
    int RecurrencesCount,
    DateTime CreatedAtUtc);

/// <summary>Filter parameters for querying difficult moments across people.</summary>
public sealed record DifficultMomentFilterRequest
{
    /// <summary>Filter by status (null matches all statuses).</summary>
    public DifficultMomentStatus? Status { get; init; }

    /// <summary>Filter by specific person (null matches all people).</summary>
    public Guid? PersonId { get; init; }

    /// <summary>Whether to include archived people (default false).</summary>
    public bool IncludeArchived { get; init; } = false;
}

/// <summary>Lookup item for selecting an earlier moment to link as recurrence.</summary>
public sealed record DifficultMomentLookupItem(
    Guid Id,
    DateOnly OccurredOn,
    string Description,
    DifficultMomentStatus Status);
