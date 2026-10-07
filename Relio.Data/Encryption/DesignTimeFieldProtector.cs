namespace Relio.Data.Encryption;

internal sealed class DesignTimeFieldProtector : IDataProtectionFieldProtector
{
    public Guid ModelCacheIdentity { get; } = Guid.NewGuid();

    public string? Protect(string? value, string purpose) =>
        throw new InvalidOperationException("The design-time context cannot protect application data.");

    public string? Unprotect(string? protectedValue, string purpose) =>
        throw new InvalidOperationException("The design-time context cannot read protected application data.");
}
