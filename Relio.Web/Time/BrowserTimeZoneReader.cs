using Microsoft.JSInterop;

namespace Relio.Web.Time;

/// <summary>
/// <see cref="IBrowserTimeZoneReader"/> implementation backed by the
/// <c>wwwroot/js/timezone.js</c> module. Scoped (registered per-circuit in <c>Program.cs</c>),
/// like Blazor Server JS interop requires; disposes the imported module reference when the
/// circuit ends.
/// </summary>
public sealed class BrowserTimeZoneReader(IJSRuntime jsRuntime) : IBrowserTimeZoneReader, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(
        () => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/timezone.js").AsTask());

    /// <inheritdoc />
    public async Task<string?> GetBrowserTimeZoneIdAsync()
    {
        var module = await _moduleTask.Value;
        return await module.InvokeAsync<string?>("getBrowserTimeZone");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
