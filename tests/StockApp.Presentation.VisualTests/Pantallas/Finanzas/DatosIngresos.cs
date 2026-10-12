using Moq;
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/ingreso*.html (textos fijados en DatosIngresosTests). El listado
/// viene ordenado como lo devuelve el servidor: fecha descendente y, a igual fecha, id descendente.</summary>
public static class DatosIngresos
{
    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "FIGM Literal B", Activo = true },
        new() { Id = 2, Nombre = "Rentas generales", Activo = true },
        new() { Id = 3, Nombre = "Préstamo BROU", Activo = true },
    ];

    private static IngresoCaja I(int id, int anio, int mes, int dia, string concepto, int fuente, decimal monto, bool activo = true) => new()
    {
        Id = id, Fecha = new DateTime(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc), Concepto = concepto,
        FuenteFinanciamientoId = fuente, FuenteFinanciamiento = Fuentes[fuente - 1], Monto = monto, Activo = activo,
    };

    public static readonly IReadOnlyList<IngresoCaja> Ingresos =
    [
        I(8, 2026, 10, 6, "Partida mensual FIGM octubre", 1, 1_250_000m),
        I(7, 2026, 10, 2, "Multas de tránsito setiembre", 2, 48_300m),
        I(6, 2026, 10, 2, "Multas de tránsito setiembre (duplicado)", 2, 48_300m, activo: false),
        I(5, 2026, 9, 15, "Préstamo BROU obras de pluviales", 3, 2_000_000m),
        I(4, 2026, 9, 5, "Partida mensual FIGM setiembre", 1, 1_250_000m),
        I(3, 2026, 8, 28, "Aporte vecinal plaza Artigas", 2, 15_000m, activo: false),
        I(2, 2026, 8, 5, "Partida mensual FIGM agosto", 1, 1_250_000m),
        I(1, 2026, 1, 2, "Saldo inicial del ejercicio", 2, 812_430.55m),
    ];

    public static readonly UsuarioSesion UsuarioOperador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    private static ICurrentSession Sesion(bool soloLectura)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(soloLectura ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(soloLectura
            ? new HashSet<string> { Permisos.VerFinanzas }
            : new HashSet<string>());
        return sesion.Object;
    }

    public static IngresosViewModel Listado(Func<Task<IReadOnlyList<IngresoCaja>>> responder, bool soloLectura = false)
    {
        var servicio = new Mock<IIngresoCajaService>();
        servicio.Setup(s => s.ListarTodosAsync()).Returns(responder);
        return new IngresosViewModel(servicio.Object, Sesion(soloLectura), Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    /// <summary>Formulario con las tres fuentes y la fecha del alta fija (el VM arranca en "hoy").</summary>
    public static IngresoFormViewModel Formulario()
    {
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes);
        var vm = new IngresoFormViewModel(Mock.Of<IIngresoCajaService>(), fuentes.Object, Sesion(soloLectura: false), Mock.Of<INavigationService>())
        {
            FechaSeleccionada = new DateTime(2026, 10, 6),
        };
        return vm;
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) => DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Ingresos", vm);

    private static Task<IReadOnlyList<IngresoCaja>> Lista() => Task.FromResult(Ingresos);

    public static ShellMainViewModel ConIngresos() => EnShell(Listado(Lista));
    public static ShellMainViewModel Cargando() => EnShell(Listado(() => new TaskCompletionSource<IReadOnlyList<IngresoCaja>>().Task));
    public static ShellMainViewModel SinDatos() => EnShell(Listado(() => Task.FromResult<IReadOnlyList<IngresoCaja>>([])));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Listado(() => Task.FromException<IReadOnlyList<IngresoCaja>>(new ServidorNoDisponibleException())));

    public static ShellMainViewModel SinResultados()
    {
        var vm = Listado(Lista);
        vm.TextoBusqueda = "donación";
        return EnShell(vm);
    }

    public static ShellMainViewModel ConInactivos()
    {
        var vm = Listado(Lista);
        vm.MostrarInactivos = true;
        return EnShell(vm);
    }

    public static ShellMainViewModel SoloLectura() =>
        DatosShell.Crear(UsuarioOperador, [Permisos.VerFinanzas], "Ingresos", Listado(Lista, soloLectura: true));

    public static ShellMainViewModel Nuevo() => EnShell(Formulario());

    public static ShellMainViewModel Editar()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Ingresos[1]);
        return EnShell(vm);
    }

    /// <summary>Concepto tocado y vacío, y un monto con punto decimal: los dos errores debajo de su campo.</summary>
    public static ShellMainViewModel ConErrorDeCampo()
    {
        var vm = Formulario();
        vm.MarcarTocado(nameof(IngresoFormViewModel.Concepto));
        vm.MontoTexto = "1.5";
        return EnShell(vm);
    }

    /// <summary>La regla del servidor ("mayor a cero") sale en la Alerta de error arriba del pie.</summary>
    public static ShellMainViewModel ConErrorDelServidor()
    {
        var vm = Formulario();
        vm.Concepto = "Partida mensual FIGM octubre";
        vm.MontoTexto = "0,00";
        vm.MensajeError = "El monto del ingreso debe ser mayor a cero.";
        return EnShell(vm);
    }
}
