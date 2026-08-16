using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta IngresosView real (mismo patron que GastosViewTests.cs) para confirmar el bugfix
/// 2026-08-15: el boton "Nuevo ingreso" abria IngresoFormView sin ningun gating -- un Operador
/// con solo VerFinanzas (alcanza para entrar a esta pantalla) pero sin RegistrarIngresos llegaba
/// a un formulario completo sin boton "Guardar" visible (ese SI esta gateado por
/// PuedeRegistrarIngresos en IngresoFormView), una puerta a una habitacion sin salida. La
/// propiedad PuedeRegistrarIngresos ya existia (usada hoy por "Editar"/"Dar de baja") -- este
/// test cubre que "Nuevo ingreso" tambien la use.
/// </summary>
public class IngresosViewTests
{
    private sealed class CurrentSessionFake : ICurrentSession
    {
        private readonly RolUsuario _rol;
        private readonly IReadOnlySet<string> _permisos;

        public CurrentSessionFake(RolUsuario rol, IReadOnlySet<string> permisos)
        {
            _rol = rol;
            _permisos = permisos;
        }

        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => new(1, "operador", _rol, "Operador de prueba");
        public RolUsuario? RolActual => _rol;
        public IReadOnlySet<string> PermisosActuales => _permisos;
        public void EstablecerPermisos(IReadOnlySet<string> permisos) { }
        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public void CerrarSesion() => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

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
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="900" Height="600">
            <vistas:IngresosView />
        </Window>
        """;

    private static (Window Window, IngresosViewModel Vm) Montar(RolUsuario rol, IReadOnlySet<string> permisos)
    {
        var vm = new IngresosViewModel(
            new IngresoCajaServiceFake(),
            new CurrentSessionFake(rol, permisos),
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

    [AvaloniaFact]
    public void Montar_OperadorSinRegistrarIngresos_OcultaNuevoIngreso()
    {
        var (window, vm) = Montar(RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarIngresos);
        Assert.False(BuscarBotonPorTexto(window, "Nuevo ingreso").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorConRegistrarIngresos_MuestraNuevoIngreso()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarIngresos });

        Assert.True(vm.PuedeRegistrarIngresos);
        Assert.True(BuscarBotonPorTexto(window, "Nuevo ingreso").IsVisible);
    }
}
