using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderDeliveryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReminderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderDeliveries", x => x.Id);
                    table.CheckConstraint("CK_ReminderDeliveries_Kind", "[Kind] IN (N'Reminder', N'Birthday')");
                    table.CheckConstraint("CK_ReminderDeliveries_Status", "[Status] IN (N'Pending', N'Sent')");
                    table.ForeignKey(
                        name: "FK_ReminderDeliveries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDeliveries_ClaimedAtUtc",
                table: "ReminderDeliveries",
                column: "ClaimedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDeliveries_DeliveryKey",
                table: "ReminderDeliveries",
                columns: new[] { "UserId", "Kind", "SourceId", "OccurrenceDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderDeliveries");
        }
    }
}
