using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Data.Accounts;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data;

/// <summary>
/// Entity Framework Core database context for Relio. Entity configurations are discovered
/// automatically from this assembly via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>.
/// </summary>
/// <remarks>
/// Audit timestamps (<see cref="IOwnedEntity.CreatedAtUtc"/>, <see cref="IOwnedEntity.UpdatedAtUtc"/>)
/// are stamped here, from the injected <see cref="TimeProvider"/>, on every <c>SaveChanges</c> call - callers
/// and application services never set them directly. <see cref="IOwnedEntity.OwnerId"/> is not
/// touched here: it is set once by the service that creates the entity. This context does not
/// apply a global query filter on owner id; see the "User-scoped data pattern" section of
/// AGENTS.md for why ownership is instead enforced explicitly in each service method.
/// </remarks>
/// <remarks>
/// A <see cref="DbContext"/> allows one operation in flight, and the scoped instance is shared by
/// every component of a Blazor circuit, so <see cref="Lane"/> serializes data service calls on it.
/// </remarks>
/// <remarks>
/// Inherits <see cref="IdentityDbContext{TUser}"/> (epic #14) instead of plain <see cref="DbContext"/>
/// so ASP.NET Core Identity's own tables (<c>AspNetUsers</c>, <c>AspNetUserClaims</c>, etc.) live in
/// the same database and migration history as the rest of Relio. Identity's own entities are not
/// <see cref="IOwnedEntity"/> - they are not user-owned data, they *are* the user - so they are
/// untouched by <see cref="ApplyAuditTimestamps"/> below.
/// </remarks>
public sealed partial class RelioDbContext : IdentityDbContext<RelioUser>
{
    private readonly TimeProvider _timeProvider;
    private readonly IDataProtectionFieldProtector _fieldProtector;
    private readonly FieldProtectionMode _fieldProtectionMode;

    /// <summary>
    /// Creates a Relio database context.
    /// </summary>
    /// <param name="options">The EF Core provider and connection options.</param>
    /// <param name="timeProvider">The clock used for audit timestamps.</param>
    /// <param name="fieldProtector">The shared, durable-key-ring field protector.</param>
    /// <param name="fieldProtectionMode">The storage mode, normally encrypted.</param>
    public RelioDbContext(
        DbContextOptions<RelioDbContext> options,
        TimeProvider timeProvider,
        IDataProtectionFieldProtector fieldProtector,
        FieldProtectionMode fieldProtectionMode = FieldProtectionMode.Encrypted)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(fieldProtector);
        if (!Enum.IsDefined(fieldProtectionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldProtectionMode));
        }

        _timeProvider = timeProvider;
        _fieldProtector = fieldProtector;
        _fieldProtectionMode = fieldProtectionMode;
    }

    internal Guid FieldProtectionModelIdentity => _fieldProtector.ModelCacheIdentity;

    internal FieldProtectionMode FieldProtectionMode => _fieldProtectionMode;

    /// <summary>
    /// Lets one operation at a time run on this context. A Blazor circuit (and a prerender) shares one
    /// scoped context across components, and the renderer starts a sibling's load while the previous
    /// one is still awaiting the database - see "One database operation at a time" in AGENTS.md.
    /// Every data service runs through it (<c>AddRelioData</c> wraps them). It is idle whenever no
    /// data service call is running, so it stays correct if context pooling is adopted later. Not
    /// mapped: EF Core only maps <see cref="DbSet{TEntity}"/> properties, so there is no model change.
    /// </summary>
    public Concurrency.DatabaseLane Lane { get; } = new();

    /// <summary>The current user's people (filtered explicitly by services, not by a global query filter).</summary>
    public DbSet<Person> People => Set<Person>();

    /// <summary>The current user's tags.</summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>The current user's people's contact methods (email, phone, ...), filtered explicitly by services like everything else.</summary>
    public DbSet<ContactMethod> ContactMethods => Set<ContactMethod>();

    /// <summary>The current user's relationship types ("Friend", "Colleague", ...), seeded per user at registration.</summary>
    public DbSet<RelationshipType> RelationshipTypes => Set<RelationshipType>();

    /// <summary>The current user's dated interactions, shared through their participant links.</summary>
    public DbSet<Interaction> Interactions => Set<Interaction>();

    /// <summary>The current user's links between interactions and people.</summary>
    public DbSet<InteractionParticipant> InteractionParticipants => Set<InteractionParticipant>();

    /// <summary>The current user's notes about people.</summary>
    public DbSet<Note> Notes => Set<Note>();

    /// <summary>The current user's reconnect reminders (epic #36).</summary>
    public DbSet<Reminder> Reminders => Set<Reminder>();

    /// <summary>The current user's difficult moments in relationships (epic #42).</summary>
    public DbSet<DifficultMoment> DifficultMoments => Set<DifficultMoment>();

    /// <summary>User profiles (currently just the user's time zone, see epic #12), one per user.</summary>
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    /// <summary>Optional, retention-bounded product activity contributions.</summary>
    public DbSet<ProductActivity> ProductActivities => Set<ProductActivity>();

    /// <summary>
    /// Pending sign-up invitations for an invitation-only instance (issue #19). Instance
    /// administration data, not user-owned: see <see cref="Administration.RegistrationInvitation"/>.
    /// </summary>
    public DbSet<Administration.RegistrationInvitation> RegistrationInvitations => Set<Administration.RegistrationInvitation>();

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, RelioDbContextModelCacheKeyFactory>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RelioDbContext).Assembly);
        OwnedEntityIdentityRelationships.Apply(modelBuilder);
        ConfigureProtectedProperties(modelBuilder);
        ConfigureProtectionVersions(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySensitiveDataProtectionMetadata();
        if (_fieldProtectionMode != FieldProtectionMode.LegacyBackfill)
        {
            ApplyAuditTimestamps();
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplySensitiveDataProtectionMetadata();
        if (_fieldProtectionMode != FieldProtectionMode.LegacyBackfill)
        {
            ApplyAuditTimestamps();
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureProtectedProperties(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<Person>().Property(person => person.HowWeMet), ProtectedFieldPurposes.PersonHowWeMet);
        Configure(modelBuilder.Entity<Person>().Property(person => person.Details), ProtectedFieldPurposes.PersonDetails);
        Configure(modelBuilder.Entity<Note>().Property(note => note.Text), ProtectedFieldPurposes.NoteText);
        Configure(modelBuilder.Entity<Interaction>().Property(interaction => interaction.Description), ProtectedFieldPurposes.InteractionDescription);
        Configure(modelBuilder.Entity<Reminder>().Property(reminder => reminder.Title), ProtectedFieldPurposes.ReminderTitle);
        Configure(
            modelBuilder.Entity<IdentityUserToken<string>>().Property(token => token.Value),
            ProtectedFieldPurposes.IdentityUserTokenValue);
        Configure(
            modelBuilder.Entity<UserProfile>().Property(profile => profile.UnsubscribeToken),
            ProtectedFieldPurposes.UnsubscribeToken);
        Configure(
            modelBuilder.Entity<DifficultMoment>().Property(m => m.Description),
            ProtectedFieldPurposes.DifficultMomentDescription);
        Configure(
            modelBuilder.Entity<DifficultMoment>().Property(m => m.Trigger),
            ProtectedFieldPurposes.DifficultMomentTrigger);
        Configure(
            modelBuilder.Entity<DifficultMoment>().Property(m => m.Resolution),
            ProtectedFieldPurposes.DifficultMomentResolution);
        Configure(
            modelBuilder.Entity<DifficultMoment>().Property(m => m.LessonsLearned),
            ProtectedFieldPurposes.DifficultMomentLessonsLearned);
    }

    private void ConfigureProtectionVersions(ModelBuilder modelBuilder)
    {
        ConfigureProtectionVersion(modelBuilder.Entity<Person>());
        ConfigureProtectionVersion(modelBuilder.Entity<Note>());
        ConfigureProtectionVersion(modelBuilder.Entity<Interaction>());
        ConfigureProtectionVersion(modelBuilder.Entity<Reminder>());
        ConfigureProtectionVersion(modelBuilder.Entity<DifficultMoment>());
        ConfigureProtectionVersion(modelBuilder.Entity<UserProfile>());
        ConfigureProtectionVersion(modelBuilder.Entity<IdentityUserToken<string>>());
    }

    private void Configure(PropertyBuilder property, string purpose)
    {
        property.HasConversion(FieldProtectionValueConverter.Create(_fieldProtector, _fieldProtectionMode, purpose));
    }

    private static void ConfigureProtectionVersion<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<int>(FieldProtectionSchema.VersionPropertyName)
            .IsRequired()
            .HasDefaultValue(FieldProtectionSchema.LegacyVersion)
            .IsConcurrencyToken();
    }

    private void ApplySensitiveDataProtectionMetadata()
    {
        if (_fieldProtectionMode == FieldProtectionMode.DesignTime)
        {
            throw new InvalidOperationException("The design-time context cannot write application data.");
        }

        if (_fieldProtectionMode != FieldProtectionMode.LegacyBackfill)
        {
            foreach (var entry in ChangeTracker.Entries<UserProfile>())
            {
                if (entry.State == EntityState.Added || entry.Property(profile => profile.UnsubscribeToken).IsModified)
                {
                    entry.Entity.UnsubscribeTokenVerifier = UnsubscribeTokenHash.Compute(entry.Entity.UnsubscribeToken);
                }
            }
        }
        else
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || entry.Metadata.FindProperty(FieldProtectionSchema.VersionPropertyName) is null)
            {
                continue;
            }

            var version = entry.Property(FieldProtectionSchema.VersionPropertyName);
            if (version.CurrentValue is not int currentVersion
                || currentVersion < FieldProtectionSchema.LegacyVersion
                || currentVersion > FieldProtectionSchema.CurrentVersion)
            {
                throw new InvalidOperationException("The row uses an unsupported protected-data version.");
            }

            version.CurrentValue = FieldProtectionSchema.CurrentVersion;
            if (entry.State == EntityState.Modified)
            {
                version.IsModified = true;
            }
        }
    }

    private void ApplyAuditTimestamps()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<IOwnedEntity>())
        {
            if (TryApplyImportedAudit(entry.Entity))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.UpdatedAtUtc = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    break;
            }
        }
    }
}
