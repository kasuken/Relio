using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemindersAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BirthdayRemindersEnabled",
                table: "UserProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultBirthdayLeadDays",
                table: "UserProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReminderEmailDelivery",
                table: "UserProfiles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "DailyDigest");

            migrationBuilder.AddColumn<string>(
                name: "UnsubscribeToken",
                table: "UserProfiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BirthdayReminderDisabled",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BirthdayReminderLeadDays",
                table: "People",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StayInTouchCadenceDays",
                table: "People",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomIntervalMonths = table.Column<int>(type: "int", nullable: true),
                    SnoozedUntilDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastDeliveredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reminders_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UnsubscribeToken",
                table: "UserProfiles",
                column: "UnsubscribeToken");

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_OwnerId_IsCompleted_DueDate",
                table: "Reminders",
                columns: new[] { "OwnerId", "IsCompleted", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_OwnerId_PersonId",
                table: "Reminders",
                columns: new[] { "OwnerId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_PersonId",
                table: "Reminders",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reminders");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_UnsubscribeToken",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "BirthdayRemindersEnabled",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultBirthdayLeadDays",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReminderEmailDelivery",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "UnsubscribeToken",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "BirthdayReminderDisabled",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BirthdayReminderLeadDays",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StayInTouchCadenceDays",
                table: "People");
        }
    }
}
