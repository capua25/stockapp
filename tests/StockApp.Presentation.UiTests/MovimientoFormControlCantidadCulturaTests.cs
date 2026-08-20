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
/// TEST DE FIJACIÓN DE COMPORTAMIENTO (no reproduce un bug) — documenta ejecutablemente por qué
/// el campo "Cantidad" de <c>MovimientoFormControl.axaml:34</c> (<c>Text="{Binding Cantidad}"</c>,
/// <c>decimal</c> NO nullable, SIN converter) NO necesita <see cref="StockApp.Presentation.Converters.DecimalConverter"/>,
/// a diferencia de las columnas homónimas de <c>IngresoPorFacturaView.axaml</c> que sí lo tienen.
///
/// Investigación (sesión de locale-decimales, 2026-08-05): se sospechó que este <c>TextBox</c>
/// sufría el mismo bug ya arreglado en <c>IngresoPorFacturaView.axaml</c> — "1.200" guardándose
/// silenciosamente como 1200 en vez de 1,2 bajo cultura ambiente es-UY. Se armó un test A/B
/// cableando <c>DecimalConverter</c> a este binding y sacándolo: el resultado fue IDÉNTICO,
/// byte a byte, con y sin converter. Causa raíz (ver docstring de
/// <see cref="StockApp.Presentation.Converters.DecimalConverter"/>): el bug de
/// <c>NumberStyles.Number</c> (que incluye <c>AllowThousands</c>) es específico del codepath
/// <c>DataGridBoundColumn.GenerateEditingElement</c> → <c>Convert.ChangeType</c>. Un
/// <c>TextBox</c> plano fuera de un <c>DataGrid</c>, como este, cae en
/// <c>DefaultValueConverter</c> → <c>TypeUtilities.TryConvert</c>, que para <c>decimal</c> usa
/// <c>NumberStyles.Float</c> — SIN <c>AllowThousands</c> — así que "1.200"/"12.35" ya se
/// rechazan (no se corrompen) sin necesidad de ningún converter de dominio.
///
/// SI ALGÚN DÍA este campo pasa a ser una celda de <c>DataGridTextColumn</c> (o cualquier otro
/// binding cambia de codepath), este test HAY QUE revisarlo — el traspaso de codepath es
/// exactamente lo que reintroduce el bug (ver docstring de DecimalConverter.cs).
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

    /// <summary>
    /// FIJA el comportamiento seguro (NumberStyles.Float, sin AllowThousands): "1.200" — el caso
    /// que SÍ corrompía datos en la celda de <c>DataGridTextColumn</c> de
    /// <c>IngresoPorFacturaView.axaml</c> antes del fix — en este <c>TextBox</c> plano NUNCA se
    /// interpreta como separador de miles. El punto no coincide con el separador decimal de
    /// es-UY (","), así que el parseo falla de forma visible: el valor anterior se conserva y
    /// <c>DataValidationErrors.HasErrors</c> queda en <c>True</c> con el mensaje de dominio
    /// genérico (saneado globalmente por <see cref="StockApp.Presentation.Converters.ErrorValidacionConverter"/>).
    /// Si esta aserción alguna vez falla con <c>vm.Cantidad == 1200m</c>, es señal de que el
    /// codepath de este binding cambió (por ejemplo, a una celda de DataGrid) y SÍ hay que
    /// cablear <see cref="StockApp.Presentation.Converters.DecimalConverter"/> acá.
    /// </summary>
    [AvaloniaFact]
    public void Cantidad_PuntoComoMiles_CulturaAmbienteEsUy_NuncaSeInterpretaComoMiles()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (_, cantidadBox, vm) = Montar();
            vm.Cantidad = 99m;

            cantidadBox.Text = "1.200";
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(1200m, vm.Cantidad);
            Assert.Equal(99m, vm.Cantidad);
            Assert.True(DataValidationErrors.GetHasErrors(cantidadBox));
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
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }
}
