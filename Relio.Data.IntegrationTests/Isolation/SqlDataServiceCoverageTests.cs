using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.Dashboard;
using Relio.Application.Interactions;
using Relio.Application.Metrics;
using Relio.Application.Notes;
using Relio.Application.Onboarding;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Portability;
using Relio.Application.Profile;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Application.Timeline;
using Relio.Data.DependencyInjection;
using Relio.Data.IntegrationTests.Administration;
using Relio.Data.IntegrationTests.Dashboard;
using Relio.Data.IntegrationTests.Encryption;
using Relio.Data.IntegrationTests.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.IntegrationTests.Interactions;
using Relio.Data.IntegrationTests.Metrics;
using Relio.Data.IntegrationTests.Notes;
using Relio.Data.IntegrationTests.Onboarding;
using Relio.Data.IntegrationTests.People;
using Relio.Data.IntegrationTests.Profile;
using Relio.Data.IntegrationTests.Time;

namespace Relio.Data.IntegrationTests.Isolation;

/// <summary>
/// Guards SQL Server isolation evidence for every Application interface implemented by Data or
/// registered by <see cref="ServiceCollectionExtensions.AddRelioData"/>. New services and methods
/// fail this test until they have an explicit, real SQL scenario.
/// </summary>
public sealed class SqlDataServiceCoverageTests
{
    private const string UnusedValidationConnectionString =
        "Server=localhost;Database=RelioCoverageCatalog;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public void Every_registered_application_data_service_method_has_named_sql_evidence()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = ServiceCollectionExtensions.SqlServerProvider,
                ["ConnectionStrings:Relio"] = UnusedValidationConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddRelioData(configuration);

        var applicationAssembly = typeof(IPeopleService).Assembly;
        var applicationDescriptors = services
            .Where(descriptor => descriptor.ServiceType.Assembly == applicationAssembly
                && descriptor.ServiceType.IsInterface)
            .ToArray();
        var registeredServices = applicationDescriptors
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToHashSet();
        var dataAssembly = typeof(ServiceCollectionExtensions).Assembly;
        var implementedDataServices = dataAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
            .SelectMany(type => type.GetInterfaces())
            .Where(interfaceType => interfaceType.IsInterface
                && interfaceType.Assembly == applicationAssembly
                && HasTaskReturningContract(interfaceType))
            .Distinct()
            .ToHashSet();
        var failures = new List<string>();
        var coverageByService = ServiceCoverageCatalog.All.ToDictionary(entry => entry.ServiceType);

        foreach (var descriptor in applicationDescriptors.Where(descriptor => descriptor.ImplementationFactory is null))
        {
            failures.Add(
                $"{descriptor.ServiceType.FullName} is an Application interface registered outside AddDataService; " +
                "its database-lane and isolation coverage must be explicit.");
        }

        foreach (var serviceCoverage in ServiceCoverageCatalog.All)
        {
            var serviceType = serviceCoverage.ServiceType;
            var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .ToArray();
            foreach (var method in methods)
            {
                var methodCoverage = serviceCoverage.Methods
                    .Where(entry => string.Equals(entry.ServiceMethod, method.Name, StringComparison.Ordinal))
                    .ToArray();
                if (methodCoverage.Length == 0)
                {
                    failures.Add($"{serviceType.Name}.{method.Name} has no named SQL scenario.");
                    continue;
                }

                foreach (var scenario in methodCoverage.SelectMany(entry => entry.Scenarios))
                {
                    ValidateScenario(serviceType, method.Name, scenario, failures);
                }
            }

            var missingRequirements = serviceCoverage.RequiredEvidence
                & ~serviceCoverage.Methods
                    .SelectMany(method => method.Scenarios)
                    .Concat(serviceCoverage.SharedScenarios)
                    .Aggregate(SqlIsolationEvidence.None, (evidence, scenario) => evidence | scenario.Evidence);
            if (missingRequirements != SqlIsolationEvidence.None)
            {
                failures.Add(
                    $"{serviceType.Name} is classified as {serviceCoverage.Classification}, but its named SQL " +
                    $"scenarios are missing evidence: {missingRequirements}.");
            }
        }

        foreach (var serviceType in registeredServices.Where(serviceType => !coverageByService.ContainsKey(serviceType)))
        {
            failures.Add($"{serviceType.FullName} has no SQL isolation classification or coverage entry.");
        }

        foreach (var serviceType in implementedDataServices)
        {
            if (!coverageByService.ContainsKey(serviceType))
            {
                failures.Add(
                    $"{serviceType.FullName} has a Data implementation but no SQL isolation classification or coverage entry.");
            }

            if (!registeredServices.Contains(serviceType))
            {
                failures.Add(
                    $"{serviceType.FullName} has a Data implementation but is not registered by AddRelioData; " +
                    "data services must use AddDataService so the database lane is enforced.");
            }
        }

        foreach (var pending in ServiceCoverageCatalog.Pending)
        {
            if (string.IsNullOrWhiteSpace(pending.Blocker))
            {
                failures.Add($"{pending.InterfaceFullName} has no explicit SQL coverage blocker.");
            }

            var pendingType = applicationAssembly.GetType(pending.InterfaceFullName, throwOnError: false);
            if (pendingType is null)
            {
                if (pending.MethodNames.Count != 0)
                {
                    failures.Add(
                        $"{pending.InterfaceFullName} is missing while its pending method list is not empty.");
                }

                continue;
            }

            var actualMethodNames = pendingType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (!actualMethodNames.SequenceEqual(
                    pending.MethodNames.OrderBy(name => name, StringComparer.Ordinal)))
            {
                failures.Add(
                    $"{pending.InterfaceFullName} changed its contract; update the explicit pending method list " +
                    "and complete SQL scenarios before treating it as covered.");
            }

            if (registeredServices.Contains(pendingType))
            {
                failures.Add(
                    $"{pending.InterfaceFullName} is registered while its SQL isolation coverage is explicitly pending: " +
                    pending.Blocker);
            }
        }

        foreach (var exclusion in ServiceCoverageCatalog.Exclusions)
        {
            if (string.IsNullOrWhiteSpace(exclusion.Reason))
            {
                failures.Add($"The exclusion '{exclusion.Name}' has no rationale.");
            }

            if (applicationAssembly.GetType(exclusion.Name, throwOnError: false) is null
                && dataAssembly.GetType(exclusion.Name, throwOnError: false) is null)
            {
                failures.Add($"The exclusion '{exclusion.Name}' does not identify an actual Application or Data type.");
            }
        }

        failures.Should().BeEmpty(
            "each database service must have a named SQL Server scenario per public method, and its privilege boundary must be classified");
    }

    private static void ValidateScenario(
        Type serviceType,
        string serviceMethod,
        SqlIsolationScenario scenario,
        ICollection<string> failures)
    {
        var matchingMethods = scenario.TestType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, scenario.TestMethod, StringComparison.Ordinal))
            .ToArray();
        if (matchingMethods.Length != 1)
        {
            failures.Add(
                $"{serviceType.Name}.{serviceMethod} references missing or ambiguous SQL scenario " +
                $"{scenario.TestType.Name}.{scenario.TestMethod}.");
            return;
        }

        var isSqlServerScenario = CustomAttributeData.GetCustomAttributes(matchingMethods[0])
            .Any(attribute => attribute.AttributeType == typeof(SqlServerFactAttribute)
                || attribute.AttributeType == typeof(SqlServerTheoryAttribute));
        if (!isSqlServerScenario)
        {
            failures.Add(
                $"{serviceType.Name}.{serviceMethod} references {scenario.TestType.Name}.{scenario.TestMethod}, " +
                "which is not guarded by SqlServerFact or SqlServerTheory.");
        }
    }

    private static bool HasTaskReturningContract(Type interfaceType) =>
        interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Any(method => method.ReturnType == typeof(Task)
                || method.ReturnType.IsGenericType
                    && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>));
}

internal static class ServiceCoverageCatalog
{
    private static readonly SqlIsolationScenario AnonymousCurrentUserScenario =
        Scenario<CurrentUserSqlIsolationTests>(
            nameof(CurrentUserSqlIsolationTests.Every_current_user_data_service_rejects_anonymous_calls_before_data_access),
            SqlIsolationEvidence.Anonymous);

    public static IReadOnlyList<SqlDataServiceCoverage> All { get; } =
    [
        CurrentUserWithForeignIdsAndArchiveRules<IPeopleService>(
            Method(nameof(IPeopleService.GetAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.GetAsync_for_another_users_person_returns_null),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.GetAsync_for_a_nonexistent_person_also_returns_null),
                    SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IPeopleService.ListAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.ListAsync_only_returns_the_current_users_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.ListAsync_excludes_archived_people_by_default),
                    SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IPeopleService.ListPageAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.ListPageAsync_only_lists_and_counts_the_current_users_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<ArchiveBoundarySqlIsolationTests>(
                    nameof(ArchiveBoundarySqlIsolationTests.People_pages_hide_archived_rows_by_default_and_report_only_the_current_owners_counts),
                    SqlIsolationEvidence.ArchiveRule),
                Scenario<PeopleListSqlServerTests>(
                    nameof(PeopleListSqlServerTests.ListPageAsync_translates_every_sort),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IPeopleService.FindPossibleDuplicatesAsync),
                Scenario<PossibleDuplicateSqlServerTests>(
                    nameof(PossibleDuplicateSqlServerTests.FindPossibleDuplicatesAsync_never_matches_another_owners_people),
                    SqlIsolationEvidence.CrossOwner),
                Scenario<PossibleDuplicateSqlServerTests>(
                    nameof(PossibleDuplicateSqlServerTests.FindPossibleDuplicatesAsync_includes_archived_people),
                    SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IPeopleService.CreateAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.CreateAsync_can_attach_the_current_users_own_tag),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.CreateAsync_cannot_assign_another_users_relationship_type),
                    SqlIsolationEvidence.ForeignIdRejected),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.CreateAsync_cannot_attach_another_users_tag),
                    SqlIsolationEvidence.ForeignIdRejected)),
            Method(nameof(IPeopleService.UpdateAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.UpdateAsync_for_another_users_person_returns_false_and_does_not_modify_it),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.UpdateAsync_cannot_edit_another_users_contact_method),
                    SqlIsolationEvidence.ForeignIdRejected),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.UpdateAsync_cannot_assign_another_users_relationship_type),
                    SqlIsolationEvidence.ForeignIdRejected)),
            Method(nameof(IPeopleService.ArchiveAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.ArchiveAsync_for_another_users_person_returns_false_and_does_not_archive_it),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IPeopleService.RestoreAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.RestoreAsync_for_another_users_person_returns_false_and_does_not_restore_it),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IPeopleService.DeleteAsync),
                Scenario<PeopleServiceSqlServerOwnershipTests>(
                    nameof(PeopleServiceSqlServerOwnershipTests.DeleteAsync_for_another_users_person_returns_false),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<PersonDeleteSqlServerTests>(
                    nameof(PersonDeleteSqlServerTests.DeleteAsync_deletes_an_archived_person_and_a_second_delete_finds_nobody),
                    SqlIsolationEvidence.ArchiveRule))),

        CurrentUserWithArchiveRules<IDashboardService>(
            Method(nameof(IDashboardService.GetAsync),
                Scenario<DashboardServiceSqlServerTests>(
                    nameof(DashboardServiceSqlServerTests.GetAsync_scopes_each_section_and_related_rows_to_the_current_owner),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.ArchiveRule),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Administrator_role_does_not_bypass_owner_scoping_for_relationship_memory),
                    SqlIsolationEvidence.CrossOwner))),

        CurrentUser<IOnboardingService>(
            Method(nameof(IOnboardingService.GetStateAsync),
                Scenario<OnboardingServiceSqlServerTests>(
                    nameof(OnboardingServiceSqlServerTests.Pending_state_and_dismissal_are_persistent_and_owner_scoped),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IOnboardingService.DismissAsync),
                Scenario<OnboardingServiceSqlServerTests>(
                    nameof(OnboardingServiceSqlServerTests.Pending_state_and_dismissal_are_persistent_and_owner_scoped),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation))),

        CurrentUserWithForeignIdsAndArchiveRules<IInteractionService>(
            Method(nameof(IInteractionService.GetAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Invalid_interaction_participants_and_foreign_notes_leave_both_owners_unchanged),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IInteractionService.ListParticipantCandidatesAsync),
                Scenario<InteractionServiceSqlServerTests>(
                    nameof(InteractionServiceSqlServerTests.Foreign_participant_ids_and_foreign_interactions_are_indistinguishable_and_change_nothing),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.ForeignIdRejected),
                Scenario<ArchiveBoundarySqlIsolationTests>(
                    nameof(ArchiveBoundarySqlIsolationTests.Interaction_participants_keep_only_the_current_profile_or_existing_archived_participants),
                    SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IInteractionService.CreateAsync),
                Scenario<InteractionServiceSqlServerTests>(
                    nameof(InteractionServiceSqlServerTests.Create_update_and_delete_recalculate_each_participants_last_contact_date),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<InteractionServiceSqlServerTests>(
                    nameof(InteractionServiceSqlServerTests.Foreign_participant_ids_and_foreign_interactions_are_indistinguishable_and_change_nothing),
                    SqlIsolationEvidence.ForeignIdRejected),
                Scenario<ArchiveBoundarySqlIsolationTests>(
                    nameof(ArchiveBoundarySqlIsolationTests.Interaction_participants_keep_only_the_current_profile_or_existing_archived_participants),
                    SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IInteractionService.UpdateAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Invalid_interaction_participants_and_foreign_notes_leave_both_owners_unchanged),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ForeignIdRejected | SqlIsolationEvidence.MissingPrimary),
                Scenario<InteractionServiceSqlServerTests>(
                    nameof(InteractionServiceSqlServerTests.Create_update_and_delete_recalculate_each_participants_last_contact_date),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<ArchiveBoundarySqlIsolationTests>(
                    nameof(ArchiveBoundarySqlIsolationTests.Interaction_participants_keep_only_the_current_profile_or_existing_archived_participants),
                    SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IInteractionService.DeleteAsync),
                Scenario<InteractionServiceSqlServerTests>(
                    nameof(InteractionServiceSqlServerTests.Foreign_participant_ids_and_foreign_interactions_are_indistinguishable_and_change_nothing),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<InteractionServiceSqlServerTests>(
                    nameof(InteractionServiceSqlServerTests.Create_update_and_delete_recalculate_each_participants_last_contact_date),
                    SqlIsolationEvidence.OwnerOperation))),

        CurrentUserWithForeignIds<INoteService>(
            Method(nameof(INoteService.GetAsync),
                Scenario<NoteSqlServerTests>(
                    nameof(NoteSqlServerTests.Notes_are_isolated_by_owner_for_all_reads_and_mutations),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Invalid_interaction_participants_and_foreign_notes_leave_both_owners_unchanged),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(INoteService.ListPinnedAsync),
                Scenario<NoteSqlServerTests>(
                    nameof(NoteSqlServerTests.Notes_are_isolated_by_owner_for_all_reads_and_mutations),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ForeignIdRejected)),
            Method(nameof(INoteService.CreateAsync),
                Scenario<NoteSqlServerTests>(
                    nameof(NoteSqlServerTests.Note_creation_and_reads_round_trip_with_sql_server),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<NoteSqlServerTests>(
                    nameof(NoteSqlServerTests.Notes_are_isolated_by_owner_for_all_reads_and_mutations),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ForeignIdRejected)),
            Method(nameof(INoteService.UpdateAsync),
                Scenario<NoteSqlServerTests>(
                    nameof(NoteSqlServerTests.Notes_are_isolated_by_owner_for_all_reads_and_mutations),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(INoteService.DeleteAsync),
                Scenario<NoteSqlServerTests>(
                    nameof(NoteSqlServerTests.Notes_are_isolated_by_owner_for_all_reads_and_mutations),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(INoteService.SetPinnedAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Invalid_interaction_participants_and_foreign_notes_leave_both_owners_unchanged),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary))),

        CurrentUserWithPrimaryIdsAndArchiveRules<IPersonTimelineService>(
            Method(nameof(IPersonTimelineService.GetPageAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Timeline_reads_only_the_current_owner_and_keeps_archived_people_timeline),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation
                        | SqlIsolationEvidence.MissingPrimary | SqlIsolationEvidence.ArchiveRule))),

        CurrentUser<IPeopleImportService>(
            Method(nameof(IPeopleImportService.PreviewAsync),
                Scenario<PeopleImportSqlServerTests>(
                    nameof(PeopleImportSqlServerTests.PreviewAsync_never_reads_another_owners_rows),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IPeopleImportService.ImportAsync),
                Scenario<PeopleImportSqlServerTests>(
                    nameof(PeopleImportSqlServerTests.Imported_people_are_invisible_to_another_owner),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation))),

        CurrentUserWithPrimaryIds<IPersonMergeService>(
            Method(nameof(IPersonMergeService.ListCandidatesAsync),
                Scenario<PersonMergeSqlServerTests>(
                    nameof(PersonMergeSqlServerTests.ListCandidatesAsync_suggests_duplicates_and_never_lists_another_owners_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<PersonMergeBoundarySqlIsolationTests>(
                    nameof(PersonMergeBoundarySqlIsolationTests.Foreign_and_missing_merge_targets_are_indistinguishable_and_owner_B_can_merge_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IPersonMergeService.MergeAsync),
                Scenario<PersonMergeSqlServerTests>(
                    nameof(PersonMergeSqlServerTests.MergeAsync_with_another_owners_duplicate_returns_NotFound_and_changes_nothing),
                    SqlIsolationEvidence.CrossOwner),
                Scenario<PersonMergeSqlServerTests>(
                    nameof(PersonMergeSqlServerTests.MergeAsync_moves_everything_and_deletes_the_duplicate_on_SQL_Server),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<PersonMergeBoundarySqlIsolationTests>(
                    nameof(PersonMergeBoundarySqlIsolationTests.Foreign_and_missing_merge_targets_are_indistinguishable_and_owner_B_can_merge_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary | SqlIsolationEvidence.OwnerOperation))),

        CurrentUserWithForeignIds<IRelationshipTypeService>(
            Method(nameof(IRelationshipTypeService.ListAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IRelationshipTypeService.ListWithUsageAsync),
                Scenario<RelationshipTypeServiceSqlServerTests>(
                    nameof(RelationshipTypeServiceSqlServerTests.ListWithUsageAsync_translates_and_counts_people_including_archived),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IRelationshipTypeService.CreateAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<RelationshipTypeServiceSqlServerTests>(
                    nameof(RelationshipTypeServiceSqlServerTests.CreateAsync_refuses_a_name_that_differs_only_in_case),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IRelationshipTypeService.RenameAsync),
                Scenario<RelationshipTypeServiceSqlServerTests>(
                    nameof(RelationshipTypeServiceSqlServerTests.RenameAsync_and_DeleteAsync_for_another_owners_type_return_false),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IRelationshipTypeService.DeleteAsync),
                Scenario<RelationshipTypeServiceSqlServerTests>(
                    nameof(RelationshipTypeServiceSqlServerTests.DeleteAsync_cannot_move_people_to_another_owners_type),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ForeignIdRejected),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.OwnerOperation))),

        CurrentUserWithPrimaryIds<ITagService>(
            Method(nameof(ITagService.ListAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(ITagService.ListWithUsageAsync),
                Scenario<TagServiceSqlServerTests>(
                    nameof(TagServiceSqlServerTests.ListWithUsageAsync_counts_people_per_tag),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(ITagService.CreateAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<TagServiceSqlServerTests>(
                    nameof(TagServiceSqlServerTests.CreateAsync_refuses_case_only_duplicates),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(ITagService.RenameAsync),
                Scenario<TagServiceSqlServerTests>(
                    nameof(TagServiceSqlServerTests.RenameAsync_and_DeleteAsync_for_another_owners_tag_return_false),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Label_lists_and_mutations_are_owner_scoped_and_foreign_reassignment_is_rejected),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(ITagService.DeleteAsync),
                Scenario<TagServiceSqlServerTests>(
                    nameof(TagServiceSqlServerTests.RenameAsync_and_DeleteAsync_for_another_owners_tag_return_false),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary))),

        CurrentUserWithForeignIdsAndArchiveRules<IReminderService>(
            Method(nameof(IReminderService.GetAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IReminderService.ListAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IReminderService.ListForPersonAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.ForeignIdRejected),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_person_ids_are_rejected_and_reminder_lists_exclude_archived_people),
                    SqlIsolationEvidence.ArchiveRule | SqlIsolationEvidence.ForeignIdRejected)),
            Method(nameof(IReminderService.ListDueAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_person_ids_are_rejected_and_reminder_lists_exclude_archived_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IReminderService.CreateAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_person_ids_are_rejected_and_reminder_lists_exclude_archived_people),
                    SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.ForeignIdRejected)),
            Method(nameof(IReminderService.UpdateAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IReminderService.CompleteAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IReminderService.SnoozeAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IReminderService.DeleteAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.MissingPrimary)),
            Method(nameof(IReminderService.ListDueBirthdaysAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Birthday_and_reach_out_projections_are_owner_scoped_and_hide_archived_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IReminderService.ListUpcomingBirthdaysAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Birthday_and_reach_out_projections_are_owner_scoped_and_hide_archived_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IReminderService.ListOverdueReachOutsAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Birthday_and_reach_out_projections_are_owner_scoped_and_hide_archived_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.ArchiveRule)),
            Method(nameof(IReminderService.MarkContactedAsync),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_person_ids_are_rejected_and_reminder_lists_exclude_archived_people),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.MissingPrimary),
                Scenario<ReminderSqlIsolationTests>(
                    nameof(ReminderSqlIsolationTests.Foreign_and_missing_reminders_have_the_same_results_and_owner_B_can_use_their_own),
                    SqlIsolationEvidence.OwnerOperation))),

        CurrentUser<INotificationPreferencesService>(
            Method(nameof(INotificationPreferencesService.GetPreferencesAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Profile_time_zone_notification_and_two_factor_status_are_per_user),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(INotificationPreferencesService.SetPreferencesAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Profile_time_zone_notification_and_two_factor_status_are_per_user),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation))),

        Capability<IUnsubscribeService>(
            Method(nameof(IUnsubscribeService.UnsubscribeAsync),
                Scenario<ReminderCapabilitySqlIsolationTests>(
                    nameof(ReminderCapabilitySqlIsolationTests.Unsubscribe_bearer_tokens_change_only_the_matching_users_preferences),
                    SqlIsolationEvidence.CapabilityAccepted | SqlIsolationEvidence.CapabilityRejected))),

        Trusted<IReminderSchedulerRunner>(
            Method(nameof(IReminderSchedulerRunner.RunDueRemindersJobAsync),
                Scenario<ReminderCapabilitySqlIsolationTests>(
                    nameof(ReminderCapabilitySqlIsolationTests.Trusted_scheduler_delivers_per_owner_and_stamps_due_reminders_once),
                    SqlIsolationEvidence.TrustedMultiOwner))),

        CurrentUser<IUserTimeZoneService>(
            Method(nameof(IUserTimeZoneService.GetTimeZoneAsync),
                Scenario<UserTimeZoneServiceSqlServerTests>(
                    nameof(UserTimeZoneServiceSqlServerTests.GetTimeZoneAsync_defaults_to_UTC_when_no_profile_exists),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<UserTimeZoneServiceSqlServerTests>(
                    nameof(UserTimeZoneServiceSqlServerTests.User_A_cannot_read_or_overwrite_user_Bs_time_zone),
                    SqlIsolationEvidence.CrossOwner)),
            Method(nameof(IUserTimeZoneService.GetTodayAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Profile_time_zone_notification_and_two_factor_status_are_per_user),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IUserTimeZoneService.SetTimeZoneAsync),
                Scenario<UserTimeZoneServiceSqlServerTests>(
                    nameof(UserTimeZoneServiceSqlServerTests.User_A_cannot_read_or_overwrite_user_Bs_time_zone),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<UserTimeZoneServiceSqlServerTests>(
                    nameof(UserTimeZoneServiceSqlServerTests.SetTimeZoneAsync_with_an_unknown_id_throws_and_creates_no_profile),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IUserTimeZoneService.IsDueTodayAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Profile_time_zone_notification_and_two_factor_status_are_per_user),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IUserTimeZoneService.IsOverdueAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Profile_time_zone_notification_and_two_factor_status_are_per_user),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation))),

        CurrentUser<IUserProfileService>(
            Method(nameof(IUserProfileService.GetDisplayNameAsync),
                Scenario<UserProfileServiceSqlServerTests>(
                    nameof(UserProfileServiceSqlServerTests.User_A_cannot_overwrite_user_Bs_display_name),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IUserProfileService.SetDisplayNameAsync),
                Scenario<UserProfileServiceSqlServerTests>(
                    nameof(UserProfileServiceSqlServerTests.Display_name_round_trips_through_the_real_migrations),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<UserProfileServiceSqlServerTests>(
                    nameof(UserProfileServiceSqlServerTests.User_A_cannot_overwrite_user_Bs_display_name),
                    SqlIsolationEvidence.CrossOwner))),

        CurrentUser<ITwoFactorStatusService>(
            Method(nameof(ITwoFactorStatusService.GetStatusAsync),
                Scenario<CurrentUserSqlIsolationTests>(
                    nameof(CurrentUserSqlIsolationTests.Profile_time_zone_notification_and_two_factor_status_are_per_user),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<TwoFactorSqlServerTests>(
                    nameof(TwoFactorSqlServerTests.Authenticator_key_and_recovery_codes_round_trip_through_the_real_migrations),
                    SqlIsolationEvidence.OwnerOperation))),

        CurrentUserWithForeignReferences<IUserDataPortabilityService>(
            Method(nameof(IUserDataPortabilityService.ExportAsync),
                Scenario<UserDataPortabilitySqlIsolationTests>(
                    nameof(UserDataPortabilitySqlIsolationTests.Export_json_is_scoped_to_the_current_owner_for_every_record_kind),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<UserDataPortabilityLaneSqlServerTests>(
                    nameof(UserDataPortabilityLaneSqlServerTests.Concurrent_exports_are_serialized_on_one_context),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IUserDataPortabilityService.ExportPeopleVCardAsync),
                Scenario<UserDataPortabilitySqlIsolationTests>(
                    nameof(UserDataPortabilitySqlIsolationTests.Export_vCard_is_scoped_to_the_current_owner_and_contains_only_contact_fields),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<UserDataPortabilityLaneSqlServerTests>(
                    nameof(UserDataPortabilityLaneSqlServerTests.Concurrent_exports_are_serialized_on_one_context),
                    SqlIsolationEvidence.OwnerOperation)),
            Method(nameof(IUserDataPortabilityService.RestoreAsync),
                Scenario<UserDataPortabilitySqlIsolationTests>(
                    nameof(UserDataPortabilitySqlIsolationTests.Restore_remaps_the_full_graph_to_a_fresh_account_and_does_not_import_product_activity),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<UserDataPortabilitySqlIsolationTests>(
                    nameof(UserDataPortabilitySqlIsolationTests.Restore_rejects_every_broken_graph_reference_without_changing_registration_data),
                    SqlIsolationEvidence.ForeignIdRejected),
                Scenario<UserDataPortabilitySqlIsolationTests>(
                    nameof(UserDataPortabilitySqlIsolationTests.Restore_rejects_existing_owner_content_without_replacing_it),
                    SqlIsolationEvidence.FreshDestinationRejected))),

        Registration<IAccountRegistrationService>(
            Method(nameof(IAccountRegistrationService.GetEligibilityAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Invitations_are_single_use_capabilities_and_registration_rechecks_instance_rules),
                    SqlIsolationEvidence.RegistrationAllowed | SqlIsolationEvidence.RegistrationRejected)),
            Method(nameof(IAccountRegistrationService.RegisterAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Invitations_are_single_use_capabilities_and_registration_rechecks_instance_rules),
                    SqlIsolationEvidence.RegistrationAllowed | SqlIsolationEvidence.RegistrationRejected))),

        Administration<IUserAdministrationService>(
            Method(nameof(IUserAdministrationService.ListAccountsAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Administration_requires_an_active_administrator_and_never_changes_owned_memory),
                    SqlIsolationEvidence.AdministratorAllowed
                        | SqlIsolationEvidence.NonAdministratorDenied
                        | SqlIsolationEvidence.DisabledAdministratorDenied)),
            Method(nameof(IUserAdministrationService.DisableAccountAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Administration_requires_an_active_administrator_and_never_changes_owned_memory),
                    SqlIsolationEvidence.AdministratorAllowed
                        | SqlIsolationEvidence.NonAdministratorDenied
                        | SqlIsolationEvidence.DisabledAdministratorDenied)),
            Method(nameof(IUserAdministrationService.EnableAccountAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Administration_requires_an_active_administrator_and_never_changes_owned_memory),
                    SqlIsolationEvidence.AdministratorAllowed
                        | SqlIsolationEvidence.NonAdministratorDenied
                        | SqlIsolationEvidence.DisabledAdministratorDenied)),
            Method(nameof(IUserAdministrationService.ListPendingInvitationsAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Administration_requires_an_active_administrator_and_never_changes_owned_memory),
                    SqlIsolationEvidence.NonAdministratorDenied | SqlIsolationEvidence.DisabledAdministratorDenied),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Invitations_are_single_use_capabilities_and_registration_rechecks_instance_rules),
                    SqlIsolationEvidence.AdministratorAllowed)),
            Method(nameof(IUserAdministrationService.CreateInvitationAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Administration_requires_an_active_administrator_and_never_changes_owned_memory),
                    SqlIsolationEvidence.NonAdministratorDenied | SqlIsolationEvidence.DisabledAdministratorDenied),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Invitations_are_single_use_capabilities_and_registration_rechecks_instance_rules),
                    SqlIsolationEvidence.AdministratorAllowed)),
            Method(nameof(IUserAdministrationService.RevokeInvitationAsync),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Administration_requires_an_active_administrator_and_never_changes_owned_memory),
                    SqlIsolationEvidence.NonAdministratorDenied | SqlIsolationEvidence.DisabledAdministratorDenied),
                Scenario<InstanceBoundarySqlIsolationTests>(
                    nameof(InstanceBoundarySqlIsolationTests.Invitations_are_single_use_capabilities_and_registration_rechecks_instance_rules),
                    SqlIsolationEvidence.AdministratorAllowed))),

        CurrentAccountStatus<IAccountSessionStatusService>(
            Method(nameof(IAccountSessionStatusService.GetCurrentStatusAsync),
                Scenario<AccountSessionStatusSqlIsolationTests>(
                    nameof(AccountSessionStatusSqlIsolationTests.Anonymous_call_is_rejected_before_identity_status_is_read),
                    SqlIsolationEvidence.Anonymous),
                Scenario<AccountSessionStatusSqlIsolationTests>(
                    nameof(AccountSessionStatusSqlIsolationTests.Status_is_fresh_owner_scoped_and_reports_a_deleted_real_identity_account_as_missing),
                    SqlIsolationEvidence.CrossOwner
                        | SqlIsolationEvidence.OwnerOperation
                        | SqlIsolationEvidence.AccountUnavailable
                        | SqlIsolationEvidence.DisabledAccountStatus),
                Scenario<AccountLifecycleLaneSqlServerTests>(
                    nameof(AccountLifecycleLaneSqlServerTests.Account_erasure_and_session_status_reads_are_serialized_on_one_context_lane),
                    SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.DatabaseLaneSerialized))),

        SelfAccountErasure<IAccountDeletionService>(
            Method(nameof(IAccountDeletionService.DeleteAsync),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Anonymous_call_is_rejected_before_account_data_is_accessed),
                    SqlIsolationEvidence.Anonymous),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Confirmation_password_and_active_account_checks_reject_without_erasing_owned_data),
                    SqlIsolationEvidence.ConfirmationRequired
                        | SqlIsolationEvidence.PasswordReauthentication
                        | SqlIsolationEvidence.AccountUnavailable),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Last_active_administrator_is_protected_and_can_delete_after_another_admin_exists),
                    SqlIsolationEvidence.LastActiveAdministratorProtected | SqlIsolationEvidence.OwnerOperation),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Erasure_removes_the_identity_owners_full_sql_graph_and_preserves_another_owner),
                    SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Late_owner_row_rejects_erasure_and_rolls_back_the_entire_sql_write_batch),
                    SqlIsolationEvidence.ConcurrentChange),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Erasure_holds_the_database_wide_lifecycle_lock_on_its_sql_session_through_save),
                    SqlIsolationEvidence.CrossInstanceLifecycleLock),
                Scenario<AccountDeletionSqlIsolationTests>(
                    nameof(AccountDeletionSqlIsolationTests.Concurrent_administrator_deletions_leave_one_active_identity_administrator),
                    SqlIsolationEvidence.LastActiveAdministratorProtected),
                Scenario<AccountLifecycleLaneSqlServerTests>(
                    nameof(AccountLifecycleLaneSqlServerTests.Account_erasure_and_session_status_reads_are_serialized_on_one_context_lane),
                    SqlIsolationEvidence.OwnerOperation | SqlIsolationEvidence.DatabaseLaneSerialized))),

        CurrentUser<IProductActivityService>(
            Method(nameof(IProductActivityService.RecordAsync),
                Scenario<ProductActivitySqlIsolationTests>(
                    nameof(ProductActivitySqlIsolationTests.RecordAsync_uses_the_authenticated_active_account_and_is_idempotent_per_day),
                    SqlIsolationEvidence.Anonymous | SqlIsolationEvidence.CrossOwner | SqlIsolationEvidence.OwnerOperation),
                Scenario<ProductActivitySqlIsolationTests>(
                    nameof(ProductActivitySqlIsolationTests.RecordAsync_when_collection_is_disabled_and_authenticated_does_not_query_SQL),
                    SqlIsolationEvidence.OwnerOperation),
                Scenario<ProductActivityLaneSqlServerTests>(
                    nameof(ProductActivityLaneSqlServerTests.Concurrent_activity_records_are_serialized_on_one_context),
                    SqlIsolationEvidence.OwnerOperation))),

        AggregateAdministrator<IProductMetricsReportService>(
            Method(nameof(IProductMetricsReportService.GetAsync),
                Scenario<ProductMetricsSqlServerTests>(
                    nameof(ProductMetricsSqlServerTests.Report_uses_SQL_translatable_aggregates_without_selecting_narrative_columns),
                    SqlIsolationEvidence.AdministratorAllowed),
                Scenario<ProductMetricsSqlServerTests>(
                    nameof(ProductMetricsSqlServerTests.Report_rejects_a_real_nonadministrator_even_if_the_page_is_reached_directly),
                    SqlIsolationEvidence.NonAdministratorDenied),
                Scenario<ProductMetricsPrivilegeSqlIsolationTests>(
                    nameof(ProductMetricsPrivilegeSqlIsolationTests.Report_requires_an_active_administrator_and_rejects_anonymous_or_disabled_callers),
                    SqlIsolationEvidence.Anonymous | SqlIsolationEvidence.DisabledAdministratorDenied),
                Scenario<ProductMetricsLaneSqlServerTests>(
                    nameof(ProductMetricsLaneSqlServerTests.Concurrent_aggregate_reports_are_serialized_on_one_context),
                    SqlIsolationEvidence.AdministratorAllowed))),

        Trusted<IProductMetricsRetentionRunner>(
            Method(nameof(IProductMetricsRetentionRunner.RunAsync),
                Scenario<ProductMetricsSqlServerTests>(
                    nameof(ProductMetricsSqlServerTests.RetentionRunner_deletes_day_90_rows_and_keeps_day_89_rows),
                    SqlIsolationEvidence.TrustedMultiOwner),
                Scenario<ProductMetricsRetentionSqlIsolationTests>(
                    nameof(ProductMetricsRetentionSqlIsolationTests.Trusted_retention_cleans_expired_rows_for_identity_owners_without_removing_retained_rows),
                    SqlIsolationEvidence.TrustedMultiOwner))),

        new(
            typeof(Relio.Data.Encryption.SensitiveFieldBackfillService),
            SqlServiceClassification.TrustedInfrastructure,
            SqlIsolationEvidence.TrustedInfrastructure,
            [
                Method(nameof(Relio.Data.Encryption.SensitiveFieldBackfillService.RunAsync),
                    Scenario<SensitiveFieldBackfillSqlServerTests>(
                        nameof(SensitiveFieldBackfillSqlServerTests.Backfill_converts_legacy_sql_rows_and_is_idempotent_across_context_restarts),
                        SqlIsolationEvidence.TrustedInfrastructure)),
            ],
            []),
        new(
            typeof(Relio.Data.Encryption.IDataProtectionFieldProtector),
            SqlServiceClassification.TrustedInfrastructure,
            SqlIsolationEvidence.TrustedInfrastructure,
            [
                Method(nameof(Relio.Data.Encryption.IDataProtectionFieldProtector.Protect),
                    Scenario<SensitiveFieldProtectionSqlServerTests>(
                        nameof(SensitiveFieldProtectionSqlServerTests.Raw_database_values_do_not_contain_the_sensitive_plaintext_fields),
                        SqlIsolationEvidence.TrustedInfrastructure),
                    Scenario<SensitiveFieldProtectionSqlServerTests>(
                        nameof(SensitiveFieldProtectionSqlServerTests.Maximum_validated_sensitive_values_round_trip_through_sql_server),
                        SqlIsolationEvidence.TrustedInfrastructure)),
                Method(nameof(Relio.Data.Encryption.IDataProtectionFieldProtector.Unprotect),
                    Scenario<SensitiveFieldProtectionSqlServerTests>(
                        nameof(SensitiveFieldProtectionSqlServerTests.Maximum_validated_sensitive_values_round_trip_through_sql_server),
                        SqlIsolationEvidence.TrustedInfrastructure)),
            ],
            []),
    ];

    public static IReadOnlyList<PendingSqlServiceCoverage> Pending { get; } = [];

    public static IReadOnlyList<SqlIsolationExclusion> Exclusions { get; } =
    [
        new("Relio.Application.Reminders.IReminderEmailSender", "Outbound delivery transport; ownership and scheduling are proved through IReminderSchedulerRunner."),
        new("Relio.Application.Time.UserCalendar", "Pure calendar-date calculations; no database or authenticated user."),
        new("Relio.Application.Time.TimeZoneIds", "Pure time-zone identifier parsing and lookup."),
        new("Relio.Application.People.ContactMethodRules", "Pure input normalization and validation rules."),
        new("Relio.Application.People.PersonProfileRules", "Pure profile validation; database ownership is proved by IPeopleService."),
        new("Relio.Application.People.LabelNameRules", "Pure lookup-name validation; persistence and ownership are proved by the relationship-type and tag services."),
        new("Relio.Application.People.TagNameRules", "Pure tag-name normalization and comparison; persistence and ownership are proved by ITagService."),
        new("Relio.Application.People.PersonMergeRules", "Pure merge choices and field-combination rules; persistence and ownership are proved by IPersonMergeService."),
        new("Relio.Application.People.PersonNameNormalizer", "Pure name normalization used by duplicate matching and merge candidate search."),
        new("Relio.Application.People.PossibleDuplicateMatcher", "Pure in-memory duplicate ranking; owner-scoped candidate loading is proved by IPeopleService."),
        new("Relio.Application.Interactions.InteractionRules", "Pure interaction input validation; persistence and participant ownership are proved by IInteractionService."),
        new("Relio.Application.Notes.NoteRules", "Pure note-text validation; persistence and owner scoping are proved by INoteService."),
        new("Relio.Application.Reminders.ReminderRules", "Pure reminder input validation; persistence and owner scoping are proved by IReminderService."),
        new("Relio.Application.Reminders.BirthdayReminderCalculator", "Pure birthday-date calculation; user time-zone reads are proved by IReminderService and IUserTimeZoneService."),
        new("Relio.Application.Reminders.ReachOutCalculator", "Pure cadence-date calculation; owner scoping is proved by IReminderService."),
        new("Relio.Application.Reminders.ReminderRecurrence", "Pure recurrence-date calculation; persistence and owner scoping are proved by IReminderService."),
        new("Relio.Application.Portability.UserDataPortabilityRules", "Pure portable-document validation; real restore ownership and fresh-destination behavior are proved by IUserDataPortabilityService."),
        new("Relio.Application.Metrics.ProductActivityCohortRules", "Pure cohort-date calculation; persisted owner attribution is proved by IProductActivityService."),
    ];

    private static SqlDataServiceCoverage CurrentUser<TService>(params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(false, false, false, methods);

    private static SqlDataServiceCoverage CurrentUserWithPrimaryIds<TService>(
        params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(false, false, true, methods);

    private static SqlDataServiceCoverage CurrentUserWithForeignIds<TService>(
        params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(true, false, true, methods);

    private static SqlDataServiceCoverage CurrentUserWithForeignReferences<TService>(
        params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(true, false, false, methods);

    private static SqlDataServiceCoverage CurrentUserWithArchiveRules<TService>(
        params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(false, true, false, methods);

    private static SqlDataServiceCoverage CurrentUserWithPrimaryIdsAndArchiveRules<TService>(
        params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(false, true, true, methods);

    private static SqlDataServiceCoverage CurrentUserWithForeignIdsAndArchiveRules<TService>(
        params SqlMethodCoverage[] methods) =>
        CurrentUser<TService>(true, true, true, methods);

    private static SqlDataServiceCoverage CurrentUser<TService>(
        bool foreignIdValidation,
        bool archivedBehavior,
        bool primaryIds,
        params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.CurrentUserOwned,
            SqlIsolationEvidence.Anonymous
                | SqlIsolationEvidence.CrossOwner
                | SqlIsolationEvidence.OwnerOperation
                | (primaryIds ? SqlIsolationEvidence.MissingPrimary : SqlIsolationEvidence.None)
                | (foreignIdValidation ? SqlIsolationEvidence.ForeignIdRejected : SqlIsolationEvidence.None)
                | (archivedBehavior ? SqlIsolationEvidence.ArchiveRule : SqlIsolationEvidence.None),
            methods,
            [AnonymousCurrentUserScenario]);

    private static SqlDataServiceCoverage Capability<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.BearerCapability,
            SqlIsolationEvidence.CapabilityAccepted | SqlIsolationEvidence.CapabilityRejected,
            methods,
            []);

    private static SqlDataServiceCoverage Trusted<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.TrustedBackground,
            SqlIsolationEvidence.TrustedMultiOwner,
            methods,
            []);

    private static SqlDataServiceCoverage Registration<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.PublicRegistration,
            SqlIsolationEvidence.RegistrationAllowed | SqlIsolationEvidence.RegistrationRejected,
            methods,
            []);

    private static SqlDataServiceCoverage Administration<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.InstanceAdministration,
            SqlIsolationEvidence.AdministratorAllowed
                | SqlIsolationEvidence.NonAdministratorDenied
                | SqlIsolationEvidence.DisabledAdministratorDenied,
            methods,
            []);

    private static SqlDataServiceCoverage AggregateAdministrator<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.AggregateAdministrator,
            SqlIsolationEvidence.AdministratorAllowed
                | SqlIsolationEvidence.NonAdministratorDenied
                | SqlIsolationEvidence.DisabledAdministratorDenied
                | SqlIsolationEvidence.Anonymous,
            methods,
            []);

    private static SqlDataServiceCoverage SelfAccountErasure<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.SelfAccountErasure,
            SqlIsolationEvidence.Anonymous
                | SqlIsolationEvidence.CrossOwner
                | SqlIsolationEvidence.OwnerOperation
                | SqlIsolationEvidence.ConfirmationRequired
                | SqlIsolationEvidence.PasswordReauthentication
                | SqlIsolationEvidence.AccountUnavailable
                | SqlIsolationEvidence.LastActiveAdministratorProtected
                | SqlIsolationEvidence.ConcurrentChange
                | SqlIsolationEvidence.CrossInstanceLifecycleLock
                | SqlIsolationEvidence.DatabaseLaneSerialized,
            methods,
            []);

    private static SqlDataServiceCoverage CurrentAccountStatus<TService>(params SqlMethodCoverage[] methods) =>
        new(
            typeof(TService),
            SqlServiceClassification.CurrentAccountStatus,
            SqlIsolationEvidence.Anonymous
                | SqlIsolationEvidence.CrossOwner
                | SqlIsolationEvidence.OwnerOperation
                | SqlIsolationEvidence.AccountUnavailable
                | SqlIsolationEvidence.DisabledAccountStatus
                | SqlIsolationEvidence.DatabaseLaneSerialized,
            methods,
            []);

    private static SqlMethodCoverage Method(string serviceMethod, params SqlIsolationScenario[] scenarios) =>
        new(serviceMethod, scenarios);

    private static SqlIsolationScenario Scenario<TTest>(string testMethod, SqlIsolationEvidence evidence) =>
        new(typeof(TTest), testMethod, evidence);
}

internal sealed record SqlDataServiceCoverage(
    Type ServiceType,
    SqlServiceClassification Classification,
    SqlIsolationEvidence RequiredEvidence,
    IReadOnlyList<SqlMethodCoverage> Methods,
    IReadOnlyList<SqlIsolationScenario> SharedScenarios);

internal sealed record SqlMethodCoverage(string ServiceMethod, IReadOnlyList<SqlIsolationScenario> Scenarios);

internal sealed record SqlIsolationScenario(Type TestType, string TestMethod, SqlIsolationEvidence Evidence);

internal sealed record PendingSqlServiceCoverage(
    string InterfaceFullName,
    IReadOnlyList<string> MethodNames,
    string Blocker);

internal sealed record SqlIsolationExclusion(string Name, string Reason);

internal enum SqlServiceClassification
{
    CurrentUserOwned,
    BearerCapability,
    TrustedBackground,
    PublicRegistration,
    InstanceAdministration,
    AggregateAdministrator,
    CurrentAccountStatus,
    SelfAccountErasure,
    TrustedInfrastructure,
}

[Flags]
internal enum SqlIsolationEvidence
{
    None = 0,
    Anonymous = 1 << 0,
    CrossOwner = 1 << 1,
    OwnerOperation = 1 << 2,
    ForeignIdRejected = 1 << 3,
    MissingPrimary = 1 << 4,
    ArchiveRule = 1 << 5,
    CapabilityAccepted = 1 << 6,
    CapabilityRejected = 1 << 7,
    TrustedMultiOwner = 1 << 8,
    RegistrationAllowed = 1 << 9,
    RegistrationRejected = 1 << 10,
    AdministratorAllowed = 1 << 11,
    NonAdministratorDenied = 1 << 12,
    DisabledAdministratorDenied = 1 << 13,
    FreshDestinationRejected = 1 << 14,
    TrustedInfrastructure = 1 << 15,
    ConfirmationRequired = 1 << 16,
    PasswordReauthentication = 1 << 17,
    AccountUnavailable = 1 << 18,
    LastActiveAdministratorProtected = 1 << 19,
    ConcurrentChange = 1 << 20,
    CrossInstanceLifecycleLock = 1 << 21,
    DisabledAccountStatus = 1 << 22,
    DatabaseLaneSerialized = 1 << 23,
}
