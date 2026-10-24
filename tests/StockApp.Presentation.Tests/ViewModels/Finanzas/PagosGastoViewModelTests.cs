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

    private static readonly FuenteFinanciamiento Rentas = new() { Id = 1, Nombre = "Rentas Generales", Caja = Caja.Local, Activo = true };
    private static readonly FuenteFinanciamiento Tributos = new() { Id = 2, Nombre = "Tributos", Caja = Caja.Local, Activo = true };
    private static readonly FuenteFinanciamiento Figm = new() { Id = 3, Nombre = "FIGM", Caja = Caja.Poa, Activo = true };
    private static readonly FuenteFinanciamiento[] FuentesDeLaCaja = [Rentas, Tributos, Figm];

    private static Gasto GastoConPago() => new()
    {
        Id = 5, ProveedorId = 1, Detalle = "Materiales", Fecha = Hoy, MontoTotal = 1000m,
        RubroGastoId = 3,
        CondicionPago = CondicionPago.Credito, FechaVencimiento = Hoy.AddDays(30),
        Pagos = { new PagoGasto { Id = 21, GastoId = 5, Fecha = Hoy, Monto = 400m } },
    };

    private static (PagosGastoViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock,
                    Mock<IAdjuntoService> adjuntosSvcMock)
        Crear(RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null,
              HashSet<string>? permisosMutables = null, Gasto? gasto = null, Mock<IGastoService>? svc = null,
              IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        if (svc is null)
        {
            svc = new Mock<IGastoService>();
            svc.Setup(s => s.ObtenerPorIdAsync(5)).ReturnsAsync(gasto ?? GastoConPago());
        }
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>())).ReturnsAsync(22);

        var fuentesSvc = new Mock<IFuenteFinanciamientoService>();
        fuentesSvc.Setup(f => f.ListarActivasAsync()).ReturnsAsync(fuentes ?? FuentesDeLaCaja);

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

        var vm = new PagosGastoViewModel(svc.Object, fuentesSvc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
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

        var fuentesSvc = new Mock<IFuenteFinanciamientoService>();
        var vm = new PagosGastoViewModel(svc.Object, fuentesSvc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
        vm.MontoTexto = "100";
        vm.Nota = "x";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.MontoTexto);
        Assert.Null(vm.Nota);
        Assert.Null(vm.ErrorDe(Monto));
        Assert.False(vm.HasErrors);
    }

    // ── Caja POA (2026-10-21): el pago manual siempre sale de la caja Local y elige su literal ──────────

    [Fact]
    public async Task Inicializar_FuentesSoloLocalesActivas()
    {
        var (vm, _, _, _, _) = Crear(fuentes: [Rentas, Tributos, Figm]);
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        // El servicio ya devuelve solo activas; el filtro del VM deja solo las de caja Local.
        Assert.Equal(new[] { "Rentas Generales", "Tributos" }, vm.FuentesDisponibles.Select(f => f.Nombre));
        Assert.DoesNotContain(vm.FuentesDisponibles, f => f.Caja == Caja.Poa);
    }

    [Fact]
    public async Task Inicializar_NoPreseleccionaLaFuente_NiConUnaSolaLocal()
    {
        var (vm, _, _, _, _) = Crear(fuentes: [Rentas, Figm]);
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        Assert.Single(vm.FuentesDisponibles);
        Assert.Null(vm.FuenteSeleccionada);
    }

    [Fact]
    public async Task RegistrarPago_EnviaLaFuenteElegida()
    {
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "100";
        vm.FuenteSeleccionada = Tributos;

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarPagoAsync(It.Is<PagoGasto>(p => p.FuenteFinanciamientoId == 2)), Times.Once);
    }

    [Fact]
    public async Task SinFuente_RegistrarDeshabilitado()
    {
        // Todo lo demás válido (fecha de hoy, monto legible, permiso, gasto activo): falta SOLO la fuente.
        var (vm, svc, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "100";

        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        await vm.RegistrarPagoCommand.ExecuteAsync(null);
        svc.Verify(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
        Assert.Equal(PagosGastoViewModel.MensajeFuenteObligatoria, vm.ErrorDe(nameof(PagosGastoViewModel.FuenteSeleccionada)));

        vm.FuenteSeleccionada = Rentas;   // control: con la fuente elegida, el mismo formulario se habilita
        Assert.True(vm.RegistrarPagoCommand.CanExecute(null));
    }

    [Fact]
    public void MensajeFuenteObligatoria_ConElTextoDelPlan()
        => Assert.Equal("Elegí la fuente de financiamiento.", PagosGastoViewModel.MensajeFuenteObligatoria);

    [Fact]
    public async Task SinLiteralesLocales_AvisoConElTexto()
    {
        var (vm, _, _, _, _) = Crear(fuentes: [Figm]);
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        Assert.Empty(vm.FuentesDisponibles);
        Assert.Equal("No hay literales asignados a la caja Local; configuralos en Fuentes de financiamiento", vm.AvisoSinLiterales);
    }

    [Fact]
    public async Task ConLiteralesLocales_SinAviso()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        Assert.Null(vm.AvisoSinLiterales);
    }

    [Fact]
    public async Task RegistrarPago_TrasGuardar_LimpiaLaFuente()
    {
        // Decisión del usuario (se aparta del plan, que la conservaba): cada pago vuelve a elegir su literal.
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "100";
        vm.Nota = "x";
        vm.FuenteSeleccionada = Tributos;

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.MontoTexto);
        Assert.Null(vm.Nota);
        Assert.Null(vm.FuenteSeleccionada);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.Null(vm.ErrorDe(nameof(PagosGastoViewModel.FuenteSeleccionada)));   // los tocados se sueltan: no aparece "Elegí la fuente"
    }

    // ── Gasto POA al contado: el pago manual lo rechaza el servidor, así que no se ofrece ──────────────

    private static Gasto GastoPoaAlContado() => new()
    {
        Id = 5, ProveedorId = 1, Detalle = "Obra POA", Fecha = Hoy, MontoTotal = 1000m,
        RubroGastoId = 3, CondicionPago = CondicionPago.Contado,
        Pagos = { PagoGasto.Automatico(Hoy, 1000m, "Pago contado", Caja.Poa, 3) },
    };

    private static Gasto GastoLocalAlContado() => new()
    {
        Id = 5, ProveedorId = 1, Detalle = "Compra local", Fecha = Hoy, MontoTotal = 1000m,
        RubroGastoId = 3, CondicionPago = CondicionPago.Contado,
        Pagos = { PagoGasto.Automatico(Hoy, 1000m, "Pago contado", Caja.Local, 1) },
    };

    [Fact]
    public async Task GastoPoaAlContado_NoSePuedeRegistrar_YAvisaConElMensajeDelServidor()
    {
        var (vm, _, _, _, _) = Crear(gasto: GastoPoaAlContado());
        vm.CargarParaGasto(GastoPoaAlContado());
        await vm.InicializarAsync();
        vm.MontoTexto = "10";
        vm.FuenteSeleccionada = Rentas;

        Assert.False(vm.PuedeRegistrar);
        Assert.True(vm.SoloLectura);
        Assert.False(vm.RegistrarPagoCommand.CanExecute(null));
        Assert.Equal(CajaYLiteral.MensajePagoManualEnGastoPoa, vm.AvisoGastoPoa);
    }

    [Fact]
    public async Task GastoLocalAlContado_SinAviso_YRegistraComoSiempre()
    {
        var (vm, _, _, _, _) = Crear(gasto: GastoLocalAlContado());
        vm.CargarParaGasto(GastoLocalAlContado());
        await vm.InicializarAsync();
        vm.MontoTexto = "10";
        vm.FuenteSeleccionada = Rentas;

        Assert.True(vm.PuedeRegistrar);
        Assert.False(vm.SoloLectura);
        Assert.Null(vm.AvisoGastoPoa);
        Assert.True(vm.RegistrarPagoCommand.CanExecute(null));
    }

    [Fact]
    public async Task GastoPoaAlContado_Anulado_NoRepiteElAvisoDeAnulacion()
    {
        var anulado = GastoPoaAlContado();
        anulado.Activo = false;
        var (vm, _, _, _, _) = Crear(gasto: anulado);
        vm.CargarParaGasto(anulado);
        await vm.InicializarAsync();

        Assert.Null(vm.AvisoGastoPoa);   // "anulalo y cargalo de nuevo" sobre un gasto ya anulado confunde; manda la alerta de anulado
    }

    [Fact]
    public async Task Inicializar_FuentesQueRespondeUn403_MarcaSinPermisoSinPropagar()
    {
        // Las fuentes van dentro de la MISMA carga protegida que el gasto: un 403 acá no escala.
        var fuentesSvc = new Mock<IFuenteFinanciamientoService>();
        fuentesSvc.Setup(f => f.ListarActivasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ObtenerPorIdAsync(5)).ReturnsAsync(GastoConPago());
        var confirm = new Mock<IConfirmacionService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var panel = new AdjuntosPanelViewModel(new Mock<IAdjuntoService>().Object, new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object, confirm.Object, session.Object);
        var vm = new PagosGastoViewModel(svc.Object, fuentesSvc.Object, session.Object, new Mock<INavigationService>().Object, confirm.Object, panel);
        vm.CargarParaGasto(GastoConPago());

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
        Assert.True(vm.SinPermiso);
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
        vm.FuenteSeleccionada = Rentas;
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
        vm.FuenteSeleccionada = Rentas;
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

    // ── Saldo del resumen (GATE 1 4e): un gasto anulado no tiene saldo ──────────────────────────────

    [Fact]
    public async Task TextoSaldo_GastoActivo_EsElSaldoEnEsUy()
    {
        var (vm, _, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        Assert.Equal("$ 600,00", vm.TextoSaldo);
    }

    [Fact]
    public async Task TextoSaldo_GastoAnulado_EsUnGuion_YAvisaElCambio()
    {
        var anulado = GastoConPago();
        anulado.Activo = false;
        var (vm, _, _, _, _) = Crear(gasto: anulado);
        vm.CargarParaGasto(anulado);
        // Lo que la vista pinta es lo ÚLTIMO que leyó al enterarse del cambio: tiene que ser el guion, no el saldo de antes.
        string? ultimoLeido = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PagosGastoViewModel.TextoSaldo)) ultimoLeido = vm.TextoSaldo;
        };

        await vm.InicializarAsync();

        Assert.Equal("—", vm.TextoSaldo);
        Assert.Equal("—", ultimoLeido);
    }

    [Fact]
    public async Task TextoSaldo_TrasRegistrarUnPago_SeActualizaConElNuevoSaldo()
    {
        var antes = GastoConPago();
        var despues = GastoConPago();
        despues.Pagos.Add(new PagoGasto { Id = 30, GastoId = 5, Fecha = Hoy, Monto = 100m });
        var svc = new Mock<IGastoService>();
        svc.SetupSequence(s => s.ObtenerPorIdAsync(5)).ReturnsAsync(antes).ReturnsAsync(despues);
        var (vm, _, _, _, _) = Crear(svc: svc);
        vm.CargarParaGasto(antes);
        await vm.InicializarAsync();
        vm.FuenteSeleccionada = Rentas;
        string? ultimoLeido = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PagosGastoViewModel.TextoSaldo)) ultimoLeido = vm.TextoSaldo;
        };
        vm.MontoTexto = "100";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal("$ 500,00", ultimoLeido);
    }

    // ── Orden por click: la grilla se alimenta de una DataGridCollectionView (una ObservableCollection cruda no ordena) ──

    [Fact]
    public async Task PagosView_EsLaVistaDeLaListaDePagos_YSeActualizaConElla()
    {
        var (vm, _, _, _, _) = Crear(gasto: GastoConPagoAnulado());
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        Assert.Same(vm.Pagos, vm.PagosView.SourceCollection);
        Assert.Equal(vm.Pagos.Select(p => p.Id), vm.PagosView.Cast<PagoGasto>().Select(p => p.Id));
        Assert.Equal(2, vm.PagosView.Count);
    }
}
