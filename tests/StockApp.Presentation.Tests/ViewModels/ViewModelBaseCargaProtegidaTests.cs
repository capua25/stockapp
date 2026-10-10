using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.ApiClient;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// Cubre <see cref="ViewModelBase.EjecutarCargaProtegidaAsync"/>: el único camino correcto para
/// proteger una carga fire-and-forget disparada desde DataContextChanged (ver
/// docs/superpowers/plans -- bugfix "14 puntos desnudos dejaban la pantalla muda"). Antes de este
/// helper, 14 ViewModels no atrapaban UnauthorizedAccessException (403/401 traducido por
/// ApiErrores.AsegurarExitoAsync) y la carga moría a mitad de camino sin dejar rastro bindeable
/// para la vista.
/// </summary>
public class ViewModelBaseCargaProtegidaTests
{
    /// <summary>Subclase mínima para poder ejercitar el método protected desde el test.</summary>
    private sealed class VmDePrueba : ViewModelBase
    {
        public Task EjecutarAsync(Func<Task> cargar, string mensaje) =>
            EjecutarCargaProtegidaAsync(cargar, mensaje);

        public Task EjecutarAsync(Func<Task> cargar, string mensaje, ConsultaVigente.Turno turno) =>
            EjecutarCargaProtegidaAsync(cargar, mensaje, turno);
    }

    /// <summary>Un listado cuya vista muestra el error de conexión en línea (opta por el estado SinConexion).</summary>
    private sealed class VmConErrorEnLinea : ViewModelBase
    {
        protected override bool AvisaSinConexionEnLinea => true;

        public Task EjecutarAsync(Func<Task> cargar) => EjecutarCargaProtegidaAsync(cargar, "x");

        public Task EjecutarAsync(Func<Task> cargar, ConsultaVigente.Turno turno) =>
            EjecutarCargaProtegidaAsync(cargar, "x", turno);
    }

    /// <summary>Lo que lanza el ApiClient real con la API caída o pausada (ApiErrores.EnviarAsync).</summary>
    private static ServidorNoDisponibleException SinServidor() => new(new TaskCanceledException());

    [Fact]
    public async Task SiElDelegadoLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var vm = new VmDePrueba();

        var ex = await Record.ExceptionAsync(() =>
            vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso."));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SiElDelegadoLanzaUnauthorized_DejaSinPermisoEnTrueConElMensaje()
    {
        var vm = new VmDePrueba();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso para ver esto.");

        Assert.True(vm.SinPermiso);
        Assert.Equal("No tenés permiso para ver esto.", vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task SiElDelegadoLanzaUnauthorized_NoLlamaNingunServicioDeConfirmacion()
    {
        // Red de contención (ver comentario de EjecutarCargaProtegidaAsync): el aviso global de
        // permisos ya existe (AuthTokenHandler -> ApiSession.AccesoRevocado). Este helper NO debe
        // disparar un segundo modal -- eso ya se rompió una vez en el proyecto (ver
        // TareaListViewModel/MantenimientoViewModel, protegidos-con-mensaje, deuda aceptada).
        // Este test documenta el contrato: el helper es puramente de ESTADO, sin efectos
        // colaterales de UI.
        var vm = new VmDePrueba();
        var llamadasExternas = 0;

        await vm.EjecutarAsync(() =>
        {
            llamadasExternas++;
            throw new UnauthorizedAccessException();
        }, "No tenés permiso.");

        Assert.Equal(1, llamadasExternas); // solo la llamada al propio delegado de carga
    }

    [Fact]
    public async Task SiElDelegadoNoLanzaNada_SinPermisoQuedaEnFalse()
    {
        var vm = new VmDePrueba();

        await vm.EjecutarAsync(() => Task.CompletedTask, "No tenés permiso.");

        Assert.False(vm.SinPermiso);
        Assert.Null(vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task SiElDelegadoLanzaOtraExcepcion_SePropagaTalCual()
    {
        // Cualquier excepción que NO sea UnauthorizedAccessException debe seguir propagando --
        // esto NO es un catch-all que se trague bugs reales.
        var vm = new VmDePrueba();

        var ex = await Record.ExceptionAsync(() =>
            vm.EjecutarAsync(() => throw new InvalidOperationException("boom"), "No tenés permiso."));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task UnaCargaExitosaDespuesDeUnRechazo_ResetSinPermisoAFalse()
    {
        // Escenario de reintento: la vista puede volver a disparar la carga (ej. tras reactivar
        // el permiso). SinPermiso no debe quedar pegado en True para siempre.
        var vm = new VmDePrueba();
        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso.");
        Assert.True(vm.SinPermiso);

        await vm.EjecutarAsync(() => Task.CompletedTask, "No tenés permiso.");

        Assert.False(vm.SinPermiso);
        Assert.Null(vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task Cargando_EsTrueMientrasCorreLaCarga_YFalseAlTerminar()
    {
        var vm = new VmDePrueba();
        var pendiente = new TaskCompletionSource();

        var carga = vm.EjecutarAsync(() => pendiente.Task, "x");
        Assert.True(vm.Cargando);

        pendiente.SetResult();
        await carga;
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task DosCargasSuperpuestas_LaPrimeraEnTerminarNoApagaElFlag()
    {
        var vm = new VmDePrueba();
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        var cargaA = vm.EjecutarAsync(() => a.Task, "x");
        var cargaB = vm.EjecutarAsync(() => b.Task, "x");
        b.SetResult();
        await cargaB;
        Assert.True(vm.Cargando);

        a.SetResult();
        await cargaA;
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Cargando_SeApagaAunqueLaCargaLanceOtraExcepcion()
    {
        var vm = new VmDePrueba();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.EjecutarAsync(() => throw new InvalidOperationException("bug"), "x"));

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Cargando_SeApagaTrasUn403_YQuedaSinPermiso()
    {
        var vm = new VmDePrueba();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso.");

        Assert.False(vm.Cargando);
        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task Cargando_NotificaSoloEnLasTransiciones()
    {
        var vm = new VmDePrueba();
        var cambios = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModelBase.Cargando)) cambios.Add(vm.Cargando);
        };
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        var cargaA = vm.EjecutarAsync(() => a.Task, "x");
        var cargaB = vm.EjecutarAsync(() => b.Task, "x");
        a.SetResult();
        b.SetResult();
        await Task.WhenAll(cargaA, cargaB);

        Assert.Equal(new[] { true, false }, cambios);
    }

    [Fact]
    public async Task ConTurno_Un403DeUnaConsultaVieja_NoMarcaSinPermiso()
    {
        var vm = new VmDePrueba();
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();
        consultas.Empezar();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "x", vieja);

        Assert.False(vm.SinPermiso);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task ConTurno_Un403DeLaUltima_MarcaSinPermiso()
    {
        var vm = new VmDePrueba();
        var turno = new ConsultaVigente().Empezar();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso.", turno);

        Assert.True(vm.SinPermiso);
        Assert.Equal("No tenés permiso.", vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ConTurno_OtraExcepcion_SePropagaAunqueLaConsultaSeaVieja()
    {
        var vm = new VmDePrueba();
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();
        consultas.Empezar();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.EjecutarAsync(() => throw new InvalidOperationException("bug"), "x", vieja));
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task SinOptarPorElErrorEnLinea_ElServidorCaidoSePropagaComoAntes()
    {
        // Vistas que todavía no muestran el estado (sistema viejo): sin el modal global quedarían mudas.
        var vm = new VmDePrueba();

        var ex = await Record.ExceptionAsync(() => vm.EjecutarAsync(() => throw SinServidor(), "x"));

        Assert.IsType<ServidorNoDisponibleException>(ex);
        Assert.False(vm.SinConexion);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task ConErrorEnLinea_ServidorCaido_NoPropagaYMarcaSinConexion()
    {
        var vm = new VmConErrorEnLinea();

        var ex = await Record.ExceptionAsync(() => vm.EjecutarAsync(() => throw SinServidor()));

        Assert.Null(ex);
        Assert.True(vm.SinConexion);
        Assert.False(vm.SinPermiso);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task ConErrorEnLinea_OtraExcepcion_SePropagaTalCual()
    {
        var vm = new VmConErrorEnLinea();

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.EjecutarAsync(() => throw new InvalidOperationException("bug")));
        Assert.False(vm.SinConexion);
    }

    [Fact]
    public async Task ConErrorEnLinea_UnaCargaNuevaLimpiaSinConexionAlEmpezar()
    {
        var vm = new VmConErrorEnLinea();
        await vm.EjecutarAsync(() => throw SinServidor());
        var pendiente = new TaskCompletionSource();

        var carga = vm.EjecutarAsync(() => pendiente.Task);

        Assert.False(vm.SinConexion);
        Assert.True(vm.Cargando);
        pendiente.SetResult();
        await carga;
    }

    [Fact]
    public async Task ReintentarCarga_VuelveACorrerLaCargaQueFallo_YLimpiaElError()
    {
        var vm = new VmConErrorEnLinea();
        var llamadas = 0;
        await vm.EjecutarAsync(() =>
        {
            if (++llamadas == 1)
                throw SinServidor();
            return Task.CompletedTask;
        });
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.Equal(2, llamadas);
        Assert.False(vm.SinConexion);
    }

    [Fact]
    public async Task ReintentarCarga_SiVuelveAFallar_SigueSinConexion()
    {
        var vm = new VmConErrorEnLinea();
        await vm.EjecutarAsync(() => throw SinServidor());

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.True(vm.SinConexion);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task ConTurno_ElFalloDeConexionDeUnaConsultaVieja_NoPisaALaNuevaQueYaRespondio()
    {
        var vm = new VmConErrorEnLinea();
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();
        var colgada = new TaskCompletionSource();
        var cargaVieja = vm.EjecutarAsync(() => colgada.Task, vieja);
        var nueva = consultas.Empezar();
        await vm.EjecutarAsync(() => Task.CompletedTask, nueva);

        colgada.SetException(SinServidor());
        await cargaVieja;

        Assert.False(vm.SinConexion);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task ConTurno_ElFalloDeConexionDeLaUltima_MarcaSinConexion()
    {
        var vm = new VmConErrorEnLinea();
        var turno = new ConsultaVigente().Empezar();

        await vm.EjecutarAsync(() => throw SinServidor(), turno);

        Assert.True(vm.SinConexion);
    }

    [Fact]
    public async Task ConErrorEnLinea_Un403SigueMarcandoSinPermisoYNoSinConexion()
    {
        var vm = new VmConErrorEnLinea();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException());

        Assert.True(vm.SinPermiso);
        Assert.False(vm.SinConexion);
    }

    [Fact]
    public async Task ReintentarCarga_TrasUnExito_NoVuelveACorrerLaCargaVieja()
    {
        var vm = new VmConErrorEnLinea();
        var llamadas = 0;
        await vm.EjecutarAsync(() =>
        {
            if (++llamadas == 1)
                throw SinServidor();
            return Task.CompletedTask;
        });
        await vm.ReintentarCargaCommand.ExecuteAsync(null);
        Assert.Equal(2, llamadas);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.Equal(2, llamadas);
    }

    [Fact]
    public async Task ReintentarCarga_TrasUnaCargaNuevaExitosa_NoCorreLaQueFallo()
    {
        var vm = new VmConErrorEnLinea();
        var falladas = 0;
        await vm.EjecutarAsync(() =>
        {
            falladas++;
            throw SinServidor();
        });
        await vm.EjecutarAsync(() => Task.CompletedTask);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.Equal(1, falladas);
    }

    [Fact]
    public async Task ReintentarCarga_SiTerminoBienUnaConsultaVieja_ConservaLaDeLaVigenteQueFallo()
    {
        var vm = new VmConErrorEnLinea();
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();
        var colgada = new TaskCompletionSource();
        var cargaVieja = vm.EjecutarAsync(() => colgada.Task, vieja);
        var nueva = consultas.Empezar();
        var intentos = 0;
        await vm.EjecutarAsync(() =>
        {
            intentos++;
            throw SinServidor();
        }, nueva);

        colgada.SetResult();
        await cargaVieja;
        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.Equal(2, intentos);
    }
}
