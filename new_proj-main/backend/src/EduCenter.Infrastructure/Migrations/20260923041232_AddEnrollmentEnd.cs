using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndReason",
                table: "enrollments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndedAt",
                table: "enrollments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "enrollments");
        }
    }
}
