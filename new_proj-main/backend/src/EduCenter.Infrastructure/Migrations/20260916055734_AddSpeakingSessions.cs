using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "speaking_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    SpokenSeconds = table.Column<int>(type: "integer", nullable: false),
                    EstimatedBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    FluencyBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    LexicalBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    GrammarBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    AssessmentJson = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_speaking_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "speaking_turns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Question = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AudioKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Transcript = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SpokenSeconds = table.Column<int>(type: "integer", nullable: false),
                    WordsPerMinute = table.Column<int>(type: "integer", nullable: true),
                    LongPauses = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_speaking_turns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_speaking_sessions_UserId_CreatedAt",
                table: "speaking_sessions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_speaking_turns_SessionId_Ordinal",
                table: "speaking_turns",
                columns: new[] { "SessionId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "speaking_sessions");

            migrationBuilder.DropTable(
                name: "speaking_turns");
        }
    }
}
