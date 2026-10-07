using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #27: the candidate queries translate (a string
/// column compared with an enum, <c>IN</c> over the normalized value, <c>EndsWith</c> on the phone
/// key) and find what the matcher then ranks. The matching rules themselves are proven in
/// <c>Relio.Application.Tests</c>. That the <c>(OwnerId, NormalizedValue)</c> index these queries rely
/// on exists is already proven by <see cref="ContactMethodSqlServerTests"/>
/// (<c>Contact_method_indexes_exist_with_their_columns_in_order</c>).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PossibleDuplicateSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task FindPossibleDuplicatesAsync_translates_and_finds_similar_names_emails_and_phones()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var john = await TestDataFactory.CreatePersonAsync(dbContext, owner, "John", "Smith");
        var ada = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada", "Byron");
        var grace = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Grace", "Hopper");
        var lin = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Lin", "Chen");
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, ada, "Ada@Example.com");
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, grace, "+44 7700 900123", ContactMethodKind.Phone);
        // An address that looks like the probe's email must not count: Kind is compared.
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, lin, "ada@example.com", ContactMethodKind.Address);
        var service = TestDataFactory.CreateService(dbContext, owner);

        var byName = await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "Jon", LastName = "Smith" });
        var byEmail = await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Zed",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ADA@example.COM")],
        });
        var byPhone = await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Zed",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Phone, null, "(07700) 900 123")],
        });

        byName.Should().ContainSingle().Which.Id.Should().Be(john);
        byName[0].Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
        byEmail.Should().ContainSingle().Which.Id.Should().Be(ada);
        byEmail[0].Reasons.Should().Equal(PossibleDuplicateReason.SameEmail);
        byPhone.Should().ContainSingle().Which.Id.Should().Be(grace);
        byPhone[0].Reasons.Should().Equal(PossibleDuplicateReason.SamePhone);
    }

    [SqlServerFact]
    public async Task FindPossibleDuplicatesAsync_includes_archived_people()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var archived = await TestDataFactory.CreatePersonAsync(dbContext, owner, "John", "Smith", isArchived: true);

        var result = await TestDataFactory.CreateService(dbContext, owner)
            .FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "john", LastName = "SMITH" });

        result.Should().ContainSingle();
        result[0].Id.Should().Be(archived);
        result[0].IsArchived.Should().BeTrue();
    }

    [SqlServerFact]
    public async Task FindPossibleDuplicatesAsync_never_matches_another_owners_people()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var other = await TestDataFactory.CreateOwnerAsync(fixture);
        var theirs = await TestDataFactory.CreatePersonAsync(dbContext, other, "John", "Smith");
        await TestDataFactory.CreateContactMethodAsync(dbContext, other, theirs, "john@example.com");
        await TestDataFactory.CreateContactMethodAsync(dbContext, other, theirs, "+44 7700 900123", ContactMethodKind.Phone, 1);

        var result = await TestDataFactory.CreateService(dbContext, owner).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "John",
            LastName = "Smith",
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Email, null, "john@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "07700 900123"),
            ],
        });

        result.Should().BeEmpty();
    }
}
