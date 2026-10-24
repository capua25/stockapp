using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuitaFuenteDelGasto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Un gasto a crédito sin pagos pierde su fuente: no hay pago del que heredarla. Lo acepta la
            // spec §2.4; en producción hay 0 casos (medido el 2026-10-23).
            migrationBuilder.DropForeignKey(
                name: "FK_Gastos_FuentesFinanciamiento_FuenteFinanciamientoId",
                table: "Gastos");

            migrationBuilder.DropIndex(
                name: "IX_Gastos_FuenteFinanciamientoId",
                table: "Gastos");

            migrationBuilder.DropColumn(
                name: "FuenteFinanciamientoId",
                table: "Gastos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La columna vuelve nullable: un gasto sin pagos no tiene de dónde recuperar su fuente.
            migrationBuilder.AddColumn<int>(
                name: "FuenteFinanciamientoId",
                table: "Gastos",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(CajaYFuenteAlPagoSql.RepoblarFuenteDelGastoDesdeLosPagos);

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_FuenteFinanciamientoId",
                table: "Gastos",
                column: "FuenteFinanciamientoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gastos_FuentesFinanciamiento_FuenteFinanciamientoId",
                table: "Gastos",
                column: "FuenteFinanciamientoId",
                principalTable: "FuentesFinanciamiento",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
