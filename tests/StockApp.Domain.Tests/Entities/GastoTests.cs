using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class GastoTests
{
    private static readonly DateTime Hoy = new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto NuevoGasto() => new()
    {
        Id = 1,
        ProveedorId = 1,
        Detalle = "Compra de insumos",
        Fecha = Hoy,
        MontoTotal = 1000m,
        FuenteFinanciamientoId = 1,
        RubroGastoId = 1,
        CondicionPago = CondicionPago.Contado,
    };

    // ── PagosAutomaticosADarDeBajaEnAnulacion (guard unificado de anulación en cascada) ──

    [Fact]
    public void PagosAutomaticosADarDeBajaEnAnulacion_SinPagosActivos_DevuelveVacio()
    {
        var gasto = NuevoGasto();
        var pagoInactivo = PagoGasto.Automatico(Hoy, 1000m, "Pago contado (automático)");
        pagoInactivo.Id = 1;
        pagoInactivo.Activo = false;
        gasto.Pagos.Add(pagoInactivo);

        var resultado = gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico: false);

        Assert.Empty(resultado);
    }

    [Fact]
    public void PagosAutomaticosADarDeBajaEnAnulacion_ConPagoManualActivo_LanzaReglaDeNegocioSinImportarConfirmacion()
    {
        var gasto = NuevoGasto();
        gasto.Pagos.Add(new PagoGasto { Id = 1, Monto = 300m, Activo = true });

        Assert.Throws<ReglaDeNegocioException>(
            () => gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico: false));
        Assert.Throws<ReglaDeNegocioException>(
            () => gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico: true));
    }

    [Fact]
    public void PagosAutomaticosADarDeBajaEnAnulacion_ConPagoAutomaticoActivoSinConfirmar_LanzaExcepcionEstructuradaConGastoIdYMonto()
    {
        // Excepción específica (no ReglaDeNegocioException genérica) — mismo patrón que
        // StockInsuficienteException: lleva datos estructurados para que la capa HTTP los
        // exponga como extensión del problem+json y el cliente distinga este 409 de uno
        // genérico sin tener que parsear el texto del mensaje.
        var gasto = NuevoGasto();
        var pago = PagoGasto.Automatico(Hoy, 1000m, "Pago contado (automático)");
        pago.Id = 1;
        gasto.Pagos.Add(pago);

        var ex = Assert.Throws<AnulacionRequierePagoAutomaticoConfirmadoException>(
            () => gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico: false));

        Assert.Contains("pago automático", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, ex.GastoId);
        Assert.Equal(1000m, ex.MontoPagoAutomatico);
    }

    [Fact]
    public void PagosAutomaticosADarDeBajaEnAnulacion_ConPagoAutomaticoActivoConfirmando_DevuelveElPagoAutomatico()
    {
        var gasto = NuevoGasto();
        var pago = PagoGasto.Automatico(Hoy, 1000m, "Pago contado (automático)");
        pago.Id = 1;
        gasto.Pagos.Add(pago);

        var resultado = gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico: true);

        var unico = Assert.Single(resultado);
        Assert.Same(pago, unico);
    }

    [Fact]
    public void PagosAutomaticosADarDeBajaEnAnulacion_AutomaticoYManualActivos_LanzaReglaDeNegocioAunConfirmando()
    {
        // Protege plata real: si además del pago automático hay un pago manual activo, la
        // confirmación de "anular el pago automático" NO alcanza para destrabar la anulación.
        var gasto = NuevoGasto();
        var pagoAutomatico = PagoGasto.Automatico(Hoy, 1000m, "Pago contado (automático)");
        pagoAutomatico.Id = 1;
        gasto.Pagos.Add(pagoAutomatico);
        gasto.Pagos.Add(new PagoGasto { Id = 2, Monto = 200m, Activo = true });

        Assert.Throws<ReglaDeNegocioException>(
            () => gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico: true));
    }
}
