using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceInstallments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_EnrollmentId_PeriodStart",
                table: "invoices");

            migrationBuilder.AddColumn<int>(
                name: "InstallmentCount",
                table: "invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentNumber",
                table: "invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentInvoiceId",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_EnrollmentId_PeriodStart",
                table: "invoices",
                columns: new[] { "EnrollmentId", "PeriodStart" },
                unique: true,
                filter: "\"ParentInvoiceId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_ParentInvoiceId",
                table: "invoices",
                column: "ParentInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_EnrollmentId_PeriodStart",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_ParentInvoiceId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InstallmentCount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InstallmentNumber",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "ParentInvoiceId",
                table: "invoices");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_EnrollmentId_PeriodStart",
                table: "invoices",
                columns: new[] { "EnrollmentId", "PeriodStart" },
                unique: true);
        }
    }
}
