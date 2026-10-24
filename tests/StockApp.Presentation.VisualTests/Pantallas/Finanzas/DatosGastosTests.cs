using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de gastos escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla por
/// un texto distinto y el diagnóstico no lo dice. El estado se calcula con el reloj real: los vencimientos están lejos de
/// "hoy" para que no cambie con el día.</summary>
public class DatosGastosTests
{
    private static GastoFila Fila(StockApp.Domain.Entities.Gasto g) => new(g, DateTime.UtcNow);

    [Fact]
    public void Gastos_CuatroEstadosEnElListado_YAnuladaEnElOtro()
    {
        Assert.Equal(new[] { "Pendiente", "Parcial", "Pagada", "Pagada", "Vencida", "Pagada" },
            DatosGastos.Gastos.Select(g => Fila(g).Estado));
        Assert.Equal("Anulada", Fila(DatosGastos.Anulado).Estado);
        Assert.Equal(7, DatosGastos.ConAnuladoLista.Count);
    }

    [Fact]
    public void Gastos_TextosDelMockup()
    {
        var parcial = Fila(DatosGastos.Gastos[1]);
        Assert.Equal("12/10/2026", FechaCortaConverter.Formatear(parcial.Fecha));
        Assert.Equal("Electro Sur S.R.L.", parcial.ProveedorNombre);
        Assert.Equal("$ 310.000,00", FormatoEsUy.Moneda(parcial.MontoTotal));
        Assert.Equal("$ 150.000,00", FormatoEsUy.Moneda(parcial.TotalPagado));
        Assert.Equal("$ 160.000,00", FormatoEsUy.Moneda(parcial.Saldo));
        Assert.Equal("", Fila(DatosGastos.Gastos[2]).LineaPoaNombre);
        Assert.Equal("$ 0,00", FormatoEsUy.Moneda(Fila(DatosGastos.Gastos[2]).Saldo));
    }

    [Fact]
    public async Task Listado_SinFiltros_CargaLosSeis_YElPieDice6Gastos()
    {
        var vm = DatosGastos.Listado(_ => Task.FromResult(DatosGastos.Gastos));

        await vm.CargarAsync();

        Assert.Equal(6, vm.CantidadVisible);
        Assert.Equal(4, vm.ProveedoresDisponibles.Count);
        Assert.False(vm.HayFiltrosAplicados);
    }

    [Fact]
    public async Task SinResultados_ConDesdeDeNoviembre_NoHayFilas_YHayFiltros()
    {
        var shell = DatosGastos.SinResultados();
        var vm = (GastosViewModel)shell.CurrentContent!;

        await vm.CargarAsync();

        Assert.Empty(vm.Filas);
        Assert.True(vm.HayFiltrosAplicados);
    }

    [Fact]
    public void MensajeConfirmarAnular_EsElDelMockup()
    {
        Assert.Equal(
            "¿Confirma anular el gasto \"Materiales para bacheo de calle Zorrilla\" (factura A-0004521 — $ 186.540,00)? " +
            "Esta anulación va a descontar el stock asociado.",
            DatosGastos.MensajeConfirmarAnular());
    }

    [Fact]
    public void MensajeFacturaDuplicada_EsElDelMockup()
    {
        Assert.Equal(
            "La factura 'A-0004521' ya existe para ese proveedor (\"Materiales para bacheo de calle Zorrilla\"). " +
            "¿Asociar la entrada de stock a esa factura?",
            DatosGastos.MensajeFacturaDuplicada());
    }

    [Fact]
    public async Task Formulario_Editar_PrecargaElGastoYSuAdjunto()
    {
        var vm = (GastoFormViewModel)DatosGastos.Editar().CurrentContent!;

        await vm.InicializarAsync();

        Assert.Equal(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), vm.FechaSeleccionada);
        Assert.Equal("186.540,00", vm.MontoTexto);
        Assert.Equal("Barraca Carmelo S.A.", vm.ProveedorSeleccionado?.Nombre);
        Assert.False(vm.PuedeCambiarCondicionPago);
        Assert.Equal("factura-A-0004521.pdf", Assert.Single(vm.AdjuntosPanel.Items).NombreArchivo);
    }

    [Fact]
    public async Task Formulario_ConErrorDeCampo_DetalleYMontoEnRojo()
    {
        var vm = (GastoFormViewModel)DatosGastos.ConErrorDeCampo().CurrentContent!;

        await vm.InicializarAsync();

        Assert.Equal("El detalle es obligatorio.", vm.ErrorDe(nameof(GastoFormViewModel.Detalle)));
        Assert.Equal("El monto total no es un número válido. Usá coma para los decimales: 1,5",
            vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
    }

    [Fact]
    public async Task Formulario_Anulado_SoloLecturaSinAdjuntos()
    {
        var vm = (GastoFormViewModel)DatosGastos.FormularioAnulado().CurrentContent!;

        await vm.InicializarAsync();

        Assert.True(vm.SoloLectura);
        Assert.False(vm.MostrarSeccionAdjuntos);
    }

    [Fact]
    public async Task Formulario_Nuevo_AltaVaciaDeContado_ConLaFechaFija()
    {
        var vm = (GastoFormViewModel)DatosGastos.Nuevo().CurrentContent!;

        await vm.InicializarAsync();

        Assert.False(vm.EsEdicion);
        Assert.False(vm.EsCredito);
        Assert.False(vm.SoloLectura);
        Assert.True(vm.PuedeCambiarCondicionPago);
        Assert.Equal(new DateTime(2026, 10, 6), vm.FechaSeleccionada);
        Assert.True(string.IsNullOrEmpty(vm.Detalle));
        Assert.Null(vm.ProveedorSeleccionado);
    }

    [Fact]
    public async Task Formulario_EditarCredito_PrecargaElCreditoConSuVencimiento_YLaCondicionFija()
    {
        var vm = (GastoFormViewModel)DatosGastos.EditarCredito().CurrentContent!;

        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.True(vm.EsCredito);
        Assert.Equal(new DateTime(2030, 12, 31), vm.FechaVencimientoSeleccionada?.Date);
        Assert.Equal("Constructora Oriental S.A.", vm.ProveedorSeleccionado?.Nombre);
        Assert.Equal("420.000,00", vm.MontoTexto);
        Assert.Null(vm.FuenteSeleccionada);   // un crédito no tiene fuente: se elige en cada pago
        Assert.False(vm.PuedeCambiarCondicionPago);
        Assert.Empty(vm.AdjuntosPanel.Items);
    }

    [Fact]
    public async Task Formulario_ConErrorDelServidor_FormularioCompleto_YElMensajeDeLaRegla()
    {
        var vm = (GastoFormViewModel)DatosGastos.ConErrorDelServidor().CurrentContent!;

        await vm.InicializarAsync();

        Assert.Equal("El monto total del gasto debe ser mayor a cero.", vm.MensajeError);
        Assert.Equal("Ancap Distribuidora", vm.ProveedorSeleccionado?.Nombre);
        Assert.Equal("B-0012950", vm.NumeroFactura);
        Assert.Equal("0,00", vm.MontoTexto);
        Assert.Equal("Rentas generales", vm.FuenteSeleccionada?.Nombre);
        Assert.Equal("Combustibles", vm.RubroSeleccionado?.Nombre);
    }

    [Fact]
    public async Task Formulario_Credito_AltaACreditoConSuVencimiento()
    {
        var vm = (GastoFormViewModel)DatosGastos.Credito().CurrentContent!;

        await vm.InicializarAsync();

        Assert.False(vm.EsEdicion);
        Assert.True(vm.EsCredito);
        Assert.True(vm.PuedeCambiarCondicionPago);
        Assert.Equal(new DateTime(2026, 11, 5), vm.FechaVencimientoSeleccionada?.Date);
        Assert.Equal("A-0002301", vm.NumeroFactura);
        Assert.Equal("245.000,00", vm.MontoTexto);
        Assert.Equal("Constructora Oriental S.A.", vm.ProveedorSeleccionado?.Nombre);
        Assert.Equal("Préstamo BROU", vm.FuenteSeleccionada?.Nombre);
        Assert.Equal("Obras viales", vm.RubroSeleccionado?.Nombre);
    }

    [Fact]
    public async Task Formulario_SoloLectura_PorPermiso_ConElGastoCargadoYSinAdjuntosNuevos()
    {
        var vm = (GastoFormViewModel)DatosGastos.FormularioSoloLectura().CurrentContent!;

        await vm.InicializarAsync();

        Assert.True(vm.SoloLectura);
        Assert.False(vm.PuedeRegistrarGastos);
        Assert.True(vm.MostrarSeccionAdjuntos);   // sin permiso se ve; el anulado es el que la oculta
        Assert.False(vm.PuedeCambiarCondicionPago);
        Assert.Equal("A-0004521", vm.NumeroFactura);
        Assert.Equal("factura-A-0004521.pdf", Assert.Single(vm.AdjuntosPanel.Items).NombreArchivo);
    }

    [Fact]
    public async Task Listado_SoloLectura_CargaLosSeis_SinPermisoDeRegistrar()
    {
        var vm = (GastosViewModel)DatosGastos.SoloLectura().CurrentContent!;

        await vm.CargarAsync();

        Assert.Equal(6, vm.CantidadVisible);
        Assert.False(vm.PuedeRegistrarGastos);
    }

    [Fact]
    public async Task Listado_ConAnulado_CargaLosSiete_ConLaAnuladaEnSuLugar()
    {
        var vm = (GastosViewModel)DatosGastos.ConAnulado().CurrentContent!;

        await vm.CargarAsync();

        Assert.Equal(7, vm.CantidadVisible);
        Assert.Equal(8, vm.Filas[2].Id);
        Assert.Equal("Anulada", vm.Filas[2].Estado);
        Assert.Equal(1, vm.Filas.Count(f => f.Estado == "Anulada"));
    }
}
