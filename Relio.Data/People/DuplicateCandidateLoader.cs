using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// Loads the <see cref="DuplicateCandidate"/> projections one owner's people are matched against.
/// Shared by <c>PeopleService.FindPossibleDuplicatesAsync</c> (#27) and, later, merge (#28).
/// </summary>
/// <remarks>
/// Every query is untracked, owner-scoped and awaited one after another (never <c>Task.WhenAll</c>:
/// a context allows one operation at a time, see the database lane in AGENTS.md). Archived people are
/// included. Nothing is cached or logged: names and contact keys exist in memory for the length of
/// one check. At personal scale (thousands of people) this is one small projection plus a few short
/// queries.
/// </remarks>
internal static class DuplicateCandidateLoader
{
    public static async Task<List<DuplicateCandidate>> LoadAsync(
        RelioDbContext db,
        string ownerId,
        DuplicateProbe probe,
        CancellationToken cancellationToken)
    {
        // (1) Every person the owner has, archived included, one round trip with no Include. Names
        // come from the (OwnerId, IsArchived, FirstName, LastName) index; Nickname costs a key lookup.
        var people = await db.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.Nickname, p.IsArchived })
            .ToListAsync(cancellationToken);

        var emailKeys = new Dictionary<Guid, List<string>>();
        var phoneKeys = new Dictionary<Guid, List<string>>();

        // (2) Emails: an exact IN on the normalized value, a seek on IX_ContactMethods_OwnerId_NormalizedValue.
        if (probe.EmailKeys.Count > 0)
        {
            var keys = probe.EmailKeys.ToList();
            var rows = await db.ContactMethods
                .AsNoTracking()
                .Where(c => c.OwnerId == ownerId && c.Kind == ContactMethodKind.Email && keys.Contains(c.NormalizedValue))
                .Select(c => new { c.PersonId, c.NormalizedValue })
                .ToListAsync(cancellationToken);
            Group(rows.Select(r => (r.PersonId, r.NormalizedValue)), emailKeys);
        }

        // (3) Phones: one query per distinct suffix (the last eight digits, or the whole short number).
        // EndsWith is not sargable, so this is an owner-range scan of the same index with a residual
        // predicate on the indexed column; the matcher re-checks the exact rule in memory. The suffix
        // is digits (and at most a leading +) only, so it holds no LIKE wildcard, and it is a parameter.
        foreach (var suffix in probe.PhoneSuffixes)
        {
            var rows = await db.ContactMethods
                .AsNoTracking()
                .Where(c => c.OwnerId == ownerId && c.Kind == ContactMethodKind.Phone && c.NormalizedValue.EndsWith(suffix))
                .Select(c => new { c.PersonId, c.NormalizedValue })
                .ToListAsync(cancellationToken);
            Group(rows.Select(r => (r.PersonId, r.NormalizedValue)), phoneKeys);
        }

        return people
            .Select(p => new DuplicateCandidate(
                p.Id,
                p.FirstName,
                p.LastName,
                p.Nickname,
                p.IsArchived,
                emailKeys.GetValueOrDefault(p.Id) ?? [],
                phoneKeys.GetValueOrDefault(p.Id) ?? []))
            .ToList();
    }

    /// <summary>
    /// Every person the owner has (archived included) with all of their email and phone keys, for
    /// matching many probes against the same set: one projection of the people and one query of the
    /// email and phone contact methods, grouped in memory. Used by the import preview (#29), which
    /// compares up to 2,000 rows with the user's whole list and so cannot ask per row.
    /// </summary>
    /// <remarks>
    /// Untracked, owner-scoped, and awaited one after the other (never <c>Task.WhenAll</c>). This is
    /// one person's own network: at 5,000 people and 10,000 contact rows it is a few megabytes at
    /// most, held for the length of one preview and then dropped.
    /// </remarks>
    public static async Task<List<DuplicateCandidate>> LoadAllAsync(
        RelioDbContext db,
        string ownerId,
        CancellationToken cancellationToken)
    {
        var people = await db.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.Nickname, p.IsArchived })
            .ToListAsync(cancellationToken);

        var rows = await db.ContactMethods
            .AsNoTracking()
            .Where(c => c.OwnerId == ownerId && (c.Kind == ContactMethodKind.Email || c.Kind == ContactMethodKind.Phone))
            .Select(c => new { c.PersonId, c.Kind, c.NormalizedValue })
            .ToListAsync(cancellationToken);

        var emailKeys = new Dictionary<Guid, List<string>>();
        var phoneKeys = new Dictionary<Guid, List<string>>();
        Group(rows.Where(r => r.Kind == ContactMethodKind.Email).Select(r => (r.PersonId, r.NormalizedValue)), emailKeys);
        Group(rows.Where(r => r.Kind == ContactMethodKind.Phone).Select(r => (r.PersonId, r.NormalizedValue)), phoneKeys);

        return people
            .Select(p => new DuplicateCandidate(
                p.Id,
                p.FirstName,
                p.LastName,
                p.Nickname,
                p.IsArchived,
                emailKeys.GetValueOrDefault(p.Id) ?? [],
                phoneKeys.GetValueOrDefault(p.Id) ?? []))
            .ToList();
    }

    private static void Group(IEnumerable<(Guid PersonId, string NormalizedValue)> rows, Dictionary<Guid, List<string>> into)
    {
        foreach (var (personId, value) in rows)
        {
            if (!into.TryGetValue(personId, out var list))
            {
                list = [];
                into[personId] = list;
            }

            if (!list.Contains(value, StringComparer.Ordinal))
            {
                list.Add(value);
            }
        }
    }
}
