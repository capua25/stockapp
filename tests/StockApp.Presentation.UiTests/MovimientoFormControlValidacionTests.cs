using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Reproduce con la vista y el tema REALES (no un banco de pruebas aislado) el bug reportado:
/// al vaciar el campo opcional "Precio unitario" (decimal? bindeado TwoWay a TextBox en
/// MovimientoFormControl.axaml, compartido por Registrar Entrada y Registrar Salida via
/// EntradaRegistroView/SalidaRegistroView) Avalonia lanzaba
/// <c>System.InvalidCastException: Could not convert "" (System.String) to
/// System.Nullable`1[System.Decimal]</c> porque el conversor default de Avalonia no mapea ""
/// a null. Fix: <see cref="StockApp.Presentation.Converters.DecimalOpcionalConverter"/> en el
/// binding.
///
/// Tambien verifica la politica global de Themes/Controls.axaml (DataValidationErrors.ErrorConverter
/// via <see cref="StockApp.Presentation.Converters.ErrorValidacionConverter"/>): ningun error de
/// validacion de binding debe exponer el texto crudo de una excepcion .NET, sin importar el campo.
///
/// Usa el MISMO TestApp que DataGridSortClickTests, ahora ampliado (ver TestAppBuilder.cs) para
/// mezclar Tokens/Typography/Controls.axaml reales de StockApp.Presentation, asi el TextBox
/// bajo prueba tiene exactamente el mismo estilo/politica que en la app real.
/// </summary>
public class MovimientoFormControlValidacionTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=StockApp.Presentation"
                Width="500" Height="600">
            <mov:MovimientoFormControl />
        </Window>
        """;

    private static (Window Window, TextBox PrecioBox, EntradaRegistroViewModel Vm) Montar()
    {
        var vm = new EntradaRegistroViewModel(
            new MovimientoStockServiceFake(),
            new ProductoServiceFake(),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var precioBox = window.GetVisualDescendants()
            .OfType<TextBox>()
            .First(t => t.Name == "PrecioUnitarioBox");

        return (window, precioBox, vm);
    }

    [AvaloniaFact]
    public void Vaciar_PrecioUnitario_No_Genera_Error_Y_Setea_Null()
    {
        var (_, precioBox, vm) = Montar();

        // Primero se ingresa un valor válido, como haría un usuario real.
        precioBox.Text = "10";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(10m, vm.PrecioUnitario);
        Assert.False(DataValidationErrors.GetHasErrors(precioBox));

        // Reproduce el bug: vaciar el campo opcional.
        precioBox.Text = "";
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.PrecioUnitario);
        Assert.False(DataValidationErrors.GetHasErrors(precioBox));
    }

    [AvaloniaFact]
    public void PrecioUnitario_Invalido_Muestra_Mensaje_De_Dominio_No_Excepcion_Cruda()
    {
        var (_, precioBox, vm) = Montar();

        precioBox.Text = "abc";
        Dispatcher.UIThread.RunJobs();

        Assert.True(DataValidationErrors.GetHasErrors(precioBox));

        var errores = DataValidationErrors.GetErrors(precioBox)!.Cast<object>().ToArray();
        var mensaje = Assert.Single(errores);

        Assert.Equal("Ingresá un número válido.", mensaje);
        Assert.DoesNotContain("Exception", mensaje.ToString());
        Assert.DoesNotContain("System.", mensaje.ToString());
    }
}
