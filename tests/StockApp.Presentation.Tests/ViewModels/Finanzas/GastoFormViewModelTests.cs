using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Helpers;
using StockApp.Application.Proyectos;
using StockApp.Presentation.Tests.Helpers;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class GastoFormViewModelTests
{
    private static (GastoFormViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null,
               Func<Task<IReadOnlyList<Proveedor>>>? proveedoresActivos = null,
               List<FuenteFinanciamiento>? fuentesActivas = null,
               OpcionProyectoDto[]? proyectosAbiertos = null)
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()))
            .ReturnsAsync(new ResultadoGastoDto(7, null));
        svc.Setup(s => s.ModificarAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>()))
            .ReturnsAsync(new ResultadoGastoDto(7, null));

        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
            new() { Id = 2, Nombre = "Dado de baja", Activo = false },
        });
        // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15, mismo criterio que
        // GastosViewModel/ec429d5): el servidor ya filtra a Activo=true, sin filtro repetido acá.
        proveedores.Setup(p => p.ListarActivasAsync())
            .Returns(proveedoresActivos ?? (() => Task.FromResult<IReadOnlyList<Proveedor>>(new List<Proveedor>
            {
                new() { Id = 1, Nombre = "Barraca X", Activo = true },
            })));
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(fuentesActivas ?? new List<FuenteFinanciamiento>
        {
            new() { Id = 2, Nombre = "Literal B", Caja = Caja.Local, Activo = true },
            new() { Id = 4, Nombre = "Literal POA", Caja = Caja.Poa, Activo = true },
        });
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>
        {
            new() { Id = 3, Codigo = 3, Nombre = "Materiales", Activo = true },
        });
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true },
        });

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(permisos as HashSet<string> ?? new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new GastoFormViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object, ProyectosDeTest.Servicio(proyectosAbiertos ?? Array.Empty<OpcionProyectoDto>()),
            nav.Object, confirm.Object, adjuntosPanel);
        return (vm, svc, nav, confirm);
    }

    private static async Task CompletarFormularioValidoAsync(GastoFormViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Materiales de obra";
        vm.MontoTexto = "1.500,50";   // es-UY: miles con punto, decimales con coma
    }

    [Fact]
    public void FechaSeleccionadaYVencimiento_SonDateTimeNullable_ParaBindearConCalendarDatePicker()
    {
        // Migración DatePicker (DateTimeOffset?) → CalendarDatePicker (DateTime?), spec de
        // reemplazo del spinner de 3 columnas por el calendario desplegable.
        Assert.Equal(typeof(DateTime?),
            typeof(GastoFormViewModel).GetProperty(nameof(GastoFormViewModel.FechaSeleccionada))!.PropertyType);
        Assert.Equal(typeof(DateTime?),
            typeof(GastoFormViewModel).GetProperty(nameof(GastoFormViewModel.FechaVencimientoSeleccionada))!.PropertyType);
    }

    [Fact]
    public async Task InicializarAsync_SoloOfreceProveedoresActivos()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        var proveedor = Assert.Single(vm.ProveedoresDisponibles);
        Assert.Equal("Barraca X", proveedor.Nombre);
    }

    // ── bugfix 2026-08-15: un Operador de finanzas (solo VerFinanzas + RegistrarGastos) se
    // comía un 403 y "Ocurrió un error inesperado" al abrir "Nuevo gasto" — InicializarAsync
    // llamaba IProveedorService.ListarTodosAsync(), que en el servidor exige
    // GestionarTablasMaestras. Mismo bug (y mismo fix) que GastosViewModel.CargarAsync (ec429d5):
    // pasa a ListarActivasAsync(), que ya filtra a Activo=true del lado del servidor con
    // VerFinanzas.

    [Fact]
    public async Task InicializarAsync_ConsultaProveedoresActivos_NoTodosLosProveedores()
    {
        var svc = new Mock<IGastoService>();
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
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new GastoFormViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object, ProyectosDeTest.Servicio(),
            nav.Object, confirm.Object, adjuntosPanel);

        await vm.InicializarAsync();

        proveedores.Verify(p => p.ListarActivasAsync(), Times.Once);
        proveedores.Verify(p => p.ListarTodosAsync(), Times.Never);
    }

    // ── bugfix 2026-08-15: red de contención — InicializarAsync no debe escalar un 403 ─────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c): InicializarAsync la dispara la
    // View (DataContextChanged) fire-and-forget — un UnauthorizedAccessException no atrapado
    // escala a Dispatcher.UIThread.UnhandledException (App.axaml.cs), que muestra el genérico
    // "Ocurrió un error inesperado" y lo loguea a crash.log como si fuera un bug real. El 403 ya
    // se avisó ANTES de llegar acá (AuthTokenHandler + App.axaml.cs), así que el catch es
    // silencioso.

    [Fact]
    public async Task InicializarAsync_SiProveedoresLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svc = new Mock<IGastoService>();
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
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new GastoFormViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object, ProyectosDeTest.Servicio(),
            nav.Object, confirm.Object, adjuntosPanel);

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
    }

    [Fact]
    public async Task Guardar_ParseaElMontoConCulturaEsUY()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(
            It.Is<Gasto>(g => g.MontoTotal == 1500.50m && g.CondicionPago == CondicionPago.Contado),
            new PagoContadoDto(Caja.Local, 2), null), Times.Once);
    }

    [Fact]
    public async Task Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "abc";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_Credito_MandaVencimiento()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = DateTime.UtcNow.AddDays(30);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<Gasto>(g =>
            g.CondicionPago == CondicionPago.Credito && g.FechaVencimiento != null), null, null), Times.Once);
    }

    [Fact]
    public async Task Guardar_Contado_EnviaPagoContadoConLaFuente()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), new PagoContadoDto(Caja.Local, 2), null), Times.Once);
    }

    [Fact]
    public async Task Guardar_Credito_EnviaPagoContadoNull_SinElegirFuente()
    {
        var (vm, svc, _, _) = Crear();
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Materiales a crédito";
        vm.MontoTexto = "1.500,50";
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = DateTime.UtcNow.AddDays(30);

        Assert.Null(vm.FuenteSeleccionada);
        Assert.True(vm.GuardarCommand.CanExecute(null));
        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(
            It.Is<Gasto>(g => g.CondicionPago == CondicionPago.Credito), null, null), Times.Once);
    }

    [Fact]
    public async Task Contado_SinFuente_NoSePuedeGuardar_YAlPasarACreditoSi()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Materiales";
        vm.MontoTexto = "100";

        Assert.False(vm.GuardarCommand.CanExecute(null));   // contado sin fuente elegida

        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = DateTime.UtcNow.AddDays(30);

        Assert.True(vm.GuardarCommand.CanExecute(null));    // crédito no lleva fuente
    }

    [Fact]
    public async Task Editar_Contado_PrecargaLaFuenteDelPagoAutomatico_YConservaSuCaja()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaEditar(GastoExistente(caja: Caja.Poa, fuenteId: 4));
        await vm.InicializarAsync();

        Assert.Equal(4, vm.FuenteSeleccionada?.Id);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9), new PagoContadoDto(Caja.Poa, 4)), Times.Once);
    }

    [Fact]
    public async Task Guardar_FechaSeleccionadaYVencimiento_SinCorrimientoDeDia()
    {
        // CalendarDatePicker bindea DateTime? (no DateTimeOffset?, ver migración de
        // DatePicker→CalendarDatePicker). El dominio de Finanzas no tiene componente
        // horario: la fecha elegida debe guardarse tal cual, sin conversión de huso
        // horario (a diferencia de MovimientoHistorialViewModel, que sí convierte
        // local→UTC porque ahí Fecha es un instante real).
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.FechaSeleccionada = new DateTime(2026, 7, 20);
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = new DateTime(2026, 8, 19);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<Gasto>(g =>
            g.Fecha == new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc)
            && g.FechaVencimiento == new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc)),
            null, null), Times.Once);
    }

    [Fact]
    public async Task Guardar_ConAdvertenciaDeSobregiro_LaInformaYNavega()
    {
        var (vm, svc, nav, confirm) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), null))
            .ReturnsAsync(new ResultadoGastoDto(7, "Atención: sobregiro POA"));
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Atención: sobregiro POA"), Times.Once);
        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
    }

    [Fact]
    public async Task Guardar_ReglaDeNegocio_MuestraMensajeError()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), null))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-1' para ese proveedor."));
        await CompletarFormularioValidoAsync(vm);
        vm.NumeroFactura = "A-1";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe la factura 'A-1' para ese proveedor.", vm.MensajeError);
    }

    [Fact]
    public async Task CargarParaEditar_PrecargaLosCampos()
    {
        var (vm, svc, _, _) = Crear();
        var gasto = new Gasto
        {
            Id = 9, ProveedorId = 1, NumeroFactura = "A-9", Detalle = "Histórico",
            Fecha = DateTime.UtcNow, MontoTotal = 2000m,
            RubroGastoId = 3, LineaPoaId = 5,
            CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(10),
        };
        vm.CargarParaEditar(gasto);
        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.Equal("Histórico", vm.Detalle);
        Assert.True(vm.EsCredito);
        Assert.Equal("A-9", vm.NumeroFactura);
        Assert.NotNull(vm.LineaPoaSeleccionada);

        await vm.GuardarCommand.ExecuteAsync(null);
        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9), null), Times.Once);
    }

    [Fact]
    public async Task CargarParaEditar_FechaYVencimiento_SinCorrimientoDeDia()
    {
        // Round-trip: la fecha que carga CargarParaEditar (gasto.Fecha, ya en UTC
        // medianoche) tiene que reflejarse igual en FechaSeleccionada (DateTime?,
        // ver migración a CalendarDatePicker) y volver idéntica al guardar.
        var (vm, svc, _, _) = Crear();
        var gasto = new Gasto
        {
            Id = 11, ProveedorId = 1, Detalle = "Histórico con fecha",
            Fecha = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), MontoTotal = 500m,
            RubroGastoId = 3,
            CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        vm.CargarParaEditar(gasto);
        await vm.InicializarAsync();

        Assert.Equal(new DateTime(2026, 3, 15), vm.FechaSeleccionada!.Value.Date);
        Assert.Equal(new DateTime(2026, 4, 15), vm.FechaVencimientoSeleccionada!.Value.Date);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g =>
            g.Fecha == new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)
            && g.FechaVencimiento == new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)), null), Times.Once);
    }

    [Fact]
    public async Task CargarDesdeEntrada_PrecargaMontoYVinculaMovimiento()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarDesdeEntrada(movimientoId: 40, montoSugerido: 2500m);
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Factura de la entrada";

        Assert.Equal("2.500,00", vm.MontoTexto);   // precargado, editable (fletes, redondeos)

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(),
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
    }

    [Fact]
    public async Task CargarDesdeEntrada_FacturaYaExistente_OfreceAsociarLaExistente()
    {
        var (vm, svc, nav, confirm) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-1' para ese proveedor."));
        svc.Setup(s => s.ObtenerPorProveedorYFacturaAsync(1, "A-1", null))
            .ReturnsAsync(new Gasto { Id = 55, ProveedorId = 1, NumeroFactura = "A-1", Detalle = "Existente" });
        vm.CargarDesdeEntrada(movimientoId: 40, montoSugerido: 100m);
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Factura repetida";
        vm.NumeroFactura = "A-1";

        await vm.GuardarCommand.ExecuteAsync(null);

        // Preguntó, el usuario aceptó (mock devuelve true) ⇒ asocia el movimiento a la existente
        svc.Verify(s => s.AsociarMovimientosAsync(55,
            It.Is<IReadOnlyList<int>>(ids => ids[0] == 40)), Times.Once);
        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
    }

    [Fact]
    public async Task CargarParaEditar_InicializaElPanelDeAdjuntosConElGastoId()
    {
        var svc = new Mock<IGastoService>();
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        var adjuntosService = new Mock<IAdjuntoService>();
        adjuntosService.Setup(a => a.ListarPorGastoAsync(42))
            .ReturnsAsync(new List<AdjuntoDto>
            {
                new(1, "a.pdf", "application/pdf", 10, 42, null, DateTime.UtcNow),
            });
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var adjuntosPanel = new AdjuntosPanelViewModel(
            adjuntosService.Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new GastoFormViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object, ProyectosDeTest.Servicio(),
            nav.Object, confirm.Object, adjuntosPanel);

        var gasto = new Gasto
        {
            Id = 42, ProveedorId = 1, Detalle = "Con adjuntos", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, RubroGastoId = 1,
        };

        vm.CargarParaEditar(gasto);
        await Task.Delay(1); // deja correr el fire-and-forget de InicializarAsync del panel

        Assert.NotNull(vm.AdjuntosPanel);
        Assert.Same(adjuntosPanel, vm.AdjuntosPanel);
        adjuntosService.Verify(a => a.ListarPorGastoAsync(42), Times.Once);
        Assert.Single(vm.AdjuntosPanel.Items);
    }

    // ── PuedeRegistrarGastos: gating del botón "Guardar" (bugfix 2026-08-15) ───────────────
    // El botón Guardar no tenía NINGÚN gating por permiso. GastoService.AltaAsync/
    // ModificarAsync exigen Permisos.RegistrarGastos sin condición — se llega a este
    // formulario desde GastosView ("Nuevo gasto"/"Editar"), que solo exige VerFinanzas.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarGastos_PuedeRegistrarGastos_EsFalse()
    {
        var (vm, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarGastos);
    }

    [Fact]
    public void Operador_ConRegistrarGastos_PuedeRegistrarGastos_EsTrue()
    {
        var (vm, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        Assert.True(vm.PuedeRegistrarGastos);
    }

    [Fact]
    public void Admin_PuedeRegistrarGastos_EsTrue()
    {
        var (vm, _, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarGastos);
    }

    // ── Formato es-UY (decisión 2026-10-01): parseo seguro ───────────────────

    [Fact]
    public async Task Guardar_MontoConPuntoDecimal_SeRechazaConMensajeClaroSinLlamarAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "5.4";   // con NumberStyles.Number + es-UY se guardaba como 54

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("El monto total no es un número válido. Usá coma para los decimales: 5,4", vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public void NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos()
    {
        var (vm, _, _, _) = Crear();
        var avisos = new List<string?>();
        vm.AdjuntosPanel.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.NotificarPermisosActualizados();

        Assert.Contains(avisos, n => string.IsNullOrEmpty(n) || n == nameof(AdjuntosPanelViewModel.PuedeModificar));
    }

    // ── Carga inicial: sin conexión y sin permiso ───────────────────────────────

    private static Func<Task<IReadOnlyList<Proveedor>>> FallaUnaVez(Exception falla)
    {
        var intentos = 0;
        return () => ++intentos == 1
            ? Task.FromException<IReadOnlyList<Proveedor>>(falla)
            : Task.FromResult<IReadOnlyList<Proveedor>>(new List<Proveedor> { new() { Id = 1, Nombre = "Barraca X", Activo = true } });
    }

    [Fact]
    public async Task InicializarAsync_ServidorCaido_MarcaSinConexion_SinModalNiExcepcion()
    {
        var (vm, _, _, confirm) = Crear(proveedoresActivos: FallaUnaVez(new ServidorNoDisponibleException()));

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
        Assert.True(vm.SinConexion);
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reintentar_TrasLaCaida_CargaLosCombos()
    {
        var (vm, _, _, _) = Crear(proveedoresActivos: FallaUnaVez(new ServidorNoDisponibleException()));
        await vm.InicializarAsync();

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Single(vm.ProveedoresDisponibles);
        vm.CajaSeleccionada = Caja.Local;   // el combo de literales se llena con los de la caja elegida
        Assert.Single(vm.FuentesDisponibles);
        Assert.Single(vm.RubrosDisponibles);
    }

    [Fact]
    public async Task Reintentar_EnEdicion_ResuelveLasSeleccionesDelGasto()
    {
        var (vm, _, _, _) = Crear(proveedoresActivos: FallaUnaVez(new ServidorNoDisponibleException()));
        vm.CargarParaEditar(GastoExistente());
        await vm.InicializarAsync();

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.ProveedorSeleccionado?.Id);
        Assert.Equal(2, vm.FuenteSeleccionada?.Id);
        Assert.Equal(3, vm.RubroSeleccionado?.Id);
    }

    [Fact]
    public async Task InicializarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, _, _, confirm) = Crear(proveedoresActivos: FallaUnaVez(new UnauthorizedAccessException("403")));

        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(GastoFormViewModel.MensajeSinPermisoFormulario, vm.MensajeSinPermiso);
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InicializarAsync_MientrasEspera_CargandoEsTrue()
    {
        var pendiente = new TaskCompletionSource<IReadOnlyList<Proveedor>>();
        var (vm, _, _, _) = Crear(proveedoresActivos: () => pendiente.Task);

        var carga = vm.InicializarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(new List<Proveedor>());
        await carga;
        Assert.False(vm.Cargando);
    }

    // ── Caja y literal explícitos del pago de contado (Caja POA) ────────────────

    private static FuenteFinanciamiento Fuente(int id, string nombre, Caja caja, bool activo = true) =>
        new() { Id = id, Nombre = nombre, Caja = caja, Activo = activo };

    [Fact]
    public async Task Alta_ArrancaSinCajaNiLiteral_YGuardarDeshabilitado()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();

        Assert.Null(vm.CajaSeleccionada);
        Assert.Null(vm.FuenteSeleccionada);
        Assert.Empty(vm.FuentesDisponibles);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Contado_SinCaja_AunConLiteralYTodoLoDemasValido_NoSePuedeGuardar()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        var literal = vm.FuentesDisponibles[0];
        vm.CajaSeleccionada = null;        // limpia el literal de la caja anterior...
        vm.FuenteSeleccionada = literal;   // ...y se lo devolvemos: falta SOLO la caja

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Equal(GastoFormViewModel.MensajeCajaObligatoria, vm.ErrorDe(nameof(GastoFormViewModel.CajaSeleccionada)));
        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.FuenteSeleccionada)));
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public async Task Contado_ConCajaSinLiteral_AunConTodoLoDemasValido_NoSePuedeGuardar()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.FuenteSeleccionada = null;      // la caja queda elegida: falta SOLO el literal

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.CajaSeleccionada)));
        Assert.Equal(GastoFormViewModel.MensajeFuenteObligatoria, vm.ErrorDe(nameof(GastoFormViewModel.FuenteSeleccionada)));
    }

    [Fact]
    public async Task Credito_OcultaCajaFuenteYLinea_YNoExigeNiAvisa()
    {
        var (vm, _, _, _) = Crear(fuentesActivas: new List<FuenteFinanciamiento> { Fuente(2, "Literal B", Caja.Local) });
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Poa;     // POA sin literales: avisa en contado
        Assert.NotNull(vm.AvisoSinLiterales);
        Assert.True(vm.MostrarCajaYFuente);
        Assert.True(vm.MostrarLineaPoa);

        vm.EsCredito = true;

        Assert.False(vm.MostrarCajaYFuente);
        Assert.False(vm.MostrarLineaPoa);
        Assert.Null(vm.AvisoSinLiterales);
        vm.MarcarTocado(nameof(GastoFormViewModel.CajaSeleccionada));
        vm.MarcarTocado(nameof(GastoFormViewModel.FuenteSeleccionada));
        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.FuenteSeleccionada)));
    }

    [Fact]
    public async Task Credito_SinCajaNiLiteral_SePuedeGuardar_YNoLosManda()
    {
        var (vm, svc, _, _) = Crear();
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "A crédito";
        vm.MontoTexto = "100";
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = DateTime.UtcNow.AddDays(30);

        Assert.Null(vm.CajaSeleccionada);
        Assert.True(vm.GuardarCommand.CanExecute(null));
        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), null, null), Times.Once);
    }

    [Fact]
    public async Task ContadoPoa_MuestraLaLineaPoa_YLocalNo()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        Assert.False(vm.MostrarLineaPoa);   // sin caja

        vm.CajaSeleccionada = Caja.Local;
        Assert.False(vm.MostrarLineaPoa);

        vm.CajaSeleccionada = Caja.Poa;
        Assert.True(vm.MostrarLineaPoa);
    }

    [Fact]
    public async Task ElegirUnaLineaPoa_NoDeduceLaCaja()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();

        vm.LineaPoaSeleccionada = vm.LineasPoaDisponibles[0];

        Assert.Null(vm.CajaSeleccionada);
    }

    [Fact]
    public async Task PasarDePoaALocal_LimpiaLaLineaPoaElegida()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Poa;
        vm.LineaPoaSeleccionada = vm.LineasPoaDisponibles[0];

        vm.CajaSeleccionada = Caja.Local;

        Assert.Null(vm.LineaPoaSeleccionada);
    }

    [Fact]
    public async Task PasarACredito_LimpiaLaLineaPoaElegida()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Poa;
        vm.LineaPoaSeleccionada = vm.LineasPoaDisponibles[0];

        vm.EsCredito = true;

        Assert.Null(vm.LineaPoaSeleccionada);
    }

    [Fact]
    public async Task CambiarCaja_FiltraYLimpiaLaFuenteIncompatible()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Local;
        Assert.Equal(new[] { 2 }, vm.FuentesDisponibles.Select(f => f.Id));
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];

        vm.CajaSeleccionada = Caja.Poa;

        Assert.Equal(new[] { 4 }, vm.FuentesDisponibles.Select(f => f.Id));
        Assert.Null(vm.FuenteSeleccionada);
    }

    [Fact]
    public async Task CajaSinLiterales_Avisa()
    {
        var (vm, _, _, _) = Crear(fuentesActivas: new List<FuenteFinanciamiento> { Fuente(2, "Literal B", Caja.Local) });
        await vm.InicializarAsync();

        vm.CajaSeleccionada = Caja.Poa;

        Assert.Equal(LiteralesPorCaja.Aviso(Caja.Poa, 0), vm.AvisoSinLiterales);
        Assert.Empty(vm.FuentesDisponibles);
    }

    [Fact]
    public async Task Guardar_ContadoPoa_EnviaPagoContadoPoaConLaFuente()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.CajaSeleccionada = Caja.Poa;
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), new PagoContadoDto(Caja.Poa, 4), null), Times.Once);
    }

    [Fact]
    public async Task Editar_ContadoPoa_PrecargaCajaFuenteYLinea()
    {
        var (vm, svc, _, _) = Crear();
        var gasto = GastoExistente(caja: Caja.Poa, fuenteId: 4);
        gasto.LineaPoaId = 5;
        vm.CargarParaEditar(gasto);
        await vm.InicializarAsync();

        Assert.Equal(Caja.Poa, vm.CajaSeleccionada);
        Assert.Equal(4, vm.FuenteSeleccionada?.Id);
        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);
        Assert.True(vm.MostrarLineaPoa);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9 && g.LineaPoaId == 5), new PagoContadoDto(Caja.Poa, 4)), Times.Once);
    }

    [Fact]
    public async Task Editar_ContadoConLiteralDadoDeBaja_SigueElegidoEnElComboDeSuCaja()
    {
        var (vm, _, _, _) = Crear();
        var gasto = GastoExistente(caja: Caja.Poa, fuenteId: 9);
        gasto.PagoAutomatico!.FuenteFinanciamiento = Fuente(9, "Literal viejo", Caja.Poa, activo: false);
        vm.CargarParaEditar(gasto);

        await vm.InicializarAsync();

        Assert.Equal(9, vm.FuenteSeleccionada?.Id);
        Assert.Contains(vm.FuentesDisponibles, f => f.Id == 9);
        vm.CajaSeleccionada = Caja.Local;
        Assert.DoesNotContain(vm.FuentesDisponibles, f => f.Id == 9);
    }

    [Fact]
    public async Task Editar_Credito_NoPideCajaNiLiteral()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(GastoExistente(condicion: CondicionPago.Credito));
        await vm.InicializarAsync();

        Assert.Null(vm.CajaSeleccionada);
        Assert.False(vm.MostrarCajaYFuente);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    // ── Fuente legada de otra caja: la migración dejó pagos en caja Local con literales hoy POA ──

    [Fact]
    public async Task Editar_PagoLocalConLiteralPoa_SigueElegido_YSeGuarda()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaEditar(GastoExistente(caja: Caja.Local, fuenteId: 4));   // literal 4 es de la caja POA

        await vm.InicializarAsync();

        Assert.Equal(Caja.Local, vm.CajaSeleccionada);
        Assert.Equal(4, vm.FuenteSeleccionada?.Id);
        Assert.Contains(vm.FuentesDisponibles, f => f.Id == 4);
        Assert.True(vm.GuardarCommand.CanExecute(null));

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9), new PagoContadoDto(Caja.Local, 4)), Times.Once);
    }

    [Fact]
    public async Task Editar_PagoLocalConLiteralPoa_VolverALaCajaOriginal_OfreceElLiteralPeroNoLoReelige()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(GastoExistente(caja: Caja.Local, fuenteId: 4));
        await vm.InicializarAsync();

        vm.CajaSeleccionada = Caja.Poa;
        vm.CajaSeleccionada = Caja.Local;   // el literal es de POA: al salir de Local se limpia y al volver no se reelige

        Assert.Contains(vm.FuentesDisponibles, f => f.Id == 4);
        Assert.Null(vm.FuenteSeleccionada);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Editar_LiteralHistoricoDeBaja_SoloSeOfreceEnLaCajaOriginal()
    {
        var (vm, _, _, _) = Crear();
        var gasto = GastoExistente(caja: Caja.Local, fuenteId: 9);
        gasto.PagoAutomatico!.FuenteFinanciamiento = Fuente(9, "Literal viejo POA", Caja.Poa, activo: false);
        vm.CargarParaEditar(gasto);
        await vm.InicializarAsync();

        Assert.Equal(9, vm.FuenteSeleccionada?.Id);
        vm.CajaSeleccionada = Caja.Poa;
        Assert.DoesNotContain(vm.FuentesDisponibles, f => f.Id == 9);   // POA no es su caja original
    }

    // ── Crédito limpia caja y literal: al volver a contado se eligen de nuevo ──

    [Fact]
    public async Task ContadoACreditoYDeVuelta_NoQuedanCajaNiLiteralElegidos_YGuardarSeDeshabilita()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        Assert.True(vm.GuardarCommand.CanExecute(null));

        vm.EsCredito = true;
        vm.EsCredito = false;

        Assert.Null(vm.CajaSeleccionada);
        Assert.Null(vm.FuenteSeleccionada);
        Assert.Empty(vm.FuentesDisponibles);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    // ── Línea POA huérfana de un gasto legado ──

    private static Gasto LegadoConLinea(Caja caja = Caja.Local, CondicionPago condicion = CondicionPago.Contado)
    {
        var gasto = GastoExistente(condicion: condicion, caja: caja);
        gasto.LineaPoaId = 5;
        return gasto;
    }

    [Fact]
    public async Task Editar_LegadoLocalConLinea_MuestraLaLineaEnSoloLectura()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(LegadoConLinea());

        await vm.InicializarAsync();

        Assert.True(vm.LineaLegadaHuerfana);
        Assert.False(vm.MostrarLineaPoa);
        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);   // se conserva al cargar
        Assert.Equal("Este gasto tiene una línea POA que ya no corresponde a su caja. Se conserva tal cual.",
            GastoFormViewModel.TextoLineaLegadaHuerfana);
    }

    [Fact]
    public async Task Editar_ContadoPoaConLinea_NoEsHuerfana()
    {
        var (vm, _, _, _) = Crear();
        var gasto = GastoExistente(caja: Caja.Poa, fuenteId: 4);
        gasto.LineaPoaId = 5;
        vm.CargarParaEditar(gasto);

        await vm.InicializarAsync();

        Assert.False(vm.LineaLegadaHuerfana);
        Assert.True(vm.MostrarLineaPoa);
    }

    [Fact]
    public async Task Editar_LegadoLocalConLinea_QuitarLinea_ElGuardarMandaNull()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaEditar(LegadoConLinea());
        await vm.InicializarAsync();

        vm.QuitarLineaLegadaCommand.Execute(null);

        Assert.Null(vm.LineaPoaSeleccionada);
        Assert.False(vm.LineaLegadaHuerfana);
        await vm.GuardarCommand.ExecuteAsync(null);
        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9 && g.LineaPoaId == null), new PagoContadoDto(Caja.Local, 2)), Times.Once);
    }

    [Fact]
    public async Task Editar_LegadoLocalConLinea_SoloDetalle_SeGuardaConLaLinea()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaEditar(LegadoConLinea());
        await vm.InicializarAsync();

        vm.Detalle = "Detalle corregido";
        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(
            It.Is<Gasto>(g => g.Id == 9 && g.LineaPoaId == 5 && g.Detalle == "Detalle corregido"), new PagoContadoDto(Caja.Local, 2)), Times.Once);
    }

    [Fact]
    public async Task Editar_CreditoLegadoConLinea_MuestraSoloLecturaYSePuedeQuitar()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaEditar(LegadoConLinea(condicion: CondicionPago.Credito));
        await vm.InicializarAsync();

        Assert.True(vm.LineaLegadaHuerfana);
        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);

        vm.QuitarLineaLegadaCommand.Execute(null);

        Assert.False(vm.LineaLegadaHuerfana);
        await vm.GuardarCommand.ExecuteAsync(null);
        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9 && g.LineaPoaId == null), null), Times.Once);
    }

    [Fact]
    public async Task LineaHuerfana_CambiarCajaOCondicion_NoLaBorraEnSilencio()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(LegadoConLinea());
        await vm.InicializarAsync();

        vm.CajaSeleccionada = Caja.Poa;                 // contado POA: vuelve al combo normal, editable, con la misma línea
        Assert.False(vm.LineaLegadaHuerfana);
        Assert.True(vm.MostrarLineaPoa);
        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);

        vm.CajaSeleccionada = Caja.Local;               // de vuelta: sigue huérfana, no se borró
        Assert.True(vm.LineaLegadaHuerfana);
        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);

        vm.EsCredito = true;                            // crédito tampoco la borra
        Assert.True(vm.LineaLegadaHuerfana);
        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);
    }

    [Fact]
    public async Task LineaHuerfana_ElegirOtraLineaEnModoPoa_YVolverALocal_LaLimpia()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(LegadoConLinea());
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Poa;
        vm.LineasPoaDisponibles.Add(new LineaPoa { Id = 6, Nombre = "OTRA", Programa = "X", Ejercicio = 2026, Activo = true });

        vm.LineaPoaSeleccionada = vm.LineasPoaDisponibles.Single(l => l.Id == 6);
        vm.CajaSeleccionada = Caja.Local;

        Assert.Null(vm.LineaPoaSeleccionada);
        Assert.False(vm.LineaLegadaHuerfana);
    }

    // ── Errores por campo ───────────────────────────────────────────────────────

    private static readonly string[] Campos =
    [
        nameof(GastoFormViewModel.ProveedorSeleccionado), nameof(GastoFormViewModel.FechaSeleccionada),
        nameof(GastoFormViewModel.Detalle), nameof(GastoFormViewModel.MontoTexto),
        nameof(GastoFormViewModel.CajaSeleccionada), nameof(GastoFormViewModel.FuenteSeleccionada),
        nameof(GastoFormViewModel.RubroSeleccionado), nameof(GastoFormViewModel.FechaVencimientoSeleccionada),
    ];

    [Fact]
    public async Task AlAbrir_NingunCampoTieneError()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.False(vm.HasErrors);
        Assert.All(Campos, c => Assert.Empty(vm.GetErrors(c).Cast<object>()));
    }

    [Fact]
    public async Task TocarDetalleVacio_MuestraSuError()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();

        vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));

        Assert.Equal(GastoFormViewModel.MensajeDetalleObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.Detalle)));
        Assert.Equal(new[] { GastoFormViewModel.MensajeDetalleObligatorio },
            vm.GetErrors(nameof(GastoFormViewModel.Detalle)).Cast<string>());
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public async Task CambiarUnCampo_LevantaErrorsChangedConElNombreDeLaPropiedadBindeada()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        var avisos = new List<string?>();
        vm.ErrorsChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.MontoTexto = "abc";

        Assert.Contains(nameof(GastoFormViewModel.MontoTexto), avisos);
        Assert.StartsWith("El monto total no es un número válido.", vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
    }

    public static TheoryData<string> CamposObligatorios => new()
    {
        nameof(GastoFormViewModel.ProveedorSeleccionado), nameof(GastoFormViewModel.FechaSeleccionada),
        nameof(GastoFormViewModel.Detalle), nameof(GastoFormViewModel.MontoTexto),
        nameof(GastoFormViewModel.CajaSeleccionada), nameof(GastoFormViewModel.FuenteSeleccionada),
        nameof(GastoFormViewModel.RubroSeleccionado),
    };

    private static void Vaciar(GastoFormViewModel vm, string campo)
    {
        switch (campo)
        {
            case nameof(GastoFormViewModel.ProveedorSeleccionado): vm.ProveedorSeleccionado = null; break;
            case nameof(GastoFormViewModel.FechaSeleccionada): vm.FechaSeleccionada = null; break;
            case nameof(GastoFormViewModel.Detalle): vm.Detalle = "  "; break;
            case nameof(GastoFormViewModel.MontoTexto): vm.MontoTexto = ""; break;
            case nameof(GastoFormViewModel.CajaSeleccionada): vm.CajaSeleccionada = null; break;
            case nameof(GastoFormViewModel.FuenteSeleccionada): vm.FuenteSeleccionada = null; break;
            case nameof(GastoFormViewModel.RubroSeleccionado): vm.RubroSeleccionado = null; break;
        }
    }

    [Theory]
    [MemberData(nameof(CamposObligatorios))]
    public async Task UnCampoObligatorioVacio_GuardarDeshabilitado_YSuErrorVisible(string campo)
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        Assert.True(vm.GuardarCommand.CanExecute(null));

        Vaciar(vm, campo);

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.NotNull(vm.ErrorDe(campo));
    }

    [Fact]
    public async Task MontoIlegible_GuardarDeshabilitado()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.MontoTexto = "5.4";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Credito_SinVencimiento_GuardarDeshabilitado_YElErrorDelVencimiento()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.EsCredito = true;
        vm.MarcarTocado(nameof(GastoFormViewModel.FechaVencimientoSeleccionada));

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Equal(GastoFormViewModel.MensajeVencimientoObligatorio,
            vm.ErrorDe(nameof(GastoFormViewModel.FechaVencimientoSeleccionada)));
    }

    [Fact]
    public async Task DestildarCredito_SinVencimiento_GuardarSeHabilita()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.EsCredito = true;
        vm.MarcarTocado(nameof(GastoFormViewModel.FechaVencimientoSeleccionada));
        var avisos = new List<string?>();
        vm.ErrorsChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.EsCredito = false;
        vm.CajaSeleccionada = Caja.Local;   // el crédito limpió caja y literal: a contado se eligen de nuevo
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];

        Assert.True(vm.GuardarCommand.CanExecute(null));
        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.FechaVencimientoSeleccionada)));
        Assert.Contains(nameof(GastoFormViewModel.FechaVencimientoSeleccionada), avisos);
    }

    [Fact]
    public async Task Contado_SinVencimiento_NoEsError()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.MarcarTocado(nameof(GastoFormViewModel.FechaVencimientoSeleccionada));

        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.FechaVencimientoSeleccionada)));
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task IntentarGuardarIncompleto_MarcaTodosLosCampos_SinLlamarAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(GastoFormViewModel.MensajeProveedorObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.ProveedorSeleccionado)));
        Assert.Equal(GastoFormViewModel.MensajeDetalleObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.Detalle)));
        Assert.Equal(GastoFormViewModel.MensajeMontoObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
        Assert.Equal(GastoFormViewModel.MensajeFuenteObligatoria, vm.ErrorDe(nameof(GastoFormViewModel.FuenteSeleccionada)));
        Assert.Equal(GastoFormViewModel.MensajeRubroObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.RubroSeleccionado)));
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public async Task Edicion_ConTodoCargado_NingunCampoEnRojo()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(new Gasto
        {
            Id = 9, ProveedorId = 1, NumeroFactura = "A-9", Detalle = "Histórico", Fecha = DateTime.UtcNow, MontoTotal = 2000m,
            RubroGastoId = 3, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = DateTime.UtcNow.AddDays(10),
        });

        await vm.InicializarAsync();

        Assert.False(vm.HasErrors);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void MensajesDeLosCampos_SonLosAprobados()
    {
        Assert.Equal("Elegí el proveedor.", GastoFormViewModel.MensajeProveedorObligatorio);
        Assert.Equal("La fecha del gasto es obligatoria.", GastoFormViewModel.MensajeFechaObligatoria);
        Assert.Equal("El detalle es obligatorio.", GastoFormViewModel.MensajeDetalleObligatorio);
        Assert.Equal("El monto total es obligatorio.", GastoFormViewModel.MensajeMontoObligatorio);
        Assert.Equal("Elegí la fuente de financiamiento.", GastoFormViewModel.MensajeFuenteObligatoria);
        Assert.Equal("Elegí el rubro.", GastoFormViewModel.MensajeRubroObligatorio);
        Assert.Equal("Un gasto a crédito exige fecha de vencimiento.", GastoFormViewModel.MensajeVencimientoObligatorio);
    }

    // ── Guardar avisa cuando cambia lo que decide su CanExecute (CanExecute(null) lo recalcula siempre: no lo prueba) ──

    [Fact]
    public async Task CambiarLaFecha_AvisaQueGuardarCambio()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        var avisos = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisos++;

        vm.FechaSeleccionada = null;

        Assert.True(avisos > 0);
    }

    [Fact]
    public async Task TildarOQuitarCredito_AvisaQueGuardarCambio()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        var avisos = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisos++;

        vm.EsCredito = true;

        Assert.True(avisos > 0);
    }

    [Fact]
    public async Task CambiarElVencimiento_AvisaQueGuardarCambio()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.EsCredito = true;
        var avisos = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisos++;

        vm.FechaVencimientoSeleccionada = DateTime.Today.AddDays(5);

        Assert.True(avisos > 0);
    }

    [Fact]
    public async Task DestildarCredito_SinVencimiento_AvisaQueGuardarSeHabilito()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.EsCredito = true;
        var avisos = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisos++;

        vm.EsCredito = false;

        Assert.True(avisos > 0);
        vm.CajaSeleccionada = Caja.Local;   // el crédito limpió caja y literal: a contado se eligen de nuevo
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task MarcarTocadoDosVeces_AvisaUnaSolaVez()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        var avisos = new List<string?>();
        vm.ErrorsChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));
        vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));

        Assert.Single(avisos);
    }

    // ── Solo lectura, gasto anulado y condición de pago ─────────────────────────

    /// <summary>Contado: lleva su pago automático (Local, literal 2), que es donde vive la fuente del gasto.</summary>
    private static Gasto GastoExistente(
        bool activo = true, CondicionPago condicion = CondicionPago.Contado, Caja caja = Caja.Local, int fuenteId = 2)
    {
        var gasto = new Gasto
        {
            Id = 9, ProveedorId = 1, Detalle = "Histórico", Fecha = DateTime.UtcNow, MontoTotal = 2000m,
            RubroGastoId = 3, CondicionPago = condicion,
            FechaVencimiento = condicion == CondicionPago.Credito ? DateTime.UtcNow.AddDays(10) : null, Activo = activo,
        };
        if (condicion == CondicionPago.Contado)
            gasto.Pagos.Add(PagoGasto.Automatico(gasto.Fecha, gasto.MontoTotal, "Pago contado (automático)", caja, fuenteId));
        return gasto;
    }

    [Fact]
    public void ConPermiso_NoEsSoloLectura()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        Assert.False(vm.SoloLectura);
    }

    [Fact]
    public void SinRegistrarGastos_EsSoloLectura()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.VerFinanzas });

        Assert.True(vm.SoloLectura);
    }

    [Fact]
    public void GastoAnulado_EsSoloLecturaAunConPermiso_YOcultaAdjuntos()
    {
        var (vm, _, _, _) = Crear();

        vm.CargarParaEditar(GastoExistente(activo: false));

        Assert.True(vm.EsAnulado);
        Assert.True(vm.SoloLectura);
        Assert.False(vm.MostrarSeccionAdjuntos);
    }

    [Fact]
    public void GastoActivo_MuestraAdjuntos_YNoEsSoloLectura()
    {
        var (vm, _, _, _) = Crear();

        vm.CargarParaEditar(GastoExistente());

        Assert.False(vm.EsAnulado);
        Assert.False(vm.SoloLectura);
        Assert.True(vm.MostrarSeccionAdjuntos);
    }

    [Fact]
    public void CargarParaEditarUnAnulado_AvisaSoloLecturaYAdjuntos()
    {
        var (vm, _, _, _) = Crear();
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.CargarParaEditar(GastoExistente(activo: false));

        Assert.Contains(nameof(GastoFormViewModel.SoloLectura), avisos);
        Assert.Contains(nameof(GastoFormViewModel.MostrarSeccionAdjuntos), avisos);
    }

    [Fact]
    public void SoloLectura_SeReevaluaConLosPermisosActuales_SinReabrir()
    {
        var permisos = new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarGastos };
        var (vm, _, _, _) = Crear(RolUsuario.Operador, permisos);
        Assert.False(vm.SoloLectura);

        permisos.Remove(Permisos.RegistrarGastos);

        Assert.True(vm.SoloLectura);
    }

    [Fact]
    public void Edicion_LaCondicionDePagoNoSeCambia()
    {
        var (vm, _, _, _) = Crear();

        vm.CargarParaEditar(GastoExistente(condicion: CondicionPago.Credito));

        Assert.False(vm.PuedeCambiarCondicionPago);
        Assert.True(vm.EsCredito);
    }

    [Fact]
    public void CargarParaEditar_AvisaQueLaCondicionDePagoYaNoSeCambia()
    {
        var (vm, _, _, _) = Crear();
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.CargarParaEditar(GastoExistente());

        Assert.Contains(nameof(GastoFormViewModel.PuedeCambiarCondicionPago), avisos);
    }

    [Fact]
    public void AltaYDesdeEntrada_LaCondicionDePagoSeElige()
    {
        var (alta, _, _, _) = Crear();
        var (desdeEntrada, _, _, _) = Crear();

        desdeEntrada.CargarDesdeEntrada(movimientoId: 61, montoSugerido: 1500m);

        Assert.True(alta.PuedeCambiarCondicionPago);
        Assert.True(desdeEntrada.PuedeCambiarCondicionPago);
    }

    [Fact]
    public void TextoDeLaCondicionFija_EsElAprobado()
    {
        Assert.Equal("La condición de pago no se cambia: anulá el gasto y cargalo de nuevo",
            GastoFormViewModel.TextoCondicionPagoFija);
    }

    // ── R9: la solo lectura vive en el VM (no solo en la vista) ──────────────────

    [Fact]
    public async Task GastoAnulado_NoSePuedeGuardar_PeroElActivoIgualSi()
    {
        var (activo, _, _, _) = Crear();
        activo.CargarParaEditar(GastoExistente());
        await activo.InicializarAsync();
        var (anulado, _, _, _) = Crear();
        anulado.CargarParaEditar(GastoExistente(activo: false));
        await anulado.InicializarAsync();

        Assert.True(activo.GuardarCommand.CanExecute(null));
        Assert.False(anulado.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task SinRegistrarGastos_NoSePuedeGuardar()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.VerFinanzas });
        await CompletarFormularioValidoAsync(vm);

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task RevocarRegistrarGastosEnCaliente_DeshabilitaGuardarYAvisa()
    {
        var permisos = new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarGastos };
        var (vm, _, _, _) = Crear(RolUsuario.Operador, permisos);
        await CompletarFormularioValidoAsync(vm);
        Assert.True(vm.GuardarCommand.CanExecute(null));
        var avisos = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisos++;

        permisos.Remove(Permisos.RegistrarGastos);
        vm.NotificarPermisosActualizados();

        Assert.True(avisos > 0);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void CargarUnAnulado_AvisaQueGuardarCambio()
    {
        var (vm, _, _, _) = Crear();
        var gasto = GastoExistente();
        vm.CargarParaEditar(gasto);   // mismo objeto: solo cambia EsAnulado (la Fecha es UtcNow y diferiría)
        var avisos = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisos++;

        gasto.Activo = false;
        vm.CargarParaEditar(gasto);

        Assert.True(avisos > 0);
    }

    [Fact]
    public void SoloLectura_LaCondicionDePagoNoSeCambia()
    {
        var (sinPermiso, _, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.VerFinanzas });
        var (anulado, _, _, _) = Crear();
        anulado.CargarParaEditar(GastoExistente(activo: false));

        Assert.False(sinPermiso.PuedeCambiarCondicionPago);
        Assert.False(anulado.PuedeCambiarCondicionPago);
    }

    [Fact]
    public void CargarUnAnulado_AvisaQueLaCondicionDePagoCambio()
    {
        var (vm, _, _, _) = Crear();
        var gasto = GastoExistente();
        vm.CargarParaEditar(gasto);   // EsEdicion ya true: aísla la notificación de EsAnulado
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        gasto.Activo = false;
        vm.CargarParaEditar(gasto);

        Assert.Contains(nameof(GastoFormViewModel.PuedeCambiarCondicionPago), avisos);
    }

    // ── Proyecto (spec 2026-10-30): combo opcional "Sin proyecto" alimentado por ListarOpcionesAsync ────────

    private static readonly OpcionProyectoDto[] ProyectosAbiertos =
    {
        new(10, "Rambla Sur", EstadoProyecto.EnCurso),
        new(11, "Plaza Nueva", EstadoProyecto.Pendiente),
    };

    [Fact]
    public async Task InicializarAsync_CargaProyectosDesdeListarOpciones()
    {
        var (vm, _, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);

        await vm.InicializarAsync();

        Assert.Equal(new int?[] { null, 10, 11 }, vm.ProyectosDisponibles.Select(o => o.Id).ToArray());
        Assert.Equal("Sin proyecto", vm.ProyectosDisponibles[0].Nombre);
        Assert.Equal("Rambla Sur", vm.ProyectosDisponibles[1].Nombre);
    }

    [Fact]
    public async Task InicializarAsync_Alta_ArrancaEnSinProyecto()
    {
        var (vm, _, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);

        await vm.InicializarAsync();

        Assert.NotNull(vm.ProyectoSeleccionado);
        Assert.Null(vm.ProyectoSeleccionado!.Id);
    }

    [Fact]
    public async Task CargarParaEditar_ProyectoAbierto_ApareceSeleccionado()
    {
        var (vm, _, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);
        var gasto = GastoExistente();
        gasto.ProyectoId = 11;
        vm.CargarParaEditar(gasto);

        await vm.InicializarAsync();

        Assert.Equal(11, vm.ProyectoSeleccionado?.Id);
        Assert.Equal(3, vm.ProyectosDisponibles.Count);
    }

    [Fact]
    public async Task CargarParaEditar_ProyectoTerminado_ApareceSeleccionado()
    {
        // El proyecto del gasto ya no está entre los abiertos (Terminado/Cancelado): se agrega desde la nav para no perder
        // el dato histórico, igual que un rubro o una línea dados de baja.
        var (vm, _, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);
        var gasto = GastoExistente();
        gasto.ProyectoId = 99;
        gasto.Proyecto = new Proyecto { Id = 99, Titulo = "Obra vieja", Estado = EstadoProyecto.Terminado };
        vm.CargarParaEditar(gasto);

        await vm.InicializarAsync();

        Assert.Equal(99, vm.ProyectoSeleccionado?.Id);
        Assert.Equal("Obra vieja", vm.ProyectoSeleccionado?.Nombre);
        Assert.Contains(vm.ProyectosDisponibles, o => o.Id == 99);
    }

    [Fact]
    public async Task Guardar_EnviaProyectoId()
    {
        var (vm, svc, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);
        await CompletarFormularioValidoAsync(vm);
        vm.ProyectoSeleccionado = vm.ProyectosDisponibles.Single(o => o.Id == 10);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<Gasto>(g => g.ProyectoId == 10),
            It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()), Times.Once);
    }

    [Fact]
    public async Task Guardar_SinProyecto_EnviaNull()
    {
        var (vm, svc, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<Gasto>(g => g.ProyectoId == null),
            It.IsAny<PagoContadoDto?>(), It.IsAny<IReadOnlyList<int>?>()), Times.Once);
    }

    [Fact]
    public async Task Editar_QuitarElProyecto_EnviaNull()
    {
        var (vm, svc, _, _) = Crear(proyectosAbiertos: ProyectosAbiertos);
        var gasto = GastoExistente();
        gasto.ProyectoId = 10;
        vm.CargarParaEditar(gasto);
        await vm.InicializarAsync();
        vm.ProyectoSeleccionado = vm.ProyectosDisponibles[0];

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9 && g.ProyectoId == null),
            It.IsAny<PagoContadoDto?>()), Times.Once);
    }
}
