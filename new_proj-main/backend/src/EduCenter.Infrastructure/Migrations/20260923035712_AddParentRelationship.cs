using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParentRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows (seeded before this column existed) get "Guardian" rather than an empty string —
            // an empty string is not a valid ParentRelationship name and would fail to deserialize on the next read.
            migrationBuilder.AddColumn<string>(
                name: "Relationship",
                table: "student_parents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Guardian");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Relationship",
                table: "student_parents");
        }
    }
}
