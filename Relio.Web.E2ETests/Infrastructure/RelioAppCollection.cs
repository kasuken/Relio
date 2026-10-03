namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// Groups every E2E test class onto one shared <see cref="RelioAppFixture"/> (one running
/// Relio.Web app, one Chromium instance), via <c>[Collection(RelioAppCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RelioAppCollection : ICollectionFixture<RelioAppFixture>
{
    public const string Name = "Relio app (Playwright)";
}
