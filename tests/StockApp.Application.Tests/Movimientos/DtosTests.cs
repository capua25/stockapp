using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Movimientos;

/// <summary>
/// Verifica que los records de DTOs de movimientos existen,
/// son construibles y exponen las propiedades esperadas con los tipos correctos.
/// </summary>
public class DtosTests
{
    [Fact]
    public void RegistrarMovimientoDto_PropiedadesAccesibles()
    {
        var dto = new RegistrarMovimientoDto(
            ProductoId: 1,
            Tipo: TipoMovimiento.Entrada,
            Motivo: MotivoMovimiento.Compra,
            Cantidad: 10m,
            PrecioUnitario: 150m,
            Comentario: "Compra inicial");

        Assert.Equal(1, dto.ProductoId);
        Assert.Equal(TipoMovimiento.Entrada, dto.Tipo);
        Assert.Equal(MotivoMovimiento.Compra, dto.Motivo);
        Assert.Equal(10m, dto.Cantidad);
        Assert.Equal(150m, dto.PrecioUnitario);
        Assert.Equal("Compra inicial", dto.Comentario);
    }

    [Fact]
    public void RegistrarMovimientoDto_PrecioYComentarioOpcionales()
    {
        var dto = new RegistrarMovimientoDto(
            ProductoId: 2,
            Tipo: TipoMovimiento.Salida,
            Motivo: MotivoMovimiento.Ajuste,
            Cantidad: 5m,
            PrecioUnitario: null,
            Comentario: null);

        Assert.Null(dto.PrecioUnitario);
        Assert.Null(dto.Comentario);
    }

    [Fact]
    public void MovimientoRegistradoDto_PropiedadesAccesibles()
    {
        var fecha = DateTime.UtcNow;
        var dto = new MovimientoRegistradoDto(
            MovimientoId: 42,
            ProductoId: 1,
            Tipo: TipoMovimiento.Entrada,
            Motivo: MotivoMovimiento.Compra,
            Cantidad: 10m,
            PrecioUnitario: 150m,
            StockAnterior: 5m,
            StockNuevo: 15m,
            Fecha: fecha);

        Assert.Equal(42, dto.MovimientoId);
        Assert.Equal(1, dto.ProductoId);
        Assert.Equal(TipoMovimiento.Entrada, dto.Tipo);
        Assert.Equal(MotivoMovimiento.Compra, dto.Motivo);
        Assert.Equal(10m, dto.Cantidad);
        Assert.Equal(150m, dto.PrecioUnitario);
        Assert.Equal(5m, dto.StockAnterior);
        Assert.Equal(15m, dto.StockNuevo);
        Assert.Equal(fecha, dto.Fecha);
    }

    [Fact]
    public void HistorialMovimientoFiltro_PropiedadesOpcionales()
    {
        // Sin argumentos: todo null
        var filtroVacio = new HistorialMovimientoFiltro();
        Assert.Null(filtroVacio.ProductoId);
        Assert.Null(filtroVacio.Tipo);
        Assert.Null(filtroVacio.FechaDesde);
        Assert.Null(filtroVacio.FechaHasta);

        // Con argumentos
        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 12, 31);
        var filtro = new HistorialMovimientoFiltro(
            ProductoId: 5,
            Tipo: TipoMovimiento.Salida,
            FechaDesde: desde,
            FechaHasta: hasta);

        Assert.Equal(5, filtro.ProductoId);
        Assert.Equal(TipoMovimiento.Salida, filtro.Tipo);
        Assert.Equal(desde, filtro.FechaDesde);
        Assert.Equal(hasta, filtro.FechaHasta);
    }

    [Fact]
    public void MovimientoHistorialDto_PropiedadesAccesibles()
    {
        var fecha = DateTime.UtcNow;
        var dto = new MovimientoHistorialDto(
            MovimientoId: 7,
            ProductoId: 3,
            ProductoNombre: "Fideos 500g",
            Tipo: TipoMovimiento.Salida,
            Motivo: MotivoMovimiento.Venta,
            Cantidad: 2m,
            PrecioUnitario: 200m,
            StockAnterior: 20m,
            StockNuevo: 18m,
            Comentario: null,
            Fecha: fecha,
            UsuarioId: 1,
            UsuarioNombre: "Admin");

        Assert.Equal(7, dto.MovimientoId);
        Assert.Equal(3, dto.ProductoId);
        Assert.Equal("Fideos 500g", dto.ProductoNombre);
        Assert.Equal(TipoMovimiento.Salida, dto.Tipo);
        Assert.Equal(MotivoMovimiento.Venta, dto.Motivo);
        Assert.Equal(2m, dto.Cantidad);
        Assert.Equal(200m, dto.PrecioUnitario);
        Assert.Equal(20m, dto.StockAnterior);
        Assert.Equal(18m, dto.StockNuevo);
        Assert.Null(dto.Comentario);
        Assert.Equal(fecha, dto.Fecha);
        Assert.Equal(1, dto.UsuarioId);
        Assert.Equal("Admin", dto.UsuarioNombre);
    }

    [Fact]
    public void RecalculoResultadoDto_PropiedadesAccesibles()
    {
        var dto = new RecalculoResultadoDto(
            ProductoId: 10,
            StockAnterior: 8m,
            StockNuevo: 12m,
            TotalMovimientos: 5);

        Assert.Equal(10, dto.ProductoId);
        Assert.Equal(8m, dto.StockAnterior);
        Assert.Equal(12m, dto.StockNuevo);
        Assert.Equal(5, dto.TotalMovimientos);
    }
}
