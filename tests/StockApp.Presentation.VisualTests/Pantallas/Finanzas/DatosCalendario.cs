using Moq;
using StockApp.ApiClient;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/calendario-pagos*.html (textos fijados en DatosFinanzasTests).
/// Las facturas vienen ordenadas por vencimiento, como las devuelve el servidor.</summary>
public static class DatosCalendario
{
    public static readonly CalendarioPagosDto Calendario = new(
        Vencidas:
        [
            new(101, "Barraca Carmelo S.A.", "A-0004398", 18_450m, new DateOnly(2026, 9, 28), "Pendiente"),
            new(102, "Ferretería Industrial del Este", null, 6_120.50m, new DateOnly(2026, 10, 2), "Pendiente"),
        ],
        AVencer7Dias:
        [
            new(103, "Distribuidora Eléctrica Colonia", "B-0012877", 9_870m, new DateOnly(2026, 10, 9), "Pendiente"),
        ],
        AVencer30Dias:
        [
            new(104, "Imprenta Rosales", "A-0000815", 3_200m, new DateOnly(2026, 10, 20), "Pendiente"),
            new(105, "Barraca Carmelo S.A.", "A-0004521", 44_940m, new DateOnly(2026, 11, 5), "Pendiente"),
        ],
        PagosRecientes:
        [
            new(106, "Corralón Sur", "A-0002210", new DateOnly(2026, 10, 5), 12_300m),
            new(104, "Imprenta Rosales", "A-0000815", new DateOnly(2026, 10, 1), 1_500m),
        ]);

    public static readonly CalendarioPagosDto Vacio = new([], [], [], []);

    public static CalendarioPagosViewModel Vm(Func<Task<CalendarioPagosDto>> responder)
    {
        var servicio = new Mock<IFinanzasVistasService>();
        servicio.Setup(s => s.ObtenerCalendarioPagosAsync(null)).Returns(responder);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(DatosProductos.UsuarioAdmin.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new CalendarioPagosViewModel(servicio.Object, Mock.Of<IGastoService>(), sesion.Object, Mock.Of<INavigationService>());
    }

    private static ShellMainViewModel EnShell(CalendarioPagosViewModel vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "CalendarioPagos", vm);

    public static ShellMainViewModel ConDatos() => EnShell(Vm(() => Task.FromResult(Calendario)));
    public static ShellMainViewModel SinDatos() => EnShell(Vm(() => Task.FromResult(Vacio)));
    public static ShellMainViewModel Cargando() => EnShell(Vm(() => new TaskCompletionSource<CalendarioPagosDto>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Vm(() => Task.FromException<CalendarioPagosDto>(new ServidorNoDisponibleException())));
}
