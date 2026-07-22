using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PagoGastoIdImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdImportacion",
                table: "PagosGasto",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagosGasto_IdImportacion",
                table: "PagosGasto",
                column: "IdImportacion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PagosGasto_IdImportacion",
                table: "PagosGasto");

            migrationBuilder.DropColumn(
                name: "IdImportacion",
                table: "PagosGasto");
        }
    }
}
