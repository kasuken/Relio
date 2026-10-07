using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Relio.Data.Encryption;

internal static class FieldProtectionValueConverter
{
    public static ValueConverter<string?, string?> Create(
        IDataProtectionFieldProtector protector,
        FieldProtectionMode mode,
        string purpose)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        return mode switch
        {
            FieldProtectionMode.Encrypted => new ValueConverter<string?, string?>(
                value => protector.Protect(value, purpose),
                value => protector.Unprotect(value, purpose)),
            FieldProtectionMode.LegacyBackfill => new ValueConverter<string?, string?>(
                value => value,
                value => value),
            FieldProtectionMode.DesignTime => new ValueConverter<string?, string?>(
                value => RejectDesignTimeValue(value),
                value => RejectDesignTimeValue(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static string? RejectDesignTimeValue(string? _) =>
        throw new InvalidOperationException("The design-time context cannot read or write protected application data.");
}
