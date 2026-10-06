namespace Relio.Application.People;

/// <summary>
/// How the people list is ordered. Every ordering ends with the person's id, so two people the
/// rest of the ordering cannot tell apart always come back in the same order and a paged list
/// never repeats or skips a row.
/// </summary>
public enum PeopleSort
{
    /// <summary>First name, then last name (A to Z). A missing last name sorts before any last name: "Ada" comes before "Ada Byron".</summary>
    Name = 0,

    /// <summary>The newest profile first (<c>CreatedAtUtc</c> descending).</summary>
    RecentlyAdded = 1,

    /// <summary>
    /// The most recently contacted first (<c>LastContactedOn</c> descending). People who have never
    /// been contacted come last - explicitly, not by accident of how a database orders nulls - in
    /// <see cref="Name"/> order.
    /// </summary>
    LastContacted = 2,
}
