using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// User B cannot list, create, update or delete user A's person links, and cannot link his own
/// person to one of user A's people (issue #129). InMemory proves the service-level logic; the same
/// scenarios run against SQL Server in <c>PersonLinkSqlServerTests</c>.
/// </summary>
public class PersonLinkServiceOwnershipTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task Listing_another_users_person_gives_nothing()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext, UserA);
        await CreateService(dbContext, UserA).CreateAsync(Create(anna, marco, PersonLinkKind.Friend));

        (await CreateService(dbContext, UserB).ListForPersonAsync(anna)).Should().BeEmpty();
        (await CreateService(dbContext, UserB).ListForPersonAsync(marco)).Should().BeEmpty();
    }

    [Fact]
    public async Task Updating_or_deleting_another_users_link_returns_false_and_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext, UserA);
        var linkId = await CreateService(dbContext, UserA).CreateAsync(Create(anna, marco, PersonLinkKind.Friend));
        var serviceB = CreateService(dbContext, UserB);

        (await serviceB.UpdateAsync(linkId, new UpdatePersonLinkRequest { PersonId = anna, Kind = PersonLinkKind.Manager }))
            .Should().BeFalse();
        (await serviceB.DeleteAsync(linkId)).Should().BeFalse();

        var stored = await dbContext.PersonLinks.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(linkId);
        stored.Kind.Should().Be(PersonLinkKind.Friend);
    }

    [Fact]
    public async Task Linking_two_of_another_users_people_is_refused()
    {
        await using var dbContext = CreateDbContext();
        var (anna, marco) = await AddTwoPeopleAsync(dbContext, UserA);

        var act = () => CreateService(dbContext, UserB).CreateAsync(Create(anna, marco, PersonLinkKind.Friend));

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.People);
        (await dbContext.PersonLinks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Linking_ones_own_person_to_another_users_person_is_refused_exactly_like_an_unknown_id()
    {
        await using var dbContext = CreateDbContext();
        var annaOfA = await PersonLinkServiceTests.AddPersonAsync(dbContext, UserA, "Anna");
        var ownOfB = await PersonLinkServiceTests.AddPersonAsync(dbContext, UserB, "Bea");
        var serviceB = CreateService(dbContext, UserB);

        var foreign = () => serviceB.CreateAsync(Create(ownOfB, annaOfA, PersonLinkKind.Friend));
        var foreignFirst = () => serviceB.CreateAsync(Create(annaOfA, ownOfB, PersonLinkKind.Friend));
        var unknown = () => serviceB.CreateAsync(Create(ownOfB, Guid.NewGuid(), PersonLinkKind.Friend));

        var foreignError = (await foreign.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which;
        var foreignFirstError = (await foreignFirst.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which;
        var unknownError = (await unknown.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which;
        foreignError.Message.Should().Be(unknownError.Message);
        foreignFirstError.Message.Should().Be(unknownError.Message);
        foreignError.EntityName.Should().Be(ForeignEntityNames.People);
        (await dbContext.PersonLinks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Each_user_can_link_the_same_pair_of_ids_only_among_their_own_people()
    {
        await using var dbContext = CreateDbContext();
        var (annaA, marcoA) = await AddTwoPeopleAsync(dbContext, UserA);
        var (annaB, marcoB) = await AddTwoPeopleAsync(dbContext, UserB);

        await CreateService(dbContext, UserA).CreateAsync(Create(annaA, marcoA, PersonLinkKind.Friend));
        await CreateService(dbContext, UserB).CreateAsync(Create(annaB, marcoB, PersonLinkKind.Colleague));

        (await CreateService(dbContext, UserA).ListForPersonAsync(annaA)).Single().Role.Should().Be(PersonLinkKind.Friend);
        (await CreateService(dbContext, UserB).ListForPersonAsync(annaB)).Single().Role.Should().Be(PersonLinkKind.Colleague);
    }

    private static CreatePersonLinkRequest Create(Guid personId, Guid relatedPersonId, PersonLinkKind kind) =>
        new() { PersonId = personId, RelatedPersonId = relatedPersonId, Kind = kind };

    private static PersonLinkService CreateService(RelioDbContext dbContext, string userId) =>
        new(dbContext, new FakeCurrentUser(userId));

    private static RelioDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        TimeProvider.System,
        FieldProtector);

    private static async Task<(Guid Anna, Guid Marco)> AddTwoPeopleAsync(RelioDbContext dbContext, string ownerId) =>
        (await PersonLinkServiceTests.AddPersonAsync(dbContext, ownerId, "Anna", "Rossi"),
         await PersonLinkServiceTests.AddPersonAsync(dbContext, ownerId, "Marco", "Rossi"));
}
