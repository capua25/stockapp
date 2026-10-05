using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Gate de visibilidad del XAML (no lo custodia un test de ViewModel): con la View real, el
/// campo de precio no se muestra NUNCA en una Salida (ningún motivo), y en Entrada su
/// placeholder dice "Obligatorio" con Compra y "Opcional" con Ajuste.
/// </summary>
public class MovimientoFormControlPrecioPorMotivoTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=GestionMunicipal"
                Width="500" Height="600">
            <mov:MovimientoFormControl />
        </Window>
        """;

    /// <summary>
    /// Vista de la fila del precio. Con la fila oculta Avalonia no construye el contenido del
    /// CampoFormulario (el TextBox deja de existir en el árbol visual): "oculto" significa que
    /// el campo no es visible y que el TextBox, si existe, tampoco.
    /// </summary>
    private sealed class FilaPrecio
    {
        private readonly Window _window;
        public FilaPrecio(Window window) => _window = window;

        public bool EstaVisible
        {
            get
            {
                var campo = _window.GetVisualDescendants().OfType<CampoFormulario>()
                    .First(c => c.Etiqueta == "Precio unitario");
                var caja = Caja;
                return campo.IsEffectivelyVisible && caja is { IsEffectivelyVisible: true };
            }
        }

        public TextBox? Caja => _window.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(t => t.Name == "PrecioUnitarioBox");

        public string? Placeholder => Caja?.PlaceholderText;
    }

    private static (FilaPrecio Precio, T Vm) Montar<T>(T vm) where T : MovimientoRegistroViewModelBase
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (new FilaPrecio(window), vm);
    }

    private static SalidaRegistroViewModel Salida() => new(
        new MovimientoStockServiceFake(), new ProductoServiceFake(),
        new NavigationServiceFake(), new ConfirmacionServiceFake());

    private static EntradaRegistroViewModel Entrada() => new(
        new MovimientoStockServiceFake(), new ProductoServiceFake(),
        new NavigationServiceFake(), new ConfirmacionServiceFake());

    [AvaloniaTheory]
    [InlineData(MotivoMovimiento.UsoOConsumo)]
    [InlineData(MotivoMovimiento.Merma)]
    [InlineData(MotivoMovimiento.Ajuste)]
    public void Salida_NuncaMuestraElCampoDePrecio(MotivoMovimiento motivo)
    {
        var (precio, vm) = Montar(Salida());

        vm.Motivo = motivo;
        Dispatcher.UIThread.RunJobs();

        Assert.False(precio.EstaVisible);
    }

    [AvaloniaFact]
    public void Compra_MuestraElPrecioComoObligatorio()
    {
        var (precio, _) = Montar(Entrada());   // Entrada arranca en Compra

        Assert.True(precio.EstaVisible);
        Assert.Equal("Obligatorio", precio.Placeholder);
    }

    [AvaloniaFact]
    public void Entrada_CambiarDeCompraAAjuste_CambiaElPlaceholder()
    {
        var (precio, vm) = Montar(Entrada());

        vm.Motivo = MotivoMovimiento.Ajuste;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Opcional", precio.Placeholder);
    }

    [AvaloniaFact]
    public void Entrada_Ajuste_MuestraElPrecioComoOpcional()
    {
        var (precio, vm) = Montar(Entrada());

        vm.Motivo = MotivoMovimiento.Ajuste;
        Dispatcher.UIThread.RunJobs();

        Assert.True(precio.EstaVisible);
        Assert.Equal("Opcional", precio.Placeholder);
    }
}
