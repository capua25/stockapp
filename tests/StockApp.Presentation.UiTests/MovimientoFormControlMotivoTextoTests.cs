using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El combo "Motivo" de MovimientoFormControl debe mostrar "Uso o consumo" y no el nombre
/// crudo del enum ("UsoOConsumo"), con la View real.
/// </summary>
public class MovimientoFormControlMotivoTextoTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=GestionMunicipal"
                Width="500" Height="600">
            <mov:MovimientoFormControl />
        </Window>
        """;

    [AvaloniaFact]
    public void Combo_Motivo_MuestraUsoOConsumoConEspacios()
    {
        var vm = new SalidaRegistroViewModel(
            new MovimientoStockServiceFake(),
            new ProductoServiceFake(),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>()
            .First(c => ReferenceEquals(c.ItemsSource, vm.MotivosDisponibles));

        var textos = combo.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("Uso o consumo", textos);
        Assert.DoesNotContain("UsoOConsumo", textos);
    }
}
