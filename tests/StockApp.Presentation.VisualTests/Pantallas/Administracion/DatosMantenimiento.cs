using Moq;
using StockApp.Application.Alertas;
using StockApp.Application.Backups;
using StockApp.Application.Logs;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Datos fijos de docs/ui-v2/mockups/administracion/mantenimiento*.html (textos fijados en DatosAdministracionTests).
/// Las fechas son UTC y la comparación corre con la zona horaria de la máquina en UTC.</summary>
public static class DatosMantenimiento
{
    public static readonly IReadOnlyList<CorridaBackupDto> Corridas =
    [
        new(3, new DateTime(2026, 10, 6, 11, 0, 12, DateTimeKind.Utc), "Exitosa", "stockapp-20261006-1100.dump", 48_234_496, null),
        new(2, new DateTime(2026, 10, 5, 23, 0, 4, DateTimeKind.Utc), "Fallida", null, null, "pg_dump terminó con código 1: no hay espacio en disco."),
        new(1, new DateTime(2026, 10, 5, 11, 0, 9, DateTimeKind.Utc), "Exitosa", "stockapp-20261005-1100.dump", 47_185_920,
            "Esta fila no proviene de una corrida real: se dio de alta al reconciliar un dump existente."),
    ];

    private static MantenimientoViewModel Armar(
        Func<Task<IReadOnlyList<CorridaBackupDto>>> listar, Func<Task<ConfiguracionAlertasDto>> alertas, Func<Task<ResumenLogsDto>> logs)
    {
        var backups = new Mock<IBackupsService>();
        backups.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>())).Returns(listar);
        var servicioAlertas = new Mock<IConfiguracionAlertasService>();
        servicioAlertas.Setup(a => a.ObtenerAsync(It.IsAny<CancellationToken>())).Returns(alertas);
        var servicioLogs = new Mock<ILogsService>();
        servicioLogs.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>())).Returns(logs);
        return new MantenimientoViewModel(backups.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            servicioLogs.Object, servicioAlertas.Object);
    }

    private static ShellMainViewModel EnShell(MantenimientoViewModel vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Mantenimiento", vm);

    private static Task<ConfiguracionAlertasDto> AlertasConfiguradas() => Task.FromResult(
        new ConfiguracionAlertasDto("https://hooks.municipio.uy/alertas/stockapp", true, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)));

    private static Task<ResumenLogsDto> Logs() => Task.FromResult(
        new ResumenLogsDto(12, new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc), 3_355_443));

    public static ShellMainViewModel ConCorridas() => EnShell(Armar(() => Task.FromResult(Corridas), AlertasConfiguradas, Logs));

    public static ShellMainViewModel Vacio() => EnShell(Armar(
        () => Task.FromResult<IReadOnlyList<CorridaBackupDto>>([]),
        () => Task.FromResult(new ConfiguracionAlertasDto(null, false, null)),
        () => Task.FromResult(new ResumenLogsDto(0, null, null, 0))));

    /// <summary>Fallan la lista de backups y las alertas: la pantalla lo dice sin mostrar "Todavía no hay backups".</summary>
    public static ShellMainViewModel ConErrores() => EnShell(Armar(
        () => Task.FromException<IReadOnlyList<CorridaBackupDto>>(new InvalidOperationException("sin servidor")),
        () => Task.FromException<ConfiguracionAlertasDto>(new InvalidOperationException("sin servidor")),
        Logs));
}
