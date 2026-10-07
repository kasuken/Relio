using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedValue = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactMethods", x => x.Id);
                    table.CheckConstraint("CK_ContactMethods_Kind", "[Kind] IN (N'Email', N'Phone', N'Address', N'Social', N'Other')");
                    table.ForeignKey(
                        name: "FK_ContactMethods_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactMethods_OwnerId_NormalizedValue",
                table: "ContactMethods",
                columns: new[] { "OwnerId", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactMethods_OwnerId_PersonId",
                table: "ContactMethods",
                columns: new[] { "OwnerId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactMethods_PersonId",
                table: "ContactMethods",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactMethods");
        }
    }
}
