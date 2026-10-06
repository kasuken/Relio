using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>A tag the person form has chosen: an existing tag (<see cref="Id"/> set) or a name to create (<see cref="Id"/> null).</summary>
public sealed record TagSelection(Guid? Id, string Name);

/// <summary>One line in the tag picker's list: an existing tag, or "Create tag" for a new name (<see cref="IsNew"/>).</summary>
public sealed record TagOption(Guid? Id, string Name, bool IsNew);

/// <summary>
/// What the tag picker offers for the text typed so far. Pure. Names are compared with
/// <see cref="TagNameRules.Comparer"/>, the rule the service uses, so the picker never offers to
/// "create" a tag the service would match to an existing one.
/// </summary>
public static class TagSuggestions
{
    /// <summary>The most existing tags offered at once; typing narrows them.</summary>
    public const int DefaultMax = 8;

    /// <summary>
    /// The options for <paramref name="text"/>: the user's tags that are not chosen yet (blank text
    /// lists the first <paramref name="max"/> by name; otherwise names that contain the text, those
    /// that start with it first), then a "create" option when the text is a usable new name.
    /// </summary>
    public static IReadOnlyList<TagOption> For(
        string? text,
        IReadOnlyList<Tag> available,
        IReadOnlyList<TagSelection> selected,
        int max = DefaultMax)
    {
        var typed = TagNameRules.Normalize(text);

        bool IsSelected(Tag tag) => selected.Any(choice =>
            choice.Id == tag.Id || TagNameRules.Comparer.Equals(choice.Name, tag.Name));

        var unchosen = available.Where(tag => !IsSelected(tag)).ToList();

        IEnumerable<Tag> matches = typed is null
            ? unchosen.OrderBy(tag => tag.Name, TagNameRules.Comparer)
            : unchosen
                .Where(tag => tag.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(tag => tag.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(tag => tag.Name, TagNameRules.Comparer);

        var options = matches
            .Take(max)
            .Select(tag => new TagOption(tag.Id, tag.Name, IsNew: false))
            .ToList();

        var canCreate = typed is not null
            && typed.Length <= Tag.NameMaxLength
            && !available.Any(tag => TagNameRules.Comparer.Equals(tag.Name, typed))
            && !selected.Any(choice => TagNameRules.Comparer.Equals(choice.Name, typed));
        if (canCreate)
        {
            options.Add(new TagOption(null, typed!, IsNew: true));
        }

        return options;
    }
}
