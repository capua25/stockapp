using System;
using System.Threading.Tasks;
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
    }

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
}
