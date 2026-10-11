using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Security;
using Relio.Data.Billing;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="IPeopleImportService"/> (issue #29): previews what a
/// parsed contacts file would create and creates the people the user confirmed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file never reaches this layer.</b> The page reads and parses it (pure code in
/// <c>Relio.Application.People.Import</c>) and passes only what was parsed, so nothing about the file
/// - its name, its bytes, its cells - is stored or logged here. Nothing in this class logs anything.
/// </para>
/// <para>
/// <b>The circuit and the lane.</b> Like every owned-data service this one runs on a context that can
/// live as long as a Blazor circuit: reads are untracked, the one mutation loads nothing, saves once
/// and clears the change tracker in a <c>finally</c> block (a failed save would otherwise leave
/// <i>Added</i> people that the next save inserts). It is registered with <c>AddDataService</c>, so its
/// calls take turns with every other service's on the shared context.
/// </para>
/// <para>
/// <b>All or nothing.</b> <see cref="ImportAsync"/> adds every person to the context and calls
/// <c>SaveChangesAsync</c> <b>once</b>: one transaction on SQL Server, so a failure leaves nothing
/// behind. No explicit transaction and no <c>ExecuteUpdate</c> (the InMemory provider used by the
/// unit tests supports neither). The people are new rows, so there is no ownership check of foreign
/// ids to make: an import sets no relationship type and no tags.
/// </para>
/// </remarks>
public sealed class PeopleImportService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    PlanLimits? planLimits = null) : IPeopleImportService
{
    /// <inheritdoc />
    public async Task<ImportPreview> PreviewAsync(ImportReadResult read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        var ownerId = currentUser.RequireUserId();

        var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);

        // Everything here is untracked and read-only, so there is nothing to clear afterwards. The
        // user's people are matched in memory and never persisted, cached or logged.
        var existing = PossibleDuplicateMatcher.Prepare(await DuplicateCandidateLoader.LoadAllAsync(dbContext, ownerId, cancellationToken));
        return ImportCandidateBuilder.Build(read, today, existing);
    }

    /// <inheritdoc />
    public async Task<int> ImportAsync(IReadOnlyList<ImportPersonRequest> people, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(people);
        var ownerId = currentUser.RequireUserId();

        if (people.Count == 0)
        {
            throw new ArgumentException("There is nobody to import.", nameof(people));
        }

        if (people.Count > ImportLimits.MaxPeople)
        {
            throw new ArgumentOutOfRangeException(nameof(people), "An import creates at most " + ImportLimits.MaxPeople + " people.");
        }

        if (people.Any(person => person is null))
        {
            throw new ArgumentException("A request in the list is missing.", nameof(people));
        }

        // A contact method id means "edit this existing row". Nothing here exists yet, so one is malformed
        // (no page produces it) rather than something a user can fix.
        if (people.Any(person => person.ContactMethods?.Any(method => method.Id is not null) == true))
        {
            throw new ArgumentException("A new person has no contact methods to edit.", nameof(people));
        }

        // The service is the authority: every request is checked again, with today in the user's own
        // time zone, before anything is added to the context.
        var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);
        var rowErrors = new List<ImportRowError>();
        for (var index = 0; index < people.Count; index++)
        {
            var errors = PersonProfileRules.Validate(people[index], today);
            var contactMethodProblems = PersonProfileRules.ValidateContactMethods(people[index]);
            if (errors.Count > 0 || contactMethodProblems.Count > 0)
            {
                rowErrors.Add(new ImportRowError(index, errors, contactMethodProblems));
            }
        }

        if (rowErrors.Count > 0)
        {
            throw new PeopleImportValidationException(rowErrors);
        }

        // Every imported person is active: all of them must fit the plan's limit, or none is imported.
        await (planLimits ?? PlanLimits.Unlimited).EnsureRoomForActivePeopleAsync(
            dbContext, ownerId, people.Count, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        try
        {
            var created = people.Select(request => PersonEntityBuilder.NewPerson(ownerId, request)).ToList();

            // AddRange traverses each graph, so the people and their contact methods are all inserted.
            dbContext.People.AddRange(created);
            await dbContext.SaveChangesAsync(cancellationToken);
            return created.Count;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
