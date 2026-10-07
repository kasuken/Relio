using Microsoft.AspNetCore.DataProtection;

namespace Relio.Web.Encryption;

/// <inheritdoc />
public sealed class DataProtectionStartupCheck(IDataProtectionProvider provider) : IDataProtectionStartupCheck
{
    private const string Purpose = "Relio.Startup.KeyRingValidation.v1";
    private const string Canary = "Relio Data Protection key-ring validation v1";

    /// <inheritdoc />
    public void Validate()
    {
        try
        {
            var protector = provider.CreateProtector(Purpose);
            var protectedCanary = protector.Protect(Canary);
            if (!string.Equals(protector.Unprotect(protectedCanary), Canary, StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "Relio Data Protection could not use the configured durable key ring and key protection.");
        }
    }
}
