using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #29: an import is one transaction that rolls back
/// completely when any statement fails, 2,000 people with their contact methods save and read back in
/// order, year-less birthdays satisfy the check constraints, and the preview matches emails
/// case-insensitively and phones by their last eight digits, only ever against the user's own rows.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PeopleImportSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task ImportAsync_saves_people_and_contact_methods_and_they_read_back_in_order()
    {
        var owner = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();

        var created = await TestDataFactory.CreatePeopleImportService(dbContext, owner).ImportAsync(
        [
            Request("Ada", "Lovelace", contacts:
            [
                new ContactMethodInput(null, ContactMethodKind.Email, "Home", "ada@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, "Mobile", "+44 7700 900123"),
                new ContactMethodInput(null, ContactMethodKind.Address, null, "1 Example Road\nLondon"),
            ]),
            Request("Grace", "Hopper"),
        ]);

        created.Should().Be(2);
        await using var verify = fixture.CreateDbContext();
        var people = await TestDataFactory.CreateService(verify, owner).ListAsync();
        people.Select(p => p.DisplayName).Should().Equal("Ada Lovelace", "Grace Hopper");
        var ada = await TestDataFactory.CreateService(verify, owner).GetAsync(people.Single(p => p.FirstName == "Ada").Id);
        ada!.ContactMethods.Select(c => c.Value).Should().Equal("ada@example.com", "+44 7700 900123", "1 Example Road\nLondon");
        ada.ContactMethods.Select(c => c.SortOrder).Should().Equal(0, 1, 2);
        ada.ContactMethods.Select(c => c.NormalizedValue).Should().Equal("ada@example.com", "+447700900123", "1 example road london");
    }

    [SqlServerFact]
    public async Task ImportAsync_rolls_back_everything_when_a_command_fails()
    {
        var owner = TestDataFactory.NewOwnerId();
        var failure = new FailOnNthInsertInterceptor(failOn: 4);
        await using var dbContext = CreateOneStatementPerCommandContext(failure);

        var act = () => TestDataFactory.CreatePeopleImportService(dbContext, owner).ImportAsync(
            Enumerable.Range(0, 3).Select(i => Request($"Person{i}", null, contacts: [new ContactMethodInput(null, ContactMethodKind.Email, null, $"p{i}@example.com")])).ToList());

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.ToString().Should().Contain("simulated failure");
        failure.InsertsBeforeFailure.Should().Be(3, "some inserts ran inside the transaction before the failure");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await using var verify = fixture.CreateDbContext();
        (await verify.People.CountAsync(p => p.OwnerId == owner)).Should().Be(0);
        (await verify.ContactMethods.CountAsync(c => c.OwnerId == owner)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task ImportAsync_runs_every_write_in_one_transaction()
    {
        var owner = TestDataFactory.NewOwnerId();
        var recorder = new FailOnNthInsertInterceptor(failOn: int.MaxValue);
        await using var dbContext = CreateOneStatementPerCommandContext(recorder);

        await TestDataFactory.CreatePeopleImportService(dbContext, owner).ImportAsync(
            Enumerable.Range(0, 3).Select(i => Request($"Person{i}", null)).ToList());

        recorder.Transactions.Should().HaveCountGreaterThan(2);
        recorder.Transactions.Should().OnlyContain(transaction => transaction != null);
        recorder.Transactions.Distinct().Should().ContainSingle("one transaction for the whole import");
    }

    [SqlServerFact]
    public async Task ImportAsync_of_2000_people_with_three_contact_methods_each_completes()
    {
        var owner = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();
        var requests = Enumerable.Range(0, ImportLimits.MaxPeople).Select(i => Request(
            $"Person{i}",
            $"Family{i % 50}",
            contacts:
            [
                new ContactMethodInput(null, ContactMethodKind.Email, "Home", $"person{i}@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, "Mobile", $"+44 7700 9{i:D5}"),
                new ContactMethodInput(null, ContactMethodKind.Address, null, $"{i} Example Road\nLondon"),
            ])).ToList();

        var created = await TestDataFactory.CreatePeopleImportService(dbContext, owner).ImportAsync(requests);

        created.Should().Be(ImportLimits.MaxPeople);
        await using var verify = fixture.CreateDbContext();
        (await verify.People.CountAsync(p => p.OwnerId == owner)).Should().Be(ImportLimits.MaxPeople);
        (await verify.ContactMethods.CountAsync(c => c.OwnerId == owner)).Should().Be(ImportLimits.MaxPeople * 3);
        (await verify.ContactMethods.Where(c => c.OwnerId == owner && c.Kind == ContactMethodKind.Phone).OrderBy(c => c.NormalizedValue).Select(c => c.NormalizedValue).FirstAsync())
            .Should().Be("+447700900000");
    }

    [SqlServerFact]
    public async Task ImportAsync_respects_the_birthday_check_constraints_for_yearless_birthdays()
    {
        var owner = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();

        await TestDataFactory.CreatePeopleImportService(dbContext, owner).ImportAsync(
        [
            Request("Leap", null, birthday: (2, 29, null)),
            Request("Plain", null, birthday: (4, 15, null)),
            Request("Full", null, birthday: (12, 10, 1985)),
            Request("None", null),
        ]);

        await using var verify = fixture.CreateDbContext();
        var people = await verify.People.AsNoTracking().Where(p => p.OwnerId == owner).ToDictionaryAsync(p => p.FirstName);
        people["Leap"].Birthday.Should().Be(Domain.Birthday.Create(2, 29));
        people["Leap"].BirthdayYear.Should().BeNull();
        people["Plain"].Birthday.Should().Be(Domain.Birthday.Create(4, 15));
        people["Full"].Birthday.Should().Be(Domain.Birthday.Create(12, 10, 1985));
        people["None"].Birthday.Should().BeNull();
    }

    [SqlServerFact]
    public async Task PreviewAsync_matches_emails_case_insensitively_and_phones_by_last_eight_digits()
    {
        var owner = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();
        var peopleService = TestDataFactory.CreateService(dbContext, owner);
        var mail = await peopleService.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Mail",
            LastName = "Holder",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "Holder@Example.com")],
        });
        var phone = await peopleService.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Phone",
            LastName = "Holder",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123")],
        });
        var archived = await peopleService.CreateAsync(new CreatePersonRequest { FirstName = "Gone", LastName = "Away" });
        await peopleService.ArchiveAsync(archived.Id);

        var preview = await TestDataFactory.CreatePeopleImportService(dbContext, owner).PreviewAsync(new ImportReadResult(
        [
            Draft(2, "Zed", "One", new ImportContactDraft(ContactMethodKind.Email, null, "holder@example.com")),
            Draft(3, "Zed", "Two", new ImportContactDraft(ContactMethodKind.Phone, null, "07700 900123")),
            Draft(4, "Gone", "Away"),
            Draft(5, "Zed", "Three"),
        ], 4, false, 0));

        preview.Candidates.Select(c => c.ExistingMatches.Select(m => m.Id).ToArray()).Should().SatisfyRespectively(
            a => a.Should().Equal(mail.Id),
            b => b.Should().Equal(phone.Id),
            c => c.Should().Equal(archived.Id),
            d => d.Should().BeEmpty());
    }

    [SqlServerFact]
    public async Task PreviewAsync_never_reads_another_owners_rows()
    {
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();
        await TestDataFactory.CreateService(dbContext, ownerB).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
        });

        var preview = await TestDataFactory.CreatePeopleImportService(dbContext, ownerA).PreviewAsync(new ImportReadResult(
            [Draft(2, "Ada", "Lovelace", new ImportContactDraft(ContactMethodKind.Email, null, "ada@example.com"))], 1, false, 0));

        preview.Candidates.Single().ExistingMatches.Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task Imported_people_are_invisible_to_another_owner()
    {
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        await using var dbContext = fixture.CreateDbContext();
        await TestDataFactory.CreatePeopleImportService(dbContext, ownerA).ImportAsync([Request("Ada", null, contacts: [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")])]);
        await using var verify = fixture.CreateDbContext();
        var id = (await verify.People.AsNoTracking().SingleAsync(p => p.OwnerId == ownerA)).Id;

        var asB = TestDataFactory.CreateService(verify, ownerB);

        (await asB.GetAsync(id)).Should().BeNull();
        (await asB.ListAsync(includeArchived: true)).Should().BeEmpty();
    }

    private static ImportPersonDraft Draft(int row, string first, string? last, params ImportContactDraft[] contacts) =>
        new(row, first, last, null, null, false, null, contacts);

    private static ImportPersonRequest Request(
        string first,
        string? last,
        (int Month, int Day, int? Year)? birthday = null,
        IReadOnlyList<ContactMethodInput>? contacts = null) =>
        new()
        {
            FirstName = first,
            LastName = last,
            BirthdayMonth = birthday?.Month,
            BirthdayDay = birthday?.Day,
            BirthdayYear = birthday?.Year,
            ContactMethods = contacts ?? [],
        };

    private RelioDbContext CreateOneStatementPerCommandContext(params IInterceptor[] interceptors)
    {
        // MaxBatchSize(1): every INSERT is its own command, so a failure can hit exactly one of them.
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(fixture.ConnectionString, sqlServer => sqlServer.MaxBatchSize(1))
            .AddInterceptors(interceptors)
            .Options;
        return new RelioDbContext(options, TimeProvider.System);
    }

    /// <summary>Counts INSERT commands (and the transaction each ran in); throws on the <c>failOn</c>-th.</summary>
    private sealed class FailOnNthInsertInterceptor(int failOn) : DbCommandInterceptor
    {
        public int InsertsBeforeFailure { get; private set; }

        public List<DbTransaction?> Transactions { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (!command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal))
            {
                return;
            }

            if (InsertsBeforeFailure + 1 >= failOn)
            {
                throw new InvalidOperationException("simulated failure");
            }

            InsertsBeforeFailure++;
            Transactions.Add(command.Transaction);
        }
    }
}
