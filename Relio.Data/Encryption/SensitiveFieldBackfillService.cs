using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Identity;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.Encryption;

/// <summary>
/// Converts legacy plaintext columns to authenticated Data Protection values in restartable
/// tracked batches. Invoke it after the schema migration and before normal application reads.
/// </summary>
public sealed class SensitiveFieldBackfillService
{
    private const int BatchSize = 100;

    private readonly DbContextOptions<RelioDbContext> _options;
    private readonly TimeProvider _timeProvider;
    private readonly IDataProtectionFieldProtector _protector;

    /// <summary>Creates the one-time database maintenance service.</summary>
    public SensitiveFieldBackfillService(
        DbContextOptions<RelioDbContext> options,
        TimeProvider timeProvider,
        IDataProtectionFieldProtector protector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(protector);
        _options = options;
        _timeProvider = timeProvider;
        _protector = protector;
    }

    /// <summary>
    /// Protects every legacy row, validates all protected values against the configured key ring,
    /// and returns counts of converted rows. Running it again is safe and verifies the completed
    /// state without encrypting rows a second time.
    /// </summary>
    public async Task<SensitiveFieldBackfillResult> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await VerifyProtectedRowsAsync(cancellationToken);
            var result = new SensitiveFieldBackfillResult(
                await BackfillAsync(
                    db => db.People
                        .Where(person => EF.Property<int>(person, FieldProtectionSchema.VersionPropertyName)
                            == FieldProtectionSchema.LegacyVersion)
                        .OrderBy(person => person.Id),
                    ProtectPerson,
                    cancellationToken),
                await BackfillAsync(
                    db => db.Notes
                        .Where(note => EF.Property<int>(note, FieldProtectionSchema.VersionPropertyName)
                            == FieldProtectionSchema.LegacyVersion)
                        .OrderBy(note => note.Id),
                    ProtectNote,
                    cancellationToken),
                await BackfillAsync(
                    db => db.Interactions
                        .Where(interaction => EF.Property<int>(interaction, FieldProtectionSchema.VersionPropertyName)
                            == FieldProtectionSchema.LegacyVersion)
                        .OrderBy(interaction => interaction.Id),
                    ProtectInteraction,
                    cancellationToken),
                await BackfillAsync(
                    db => db.Reminders
                        .Where(reminder => EF.Property<int>(reminder, FieldProtectionSchema.VersionPropertyName)
                            == FieldProtectionSchema.LegacyVersion)
                        .OrderBy(reminder => reminder.Id),
                    ProtectReminder,
                    cancellationToken),
                await BackfillAsync(
                    db => db.UserProfiles
                        .Where(profile => EF.Property<int>(profile, FieldProtectionSchema.VersionPropertyName)
                            == FieldProtectionSchema.LegacyVersion)
                        .OrderBy(profile => profile.Id),
                    ProtectUserProfile,
                    cancellationToken),
                await BackfillAsync(
                    db => db.UserTokens
                        .Where(token => EF.Property<int>(token, FieldProtectionSchema.VersionPropertyName)
                            == FieldProtectionSchema.LegacyVersion)
                        .OrderBy(token => token.UserId)
                        .ThenBy(token => token.LoginProvider)
                        .ThenBy(token => token.Name),
                    ProtectIdentityToken,
                    cancellationToken));

            await VerifyAllRowsAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SensitiveFieldBackfillException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SensitiveFieldBackfillException(
                "Sensitive-field backfill failed. No protected content or exception details were recorded.");
        }
    }

    private async Task<int> BackfillAsync<TEntity>(
        Func<RelioDbContext, IQueryable<TEntity>> legacyRows,
        Action<RelioDbContext, TEntity> protect,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var converted = 0;

        while (true)
        {
            await using var dbContext = CreateBackfillContext();
            try
            {
                var batch = await legacyRows(dbContext)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                if (batch.Count == 0)
                {
                    return converted;
                }

                foreach (var row in batch)
                {
                    var version = dbContext.Entry(row)
                        .Property<int>(FieldProtectionSchema.VersionPropertyName)
                        .CurrentValue;
                    if (version != FieldProtectionSchema.LegacyVersion)
                    {
                        throw new SensitiveFieldBackfillException(
                            "A row changed protection state during the backfill.");
                    }

                    protect(dbContext, row);
                }

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    continue;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    throw new SensitiveFieldBackfillException(
                        "A protected-data batch could not be saved; the batch was rolled back.");
                }

                converted = checked(converted + batch.Count);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private void ProtectPerson(RelioDbContext dbContext, Person person)
    {
        person.HowWeMet = Protect(person.HowWeMet, ProtectedFieldPurposes.PersonHowWeMet);
        person.Details = Protect(person.Details, ProtectedFieldPurposes.PersonDetails);
        MarkProtected(dbContext, person);
    }

    private void ProtectNote(RelioDbContext dbContext, Note note)
    {
        note.Text = Protect(note.Text, ProtectedFieldPurposes.NoteText)!;
        MarkProtected(dbContext, note);
    }

    private void ProtectInteraction(RelioDbContext dbContext, Interaction interaction)
    {
        interaction.Description = Protect(interaction.Description, ProtectedFieldPurposes.InteractionDescription)!;
        MarkProtected(dbContext, interaction);
    }

    private void ProtectReminder(RelioDbContext dbContext, Reminder reminder)
    {
        reminder.Title = Protect(reminder.Title, ProtectedFieldPurposes.ReminderTitle)!;
        MarkProtected(dbContext, reminder);
    }

    private void ProtectUserProfile(RelioDbContext dbContext, UserProfile profile)
    {
        var verifier = UnsubscribeTokenHash.Compute(profile.UnsubscribeToken);
        if (profile.UnsubscribeTokenVerifier is not null
            && !FixedTimeEquals(profile.UnsubscribeTokenVerifier, verifier))
        {
            throw new SensitiveFieldBackfillException(
                "An unsubscribe-token verifier did not match its stored token.");
        }

        profile.UnsubscribeTokenVerifier = verifier;
        profile.UnsubscribeToken = Protect(profile.UnsubscribeToken, ProtectedFieldPurposes.UnsubscribeToken);
        MarkProtected(dbContext, profile);
    }

    private void ProtectIdentityToken(RelioDbContext dbContext, IdentityUserToken<string> token)
    {
        token.Value = Protect(token.Value, ProtectedFieldPurposes.IdentityUserTokenValue);
        MarkProtected(dbContext, token);
    }

    private string? Protect(string? value, string purpose)
    {
        try
        {
            return _protector.Protect(value, purpose);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SensitiveFieldBackfillException(
                "A value could not be protected with the configured key ring.");
        }
    }

    private static void MarkProtected<TEntity>(RelioDbContext dbContext, TEntity entity)
        where TEntity : class
    {
        var version = dbContext.Entry(entity).Property<int>(FieldProtectionSchema.VersionPropertyName);
        version.CurrentValue = FieldProtectionSchema.CurrentVersion;
        version.IsModified = true;
    }

    private async Task VerifyAllRowsAsync(CancellationToken cancellationToken)
    {
        await VerifyRowsAsync(
            db => db.People,
            rows => rows.OrderBy(person => person.Id),
            person =>
            {
                Unprotect(person.HowWeMet, ProtectedFieldPurposes.PersonHowWeMet);
                Unprotect(person.Details, ProtectedFieldPurposes.PersonDetails);
            },
            allowLegacyRows: false,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.Notes,
            rows => rows.OrderBy(note => note.Id),
            note => Unprotect(note.Text, ProtectedFieldPurposes.NoteText),
            allowLegacyRows: false,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.Interactions,
            rows => rows.OrderBy(interaction => interaction.Id),
            interaction => Unprotect(interaction.Description, ProtectedFieldPurposes.InteractionDescription),
            allowLegacyRows: false,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.Reminders,
            rows => rows.OrderBy(reminder => reminder.Id),
            reminder => Unprotect(reminder.Title, ProtectedFieldPurposes.ReminderTitle),
            allowLegacyRows: false,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.UserProfiles,
            rows => rows.OrderBy(profile => profile.Id),
            profile => VerifyUserProfile(profile),
            allowLegacyRows: false,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.UserTokens,
            rows => rows
                .OrderBy(token => token.UserId)
                .ThenBy(token => token.LoginProvider)
                .ThenBy(token => token.Name),
            token => Unprotect(token.Value, ProtectedFieldPurposes.IdentityUserTokenValue),
            allowLegacyRows: false,
            cancellationToken);
    }

    private async Task VerifyProtectedRowsAsync(CancellationToken cancellationToken)
    {
        await VerifyRowsAsync(
            db => db.People,
            rows => rows.OrderBy(person => person.Id),
            person =>
            {
                Unprotect(person.HowWeMet, ProtectedFieldPurposes.PersonHowWeMet);
                Unprotect(person.Details, ProtectedFieldPurposes.PersonDetails);
            },
            allowLegacyRows: true,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.Notes,
            rows => rows.OrderBy(note => note.Id),
            note => Unprotect(note.Text, ProtectedFieldPurposes.NoteText),
            allowLegacyRows: true,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.Interactions,
            rows => rows.OrderBy(interaction => interaction.Id),
            interaction => Unprotect(interaction.Description, ProtectedFieldPurposes.InteractionDescription),
            allowLegacyRows: true,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.Reminders,
            rows => rows.OrderBy(reminder => reminder.Id),
            reminder => Unprotect(reminder.Title, ProtectedFieldPurposes.ReminderTitle),
            allowLegacyRows: true,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.UserProfiles,
            rows => rows.OrderBy(profile => profile.Id),
            profile => VerifyUserProfile(profile),
            allowLegacyRows: true,
            cancellationToken);
        await VerifyRowsAsync(
            db => db.UserTokens,
            rows => rows
                .OrderBy(token => token.UserId)
                .ThenBy(token => token.LoginProvider)
                .ThenBy(token => token.Name),
            token => Unprotect(token.Value, ProtectedFieldPurposes.IdentityUserTokenValue),
            allowLegacyRows: true,
            cancellationToken);
    }

    private async Task VerifyRowsAsync<TEntity>(
        Func<RelioDbContext, IQueryable<TEntity>> allRows,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy,
        Action<TEntity> verify,
        bool allowLegacyRows,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        await using (var validationContext = CreateBackfillContext())
        {
            try
            {
                var rows = allRows(validationContext);
                var unsupportedCount = await rows
                    .Where(entity => allowLegacyRows
                        ? EF.Property<int>(entity, FieldProtectionSchema.VersionPropertyName)
                            < FieldProtectionSchema.LegacyVersion
                            || EF.Property<int>(entity, FieldProtectionSchema.VersionPropertyName)
                                > FieldProtectionSchema.CurrentVersion
                        : EF.Property<int>(entity, FieldProtectionSchema.VersionPropertyName)
                            != FieldProtectionSchema.CurrentVersion)
                    .CountAsync(cancellationToken);

                if (unsupportedCount != 0)
                {
                    throw new SensitiveFieldBackfillException(
                        "One or more rows are not marked with the current protected-data version.");
                }
            }
            finally
            {
                validationContext.ChangeTracker.Clear();
            }
        }

        var offset = 0;
        while (true)
        {
            await using var dbContext = CreateBackfillContext();
            try
            {
                var rows = orderBy(allRows(dbContext));
                var protectedRows = allowLegacyRows
                    ? rows.Where(entity => EF.Property<int>(entity, FieldProtectionSchema.VersionPropertyName)
                        == FieldProtectionSchema.CurrentVersion)
                    : rows;
                var batch = await protectedRows
                    .AsNoTracking()
                    .Skip(offset)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);
                if (batch.Count == 0)
                {
                    return;
                }

                foreach (var row in batch)
                {
                    verify(row);
                }

                offset = checked(offset + batch.Count);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private void VerifyUserProfile(UserProfile profile)
    {
        var token = Unprotect(profile.UnsubscribeToken, ProtectedFieldPurposes.UnsubscribeToken);
        var expectedVerifier = UnsubscribeTokenHash.Compute(token);
        if (!FixedTimeEquals(profile.UnsubscribeTokenVerifier, expectedVerifier))
        {
            throw new SensitiveFieldBackfillException(
                "An unsubscribe-token verifier did not match its protected token.");
        }
    }

    private string? Unprotect(string? protectedValue, string purpose)
    {
        try
        {
            return _protector.Unprotect(protectedValue, purpose);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SensitiveFieldBackfillException(
                "A protected value could not be authenticated with the configured key ring.");
        }
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private RelioDbContext CreateBackfillContext() =>
        new(_options, _timeProvider, _protector, FieldProtectionMode.LegacyBackfill);
}

/// <summary>
/// Counts rows transitioned by one backfill run. The record intentionally contains no owner,
/// field content, key identifier, or token value.
/// </summary>
/// <param name="People">Person rows converted.</param>
/// <param name="Notes">Note rows converted.</param>
/// <param name="Interactions">Interaction rows converted.</param>
/// <param name="Reminders">Reminder rows converted.</param>
/// <param name="UserProfiles">User-profile rows converted.</param>
/// <param name="IdentityTokens">Identity token rows converted.</param>
public sealed record SensitiveFieldBackfillResult(
    int People,
    int Notes,
    int Interactions,
    int Reminders,
    int UserProfiles,
    int IdentityTokens);

/// <summary>A sanitized failure that can be safely surfaced as an application-startup blocker.</summary>
public sealed class SensitiveFieldBackfillException(string message) : Exception(message);
