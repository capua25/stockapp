using StockApp.Domain.Formato;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Los mockups de Ingreso por factura escriben estos textos a mano: si un dato cambia y el mockup no, la comparación
/// falla por un texto distinto y el diagnóstico no lo dice. El estado se aplica después de la carga de los combos.
/// </summary>
public class DatosIngresoFacturaTests
{
    [Fact]
    public async Task ConRenglones_SumaYDiferencia_SonLasDelMockup()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConRenglones);

        Assert.Equal(6, vm.Renglones.Count);
        Assert.Equal("$ 44.940,00", FormatoEsUy.Moneda(vm.SumaRenglones));
        Assert.Equal("$ 0,00", FormatoEsUy.Moneda(vm.DiferenciaConTotal));
        Assert.Equal(DatosIngresoFactura.ProductoNuevoNombre, vm.Renglones[5].NombreMostrado);
        Assert.True(vm.Renglones[5].EsProductoNuevo);
        Assert.True(vm.Renglones[0].ActualizarPrecioCosto);
    }

    [Fact]
    public async Task ConDiferencia_DiferenciaCreditoYErrorDeCarga()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConDiferencia);

        Assert.Equal("$ 60,00", FormatoEsUy.Moneda(vm.DiferenciaConTotal));
        Assert.True(vm.EsCredito);
        Assert.Equal(DatosIngresoFactura.Vencimiento, vm.FechaVencimientoSeleccionada);
        Assert.Equal("La cantidad debe ser mayor a cero.", vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task ConDiferencia_ElErrorDeCargaEsElQueDaElVmConCantidadCero()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();
        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        vm.CantidadEnCarga = 0m;
        vm.PrecioUnitarioEnCarga = 480m;

        vm.AgregarArticuloCommand.Execute(null);

        Assert.Equal(DatosIngresoFactura.ConDiferencia.ErrorCarga, vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task ConDiferencia_LaFilaDeCargaQuedaVaciaConElError()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConDiferencia);

        Assert.Null(vm.ProductoEnCarga);
        Assert.False(vm.EsProductoNuevoEnCarga);
        Assert.Equal(0m, vm.CantidadEnCarga);
        Assert.Equal(0m, vm.PrecioUnitarioEnCarga);
        Assert.False(vm.ActualizarPrecioCostoEnCarga);
        Assert.Equal(DatosIngresoFactura.ConDiferencia.ErrorCarga, vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task Guardado_QuedaGuardadoSinPedirConfirmacionDePrecios()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.Guardado);

        Assert.True(vm.GuardadoExitoso);
        Assert.Equal(4521, vm.GastoIdCreado);
        Assert.Single(vm.AdjuntosPanel.Items);
    }

    [Fact]
    public async Task Vacio_SoloLaFechaFija()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.Vacio);

        Assert.Empty(vm.Renglones);
        Assert.Null(vm.ProveedorSeleccionado);
        Assert.Equal(DatosIngresoFactura.Fecha, vm.FechaSeleccionada);
    }

    [Fact]
    public void Aplicar_SinLaCarga_FallaEnVezDeDejarLosCombosVacios()
    {
        var vm = DatosIngresoFactura.Vm();

        Assert.Throws<InvalidOperationException>(() => DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConRenglones));
    }
}
