using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

public class PersonLinkServiceTests
{
    private const string Owner = "link-owner";

    [Fact]
    public async Task Linking_from_either_profile_stores_one_identical_normalized_row()
    {
        await using var fromAnna = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(fromAnna);

        await CreateService(fromAnna).CreateAsync(Create(anna, marco, PersonLinkKind.Parent));
        var first = await fromAnna.PersonLinks.AsNoTracking().SingleAsync();

        // The same pair from the other profile is the same row, so it is refused, not stored twice.
        var again = () => CreateService(fromAnna).CreateAsync(Create(marco, anna, PersonLinkKind.Child));
        (await again.Should().ThrowAsync<PersonLinkValidationException>())
            .Which.Errors.Should().Equal(PersonLinkValidationError.AlreadyLinked);

        (await fromAnna.PersonLinks.CountAsync()).Should().Be(1);
        first.OwnerId.Should().Be(Owner);
        first.PersonId.CompareTo(first.RelatedPersonId).Should().BeNegative();
        first.RoleOfOther(anna).Should().Be(PersonLinkKind.Parent);
        first.RoleOfOther(marco).Should().Be(PersonLinkKind.Child);
    }

    [Fact]
    public async Task Creating_from_the_higher_id_stores_the_same_row_as_creating_from_the_lower()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        var lower = anna.CompareTo(marco) < 0 ? anna : marco;
        var higher = lower == anna ? marco : anna;

        await CreateService(dbContext).CreateAsync(Create(higher, lower, PersonLinkKind.Child));

        var stored = await dbContext.PersonLinks.AsNoTracking().SingleAsync();
        stored.PersonId.Should().Be(lower);
        stored.RelatedPersonId.Should().Be(higher);
        stored.Kind.Should().Be(PersonLinkKind.Parent, "the lower person's parent is the higher person");
    }

    [Fact]
    public async Task Each_profile_lists_the_other_with_the_reciprocal_role()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        await CreateService(dbContext).CreateAsync(Create(anna, marco, PersonLinkKind.Parent));

        var fromAnna = await CreateService(dbContext).ListForPersonAsync(anna);
        var fromMarco = await CreateService(dbContext).ListForPersonAsync(marco);

        var annaSees = fromAnna.Should().ContainSingle().Subject;
        annaSees.OtherPersonId.Should().Be(marco);
        annaSees.OtherDisplayName.Should().Be("Marco Rossi");
        annaSees.Role.Should().Be(PersonLinkKind.Parent);
        var marcoSees = fromMarco.Should().ContainSingle().Subject;
        marcoSees.OtherPersonId.Should().Be(anna);
        marcoSees.OtherDisplayName.Should().Be("Anna Rossi");
        marcoSees.Role.Should().Be(PersonLinkKind.Child);
        marcoSees.LinkId.Should().Be(annaSees.LinkId);
    }

    [Fact]
    public async Task A_symmetric_kind_reads_the_same_from_both_profiles()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        await CreateService(dbContext).CreateAsync(Create(anna, marco, PersonLinkKind.Sibling));

        (await CreateService(dbContext).ListForPersonAsync(anna)).Single().Role.Should().Be(PersonLinkKind.Sibling);
        (await CreateService(dbContext).ListForPersonAsync(marco)).Single().Role.Should().Be(PersonLinkKind.Sibling);
    }

    [Fact]
    public async Task Connections_are_ordered_by_the_other_persons_name()
    {
        await using var dbContext = CreateDbContext();
        var anna = await AddPersonAsync(dbContext, Owner, "Anna");
        var zoe = await AddPersonAsync(dbContext, Owner, "Zoe");
        var bruno = await AddPersonAsync(dbContext, Owner, "bruno");
        var chiara = await AddPersonAsync(dbContext, Owner, "Chiara");
        var service = CreateService(dbContext);
        await service.CreateAsync(Create(anna, zoe, PersonLinkKind.Friend));
        await service.CreateAsync(Create(anna, bruno, PersonLinkKind.Friend));
        await service.CreateAsync(Create(anna, chiara, PersonLinkKind.Friend));

        var names = (await service.ListForPersonAsync(anna)).Select(c => c.OtherDisplayName);

        names.Should().Equal("bruno", "Chiara", "Zoe");
    }

    [Fact]
    public async Task Validation_codes_are_thrown_before_any_person_is_looked_up_and_nothing_is_saved()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var unknown = Guid.NewGuid();

        // None of these ids exist: if the rules did not run first, the foreign-id exception would win.
        var noPerson = () => service.CreateAsync(Create(unknown, Guid.Empty, PersonLinkKind.Friend));
        var self = () => service.CreateAsync(Create(unknown, unknown, PersonLinkKind.Friend));
        var noKind = () => service.CreateAsync(Create(unknown, Guid.NewGuid(), null));
        var badKind = () => service.CreateAsync(Create(unknown, Guid.NewGuid(), (PersonLinkKind)999));

        (await noPerson.Should().ThrowAsync<PersonLinkValidationException>()).Which.Errors
            .Should().Equal(PersonLinkValidationError.PersonRequired);
        (await self.Should().ThrowAsync<PersonLinkValidationException>()).Which.Errors
            .Should().Equal(PersonLinkValidationError.SelfLink);
        (await noKind.Should().ThrowAsync<PersonLinkValidationException>()).Which.Errors
            .Should().Equal(PersonLinkValidationError.KindRequired);
        (await badKind.Should().ThrowAsync<PersonLinkValidationException>()).Which.Errors
            .Should().Equal(PersonLinkValidationError.KindInvalid);
        (await dbContext.PersonLinks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Linking_an_archived_person_is_refused_from_either_side_and_nothing_is_saved()
    {
        await using var dbContext = CreateDbContext();
        var active = await AddPersonAsync(dbContext, Owner, "Anna");
        var archived = await AddPersonAsync(dbContext, Owner, "Marco", isArchived: true);
        var service = CreateService(dbContext);

        var fromActive = () => service.CreateAsync(Create(active, archived, PersonLinkKind.Friend));
        var fromArchived = () => service.CreateAsync(Create(archived, active, PersonLinkKind.Friend));

        (await fromActive.Should().ThrowAsync<PersonLinkValidationException>())
            .Which.Errors.Should().Equal(PersonLinkValidationError.ArchivedPersonNotAllowed);
        (await fromArchived.Should().ThrowAsync<PersonLinkValidationException>())
            .Which.Errors.Should().Equal(PersonLinkValidationError.ArchivedPersonNotAllowed);
        (await dbContext.PersonLinks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Existing_links_to_archived_people_stay_listed_and_editable()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        var service = CreateService(dbContext);
        var linkId = await service.CreateAsync(Create(anna, marco, PersonLinkKind.Friend));
        var archived = await dbContext.People.SingleAsync(p => p.Id == marco);
        archived.IsArchived = true;
        archived.ArchivedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var listed = await service.ListForPersonAsync(anna);
        var updated = await service.UpdateAsync(linkId, new UpdatePersonLinkRequest { PersonId = anna, Kind = PersonLinkKind.Colleague });

        listed.Should().ContainSingle().Which.OtherIsArchived.Should().BeTrue();
        updated.Should().BeTrue();
        (await service.ListForPersonAsync(anna)).Single().Role.Should().Be(PersonLinkKind.Colleague);
    }

    [Fact]
    public async Task Updating_from_either_perspective_stores_the_right_kind()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        var service = CreateService(dbContext);
        var linkId = await service.CreateAsync(Create(anna, marco, PersonLinkKind.Friend));

        (await service.UpdateAsync(linkId, new UpdatePersonLinkRequest { PersonId = marco, Kind = PersonLinkKind.Manager }))
            .Should().BeTrue();

        (await service.ListForPersonAsync(marco)).Single().Role.Should().Be(PersonLinkKind.Manager);
        (await service.ListForPersonAsync(anna)).Single().Role.Should().Be(PersonLinkKind.DirectReport);
        (await dbContext.PersonLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Updating_from_a_person_who_is_not_an_end_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        var stranger = await AddPersonAsync(dbContext, Owner, "Stranger");
        var service = CreateService(dbContext);
        var linkId = await service.CreateAsync(Create(anna, marco, PersonLinkKind.Friend));

        var result = await service.UpdateAsync(linkId, new UpdatePersonLinkRequest { PersonId = stranger, Kind = PersonLinkKind.Manager });

        result.Should().BeFalse();
        (await service.ListForPersonAsync(anna)).Single().Role.Should().Be(PersonLinkKind.Friend);
    }

    [Fact]
    public async Task Updating_with_a_bad_kind_throws_codes_before_loading_the_link()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var missing = () => service.UpdateAsync(Guid.NewGuid(), new UpdatePersonLinkRequest { PersonId = Guid.NewGuid(), Kind = null });
        var undefined = () => service.UpdateAsync(Guid.NewGuid(), new UpdatePersonLinkRequest { PersonId = Guid.NewGuid(), Kind = (PersonLinkKind)99 });

        (await missing.Should().ThrowAsync<PersonLinkValidationException>()).Which.Errors
            .Should().Equal(PersonLinkValidationError.KindRequired);
        (await undefined.Should().ThrowAsync<PersonLinkValidationException>()).Which.Errors
            .Should().Equal(PersonLinkValidationError.KindInvalid);
    }

    [Fact]
    public async Task Deleting_a_link_removes_it_from_both_profiles_and_keeps_the_people()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        var service = CreateService(dbContext);
        var linkId = await service.CreateAsync(Create(anna, marco, PersonLinkKind.Partner));

        (await service.DeleteAsync(linkId)).Should().BeTrue();
        (await service.DeleteAsync(linkId)).Should().BeFalse();

        (await service.ListForPersonAsync(anna)).Should().BeEmpty();
        (await service.ListForPersonAsync(marco)).Should().BeEmpty();
        (await dbContext.People.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_missing_link_is_reported_as_no_result()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        (await service.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        (await service.UpdateAsync(Guid.NewGuid(), new UpdatePersonLinkRequest { PersonId = Guid.NewGuid(), Kind = PersonLinkKind.Friend }))
            .Should().BeFalse();
        (await service.ListForPersonAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task Mutations_leave_nothing_tracked_even_when_they_fail()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext);
        var service = CreateService(dbContext);

        var linkId = await service.CreateAsync(Create(anna, marco, PersonLinkKind.Friend));
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.UpdateAsync(linkId, new UpdatePersonLinkRequest { PersonId = anna, Kind = PersonLinkKind.Sibling });
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        var failed = () => service.CreateAsync(Create(anna, marco, PersonLinkKind.Friend));
        await failed.Should().ThrowAsync<PersonLinkValidationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.ListForPersonAsync(anna);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.DeleteAsync(linkId);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Every_method_needs_a_signed_in_user()
    {
        await using var dbContext = CreateDbContext();
        var service = new PersonLinkService(dbContext, new FakeCurrentUser(null));
        var id = Guid.NewGuid();

        await ((Func<Task>)(() => service.ListForPersonAsync(id))).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.CreateAsync(Create(id, Guid.NewGuid(), PersonLinkKind.Friend))))
            .Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.UpdateAsync(id, new UpdatePersonLinkRequest { PersonId = id, Kind = PersonLinkKind.Friend })))
            .Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.DeleteAsync(id))).Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static CreatePersonLinkRequest Create(Guid personId, Guid relatedPersonId, PersonLinkKind? kind) =>
        new() { PersonId = personId, RelatedPersonId = relatedPersonId, Kind = kind };

    private static PersonLinkService CreateService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner));

    private static RelioDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        TimeProvider.System,
        FieldProtector);

    private static async Task<(Guid Anna, Guid Marco)> AddTwoPeopleAsync(RelioDbContext dbContext)
    {
        var anna = await AddPersonAsync(dbContext, Owner, "Anna", "Rossi");
        var marco = await AddPersonAsync(dbContext, Owner, "Marco", "Rossi");
        return (anna, marco);
    }

    internal static async Task<Guid> AddPersonAsync(
        RelioDbContext dbContext,
        string ownerId,
        string firstName,
        string? lastName = null,
        bool isArchived = false)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = firstName,
            LastName = lastName,
            IsArchived = isArchived,
            ArchivedAtUtc = isArchived ? DateTime.UtcNow : null,
        };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }
}
