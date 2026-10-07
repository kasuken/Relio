using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Relio.Web.Configuration;

/// <summary>
/// Loads enabled policy files once at startup and exposes only immutable, plain-text documents.
/// </summary>
public sealed class HostedPolicyDocumentProvider(
    IOptions<HostedPoliciesOptions> options) : IHostedPolicyDocumentProvider, IHostedService
{
    /// <summary>The maximum UTF-8 size of one policy document.</summary>
    public const int MaximumContentBytes = 1_048_576;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly IOptions<HostedPoliciesOptions> _options = options
        ?? throw new ArgumentNullException(nameof(options));
    private IReadOnlyList<HostedPolicyDocument> _documents = Array.Empty<HostedPolicyDocument>();

    /// <inheritdoc />
    public bool IsEnabled => EnabledDocuments.Count > 0;

    /// <inheritdoc />
    public IReadOnlyList<HostedPolicyDocument> EnabledDocuments => Volatile.Read(ref _documents);

    /// <inheritdoc />
    public HostedPolicyDocument? GetDocument(PolicyDocumentKind kind) =>
        EnabledDocuments.FirstOrDefault(document => document.Kind == kind);

    /// <summary>Loads all enabled external documents before the server accepts requests.</summary>
    /// <param name="cancellationToken">Cancels file reads during host startup.</param>
    /// <returns>A task that completes after every enabled document has been loaded.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var policyOptions = _options.Value;
        if (!policyOptions.Enabled)
        {
            Volatile.Write(ref _documents, Array.Empty<HostedPolicyDocument>());
            return;
        }

        var documents = new List<HostedPolicyDocument>();
        foreach (var (kind, documentOptions) in policyOptions.GetDocuments())
        {
            if (documentOptions?.Enabled != true)
            {
                continue;
            }

            var content = await ReadContentAsync(kind, documentOptions.ContentFile!, cancellationToken)
                .ConfigureAwait(false);
            documents.Add(new HostedPolicyDocument(
                kind,
                documentOptions.Title!,
                documentOptions.Description!,
                documentOptions.Version!,
                documentOptions.ReviewedAtUtc!.Value,
                content));
        }

        ReadOnlyCollection<HostedPolicyDocument> immutableDocuments = documents.AsReadOnly();
        Volatile.Write(ref _documents, immutableDocuments);
    }

    /// <summary>Completes shutdown; loaded policy text requires no disposal.</summary>
    /// <param name="cancellationToken">Cancels host shutdown.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<string> ReadContentAsync(
        PolicyDocumentKind kind,
        string contentFile,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                Path.GetFullPath(contentFile),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                useAsync: true);
            var bytes = new byte[MaximumContentBytes + 1];
            var byteCount = 0;
            while (byteCount < bytes.Length)
            {
                var bytesRead = await stream.ReadAsync(bytes.AsMemory(byteCount), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                byteCount += bytesRead;
            }

            if (byteCount > MaximumContentBytes)
            {
                throw CreateLoadException(kind);
            }

            var content = StrictUtf8.GetString(bytes, 0, byteCount);
            if (content.StartsWith('\uFEFF'))
            {
                content = content[1..];
            }

            if (string.IsNullOrWhiteSpace(content) || content.Contains('\0'))
            {
                throw CreateLoadException(kind);
            }

            return content;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            throw CreateLoadException(kind);
        }
    }

    private static InvalidOperationException CreateLoadException(PolicyDocumentKind kind) =>
        new(
            $"The enabled {kind.GetLinkLabel()} document could not be loaded. " +
            $"Provide a readable, non-empty external UTF-8 file no larger than {MaximumContentBytes} bytes.");
}
