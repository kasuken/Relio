using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedBillingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedBillingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PlanRenewsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlanCancelsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastBillingEventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BillingProviderCustomerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingProviderSubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                    table.CheckConstraint("CK_UserSubscriptions_Tier", "[Tier] IN (N'Free', N'Pro')");
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedBillingEvents_ProviderEventId",
                table: "ProcessedBillingEvents",
                column: "ProviderEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_BillingProviderCustomerId",
                table: "UserSubscriptions",
                column: "BillingProviderCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_BillingProviderSubscriptionId",
                table: "UserSubscriptions",
                column: "BillingProviderSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                table: "UserSubscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedBillingEvents");

            migrationBuilder.DropTable(
                name: "UserSubscriptions");
        }
    }
}
