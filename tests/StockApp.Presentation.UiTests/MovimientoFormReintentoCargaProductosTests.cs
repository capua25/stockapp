using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Bug 2026-10-09 (verificación orgánica): con la API caída al abrir "Registrar entrada" el combo de
/// Producto quedaba vacío para siempre y el form no ofrecía forma de reintentar. Se prueba con las
/// vistas REALES (Entrada y Salida comparten MovimientoFormControl): al fallar la carga aparece el
/// aviso con "Reintentar", y al pulsarlo el combo se llena y el aviso desaparece.
/// </summary>
public class MovimientoFormReintentoCargaProductosTests
{
    /// <summary>Falla con ServidorNoDisponibleException las primeras <c>fallos</c> veces, como lo hace
    /// el ApiClient real con la API pausada; después responde el catálogo.</summary>
    private sealed class ProductoServiceIntermitente : IProductoService
    {
        private int _fallosRestantes;
        public ProductoServiceIntermitente(int fallos) => _fallosRestantes = fallos;

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
        {
            if (_fallosRestantes > 0)
            {
                _fallosRestantes--;
                throw new ServidorNoDisponibleException();
            }

            IReadOnlyList<ProductoDto> productos =
            [
                new ProductoDto(1, "C-001", null, "Cemento portland 50 kg", null, null, null, null,
                    1, "Unidad", 100m, 10m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)),
            ];
            return Task.FromResult(productos);
        }

        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) => throw new NotSupportedException();
    }

    private static (Window Ventana, MovimientoRegistroViewModelBase Vm) Montar(bool salida, int fallos)
    {
        var productos = new ProductoServiceIntermitente(fallos);
        MovimientoRegistroViewModelBase vm;
        UserControl vista;
        if (salida)
        {
            var s = new SalidaRegistroViewModel(new MovimientoStockServiceFake(), productos,
                new NavigationServiceFake(), new ConfirmacionServiceFake());
            vm = s;
            vista = new SalidaRegistroView { DataContext = s };
        }
        else
        {
            var e = new EntradaRegistroViewModel(new MovimientoStockServiceFake(), productos,
                new NavigationServiceFake(), new ConfirmacionServiceFake(), new SesionFake(RolUsuario.Operador));
            vm = e;
            vista = new EntradaRegistroView { DataContext = e };
        }

        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return (ventana, vm);
    }

    private static T Buscar<T>(Window ventana, string nombre) where T : Control
        => ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServidorCaidoAlAbrir_MuestraAvisoConReintentar_YReintentarLlenaElCombo(bool salida)
    {
        var (ventana, vm) = Montar(salida, fallos: 1);

        var combo = Buscar<ComboBox>(ventana, "ComboProducto");
        var panel = Buscar<StackPanel>(ventana, "ErrorCargaProductos");
        var reintentar = Buscar<Button>(ventana, "BotonReintentarProductos");
        Assert.Empty(vm.Productos);
        Assert.True(panel.IsEffectivelyVisible, "Con la carga fallida debe verse el aviso con Reintentar.");
        Assert.True(reintentar.IsEffectivelyVisible);

        reintentar.Command!.Execute(reintentar.CommandParameter);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Productos);
        Assert.Equal(1, combo.ItemCount);
        Assert.False(panel.IsEffectivelyVisible, "Tras una carga exitosa el aviso debe desaparecer.");
    }

    [AvaloniaFact]
    public void CargaExitosaAlAbrir_NoMuestraElAviso()
    {
        var (ventana, _) = Montar(salida: false, fallos: 0);

        Assert.False(Buscar<StackPanel>(ventana, "ErrorCargaProductos").IsEffectivelyVisible);
    }
}
