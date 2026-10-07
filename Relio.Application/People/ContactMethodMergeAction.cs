namespace Relio.Application.People;

/// <summary>What merging does with one contact method row.</summary>
public enum ContactMethodMergeAction
{
    /// <summary>A row of the primary: it stays where it is.</summary>
    KeepPrimary,

    /// <summary>A row of the duplicate that the primary does not have yet: it moves to the primary.</summary>
    MoveFromDuplicate,

    /// <summary>A row of the duplicate that repeats one the primary already has (or an earlier duplicate row): it is deleted.</summary>
    DropDuplicate,
}
