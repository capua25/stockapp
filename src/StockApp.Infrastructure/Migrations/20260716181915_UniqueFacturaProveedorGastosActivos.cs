using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueFacturaProveedorGastosActivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos",
                columns: new[] { "ProveedorId", "NumeroFactura" },
                unique: true,
                filter: "\"Activo\" = TRUE AND \"NumeroFactura\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos",
                columns: new[] { "ProveedorId", "NumeroFactura" });
        }
    }
}
