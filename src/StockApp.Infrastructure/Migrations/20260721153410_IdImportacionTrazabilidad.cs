using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdImportacionTrazabilidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdImportacion",
                table: "LineasPoa",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdImportacion",
                table: "IngresosCaja",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdImportacion",
                table: "Gastos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineasPoa_IdImportacion",
                table: "LineasPoa",
                column: "IdImportacion");

            migrationBuilder.CreateIndex(
                name: "IX_IngresosCaja_IdImportacion",
                table: "IngresosCaja",
                column: "IdImportacion");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_IdImportacion",
                table: "Gastos",
                column: "IdImportacion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LineasPoa_IdImportacion",
                table: "LineasPoa");

            migrationBuilder.DropIndex(
                name: "IX_IngresosCaja_IdImportacion",
                table: "IngresosCaja");

            migrationBuilder.DropIndex(
                name: "IX_Gastos_IdImportacion",
                table: "Gastos");

            migrationBuilder.DropColumn(
                name: "IdImportacion",
                table: "LineasPoa");

            migrationBuilder.DropColumn(
                name: "IdImportacion",
                table: "IngresosCaja");

            migrationBuilder.DropColumn(
                name: "IdImportacion",
                table: "Gastos");
        }
    }
}
