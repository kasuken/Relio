using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractionsNotesAndTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Interactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interactions", x => x.Id);
                    table.CheckConstraint("CK_Interactions_Kind", "[Kind] IN (N'Call', N'Meeting', N'Message', N'Event', N'Other')");
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notes_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InteractionParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InteractionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractionParticipants_Interactions_InteractionId",
                        column: x => x.InteractionId,
                        principalTable: "Interactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InteractionParticipants_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionParticipants_InteractionId_PersonId",
                table: "InteractionParticipants",
                columns: new[] { "InteractionId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractionParticipants_OwnerId_PersonId_InteractionId",
                table: "InteractionParticipants",
                columns: new[] { "OwnerId", "PersonId", "InteractionId" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionParticipants_PersonId",
                table: "InteractionParticipants",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_OwnerId_OccurredOn_CreatedAtUtc_Id",
                table: "Interactions",
                columns: new[] { "OwnerId", "OccurredOn", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_OwnerId_OccurredOn_Id",
                table: "Interactions",
                columns: new[] { "OwnerId", "OccurredOn", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_OwnerId_PersonId_CreatedAtUtc_Id",
                table: "Notes",
                columns: new[] { "OwnerId", "PersonId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_OwnerId_PersonId_IsPinned",
                table: "Notes",
                columns: new[] { "OwnerId", "PersonId", "IsPinned" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_PersonId",
                table: "Notes",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteractionParticipants");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "Interactions");
        }
    }
}
