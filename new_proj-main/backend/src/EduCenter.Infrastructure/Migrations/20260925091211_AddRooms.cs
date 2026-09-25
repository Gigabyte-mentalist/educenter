using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "lessons",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    Equipment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lessons_RoomId_StartsAt",
                table: "lessons",
                columns: new[] { "RoomId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_rooms_BranchId_Name",
                table: "rooms",
                columns: new[] { "BranchId", "Name" },
                unique: true);

            // Existing lessons only ever named their room as text. Turn each branch's distinct names into rooms (case-insensitively — "A-12"
            // and "a-12" were one room to everybody but the database) and tie the lessons to them. RoomName stays on the lesson as it was.
            migrationBuilder.Sql("""
                INSERT INTO rooms ("Id", "Name", "Capacity", "Equipment", "IsActive", "CreatedAt", "OrganizationId", "BranchId")
                SELECT DISTINCT ON ("BranchId", lower(btrim("RoomName")))
                       gen_random_uuid(), btrim("RoomName"), NULL, NULL, TRUE, now(), "OrganizationId", "BranchId"
                FROM lessons
                WHERE btrim("RoomName") <> ''
                ORDER BY "BranchId", lower(btrim("RoomName")), "RoomName";
                """);
            migrationBuilder.Sql("""
                UPDATE lessons AS l SET "RoomId" = r."Id"
                FROM rooms AS r
                WHERE r."BranchId" = l."BranchId" AND lower(r."Name") = lower(btrim(l."RoomName"));
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_lessons_rooms_RoomId",
                table: "lessons",
                column: "RoomId",
                principalTable: "rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lessons_rooms_RoomId",
                table: "lessons");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_lessons_RoomId_StartsAt",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "lessons");
        }
    }
}
