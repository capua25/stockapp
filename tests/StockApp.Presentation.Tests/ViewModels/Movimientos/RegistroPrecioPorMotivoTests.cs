using System.Collections.Generic;
using System.ComponentModel;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

/// <summary>
/// Regla de precio por motivo en el formulario de movimiento: obligatorio (> 0) solo para
/// Compra, oculto (y vacío) para Uso o consumo, opcional para Ajuste y Merma.
/// </summary>
public class RegistroPrecioPorMotivoTests
{
    private static ProductoDto Producto() => new(
        Id: 1, Codigo: "SKU1", CodigoBarras: null, Nombre: "Azúcar", Descripcion: null,
        CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
        UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, StockActual: 0m,
        StockMinimo: 0m, Activo: true, FechaAlta: default);

    private static EntradaRegistroViewModel Entrada() => new(
        Mock.Of<IMovimientoStockService>(), Mock.Of<IProductoService>(),
        Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());

    private static SalidaRegistroViewModel Salida() => new(
        Mock.Of<IMovimientoStockService>(), Mock.Of<IProductoService>(),
        Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());

    private static void Completar(MovimientoRegistroViewModelBase vm)
    {
        vm.ProductoSeleccionado = Producto();
        vm.Cantidad = 5m;
    }

    // ── placeholder y visibilidad ────────────────────────────────────────────

    [Theory]
    [InlineData(MotivoMovimiento.Compra, "Obligatorio", true)]
    [InlineData(MotivoMovimiento.Ajuste, "Opcional", true)]
    public void Entrada_PlaceholderYVisibilidad_SegunMotivo(MotivoMovimiento motivo, string placeholder, bool visible)
    {
        var vm = Entrada();
        vm.Motivo = motivo;

        Assert.Equal(placeholder, vm.PrecioPlaceholder);
        Assert.Equal(visible, vm.PrecioVisible);
    }

    [Theory]
    [InlineData(MotivoMovimiento.UsoOConsumo, false)]
    [InlineData(MotivoMovimiento.Merma, true)]
    [InlineData(MotivoMovimiento.Ajuste, true)]
    public void Salida_Visibilidad_SegunMotivo(MotivoMovimiento motivo, bool visible)
    {
        var vm = Salida();
        vm.Motivo = motivo;

        Assert.Equal(visible, vm.PrecioVisible);
        if (visible) Assert.Equal("Opcional", vm.PrecioPlaceholder);
    }

    [Fact]
    public void CambiarMotivo_NotificaLasPropiedadesDerivadas()
    {
        var vm = Entrada();   // arranca en Compra
        var cambiadas = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => cambiadas.Add(e.PropertyName);

        vm.Motivo = MotivoMovimiento.Ajuste;

        Assert.Contains(nameof(MovimientoRegistroViewModelBase.PrecioVisible), cambiadas);
        Assert.Contains(nameof(MovimientoRegistroViewModelBase.PrecioPlaceholder), cambiadas);
    }

    [Fact]
    public void CambiarMotivo_ReevaluaElCanExecuteDelComando()
    {
        var vm = Entrada();
        var veces = 0;
        vm.RegistrarCommand.CanExecuteChanged += (_, _) => veces++;

        vm.Motivo = MotivoMovimiento.Ajuste;

        Assert.True(veces > 0);
    }

    // ── limpieza del precio al ocultarse ─────────────────────────────────────

    [Fact]
    public void ElegirUsoOConsumo_LimpiaElPrecioCargado()
    {
        var vm = Salida();
        vm.Motivo = MotivoMovimiento.Merma;
        vm.PrecioUnitario = 25m;

        vm.Motivo = MotivoMovimiento.UsoOConsumo;

        Assert.Null(vm.PrecioUnitario);
    }

    [Fact]
    public void ElegirMerma_ConservaElPrecioCargado()
    {
        var vm = Salida();
        vm.Motivo = MotivoMovimiento.Ajuste;
        vm.PrecioUnitario = 25m;

        vm.Motivo = MotivoMovimiento.Merma;

        Assert.Equal(25m, vm.PrecioUnitario);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    [InlineData(10, true)]
    public void Compra_ExigePrecioMayorACero(int? precio, bool habilitado)
    {
        var vm = Entrada();
        vm.Motivo = MotivoMovimiento.Compra;
        Completar(vm);
        vm.PrecioUnitario = precio;

        Assert.Equal(habilitado, vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void Compra_AlCargarElPrecio_SeHabilitaRegistrar()
    {
        var vm = Entrada();
        Completar(vm);
        Assert.False(vm.RegistrarCommand.CanExecute(null));

        vm.PrecioUnitario = 10m;

        Assert.True(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void UsoOConsumo_NoExigePrecio()
    {
        var vm = Salida();
        vm.Motivo = MotivoMovimiento.UsoOConsumo;
        Completar(vm);

        Assert.True(vm.RegistrarCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(MotivoMovimiento.Merma)]
    [InlineData(MotivoMovimiento.Ajuste)]
    public void AjusteYMerma_NoExigenPrecio(MotivoMovimiento motivo)
    {
        var vm = Salida();
        vm.Motivo = motivo;
        Completar(vm);

        Assert.True(vm.RegistrarCommand.CanExecute(null));
    }
}
