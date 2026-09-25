using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingTeacherReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverriddenAt",
                table: "speaking_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverriddenBy",
                table: "speaking_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherBand",
                table: "speaking_sessions",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeacherComment",
                table: "speaking_sessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherFluencyBand",
                table: "speaking_sessions",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherGrammarBand",
                table: "speaking_sessions",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherLexicalBand",
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
                name: "OverriddenAt",
                table: "speaking_sessions");

            migrationBuilder.DropColumn(
                name: "OverriddenBy",
                table: "speaking_sessions");

            migrationBuilder.DropColumn(
                name: "TeacherBand",
                table: "speaking_sessions");

            migrationBuilder.DropColumn(
                name: "TeacherComment",
                table: "speaking_sessions");

            migrationBuilder.DropColumn(
                name: "TeacherFluencyBand",
                table: "speaking_sessions");

            migrationBuilder.DropColumn(
                name: "TeacherGrammarBand",
                table: "speaking_sessions");

            migrationBuilder.DropColumn(
                name: "TeacherLexicalBand",
                table: "speaking_sessions");
        }
    }
}
