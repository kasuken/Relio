using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relio.Data.Migrations
{
    /// <summary>
    /// Issue #22: the person profile. Adds the per-user <c>RelationshipTypes</c> table, a nickname,
    /// "how we met" and details on <c>People</c>, and replaces <c>People.Birthday</c> (a
    /// <c>date</c>, which cannot be missing its year) with three nullable columns.
    /// </summary>
    /// <remarks>
    /// Scaffolded, then reordered by hand so existing data survives; the hand edits never change
    /// the model (<c>dotnet ef migrations has-pending-model-changes</c> must stay clean).
    /// <list type="bullet">
    /// <item>Existing birthdays are copied into the new columns before the old column is dropped.</item>
    /// <item>Every existing account gets the six default relationship types - this is the only place
    /// they are added for accounts that predate the table (new accounts get them at registration).
    /// The names are a literal copy of <c>RelationshipType.DefaultNames</c> on purpose: a migration
    /// must keep producing the same result when that list changes later.</item>
    /// <item>Statements that read columns added earlier in the same migration run through
    /// <c>EXEC(N'...')</c>: SQL Server compiles a whole batch before running it, and an idempotent
    /// script (<c>dotnet ef migrations script --idempotent</c>) puts a migration in one batch.</item>
    /// </list>
    /// Downgrading cannot be lossless: a birthday without a year has nowhere to live in the old
    /// <c>date</c> column, so it is dropped (and so is everything else this migration added).
    /// </remarks>
    public partial class AddPersonProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The new table.
            migrationBuilder.CreateTable(
                name: "RelationshipTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationshipTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipTypes_OwnerId_Name",
                table: "RelationshipTypes",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipTypes_OwnerId_SortOrder",
                table: "RelationshipTypes",
                columns: new[] { "OwnerId", "SortOrder" });

            // 2. The new People columns.
            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "People",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HowWeMet",
                table: "People",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "People",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelationshipTypeId",
                table: "People",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirthdayYear",
                table: "People",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirthdayMonth",
                table: "People",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirthdayDay",
                table: "People",
                type: "int",
                nullable: true);

            // 3. Keep every existing birthday: split the date into the three new columns.
            migrationBuilder.Sql(
                "EXEC(N'UPDATE [People] SET [BirthdayYear] = YEAR([Birthday]), [BirthdayMonth] = MONTH([Birthday]), [BirthdayDay] = DAY([Birthday]) WHERE [Birthday] IS NOT NULL')");

            // 4. The old column is now redundant.
            migrationBuilder.DropColumn(
                name: "Birthday",
                table: "People");

            // 5. The default relationship types for every account that already exists. Names are
            //    a deliberate literal copy of RelationshipType.DefaultNames (see the remarks).
            migrationBuilder.Sql(
                "EXEC(N'INSERT INTO [RelationshipTypes] ([Id], [OwnerId], [Name], [SortOrder], [CreatedAtUtc], [UpdatedAtUtc]) " +
                "SELECT NEWID(), u.[Id], v.[Name], v.[SortOrder], SYSUTCDATETIME(), SYSUTCDATETIME() " +
                "FROM [AspNetUsers] u CROSS JOIN (VALUES (N''Family'', 0), (N''Partner'', 1), (N''Friend'', 2), " +
                "(N''Colleague'', 3), (N''Acquaintance'', 4), (N''Other'', 5)) v([Name], [SortOrder])')");

            // 6. Constraints last, once the data is in its final shape.
            migrationBuilder.CreateIndex(
                name: "IX_People_RelationshipTypeId",
                table: "People",
                column: "RelationshipTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_People_RelationshipTypes_RelationshipTypeId",
                table: "People",
                column: "RelationshipTypeId",
                principalTable: "RelationshipTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddCheckConstraint(
                name: "CK_People_BirthdayComplete",
                table: "People",
                sql: "([BirthdayMonth] IS NULL AND [BirthdayDay] IS NULL AND [BirthdayYear] IS NULL) OR ([BirthdayMonth] IS NOT NULL AND [BirthdayDay] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_People_BirthdayDay",
                table: "People",
                sql: "[BirthdayDay] IS NULL OR [BirthdayDay] BETWEEN 1 AND 31");

            migrationBuilder.AddCheckConstraint(
                name: "CK_People_BirthdayMonth",
                table: "People",
                sql: "[BirthdayMonth] IS NULL OR [BirthdayMonth] BETWEEN 1 AND 12");

            migrationBuilder.AddCheckConstraint(
                name: "CK_People_BirthdayYear",
                table: "People",
                sql: "[BirthdayYear] IS NULL OR [BirthdayYear] BETWEEN 1 AND 9999");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_RelationshipTypes_RelationshipTypeId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_RelationshipTypeId",
                table: "People");

            migrationBuilder.DropCheckConstraint(
                name: "CK_People_BirthdayComplete",
                table: "People");

            migrationBuilder.DropCheckConstraint(
                name: "CK_People_BirthdayDay",
                table: "People");

            migrationBuilder.DropCheckConstraint(
                name: "CK_People_BirthdayMonth",
                table: "People");

            migrationBuilder.DropCheckConstraint(
                name: "CK_People_BirthdayYear",
                table: "People");

            migrationBuilder.DropTable(
                name: "RelationshipTypes");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Birthday",
                table: "People",
                type: "date",
                nullable: true);

            // Only a birthday with a year fits the old date column. Birthdays without a year are
            // lost on downgrade - there is no honest date to give them.
            migrationBuilder.Sql(
                "EXEC(N'UPDATE [People] SET [Birthday] = DATEFROMPARTS([BirthdayYear], [BirthdayMonth], [BirthdayDay]) " +
                "WHERE [BirthdayYear] IS NOT NULL AND [BirthdayMonth] IS NOT NULL AND [BirthdayDay] IS NOT NULL')");

            migrationBuilder.DropColumn(
                name: "BirthdayDay",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BirthdayMonth",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BirthdayYear",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HowWeMet",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "People");

            migrationBuilder.DropColumn(
                name: "RelationshipTypeId",
                table: "People");
        }
    }
}
