using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Relio.Web.Identity;

/// <summary>
/// A <see cref="DataProtectorTokenProvider{TUser}"/> used only for password reset tokens (issue
/// #17). <see cref="ServiceCollectionExtensions.AddRelioIdentity"/> registers this under its own
/// provider name and sets <c>IdentityOptions.Tokens.PasswordResetTokenProvider</c> to it, instead
/// of leaving password reset on Identity's "Default" provider - the same one email confirmation
/// uses via <c>IdentityOptions.Tokens.EmailConfirmationTokenProvider</c>
/// (<c>TokenOptions.DefaultProvider</c> covers both by default). Sharing that provider would mean
/// <see cref="PasswordResetTokenProviderOptions.TokenLifespan"/> and the default
/// <see cref="DataProtectionTokenProviderOptions.TokenLifespan"/> (email confirmation's) could
/// never be configured independently - this type exists purely so they can be. See the official
/// guidance on changing a single token purpose's lifespan ("Change the email token lifespan" at
/// https://learn.microsoft.com/aspnet/core/security/authentication/accconfirm), adapted here for
/// password reset instead of email confirmation.
/// </summary>
public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class
{
}

/// <summary>
/// <see cref="DataProtectionTokenProviderOptions"/> for <see cref="PasswordResetTokenProvider{TUser}"/>
/// only. Being a distinct options <em>type</em> (not just a distinct configured value) is what
/// actually isolates this lifespan from email confirmation's - see
/// <see cref="PasswordResetTokenProvider{TUser}"/>'s remarks.
/// </summary>
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        // Distinct from DataProtectionTokenProviderOptions's own default Name
        // ("DataProtectorTokenProvider", used by the "Default" provider/email confirmation) - the
        // data protection purpose string is derived from this, so a password reset token and an
        // email confirmation token are protected under different purposes even though both are a
        // DataProtectorTokenProvider<RelioUser> under the hood.
        Name = "RelioPasswordResetTokenProvider";

        // Overridden from AccountOptions.PasswordReset.TokenLifespan (default 1 hour) by
        // ServiceCollectionExtensions.AddRelioIdentity.
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
