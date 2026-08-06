using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrigePagoGastoEsAutomaticoImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill correctivo de 20260806022959_AgregaPagoGastoEsAutomatico: esa migración
            // corrigió el texto de nota "Pago contado (automático)" (GastoService.AltaAsync e
            // IngresoPorFacturaService.RegistrarAsync) pero no contempló el tercer origen del pago
            // automático de contado — ImportacionRepository.ConfirmarAsync, que escribe la nota
            // "Pago contado (importación)" y armaba el PagoGasto sin EsAutomatico=true. Esta
            // migración corrige ESE texto puntual. No se afirma que estos dos sean los únicos
            // orígenes posibles: la garantía real contra un cuarto origen la da ahora
            // PagoGasto.Automatico (factory único de construcción), no un match de texto acá.
            migrationBuilder.Sql(
                "UPDATE \"PagosGasto\" SET \"EsAutomatico\" = TRUE " +
                "WHERE \"Nota\" = 'Pago contado (importación)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"PagosGasto\" SET \"EsAutomatico\" = FALSE " +
                "WHERE \"Nota\" = 'Pago contado (importación)';");
        }
    }
}
