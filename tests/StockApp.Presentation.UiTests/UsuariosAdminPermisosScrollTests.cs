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
using StockApp.Presentation.UiTests.V2;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.Views.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Bug de producción (c145c11): a 1366x768 (alto útil ~700 con la ventana maximizada) el panel de permisos se salía de la card y
/// "Guardar permisos" quedaba inalcanzable. Ahora el botón vive en un PIE FIJO de la card, fuera del scroll de los permisos: se
/// alcanza SIEMPRE, sin scrollear, y el scroll de los permisos termina por encima del pie sin que éste tape el último permiso.
/// Monta la View REAL con VM real para un operador, un administrador y un usuario dado de baja.
/// </summary>
public class UsuariosAdminPermisosScrollTests
{
    private static readonly UsuarioDto Operador = new(2, "operador1", "Operador Uno", RolUsuario.Operador, true, DateTime.UtcNow);
    private static readonly UsuarioDto Admin = new(1, "admin1", "Admin Uno", RolUsuario.Admin, true, DateTime.UtcNow);
    private static readonly UsuarioDto DeBaja = new(3, "baja1", "Baja Uno", RolUsuario.Operador, false, DateTime.UtcNow);

    public static IEnumerable<object[]> Usuarios() => [[2], [1], [3]];

    [AvaloniaTheory]
    [MemberData(nameof(Usuarios))]
    public async Task GuardarPermisos_A1366x768_SeAlcanzaSinScrollYNoTapaElUltimoPermiso(int idUsuario)
    {
        var svc = new UsuarioServicioFake { Lista = [Admin, Operador, DeBaja] };
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new PanelPermisosViewModel(svc, confirmacion);
        var vm = new UsuariosAdminViewModel(svc, confirmacion, panel, new SesionFake(RolUsuario.Admin));

        var vista = new UsuariosAdminView();
        // 1366x768 maximizada: ~700 de alto útil (barra de título y de tareas).
        var window = new Window { Width = 1366, Height = 700, Content = vista };
        window.Show();
        vista.DataContext = vm;
        await vm.CargarAsync();
        vm.UsuarioSeleccionado = vm.Items.Single(u => u.Id == idUsuario);
        await panel._tareaCarga;
        Dispatcher.UIThread.RunJobs();

        var guardar = MontajeV2.Parte<Button>(vista, "BotonGuardarPermisos");
        var scroll = MontajeV2.Parte<ScrollViewer>(vista, "ScrollPermisos");
        Assert.DoesNotContain(guardar.GetVisualAncestors(), a => ReferenceEquals(a, scroll));

        // Sin scrollear nada: el botón está dentro del área visible de la ventana.
        var origen = guardar.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("El botón no está en el árbol.");
        Assert.True(origen.Y >= 0 && origen.Y + guardar.Bounds.Height <= window.ClientSize.Height,
            $"'Guardar permisos' queda fuera del área visible sin scrollear: y={origen.Y:F0}..{origen.Y + guardar.Bounds.Height:F0}, ventana {window.ClientSize.Height:F0}.");

        // Al final del scroll, el último permiso queda por encima del pie: el pie no lo tapa.
        scroll.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        var pie = MontajeV2.Parte<Border>(vista, "PiePermisos");
        var ultimo = scroll.GetVisualDescendants().OfType<CheckBox>().Last();
        var yUltimo = ultimo.TranslatePoint(new Point(0, ultimo.Bounds.Height), window)!.Value.Y;
        var yPie = pie.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.True(yUltimo <= yPie + 0.5, $"El último permiso termina en y={yUltimo:F0} y el pie empieza en y={yPie:F0}: el pie lo tapa.");
    }
}
