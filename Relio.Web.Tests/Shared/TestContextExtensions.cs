using Bunit;
using MudBlazor.Services;

namespace Relio.Web.Tests.Shared;

/// <summary>Wires up what MudBlazor components need to render under bUnit: services and loose JS interop.</summary>
internal static class TestContextExtensions
{
    public static void UseMudBlazor(this BunitContext context)
    {
        context.Services.AddMudServices();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
