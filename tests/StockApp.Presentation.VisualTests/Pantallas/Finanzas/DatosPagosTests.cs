using StockApp.Domain.Enums;
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de pagos escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla por un
/// texto distinto y el diagnóstico no lo dice. Cada fábrica tiene su aserción propia.</summary>
public class DatosPagosTests
{
    /// <summary>La fecha como la pinta la celda: por el converter de la vista, con el DateTime de fecha calendario tal cual.</summary>
    private static string Texto(DateTime fecha) =>
        (string)FechaCortaConverter.Instance.Convert(fecha, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)!;

    private static PagosGastoViewModel Vm(ShellMainViewModel shell) => (PagosGastoViewModel)shell.CurrentContent!;

    [Fact]
    public async Task ConPagos_ElGastoDeLuminarias_TresPagosYElResumenDelMockup()
    {
        var vm = Vm(DatosPagos.ConPagos());

        await vm.InicializarAsync();

        Assert.Equal("Luminarias LED plaza Artigas — factura A-0001190 (Electro Sur S.R.L.)", vm.TituloGasto);
        Assert.Equal(new[] { 61, 62, 63 }, vm.Pagos.Select(p => p.Id));
        Assert.Equal("$ 310.000,00", FormatoEsUy.Moneda(vm.MontoTotal));
        Assert.Equal("$ 150.000,00", FormatoEsUy.Moneda(vm.TotalPagado));
        Assert.Equal("$ 160.000,00", vm.TextoSaldo);
        Assert.Equal("Parcial", vm.Estado);
        Assert.True(vm.PuedeRegistrar);
        Assert.False(vm.EsAnulado);
        Assert.Equal(new DateTime(2026, 10, 17), vm.FechaSeleccionada);
        Assert.Null(vm.PagoSeleccionado);
    }

    [Fact]
    public async Task ConPagos_LosTextosDeLaGrilla_SinCorrimientoDeDia()
    {
        var vm = Vm(DatosPagos.ConPagos());

        await vm.InicializarAsync();

        var textos = vm.Pagos.Select(p => (Texto(p.Fecha), FormatoEsUy.Moneda(p.Monto), p.Nota, p.Activo)).ToArray();
        Assert.Equal(new[]
        {
            ("12/10/2026", "$ 100.000,00", (string?)"Recibo 2231", true),
            ("13/10/2026", "$ 50.000,00", "Cargado dos veces", false),
            ("14/10/2026", "$ 50.000,00", "Recibo 2240", true),
        }, textos);
    }

    [Fact]
    public async Task ConPagos_CajaYFuenteDeCadaPago_LasDelMockup_YElComboSoloOfreceLosLocales()
    {
        var vm = Vm(DatosPagos.ConPagos());

        await vm.InicializarAsync();

        Assert.All(vm.Pagos, p => Assert.Equal(("Local", "Rentas Generales"), (p.Caja.ToString(), p.FuenteFinanciamiento!.Nombre)));
        Assert.Equal(new[] { "Rentas Generales", "Tributos" }, vm.FuentesDisponibles.Select(f => f.Nombre));
        Assert.Null(vm.FuenteSeleccionada);   // el mockup muestra el combo con "Elegí la fuente"
        Assert.Null(vm.AvisoSinLiterales);
    }

    [Fact]
    public async Task ConPagos_ElAdjuntoDelPago61Y_ElDelPago62_SonLosDelMockup()
    {
        var vm = Vm(DatosPagos.ConPagos());
        await vm.InicializarAsync();

        vm.PagoSeleccionado = vm.Pagos.Single(p => p.Id == 61);
        await vm.AdjuntosPanel.InicializarAsync(null, 61);
        var recibo = Assert.Single(vm.AdjuntosPanel.Items);
        Assert.Equal("recibo-2231.pdf", recibo.NombreArchivo);
        Assert.Equal(96_300, recibo.TamanoBytes);
        Assert.False(vm.AdjuntosPanel.SoloLectura);

        vm.PagoSeleccionado = vm.Pagos.Single(p => p.Id == 62);
        await vm.AdjuntosPanel.InicializarAsync(null, 62);
        var duplicado = Assert.Single(vm.AdjuntosPanel.Items);
        Assert.Equal("comprobante-duplicado.pdf", duplicado.NombreArchivo);
        Assert.Equal(61_440, duplicado.TamanoBytes);
        Assert.True(vm.AdjuntosPanel.SoloLectura);
        Assert.Equal("Pago anulado: los adjuntos son solo de consulta", vm.AdjuntosPanel.AvisoSoloLectura);
    }

    [Fact]
    public async Task SinPagos_ElGastoDeConstructoraOriental_PendienteYSinPagos()
    {
        var vm = Vm(DatosPagos.SinPagos());

        await vm.InicializarAsync();

        Assert.Empty(vm.Pagos);
        Assert.Equal("Reparación de cordón cuneta en calle Zorrilla — factura A-0002210 (Constructora Oriental S.A.)", vm.TituloGasto);
        Assert.Equal("Pendiente", vm.Estado);
        Assert.Equal("$ 420.000,00", vm.TextoSaldo);
    }

    [Fact]
    public async Task ConError_MontoConPuntoDecimal_ErrorDeCampoConElDetalleSolo()
    {
        var vm = Vm(DatosPagos.ConError());

        await vm.InicializarAsync();

        Assert.Equal("1.5", vm.MontoTexto);
        Assert.Equal("Usá coma para los decimales: 1,5", vm.ErrorDe(nameof(PagosGastoViewModel.MontoTexto)));
        Assert.Null(vm.ErrorDe(nameof(PagosGastoViewModel.FechaSeleccionada)));
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
    }

    [Fact]
    public async Task SoloLectura_OperadorSinRegistrarPagos_NoRegistraNiAnula()
    {
        var vm = Vm(DatosPagos.SoloLectura());

        await vm.InicializarAsync();

        Assert.False(vm.PuedeRegistrar);
        Assert.True(vm.SoloLectura);
        Assert.False(vm.AnularPagoCommand.CanExecute(vm.Pagos.First(p => p.Activo)));
        Assert.False(vm.AdjuntosPanel.PuedeModificar);
    }

    [Fact]
    public async Task GastoAnuladoPantalla_ElCombustibleDuplicado_SoloLecturaYSaldoConGuion()
    {
        var vm = Vm(DatosPagos.GastoAnuladoPantalla());

        await vm.InicializarAsync();

        Assert.True(vm.EsAnulado);
        Assert.True(vm.SoloLectura);
        Assert.Equal("Combustible de la flota municipal (duplicado) — factura B-0012901 (Ancap Distribuidora)", vm.TituloGasto);
        Assert.Equal("$ 92.300,00", FormatoEsUy.Moneda(vm.MontoTotal));
        Assert.Equal("$ 0,00", FormatoEsUy.Moneda(vm.TotalPagado));
        Assert.Equal("—", vm.TextoSaldo);
        Assert.Equal("Anulada", vm.Estado);
        var pago = Assert.Single(vm.Pagos);
        Assert.Equal(("10/10/2026", "Pago contado (automático)", false), (Texto(pago.Fecha), pago.Nota, pago.Activo));
    }

    [Fact]
    public async Task GastoPoaAlContadoPantalla_ElCombustibleDeLaPoa_SinRegistroYConElAviso()
    {
        var vm = Vm(DatosPagos.GastoPoaAlContadoPantalla());

        await vm.InicializarAsync();

        Assert.False(vm.EsAnulado);
        Assert.False(vm.PuedeRegistrar);
        Assert.Equal("Un gasto de la caja POA no admite pagos manuales: anulalo y cargalo de nuevo.", vm.AvisoGastoPoa);
        Assert.Equal("Combustible de la flota municipal — factura B-0012877 (Ancap Distribuidora)", vm.TituloGasto);
        Assert.Equal("$ 92.300,00", FormatoEsUy.Moneda(vm.TotalPagado));
        Assert.Equal("$ 0,00", vm.TextoSaldo);
        Assert.Equal("Pagada", vm.Estado);
        var pago = Assert.Single(vm.Pagos);
        Assert.Equal(("08/10/2026", "Pago contado (automático)", Caja.Poa, "FIGM"), (Texto(pago.Fecha), pago.Nota, pago.Caja, pago.FuenteFinanciamiento!.Nombre));
    }

    [Fact]
    public async Task PagoAnulado_ElMismoGastoQueConPagos_ConElPagoAnuladoParaSeleccionar()
    {
        var vm = Vm(DatosPagos.PagoAnulado());

        await vm.InicializarAsync();

        Assert.Equal(3, vm.Pagos.Count);
        Assert.Single(vm.Pagos, p => !p.Activo);
        Assert.True(vm.PuedeRegistrar);
    }

    [Fact]
    public void MensajeConfirmarAnular_EsElDelMockup()
    {
        Assert.Equal("¿Confirma anular el pago de 100.000,00 del 12/10/2026?", DatosPagos.MensajeConfirmarAnular());
    }
}
