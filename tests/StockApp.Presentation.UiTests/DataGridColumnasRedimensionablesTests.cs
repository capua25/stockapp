using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verifica el pedido "todas las columnas de todas las grillas son redimensionables por el
/// usuario" contra el estilo REAL de Themes/DataGrid.axaml (mismo TestApp que
/// <see cref="DataGridEstiloRealTests"/> — carga Tokens/Typography/Controls/DataGrid.axaml en
/// el mismo orden que App.axaml).
///
/// ESTADO ANTES (evidencia, no hipótesis): decompilando Avalonia.Controls.DataGrid 12.0.1
/// (Avalonia.Controls.DataGrid.decompiled.cs, método estático de DataGrid) el registro real de
/// la propiedad es
///   <c>CanUserResizeColumnsProperty = AvaloniaProperty.Register&lt;DataGrid, bool&gt;
///   ("CanUserResizeColumns", false, ...)</c>
/// — el default registrado es <c>false</c> (hay una constante interna sin usar,
/// <c>DATAGRID_defaultCanUserResizeColumns = true</c>, que sugiere lo contrario pero jamás se
/// pasa al Register: es resto muerto del port de WPF). Una <c>DataGridColumn.CanUserResize</c>
/// (nullable) resuelve así: <c>CanUserResizeInternal ?? OwningGrid?.CanUserResizeColumns ?? true</c>
/// — si la columna no fija nada explícito, hereda del DataGrid dueño, y el DataGrid dueño por
/// default es <c>false</c>. Con Fluent.xaml crudo (sin Themes/DataGrid.axaml) NINGUNA columna de
/// NINGUNA grilla es redimensionable salvo que la vista lo declare a mano (7 de ~21 grillas de
/// la app lo hacían; las otras 14 no).
///
/// Este test monta un DataGrid SIN fijar <c>CanUserResizeColumns</c> a mano (a propósito, para
/// no enmascarar el default real con un atributo del XAML de prueba) y verifica el resize
/// EFECTIVO resuelto en <c>true</c> — ni sólo el atributo en el XAML de una vista, ni el valor de
/// la propiedad del DataGrid sin mirar la columna: lo que el usuario puede hacer con el mouse.
/// </summary>
public class DataGridColumnasRedimensionablesTests
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
    public void Montar_UnaGrilla_SinFijarCanUserResizeAMano_ElResizeQuedaEfectivoEnTrue()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grilla = window.GetVisualDescendants().OfType<DataGrid>().First();
        grilla.ItemsSource = new[] { new ItemPrueba("uno", 1) };
        Dispatcher.UIThread.RunJobs();

        Assert.True(grilla.CanUserResizeColumns,
            "DataGrid.CanUserResizeColumns quedó en false: Themes/DataGrid.axaml debería fijar " +
            "true en el Style Selector=\"DataGrid\" (el default registrado de Avalonia.Controls." +
            "DataGrid 12.0.1 es false, ver comentario de la clase).");

        Assert.All(grilla.Columns, columna => Assert.True(columna.CanUserResize,
            $"La columna \"{columna.Header}\" resolvió CanUserResize en false pese a no fijarlo " +
            "explícito: no hereda el true de Themes/DataGrid.axaml."));
    }

    [AvaloniaFact]
    public void Montar_UnaGrilla_ElAnchoMinimoDeColumnaTieneUnPisoRazonable()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grilla = window.GetVisualDescendants().OfType<DataGrid>().First();
        grilla.ItemsSource = new[] { new ItemPrueba("uno", 1) };
        Dispatcher.UIThread.RunJobs();

        // El default de fábrica de Avalonia.Controls.DataGrid.MinColumnWidth es 20px (ver
        // DATAGRID_defaultMinColumnWidth decompilado) -- deja demasiado poco para que un header
        // de 11px SemiBold con letter-spacing (Themes/DataGrid.axaml) siga siendo legible al
        // arrastrar una columna a su mínimo. 40px es el piso que fija el Style global.
        Assert.Equal(40.0, grilla.MinColumnWidth);
    }
}
