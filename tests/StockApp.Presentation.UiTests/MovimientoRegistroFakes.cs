using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fakes minimos de las dependencias de <see cref="StockApp.Presentation.ViewModels.Movimientos.MovimientoRegistroViewModelBase"/>,
/// usados solo por <see cref="MovimientoFormControlValidacionTests"/> para montar
/// EntradaRegistroViewModel real sin depender de un contenedor DI ni de Moq (este proyecto
/// no lo referencia, a diferencia de StockApp.Presentation.Tests). Ninguno de estos metodos
/// se ejercita en esos tests (solo se valida el binding de PrecioUnitario), por eso lanzan.
/// </summary>
internal sealed class MovimientoStockServiceFake : IMovimientoStockService
{
    public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)
        => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class ProductoServiceFake : IProductoService
{
    public Task<int> AltaAsync(Producto producto)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task ModificarAsync(Producto producto)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task BajaLogicaAsync(int id)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
        => Task.FromResult<IReadOnlyList<Producto>>(Array.Empty<Producto>());

    public Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto)
        => Task.FromResult<IReadOnlyList<Producto>>(Array.Empty<Producto>());
}

internal sealed class NavigationServiceFake : INavigationService
{
    public ViewModelBase? Actual => null;

    public event Action? Cambiado
    {
        add { }
        remove { }
    }

    public void Navegar<TVm>() where TVm : ViewModelBase
    {
    }

    public void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase
    {
    }
}

internal sealed class ConfirmacionServiceFake : IConfirmacionService
{
    public Task<bool> PreguntarAsync(string mensaje) => Task.FromResult(true);

    public Task InformarAsync(string mensaje) => Task.CompletedTask;
}
