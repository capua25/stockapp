using System.Linq;
using Avalonia;
using Avalonia.Collections;
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
            // La derecha la hace el TextBlock, NO el HorizontalContentAlignment de la celda (recorta al ordenar).
            Assert.NotEqual(HorizontalAlignment.Right, c.HorizontalContentAlignment);
            Assert.All(c.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "CellTextBlock"),
                t => Assert.Equal(HorizontalAlignment.Right, t.HorizontalAlignment));
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
    private const string GrillaOrdenable = """
        <DataGrid x:Name="G" AutoGenerateColumns="False" Height="200" CanUserSortColumns="True" IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="CÓDIGO" Binding="{ReflectionBinding Nombre}" Width="100" />
                <DataGridTextColumn Header="VALOR" Binding="{ReflectionBinding Valor}" Width="*" />
            </DataGrid.Columns>
        </DataGrid>
        """;

    private static (Window Ventana, DataGrid Grilla) MontarGrillaOrdenable()
    {
        var ventana = Montar(GrillaOrdenable);
        var grilla = Buscar<DataGrid>(ventana, "G");
        grilla.ItemsSource = new DataGridCollectionView(new[] { new ItemPrueba("tres", 3), new ItemPrueba("uno", 1), new ItemPrueba("dos", 2) });
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();
        return (ventana, grilla);
    }

    private static DataGridColumnHeader HeaderDe(DataGrid grilla, string texto) =>
        grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, texto));

    [AvaloniaFact]
    public void Header_SinOrden_ElTextoUsaTodoElAnchoMenosElPadding()
    {
        // Fluent reserva SIEMPRE 32 px (DataGridSortIconMinWidth) para la flecha de orden: en la
        // columna CÓDIGO de 100 del catálogo el texto tenía 44 px y salía "CÓDIG" (Task 17, F1).
        // En v2 el hueco existe solo con la columna ordenada; sin orden, el texto tiene 100 - 24.
        var (_, grilla) = MontarGrillaOrdenable();
        var header = HeaderDe(grilla, "CÓDIGO");
        var contenido = Parte<Control>(header, "PART_ContentPresenter");

        Assert.Equal(100, header.Bounds.Width);
        Assert.Equal(76, contenido.Bounds.Width);
    }

    [AvaloniaFact]
    public void Header_ClickOrdenaYMuestraLaFlecha()
    {
        // Historia de sort roto en Avalonia 12 (DataGridSortClickTests): click real de puntero.
        var (ventana, grilla) = MontarGrillaOrdenable();
        var header = HeaderDe(grilla, "CÓDIGO");

        Clic(ventana, header);
        ventana.UpdateLayout();

        var nombres = ((System.Collections.IEnumerable)grilla.ItemsSource!).Cast<ItemPrueba>().Select(i => i.Nombre).ToArray();
        Assert.Equal(new[] { "dos", "tres", "uno" }, nombres);
        var flecha = Parte<Avalonia.Controls.Shapes.Path>(header, "SortIcon");
        Assert.True(flecha.IsVisible);
        Assert.True(flecha.Bounds.Width > 0);
        Assert.True(Parte<Control>(header, "PART_ContentPresenter").Bounds.Width < 76);
    }
    [AvaloniaFact]
    public void EsquinaSuperiorIzquierda_ConservaSuPlantillaPropia()
    {
        // La plantilla v2 del header no pisa la de PART_TopLeftCornerHeader (ControlTheme
        // DataGridTopLeftColumnHeader), visible cuando la grilla muestra encabezados de fila.
        var grilla = MontarGrilla();
        grilla.HeadersVisibility = DataGridHeadersVisibility.All;
        Dispatcher.UIThread.RunJobs();
        var esquina = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => h.Name == "PART_TopLeftCornerHeader");

        Assert.Contains(esquina.GetVisualDescendants(), v => v is Grid { Name: "TopLeftHeaderRoot" });
    }
}
