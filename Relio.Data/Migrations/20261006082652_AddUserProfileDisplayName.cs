using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "UserProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "UserProfiles");
        }
    }
}
