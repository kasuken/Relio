using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.People;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class PersonMergeBoundarySqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Foreign_and_missing_merge_targets_are_indistinguishable_and_owner_B_can_merge_their_own()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid personA;
        Guid personBPrimary;
        Guid personBDuplicate;

        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Owner A");
            personBPrimary = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Owner B");
            personBDuplicate = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Owner B");
        }

        await using (var ownerAScope = harness.AsOwnerA())
        {
            var mergeA = new PersonMergeService(
                ownerAScope.DbContext,
                ownerAScope.CurrentUser,
                harness.Clock);
            (await mergeA.ListCandidatesAsync(personBPrimary)).Should().BeNull();
            (await mergeA.ListCandidatesAsync(Guid.NewGuid())).Should().BeNull();

            var foreignOutcome = await mergeA.MergeAsync(new MergePeopleRequest
            {
                PrimaryId = personA,
                DuplicateId = personBDuplicate,
            });
            var missingOutcome = await mergeA.MergeAsync(new MergePeopleRequest
            {
                PrimaryId = personA,
                DuplicateId = Guid.NewGuid(),
            });
            var foreignPrimaryOutcome = await mergeA.MergeAsync(new MergePeopleRequest
            {
                PrimaryId = personBPrimary,
                DuplicateId = personA,
            });
            var missingPrimaryOutcome = await mergeA.MergeAsync(new MergePeopleRequest
            {
                PrimaryId = Guid.NewGuid(),
                DuplicateId = personA,
            });
            foreignOutcome.Should().Be(MergeOutcome.NotFound);
            missingOutcome.Should().Be(foreignOutcome);
            foreignPrimaryOutcome.Should().Be(foreignOutcome);
            missingPrimaryOutcome.Should().Be(foreignOutcome);
        }

        await using (var ownerBScope = harness.AsOwnerB())
        {
            var mergeB = new PersonMergeService(
                ownerBScope.DbContext,
                ownerBScope.CurrentUser,
                harness.Clock);
            (await mergeB.MergeAsync(new MergePeopleRequest
            {
                PrimaryId = personBPrimary,
                DuplicateId = personBDuplicate,
            })).Should().Be(MergeOutcome.Merged);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == personA))
            .Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == personBPrimary))
            .Should().BeTrue();
        (await verify.People.AsNoTracking().AnyAsync(person => person.Id == personBDuplicate))
            .Should().BeFalse();
        (await verify.People.CountAsync(person => person.OwnerId == harness.OwnerA.Id))
            .Should().Be(1);
        (await verify.People.CountAsync(person => person.OwnerId == harness.OwnerB.Id))
            .Should().Be(1);
    }
}
