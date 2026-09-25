using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "writing_evaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeworkId = table.Column<Guid>(type: "uuid", nullable: true),
                    HomeworkSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TaskPrompt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Essay = table.Column<string>(type: "character varying(9000)", maxLength: 9000, nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AiRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstimatedBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    TaskResponseBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    CoherenceBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    LexicalBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    GrammarBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    FeedbackJson = table.Column<string>(type: "text", nullable: true),
                    TeacherBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    TeacherTaskResponseBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    TeacherCoherenceBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    TeacherLexicalBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    TeacherGrammarBand = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    TeacherComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OverriddenBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OverriddenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_writing_evaluations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_writing_evaluations_HomeworkId",
                table: "writing_evaluations",
                column: "HomeworkId");

            migrationBuilder.CreateIndex(
                name: "IX_writing_evaluations_HomeworkSubmissionId",
                table: "writing_evaluations",
                column: "HomeworkSubmissionId",
                unique: true,
                filter: "\"HomeworkSubmissionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_writing_evaluations_RequestedBy_CreatedAt",
                table: "writing_evaluations",
                columns: new[] { "RequestedBy", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_writing_evaluations_StudentId_CreatedAt",
                table: "writing_evaluations",
                columns: new[] { "StudentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "writing_evaluations");
        }
    }
}
