namespace Relio.Web.Components.Marketing;

/// <summary>
/// Marks a static public page endpoint whose stylesheet is loaded by endpoint metadata in
/// <c>App.razor</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MarketingPageAttribute : Attribute
{
}
