using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/finanzas/gasto*.html (textos fijados en DatosGastosTests). El listado viene como lo
/// devuelve el servidor: fecha descendente. El estado lo calcula GastoFila con el reloj real, así que los vencimientos están
/// lejos de "hoy": 2030 para los que no vencen y 30/09/2026 para el vencido.
/// </summary>
public static class DatosGastos
{
    public static readonly IReadOnlyList<Proveedor> Proveedores =
    [
        new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true },
        new() { Id = 2, Nombre = "Ancap Distribuidora", Activo = true },
        new() { Id = 3, Nombre = "Electro Sur S.R.L.", Activo = true },
        new() { Id = 4, Nombre = "Constructora Oriental S.A.", Activo = true },
    ];

    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "FIGM Literal B", Activo = true },
        new() { Id = 2, Nombre = "Rentas generales", Activo = true },
        new() { Id = 3, Nombre = "Préstamo BROU", Activo = true },
    ];

    public static readonly IReadOnlyList<RubroGasto> Rubros =
    [
        new() { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true },
        new() { Id = 2, Codigo = 20, Nombre = "Combustibles", Activo = true },
        new() { Id = 3, Codigo = 30, Nombre = "Alumbrado público", Activo = true },
        new() { Id = 4, Codigo = 40, Nombre = "Obras viales", Activo = true },
    ];

    public static readonly IReadOnlyList<LineaPoa> Lineas =
    [
        new() { Id = 1, Nombre = "Bacheo y caminería rural", Programa = "Infraestructura", Ejercicio = 2026, Activo = true },
        new() { Id = 2, Nombre = "Alumbrado de plazas", Programa = "Servicios", Ejercicio = 2026, Activo = true },
    ];

    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto G(int id, int mes, int dia, int proveedor, string factura, string detalle, int fuente, int rubro,
        int? linea, decimal monto, decimal pagado = 0m, DateTime? vence = null, bool activo = true, bool conStock = false)
    {
        var gasto = new Gasto
        {
            Id = id, ProveedorId = proveedor, Proveedor = Proveedores[proveedor - 1], NumeroFactura = factura, Detalle = detalle,
            Fecha = Dia(2026, mes, dia), MontoTotal = monto,
            RubroGastoId = rubro, RubroGasto = Rubros[rubro - 1],
            LineaPoaId = linea, LineaPoa = linea is int l ? Lineas[l - 1] : null,
            CondicionPago = vence is null ? CondicionPago.Contado : CondicionPago.Credito, FechaVencimiento = vence,
            Activo = activo, TieneMovimientosDeStock = conStock,
        };
        // La fuente vive en el pago. El contado siempre tiene su pago automático (con la fuente del gasto de los
        // mockups; el del gasto anulado está dado de baja); un crédito paga con pagos manuales.
        PagoGasto? pago = null;
        if (vence is null)
        {
            pago = PagoGasto.Automatico(
                gasto.Fecha, pagado > 0m ? pagado : monto, "Pago contado (automático)", Caja.Local, fuente);
            pago.Activo = pagado > 0m;
        }
        else if (pagado > 0m)
            pago = new PagoGasto { Fecha = gasto.Fecha, Monto = pagado, FuenteFinanciamientoId = fuente };
        if (pago is not null)
        {
            pago.Id = id * 10;
            pago.GastoId = id;
            pago.FuenteFinanciamiento = Fuentes[fuente - 1];
            gasto.Pagos.Add(pago);
        }
        return gasto;
    }

    /// <summary>Pendiente, Parcial, Pagada, Pagada (con stock: la pregunta de Anular lo avisa), Vencida y Pagada.</summary>
    public static readonly IReadOnlyList<Gasto> Gastos =
    [
        G(7, 10, 14, 4, "A-0002210", "Reparación de cordón cuneta en calle Zorrilla", 3, 4, 1, 420_000m, vence: Dia(2030, 12, 31)),
        G(6, 10, 12, 3, "A-0001190", "Luminarias LED plaza Artigas", 1, 3, 2, 310_000m, pagado: 150_000m, vence: Dia(2030, 11, 30)),
        G(5, 10, 8, 2, "B-0012877", "Combustible de la flota municipal", 2, 2, null, 92_300m, pagado: 92_300m),
        G(4, 10, 3, 1, "A-0004521", "Materiales para bacheo de calle Zorrilla", 1, 1, 1, 186_540m, pagado: 186_540m, conStock: true),
        G(3, 9, 15, 1, "A-0004377", "Arena y pedregullo para caminería rural", 1, 1, 1, 64_800m, vence: Dia(2026, 9, 30)),
        G(2, 9, 2, 3, "A-0001102", "Cableado del alumbrado de la rambla", 2, 3, null, 48_950m, pagado: 48_950m),
    ];

    /// <summary>La carga duplicada del combustible, anulada (su pago automático se dio de baja con ella).</summary>
    public static readonly Gasto Anulado =
        G(8, 10, 10, 2, "B-0012901", "Combustible de la flota municipal (duplicado)", 2, 2, null, 92_300m, activo: false);

    public static readonly IReadOnlyList<Gasto> ConAnuladoLista = [Gastos[0], Gastos[1], Anulado, Gastos[2], Gastos[3], Gastos[4], Gastos[5]];

    public static readonly AdjuntoDto AdjuntoDeLaFacturaA4521 =
        new(1, "factura-A-0004521.pdf", "application/pdf", 182_400, 4, null, new DateTime(2026, 10, 3, 15, 20, 0, DateTimeKind.Utc));

    private static ICurrentSession Sesion(bool soloLectura)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(soloLectura ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.UsuarioActual).Returns(soloLectura ? DatosIngresos.UsuarioOperador : DatosProductos.UsuarioAdmin);
        sesion.Setup(s => s.PermisosActuales).Returns(soloLectura ? new HashSet<string> { Permisos.VerFinanzas } : new HashSet<string>());
        return sesion.Object;
    }

    private static (IProveedorService, IFuenteFinanciamientoService, IRubroGastoService, ILineaPoaService) Maestros()
    {
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(Proveedores);
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes);
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(Rubros);
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(Lineas);
        return (proveedores.Object, fuentes.Object, rubros.Object, lineas.Object);
    }

    public static GastosViewModel Listado(Func<GastoFiltro, Task<IReadOnlyList<Gasto>>> responder, bool soloLectura = false,
        IConfirmacionService? confirmacion = null)
    {
        var servicio = new Mock<IGastoService>();
        servicio.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).Returns(responder);
        var (proveedores, fuentes, rubros, lineas) = Maestros();
        // Debounce en cero: con los 300 ms reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado depende de cuánto tarde la máquina.
        return new GastosViewModel(servicio.Object, Sesion(soloLectura), proveedores, fuentes, rubros, lineas,
            Mock.Of<INavigationService>(), confirmacion ?? Mock.Of<IConfirmacionService>(), Mock.Of<IServicioGuardadoArchivo>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>())
        { EsperaDebounce = TimeSpan.Zero };
    }

    /// <summary>Formulario con los cuatro combos, el adjunto de la factura A-0004521 y la fecha del alta fija.</summary>
    public static GastoFormViewModel Formulario(bool soloLectura = false, IGastoService? servicio = null,
        IConfirmacionService? confirmacion = null)
    {
        var sesion = Sesion(soloLectura);
        var adjuntos = new Mock<IAdjuntoService>();
        adjuntos.Setup(a => a.ListarPorGastoAsync(4)).ReturnsAsync(new List<AdjuntoDto> { AdjuntoDeLaFacturaA4521 });
        adjuntos.Setup(a => a.ListarPorGastoAsync(It.Is<int>(id => id != 4))).ReturnsAsync(new List<AdjuntoDto>());
        var panel = new AdjuntosPanelViewModel(adjuntos.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion);
        var (proveedores, fuentes, rubros, lineas) = Maestros();
        return new GastoFormViewModel(servicio ?? Mock.Of<IGastoService>(), sesion, proveedores, fuentes, rubros, lineas,
            Mock.Of<INavigationService>(), confirmacion ?? Mock.Of<IConfirmacionService>(), panel)
        {
            FechaSeleccionada = new DateTime(2026, 10, 6),
        };
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) => DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Gastos", vm);

    private static ShellMainViewModel EnShellSoloLectura(ViewModelBase vm) =>
        DatosShell.Crear(DatosIngresos.UsuarioOperador, [Permisos.VerFinanzas], "Gastos", vm);

    public static ShellMainViewModel ConGastos() => EnShell(Listado(_ => Task.FromResult(Gastos)));
    public static ShellMainViewModel Cargando() => EnShell(Listado(_ => new TaskCompletionSource<IReadOnlyList<Gasto>>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Listado(_ => Task.FromException<IReadOnlyList<Gasto>>(new ServidorNoDisponibleException())));
    public static ShellMainViewModel SinPermiso() =>
        EnShell(Listado(_ => Task.FromException<IReadOnlyList<Gasto>>(new UnauthorizedAccessException("403"))));
    public static ShellMainViewModel SinGastos() => EnShell(Listado(_ => Task.FromResult<IReadOnlyList<Gasto>>([])));
    public static ShellMainViewModel SoloLectura() => EnShellSoloLectura(Listado(_ => Task.FromResult(Gastos), soloLectura: true));
    public static ShellMainViewModel ConAnulado() => EnShell(Listado(_ => Task.FromResult(ConAnuladoLista)));

    /// <summary>"Desde" puesto antes de la carga (no programa recarga): el servidor no tiene gastos desde el 01/11/2026.</summary>
    public static ShellMainViewModel SinResultados()
    {
        var vm = Listado(f => Task.FromResult<IReadOnlyList<Gasto>>(f.FechaDesde is null ? Gastos : []));
        vm.FechaDesde = new DateTime(2026, 11, 1);
        return EnShell(vm);
    }

    public static ShellMainViewModel Nuevo() => EnShell(Formulario());

    public static ShellMainViewModel Editar()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Gastos[3]);
        return EnShell(vm);
    }

    public static ShellMainViewModel EditarCredito()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Gastos[0]);
        return EnShell(vm);
    }

    /// <summary>Detalle tocado y vacío, y un monto con punto decimal: los dos errores debajo de su campo.</summary>
    public static ShellMainViewModel ConErrorDeCampo()
    {
        var vm = Formulario();
        vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));
        vm.MontoTexto = "1.5";
        return EnShell(vm);
    }

    /// <summary>La regla del servidor ("mayor a cero") en la Alerta de arriba del pie, con el formulario completo.</summary>
    public static ShellMainViewModel ConErrorDelServidor()
    {
        var vm = Formulario();
        vm.ProveedorSeleccionado = Proveedores[1];
        vm.NumeroFactura = "B-0012950";
        vm.Detalle = "Combustible de la flota municipal";
        vm.MontoTexto = "0,00";
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = Fuentes[1];
        vm.RubroSeleccionado = Rubros[1];
        vm.MensajeError = "El monto total del gasto debe ser mayor a cero.";
        return EnShell(vm);
    }

    /// <summary>Alta a crédito con su vencimiento.</summary>
    public static ShellMainViewModel Credito()
    {
        var vm = Formulario();
        vm.ProveedorSeleccionado = Proveedores[3];
        vm.NumeroFactura = "A-0002301";
        vm.Detalle = "Hormigón para badenes en camino Las Flores";
        vm.MontoTexto = "245.000,00";
        vm.RubroSeleccionado = Rubros[3];
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = new DateTime(2026, 11, 5);
        return EnShell(vm);
    }

    public static ShellMainViewModel FormularioSoloLectura()
    {
        var vm = Formulario(soloLectura: true);
        vm.CargarParaEditar(Gastos[3]);
        return EnShellSoloLectura(vm);
    }

    public static ShellMainViewModel FormularioAnulado()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Anulado);
        return EnShell(vm);
    }

    /// <summary>La pregunta de Anular sobre los materiales para bacheo (con stock), armada por el VM real.</summary>
    public static string MensajeConfirmarAnular()
    {
        string? mensaje = null;
        var confirmacion = new Mock<IConfirmacionService>();
        confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).Callback<string>(m => mensaje = m).ReturnsAsync(false);
        var vm = Listado(_ => Task.FromResult(Gastos), confirmacion: confirmacion.Object);
        vm.CargarAsync().GetAwaiter().GetResult();
        vm.FilaSeleccionada = vm.Filas.Single(f => f.Id == 4);
        vm.AnularCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return mensaje!;
    }

    /// <summary>La pregunta de asociar la entrada a una factura que ya existe, armada por el VM real ("desde entrada").</summary>
    public static string MensajeFacturaDuplicada()
    {
        string? mensaje = null;
        var confirmacion = new Mock<IConfirmacionService>();
        confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).Callback<string>(m => mensaje = m).ReturnsAsync(false);
        var servicio = new Mock<IGastoService>();
        servicio.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-0004521' para ese proveedor."));
        servicio.Setup(s => s.ObtenerPorProveedorYFacturaAsync(1, "A-0004521", null)).ReturnsAsync(Gastos[3]);
        var vm = Formulario(servicio: servicio.Object, confirmacion: confirmacion.Object);
        vm.CargarDesdeEntrada(movimientoId: 61, montoSugerido: 186_540m);
        vm.InicializarAsync().GetAwaiter().GetResult();
        vm.ProveedorSeleccionado = Proveedores[0];
        vm.NumeroFactura = "A-0004521";
        vm.Detalle = "Materiales para bacheo de calle Zorrilla";
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = Fuentes[0];
        vm.RubroSeleccionado = Rubros[0];
        vm.GuardarCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return mensaje!;
    }
}
