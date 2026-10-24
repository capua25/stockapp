using Moq;
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

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/finanzas/pagos*.html (textos fijados en DatosPagosTests). El gasto de los mockups es
/// el de Luminarias LED (DatosGastos.Gastos[1], Id 6) con tres pagos propios; la fecha del formulario es 17/10/2026. La
/// selección de un pago la hace la comparación sobre la vista ya cargada (la carga de la vista suelta la selección).
/// </summary>
public static class DatosPagos
{
    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto Copia(Gasto origen, params PagoGasto[] pagos)
    {
        var gasto = new Gasto
        {
            Id = origen.Id, ProveedorId = origen.ProveedorId, Proveedor = origen.Proveedor, NumeroFactura = origen.NumeroFactura,
            Detalle = origen.Detalle, Fecha = origen.Fecha, MontoTotal = origen.MontoTotal,
            RubroGastoId = origen.RubroGastoId, RubroGasto = origen.RubroGasto,
            LineaPoaId = origen.LineaPoaId, LineaPoa = origen.LineaPoa,
            CondicionPago = origen.CondicionPago, FechaVencimiento = origen.FechaVencimiento, Activo = origen.Activo,
        };
        foreach (var p in pagos) gasto.Pagos.Add(p);
        return gasto;
    }

    /// <summary>Literales fijos de los mockups: dos de la caja Local y uno de la POA (el combo del formulario solo ofrece los Local).</summary>
    public static readonly FuenteFinanciamiento RentasGenerales = new() { Id = 1, Nombre = "Rentas Generales", Caja = Caja.Local, Activo = true };
    public static readonly FuenteFinanciamiento Tributos = new() { Id = 2, Nombre = "Tributos", Caja = Caja.Local, Activo = true };
    public static readonly FuenteFinanciamiento Figm = new() { Id = 3, Nombre = "FIGM", Caja = Caja.Poa, Activo = true };

    private static PagoGasto Pago(int id, int gastoId, DateTime fecha, decimal monto, string nota, bool activo = true,
        FuenteFinanciamiento? fuente = null) =>
        new()
        {
            Id = id, GastoId = gastoId, Fecha = fecha, Monto = monto, Nota = nota, Activo = activo,
            Caja = (fuente ?? RentasGenerales).Caja, FuenteFinanciamientoId = (fuente ?? RentasGenerales).Id,
            FuenteFinanciamiento = fuente ?? RentasGenerales,
        };

    /// <summary>D8: pagos 61 (activo), 62 (anulado) y 63 (activo): pagado 150.000, saldo 160.000, Parcial.</summary>
    public static readonly Gasto Gasto = Copia(DatosGastos.Gastos[1],
        Pago(61, 6, Dia(2026, 10, 12), 100_000m, "Recibo 2231"),
        Pago(62, 6, Dia(2026, 10, 13), 50_000m, "Cargado dos veces", activo: false),
        Pago(63, 6, Dia(2026, 10, 14), 50_000m, "Recibo 2240"));

    /// <summary>Constructora Oriental: Pendiente, sin pagos.</summary>
    public static readonly Gasto GastoSinPagos = Copia(DatosGastos.Gastos[0]);

    /// <summary>El combustible duplicado, anulado, con su pago automático de contado dado de baja con él.</summary>
    public static readonly Gasto GastoAnulado = Copia(DatosGastos.Anulado,
        Pago(80, 8, Dia(2026, 10, 10), 92_300m, "Pago contado (automático)", activo: false));

    /// <summary>El combustible pagado de contado desde la caja POA: su pago automático es de la POA, así que no admite pagos manuales.</summary>
    public static readonly Gasto GastoPoaAlContado = Copia(DatosGastos.Gastos[2],
        ConId(PagoGasto.Automatico(Dia(2026, 10, 8), 92_300m, "Pago contado (automático)", Caja.Poa, Figm.Id), 50, Figm));

    private static PagoGasto ConId(PagoGasto pago, int id, FuenteFinanciamiento fuente)
    {
        pago.Id = id;
        pago.GastoId = 5;
        pago.FuenteFinanciamiento = fuente;
        return pago;
    }

    public static readonly AdjuntoDto ReciboDelPago61 =
        new(1, "recibo-2231.pdf", "application/pdf", 96_300, null, 61, new DateTime(2026, 10, 12, 15, 20, 0, DateTimeKind.Utc));

    public static readonly AdjuntoDto ComprobanteDuplicadoDelPago62 =
        new(2, "comprobante-duplicado.pdf", "application/pdf", 61_440, null, 62, new DateTime(2026, 10, 13, 11, 5, 0, DateTimeKind.Utc));

    private static ICurrentSession Sesion(bool soloLectura)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(soloLectura ? StockApp.Domain.Enums.RolUsuario.Operador : StockApp.Domain.Enums.RolUsuario.Admin);
        sesion.Setup(s => s.UsuarioActual).Returns(soloLectura ? DatosIngresos.UsuarioOperador : DatosProductos.UsuarioAdmin);
        sesion.Setup(s => s.PermisosActuales).Returns(soloLectura ? new HashSet<string> { Permisos.VerFinanzas } : new HashSet<string>());
        return sesion.Object;
    }

    /// <summary>Pantalla de pagos del gasto dada, con el adjunto del pago 61 y el del 62, y la fecha del formulario fija.</summary>
    public static PagosGastoViewModel Pantalla(Gasto gasto, bool soloLectura = false, string? montoTexto = null)
    {
        var sesion = Sesion(soloLectura);
        var servicio = new Mock<IGastoService>();
        servicio.Setup(s => s.ObtenerPorIdAsync(gasto.Id)).ReturnsAsync(gasto);
        var adjuntos = new Mock<IAdjuntoService>();
        adjuntos.Setup(a => a.ListarPorPagoAsync(It.IsAny<int>())).ReturnsAsync(new List<AdjuntoDto>());
        adjuntos.Setup(a => a.ListarPorPagoAsync(61)).ReturnsAsync(new List<AdjuntoDto> { ReciboDelPago61 });
        adjuntos.Setup(a => a.ListarPorPagoAsync(62)).ReturnsAsync(new List<AdjuntoDto> { ComprobanteDuplicadoDelPago62 });
        var panel = new AdjuntosPanelViewModel(adjuntos.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion);
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento> { RentasGenerales, Tributos, Figm });
        var vm = new PagosGastoViewModel(servicio.Object, fuentes.Object, sesion, Mock.Of<INavigationService>(),
            Mock.Of<IConfirmacionService>(), panel);
        vm.CargarParaGasto(gasto);
        vm.FechaSeleccionada = new DateTime(2026, 10, 17);
        if (montoTexto is not null)
            vm.MontoTexto = montoTexto;
        return vm;
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) => DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Gastos", vm);

    public static ShellMainViewModel ConPagos() => EnShell(Pantalla(Gasto));
    public static ShellMainViewModel SinPagos() => EnShell(Pantalla(GastoSinPagos));

    /// <summary>Monto "1.5": el error del campo (el detalle solo) y Registrar pago deshabilitado.</summary>
    public static ShellMainViewModel ConError() => EnShell(Pantalla(Gasto, montoTexto: "1.5"));

    /// <summary>Operador con finanzas.ver y sin finanzas.pagos: sin registrar ni anular, panel solo con Ver.</summary>
    public static ShellMainViewModel SoloLectura() =>
        DatosShell.Crear(DatosIngresos.UsuarioOperador, [Permisos.VerFinanzas], "Gastos", Pantalla(Gasto, soloLectura: true));

    public static ShellMainViewModel GastoAnuladoPantalla() => EnShell(Pantalla(GastoAnulado));

    /// <summary>Admin con un gasto POA al contado: la card de registrar se reemplaza por el aviso.</summary>
    public static ShellMainViewModel GastoPoaAlContadoPantalla() => EnShell(Pantalla(GastoPoaAlContado));

    /// <summary>Mismo gasto que <see cref="ConPagos"/>: la comparación selecciona el pago 62 (anulado) con la vista cargada.</summary>
    public static ShellMainViewModel PagoAnulado() => EnShell(Pantalla(Gasto));

    /// <summary>La pregunta de Anular sobre el pago 61, la que arma el VM real.</summary>
    public static string MensajeConfirmarAnular() => PagosGastoViewModel.MensajeConfirmarAnular(Gasto.Pagos.Single(p => p.Id == 61));
}
