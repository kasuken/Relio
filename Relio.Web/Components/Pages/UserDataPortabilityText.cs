using Relio.Application.Portability;

namespace Relio.Web.Components.Pages;

/// <summary>Calm, content-free wording for JSON restore validation errors.</summary>
public static class UserDataPortabilityText
{
    /// <summary>Returns a safe user-facing message for the supplied error codes.</summary>
    public static string For(IReadOnlyList<UserDataPortabilityError> errors)
    {
        if (errors.Contains(UserDataPortabilityError.DestinationNotFresh))
        {
            return "Restore is only available for a fresh account. This account already has Relio data or non-default relationship types.";
        }

        if (errors.Contains(UserDataPortabilityError.UnsupportedVersion))
        {
            return "This Relio export version isn't supported by this instance.";
        }

        if (errors.Contains(UserDataPortabilityError.TooManyRows))
        {
            return "This file contains more data than Relio can safely restore.";
        }

        if (errors.Contains(UserDataPortabilityError.InvalidTimeZone))
        {
            return "The export has an unsupported time zone. Nothing was imported.";
        }

        if (errors.Contains(UserDataPortabilityError.InvalidReference))
        {
            return "Some records in the export don't link to other records in that same file.";
        }

        if (errors.Contains(UserDataPortabilityError.DuplicateId))
        {
            return "The export contains repeated record identifiers.";
        }

        if (errors.Contains(UserDataPortabilityError.InvalidAuditDate))
        {
            return "The export contains invalid record dates or timestamps.";
        }

        if (errors.Contains(UserDataPortabilityError.InvalidValue))
        {
            return "Some values in the export aren't valid for Relio.";
        }

        return "This file isn't a valid Relio data export.";
    }
}
