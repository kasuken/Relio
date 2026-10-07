namespace Relio.Web.Configuration;

/// <summary>The fixed public policy routes Relio can host.</summary>
public enum PolicyDocumentKind
{
    /// <summary>The privacy notice at <c>/privacy</c>.</summary>
    Privacy,

    /// <summary>The terms of service at <c>/terms</c>.</summary>
    Terms,

    /// <summary>The acceptable-use policy at <c>/acceptable-use</c>.</summary>
    AcceptableUse,
}

/// <summary>Provides fixed route and navigation labels for public policy documents.</summary>
public static class PolicyDocumentKindExtensions
{
    /// <summary>Gets the application-owned route for a policy kind.</summary>
    /// <param name="kind">The policy kind.</param>
    /// <returns>A fixed route that contains no user-supplied value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not supported.</exception>
    public static string GetRoute(this PolicyDocumentKind kind) =>
        kind switch
        {
            PolicyDocumentKind.Privacy => "/privacy",
            PolicyDocumentKind.Terms => "/terms",
            PolicyDocumentKind.AcceptableUse => "/acceptable-use",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The policy document kind is not supported."),
        };

    /// <summary>Gets the concise, application-owned navigation label for a policy kind.</summary>
    /// <param name="kind">The policy kind.</param>
    /// <returns>A fixed link label.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not supported.</exception>
    public static string GetLinkLabel(this PolicyDocumentKind kind) =>
        kind switch
        {
            PolicyDocumentKind.Privacy => "Privacy",
            PolicyDocumentKind.Terms => "Terms",
            PolicyDocumentKind.AcceptableUse => "Acceptable use",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The policy document kind is not supported."),
        };
}
