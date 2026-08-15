using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
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
                    Mock<IServicioGuardadoArchivo> guardadoMock)
        Crear(
            IReadOnlyList<Gasto>? gastos = null, IReadOnlyList<LineaPoa>? lineasPoa = null,
            RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>()))
            .ReturnsAsync(gastos ?? new List<Gasto>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
        });
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

        var vm = new GastosViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object);
        return (vm, svc, nav, confirm, guardado);
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
        var (vm, _, _, _, _) = Crear(new List<Gasto>
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
        var (vm, _, _, _, _) = Crear(new List<Gasto>
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
        var (vm, _, _, _, _) = Crear(new List<Gasto>
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
        var (vm, _, _, _, _) = Crear(new List<Gasto>
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
        var (vm, svc, _, _, _) = Crear();
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
        var (vm, svc, _, _, _) = Crear();
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
        var (vm, _, _, confirm, _) = Crear(new List<Gasto> { gasto });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => s.Contains("$ 850,50") && !s.Contains("850.5000"))), Times.Once);
    }

    [Fact]
    public async Task AnularCommand_ConConfirmacion_AnulaYRecarga()
    {
        var (vm, svc, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Para anular") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(1), Times.Once);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task AnularCommand_ErrorDeRegla_SeInformaSinCrashear()
    {
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { GastoDe(1, "Con pagos", pagado: true) });
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
        var (vm, _, _, confirm, _) = Crear(new List<Gasto> { gasto });
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
        var (vm, _, _, confirm, _) = Crear(new List<Gasto> { gasto });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { gasto });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { gasto });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { GastoDe(1, "Con pago manual", pagado: true) });
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
        var (vm, svc, _, confirm, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
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
        var (vm, _, nav, _, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarYPagos_ConSeleccion_NaveganConElGasto()
    {
        var (vm, _, nav, _, _) = Crear(new List<Gasto> { GastoDe(1, "Editable") });
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
        var (vm, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Con fecha límite", fechaUtc: fechaUtc) });

        await vm.CargarAsync();

        Assert.Equal(new DateOnly(2026, 7, 16), vm.Filas[0].Fecha);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.PagosCommand.CanExecute(null));
        Assert.False(vm.AnularCommand.CanExecute(null));
    }

    [Fact]
    public void FiltrarPorLineaPoa_SeteaLineaPoaSeleccionada()
    {
        var (vm, _, _, _, _) = Crear();
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
        var (vm, _, _, _, _) = Crear(lineasPoa: new List<LineaPoa> { lineaDelCombo });

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
        var (vm, _, _, _, _) = Crear(lineasPoa: new List<LineaPoa>
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
        var (vm, _, _, confirm, guardado) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
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
        var (vm, _, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarPagos);
    }

    [Fact]
    public void Operador_ConRegistrarPagos_PuedeRegistrarPagos_EsTrue()
    {
        var (vm, _, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarPagos });

        Assert.True(vm.PuedeRegistrarPagos);
    }

    [Fact]
    public void Admin_PuedeRegistrarPagos_EsTrue()
    {
        var (vm, _, _, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarPagos);
    }
}
