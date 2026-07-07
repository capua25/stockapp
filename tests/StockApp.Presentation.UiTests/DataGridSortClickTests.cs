using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Reproduce y verifica con un CLICK REAL de puntero (no invocacion programatica del sort)
/// el bug de AvaloniaUI/Avalonia.Controls.DataGrid#232 (issue original transferido desde
/// avaloniaui/avalonia#21129): clickear el encabezado de una columna de DataGrid no ordena
/// las filas en Avalonia.Controls.DataGrid 12.0.0 cuando la columna usa un compiled binding
/// (DataGridTextColumn.Binding con DataType), que es exactamente como estan escritas las
/// columnas reales de MovimientoHistorialView y ProductoListView.
///
/// Causa raiz confirmada en el codigo fuente de Avalonia.Controls.DataGrid (ver
/// DataGridColumn.GetSortPropertyName): en 12.0.0 el metodo chequea
/// "boundColumn.Binding is CompiledBindingExtension", pero en runtime el binding ya
/// resuelto es de tipo CompiledBinding (CompiledBindingExtension es la markup extension
/// que lo produce en tiempo de parseo, no el tipo del objeto final). El chequeo nunca
/// matchea, GetSortPropertyName devuelve string vacio, y ProcessSort hace
/// "no-opt if we couldn't find a property to sort on" -> return silencioso. Fixeado en
/// Avalonia.Controls.DataGrid 12.0.1 (PR #230), donde el chequeo es contra CompiledBinding.
///
/// Ademas, sortear requiere que el ItemsSource implemente IDataGridCollectionView
/// (ver DataGridDataConnection.AllowSort: si CollectionView es null, AllowSort es false y
/// ProcessSort no hace nada). Una ObservableCollection cruda NO alcanza; hace falta
/// envolverla en DataGridCollectionView (o similar), que es lo que ya hacen los VMs reales
/// via la propiedad ItemsView.
/// </summary>
public class DataGridSortClickTests
{
    private const string XamlConCollectionView = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:t="clr-namespace:StockApp.Presentation.UiTests;assembly=StockApp.Presentation.UiTests"
                x:DataType="t:VentanaPruebaViewModel"
                x:CompileBindings="True"
                Width="400" Height="300">
            <DataGrid Name="Grid"
                      ItemsSource="{Binding ItemsView}"
                      IsReadOnly="True"
                      CanUserSortColumns="True">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Nombre" Binding="{Binding Nombre, DataType={x:Type t:ItemPrueba}}" />
                    <DataGridTextColumn Header="Valor" Binding="{Binding Valor, DataType={x:Type t:ItemPrueba}}" />
                </DataGrid.Columns>
            </DataGrid>
        </Window>
        """;

    private const string XamlSinCollectionView = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:t="clr-namespace:StockApp.Presentation.UiTests;assembly=StockApp.Presentation.UiTests"
                x:DataType="t:VentanaPruebaViewModel"
                x:CompileBindings="True"
                Width="400" Height="300">
            <DataGrid Name="Grid"
                      ItemsSource="{Binding Items}"
                      IsReadOnly="True"
                      CanUserSortColumns="True">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Nombre" Binding="{Binding Nombre, DataType={x:Type t:ItemPrueba}}" />
                    <DataGridTextColumn Header="Valor" Binding="{Binding Valor, DataType={x:Type t:ItemPrueba}}" />
                </DataGrid.Columns>
            </DataGrid>
        </Window>
        """;

    private static (Window Window, DataGrid Grid, ObservableCollection<ItemPrueba> Items) MontarVentana(string xaml)
    {
        var items = new ObservableCollection<ItemPrueba>
        {
            new("Carlos", 3),
            new("Ana", 1),
            new("Beatriz", 2),
        };
        var vm = new VentanaPruebaViewModel(items);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = window.FindControl<DataGrid>("Grid")!;
        return (window, grid, items);
    }

    private static void ClickearHeader(Window window, DataGrid grid, string headerTexto)
    {
        Dispatcher.UIThread.RunJobs();

        var header = grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, headerTexto));

        Assert.NotNull(header);

        var centro = new Point(header!.Bounds.Width / 2, header.Bounds.Height / 2);
        var puntoEnVentana = header.TranslatePoint(centro, window) ?? centro;

        window.MouseMove(puntoEnVentana);
        window.MouseDown(puntoEnVentana, MouseButton.Left);
        window.MouseUp(puntoEnVentana, MouseButton.Left);

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Click_En_Header_Nombre_Ordena_Las_Filas_Alfabeticamente()
    {
        var (window, grid, items) = MontarVentana(XamlConCollectionView);

        // Orden inicial (sin ordenar): Carlos, Ana, Beatriz
        Assert.Equal(new[] { "Carlos", "Ana", "Beatriz" }, items.Select(i => i.Nombre));

        ClickearHeader(window, grid, "Nombre");

        var vistaOrdenada = ((System.Collections.IEnumerable)grid.ItemsSource!)
            .Cast<ItemPrueba>()
            .Select(i => i.Nombre)
            .ToArray();

        Assert.Equal(new[] { "Ana", "Beatriz", "Carlos" }, vistaOrdenada);
    }

    /// <summary>
    /// Confirma con click real la segunda causa (independiente) del bug: si el ItemsSource
    /// NO implementa IDataGridCollectionView (ObservableCollection cruda), DataGridDataConnection.AllowSort
    /// es false y ProcessSort no hace nada, sin importar la version de Avalonia.Controls.DataGrid.
    /// Este test se corre con el paquete ya en la version fixeada (12.0.1) para aislar esta causa
    /// de la otra (compiled binding). Se espera que NO ordene: es intencionalmente un test que
    /// documenta una limitacion real, no una regresion a arreglar aca.
    /// </summary>
    [AvaloniaFact]
    public void Click_En_Header_Sin_CollectionView_No_Ordena()
    {
        var (window, grid, items) = MontarVentana(XamlSinCollectionView);

        Assert.Equal(new[] { "Carlos", "Ana", "Beatriz" }, items.Select(i => i.Nombre));

        ClickearHeader(window, grid, "Nombre");

        // Con ObservableCollection cruda (sin DataGridCollectionView), el click no reordena.
        Assert.Equal(new[] { "Carlos", "Ana", "Beatriz" }, items.Select(i => i.Nombre));
    }
}
