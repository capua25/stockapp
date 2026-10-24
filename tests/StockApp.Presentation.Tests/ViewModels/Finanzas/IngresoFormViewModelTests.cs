using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class IngresoFormViewModelTests
{
    private static (IngresoFormViewModel vm,
                    Mock<IIngresoCajaService> svcMock,
                    Mock<INavigationService> navMock)
        Crear(RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        var svc = new Mock<IIngresoCajaService>();
        svc.Setup(s => s.AltaAsync(It.IsAny<IngresoCaja>())).ReturnsAsync(7);
        svc.Setup(s => s.ModificarAsync(It.IsAny<IngresoCaja>())).Returns(Task.CompletedTask);

        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 2, Nombre = "Literal B", Activo = true, Caja = Caja.Local },
            new() { Id = 5, Nombre = "Literal POA 1", Activo = true, Caja = Caja.Poa },
        });

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var nav = new Mock<INavigationService>();

        var vm = new IngresoFormViewModel(svc.Object, fuentes.Object, session.Object, nav.Object);
        return (vm, svc, nav);
    }

    private static async Task CompletarFormularioValidoAsync(IngresoFormViewModel vm)
    {
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Concepto = "Partida mensual";
        vm.MontoTexto = "1.500,50";   // es-UY: miles con punto, decimales con coma
    }

    private static IngresoFormViewModel ConFuentesQueFallan(Exception falla)
    {
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ThrowsAsync(falla);
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new IngresoFormViewModel(Mock.Of<IIngresoCajaService>(), fuentes.Object, session.Object, Mock.Of<INavigationService>());
    }

    [Fact]
    public async Task InicializarAsync_403_MarcaSinPermisoConElMensajeDelFormulario()
    {
        var vm = ConFuentesQueFallan(new UnauthorizedAccessException("403"));

        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(IngresoFormViewModel.MensajeSinPermisoFormulario, vm.MensajeSinPermiso);
    }

    [Fact]
    public void MensajeSinPermisoFormulario_EsElTextoAcordado()
    {
        Assert.Equal("Ya no tenés permiso para ver finanzas, así que no se puede registrar el ingreso.",
            IngresoFormViewModel.MensajeSinPermisoFormulario);
    }

    [Fact]
    public async Task InicializarAsync_403_GuardarQuedaDeshabilitadoAunqueLosCamposEstenCompletos()
    {
        var vm = ConFuentesQueFallan(new UnauthorizedAccessException("403"));
        var notificado = false;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => notificado = true;

        await vm.InicializarAsync();

        // Solo cuenta la notificación de InicializarAsync: con la carga fallida ningún setter de campo corrió todavía.
        Assert.True(notificado);

        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B", Activo = true };
        vm.Concepto = "Partida mensual";
        vm.MontoTexto = "1.500,50";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task InicializarAsync_Exitosa_NoMarcaSinPermiso()
    {
        var (vm, _, _) = Crear();

        await CompletarFormularioValidoAsync(vm);

        Assert.False(vm.SinPermiso);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task InicializarAsync_ServidorCaido_SigueSaliendoAlAvisoGlobal()
    {
        var vm = ConFuentesQueFallan(new ServidorNoDisponibleException());

        await Assert.ThrowsAsync<ServidorNoDisponibleException>(() => vm.InicializarAsync());
    }

    [Fact]
    public void FechaSeleccionada_EsDateTimeNullable_ParaBindearConCalendarDatePicker()
    {
        // Migración DatePicker (DateTimeOffset?) → CalendarDatePicker (DateTime?).
        Assert.Equal(typeof(DateTime?),
            typeof(IngresoFormViewModel).GetProperty(nameof(IngresoFormViewModel.FechaSeleccionada))!.PropertyType);
    }

    [Fact]
    public async Task InicializarAsync_CargaFuentesDisponibles()
    {
        var (vm, _, _) = Crear();

        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Local;

        var fuente = Assert.Single(vm.FuentesDisponibles);
        Assert.Equal("Literal B", fuente.Nombre);
    }

    // ── bugfix 2026-08-15: red de contención — InicializarAsync no debe escalar un 403 ─────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c) y GastoFormViewModel.
    // InicializarAsync la dispara la View (DataContextChanged) fire-and-forget — un
    // UnauthorizedAccessException no atrapado escala a Dispatcher.UIThread.UnhandledException
    // (App.axaml.cs), que muestra el genérico "Ocurrió un error inesperado". El 403 ya se
    // avisó ANTES de llegar acá, así que el catch es silencioso.

    [Fact]
    public async Task InicializarAsync_SiFuentesLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svc = new Mock<IIngresoCajaService>();
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var nav = new Mock<INavigationService>();

        var vm = new IngresoFormViewModel(svc.Object, fuentes.Object, session.Object, nav.Object);

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
    }

    [Fact]
    public async Task Guardar_ParseaElMontoConCulturaEsUY()
    {
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<IngresoCaja>(i =>
            i.Monto == 1500.50m && i.Concepto == "Partida mensual")), Times.Once);
    }

    [Fact]
    public async Task Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio()
    {
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "abc";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<IngresoCaja>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_ReglaDeNegocio_MuestraMensajeError()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<IngresoCaja>()))
            .ThrowsAsync(new ReglaDeNegocioException("Fuente inválida."));
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Fuente inválida.", vm.MensajeError);
    }

    [Fact]
    public async Task Guardar_ConAltaExitosa_Navega()
    {
        var (vm, _, nav) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresosViewModel>(), Times.Once);
    }

    [Fact]
    public async Task Guardar_FechaSeleccionada_SinCorrimientoDeDia()
    {
        // CalendarDatePicker bindea DateTime? (no DateTimeOffset?, ver migración de
        // DatePicker→CalendarDatePicker). El dominio de Finanzas no tiene componente
        // horario: la fecha elegida debe guardarse tal cual, sin conversión de huso
        // horario.
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.FechaSeleccionada = new DateTime(2026, 6, 5);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<IngresoCaja>(i =>
            i.Fecha == new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    [Fact]
    public async Task CargarParaEditar_PrecargaCamposIncluyendoFechaSinCorrimiento()
    {
        var (vm, svc, _) = Crear();
        var ingreso = new IngresoCaja
        {
            Id = 9, Concepto = "Histórico",
            Fecha = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            FuenteFinanciamientoId = 2, Monto = 800m, Caja = Caja.Local,
        };
        vm.CargarParaEditar(ingreso);
        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.Equal("Histórico", vm.Concepto);
        Assert.Equal(new DateTime(2026, 2, 10), vm.FechaSeleccionada!.Value.Date);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<IngresoCaja>(i =>
            i.Id == 9 && i.Fecha == new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    // ── Caja: sin preselección en el alta (decisión del usuario), literales filtrados por caja ─────

    [Fact]
    public async Task Alta_SinCaja_GuardarDeshabilitado()
    {
        var (vm, svc, _) = Crear();
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Concepto = "Partida mensual";
        vm.MontoTexto = "1.500,50";
        var fuente = vm.FuenteSeleccionada;
        vm.CajaSeleccionada = null;
        vm.FuenteSeleccionada = fuente;   // todo lo demás válido: solo la caja falta

        Assert.Null(vm.CajaSeleccionada);
        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.FuenteSeleccionada)));
        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Equal(IngresoFormViewModel.MensajeCajaObligatoria, vm.ErrorDe(nameof(IngresoFormViewModel.CajaSeleccionada)));
        svc.Verify(s => s.AltaAsync(It.IsAny<IngresoCaja>()), Times.Never);
    }

    [Fact]
    public async Task Alta_AlAbrir_LaCajaNoVieneElegida_NiAvisoNiFuentes()
    {
        var (vm, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.Null(vm.CajaSeleccionada);
        Assert.Empty(vm.FuentesDisponibles);
        Assert.Null(vm.AvisoSinLiterales);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_EnviaLaCajaElegida()
    {
        var (vm, svc, _) = Crear();
        await vm.InicializarAsync();
        vm.CajaSeleccionada = Caja.Poa;
        vm.FuenteSeleccionada = Assert.Single(vm.FuentesDisponibles);
        vm.Concepto = "Aporte POA";
        vm.MontoTexto = "100";

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<IngresoCaja>(i => i.Caja == Caja.Poa && i.FuenteFinanciamientoId == 5)), Times.Once);
    }

    [Fact]
    public async Task CambiarAPoa_FiltraLasFuentesYLimpiaLaLocalElegida()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        Assert.Equal(2, vm.FuenteSeleccionada!.Id);

        vm.CajaSeleccionada = Caja.Poa;

        Assert.Equal(new[] { 5 }, vm.FuentesDisponibles.Select(f => f.Id));
        Assert.Null(vm.FuenteSeleccionada);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task CajaPoaSinLiterales_AvisoConElTextoExacto()
    {
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 2, Nombre = "Literal B", Activo = true, Caja = Caja.Local },
        });
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new IngresoFormViewModel(Mock.Of<IIngresoCajaService>(), fuentes.Object, sesion.Object, Mock.Of<INavigationService>());
        await vm.InicializarAsync();

        vm.CajaSeleccionada = Caja.Poa;

        Assert.Equal("No hay literales asignados a la caja POA; configuralos en Fuentes de financiamiento", vm.AvisoSinLiterales);
        vm.CajaSeleccionada = Caja.Local;
        Assert.Null(vm.AvisoSinLiterales);
    }

    [Fact]
    public async Task Editar_PrecargaCajaYFuente()
    {
        var (vm, svc, _) = Crear();
        vm.CargarParaEditar(new IngresoCaja
        {
            Id = 11, Concepto = "Aporte", Fecha = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            FuenteFinanciamientoId = 5, Monto = 10m, Caja = Caja.Poa,
        });
        await vm.InicializarAsync();

        Assert.Equal(Caja.Poa, vm.CajaSeleccionada);
        Assert.Equal(5, vm.FuenteSeleccionada!.Id);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<IngresoCaja>(i => i.Id == 11 && i.Caja == Caja.Poa && i.FuenteFinanciamientoId == 5)), Times.Once);
    }

    [Fact]
    public async Task Editar_FuenteDadaDeBaja_SigueSeleccionadaEnElComboDeSuCaja()
    {
        var (vm, _, _) = Crear();
        var deBaja = new FuenteFinanciamiento { Id = 9, Nombre = "Fondo viejo", Activo = false, Caja = Caja.Local };
        vm.CargarParaEditar(new IngresoCaja
        {
            Id = 12, Concepto = "Histórico", Fecha = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            FuenteFinanciamientoId = 9, FuenteFinanciamiento = deBaja, Monto = 10m, Caja = Caja.Local,
        });

        await vm.InicializarAsync();

        Assert.Same(deBaja, vm.FuenteSeleccionada);
        Assert.Contains(deBaja, vm.FuentesDisponibles);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Editar_IngresoLocalConLiteralPoa_SigueElegido_YSeGuarda()
    {
        var (vm, svc, _) = Crear();
        vm.CargarParaEditar(new IngresoCaja
        {
            Id = 14, Concepto = "Legado", Fecha = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            FuenteFinanciamientoId = 5, Monto = 10m, Caja = Caja.Local,   // literal 5 es de la caja POA
        });

        await vm.InicializarAsync();

        Assert.Equal(Caja.Local, vm.CajaSeleccionada);
        Assert.Equal(5, vm.FuenteSeleccionada?.Id);
        Assert.Contains(vm.FuentesDisponibles, f => f.Id == 5);
        Assert.True(vm.GuardarCommand.CanExecute(null));

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<IngresoCaja>(i => i.Id == 14 && i.Caja == Caja.Local && i.FuenteFinanciamientoId == 5)), Times.Once);
    }

    [Fact]
    public async Task Editar_IngresoLocalConLiteralPoa_VolverALaCajaOriginal_NoReeligeElLiteral()
    {
        var (vm, _, _) = Crear();
        vm.CargarParaEditar(new IngresoCaja
        {
            Id = 14, Concepto = "Legado", Fecha = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            FuenteFinanciamientoId = 5, Monto = 10m, Caja = Caja.Local,
        });
        await vm.InicializarAsync();

        vm.CajaSeleccionada = Caja.Poa;
        vm.CajaSeleccionada = Caja.Local;

        Assert.Contains(vm.FuentesDisponibles, f => f.Id == 5);
        Assert.Null(vm.FuenteSeleccionada);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    // La fuente histórica llega con la forma REAL que produce el cliente HTTP (pasa por IngresoCajaApiClient.AEntidad),
    // no armada a mano con la Caja ya seteada: ese stub ocultaba que el mapeo la perdía.
    private sealed class HandlerFijo : HttpMessageHandler
    {
        private readonly string _json;
        public HandlerFijo(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
    }

    [Fact]
    public async Task Editar_IngresoPoaConFuenteDadaDeBajaLeidoPorElClienteHttp_QuedaEnElComboDePoaYPuedeGuardar()
    {
        var http = new HttpClient(new HandlerFijo(
            """[{"id":13,"fecha":"2026-03-01T00:00:00Z","concepto":"Histórico POA","fuenteFinanciamientoId":9,"fuenteNombre":"Literal POA viejo","monto":10,"activo":true,"caja":1}]"""))
        { BaseAddress = new Uri("http://localhost/") };
        var ingreso = (await new IngresoCajaApiClient(http).ListarTodosAsync()).Single();

        var (vm, _, _) = Crear();
        vm.CargarParaEditar(ingreso);
        await vm.InicializarAsync();

        Assert.Equal(Caja.Poa, vm.CajaSeleccionada);
        Assert.Equal(9, vm.FuenteSeleccionada?.Id);
        Assert.Contains(vm.FuentesDisponibles, f => f.Id == 9);
        Assert.True(vm.GuardarCommand.CanExecute(null));

        vm.CajaSeleccionada = Caja.Local;
        Assert.DoesNotContain(vm.FuentesDisponibles, f => f.Id == 9);
    }

    // ── PuedeRegistrarIngresos: gating del botón "Guardar" (bugfix 2026-08-15) ─────────────
    // El botón no tenía NINGÚN gating por permiso. Se llega desde IngresosView ("Nuevo
    // ingreso"/"Editar"), que solo exige VerFinanzas, pero IngresoCajaService.AltaAsync/
    // ModificarAsync exigen Permisos.RegistrarIngresos sin condición — un Operador con
    // VerFinanzas pero sin RegistrarIngresos llenaba el formulario y recién al guardar se
    // comía un 403.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarIngresos_PuedeRegistrarIngresos_EsFalse()
    {
        var (vm, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarIngresos);
    }

    [Fact]
    public void Operador_ConRegistrarIngresos_PuedeRegistrarIngresos_EsTrue()
    {
        var (vm, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarIngresos });

        Assert.True(vm.PuedeRegistrarIngresos);
    }

    [Fact]
    public void Admin_PuedeRegistrarIngresos_EsTrue()
    {
        var (vm, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarIngresos);
    }

    // ── Formato es-UY (decisión 2026-10-01): parseo seguro ───────────────────

    [Fact]
    public async Task Guardar_MontoConPuntoDecimal_SeRechazaConMensajeClaroSinLlamarAlServicio()
    {
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "1.5";   // con NumberStyles.Number + es-UY se guardaba como 15

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("El monto no es un número válido. Usá coma para los decimales: 1,5",
            vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
        svc.Verify(s => s.AltaAsync(It.IsAny<IngresoCaja>()), Times.Never);
    }

    private static readonly string[] Campos =
    {
        nameof(IngresoFormViewModel.FechaSeleccionada), nameof(IngresoFormViewModel.MontoTexto),
        nameof(IngresoFormViewModel.Concepto), nameof(IngresoFormViewModel.FuenteSeleccionada),
        nameof(IngresoFormViewModel.CajaSeleccionada),
    };

    [Fact]
    public async Task AlAbrir_NingunCampoTieneError()
    {
        var (vm, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.All(Campos, c => Assert.Null(vm.ErrorDe(c)));
        Assert.All(Campos, c => Assert.Empty(vm.GetErrors(c).Cast<object>()));
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public async Task Edicion_ConDatosCargados_SinErrores()
    {
        var (vm, _, _) = Crear();
        vm.CargarParaEditar(new IngresoCaja
        {
            Id = 5, Fecha = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), Concepto = "Multas",
            FuenteFinanciamientoId = 2, Monto = 48_300m, Caja = Caja.Local,
        });

        await vm.InicializarAsync();

        Assert.All(Campos, c => Assert.Null(vm.ErrorDe(c)));
    }

    [Fact]
    public async Task ConceptoBorradoTrasEscribir_MuestraSuError()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();

        vm.Concepto = "x";
        vm.Concepto = "";

        Assert.Equal(IngresoFormViewModel.MensajeConceptoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.Concepto)));
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public async Task MarcarTocado_SinCambiarElValor_MuestraElErrorYLoNotifica()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();
        var notificados = new List<string?>();
        vm.ErrorsChanged += (_, e) => notificados.Add(e.PropertyName);

        vm.MarcarTocado(nameof(IngresoFormViewModel.Concepto));

        Assert.Equal(IngresoFormViewModel.MensajeConceptoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.Concepto)));
        Assert.Equal(new[] { IngresoFormViewModel.MensajeConceptoObligatorio },
            vm.GetErrors(nameof(IngresoFormViewModel.Concepto)).Cast<string>());
        Assert.Equal(new[] { nameof(IngresoFormViewModel.Concepto) }, notificados);
    }

    [Fact]
    public async Task MontoInvalido_ErrorDelCampoConElDetalleEsUy()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();

        vm.MontoTexto = "1.5";

        Assert.Equal("El monto no es un número válido. Usá coma para los decimales: 1,5",
            vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
    }

    [Fact]
    public async Task MontoBorrado_EsObligatorio()
    {
        var (vm, _, _) = Crear();
        await vm.InicializarAsync();

        vm.MontoTexto = "10";
        vm.MontoTexto = "  ";

        Assert.Equal(IngresoFormViewModel.MensajeMontoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
    }

    [Fact]
    public async Task FechaBorrada_EsObligatoria_YGuardarSeDeshabilita()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.FechaSeleccionada = null;

        Assert.Equal(IngresoFormViewModel.MensajeFechaObligatoria, vm.ErrorDe(nameof(IngresoFormViewModel.FechaSeleccionada)));
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task CorregirElMonto_QuitaElError()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "1.5";

        vm.MontoTexto = "1,5";

        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_DeshabilitadoConMontoInvalido()
    {
        var (vm, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.MontoTexto = "abc";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task IntentarGuardarConCamposVacios_TocaTodosYNoLlamaAlServicio()
    {
        var (vm, svc, _) = Crear();
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(IngresoFormViewModel.MensajeConceptoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.Concepto)));
        Assert.Equal(IngresoFormViewModel.MensajeMontoObligatorio, vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
        Assert.Equal(IngresoFormViewModel.MensajeFuenteObligatoria, vm.ErrorDe(nameof(IngresoFormViewModel.FuenteSeleccionada)));
        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.FechaSeleccionada)));   // arranca en hoy
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<IngresoCaja>()), Times.Never);
    }

    [Fact]
    public async Task ErrorDelServidor_VaALaAlertaYNoAUnCampo()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<IngresoCaja>()))
            .ThrowsAsync(new ArgumentException("El monto del ingreso debe ser mayor a cero."));
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "0,00";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("El monto del ingreso debe ser mayor a cero.", vm.MensajeError);
        Assert.Null(vm.ErrorDe(nameof(IngresoFormViewModel.MontoTexto)));
    }

    [Fact]
    public void ConceptoTocadoYCorregido_LevantaErrorsChangedYQuedaSinErrores()
    {
        var (vm, _, _) = Crear();
        vm.MarcarTocado(nameof(IngresoFormViewModel.Concepto));
        Assert.Single(vm.GetErrors(nameof(IngresoFormViewModel.Concepto)).Cast<object>());
        var avisos = new List<string?>();
        vm.ErrorsChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.Concepto = "Multas de tránsito";

        // INotifyDataErrorInfo exige avisar cuando cambian los errores de la propiedad: un consumidor que no relee
        // GetErrors con cada PropertyChanged dejaría el error pintado.
        Assert.Contains(nameof(IngresoFormViewModel.Concepto), avisos);
        Assert.Empty(vm.GetErrors(nameof(IngresoFormViewModel.Concepto)).Cast<object>());
    }
}
