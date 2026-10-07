using StockApp.ApiClient;
using StockApp.Domain.Enums;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Los mockups de Entrada/Salida escriben estos textos a mano: si un dato cambia y el mockup no, la comparación
/// falla por un texto distinto y el diagnóstico no lo dice. Y el estado se aplica después de la carga, porque
/// InicializarAsync vacía la lista de productos y el combo suelta la selección.
/// </summary>
public class DatosMovimientosTests
{
    [Fact]
    public void Estados_TextosFormateados_SonLosDelMockup()
    {
        Assert.Equal("0", FormatoEsUy.Cantidad(DatosMovimientos.EntradaCompra.Cantidad));
        Assert.Equal("2", FormatoEsUy.Cantidad(DatosMovimientos.EntradaAjuste.Cantidad));
        Assert.Equal("40", FormatoEsUy.Cantidad(DatosMovimientos.EntradaError.Cantidad));
        Assert.Equal("480", FormatoEsUy.Cantidad(DatosMovimientos.EntradaError.Precio!.Value));
        Assert.Equal("25,5", FormatoEsUy.Cantidad(DatosMovimientos.SalidaUso.Cantidad));
        Assert.Equal("Uso o consumo", DatosMovimientos.SalidaUso.Motivo.Nombre());
        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, DatosMovimientos.EntradaError.Error);
    }

    [Fact]
    public async Task Aplicar_DespuesDeLaCarga_ConservaElProductoElegido()
    {
        var vm = DatosMovimientos.Vm(salida: true);
        await vm.InicializarAsync();

        DatosMovimientos.Aplicar(vm, DatosMovimientos.SalidaUso);

        Assert.Equal(11, vm.Productos.Count);
        Assert.Equal("Cable 2x1,5 mm", vm.ProductoSeleccionado!.Nombre);
        Assert.Equal(MotivoMovimiento.UsoOConsumo, vm.Motivo);
        Assert.Equal(25.5m, vm.Cantidad);
    }

    [Fact]
    public void Aplicar_SinLaCarga_FallaEnVezDeDejarElComboVacio()
    {
        var vm = DatosMovimientos.Vm(salida: false);

        var ex = Assert.Throws<InvalidOperationException>(() => DatosMovimientos.Aplicar(vm, DatosMovimientos.EntradaAjuste));
        Assert.Contains("no corrió", ex.Message);
    }

    [Fact]
    public async Task Aplicar_Entrada_AplicaMotivoPrecioComentarioYError()
    {
        var vm = DatosMovimientos.Vm(salida: false);
        await vm.InicializarAsync();

        DatosMovimientos.Aplicar(vm, DatosMovimientos.EntradaError);

        Assert.Equal(MotivoMovimiento.Compra, vm.Motivo);
        Assert.Equal("Cemento portland 50 kg", vm.ProductoSeleccionado!.Nombre);
        Assert.Equal(40m, vm.Cantidad);
        Assert.Equal(480m, vm.PrecioUnitario);
        Assert.Equal("Remito 0042-00001234", vm.Comentario);
        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, vm.MensajeError);

        DatosMovimientos.Aplicar(vm, DatosMovimientos.EntradaAjuste);

        Assert.Equal(MotivoMovimiento.Ajuste, vm.Motivo);
    }
}
