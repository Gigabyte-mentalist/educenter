using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceFilesAndUserDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "learning_resources",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<string>(
                name: "FileContentType",
                table: "learning_resources",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "learning_resources",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "learning_resources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "learning_resources",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_learning_resources_link_or_file",
                table: "learning_resources",
                sql: "(\"Url\" IS NOT NULL) <> (\"StorageKey\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_learning_resources_link_or_file",
                table: "learning_resources");

            migrationBuilder.DropColumn(
                name: "FileContentType",
                table: "learning_resources");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "learning_resources");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "learning_resources");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "learning_resources");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "learning_resources",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
