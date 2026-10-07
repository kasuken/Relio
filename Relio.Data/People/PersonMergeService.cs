using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="IPersonMergeService"/> (issue #28): merges two of the
/// current user's profiles of the same person. Lives in Relio.Data because it depends on
/// <see cref="RelioDbContext"/>; every query is explicitly filtered by owner, like every other owned-data
/// service (see the "User-scoped data pattern" in AGENTS.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>The context can outlive a request</b> (it lives as long as a Blazor circuit), so the read
/// (<see cref="ListCandidatesAsync"/>) is untracked, and <see cref="MergeAsync"/> loads both people
/// tracked, saves <b>once</b> and clears the change tracker in a <c>finally</c> block on every path.
/// Calls run in the context's database lane (registered with <c>AddDataService</c>), so loads started
/// by sibling components queue instead of colliding. Nothing here logs: not a name, not a contact
/// method, not even an id.
/// </para>
/// <para>
/// <b>One save is the transaction.</b> Everything - the moved and deleted contact methods, the tag
/// links, the primary's new field values and the removal of the duplicate - is one tracked
/// <c>SaveChangesAsync</c>, which is one transaction on SQL Server: the merge happens completely or not
/// at all. No <c>BeginTransaction</c> and no <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>: the InMemory
/// provider used by the unit tests supports neither.
/// </para>
/// </remarks>
public sealed class PersonMergeService(RelioDbContext dbContext, ICurrentUser currentUser, TimeProvider timeProvider) : IPersonMergeService
{
    /// <inheritdoc />
    public async Task<MergeCandidates?> ListCandidatesAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var person = await dbContext.People
            .AsNoTracking()
            .Include(p => p.ContactMethods.Where(c => c.OwnerId == ownerId))
            .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
        if (person is null)
        {
            return null;
        }

        // The same probe and matcher as the duplicate warning (#27), so the suggestions here are the
        // people that warning would have listed. The loader returns every other person too (archived
        // included), which is exactly the picker's list.
        var probe = DuplicateProbe.Create(new PossibleDuplicateQuery
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            Nickname = person.Nickname,
            ContactMethods = person.ContactMethods
                .Select(c => new ContactMethodInput(c.Id, c.Kind, c.Label, c.Value))
                .ToList(),
            ExcludePersonId = personId,
        });

        var candidates = await DuplicateCandidateLoader.LoadAsync(dbContext, ownerId, probe, cancellationToken);
        var suggestions = PossibleDuplicateMatcher.Find(probe, candidates, personId);
        var others = candidates
            .Where(c => c.Id != personId)
            .OrderBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.LastName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .Select(c => new PersonSummary(c.Id, c.FirstName, c.LastName, c.IsArchived))
            .ToList();

        return new MergeCandidates(suggestions, others);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Order matters: the request-only checks first (they say nothing about whether anyone exists),
    /// then one tracked query for both people (so a tag they share is one instance), then
    /// <see cref="PersonMergeRules.Combine"/> and validation <b>before</b> any mutation, so a refused
    /// merge changes nothing. A <see cref="DbUpdateConcurrencyException"/> means a side was deleted
    /// between the load and the save (another tab, or <c>DeleteAsync</c>); the transaction rolled back,
    /// and it is reported as <see cref="MergeOutcome.NotFound"/>. A child added to the duplicate in that
    /// tiny window is removed with it by the database cascade, the same as when a person is deleted.
    /// </remarks>
    public async Task<MergeOutcome> MergeAsync(MergePeopleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        if (request.PrimaryId == request.DuplicateId)
        {
            throw new ArgumentException("A person cannot be merged with themselves.", nameof(request));
        }

        PersonMergeRules.ValidateChoices(request.FieldChoices);

        try
        {
            var ids = new[] { request.PrimaryId, request.DuplicateId };
            var people = await dbContext.People
                .Include(p => p.Tags)
                .Include(p => p.ContactMethods.Where(c => c.OwnerId == ownerId))
                .Where(p => p.OwnerId == ownerId && ids.Contains(p.Id))
                .ToListAsync(cancellationToken);

            var primary = people.SingleOrDefault(p => p.Id == request.PrimaryId);
            var duplicate = people.SingleOrDefault(p => p.Id == request.DuplicateId);
            if (primary is null || duplicate is null)
            {
                // Missing and somebody else's look the same, and nothing was touched.
                return MergeOutcome.NotFound;
            }

            var merged = PersonMergeRules.Combine(primary, duplicate, request.FieldChoices);

            var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);
            var errors = PersonProfileRules.Validate(merged, today);
            if (errors.Count > 0)
            {
                throw new PersonValidationException(errors);
            }

            ApplyFields(primary, merged);
            await MoveDependentsAsync(ownerId, primary, duplicate, merged, cancellationToken);
            await PersonLastContactUpdater.RecalculateAsync(
                dbContext,
                ownerId,
                [primary.Id, duplicate.Id],
                combinedPersonIds: new Dictionary<Guid, IReadOnlyCollection<Guid>>
                {
                    [primary.Id] = [primary.Id, duplicate.Id],
                },
                cancellationToken: cancellationToken);

            // Before Remove, on purpose. EF Core cascades a removed principal's delete to the dependents
            // it is tracking *immediately* (CascadeDeleteTiming.Immediate), and it only knows a moved
            // row left the duplicate once change detection has seen the new foreign key and navigation;
            // a row it still thinks belongs to the duplicate would be deleted with it. Current EF Core
            // also runs change detection inside Remove (the tests pass without this line), so this is a
            // pinned safety net rather than the fix: it keeps the order from depending on that detail
            // (or on AutoDetectChangesEnabled). PersonMergeServiceTests (InMemory) and
            // PersonMergeSqlServerTests prove the moved rows survive the removal.
            dbContext.ChangeTracker.DetectChanges();
            dbContext.People.Remove(duplicate);

            await dbContext.SaveChangesAsync(cancellationToken);
            return MergeOutcome.Merged;
        }
        catch (DbUpdateConcurrencyException)
        {
            return MergeOutcome.NotFound;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Writes the resolved fields onto the primary. The values were stored by this app's own services
    /// (already trimmed and validated for length), so nothing is normalized again here.
    /// <see cref="MergedProfile.RelationshipTypeId"/> needs no ownership check: both people are the
    /// owner's, so any type id either of them holds is already one of the owner's.
    /// </summary>
    private static void ApplyFields(Person primary, MergedProfile merged)
    {
        primary.FirstName = merged.FirstName;
        primary.LastName = merged.LastName;
        primary.Nickname = merged.Nickname;
        primary.RelationshipTypeId = merged.RelationshipTypeId;
        primary.BirthdayDay = merged.BirthdayDay;
        primary.BirthdayMonth = merged.BirthdayMonth;
        primary.BirthdayYear = merged.BirthdayYear;
        primary.HowWeMet = merged.HowWeMet;
        primary.Details = merged.Details;
        primary.IsArchived = merged.IsArchived;
        primary.ArchivedAtUtc = merged.ArchivedAtUtc;
        primary.LastContactedOn = merged.LastContactedOn;
        primary.StayInTouchCadenceDays = merged.StayInTouchCadenceDays;
        primary.BirthdayReminderDisabled = merged.BirthdayReminderDisabled;
        primary.BirthdayReminderLeadDays = merged.BirthdayReminderLeadDays;
    }

    /// <summary>
    /// Moves everything that belongs to <paramref name="duplicate"/> to <paramref name="primary"/>, so
    /// removing the duplicate afterwards takes nothing with it. Called only by <see cref="MergeAsync"/>,
    /// before the duplicate is removed and in the same save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>CHECKLIST - every new entity with a <c>PersonId</c> adds one line here, in the same pull
    /// request, next to its line in <c>PeopleService.RemoveDependentsAsync</c>, and to
    /// <c>PersonMergeChecklistTests</c>.</b> Load rows with <c>OwnerId == ownerId &amp;&amp; PersonId ==
    /// duplicate.Id</c>, tracked; reassign them; never <c>ExecuteUpdate</c>. A new entity must also extend
    /// the end-to-end merge test so the merged profile shows its entries.
    /// <list type="bullet">
    /// <item><description>[x] <c>ContactMethods</c> (#24): moved, deduplicated, or dropped, as <see cref="PersonMergeRules.Combine"/> decided (already loaded with both people).</description></item>
    /// <item><description>[x] Tag links (the <c>PersonTags</c> join): the duplicate's tags the primary lacks are attached to the primary, then the duplicate's links are cleared. The <c>Tag</c> rows are never removed.</description></item>
    /// <item><description>[x] Interactions (#31) and their participants (#35): when the interaction already has the primary as a participant, remove the duplicate's participant row; otherwise set its <c>PersonId</c> to the primary.</description></item>
    /// <item><description>[x] Notes (#32): move the duplicate's notes to the primary, preserving text, pin state and creation dates; the normal save stamping advances <c>UpdatedAtUtc</c>.</description></item>
    /// <item><description>[x] Reminders (#37, #38): <c>PersonId = primary.Id</c>. Birthday reminders are derived from the birthday columns and need nothing.</description></item>
    /// <item><description>[ ] Difficult moments (#43): <c>PersonId = primary.Id</c>.</description></item>
    /// <item><description>[ ] Any person-to-person link (two foreign keys to <c>People</c>): drop a link between primary and duplicate, and dedupe the links both had.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private async Task MoveDependentsAsync(
        string ownerId,
        Person primary,
        Person duplicate,
        MergedProfile merged,
        CancellationToken cancellationToken)
    {
        foreach (var step in merged.ContactMethodSteps)
        {
            var row = step.Row;
            switch (step.Action)
            {
                case ContactMethodMergeAction.KeepPrimary:
                    if (step.LabelToFill is not null)
                    {
                        row.Label = step.LabelToFill;
                    }

                    break;
                case ContactMethodMergeAction.MoveFromDuplicate:
                    // Fix up both ends (the key and the collections) so change detection sees the move.
                    duplicate.ContactMethods.Remove(row);
                    row.PersonId = primary.Id;
                    row.SortOrder = step.SortOrder;
                    primary.ContactMethods.Add(row);
                    break;
                case ContactMethodMergeAction.DropDuplicate:
                    dbContext.ContactMethods.Remove(row);
                    break;
            }
        }

        // The tags were loaded with the people in one query, so a tag both have is one instance. Only
        // join rows change; the Tag rows and every other person's links to them are untouched.
        foreach (var tag in merged.Tags)
        {
            if (primary.Tags.All(existing => existing.Id != tag.Id))
            {
                primary.Tags.Add(tag);
            }
        }

        duplicate.Tags.Clear();

        var duplicateParticipants = await dbContext.InteractionParticipants
            .Where(participant => participant.OwnerId == ownerId && participant.PersonId == duplicate.Id)
            .ToListAsync(cancellationToken);
        if (duplicateParticipants.Count > 0)
        {
            var interactionIds = duplicateParticipants
                .Select(participant => participant.InteractionId)
                .Distinct()
                .ToArray();
            var primaryInteractionIds = await dbContext.InteractionParticipants
                .AsNoTracking()
                .Where(participant => participant.OwnerId == ownerId
                    && participant.PersonId == primary.Id
                    && interactionIds.Contains(participant.InteractionId))
                .Select(participant => participant.InteractionId)
                .ToListAsync(cancellationToken);
            var alreadyParticipating = primaryInteractionIds.ToHashSet();

            foreach (var participant in duplicateParticipants)
            {
                if (alreadyParticipating.Contains(participant.InteractionId))
                {
                    dbContext.InteractionParticipants.Remove(participant);
                }
                else
                {
                    participant.PersonId = primary.Id;
                    participant.Person = primary;
                }
            }
        }

        var duplicateNotes = await dbContext.Notes
            .Where(note => note.OwnerId == ownerId && note.PersonId == duplicate.Id)
            .ToListAsync(cancellationToken);
        foreach (var note in duplicateNotes)
        {
            note.PersonId = primary.Id;
            note.Person = primary;
        }

        var reminders = await dbContext.Reminders
            .Where(r => r.OwnerId == ownerId && r.PersonId == duplicate.Id)
            .ToListAsync(cancellationToken);
        foreach (var reminder in reminders)
        {
            reminder.PersonId = primary.Id;
        }
    }
}
