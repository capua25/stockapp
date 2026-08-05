using System.Globalization;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Repro del hallazgo de verificación visual (WSLg, factura con decimales no redondos, "12.35"):
/// ANTES del fix, las columnas "Cantidad" y "Precio unitario" de la grilla de renglones en
/// IngresoPorFacturaView.axaml eran <c>DataGridTextColumn</c> que bindeaban DIRECTO a las
/// propiedades <c>decimal</c> <see cref="FilaRenglonFacturaVm.Cantidad"/>/
/// <see cref="FilaRenglonFacturaVm.PrecioUnitario"/>, SIN converter — a diferencia de TODO el
/// resto de la app (MonedaConverter, CantidadConverter, DecimalOpcionalConverter, MontoTotalTexto
/// en este mismo ViewModel), que fija la cultura "es-UY" a mano precisamente porque, según sus
/// propios comentarios, "la app no fija ningún CultureInfo global" y depender de la cultura
/// ambiente "hacía que '850,50' se interpretara con InvariantCulture... y
/// NumberStyles.AllowThousands descartaba la coma como separador de miles".
///
/// Este test reproduce el binding EXACTO que usaba (y, sin converter, seguiría usando) la celda
/// de edición de un DataGridTextColumn, sin pasar por el DataGrid en sí (que en este arnés
/// headless minimalista no encuentra el recurso "DataGridCellTextBoxTheme" →
/// "Avalonia.Controls.TextBox" del tema Fluent completo — un problema de plomería del arnés de
/// test, no del código bajo prueba): verificado con ilspycmd contra Avalonia.Controls.DataGrid
/// 12.0.1, <c>DataGridTextColumn.BindingTarget = TextBox.TextProperty</c> y
/// <c>DataGridBoundColumn.GenerateEditingElement</c> hace exactamente
/// <c>textBox.Bind(TextBox.TextProperty, Binding)</c> — es decir, un <c>TextBox</c> con
/// <c>Text="{Binding PrecioUnitario}"</c> sin converter es el binding real, byte a byte, que la
/// celda de edición del DataGrid usaba.
///
/// Cuando un <c>Binding</c> no especifica <c>ConverterCulture</c>, Avalonia usa
/// <c>CultureInfo.CurrentCulture</c> (verificado con ilspycmd contra Avalonia.Base 12.0.5:
/// <c>BindingExpression.ConverterCulture => _uncommon?._converterCulture ?? CultureInfo.CurrentCulture</c>,
/// y <c>DefaultValueConverter.Convert</c> delega en <c>TypeUtilities.TryConvert</c>, que para
/// string→decimal llama a <c>Convert.ChangeType(value, typeof(decimal), culture)</c> —
/// <c>NumberStyles.Number</c>, que INCLUYE <c>AllowThousands</c>). RESULTADO OBSERVADO (no el que
/// se hipotetizó al principio): con cultura ambiente es-UY/es-AR, escribir "12.35" NO se
/// interpreta silenciosamente como 1235 — el punto se toma como separador de miles pero el
/// agrupamiento resultante ("12" + "35", ninguno de 3 dígitos) es inválido para
/// <c>NumberStyles.Number</c>, así que el parseo FALLA: la celda queda con un error de validación
/// y el valor de la fila se queda en el que tenía ANTES de tipear — el dato tipeado se pierde en
/// silencio (ver <see cref="PrecioUnitario_PuntoDecimal_CulturaAmbienteEsUy_SeRechazaYElValorViejoQueda"/>).
/// Con otros valores cuyo agrupamiento sí resulta válido para <see cref="NumberStyles.Number"/>
/// (ej. "1.200") el resultado SÍ sería el escenario más peligroso: guardarse silenciosamente
/// como 1200 en vez de 1,2 — el mismo bug de fondo, un caso más grave. Fix aplicado:
/// <see cref="StockApp.Presentation.Converters.DecimalConverter"/> (cultura es-UY fija,
/// <c>NumberStyles.AllowDecimalPoint | AllowLeadingSign</c>, SIN <c>AllowThousands</c>), cableado
/// en <c>IngresoPorFacturaView.axaml</c>. Este test de UI queda como regla de no-regresión sobre
/// el binding CRUDO (sin converter) — la cobertura del fix en sí vive en
/// <c>DecimalConverterTests.cs</c> (StockApp.Presentation.Tests), que no depende de la cultura
/// ambiente de la máquina que corre la suite.
/// </summary>
public class IngresoPorFacturaLocaleDecimalTests
{
    private const string XamlPrecio = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="using:StockApp.Presentation.ViewModels.Movimientos"
                x:CompileBindings="True" x:DataType="vm:FilaRenglonFacturaVm"
                Width="300" Height="100">
            <TextBox Name="Caja" Text="{Binding PrecioUnitario}" />
        </Window>
        """;

    private const string XamlCantidad = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="using:StockApp.Presentation.ViewModels.Movimientos"
                x:CompileBindings="True" x:DataType="vm:FilaRenglonFacturaVm"
                Width="300" Height="100">
            <TextBox Name="Caja" Text="{Binding Cantidad}" />
        </Window>
        """;

    /// <summary>Mismo criterio de cultura fallback que MonedaConverter/CantidadConverter/etc.:
    /// si "es-UY" no está instalada en el runtime, se usa "es-AR" (mismos separadores) como
    /// segunda opción antes de fallar el test por un motivo ajeno al bug bajo prueba.</summary>
    private static CultureInfo ObtenerCulturaEsUyOEsAr()
    {
        try { return CultureInfo.GetCultureInfo("es-UY"); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("es-AR"); }
    }

    private static (Window Window, TextBox Caja, FilaRenglonFacturaVm Fila) Montar(string xaml, decimal valorInicial)
    {
        var fila = new FilaRenglonFacturaVm { Cantidad = valorInicial, PrecioUnitario = valorInicial };

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        window.DataContext = fila;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var caja = (TextBox)window.FindControl<TextBox>("Caja")!;
        return (window, caja, fila);
    }

    /// <summary>
    /// Caso de CONTROL: con cultura ambiente es-UY/es-AR fijada explícitamente en el hilo del
    /// test (para que el resultado sea reproducible y no dependa de la cultura de la máquina que
    /// corre la suite), escribir "12,35" — el formato que la propia cultura ambiente espera — debe
    /// dar 12.35m. Si esto fallara, el problema NO sería de locale sino algo más.
    /// </summary>
    [AvaloniaFact]
    public void PrecioUnitario_ComaDecimal_CulturaAmbienteEsUy_ParseaCorrecto()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (window, caja, fila) = Montar(XamlPrecio, valorInicial: 0m);

            caja.Text = "12,35";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(12.35m, fila.PrecioUnitario);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }

    /// <summary>
    /// Repro del bug SIN el fix (binding crudo, sin converter): el usuario escribe "12.35" (punto
    /// decimal, el formato que efectivamente pidió el usuario en la sesión de verificación visual
    /// que originó este test). Con cultura ambiente es-UY/es-AR, el punto es separador de MILES
    /// para <c>NumberStyles.Number</c>, y el agrupamiento resultante de "12.35" es inválido
    /// (ningún grupo de 3 dígitos) — el parseo FALLA (no lanza: Avalonia lo convierte en un
    /// <c>BindingNotification</c> de error) y el valor de la fila se queda en el que tenía ANTES
    /// de tipear (99m, elegido para no confundirse con 0 default). El dato tipeado por el usuario
    /// se pierde en silencio, sin que la fila refleje "12,35" ni ningún otro número relacionado.
    /// </summary>
    [AvaloniaFact]
    public void PrecioUnitario_PuntoDecimal_CulturaAmbienteEsUy_SeRechazaYElValorViejoQueda()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (window, caja, fila) = Montar(XamlPrecio, valorInicial: 99m);

            caja.Text = "12.35";
            Dispatcher.UIThread.RunJobs();

            // Esta es la aserción que documenta el bug: el valor esperado por el usuario es
            // 12.35m, pero el binding sin converter, bajo cultura ambiente es-UY, ni siquiera
            // llega a guardar un número — el parseo falla y el valor queda en el viejo (99m).
            Assert.Equal(99m, fila.PrecioUnitario);
            Assert.NotEqual(12.35m, fila.PrecioUnitario);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }

    /// <summary>Misma reproducción que el precio, para la columna "Cantidad" — mismo binding sin
    /// converter, mismo bug esperable.</summary>
    [AvaloniaFact]
    public void Cantidad_PuntoDecimal_CulturaAmbienteEsUy_SeRechazaYElValorViejoQueda()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (window, caja, fila) = Montar(XamlCantidad, valorInicial: 99m);

            caja.Text = "3.5";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(99m, fila.Cantidad);
            Assert.NotEqual(3.5m, fila.Cantidad);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }
}
