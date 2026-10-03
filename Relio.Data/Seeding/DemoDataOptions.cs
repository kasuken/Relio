namespace Relio.Data.Seeding;

/// <summary>
/// Options for <see cref="DemoDataSeeder"/>, bound from the <c>DemoData</c> configuration
/// section.
/// </summary>
public sealed class DemoDataOptions
{
    /// <summary>The configuration section name (<c>DemoData</c>).</summary>
    public const string SectionName = "DemoData";

    /// <summary>
    /// Whether to seed the demo account and its sample data at startup. Defaults to
    /// <see langword="false"/>: this is a development/demo convenience, never a hosted or
    /// self-hosted production behaviour. See <see cref="DemoDataSeeder"/> for the additional
    /// environment guardrail.
    /// </summary>
    public bool Enabled { get; set; }
}
