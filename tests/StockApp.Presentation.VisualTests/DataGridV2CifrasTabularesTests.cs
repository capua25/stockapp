using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Review Focus F1 (Task 16, fix round 1): DataGridV2Tests.CeldaNum_ALaDerechaConCifrasTabulares
/// (UiTests) solo lee TextElement.FontFeaturesProperty sobre el DataGridCell — UiTests corre con
/// métricas de fuente FALSAS, así que no puede detectar si "+tnum" deja de llegar al TextBlock real
/// o deja de producir efecto. Este test mide el ancho RENDERIZADO (Skia real, Inter embebida) del
/// TextBlock de cada celda: "1111" y "0000" tienen que medir IGUAL en la columna .num (cifras
/// tabulares) y DISTINTO fuera de ella (control: sin +tnum el glifo "1" es más angosto que "0" en
/// Inter, así que un empate ahí sería sospechoso, no una prueba de que la fuente real distingue).
/// </summary>
public class DataGridV2CifrasTabularesTests
{
    private sealed class Fila(string texto)
    {
        public string Texto { get; } = texto;
    }

    private static DataGrid MontarGrilla()
    {
        var grilla = new DataGrid
        {
            Name = "G",
            AutoGenerateColumns = false,
            Height = 200,
            Columns =
            {
                new DataGridTextColumn { Header = "TEXTO", Binding = new Binding(nameof(Fila.Texto)), Width = new DataGridLength(150) },
                new DataGridTextColumn { Header = "VALOR", Binding = new Binding(nameof(Fila.Texto)), Width = new DataGridLength(150), CellStyleClasses = { "num" } },
            },
        };
        var raiz = new UserControl { Classes = { "ui2" }, Content = grilla };
        var ventana = new Window { Width = 800, Height = 400, Content = raiz };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        grilla.ItemsSource = new[] { new Fila("1111"), new Fila("0000") };
        Dispatcher.UIThread.RunJobs();
        return grilla;
    }

    [AvaloniaFact]
    public void CeldaNum_MideIgualParaCifrasDistintasPorLasFigurasTabulares_YDistintoFueraDeLaClase()
    {
        var grilla = MontarGrilla();

        // DataGrid agrega una columna "filler" interna (sin contenido real) cuando las columnas
        // declaradas no llenan el ancho disponible: filtramos a las celdas que de verdad tienen el
        // CellTextBlock que genera DataGridTextColumn.
        var celdasDeDatos = grilla.GetVisualDescendants().OfType<DataGridCell>()
            .Where(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "CellTextBlock"))
            .ToList();
        var celdasNum = celdasDeDatos.Where(c => c.Classes.Contains("num")).ToList();
        var celdasTexto = celdasDeDatos.Where(c => !c.Classes.Contains("num")).ToList();

        Assert.Equal(2, celdasNum.Count);
        Assert.Equal(2, celdasTexto.Count);

        var anchosNum = celdasNum.Select(AnchoDelTexto).ToList();
        var anchosTexto = celdasTexto.Select(AnchoDelTexto).ToList();

        // Control: sin +tnum, "1111" y "0000" NO miden igual (si midieran igual acá, el empate de
        // abajo no probaría nada sobre las figuras tabulares).
        Assert.NotEqual(anchosTexto[0], anchosTexto[1]);
        // Cifras tabulares: con +tnum, "1111" y "0000" miden IGUAL.
        Assert.Equal(anchosNum[0], anchosNum[1]);
    }

    private static double AnchoDelTexto(DataGridCell celda) =>
        celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock").DesiredSize.Width;
}
