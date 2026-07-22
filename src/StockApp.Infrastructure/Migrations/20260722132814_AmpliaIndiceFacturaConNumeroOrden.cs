using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AmpliaIndiceFacturaConNumeroOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden",
                table: "Gastos",
                columns: new[] { "ProveedorId", "NumeroFactura", "NumeroOrden" },
                unique: true,
                filter: "\"Activo\" = TRUE AND \"NumeroFactura\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden",
                table: "Gastos");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos",
                columns: new[] { "ProveedorId", "NumeroFactura" },
                unique: true,
                filter: "\"Activo\" = TRUE AND \"NumeroFactura\" IS NOT NULL");
        }
    }
}
