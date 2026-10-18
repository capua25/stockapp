using Moq;
using StockApp.ApiClient;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/control-poa*.html. Saldo = presupuesto − gastado y el porcentaje
/// redondeado a 2, como los calcula el servidor (fijado en DatosFinanzasTests).</summary>
public static class DatosControlPoa
{
    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
    }

    public static readonly TimeProvider Reloj = new RelojFijo();

    public static readonly IReadOnlyList<ControlPoaLineaDto> Lineas =
    [
        new(1, "Mantenimiento de espacios públicos", "Obras", 2026, 1_200_000m, 567_600m, 632_400m, 47.30m, false),
        new(2, "Alumbrado público", "Obras", 2026, 850_000m, 801_250m, 48_750m, 94.26m, false),
        new(3, "Comunicación institucional", "Prensa", 2026, 150_000m, 37_500m, 112_500m, 25.00m, false),
        new(4, "Fiestas tradicionales", "Cultura", 2026, 400_000m, 400_000m, 0m, 100.00m, false),
    ];

    public static readonly IReadOnlyList<ControlPoaLineaDto> LineasConSobreejecutada =
        [.. Lineas, new(5, "Limpieza de playas", "Turismo", 2026, 300_000m, 342_900m, -42_900m, 114.30m, true)];

    public static ControlPoaViewModel Vm(Func<Task<IReadOnlyList<ControlPoaLineaDto>>> responder)
    {
        var servicio = new Mock<IFinanzasVistasService>();
        servicio.Setup(s => s.ObtenerControlPoaAsync(2026)).Returns(responder);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(DatosProductos.UsuarioAdmin.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        // Debounce en cero: con los 300 ms reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado depende de cuánto tarde la máquina.
        return new ControlPoaViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(), Mock.Of<IPdfExporter>(),
            Mock.Of<IServicioAperturaArchivo>(), sesion.Object, Reloj)
        { EsperaDebounce = TimeSpan.Zero };
    }

    private static ShellMainViewModel EnShell(ControlPoaViewModel vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "ControlPoa", vm);

    public static ShellMainViewModel ConLineas() => EnShell(Vm(() => Task.FromResult(Lineas)));
    public static ShellMainViewModel Sobreejecutado() => EnShell(Vm(() => Task.FromResult(LineasConSobreejecutada)));
    public static ShellMainViewModel SinLineas() => EnShell(Vm(() => Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>([])));
    public static ShellMainViewModel Cargando() => EnShell(Vm(() => new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Vm(() => Task.FromException<IReadOnlyList<ControlPoaLineaDto>>(new ServidorNoDisponibleException())));
}
