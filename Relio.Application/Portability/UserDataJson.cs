using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relio.Application.Portability;

/// <summary>Versioned JSON serialization helpers for the portable user-data format.</summary>
public static class UserDataJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Writes a complete snapshot using explicit camel-case fields and string enums.</summary>
    public static Task SerializeAsync(
        Stream destination,
        UserDataExportDocument document,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeAsync(destination, document, Options, cancellationToken);

    /// <summary>Reads one complete snapshot. Unknown fields and malformed JSON are rejected.</summary>
    public static ValueTask<UserDataExportDocument?> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.DeserializeAsync<UserDataExportDocument>(source, Options, cancellationToken);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            MaxDepth = 64,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
