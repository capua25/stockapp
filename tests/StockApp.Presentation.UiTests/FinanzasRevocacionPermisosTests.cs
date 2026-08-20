using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Cierra la Task 8.0 (Fase B): prueba que la migracion de los tres CurrentSessionFake privados
/// (GastosView/IngresosView/PagosGastoView) a SesionFake habilita testear revocacion de permiso
/// en caliente -- lo mismo que el Ruling 6 de la Fase A arreglo en SesionFake para el sidebar.
///
/// Ajuste respecto del Step 1 del plan (docs/superpowers/plans/2026-08-19-ui-refactor-dashboard-
/// fase-b.md, Task 8.0): el plan pedia assertear el boton de la MISMA ventana montada, antes y
/// despues de EstablecerPermisos + RunJobs. Se corrio asi primero (ver ledger) y NUNCA pasa, ni
/// con SesionFake real: IngresosViewModel.PuedeRegistrarIngresos es una propiedad calculada sin
/// [ObservableProperty] ni OnPropertyChanged propio -- a diferencia de ShellMainViewModel (que
/// SI escucha IAuthService y dispara OnPropertyChanged por cada gate tras un evento de
/// navegacion), estos VM de pantalla son AddTransient y, segun su propio comentario de clase,
/// deliberadamente "no se refrescan en caliente: se recrean en cada navegacion, asi que ya toman
/// el ICurrentSession vigente en ese momento". Pedirle a la MISMA instancia que se redibuje solos
/// hubiera sido custodiar un comportamiento que la vista nunca prometio tener.
///
/// Este test prueba las dos cosas que SI son ciertas y que SI dependian del fake no-op:
/// 1. El gate del VM (`PuedeRegistrarIngresos`) recalcula al toque en la MISMA instancia apenas
///    se llama `EstablecerPermisos` -- lee `_session.PermisosActuales` en cada acceso, no cachea.
/// 2. La PROXIMA navegacion (nueva instancia sobre la misma sesion, el patron real de la app)
///    refleja la revocacion tanto en el VM como en el arbol visual (boton oculto, "Solo lectura"
///    visible).
///
/// Con el fake no-op viejo, (1) y (2) fallan igual -- <c>EstablecerPermisos</c> nunca mueve
/// <c>_permisos</c>, asi que cualquier lectura posterior (misma instancia o instancia nueva)
/// sigue viendo el permiso original. Verificado por mutacion, ver el ledger de la Task 8.0.
/// </summary>
public class FinanzasRevocacionPermisosTests
{
    private sealed class IngresoCajaServiceFake : IIngresoCajaService
    {
        public Task<int> AltaAsync(IngresoCaja ingreso) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task ModificarAsync(IngresoCaja ingreso) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync() => Task.FromResult<IReadOnlyList<IngresoCaja>>(Array.Empty<IngresoCaja>());
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="900" Height="600">
            <vistas:IngresosView />
        </Window>
        """;

    private static (Window Window, IngresosViewModel Vm) Montar(SesionFake sesion)
    {
        var vm = new IngresosViewModel(
            new IngresoCajaServiceFake(),
            sesion,
            new NavigationServiceFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    private static Button BuscarBotonPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static TextBlock BuscarTextoPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto);

    [AvaloniaFact]
    public void EstablecerPermisos_RevocaEnCaliente_ElGateDelVmRecalculaYLaProximaNavegacionLoRefleja()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarIngresos);

        var (windowInicial, vmInicial) = Montar(sesion);
        Assert.True(vmInicial.PuedeRegistrarIngresos);
        Assert.True(BuscarBotonPorTexto(windowInicial, "Nuevo ingreso").IsVisible);
        Assert.False(BuscarTextoPorContenido(windowInicial, "Solo lectura").IsVisible);

        // El servidor revoca RegistrarIngresos; en produccion AuthApiClient llama esto como
        // efecto de borde tras el refresco (ver AuthServiceFake, SesionFakes.cs:71-73).
        sesion.EstablecerPermisos(new HashSet<string> { Permisos.VerFinanzas });
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        // (1) El gate del VM YA MONTADO recalcula: lee la sesion en cada acceso, no cachea nada.
        Assert.False(vmInicial.PuedeRegistrarIngresos);

        // (2) La proxima navegacion (nueva instancia, AddTransient) sobre la MISMA sesion es el
        // mecanismo real por el que la revocacion se ve reflejada en el arbol visual.
        var (windowNuevo, vmNuevo) = Montar(sesion);
        Assert.False(vmNuevo.PuedeRegistrarIngresos);
        Assert.False(BuscarBotonPorTexto(windowNuevo, "Nuevo ingreso").IsVisible);
        Assert.True(BuscarTextoPorContenido(windowNuevo, "Solo lectura").IsVisible);
    }
}
