using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class GastosViewModelTests
{
    private static readonly DateTime Hoy = DateTime.UtcNow;

    private static Gasto GastoDe(
        int id, string detalle, bool pagado = false, bool activo = true, DateTime? fechaUtc = null)
    {
        var gasto = new Gasto
        {
            Id = id,
            ProveedorId = 1,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
            Detalle = detalle,
            Fecha = fechaUtc ?? Hoy,
            MontoTotal = 1000m,
            FuenteFinanciamientoId = 2,
            RubroGastoId = 3,
            CondicionPago = CondicionPago.Credito,
            FechaVencimiento = Hoy.AddDays(30),
            Activo = activo,
        };
        if (pagado)
            gasto.Pagos.Add(new PagoGasto { GastoId = id, Fecha = Hoy, Monto = 1000m });
        return gasto;
    }

    private static (GastosViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock,
                    Mock<IServicioGuardadoArchivo> guardadoMock,
                    Mock<IPdfExporter> pdfExporterMock,
                    Mock<IServicioAperturaArchivo> aperturaMock)
        Crear(
            IReadOnlyList<Gasto>? gastos = null, IReadOnlyList<LineaPoa>? lineasPoa = null,
            RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null,
            UsuarioSesion? usuarioSesion = null)
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>()))
            .ReturnsAsync(gastos ?? new List<Gasto>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));
        session.Setup(s => s.UsuarioActual).Returns(usuarioSesion ?? new UsuarioSesion(1, "admin", rol, null));

        var proveedores = new Mock<ICategoriaProveedorService>();
        var proveedoresDisponibles = new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
        };
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(proveedoresDisponibles);
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(proveedoresDisponibles);
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(lineasPoa ?? new List<LineaPoa>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();

        var vm = new GastosViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object, pdfExporter.Object, apertura.Object);
        return (vm, svc, nav, confirm, guardado, pdfExporter, apertura);
    }

    [Fact]
    public void FechaDesdeYHasta_SonDateTimeNullable_ParaBindearConCalendarDatePicker()
    {
        // Migración DatePicker (DateTimeOffset?) → CalendarDatePicker (DateTime?).
        Assert.Equal(typeof(DateTime?),
            typeof(GastosViewModel).GetProperty(nameof(GastosViewModel.FechaDesde))!.PropertyType);
        Assert.Equal(typeof(DateTime?),
            typeof(GastosViewModel).GetProperty(nameof(GastosViewModel.FechaHasta))!.PropertyType);
    }

    [Fact]
    public async Task CargarAsync_PopulaFilasConEstadoCalculado()
    {
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal("Pendiente", vm.Filas[0].Estado);
        Assert.Equal("Pagada", vm.Filas[1].Estado);
        Assert.Equal("Barraca X", vm.Filas[0].ProveedorNombre);
    }

    // ── FilasView: fix de ordenamiento por click en encabezados (Avalonia 12, regresión #21129) ──

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });

        await vm.CargarAsync();

        Assert.NotNull(vm.FilasView);
        Assert.IsType<DataGridCollectionView>(vm.FilasView);
        Assert.True(vm.FilasView.CanSort);
    }

    [Fact]
    public async Task FilasView_TrasCargarAsync_ReflejaLosItemsDeFilas()
    {
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });

        await vm.CargarAsync();

        Assert.Equal(vm.Filas.Count, vm.FilasView.Cast<GastoFila>().Count());
    }

    [Fact]
    public async Task FiltroDeEstado_FiltraEnMemoria()
    {
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });
        await vm.CargarAsync();

        vm.EstadoSeleccionado = "Pagada";
        await vm.FiltrarCommand.ExecuteAsync(null);

        var fila = Assert.Single(vm.Filas);
        Assert.Equal("Pagada", fila.Estado);
    }

    [Fact]
    public async Task FiltrarCommand_PasaLosFiltrosAlServicio()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        await vm.CargarAsync();
        vm.FechaDesde = new DateTime(2026, 7, 1);
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];

        await vm.FiltrarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.ProveedorId == 1 && f.FechaDesde != null)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task FiltrarCommand_FechaDesdeYHasta_SinCorrimientoDeDia()
    {
        // CalendarDatePicker bindea DateTime? (no DateTimeOffset?). El filtro se arma
        // fijando el Date elegido a medianoche UTC, SIN conversión real de huso horario
        // (el dominio de Finanzas no tiene componente horario): Desde = medianoche del
        // día, Hasta = el último tick del día elegido.
        var (vm, svc, _, _, _, _, _) = Crear();
        await vm.CargarAsync();
        vm.FechaDesde = new DateTime(2026, 7, 1);
        vm.FechaHasta = new DateTime(2026, 7, 31);

        await vm.FiltrarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.FechaDesde == new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            && f.FechaHasta == new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnularCommand_PideConfirmacionConMontoFormateado()
    {
        // Bug real (verificación orgánica): el mensaje mostraba el decimal crudo
        // ("850.5000") en vez del formato moneda es-UY que usan las grillas ("$ 850,50").
        var gasto = GastoDe(1, "Para anular");
        gasto.MontoTotal = 850.5000m;
        var (vm, _, _, confirm, _, _, _) = Crear(new List<Gasto> { gasto });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => s.Contains("$ 850,50") && !s.Contains("850.5000"))), Times.Once);
    }

    [Fact]
    public async Task AnularCommand_ConConfirmacion_AnulaYRecarga()
    {
        var (vm, svc, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Para anular") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(1), Times.Once);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task AnularCommand_ErrorDeRegla_SeInformaSinCrashear()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Con pagos", pagado: true) });
        svc.Setup(s => s.AnularAsync(1))
            .ThrowsAsync(new StockApp.Domain.Exceptions.ReglaDeNegocioException("Tiene pagos activos."));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Tiene pagos activos."), Times.Once);
    }

    // -- Deuda A parte 2: el dialogo de anulacion tiene que advertir que va a descontar stock,
    // SOLO cuando el gasto tiene movimientos asociados (Gasto.TieneMovimientosDeStock, ya
    // propagado end-to-end por GastoDto/GastoWire). --

    [Fact]
    public async Task AnularCommand_ConMovimientosDeStock_AdviertePeroDescuentaStock()
    {
        var gasto = GastoDe(1, "Para anular");
        gasto.TieneMovimientosDeStock = true;
        var (vm, _, _, confirm, _, _, _) = Crear(new List<Gasto> { gasto });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => s.Contains("stock"))), Times.Once);
    }

    [Fact]
    public async Task AnularCommand_SinMovimientosDeStock_NoAdvierteDescuentoDeStock()
    {
        // Un test que miente es peor que no tener test: si no hay movimientos de stock,
        // el dialogo NO debe insinuar que se va a descontar stock.
        var gasto = GastoDe(1, "Para anular");
        gasto.TieneMovimientosDeStock = false;
        var (vm, _, _, confirm, _, _, _) = Crear(new List<Gasto> { gasto });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => !s.Contains("stock"))), Times.Once);
    }

    [Fact]
    public async Task AnularCommand_ConMovimientosYPagoAutomatico_ElReintentoAdvierteAmbasCosasEnUnSoloMensaje()
    {
        // No deben ser tres dialogos seguidos que el operador clickea sin leer: el mensaje
        // FINAL (el que dispara la anulacion en cascada real) tiene que decir todo lo que va
        // a pasar en un solo PreguntarAsync — pago automatico Y descuento de stock juntos.
        var gasto = GastoDe(1, "Factura de luz");
        gasto.TieneMovimientosDeStock = true;
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { gasto });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new AnulacionRequierePagoAutomaticoConfirmadoException(1, 500m));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => (s.Contains("pago automatico") || s.Contains("automático")) && s.Contains("stock"))),
            Times.Once);
        svc.Verify(s => s.AnularAsync(1, true), Times.Once);
    }

    [Fact]
    public async Task AnularCommand_ConMovimientosYPagoAutomatico_AlRechazarElReintento_NoAnulaNada()
    {
        var gasto = GastoDe(1, "Factura de luz");
        gasto.TieneMovimientosDeStock = true;
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { gasto });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new AnulacionRequierePagoAutomaticoConfirmadoException(1, 500m));
        confirm.Setup(c => c.PreguntarAsync(It.Is<string>(
                s => (s.Contains("pago automatico") || s.Contains("automático")) && s.Contains("stock"))))
            .ReturnsAsync(false);
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(It.IsAny<int>(), true), Times.Never);
    }

    // -- Anulacion en cascada del pago automatico de contado (decision: no bloquear con un
    // 409 seco, ofrecer confirmar la baja del pago en vez de eso) --

    [Fact]
    public async Task AnularCommand_PagoAutomatico_OfreceConfirmacionConMontoFormateado()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new AnulacionRequierePagoAutomaticoConfirmadoException(1, 850.5000m));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => s.Contains("$ 850,50") && !s.Contains("850.5000"))), Times.Once);
    }

    [Fact]
    public async Task AnularCommand_PagoAutomatico_AlAceptar_ReintentaConfirmandoYAnula()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new AnulacionRequierePagoAutomaticoConfirmadoException(1, 500m));
        confirm.Setup(c => c.PreguntarAsync(It.Is<string>(s => s.Contains("pago automatico") || s.Contains("automático"))))
            .ReturnsAsync(true);
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(1, true), Times.Once);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task AnularCommand_PagoAutomatico_AlRechazar_NoReintentaNiAnulaNada()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new AnulacionRequierePagoAutomaticoConfirmadoException(1, 500m));
        confirm.Setup(c => c.PreguntarAsync(It.Is<string>(s => s.Contains("pago automatico") || s.Contains("automático"))))
            .ReturnsAsync(false);
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];
        var listarLlamadasPrevias = svc.Invocations.Count(i => i.Method.Name == nameof(IGastoService.ListarAsync));

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(1, true), Times.Never);
        svc.Verify(s => s.AnularAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Once);
        var listarLlamadasPosteriores = svc.Invocations.Count(i => i.Method.Name == nameof(IGastoService.ListarAsync));
        Assert.Equal(listarLlamadasPrevias, listarLlamadasPosteriores);
    }

    [Fact]
    public async Task AnularCommand_PagoManual_NoOfreceConfirmacion_MuestraElErrorTalCual()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Con pago manual", pagado: true) });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anula los pagos."));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(s => s.Contains("pago automatico") || s.Contains("automático"))), Times.Never);
        confirm.Verify(c => c.InformarAsync(
            "No se puede anular un gasto con pagos activos: primero anula los pagos."), Times.Once);
        svc.Verify(s => s.AnularAsync(It.IsAny<int>(), true), Times.Never);
    }

    [Fact]
    public async Task AnularCommand_PagoAutomatico_FalloDeRedEnElReintento_NoQuedaDiciendoQueSeAnulo()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        svc.Setup(s => s.AnularAsync(1, false))
            .ThrowsAsync(new AnulacionRequierePagoAutomaticoConfirmadoException(1, 500m));
        svc.Setup(s => s.AnularAsync(1, true))
            .ThrowsAsync(new ServidorNoDisponibleException());
        confirm.Setup(c => c.PreguntarAsync(It.Is<string>(s => s.Contains("pago automatico") || s.Contains("automático"))))
            .ReturnsAsync(true);
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];
        var listarLlamadasPrevias = svc.Invocations.Count(i => i.Method.Name == nameof(IGastoService.ListarAsync));

        await vm.AnularCommand.ExecuteAsync(null);

        var listarLlamadasPosteriores = svc.Invocations.Count(i => i.Method.Name == nameof(IGastoService.ListarAsync));
        Assert.Equal(listarLlamadasPrevias, listarLlamadasPosteriores);
        confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, nav, _, _, _, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarYPagos_ConSeleccion_NaveganConElGasto()
    {
        var (vm, _, nav, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Editable") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.EditarCommand.ExecuteAsync(null);
        await vm.PagosCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Once);
        nav.Verify(n => n.Navegar<PagosGastoViewModel>(
            It.IsAny<Action<PagosGastoViewModel>>()), Times.Once);
    }

    [Fact]
    public async Task GastoFila_Fecha_EsDateOnly_SinConversionDeHusoHorario()
    {
        // Bug real (verificación orgánica Fase 2): el export CSV mostraba la fecha corrida
        // un día para atrás porque GastoFila.Fecha era DateTime y CsvExporter convierte TODO
        // DateTime a hora local. Fecha debe ser DateOnly: no hay instante que convertir.
        var fechaUtc = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Con fecha límite", fechaUtc: fechaUtc) });

        await vm.CargarAsync();

        Assert.Equal(new DateOnly(2026, 7, 16), vm.Filas[0].Fecha);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.PagosCommand.CanExecute(null));
        Assert.False(vm.AnularCommand.CanExecute(null));
    }

    [Fact]
    public void FiltrarPorLineaPoa_SeteaLineaPoaSeleccionada()
    {
        var (vm, _, _, _, _, _, _) = Crear();
        var linea = new LineaPoa { Id = 5, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 };

        vm.FiltrarPorLineaPoa(linea);

        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);
    }

    [Fact]
    public async Task FiltrarPorLineaPoa_TrasCargarAsync_ComboQuedaMatcheadoPorId()
    {
        // Bug real (verificación orgánica F4): al navegar desde Control POA con doble click,
        // la LineaPoa que llega es una instancia distinta (otra consulta) a la que puebla
        // LineasPoaDisponibles en CargarAsync. El filtro de datos quedaba correcto, pero el
        // combo de la View (bindeado por referencia) se mostraba en "Todas".
        var lineaDeOtraConsulta = new LineaPoa { Id = 5, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 };
        var lineaDelCombo = new LineaPoa { Id = 5, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 };
        var (vm, _, _, _, _, _, _) = Crear(lineasPoa: new List<LineaPoa> { lineaDelCombo });

        vm.FiltrarPorLineaPoa(lineaDeOtraConsulta);
        await vm.CargarAsync();

        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);
        Assert.Same(lineaDelCombo, vm.LineaPoaSeleccionada);
        Assert.Contains(vm.LineaPoaSeleccionada!, vm.LineasPoaDisponibles);
    }

    [Fact]
    public async Task CargarAsync_SinFiltroPrevio_LineaPoaSeleccionadaQuedaNull()
    {
        // Flujo normal: abrir Gastos sin venir de Control POA debe seguir mostrando "Todas".
        var (vm, _, _, _, _, _, _) = Crear(lineasPoa: new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 },
        });

        await vm.CargarAsync();

        Assert.Null(vm.LineaPoaSeleccionada);
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCsvCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var (vm, _, _, confirm, guardado, _, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        guardado
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    // ── PuedeRegistrarPagos: gating del botón "Pagos" (bugfix 2026-08-15) ──────────────────
    // El botón no tenía NINGÚN gating por permiso. Basta VerFinanzas para entrar a la pantalla,
    // pero GastoService.RegistrarPagoAsync/AnularPagoAsync exigen Permisos.RegistrarPagos sin
    // condición — un Operador con VerFinanzas pero sin RegistrarPagos llenaba el formulario y
    // recién al guardar se comía un 403.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarPagos_PuedeRegistrarPagos_EsFalse()
    {
        var (vm, _, _, _, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarPagos);
    }

    [Fact]
    public void Operador_ConRegistrarPagos_PuedeRegistrarPagos_EsTrue()
    {
        var (vm, _, _, _, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarPagos });

        Assert.True(vm.PuedeRegistrarPagos);
    }

    [Fact]
    public void Admin_PuedeRegistrarPagos_EsTrue()
    {
        var (vm, _, _, _, _, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarPagos);
    }

    // ── PuedeRegistrarGastos: gating del botón "Anular" (bugfix 2026-08-15) ────────────────
    // El botón "Anular" solo estaba gateado por TieneSeleccion() (CanExecute), nunca por
    // permiso. GastoService.AnularAsync exige Permisos.RegistrarGastos sin condición — mismo
    // patrón que PuedeRegistrarPagos, calcado.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarGastos_PuedeRegistrarGastos_EsFalse()
    {
        var (vm, _, _, _, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarGastos);
    }

    [Fact]
    public void Operador_ConRegistrarGastos_PuedeRegistrarGastos_EsTrue()
    {
        var (vm, _, _, _, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        Assert.True(vm.PuedeRegistrarGastos);
    }

    [Fact]
    public void Admin_PuedeRegistrarGastos_EsTrue()
    {
        var (vm, _, _, _, _, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarGastos);
    }

    // ── bugfix 2026-08-15: un Operador con solo VerFinanzas se comía un 403 al abrir
    // "Gastos y facturas" — CargarAsync llamaba IProveedorService.ListarTodosAsync(), que en
    // el servidor exige GestionarTablasMaestras. El resto de los combos de este mismo método
    // (fuentes, rubros, líneas POA) ya consultan sus variantes *ActivasAsync/*ActivosAsync con
    // VerFinanzas — Proveedor era la excepción, por una asimetría real del código (spec Fase
    // 2b, alternativa C descartada: IProveedorService no tenía ListarActivasAsync todavía).

    [Fact]
    public async Task CargarAsync_ConsultaProveedoresActivos_NoTodosLosProveedores()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).ReturnsAsync(new List<Gasto>());
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(new List<Proveedor>());
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        var csv = new Mock<ICsvExporter>();
        var guardado = new Mock<IServicioGuardadoArchivo>();

        var vm = new GastosViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object,
            new Mock<IPdfExporter>().Object, new Mock<IServicioAperturaArchivo>().Object);

        await vm.CargarAsync();

        proveedores.Verify(p => p.ListarActivasAsync(), Times.Once);
        proveedores.Verify(p => p.ListarTodosAsync(), Times.Never);
    }

    // ── bugfix 2026-08-15: red de contención — CargarAsync no debe escalar un 403 ──────────
    // Un UnauthorizedAccessException que escapa de CargarAsync (llamado fire-and-forget desde
    // DataContextChanged de la View) termina en Dispatcher.UIThread.UnhandledException
    // (App.axaml.cs), que muestra el genérico "Ocurrió un error inesperado" y lo loguea a
    // crash.log como si fuera un bug real. Pero un 403 YA se avisa antes de llegar acá:
    // AuthTokenHandler dispara ApiSession.AccesoRevocado en cuanto ve el 403 en la respuesta
    // HTTP, y App.axaml.cs ya tiene un handler que informa "Tus permisos cambiaron...". Por
    // eso el catch acá es silencioso — mismo criterio que
    // MovimientoHistorialViewModel.RecalcularAsync ("silenciando UnauthorizedAccessException
    // porque el 403 ya lo avisa el manejo central de App.axaml.cs"), no el de
    // GastosViewModel.ReintentarAnulacionConPagoAutomaticoAsync (que sí informa, porque corre
    // dentro de un AsyncRelayCommand que NO llega al handler global).

    [Fact]
    public async Task CargarAsync_SiProveedoresLanzaUnauthorized_NoPropagaLaExcepcionYNoDuplicaAviso()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).ReturnsAsync(new List<Gasto>());
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.VerFinanzas });
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        var csv = new Mock<ICsvExporter>();
        var guardado = new Mock<IServicioGuardadoArchivo>();

        var vm = new GastosViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object,
            new Mock<IPdfExporter>().Object, new Mock<IServicioAperturaArchivo>().Object);

        await vm.CargarAsync();

        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    // ── Export PDF (spec 2026-09-18): peor caso del diseño, 11 columnas apaisado, 4 de texto
    // libre (Detalle, Proveedor, Fuente, Línea POA), filtro de Estado APLICADO EN MEMORIA. ──

    private static void ConfigurarPdfYGuardadoExitosos(
        Mock<IPdfExporter> pdfExporterMock, Mock<IServicioGuardadoArchivo> guardadoMock)
    {
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task ExportarPdfCommand_UsaLasOnceColumnasDeLaGrilla()
    {
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        await vm.CargarAsync();
        ConfigurarPdfYGuardadoExitosos(pdfExporterMock, guardadoMock);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        // Lista literal hardcodeada (nunca comparar contra GastosViewModel.ColumnasPdf: sería
        // tautológico, la mutación del VALOR de la constante nunca pondría el assert en rojo).
        var esperado = new[]
        {
            "Fecha", "ProveedorNombre", "NumeroFactura", "Detalle", "FuenteNombre", "RubroNombre",
            "LineaPoaNombre", "MontoTotal", "TotalPagado", "Saldo", "Estado",
        };
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<GastoFila>>(),
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(esperado) && cols.Count == 11),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinFilas_NoExporta()
    {
        var (vm, _, _, _, _, pdfExporterMock, _) = Crear();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_AvisoDeVolumen_SiCancelaNoExporta()
    {
        var muchos = Enumerable.Range(1, 501).Select(i => GastoDe(i, $"Gasto {i}")).ToList();
        var (vm, _, _, confirmMock, _, pdfExporterMock, _) = Crear(muchos);
        await vm.CargarAsync();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_FallaGuardado_InformaYNoPropaga()
    {
        var (vm, _, _, confirmMock, guardadoMock, pdfExporterMock, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(It.Is<string>(m => m.Contains("disco lleno"))), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoExitoso_OfreceAbrir()
    {
        var (vm, _, _, confirmMock, guardadoMock, pdfExporterMock, aperturaMock) =
            Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        await vm.CargarAsync();
        ConfigurarPdfYGuardadoExitosos(pdfExporterMock, guardadoMock);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.Is<string>(m => m.Contains("abrirlo"))), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var (vm, _, _, confirmMock, guardadoMock, pdfExporterMock, aperturaMock) =
            Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.Is<string>(m => m.Contains("abrirlo"))), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaMetadatosConTituloYUsuarioEmisor()
    {
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<GastoFila>, IReadOnlyList<string>, MetadatosDocumento>((_, _, m) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Equal("Gastos y facturas", metadatosCapturados!.Titulo);
        Assert.Equal("admin", metadatosCapturados.UsuarioEmisor);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConNombreCompleto_UsaNombreCompletoComoUsuarioEmisor()
    {
        // Tarea 21: el helper Crear() siempre construia UsuarioSesion con NombreCompleto null,
        // asi que ningun test ejercitaba la rama principal del fallback
        // (_session.UsuarioActual?.NombreCompleto ?? ... ?? "Sistema") -- solo la de "admin"
        // (NombreUsuario). Este test cubre la rama que el PDF le muestra a casi todo usuario real.
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear(
            new List<Gasto> { GastoDe(1, "Factura de luz") },
            usuarioSesion: new UsuarioSesion(1, "admin", RolUsuario.Admin, "Ana Pérez"));
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<GastoFila>, IReadOnlyList<string>, MetadatosDocumento>((_, _, m) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.Equal("Ana Pérez", metadatosCapturados!.UsuarioEmisor);
    }

    [Fact]
    public async Task ExportarPdfCommand_LaDescripcionDeFiltrosSoloIncluyeLosFiltrosActivos()
    {
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<GastoFila>, IReadOnlyList<string>, MetadatosDocumento>((_, _, m) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Contains("01/01/2026", metadatosCapturados!.DescripcionFiltros);
        Assert.Contains("31/01/2026", metadatosCapturados.DescripcionFiltros);
        // Sin proveedor/fuente/rubro/línea/estado seleccionados, esas etiquetas NO aparecen.
        Assert.DoesNotContain("Proveedor:", metadatosCapturados.DescripcionFiltros);
        Assert.DoesNotContain("Fuente:", metadatosCapturados.DescripcionFiltros);
        Assert.DoesNotContain("Rubro:", metadatosCapturados.DescripcionFiltros);
        Assert.DoesNotContain("Línea POA:", metadatosCapturados.DescripcionFiltros);
        Assert.DoesNotContain("Estado:", metadatosCapturados.DescripcionFiltros);
    }

    /// <summary>
    /// Cubre la particularidad crítica de esta pantalla (spec 2026-09-18): <c>Estado</c> es un
    /// valor CALCULADO (<see cref="Gasto.CalcularEstado"/>) que no existe como columna en la
    /// base — el filtro de estado se aplica EN MEMORIA sobre <see cref="GastosViewModel.Filas"/>
    /// dentro de <c>FiltrarAsync</c> (no en el servidor). Si el PDF exportara la respuesta cruda
    /// del servicio en vez de <c>Filas</c>, este test lo detectaría: con "Pagada" seleccionado,
    /// solo debe llegar al exportador la fila ya pagada, nunca la pendiente.
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_ConEstadoSeleccionado_SoloExportaFilasDeEseEstadoYLoMencionaEnFiltros()
    {
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });
        await vm.CargarAsync();
        vm.EstadoSeleccionado = "Pagada";
        await vm.FiltrarCommand.ExecuteAsync(null);

        IEnumerable<GastoFila>? filasCapturadas = null;
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<GastoFila>, IReadOnlyList<string>, MetadatosDocumento>(
                (items, _, m) => { filasCapturadas = items; metadatosCapturados = m; })
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(filasCapturadas);
        var fila = Assert.Single(filasCapturadas!);
        Assert.Equal("Pagada", fila.Estado);
        Assert.NotNull(metadatosCapturados);
        Assert.Contains("Estado: Pagada", metadatosCapturados!.DescripcionFiltros);
    }
}
