using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaFksLotesImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_Gastos_LotesImportacion_IdImportacion",
                table: "Gastos",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IngresosCaja_LotesImportacion_IdImportacion",
                table: "IngresosCaja",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LineasPoa_LotesImportacion_IdImportacion",
                table: "LineasPoa",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PagosGasto_LotesImportacion_IdImportacion",
                table: "PagosGasto",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gastos_LotesImportacion_IdImportacion",
                table: "Gastos");

            migrationBuilder.DropForeignKey(
                name: "FK_IngresosCaja_LotesImportacion_IdImportacion",
                table: "IngresosCaja");

            migrationBuilder.DropForeignKey(
                name: "FK_LineasPoa_LotesImportacion_IdImportacion",
                table: "LineasPoa");

            migrationBuilder.DropForeignKey(
                name: "FK_PagosGasto_LotesImportacion_IdImportacion",
                table: "PagosGasto");
        }
    }
}
