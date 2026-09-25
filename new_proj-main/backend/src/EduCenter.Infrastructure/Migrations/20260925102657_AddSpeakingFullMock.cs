using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingFullMock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Part",
                table: "speaking_turns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RelevanceBand",
                table: "speaking_sessions",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Part",
                table: "speaking_turns");

            migrationBuilder.DropColumn(
                name: "RelevanceBand",
                table: "speaking_sessions");
        }
    }
}
