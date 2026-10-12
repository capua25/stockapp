using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/adjuntos-vacio.html. El panel no es una pantalla: se compara en
/// una card de 760 sobre fondo app, el mismo marco que dibuja el mockup. El panel con un adjunto se compara dentro de
/// Ingreso por factura (ingreso-factura-guardado.html).</summary>
public static class DatosAdjuntos
{
    public static AdjuntosPanelViewModel VmVacio()
    {
        var servicio = new Mock<IAdjuntoService>();
        servicio.Setup(a => a.ListarPorGastoAsync(4521)).ReturnsAsync([]);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new AdjuntosPanelViewModel(servicio.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion.Object);
        vm.InicializarAsync(4521, null).GetAwaiter().GetResult();
        return vm;
    }

    public static Control EnTarjeta(AdjuntosPanelViewModel vm)
    {
        Avalonia.Application.Current!.TryGetResource("V2FondoBrush", null, out var fondo);
        var raiz = new UserControl
        {
            Background = (IBrush?)fondo,
            Content = new TarjetaContenido
            {
                Width = 760,
                Margin = new Thickness(28, 24),
                Padding = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = new AdjuntosPanelView { DataContext = vm },
            },
        };
        raiz.Classes.Add("ui2");
        return raiz;
    }
}
