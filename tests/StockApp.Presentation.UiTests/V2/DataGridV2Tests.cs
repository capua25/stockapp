using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class DataGridV2Tests
{
    private const string Grilla = """
        <DataGrid x:Name="G" AutoGenerateColumns="False" Height="200">
            <DataGrid.Columns>
                <DataGridTextColumn Header="NOMBRE" Binding="{ReflectionBinding Nombre}" Width="*" />
                <DataGridTextColumn Header="VALOR" Binding="{ReflectionBinding Valor}" Width="100" CellStyleClasses="num" />
            </DataGrid.Columns>
        </DataGrid>
        """;

    private static DataGrid MontarGrilla(bool conScopeUi2 = true)
    {
        var grilla = Buscar<DataGrid>(Montar(Grilla, conScopeUi2), "G");
        grilla.ItemsSource = new[] { new ItemPrueba("uno", 1), new ItemPrueba("dos", 1111), new ItemPrueba("tres", 3) };
        Dispatcher.UIThread.RunJobs();
        return grilla;
    }

    [AvaloniaFact]
    public void Filas_TienenElPitchDelTokenAltoFilaGrilla()
    {
        // DataGrid 12.0.1 suma 1px de separador DEBAJO del RowHeight (medido). El tema usa
        // RowHeight 39 para que el pitch sea el token (40) igual que el mockup.
        Assert.True(Avalonia.Application.Current!.TryGetResource("V2AltoFilaGrilla", ThemeVariant.Light, out var token));
        var filas = MontarGrilla().GetVisualDescendants().OfType<DataGridRow>().ToList();

        Assert.Equal(3, filas.Count);
        Assert.All(filas, f => Assert.Equal((double)token!, f.Bounds.Height));
    }

    [AvaloniaFact]
    public void Header_Alto36FondoHeaderGrillaTipografia11_600Terciario()
    {
        var header = MontarGrilla().GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => h.Content is "NOMBRE");

        Assert.Equal(36, header.Bounds.Height);
        Assert.Equal(Hex("#FAFAF9"), ColorDe(header.Background));
        Assert.Equal(Hex("#78716C"), ColorDe(header.Foreground));
        Assert.Equal(11, header.FontSize);
        Assert.Equal(FontWeight.SemiBold, header.FontWeight);
        Assert.Equal(0.6, header.LetterSpacing);
    }

    [AvaloniaFact]
    public void SinZebra_TodasLasFilasBlancas()
    {
        var filas = MontarGrilla().GetVisualDescendants().OfType<DataGridRow>().ToList();

        Assert.All(filas, f => Assert.Equal(Hex("#FFFFFF"), ColorDe(f.Background)));
    }

    [AvaloniaFact]
    public void FueraDeUi2_ConservaLaZebraVieja()
    {
        // Review Focus 1: Themes/DataGrid.axaml sigue mandando en las grillas no migradas.
        var fondos = MontarGrilla(conScopeUi2: false).GetVisualDescendants().OfType<DataGridRow>()
            .Select(f => ColorDe(f.Background)).Distinct().ToList();

        Assert.Equal(2, fondos.Count);
    }

    [AvaloniaFact]
    public void CeldaNum_ALaDerechaConCifrasTabulares()
    {
        var celdas = MontarGrilla().GetVisualDescendants().OfType<DataGridCell>().Where(c => c.Classes.Contains("num")).ToList();

        Assert.NotEmpty(celdas);
        Assert.All(celdas, c =>
        {
            Assert.Equal(HorizontalAlignment.Right, c.HorizontalContentAlignment);
            var rasgos = c.GetValue(TextElement.FontFeaturesProperty);
            Assert.NotNull(rasgos);
            Assert.Contains(rasgos!, f => f.ToString().Contains("tnum"));
        });
    }

    [AvaloniaFact]
    public void Celda_Fuente13ColorEtiquetaPadding12()
    {
        var celda = MontarGrilla().GetVisualDescendants().OfType<DataGridCell>().First();

        Assert.Equal(13, celda.FontSize);
        Assert.Equal(Hex("#44403C"), ColorDe(celda.Foreground));
        Assert.Equal(new Thickness(12, 0), celda.Padding);
    }

    [AvaloniaFact]
    public void Celda_TextoEmpiezaA12DelBordeDeLaCeldaComoEnElMockup()
    {
        // Fluent le da 12 de margen propio al TextBlock de DataGridTextColumn: sin anularlo el
        // texto queda a 24 (12 + 12), el doble que en .v2-grilla-celda.
        var celda = MontarGrilla().GetVisualDescendants().OfType<DataGridCell>().First(c => !c.Classes.Contains("num"));
        var texto = celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock");

        Assert.Equal(12, texto.TranslatePoint(new Point(0, 0), celda)!.Value.X);
    }

    [AvaloniaFact]
    public void Grilla_BordeSeparadorRadio8YConservaResizeYAnchoMinimo()
    {
        var grilla = MontarGrilla();

        Assert.Equal(Hex("#F0EFED"), ColorDe(grilla.BorderBrush));
        Assert.Equal(Hex("#F0EFED"), ColorDe(grilla.HorizontalGridLinesBrush));
        Assert.Equal(new CornerRadius(8), grilla.CornerRadius);
        Assert.True(grilla.CanUserResizeColumns);
        Assert.Equal(40, grilla.MinColumnWidth);
    }

    [AvaloniaFact]
    public void Header_SinSeparadoresVerticales()
    {
        var separadores = MontarGrilla().GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Name == "VerticalSeparator").ToList();

        // H3 (preflight): sin este Assert, una lista vacía deja Assert.All en verde vacuo — no
        // custodia nada. Confirma que el template real del header trae la parte antes de pedir
        // que esté oculta.
        Assert.NotEmpty(separadores);
        Assert.All(separadores, s => Assert.False(ArbolVisual.EsVisibleEnArbol(s)));
    }
}
