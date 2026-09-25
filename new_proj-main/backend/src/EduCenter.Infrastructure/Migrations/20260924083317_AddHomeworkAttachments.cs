using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeworkAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "homework_submissions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentKey",
                table: "homework_submissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                table: "homework_submissions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSize",
                table: "homework_submissions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "homework",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentKey",
                table: "homework",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                table: "homework",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSize",
                table: "homework",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "homework_submissions");

            migrationBuilder.DropColumn(
                name: "AttachmentKey",
                table: "homework_submissions");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                table: "homework_submissions");

            migrationBuilder.DropColumn(
                name: "AttachmentSize",
                table: "homework_submissions");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "homework");

            migrationBuilder.DropColumn(
                name: "AttachmentKey",
                table: "homework");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                table: "homework");

            migrationBuilder.DropColumn(
                name: "AttachmentSize",
                table: "homework");
        }
    }
}
