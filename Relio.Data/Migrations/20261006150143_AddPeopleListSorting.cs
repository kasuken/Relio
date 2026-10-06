using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleListSorting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_OwnerId_IsArchived",
                table: "People");

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastContactedOn",
                table: "People",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerId_IsArchived_CreatedAtUtc",
                table: "People",
                columns: new[] { "OwnerId", "IsArchived", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerId_IsArchived_FirstName_LastName",
                table: "People",
                columns: new[] { "OwnerId", "IsArchived", "FirstName", "LastName" });

            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerId_IsArchived_LastContactedOn",
                table: "People",
                columns: new[] { "OwnerId", "IsArchived", "LastContactedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_OwnerId_IsArchived_CreatedAtUtc",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_OwnerId_IsArchived_FirstName_LastName",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_OwnerId_IsArchived_LastContactedOn",
                table: "People");

            migrationBuilder.DropColumn(
                name: "LastContactedOn",
                table: "People");

            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerId_IsArchived",
                table: "People",
                columns: new[] { "OwnerId", "IsArchived" });
        }
    }
}
