using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtectSensitiveFieldsAndAddProductMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_UnsubscribeToken",
                table: "UserProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "UnsubscribeToken",
                table: "UserProfiles",
                type: "nvarchar(1344)",
                maxLength: 1344,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SensitiveDataProtectionVersion",
                table: "UserProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UnsubscribeTokenVerifier",
                table: "UserProfiles",
                type: "char(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Reminders",
                type: "nvarchar(2024)",
                maxLength: 2024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<int>(
                name: "SensitiveDataProtectionVersion",
                table: "Reminders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "HowWeMet",
                table: "People",
                type: "nvarchar(max)",
                maxLength: 6024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "People",
                type: "nvarchar(max)",
                maxLength: 21024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SensitiveDataProtectionVersion",
                table: "People",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SensitiveDataProtectionVersion",
                table: "Notes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SensitiveDataProtectionVersion",
                table: "Interactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SensitiveDataProtectionVersion",
                table: "AspNetUserTokens",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProductActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CohortStartedOnUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    LastActiveOnUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    ReturnedInDays30To59 = table.Column<bool>(type: "bit", nullable: false),
                    RetentionExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductActivities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UnsubscribeTokenVerifier",
                table: "UserProfiles",
                column: "UnsubscribeTokenVerifier");

            migrationBuilder.CreateIndex(
                name: "IX_ProductActivities_CohortStartedOnUtc",
                table: "ProductActivities",
                column: "CohortStartedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProductActivities_OwnerId",
                table: "ProductActivities",
                column: "OwnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductActivities_RetentionExpiresAtUtc",
                table: "ProductActivities",
                column: "RetentionExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Protected data cannot be downgraded to the plaintext schema. Restore the pre-upgrade " +
                "database backup and its matching key material instead.");
        }
    }
}
