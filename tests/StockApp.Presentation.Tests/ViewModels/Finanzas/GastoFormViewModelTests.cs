using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Authorization;
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

public class GastoFormViewModelTests
{
    private static (GastoFormViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>?>()))
            .ReturnsAsync(new ResultadoGastoDto(7, null));
        svc.Setup(s => s.ModificarAsync(It.IsAny<Gasto>()))
            .ReturnsAsync(new ResultadoGastoDto(7, null));

        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
            new() { Id = 2, Nombre = "Dado de baja", Activo = false },
        });
        // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15, mismo criterio que
        // GastosViewModel/ec429d5): el servidor ya filtra a Activo=true, sin filtro repetido acá.
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
        });
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 2, Nombre = "Literal B", Activo = true },
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
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new GastoFormViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel);
        return (vm, svc, nav, confirm);
    }

    private static async Task CompletarFormularioValidoAsync(GastoFormViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
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
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
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
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
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
            null), Times.Once);
    }

    [Fact]
    public async Task Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "abc";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
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
            g.CondicionPago == CondicionPago.Credito && g.FechaVencimiento != null), null), Times.Once);
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
            null), Times.Once);
    }

    [Fact]
    public async Task Guardar_ConAdvertenciaDeSobregiro_LaInformaYNavega()
    {
        var (vm, svc, nav, confirm) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), null))
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
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), null))
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
            FuenteFinanciamientoId = 2, RubroGastoId = 3, LineaPoaId = 5,
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
        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9)), Times.Once);
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
            FuenteFinanciamientoId = 2, RubroGastoId = 3,
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
            && g.FechaVencimiento == new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    [Fact]
    public async Task CargarDesdeEntrada_PrecargaMontoYVinculaMovimiento()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarDesdeEntrada(movimientoId: 40, montoSugerido: 2500m);
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Factura de la entrada";

        Assert.Equal("2.500,00", vm.MontoTexto);   // precargado, editable (fletes, redondeos)

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(),
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
    }

    [Fact]
    public async Task CargarDesdeEntrada_FacturaYaExistente_OfreceAsociarLaExistente()
    {
        var (vm, svc, nav, confirm) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-1' para ese proveedor."));
        svc.Setup(s => s.ObtenerPorProveedorYFacturaAsync(1, "A-1", null))
            .ReturnsAsync(new Gasto { Id = 55, ProveedorId = 1, NumeroFactura = "A-1", Detalle = "Existente" });
        vm.CargarDesdeEntrada(movimientoId: 40, montoSugerido: 100m);
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
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
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel);

        var gasto = new Gasto
        {
            Id = 42, ProveedorId = 1, Detalle = "Con adjuntos", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = 1, RubroGastoId = 1,
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
}
