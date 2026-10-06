using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.Views.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Bug de produccion: a 1366x768 (alto util ~700 con la ventana maximizada) el panel de
/// permisos de UsuariosAdminView se salia de la tarjeta y la vista no tenia ScrollViewer, asi
/// que el final de la lista y el boton "Guardar permisos" quedaban inalcanzables. Monta la View
/// REAL con VM real y un operador seleccionado, y exige que, scrolleando al final, el boton
/// quede dentro del area visible de la ventana.
/// </summary>
public class UsuariosAdminPermisosScrollTests
{
    private sealed class UsuarioServiceFake : IUsuarioService
    {
        public Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol)
            => Task.FromResult(1);
        public Task BajaLogicaAsync(int usuarioId) => Task.CompletedTask;
        public Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol) => Task.CompletedTask;
        public Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null)
            => Task.CompletedTask;
        public Task<IReadOnlyList<UsuarioDto>> ListarAsync()
            => Task.FromResult<IReadOnlyList<UsuarioDto>>(new List<UsuarioDto>
            {
                new(2, "operador1", "Operador Uno", RolUsuario.Operador, true, DateTime.UtcNow),
            });
        public Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        public Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task GuardarPermisos_A1366x768_QuedaAlcanzableScrolleandoElPanel()
    {
        var svc = new UsuarioServiceFake();
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new PanelPermisosViewModel(svc, confirmacion);
        var vm = new UsuariosAdminViewModel(svc, confirmacion, panel, new SesionFake(RolUsuario.Admin));

        var vista = new UsuariosAdminView();
        // 1366x768 maximizada: ~700 de alto util (barra de titulo y de tareas).
        var window = new Window { Width = 1366, Height = 700, Content = vista };
        window.Show();
        vista.DataContext = vm;
        await vm.CargarAsync();
        vm.UsuarioSeleccionado = vm.Items.Single();
        await panel._tareaCarga;
        Dispatcher.UIThread.RunJobs();

        var guardar = vista.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == "Guardar permisos");

        var scroll = guardar.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        Assert.True(scroll is not null,
            "El boton 'Guardar permisos' no esta dentro de ningun ScrollViewer: a 1366x768 queda inalcanzable.");

        scroll!.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();

        var origen = guardar.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("El boton no esta en el arbol visual de la ventana.");
        var inferior = origen.Y + guardar.Bounds.Height;
        Assert.True(origen.Y >= 0 && inferior <= window.ClientSize.Height,
            $"Tras scrollear al final, 'Guardar permisos' sigue fuera del area visible: y={origen.Y:F0}..{inferior:F0}, alto de ventana {window.ClientSize.Height:F0}.");
    }
}
