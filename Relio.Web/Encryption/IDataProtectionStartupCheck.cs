namespace Relio.Web.Encryption;

/// <summary>Performs an explicit fail-closed check of the configured persistent Data Protection provider.</summary>
public interface IDataProtectionStartupCheck
{
    /// <summary>Verifies that the key ring can protect and authenticate a startup canary.</summary>
    void Validate();
}
