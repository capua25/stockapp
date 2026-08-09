using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StockApp.Application.Alertas;
using StockApp.Application.Catalogo;
using StockApp.Application.Logs;
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

    public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
        => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());

    public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
        => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
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

    /// <summary>
    /// Espía (fix/integridad-referencial, Minor 10 del review adversarial): antes InformarAsync
    /// era un no-op puro -- ningún test que dijera "informa el error" podía comprobarlo de verdad,
    /// porque el fake no dejaba rastro de qué se llamó. Ver
    /// MantenimientoViewTests.Montar_HacerBackupAhoraFalla_InformaElErrorYRestauraElBoton.
    /// </summary>
    public List<string> MensajesInformados { get; } = new();

    public Task InformarAsync(string mensaje)
    {
        MensajesInformados.Add(mensaje);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake de ILogsService (Entrega 2, Task 9; extendido Task 10 con resumen configurable). Por
/// defecto "sin datos": usado por los montajes headless de MantenimientoView/AccesoLimitadoView
/// que no ejercitan la zona Diagnóstico, solo necesitan que el ViewModel pueda construirse y que
/// CargarResumenLogsAsync() no explote al ejecutarse. MantenimientoViewTests le pasa un
/// ResumenLogsDto explícito para cubrir los casos "con logs" y "sin logs" de esa zona.
/// </summary>
internal sealed class LogsServiceFake : ILogsService
{
    private readonly ResumenLogsDto _resumen;

    public LogsServiceFake(ResumenLogsDto? resumen = null) =>
        _resumen = resumen ?? new ResumenLogsDto(0, null, null, 0);

    public Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default) =>
        Task.FromResult(_resumen);

    public Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default) =>
        Task.FromResult(new LogsDescargaDto("logs.zip", new MemoryStream([1, 2, 3])));
}

/// <summary>
/// Fake de IConfiguracionAlertasService (Task 7, sección Alertas de Mantenimiento). Mismo
/// criterio que <see cref="LogsServiceFake"/>: por defecto "sin configurar", usado por los
/// montajes headless que no ejercitan la zona Alertas, solo necesitan que el ViewModel pueda
/// construirse y que CargarAlertasAsync() no explote al ejecutarse.
/// </summary>
internal sealed class ConfiguracionAlertasServiceFake : IConfiguracionAlertasService
{
    private readonly ConfiguracionAlertasDto _configuracion;

    public ConfiguracionAlertasServiceFake(ConfiguracionAlertasDto? configuracion = null) =>
        _configuracion = configuracion ?? new ConfiguracionAlertasDto(null, false, null);

    public Task<ConfiguracionAlertasDto> ObtenerAsync(CancellationToken ct = default) =>
        Task.FromResult(_configuracion);

    public Task GuardarAsync(string? urlWebhook, bool habilitado, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<ResultadoPruebaAlertaDto> ProbarAsync(CancellationToken ct = default) =>
        Task.FromResult(new ResultadoPruebaAlertaDto(true, 200, "Se envió un ping de prueba."));
}
