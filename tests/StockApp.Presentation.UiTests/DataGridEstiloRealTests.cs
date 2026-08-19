using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verifica el estilo real de Themes/DataGrid.axaml (Tarea 2.2) contra un DataGrid montado
/// con el TestApp real (Tokens/Typography/Controls/DataGrid.axaml — ver TestAppBuilder.cs).
///
/// NOTA: el plan original (docs/superpowers/plans/2026-08-18-ui-refactor-dashboard-fase-a.md)
/// preveía "ampliar" este archivo, creado supuestamente en la Task 0.2. Ese archivo NUNCA
/// llegó a existir en el árbol: se creó sin trackear durante la Tanda 0 y se borró por el
/// Ruling 4 (test defectuoso — medía el ancho del contenedor, no del texto, ver
/// task-0-cierre-report.md). Este archivo se crea desde cero en la Tanda 2, con la misma
/// entidad de prueba (<see cref="ItemPrueba"/>) que ya usa <see cref="DataGridSortClickTests"/>,
/// en vez del <c>ItemPrueba { Nombre, Numero }</c> con setters que proponía el snippet del
/// plan (esa clase no existe: la real es <c>ItemPrueba(string nombre, int valor)</c>, sin
/// setters, con la propiedad <c>Valor</c> — no <c>Numero</c>).
///
/// SEGUNDO defecto del plan descubierto al escribir este archivo: el snippet del plan
/// asertaba contra <c>DataGrid.AlternatingRowBackground</c>, propiedad que EXISTE en el
/// DataGrid de WPF pero NO en Avalonia.Controls.DataGrid 12.0.1 (confirmado decompilando
/// el ensamblado: solo hay <c>RowBackground</c>, único, sin variante alternada). El
/// "fondo alternado" de este puerto se logra en el XAML con un selector estructural
/// <c>DataGridRow:nth-child(2n)</c> (Avalonia SÍ soporta nth-child), no con una propiedad.
/// El segundo test de abajo verifica el resultado observable (dos filas con Background
/// distinto), no una propiedad inexistente.
/// </summary>
public class DataGridEstiloRealTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="500" Height="400">
            <DataGrid Name="Grid" AutoGenerateColumns="False">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Nombre" Binding="{Binding Nombre}" />
                    <DataGridTextColumn Header="Valor" Binding="{Binding Valor}" />
                </DataGrid.Columns>
            </DataGrid>
        </Window>
        """;

    [AvaloniaFact]
    public void Montar_UnaGrilla_LosHeadersUsanLaEscalaMicro()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grilla = window.GetVisualDescendants().OfType<DataGrid>().First();
        grilla.ItemsSource = new[] { new ItemPrueba("uno", 1) };
        Dispatcher.UIThread.RunJobs();

        var header = window.GetVisualDescendants().OfType<DataGridColumnHeader>().First();

        Assert.Equal(11.0, header.FontSize);
        Assert.Equal(FontWeight.SemiBold, header.FontWeight);
    }

    [AvaloniaFact]
    public void Montar_UnaGrilla_LasFilasTienenAltoYAlternanFondo()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grilla = window.GetVisualDescendants().OfType<DataGrid>().First();
        grilla.ItemsSource = new[]
        {
            new ItemPrueba("uno", 1),
            new ItemPrueba("dos", 2),
        };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(36.0, grilla.RowHeight);

        var filas = window.GetVisualDescendants()
            .OfType<DataGridRow>()
            .OrderBy(f => f.Index)
            .ToList();

        Assert.True(filas.Count >= 2, "Se esperaban al menos 2 filas realizadas para comparar el fondo alternado.");
        Assert.NotEqual(filas[0].Background, filas[1].Background);
    }
}
