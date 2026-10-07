namespace Relio.Web.Configuration;

/// <summary>Controls optional, operator-supplied policy pages for a hosted Relio instance.</summary>
public sealed class HostedPoliciesOptions
{
    /// <summary>The configuration section containing these options.</summary>
    public const string SectionName = "HostedFeatures:Policies";

    /// <summary>Gets or sets the master switch. Policy hosting is off unless explicitly enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether search engines may index the enabled policy routes in Production.
    /// This is off by default; development and other non-production environments never publish a sitemap.
    /// </summary>
    public bool IndexingEnabled { get; set; }

    /// <summary>Gets or sets privacy-document settings.</summary>
    public PolicyDocumentOptions Privacy { get; set; } = new();

    /// <summary>Gets or sets terms-document settings.</summary>
    public PolicyDocumentOptions Terms { get; set; } = new();

    /// <summary>Gets or sets acceptable-use-document settings.</summary>
    public PolicyDocumentOptions AcceptableUse { get; set; } = new();

    /// <summary>Returns the policy kinds and their configured values in stable route order.</summary>
    /// <returns>The policy settings in privacy, terms, acceptable-use order.</returns>
    public IEnumerable<(PolicyDocumentKind Kind, PolicyDocumentOptions? Options)> GetDocuments()
    {
        yield return (PolicyDocumentKind.Privacy, Privacy);
        yield return (PolicyDocumentKind.Terms, Terms);
        yield return (PolicyDocumentKind.AcceptableUse, AcceptableUse);
    }
}

/// <summary>Configures one externally stored, review-gated policy document.</summary>
public sealed class PolicyDocumentOptions
{
    /// <summary>Gets or sets whether this policy route is enabled under the master switch.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets an absolute path to an external UTF-8 text file. The file must be outside the
    /// application's content root and is read only at startup.
    /// </summary>
    public string? ContentFile { get; set; }

    /// <summary>Gets or sets the public page heading and title metadata.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the unique search and social description for this document.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the operator-supplied document version.</summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the operator's explicit attestation that a human reviewed this exact version.
    /// The application does not draft, verify, or legally approve the document.
    /// </summary>
    public bool HumanReviewAttested { get; set; }

    /// <summary>Gets or sets the UTC date and time the operator recorded the review attestation.</summary>
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}

/// <summary>An immutable policy document loaded from an operator-supplied file.</summary>
/// <param name="Kind">The fixed policy route.</param>
/// <param name="Title">The operator-supplied title and page heading.</param>
/// <param name="Description">The operator-supplied SEO description.</param>
/// <param name="Version">The operator-supplied document version.</param>
/// <param name="ReviewedAtUtc">The UTC time recorded with the human-review attestation.</param>
/// <param name="Content">The plain-text contents, rendered as encoded text.</param>
public sealed record HostedPolicyDocument(
    PolicyDocumentKind Kind,
    string Title,
    string Description,
    string Version,
    DateTimeOffset ReviewedAtUtc,
    string Content)
{
    /// <summary>Gets the fixed route for this document.</summary>
    public string CanonicalPath => Kind.GetRoute();

    /// <summary>Gets the fixed navigation label for this document.</summary>
    public string LinkLabel => Kind.GetLinkLabel();
}

/// <summary>Provides the validated set of policy documents loaded during application startup.</summary>
public interface IHostedPolicyDocumentProvider
{
    /// <summary>Gets whether at least one policy route is enabled and loaded.</summary>
    bool IsEnabled { get; }

    /// <summary>Gets the enabled documents in stable route order.</summary>
    IReadOnlyList<HostedPolicyDocument> EnabledDocuments { get; }

    /// <summary>Gets one enabled document, or <see langword="null"/> when that route is disabled.</summary>
    /// <param name="kind">The fixed policy kind.</param>
    /// <returns>The loaded document, or <see langword="null"/>.</returns>
    HostedPolicyDocument? GetDocument(PolicyDocumentKind kind);
}
