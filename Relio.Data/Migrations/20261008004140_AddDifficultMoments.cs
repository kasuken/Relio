using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDifficultMoments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DifficultMoments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 51024, nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(max)", maxLength: 51024, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(max)", maxLength: 51024, nullable: true),
                    LessonsLearned = table.Column<string>(type: "nvarchar(max)", maxLength: 51024, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolvedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    RecurrenceOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SensitiveDataProtectionVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DifficultMoments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DifficultMoments_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DifficultMoments_DifficultMoments_RecurrenceOfId",
                        column: x => x.RecurrenceOfId,
                        principalTable: "DifficultMoments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DifficultMoments_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultMoments_OwnerId_PersonId_CreatedAtUtc_Id",
                table: "DifficultMoments",
                columns: new[] { "OwnerId", "PersonId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultMoments_OwnerId_PersonId_OccurredOn",
                table: "DifficultMoments",
                columns: new[] { "OwnerId", "PersonId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultMoments_OwnerId_RecurrenceOfId",
                table: "DifficultMoments",
                columns: new[] { "OwnerId", "RecurrenceOfId" });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultMoments_OwnerId_Status_OccurredOn",
                table: "DifficultMoments",
                columns: new[] { "OwnerId", "Status", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_DifficultMoments_PersonId",
                table: "DifficultMoments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_DifficultMoments_RecurrenceOfId",
                table: "DifficultMoments",
                column: "RecurrenceOfId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DifficultMoments");
        }
    }
}
