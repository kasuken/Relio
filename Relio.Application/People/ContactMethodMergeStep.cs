using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// One contact method row and what merging does with it.
/// </summary>
/// <param name="Row">The row, as loaded. Personal data; never log it.</param>
/// <param name="Action">What happens to it.</param>
/// <param name="LabelToFill">
/// For a <see cref="ContactMethodMergeAction.KeepPrimary"/> row without a label whose duplicate row
/// had one: the label to copy over. Otherwise <see langword="null"/>.
/// </param>
/// <param name="SortOrder">The position the row ends up at. A dropped row keeps its own.</param>
public sealed record ContactMethodMergeStep(
    ContactMethod Row,
    ContactMethodMergeAction Action,
    string? LabelToFill,
    int SortOrder);
