using Moq;
using StockApp.ApiClient;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/libro-caja*.html (cuadres y textos fijados en DatosLibroCajaTests).
/// Octubre de 2026 con el reloj en el 06/10/2026: el saldo corrido es el del servidor (inicial + ingresos − egresos en
/// orden de fecha) y los totales por rubro y por fuente vienen ordenados de mayor a menor, como los arma el servidor.</summary>
public static class DatosLibroCaja
{
    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public static readonly TimeProvider Reloj = new RelojFijo();

    private static MovimientoCajaDto Ingreso(int dia, string concepto, string fuente, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Ingreso", concepto, null, null, fuente, null, monto, 0m, corrido);

    private static MovimientoCajaDto Egreso(int dia, string concepto, string proveedor, string factura, string fuente,
        string rubro, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Egreso", concepto, proveedor, factura, fuente, rubro, 0m, monto, corrido);

    public static readonly LibroCajaMesDto Octubre = new(2026, 10, 2_350_000m, 2_639_460m,
    [
        Ingreso(2, "Multas de tránsito setiembre", "Rentas generales", 48_300m, 2_398_300m),
        Egreso(3, "Materiales para bacheo de calle Zorrilla", "Barraca Carmelo S.A.", "A-0004521", "FIGM Literal B",
            "Materiales de construcción", 186_540m, 2_211_760m),
        Ingreso(6, "Partida mensual FIGM octubre", "FIGM Literal B", 1_250_000m, 3_461_760m),
        Egreso(8, "Combustible de la flota municipal", "Ancap Distribuidora", "B-0012877", "Rentas generales",
            "Combustibles", 92_300m, 3_369_460m),
        Egreso(12, "Luminarias LED plaza Artigas", "Electro Sur S.R.L.", "A-0001190", "FIGM Literal B",
            "Alumbrado público", 310_000m, 3_059_460m),
        Egreso(15, "Reparación de cordón cuneta", "Constructora Oriental S.A.", "A-0002210", "Préstamo BROU",
            "Obras viales", 420_000m, 2_639_460m),
    ],
    [
        new("Obras viales", 420_000m), new("Alumbrado público", 310_000m),
        new("Materiales de construcción", 186_540m), new("Combustibles", 92_300m),
    ],
    [new("FIGM Literal B", 753_460m), new("Rentas generales", -44_000m), new("Préstamo BROU", -420_000m)]);

    public static readonly LibroCajaMesDto OctubreSaldoNegativo = new(2026, 10, 85_000m, -51_540m,
    [
        Egreso(3, "Materiales para bacheo de calle Zorrilla", "Barraca Carmelo S.A.", "A-0004521", "FIGM Literal B",
            "Materiales de construcción", 186_540m, -101_540m),
        Ingreso(6, "Adelanto de la partida FIGM octubre", "FIGM Literal B", 50_000m, -51_540m),
    ],
    [new("Materiales de construcción", 186_540m)],
    [new("FIGM Literal B", -136_540m)]);

    public static readonly LibroCajaMesDto OctubreVacio = new(2026, 10, 2_350_000m, 2_350_000m, [], [], []);

    /// <summary>Enero solo aporta el saldo inicial del año (el anual no trae saldos).</summary>
    public static readonly LibroCajaMesDto Enero2026 = new(2026, 1, 949_350m, 949_350m, [], [], []);

    public static readonly LibroCajaAnualDto Anio2026 = new(2026,
    [
        new(1, 1_250_000m, 980_200m, 269_800m),
        new(2, 1_250_000m, 1_104_500m, 145_500m),
        new(3, 1_250_000m, 1_322_750m, -72_750m),
        new(4, 1_298_300m, 1_010_000m, 288_300m),
        new(5, 1_250_000m, 890_400m, 359_600m),
        new(6, 1_250_000m, 1_415_900m, -165_900m),
        new(7, 1_265_000m, 1_120_000m, 145_000m),
        new(8, 1_250_000m, 1_198_600m, 51_400m),
        new(9, 3_250_000m, 2_870_300m, 379_700m),
        new(10, 1_298_300m, 1_008_840m, 289_460m),
        new(11, 0m, 0m, 0m),
        new(12, 0m, 0m, 0m),
    ],
    [
        new("Obras viales", 4_210_000m), new("Materiales de construcción", 2_986_540m),
        new("Servicios contratados", 2_772_650m), new("Alumbrado público", 1_840_000m), new("Combustibles", 1_112_300m),
    ]);

    public static LibroCajaViewModel Vm(Func<Task<LibroCajaMesDto>> octubre)
    {
        var servicio = new Mock<IFinanzasVistasService>();
        servicio.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).Returns(octubre);
        servicio.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 1)).ReturnsAsync(Enero2026);
        servicio.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(Anio2026);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(DatosProductos.UsuarioAdmin.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new LibroCajaViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), sesion.Object, Reloj);
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "LibroCaja", vm);

    public static ShellMainViewModel ConMovimientos() => EnShell(Vm(() => Task.FromResult(Octubre)));
    public static ShellMainViewModel SaldoNegativo() => EnShell(Vm(() => Task.FromResult(OctubreSaldoNegativo)));
    public static ShellMainViewModel SinMovimientos() => EnShell(Vm(() => Task.FromResult(OctubreVacio)));
    public static ShellMainViewModel Cargando() => EnShell(Vm(() => new TaskCompletionSource<LibroCajaMesDto>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Vm(() => Task.FromException<LibroCajaMesDto>(new ServidorNoDisponibleException())));
    public static ShellMainViewModel SinPermiso() =>
        EnShell(Vm(() => Task.FromException<LibroCajaMesDto>(new UnauthorizedAccessException("403"))));

    /// <summary>El selector cambia antes de que la vista pida la primera carga; el debounce que eso dispara recarga lo
    /// mismo (el Moq responde igual), así que no cambia el render.</summary>
    public static ShellMainViewModel AnioCompleto()
    {
        var vm = Vm(() => Task.FromResult(Octubre));
        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;
        return EnShell(vm);
    }
}
