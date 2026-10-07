using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="UserProfile"/>.</summary>
public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.Property(p => p.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(p => p.TimeZoneId)
            .IsRequired()
            .HasMaxLength(100); // comfortably fits every IANA id, e.g. "America/Argentina/Buenos_Aires".

        // Optional; no index - it is only ever read together with the owner's single profile row.
        builder.Property(p => p.DisplayName)
            .HasMaxLength(UserProfile.DisplayNameMaxLength);

        builder.Property(p => p.BirthdayRemindersEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.DefaultBirthdayLeadDays)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.ReminderEmailDelivery)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired()
            .HasDefaultValue(ReminderEmailDelivery.DailyDigest);

        builder.Property(p => p.UnsubscribeToken)
            .HasMaxLength(64);

        // Exactly one profile per user.
        builder.HasIndex(p => p.OwnerId).IsUnique();

        builder.HasIndex(p => p.UnsubscribeToken);
    }
}
