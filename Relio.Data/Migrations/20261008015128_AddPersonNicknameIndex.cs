using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonNicknameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_People_OwnerId_IsArchived_Nickname",
                table: "People",
                columns: new[] { "OwnerId", "IsArchived", "Nickname" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_OwnerId_IsArchived_Nickname",
                table: "People");
        }
    }
}
