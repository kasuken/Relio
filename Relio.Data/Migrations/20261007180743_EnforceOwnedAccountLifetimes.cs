using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOwnedAccountLifetimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_ContactMethods_AspNetUsers_OwnerId",
                table: "ContactMethods",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InteractionParticipants_AspNetUsers_OwnerId",
                table: "InteractionParticipants",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Interactions_AspNetUsers_OwnerId",
                table: "Interactions",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_AspNetUsers_OwnerId",
                table: "Notes",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_People_AspNetUsers_OwnerId",
                table: "People",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductActivities_AspNetUsers_OwnerId",
                table: "ProductActivities",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RelationshipTypes_AspNetUsers_OwnerId",
                table: "RelationshipTypes",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Reminders_AspNetUsers_OwnerId",
                table: "Reminders",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_AspNetUsers_OwnerId",
                table: "Tags",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_AspNetUsers_OwnerId",
                table: "UserProfiles",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContactMethods_AspNetUsers_OwnerId",
                table: "ContactMethods");

            migrationBuilder.DropForeignKey(
                name: "FK_InteractionParticipants_AspNetUsers_OwnerId",
                table: "InteractionParticipants");

            migrationBuilder.DropForeignKey(
                name: "FK_Interactions_AspNetUsers_OwnerId",
                table: "Interactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Notes_AspNetUsers_OwnerId",
                table: "Notes");

            migrationBuilder.DropForeignKey(
                name: "FK_People_AspNetUsers_OwnerId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductActivities_AspNetUsers_OwnerId",
                table: "ProductActivities");

            migrationBuilder.DropForeignKey(
                name: "FK_RelationshipTypes_AspNetUsers_OwnerId",
                table: "RelationshipTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Reminders_AspNetUsers_OwnerId",
                table: "Reminders");

            migrationBuilder.DropForeignKey(
                name: "FK_Tags_AspNetUsers_OwnerId",
                table: "Tags");

            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_AspNetUsers_OwnerId",
                table: "UserProfiles");
        }
    }
}
