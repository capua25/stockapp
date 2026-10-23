using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaCajaYFuenteAlPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Caja",
                table: "FuentesFinanciamiento",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Caja",
                table: "IngresosCaja",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Caja",
                table: "PagosGasto",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(CajaYFuenteAlPagoSql.ClasificarFuentesPoa);

            migrationBuilder.AddColumn<int>(
                name: "FuenteFinanciamientoId",
                table: "PagosGasto",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(CajaYFuenteAlPagoSql.CopiarFuenteDelGastoALosPagos);

            migrationBuilder.AlterColumn<int>(
                name: "FuenteFinanciamientoId",
                table: "PagosGasto",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagosGasto_FuenteFinanciamientoId",
                table: "PagosGasto",
                column: "FuenteFinanciamientoId");

            migrationBuilder.AddForeignKey(
                name: "FK_PagosGasto_FuentesFinanciamiento_FuenteFinanciamientoId",
                table: "PagosGasto",
                column: "FuenteFinanciamientoId",
                principalTable: "FuentesFinanciamiento",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PagosGasto_FuentesFinanciamiento_FuenteFinanciamientoId",
                table: "PagosGasto");

            migrationBuilder.DropIndex(
                name: "IX_PagosGasto_FuenteFinanciamientoId",
                table: "PagosGasto");

            migrationBuilder.DropColumn(
                name: "Caja",
                table: "PagosGasto");

            migrationBuilder.DropColumn(
                name: "FuenteFinanciamientoId",
                table: "PagosGasto");

            migrationBuilder.DropColumn(
                name: "Caja",
                table: "IngresosCaja");

            migrationBuilder.DropColumn(
                name: "Caja",
                table: "FuentesFinanciamiento");
        }
    }
}
