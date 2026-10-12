using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonLinks", x => x.Id);
                    table.CheckConstraint("CK_PersonLinks_DistinctPeople", "[PersonId] <> [RelatedPersonId]");
                    table.CheckConstraint("CK_PersonLinks_Kind", "[Kind] IN (N'Partner', N'FormerPartner', N'Parent', N'Child', N'Sibling', N'Grandparent', N'Grandchild', N'Relative', N'Friend', N'Colleague', N'Manager', N'DirectReport', N'Neighbour', N'Other')");
                    table.ForeignKey(
                        name: "FK_PersonLinks_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PersonLinks_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonLinks_People_RelatedPersonId",
                        column: x => x.RelatedPersonId,
                        principalTable: "People",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonLinks_OwnerId_PersonId_RelatedPersonId",
                table: "PersonLinks",
                columns: new[] { "OwnerId", "PersonId", "RelatedPersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonLinks_OwnerId_RelatedPersonId",
                table: "PersonLinks",
                columns: new[] { "OwnerId", "RelatedPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonLinks_PersonId",
                table: "PersonLinks",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonLinks_RelatedPersonId",
                table: "PersonLinks",
                column: "RelatedPersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonLinks");
        }
    }
}
