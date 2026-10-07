namespace Relio.Web.Configuration;

/// <summary>Configures the public link to the source for the running Relio build.</summary>
public sealed class SourceCodeOptions
{
    /// <summary>The top-level configuration key that supplies the source URL.</summary>
    public const string ConfigurationKey = "SourceCodeUrl";

    /// <summary>The upstream repository used by an unmodified standard release.</summary>
    public const string DefaultSourceCodeUrl = "https://github.com/kasuken/Relio";

    /// <summary>Gets or sets the validated HTTP(S) URL shown in every Relio shell.</summary>
    public string SourceCodeUrl { get; set; } = DefaultSourceCodeUrl;
}
