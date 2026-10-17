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
using StockApp.ApiClient;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class PagosGastoViewModelTests
{
    private static readonly DateTime Hoy = DateTime.UtcNow;

    private static Gasto GastoConPago() => new()
    {
        Id = 5, ProveedorId = 1, Detalle = "Materiales", Fecha = Hoy, MontoTotal = 1000m,
        FuenteFinanciamientoId = 2, RubroGastoId = 3,
        CondicionPago = CondicionPago.Credito, FechaVencimiento = Hoy.AddDays(30),
        Pagos = { new PagoGasto { Id = 21, GastoId = 5, Fecha = Hoy, Monto = 400m } },
    };

    private static (PagosGastoViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock,
                    Mock<IAdjuntoService> adjuntosSvcMock)
        Crear(RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null,
              HashSet<string>? permisosMutables = null, Gasto? gasto = null, Mock<IGastoService>? svc = null)
    {
        if (svc is null)
        {
            svc = new Mock<IGastoService>();
            svc.Setup(s => s.ObtenerPorIdAsync(5)).ReturnsAsync(gasto ?? GastoConPago());
        }
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>())).ReturnsAsync(22);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var adjuntosSvc = new Mock<IAdjuntoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        // permisosMutables: la sesión devuelve ESE set, para quitarle un permiso desde el test (revocación en caliente).
        session.Setup(s => s.PermisosActuales).Returns(permisosMutables ?? new HashSet<string>(permisos ?? Enumerable.Empty<string>()));
        var adjuntosPanel = new AdjuntosPanelViewModel(
            adjuntosSvc.Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new PagosGastoViewModel(svc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
        return (vm, svc, nav, confirm, adjuntosSvc);
    }

    [Fact]
    public void FechaSeleccionada_EsDateTimeNullable_ParaBindearConCalendarDatePicker()
    {
        // Migración DatePicker (DateTimeOffset?) → CalendarDatePicker (DateTime?).
        Assert.Equal(typeof(DateTime?),
            typeof(PagosGastoViewModel).GetProperty(nameof(PagosGastoViewModel.FechaSeleccionada))!.PropertyType);
    }

    [Fact]
    public async Task Inicializar_MuestraPagosYSaldo()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        Assert.Single(vm.Pagos);
        Assert.Equal(600m, vm.SaldoPendiente);
        Assert.Contains("Materiales", vm.TituloGasto);
    }

    // ── bugfix 2026-08-15: red de contención — InicializarAsync no debe escalar un 403 ─────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c): InicializarAsync la dispara la
    // View (DataContextChanged) fire-and-forget — un UnauthorizedAccessException no atrapado
    // escala a Dispatcher.UIThread.UnhandledException (App.axaml.cs). El try/catch existente
    // solo cubría ReglaDeNegocioException/EntidadNoEncontradaException.

    [Fact]
    public async Task InicializarAsync_Un403_MarcaSinPermiso()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ObtenerPorIdAsync(5)).ThrowsAsync(new UnauthorizedAccessException());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        var adjuntosSvc = new Mock<IAdjuntoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var adjuntosPanel = new AdjuntosPanelViewModel(
            adjuntosSvc.Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            session.Object);

        var vm = new PagosGastoViewModel(svc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
        vm.CargarParaGasto(GastoConPago());

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
        Assert.True(vm.SinPermiso);
        Assert.Equal(PagosGastoViewModel.MensajeSinPermisoPagos, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task InicializarAsync_SinServidor_MarcaSinConexionSinPropagar()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ObtenerPorIdAsync(5)).ThrowsAsync(new ServidorNoDisponibleException());
        var sinServidor = Crear(svc: svc);
        sinServidor.vm.CargarParaGasto(GastoConPago());

        var excepcion = await Record.ExceptionAsync(() => sinServidor.vm.InicializarAsync());

        Assert.Null(excepcion);
        Assert.True(sinServidor.vm.SinConexion);
        Assert.Empty(sinServidor.vm.Pagos);
        sinServidor.confirmMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ReintentarCarga_TrasUnaCaida_CargaElGasto()
    {
        var svc = new Mock<IGastoService>();
        var llamadas = 0;
        svc.Setup(s => s.ObtenerPorIdAsync(5)).Returns(() =>
            ++llamadas == 1 ? throw new ServidorNoDisponibleException() : Task.FromResult(GastoConPago()));
        var montaje = Crear(svc: svc);
        montaje.vm.CargarParaGasto(GastoConPago());
        await montaje.vm.InicializarAsync();
        Assert.True(montaje.vm.SinConexion);

        await montaje.vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(montaje.vm.SinConexion);
        Assert.Single(montaje.vm.Pagos);
    }

    [Fact]
    public async Task RegistrarPago_ParseaEsUY_RegistraYRefresca()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "600,00";
        vm.Nota = "saldo final";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarPagoAsync(It.Is<PagoGasto>(p =>
            p.GastoId == 5 && p.Monto == 600m && p.Nota == "saldo final")), Times.Once);
        svc.Verify(s => s.ObtenerPorIdAsync(5), Times.AtLeastOnce);  // refresco post-pago
    }

    [Fact]
    public async Task RegistrarPago_FechaSeleccionada_SinCorrimientoDeDia()
    {
        // CalendarDatePicker bindea DateTime? (no DateTimeOffset?). El pago se registra
        // fijando el Date elegido a medianoche UTC, sin conversión de huso horario (el
        // dominio de Finanzas no tiene componente horario).
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "100,00";
        vm.FechaSeleccionada = new DateTime(2026, 5, 10);

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarPagoAsync(It.Is<PagoGasto>(p =>
            p.Fecha == new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    [Fact]
    public async Task RegistrarPago_MontoIlegible_MarcaElCampoYNoLlamaAlServicio()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "no-es-numero";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal(PagosGastoViewModel.MensajeMontoInvalido, vm.ErrorDe(nameof(PagosGastoViewModel.MontoTexto)));
        svc.Verify(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarPago_ReglaDeNegocio_MuestraMensaje()
    {
        var (vm, svc, _, _, _) = Crear();
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()))
            .ThrowsAsync(new ReglaDeNegocioException("El pago supera el saldo pendiente."));
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "9999";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal("El pago supera el saldo pendiente.", vm.MensajeError);
    }

    [Fact]
    public async Task AnularPago_ConConfirmacion_AnulaYRefresca()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        await vm.AnularPagoCommand.ExecuteAsync(vm.Pagos[0]);

        svc.Verify(s => s.AnularPagoAsync(5, 21), Times.Once);
        svc.Verify(s => s.ObtenerPorIdAsync(5), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Volver_NavegaALaGrilla()
    {
        var (vm, _, nav, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        vm.VolverCommand.Execute(null);

        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SeleccionarPago_InicializaElPanelDeAdjuntosConElPagoId()
    {
        var (vm, _, _, _, adjuntosSvc) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        vm.PagoSeleccionado = vm.Pagos[0];
        await Task.Delay(1); // deja correr el fire-and-forget de OnPagoSeleccionadoChanged

        Assert.NotNull(vm.AdjuntosPanel);
        adjuntosSvc.Verify(a => a.ListarPorPagoAsync(21), Times.Once);
    }

    // ── PuedeRegistrarPagos: gating de "Registrar pago" / "Anular pago" (bugfix 2026-08-15) ──
    // Ninguno de los dos botones tenía gating por permiso. GastoService.RegistrarPagoAsync/
    // AnularPagoAsync exigen Permisos.RegistrarPagos sin condición — esta pantalla se alcanza
    // desde GastosView (ya gateado) y desde CalendarioPagosView (sin gatear), así que el gating
    // tiene que vivir ACÁ, donde vive la acción, para cubrir ambos caminos.

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

    // ── Formato es-UY (decisión 2026-10-01): parseo seguro y mensajes ────────

    [Fact]
    public async Task RegistrarPago_MontoConPuntoDecimal_SeRechazaConMensajeClaro()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "12.34";   // con NumberStyles.Number + es-UY se registraba como 1234

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        // GATE 1 4e: el error del campo es solo el detalle (entra en los 160 px del campo), sin el prefijo.
        Assert.Equal("Usá coma para los decimales: 12,34", vm.ErrorDe(nameof(PagosGastoViewModel.MontoTexto)));
        svc.Verify(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarPago_MilesConPunto_RegistraElValorCorrecto()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "1.500,50";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarPagoAsync(It.Is<PagoGasto>(p => p.Monto == 1500.50m)), Times.Once);
    }

    [Fact]
    public async Task AnularPago_MensajeDeConfirmacion_MuestraElMontoEnEsUy()
    {
        var (vm, _, _, confirm, _) = Crear();
        var gasto = GastoConPago();
        gasto.Pagos[0].Monto = 1400.5m;
        vm.CargarParaGasto(gasto);
        await vm.InicializarAsync();

        await vm.AnularPagoCommand.ExecuteAsync(gasto.Pagos[0]);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(m => m.Contains("1.400,50"))), Times.Once);
    }

    [Fact]
    public void NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos()
    {
        var (vm, _, _, _, _) = Crear();
        var avisos = new List<string?>();
        vm.AdjuntosPanel.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.NotificarPermisosActualizados();

        Assert.Contains(avisos, n => string.IsNullOrEmpty(n) || n == nameof(AdjuntosPanelViewModel.PuedeModificar));
    }

    // ── Q-4 B (GATE 1 4e): errores POR CAMPO (convención 31) en Fecha y Monto ───────────────────────

    private static string Monto => nameof(PagosGastoViewModel.MontoTexto);
    private static string Fecha => nameof(PagosGastoViewModel.FechaSeleccionada);

    [Fact]
    public async Task Monto_SinTocar_NoMuestraError_PeroElComandoNoEjecuta()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        Assert.Null(vm.ErrorDe(Monto));
        Assert.False(vm.HasErrors);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
    }

    [Fact]
    public async Task MarcarTocado_MontoVacio_MuestraElObligatorio()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        vm.MarcarTocado(Monto);

        Assert.Equal(PagosGastoViewModel.MensajeMontoObligatorio, vm.ErrorDe(Monto));
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public async Task Monto_Ilegible_LevantaErrorsChangedConElNombreDeLaPropiedad_YBloqueaElComando()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        var cambios = new List<string?>();
        vm.ErrorsChanged += (_, e) => cambios.Add(e.PropertyName);

        vm.MontoTexto = "1.5";

        Assert.Contains(Monto, cambios);
        Assert.Equal("Usá coma para los decimales: 1,5", vm.ErrorDe(Monto));
        Assert.Equal(new[] { "Usá coma para los decimales: 1,5" }, vm.GetErrors(Monto).Cast<string>());
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Monto_Valido_HabilitaElComandoYLevantaCanExecuteChanged()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        var levantado = 0;
        vm.RegistrarPagoCommand.CanExecuteChanged += (_, _) => levantado++;

        vm.MontoTexto = "10,50";

        Assert.True(levantado > 0);
        Assert.True(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.Null(vm.ErrorDe(Monto));
    }

    [Fact]
    public async Task Fecha_Nula_ErrorDeCampoYComandoBloqueado()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "10";
        var levantado = 0;
        vm.RegistrarPagoCommand.CanExecuteChanged += (_, _) => levantado++;

        vm.FechaSeleccionada = null;

        Assert.Equal(PagosGastoViewModel.MensajeFechaObligatoria, vm.ErrorDe(Fecha));
        Assert.True(levantado > 0);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        await vm.RegistrarPagoCommand.ExecuteAsync(null);
        svc.Verify(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarPago_Exitoso_LimpiaElFormularioSinDejarErroresEnRojo()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "100";
        vm.Nota = "x";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.MontoTexto);
        Assert.Null(vm.Nota);
        Assert.Null(vm.ErrorDe(Monto));
        Assert.False(vm.HasErrors);
    }

    // ── Solo lectura por permiso o por gasto anulado (Q-3 A) ─────────────────────────────────────────

    [Fact]
    public async Task GastoAnulado_EsSoloLecturaYNoRegistraNiAnula()
    {
        var anulado = GastoConPago();
        anulado.Activo = false;
        var (vm, _, _, _, _) = Crear(gasto: anulado);
        vm.CargarParaGasto(anulado);
        await vm.InicializarAsync();
        vm.MontoTexto = "10";

        Assert.True(vm.EsAnulado);
        Assert.True(vm.SoloLectura);
        Assert.False(vm.PuedeRegistrar);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.False(vm.AnularPagoCommand.CanExecute(new PagoGasto { Id = 1, Activo = true }));
    }

    [Fact]
    public async Task Operador_ConVerFinanzasSinRegistrarPagos_NoPuedeRegistrar_NiAnular_ConGastoActivo()
    {
        var (vm, _, _, _, _) = Crear(rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "10";

        Assert.False(vm.PuedeRegistrar);
        Assert.True(vm.SoloLectura);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.False(vm.AnularPagoCommand.CanExecute(new PagoGasto { Id = 1, Activo = true }));
    }

    [Fact]
    public async Task AnularPago_SobreUnPagoAnulado_NoEjecuta()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        Assert.False(vm.AnularPagoCommand.CanExecute(new PagoGasto { Id = 1, Activo = false }));
        Assert.True(vm.AnularPagoCommand.CanExecute(new PagoGasto { Id = 1, Activo = true }));
    }

    /// <summary>Review Focus 3: el 403 oculta los botones, pero los comandos no pueden seguir ejecutables.</summary>
    [Fact]
    public async Task NotificarPermisosActualizados_SinRegistrarPagos_LosComandosNoEjecutan()
    {
        var permisos = new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarPagos };
        var (vm, _, _, _, _) = Crear(rol: RolUsuario.Operador, permisosMutables: permisos);
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "10";
        var pago = new PagoGasto { Id = 1, Activo = true };
        Assert.True(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.True(vm.AnularPagoCommand.CanExecute(pago));
        var registrar = 0;
        var anular = 0;
        vm.RegistrarPagoCommand.CanExecuteChanged += (_, _) => registrar++;
        vm.AnularPagoCommand.CanExecuteChanged += (_, _) => anular++;

        permisos.Remove(Permisos.RegistrarPagos);
        vm.NotificarPermisosActualizados();

        Assert.True(registrar > 0);
        Assert.True(anular > 0);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.False(vm.AnularPagoCommand.CanExecute(pago));
    }

    // ── Panel de adjuntos y selección (Q-2 B: el pago anulado MUESTRA el panel, en solo lectura) ────

    private static Gasto GastoConPagoAnulado()
    {
        var gasto = GastoConPago();
        gasto.Pagos.Add(new PagoGasto { Id = 22, GastoId = 5, Fecha = Hoy, Monto = 50m, Activo = false });
        return gasto;
    }

    [Fact]
    public async Task SeleccionarPagoAnulado_InicializaElPanelEnSoloLecturaConSuAviso()
    {
        var (vm, _, _, _, adjuntosSvc) = Crear(gasto: GastoConPagoAnulado());
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        vm.PagoSeleccionado = vm.Pagos.Single(p => !p.Activo);
        await Task.Delay(1);

        Assert.True(vm.MostrarAdjuntos);
        Assert.False(vm.SinPagoSeleccionado);
        Assert.True(vm.AdjuntosPanel.SoloLectura);
        Assert.Equal(PagosGastoViewModel.AvisoPagoAnulado, vm.AdjuntosPanel.AvisoSoloLectura);
        Assert.Equal("Pago anulado: los adjuntos son solo de consulta", PagosGastoViewModel.AvisoPagoAnulado);
        Assert.False(vm.AdjuntosPanel.PuedeModificar);   // Admin y todo
        adjuntosSvc.Verify(a => a.ListarPorPagoAsync(22), Times.Once);
    }

    [Fact]
    public async Task SeleccionarPagoActivo_DespuesDeUnAnulado_VuelveAEditableSinAviso()
    {
        var (vm, _, _, _, _) = Crear(gasto: GastoConPagoAnulado());
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.PagoSeleccionado = vm.Pagos.Single(p => !p.Activo);

        vm.PagoSeleccionado = vm.Pagos.Single(p => p.Activo);
        await Task.Delay(1);

        Assert.False(vm.AdjuntosPanel.SoloLectura);
        Assert.Null(vm.AdjuntosPanel.AvisoSoloLectura);
        Assert.True(vm.AdjuntosPanel.PuedeModificar);
    }

    [Fact]
    public async Task SinSeleccion_NoMuestraElPanel()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        Assert.False(vm.MostrarAdjuntos);
        Assert.True(vm.SinPagoSeleccionado);
    }

    /// <summary>Review Focus 2: tras registrar o anular, la lista se reemplaza y el pago seleccionado ya no existe.</summary>
    [Fact]
    public async Task RegistrarPago_TrasRefrescar_SueltaLaSeleccionYOcultaElPanel()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.PagoSeleccionado = vm.Pagos[0];
        vm.MontoTexto = "100";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Null(vm.PagoSeleccionado);
        Assert.True(vm.SinPagoSeleccionado);
        Assert.False(vm.MostrarAdjuntos);
    }

    // ── Origen del breadcrumb (Review Focus 5) ──────────────────────────────────────────────────────

    [Fact]
    public void ConfigurarVolver_ConOrigen_ElBreadcrumbYLaVueltaCoinciden()
    {
        var (vm, _, nav, _, _) = Crear();
        Assert.Equal("Gastos y facturas", vm.TextoOrigen);
        var volvio = false;

        vm.ConfigurarVolver(() => volvio = true, "Calendario de pagos");
        vm.VolverCommand.Execute(null);

        Assert.Equal("Calendario de pagos", vm.TextoOrigen);
        Assert.True(volvio);
        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Never);
    }

    [Fact]
    public void MensajeConfirmarAnular_EsElTextoDelDialogo()
    {
        var texto = PagosGastoViewModel.MensajeConfirmarAnular(
            new PagoGasto { Monto = 100_000m, Fecha = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc) });

        Assert.Equal("¿Confirma anular el pago de 100.000,00 del 12/10/2026?", texto);
    }
}
