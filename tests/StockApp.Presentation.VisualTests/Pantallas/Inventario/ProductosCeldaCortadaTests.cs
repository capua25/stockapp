using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Views;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// El dato de ConCeldaCortada tiene que cortarse de verdad: con la tipografía real de la celda UNIDAD de Productos,
/// la unidad larga de "Arena gruesa" no entra en el tope de las columnas medidas. Si entrara, la celda cortada del
/// mockup no tendría nada que cortar y ningún test se enteraría.
/// </summary>
public class ProductosCeldaCortadaTests
{
    [AvaloniaFact]
    public void ConCeldaCortada_LaUnidadDeArenaGruesaMideMasQueElTopeMenosElPadding()
    {
        var vista = new ShellMainView { DataContext = DatosProductos.ConCeldaCortada() };
        var ventana = new Window { Width = 1280, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var fila = vista.GetVisualDescendants().OfType<DataGridRow>()
            .Single(r => r.DataContext is ProductoDto { Nombre: "Arena gruesa" });
        var texto = fila.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == DatosProductos.UnidadLarga);
        var celda = texto.FindAncestorOfType<DataGridCell>()!;
        var gemelo = new TextBlock
        {
            Text = texto.Text, FontFamily = texto.FontFamily, FontSize = texto.FontSize, FontWeight = texto.FontWeight,
            FontStyle = texto.FontStyle, FontStretch = texto.FontStretch, LetterSpacing = texto.LetterSpacing,
            FontFeatures = texto.FontFeatures,
        };
        gemelo.Measure(Size.Infinity);
        var disponible = ColumnaAuto.TopePorDefecto - celda.Padding.Left - celda.Padding.Right - texto.Margin.Left - texto.Margin.Right;

        Assert.True(gemelo.DesiredSize.Width > disponible,
            $"'{texto.Text}' mide {gemelo.DesiredSize.Width:F1} px ({texto.FontFamily} {texto.FontSize}) y entra en los {disponible:F1} px del tope.");
    }
}
