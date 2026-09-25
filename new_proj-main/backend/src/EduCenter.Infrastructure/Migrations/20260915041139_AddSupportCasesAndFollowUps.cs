using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportCasesAndFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "leads",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "crm_tasks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "crm_tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "support_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SignalCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProposedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SupportTeacherId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_cases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_LeadId_ReasonCode",
                table: "crm_tasks",
                columns: new[] { "LeadId", "ReasonCode" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_StudentId_ReasonCode",
                table: "crm_tasks",
                columns: new[] { "StudentId", "ReasonCode" });

            migrationBuilder.CreateIndex(
                name: "IX_support_cases_OrganizationId_Status",
                table: "support_cases",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_support_cases_StudentId_GroupId_SignalCode",
                table: "support_cases",
                columns: new[] { "StudentId", "GroupId", "SignalCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_cases");

            migrationBuilder.DropIndex(
                name: "IX_crm_tasks_LeadId_ReasonCode",
                table: "crm_tasks");

            migrationBuilder.DropIndex(
                name: "IX_crm_tasks_StudentId_ReasonCode",
                table: "crm_tasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "crm_tasks");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "crm_tasks");
        }
    }
}
