namespace Relio.Domain;

/// <summary>
/// A recorded difficult moment in a relationship, for private reflection and pattern recognition.
/// </summary>
public sealed class DifficultMoment : OwnedEntity
{
    /// <summary>The maximum length of a description, in characters.</summary>
    public const int DescriptionMaxLength = 10_000;

    /// <summary>The maximum length of a trigger explanation, in characters.</summary>
    public const int TriggerMaxLength = 10_000;

    /// <summary>The maximum length of a resolution description, in characters.</summary>
    public const int ResolutionMaxLength = 10_000;

    /// <summary>The maximum length of lessons learned, in characters.</summary>
    public const int LessonsLearnedMaxLength = 10_000;

    /// <summary>The id of the person this difficult moment is with.</summary>
    public Guid PersonId { get; set; }

    /// <summary>The person this difficult moment is with, when loaded.</summary>
    public Person Person { get; set; } = null!;

    /// <summary>The calendar date on which the difficult moment occurred in the user's time zone.</summary>
    public DateOnly OccurredOn { get; set; }

    /// <summary>What happened during the difficult moment.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>What set the moment off or contributed to the situation.</summary>
    public string? Trigger { get; set; }

    /// <summary>How the moment was resolved or steps taken to resolve it.</summary>
    public string? Resolution { get; set; }

    /// <summary>Insights or lessons learned from reflecting on this moment.</summary>
    public string? LessonsLearned { get; set; }

    /// <summary>The resolution status of the difficult moment.</summary>
    public DifficultMomentStatus Status { get; set; } = DifficultMomentStatus.Open;

    /// <summary>The calendar date on which the moment was marked as resolved, if applicable.</summary>
    public DateOnly? ResolvedOn { get; set; }

    /// <summary>The id of an earlier moment this moment is a recurrence of, if linked.</summary>
    public Guid? RecurrenceOfId { get; set; }

    /// <summary>The earlier moment this moment is a recurrence of, when loaded.</summary>
    public DifficultMoment? RecurrenceOf { get; set; }

    /// <summary>Later moments that have been linked as recurrences of this moment.</summary>
    public ICollection<DifficultMoment> Recurrences { get; set; } = new List<DifficultMoment>();
}
