using System.Globalization;
using System.Linq;
using System.Threading;
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
/// Campo "Cantidad" de <c>MovimientoFormControl.axaml</c> (Registrar Entrada/Salida), con la View
/// real, bajo cultura ambiente es-UY/es-AR forzada.
///
/// Historia: el 2026-08-05 este archivo FIJABA que el <c>TextBox</c> no necesitaba converter (el
/// binding crudo usa <c>NumberStyles.Float</c>, sin AllowThousands, así que "1.200"/"12.35" se
/// rechazaban con el genérico). Decisión vigente desde el 2026-10-01: es-UY en TODA la app con
/// parseo seguro único (<see cref="StockApp.Domain.Formato.FormatoEsUy"/>), así que el campo pasa
/// por <see cref="StockApp.Presentation.Converters.DecimalConverter"/>: "1.200" es mil doscientos
/// (punto de miles válido) y "12.35"/"5.4" se rechazan con un mensaje que sugiere la coma.
/// </summary>
public class MovimientoFormControlCantidadCulturaTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=GestionMunicipal"
                Width="500" Height="600">
            <mov:MovimientoFormControl />
        </Window>
        """;

    /// <summary>Mismo criterio de cultura fallback que el resto de los tests de locale: si "es-UY"
    /// no está instalada en el runtime, se usa "es-AR" (mismos separadores) como segunda opción.</summary>
    private static CultureInfo ObtenerCulturaEsUyOEsAr()
    {
        try { return CultureInfo.GetCultureInfo("es-UY"); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("es-AR"); }
    }

    private static (Window Window, TextBox CantidadBox, EntradaRegistroViewModel Vm) Montar()
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

        var cantidadBox = window.GetVisualDescendants()
            .OfType<TextBox>()
            .First(t => t.PlaceholderText == "Ingresá la cantidad");

        return (window, cantidadBox, vm);
    }

    /// <summary>Caso de control: "12,35" es el formato que la propia cultura ambiente es-UY
    /// espera, debe parsear correcto.</summary>
    [AvaloniaFact]
    public void Cantidad_ComaDecimal_CulturaAmbienteEsUy_ParseaCorrecto()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (_, cantidadBox, vm) = Montar();

            cantidadBox.Text = "12,35";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(12.35m, vm.Cantidad);
            Assert.False(DataValidationErrors.GetHasErrors(cantidadBox));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }

    /// <summary>"1.200" tiene agrupación de miles válida en es-UY: es 1200 (antes, con el binding
    /// crudo, se rechazaba). Cambio de expectativa por la decisión del 2026-10-01.</summary>
    [AvaloniaFact]
    public void Cantidad_PuntoComoMiles_CulturaAmbienteEsUy_SeLeeComoMiles()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (_, cantidadBox, vm) = Montar();
            vm.Cantidad = 99m;

            cantidadBox.Text = "1.200";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1200m, vm.Cantidad);
            Assert.False(DataValidationErrors.GetHasErrors(cantidadBox));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }

    /// <summary>Mismo criterio que el caso anterior, con "12.35": el punto no es el separador
    /// decimal de es-UY, así que el parseo falla de forma visible (no silenciosa) y el valor
    /// anterior se conserva — nunca se interpreta como 1235.</summary>
    [AvaloniaFact]
    public void Cantidad_PuntoDecimal_CulturaAmbienteEsUy_FallaVisibleNoSilenciosa()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (_, cantidadBox, vm) = Montar();
            vm.Cantidad = 99m;

            cantidadBox.Text = "12.35";
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(1235m, vm.Cantidad);
            Assert.Equal(99m, vm.Cantidad);
            Assert.True(DataValidationErrors.GetHasErrors(cantidadBox));
            Assert.Contains("Usá coma para los decimales: 12,35",
                DataValidationErrors.GetErrors(cantidadBox)!.Cast<object>());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }
}
