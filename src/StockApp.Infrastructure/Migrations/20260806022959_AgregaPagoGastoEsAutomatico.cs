using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaPagoGastoEsAutomatico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsAutomatico",
                table: "PagosGasto",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: todo pago preexistente creado por la regla "Contado ⇒ pago automático"
            // (GastoService.AltaAsync, antes de esta migración) quedaría marcado EsAutomatico=false
            // por el defaultValue de arriba — hay que corregirlo o la anulación en cascada nueva
            // trataría esos pagos como manuales (bloqueando la anulación en vez de ofrecer la
            // confirmación) y los reportes de auditoría por EsAutomatico quedarían mal para
            // historia previa a este cambio. Match exacto de Nota: es el mismo texto literal que
            // escribe GastoService.AltaAsync y que ahora también escribe
            // IngresoPorFacturaService.RegistrarAsync — no hay otro origen posible para ese texto.
            migrationBuilder.Sql(
                "UPDATE \"PagosGasto\" SET \"EsAutomatico\" = TRUE " +
                "WHERE \"Nota\" = 'Pago contado (automático)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EsAutomatico",
                table: "PagosGasto");
        }
    }
}
