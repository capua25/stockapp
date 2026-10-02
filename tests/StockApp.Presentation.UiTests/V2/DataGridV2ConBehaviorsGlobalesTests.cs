using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Behaviors;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// El tema v2 (Themes/V2/DataGridV2.axaml, plantilla de header incluida) convive con los
/// behaviors y estilos GLOBALES de Themes/DataGrid.axaml que entraron a main el 2026-10-01:
/// <see cref="DataGridColumnasAutoBehavior"/> (reajuste de columnas Auto + piso de 160 en las
/// columnas *) y la regla de celdas numéricas (la alineación a la derecha la hace SOLO el
/// TextBlock; un HorizontalContentAlignment=Right en DataGridCell.num recorta el último
/// carácter al ordenar, ver DataGridCeldaNumericaOrdenTests). Estos tests montan la grilla DENTRO
/// de un scope .ui2 para que un selector v2 no pueda pisar nada de lo global sin que se note.
/// El guardián del HorizontalContentAlignment de .num es DataGridV2Tests.CeldaNum_ALaDerechaConCifrasTabulares
/// (verificado por mutación: reponer el setter en DataGridV2.axaml lo pone en rojo; el recorte al
/// ordenar no se reprodujo en esta grilla headless, así que no hay un test de ordenamiento acá).
/// </summary>
public class DataGridV2ConBehaviorsGlobalesTests
{
    private const string GrillaProporcional = """
        <DataGrid x:Name="G" AutoGenerateColumns="False" Height="300" CanUserResizeColumns="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="A" Binding="{ReflectionBinding Nombre}" Width="*" />
                <DataGridTextColumn Header="B" Binding="{ReflectionBinding Nombre}" Width="*" />
                <DataGridTextColumn Header="C" Binding="{ReflectionBinding Nombre}" Width="*" />
                <DataGridTextColumn Header="D" Binding="{ReflectionBinding Nombre}" Width="*" />
            </DataGrid.Columns>
        </DataGrid>
        """;

    private const string GrillaAutoYNumerica = """
        <DataGrid x:Name="G" AutoGenerateColumns="False" Height="400" CanUserResizeColumns="True" CanUserSortColumns="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Nombre" Binding="{ReflectionBinding Nombre}" Width="*" />
                <DataGridTextColumn Header="Detalle" Binding="{ReflectionBinding Nombre}" Width="Auto" />
                <DataGridTextColumn Header="Valor" Binding="{ReflectionBinding Valor}" SortMemberPath="Valor" Width="Auto" CellStyleClasses="num" />
            </DataGrid.Columns>
        </DataGrid>
        """;

    private static DataGrid Montar(string xaml, double ancho, out Window ventana)
    {
        ventana = MontajeV2.Montar(xaml, conScopeUi2: true, ancho: ancho, alto: 600);
        return Buscar<DataGrid>(ventana, "G");
    }

    private static void Cargar(DataGrid grid, IEnumerable<ItemPrueba> items)
    {
        grid.ItemsSource = new DataGridCollectionView(items.ToList());
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Ui2_LasColumnasProporcionalesTienenElPisoDe160_YApareceElScrollHorizontal()
    {
        // 4 columnas * en ~520px: repartidas parejo serían ~130 (< 160). Con el piso quedan en 160
        // y lo que no entra se ve con el scroll horizontal.
        var grid = Montar(GrillaProporcional, 560, out _);
        Cargar(grid, Enumerable.Range(0, 5).Select(i => new ItemPrueba($"fila {i}", i)));

        Assert.True(DataGridColumnasAutoBehavior.GetAjustarColumnasAuto(grid),
            "el setter global del tema tiene que llegar a una grilla dentro de .ui2");
        Assert.All(grid.Columns, c => Assert.True(c.ActualWidth >= 160 - 0.5, $"{c.Header} mide {c.ActualWidth:F1}px (piso 160)"));
        Assert.True(grid.GetVisualDescendants().OfType<ScrollBar>()
                .Any(s => s.Orientation == Avalonia.Layout.Orientation.Horizontal && s.IsVisible),
            "no entran 4 x 160 en 560: tiene que aparecer el scroll horizontal");
    }

    [AvaloniaFact]
    public void Ui2_UnaColumnaAutoQueCreceAlLlegarLosDatos_QuedaEnSuAnchoNatural()
    {
        var grid = Montar(GrillaAutoYNumerica, 900, out _);
        // La grilla se arma vacía y los datos llegan después (es lo que dispara el bug de DataGrid 12.0.1).
        Cargar(grid, Enumerable.Range(0, 5).Select(i => new ItemPrueba("Encargada de Depósito Municipal Norte", i)));

        var recortados = grid.GetVisualDescendants().OfType<DataGridCell>()
            .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
            .Where(tb => tb.IsEffectivelyVisible && !string.IsNullOrEmpty(tb.Text))
            .Where(tb => tb.Bounds.Width < tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right - 0.5)
            .Select(tb => $"'{tb.Text}' mide {tb.Bounds.Width:F1}px y necesita {tb.DesiredSize.Width:F1}px")
            .ToList();
        Assert.True(recortados.Count == 0, $"textos recortados: {string.Join("; ", recortados)}");

        var detalle = grid.Columns.Single(c => Equals(c.Header, "Detalle"));
        var texto = grid.GetVisualDescendants().OfType<DataGridCell>()
            .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
            .First(tb => tb.Text == "Encargada de Depósito Municipal Norte");
        Assert.True(detalle.ActualWidth >= texto.DesiredSize.Width - 0.5,
            $"Detalle (Auto) mide {detalle.ActualWidth:F1}px y su texto necesita {texto.DesiredSize.Width:F1}px");
    }
}
